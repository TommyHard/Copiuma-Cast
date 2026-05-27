import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { api } from '@/lib/api';
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
    const { data } = useQuery({
        queryKey: ['news'],
        queryFn: async () => (await api.get<NewsPost[]>('/news')).data,
    });

    return (
        <div className="mx-auto max-w-3xl space-y-4">
            <section className="rounded-lg border border-border bg-bg-elevated p-4">
                <h2 className="mb-3 text-lg font-semibold text-fg">{t('dashboard.news')}</h2>
                {data && data.length > 0 ? (
                    <div className="space-y-4">
                        {data.map((n) => (
                            <article key={n.id} className="border-b border-border pb-3 last:border-0">
                                {n.imageUrl && <img src={n.imageUrl} alt="" className="mb-2 max-h-48 w-full rounded-md object-cover" />}
                                <Link to={`/news/${n.id}`} className="hover:underline">
                                    <h3 className="font-semibold text-fg">{n.title}</h3>
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
        </div>
    );
}