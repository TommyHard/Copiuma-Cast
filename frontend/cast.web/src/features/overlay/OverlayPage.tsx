import { useEffect, useState } from 'react';

declare global {
    interface Window {
        // Состояние оверлея, которое нативный CEF-хост выставляет через JS
        __castOverlayOpen?: boolean;
    }
}

const ACCENT = 'rgb(155 223 30)'; // акцент платформы (#9BDF1E)

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
                    className="flex items-center gap-2 rounded-full px-4 py-2 text-sm text-white shadow-lg"
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
                    className="relative flex h-[80vh] w-[70vw] max-w-5xl flex-col overflow-hidden rounded-2xl text-white"
                    style={{
                        background: 'rgba(20,20,24,0.84)',
                        border: '1px solid rgba(255,255,255,0.10)',
                        boxShadow: '0 24px 80px rgba(0,0,0,0.55)',
                    }}
                >
                    <header
                        className="flex items-center justify-between px-6 py-4"
                        style={{ borderBottom: '1px solid rgba(255,255,255,0.10)' }}
                    >
                        <div className="flex items-center gap-2">
                            <span className="h-2.5 w-2.5 rounded-full" style={{ background: ACCENT }} />
                            <h1 className="text-lg font-semibold">Cast Overlay</h1>
                        </div>
                        <span className="text-xs" style={{ color: 'rgba(255,255,255,0.5)' }}>
                            F8 — закрыть
                        </span>
                    </header>

                    <main className="flex-1 overflow-auto p-6">
                        {/* TODO: наполнение оверлея — виджеты, чат, друзья, ставки и т.д. */}
                        <p style={{ color: 'rgba(255,255,255,0.7)' }}>
                            Внутриигровой оверлей Cast. Здесь будет интерфейс.
                        </p>
                    </main>
                </div>
            </div>
        </div>
    );
}