import { useCallback, useEffect, useRef, useState } from 'react';
import {
    HubConnection,
    HubConnectionBuilder,
    HttpTransportType,
    LogLevel,
} from '@microsoft/signalr';
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack';

declare global {
    interface Window {
        // Состояние оверлея, которое нативный CEF-хост выставляет через JS
        __castOverlayOpen?: boolean;
    }
}

const ACCENT = 'rgb(155 223 30)'; // акцент платформы (#9BDF1E)

interface OverlayMember { userId: string; displayName: string; role: string }

interface ActiveMedia {
    id: number;
    title: string;
    webmUrl: string | null;
    oggUrl: string | null;
    isVideo: boolean;
    senderName: string;
    cost: number;
    clipStartMs: number | null;
    clipEndMs: number | null;
    chargeId: string | null;
    // Случайное размещение видео на экране (% от ширины/высоты) и наклон (град.)
    rndLeft: number;
    rndTop: number;
    rotation: number;
}

const num = (v: any, d: number): number => (typeof v === 'number' ? v : d);
const rand = (min: number, max: number) => min + Math.random() * (max - min);

function toActiveMedia(m: any, id: number): ActiveMedia | null {
    if (!m) return null;
    const webm = m?.WebmUrl ?? m?.webmUrl ?? null;
    const ogg = m?.OggUrl ?? m?.oggUrl ?? null;
    if (!webm && !ogg) return null;
    return {
        id,
        title: m?.Title ?? m?.title ?? '',
        webmUrl: webm,
        oggUrl: ogg,
        isVideo: !!(m?.IsVideo ?? m?.isVideo),
        senderName: m?.SenderName ?? m?.senderName ?? '',
        cost: num(m?.Cost ?? m?.cost, 0),
        clipStartMs: (m?.ClipStartMs ?? m?.clipStartMs) ?? null,
        clipEndMs: (m?.ClipEndMs ?? m?.clipEndMs) ?? null,
        chargeId: m?.ChargeId ?? m?.chargeId ?? null,
        // Позиция с отступом от краёв (8-92% по X, 12-72% по Y — низ оставляем
        // под подпись), наклон −45...+45, чтобы видео не переворачивалось
        rndLeft: rand(8, 92),
        rndTop: rand(12, 72),
        rotation: rand(-45, 45),
    };
}

// Разбор объекта MediaPlayback (прямое сообщение "MediaPlayback")
const readMediaObj = (m: any, id: number): ActiveMedia | null => toActiveMedia(m, id);

// Разбор GameCommand.Media (PascalCase от MessagePack / camelCase от JSON)
const readMedia = (cmd: any, id: number): ActiveMedia | null => toActiveMedia(cmd?.Media ?? cmd?.media, id);

const readRole = (d: any): string => {
    const r = d?.Role ?? d?.role;
    return typeof r === 'number' ? (r === 1 ? 'Streamer' : 'Viewer') : (r ?? 'Viewer');
};

/**
 * Хук подключения оверлея к хабу комнаты. Код комнаты и токен приходят в URL
 * (?code=...&token=...) — их подставляет десктоп при запуске CEF-хоста.
 * Стример (под своим токеном) видит ростер и может кикнуть/забанить зрителя
 */
function useOverlayRoom() {
    const [members, setMembers] = useState<OverlayMember[]>([]);
    const [online, setOnline] = useState(0);
    const [roomId, setRoomId] = useState<string | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [media, setMedia] = useState<ActiveMedia[]>([]);
    const [feed, setFeed] = useState<{ id: number; eventId: string; username: string }[]>([]);
    const [bets, setBets] = useState<any[]>([]);
    const [events, setEvents] = useState<{ eventId: string; title: string; category: string | null; costCoins: number; enabled: boolean }[]>([]);
    const connRef = useRef<HubConnection | null>(null);
    const tokenRef = useRef<string | null>(null);
    const roomIdRef = useRef<string | null>(null);
    const codeRef = useRef<string | null>(null);
    const mediaSeq = useRef(0);

    const removeMedia = useCallback((id: number) => {
        setMedia((list) => list.filter((m) => m.id !== id));
    }, []);

    // Skip: убрать последнее проигрываемое медиа / очистить все
    const skipLast = useCallback(() => setMedia((list) => list.slice(0, -1)), []);
    const skipAll = useCallback(() => setMedia([]), []);

    // #Громкость медиа (0-100). Хранится локально, синхронизируется через хаб.
    // volumeEcho — флаг, чтобы пришедшее с сервера значение не ушло обратно
    const [volume, setVolumeState] = useState<number>(() => {
        try { const v = Number(localStorage.getItem('cast.overlay.volume')); return Number.isFinite(v) && v >= 0 ? v : 100; }
        catch { return 100; }
    });
    const volumeEcho = useRef(false);
    const setVolume = useCallback((v: number) => {
        const vol = Math.max(0, Math.min(100, Math.round(v)));
        setVolumeState(vol);
        try { localStorage.setItem('cast.overlay.volume', String(vol)); } catch { /* ignore */ }
        if (!volumeEcho.current && connRef.current && roomIdRef.current)
            void connRef.current.invoke('SetMediaVolume', roomIdRef.current, vol).catch(() => { });
        volumeEcho.current = false;
    }, []);

    // Сообщить серверу, что медиа не воспроизвелось — он вернёт баллы зрителю
    const reportMediaFailed = useCallback((chargeId: string | null) => {
        if (!chargeId || !connRef.current) return;
        try { void connRef.current.invoke('ReportMediaFailed', chargeId); } catch { /* ignore */ }
    }, []);

    // REST к API под токеном из URL (оверлей вне auth-store веб-приложения)
    const rest = useCallback((path: string, init?: RequestInit) =>
        fetch(`/api${path}`, {
            ...init,
            headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${tokenRef.current}`, ...(init?.headers ?? {}) },
        }), []);

    const loadBets = useCallback(async () => {
        if (!roomIdRef.current) return;
        try {
            const r = await rest(`/rooms/${roomIdRef.current}/bets`);
            if (r.ok) setBets(await r.json());
        } catch { /* ignore */ }
    }, [rest]);

    const createBet = useCallback(async (title: string, outcomes: string[], locksInSeconds: number) => {
        if (!roomIdRef.current || !title || outcomes.length < 2) return;
        await rest(`/rooms/${roomIdRef.current}/bets`, { method: 'POST', body: JSON.stringify({ title, outcomes, locksInSeconds }) });
        void loadBets();
    }, [rest, loadBets]);

    const resolveBet = useCallback(async (betId: string, winningOutcomeId: string) => {
        if (!roomIdRef.current) return;
        await rest(`/rooms/${roomIdRef.current}/bets/${betId}/resolve`, { method: 'POST', body: JSON.stringify({ winningOutcomeId }) });
        void loadBets();
    }, [rest, loadBets]);

    const cancelBet = useCallback(async (betId: string) => {
        if (!roomIdRef.current) return;
        await rest(`/rooms/${roomIdRef.current}/bets/${betId}/cancel`, { method: 'POST' });
        void loadBets();
    }, [rest, loadBets]);

    // Отладка событий: список событий комнаты + вызов через хаб (стример = ∞ валюта)
    const loadEvents = useCallback(async () => {
        if (!roomIdRef.current) return;
        try {
            const r = await rest(`/rooms/${roomIdRef.current}/events`);
            if (r.ok) setEvents(await r.json());
        } catch { /* ignore */ }
    }, [rest]);

    const triggerEvent = useCallback(async (eventId: string) => {
        if (!connRef.current || !codeRef.current) return;
        try { await connRef.current.invoke('TriggerEvent', codeRef.current, eventId, {}, null); }
        catch { /* ignore */ }
    }, []);

    useEffect(() => {
        const params = new URLSearchParams(window.location.search);
        const code = params.get('code');
        const token = params.get('token');
        if (!code || !token) {
            setError('no-context');
            return;
        }
        tokenRef.current = token;
        codeRef.current = code.trim().toUpperCase();

        const conn = new HubConnectionBuilder()
            .withUrl('/hubs/room', {
                accessTokenFactory: () => token,
                skipNegotiation: true,
                transport: HttpTransportType.WebSockets,
            })
            .withHubProtocol(new MessagePackHubProtocol())
            .withAutomaticReconnect()
            .configureLogging(LogLevel.Warning)
            .build();
        connRef.current = conn;

        let cancelled = false;

        conn.on('RoomRoster', (r: any) => {
            const list = (r?.Members ?? r?.members ?? []).map((m: any) => ({
                userId: m?.UserId ?? m?.userId ?? '',
                displayName: m?.DisplayName ?? m?.displayName ?? '',
                role: readRole(m),
            }));
            setMembers(list);
            setOnline(r?.Online ?? r?.online ?? list.length);
            setError(null); // пришёл ростер — соединение точно живо
        });

        // Команда события доходит до стримера (оверлей в streamer-группе):
        // ведём ленту последних событий и проигрываем прикреплённое медиа
        conn.on('GameCommand', (cmd: any) => {
            const eventId = cmd?.EventId ?? cmd?.eventId ?? '';
            const username = cmd?.Username ?? cmd?.username ?? '';
            setFeed((list) => [{ id: ++mediaSeq.current, eventId, username }, ...list].slice(0, 20));
            const item = readMedia(cmd, ++mediaSeq.current);
            if (item) setMedia((list) => [...list, item]);
        });

        // Прямое медиа от зрителя (отдельно от игровых событий) — проигрываем
        // и пишем в ленту последних событий ("кто -> что отправил")
        conn.on('MediaPlayback', (m: any) => {
            const item = readMediaObj(m, ++mediaSeq.current);
            if (item) {
                setMedia((list) => [...list, item]);
                setFeed((list) => [{ id: ++mediaSeq.current, eventId: `медиа: ${item.title}`, username: item.senderName || 'viewer' }, ...list].slice(0, 20));
            }
        });

        // Изменение ставок — перечитываем список
        conn.on('BetUpdated', () => void loadBets());

        // Синхронизация громкости (пришло с сервера — применяем без эха назад)
        conn.on('MediaVolumeChanged', (v: number) => {
            volumeEcho.current = true;
            setVolume(typeof v === 'number' ? v : 100);
        });

        const joinAgain = () => conn.invoke('JoinRoom', code.trim().toUpperCase());
        conn.onreconnected(() => { setError(null); return joinAgain(); });

        // Надёжный вход: на первом запуске игры комната/группа могла быть ещё
        // не готова — повторяем подключение и вход несколько раз, иначе медиа не
        // доходило до оверлея
        void (async () => {
            for (let attempt = 0; attempt < 6 && !cancelled; attempt++) {
                try {
                    if (conn.state === 'Disconnected') await conn.start();
                    const dto = await joinAgain();
                    if (cancelled) return;
                    const rid = dto?.Id ?? dto?.id ?? null;
                    roomIdRef.current = rid;
                    setRoomId(rid);
                    setError(null);
                    void loadBets();
                    void loadEvents();
                    return;
                } catch {
                    if (cancelled) return;
                    await new Promise((r) => setTimeout(r, 1500));
                }
            }
            if (!cancelled) setError('connect');
        })();

        return () => { cancelled = true; void conn.stop(); };
    }, []);

    const kick = useCallback(async (userId: string, ban: boolean) => {
        if (!connRef.current || !roomId) return;
        try { await connRef.current.invoke('KickViewer', roomId, userId, ban); }
        catch { /* ignore */ }
    }, [roomId]);

    return { members, online, error, kick, media, removeMedia, reportMediaFailed, skipLast, skipAll, volume, setVolume, feed, bets, createBet, resolveBet, cancelBet, events, triggerEvent };
}

/**
 * Один проигрываемый медиа-элемент поверх игры. Видео (WebM) показывается с
 * учётом позиции/масштаба; для звука (Ogg) — невидимый аудио-тег. По окончании
 * (или ошибке) элемент убирается. Поддерживается обрезка клипа (clipStart/End)
 */
function MediaLayer({ item, onDone, onFailed, volume }: { item: ActiveMedia; onDone: (id: number) => void; onFailed: (chargeId: string | null) => void; volume: number }) {
    const ref = useRef<HTMLVideoElement | HTMLAudioElement | null>(null);

    // Живое применение громкости (0-100 -> 0-1)
    useEffect(() => { if (ref.current) ref.current.volume = Math.max(0, Math.min(1, volume / 100)); }, [volume]);

    useEffect(() => {
        const el = ref.current;
        if (!el) return;
        const startSec = (item.clipStartMs ?? 0) / 1000;
        const endSec = item.clipEndMs != null ? item.clipEndMs / 1000 : null;

        const fail = () => { onFailed(item.chargeId); onDone(item.id); };
        const onLoaded = () => { if (startSec > 0) el.currentTime = startSec; void el.play?.().catch(fail); };
        const onTime = () => { if (endSec != null && el.currentTime >= endSec) onDone(item.id); };
        const onEnded = () => onDone(item.id);
        const onErr = () => fail();

        el.addEventListener('loadedmetadata', onLoaded);
        el.addEventListener('timeupdate', onTime);
        el.addEventListener('ended', onEnded);
        el.addEventListener('error', onErr);
        // не оставляем медиа висеть дольше 30 c
        const guard = window.setTimeout(() => onDone(item.id), 30000);
        return () => {
            window.clearTimeout(guard);
            el.removeEventListener('loadedmetadata', onLoaded);
            el.removeEventListener('timeupdate', onTime);
            el.removeEventListener('ended', onEnded);
            el.removeEventListener('error', onErr);
        };
    }, [item, onDone, onFailed]);

    if (item.isVideo && item.webmUrl) {
        // Видео: случайная позиция и наклон, рамка #9BDF1E, 3px
        return (
            <div
                className="pointer-events-none fixed"
                style={{
                    left: `${item.rndLeft}%`,
                    top: `${item.rndTop}%`,
                    transform: `translate(-50%, -50%) rotate(${item.rotation}deg)`,
                }}
            >
                <video ref={ref as any} src={item.webmUrl} autoPlay
                    style={{ border: '3px solid #9BDF1E', borderRadius: 6, background: '#000' }}
                    className="max-h-[50vh] max-w-[50vw]" />
            </div>
        );
    }
    // Аудио: невидимый плеер (источник — ogg, иначе webm-аудио)
    return <audio ref={ref as any} src={item.oggUrl ?? item.webmUrl ?? undefined} autoPlay />;
}

/**
 * Подпись медиа внизу по центру: 2 строки — кто отправил и название • цена.
 * Группируем по отправителю: один блок на пользователя, внутри —
 * перечень его медиа (повторы схлопываются с xN), чтобы не плодить дубли
 */
function MediaCaptions({ items }: { items: ActiveMedia[] }) {
    if (items.length === 0) return null;

    // sender -> Map(label -> count), сохраняем порядок появления отправителей
    const order: string[] = [];
    const bySender = new Map<string, Map<string, number>>();
    for (const m of items) {
        const sender = m.senderName || 'viewer';
        const label = `${m.title}${m.cost ? ` • ${m.cost}` : ''}`;
        if (!bySender.has(sender)) { bySender.set(sender, new Map()); order.push(sender); }
        const labels = bySender.get(sender)!;
        labels.set(label, (labels.get(label) ?? 0) + 1);
    }

    return (
        <div className="pointer-events-none fixed bottom-8 left-1/2 -translate-x-1/2 flex flex-col items-center gap-2">
            {order.map((sender) => (
                <div key={sender} className="rounded-lg px-4 py-2 text-center"
                    style={{ background: 'rgba(0,0,0,0.7)', backdropFilter: 'blur(4px)' }}>
                    <div className="text-sm font-semibold" style={{ color: '#9BDF1E' }}>{sender}</div>
                    {[...bySender.get(sender)!.entries()].map(([label, count]) => (
                        <div key={label} className="text-xs text-white">{label}{count > 1 ? ` ×${count}` : ''}</div>
                    ))}
                </div>
            ))}
        </div>
    );
}

/**
 * Страница внутриигрового оверлея.
 *
 * Рендерится off-screen в CEF-хосте (Cast.Overlay.Host) и композитится поверх
 * кадра игры инжектированной DLL. Фон страницы прозрачный — игра видна сквозь
 * оверлей (в хосте задан background_color = 0, рендерер блендит premultiplied
 * alpha).
 *
 * Состояние open/closed приходит из нативной части: при нажатии F8 хост
 * выполняет JS и шлёт CustomEvent('cast-overlay', { detail: { open } }), а также
 * выставляет window.__castOverlayOpen.
 *   - closed: только подсказка-тост, клики проходят в игру (pointer-events: none);
 *   - open:   полноценный интерактивный UI
 */
export function OverlayPage() {
    const [open, setOpen] = useState<boolean>(() => window.__castOverlayOpen ?? false);
    const [hintVisible, setHintVisible] = useState(true);
    const { members, online, error, kick, media, removeMedia, reportMediaFailed, skipLast, skipAll, volume, setVolume, feed, bets, createBet, resolveBet, cancelBet, events, triggerEvent } = useOverlayRoom();
    const [betTitle, setBetTitle] = useState('');
    const [betOutcomes, setBetOutcomes] = useState('');
    const [betDuration, setBetDuration] = useState(60);

    // Тик раз в секунду, чтобы скрывать закрытые ставки через 30c
    const [, setNow] = useState(0);
    useEffect(() => {
        const id = window.setInterval(() => setNow((n) => n + 1), 1000);
        return () => window.clearInterval(id);
    }, []);
    const visibleBets = (bets as any[]).filter((b) => {
        const status = b.status ?? b.Status;
        const resolvedAt = b.resolvedAt ?? b.ResolvedAt;
        return status === 'Open' || !resolvedAt || (Date.now() - new Date(resolvedAt).getTime()) < 30000;
    });

    // Отладка событий: поиск + фильтр по категории
    const [eventQuery, setEventQuery] = useState('');
    const [eventCategory, setEventCategory] = useState<string>('');
    const categories = Array.from(new Set(events.map((e) => e.category).filter(Boolean))) as string[];
    const filteredEvents = events.filter((e) =>
        (!eventQuery || e.title.toLowerCase().includes(eventQuery.toLowerCase()))
        && (!eventCategory || e.category === eventCategory));

    // Перетаскивание панели оверлея: смещение от центра
    const [drag, setDrag] = useState({ x: 0, y: 0 });
    const dragState = useRef<{ sx: number; sy: number; ox: number; oy: number } | null>(null);
    const onDragDown = (e: any) => {
        dragState.current = { sx: e.clientX, sy: e.clientY, ox: drag.x, oy: drag.y };
        e.currentTarget?.setPointerCapture?.(e.pointerId);
    };
    const onDragMove = (e: any) => {
        const s = dragState.current;
        if (!s) return;
        setDrag({ x: s.ox + (e.clientX - s.sx), y: s.oy + (e.clientY - s.sy) });
    };
    const onDragUp = () => { dragState.current = null; };

    // Прозрачный фон только на время жизни страницы оверлея
    useEffect(() => {
        const html = document.documentElement;
        const body = document.body;
        const prevHtml = html.style.background;
        const prevBody = body.style.background;
        html.style.background = 'transparent';
        body.style.background = 'transparent';
        return () => {
            html.style.background = prevHtml;
            body.style.background = prevBody;
        };
    }, []);

    // Подписка на состояние из нативной части
    useEffect(() => {
        const onState = (e: Event) => {
            const detail = (e as CustomEvent<{ open: boolean }>).detail;
            setOpen(!!detail?.open);
        };
        window.addEventListener('cast-overlay', onState as EventListener);
        return () => window.removeEventListener('cast-overlay', onState as EventListener);
    }, []);

    // Тост-подсказка: показываем на старте и при каждом закрытии, прячем через 5 c
    useEffect(() => {
        if (open) {
            setHintVisible(false);
            return;
        }
        setHintVisible(true);
        const t = window.setTimeout(() => setHintVisible(false), 5000);
        return () => window.clearTimeout(t);
    }, [open]);

    return (
        <div className="fixed inset-0 overflow-hidden select-none" style={{ background: 'transparent' }}>
            {/* Медиа от зрителей — проигрывается поверх игры в любом состоянии оверлея */}
            {media.map((m) => <MediaLayer key={m.id} item={m} onDone={removeMedia} onFailed={reportMediaFailed} volume={volume} />)}
            <MediaCaptions items={media} />

            {/* Закрытое состояние: тост-подсказка. Клики проходят в игру */}
            <div
                className="fixed bottom-6 left-1/2 -translate-x-1/2"
                style={{
                    opacity: !open && hintVisible ? 1 : 0,
                    pointerEvents: 'none',
                    transition: 'opacity 0.5s ease',
                }}
            >
                <div
                    className="flex items-center gap-2 rounded px-4 py-2 text-sm text-white shadow-lg"
                    style={{ background: 'rgba(0,0,0,0.72)', backdropFilter: 'blur(4px)' }}
                >
                    <span>Нажмите</span>
                    <kbd
                        className="rounded px-2 py-0.5 font-mono text-xs"
                        style={{ background: 'rgba(255,255,255,0.15)', color: ACCENT }}
                    >
                        F8
                    </kbd>
                    <span>, чтобы открыть оверлей</span>
                </div>
            </div>

            {/* Открытое состояние: полноценный интерактивный UI */}
            <div
                className="fixed inset-0 flex items-center justify-center"
                style={{
                    opacity: open ? 1 : 0,
                    pointerEvents: open ? 'auto' : 'none',
                    transition: 'opacity 0.2s ease',
                }}
            >
                <div className="absolute inset-0" style={{ background: 'rgba(0,0,0,0.5)', backdropFilter: 'blur(4px)' }} />
                <div
                    className="relative flex h-[80vh] w-[70vw] max-w-5xl flex-col overflow-hidden rounded text-white"
                    style={{
                        background: 'rgba(20,20,24,0.84)',
                        border: '1px solid rgba(255,255,255,0.10)',
                        boxShadow: '0 24px 80px rgba(0,0,0,0.55)',
                        transform: `translate(${drag.x}px, ${drag.y}px)`,
                    }}
                >
                    <header
                        className="flex items-center justify-between px-6 py-4 select-none"
                        style={{ borderBottom: '1px solid rgba(255,255,255,0.10)', cursor: 'move', touchAction: 'none' }}
                        onPointerDown={onDragDown}
                        onPointerMove={onDragMove}
                        onPointerUp={onDragUp}
                    >
                        <div className="flex items-center gap-2">
                            <span className="h-2.5 w-2.5 rounded-full" style={{ background: ACCENT }} />
                            <h1 className="text-lg font-semibold">Cast Overlay</h1>
                            <span className="text-xs" style={{ color: 'rgba(255,255,255,0.4)' }}>(тащите заголовок)</span>
                        </div>
                        <span className="text-xs" style={{ color: 'rgba(255,255,255,0.5)' }}>
                            F8 — закрыть
                        </span>
                    </header>

                    <main className="flex-1 overflow-auto p-6">
                        {/* громкость медиа + пропуск */}
                        <div className="mb-3 flex flex-wrap items-center gap-3 rounded-lg px-3 py-2"
                            style={{ background: 'rgba(255,255,255,0.05)' }}>
                            <span className="text-xs" style={{ color: 'rgba(255,255,255,0.6)' }}>Громкость</span>
                            <input type="range" min={0} max={100} value={volume}
                                onChange={(e) => setVolume(+e.target.value)} className="flex-1" style={{ minWidth: 120 }} />
                            <span className="w-8 text-right text-xs">{volume}</span>
                            <button onClick={skipLast} disabled={media.length === 0}
                                className="rounded-md px-2 py-1 text-xs disabled:opacity-40"
                                style={{ border: '1px solid rgba(255,255,255,0.2)' }}>Пропустить последнее</button>
                            <button onClick={skipAll} disabled={media.length === 0}
                                className="rounded-md px-2 py-1 text-xs disabled:opacity-40"
                                style={{ border: '1px solid rgba(255,120,120,0.5)', color: 'rgb(255,140,140)' }}>Пропустить всё</button>
                        </div>

                        <div className="flex items-center justify-between">
                            <h2 className="text-base font-semibold">Участники</h2>
                            <span className="text-sm" style={{ color: ACCENT }}>{online} онлайн</span>
                        </div>

                        {error === 'no-context' && (
                            <p className="mt-3 text-sm" style={{ color: 'rgba(255,255,255,0.55)' }}>
                                Нет данных о комнате. Создайте комнату в Cast.Desktop перед запуском игры.
                            </p>
                        )}
                        {error === 'connect' && (
                            <p className="mt-3 text-sm" style={{ color: 'rgba(255,120,120,0.9)' }}>
                                Не удалось подключиться к комнате.
                            </p>
                        )}

                        <div className="mt-3 space-y-1">
                            {members.map((m) => (
                                <div
                                    key={m.userId}
                                    className="flex items-center gap-3 rounded-lg px-3 py-2"
                                    style={{ background: 'rgba(255,255,255,0.05)' }}
                                >
                                    <span className="flex-1 truncate">{m.displayName}</span>
                                    <span
                                        className="text-xs"
                                        style={{ color: m.role === 'Streamer' ? ACCENT : 'rgba(255,255,255,0.5)' }}
                                    >
                                        {m.role === 'Streamer' ? 'Стример' : 'Зритель'}
                                    </span>
                                    {m.role !== 'Streamer' && (
                                        <div className="flex items-center gap-1">
                                            <button
                                                onClick={() => void kick(m.userId, false)}
                                                className="rounded-md px-2 py-1 text-xs"
                                                style={{ border: '1px solid rgba(255,255,255,0.2)' }}
                                            >
                                                Выгнать
                                            </button>
                                            <button
                                                onClick={() => void kick(m.userId, true)}
                                                className="rounded-md px-2 py-1 text-xs"
                                                style={{ border: '1px solid rgba(255,120,120,0.5)', color: 'rgb(255,140,140)' }}
                                            >
                                                Бан
                                            </button>
                                        </div>
                                    )}
                                </div>
                            ))}
                            {members.length === 0 && !error && (
                                <p className="text-sm" style={{ color: 'rgba(255,255,255,0.5)' }}>
                                    Пока никто не подключился.
                                </p>
                            )}
                        </div>

                        {/* Ставки: создание и разрешение (стример) */}
                        <h2 className="mt-6 text-base font-semibold">Ставки</h2>
                        <div className="mt-2 flex flex-wrap items-center gap-2">
                            <input
                                value={betTitle}
                                onChange={(e) => setBetTitle(e.target.value)}
                                placeholder="Вопрос"
                                className="flex-1 rounded-md px-2 py-1 text-sm text-white"
                                style={{ background: 'rgba(255,255,255,0.08)', minWidth: 120 }}
                            />
                            <input
                                value={betOutcomes}
                                onChange={(e) => setBetOutcomes(e.target.value)}
                                placeholder="Исходы через запятую"
                                className="flex-1 rounded-md px-2 py-1 text-sm text-white"
                                style={{ background: 'rgba(255,255,255,0.08)', minWidth: 120 }}
                            />
                            <input
                                type="number" min={5} value={betDuration}
                                onChange={(e) => setBetDuration(Math.max(5, +e.target.value || 0))}
                                title="Приём ставок, секунд"
                                className="w-20 rounded-md px-2 py-1 text-sm text-white"
                                style={{ background: 'rgba(255,255,255,0.08)' }}
                            />
                            <span className="text-xs" style={{ color: 'rgba(255,255,255,0.5)' }}>сек</span>
                            <button
                                onClick={() => {
                                    const list = betOutcomes.split(',').map((s) => s.trim()).filter(Boolean);
                                    void createBet(betTitle.trim(), list, betDuration);
                                    setBetTitle(''); setBetOutcomes('');
                                }}
                                className="rounded-md px-3 py-1 text-sm font-medium"
                                style={{ background: ACCENT, color: '#0d0d10' }}
                            >
                                Создать
                            </button>
                        </div>
                        <div className="mt-2 space-y-2">
                            {visibleBets.map((b: any) => {
                                const status = b.status ?? b.Status;
                                const open = status === 'Open';
                                const locksAt = b.locksAt ?? b.LocksAt;
                                const msLeft = locksAt ? new Date(locksAt).getTime() - Date.now() : 0;
                                const secLeft = Math.max(0, Math.ceil(msLeft / 1000));
                                // Открыт и таймер не истёк -> отсчёт; истёк -> "Закрыт" (ждёт исхода)
                                const statusText = open ? (msLeft > 0 ? `Приём: ${secLeft}с` : 'Закрыт') : status;
                                const outcomes = b.outcomes ?? b.Outcomes ?? [];
                                return (
                                    <div key={b.id ?? b.Id} className="rounded-lg p-2" style={{ background: 'rgba(255,255,255,0.05)' }}>
                                        <div className="flex items-center justify-between">
                                            <span className="text-sm font-medium">{b.title ?? b.Title}</span>
                                            <span className="text-xs" style={{ color: msLeft > 0 && open ? ACCENT : 'rgba(255,255,255,0.5)' }}>{statusText}</span>
                                        </div>
                                        <div className="mt-1 space-y-1">
                                            {outcomes.map((o: any) => (
                                                <div key={o.id ?? o.Id} className="flex items-center gap-2 text-sm">
                                                    <span className="flex-1">{o.label ?? o.Label}</span>
                                                    <span className="text-xs" style={{ color: 'rgba(255,255,255,0.5)' }}>{o.pool ?? o.Pool}</span>
                                                    {open && (
                                                        <button onClick={() => void resolveBet(b.id ?? b.Id, o.id ?? o.Id)}
                                                            className="rounded px-2 py-0.5 text-xs" style={{ border: `1px solid ${ACCENT}`, color: ACCENT }}>
                                                            Победил
                                                        </button>
                                                    )}
                                                </div>
                                            ))}
                                        </div>
                                        {open && (
                                            <button onClick={() => void cancelBet(b.id ?? b.Id)} className="mt-1 text-xs" style={{ color: 'rgb(255,140,140)' }}>
                                                Отменить
                                            </button>
                                        )}
                                    </div>
                                );
                            })}
                            {visibleBets.length === 0 && <p className="text-sm" style={{ color: 'rgba(255,255,255,0.5)' }}>Ставок пока нет.</p>}
                        </div>

                        {/* Отладка: вызов игровых событий с поиском и фильтром */}
                        <h2 className="mt-6 text-base font-semibold">Игровые события (отладка)</h2>
                        <div className="mt-2 flex flex-wrap items-center gap-2">
                            <input
                                value={eventQuery}
                                onChange={(e) => setEventQuery(e.target.value)}
                                placeholder="Поиск…"
                                className="flex-1 rounded-md px-2 py-1 text-sm text-white"
                                style={{ background: 'rgba(255,255,255,0.08)', minWidth: 120 }}
                            />
                            <select
                                value={eventCategory}
                                onChange={(e) => setEventCategory(e.target.value)}
                                className="rounded-md px-2 py-1 text-sm"
                                style={{ background: 'rgba(255,255,255,0.08)', color: 'white' }}
                            >
                                <option value="" style={{ color: 'black' }}>Все категории</option>
                                {categories.map((c) => <option key={c} value={c} style={{ color: 'black' }}>{c}</option>)}
                            </select>
                        </div>
                        <div className="mt-2 flex gap-2 overflow-x-auto pb-2">
                            {filteredEvents.map((e) => (
                                <button
                                    key={e.eventId}
                                    disabled={!e.enabled}
                                    onClick={() => void triggerEvent(e.eventId)}
                                    title={e.category ?? ''}
                                    className="shrink-0 rounded-lg px-3 py-2 text-left text-sm disabled:opacity-40"
                                    style={{ background: 'rgba(255,255,255,0.06)', border: '1px solid rgba(255,255,255,0.12)', minWidth: 120 }}
                                >
                                    <div className="font-medium">{e.title}</div>
                                    <div className="text-xs" style={{ color: ACCENT }}>{e.costCoins}</div>
                                </button>
                            ))}
                            {filteredEvents.length === 0 && <p className="text-sm" style={{ color: 'rgba(255,255,255,0.5)' }}>Событий нет.</p>}
                        </div>

                        {/* Лента последних событий */}
                        <h2 className="mt-6 text-base font-semibold">Последние события</h2>
                        <div className="mt-2 space-y-1">
                            {feed.map((f) => (
                                <div key={f.id} className="text-sm" style={{ color: 'rgba(255,255,255,0.8)' }}>
                                    <span style={{ color: ACCENT }}>{f.username}</span> → {f.eventId}
                                </div>
                            ))}
                            {feed.length === 0 && <p className="text-sm" style={{ color: 'rgba(255,255,255,0.5)' }}>Событий пока нет.</p>}
                        </div>
                    </main>
                </div>
            </div>
        </div>
    );
}