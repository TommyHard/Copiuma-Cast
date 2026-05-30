import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '@/lib/api';

interface Props {
  value: string[];
  onChange: (tags: string[]) => void;
  placeholder?: string;
}

/**
 * Выбор тегов: поиск существующих (подсказки из /api/tags), добавление и
 * создание новых. Используется при загрузке, модерации и в фильтрах стримера
 */
export function TagInput({ value, onChange, placeholder }: Props) {
    const { t } = useTranslation();
    const [query, setQuery] = useState('');
    const [suggestions, setSuggestions] = useState<string[]>([]);
    const [open, setOpen] = useState(false);
    const boxRef = useRef<HTMLDivElement>(null);

    useEffect(() => {
        let active = true;
        const h = setTimeout(async () => {
            try {
                const res = await api.get<string[]>('/tags', { params: { query } });
                if (active) setSuggestions(res.data.filter((s) => !value.includes(s)));
            } catch { /* ignore */ }
        }, 200);
        return () => { active = false; clearTimeout(h); };
    }, [query, value]);

    useEffect(() => {
        const onDoc = (e: MouseEvent) => { if (!boxRef.current?.contains(e.target as Node)) setOpen(false); };
        document.addEventListener('mousedown', onDoc);
        return () => document.removeEventListener('mousedown', onDoc);
    }, []);

    const norm = (s: string) => s.trim().toLowerCase();

    function add(tag: string) {
        const n = norm(tag);
        if (!n || value.includes(n)) { setQuery(''); return; }
        onChange([...value, n]);
        setQuery('');
    }

    async function createAndAdd(tag: string) {
        const n = norm(tag);
        if (!n) return;
        if (!suggestions.includes(n)) {
            try { await api.post('/tags', { name: n }); } catch { /* ignore */ }
        }
        add(n);
    }

    const input = 'rounded-md border border-border bg-bg px-3 py-2 text-fg outline-none focus:border-accent';

    return (
        <div ref={boxRef} className="relative">
            <div className="mb-1 flex flex-wrap gap-1">
                {value.map((tag) => (
                    <span key={tag} className="flex items-center gap-1 rounded bg-bg-accent px-1.5 py-0.5 text-xs text-accent">
                        #{tag}
                        <button type="button" onClick={() => onChange(value.filter((x) => x !== tag))} className="text-fg-muted hover:text-danger">×</button>
                    </span>
                ))}
            </div>
            <input
                className={`${input} w-full`}
                value={query}
                placeholder={placeholder ?? t('media.tagSearch')}
                onChange={(e) => { setQuery(e.target.value); setOpen(true); }}
                onFocus={() => setOpen(true)}
                onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); void createAndAdd(query); } }}
            />
            {open && (suggestions.length > 0 || query.trim()) && (
                <div className="absolute z-10 mt-1 max-h-48 w-full overflow-auto rounded-md border border-border bg-bg-elevated p-1 shadow-lg">
                    {suggestions.map((s) => (
                        <button type="button" key={s} onClick={() => add(s)}
                            className="block w-full rounded px-2 py-1 text-left text-sm text-fg hover:bg-bg-accent">#{s}</button>
                    ))}
                    {query.trim() && !suggestions.includes(norm(query)) && !value.includes(norm(query)) && (
                        <button type="button" onClick={() => void createAndAdd(query)}
                            className="block w-full rounded px-2 py-1 text-left text-sm text-accent hover:bg-bg-accent">
                            + {t('media.addTag')}: #{norm(query)}
                        </button>
                    )}
                </div>
            )}
        </div>
    );
}