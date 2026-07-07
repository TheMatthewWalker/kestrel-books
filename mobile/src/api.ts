import axios from 'axios';
import * as SecureStore from 'expo-secure-store';

// Point this at your machine on your local network, e.g. http://192.168.1.50:5000
// (find it with `ipconfig` on Windows). The phone and computer must share a network.
export const API_BASE = 'http://192.168.1.50:5000';

export const api = axios.create({ baseURL: `${API_BASE}/api` });

api.interceptors.request.use(async (config) => {
  const token = await SecureStore.getItemAsync('accessToken');
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});

export const errorMessage = (e: any): string =>
  e?.response?.data?.error ?? e?.message ?? 'Something went wrong.';
