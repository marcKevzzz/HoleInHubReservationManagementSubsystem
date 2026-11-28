(() => {
    const { remaining, statusUrl, loginUrl } = document.currentScript.dataset;
    let timer, deadline, checking = false;
    function schedule(ms) {
        clearTimeout(timer);
        deadline = Date.now() + ms;
        timer = setTimeout(check, Math.min(ms + 250, 2147483647));
    }
    async function check() {
        if (checking) return;
        checking = true;
        try {
            const response = await fetch(statusUrl, { cache: 'no-store', credentials: 'same-origin' });
            if (response.status === 401) {
                const url = new URL(loginUrl, location.origin);
                url.searchParams.set('returnUrl', location.pathname + location.search);
                location.replace(url);
                return;
            }
            if (!response.ok) throw new Error();
            schedule((await response.json()).remainingMs);
        } catch {
            schedule(15000);
        } finally {
            checking = false;
        }
    }
    schedule(Number(remaining));
    document.addEventListener('visibilitychange', () => {
        if (!document.hidden && Date.now() >= deadline) check();
    });
})();
