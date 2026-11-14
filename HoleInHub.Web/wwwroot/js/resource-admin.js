(() => {
    const page = document.querySelector('.resource-admin');
    const add = page?.querySelector('[data-resource-add-dialog]');
    const confirm = page?.querySelector('[data-resource-confirm]');
    const edit = page?.querySelector('[data-resource-edit-dialog]');
    if (typeof add?.showModal !== 'function' || typeof confirm?.showModal !== 'function') return;
    add.append(page.querySelector('[data-resource-add-content]'));
    let opener, pending, saving = false;
    const reduced = () => window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? true;
    function dismiss(dialog) {
        if (saving) return;
        if (dialog === confirm || reduced() || !window.gsap) { dialog.close(); return; }
        window.gsap.killTweensOf(dialog);
        window.gsap.to(dialog, {x:64,opacity:0,duration:.2,ease:'power2.in',onComplete:() => dialog.close()});
    }
    function open(dialog, trigger) {
        if (saving) return;
        opener = trigger;
        const error = dialog.querySelector('[data-resource-error]');
        error.hidden = true;
        error.textContent = '';
        dialog.showModal();
        if (dialog !== confirm && !reduced() && window.gsap) {
            window.gsap.killTweensOf(dialog);
            window.gsap.fromTo(dialog, {x:64,opacity:0}, {x:0,opacity:1,duration:.28,ease:'power3.out',clearProps:'transform,opacity'});
        }
    }
    if (edit) {
        const form = edit.querySelector('form');
        const clearAcknowledgement = () => { form.elements.capacityAcknowledgement.value = ''; };
        form.addEventListener('input', clearAcknowledgement);
        form.addEventListener('change', clearAcknowledgement);
    }
    page.querySelector('[data-resource-add]').addEventListener('click', event => {
        event.preventDefault();
        open(add, event.currentTarget);
        add.querySelector('[name=name]').focus();
    });
    for (const dialog of [add, confirm]) {
        dialog.querySelector('[data-resource-cancel]')?.addEventListener('click', () => dismiss(dialog));
        dialog.addEventListener('cancel', event => { event.preventDefault(); dismiss(dialog); });
        dialog.addEventListener('close', () => opener?.focus({ preventScroll: true }));
    }
    for (const dialog of [add, edit].filter(Boolean)) {
        dialog.querySelector('[data-resource-close]')?.addEventListener('click', () => dismiss(dialog));
        let pressedOutside = false;
        function outside(event) {
            const bounds = dialog.getBoundingClientRect();
            return event.target === dialog && (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom);
        }
        dialog.addEventListener('pointerdown', event => { pressedOutside = outside(event); });
        dialog.addEventListener('click', event => { if (pressedOutside && outside(event)) dismiss(dialog); pressedOutside = false; });
        dialog.addEventListener('close', () => { window.gsap?.killTweensOf(dialog); window.gsap?.set(dialog, {clearProps:'transform,opacity'}); });
    }
    if (edit) {
        edit.addEventListener('cancel', event => { event.preventDefault(); dismiss(edit); });
        edit.addEventListener('close', () => opener?.focus({ preventScroll: true }));
        for (const button of page.querySelectorAll('[data-resource-edit]')) {
            button.addEventListener('click', () => {
                const form = edit.querySelector('form');
                form.elements.id.value = button.dataset.bayId;
                form.elements.name.value = button.dataset.bayName;
                form.elements.service.value = button.dataset.bayService;
                form.elements.expectedRevision.value = button.dataset.bayRevision;
                form.elements.maxPlayers.value = button.dataset.bayMaxPlayers;
                form.elements.maxTotalGuests.value = button.dataset.bayMaxTotalGuests;
                form.elements.maxNonPlayers.value = button.dataset.bayMaxNonPlayers ?? '';
                form.elements.capacityAcknowledgement.value = '';
                open(edit, button);
                form.elements.name.focus();
            });
        }
        edit.querySelector('form').addEventListener('submit', event => { event.preventDefault(); void save(event.currentTarget, edit); });
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
            button.dataset.danger = String(!restore);
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
                if (result.acknowledgement && form.elements.capacityAcknowledgement) {
                    form.elements.capacityAcknowledgement.value = result.acknowledgement;
                    error.textContent += ' Select Save bay again to apply these limits.';
                }
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
