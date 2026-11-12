(() => {
    const page = document.querySelector('[data-activity-page]');
    if (!page) return;
    const panel = page.querySelector('[data-activity-panel]');
    if (typeof panel.showModal !== 'function') return;
    const form = page.querySelector('[data-activity-filters]');
    const dates = page.querySelector('[data-activity-dates]');
    const body = page.querySelector('[data-activity-panel-body]');
    const status = page.querySelector('[data-activity-status]');
    const categories = page.querySelector('.activity-categories');
    const reducedMotion = matchMedia('(prefers-reduced-motion: reduce)');
    const results = () => page.querySelector('[data-activity-results]');
    let listUrl = page.dataset.activityReturnUrl || results().dataset.listUrl;
    let listRequest, detailRequest, debounce, trigger;

    function remember(kind, url, value) {
        try { sessionStorage.setItem('activity:' + kind + ':' + url, JSON.stringify(value)); } catch {}
    }
    function recall(kind, url) {
        try { return JSON.parse(sessionStorage.getItem('activity:' + kind + ':' + url)); } catch { return null; }
    }
    function announce(message) {
        status.textContent = message;
        status.hidden = !message;
    }
    function savePosition() {
        remember('position', listUrl, { y: scrollY, key: trigger?.closest('[data-event-key]')?.dataset.eventKey });
    }
    function restorePosition(eventKey) {
        const position = recall('position', listUrl);
        if (position) window.scrollTo(0, position.y);
        const key = eventKey || position?.key;
        if (!key) return;
        const row = [...page.querySelectorAll('[data-event-key]')].find(el => el.dataset.eventKey === key);
        row?.scrollIntoView({ block: 'nearest' });
        row?.focus({ preventScroll: true });
    }
    function unpaged(value) {
        const url = new URL(value, location.origin);
        url.searchParams.delete('cursor');
        url.searchParams.delete('asOf');
        return url;
    }
    function showDates() {
        const custom = form.elements.range.value === 'custom';
        dates.hidden = !custom;
        for (const input of dates.querySelectorAll('input')) input.required = custom;
    }
    function syncFilters() {
        if (!form) return;
        const url = new URL(listUrl, location.origin);
        page.querySelector('.staff-workspace-heading [data-activity-refresh]').href = unpaged(url);
        for (const name of ['search', 'range', 'from', 'to', 'actor', 'category']) {
            const value = url.searchParams.get(name) ?? '';
            const field = form.elements.namedItem(name);
            if (name === 'actor' && value && ![...field.options].some(option => option.value === value)) {
                field.add(new Option(recall('actor', value) ?? 'Selected employee', value));
            }
            field.value = value;
        }
        showDates();
        for (const link of page.querySelectorAll('[data-activity-filter]')) {
            const category = new URL(link.href).searchParams.get('category');
            const target = unpaged(url);
            target.searchParams.set('category', category);
            link.href = target;
            if (category === form.elements.category.value) link.setAttribute('aria-current', 'page');
            else link.removeAttribute('aria-current');
        }
        const link = page.querySelector('[data-activity-previous]');
        const previous = recall('previous', listUrl);
        if (link && previous) {
            link.href = previous;
            link.textContent = 'Previous';
        }
    }
    async function fragment(url, kind, selector, request) {
        const timeout = setTimeout(() => request.abort(), 15000);
        try {
            const response = await fetch(url, { headers: { 'X-Activity': kind }, signal: request.signal, redirect: 'error', cache: 'no-store' });
            if (!response.ok && !(kind === 'list' && response.status === 400)) throw new Error();
            const markup = new DOMParser().parseFromString(await response.text(), 'text/html').querySelector(selector);
            if (!markup) throw new Error();
            return markup;
        } finally {
            clearTimeout(timeout);
        }
    }
    function setListPending(pending, tab) {
        if (pending) results().setAttribute('aria-busy', 'true');
        else results().removeAttribute('aria-busy');
        categories?.classList.toggle('is-loading', pending);
        categories?.querySelector('[data-activity-pending]')?.removeAttribute('data-activity-pending');
        tab?.setAttribute('data-activity-pending', '');
    }
    function cancelList() {
        listRequest?.abort();
        listRequest = undefined;
        setListPending(false);
    }
    async function loadList(url, { push = true, next = false, tab } = {}) {
        clearTimeout(debounce);
        savePosition();
        listRequest?.abort();
        const request = listRequest = new AbortController();
        announce('');
        setListPending(true, tab);
        try {
            const updated = await fragment(url, 'list', '[data-activity-results]', request);
            if (request !== listRequest) return;
            if (next) remember('previous', updated.dataset.listUrl, listUrl);
            results().replaceWith(updated);
            if (!reducedMotion.matches) updated.animate([{ opacity: .76 }, { opacity: 1 }], { duration: 150, easing: 'ease-out' });
            listUrl = updated.dataset.listUrl;
            syncFilters();
            if (push) history.pushState({ activityList: true }, '', listUrl);
            else restorePosition();
            announce('');
        } catch {
            if (request === listRequest) announce('Could not load activity. Previous results remain. Use Refresh to retry.');
        } finally {
            if (request === listRequest) {
                listRequest = undefined;
                setListPending(false);
            }
        }
    }
    async function openDetail(url, push = true) {
        savePosition();
        clearTimeout(debounce);
        cancelList();
        announce('');
        detailRequest?.abort();
        const request = detailRequest = new AbortController();
        const message = document.createElement('p');
        message.className = 'activity-panel-feedback';
        message.setAttribute('aria-label', 'Loading event');
        message.classList.add('activity-loading');
        message.setAttribute('role', 'status');
        message.textContent = 'Loading activity…';
        const skeleton = document.createElement('div');
        skeleton.className = 'activity-panel-skeleton';
        skeleton.setAttribute('aria-hidden', 'true');
        for (let index = 0; index < 5; index++) skeleton.append(document.createElement('span'));
        body.replaceChildren(message, skeleton);
        body.scrollTop = 0;
        if (!panel.open) panel.showModal();
        body.setAttribute('aria-busy', 'true');
        document.documentElement.classList.add('activity-panel-open');
        if (push) history.pushState({ activityDetail: true }, '', url);
        try {
            const content = await fragment(url, 'panel', '[data-activity-event-content]', request);
            if (request !== detailRequest || !panel.open) return;
            body.replaceChildren(content);
            content.querySelector('h2')?.focus();
            if (!reducedMotion.matches) {
                content.animate([{ opacity: .6, transform: 'translateX(8px)' }, { opacity: 1, transform: 'translateX(0)' }], { duration: 180, easing: 'ease-out' });
            }
        } catch {
            if (request !== detailRequest || !panel.open) return;
            message.classList.remove('activity-loading');
            message.removeAttribute('aria-label');
            skeleton.remove();
            message.textContent = 'Activity unavailable or your session expired. ';
            const link = document.createElement('a');
            link.href = url;
            link.textContent = 'Open full page';
            message.append(link);
        } finally {
            if (request === detailRequest) body.removeAttribute('aria-busy');
        }
    }
    function closePanel() {
        detailRequest?.abort();
        detailRequest = undefined;
        panel.close();
        body.removeAttribute('aria-busy');
        document.documentElement.classList.remove('activity-panel-open');
        restorePosition();
        if (trigger?.isConnected) trigger.focus({ preventScroll: true });
    }
    function dismiss() {
        if (history.state?.activityDetail) history.back();
        else closePanel();
    }
    page.addEventListener('click', event => {
        if (event.defaultPrevented || event.button || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        const link = event.target.closest('a');
        if (link?.matches('[data-activity-filter]')) {
            event.preventDefault();
            submit(link);
            return;
        }
        if (link?.matches('[data-activity-next],[data-activity-previous],[data-activity-refresh], .staff-empty-state a')) {
            event.preventDefault();
            void loadList(link.href, { next: link.hasAttribute('data-activity-next') });
            return;
        }
        const actor = event.target.closest('[data-activity-actor]');
        if (actor && form) {
            event.preventDefault();
            remember('actor', actor.dataset.activityActor, actor.textContent.trim());
            void loadList(actor.href);
            return;
        }
        if (event.target.closest('[data-activity-close]')) {
            dismiss();
            return;
        }
        const row = event.target.closest('[data-activity-row]');
        if (!row || (link && !link.matches('[data-activity-event]')) || window.getSelection()?.toString()) return;
        trigger = row.querySelector('[data-activity-event]');
        event.preventDefault();
        void openDetail(trigger.href);
    });
    function submit(tab) {
        clearTimeout(debounce);
        if (!form.reportValidity()) return;
        const url = new URL(form.action);
        url.search = new URLSearchParams(new FormData(form));
        if (tab) url.searchParams.set('category', new URL(tab.href).searchParams.get('category'));
        void loadList(url, { tab });
    }
    form?.addEventListener('submit', event => { event.preventDefault(); submit(); });
    function scheduleSearch(event) {
        if (event.target.type !== 'search' || event.isComposing) return;
        cancelList();
        clearTimeout(debounce);
        debounce = setTimeout(submit, 350);
    }
    form?.addEventListener('input', scheduleSearch);
    form?.addEventListener('compositionend', scheduleSearch);
    form?.addEventListener('keydown', event => {
        if (event.key !== 'Enter' || event.isComposing || event.target.type !== 'search') return;
        event.preventDefault();
        submit();
    });
    form?.addEventListener('change', event => {
        if (event.target.type === 'search') return;
        showDates();
        if (event.target.name !== 'range' || form.elements.range.value !== 'custom') submit();
    });
    panel.addEventListener('cancel', event => { event.preventDefault(); dismiss(); });
    let backdropPressed = false;
    function outsidePanel(event) {
        const rect = panel.getBoundingClientRect();
        return event.clientX < rect.left || event.clientX > rect.right || event.clientY < rect.top || event.clientY > rect.bottom;
    }
    panel.addEventListener('pointerdown', event => { backdropPressed = event.target === panel && outsidePanel(event); });
    panel.addEventListener('click', event => {
        if (backdropPressed && event.target === panel && outsidePanel(event)) dismiss();
        backdropPressed = false;
    });
    window.addEventListener('popstate', () => {
        if (location.pathname === new URL(listUrl, location.origin).pathname) {
            if (panel.open) closePanel();
            if (location.pathname + location.search !== listUrl) void loadList(location.href, { push: false });
            else restorePosition();
        } else {
            void openDetail(location.href, false);
        }
    });
    page.addEventListener('keydown', event => {
        if (event.defaultPrevented || event.key !== 'Enter' || !event.target.matches('[data-activity-row]')) return;
        event.preventDefault();
        trigger = event.target.querySelector('[data-activity-event]');
        void openDetail(trigger.href);
    });
    const returnEvent = new URL(location.href).searchParams.get('focus');
    history.replaceState({ activityList: true }, '', listUrl);
    syncFilters();
    if (returnEvent) restorePosition(returnEvent);
})();
