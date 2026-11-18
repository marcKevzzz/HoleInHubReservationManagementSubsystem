(function () {
    const triggers = [...document.querySelectorAll('[data-open-policy-dialog]')];
    if (!triggers.length) return;

    const modals = new Map([...document.querySelectorAll('[data-legal-modal]')]
        .map(modal => [modal.dataset.legalModal, modal]));
    const reducedMotion = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false;
    const canAnimate = !reducedMotion && typeof window.gsap !== 'undefined';
    let activeModal = null;
    let returnFocus = null;

    function focusableElements(modal) {
        return [...modal.querySelectorAll('a[href], button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex]:not([tabindex="-1"])')]
            .filter(element => !element.hidden && element.getAttribute('aria-hidden') !== 'true');
    }

    function openPolicy(name, trigger) {
        const modal = modals.get(name);
        const dialog = modal?.querySelector('[role="dialog"]');
        if (!modal || !dialog || activeModal) return;

        activeModal = modal;
        returnFocus = trigger;
        modal.classList.add('is-open');
        modal.style.display = 'grid';
        document.body.classList.add('legal-policy-open');
        triggers.forEach(link => link.setAttribute('aria-expanded', String(link === trigger)));
        dialog.focus({ preventScroll: true });

        if (canAnimate) {
            window.gsap.killTweensOf(dialog);
            window.gsap.fromTo(dialog, { y: 22, scale: 0.975, opacity: 0 }, {
                y: 0, scale: 1, opacity: 1, duration: 0.42, ease: 'power3.out', clearProps: 'transform,opacity'
            });
        } else if (!reducedMotion && typeof dialog.animate === 'function') {
            dialog.animate([
                { transform: 'translateY(22px) scale(.975)', opacity: 0 },
                { transform: 'translateY(0) scale(1)', opacity: 1 }
            ], { duration: 420, easing: 'cubic-bezier(.16, 1, .3, 1)' });
        }
    }

    function closePolicy() {
        if (!activeModal) return;
        const modal = activeModal;
        const dialog = modal.querySelector('[role="dialog"]');
        activeModal = null;
        modal.classList.remove('is-open');
        modal.style.removeProperty('display');
        document.body.classList.remove('legal-policy-open');
        triggers.forEach(link => link.setAttribute('aria-expanded', 'false'));
        if (window.location.hash === `#${modal.id}`) {
            window.history.replaceState(null, '', `${window.location.pathname}${window.location.search}`);
        }

        const finish = () => {
            dialog?.style.removeProperty('transform');
            dialog?.style.removeProperty('opacity');
            if (returnFocus?.isConnected) returnFocus.focus({ preventScroll: true });
            returnFocus = null;
        };

        if (canAnimate && dialog) {
            window.gsap.killTweensOf(dialog);
            window.gsap.to(dialog, { y: 12, opacity: 0, duration: 0.2, ease: 'power3.in', onComplete: finish });
        } else {
            finish();
        }
    }

    document.addEventListener('click', event => {
        const trigger = event.target instanceof Element ? event.target.closest('[data-open-policy-dialog]') : null;
        if (!trigger || !triggers.includes(trigger)) return;
        event.preventDefault();
        event.stopImmediatePropagation();
        openPolicy(trigger.dataset.openPolicyDialog, trigger);
    }, true);

    modals.forEach(modal => {
        if (window.location.hash === `#${modal.id}`) {
            openPolicy(modal.dataset.legalModal, null);
        }
    });

    modals.forEach(modal => {
        modal.querySelectorAll('[data-legal-close], [data-legal-backdrop]').forEach(control => {
            control.addEventListener('click', event => {
                event.preventDefault();
                closePolicy();
            });
        });
    });

    document.addEventListener('keydown', event => {
        if (!activeModal) return;
        if (event.key === 'Escape') {
            event.preventDefault();
            closePolicy();
            return;
        }
        if (event.key !== 'Tab') return;

        const focusable = focusableElements(activeModal);
        const dialog = activeModal.querySelector('[role="dialog"]');
        if (!focusable.length) {
            event.preventDefault();
            dialog?.focus();
            return;
        }
        const first = focusable[0];
        const last = focusable[focusable.length - 1];
        if (event.shiftKey && (document.activeElement === first || document.activeElement === dialog)) {
            event.preventDefault();
            last.focus();
        } else if (!event.shiftKey && (document.activeElement === last || document.activeElement === dialog)) {
            event.preventDefault();
            first.focus();
        }
    });
})();
