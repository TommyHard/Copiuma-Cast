import { useState, type FormEvent } from 'react';
import { Navigate } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import rehypeSanitize from 'rehype-sanitize';
import { api } from '@/lib/api';
import { useAuthStore } from '@/store/auth';
import { ImageCropperModal } from '@/components/ImageCropperModal';
import type { AdminApplication, AdminEvent, AdminGame, AdminUser, MediaItem, NewsPost } from '@/lib/types';

const box = 'rounded-lg border border-border bg-bg-elevated p-4';
const input = 'rounded-md border border-border bg-bg px-2 py-1 text-sm text-fg outline-none focus:border-accent';
const btn = 'rounded-md border border-border px-2 py-1 text-sm text-fg hover:border-accent';

function MediaModeration() {
    const { t } = useTranslation();
    const qc = useQueryClient();
    const { data } = useQuery({ queryKey: ['mod-media'], queryFn: async () => (await api.get<MediaItem[]>('/media/moderation/pending')).data });
    const [form, setForm] = useState<Record<string, { tags: string; cost: number }>>({});
    const refetch = () => qc.invalidateQueries({ queryKey: ['mod-media'] });

    const approve = async (id: string) => {
        const f = form[id] ?? { tags: '', cost: 0 };
        await api.post(`/media/${id}/approve`, { tags: f.tags.split(',').map((s) => s.trim()).filter(Boolean), costCoins: f.cost || 0 });
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
                        <div className="mt-1 flex flex-wrap items-center gap-2">
                            <input className={input} placeholder={t('admin.tags')} value={form[m.id]?.tags ?? ''}
                                onChange={(e) => setForm((s) => ({ ...s, [m.id]: { ...(s[m.id] ?? { cost: 0 }), tags: e.target.value } }))} />
                            <input className={`${input} w-24`} type="number" min={0} placeholder={t('admin.cost')} value={form[m.id]?.cost ?? ''}
                                onChange={(e) => setForm((s) => ({ ...s, [m.id]: { ...(s[m.id] ?? { tags: '' }), cost: +e.target.value } }))} />
                            <button onClick={() => approve(m.id)} className={btn}>{t('admin.approve')}</button>
                            <button onClick={() => reject(m.id)} className="rounded-md border border-border px-2 py-1 text-sm text-danger hover:border-danger">{t('admin.reject')}</button>
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

function NewsManager() {
    const { t } = useTranslation();
    const qc = useQueryClient();
    const { data } = useQuery({
        queryKey: ['admin-news'],
        queryFn: async () => (await api.get<NewsPost[]>('/news/admin/all')).data
    });

    const [isModalOpen, setIsModalOpen] = useState(false);
    const [editingId, setEditingId] = useState<string | null>(null);
    const [form, setForm] = useState({ title: '', body: '', imageUrl: '', published: true });
    const [isPreview, setIsPreview] = useState(false);
    const [cropper, setCropper] = useState<{ isOpen: boolean; src: string; target: 'cover' | 'inline' }>({ isOpen: false, src: '', target: 'cover' });

    const refetch = () => qc.invalidateQueries({ queryKey: ['admin-news'] });

    const openCreate = () => {
        setEditingId(null);
        setForm({ title: '', body: '', imageUrl: '', published: true });
        setIsPreview(false);
        setIsModalOpen(true);
    };

    const openEdit = (n: NewsPost) => {
        setEditingId(n.id);
        setForm({ title: n.title, body: n.body, imageUrl: n.imageUrl || '', published: n.published });
        setIsPreview(false);
        setIsModalOpen(true);
    };

    const handleFileSelect = (e: React.ChangeEvent<HTMLInputElement>, target: 'cover' | 'inline') => {
        const file = e.target.files?.[0];
        if (!file) return;

        const reader = new FileReader();
        reader.onload = () => {
            setCropper({ isOpen: true, src: reader.result as string, target });
        };
        reader.readAsDataURL(file);
        e.target.value = '';
    };

    const executeUpload = async (blob: Blob) => {
        const formData = new FormData();
        formData.append('file', blob, 'cropped.jpg');

        try {
            const res = await api.post('/news/image', formData, { headers: { 'Content-Type': 'multipart/form-data' } });
            if (cropper.target === 'cover') {
                setForm((prev) => ({ ...prev, imageUrl: res.data.url }));
            } else {
                setForm((prev) => ({ ...prev, body: `${prev.body}\n![image](${res.data.url})\n` }));
            }
        } catch (error) {
            console.error('Upload failed', error);
        } finally {
            setCropper({ isOpen: false, src: '', target: 'cover' });
        }
    };

    const saveNews = async (e: FormEvent) => {
        e.preventDefault();
        const payload = { title: form.title, body: form.body, imageUrl: form.imageUrl || null, published: form.published };

        if (editingId) await api.put(`/news/${editingId}`, payload);
        else await api.post('/news', payload);

        setIsModalOpen(false);
        refetch();
    };

    const togglePublish = async (n: NewsPost) => {
        await api.put(`/news/${n.id}`, { title: n.title, body: n.body, imageUrl: n.imageUrl, published: !n.published });
        refetch();
    };

    const del = async (id: string) => {
        if (confirm('Удалить новость и все её картинки из сервера?')) {
            await api.delete(`/news/${id}`);
            refetch();
        }
    };

    return (
        <section className={box}>
            <div className="mb-4 flex items-center justify-between">
                <h2 className="font-semibold text-fg">{t('admin.news')}</h2>
                <button onClick={openCreate} className="rounded-md bg-accent px-3 py-1.5 text-sm font-medium text-accent-fg hover:opacity-90">
                    + {t('admin.create')}
                </button>
            </div>

            <div className="space-y-1">
                {(data ?? []).map((n) => (
                    <div key={n.id} className="flex items-center gap-2 rounded-md border border-border p-2">
                        <div className="flex-1 min-w-0">
                            <div className="truncate text-sm text-fg font-medium">{n.title}</div>
                            <div className="text-xs text-fg-muted">
                                {new Date(n.createdAt).toLocaleDateString()} &bull; {n.authorName}
                            </div>
                        </div>
                        <span className={`text-xs ${n.published ? 'text-success' : 'text-fg-muted'}`}>
                            {n.published ? t('admin.published') : 'Черновик'}
                        </span>
                        <button onClick={() => togglePublish(n)} className={btn}>{n.published ? t('admin.disable') : t('admin.enable')}</button>
                        <button onClick={() => openEdit(n)} className={btn}>{t('news.edit')}</button>
                        <button onClick={() => del(n.id)} className="rounded-md border border-border px-2 py-1 text-sm text-danger hover:border-danger">{t('admin.delete')}</button>
                    </div>
                ))}
            </div>

            {isModalOpen && (
                <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 backdrop-blur-sm">
                    <div className="w-full max-w-4xl rounded-lg border border-border bg-bg-elevated p-6 shadow-xl max-h-[90vh] overflow-y-auto">
                        <h3 className="mb-4 text-xl font-bold text-fg">{editingId ? t('news.edit') : t('admin.create')}</h3>

                        <form onSubmit={saveNews} className="space-y-4">
                            <div>
                                <label className="text-sm text-fg-muted">Заголовок</label>
                                <input className={`${input} w-full`} value={form.title} onChange={(e) => setForm({ ...form, title: e.target.value })} required />
                            </div>

                            <div>
                                <label className="text-sm text-fg-muted">URL обложки</label>
                                <div className="flex items-center gap-2">
                                    <input className={`${input} flex-1`} value={form.imageUrl} onChange={(e) => setForm({ ...form, imageUrl: e.target.value })} />
                                    <label className="cursor-pointer whitespace-nowrap rounded border border-border bg-bg-base px-3 py-1.5 text-sm text-fg hover:border-accent hover:text-accent">
                                        Загрузить файл
                                        <input type="file" accept="image/*" className="hidden" onChange={(e) => handleFileSelect(e, 'cover')} />
                                    </label>
                                </div>
                            </div>

                            <div className="flex items-center justify-between border-b border-border pb-2 mt-4">
                                <label className="flex items-center gap-2 text-sm text-fg">
                                    <input type="checkbox" checked={form.published} onChange={(e) => setForm({ ...form, published: e.target.checked })} />
                                    Опубликовать сразу
                                </label>
                                <div className="flex items-center gap-2">
                                    <label className="cursor-pointer rounded border border-accent bg-accent/10 px-2 py-1 text-xs text-accent hover:bg-accent/20">
                                        {t('news.insertImage')} (в текст)
                                        <input type="file" accept="image/*" className="hidden" onChange={(e) => handleFileSelect(e, 'inline')} />
                                    </label>
                                    <button type="button" onClick={() => setIsPreview(!isPreview)} className={btn}>
                                        {isPreview ? t('news.edit') : t('news.preview')}
                                    </button>
                                </div>
                            </div>

                            <div className="h-[400px] w-full rounded-md border border-border bg-bg p-2">
                                {isPreview ? (
                                    <div className="prose prose-sm prose-invert h-full max-w-none overflow-y-auto p-2 text-fg">
                                        <ReactMarkdown remarkPlugins={[remarkGfm]} rehypePlugins={[rehypeSanitize]}>
                                            {form.body || t('news.emptyBody')}
                                        </ReactMarkdown>
                                    </div>
                                ) : (
                                    <textarea
                                        className="h-full w-full resize-none bg-transparent text-sm text-fg outline-none font-mono"
                                        placeholder={t('news.markdownPlaceholder')}
                                        value={form.body}
                                        onChange={(e) => setForm({ ...form, body: e.target.value })}
                                        required
                                    />
                                )}
                            </div>

                            <div className="flex justify-end gap-2 pt-4">
                                <button type="button" onClick={() => setIsModalOpen(false)} className={btn}>{t('common.cancel')}</button>
                                <button type="submit" className="rounded-md bg-accent px-4 py-2 text-sm font-medium text-accent-fg hover:opacity-90">{t('common.save')}</button>
                            </div>
                        </form>
                    </div>
                </div>
            )}

            <ImageCropperModal
                isOpen={cropper.isOpen}
                imageSrc={cropper.src}
                aspectRatio={cropper.target === 'cover' ? 16 / 9 : 4 / 3}
                onClose={() => setCropper({ isOpen: false, src: '', target: 'cover' })}
                onCropComplete={executeUpload}
            />

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
            <Applications />
            <Users />
            <NewsManager />
        </div>
    );
}