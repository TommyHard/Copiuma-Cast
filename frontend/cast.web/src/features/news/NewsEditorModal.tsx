import { useState, type ChangeEvent, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import rehypeSanitize from 'rehype-sanitize';
import { api } from '@/lib/api';
import type { NewsPost } from '@/lib/types';
import { ImageCropperModal } from '@/components/ImageCropperModal';

interface Props {
    isOpen: boolean;
    onClose: () => void;
    post?: NewsPost | null;
    onSaved: () => void;
}

const input = 'rounded-md border border-border bg-bg px-2 py-1 text-sm text-fg outline-none focus:border-accent';
const btn = 'rounded-md border border-border px-2 py-1 text-sm text-fg hover:border-accent';

/** Создание/редактирование новости. Используется на главной и в карточке новости */
export function NewsEditorModal({ isOpen, onClose, post, onSaved }: Props) {
    const { t } = useTranslation();
    const [form, setForm] = useState({
        title: post?.title ?? '',
        body: post?.body ?? '',
        imageUrl: post?.imageUrl ?? '',
        published: post?.published ?? true,
    });
    const [isPreview, setIsPreview] = useState(false);
    const [cropper, setCropper] = useState<{ isOpen: boolean; src: string; target: 'cover' | 'inline' }>({ isOpen: false, src: '', target: 'cover' });

    const handleFileSelect = (e: ChangeEvent<HTMLInputElement>, target: 'cover' | 'inline') => {
        const file = e.target.files?.[0];
        if (!file) return;
        const reader = new FileReader();
        reader.onload = () => setCropper({ isOpen: true, src: reader.result as string, target });
        reader.readAsDataURL(file);
        e.target.value = '';
    };

    const executeUpload = async (blob: Blob) => {
        const fd = new FormData();
        fd.append('file', blob, 'cropped.jpg');
        try {
            const res = await api.post('/news/image', fd, { headers: { 'Content-Type': 'multipart/form-data' } });
            if (cropper.target === 'cover') setForm((p) => ({ ...p, imageUrl: res.data.url }));
            else setForm((p) => ({ ...p, body: `${p.body}\n![image](${res.data.url})\n` }));
        } finally {
            setCropper({ isOpen: false, src: '', target: 'cover' });
        }
    };

    const save = async (e: FormEvent) => {
        e.preventDefault();
        const payload = { title: form.title, body: form.body, imageUrl: form.imageUrl || null, published: form.published };
        if (post) await api.put(`/news/${post.id}`, payload);
        else await api.post('/news', payload);
        onSaved();
        onClose();
    };

    if (!isOpen) return null;

    return (
        <>
            <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 backdrop-blur-sm">
                <div className="max-h-[90vh] w-full max-w-4xl overflow-y-auto rounded-lg border border-border bg-bg-elevated p-6 shadow-xl">
                    <h3 className="mb-4 text-xl font-bold text-fg">{post ? t('news.edit') : t('admin.create')}</h3>

                    <form onSubmit={save} className="space-y-4">
                        <div>
                            <label className="text-sm text-fg-muted">{t('news.titlePlaceholder')}</label>
                            <input className={`${input} w-full`} value={form.title} onChange={(e) => setForm({ ...form, title: e.target.value })} required />
                        </div>

                        <div>
                            <label className="text-sm text-fg-muted">{t('news.coverUrlPlaceholder')}</label>
                            <div className="flex items-center gap-2">
                                <input className={`${input} flex-1`} value={form.imageUrl} onChange={(e) => setForm({ ...form, imageUrl: e.target.value })} />
                                <label className="cursor-pointer whitespace-nowrap rounded border border-border bg-bg-base px-3 py-1.5 text-sm text-fg hover:border-accent hover:text-accent">
                                    {t('news.insertImage')}
                                    <input type="file" accept="image/*" className="hidden" onChange={(e) => handleFileSelect(e, 'cover')} />
                                </label>
                            </div>
                        </div>

                        <div className="mt-4 flex items-center justify-between border-b border-border pb-2">
                            <label className="flex items-center gap-2 text-sm text-fg">
                                <input type="checkbox" checked={form.published} onChange={(e) => setForm({ ...form, published: e.target.checked })} />
                                {t('news.isPublished')}
                            </label>
                            <div className="flex items-center gap-2">
                                <label className="cursor-pointer rounded border border-accent bg-accent/10 px-2 py-1 text-xs text-accent hover:bg-accent/20">
                                    {t('news.insertImage')}
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
                                    className="h-full w-full resize-none bg-transparent font-mono text-sm text-fg outline-none"
                                    placeholder={t('news.markdownPlaceholder')}
                                    value={form.body}
                                    onChange={(e) => setForm({ ...form, body: e.target.value })}
                                    required
                                />
                            )}
                        </div>

                        <div className="flex justify-end gap-2 pt-4">
                            <button type="button" onClick={onClose} className={btn}>{t('common.cancel')}</button>
                            <button type="submit" className="rounded-md bg-accent px-4 py-2 text-sm font-medium text-accent-fg hover:opacity-90">{t('common.save')}</button>
                        </div>
                    </form>
                </div>
            </div>

            <ImageCropperModal
                isOpen={cropper.isOpen}
                imageSrc={cropper.src}
                aspectRatio={cropper.target === 'cover' ? 16 / 9 : 4 / 3}
                onClose={() => setCropper({ isOpen: false, src: '', target: 'cover' })}
                onCropComplete={executeUpload}
            />
        </>
    );
}