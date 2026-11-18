import axios, { AxiosError, type AxiosInstance, type InternalAxiosRequestConfig } from 'axios';
import { session } from './session';
import type { ApiEnvelope, AuthResponse } from './types';

/** Any non-2xx response, with the API's error messages. */
export class ApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly errors: string[],
  ) {
    super(errors[0] ?? `Request failed (${status})`);
  }
}

type RetriableConfig = InternalAxiosRequestConfig & { _retried?: boolean };

/** Endpoints that must never trigger the refresh-and-retry dance. */
const NO_REFRESH = ['/api/auth/login', '/api/auth/register', '/api/auth/refresh'];

/**
 * Builds the axios instance used by every API call:
 *  - attaches the access token;
 *  - on 401, exchanges the refresh token once (single-flight: concurrent 401s share one refresh)
 *    and retries the original request with the new token;
 *  - if the refresh fails, clears the session (the app then routes to /login);
 *  - unwraps { success, data, errors } so callers get `data` or an ApiError.
 */
export function createClient(baseURL = ''): AxiosInstance {
  const http = axios.create({ baseURL });
  let refreshing: Promise<string | null> | null = null;

  async function refreshAccessToken(): Promise<string | null> {
    const current = session.get();
    if (!current) return null;
    try {
      // Bare axios call: must not go through this instance's interceptors.
      const res = await axios.post<ApiEnvelope<AuthResponse>>(`${baseURL}/api/auth/refresh`, {
        refreshToken: current.refreshToken,
      });
      const auth = res.data.data!;
      session.set(session.fromAuth(auth)); // also picks up role changes (new roles in the token)
      return auth.accessToken;
    } catch {
      session.set(null);
      return null;
    }
  }

  http.interceptors.request.use((config) => {
    const token = session.get()?.accessToken;
    if (token) config.headers.Authorization = `Bearer ${token}`;
    return config;
  });

  http.interceptors.response.use(
    (res) => res,
    async (error: AxiosError<ApiEnvelope<unknown>>) => {
      const config = error.config as RetriableConfig | undefined;
      const status = error.response?.status;

      if (status === 401 && config && !config._retried && !NO_REFRESH.some((u) => config.url?.endsWith(u)) && session.get()) {
        refreshing ??= refreshAccessToken().finally(() => (refreshing = null));
        const token = await refreshing;
        if (token) {
          config._retried = true;
          config.headers.Authorization = `Bearer ${token}`;
          return http.request(config);
        }
      }

      const errors = error.response?.data?.errors?.length
        ? error.response.data.errors
        : [status ? `Request failed (${status})` : 'Cannot reach the server'];
      return Promise.reject(new ApiError(status ?? 0, errors));
    },
  );

  return http;
}

export const http = createClient();

/** Unwraps the envelope. */
export async function call<T>(promise: Promise<{ data: ApiEnvelope<T> }>): Promise<T> {
  const res = await promise;
  return res.data.data as T;
}

/** Builds a query string; arrays become repeated keys (?status=Todo&status=Done), empty values are dropped. */
export function toQuery(params: object): string {
  const qs = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null || value === '') continue;
    if (Array.isArray(value)) value.forEach((v) => qs.append(key, String(v)));
    else qs.append(key, String(value));
  }
  const s = qs.toString();
  return s ? `?${s}` : '';
}

export function errorMessages(e: unknown): string[] {
  if (e instanceof ApiError) return e.errors;
  if (e instanceof Error) return [e.message];
  return ['Something went wrong'];
}
