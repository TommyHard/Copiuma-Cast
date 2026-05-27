import { useEffect, useState } from 'react';
import { Navigate } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { api } from '@/lib/api';
import { useAuthStore } from '@/store/auth';
import type { Analytics, FilterMode, TagFilters } from '@/lib/types';

const box = 'rounded-lg border border-border bg-bg-elevated p-4';
const input = 'rounded-md border border-border bg-bg px-2 py-1 text-sm text-fg outline-none focus:border-accent';

function Filters() {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const { data } = useQuery({ queryKey: ['filters'], queryFn: async () => (await api.get<TagFilters>('/streamer/filters')).data });
  const [mode, setMode] = useState<FilterMode>('Blocklist');
  const [tags, setTags] = useState('');
  const [saved, setSaved] = useState(false);

  useEffect(() => { if (data) { setMode(data.mode); setTags(data.tags.join(', ')); } }, [data]);

  async function save() {
    const list = tags.split(',').map((s) => s.trim()).filter(Boolean);
    await api.put('/streamer/filters', { mode, tags: list });
    setSaved(true);
    qc.invalidateQueries({ queryKey: ['filters'] });
  }

  return (
    <section className={box}>
      <h2 className="mb-2 font-semibold text-fg">{t('streamer.filters')}</h2>
      <div className="flex flex-wrap items-center gap-2">
        <label className="text-sm text-fg-muted">{t('streamer.mode')}</label>
        <select className={input} value={mode} onChange={(e) => { setMode(e.target.value as FilterMode); setSaved(false); }}>
          <option value="Blocklist">{t('streamer.Blocklist')}</option>
          <option value="Allowlist">{t('streamer.Allowlist')}</option>
        </select>
      </div>
      <input className={`${input} mt-2 w-full`} placeholder={t('streamer.tags')} value={tags}
        onChange={(e) => { setTags(e.target.value); setSaved(false); }} />
      <button onClick={save} className="mt-2 rounded-md bg-accent px-4 py-2 font-medium text-accent-fg hover:opacity-90">{t('common.save')}</button>
      {saved && <span className="ml-2 text-sm text-success">{t('profile.saved')}</span>}
    </section>
  );
}

function AnalyticsView() {
    const { t } = useTranslation();
    const { data } = useQuery({ queryKey: ['analytics'], queryFn: async () => (await api.get<Analytics>('/streamer/analytics')).data });
    if (!data) return null;

    return (
        <section className={box}>
            <h2 className="mb-2 font-semibold text-fg">{t('streamer.analytics')}</h2>
            <div className="mb-3 flex gap-6 text-sm">
                <div><span className="text-fg-muted">{t('streamer.turnoverSpent')}: </span><span className="text-fg">{data.turnoverSpent}</span></div>
                <div><span className="text-fg-muted">{t('streamer.turnoverCredited')}: </span><span className="text-fg">{data.turnoverCredited}</span></div>
            </div>
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
                <div>
                    <h3 className="mb-1 text-sm font-semibold text-fg">{t('streamer.topViewers')}</h3>
                    {data.topViewers.map((v) => <div key={v.userId} className="flex justify-between text-sm"><span className="truncate text-fg-muted">@{v.handle}</span><span className="text-fg">{v.spent}</span></div>)}
                </div>
                <div>
                    <h3 className="mb-1 text-sm font-semibold text-fg">{t('streamer.popularEvents')}</h3>
                    {data.popularEvents.map((e) => <div key={e.eventId} className="flex justify-between text-sm"><span className="truncate text-fg-muted">{e.eventId}</span><span className="text-fg">{e.count}</span></div>)}
                </div>
                <div>
                    <h3 className="mb-1 text-sm font-semibold text-fg">{t('streamer.popularMedia')}</h3>
                    {data.popularMedia.map((m) => <div key={m.mediaId} className="flex justify-between text-sm"><span className="truncate text-fg-muted">{m.title}</span><span className="text-fg">{m.count}</span></div>)}
                </div>
            </div>
        </section>
    );
}

export function StreamerPage() {
    const { t } = useTranslation();
    const isStreamer = useAuthStore((s) => s.hasRole('Streamer'));
    if (!isStreamer) return <Navigate to="/" replace />;
    return (
        <div className="mx-auto max-w-3xl space-y-5">
            <h1 className="text-2xl font-bold text-fg">{t('streamer.title')}</h1>
            <Filters />
            <AnalyticsView />
        </div>
    );
}