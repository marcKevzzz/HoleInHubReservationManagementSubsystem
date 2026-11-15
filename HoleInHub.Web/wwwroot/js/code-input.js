const CodeInput = value => value.replace(/[^0-9]/g, '').slice(0, 6);
if (typeof module !== 'undefined') module.exports = CodeInput;
if (typeof document !== 'undefined') (() => {
    document.querySelectorAll('[data-code-entry]').forEach(entry => {
        const input = entry.querySelector('input');
        const slots = entry.querySelector('[data-code-slots]');
        function render() {
            [...slots.children].forEach((slot, index) => {
                const digit = input.value[index] ?? '';
                slot.textContent = entry.hasAttribute('data-code-secret') ? (digit ? '•' : '') : digit;
                slot.toggleAttribute('data-filled', Boolean(digit));
                slot.toggleAttribute('data-current', index === Math.min(input.value.length, 5));
            });
        }
        input.addEventListener('input', () => {
            input.value = CodeInput(input.value);
            input.setAttribute('aria-invalid', 'false');
            const feedback = document.getElementById(`${input.id}-error`);
            if (feedback) feedback.textContent = '';
            render();
        });
        // One real input preserves paste, autofill, arrow keys, selection and screen reader semantics.
        entry.dataset.enhanced = '';
        slots.hidden = false;
        render();
    });
    document.querySelectorAll('form[data-email-code]').forEach(form => {
        const input = form.querySelector('[name="code"]');
        let submitting = false;
        form.noValidate = true;
        form.addEventListener('submit', event => {
            if (submitting) { event.preventDefault(); return; }
            if (event.submitter?.value !== 'resend' && input && !input.checkValidity()) {
                event.preventDefault();
                const message = 'Enter the six-digit code from your email.';
                input.setAttribute('aria-invalid', 'true');
                document.getElementById(`${input.id}-error`).textContent = message;
                input.focus();
                window.HoleInHubAuthFeedback?.invalid([input], message);
                return;
            }
            submitting = true;
            form.setAttribute('aria-busy', 'true');
            // Keep the submitter enabled so the resend intent is included in the POST.
            if (input) input.readOnly = true;
        });
        window.addEventListener('pageshow', () => {
            submitting = false;
            form.removeAttribute('aria-busy');
            if (input) input.readOnly = false;
        });
    });
})();
