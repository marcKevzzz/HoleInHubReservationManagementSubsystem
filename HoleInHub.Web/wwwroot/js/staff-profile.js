(() => {
    const profile = document.querySelector('.staff-profile');
    if (!profile) return;
    profile.querySelectorAll('[data-password-toggle]').forEach(toggle => {
        const input = document.getElementById(toggle.dataset.passwordToggle);
        toggle.hidden = false;
        toggle.addEventListener('click', () => {
            const show = input.type === 'password';
            input.type = show ? 'text' : 'password';
            toggle.setAttribute('aria-label', (show ? 'Hide ' : 'Show ') + input.labels[0].textContent.toLowerCase());
            toggle.querySelector('.password-eye').hidden = show;
            toggle.querySelector('.password-eye-slash').hidden = !show;
        });
    });
    const password = profile.querySelector('[name="Password"]');
    const confirm = profile.querySelector('[name="ConfirmPassword"]');
    function matchPasswords() {
        confirm?.setCustomValidity(confirm.value && confirm.value !== password.value ? 'Passwords do not match.' : '');
    }
    password?.addEventListener('input', matchPasswords);
    confirm?.addEventListener('input', matchPasswords);
    profile.querySelectorAll('input:not([type=hidden])').forEach(input => {
        function validate() {
            const feedback = document.getElementById(input.id + '-error');
            input.setAttribute('aria-invalid', String(!input.validity.valid));
            if (feedback) feedback.textContent = input.validity.valid ? '' : input.validationMessage;
        }
        input.addEventListener('blur', validate);
        input.addEventListener('input', () => { if (input.getAttribute('aria-invalid') === 'true') validate(); });
    });
})();
