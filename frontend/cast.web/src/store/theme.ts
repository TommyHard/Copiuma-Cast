import { create } from 'zustand';
import { persist } from 'zustand/middleware';

type Theme = 'light' | 'dark';

interface ThemeState {
    theme: Theme;
    toggle: () => void;
    apply: () => void;
}

export const useThemeStore = create<ThemeState>()(
    persist(
        (set, get) => ({
            theme: 'dark',
            toggle: () => {
                const next = get().theme === 'dark' ? 'light' : 'dark';
                set({ theme: next });
                document.documentElement.classList.toggle('dark', next === 'dark');
            },
            apply: () => document.documentElement.classList.toggle('dark', get().theme === 'dark'),
        }),
        { name: 'cast.theme' },
    ),
);