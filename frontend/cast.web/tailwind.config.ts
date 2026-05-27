import type { Config } from 'tailwindcss';

export default {
    darkMode: 'class',
    content: ['./index.html', './src/**/*.{ts,tsx}'],
    theme: {
        extend: {
            colors: {
                bg: 'rgb(var(--bg) / <alpha-value>)',
                'bg-elevated': 'rgb(var(--bg-elevated) / <alpha-value>)',
                'bg-accent': 'rgb(var(--bg-accent) / <alpha-value>)',
                fg: 'rgb(var(--fg) / <alpha-value>)',
                'fg-muted': 'rgb(var(--fg-muted) / <alpha-value>)',
                border: 'rgb(var(--border) / <alpha-value>)',
                accent: 'rgb(var(--accent) / <alpha-value>)',
                'accent-fg': 'rgb(var(--accent-fg) / <alpha-value>)',
                danger: 'rgb(var(--danger) / <alpha-value>)',
                success: 'rgb(var(--success) / <alpha-value>)',
            },
            fontFamily: {
                sans: ['Copiuma', 'Inter', 'system-ui', '-apple-system', 'Segoe UI', 'Roboto', 'sans-serif'],
            },
        },
    },
    plugins: [require('@tailwindcss/typography')],
} satisfies Config;