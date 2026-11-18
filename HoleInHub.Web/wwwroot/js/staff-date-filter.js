(() => {
function mount() {
for (const form of document.querySelectorAll('[data-staff-date-filter]')) {
    const input = form.querySelector('input[type=date]');
    const button = form.querySelector('[data-open-date]');
    if (typeof input.showPicker !== 'function') continue;

    form.classList.add('staff-date-enhanced');
    input.tabIndex = -1;
    button.hidden = false;
    button.addEventListener('click', () => {
        try { input.showPicker(); }
        catch {
            form.classList.remove('staff-date-enhanced');
            button.hidden = true;
            input.removeAttribute('tabindex');
            input.focus();
        }
    });
    input.addEventListener('change', () => {
        if (input.value) form.requestSubmit();
    });
}

}
document.addEventListener('staff:schedule-mounted', mount);
mount();
})();
