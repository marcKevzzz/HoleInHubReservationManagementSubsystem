(() => {
    const form = document.querySelector('[data-staff-pin]');
    if (!form) return;

    const entry = form.querySelector('[data-pin-entry]');
    const boxes = form.querySelector('[data-pin-slots]');
    const input = form.querySelector('#Pin');
    const error = form.querySelector('[data-pin-error]');
    const button = form.querySelector('button[type="submit"]');
    let submitting = false;

    function render() {
        Array.from(boxes.children).forEach((slot, index) => {
            if (index < input.value.length) slot.dataset.filled = '';
            else delete slot.dataset.filled;
        });
    }

    input.addEventListener('input', () => {
        input.value = input.value.replace(/[^0-9]/g, '').slice(0, input.maxLength);
        input.setAttribute('aria-invalid', 'false');
        error.hidden = true;
        render();
        if (input.value.length === input.maxLength && !submitting) form.requestSubmit();
    });

    form.addEventListener('submit', event => {
        if (submitting) {
            event.preventDefault();
            return;
        }
        submitting = true;
        input.readOnly = true;
        button.disabled = true;
        button.textContent = 'Signing in…';
        form.setAttribute('aria-busy', 'true');
    });

    window.addEventListener('pageshow', event => {
        if (!event.persisted) return;
        submitting = false;
        input.value = '';
        input.readOnly = false;
        button.disabled = false;
        button.textContent = 'Sign in';
        form.setAttribute('aria-busy', 'false');
        render();
    });

    entry.dataset.enhanced = '';
    boxes.hidden = false;
    render();
    if (input.getAttribute('aria-invalid') === 'true') {
        input.focus({ preventScroll: true });
        if (!window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
            window.gsap?.to(boxes, { keyframes: { x: [0, -7, 7, -4, 4, 0] }, duration: 0.32, ease: 'power2.out' });
        }
    }
})();
