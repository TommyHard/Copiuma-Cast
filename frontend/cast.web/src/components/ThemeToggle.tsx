import { useThemeStore } from '@/store/theme';

export function ThemeToggle() {
    const { theme, toggle } = useThemeStore();
    return (
        <button
            type="button"
            onClick={toggle}
            aria-label="toggle theme"
            className="rounded-md border border-border bg-bg-elevated px-2 py-1 text-sm text-fg hover:border-accent"
        >
            {theme === 'dark' ? '☾' : '☀'}
        </button>
    );
}