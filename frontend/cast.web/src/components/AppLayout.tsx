import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuthStore } from '@/store/auth';
import { LanguageSwitcher } from './LanguageSwitcher';
import { ThemeToggle } from './ThemeToggle';
import { RightPanel } from './RightPanel';
import { usePresence } from '@/hooks/usePresence';

const navItems = [
  { to: '/', key: 'nav.dashboard', end: true },
  { to: '/games', key: 'nav.games', end: false },
  { to: '/rooms', key: 'nav.rooms', end: false },
  { to: '/media', key: 'nav.media', end: true },
  { to: '/media/catalog', key: 'nav.mediaCatalog', end: false },
  { to: '/friends', key: 'nav.friends', end: false },
  { to: '/profile', key: 'nav.profile', end: false },
];

export function AppLayout() {
    const { t } = useTranslation();
    const navigate = useNavigate();
    const { user, logout } = useAuthStore();
    usePresence();
    const isAdmin = useAuthStore((s) => s.hasRole('Admin'));
    const isStreamer = useAuthStore((s) => s.hasRole('Streamer'));

    return (
        <div className="flex h-full flex-col">
            <header className="flex items-center gap-3 border-b border-border bg-bg-elevated px-4 py-2">
                <span className="font-bold text-accent">{t('app.name')}</span>
                <div className="ml-auto flex items-center gap-2">
                    <LanguageSwitcher />
                    <ThemeToggle />
                    <span className="hidden text-sm text-fg-muted sm:inline">@{user?.handle}</span>
                    <button
                        type="button"
                        onClick={() => { logout(); navigate('/login'); }}
                        className="rounded-md border border-border px-2 py-1 text-sm text-fg hover:border-danger"
                    >
                        {t('nav.logout')}
                    </button>
                </div>
            </header>

            <div className="flex min-h-0 flex-1">
                <nav className="hidden w-48 shrink-0 border-r border-border bg-bg-elevated p-2 sm:block">
                    {navItems.map((it) => (
                        <NavLink
                            key={it.to}
                            to={it.to}
                            end={it.end}
                            className={({ isActive }) =>
                                `block rounded-md px-3 py-2 text-sm ${isActive ? 'bg-bg-accent text-accent' : 'text-fg hover:bg-bg-accent'}`
                            }
                        >
                            {t(it.key)}
                        </NavLink>
                    ))}
                    {isStreamer && (
                        <NavLink
                            to="/streamer"
                            className={({ isActive }) =>
                                `block rounded-md px-3 py-2 text-sm ${isActive ? 'bg-bg-accent text-accent' : 'text-fg hover:bg-bg-accent'}`
                            }
                        >
                            {t('nav.streamer')}
                        </NavLink>
                    )}
                    {isAdmin && (
                        <NavLink
                            to="/admin"
                            className={({ isActive }) =>
                                `block rounded-md px-3 py-2 text-sm ${isActive ? 'bg-bg-accent text-accent' : 'text-fg hover:bg-bg-accent'}`
                            }
                        >
                            {t('nav.admin')}
                        </NavLink>
                    )}
                </nav>

                <main className="min-w-0 flex-1 overflow-auto p-4"><Outlet /></main>
                <RightPanel />
            </div>
        </div>
    );
}