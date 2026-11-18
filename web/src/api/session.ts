import type { AuthResponse, UserDto } from './types';

/**
 * Client-side session: tokens + current user, persisted in localStorage so a page reload keeps
 * you signed in (US-A4). POC trade-off: localStorage is readable by any script on the origin;
 * a production build would keep the refresh token in an HttpOnly cookie instead.
 */
export interface Session {
  accessToken: string;
  refreshToken: string;
  user: UserDto;
}

const KEY = 'tasktracker.session';

type Listener = (session: Session | null) => void;
const listeners = new Set<Listener>();

let current: Session | null = load();

function load(): Session | null {
  try {
    const raw = globalThis.localStorage?.getItem(KEY);
    return raw ? (JSON.parse(raw) as Session) : null;
  } catch {
    return null;
  }
}

function persist(session: Session | null) {
  try {
    if (session) globalThis.localStorage?.setItem(KEY, JSON.stringify(session));
    else globalThis.localStorage?.removeItem(KEY);
  } catch {
    /* storage unavailable (private mode) — session stays in memory only */
  }
}

export const session = {
  get: () => current,

  set(next: Session | null) {
    current = next;
    persist(next);
    listeners.forEach((l) => l(next));
  },

  fromAuth: (auth: AuthResponse): Session => ({
    accessToken: auth.accessToken,
    refreshToken: auth.refreshToken,
    user: auth.user,
  }),

  subscribe(listener: Listener) {
    listeners.add(listener);
    return () => { listeners.delete(listener); };
  },
};
