(() => {
    const page = document.querySelector('.resource-admin');
    const add = page?.querySelector('[data-resource-add-dialog]');
    const confirm = page?.querySelector('[data-resource-confirm]');
    if (typeof add?.showModal !== 'function' || typeof confirm?.showModal !== 'function') return;
    add.append(page.querySelector('[data-resource-add-content]'));
    add.querySelector('[data-resource-cancel]').hidden = false;
    let opener, pending, saving = false;
    function open(dialog, trigger) {
        if (saving) return;
        opener = trigger;
        const error = dialog.querySelector('[data-resource-error]');
        error.hidden = true;
        error.textContent = '';
        dialog.showModal();
    }
    page.querySelector('[data-resource-add]').addEventListener('click', event => {
        event.preventDefault();
        open(add, event.currentTarget);
        add.querySelector('[name=name]').focus();
    });
    for (const dialog of [add, confirm]) {
        dialog.querySelector('[data-resource-cancel]').addEventListener('click', () => { if (!saving) dialog.close(); });
        dialog.addEventListener('cancel', event => { if (saving) event.preventDefault(); });
        dialog.addEventListener('close', () => opener?.focus({ preventScroll: true }));
    }
    for (const form of page.querySelectorAll('[data-resource-state]')) {
        form.addEventListener('submit', event => {
            event.preventDefault();
            if (saving) return;
            pending = form;
            const restore = form.elements.active.value === 'true';
            confirm.querySelector('h2').textContent = `${restore ? 'Restore' : 'Retire'} ${form.dataset.bayName}?`;
            confirm.querySelector('#resource-confirm-description').textContent = `${form.dataset.bayId}. ` + (restore
                ? 'This bay will be available for new reservations again. Active whole-shop bookings may prevent this change.'
                : 'This removes the bay from new reservations without deleting its history. Resolve current or upcoming reservations and whole-shop bookings first. You can restore it later.');
            const button = confirm.querySelector('[data-resource-commit]');
            const label = restore ? 'Restore bay' : 'Retire bay';
            if (window.HoleInHubButtons) window.HoleInHubButtons.setLabel(button, label);
            else button.textContent = label;
            open(confirm, form.querySelector('button'));
        });
    }
    async function save(form, dialog) {
        if (saving || !form.reportValidity()) return;
        const body = new FormData(form);
        saving = true;
        const controls = [...dialog.querySelectorAll('button,input,select')];
        controls.forEach(control => control.disabled = true);
        dialog.setAttribute('aria-busy', 'true');
        const error = dialog.querySelector('[data-resource-error]');
        error.hidden = true;
        const request = new AbortController();
        const timeout = setTimeout(() => request.abort(), 25000);
        try {
            const response = await fetch(form.action, { method: 'POST', body, headers: { Accept: 'application/json' }, signal: request.signal, redirect: 'error' });
            const result = await response.json();
            if (!response.ok) {
                error.textContent = result.error || 'Could not save. Reload the inventory and try again.';
                error.hidden = false;
                window.HoleInHubToasts?.show({ kind: 'error', title: 'Bay could not be saved', message: error.textContent });
                return;
            }
            const destination = new URL(result.redirect, location.origin);
            if (destination.origin !== location.origin || destination.pathname !== '/admin/resources') throw new Error();
            window.location.assign(destination.href);
        } catch {
            error.textContent = 'The save could not be verified. Reload the inventory before retrying; your change may already have saved.';
            error.hidden = false;
            window.HoleInHubToasts?.show({ kind: 'error', title: 'Save could not be verified', message: error.textContent });
        } finally {
            clearTimeout(timeout);
            saving = false;
            controls.forEach(control => control.disabled = false);
            dialog.removeAttribute('aria-busy');
        }
    }
    add.querySelector('form').addEventListener('submit', event => { event.preventDefault(); void save(event.currentTarget, add); });
    confirm.querySelector('[data-resource-commit]').addEventListener('click', () => { if (pending) void save(pending, confirm); });
})();
