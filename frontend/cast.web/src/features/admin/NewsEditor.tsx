import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import rehypeSanitize from 'rehype-sanitize';
import { api } from '@/lib/api';

export function NewsEditor() {
    const { t } = useTranslation();
    const [title, setTitle] = useState('');
    const [body, setBody] = useState('');
    const [coverUrl, setCoverUrl] = useState('');
    const [published, setPublished] = useState(false);
    const [isPreview, setIsPreview] = useState(false);

    const handleInlineImageUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
        const file = e.target.files?.[0];
        if (!file) return;

        const formData = new FormData();
        formData.append('file', file);

        try {
            const res = await api.post('/news/image', formData, {
                headers: { 'Content-Type': 'multipart/form-data' },
            });
            const imageUrl = res.data.url;
            setBody((prev) => `${prev}\n![${file.name}](${imageUrl})\n`);
        } catch (error) {
            console.error('Failed to upload image', error);
        }
    };

    const handleSave = async () => {
        try {
            await api.post('/news', { title, body, imageUrl: coverUrl, published });
            alert(t('admin.newsSaved'));
        } catch (error) {
            console.error(error);
        }
    };

    return (
        <div className="mx-auto max-w-5xl space-y-4 p-4">
            <h2 className="text-xl font-bold text-fg">{t('admin.createNews')}</h2>

            <input
                type="text"
                placeholder={t('news.titlePlaceholder')}
                value={title}
                onChange={(e) => setTitle(e.target.value)}
                className="w-full rounded border border-border bg-bg-base p-2 text-fg"
            />

            <input
                type="text"
                placeholder={t('news.coverUrlPlaceholder')}
                value={coverUrl}
                onChange={(e) => setCoverUrl(e.target.value)}
                className="w-full rounded border border-border bg-bg-base p-2 text-fg"
            />

            <div className="flex items-center justify-between">
                <label className="flex items-center space-x-2 text-fg">
                    <input
                        type="checkbox"
                        checked={published}
                        onChange={(e) => setPublished(e.target.checked)}
                    />
                    <span>{t('news.isPublished')}</span>
                </label>

                <div className="space-x-2">
                    <label className="cursor-pointer rounded bg-accent/20 px-3 py-1 text-sm text-accent hover:bg-accent/30">
                        {t('news.insertImage')}
                        <input type="file" accept="image/*" className="hidden" onChange={handleInlineImageUpload} />
                    </label>

                    <button
                        onClick={() => setIsPreview(!isPreview)}
                        className="rounded bg-border px-3 py-1 text-sm text-fg"
                    >
                        {isPreview ? t('news.edit') : t('news.preview')}
                    </button>
                </div>
            </div>

            <div className="h-96 w-full rounded border border-border bg-bg-base">
                {isPreview ? (
                    <div className="prose prose-invert h-full max-w-none overflow-y-auto p-4 text-fg">
                        <ReactMarkdown remarkPlugins={[remarkGfm]} rehypePlugins={[rehypeSanitize]}>
                            {body || t('news.emptyBody')}
                        </ReactMarkdown>
                    </div>
                ) : (
                    <textarea
                        value={body}
                        onChange={(e) => setBody(e.target.value)}
                        className="h-full w-full resize-none bg-transparent p-4 text-fg outline-none"
                        placeholder={t('news.markdownPlaceholder')}
                    />
                )}
            </div>

            <button
                onClick={handleSave}
                className="rounded bg-accent px-4 py-2 text-white hover:bg-accent/80"
            >
                {t('common.save')}
            </button>
        </div>
    );
}