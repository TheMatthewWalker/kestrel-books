import axios from 'axios';
import * as SecureStore from 'expo-secure-store';

// Point this at your machine on your local network, e.g. http://192.168.1.50:5000
export const API_BASE = 'http://192.168.1.50:5000';

export const api = axios.create({ baseURL: `${API_BASE}/api` });

let onSessionExpired: (() => void) | null = null;
export const setSessionExpiredHandler = (fn: () => void) => { onSessionExpired = fn; };

api.interceptors.request.use(async (config) => {
  const token = await SecureStore.getItemAsync('accessToken');
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});

// On 401: try one silent refresh (rotating token), retry the request once;
// if refresh fails, the session is over — sign the user out.
let refreshing: Promise<boolean> | null = null;

async function tryRefresh(): Promise<boolean> {
  const refreshToken = await SecureStore.getItemAsync('refreshToken');
  if (!refreshToken) return false;
  try {
    const r = await axios.post(`${API_BASE}/api/auth/refresh`, { refreshToken });
    await SecureStore.setItemAsync('accessToken', r.data.accessToken);
    await SecureStore.setItemAsync('refreshToken', r.data.refreshToken);
    return true;
  } catch {
    return false;
  }
}

api.interceptors.response.use(
  (res) => res,
  async (error) => {
    const original = error.config;
    if (error.response?.status === 401 && !original._retried) {
      original._retried = true;
      refreshing = refreshing ?? tryRefresh();
      const ok = await refreshing;
      refreshing = null;
      if (ok) {
        original.headers.Authorization = `Bearer ${await SecureStore.getItemAsync('accessToken')}`;
        return api(original);
      }
      await SecureStore.deleteItemAsync('accessToken');
      await SecureStore.deleteItemAsync('refreshToken');
      onSessionExpired?.();
    }
    return Promise.reject(error);
  },
);

export const errorMessage = (e: any): string =>
  e?.response?.data?.error ?? e?.message ?? 'Something went wrong.';
