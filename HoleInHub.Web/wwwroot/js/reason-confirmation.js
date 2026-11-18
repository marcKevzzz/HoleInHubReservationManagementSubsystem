(() => {
    for (const form of document.querySelectorAll('[data-reason-form]')) {
        const select = form.querySelector('[data-confirm-change]');
        const dialog = form.querySelector('[data-reason-dialog]');
        if (typeof dialog?.showModal !== 'function') continue;
        const reason = dialog.querySelector('[data-reason-input]');
        const cancel = dialog.querySelector('[data-reason-cancel]');
        const submit = dialog.querySelector('[data-reason-submit]');
        const description = dialog.querySelector('[data-reason-description]');
        const original = form.dataset.currentValue;
        const originalLabel = select ? [...select.options].find(option => option.value === original)?.textContent.trim() || 'Multiple roles' : '';
        let saving = false;
        const fallback = form.querySelector('[data-reason-fallback]');
        if (select) fallback.hidden = true;
        function open() {
            if (saving || (select && (!select.value || select.value === original))) return;
            if (select && form.dataset.reasonTemplate) description.textContent = form.dataset.reasonTemplate
                .replace('{from}', originalLabel).replace('{to}', select.selectedOptions[0].textContent.trim());
            reason.disabled = false;
            dialog.showModal();
        }
        select?.addEventListener('change', open);
        cancel.addEventListener('click', () => { if (!saving) dialog.close(); });
        dialog.addEventListener('cancel', event => { if (saving) event.preventDefault(); });
        dialog.addEventListener('close', () => {
            if (saving) return;
            if (select) { select.value = original; reason.value = ''; }
            reason.disabled = true;
            (select || fallback).focus({ preventScroll: true });
        });
        form.addEventListener('submit', event => {
            if (saving) { event.preventDefault(); return; }
            if (!dialog.open) { event.preventDefault(); open(); return; }
            saving = true;
            submit.disabled = true;
            submit.textContent = 'Saving…';
        });
        if (form.dataset.openOnLoad === 'true') open();
    }
})();
