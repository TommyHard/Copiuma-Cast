import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { LanguageSwitcher } from '@/components/LanguageSwitcher';
import { ThemeToggle } from '@/components/ThemeToggle';

export function LandingPage() {
    const { t } = useTranslation();
    return (
        <div className="flex min-h-full flex-col">
            <header className="flex items-center gap-3 px-6 py-4">
                <span className="font-bold text-accent">{t('app.name')}</span>
                <div className="ml-auto flex items-center gap-2">
                    <LanguageSwitcher />
                    <ThemeToggle />
                </div>
            </header>
            <main className="mx-auto flex max-w-2xl flex-1 flex-col items-center justify-center px-6 text-center">
                <h1 className="text-4xl font-bold text-fg">{t('landing.heroTitle')}</h1>
                <p className="mt-4 text-lg text-fg-muted">{t('landing.heroSubtitle')}</p>
                <div className="mt-8 flex gap-3">
                    <Link to="/register" className="rounded-md bg-accent px-5 py-2.5 font-medium text-accent-fg hover:opacity-90">
                        {t('landing.getStarted')}
                    </Link>
                    <Link to="/login" className="rounded-md border border-border px-5 py-2.5 font-medium text-fg hover:border-accent">
                        {t('auth.login')}
                    </Link>
                </div>
            </main>
        </div>
    );
}