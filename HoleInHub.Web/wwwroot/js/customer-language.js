(function(root) {
    const messages = root.HoleInHubLanguage || {};
    const t = text => Object.hasOwn(messages, text) ? messages[text] : text;
    const f = (text, ...args) => t(text).replace(/\{(\d+)\}/g, (_, index) => String(args[index] ?? ''));
    root.HoleInHubText = { t, f, locale: typeof document !== 'undefined' && document.documentElement.lang === 'ko' ? 'ko-KR' : 'en-PH' };
    if (typeof module !== 'undefined') module.exports = root.HoleInHubText;
})(typeof window !== 'undefined' ? window : globalThis);
