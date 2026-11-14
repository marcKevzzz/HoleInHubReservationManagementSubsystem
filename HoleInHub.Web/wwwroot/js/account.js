(() => {
    const content = document.querySelector('.account-inner');
    const form = content?.querySelector('form');
    if (!form || form.hasAttribute?.('data-email-code') || form.hasAttribute?.('data-staff-pin')) return;

    // Only the form moves; the shared photo stays still between account pages.
    function enter() {
        window.HoleInHubMotion?.enter(content);
    }
    enter();
    window.addEventListener('pageshow', event => { if (event.persisted) enter(); });


    content.querySelectorAll('.auth-switch a,.auth-recovery').forEach(link => link.classList.add('prototype-link'));
    const inputs = [...form.querySelectorAll('input:not([type="hidden"])')];
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
        if (!feedback) return true;
        let message = '';
        if (!input.value.trim()) message = ({FirstName:'Enter your first name.',LastName:'Enter your last name.',Phone:'Enter your contact number.'})[input.name] || (input.type === 'email' ? 'Enter your email address.' : 'Enter your password.');
        else if (input.validity.typeMismatch) message = 'Enter a valid email address.';
        else if (input.validity.patternMismatch) message = ['FirstName','LastName'].includes(input.name) ? 'Use letters for names; numbers are not allowed.' : 'Enter a valid contact number using 7–15 digits only.';
        else if (input.validity.tooLong) message = `Use no more than ${input.maxLength} characters.`;
        else if (input.dataset.passwordRules && (input.value.length < 12 || !/[a-z]/.test(input.value) || !/[A-Z]/.test(input.value) || !/[0-9]/.test(input.value) || !/[^a-zA-Z0-9]/.test(input.value))) {
            message = 'Use 12â€“128 characters with uppercase and lowercase letters, a number, and a symbol.';
        } else if (input.name === 'ConfirmPassword' && input.value !== form.elements.Password.value) message = 'Passwords do not match.';
        input.setAttribute('aria-invalid', String(Boolean(message)));
        const text = message || (input.dataset.passwordRules ? 'Password meets the requirements.' : '');
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
            validate(input);
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
        if (invalid.length) { event.preventDefault(); invalid[0].focus(); return; }
        submitting=true;submitButtons.forEach(button=>{button.disabled=true;button.setAttribute("aria-busy","true");});
    });
})();
