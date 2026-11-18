(function () {
    const popout = document.querySelector('[data-cookie-popout]');
    if (!popout) return;

    const policyModal = document.querySelector('[data-cookie-policy-modal]');
    const policyDialog = policyModal?.querySelector('[role="dialog"]');
    const policyOpeners = [...document.querySelectorAll('[data-cookie-policy-open]')];
    const policyCloseButtons = [...(policyModal?.querySelectorAll('button[data-cookie-policy-close]') ?? [])];
    const policyPreferenceInputs = [...(policyModal?.querySelectorAll('[data-cookie-policy-pref]') ?? [])];
    const cookieTooltipTriggers = [...document.querySelectorAll('.cookie-popout .cookie-tooltip-trigger, .cookie-policy-dialog .cookie-tooltip-trigger')];
    const toggleButtons = [...document.querySelectorAll('[data-cookie-toggle]')];
    const actions = popout.querySelector('[data-cookie-actions]');
    const preferencesPanel = popout.querySelector('[data-cookie-preferences]');
    const preferenceInputs = [...popout.querySelectorAll('[data-cookie-pref]')];
    const preferenceTitle = popout.querySelector('#cookie-preferences-title');
    const storageKey = 'hih-cookie-preferences';
    const sessionKey = 'hih-cookie-policy-shown-v2';
    const reducedMotion = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false;
    const canAnimate = !reducedMotion && typeof window.gsap !== 'undefined';
    let open = false;
    let policyOpen = false;
    let policyReturnFocus = null;

    function readPreferences() {
        try {
            const value = JSON.parse(window.localStorage.getItem(storageKey) || 'null');
            if (!value || value.version !== 1) return null;
            return {
                ...value,
                essential: true,
                functional: Boolean(value.functional ?? value.marketing),
                analytics: Boolean(value.analytics)
            };
        } catch {
            return null;
        }
    }

    function setExpanded(value) {
        toggleButtons.forEach(button => button.setAttribute('aria-expanded', String(value)));
    }

    function positionTooltip(trigger) {
        const tooltip = trigger.nextElementSibling;
        if (!tooltip?.classList.contains('cookie-tooltip')) return;
        const rect = trigger.getBoundingClientRect();
        const desiredLeft = rect.right + 8;
        const availableRight = Math.max(132, window.innerWidth - desiredLeft - 12);
        const width = Math.min(window.innerWidth < 520 ? 200 : 230, availableRight);
        const left = Math.max(12, Math.min(desiredLeft, window.innerWidth - width - 12));
        tooltip.style.setProperty('--cookie-tooltip-x', `${left}px`);
        tooltip.style.setProperty('--cookie-tooltip-y', `${rect.top + rect.height / 2}px`);
        tooltip.style.setProperty('--cookie-tooltip-width', `${width}px`);
    }

    cookieTooltipTriggers.forEach(trigger => {
        trigger.addEventListener('pointerenter', () => positionTooltip(trigger));
        trigger.addEventListener('focus', () => positionTooltip(trigger));
    });
    window.addEventListener('resize', () => cookieTooltipTriggers.forEach(positionTooltip));
    document.addEventListener('scroll', () => cookieTooltipTriggers.forEach(positionTooltip), true);

    function applySavedPreferences(inputs) {
        const saved = readPreferences();
        inputs.forEach(input => {
            const key = input.dataset.cookiePref ?? input.dataset.cookiePolicyPref;
            input.checked = saved ? Boolean(saved[key]) : false;
        });
    }

    function animateShow() {
        if (canAnimate) {
            window.gsap.killTweensOf(popout);
            window.gsap.fromTo(popout, { x: 84, y: 20, scale: 0.94, opacity: 0 }, {
                x: 0,
                y: 0,
                scale: 1,
                opacity: 1,
                duration: 0.72,
                ease: 'power3.out',
                clearProps: 'transform,opacity'
            });
        } else if (!reducedMotion && typeof popout.animate === 'function') {
            popout.animate([
                { transform: 'translate(84px, 20px) scale(.94)', opacity: 0 },
                { transform: 'translate(0, 0) scale(1)', opacity: 1 }
            ], { duration: 720, easing: 'cubic-bezier(.16, 1, .3, 1)' });
        }
    }

    function animatePolicyShow() {
        if (!policyDialog) return;
        if (canAnimate) {
            window.gsap.killTweensOf(policyDialog);
            window.gsap.fromTo(policyDialog, { y: 24, scale: 0.97, opacity: 0 }, {
                y: 0,
                scale: 1,
                opacity: 1,
                duration: 0.42,
                ease: 'power3.out',
                clearProps: 'transform,opacity'
            });
        } else if (!reducedMotion && typeof policyDialog.animate === 'function') {
            policyDialog.animate([
                { transform: 'translateY(24px) scale(.97)', opacity: 0 },
                { transform: 'translateY(0) scale(1)', opacity: 1 }
            ], { duration: 420, easing: 'cubic-bezier(.16, 1, .3, 1)' });
        }
    }

    function setPreferencesView(show) {
        if (!preferencesPanel || !actions) return;
        preferencesPanel.hidden = !show;
        actions.hidden = show;
        if (show) {
            applySavedPreferences(preferenceInputs);
            preferenceTitle?.focus({ preventScroll: true });
        }
    }

    function show() {
        if (open) return;
        open = true;
        popout.hidden = false;
        setExpanded(true);
        setPreferencesView(false);
        animateShow();
    }

    function hide({ focusMain = false } = {}) {
        if (!open) return;
        open = false;
        setExpanded(false);
        if (focusMain) document.querySelector('#main-content')?.focus({ preventScroll: true });
        setPreferencesView(false);

        const finish = () => {
            popout.hidden = true;
            popout.style.removeProperty('transform');
            popout.style.removeProperty('opacity');
        };

        if (canAnimate) {
            window.gsap.killTweensOf(popout);
            window.gsap.to(popout, {
                x: 28,
                opacity: 0.92,
                duration: 0.28,
                ease: 'power3.in',
                onComplete: finish
            });
        } else {
            finish();
        }
    }

    function showPolicy(opener) {
        if (!policyModal || !policyDialog || policyOpen) return;
        policyOpen = true;
        policyReturnFocus = opener;
        applySavedPreferences(policyPreferenceInputs);
        policyModal.hidden = false;
        document.body.classList.add('cookie-policy-modal-open');
        policyOpeners.forEach(button => button.setAttribute('aria-expanded', String(button === opener)));
        policyDialog.focus({ preventScroll: true });
        animatePolicyShow();
    }

    function hidePolicy({ restoreFocus = true } = {}) {
        if (!policyOpen || !policyModal) return;
        policyOpen = false;
        document.body.classList.remove('cookie-policy-modal-open');
        policyOpeners.forEach(button => button.setAttribute('aria-expanded', 'false'));

        const finish = () => {
            policyModal.hidden = true;
            policyDialog?.style.removeProperty('transform');
            policyDialog?.style.removeProperty('opacity');
            if (restoreFocus && policyReturnFocus?.isConnected) policyReturnFocus.focus({ preventScroll: true });
            policyReturnFocus = null;
        };

        if (canAnimate && policyDialog) {
            window.gsap.killTweensOf(policyDialog);
            window.gsap.to(policyDialog, {
                y: 12,
                opacity: 0,
                duration: 0.2,
                ease: 'power3.in',
                onComplete: finish
            });
        } else {
            finish();
        }
    }

    function savePreferences(functional, analytics, { closeConsent = true } = {}) {
        const value = { version: 1, essential: true, functional, analytics };
        try {
            window.localStorage.setItem(storageKey, JSON.stringify(value));
        } catch {
            // The current-page choice still closes the notice if storage is unavailable.
        }
        window.dispatchEvent(new CustomEvent('hih:cookie-preferences-changed', { detail: value }));
        if (closeConsent) hide({ focusMain: true });
        [...preferenceInputs, ...policyPreferenceInputs].forEach(input => {
            const key = input.dataset.cookiePref ?? input.dataset.cookiePolicyPref;
            input.checked = Boolean(value[key]);
        });
    }

    function focusablePolicyElements() {
        return [...(policyDialog?.querySelectorAll('button:not(:disabled), input:not(:disabled), [href], [tabindex]:not([tabindex="-1"])') ?? [])]
            .filter(element => !element.hidden);
    }

    let popupAlreadyShown = false;
    try {
        popupAlreadyShown = window.sessionStorage.getItem(sessionKey) === 'true';
        if (!popupAlreadyShown) window.sessionStorage.setItem(sessionKey, 'true');
    } catch {
        // Show the notice when browser storage is unavailable.
    }

    if (popupAlreadyShown) {
        open = false;
        popout.hidden = true;
        setExpanded(false);
    } else {
        open = true;
        setExpanded(true);
        animateShow();
    }

    toggleButtons.forEach(button => button.addEventListener('click', () => open ? hide() : show()));
    policyOpeners.forEach(button => button.addEventListener('click', event => {
        event.preventDefault();
        showPolicy(button);
    }));
    policyCloseButtons.forEach(button => button.addEventListener('click', () => hidePolicy()));
    popout.querySelector('[data-cookie-customize]')?.addEventListener('click', () => setPreferencesView(true));
    popout.querySelector('[data-cookie-back]')?.addEventListener('click', () => {
        setPreferencesView(false);
        popout.querySelector('[data-cookie-customize]')?.focus({ preventScroll: true });
    });
    popout.querySelector('[data-cookie-accept]')?.addEventListener('click', () => savePreferences(true, true));
    popout.querySelector('[data-cookie-save]')?.addEventListener('click', () => {
        const choices = Object.fromEntries(preferenceInputs.map(input => [input.dataset.cookiePref, input.checked]));
        savePreferences(Boolean(choices.functional), Boolean(choices.analytics));
    });
    policyModal?.querySelector('[data-cookie-policy-save]')?.addEventListener('click', () => {
        const choices = Object.fromEntries(policyPreferenceInputs.map(input => [input.dataset.cookiePolicyPref, input.checked]));
        savePreferences(Boolean(choices.functional), Boolean(choices.analytics), { closeConsent: false });
        hidePolicy();
    });
    policyModal?.querySelector('.cookie-policy-backdrop')?.addEventListener('click', () => hidePolicy());

    document.addEventListener('keydown', event => {
        if (policyOpen && event.key === 'Escape') {
            hidePolicy();
            return;
        }
        if (policyOpen && event.key === 'Tab') {
            const focusable = focusablePolicyElements();
            if (!focusable.length) {
                event.preventDefault();
                policyDialog?.focus();
                return;
            }
            const first = focusable[0];
            const last = focusable[focusable.length - 1];
            if (event.shiftKey && (document.activeElement === first || document.activeElement === policyDialog)) {
                event.preventDefault();
                last.focus();
            } else if (!event.shiftKey && (document.activeElement === last || document.activeElement === policyDialog)) {
                event.preventDefault();
                first.focus();
            }
            return;
        }
        if (event.key === 'Escape' && open) hide({ focusMain: true });
    });
})();
