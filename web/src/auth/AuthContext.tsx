import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { authApi } from '../api/endpoints';
import { session, type Session } from '../api/session';
import type { GlobalRole, UserDto } from '../api/types';

interface AuthState {
  user: UserDto | null;
  isAdmin: boolean;
  hasRole: (role: GlobalRole) => boolean;
  login: (email: string, password: string) => Promise<void>;
  register: (email: string, password: string, displayName: string) => Promise<void>;
  logout: () => Promise<void>;
}

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [current, setCurrent] = useState<Session | null>(session.get());

  // The API client updates the session on silent refresh (e.g. new role) or clears it when
  // refresh fails — mirror that into React state so the UI re-renders / redirects to login.
  useEffect(() => session.subscribe(setCurrent), []);

  // On load, confirm the stored session is still valid (refreshes transparently if the access token expired).
  useEffect(() => {
    if (!session.get()) return;
    authApi.me()
      .then((user) => { const s = session.get(); if (s) session.set({ ...s, user }); })
      .catch(() => { /* a failed refresh already cleared the session */ });
  }, []);

  const login = useCallback(async (email: string, password: string) => {
    session.set(session.fromAuth(await authApi.login({ email, password })));
  }, []);

  const register = useCallback(async (email: string, password: string, displayName: string) => {
    await authApi.register({ email, password, displayName });
    session.set(session.fromAuth(await authApi.login({ email, password }))); // straight in after sign-up
  }, []);

  const logout = useCallback(async () => {
    try {
      await authApi.logout(); // ends every session server-side
    } finally {
      session.set(null);
    }
  }, []);

  const value = useMemo<AuthState>(() => {
    const user = current?.user ?? null;
    const hasRole = (role: GlobalRole) => !!user?.roles.includes(role);
    return { user, isAdmin: hasRole('Admin'), hasRole, login, register, logout };
  }, [current, login, register, logout]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside <AuthProvider>');
  return ctx;
}
