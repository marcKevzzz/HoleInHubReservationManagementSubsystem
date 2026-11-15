const AuthPasswordStrength = value => {
    if (!value) return { score: 0, label: '' };
    if (!/^[A-Za-z0-9]{8,128}$/.test(value)) return { score: 1, label: 'Does not meet the requirements' };
    const varied = /[a-z]/.test(value) && /[A-Z]/.test(value) && /[0-9]/.test(value);
    const repeated = /^(.)\1+$/.test(value) || /^(?:password|12345678|abcdefgh)/i.test(value);
    const score = repeated ? 1 : value.length >= 16 && varied ? 4 : value.length >= 12 ? 3 : varied ? 2 : 1;
    return { score, label: ['','Weak — try a longer password','Fair','Good','Strong'][score] };
};
if (typeof module !== 'undefined') module.exports = AuthPasswordStrength;
if (typeof document !== 'undefined') (() => {
    function shake(element) {
        if (!element || window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;
        window.gsap?.fromTo(element, { x: 0 }, { keyframes: { x: [-5, 5, -3, 3, 0] }, duration: .3, ease: 'power2.out', overwrite: true });
    }
    function notify(message, kind = 'error') {
        return window.HoleInHubToasts?.show({ kind, title: kind === 'error' ? 'Please check your details' : 'All set', message, replaceKey: `auth-feedback:${kind}:${message}`, autoDismiss: kind !== 'error' });
    }
    function invalid(inputs, message) {
        inputs.forEach(input => shake(input.closest('.code-entry, .staff-pin-entry, .field') ?? input));
        notify(message);
    }
    window.HoleInHubAuthFeedback = { shake, invalid, notify };

    const scope = document.querySelector('.auth-main, .verification-shell main, .staff-profile');
    if (!scope) return;
    const summaries = [...scope.querySelectorAll('.validation-summary-errors, .booking-errors')].filter(node => node.textContent.trim());
    const errors = summaries.map(node => node.textContent.trim()).join(' ');
    if (errors && notify(errors)) summaries.forEach(node => { node.hidden = true; });
    scope.querySelectorAll('p[role="status"]').forEach(node => {
        if (notify(node.textContent.trim(), 'success')) node.hidden = true;
    });
    const serverInvalid = [...scope.querySelectorAll('.input-validation-error')];
    serverInvalid.forEach(input => input.setAttribute('aria-invalid', 'true'));
    if (errors && !serverInvalid.length) {
        const code = scope.querySelector('[name="code"], [name="Pin"]');
        if (code) { code.setAttribute('aria-invalid', 'true'); serverInvalid.push(code); }
        else shake(scope.querySelector('form'));
    }
    if (serverInvalid.length) {
        serverInvalid[0].focus({ preventScroll: true });
        serverInvalid.forEach(input => shake(input.closest('.code-entry, .staff-pin-entry, .field') ?? input));
    }

    scope.addEventListener('invalid', event => {
        const input = event.target;
        input.setAttribute('aria-invalid', 'true');
        const feedback = document.getElementById(`${input.id}-error`);
        if (feedback) feedback.textContent = input.validationMessage;
        invalid([input], input.validationMessage);
    }, true);

    const password = scope.querySelector('[data-password-rules]');
    const strength = scope.querySelector('[data-password-strength]');
    if (password && strength) {
        const meter = strength.querySelector('meter');
        const label = strength.querySelector('span');
        password.setAttribute('aria-describedby', `${password.getAttribute('aria-describedby') ?? ''} password-guidance password-strength-label`.trim());
        function update() {
            const result = AuthPasswordStrength(password.value);
            strength.hidden = !/^[A-Za-z0-9]{8,128}$/.test(password.value);
            meter.value = result.score;
            label.textContent = result.label;
        }
        password.addEventListener('input', update);
        update();
    }
})();
