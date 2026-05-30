import { useEffect, useState, type ReactNode } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { api } from '@/lib/api';
import { useAuthStore } from '@/store/auth';
import type { MediaItem } from '@/lib/types';
import { TagInput } from '@/components/TagInput';

/** Карточка медиа; для админа — правка/приостановка/удаление */
export function MediaDetailPage() {
    const { t } = useTranslation();
    const { id } = useParams();
    const qc = useQueryClient();
    const navigate = useNavigate();
    const isAdmin = useAuthStore((s) => s.hasRole('Admin'));

    const { data: m, isLoading, isError } = useQuery({
        queryKey: ['media-detail', id],
        queryFn: async () => (await api.get<MediaItem>(`/media/${id}`)).data,
        enabled: !!id,
    });

    const [tags, setTags] = useState<string[]>([]);
    const [cost, setCost] = useState(0);
    useEffect(() => { if (m) { setTags(m.tags); setCost(m.costCoins); } }, [m]);

    const refetch = () => qc.invalidateQueries({ queryKey: ['media-detail', id] });
    const saveEdit = async () => { await api.put(`/media/${id}/admin-edit`, { tags, costCoins: cost }); refetch(); };
    const suspend = async () => { await api.post(`/media/${id}/suspend`); refetch(); };
    const restore = async () => { await api.post(`/media/${id}/restore`); refetch(); };
    const remove = async () => {
        if (!window.confirm(t('media.confirmDelete'))) return;
        await api.delete(`/media/${id}`);
        navigate('/media/catalog');
    };

    if (isLoading) return <p className="p-6 text-fg-muted">{t('common.loading')}</p>;
    if (isError || !m) return (
        <div className="p-6">
            <p className="text-fg-muted">{t('media.notFound')}</p>
            <Link to="/media/catalog" className="text-sm text-accent hover:underline">{t('media.backToCatalog')}</Link>
        </div>
    );

    const row = (label: string, val: ReactNode) => (
        <div className="flex justify-between border-b border-border py-2 text-sm">
            <span className="text-fg-muted">{label}</span>
            <span className="text-fg">{val}</span>
        </div>
    );

    const btn = 'rounded-md border border-border px-3 py-1.5 text-sm text-fg hover:border-accent';

    return (
        <div className="mx-auto max-w-2xl space-y-4">
            <Link to="/media/catalog" className="text-sm text-accent hover:underline">← {t('media.backToCatalog')}</Link>
            <h1 className="text-2xl font-bold text-fg">{m.title}</h1>

            {m.type === 'Sound' && m.processed && (m.webmUrl || m.oggUrl) && (
                <audio controls className="w-full">
                    {m.webmUrl && <source src={m.webmUrl} type="audio/webm" />}
                    {m.oggUrl && <source src={m.oggUrl} type="audio/ogg" />}
                </audio>
            )}
            {m.type === 'Video' && <video controls className="w-full rounded-md"><source src={m.originalUrl} /></video>}

            <div className="rounded-lg border border-border bg-bg-elevated p-4">
                {row(t('media.type'), t(`media.${m.type === 'Sound' ? 'sound' : 'video'}`))}
                {row(t('media.status'), t(`media.${m.status}`))}
                {row(t('media.cost'), m.costCoins)}
                {row(t('media.tags'), m.tags.length ? m.tags.map((tg) => `#${tg}`).join(' ') : '—')}
                {row(t('media.uploadedBy'), m.ownerName ?? '—')}
                {row(t('media.approvedBy'), m.approvedByName ?? '—')}
                {row(t('media.reviewedAt'), m.reviewedAt ? new Date(m.reviewedAt).toLocaleString() : '—')}
            </div>

            {isAdmin && (
                <div className="space-y-3 rounded-lg border border-accent/40 bg-bg-elevated p-4">
                    <h2 className="font-semibold text-fg">{t('media.adminActions')}</h2>
                    <TagInput value={tags} onChange={setTags} />
                    <div className="flex flex-wrap items-center gap-2">
                        <input className="w-24 rounded-md border border-border bg-bg px-2 py-1 text-sm text-fg outline-none focus:border-accent"
                            type="number" min={0} value={cost} onChange={(e) => setCost(+e.target.value)} />
                        <button onClick={saveEdit} className={btn}>{t('media.saveTags')}</button>
                        {m.status === 'Suspended'
                            ? <button onClick={restore} className={btn}>{t('media.restore')}</button>
                            : <button onClick={suspend} className={btn}>{t('media.suspend')}</button>}
                        <button onClick={remove} className="rounded-md border border-border px-3 py-1.5 text-sm text-danger hover:border-danger">{t('media.delete')}</button>
                    </div>
                </div>
            )}
        </div>
    );
}