import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { api } from '@/lib/api';
import { useAuthStore } from '@/store/auth';
import type { MyApplication } from '@/lib/types';

export function StreamerApplicationSection() {
    const { t } = useTranslation();
    const qc = useQueryClient();
    const isStreamer = useAuthStore((s) => s.hasRole('Streamer'));
    const isAdmin = useAuthStore((s) => s.hasRole('Admin'));
    const [message, setMessage] = useState('');

    const { data, isLoading } = useQuery({
        queryKey: ['my-application'],
        queryFn: async () => {
            try {
                const res = await api.get<MyApplication>('/streamer/application');
                return res.data || null; // 204 -> пустое тело -> null
            } catch { return null; }
        },
        enabled: !isStreamer && !isAdmin,
    });

    async function apply() {
        await api.post('/streamer/application', { message });
        setMessage('');
        await qc.invalidateQueries({ queryKey: ['my-application'] });
    }

    const box = 'rounded-lg border border-border bg-bg-elevated p-4';

    if (isStreamer) return <section className={box}><h2 className="font-semibold text-fg">{t('streamerApp.alreadyStreamer')}</h2></section>;
    if (isLoading) return null;

    if (data?.status === 'Pending') return <section className={box}><p className="text-sm text-fg-muted">{t('streamerApp.pending')}</p></section>;
    if (data?.status === 'Approved') return <section className={box}><p className="text-sm text-success">{t('streamerApp.approved')}</p></section>;

    return (
        <section className={box}>
            <h2 className="mb-2 font-semibold text-fg">{t('streamerApp.title')}</h2>
            {data?.status === 'Rejected' && <p className="mb-2 text-sm text-fg-muted">{t('streamerApp.rejected')}</p>}
            <textarea className="w-full rounded-md border border-border bg-bg px-3 py-2 text-fg outline-none focus:border-accent"
                rows={3} placeholder={t('streamerApp.message')} value={message} onChange={(e) => setMessage(e.target.value)} />
            <button onClick={apply} className="mt-2 rounded-md bg-accent px-4 py-2 font-medium text-accent-fg hover:opacity-90">
                {t('streamerApp.apply')}
            </button>
        </section>
    );
}