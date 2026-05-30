import { useRef, useState, type FormEvent } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { api } from '@/lib/api';
import type { MediaItem, MediaType } from '@/lib/types';
import { TagInput } from '@/components/TagInput';

function MediaCard({ m }: { m: MediaItem }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const [editing, setEditing] = useState(false);
    const [form, setForm] = useState({
        clipStartMs: m.clipStartMs ?? 0,
        clipEndMs: m.clipEndMs ?? 0,
        posXPct: m.posXPct,
        posYPct: m.posYPct,
        scalePct: m.scalePct,
    });

  async function saveEdit() {
    await api.put(`/media/${m.id}/edit`, form);
    setEditing(false);
    await qc.invalidateQueries({ queryKey: ['media'] });
  }

  const num = 'w-20 rounded-md border border-border bg-bg px-2 py-1 text-sm text-fg outline-none focus:border-accent';

  return (
    <div className="rounded-lg border border-border bg-bg-elevated p-3">
      <div className="flex items-center justify-between">
        <Link to={`/media/${m.id}`} className="font-medium text-fg hover:text-accent hover:underline">{m.title}</Link>
        <span className="rounded bg-bg-accent px-2 py-0.5 text-xs text-accent">{m.costCoins}</span>
      </div>
      <div className="mt-1 flex flex-wrap items-center gap-2 text-xs text-fg-muted">
        <span>{t(`media.${m.type === 'Sound' ? 'sound' : 'video'}`)}</span>
        <span className="rounded bg-bg-accent px-1.5 py-0.5">{t(`media.${m.status}`)}</span>
        {m.tags.map((tag) => <span key={tag} className="rounded bg-bg-accent px-1.5 py-0.5">#{tag}</span>)}
      </div>

      {m.type === 'Sound' && !m.processed && m.status !== 'Rejected' && (
        <p className="mt-2 text-xs text-fg-muted">{t('media.processing')}</p>
      )}
      {m.type === 'Sound' && m.processed && (m.webmUrl || m.oggUrl) && (
        <audio controls className="mt-2 w-full">
          {m.webmUrl && <source src={m.webmUrl} type="audio/webm" />}
          {m.oggUrl && <source src={m.oggUrl} type="audio/ogg" />}
        </audio>
      )}
      {m.type === 'Video' && (
        <video controls className="mt-2 w-full rounded-md"><source src={m.originalUrl} /></video>
      )}

      <button onClick={() => setEditing((v) => !v)} className="mt-2 text-xs text-accent hover:underline">{t('media.edit')}</button>
      {editing && (
        <div className="mt-2 space-y-2 rounded-md border border-border p-2">
          <div className="flex flex-wrap items-center gap-2 text-xs text-fg-muted">
            <label>{t('media.clipStart')}<input className={num} type="number" min={0} value={form.clipStartMs}
              onChange={(e) => setForm((f) => ({ ...f, clipStartMs: +e.target.value }))} /></label>
            <label>{t('media.clipEnd')}<input className={num} type="number" min={0} value={form.clipEndMs}
              onChange={(e) => setForm((f) => ({ ...f, clipEndMs: +e.target.value }))} /></label>
          </div>
          <div className="flex flex-wrap items-center gap-2 text-xs text-fg-muted">
            <span>{t('media.position')}</span>
            <input className={num} type="number" min={0} max={100} value={form.posXPct} onChange={(e) => setForm((f) => ({ ...f, posXPct: +e.target.value }))} />
            <input className={num} type="number" min={0} max={100} value={form.posYPct} onChange={(e) => setForm((f) => ({ ...f, posYPct: +e.target.value }))} />
            <input className={num} type="number" min={10} max={400} value={form.scalePct} onChange={(e) => setForm((f) => ({ ...f, scalePct: +e.target.value }))} />
          </div>
          <button onClick={saveEdit} className="rounded-md bg-accent px-3 py-1.5 text-sm font-medium text-accent-fg hover:opacity-90">{t('common.save')}</button>
        </div>
      )}
    </div>
  );
}

export function MediaPage() {
    const { t } = useTranslation();
    const qc = useQueryClient();
    const fileRef = useRef<HTMLInputElement>(null);
    const [title, setTitle] = useState('');
    const [type, setType] = useState<MediaType>('Sound');
    const [tags, setTags] = useState<string[]>([]);
    const [clip, setClip] = useState({ start: 0, end: 0, x: 50, y: 50, scale: 100 });
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const { data, isLoading } = useQuery({
        queryKey: ['media'],
        queryFn: async () => (await api.get<MediaItem[]>('/media')).data,
        refetchInterval: 10000,
    });

    async function upload(e: FormEvent) {
        e.preventDefault();
        const file = fileRef.current?.files?.[0];
        if (!file || !title) return;
        setBusy(true); setError(null);
        try {
            const form = new FormData();
            form.append('file', file);
            form.append('title', title);
            form.append('type', type);
            tags.forEach((tg) => form.append('tags', tg));
            form.append('clipStartMs', String(clip.start));
            form.append('clipEndMs', String(clip.end));
            form.append('posXPct', String(clip.x));
            form.append('posYPct', String(clip.y));
            form.append('scalePct', String(clip.scale));
            await api.post('/media', form);
            setTitle('');
            setTags([]);
            setClip({ start: 0, end: 0, x: 50, y: 50, scale: 100 });
            if (fileRef.current) fileRef.current.value = '';
            await qc.invalidateQueries({ queryKey: ['media'] });
        } catch {
            setError(t('common.error'));
        } finally { setBusy(false); }
    }

    const input = 'rounded-md border border-border bg-bg px-3 py-2 text-fg outline-none focus:border-accent';

    return (
        <div className="mx-auto max-w-3xl space-y-6">
            <h1 className="text-2xl font-bold text-fg">{t('media.title')}</h1>

            <form onSubmit={upload} className="space-y-2 rounded-lg border border-border bg-bg-elevated p-4">
                <div className="flex flex-wrap gap-2">
                    <input className={input} placeholder={t('media.fileTitle')} value={title} onChange={(e) => setTitle(e.target.value)} required />
                    <select className={input} value={type} onChange={(e) => setType(e.target.value as MediaType)}>
                        <option value="Sound">{t('media.sound')}</option>
                        <option value="Video">{t('media.video')}</option>
                    </select>
                    <input ref={fileRef} className={input} type="file" accept="audio/*,video/*" required />
                    <button type="submit" disabled={busy}
                        className="rounded-md bg-accent px-4 py-2 font-medium text-accent-fg hover:opacity-90 disabled:opacity-50">
                        {t('media.upload')}
                    </button>
                </div>
                <div>
                    <label className="mb-1 block text-xs text-fg-muted">{t('media.tags')}</label>
                    <TagInput value={tags} onChange={setTags} />
                </div>
                <div className="space-y-2 rounded-md border border-border p-2">
                    <span className="text-xs text-fg-muted">{t('media.clip')} / {t('media.position')}</span>
                    <div className="flex flex-wrap items-center gap-2 text-xs text-fg-muted">
                        <label>{t('media.clipStart')}<input className="w-20 rounded-md border border-border bg-bg px-2 py-1 text-sm text-fg outline-none focus:border-accent" type="number" min={0} value={clip.start}
                            onChange={(e) => setClip((c) => ({ ...c, start: +e.target.value }))} /></label>
                        <label>{t('media.clipEnd')}<input className="w-20 rounded-md border border-border bg-bg px-2 py-1 text-sm text-fg outline-none focus:border-accent" type="number" min={0} value={clip.end}
                            onChange={(e) => setClip((c) => ({ ...c, end: +e.target.value }))} /></label>
                        <input className="w-20 rounded-md border border-border bg-bg px-2 py-1 text-sm text-fg outline-none focus:border-accent" type="number" min={0} max={100} value={clip.x} onChange={(e) => setClip((c) => ({ ...c, x: +e.target.value }))} />
                        <input className="w-20 rounded-md border border-border bg-bg px-2 py-1 text-sm text-fg outline-none focus:border-accent" type="number" min={0} max={100} value={clip.y} onChange={(e) => setClip((c) => ({ ...c, y: +e.target.value }))} />
                        <input className="w-20 rounded-md border border-border bg-bg px-2 py-1 text-sm text-fg outline-none focus:border-accent" type="number" min={10} max={400} value={clip.scale} onChange={(e) => setClip((c) => ({ ...c, scale: +e.target.value }))} />
                    </div>
                </div>
                <p className="text-xs text-fg-muted">{t('media.uploadHint')}</p>
                {error && <p className="text-sm text-danger">{error}</p>}
            </form>

            {isLoading ? <p className="text-fg-muted">{t('common.loading')}</p> : (
                <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                    {(data ?? []).map((m) => <MediaCard key={m.id} m={m} />)}
                    {data && data.length === 0 && <p className="text-fg-muted">{t('common.empty')}</p>}
                </div>
            )}
        </div>
    );
}