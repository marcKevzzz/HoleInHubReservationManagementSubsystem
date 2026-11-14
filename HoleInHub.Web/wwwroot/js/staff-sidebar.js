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
    let desktopCollapsed = false;
    try { desktopCollapsed = sessionStorage.getItem(key) === 'collapsed'; } catch { /* Storage is optional. */ }
    const compact = () => !mobile.matches && desktopCollapsed;
    const languageTrigger = rail.querySelector('.staff-language-trigger');
    const languageOptions = document.getElementById('staff-language-options');

    function placeLanguagePopup() {
        if (!languageOptions?.matches(':popover-open')) return;
        const anchor = languageTrigger.getBoundingClientRect();
        const margin = 8;
        if (anchor.bottom <= 0 || anchor.top >= window.innerHeight) {
            languageOptions.hidePopover();
            return;
        }
        languageOptions.style.left = Math.max(margin, Math.min(anchor.right + margin,
            window.innerWidth - languageOptions.offsetWidth - margin)) + 'px';
        languageOptions.style.top = Math.max(margin, Math.min(anchor.top,
            window.innerHeight - languageOptions.offsetHeight - margin)) + 'px';
    }
    languageOptions?.addEventListener('toggle', placeLanguagePopup);

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
    drawer.addEventListener('scroll', () => { hideTooltip(); placePopup(); placeLanguagePopup(); }, { passive: true });
    window.addEventListener('resize', () => { hideTooltip(); placePopup(); placeLanguagePopup(); });

    function render(collapsed) {
        hideTooltip();
        closePopup();
        if (languageOptions && typeof languageOptions.showPopover === 'function') {
            if (languageOptions.matches(':popover-open')) languageOptions.hidePopover();
            languageOptions.toggleAttribute('popover', compact());
            languageTrigger.hidden = !compact();
        }
        const label = mobile.matches
            ? (collapsed ? 'Open staff navigation' : 'Close staff navigation')
            : (collapsed ? 'Expand sidebar' : 'Collapse sidebar');
        root.toggleAttribute('data-staff-collapsed', collapsed);
        const mobileOpen = mobile.matches && !collapsed;
        root.toggleAttribute('data-staff-mobile-open', mobileOpen);
        main.inert = mobileOpen;
        if (backdrop) backdrop.hidden = !mobileOpen;
        drawer.inert = mobile.matches && collapsed;
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
    const railScrollKey = () => key + (compact() ? '-compact-scroll' : '-scroll');
    if (!mobile.matches) {
        try { drawer.scrollTop = Number(sessionStorage.getItem(railScrollKey())) || 0; } catch { /* Storage is optional. */ }
    }
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
    function resetLoading() {
        clearTimeout(showTimer);
        clearTimeout(resetTimer);
        main.removeAttribute('aria-busy');
        status.textContent = '';
        skeleton.hidden = true;
    }
    rail.addEventListener('click', event => {
        const link = event.target.closest('a[href]');
        if (!link || event.defaultPrevented || event.button || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || link.target || link.download) return;
        if (mobile.matches) render(true);
        const url = new URL(link.href);
        if (url.origin !== window.location.origin || !/^\/(staff|manager|admin)(\/|$)/.test(url.pathname)
            || url.href === window.location.href || url.hash) return;
        const template = main.querySelector('[data-staff-nav-template="' + link.dataset.navLayout + '"]');
        if (!template) return;
        resetLoading();
        skeleton.replaceChildren(template.content.cloneNode(true));
        status.textContent = 'Loading ' + (link.getAttribute('aria-label') || link.textContent.trim()) + '…';
        showTimer = setTimeout(() => {
            main.setAttribute('aria-busy', 'true');
            skeleton.hidden = false;
        }, 160);
        resetTimer = setTimeout(resetLoading, 15000);
    });
    window.addEventListener('pagehide', () => {
        resetLoading();
        closePopup();
        hideTooltip();
        if (!mobile.matches) {
            try { sessionStorage.setItem(railScrollKey(), String(drawer.scrollTop)); } catch { /* Storage is optional. */ }
        }
    });
    window.addEventListener('pageshow', resetLoading);

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
