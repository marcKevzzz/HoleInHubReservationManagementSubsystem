(() => {
    const field = document.querySelector('[data-coach-field]');
    const type = document.querySelector('#BookingService');
    if (!field || !type) return;
    function sync() {
        field.hidden = !['PhilippineCoach','KpgaPro','KpgaGroup'].includes(type.value);
    }
    type.addEventListener('change', sync);
    sync();
})();
