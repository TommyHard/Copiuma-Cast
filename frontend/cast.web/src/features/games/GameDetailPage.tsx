import { useParams, Link, useNavigate } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { api } from '@/lib/api';
import { useAuthStore } from '@/store/auth';
import type { GameDetail, GameStats } from '@/lib/types';
import { useState } from 'react';
import { GameEditorModal } from './GameEditorModal';

function StatsTable({ title, s }: { title: string; s: GameStats }) {
    const { t } = useTranslation();
    const rows: [string, number][] = [
        [t('games.sessions'), s.sessions],
        [t('games.events'), s.eventsTriggered],
        [t('games.spent'), s.pointsSpent],
        [t('games.watchHours'), s.watchHours],
    ];
    return (
        <div className="rounded-lg border border-border bg-bg-elevated p-3">
            <h4 className="mb-2 text-sm font-semibold text-fg">{title}</h4>
            <dl className="space-y-1 text-sm">
                {rows.map(([k, v]) => (
                    <div key={k} className="flex justify-between"><dt className="text-fg-muted">{k}</dt><dd className="text-fg">{v}</dd></div>
                ))}
            </dl>
        </div>
    );
}

export function GameDetailPage() {
    const { slug = '' } = useParams();
    const navigate = useNavigate();
    const { t } = useTranslation();
    const qc = useQueryClient();
    const isAdmin = useAuthStore((s) => s.hasRole('Admin'));
    const [isModalOpen, setModalOpen] = useState(false);

    const { data, isLoading, refetch } = useQuery({
        queryKey: ['game', slug],
        queryFn: async () => (await api.get<GameDetail>(`/games/${slug}`)).data,
    });

    const toggleStatus = async () => {
        await api.post(`/games/${slug}/toggle`);
        refetch();
    };

    const deleteGame = async () => {
        if (confirm('Вы уверены, что хотите полностью удалить игру и всю её статистику?')) {
            await api.delete(`/games/${slug}`);
            qc.invalidateQueries({ queryKey: ['games'] });
            navigate('/games');
        }
    };

    const toggleGlobalEvent = async (eventId: string, currentStatus: boolean) => {
        try {
            await api.put(`/games/${slug}/events/${eventId}`, !currentStatus, {
                headers: { 'Content-Type': 'application/json' }
            });
            refetch();
        } catch (e) {
            alert("Ошибка при сохранении статуса события");
        }
    };

    if (isLoading) return <p className="text-fg-muted">{t('common.loading')}</p>;
    if (!data) return <p className="text-fg-muted">{t('common.empty')}</p>;

    return (
        <div className="mx-auto max-w-4xl space-y-6">
            <div className="flex items-center justify-between">
                <Link to="/games" className="text-sm text-accent">← {t('games.title')}</Link>

                {isAdmin && (
                    <div className="flex items-center gap-2">
                        <span className={`text-xs font-medium ${data.game.isEnabled ? 'text-success' : 'text-danger'}`}>
                            {data.game.isEnabled ? 'Активна' : 'Отключена'}
                        </span>
                        <button onClick={toggleStatus} className="rounded border border-border px-3 py-1 text-sm text-fg hover:bg-bg-accent">
                            {data.game.isEnabled ? 'Выключить' : 'Включить'}
                        </button>
                        <button onClick={() => setModalOpen(true)} className="rounded border border-border px-3 py-1 text-sm text-fg hover:bg-bg-accent">
                            Редактировать
                        </button>
                        <button onClick={deleteGame} className="rounded border border-danger px-3 py-1 text-sm text-danger hover:bg-danger/10">
                            Удалить
                        </button>
                    </div>
                )}
            </div>

            <header className="overflow-hidden rounded-lg bg-bg-elevated border border-border">
                {data.game.bannerUrl && (
                    <img src={data.game.bannerUrl} alt={data.game.title} className="w-full aspect-[21/9] object-cover bg-bg-accent" />
                )}
                <div className="p-4">
                    <h1 className="text-2xl font-bold text-fg">{data.game.title}</h1>
                    {data.game.genre && <p className="text-sm text-fg-muted">{t('games.genre')}: {data.game.genre}</p>}
                    {data.game.description && <p className="mt-4 text-sm text-fg">{data.game.description}</p>}
                </div>
            </header>

            <section>
                <h2 className="mb-2 text-lg font-semibold text-fg">{t('games.interactions')}</h2>
                {data.interactions.length === 0 ? (
                    <p className="text-sm text-fg-muted">Взаимодействия не загружены или манифест пуст.</p>
                ) : (
                    <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
                        {data.interactions.map((e) => {
                            const isGloballyDisabled = data.globallyDisabledEventIds?.includes(e.id) ?? false;
                            const isCurrentlyEnabled = e.enabled && !isGloballyDisabled;

                            return (
                                <div key={e.id} className={`rounded-lg border border-border p-3 ${isCurrentlyEnabled ? 'bg-bg-elevated' : 'bg-bg opacity-60'}`}>
                                    <div className="flex items-center justify-between">
                                        <div className="flex items-center gap-2">
                                            <span className="font-medium text-fg">{e.title}</span>
                                            {isAdmin && (
                                                <button
                                                    onClick={() => toggleGlobalEvent(e.id, isCurrentlyEnabled)}
                                                    className={`text-xs px-2 py-0.5 rounded ${isCurrentlyEnabled ? 'border border-danger text-danger hover:bg-danger/10' : 'border border-success text-success hover:bg-success/10'}`}
                                                >
                                                    {isCurrentlyEnabled ? 'Выключить' : 'Включить'}
                                                </button>
                                            )}
                                        </div>
                                        <span className="rounded bg-bg-accent px-2 py-0.5 text-xs text-accent">{e.costCoins}</span>
                                    </div>
                                    {e.description && <p className="mt-1 text-sm text-fg-muted">{e.description}</p>}
                                </div>
                            );
                        })}
                    </div>
                )}
            </section>

            <section>
                <h2 className="mb-2 text-lg font-semibold text-fg">{t('games.stats')}</h2>
                <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                    <StatsTable title={t('games.personal')} s={data.personal} />
                    <StatsTable title={t('games.global')} s={data.global} />
                </div>
            </section>

            {isAdmin && (
                <GameEditorModal
                    isOpen={isModalOpen}
                    onClose={() => setModalOpen(false)}
                    gameToEdit={data}
                    onSuccess={() => refetch()}
                />
            )}
        </div>
    );
}