(() => {
    const panel = document.querySelector('[data-people-create-panel]');
    if (typeof panel?.showModal !== 'function') return;
    const body = panel.querySelector('[data-people-create-body]');
    const close = panel.querySelector('[data-people-create-close]');
    let opener, source, request, saving = false, backdropPressed = false;

    function mount(content) {
        body.replaceChildren(content);
        window.HoleInHubButtons?.mount(content);
        content.querySelector('.validation-summary-errors')?.setAttribute('tabindex', '-1');
        (content.querySelector('.validation-summary-errors') || content.querySelector('input:not([type="hidden"])'))?.focus({ preventScroll: true });
    }
    async function read(response) {
        if (!response.ok) throw new Error();
        const content = new DOMParser().parseFromString(await response.text(), 'text/html').querySelector('[data-people-create-content]');
        if (!content) throw new Error();
        return content;
    }
    function dismiss() {
        if (!saving) panel.close();
    }
    function outside(event) {
        const rect = panel.getBoundingClientRect();
        return event.clientX < rect.left || event.clientX > rect.right || event.clientY < rect.top || event.clientY > rect.bottom;
    }
    document.addEventListener('click', async event => {
        const link = event.target.closest('[data-people-create]');
        if (!link || event.defaultPrevented || event.button || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        event.preventDefault();
        opener = link;
        panel.showModal();
        document.documentElement.classList.add('people-create-open');
        if (source === link.href && body.querySelector('[data-person-create-form]')) return;
        source = link.href;
        request?.abort();
        const current = request = new AbortController();
        const { message, skeleton } = window.HoleInHubPanels.loading(body, 'Loading form…');
        const timeout = setTimeout(() => current.abort(), 15000);
        try {
            const content = await read(await fetch(source, { headers: { 'X-People-Panel': 'true' }, signal: current.signal, cache: 'no-store', redirect: 'error' }));
            if (request === current && panel.open) mount(content);
        } catch {
            if (request !== current || !panel.open) return;
            skeleton.remove();
            message.classList.remove('sr-only');
            message.textContent = 'Could not load the form. ';
            const fallback = document.createElement('a');
            fallback.href = source;
            fallback.textContent = 'Open full page';
            message.append(fallback);
        } finally {
            clearTimeout(timeout);
            if (request === current) body.removeAttribute('aria-busy');
        }
    });
    body.addEventListener('submit', async event => {
        const form = event.target.closest('[data-person-create-form]');
        if (!form) return;
        event.preventDefault();
        if (saving || !form.reportValidity()) return;
        const data = new FormData(form);
        const controls = [...form.elements].map(control => [control, control.disabled]);
        const button = form.querySelector('button[type="submit"]');
        const label = button.getAttribute?.('aria-label') || button.textContent;
        const setLabel = text => window.HoleInHubButtons ? window.HoleInHubButtons.setLabel(button, text) : button.textContent = text;
        setLabel('Adding…');
        const submission = new AbortController();
        const timeout = setTimeout(() => submission.abort(), 20000);
        saving = true;
        close.disabled = true;
        controls.forEach(([control]) => { control.disabled = true; });
        body.querySelector('[data-create-error]')?.remove();
        body.setAttribute('aria-busy', 'true');
        try {
            const response = await fetch(form.action, { method: 'POST', body: data, signal: submission.signal, headers: { 'X-People-Panel': 'true' }, redirect: 'error' });
            if (response.ok && response.headers.get('content-type')?.includes('application/json')) {
                const result = await response.json();
                const destination = new URL(result.redirectUrl, location.origin);
                if (destination.origin !== location.origin || !destination.pathname.startsWith('/staff/')) throw new Error();
                window.location.assign(destination.href);
                return;
            }
            mount(await read(response));
        } catch {
            const error = document.createElement('p');
            error.dataset.createError = '';
            error.className = 'staff-error';
            error.setAttribute('role', 'alert');
            error.textContent = 'Could not finish saving. Your details are kept. Try again.';
            form.prepend(error);
        } finally {
            clearTimeout(timeout);
            setLabel(label);
            saving = false;
            close.disabled = false;
            controls.forEach(([control, disabled]) => { control.disabled = disabled; });
            body.removeAttribute('aria-busy');
        }
    });
    close.addEventListener('click', dismiss);
    panel.addEventListener('cancel', event => { if (saving) event.preventDefault(); });
    panel.addEventListener('pointerdown', event => { backdropPressed = event.target === panel && outside(event); });
    panel.addEventListener('click', event => {
        if (backdropPressed && event.target === panel && outside(event)) dismiss();
        backdropPressed = false;
    });
    panel.addEventListener('close', () => {
        request?.abort();
        request = undefined;
        body.removeAttribute('aria-busy');
        document.documentElement.classList.remove('people-create-open');
        opener?.focus({ preventScroll: true });
    });
})();
