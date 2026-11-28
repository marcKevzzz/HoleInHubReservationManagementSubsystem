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

for (const period of document.querySelectorAll('.schedule-period')) {
    const mode = period.querySelector('[data-schedule-mode]');
    if (mode) {
        const start = period.querySelector('[name=from]');
        const end = period.querySelector('[name=to]');
        const sync = () => {
            const single = mode.value === 'day';
            period.querySelector('[data-schedule-end]').hidden = single;
            period.querySelector('label[for=schedule-from]').textContent = single ? 'Date' : 'From';
            if (single) end.value = start.value;
        };
        mode.hidden = period.querySelector('[data-mode-label]').hidden = false;
        mode.addEventListener('change', sync);
        start.addEventListener('input', sync);
        sync();
    }
    document.addEventListener('click', event => {
        if (period.open && !period.contains(event.target)) period.open = false;
    });
    period.addEventListener('keydown', event => {
        if (event.key === 'Escape' && period.open) {
            period.open = false;
            period.querySelector('summary').focus();
        }
    });
}
