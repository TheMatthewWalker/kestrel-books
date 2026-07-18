import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import { api, setSessionExpiredHandler } from './api';

type SignInResult = { mfaRequired: boolean; mfaToken?: string };
type AuthState = {
  signedIn: boolean;
  displayName: string;
  signIn: (email: string, password: string) => Promise<SignInResult>;
  register: (email: string, password: string, displayName: string) => Promise<void>;
  verifyMfa: (mfaToken: string, code: string, method: 'totp' | 'email') => Promise<void>;
  requestEmailCode: (mfaToken: string) => Promise<void>;
  signOut: () => void;
};

const Ctx = createContext<AuthState>(null as unknown as AuthState);
export const useAuth = () => useContext(Ctx);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [signedIn, setSignedIn] = useState(!!localStorage.getItem('accessToken'));
  const [displayName, setDisplayName] = useState(localStorage.getItem('displayName') ?? '');

  useEffect(() => setSessionExpiredHandler(() => setSignedIn(false)), []);

  const store = (data: { accessToken: string; refreshToken: string; displayName: string }) => {
    localStorage.setItem('accessToken', data.accessToken);
    localStorage.setItem('refreshToken', data.refreshToken);
    localStorage.setItem('displayName', data.displayName);
    setDisplayName(data.displayName);
    setSignedIn(true);
  };

  return (
    <Ctx.Provider value={{
      signedIn,
      displayName,
      register: async (email, password, displayName) => {
        store((await api.post('/auth/register', { email, password, displayName })).data);
      },
      signIn: async (email, password) => {
        const r = await api.post('/auth/login', { email, password });
        if (r.data.mfaRequired) return { mfaRequired: true, mfaToken: r.data.mfaToken };
        store(r.data);
        return { mfaRequired: false };
      },
      verifyMfa: async (mfaToken, code, method) => {
        store((await api.post('/auth/mfa/verify', { mfaToken, code, method })).data);
      },
      requestEmailCode: async (mfaToken) => {
        await api.post('/auth/mfa/send-email-code', { refreshToken: mfaToken });
      },
      signOut: () => {
        localStorage.removeItem('accessToken');
        localStorage.removeItem('refreshToken');
        setSignedIn(false);
      },
    }}>
      {children}
    </Ctx.Provider>
  );
}
