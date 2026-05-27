import { useState, type FormEvent } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { api } from '@/lib/api';
import type { FriendRequest, UserCard } from '@/lib/types';
import { StatusText } from '@/components/StatusBadge';

export function SocialPage() {
    const { t } = useTranslation();
    const qc = useQueryClient();
    const [handle, setHandle] = useState('');
    const [msg, setMsg] = useState<string | null>(null);

    const friends = useQuery({ queryKey: ['friends'], queryFn: async () => (await api.get<UserCard[]>('/social/friends')).data });
    const following = useQuery({ queryKey: ['following'], queryFn: async () => (await api.get<UserCard[]>('/social/following')).data });
    const requests = useQuery({ queryKey: ['requests'], queryFn: async () => (await api.get<FriendRequest[]>('/social/friends/requests')).data });

    const refetchAll = () => {
        void qc.invalidateQueries({ queryKey: ['friends'] });
        void qc.invalidateQueries({ queryKey: ['following'] });
        void qc.invalidateQueries({ queryKey: ['requests'] });
    };

    async function addFriend(e: FormEvent) {
        e.preventDefault();
        setMsg(null);
        try {
            await api.post('/social/friends/requests', { handle });
            setHandle(''); setMsg(t('social.sent')); refetchAll();
        } catch (err: any) {
            const s = err?.response?.status;
            setMsg(s === 404 ? t('social.notFound') : s === 409 ? t('social.already') : t('common.error'));
        }
    }

    const accept = async (id: string) => { await api.post(`/social/friends/requests/${id}/accept`); refetchAll(); };
    const removeFriend = async (userId: string) => { await api.delete(`/social/friends/${userId}`); refetchAll(); };
    const unfollow = async (streamerId: string) => { await api.delete(`/social/follow/${streamerId}`); refetchAll(); };
    const follow = async (streamerId: string) => { try { await api.post(`/social/follow/${streamerId}`); } catch { /* ignore */ } refetchAll(); };

    const input = 'rounded-md border border-border bg-bg px-3 py-2 text-fg outline-none focus:border-accent';
    const card = 'flex items-center gap-2 rounded-md border border-border bg-bg-elevated px-3 py-2';

    return (
        <div className="mx-auto max-w-2xl space-y-6">
            <h1 className="text-2xl font-bold text-fg">{t('social.title')}</h1>

            <form onSubmit={addFriend} className="flex flex-wrap items-center gap-2">
                <input className={input} placeholder={t('social.byHandle')} value={handle} onChange={(e) => setHandle(e.target.value)} required />
                <button type="submit" className="rounded-md bg-accent px-4 py-2 font-medium text-accent-fg hover:opacity-90">{t('social.addFriend')}</button>
                {msg && <span className="text-sm text-fg-muted">{msg}</span>}
            </form>

            <section>
                <h2 className="mb-2 text-lg font-semibold text-fg">{t('social.requests')}</h2>
                <div className="space-y-2">
                    {(requests.data ?? []).map((r) => (
                        <div key={r.linkId} className={card}>
                            <span className="text-fg">{r.displayName} <span className="text-fg-muted">@{r.handle}</span></span>
                            <div className="ml-auto flex gap-2">
                                <button onClick={() => accept(r.linkId)} className="rounded-md border border-border px-2 py-1 text-sm text-fg hover:border-accent">{t('social.accept')}</button>
                                <button onClick={() => removeFriend(r.userId)} className="rounded-md border border-border px-2 py-1 text-sm text-fg hover:border-danger">{t('social.decline')}</button>
                            </div>
                        </div>
                    ))}
                    {requests.data && requests.data.length === 0 && <p className="text-sm text-fg-muted">{t('common.empty')}</p>}
                </div>
            </section>

            <section>
                <h2 className="mb-2 text-lg font-semibold text-fg">{t('social.friends')}</h2>
                <div className="space-y-2">
                    {(friends.data ?? []).map((c) => (
                        <div key={c.userId} className={card}>
                            <div><div className="text-fg">{c.displayName} <span className="text-fg-muted">@{c.handle}</span></div><StatusText card={c} /></div>
                            <div className="ml-auto flex gap-2">
                                <button onClick={() => follow(c.userId)} className="rounded-md border border-border px-2 py-1 text-sm text-fg hover:border-accent">{t('social.follow')}</button>
                                <button onClick={() => removeFriend(c.userId)} className="rounded-md border border-border px-2 py-1 text-sm text-fg hover:border-danger">{t('social.remove')}</button>
                            </div>
                        </div>
                    ))}
                    {friends.data && friends.data.length === 0 && <p className="text-sm text-fg-muted">{t('dashboard.noFriends')}</p>}
                </div>
            </section>

            <section>
                <h2 className="mb-2 text-lg font-semibold text-fg">{t('social.following')}</h2>
                <div className="space-y-2">
                    {(following.data ?? []).map((c) => (
                        <div key={c.userId} className={card}>
                            <div><div className="text-fg">{c.displayName} <span className="text-fg-muted">@{c.handle}</span></div><StatusText card={c} /></div>
                            <button onClick={() => unfollow(c.userId)} className="ml-auto rounded-md border border-border px-2 py-1 text-sm text-fg hover:border-danger">{t('social.unfollow')}</button>
                        </div>
                    ))}
                    {following.data && following.data.length === 0 && <p className="text-sm text-fg-muted">{t('dashboard.noFollowed')}</p>}
                </div>
            </section>
        </div>
    );
}