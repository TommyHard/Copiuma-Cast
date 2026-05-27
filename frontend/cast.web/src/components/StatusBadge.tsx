import { useTranslation } from 'react-i18next';
import type { UserCard } from '@/lib/types';

const DOT: Record<string, string> = {
    Online: 'bg-success', Away: 'bg-yellow-400', DoNotDisturb: 'bg-danger', Offline: 'bg-fg-muted',
};

export function StatusText({ card }: { card: UserCard }) {
    const { t } = useTranslation();
    let text = t(`status.${card.status}`);
    if (card.activity === 'Watching' && card.activityTarget)
        text = t('status.watching', { name: card.activityTarget });
    else if (card.activity === 'Playing' && card.activityTarget)
        text = t('status.playing', { name: card.activityTarget });
    return (
        <span className="flex items-center gap-1.5 text-xs text-fg-muted">
            <span className={`inline-block h-2 w-2 rounded-full ${DOT[card.status] ?? 'bg-fg-muted'}`} />
            {text}
        </span>
    );
}