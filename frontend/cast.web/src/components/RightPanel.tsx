import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { api } from '@/lib/api';
import type { UserCard } from '@/lib/types';
import { StatusText } from './StatusBadge';

function UserRow({ card }: { card: UserCard }) {
    return (
        <div className="flex items-center gap-2 rounded-md px-2 py-1.5 hover:bg-bg-accent">
            <div className="h-8 w-8 shrink-0 overflow-hidden rounded-full bg-bg-accent">
                {card.avatarUrl && <img src={card.avatarUrl} alt="" className="h-full w-full object-cover" />}
            </div>
            <div className="min-w-0">
                <div className="truncate text-sm text-fg">{card.displayName}</div>
                <StatusText card={card} />
            </div>
        </div>
    );
}

function List({ title, queryKey, url, empty }: { title: string; queryKey: string; url: string; empty: string }) {
    const { data, isLoading } = useQuery({
        queryKey: [queryKey],
        queryFn: async () => (await api.get<UserCard[]>(url)).data,
    });
    return (
        <section className="mb-4">
            <h3 className="mb-1 px-2 text-xs font-semibold uppercase tracking-wide text-fg-muted">{title}</h3>
            {isLoading ? null : data && data.length > 0 ? (
                data.map((c) => <UserRow key={c.userId} card={c} />)
            ) : (
                <p className="px-2 text-xs text-fg-muted">{empty}</p>
            )}
        </section>
    );
}

export function RightPanel() {
    const { t } = useTranslation();
    return (
        <aside className="hidden w-64 shrink-0 border-l border-border bg-bg-elevated p-2 lg:block">
            <List title={t('dashboard.friends')} queryKey="friends" url="/social/friends" empty={t('dashboard.noFriends')} />
            <List title={t('dashboard.followed')} queryKey="following" url="/social/following" empty={t('dashboard.noFollowed')} />
        </aside>
    );
}