(() => {
    const toggle = document.querySelector('.staff-rail-toggle');
    if (!toggle) return;
    const root = document.documentElement;
    const rail = document.querySelector('.staff-rail');
    const drawer = document.getElementById('staff-rail-content');
    const main = document.getElementById('main-content');
    const backdrop = document.querySelector('[data-staff-nav-backdrop]');
    root.setAttribute('data-staff-mobile-menu', '');
    const mobile = window.matchMedia('(max-width: 900px)');
    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)');
    const key = 'holeinhub-staff-sidebar';
    const scrollKey = key + '-scroll';
    const signedIn = root.getAttribute('data-staff-sign-in') === 'true';
    let savedScroll = 0;
    try {
        if (signedIn) sessionStorage.removeItem(scrollKey);
        else savedScroll = Math.max(0, Number(sessionStorage.getItem(scrollKey)) || 0);
    } catch { /* Storage is optional. */ }
    function saveScroll() {
        try { sessionStorage.setItem(scrollKey, String(drawer.scrollTop)); } catch { /* Storage is optional. */ }
    }
    drawer.addEventListener('scroll', saveScroll, { passive: true });
    let desktopCollapsed = false;
    try { desktopCollapsed = sessionStorage.getItem(key) === 'collapsed'; } catch { /* Storage is optional. */ }
    const compact = () => !mobile.matches && desktopCollapsed;
    const userFooter = rail.querySelector('.staff-user-footer');
    const userTrigger = rail.querySelector('.staff-user-trigger');
    const userMenu = document.getElementById('staff-user-menu');
    const scrollbar = rail.querySelector('[data-staff-rail-scrollbar]');

    function syncScrollbar() {
        if (!scrollbar) return;
        const { clientHeight, scrollHeight, scrollTop } = drawer;
        scrollbar.hidden = drawer.inert || !clientHeight || scrollHeight <= clientHeight;
        if (scrollbar.hidden) return;
        const bounds = drawer.getBoundingClientRect();
        const railBounds = rail.getBoundingClientRect();
        const height = Math.max(24, clientHeight * clientHeight / scrollHeight);
        const offset = (clientHeight - height) * scrollTop / (scrollHeight - clientHeight);
        scrollbar.style.height = height + 'px';
        scrollbar.style.left = bounds.right - railBounds.left - scrollbar.offsetWidth + 'px';
        scrollbar.style.top = bounds.top - railBounds.top + offset + 'px';
    }

    if (scrollbar) {
        drawer.addEventListener('scroll', syncScrollbar, { passive: true });
        const resize = new ResizeObserver(syncScrollbar);
        resize.observe(drawer);
        resize.observe(drawer.firstElementChild);
        let drag;
        scrollbar.addEventListener('pointerdown', event => {
            if (!event.isPrimary || event.button !== 0) return;
            drag = { y: event.clientY, top: drawer.scrollTop };
            scrollbar.setPointerCapture(event.pointerId);
            event.preventDefault();
        });
        scrollbar.addEventListener('pointermove', event => {
            if (!drag || !scrollbar.hasPointerCapture(event.pointerId)) return;
            const travel = drawer.clientHeight - scrollbar.offsetHeight;
            if (travel > 0) {
                drawer.scrollTop = drag.top + (event.clientY - drag.y) * (drawer.scrollHeight - drawer.clientHeight) / travel;
            }
        });
        scrollbar.addEventListener('lostpointercapture', () => { drag = null; });
    }

    function placeUserPopup() {
        if (!userMenu?.matches(':popover-open')) return;
        const anchor = userTrigger.getBoundingClientRect();
        const margin = 8;
        if (anchor.bottom <= 0 || anchor.top >= window.innerHeight) {
            userMenu.hidePopover();
            return;
        }
        userMenu.style.left = Math.max(margin, Math.min(compact() ? anchor.right + margin : anchor.left,
            window.innerWidth - userMenu.offsetWidth - margin)) + 'px';
        userMenu.style.top = Math.max(margin, Math.min(anchor.top - userMenu.offsetHeight - margin,
            window.innerHeight - userMenu.offsetHeight - margin)) + 'px';
    }
    userMenu?.addEventListener('toggle', placeUserPopup);

    const review = rail.querySelector('.staff-review-nav');
    const summary = review?.querySelector('summary');
    const pages = review?.querySelector('.staff-review-pages');
    const popupTitle = pages?.querySelector('.staff-review-popup-title');
    let reviewOpen = review?.open;
    let dropdownTween;
    function finishDropdown() {
        if (!review) return;
        dropdownTween?.kill();
        review.open = !compact() && reviewOpen;
        pages.inert = !review.open;
        pages.style.removeProperty('height');
        pages.style.removeProperty('overflow');
    }

    function resetPopup() {
        if (!pages?.hasAttribute('popover')) return;
        pages.removeAttribute('popover');
        pages.style.removeProperty('left');
        pages.style.removeProperty('top');
        popupTitle.hidden = true;
        summary.setAttribute('aria-expanded', 'false');
        finishDropdown();
    }
    function closePopup() {
        if (pages?.matches(':popover-open')) pages.hidePopover();
        resetPopup();
    }
    function placePopup() {
        if (!pages?.matches(':popover-open')) return;
        const anchor = summary.getBoundingClientRect();
        const margin = 8;
        if (anchor.bottom <= 0 || anchor.top >= window.innerHeight) { closePopup(); return; }
        const left = Math.max(margin, Math.min(anchor.right + margin, window.innerWidth - pages.offsetWidth - margin));
        const top = Math.max(margin, Math.min(anchor.top, window.innerHeight - pages.offsetHeight - margin));
        pages.style.left = left + 'px';
        pages.style.top = top + 'px';
    }

    const tooltip = document.querySelector('[data-staff-tooltip]');
    let tooltipTimer;
    function hideTooltip() {
        clearTimeout(tooltipTimer);
        tooltip.hidden = true;
    }
    function showTooltip(trigger) {
        hideTooltip();
        if (!compact() || pages?.matches(':popover-open')) return;
        tooltipTimer = setTimeout(() => {
            tooltip.textContent = trigger.dataset.railTooltip;
            tooltip.hidden = false;
            const bounds = trigger.getBoundingClientRect();
            tooltip.style.left = Math.min(bounds.right + 10, window.innerWidth - tooltip.offsetWidth - 8) + 'px';
            tooltip.style.top = Math.max(8, Math.min(bounds.top + (bounds.height - tooltip.offsetHeight) / 2,
                window.innerHeight - tooltip.offsetHeight - 8)) + 'px';
        }, 300);
    }
    rail.addEventListener('pointerover', event => {
        const trigger = event.target.closest('[data-rail-tooltip]');
        if (event.pointerType === 'mouse' && trigger && !trigger.contains(event.relatedTarget)) showTooltip(trigger);
    });
    rail.addEventListener('pointerout', event => {
        const trigger = event.target.closest('[data-rail-tooltip]');
        if (trigger && !trigger.contains(event.relatedTarget)) hideTooltip();
    });
    rail.addEventListener('focusin', event => {
        const trigger = event.target.closest('[data-rail-tooltip]');
        if (trigger?.matches(':focus-visible')) showTooltip(trigger);
    });
    rail.addEventListener('focusout', hideTooltip);
    rail.addEventListener('pointerdown', hideTooltip);
    drawer.addEventListener('scroll', () => { hideTooltip(); placePopup(); placeUserPopup(); }, { passive: true });
    window.addEventListener('resize', () => { hideTooltip(); placePopup(); placeUserPopup(); });

    function render(collapsed) {
        hideTooltip();
        closePopup();
        if (userMenu?.matches(':popover-open')) userMenu.hidePopover();
        const label = mobile.matches
            ? (collapsed ? 'Open staff navigation' : 'Close staff navigation')
            : (collapsed ? 'Expand sidebar' : 'Collapse sidebar');
        root.toggleAttribute('data-staff-collapsed', collapsed);
        const mobileOpen = mobile.matches && !collapsed;
        root.toggleAttribute('data-staff-mobile-open', mobileOpen);
        main.inert = mobileOpen;
        if (backdrop) backdrop.hidden = !mobileOpen;
        drawer.inert = mobile.matches && collapsed;
        syncScrollbar();
        if (userFooter) userFooter.inert = mobile.matches && collapsed;
        toggle.setAttribute('aria-expanded', String(!collapsed));
        const localizedLabel = window.HoleInHubText?.t(label) ?? label;
        toggle.setAttribute('aria-label', localizedLabel);
        toggle.title = localizedLabel;
        finishDropdown();
        if (compact()) summary?.setAttribute('aria-expanded', 'false');
        else summary?.removeAttribute('aria-expanded');
    }

    toggle.hidden = false;
    backdrop?.addEventListener('click', () => { render(true); toggle.focus(); });
    render(mobile.matches || desktopCollapsed);
    drawer.scrollTop = savedScroll;
    syncScrollbar();
    toggle.addEventListener('click', () => {
        root.setAttribute('data-staff-nav-interactive', '');
        const collapsed = toggle.getAttribute('aria-expanded') === 'true';
        if (!mobile.matches) {
            desktopCollapsed = collapsed;
            try { sessionStorage.setItem(key, collapsed ? 'collapsed' : 'expanded'); } catch { /* Storage is optional. */ }
        }
        render(collapsed);
    });

    pages?.addEventListener('toggle', event => {
        if (event.newState === 'closed' && !pages.matches(':popover-open')) resetPopup();
    });
    let pressedOpen = false;
    summary?.addEventListener('pointerdown', () => { pressedOpen = pages.matches(':popover-open'); });
    summary?.addEventListener('click', event => {
        event.preventDefault();
        hideTooltip();
        if (compact() && typeof pages.showPopover === 'function') {
            if ((event.detail > 0 && pressedOpen) || pages.matches(':popover-open')) {
                pressedOpen = false;
                closePopup();
                return;
            }
            pressedOpen = false;
            review.open = true;
            pages.inert = false;
            popupTitle.hidden = false;
            pages.setAttribute('popover', 'auto');
            pages.showPopover();
            placePopup();
            summary.setAttribute('aria-expanded', 'true');
            if (event.detail === 0) (pages.querySelector('[aria-current="page"]') || pages.querySelector('a'))?.focus();
            return;
        }
        if (compact()) {
            toggle.click();
            reviewOpen = true;
            finishDropdown();
            return;
        }
        reviewOpen = !reviewOpen;
        if (reduced.matches || !window.gsap) { finishDropdown(); return; }
        dropdownTween?.kill();
        const height = review.open ? pages.getBoundingClientRect().height : 0;
        review.open = true;
        pages.inert = !reviewOpen;
        pages.style.overflow = 'hidden';
        pages.style.height = height + 'px';
        dropdownTween = window.gsap.to(pages, {
            height: reviewOpen ? pages.scrollHeight : 0, duration: .28, ease: 'power3.out', onComplete: finishDropdown
        });
    });
    reduced.addEventListener('change', () => { if (!pages?.matches(':popover-open')) finishDropdown(); });
    mobile.addEventListener('change', () => {
        if (mobile.matches && rail.contains(document.activeElement)) toggle.focus();
        render(mobile.matches || desktopCollapsed);
    });
    rail.addEventListener('keydown', event => {
        if (event.key === 'Tab' && mobile.matches && toggle.getAttribute('aria-expanded') === 'true') {
            const controls = [...rail.querySelectorAll('a[href],button:not([disabled]),summary,[tabindex="0"]')]
                .filter(control => control.getClientRects().length && !control.closest('[inert]'));
            const first = controls[0], last = controls.at(-1);
            if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus(); }
            else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus(); }
        }
        if (event.key !== 'Escape') return;
        hideTooltip();
        if (pages?.matches(':popover-open')) {
            event.preventDefault();
            closePopup();
            summary.focus({ preventScroll: true });
        } else if (mobile.matches && toggle.getAttribute('aria-expanded') === 'true') {
            event.preventDefault();
            render(true);
            toggle.focus();
        }
    });

    const skeleton = main.querySelector('[data-staff-nav-loading]');
    const status = document.querySelector('[data-staff-nav-status]');
    let showTimer, resetTimer;
    let loadingContent = [];
    function resetLoading() {
        clearTimeout(showTimer);
        clearTimeout(resetTimer);
        main.removeAttribute('aria-busy');
        status.textContent = '';
        skeleton.hidden = true;
        skeleton.style.removeProperty('top');
        for (const [element, inert] of loadingContent) element.inert = inert;
        loadingContent = [];
    }
    document.addEventListener('click', event => {
        const link = event.target.closest('a[href][data-nav-layout]');
        if (!link || event.defaultPrevented || event.button || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || link.target || link.download) return;
        if (mobile.matches && rail.contains(link)) render(true);
        const url = new URL(link.href);
        if (url.origin !== window.location.origin || !/^\/(staff|manager|admin)(\/|$)/.test(url.pathname)
            || url.href === window.location.href || url.hash) return;
        const template = main.querySelector('[data-staff-nav-template="' + link.dataset.navLayout + '"]');
        if (!template) return;
        if (userMenu?.matches(':popover-open')) userMenu.hidePopover();
        closePopup();
        hideTooltip();
        resetLoading();
        skeleton.replaceChildren(template.content.cloneNode(true));
        status.textContent = 'Loading ' + (link.getAttribute('aria-label') || link.textContent.trim()) + '…';
        showTimer = setTimeout(() => {
            loadingContent = [...main.children].filter(element => element !== skeleton && element.tagName !== 'TEMPLATE')
                .map(element => [element, element.inert]);
            for (const [element] of loadingContent) element.inert = true;
            main.setAttribute('aria-busy', 'true');
            skeleton.style.top = Math.max(0, -main.getBoundingClientRect().top) + 'px';
            skeleton.hidden = false;
        }, 160);
        resetTimer = setTimeout(resetLoading, 15000);
    });
    window.addEventListener('pagehide', () => {
        saveScroll();
        resetLoading();
        closePopup();
        hideTooltip();
    });
    window.addEventListener('pageshow', event => {
        resetLoading();
        if (signedIn && !event?.persisted) {
            drawer.scrollTop = 0;
            saveScroll();
            window.scrollTo({ top: 0, left: 0, behavior: 'instant' });
        }
        syncScrollbar();
    });
    document.getElementById('account-logout-form')?.addEventListener('submit', event => {
        if (!event.submitter?.hasAttribute('data-confirm-logout')) return;
        resetLoading();
        closePopup();
        hideTooltip();
        if (mobile.matches) render(true);
    });

    const badges = [...rail.querySelectorAll('[data-request-count]')];
    const notificationBadge = badges.find(badge => badge.dataset.requestCount === 'notifications');
    function updateNotifications(count) {
        if (!notificationBadge || !Number.isSafeInteger(count) || count < 0) return;
        const label = count > 99 ? '99+' : count;
        notificationBadge.textContent = label;
        notificationBadge.hidden = count === 0;
        notificationBadge.parentElement.setAttribute('aria-label', count
            ? (window.HoleInHubText?.f('Notifications, {0} new', label) ?? ('Notifications, ' + label + ' new'))
            : (window.HoleInHubText?.t('Notifications') ?? 'Notifications'));
    }
    let notificationReadUpdates = 0;
    document.addEventListener('notifications:read', event => {
        notificationReadUpdates++;
        updateNotifications(event.detail);
    });
    let refreshingCounts = false;
    async function refreshCounts() {
        if (!review || document.hidden || refreshingCounts) return;
        refreshingCounts = true;
        const readUpdates = notificationReadUpdates;
        const controller = new AbortController();
        const timeout = setTimeout(() => controller.abort(), 10000);
        try {
            const response = await fetch(review.dataset.countsUrl, { cache: 'no-store', signal: controller.signal, redirect: 'error' });
            if (!response.ok) return;
            const counts = await response.json();
            for (const badge of badges) {
                const count = counts[badge.dataset.requestCount];
                if (!Number.isSafeInteger(count) || count < 0) continue;
                if (badge === notificationBadge) {
                    if (readUpdates === notificationReadUpdates) updateNotifications(count);
                    continue;
                }
                badge.textContent = count > 99 ? '99+' : count;
                badge.hidden = count === 0;
                if (badge.dataset.requestCount === 'awaiting') {
                    summary.setAttribute('aria-label', count
                        ? (window.HoleInHubText?.f('Review queue, {0} awaiting review', count) ?? ('Review queue, ' + count + ' awaiting review'))
                        : (window.HoleInHubText?.t('Review queue') ?? 'Review queue'));
                } else if (badge.dataset.requestCount === 'cancellations' && badge.parentElement.matches('.staff-nav > a')) {
                    badge.parentElement.setAttribute('aria-label', count
                        ? (window.HoleInHubText?.f('Cancellations, {0} pending', count) ?? ('Cancellations, ' + count + ' pending'))
                        : (window.HoleInHubText?.t('Cancellations') ?? 'Cancellations'));
                } else {
                    badge.setAttribute('aria-label', window.HoleInHubText?.f('{0} requests', count) ?? (count + ' requests'));
                }
            }
        } catch { /* Keep the last known counts until the next refresh. */ }
        finally {
            clearTimeout(timeout);
            refreshingCounts = false;
        }
    }
    if (review) {
        void refreshCounts();
        setInterval(refreshCounts, 30000);
    }
    document.addEventListener('visibilitychange', () => { if (!document.hidden) refreshCounts(); });
})();
