(() => {
    const trigger = document.querySelector('.profile-link');
    const popup = document.querySelector('#account-menu');
    if (!trigger || !popup || typeof popup.showPopover !== 'function') return;
    const confirmation = document.querySelector('#logout-confirmation');
    const logoutForm = document.querySelector('#account-logout-form');
    let returnToPopup = false;
    let openOnPointerDown = false;
    trigger.addEventListener('pointerdown', () => { openOnPointerDown = popup.matches(':popover-open'); });
    trigger.setAttribute('aria-controls', popup.id);
    trigger.setAttribute('aria-expanded', 'false');
    function open() {
        popup.showPopover();
        trigger.setAttribute('aria-expanded', 'true');
        window.HoleInHubMotion?.enter(popup, { y: -8, opacity: .8, duration: .2 });
    }
    trigger.addEventListener('click', event => {
        if (event.button || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        event.preventDefault();
        const wasOpen = event.detail > 0 ? openOnPointerDown : popup.matches(':popover-open');
        openOnPointerDown = false;
        if (wasOpen) popup.hidePopover();
        else {
            open();
            if (event.detail === 0) popup.querySelector('a[href]:not([aria-disabled=true]),button')?.focus({ preventScroll: true });
        }
    });
    popup.addEventListener('toggle', event => trigger.setAttribute('aria-expanded', String(event.newState === 'open')));
    popup.addEventListener('click', event => {
        if (event.target.closest('[data-account-placeholder]')) event.preventDefault();
    });
    popup.addEventListener('keydown', event => {
        if (event.key === 'Escape') {
            event.preventDefault();
            event.stopPropagation();
            popup.hidePopover();
            trigger.focus({ preventScroll: true });
        }
    });
    // Tab moves out of this nonmodal popup normally; native popover handles outside clicks and Escape.
    popup.addEventListener('focusout', event => {
        if (event.relatedTarget && !popup.contains(event.relatedTarget) && event.relatedTarget !== trigger) popup.hidePopover();
    });
    if (confirmation && logoutForm) {
        logoutForm.addEventListener('submit', event => {
            if (event.submitter?.hasAttribute('data-confirm-logout')) { returnToPopup = false; return; }
            event.preventDefault();
            popup.hidePopover();
            returnToPopup = true;
            confirmation.showModal();
            window.ScrollSmoother?.get()?.paused(true);
        });
        confirmation.querySelectorAll('[data-cancel-logout]').forEach(button => button.addEventListener('click', () => confirmation.close()));
        confirmation.addEventListener('close', () => {
            window.ScrollSmoother?.get()?.paused(document.querySelector('#site-menu')?.open ?? false);
            if (!returnToPopup) return;
            returnToPopup = false;
            open();
            logoutForm.querySelector('button').focus();
        });
    }

    window.addEventListener('scroll', () => { if (popup.matches(':popover-open')) popup.hidePopover(); }, { passive: true });
    window.addEventListener('pageshow', () => {
        returnToPopup = false;
        popup.hidePopover();
        if (confirmation?.open) confirmation.close();
    });
})();
