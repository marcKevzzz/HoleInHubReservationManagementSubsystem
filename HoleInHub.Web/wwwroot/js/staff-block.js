(() => {
    function mount(root = document) {
        for (const mode of root.querySelectorAll('[data-block-mode]')) {
            if (mode.dataset.mounted) continue;
            mode.dataset.mounted = 'true';
            const form = mode.form;
            const sync = () => {
                const wholeDay = mode.value === 'true';
                for (const field of form.querySelectorAll('[data-block-timed]')) {
                    field.hidden = wholeDay;
                    for (const input of field.querySelectorAll('input')) input.disabled = wholeDay;
                }
            };
            mode.addEventListener('change', sync);
            form.addEventListener('reset', () => queueMicrotask(sync));
            sync();
        }
        const page = root.querySelector('[data-staff-schedule]');
        const time = page?.querySelector('[data-new-block]');
        const day = page?.querySelector('[data-new-block-day]');
        const panel = page?.querySelector('[data-create-block]');
        if (!time || !day || !panel || page.dataset.blockShortcuts) return;
        page.dataset.blockShortcuts = 'true';
        day.addEventListener('click', event => {
            if (event.ctrlKey || event.metaKey || event.altKey || event.shiftKey || event.button) return;
            event.preventDefault();
            time.click();
            const form = panel.querySelector('form.staff-create-form');
            form.elements.WholeDay.value = 'true';
            form.elements.ResourceId.value = '*';
            for (const field of [form.elements.WholeDay, form.elements.ResourceId]) field.dispatchEvent(new Event('change', {bubbles:true}));
        });
        page.addEventListener('keydown', event => {
            if (event.defaultPrevented || event.repeat || !event.shiftKey || event.ctrlKey || event.metaKey || event.altKey
                || event.target.closest('input,textarea,select,[contenteditable],dialog') || !event.target.closest('.staff-timeline-scroll')) return;
            const key = event.key.toLowerCase();
            if (key !== 'b' && key !== 'd') return;
            event.preventDefault();
            (key === 'd' ? day : time).click();
        });
    }
    document.addEventListener('staff:schedule-mounted', () => mount());
    mount();
})();
