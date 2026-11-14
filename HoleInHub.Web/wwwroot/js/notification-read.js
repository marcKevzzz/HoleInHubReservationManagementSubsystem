(() => {
    const form = document.querySelector('[data-read-visible]');
    if (!form || !window.IntersectionObserver) return;

    const token = form.elements.__RequestVerificationToken.value;
    const visible = new Set();
    const pending = new Set();
    let timer;
    let sending = false;

    const observer = new IntersectionObserver(entries => {
        for (const { target, intersectionRatio } of entries) {
            if (intersectionRatio >= .5) visible.add(target);
            else visible.delete(target);
        }
        queueVisible();
    }, { threshold: .5 });

    for (const row of document.querySelectorAll('[data-unread-id]')) observer.observe(row);

    function queueVisible() {
        if (document.hidden) return;
        for (const row of visible) pending.add(row);
        if (pending.size && !sending && !timer) timer = setTimeout(save, 180);
    }

    async function save() {
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
                observer.unobserve(row);
                visible.delete(row);
                pending.delete(row);
                row.querySelector('.notification-new')?.remove();
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
