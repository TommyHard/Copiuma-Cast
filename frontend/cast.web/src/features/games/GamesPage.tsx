import { Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { api } from '@/lib/api';
import { useState } from 'react';
import { useAuthStore } from '@/store/auth';
import { GameEditorModal } from './GameEditorModal';
import type { GameCard } from '@/lib/types';

export function GamesPage() {
    const { t } = useTranslation();
    const { user } = useAuthStore();
    const isAdmin = useAuthStore((s) => s.hasRole('Admin'));
    const [isModalOpen, setModalOpen] = useState(false);

    const { data, isLoading, refetch } = useQuery({
        queryKey: ['games'],
        queryFn: async () => (await api.get<GameCard[]>('/games')).data,
    });

    if (isLoading) return <p className="text-fg-muted">{t('common.loading')}</p>;

    return (
        <div className="mx-auto max-w-4xl">
            <div className="mb-4 flex items-center justify-between">
                <h1 className="text-2xl font-bold text-fg">{t('games.title')}</h1>
                {isAdmin && (
                    <button onClick={() => setModalOpen(true)} className="rounded bg-accent px-3 py-1.5 text-sm font-medium text-white">
                        {t('games.creation')}
                    </button>
                )}
            </div>
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
                {(data ?? []).map((g) => {
                    const card = (
                        <div className="overflow-hidden rounded-lg border border-border bg-bg-elevated">
                            <div className="aspect-video bg-bg-accent">
                                {g.bannerUrl && <img src={g.bannerUrl} alt="" className="h-full w-full object-cover" />}
                            </div>
                            <div className="p-3">
                                <h3 className="font-semibold text-fg">{g.title}</h3>
                                {g.genre && <p className="text-xs text-fg-muted">{g.genre}</p>}
                                {g.description && <p className="mt-1 line-clamp-2 text-sm text-fg-muted">{g.description}</p>}
                                {!g.isEnabled && <p className="mt-1 text-xs text-danger">{t('games.disabled')}</p>}
                            </div>
                        </div>
                    );

                    if (g.isEnabled || isAdmin) {
                        return (
                            <Link
                                key={g.slug}
                                to={`/games/${g.slug}`}
                                className={`hover:opacity-90 ${!g.isEnabled ? 'opacity-60' : ''}`}
                            >
                                {card}
                            </Link>
                        );
                    }

                    return (
                        <div key={g.slug} className="cursor-not-allowed opacity-50" aria-disabled>
                            {card}
                        </div>
                    );
                })}
            </div>

            <GameEditorModal
                isOpen={isModalOpen}
                onClose={() => setModalOpen(false)}
                onSuccess={() => refetch()}
            />
        </div>
    );
}