import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { api } from '@/lib/api';
import type { MediaItem, MediaType } from '@/lib/types';
import { TagInput } from '@/components/TagInput';

/** Каталог всех одобренных медиа с поиском, фильтром и сортировкой */
export function MediaCatalogPage() {
    const { t } = useTranslation();
    const [search, setSearch] = useState('');
    const [type, setType] = useState<'' | MediaType>('');
    const [tag, setTag] = useState<string[]>([]);
    const [sort, setSort] = useState('newest');

    const { data, isLoading } = useQuery({
        queryKey: ['media-catalog', search, type, tag[0] ?? '', sort],
        queryFn: async () => (await api.get<MediaItem[]>('/media/catalog', {
            params: { search: search || undefined, type: type || undefined, tag: tag[0] || undefined, sort },
        })).data,
    });

    const input = 'rounded-md border border-border bg-bg px-3 py-2 text-fg outline-none focus:border-accent';

    return (
        <div className="mx-auto max-w-4xl space-y-5">
            <div className="flex items-center justify-between">
                <h1 className="text-2xl font-bold text-fg">{t('media.catalog')}</h1>
                <Link to="/media" className="text-sm text-accent hover:underline">{t('media.myLibrary')}</Link>
            </div>

            <div className="grid gap-3 rounded-lg border border-border bg-bg-elevated p-4 sm:grid-cols-2 lg:grid-cols-4">
                <input className={input} placeholder={t('media.search')} value={search} onChange={(e) => setSearch(e.target.value)} />
                <select className={input} value={type} onChange={(e) => setType(e.target.value as '' | MediaType)}>
                    <option value="">{t('media.allTypes')}</option>
                    <option value="Sound">{t('media.sound')}</option>
                    <option value="Video">{t('media.video')}</option>
                </select>
                <select className={input} value={sort} onChange={(e) => setSort(e.target.value)}>
                    <option value="newest">{t('media.sortNewest')}</option>
                    <option value="title">{t('media.sortTitle')}</option>
                    <option value="cheap">{t('media.sortCheap')}</option>
                    <option value="expensive">{t('media.sortExpensive')}</option>
                </select>
                <TagInput value={tag} onChange={(v) => setTag(v.slice(-1))} placeholder={t('media.anyTag')} />
            </div>

            {isLoading ? <p className="text-fg-muted">{t('common.loading')}</p> : (
                <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
                    {(data ?? []).map((m) => (
                        <Link key={m.id} to={`/media/${m.id}`}
                            className="rounded-lg border border-border bg-bg-elevated p-3 hover:border-accent">
                            <div className="flex items-center justify-between">
                                <span className="font-medium text-fg">{m.title}</span>
                                <span className="rounded bg-bg-accent px-2 py-0.5 text-xs text-accent">{m.costCoins}</span>
                            </div>
                            <div className="mt-1 flex flex-wrap gap-1 text-xs text-fg-muted">
                                <span>{t(`media.${m.type === 'Sound' ? 'sound' : 'video'}`)}</span>
                                {m.tags.map((tg) => <span key={tg} className="rounded bg-bg-accent px-1.5 py-0.5">#{tg}</span>)}
                            </div>
                            {m.ownerName && <div className="mt-1 text-xs text-fg-muted">{t('media.uploadedBy')}: {m.ownerName}</div>}
                        </Link>
                    ))}
                    {data && data.length === 0 && <p className="text-fg-muted">{t('common.empty')}</p>}
                </div>
            )}
        </div>
    );
}