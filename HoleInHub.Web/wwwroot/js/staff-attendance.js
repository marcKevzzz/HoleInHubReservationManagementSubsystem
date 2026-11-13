(() => {
    function update(form) {
        const checkIn = form.elements.Action.value === 'check-in';
        const now = Date.now();
        const outsideWindow = checkIn && (now < Date.parse(form.dataset.sessionStart) || now >= Date.parse(form.dataset.sessionEnd));
        form.querySelector('button[type="submit"]').disabled = outsideWindow || !form.elements.ConfirmAttendance.checked || !form.elements.Reason.checkValidity();
        form.querySelector('[data-attendance-expired]').hidden = !outsideWindow;
    }
    function mount(root) {
        for (const form of root.querySelectorAll('[data-attendance-form]')) update(form);
    }
    document.addEventListener('input', event => {
        const form = event.target.closest('[data-attendance-form]');
        if (form) update(form);
    });
    document.addEventListener('submit', event => {
        const form = event.target.closest('[data-attendance-form]');
        if (!form) return;
        update(form);
        if (form.querySelector('button[type="submit"]').disabled) event.preventDefault();
    });
    window.HoleInHubAttendance = { mount };
    mount(document);
})();
