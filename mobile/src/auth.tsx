import React, { createContext, useContext, useEffect, useState } from 'react';
import * as SecureStore from 'expo-secure-store';
import { api, setSessionExpiredHandler } from './api';

type SignInResult = { mfaRequired: boolean; mfaToken?: string };

type AuthState = {
  ready: boolean;
  signedIn: boolean;
  displayName: string;
  signIn: (email: string, password: string) => Promise<SignInResult>;
  verifyMfa: (mfaToken: string, code: string, method: 'totp' | 'email') => Promise<void>;
  requestEmailCode: (mfaToken: string) => Promise<void>;
  register: (email: string, password: string, displayName: string) => Promise<void>;
  signOut: () => Promise<void>;
};

const AuthContext = createContext<AuthState>(null as any);
export const useAuth = () => useContext(AuthContext);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [ready, setReady] = useState(false);
  const [signedIn, setSignedIn] = useState(false);
  const [displayName, setDisplayName] = useState('');

  useEffect(() => {
    setSessionExpiredHandler(() => setSignedIn(false));
    (async () => {
      const token = await SecureStore.getItemAsync('accessToken');
      setSignedIn(!!token);
      setDisplayName((await SecureStore.getItemAsync('displayName')) ?? '');
      setReady(true);
    })();
  }, []);

  const store = async (data: any) => {
    await SecureStore.setItemAsync('accessToken', data.accessToken);
    await SecureStore.setItemAsync('refreshToken', data.refreshToken);
    await SecureStore.setItemAsync('displayName', data.displayName);
    setDisplayName(data.displayName);
    setSignedIn(true);
  };

  return (
    <AuthContext.Provider
      value={{
        ready,
        signedIn,
        displayName,
        signIn: async (email, password) => {
          const r = await api.post('/auth/login', { email, password });
          if (r.data.mfaRequired) return { mfaRequired: true, mfaToken: r.data.mfaToken };
          await store(r.data);
          return { mfaRequired: false };
        },
        verifyMfa: async (mfaToken, code, method) => {
          const r = await api.post('/auth/mfa/verify', { mfaToken, code, method });
          await store(r.data);
        },
        requestEmailCode: async (mfaToken) => {
          await api.post('/auth/mfa/send-email-code', { refreshToken: mfaToken });
        },
        register: async (email, password, displayName) =>
          store((await api.post('/auth/register', { email, password, displayName })).data),
        signOut: async () => {
          await SecureStore.deleteItemAsync('accessToken');
          await SecureStore.deleteItemAsync('refreshToken');
          setSignedIn(false);
        },
      }}
    >
      {children}
    </AuthContext.Provider>
  );
}
