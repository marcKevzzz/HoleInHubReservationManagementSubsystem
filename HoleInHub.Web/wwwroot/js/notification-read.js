(() => {
    const form = document.querySelector('[data-read-visible]');
    if (!form) return;

    const token = form.elements.__RequestVerificationToken.value;
    const visible = new Set();
    const pending = new Set();
    const readingTimers = new Map();
    let timer;
    let sending = false;

    const observer = window.IntersectionObserver ? new IntersectionObserver(entries => {
        for (const { target, intersectionRatio } of entries) {
            if (intersectionRatio > 0) visible.add(target);
            else visible.delete(target);
        }
        queueVisible();
    }, { threshold: [0, .01] }) : null;

    const unreadRow = '[data-unread-id]:not([data-unread-id=""])';
    for (const row of document.querySelectorAll(unreadRow)) observer?.observe(row);
    for (const type of ['click', 'focusin']) document.addEventListener(type, event => {
        const row = event.target.closest(unreadRow);
        if (!row || document.hidden) return;
        pending.add(row);
        if (!sending) void save();
    });

    function queueVisible() {
        for (const [row, readingTimer] of readingTimers) {
            if (document.hidden || !visible.has(row)) {
                clearTimeout(readingTimer);
                readingTimers.delete(row);
            }
        }
        if (document.hidden) return;
        for (const row of visible) {
            if (readingTimers.has(row) || pending.has(row)) continue;
            readingTimers.set(row, setTimeout(() => {
                readingTimers.delete(row);
                if (document.hidden || !visible.has(row) || !row.dataset.unreadId) return;
                pending.add(row);
                if (!sending) void save();
            }, 5000));
        }
        if (pending.size && !sending && !timer) void save();
    }

    async function save() {
        clearTimeout(timer);
        timer = undefined;
        if (document.hidden || !pending.size) return;

        const rows = [...pending];
        pending.clear();
        const body = new URLSearchParams({ __RequestVerificationToken: token });
        rows.forEach((row, index) => {
            body.set(`rows[${index}].Id`, row.dataset.unreadId);
            body.set(`rows[${index}].ActivityTicks`, row.dataset.activityTicks);
        });
        sending = true;
        try {
            const response = await fetch(form.action, {
                method: 'POST', body, keepalive: true, redirect: 'error', signal: AbortSignal.timeout(8000)
            });
            if (!response.ok) throw new Error('Could not save read notifications');
            const { unread } = await response.json();
            for (const row of rows) {
                observer?.unobserve(row);
                visible.delete(row);
                pending.delete(row);
                clearTimeout(readingTimers.get(row));
                readingTimers.delete(row);
                row.querySelector('.notification-new')?.remove();
                row.removeAttribute('data-unread-id');
            }
            document.dispatchEvent(new CustomEvent('notifications:read', { detail: unread }));
        } catch {
            for (const row of rows) pending.add(row);
            timer = setTimeout(save, 5000);
        } finally {
            sending = false;
            queueVisible();
        }
    }

    document.addEventListener('visibilitychange', queueVisible);
})();
