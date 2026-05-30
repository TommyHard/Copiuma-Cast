import { useState } from 'react';
import { Link, Navigate } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { api } from '@/lib/api';
import { useAuthStore } from '@/store/auth';
import { TagInput } from '@/components/TagInput';
import type { AdminApplication, AdminEvent, AdminGame, AdminUser, MediaItem, MediaStatus } from '@/lib/types';

const box = 'rounded-lg border border-border bg-bg-elevated p-4';
const input = 'rounded-md border border-border bg-bg px-2 py-1 text-sm text-fg outline-none focus:border-accent';
const btn = 'rounded-md border border-border px-2 py-1 text-sm text-fg hover:border-accent';

function MediaModeration() {
    const { t } = useTranslation();
    const qc = useQueryClient();
    const { data } = useQuery({ queryKey: ['mod-media'], queryFn: async () => (await api.get<MediaItem[]>('/media/moderation/pending')).data });
    const [form, setForm] = useState<Record<string, { tags: string[]; cost: number }>>({});
    const refetch = () => qc.invalidateQueries({ queryKey: ['mod-media'] });

    const approve = async (id: string) => {
        const f = form[id] ?? { tags: [], cost: 0 };
        await api.post(`/media/${id}/approve`, { tags: f.tags, costCoins: f.cost || 0 });
        refetch();
    };

    const reject = async (id: string) => { await api.post(`/media/${id}/reject`); refetch(); };

    return (
        <section className={box}>
            <h2 className="mb-2 font-semibold text-fg">{t('admin.moderation')}</h2>
            <div className="space-y-2">
                {(data ?? []).map((m) => (
                    <div key={m.id} className="rounded-md border border-border p-2">
                        <div className="text-sm text-fg">
                            {m.title} <span className="text-fg-muted">({t(`media.${m.type === 'Sound' ? 'sound' : 'video'}`)})</span>
                        </div>

                        {m.type === 'Sound' && m.originalUrl && (
                            <audio controls className="my-1 w-full">
                                <source src={m.originalUrl} />
                            </audio>
                        )}
                        {m.type === 'Video' && m.originalUrl && (
                            <video controls className="my-1 w-full max-h-64 rounded-md object-contain">
                                <source src={m.originalUrl} />
                            </video>
                        )}
                        <div className="mt-1 space-y-2">
                            <TagInput value={form[m.id]?.tags ?? (m.tags ?? [])}
                                onChange={(tags) => setForm((s) => ({ ...s, [m.id]: { ...(s[m.id] ?? { cost: 0 }), tags } }))} />
                            <div className="flex flex-wrap items-center gap-2">
                                <input className={`${input} w-24`} type="number" min={0} placeholder={t('admin.cost')} value={form[m.id]?.cost ?? ''}
                                    onChange={(e) => setForm((s) => ({ ...s, [m.id]: { ...(s[m.id] ?? { tags: m.tags ?? [] }), cost: +e.target.value } }))} />
                                <button onClick={() => approve(m.id)} className={btn}>{t('admin.approve')}</button>
                                <button onClick={() => reject(m.id)} className="rounded-md border border-border px-2 py-1 text-sm text-danger hover:border-danger">{t('admin.reject')}</button>
                            </div>
                        </div>
                    </div>
                ))}
                {data && data.length === 0 && <p className="text-sm text-fg-muted">{t('admin.noPending')}</p>}
            </div>
        </section>
    );
}

function Applications() {
    const { t } = useTranslation();
    const qc = useQueryClient();
    const { data } = useQuery({ queryKey: ['mod-apps'], queryFn: async () => (await api.get<AdminApplication[]>('/admin/applications')).data });
    const refetch = () => qc.invalidateQueries({ queryKey: ['mod-apps'] });
    const act = async (id: string, action: 'approve' | 'reject') => { await api.post(`/admin/applications/${id}/${action}`); refetch(); };

    return (
        <section className={box}>
            <h2 className="mb-2 font-semibold text-fg">{t('admin.applications')}</h2>
            <div className="space-y-2">
                {(data ?? []).map((a) => (
                    <div key={a.id} className="flex items-center gap-2 rounded-md border border-border p-2">
                        <div>
                            <div className="text-sm text-fg">{a.displayName} <span className="text-fg-muted">@{a.handle}</span></div>
                            {a.message && <div className="text-xs text-fg-muted">{a.message}</div>}
                        </div>
                        <div className="ml-auto flex gap-2">
                            <button onClick={() => act(a.id, 'approve')} className={btn}>{t('admin.approve')}</button>
                            <button onClick={() => act(a.id, 'reject')} className="rounded-md border border-border px-2 py-1 text-sm text-danger hover:border-danger">{t('admin.reject')}</button>
                        </div>
                    </div>
                ))}
                {data && data.length === 0 && <p className="text-sm text-fg-muted">{t('admin.noPending')}</p>}
            </div>
        </section>
    );
}

function Users() {
    const { t } = useTranslation();
    const qc = useQueryClient();
    const [search, setSearch] = useState('');
    const { data } = useQuery({
        queryKey: ['admin-users', search],
        queryFn: async () => (await api.get<AdminUser[]>('/admin/users', { params: { search: search || undefined } })).data,
    });
    const act = async (id: string, action: 'block' | 'unblock') => {
        await api.post(`/admin/users/${id}/${action}`);
        qc.invalidateQueries({ queryKey: ['admin-users'] });
    };

    return (
        <section className={box}>
            <h2 className="mb-2 font-semibold text-fg">{t('admin.users')}</h2>
            <input className={`${input} mb-2 w-full`} placeholder={t('admin.searchUsers')} value={search} onChange={(e) => setSearch(e.target.value)} />
            <div className="space-y-1">
                {(data ?? []).map((u) => (
                    <div key={u.id} className="flex items-center gap-2 rounded-md border border-border p-2">
                        <div className="min-w-0">
                            <div className="truncate text-sm text-fg">{u.displayName} <span className="text-fg-muted">@{u.handle}</span></div>
                            <div className="truncate text-xs text-fg-muted">{u.email}</div>
                        </div>
                        <span className={`ml-auto text-xs ${u.isBlocked ? 'text-danger' : 'text-success'}`}>{t(u.isBlocked ? 'admin.blocked' : 'admin.active')}</span>
                        {u.isBlocked
                            ? <button onClick={() => act(u.id, 'unblock')} className={btn}>{t('admin.unblock')}</button>
                            : <button onClick={() => act(u.id, 'block')} className="rounded-md border border-border px-2 py-1 text-sm text-danger hover:border-danger">{t('admin.block')}</button>}
                    </div>
                ))}
                {data && data.length === 0 && <p className="text-sm text-fg-muted">{t('common.empty')}</p>}
            </div>
        </section>
    );
}

function MediaManagement() {
    const { t } = useTranslation();
    const qc = useQueryClient();
    const [status, setStatus] = useState<'' | MediaStatus>('');
    const { data } = useQuery({
        queryKey: ['admin-media', status],
        queryFn: async () => (await api.get<MediaItem[]>('/media/admin', { params: { status: status || undefined } })).data,
    });
    const refetch = () => qc.invalidateQueries({ queryKey: ['admin-media'] });

    const suspend = async (id: string) => { await api.post(`/media/${id}/suspend`); refetch(); };
    const restore = async (id: string) => { await api.post(`/media/${id}/restore`); refetch(); };
    const remove = async (id: string) => { if (window.confirm(t('media.confirmDelete'))) { await api.delete(`/media/${id}`); refetch(); } };

    return (
        <section className={box}>
            <div className="mb-2 flex items-center justify-between">
                <h2 className="font-semibold text-fg">{t('media.manageMedia')}</h2>
                <select className={input} value={status} onChange={(e) => setStatus(e.target.value as '' | MediaStatus)}>
                    <option value="">{t('media.allStatuses')}</option>
                    <option value="Approved">{t('media.Approved')}</option>
                    <option value="Suspended">{t('media.Suspended')}</option>
                    <option value="Pending">{t('media.Pending')}</option>
                    <option value="Rejected">{t('media.Rejected')}</option>
                </select>
            </div>
            <div className="space-y-1">
                {(data ?? []).map((m) => (
                    <div key={m.id} className="flex items-center gap-2 rounded-md border border-border p-2">
                        <Link to={`/media/${m.id}`} className="flex-1 truncate text-sm text-fg hover:text-accent hover:underline">{m.title}</Link>
                        <span className="rounded bg-bg-accent px-1.5 py-0.5 text-xs text-fg-muted">{t(`media.${m.status}`)}</span>
                        {m.status === 'Suspended'
                            ? <button onClick={() => restore(m.id)} className={btn}>{t('media.restore')}</button>
                            : <button onClick={() => suspend(m.id)} className={btn}>{t('media.suspend')}</button>}
                        <button onClick={() => remove(m.id)} className="rounded-md border border-border px-2 py-1 text-sm text-danger hover:border-danger">{t('media.delete')}</button>
                    </div>
                ))}
                {data && data.length === 0 && <p className="text-sm text-fg-muted">{t('common.empty')}</p>}
            </div>
        </section>
    );
}

export function AdminPage() {
    const { t } = useTranslation();
    const isAdmin = useAuthStore((s) => s.hasRole('Admin'));
    if (!isAdmin) return <Navigate to="/" replace />;

    return (
        <div className="mx-auto max-w-3xl space-y-5">
            <h1 className="text-2xl font-bold text-fg">{t('admin.title')}</h1>
            <MediaModeration />
            <MediaManagement />
            <Applications />
            <Users />
        </div>
    );
}