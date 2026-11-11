(() => {
    function createScheduleCache(read, now = Date.now) {
        const days = new Map();
        return function load(date) {
            const cached = days.get(date);
            if (cached && now() - cached.time < 15000) return cached.promise;
            const entry = { time: now() };
            entry.promise = Promise.resolve().then(() => read(date)).catch(error => {
                if (days.get(date) === entry) days.delete(date);
                throw error;
            });
            days.set(date, entry);
            while (days.size > 5) days.delete(days.keys().next().value);
            return entry.promise;
        };
    }
    if (typeof module !== 'undefined' && module.exports) module.exports = createScheduleCache;
    if (typeof document === 'undefined' || !document.querySelector('[data-staff-schedule]')) return;
    const route = window.location.pathname;
    const customerProfileId = document.querySelector('[data-staff-schedule]').dataset.customerProfileId;
    function dayUrl(date) {
        const url = new URL(route, window.location.origin);
        url.searchParams.set('date', date);
        if (customerProfileId) url.searchParams.set('customerProfileId', customerProfileId);
        return url;
    }
    let requestId = 0, disposeButtons;
    const read = createScheduleCache(async date => {
        const url = dayUrl(date);
        const response = await fetch(url, { headers: { 'X-Staff-Schedule': 'true' }, cache: 'no-store', redirect: 'error' });
        if (!response.ok) throw new Error('Couldn’t load that day. Please try again.');
        return response.text();
    });
    async function load(date, signal) {
        const html = await read(date);
        if (signal?.aborted) throw new DOMException('Cancelled', 'AbortError');
        const page = new DOMParser().parseFromString(html, 'text/html').querySelector('[data-staff-schedule]');
        if (page?.dataset.date !== date || !page.querySelector('.staff-timeline')) throw new Error('Schedule unavailable. Please reload and sign in again.');
        return page;
    }
    function prefetch() {
        const page = document.querySelector('[data-staff-schedule]');
        for (const direction of [-1, 1]) {
            const day = page.timelineGesture.adjacentDay(page.dataset.date, direction);
            void load(day).catch(() => {});
        }
    }
    async function go(date, push = true) {
        const page = document.querySelector('[data-staff-schedule]');
        if (!/^\d{4}-\d{2}-\d{2}$/.test(date) || page.dataset.moving || page.querySelector('dialog[open]')) return;
        const id = ++requestId;
        page.setAttribute('aria-busy', 'true');
        page.timelineGesture.preview(date, 'Loading schedule');
        const focused = document.activeElement;
        const focusLabel = focused?.getAttribute('aria-label');
        try {
            const incoming = await load(date);
            if (id !== requestId || !page.isConnected) return;
            if (page.dataset.moving || page.querySelector('dialog[open]')) {
                page.removeAttribute('aria-busy');
                page.timelineGesture.hide();
                return;
            }
            const direction = date < page.dataset.date ? -1 : 1;
            const view = page.dataset.activeView;
            const zoom = page.querySelector('.staff-timeline').style.getPropertyValue('--hour-width');
            const scroll = page.querySelector('.staff-timeline-scroll');
            page.dispatchEvent(new Event('staff:schedule-unmount'));
            disposeButtons?.();
            page.replaceWith(incoming);
            incoming.dataset.zoomBaseWidth = page.dataset.zoomBaseWidth;
            if (zoom) incoming.querySelector('.staff-timeline').style.setProperty('--hour-width', zoom);
            document.dispatchEvent(new Event('staff:schedule-mounted'));
            disposeButtons = window.HoleInHubButtons?.mount(incoming);
            if (view === 'list') incoming.querySelector('[data-schedule-view="list"]').click();
            incoming.querySelector('.staff-timeline-scroll').scrollTo({ left: scroll.scrollLeft, top: scroll.scrollTop, behavior: 'instant' });
            incoming.timelineGesture.transition(direction);
            const url = dayUrl(date);
            if (push) window.history.pushState(null, '', url);
            for (const link of document.querySelectorAll('aside a[href]')) {
                const target = new URL(link.href);
                if (target.origin === window.location.origin && target.searchParams.has('date')) {
                    target.searchParams.set('date', date);
                    link.href = target;
                }
            }
            if (focusLabel && focused !== document.body) {
                [...incoming.querySelectorAll('[aria-label]')].find(item => item.getAttribute('aria-label') === focusLabel)?.focus({ preventScroll: true });
            }
            prefetch();
        } catch (error) {
            if (id !== requestId) return;
            page.removeAttribute('aria-busy');
            page.timelineGesture.hide();
            const message = page.querySelector('[data-drag-error]');
            message.textContent = error.message;
            message.hidden = false;
        }
    }
    window.HoleInHubSchedule = { go, load };
    document.addEventListener('click', event => {
        const link = event.target.closest('[data-staff-schedule] .staff-date-controls nav a');
        if (!link || event.button || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        const date = new URL(link.href).searchParams.get('date');
        if (!date) return;
        event.preventDefault();
        void go(date);
    });
    document.addEventListener('submit', event => {
        if (!event.target.matches('[data-staff-schedule] [data-staff-date-filter]')) return;
        event.preventDefault();
        void go(event.target.elements.date.value);
    }, true);
    window.addEventListener('popstate', () => {
        if (window.location.pathname !== route || (new URL(window.location.href).searchParams.get('customerProfileId') || '') !== (customerProfileId || '')) { window.location.reload(); return; }
        const date = new URL(window.location.href).searchParams.get('date');
        if (date) void go(date, false);
        else window.location.reload();
    });
    window.addEventListener('pagehide', () => disposeButtons?.());
    prefetch();
})();
