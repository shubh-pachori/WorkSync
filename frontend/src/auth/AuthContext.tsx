import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { AuthApi } from '../api/timesheetApi';
import { setAccessToken, setRefreshHandler, setUnauthorizedHandler } from '../api/client';
import type { AuthResponse, User } from '../types';

interface LoginOutcome {
  /** True when the password was accepted but a second factor is still needed. */
  requiresTotp: boolean;
}

interface AuthContextValue {
  user: User | null;
  isManager: boolean;
  /** False until the initial session-restore attempt has finished. */
  ready: boolean;
  /** True between a password step that needs 2FA and the code being accepted. */
  awaitingTotp: boolean;

  login: (email: string, password: string) => Promise<LoginOutcome>;
  completeTotp: (code: string) => Promise<void>;
  cancelTotp: () => void;
  logout: () => Promise<void>;
  /** Refreshes the cached user after a profile change such as enabling 2FA. */
  reloadUser: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [ready, setReady] = useState(false);

  // Held in memory only: a half-finished login should not survive a page reload.
  const mfaTokenRef = useRef<string | null>(null);
  const [awaitingTotp, setAwaitingTotp] = useState(false);

  const applySession = useCallback((response: AuthResponse): string | null => {
    if (response.accessToken && response.user) {
      setAccessToken(response.accessToken);
      setUser(response.user);
      mfaTokenRef.current = null;
      setAwaitingTotp(false);
      return response.accessToken;
    }
    return null;
  }, []);

  const clearSession = useCallback(() => {
    setAccessToken(null);
    setUser(null);
    mfaTokenRef.current = null;
    setAwaitingTotp(false);
  }, []);

  /**
   * Exchanges the refresh cookie for a new access token. Used both to restore a session on
   * page load and by the 401 interceptor, which is why it returns the raw token.
   */
  const refresh = useCallback(async (): Promise<string | null> => {
    try {
      return applySession(await AuthApi.refresh());
    } catch {
      // No cookie, expired, revoked, or reuse detected — all mean "no session".
      clearSession();
      return null;
    }
  }, [applySession, clearSession]);

  // Restore an existing session before the first render that needs it. The access token
  // lives only in memory, so a reload always goes through the refresh cookie.
  useEffect(() => {
    let cancelled = false;

    void (async () => {
      await refresh();
      if (!cancelled) setReady(true);
    })();

    return () => { cancelled = true; };
  }, [refresh]);

  // Wire the axios interceptor to this context.
  useEffect(() => {
    setRefreshHandler(refresh);
    setUnauthorizedHandler(clearSession);

    return () => {
      setRefreshHandler(null);
      setUnauthorizedHandler(null);
    };
  }, [refresh, clearSession]);

  const login = useCallback(async (email: string, password: string): Promise<LoginOutcome> => {
    const response = await AuthApi.login(email, password);

    if (response.requiresTotp && response.mfaToken) {
      mfaTokenRef.current = response.mfaToken;
      setAwaitingTotp(true);
      return { requiresTotp: true };
    }

    applySession(response);
    return { requiresTotp: false };
  }, [applySession]);

  const completeTotp = useCallback(async (code: string) => {
    const mfaToken = mfaTokenRef.current;
    if (!mfaToken) {
      throw new Error('That sign-in session has expired. Enter your password again.');
    }

    applySession(await AuthApi.loginTotp(mfaToken, code));
  }, [applySession]);

  const cancelTotp = useCallback(() => {
    mfaTokenRef.current = null;
    setAwaitingTotp(false);
  }, []);

  const logout = useCallback(async () => {
    try {
      await AuthApi.logout();
    } catch {
      // Even if the call fails, drop local state — the cookie is scoped and short-lived.
    } finally {
      clearSession();
    }
  }, [clearSession]);

  const reloadUser = useCallback(async () => {
    try {
      setUser(await AuthApi.me());
    } catch {
      // Left to the interceptor: a genuine 401 here ends the session.
    }
  }, []);

  const value = useMemo<AuthContextValue>(() => ({
    user,
    isManager: user?.role === 'Manager' || user?.role === 'Admin',
    ready,
    awaitingTotp,
    login,
    completeTotp,
    cancelTotp,
    logout,
    reloadUser
  }), [user, ready, awaitingTotp, login, completeTotp, cancelTotp, logout, reloadUser]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) throw new Error('useAuth must be used inside an AuthProvider.');
  return context;
}

/** The signed-in user, for screens that only render behind the auth gate. */
export function useCurrentUser(): User {
  const { user } = useAuth();
  if (!user) throw new Error('useCurrentUser used outside an authenticated route.');
  return user;
}
