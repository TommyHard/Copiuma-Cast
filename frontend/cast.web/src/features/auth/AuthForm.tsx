import { useState, type FormEvent } from 'react';
import { useNavigate, Link, useSearchParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { api } from '@/lib/api';
import { useAuthStore } from '@/store/auth';
import type { AuthResponse } from '@/lib/types';

function redirectToDesktop(callbackUrl: string, token: string) {
    const url = new URL(callbackUrl);
    url.searchParams.set('token', token);
    window.location.href = url.toString();
}

export function AuthForm({ mode }: { mode: 'login' | 'register' }) {
    const { t } = useTranslation();
    const navigate = useNavigate();
    const [searchParams] = useSearchParams();
    const setAuth = useAuthStore((s) => s.setAuth);

    const desktopCallback = searchParams.get('desktop');

    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const [displayName, setDisplayName] = useState('');
    const [handle, setHandle] = useState('');
    const [error, setError] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);

    async function submit(e: FormEvent) {
        e.preventDefault();
        setError(null);
        setBusy(true);
        try {
            const body = mode === 'register' ? { email, password, displayName, handle } : { email, password };
            const res = await api.post<AuthResponse>(`/auth/${mode}`, body);
            setAuth(res.data);

            // Desktop-авторизация: отдаём токен десктоп-клиенту через redirect
            if (desktopCallback) {
                redirectToDesktop(desktopCallback, res.data.accessToken);
                return;
            }

            navigate('/');
        } catch (err: any) {
            const status = err?.response?.status;
            if (status === 409) setError(t('auth.handleTaken'));
            else if (status === 403) setError(t('auth.blocked'));
            else if (status === 401) setError(t('auth.loginError'));
            else setError(t('common.error'));
        } finally {
            setBusy(false);
        }
    }

    const input = 'w-full rounded-md border border-border bg-bg px-3 py-2 text-fg outline-none focus:border-accent';

    return (
        <div className="mx-auto flex min-h-full max-w-sm flex-col justify-center px-6">
            <h1 className="mb-6 text-2xl font-bold text-fg">{t(`auth.${mode}`)}</h1>

            {desktopCallback && (
                <p className="mb-4 rounded-md border border-accent/30 bg-accent/10 px-3 py-2 text-sm text-accent">
                    {t('auth.desktopHint', 'После входа вы будете перенаправлены в приложение Copiuma.Cast')}
                </p>
            )}

            <form onSubmit={submit} className="space-y-3">
                <input className={input} type="email" placeholder={t('auth.email')} value={email}
                    onChange={(e) => setEmail(e.target.value)} required />
                <input className={input} type="password" placeholder={t('auth.password')} value={password}
                    onChange={(e) => setPassword(e.target.value)} required />
                {mode === 'register' && (
                    <>
                        <input className={input} placeholder={t('auth.displayName')} value={displayName}
                            onChange={(e) => setDisplayName(e.target.value)} required />
                        <div>
                            <input className={input} placeholder={t('auth.handle')} value={handle}
                                onChange={(e) => setHandle(e.target.value)} required />
                            <p className="mt-1 text-xs text-fg-muted">{t('auth.handleHint')}</p>
                        </div>
                    </>
                )}
                {error && <p className="text-sm text-danger">{error}</p>}
                <button type="submit" disabled={busy}
                    className="w-full rounded-md bg-accent px-4 py-2 font-medium text-accent-fg hover:opacity-90 disabled:opacity-50">
                    {t(`auth.${mode}`)}
                </button>
            </form>
            <p className="mt-4 text-sm text-fg-muted">
                {mode === 'login' ? t('auth.noAccount') : t('auth.haveAccount')}{' '}
                <Link className="text-accent" to={mode === 'login' ? '/register' : '/login'}>
                    {t(mode === 'login' ? 'auth.register' : 'auth.login')}
                </Link>
            </p>
        </div>
    );
}