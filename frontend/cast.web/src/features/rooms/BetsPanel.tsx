import { useState, type FormEvent } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { api } from '@/lib/api';
import type { Bet } from '@/lib/types';

export function BetsPanel({ roomId, isStreamer, onBalance }: { roomId: string; isStreamer: boolean; onBalance: (b: number) => void }) {
    const { t } = useTranslation();
    const qc = useQueryClient();
    const { data } = useQuery({
        queryKey: ['bets', roomId],
        queryFn: async () => (await api.get<Bet[]>(`/rooms/${roomId}/bets`)).data,
    });

    const [title, setTitle] = useState('');
    const [outcomes, setOutcomes] = useState('');
    const [duration, setDuration] = useState(60);
    const [amounts, setAmounts] = useState<Record<string, number>>({});

    const refetch = () => qc.invalidateQueries({ queryKey: ['bets', roomId] });

    async function createBet(e: FormEvent) {
        e.preventDefault();
        const list = outcomes.split(',').map((s) => s.trim()).filter(Boolean);
        if (!title || list.length < 2) return;
        await api.post(`/rooms/${roomId}/bets`, { title, outcomes: list, locksInSeconds: duration });
        setTitle(''); setOutcomes(''); refetch();
    }

    async function place(betId: string, outcomeId: string) {
        const amount = amounts[outcomeId] || 0;
        if (amount <= 0) return;
        const res = await api.post<{ balance: number }>(`/rooms/${roomId}/bets/${betId}/wagers`, { outcomeId, amount });
        onBalance(res.data.balance);
        refetch();
    }

    const resolve = async (betId: string, winningOutcomeId: string) => { await api.post(`/rooms/${roomId}/bets/${betId}/resolve`, { winningOutcomeId }); refetch(); };
    const cancel = async (betId: string) => { await api.post(`/rooms/${roomId}/bets/${betId}/cancel`); refetch(); };

    const input = 'rounded-md border border-border bg-bg px-2 py-1 text-sm text-fg outline-none focus:border-accent';

    return (
        <div className="space-y-3">
            <h2 className="text-lg font-semibold text-fg">{t('bets.title')}</h2>

            {isStreamer && (
                <form onSubmit={createBet} className="space-y-2 rounded-lg border border-border bg-bg-elevated p-3">
                    <input className={`${input} w-full`} placeholder={t('bets.question')} value={title} onChange={(e) => setTitle(e.target.value)} />
                    <input className={`${input} w-full`} placeholder={t('bets.outcomes')} value={outcomes} onChange={(e) => setOutcomes(e.target.value)} />
                    <div className="flex items-center gap-2">
                        <input className={`${input} w-24`} type="number" min={5} value={duration} onChange={(e) => setDuration(+e.target.value)} />
                        <span className="text-xs text-fg-muted">{t('bets.duration')}</span>
                        <button type="submit" className="ml-auto rounded-md bg-accent px-3 py-1.5 text-sm font-medium text-accent-fg hover:opacity-90">{t('bets.create')}</button>
                    </div>
                </form>
            )}

            {(data ?? []).map((bet) => {
                const locked = bet.status !== 'Open' || new Date(bet.locksAt) <= new Date();
                return (
                    <div key={bet.id} className="rounded-lg border border-border bg-bg-elevated p-3">
                        <div className="flex items-center justify-between">
                            <span className="font-medium text-fg">{bet.title}</span>
                            <span className="text-xs text-fg-muted">
                                {bet.status === 'Open' ? (locked ? t('bets.locked') : t('bets.open')) : t(`media.${bet.status}`)}
                            </span>
                        </div>
                        <div className="mt-2 space-y-1">
                            {bet.outcomes.map((o) => {
                                const win = bet.winningOutcomeId === o.id;
                                return (
                                    <div key={o.id} className={`flex items-center gap-2 rounded-md px-2 py-1 ${win ? 'bg-bg-accent' : ''}`}>
                                        <span className="text-sm text-fg">{o.label}</span>
                                        <span className="text-xs text-fg-muted">{t('bets.pool')}: {o.pool}{o.odds ? ` · ${t('bets.odds')} ${o.odds}` : ''}</span>
                                        <div className="ml-auto flex items-center gap-1">
                                            {bet.status === 'Open' && !locked && (
                                                <>
                                                    <input className={`${input} w-20`} type="number" min={1} placeholder={t('bets.amount')}
                                                        value={amounts[o.id] ?? ''} onChange={(e) => setAmounts((a) => ({ ...a, [o.id]: +e.target.value }))} />
                                                    <button onClick={() => place(bet.id, o.id)} className="rounded-md border border-border px-2 py-1 text-xs text-fg hover:border-accent">{t('bets.place')}</button>
                                                </>
                                            )}
                                            {isStreamer && bet.status === 'Open' && (
                                                <button onClick={() => resolve(bet.id, o.id)} className="rounded-md border border-border px-2 py-1 text-xs text-accent hover:border-accent">{t('bets.resolve')}</button>
                                            )}
                                        </div>
                                    </div>
                                );
                            })}
                        </div>
                        {isStreamer && bet.status === 'Open' && (
                            <button onClick={() => cancel(bet.id)} className="mt-2 text-xs text-danger hover:underline">{t('bets.cancel')}</button>
                        )}
                    </div>
                );
            })}
            {data && data.length === 0 && <p className="text-sm text-fg-muted">{t('bets.noBets')}</p>}
        </div>
    );
}