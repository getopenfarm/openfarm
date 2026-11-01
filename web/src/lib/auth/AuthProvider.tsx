import { useQueryClient } from '@tanstack/react-query';
import {
  createContext,
  type ReactNode,
  useCallback,
  useEffect,
  useMemo,
  useState,
} from 'react';
import {
  acceptLogin,
  connectSessionChannel,
  endSession,
  refreshSession,
} from './session';
import { clearToken, getToken, subscribeToken } from './tokenStorage';

interface AuthContextValue {
  token: string | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  error: boolean;
  retry: () => void;
  login: (token: string) => void;
  logout: () => Promise<void>;
}
export const AuthContext = createContext<AuthContextValue | undefined>(
  undefined,
);

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const [token, setState] = useState(getToken);
  const [isLoading, setLoading] = useState(!getToken());
  const [error, setError] = useState(false);
  const retry = useCallback(() => {
    setLoading(true);
    setError(false);
    void refreshSession()
      .catch(() => setError(true))
      .finally(() => setLoading(false));
  }, []);
  useEffect(() => {
    localStorage.removeItem('openfarm.auth.token');
    const disconnectChannel = connectSessionChannel();
    const unsubscribe = subscribeToken(() => setState(getToken()));
    const clearCache = () => queryClient.clear();
    window.addEventListener('openfarm:logout', clearCache);
    window.addEventListener('openfarm:login', retry);
    if (!getToken()) retry();
    return () => {
      unsubscribe();
      disconnectChannel();
      window.removeEventListener('openfarm:logout', clearCache);
      window.removeEventListener('openfarm:login', retry);
    };
  }, [retry, queryClient]);
  useEffect(() => {
    if (!token) return;
    const renew = () => {
      if (document.visibilityState === 'visible')
        void refreshSession().catch(() => setError(true));
    };
    const timer = window.setInterval(renew, 10 * 60_000);
    window.addEventListener('focus', renew);
    document.addEventListener('visibilitychange', renew);
    return () => {
      clearInterval(timer);
      window.removeEventListener('focus', renew);
      document.removeEventListener('visibilitychange', renew);
    };
  }, [token]);
  const login = useCallback(
    (value: string) => {
      clearToken();
      queryClient.clear();
      acceptLogin(value);
      setLoading(false);
      setError(false);
    },
    [queryClient],
  );
  const logout = useCallback(async () => {
    await endSession();
    queryClient.clear();
  }, [queryClient]);
  const value = useMemo(
    () => ({
      token,
      isAuthenticated: token !== null,
      isLoading,
      error,
      retry,
      login,
      logout,
    }),
    [token, isLoading, error, retry, login, logout],
  );
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
