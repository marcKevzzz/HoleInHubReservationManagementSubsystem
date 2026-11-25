(() => {
    const form = document.querySelector('.coach-editor form');
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
