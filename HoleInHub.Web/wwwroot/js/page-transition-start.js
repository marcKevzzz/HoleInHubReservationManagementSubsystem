(() => {
    let arrive = document.documentElement.dataset.pageArrival === 'true';
    try {
        const key = 'holeinhub-page-arrival';
        const arrival = JSON.parse(sessionStorage.getItem(key) || 'null');
        sessionStorage.removeItem(key);
        arrive ||= Boolean(arrival && arrival.path === location.pathname + location.search && Date.now() - arrival.at < 120000);
    } catch { /* A successful sign-in can still transition without browser storage. */ }
    const homeIntro = !arrive && /^\/(?:home(?:\/index)?\/?)?$/i.test(location.pathname);
    if (document.documentElement.dataset.skipPageTransition !== 'true' && (arrive || homeIntro) && !window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
        if (homeIntro) document.documentElement.classList.add('mvc-intro');
        document.documentElement.classList.add('mvc-arriving');
        // Keep the destination usable even if an enhancement script cannot load.
        window.addEventListener('load', () => {
            if (!document.documentElement.hasAttribute('data-wipe-ready')) document.documentElement.classList.remove('mvc-arriving');
        }, { once: true });
    }
})();
