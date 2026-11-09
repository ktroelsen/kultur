import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react';
import { api, ApiError, type Me } from './api';

type AuthState = {
  user: Me | null;
  loading: boolean;
  login: (password: string) => Promise<void>;
  logout: () => Promise<void>;
};

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<Me | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    api<Me>('/api/auth/me')
      .then(setUser)
      .catch(() => setUser(null))
      .finally(() => setLoading(false));
  }, []);

  const login = useCallback(async (password: string) => {
    setUser(await api<Me>('/api/auth/login', { method: 'POST', json: { password } }));
  }, []);

  const logout = useCallback(async () => {
    await api('/api/auth/logout', { method: 'POST' });
    setUser(null);
  }, []);

  return <AuthContext.Provider value={{ user, loading, login, logout }}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside <AuthProvider>');
  return ctx;
}

export function loginErrorMessage(err: unknown) {
  if (err instanceof ApiError) {
    if (err.status === 401) return 'Forkert kodeord.';
    if (err.status === 429) return 'For mange forsøg – vent et minut.';
    if (err.status === 503) return 'Login er ikke sat op på serveren endnu.';
  }
  return 'Kunne ikke logge ind. Prøv igen.';
}
