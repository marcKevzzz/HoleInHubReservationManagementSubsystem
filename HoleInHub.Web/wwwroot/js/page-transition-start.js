(() => {
    let arrive = document.documentElement.dataset.pageArrival === 'true';
    try {
        const key = 'holeinhub-page-arrival';
        const arrival = JSON.parse(sessionStorage.getItem(key) || 'null');
        sessionStorage.removeItem(key);
        arrive ||= Boolean(arrival && arrival.path === location.pathname + location.search && Date.now() - arrival.at < 10000);
    } catch { /* A successful sign-in can still transition without browser storage. */ }
    if (arrive && !window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
        document.documentElement.classList.add('mvc-arriving');
        // Keep the destination usable even if an enhancement script cannot load.
        setTimeout(() => document.documentElement.classList.remove('mvc-arriving'), 1500);
    }
})();
