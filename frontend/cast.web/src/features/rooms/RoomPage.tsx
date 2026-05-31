import { useEffect, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router-dom';
import { api } from '@/lib/api';
import type { GameCard, MediaItem, RoomEvent, RoomInfo } from '@/lib/types';
import { useRoomStore, savedRoomCode } from '@/store/room';
import { BetsPanel } from './BetsPanel';
import { basicHtml } from '@/lib/html';

// Избранные события хранятся локально (на пользователя/браузер)
const FAV_KEY = 'cast.room.favEvents';
const loadFavs = (): Set<string> => {
    try { return new Set(JSON.parse(localStorage.getItem(FAV_KEY) || '[]')); } catch { return new Set(); }
};

// Группировка событий по категории с сохранением порядка появления
function groupByCategory<T extends { category: string | null }>(items: T[]): [string, T[]][] {
    const order: string[] = [];
    const map = new Map<string, T[]>();
    for (const it of items) {
        const key = it.category ?? '';
        if (!map.has(key)) { map.set(key, []); order.push(key); }
        map.get(key)!.push(it);
    }
    return order.map((k) => [k, map.get(k)!]);
}

export function RoomPage() {
    const { t } = useTranslation();
    const qc = useQueryClient();
    const [searchParams, setSearchParams] = useSearchParams();
    const { room, balance, log, roster, busy, join, leave, trigger, sendMedia, setBalance, setHandlers } = useRoomStore();
    const [code, setCode] = useState('');
    const [error, setError] = useState<string | null>(null);

    // Каталог игр — чтобы показать название текущей игры комнаты по её slug (gameId)
    const games = useQuery({
        queryKey: ['games-catalog'],
        queryFn: async () => (await api.get<GameCard[]>('/games')).data,
        enabled: !!room?.gameId,
    });
    const gameTitle = room?.gameId
        ? games.data?.find((g) => g.slug === room.gameId)?.title ?? room.gameId
        : null;

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

    // Комнаты, куда меня пригласили (или где я уже участник) — чтобы войти без кода
    const invites = useQuery({
        queryKey: ['my-rooms'],
        queryFn: async () => (await api.get<RoomInfo[]>('/rooms/mine')).data,
        enabled: !room,
    });

    // Перезарядка событий: eventId -> момент (ms), когда снова можно жать.
    // Тикаем раз в полсекунды, пока есть активные перезарядки, чтобы обновлять счётчик
    const [cooldowns, setCooldowns] = useState<Record<string, number>>({});
    const [, setTick] = useState(0);
    useEffect(() => {
        if (!Object.values(cooldowns).some((ts) => ts > Date.now())) return;
        const id = window.setInterval(() => setTick((n) => n + 1), 500);
        return () => window.clearInterval(id);
    }, [cooldowns]);
    const cooldownLeft = (eventId: string) =>
        Math.max(0, Math.ceil(((cooldowns[eventId] ?? 0) - Date.now()) / 1000));

    // Избранные события (звёздочка): хранятся в localStorage, показываются первыми
    const [favorites, setFavorites] = useState<Set<string>>(() => loadFavs());
    const toggleFavorite = (eventId: string) => {
        setFavorites((prev) => {
            const next = new Set(prev);
            next.has(eventId) ? next.delete(eventId) : next.add(eventId);
            try { localStorage.setItem(FAV_KEY, JSON.stringify([...next])); } catch { /* ignore */ }
            return next;
        });
    };

    // Колбэки активной страницы для событий соединения
    useEffect(() => {
        setHandlers({
            onBetUpdated: () => { if (room) void qc.invalidateQueries({ queryKey: ['bets', room.id] }); },
            onKicked: () => setError(t('room.kicked')),
        });
    }, [room, qc, setHandlers, t]);

    // Автовход по ссылке-приглашению (?code=...) или восстановление сессии после
    // перезагрузки страницы (код сохранён в localStorage)
    useEffect(() => {
        const invite = searchParams.get('code');
        if (invite && !room && !busy) {
            setCode(invite);
            void doJoin(invite);
            searchParams.delete('code');
            setSearchParams(searchParams, { replace: true });
            return;
        }
        const saved = savedRoomCode();
        if (saved && !room && !busy) {
            setCode(saved);
            void doJoin(saved);
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

    async function declineInvite(roomId: string) {
        try {
            await api.post(`/rooms/${roomId}/decline`);
            await invites.refetch();
        } catch { /* ignore */ }
    }

    async function onTrigger(eventId: string) {
        setError(null);
        const ev = events.data?.find((e) => e.eventId === eventId);
        try {
            await trigger(eventId);
            // Локальная перезарядка: блокируем кнопку на cooldownMs события
            if (ev?.cooldownMs) setCooldowns((c) => ({ ...c, [eventId]: Date.now() + ev.cooldownMs }));
        } catch (err: any) {
            // SignalR прокидывает текст HubException как message — он уже
            // локализован сервером (перезарядка, нет средств)
            const msg = String(err?.message || '').trim();
            setError(msg || t('common.error'));
        }
    }

    async function onSendMedia(id: string) {
        setError(null);
        try {
            await sendMedia(id);
        } catch (err: any) {
            // Серверный текст: не одобрено / ещё обрабатывается / фильтры / нет средств
            const msg = String(err?.message || '').trim();
            setError(msg || t('common.error'));
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

                {/* Свои комнаты (стример) — быстрый вход */}
                {(invites.data?.some((r) => r.role === 'Streamer')) && (
                    <div className="mt-6 space-y-2 text-left">
                        <h2 className="text-sm font-semibold text-fg">{t('room.yourRooms')}</h2>
                        {invites.data!.filter((r) => r.role === 'Streamer').map((r) => (
                            <div key={r.id} className="flex items-center gap-2 rounded-md border border-border bg-bg-elevated px-3 py-2 text-sm">
                                <span className="flex-1 truncate text-fg">{r.title || r.code}</span>
                                <button onClick={() => doJoin(r.code)} disabled={busy}
                                    className="rounded-md bg-accent px-3 py-1 text-xs font-medium text-accent-fg hover:opacity-90 disabled:opacity-50">
                                    {t('room.enter')}
                                </button>
                            </div>
                        ))}
                    </div>
                )}

                {/* Приглашения от других стримеров */}
                {(invites.data?.some((r) => r.role !== 'Streamer')) && (
                    <div className="mt-6 space-y-2 text-left">
                        <h2 className="text-sm font-semibold text-fg">{t('room.invites')}</h2>
                        {invites.data!.filter((r) => r.role !== 'Streamer').map((r) => (
                            <div key={r.id} className="flex items-center gap-2 rounded-md border border-border bg-bg-elevated px-3 py-2 text-sm">
                                <span className="flex-1 truncate text-fg">{r.title || r.code}</span>
                                <button onClick={() => doJoin(r.code)} disabled={busy}
                                    className="rounded-md bg-accent px-3 py-1 text-xs font-medium text-accent-fg hover:opacity-90 disabled:opacity-50">
                                    {t('room.accept')}
                                </button>
                                <button onClick={() => declineInvite(r.id)} disabled={busy}
                                    className="rounded-md border border-border px-3 py-1 text-xs text-fg hover:border-danger disabled:opacity-50">
                                    {t('room.decline')}
                                </button>
                            </div>
                        ))}
                    </div>
                )}
            </div>
        );
    }

    return (
        <div className="mx-auto grid max-w-5xl gap-6 lg:grid-cols-[1fr_360px]">
            <div className="space-y-4">
                <header className="flex flex-wrap items-center gap-3">
                    <h1 className="text-xl font-bold text-fg">{room.title || room.code}</h1>
                    <span className="rounded bg-bg-accent px-2 py-0.5 text-xs text-accent">{room.code}</span>
                    {gameTitle && <span className="text-sm text-fg-muted">{t('room.game')}: {gameTitle}</span>}
                    <span className="text-sm text-fg-muted">{t('room.balance')}: {room.role === 'Streamer' ? '∞' : (balance ?? 0)}</span>
                    <button onClick={() => void leave()} className="ml-auto rounded-md border border-border px-2 py-1 text-sm text-fg hover:border-danger">{t('room.leave')}</button>
                </header>

                <section>
                    <h2 className="mb-2 text-lg font-semibold text-fg">{t('room.media')}</h2>
                    {(media.data ?? []).length === 0 ? (
                        <p className="text-sm text-fg-muted">{t('room.noMedia')}</p>
                    ) : (
                        <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
                            {(media.data ?? []).map((m) => (
                                <div key={m.id} className="flex flex-col rounded-lg border border-border bg-bg-elevated p-3">
                                    <div className="truncate font-medium text-fg" title={m.title}>{m.title}</div>
                                    <div className="text-xs text-fg-muted">{m.type}{typeof m.costCoins === 'number' ? ` · ${m.costCoins}` : ''}</div>
                                    <button
                                        onClick={() => onSendMedia(m.id)}
                                        className="mt-2 rounded-md bg-accent px-2 py-1 text-xs font-medium text-accent-fg hover:opacity-90"
                                    >
                                        {t('room.sendMedia')}
                                    </button>
                                </div>
                            ))}
                        </div>
                    )}
                </section>

                <section>
                    <h2 className="mb-2 text-lg font-semibold text-fg">{t('room.events')}</h2>
                    <div className="space-y-4">
                        {groupByCategory(events.data ?? []).map(([category, evs]) => (
                            <div key={category || '__none'}>
                                <h3 className="mb-1 text-xs font-semibold uppercase tracking-wide text-fg-muted">
                                    {category || t('room.uncategorized')}
                                </h3>
                                <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
                                    {[...evs].sort((a, b) => (favorites.has(b.eventId) ? 1 : 0) - (favorites.has(a.eventId) ? 1 : 0)).map((e) => {
                                        const cd = cooldownLeft(e.eventId);
                                        const fav = favorites.has(e.eventId);
                                        return (
                                            <div key={e.eventId} className={`relative rounded-lg border bg-bg-elevated p-3 ${fav ? 'border-accent' : 'border-border'}`}>
                                                <button
                                                    onClick={() => toggleFavorite(e.eventId)}
                                                    title={t('room.favorite')}
                                                    className="absolute right-1 top-1 text-sm"
                                                    style={{ color: fav ? 'rgb(155 223 30)' : 'rgba(255,255,255,0.35)' }}
                                                >
                                                    {fav ? '★' : '☆'}
                                                </button>
                                                <button disabled={!e.enabled || cd > 0} onClick={() => onTrigger(e.eventId)}
                                                    className="w-full text-left hover:opacity-90 disabled:opacity-40">
                                                    <div className="pr-5 font-medium text-fg">{e.title}</div>
                                                    <div className="text-xs text-accent">{e.costCoins}</div>
                                                    {e.description && <div className="mt-1 text-xs text-fg-muted" dangerouslySetInnerHTML={{ __html: basicHtml(e.description) }} />}
                                                    {!e.enabled && <div className="text-xs text-fg-muted">{t('room.disabled')}</div>}
                                                    {e.enabled && cd > 0 && <div className="text-xs text-fg-muted">{t('room.cooldown', { sec: cd })}</div>}
                                                </button>
                                                {room.role === 'Streamer' && (
                                                    <button onClick={() => toggleEvent(e.eventId, !e.enabled)}
                                                        className="mt-2 w-full rounded-md border border-border px-2 py-1 text-xs text-fg hover:border-accent">
                                                        {e.enabled ? t('streamer.disableEvent') : t('streamer.enableEvent')}
                                                    </button>
                                                )}
                                            </div>
                                        );
                                    })}
                                </div>
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

            <div className="space-y-4">
                <section>
                    <h2 className="mb-2 text-lg font-semibold text-fg">
                        {t('room.members')} <span className="text-sm text-fg-muted">({roster.online})</span>
                    </h2>
                    <div className="space-y-1 rounded-lg border border-border bg-bg-elevated p-2">
                        {roster.members.length === 0
                            ? <p className="text-sm text-fg-muted">{t('common.empty')}</p>
                            : roster.members.map((m) => (
                                <div key={m.userId} className="flex items-center justify-between px-1 text-sm">
                                    <span className="text-fg">{m.displayName}</span>
                                    <span className={`text-xs ${m.role === 'Streamer' ? 'text-accent' : 'text-fg-muted'}`}>
                                        {t(`room.role.${m.role}`)}
                                    </span>
                                </div>
                            ))}
                    </div>
                </section>

                <BetsPanel roomId={room.id} isStreamer={room.role === 'Streamer'} onBalance={setBalance} />
            </div>
        </div>
    );
}