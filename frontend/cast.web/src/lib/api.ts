import axios from 'axios';
import { useAuthStore } from '@/store/auth';
import type { AuthResponse } from '@/lib/types';

export const api = axios.create({ baseURL: '/api' });

api.interceptors.request.use((config) => {
  const token = useAuthStore.getState().token;
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});

// Одновременные 401 не должны вызывать несколько обновлений сразу —
// держим один общий промис обновления (single-flight)
let refreshPromise: Promise<string | null> | null = null;

async function refreshAccessToken(): Promise<string | null> {
    const refreshToken = useAuthStore.getState().refreshToken;
    if (!refreshToken) return null;
    try {
        // axios (без интерсепторов), чтобы не зациклиться на 401
        const res = await axios.post<AuthResponse>('/api/auth/refresh', { refreshToken });
        useAuthStore.getState().setAuth(res.data);
        return res.data.accessToken;
    } catch {
        return null;
    }
}

api.interceptors.response.use(
    (r) => r,
    async (error) => {
        const original = error?.config;
        const status = error?.response?.status;

        // На 401 пробуем молча обновить токен по refresh и повторить запрос один раз
        if (status === 401 && original && !original._retried) {
            original._retried = true;
            refreshPromise ??= refreshAccessToken().finally(() => { refreshPromise = null; });
            const newToken = await refreshPromise;
            if (newToken) {
                original.headers = original.headers ?? {};
                original.headers.Authorization = `Bearer ${newToken}`;
                return api(original);
            }
            useAuthStore.getState().logout();
        }
        return Promise.reject(error);
    },
);