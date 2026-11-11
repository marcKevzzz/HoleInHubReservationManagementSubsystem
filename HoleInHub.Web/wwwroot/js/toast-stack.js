(() => {
    const visibleDuration = 5000;

    function createDismissTimer(dismiss, duration = visibleDuration, clock = {
        now: () => performance.now(), schedule: (callback, delay) => setTimeout(callback, delay), cancel: id => clearTimeout(id)
    }) {
        let remaining = duration;
        let started, pending;
        return {
            resume() {
                if (pending !== undefined) return;
                started = clock.now();
                pending = clock.schedule(dismiss, remaining);
            },
            pause() {
                if (pending === undefined) return;
                clock.cancel(pending);
                pending = undefined;
                remaining = Math.max(0, remaining - (clock.now() - started));
            }
        };
    }

    if (typeof module !== 'undefined' && module.exports) module.exports = { createDismissTimer };
    if (typeof document === 'undefined') return;
    const host = document.querySelector('[data-toast-stack]');
    if (!host) return;
    const cards = host.querySelector('[data-toast-cards]');
    const toggle = host.querySelector('[data-toast-toggle]');
    const count = host.querySelector('[data-toast-count]');
    const status = host.querySelector('[data-toast-status]');
    const alert = host.querySelector('[data-toast-alert]');
    const items = [];
    let expanded = false, pinned = false, hovered = false, priorFocus;
    let hold, heldButton;

    function syncTimers() {
        const paused = hovered || expanded || hold || host.contains(document.activeElement) || document.hidden;
        items.forEach((item, index) => {
            if (paused || index > 0) item.timer?.pause();
            else item.timer?.resume();
        });
    }

    function render() {
        if (items.length < 2) expanded = pinned = false;
        host.classList.toggle('is-expanded', expanded);
        toggle.hidden = items.length < 2;
        toggle.setAttribute('aria-expanded', String(expanded));
        toggle.setAttribute('aria-label', expanded ? 'Collapse notifications' : 'Show ' + (items.length - 1) + ' older notifications');
        count.textContent = expanded ? 'Collapse' : '+' + (items.length - 1);
        items.forEach((item, index) => {
            item.card.style.setProperty('--toast-depth', index);
            item.card.inert = !expanded && index > 0;
            item.card.hidden = !expanded && index > 2;
        });
        syncTimers();
    }

    function dismiss(item) {
        const index = items.indexOf(item);
        if (index < 0) return;
        const focused = item.card.contains(document.activeElement) ||
            (items.length === 2 && document.activeElement === toggle);
        item.timer?.pause();
        items.splice(index, 1);
        item.card.remove();
        render();
        if (focused) {
            const next = items[expanded ? Math.min(index, items.length - 1) : 0]?.button;
            (next || (priorFocus?.isConnected ? priorFocus : document.querySelector('main[tabindex]')))?.focus({ preventScroll: true });
        }
    }

    function localAction(href) {
        if (typeof href !== 'string' || !href.startsWith('/') || href.startsWith('//')) return null;
        try {
            const url = new URL(href, location.origin);
            return url.origin === location.origin ? url : null;
        } catch {
            return null;
        }
    }

    function show({ kind = 'info', title, message, href, action, autoDismiss } = {}) {
        if (!title) return false;
        if (!['success', 'info', 'warning', 'error'].includes(kind)) kind = 'info';
        const card = document.createElement('article');
        card.className = 'toast-card';
        card.dataset.kind = kind;
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'toast-dismiss';
        button.setAttribute('aria-label', 'Dismiss notification: ' + title);
        const icon = host.querySelector('[data-toast-icon="' + (kind === 'error' ? 'warning' : kind) + '"]');
        button.append(icon.content.cloneNode(true));
        const copy = document.createElement('span');
        const headline = document.createElement('strong');
        headline.textContent = title;
        copy.append(headline);
        if (message) {
            const detail = document.createElement('span');
            detail.className = 'toast-message';
            detail.textContent = message;
            copy.append(detail);
        }
        button.append(copy);
        card.append(button);
        const url = action ? localAction(href) : null;
        if (url) {
            const link = document.createElement('a');
            link.className = 'toast-action';
            link.href = url;
            link.setAttribute('aria-label', action);
            link.title = action;
            link.append(host.querySelector('[data-toast-action-icon]').content.cloneNode(true));
            card.append(link);
        }

        const item = { card, button };
        if (!url && (autoDismiss ?? (kind === 'success' || kind === 'info'))) {
            item.timer = createDismissTimer(() => dismiss(item));
        }
        button.addEventListener('click', () => {
            if (heldButton === button) {
                heldButton = undefined;
                return;
            }
            dismiss(item);
        });
        items.unshift(item);
        cards.prepend(card);
        if (hovered && items.length > 1) expanded = true;
        if (host.contains(document.activeElement) && items.length > 1) expanded = true;
        render();
        const region = kind === 'error' ? alert : status;
        region.textContent = [title, message].filter(Boolean).join('. ');
        return true;
    }

    function collapse() {
        const olderFocused = items.slice(1).some(item => item.card.contains(document.activeElement));
        pinned = expanded = false;
        render();
        if (olderFocused) toggle.focus({ preventScroll: true });
    }

    host.addEventListener('pointerenter', event => {
        if (event.pointerType !== 'mouse') return;
        hovered = true;
        if (items.length > 1) expanded = true;
        render();
    });
    host.addEventListener('pointerleave', event => {
        if (event.pointerType !== 'mouse') return;
        hovered = false;
        if (!pinned && !host.contains(document.activeElement)) expanded = false;
        render();
    });
    host.addEventListener('focusin', event => {
        if (event.relatedTarget && !host.contains(event.relatedTarget)) priorFocus = event.relatedTarget;
        syncTimers();
    });
    host.addEventListener('focusout', () => queueMicrotask(() => {
        if (!hovered && !pinned && !host.contains(document.activeElement)) expanded = false;
        render();
    }));
    toggle.addEventListener('click', event => {
        if (expanded) collapse();
        else {
            expanded = pinned = true;
            render();
            if (event.detail === 0) items[1]?.button.focus({ preventScroll: true });
        }
    });
    host.addEventListener('keydown', event => {
        if (event.key !== 'Escape' || !expanded) return;
        event.preventDefault();
        collapse();
    });
    document.addEventListener('pointerdown', event => {
        clearHold();
        heldButton = undefined;
        if (!host.contains(event.target)) {
            if (expanded) collapse();
            return;
        }
        if (event.pointerType !== 'touch' || expanded || items.length < 2) return;
        const button = event.target.closest('.toast-dismiss');
        if (!button) return;
        const x = event.clientX, y = event.clientY;
        hold = { x, y, timer: setTimeout(() => {
            heldButton = button;
            expanded = pinned = true;
            render();
        }, 450) };
        syncTimers();
    });
    function clearHold() {
        if (!hold) return;
        clearTimeout(hold.timer);
        hold = undefined;
        syncTimers();
    }
    document.addEventListener('pointermove', event => {
        if (hold && Math.hypot(event.clientX - hold.x, event.clientY - hold.y) > 10) clearHold();
    });
    document.addEventListener('pointerup', clearHold);
    document.addEventListener('pointercancel', clearHold);
    host.addEventListener('contextmenu', event => {
        if (hold || heldButton) event.preventDefault();
    });
    document.addEventListener('visibilitychange', syncTimers);

    window.HoleInHubToasts = { show };
    host.setAttribute('data-toast-enhanced', '');
    host.hidden = false;
    const seed = host.querySelector('[data-toast-seed]');
    if (seed) {
        const link = seed.querySelector('[data-toast-action]');
        show({ kind: seed.dataset.kind, title: seed.querySelector('[data-toast-title]').textContent,
            message: seed.querySelector('[data-toast-message]')?.textContent, href: link?.getAttribute('href'), action: link?.getAttribute('aria-label') });
        seed.remove();
    }
})();
