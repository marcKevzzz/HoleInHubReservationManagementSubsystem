const ProfilePhone = {
    countries: {
        PH: { code: '63', digits: 10, trunk: true, example: '917 123 4567' },
        KR: { code: '82', digits: 10, trunk: true, example: '10 1234 5678' },
        CN: { code: '86', digits: 11, example: '138 1234 5678' },
        US: { code: '1', digits: 10, example: '415 555 2671' },
        JP: { code: '81', digits: 10, trunk: true, example: '90 1234 5678' },
    },
    national(country, value) {
        const rule = this.countries[country];
        let digits = value.replace(/[^0-9]/g, '');
        if (rule.code && digits.startsWith(rule.code) && (value.trim().startsWith('+') || digits.length > rule.digits)) digits = digits.slice(rule.code.length);
        if (rule.trunk) digits = digits.replace(/^0/, '');
        return digits;
    },
    full(country, value) { return this.countries[country].code + this.national(country, value); },
    fits(country, value) { return this.national(country, value).length <= this.countries[country].digits; },
    saved(value) {
        const digits = value.replace(/[^0-9]/g, '');
        let country = /^010/.test(digits) ? 'KR' : 'PH';
        for (const [key, rule] of Object.entries(this.countries)) {
            if (rule.code && digits.startsWith(rule.code) && digits.length > rule.digits) { country = key; break; }
        }
        if (!digits) country = 'PH';
        return { country, national: this.national(country, value) };
    },
};
if (typeof module !== 'undefined') module.exports = ProfilePhone;
if (typeof document !== 'undefined') (() => {
    const form = document.querySelector('.account-profile-form');
    if (!form) return;
    const first = form.querySelector('[name="FirstName"]'), last = form.querySelector('[name="LastName"]');
    const phone = form.querySelector('#PhoneDisplay'), country = form.querySelector('#PhoneCountry');
    const phonePattern = new RegExp('^(?:' + phone.dataset.phonePattern + ')$');
    const saved = ProfilePhone.saved(phone.value);
    country.value = saved.country;
    phone.value = saved.national;
    country.hidden = false;
    country.disabled = false;
    country.dispatchEvent(new Event('change', { bubbles: true }));
    const hint = () => {
        const rule = ProfilePhone.countries[country.value];
        phone.placeholder = ' ';
        phone.title = rule.example;
        phone.maxLength = rule.digits + (rule.trunk ? 1 : 0);
    };
    hint();
    const shake = input => {
        if (window.matchMedia('(prefers-reduced-motion: reduce)').matches || !window.gsap) return;
        window.gsap.fromTo(input, { x: -5 }, { x: 0, duration: .4, ease: 'elastic.out(1,.3)', clearProps: 'transform', overwrite: true });
    };
    const checks = [[first, 'FirstName-error', 'First name is required.'], [last, 'LastName-error', 'Last name is required.'], [phone, 'Phone-error', 'Contact number is required.']];
    const setError = (input, id, message, animate = true) => {
        document.getElementById(id).textContent = message;
        input.setAttribute('aria-invalid', message ? 'true' : 'false');
        if (message && animate) shake(input === phone ? phone.closest('.profile-phone-field') : input);
    };
    const typeError = input => input === phone ? 'Invalid input. Numbers only are allowed.' : 'Invalid input. Text only; numbers are not allowed.';
    const maximumError = () => `Use no more than ${ProfilePhone.countries[country.value].digits} digits after the country code.`;
    const validate = ([input, id, required], animate = true) => {
        const value = input.value.trim();
        let error = value ? '' : required;
        if (value && input === phone) {
            if (/[^0-9+ ()-]/.test(value)) error = typeError(input);
            else if (!ProfilePhone.fits(country.value, value)) error = maximumError();
            else if (!phonePattern.test(ProfilePhone.full(country.value, value))) error = 'Check the contact number and selected country code.';
        }
        if (value && input !== phone && !new RegExp('^(?:' + input.pattern + ')$', 'u').test(value)) error = 'Use letters only; numbers are not allowed.';
        setError(input, id, error, animate);
        return !error;
    };
    checks.forEach(check => {
        const [input, id] = check;
        const invalidType = value => input === phone ? /[^0-9+ ()-]/.test(value) : /\p{N}/u.test(value);
        input.addEventListener('beforeinput', event => {
            if (!event.data || event.isComposing) return;
            if (invalidType(event.data)) { event.preventDefault(); setError(input, id, typeError(input)); return; }
            if (input === phone) {
                const candidate = input.value.slice(0, input.selectionStart ?? input.value.length) + event.data + input.value.slice(input.selectionEnd ?? input.value.length);
                if (!ProfilePhone.fits(country.value, candidate)) { event.preventDefault(); setError(input, id, maximumError()); }
            }
        });
        input.addEventListener('paste', event => {
            const text = event.clipboardData?.getData('text');
            if (!text) return;
            if (invalidType(text)) { event.preventDefault(); setError(input, id, typeError(input)); return; }
            if (input === phone) {
                event.preventDefault();
                const value = input.value.slice(0, input.selectionStart ?? input.value.length) + text + input.value.slice(input.selectionEnd ?? input.value.length);
                if (!ProfilePhone.fits(country.value, value)) { setError(input, id, maximumError()); return; }
                input.value = ProfilePhone.national(country.value, value);
                validate(check, false);
            }
        });
        check[0].addEventListener('blur', () => validate(check));
        check[0].addEventListener('input', event => {
            if (event.isComposing) return;
            if (invalidType(input.value)) { setError(input, id, typeError(input)); return; }
            if (input === phone) {
                if (!ProfilePhone.fits(country.value, input.value)) { setError(input, id, maximumError()); return; }
                input.value = ProfilePhone.national(country.value, input.value);
            }
            if (input.getAttribute('aria-invalid') === 'true') validate(check, false);
        });
        input.addEventListener('compositionend', () => validate(check));
    });
    country.addEventListener('change', () => { hint(); if (phone.value) validate(checks[2]); });
    form.addEventListener('submit', event => {
        const invalid = checks.filter(check => !validate(check));
        if (invalid.length) { event.preventDefault(); invalid[0][0].focus(); return; }
        phone.value = ProfilePhone.full(country.value, phone.value);
    });
    window.addEventListener('pageshow', event => {
        if (!event.persisted) return;
        const saved = ProfilePhone.saved(phone.value);
        country.value = saved.country;
        phone.value = saved.national;
        hint();
        country.dispatchEvent(new Event('change', { bubbles: true }));
    });
})();
