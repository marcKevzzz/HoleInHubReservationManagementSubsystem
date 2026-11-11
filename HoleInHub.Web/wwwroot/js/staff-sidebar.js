(() => {
    const toggle = document.querySelector('.staff-rail-toggle');
    if (!toggle) return;
    const rail = document.querySelector('.staff-rail');
    const drawer = document.getElementById('staff-rail-content');
    const mobile = window.matchMedia('(max-width: 720px)');
    const key = 'holeinhub-staff-sidebar';
    let desktopCollapsed = false;
    try { desktopCollapsed = sessionStorage.getItem(key) === 'collapsed'; } catch { /* Storage is optional. */ }

    function render(collapsed) {
        const label = mobile.matches
            ? (collapsed ? 'Open staff navigation' : 'Close staff navigation')
            : (collapsed ? 'Expand sidebar' : 'Collapse sidebar');
        document.body.toggleAttribute('data-staff-collapsed', collapsed);
        drawer.inert = mobile.matches && collapsed;
        toggle.setAttribute('aria-expanded', String(!collapsed));
        toggle.setAttribute('aria-label', label);
        toggle.title = label;
    }

    toggle.hidden = false;
    render(mobile.matches || desktopCollapsed);
    toggle.addEventListener('click', () => {
        document.body.setAttribute('data-staff-nav-interactive', '');
        const collapsed = toggle.getAttribute('aria-expanded') === 'true';
        if (!mobile.matches) {
            desktopCollapsed = collapsed;
            try { sessionStorage.setItem(key, collapsed ? 'collapsed' : 'expanded'); } catch { /* Storage is optional. */ }
        }
        render(collapsed);
    });
    const review = rail.querySelector('.staff-review-nav');
    const pages = review?.querySelector('.staff-review-pages');
    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)');
    let reviewOpen = review?.open;
    let dropdownTween;
    function finishDropdown() {
        dropdownTween?.kill();
        review.open = reviewOpen;
        pages.inert = !reviewOpen;
        pages.style.removeProperty('height');
        pages.style.removeProperty('overflow');
    }
    review?.querySelector('summary').addEventListener('click', event => {
        event.preventDefault();
        if (!mobile.matches && desktopCollapsed) {
            desktopCollapsed = false;
            try { sessionStorage.setItem(key, 'expanded'); } catch { /* Storage is optional. */ }
            render(false);
            reviewOpen = true;
            finishDropdown();
            return;
        }
        reviewOpen = !reviewOpen;
        if (reduced.matches || !window.gsap) {
            finishDropdown();
            return;
        }
        dropdownTween?.kill();
        const height = review.open ? pages.getBoundingClientRect().height : 0;
        review.open = true;
        pages.inert = !reviewOpen;
        pages.style.overflow = 'hidden';
        pages.style.height = height + 'px';
        dropdownTween = window.gsap.to(pages, {
            height: reviewOpen ? pages.scrollHeight : 0,
            duration: .28,
            ease: 'power3.out',
            onComplete: finishDropdown
        });
    });
    reduced.addEventListener('change', () => { if (review) finishDropdown(); });
    let refreshingCounts = false;
    async function refreshCounts() {
        if (!review || document.hidden || refreshingCounts) return;
        refreshingCounts = true;
        const controller = new AbortController();
        const timeout = setTimeout(() => controller.abort(), 10000);
        try {
            const response = await fetch(review.dataset.countsUrl, { cache: 'no-store', signal: controller.signal });
            if (!response.ok) return;
            const counts = await response.json();
            for (const badge of rail.querySelectorAll('[data-request-count]')) {
                const count = counts[badge.dataset.requestCount];
                if (!Number.isSafeInteger(count) || count < 0) continue;
                badge.textContent = count;
                badge.hidden = count === 0;
                badge.setAttribute('aria-label', count + ' requests');
            }
        } catch { /* Keep the last known counts until the next refresh. */ }
        finally {
            clearTimeout(timeout);
            refreshingCounts = false;
        }
    }
    if (review) setInterval(refreshCounts, 30000);
    document.addEventListener('visibilitychange', () => { if (!document.hidden) refreshCounts(); });
    mobile.addEventListener('change', () => {
        if (mobile.matches && rail.contains(document.activeElement)) toggle.focus();
        render(mobile.matches || desktopCollapsed);
    });
    rail.addEventListener('keydown', event => {
        if (event.key !== 'Escape' || !mobile.matches || toggle.getAttribute('aria-expanded') !== 'true') return;
        event.preventDefault();
        render(true);
        toggle.focus();
    });
})();
