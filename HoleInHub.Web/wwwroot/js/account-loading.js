(() => {
    const hub = document.querySelector('.account-hub');
    if (!hub) return;
    let skeleton, timeout;
    function reset() {
        clearTimeout(timeout);
        skeleton?.remove();
        skeleton = null;
        hub.removeAttribute('data-loading');
        hub.removeAttribute('aria-busy');
    }
    function show(path = location.pathname) {
        if (skeleton) return;
        skeleton = document.createElement('div');
        skeleton.className = 'account-loading-skeleton';
        skeleton.setAttribute('role', 'status');
        const label = document.createElement('span');
        label.className = 'sr-only';
        label.textContent = document.documentElement.lang === 'ko' ? '불러오는 중…' : 'Loading account page…';
        skeleton.append(label);
        const shapes = path === '/my-profile' ? ['title', 'description', 'profile'] :
            path === '/transactions' ? ['title', 'description', 'empty-title', 'empty-copy', 'link'] :
            path === '/notifications' ? ['title', 'description', 'filters', 'notification', 'notification', 'notification'] :
            ['title', 'description', 'filters', 'reservation', 'reservation'];
        for (const name of shapes) {
            const block = document.createElement('div');
            block.className = 'account-skeleton-block account-skeleton-' + name;
            block.setAttribute('aria-hidden', 'true');
            if (name === 'profile') block.innerHTML = '<div class="account-skeleton-section"></div><div class="account-skeleton-fields"><div></div><div></div><div></div><div></div><div class="wide"></div><div class="wide"></div><div class="account-skeleton-save"></div></div>';
            skeleton.append(block);
        }
        hub.append(skeleton);
        hub.setAttribute('data-loading', '');
        hub.setAttribute('aria-busy', 'true');
        timeout = setTimeout(reset, 8000);
    }
    document.addEventListener('click', event => {
        const link = event.target.closest('.account-navigation a, .account-reservation-tabs a, .account-inbox-pager a');
        if (!link || event.defaultPrevented || event.button || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey ||
            link.hasAttribute('download') || (link.target && link.target !== '_self')) return;
        const url = new URL(link.href, location.href);
        if (url.origin !== location.origin || url.href === location.href || url.hash) return;
        show(url.pathname);
        try { sessionStorage.setItem('holeinhub-account-loading', JSON.stringify({ path: url.pathname, at: Date.now() })); } catch { /* Skeletons still work without storage. */ }
    });
    document.addEventListener('submit', event => {
        if (!event.defaultPrevented && event.target.matches('.account-profile-form')) show();
    });
    window.addEventListener('pageshow', reset);
    window.addEventListener('pagehide', reset);
    function revealHeading() {
        if (!window.gsap || window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;
        const heading = hub.querySelector('.account-heading');
        if (!heading) return;
        const words = heading.querySelectorAll('h1 .split-word');
        window.gsap.fromTo(words.length ? words : heading.querySelector('h1'),
            { yPercent: 110 }, { yPercent: 0, duration: .85, stagger: .07, ease: 'power3.out', clearProps: 'transform' });
        window.gsap.fromTo(heading.querySelector('p'),
            { y: 16, opacity: 0 }, { y: 0, opacity: 1, duration: .65, delay: .2, ease: 'power3.out', clearProps: 'transform,opacity' });
    }
    if (document.documentElement.classList?.contains('account-arriving')) {
        show();
        const ready = document.fonts ? Promise.all([document.fonts.load('16px Inter'), document.fonts.load('40px Oswald')]) : Promise.resolve();
        Promise.race([ready.catch(() => {}), new Promise(resolve => setTimeout(resolve, 1500))]).then(() => {
            reset();
            document.documentElement.classList.remove('account-arriving');
            revealHeading();
        });
    } else (document.fonts?.ready ?? Promise.resolve()).then(revealHeading);
})();
