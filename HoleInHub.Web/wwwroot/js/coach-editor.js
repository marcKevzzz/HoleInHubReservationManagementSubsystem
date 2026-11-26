(() => {
    const form = document.querySelector('.coach-editor form');
    const upload = form.elements.Photo;
    const preview = form.querySelector('.coach-photo-preview');
    const tile = form.querySelector('.coach-photo-tile');
    const photo = preview.querySelector('img');
    const viewer = form.querySelector('[data-image-viewer]');
    viewer.classList.add('coach-image-viewer');
    const imageViewer = window.HoleInHubImageViewer(viewer);
    let objectUrl;
    upload.addEventListener('change', () => {
        if (!upload.files.length) return;
        URL.revokeObjectURL(objectUrl);
        objectUrl = URL.createObjectURL(upload.files[0]);
        photo.src = objectUrl;
        preview.hidden = false;
        tile.hidden = true;
    });
    preview.querySelector('[data-photo-change]').addEventListener('click', () => upload.click());
    preview.querySelector('[data-photo-delete]').addEventListener('click', () => {
        URL.revokeObjectURL(objectUrl);
        upload.value = '';
        form.elements.RemovePhoto.value = 'true';
        photo.removeAttribute('src');
        preview.hidden = true;
        tile.hidden = false;
        upload.focus();
    });
    preview.querySelector('[data-photo-open]').addEventListener('click', () => {
        const figure = document.createElement('figure');
        figure.append(photo.cloneNode());
        imageViewer.show([figure]);
    });

    const phone = form.elements.Phone;
    const country = form.elements.PhoneCountry;
    const saved = ProfilePhone.saved(phone.value);
    country.value = saved.country;
    phone.value = saved.national;
    country.disabled = country.hidden = false;
    country.dispatchEvent(new Event('change', { bubbles: true }));
    form.addEventListener('formdata', event => {
        if (phone.value.trim()) event.formData.set('Phone', '+' + ProfilePhone.full(country.value, phone.value));
    });

    const dialog = document.querySelector('#coach-remove-dialog');
    let trigger;
    form.addEventListener('click', event => {
        const button = event.target.closest('.coach-remove');
        if (!button) return;
        event.preventDefault();
        trigger = button;
        dialog.returnValue = '';
        dialog.querySelector('h2').textContent = button.getAttribute('aria-label') + '?';
        dialog.showModal();
    });
    dialog.addEventListener('close', () => {
        if (dialog.returnValue === 'remove') form.requestSubmit(trigger);
        else trigger?.focus();
    });
})();
