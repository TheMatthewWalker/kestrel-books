import React, { createContext, useContext, useEffect, useState } from 'react';
import * as SecureStore from 'expo-secure-store';
import { api } from './api';

type AuthState = {
  ready: boolean;
  signedIn: boolean;
  displayName: string;
  signIn: (email: string, password: string) => Promise<void>;
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
    (async () => {
      const token = await SecureStore.getItemAsync('accessToken');
      setSignedIn(!!token);
      setDisplayName((await SecureStore.getItemAsync('displayName')) ?? '');
      setReady(true);
    })();
  }, []);

  const handleAuth = async (res: any) => {
    await SecureStore.setItemAsync('accessToken', res.data.accessToken);
    await SecureStore.setItemAsync('displayName', res.data.displayName);
    setDisplayName(res.data.displayName);
    setSignedIn(true);
  };

  return (
    <AuthContext.Provider
      value={{
        ready,
        signedIn,
        displayName,
        signIn: async (email, password) => handleAuth(await api.post('/auth/login', { email, password })),
        register: async (email, password, displayName) =>
          handleAuth(await api.post('/auth/register', { email, password, displayName })),
        signOut: async () => {
          await SecureStore.deleteItemAsync('accessToken');
          setSignedIn(false);
        },
      }}
    >
      {children}
    </AuthContext.Provider>
  );
}
