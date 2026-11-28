(() => {
    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)');
    const html = document.documentElement;
    if (html.dataset?.skipPageTransition === 'true') return;
    const accountRoute = /^\/(?:my-reservations|transactions|my-profile|notifications|reservations(?:\/index)?|guest\/reservations)\/?$/i;
    let leaving = false, cover, tween, pausedScroller;
    function pauseScrolling() {
        const smoother = window.ScrollSmoother?.get();
        if (smoother && !smoother.paused()) {
            smoother.paused(true);
            pausedScroller = smoother;
        }
    }
    function reset() {
        leaving = false;
        tween?.kill();
        cover?.remove();
        html.style.removeProperty('--wipe-logo');
        html.classList.remove('mvc-arriving', 'mvc-intro', 'mvc-leaving');
        pausedScroller?.paused(false);
        pausedScroller = null;
        html.style.removeProperty('--arrival-y');
    }
    if (html.classList.contains('mvc-arriving')) {
        if (reduced.matches || !window.gsap) reset();
        else {
            html.setAttribute('data-wipe-ready', '');
            const reveal = () => {
                if (!html.classList.contains('mvc-arriving')) return;
                pauseScrolling();
                tween = gsap.timeline({ onComplete: reset });
                if (html.classList.contains('mvc-intro')) {
                    tween.fromTo(html, { '--wipe-logo': 0 }, { '--wipe-logo': 1, duration: .22, ease: 'sine.inOut' });
                }
                tween.to(html, { '--wipe-logo': 0, duration: .18, ease: 'sine.inOut' })
                    .fromTo(html, { '--arrival-y': '0%' }, { '--arrival-y': '-100%', duration: .42, ease: 'power2.inOut' });
            };
            if (document.readyState !== 'loading') reveal();
            else document.addEventListener('DOMContentLoaded', reveal, { once: true });
        }
    }
    document.addEventListener('click', event => {
        const link = event.target.closest('a[href]');
        if (link?.hasAttribute('data-booking-media') || event.defaultPrevented || !link || event.button || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || reduced.matches || !window.gsap || link.hasAttribute('download') || link.hasAttribute('data-social-placeholder') || (link.target && link.target !== '_self')) return;
        const destination = new URL(link.href, location.href);
        if (destination.origin !== location.origin || !/^https?:$/.test(destination.protocol) || destination.hash ||
            (accountRoute.test(location.pathname) && accountRoute.test(destination.pathname)) ||
            (destination.pathname === location.pathname && destination.search === location.search)) return;
        event.preventDefault();
        if (leaving) return;
        reset();
        leaving = true;
        html.classList.add('mvc-leaving');
        pauseScrolling();
        document.querySelector('dialog[open]')?.close();
        const navigate = () => {
            try { sessionStorage.setItem('holeinhub-page-arrival', JSON.stringify({ path: destination.pathname + destination.search, at: Date.now() })); } catch { /* Navigation also works without storage. */ }
            location.assign(destination.href);
        };
        cover = document.createElement('div');
        cover.className = 'page-departure';
        cover.setAttribute('aria-hidden', 'true');
        document.body.append(cover);
        tween = gsap.timeline({ onComplete: navigate })
            .fromTo(cover, { yPercent: 100 }, { yPercent: 0, duration: .3, ease: 'power3.inOut' })
            .fromTo(cover, { '--wipe-logo': 0 }, { '--wipe-logo': 1, duration: .2 }, .1);
    });
    document.addEventListener('keydown', event => {
        if (event.key === 'Escape' && leaving) reset();
    });
    window.addEventListener('pageshow', event => { if (event.persisted) reset(); });
    reduced.addEventListener('change', () => {
        if (reduced.matches) {
            if (leaving) tween?.progress(1);
            reset();
        }
    });
})();
