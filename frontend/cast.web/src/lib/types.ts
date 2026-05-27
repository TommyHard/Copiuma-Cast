export type UserStatus = 'Offline' | 'Online' | 'Away' | 'DoNotDisturb';
export type ActivityKind = 'None' | 'Watching' | 'Playing';

export interface AuthResponse {
    accessToken: string;
    expiresAt: string;
    userId: string;
    displayName: string;
    handle: string;
    avatarUrl: string | null;
    language: string;
    coins: number;
}

export interface Profile {
    id: string;
    email: string;
    displayName: string;
    handle: string;
    avatarUrl: string | null;
    language: string;
    status: UserStatus;
}

export interface UserCard {
    userId: string;
    displayName: string;
    handle: string;
    avatarUrl: string | null;
    status: UserStatus;
    activity: ActivityKind;
    activityTarget: string | null;
}

export interface GameCard {
    slug: string;
    title: string;
    description: string | null;
    genre: string | null;
    bannerUrl: string | null;
    releaseDate: string | null;
    isEnabled: boolean;
}

export interface GameEvent {
    id: string;
    title: string;
    description: string | null;
    category: string | null;
    costCoins: number;
    cooldownMs: number;
    enabled: boolean;
}

export interface GameStats {
    sessions: number;
    eventsTriggered: number;
    pointsSpent: number;
    watchHours: number;
}

export interface GameDetail {
    game: GameCard;
    interactions: GameEvent[];
    personal: GameStats;
    global: GameStats;
    globallyDisabledEventIds: string[];
}

// ---- Media ----
export type MediaType = 'Sound' | 'Video';
export type MediaStatus = 'Pending' | 'Approved' | 'Rejected';

export interface MediaItem {
    id: string;
    ownerId: string;
    title: string;
    type: MediaType;
    status: MediaStatus;
    tags: string[];
    costCoins: number;
    originalUrl: string;
    webmUrl: string | null;
    oggUrl: string | null;
    durationMs: number | null;
    clipStartMs: number | null;
    clipEndMs: number | null;
    posXPct: number;
    posYPct: number;
    scalePct: number;
    processed: boolean;
    createdAt: string;
}

// ---- Rooms ----
export type RoomRole = 'Viewer' | 'Streamer';

export interface RoomInfo {
    id: string;
    code: string;
    title: string;
    gameId: string | null;
    isOpen: boolean;
    role: RoomRole;
}

export interface RoomEvent {
    eventId: string;
    title: string;
    category: string | null;
    costCoins: number;
    cooldownMs: number;
    enabled: boolean;
}

export interface TriggerResult { accepted: boolean; balance: number; }

// ---- Bets ----
export type BetStatus = 'Open' | 'Resolved' | 'Cancelled';

export interface BetOutcome { id: string; label: string; pool: number; odds: number | null; }

export interface Bet {
    id: string;
    roomId: string;
    streamerId: string;
    title: string;
    status: BetStatus;
    locksAt: string;
    winningOutcomeId: string | null;
    totalPool: number;
    outcomes: BetOutcome[];
}

export interface FriendRequest {
    linkId: string;
    userId: string;
    displayName: string;
    handle: string;
    avatarUrl: string | null;
    createdAt: string;
}

// ---- Streamer application / admin ----
export type ApplicationStatus = 'Pending' | 'Approved' | 'Rejected';

export interface MyApplication {
    id: string;
    status: ApplicationStatus;
    message: string | null;
    createdAt: string;
}

export interface AdminApplication {
    id: string;
    userId: string;
    displayName: string;
    handle: string;
    message: string | null;
    status: ApplicationStatus;
    createdAt: string;
}

export interface AdminUser { id: string; email: string; displayName: string; handle: string; isBlocked: boolean; }
export interface AdminGame { slug: string; title: string; genre: string | null; isEnabled: boolean; releaseDate: string | null; }

export type FilterMode = 'Blocklist' | 'Allowlist';
export interface TagFilters { mode: FilterMode; tags: string[]; }

export interface ViewerStat { userId: string; displayName: string; handle: string; avatarUrl: string | null; spent: number; }
export interface EventStat { eventId: string; count: number; }
export interface MediaStat { mediaId: string; title: string; count: number; }
export interface Analytics {
    turnoverSpent: number;
    turnoverCredited: number;
    topViewers: ViewerStat[];
    popularEvents: EventStat[];
    popularMedia: MediaStat[];
}

export interface NewsPost {
    id: string;
    title: string;
    body: string;
    imageUrl: string | null;
    published: boolean;
    createdAt: string;
    updatedAt?: string | null;
    authorName: string;
    authorAvatarUrl?: string | null;
}

export interface AdminEvent { eventId: string; title: string; enabled: boolean; }