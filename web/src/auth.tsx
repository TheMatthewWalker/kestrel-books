import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import { api, setAccessToken, setSessionExpiredHandler, tryRefresh } from './api';

type SignInResult = { mfaRequired: boolean; mfaToken?: string };
type AuthState = {
  ready: boolean;
  signedIn: boolean;
  displayName: string;
  signIn: (email: string, password: string) => Promise<SignInResult>;
  register: (email: string, password: string, displayName: string) => Promise<void>;
  verifyMfa: (mfaToken: string, code: string, method: 'totp' | 'email') => Promise<void>;
  requestEmailCode: (mfaToken: string) => Promise<void>;
  signOut: () => Promise<void>;
};

const Ctx = createContext<AuthState>(null as unknown as AuthState);
export const useAuth = () => useContext(Ctx);
const cookieHeaders = { headers: { 'X-Use-Cookies': '1' } };

export function AuthProvider({ children }: { children: ReactNode }) {
  const [ready, setReady] = useState(false);
  const [signedIn, setSignedIn] = useState(false);
  const [displayName, setDisplayName] = useState('');

  useEffect(() => {
    setSessionExpiredHandler(() => setSignedIn(false));
    // Restore the session from the httpOnly cookie (if any) on load.
    tryRefresh().then(ok => {
      setSignedIn(ok);
      setDisplayName(localStorage.getItem('displayName') ?? '');
      setReady(true);
    });
  }, []);

  const store = (data: { accessToken: string; displayName: string }) => {
    setAccessToken(data.accessToken);
    localStorage.setItem('displayName', data.displayName);
    setDisplayName(data.displayName);
    setSignedIn(true);
  };

  return (
    <Ctx.Provider value={{
      ready, signedIn, displayName,
      signIn: async (email, password) => {
        const r = await api.post('/auth/login', { email, password }, cookieHeaders);
        if (r.data.mfaRequired) return { mfaRequired: true, mfaToken: r.data.mfaToken };
        store(r.data);
        return { mfaRequired: false };
      },
      register: async (email, password, displayName) => {
        store((await api.post('/auth/register', { email, password, displayName }, cookieHeaders)).data);
      },
      verifyMfa: async (mfaToken, code, method) => {
        store((await api.post('/auth/mfa/verify', { mfaToken, code, method }, cookieHeaders)).data);
      },
      requestEmailCode: async (mfaToken) => {
        await api.post('/auth/mfa/send-email-code', { refreshToken: mfaToken });
      },
      signOut: async () => {
        try { await api.post('/auth/logout'); } catch { /* cookie may already be gone */ }
        setAccessToken(null);
        setSignedIn(false);
      },
    }}>
      {children}
    </Ctx.Provider>
  );
}
