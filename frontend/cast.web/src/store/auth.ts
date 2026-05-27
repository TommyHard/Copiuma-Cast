import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import type { AuthResponse } from '@/lib/types';

const ROLE_CLAIM = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role';

function parseRoles(token: string): string[] {
    try {
        const part = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
        const payload = JSON.parse(decodeURIComponent(escape(atob(part))));
        const claim = payload[ROLE_CLAIM] ?? payload.role ?? payload.roles;
        if (!claim) return [];
        return Array.isArray(claim) ? claim : [claim];
    } catch {
        return [];
    }
}

interface AuthState {
    token: string | null;
    roles: string[];
    user: Pick<AuthResponse, 'userId' | 'displayName' | 'handle' | 'avatarUrl' | 'language'> | null;
    isAuthenticated: () => boolean;
    hasRole: (role: string) => boolean;
    setAuth: (res: AuthResponse) => void;
    logout: () => void;
}

export const useAuthStore = create<AuthState>()(
    persist(
        (set, get) => ({
            token: null,
            roles: [],
            user: null,
            isAuthenticated: () => !!get().token,
            hasRole: (role) => get().roles.includes(role),
            setAuth: (res) =>
                set({
                    token: res.accessToken,
                    roles: parseRoles(res.accessToken),
                    user: {
                        userId: res.userId,
                        displayName: res.displayName,
                        handle: res.handle,
                        avatarUrl: res.avatarUrl,
                        language: res.language,
                    },
                }),
            logout: () => set({ token: null, roles: [], user: null }),
        }),
        { name: 'cast.auth' },
    ),
);