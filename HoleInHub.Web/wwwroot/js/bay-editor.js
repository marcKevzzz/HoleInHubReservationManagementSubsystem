(() => {
    const form = document.querySelector('#bay-editor');
    if (!form) return;
    const error = form.querySelector('[data-save-error]');
    const save = document.querySelector('[form="bay-editor"][type="submit"]');
    const notify = message => { error.textContent = message; };
    window.HoleInHubEditorGallery(form, notify, () => {});
    let saving = false;
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (saving) return;
        saving = save.disabled = true;
        error.textContent = '';
        try {
            const response = await fetch(form.action, { method: 'POST', body: new FormData(form), headers: { Accept: 'application/json' } });
            const result = await response.json();
            if (response.ok && result.redirect) { location.assign(result.redirect); return; }
            form.elements.CapacityAcknowledgement.value = result.acknowledgement || '';
            notify((result.error || 'Could not save this bay.') + (result.acknowledgement ? ' Select Save bay again to confirm.' : ''));
        } catch {
            notify('Could not confirm the save. Check the inventory before trying again.');
        } finally { saving = save.disabled = false; }
    });
})();
