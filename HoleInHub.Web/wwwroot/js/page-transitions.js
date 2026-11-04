(() => {
    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)');
    const html = document.documentElement;
    let leaving = false, cover, tween, fallback;
    function reset() {
        leaving = false;
        tween?.kill();
        cover?.remove();
        clearTimeout(fallback);
        html.classList.remove('mvc-arriving');
        html.style.removeProperty('--arrival-y');
    }
    if (html.classList.contains('mvc-arriving')) {
        if (reduced.matches || !window.gsap) reset();
        else tween = gsap.to(html, { '--arrival-y': '-100%', duration: .55, ease: 'power3.inOut', onComplete: reset });
    }
    document.addEventListener('click', event => {
        const link = event.target.closest('a[href]');
        if (event.defaultPrevented || !link || event.button || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || reduced.matches || !window.gsap || link.hasAttribute('download') || link.hasAttribute('data-social-placeholder') || (link.target && link.target !== '_self')) return;
        const destination = new URL(link.href, location.href);
        if (destination.origin !== location.origin || !/^https?:$/.test(destination.protocol) || destination.hash || (destination.pathname === location.pathname && destination.search === location.search)) return;
        event.preventDefault();
        if (leaving) return;
        leaving = true;
        document.querySelector('dialog[open]')?.close();
        const navigate = () => {
            try { sessionStorage.setItem('holeinhub-page-arrival', JSON.stringify({ path: destination.pathname + destination.search, at: Date.now() })); } catch { /* Navigation also works without storage. */ }
            location.assign(destination.href);
        };
        cover = document.createElement('div');
        cover.className = 'page-departure';
        cover.setAttribute('aria-hidden', 'true');
        document.body.append(cover);
        tween = gsap.fromTo(cover, { yPercent: 100 }, { yPercent: 0, duration: .3, ease: 'power3.inOut', onComplete: navigate });
        fallback = setTimeout(reset, 8000);
    });
    window.addEventListener('pageshow', event => { if (event.persisted) reset(); });
    reduced.addEventListener('change', () => {
        if (reduced.matches) {
            if (leaving) tween?.progress(1);
            reset();
        }
    });
})();
