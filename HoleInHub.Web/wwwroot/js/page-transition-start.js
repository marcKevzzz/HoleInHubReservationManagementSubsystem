(() => {
    try {
        const key = 'holeinhub-account-loading';
        const pending = JSON.parse(sessionStorage.getItem(key) || 'null');
        sessionStorage.removeItem(key);
        if (pending?.path === location.pathname && Date.now() - pending.at < 10000) {
            document.documentElement.classList.add('account-arriving');
            setTimeout(() => document.documentElement.classList.remove('account-arriving'), 2000);
        }
    } catch { /* Native navigation remains usable without browser storage. */ }
    let arrive = document.documentElement.dataset.pageArrival === 'true';
    try {
        const key = 'holeinhub-page-arrival';
        const arrival = JSON.parse(sessionStorage.getItem(key) || 'null');
        sessionStorage.removeItem(key);
        arrive ||= Boolean(arrival && arrival.path === location.pathname + location.search && Date.now() - arrival.at < 10000);
    } catch { /* A successful sign-in can still transition without browser storage. */ }
    if (document.documentElement.dataset.skipPageTransition !== 'true' && arrive && !window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
        document.documentElement.classList.add('mvc-arriving');
        // Keep the destination usable even if an enhancement script cannot load.
        setTimeout(() => document.documentElement.classList.remove('mvc-arriving'), 1500);
    }
})();
