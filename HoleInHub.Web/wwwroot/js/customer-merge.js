(() => {
    function mount(root = document) {
        const form = root.querySelector('[data-customer-merge]');
        if (!form) return;
        const output = form.querySelector('[data-merge-count]');
        const submit = form.querySelector('[data-reason-fallback]');
        const description = form.querySelector('[data-reason-description]');
        const scope = form.querySelector('input[type="hidden"][name="Form.BookingScope"]');
        const included = form.querySelector('[data-merge-included]');

        function update() {
            const sourceCount = Number(form.dataset.sourceCount);
            const selected = form.querySelectorAll('[name="Form.ReservationIds"]:checked').length;
            if (scope) scope.value = selected === sourceCount && selected > 0 ? 'all' : 'selected';
            const all = scope ? scope.value === 'all' : form.querySelector('[name="Form.BookingScope"]:checked')?.value === 'all';
            const added = all ? sourceCount : selected;
            const total = Number(form.dataset.targetCount) + added;
            form.dataset.mergeIncluded = String(added);
            output.value = total;
            if (included) included.textContent = added + ' included';
            if (!scope) submit.disabled = added === 0 || form.dataset.matchingDetails === 'false';
            description.textContent = `Combine ${added} ${added === 1 ? 'reservation' : 'reservations'} into the main staff profile (${total} in its history). Customer account access and original booking details stay the same.`;
        }

        form.addEventListener('change', update);
        update();
    }
    if (typeof window !== 'undefined') window.HoleInHubCustomerMerge = { mount };
    mount();
})();
