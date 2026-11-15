(() => {
    function update(form) {
        if (form.elements.Action.value === 'correct') {
            const status = form.elements.CorrectedStatus.value;
            const arrival = form.elements.ArrivalLocal;
            const departure = form.elements.DepartureLocal;
            arrival.disabled = !['Checked-In', 'Completed'].includes(status);
            arrival.required = !arrival.disabled;
            departure.disabled = status !== 'Completed';
            departure.required = !departure.disabled;
        }
        const checkIn = form.elements.Action.value === 'check-in';
        const now = Date.now();
        const outsideWindow = checkIn && (now < Date.parse(form.dataset.checkInOpens ?? form.dataset.sessionStart) || now >= Date.parse(form.dataset.sessionEnd));
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
    document.addEventListener('change', event => {
        const form = event.target.closest('[data-attendance-form]');
        if (form) update(form);
    });
    mount(document);
})();
