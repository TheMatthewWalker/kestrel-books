import axios from 'axios';

// Same-origin in production (served from wwwroot); Vite proxy in dev.
export const api = axios.create({ baseURL: '/api' });

let onExpired: (() => void) | null = null;
export const setSessionExpiredHandler = (fn: () => void) => { onExpired = fn; };

api.interceptors.request.use((config) => {
  const token = localStorage.getItem('accessToken');
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});

let refreshing: Promise<boolean> | null = null;
async function tryRefresh(): Promise<boolean> {
  const refreshToken = localStorage.getItem('refreshToken');
  if (!refreshToken) return false;
  try {
    const r = await axios.post('/api/auth/refresh', { refreshToken });
    localStorage.setItem('accessToken', r.data.accessToken);
    localStorage.setItem('refreshToken', r.data.refreshToken);
    return true;
  } catch { return false; }
}

api.interceptors.response.use(
  (r) => r,
  async (error) => {
    const original = error.config;
    if (error.response?.status === 401 && !original._retried) {
      original._retried = true;
      refreshing = refreshing ?? tryRefresh();
      const ok = await refreshing;
      refreshing = null;
      if (ok) {
        original.headers.Authorization = `Bearer ${localStorage.getItem('accessToken')}`;
        return api(original);
      }
      localStorage.removeItem('accessToken');
      localStorage.removeItem('refreshToken');
      onExpired?.();
    }
    return Promise.reject(error);
  },
);

export const errorMessage = (e: unknown): string => {
  const err = e as { response?: { data?: { error?: string } }; message?: string };
  return err?.response?.data?.error ?? err?.message ?? 'Something went wrong.';
};

export const gbp = (v: number) =>
  new Intl.NumberFormat('en-GB', { style: 'currency', currency: 'GBP' }).format(v ?? 0);
