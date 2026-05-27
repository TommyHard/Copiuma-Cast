import { useTranslation } from 'react-i18next';
import { SUPPORTED_LANGUAGES } from '@/i18n';

export function LanguageSwitcher() {
    const { i18n } = useTranslation();
    return (
        <select
            aria-label="language"
            value={i18n.resolvedLanguage}
            onChange={(e) => i18n.changeLanguage(e.target.value)}
            className="rounded-md border border-border bg-bg-elevated px-2 py-1 text-sm text-fg"
        >
            {SUPPORTED_LANGUAGES.map((l) => (
                <option key={l} value={l}>{l.toUpperCase()}</option>
            ))}
        </select>
    );
}