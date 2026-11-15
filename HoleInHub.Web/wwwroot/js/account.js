(() => {
    const content = document.querySelector('.account-inner, .booking-verification-page');
    const form = content?.querySelector('form');
    if (!form || form.hasAttribute?.('data-email-code') || form.hasAttribute?.('data-staff-pin')) return;

    // Only the form moves; the shared photo stays still between account pages.
    function enter() {
        window.HoleInHubMotion?.enter(content);
    }
    enter();
    window.addEventListener('pageshow', event => { if (event.persisted) enter(); });


    content.querySelectorAll('.auth-switch a,.auth-recovery').forEach(link => link.classList.add('prototype-link'));
    const inputs = [...form.querySelectorAll('input:not([type="hidden"]):not([type="checkbox"])')];
    content.querySelectorAll('[data-password-toggle]').forEach(toggle => {
        const input = document.getElementById(toggle.dataset.passwordToggle);
        const name = input.name === 'ConfirmPassword' ? 'confirm password' : 'password';
        toggle.hidden = false;
        toggle.addEventListener('click', () => {
            const show = input.type === 'password';
            input.type = show ? 'text' : 'password';
            const label = `${show ? 'Hide' : 'Show'} ${name}`;
            toggle.setAttribute('aria-label', window.HoleInHubText?.t(label) ?? label);
            toggle.querySelector('.password-eye').toggleAttribute('hidden', show);
            toggle.querySelector('.password-eye-slash').toggleAttribute('hidden', !show);
        });
    });
    const touched = new Set();
    const timers = new Map();
    function validate(input) {
        const feedback = document.getElementById(`${input.id}-error`);
        if (!feedback) return input.checkValidity?.() ?? true;
        let message = '';
        if (!input.value.trim()) message = ({FirstName:'Enter your first name.',LastName:'Enter your last name.',Phone:'Enter your contact number.'})[input.name] || (input.type === 'email' ? 'Enter your email address.' : 'Enter your password.');
        else if (input.validity.typeMismatch) message = 'Enter a valid email address.';
        else if (input.validity.patternMismatch) message = input.dataset.passwordRules ? 'Use 8–128 letters or numbers.' : ['Pin','ConfirmPin'].includes(input.name) ? 'Enter a six-digit PIN.' : ['FirstName','LastName'].includes(input.name) ? 'Use letters, spaces, commas, periods, apostrophes or hyphens.' : (window.HoleInHubContacts?.error(input.value) ?? 'Check the contact number’s prefix and digit count.');
        else if (input.validity.tooLong) message = `Use no more than ${input.maxLength} characters.`;
        else if (input.dataset.passwordRules && (input.value.length < 8 || input.value.length > 128 || !/^[A-Za-z0-9]+$/.test(input.value))) {
            message = 'Use 8–128 letters or numbers.';
        } else if (input.name === 'ConfirmPassword' && input.value !== form.elements.Password.value) message = 'Passwords do not match.';
        else if (input.name === 'ConfirmPin' && input.value !== form.elements.Pin.value) message = 'PINs do not match.';
        input.setAttribute('aria-invalid', String(Boolean(message)));
        const text = message;
        const next = window.HoleInHubText?.t(text) ?? text;
        if (feedback.textContent !== next) {
            feedback.textContent = next;
            feedback.dataset.valid = String(!message);
            if (next) window.HoleInHubMotion?.enter(feedback, { y: 3, opacity: .65, duration: .18 });
        }
        return !message;
    }
    inputs.forEach(input => {
        input.addEventListener('input', () => {
            clearTimeout(timers.get(input));
            timers.set(input, setTimeout(() => { touched.add(input); validate(input); }, 450));
            if (input.name === 'Password') {
                const confirm = form.elements.ConfirmPassword;
                if (confirm && touched.has(confirm)) {
                    clearTimeout(timers.get(confirm));
                    timers.set(confirm, setTimeout(() => validate(confirm), 450));
                }
            }
        });
        input.addEventListener('blur', () => {
            clearTimeout(timers.get(input));
            touched.add(input);
            if (!validate(input)) window.HoleInHubAuthFeedback?.shake(input.closest?.('.field') ?? input);
        });
    });
    // Native validation remains available when this enhancement cannot run.
    form.noValidate = true;
    let submitting=false;
    const submitButtons=[...form.querySelectorAll('button[type="submit"]')];
    window.addEventListener('pageshow',()=>{submitting=false;submitButtons.forEach(button=>{button.disabled=false;button.removeAttribute('aria-busy');});});
    form.addEventListener('submit', event => {
        if(submitting){event.preventDefault();return;}
        const invalid = inputs.filter(input => {
            clearTimeout(timers.get(input));
            return !validate(input);
        });
        if (invalid.length) { event.preventDefault(); invalid[0].focus(); window.HoleInHubAuthFeedback?.invalid(invalid, 'Check the highlighted fields.'); return; }
        submitting=true;submitButtons.forEach(button=>{button.disabled=true;button.setAttribute("aria-busy","true");});
    });
})();
