import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useParams, useNavigate, Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import rehypeSanitize from 'rehype-sanitize';
import { api } from '@/lib/api';
import { useAuthStore } from '@/store/auth';
import { NewsEditorModal } from './NewsEditorModal';
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

export function NewsDetailPage() {
    const { id } = useParams<{ id: string }>();
    const { t } = useTranslation();
    const qc = useQueryClient();
    const navigate = useNavigate();
    const isAdmin = useAuthStore((s) => s.hasRole('Admin'));
    const [editing, setEditing] = useState(false);

    const { data, isLoading, error } = useQuery({
        queryKey: ['news', id],
        queryFn: async () => (await api.get<NewsPost>(`/news/${id}`)).data,
    });

    const refresh = () => { qc.invalidateQueries({ queryKey: ['news', id] }); qc.invalidateQueries({ queryKey: ['news'] }); };
    const togglePublish = async () => { if (!data) return; await api.put(`/news/${data.id}`, { title: data.title, body: data.body, imageUrl: data.imageUrl, published: !data.published }); refresh(); };
    const remove = async () => { if (data && window.confirm(t('admin.confirmDeleteNews'))) { await api.delete(`/news/${data.id}`); qc.invalidateQueries({ queryKey: ['news'] }); navigate('/'); } };

    if (isLoading) return <div className="p-4 text-fg">{t('common.loading')}</div>;
    if (error || !data) return <div className="p-4 text-danger">{t('common.error')}</div>;

    return (
        <div className="mx-auto max-w-3xl space-y-4">
            <div className="flex items-center justify-between">
                <Link to="/" className="inline-block text-sm text-accent hover:underline">
                    &larr; {t('common.back')}
                </Link>
                {isAdmin && (
                    <div className="flex items-center gap-2">
                        <span className={`text-xs ${data.published ? 'text-success' : 'text-fg-muted'}`}>
                            {data.published ? t('admin.published') : t('admin.draft')}
                        </span>
                        <button onClick={togglePublish} className="rounded-md border border-border px-2 py-1 text-sm text-fg hover:border-accent">
                            {data.published ? t('admin.disable') : t('admin.enable')}
                        </button>
                        <button onClick={() => setEditing(true)} className="rounded-md border border-border px-2 py-1 text-sm text-fg hover:border-accent">
                            {t('news.edit')}
                        </button>
                        <button onClick={remove} className="rounded-md border border-border px-2 py-1 text-sm text-danger hover:border-danger">
                            {t('admin.delete')}
                        </button>
                    </div>
                )}
            </div>

            <article className="rounded-lg border border-border bg-bg-elevated p-6">
                {data.imageUrl && (
                    <img src={data.imageUrl} alt="" className="mb-6 max-h-80 w-full rounded-md object-cover" />
                )}

                <h1 className="mb-2 text-3xl font-bold text-fg">{data.title}</h1>

                <div className="mb-8 flex flex-wrap items-center gap-2 text-sm text-fg-muted">
                    <time>{new Date(data.createdAt).toLocaleDateString()}</time>
                    {data.updatedAt && (
                        <>
                            <span>&bull;</span>
                            <span className="italic">
                                {t('news.edited')} {new Date(data.updatedAt).toLocaleDateString()}
                            </span>
                        </>
                    )}
                    <span>&bull;</span>
                    <AuthorAvatar name={data.authorName} url={data.authorAvatarUrl} />  <span className="font-medium text-fg">{data.authorName}</span>
                </div>

                <div className="prose prose-invert max-w-none text-fg">
                    <ReactMarkdown
                        remarkPlugins={[remarkGfm]}
                        rehypePlugins={[rehypeSanitize]}
                    >
                        {data.body}
                    </ReactMarkdown>
                </div>
            </article>

            {isAdmin && (
                <NewsEditorModal isOpen={editing} onClose={() => setEditing(false)} post={data} onSaved={refresh} />
            )}
        </div>
    );
}