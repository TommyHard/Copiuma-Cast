import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { api } from '@/lib/api';
import { useAuthStore } from '@/store/auth';
import { NewsEditorModal } from '@/features/news/NewsEditorModal';
import type { NewsPost } from '@/lib/types';

function AuthorAvatar({ name, url }: { name: string; url?: string | null }) {
    if (url) {
        return <img src={url} alt="" className="h-6 w-6 rounded-full object-cover border border-border bg-bg" />;
    }
    return (
        <div className="flex h-6 w-6 items-center justify-center rounded-full bg-accent/20 border border-accent/30 text-[10px] font-bold text-accent uppercase">
            {name.substring(0, 1)}
        </div>
    );
}

export function DashboardPage() {
    const { t } = useTranslation();
    const qc = useQueryClient();
    const isAdmin = useAuthStore((s) => s.hasRole('Admin'));
    const [creating, setCreating] = useState(false);

    const { data } = useQuery({
        queryKey: ['news', isAdmin],
        queryFn: async () => (await api.get<NewsPost[]>(isAdmin ? '/news/admin/all' : '/news')).data,
    });

    return (
        <div className="mx-auto max-w-3xl space-y-4">
            <section className="rounded-lg border border-border bg-bg-elevated p-4">
                <div className="mb-3 flex items-center justify-between">
                    <h2 className="text-lg font-semibold text-fg">{t('dashboard.news')}</h2>
                    {isAdmin && (
                        <button onClick={() => setCreating(true)} className="rounded-md bg-accent px-3 py-1.5 text-sm font-medium text-accent-fg hover:opacity-90">
                            + {t('admin.create')}
                        </button>
                    )}
                </div>
                {data && data.length > 0 ? (
                    <div className="space-y-4">
                        {data.map((n) => (
                            <article key={n.id} className="border-b border-border pb-3 last:border-0">
                                {n.imageUrl && <img src={n.imageUrl} alt="" className="mb-2 max-h-48 w-full rounded-md object-cover" />}
                                <Link to={`/news/${n.id}`} className="hover:underline">
                                    <h3 className="font-semibold text-fg">
                                        {!n.published && <span className="mr-2 text-danger">[Скрыто]</span>}
                                        {n.title}
                                    </h3>
                                </Link>

                                <div className="mt-1 flex items-center gap-2 text-xs text-fg-muted">
                                    <time>{new Date(n.createdAt).toLocaleDateString()}</time>
                                    {n.updatedAt && (
                                        <>
                                            <span>&bull;</span>
                                            <span className="italic">{t('news.edited')} {new Date(n.updatedAt).toLocaleDateString()}</span>
                                        </>
                                    )}
                                    <span>&bull;</span>
                                    <AuthorAvatar name={n.authorName} url={n.authorAvatarUrl} /> <span className="text-fg font-medium">{n.authorName}</span>
                                </div>

                                <Link to={`/news/${n.id}`} className="text-accent text-sm mt-2 inline-block hover:underline">
                                    {t('news.readMore')}
                                </Link>
                            </article>
                        ))}
                    </div>
                ) : (
                    <p className="text-sm text-fg-muted">{t('common.empty')}</p>
                )}
            </section>

            {isAdmin && (
                <NewsEditorModal
                    isOpen={creating}
                    onClose={() => setCreating(false)}
                    onSaved={() => qc.invalidateQueries({ queryKey: ['news'] })}
                />
            )}
        </div>
    );
}