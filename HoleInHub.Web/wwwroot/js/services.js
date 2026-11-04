(() => {
    const page = document.querySelector('[data-services-page]');
    if (!page) return;
    const catalog = page.querySelector('[data-services-catalog]');
    const status = page.querySelector('[data-services-status]');
    let refreshing = false;
    let lastHtml = null;
    let disposeButtons = () => {};
    async function refresh() {
        if (document.hidden || refreshing || catalog.contains(document.activeElement)) return;
        refreshing = true;
        const controller = new AbortController();
        const timeout = setTimeout(() => controller.abort(), 10000);
        try {
            const response = await fetch(page.dataset.refreshUrl, { cache: 'no-store', signal: controller.signal });
            if (!response.ok) throw new Error('Catalog unavailable');
            const html = await response.text();
            if (document.hidden || catalog.contains(document.activeElement)) return;
            if (lastHtml !== html) {
                disposeButtons();
                catalog.innerHTML = html;
                lastHtml = html;
                disposeButtons = window.HoleInHubButtons?.mount(catalog) || (() => {});
                window.ScrollTrigger?.refresh();
            }
            status.textContent = 'Current offerings from the venue.';
        } catch {
            status.textContent = 'We couldn’t refresh services. Please check rates with the venue; we’ll try again shortly.';
        } finally {
            clearTimeout(timeout);
            refreshing = false;
        }
    }
    setInterval(refresh, 45000);
    document.addEventListener('visibilitychange', () => { if (!document.hidden) refresh(); });
})();
