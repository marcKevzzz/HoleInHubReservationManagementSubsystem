(() => {
    const hub = document.querySelector('.account-hub');
    if (!hub) return;
    let saving = false;
    document.addEventListener('submit', async event => {
        const form = event.target;
        if (event.defaultPrevented || !form.matches('.account-inbox-action')) return;
        event.preventDefault();
        if (saving) return;
        saving = true;
        const buttons = [...form.querySelectorAll('button')];
        const body = new FormData(form);
        if (event.submitter?.name) body.set(event.submitter.name, event.submitter.value);
        buttons.forEach(button => button.disabled = true);
        const feedback = hub.querySelector('[data-inbox-feedback]') ?? document.createElement('p');
        feedback.dataset.inboxFeedback = '';
        feedback.setAttribute('role', 'status');
        feedback.textContent = '';
        try {
            const response = await fetch(form.action, { method: 'POST', body, signal: AbortSignal.timeout(15000) });
            if (!response.ok || new URL(response.url).pathname !== '/notifications') throw new Error();
            const next = new DOMParser().parseFromString(await response.text(), 'text/html').querySelector('.account-hub');
            const content = '.account-inbox, .account-inbox-empty, .account-inbox-pager';
            if (!next?.querySelector(content)) throw new Error();
            hub.querySelectorAll(content).forEach(node => node.remove());
            next.querySelectorAll(content).forEach(node => hub.append(node));
            hub.querySelectorAll('.account-inbox-options[open]').forEach(menu => menu.open = false);
            const focus = hub.querySelector('.account-inbox-options summary, .account-inbox-empty h2');
            if (focus) {
                if (focus.tagName !== 'SUMMARY') focus.tabIndex = -1;
                focus.focus({ preventScroll: true });
            }
            window.ScrollTrigger?.refresh();
            // Read counts include notifications on other pages too.
            try {
                const badge = await fetch('/notifications/unread', { signal: AbortSignal.timeout(8000) });
                if (badge.ok) document.dispatchEvent(new CustomEvent('notifications:read', { detail: (await badge.json()).unread }));
            } catch { /* The action succeeded; badge polling can catch up later. */ }
        } catch {
            feedback.textContent = document.documentElement.lang === 'ko' ? '알림을 업데이트하지 못했습니다. 다시 시도해 주세요.' : 'Could not update notifications. Please try again.';
            hub.append(feedback);
        } finally {
            buttons.forEach(button => button.disabled = false);
            saving = false;
        }
    });
})();
