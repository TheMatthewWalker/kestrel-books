import axios from 'axios';

// Web auth hardening: the ACCESS token lives only in this module's memory (never
// localStorage — XSS cannot exfiltrate what isn't stored). The REFRESH token
// lives only in an httpOnly cookie the server sets; JavaScript never sees it.
// Page reloads restore the session via one silent cookie-refresh.
let accessToken: string | null = null;
export const setAccessToken = (t: string | null) => { accessToken = t; };

export const api = axios.create({ baseURL: '/api' });

let onExpired: (() => void) | null = null;
export const setSessionExpiredHandler = (fn: () => void) => { onExpired = fn; };

api.interceptors.request.use((config) => {
  if (accessToken) config.headers.Authorization = `Bearer ${accessToken}`;
  return config;
});

let refreshing: Promise<boolean> | null = null;
export async function tryRefresh(): Promise<boolean> {
  try {
    const r = await axios.post('/api/auth/refresh', {}, { headers: { 'X-Use-Cookies': '1' } });
    accessToken = r.data.accessToken;
    localStorage.setItem('displayName', r.data.displayName ?? '');
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
        original.headers.Authorization = `Bearer ${accessToken}`;
        return api(original);
      }
      accessToken = null;
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

export const today = () => new Date().toISOString().slice(0, 10);

/** Authenticated file download (an <a href> can't carry the bearer token). */
export async function downloadFile(url: string, fileName: string) {
  const r = await api.get(url, { responseType: 'blob' });
  const href = URL.createObjectURL(r.data);
  const a = document.createElement('a');
  a.href = href; a.download = fileName; a.click();
  URL.revokeObjectURL(href);
}
