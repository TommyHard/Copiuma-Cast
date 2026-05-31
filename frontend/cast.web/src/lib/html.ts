// Минимальная безопасная разметка для описаний: экранируем весь HTML, затем
// возвращаем небольшой белый список тегов форматирования (u, b, i, em, strong,
// br). Так <u>важное</u> отображается подчёркнутым, а любой другой HTML
// (скрипты, ссылки, атрибуты) остаётся экранированным — без риска XSS
const ALLOWED = /&lt;(\/?(?:u|b|i|em|strong|br))\s*\/?&gt;/gi;

export function basicHtml(text: string | null | undefined): string {
    if (!text) return '';
    const escaped = text
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;');
    return escaped.replace(ALLOWED, (_m, tag: string) => `<${tag}>`);
}