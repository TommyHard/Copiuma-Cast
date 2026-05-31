import { create } from 'zustand';
import type { HubConnection } from '@microsoft/signalr';
import { createHubConnection, HUBS } from '@/lib/signalr';
import { api } from '@/lib/api';
import type { RoomInfo, RoomRole } from '@/lib/types';

const readRole = (d: any): RoomRole => {
    const r = d?.Role ?? d?.role;
    return typeof r === 'number' ? (r === 1 ? 'Streamer' : 'Viewer') : (r ?? 'Viewer');
};
const readId = (d: any): string => d?.Id ?? d?.id ?? '';
const readBalance = (r: any): number => r?.Balance ?? r?.balance ?? 0;

export interface RoomLogEntry { user: string; event: string }

// Код активной комнаты переживает перезагрузку страницы, чтобы зритель
// автоматически переподключился, а не "выпадал" из комнаты
const SESSION_KEY = 'cast.room.code';
const saveSession = (code: string) => { try { localStorage.setItem(SESSION_KEY, code); } catch { /* ignore */ } };
const clearSession = () => { try { localStorage.removeItem(SESSION_KEY); } catch { /* ignore */ } };
export const savedRoomCode = (): string | null => { try { return localStorage.getItem(SESSION_KEY); } catch { return null; } };
export interface RosterMember { userId: string; displayName: string; role: RoomRole }
export interface Roster { online: number; members: RosterMember[] }

const readRoster = (d: any): Roster => {
    const members = (d?.Members ?? d?.members ?? []).map((m: any) => ({
        userId: m?.UserId ?? m?.userId ?? '',
        displayName: m?.DisplayName ?? m?.displayName ?? '',
        role: readRole(m),
    }));
    return { online: d?.Online ?? d?.online ?? members.length, members };
};

interface RoomStore {
    room: RoomInfo | null;
    balance: number | null;
    log: RoomLogEntry[];
    roster: Roster;
    busy: boolean;
    connection: HubConnection | null;
    /** Колбэки, которые ставит активная страница комнаты */
    onBetUpdated: (() => void) | null;
    onKicked: (() => void) | null;

    setHandlers: (h: { onBetUpdated?: () => void; onKicked?: () => void }) => void;
    setBalance: (b: number) => void;
    join: (code: string) => Promise<void>;
    leave: () => Promise<void>;
    trigger: (eventId: string) => Promise<void>;
    sendMedia: (mediaId: string) => Promise<void>;
}

/**
 * Глобальное состояние комнаты: соединение и данные живут в сторе (вне дерева
 * React), поэтому переход на другую страницу не сбрасывает сессию комнаты.
 * Сессия завершается только явным выходом (leave) или киком
 */
export const useRoomStore = create<RoomStore>((set, get) => ({
    room: null,
    balance: null,
    log: [],
    roster: { online: 0, members: [] },
    busy: false,
    connection: null,
    onBetUpdated: null,
    onKicked: null,

    setHandlers: (h) => set((s) => ({
        onBetUpdated: h.onBetUpdated ?? s.onBetUpdated,
        onKicked: h.onKicked ?? s.onKicked,
    })),

    setBalance: (b) => set({ balance: b }),

    join: async (code) => {
        if (get().connection) return; // уже в комнате
        set({ busy: true });
        try {
            const conn = createHubConnection(HUBS.room);
            conn.on('GameCommand', (cmd: any) => {
                const entry: RoomLogEntry = {
                    user: cmd?.Username ?? cmd?.username ?? 'viewer',
                    event: cmd?.EventId ?? cmd?.eventId ?? '',
                };
                set((s) => ({ log: [entry, ...s.log].slice(0, 50) }));
            });
            conn.on('BetUpdated', () => get().onBetUpdated?.());
            conn.on('RoomRoster', (r: any) => set({ roster: readRoster(r) }));
            conn.on('Kicked', () => { get().onKicked?.(); void get().leave(); });
            await conn.start();
            let dto: any;
            try {
                dto = await conn.invoke('JoinRoom', code.trim().toUpperCase());
            } catch (e) {
                // Комната закрыта/не найдена — убираем сохранённый код и соединение,
                // чтобы авто-переподключение не зациклилось на "мёртвой" комнате
                clearSession();
                try { await conn.stop(); } catch { /* ignore */ }
                throw e;
            }
            const roomId = readId(dto);
            set({
                connection: conn,
                room: {
                    id: roomId,
                    code: dto?.Code ?? dto?.code ?? code,
                    title: dto?.Title ?? dto?.title ?? '',
                    gameId: dto?.GameId ?? dto?.gameId ?? null,
                    isOpen: true,
                    role: readRole(dto),
                },
            });
            saveSession(code.trim().toUpperCase());
            try {
                const bal = await api.get<number>(`/rooms/${roomId}/balance`);
                set({ balance: typeof bal.data === 'number' ? bal.data : readBalance(bal.data) });
            } catch { /* баланс необязателен */ }
        } finally {
            set({ busy: false });
        }
    },

    leave: async () => {
        const conn = get().connection;
        clearSession();
        set({ connection: null, room: null, balance: null, log: [], roster: { online: 0, members: [] } });
        try { await conn?.stop(); } catch { /* ignore */ }
    },

    trigger: async (eventId) => {
        const { connection, room } = get();
        if (!connection || !room) return;
        const res = await connection.invoke('TriggerEvent', room.code, eventId, {}, null);
        set({ balance: readBalance(res) });
    },

    sendMedia: async (mediaId) => {
        const { connection, room } = get();
        if (!connection || !room) return;
        const res = await connection.invoke('SendMedia', room.code, mediaId);
        set({ balance: readBalance(res) });
    },
}));