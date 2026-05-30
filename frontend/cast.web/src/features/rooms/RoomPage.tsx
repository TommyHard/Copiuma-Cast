import { useEffect, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router-dom';
import { api } from '@/lib/api';
import type { MediaItem, RoomEvent } from '@/lib/types';
import { useRoomStore } from '@/store/room';
import { BetsPanel } from './BetsPanel';

export function RoomPage() {
    const { t } = useTranslation();
    const qc = useQueryClient();
    const [searchParams, setSearchParams] = useSearchParams();
    const { room, balance, log, busy, join, leave, trigger, setBalance, setHandlers } = useRoomStore();
    const [code, setCode] = useState('');
    const [error, setError] = useState<string | null>(null);

    const events = useQuery({
        queryKey: ['room-events', room?.id],
        queryFn: async () => (await api.get<RoomEvent[]>(`/rooms/${room!.id}/events`)).data,
        enabled: !!room,
    });
    const media = useQuery({
        queryKey: ['media-approved'],
        queryFn: async () => (await api.get<MediaItem[]>('/media')).data.filter((m) => m.status === 'Approved'),
        enabled: !!room,
    });

    const [mediaId, setMediaId] = useState('');

    // Колбэки активной страницы для событий соединения
    useEffect(() => {
        setHandlers({
            onBetUpdated: () => { if (room) void qc.invalidateQueries({ queryKey: ['bets', room.id] }); },
            onKicked: () => setError(t('room.kicked')),
        });
    }, [room, qc, setHandlers, t]);

    // Автовход по ссылке-приглашению (?code=...)
    useEffect(() => {
        const invite = searchParams.get('code');
        if (invite && !room && !busy) {
            setCode(invite);
            void doJoin(invite);
            searchParams.delete('code');
            setSearchParams(searchParams, { replace: true });
        }
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);

    async function doJoin(c: string) {
        setError(null);
        try {
            await join(c);
        } catch {
            setError(t('common.error'));
        }
    }

    async function onTrigger(eventId: string) {
        setError(null);
        try {
            await trigger(eventId, mediaId || null);
        } catch (err: any) {
            setError(String(err?.message || '').includes('средств') ? t('room.insufficient') : t('common.error'));
        }
    }

    async function toggleEvent(eventId: string, enabled: boolean) {
        if (!room) return;
        await api.put(`/rooms/${room.id}/events/${eventId}`, { enabled });
        void events.refetch();
    }

    const input = 'rounded-md border border-border bg-bg px-3 py-2 text-fg outline-none focus:border-accent';

    if (!room) {
        return (
            <div className="mx-auto max-w-sm space-y-3 pt-10 text-center">
                <h1 className="text-2xl font-bold text-fg">{t('room.title')}</h1>
                <p className="text-sm text-fg-muted">{t('room.joinHint')}</p>
                <input className={`${input} w-full text-center uppercase`} placeholder={t('room.code')} value={code} onChange={(e) => setCode(e.target.value)} />
                <button onClick={() => doJoin(code)} disabled={busy || !code} className="w-full rounded-md bg-accent px-4 py-2 font-medium text-accent-fg hover:opacity-90 disabled:opacity-50">{t('room.join')}</button>
                {error && <p className="text-sm text-danger">{error}</p>}
            </div>
        );
    }

    return (
        <div className="mx-auto grid max-w-5xl gap-6 lg:grid-cols-[1fr_360px]">
            <div className="space-y-4">
                <header className="flex items-center gap-3">
                    <h1 className="text-xl font-bold text-fg">{room.title || room.code}</h1>
                    <span className="rounded bg-bg-accent px-2 py-0.5 text-xs text-accent">{room.code}</span>
                    {balance !== null && <span className="text-sm text-fg-muted">{t('room.balance')}: {balance}</span>}
                    <button onClick={() => void leave()} className="ml-auto rounded-md border border-border px-2 py-1 text-sm text-fg hover:border-danger">{t('room.leave')}</button>
                </header>

                <div className="flex items-center gap-2">
                    <label className="text-sm text-fg-muted">{t('room.attachMedia')}</label>
                    <select className={input} value={mediaId} onChange={(e) => setMediaId(e.target.value)}>
                        <option value="">{t('room.none')}</option>
                        {(media.data ?? []).map((m) => <option key={m.id} value={m.id}>{m.title}</option>)}
                    </select>
                </div>

                <section>
                    <h2 className="mb-2 text-lg font-semibold text-fg">{t('room.events')}</h2>
                    <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
                        {(events.data ?? []).map((e) => (
                            <div key={e.eventId} className="rounded-lg border border-border bg-bg-elevated p-3">
                                <button disabled={!e.enabled} onClick={() => onTrigger(e.eventId)}
                                    className="w-full text-left hover:opacity-90 disabled:opacity-40">
                                    <div className="font-medium text-fg">{e.title}</div>
                                    <div className="text-xs text-accent">{e.costCoins}</div>
                                    {!e.enabled && <div className="text-xs text-fg-muted">{t('room.disabled')}</div>}
                                </button>
                                {room.role === 'Streamer' && (
                                    <button onClick={() => toggleEvent(e.eventId, !e.enabled)}
                                        className="mt-2 w-full rounded-md border border-border px-2 py-1 text-xs text-fg hover:border-accent">
                                        {e.enabled ? t('streamer.disableEvent') : t('streamer.enableEvent')}
                                    </button>
                                )}
                            </div>
                        ))}
                    </div>
                    {error && <p className="mt-2 text-sm text-danger">{error}</p>}
                </section>

                <section>
                    <h2 className="mb-2 text-lg font-semibold text-fg">{t('room.log')}</h2>
                    <div className="max-h-48 space-y-1 overflow-auto rounded-lg border border-border bg-bg-elevated p-2 text-sm text-fg-muted">
                        {log.length === 0 ? <p>{t('common.empty')}</p> : log.map((l, i) => <div key={i}>{t('room.triggered', { user: l.user, event: l.event })}</div>)}
                    </div>
                </section>
            </div>

            <BetsPanel roomId={room.id} isStreamer={room.role === 'Streamer'} onBalance={setBalance} />
        </div>
    );
}