import axios, { AxiosError, type AxiosAdapter, type InternalAxiosRequestConfig } from 'axios';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ApiError, call, createClient, toQuery } from '../api/client';
import { session } from '../api/session';
import type { UserDto } from '../api/types';

const user: UserDto = { id: 'u1', email: 'bob@example.com', displayName: 'Bob', roles: ['User'], isActive: true };

type Handler = (config: InternalAxiosRequestConfig) => { status: number; data: unknown };

/** Fake transport: routes requests to `handler` and records what was sent. */
function fakeServer(handler: Handler) {
  const calls: { url: string; auth?: string }[] = [];
  const adapter: AxiosAdapter = async (config) => {
    calls.push({ url: config.url ?? '', auth: config.headers?.Authorization as string | undefined });
    const { status, data } = handler(config);
    const response = { status, data, statusText: '', headers: {}, config };
    if (status >= 400) throw new AxiosError(`HTTP ${status}`, 'ERR', config, null, response);
    return response;
  };
  axios.defaults.adapter = adapter; // the refresh call uses bare axios
  return { calls, http: createClient() }; // instance copies the adapter at creation
}

const ok = (data: unknown) => ({ status: 200, data: { success: true, data, errors: [] } });
const fail = (status: number, ...errors: string[]) => ({ status, data: { success: false, data: null, errors } });

describe('API client', () => {
  const original = axios.defaults.adapter;
  beforeEach(() => session.set({ accessToken: 'old-access', refreshToken: 'r1', user }));
  afterEach(() => { axios.defaults.adapter = original; session.set(null); });

  it('sends the bearer token and unwraps the envelope', async () => {
    const { http, calls } = fakeServer(() => ok({ hello: 'world' }));

    await expect(call(http.get('/api/auth/me'))).resolves.toEqual({ hello: 'world' });
    expect(calls[0].auth).toBe('Bearer old-access');
  });

  it('on 401 refreshes once, stores the new pair, and retries the request', async () => {
    const { http, calls } = fakeServer((c) => {
      if (c.url?.endsWith('/api/auth/refresh')) {
        return ok({ accessToken: 'new-access', refreshToken: 'r2', user: { ...user, roles: ['Admin'] } });
      }
      return c.headers.Authorization === 'Bearer new-access' ? ok('payload') : fail(401, 'Unauthorized');
    });

    await expect(call(http.get('/api/projects'))).resolves.toBe('payload');
    expect(calls.map((c) => c.url)).toEqual(['/api/projects', '/api/auth/refresh', '/api/projects']);
    expect(session.get()).toMatchObject({ accessToken: 'new-access', refreshToken: 'r2' });
    expect(session.get()?.user.roles).toEqual(['Admin']); // role change picked up via refresh
  });

  it('shares one refresh between concurrent 401s', async () => {
    let refreshes = 0;
    const { http } = fakeServer((c) => {
      if (c.url?.endsWith('/api/auth/refresh')) { refreshes++; return ok({ accessToken: 'new', refreshToken: 'r2', user }); }
      return c.headers.Authorization === 'Bearer new' ? ok(c.url) : fail(401);
    });

    const results = await Promise.all(['/a', '/b', '/c'].map((u) => call(http.get(u))));

    expect(results).toEqual(['/a', '/b', '/c']);
    expect(refreshes).toBe(1);
  });

  it('clears the session when the refresh token is rejected', async () => {
    const { http } = fakeServer((c) =>
      c.url?.endsWith('/api/auth/refresh') ? fail(401, 'Invalid or expired refresh token') : fail(401, 'Unauthorized'));

    await expect(call(http.get('/api/projects'))).rejects.toMatchObject({ status: 401 });
    expect(session.get()).toBeNull();
  });

  it('does not try to refresh on a failed login', async () => {
    const { http, calls } = fakeServer(() => fail(401, 'Invalid email or password'));

    const error = (await call(http.post('/api/auth/login', {})).catch((e: unknown) => e)) as ApiError;

    expect(error).toBeInstanceOf(ApiError);
    expect(error.errors).toEqual(['Invalid email or password']);
    expect(calls).toHaveLength(1);
    expect(session.get()).not.toBeNull();
  });

  it('surfaces 403/409/422 messages without refreshing', async () => {
    const { http, calls } = fakeServer(() => fail(409, 'User is already a member of this project'));

    await expect(call(http.post('/api/projects/1/members', {}))).rejects.toMatchObject({
      status: 409, message: 'User is already a member of this project',
    });
    expect(calls).toHaveLength(1);
  });
});

describe('toQuery', () => {
  it('repeats array keys and drops empty values', () => {
    expect(toQuery({ search: 'login', status: ['Todo', 'Done'], assigneeId: undefined, page: 2, dueAfter: '' }))
      .toBe('?search=login&status=Todo&status=Done&page=2');
  });

  it('encodes special characters', () => {
    expect(toQuery({ search: '50% & _done_' })).toBe('?search=50%25+%26+_done_');
  });

  it('returns an empty string when nothing is set', () => {
    expect(toQuery({ status: [] })).toBe('');
  });
});
