(() => {
    const listeners = new Set();
    let source, debounce;
    function notify() {
        clearTimeout(debounce);
        debounce = setTimeout(() => {
            if (!document.hidden) listeners.forEach(listener => listener());
        }, 200);
    }
    function connect() {
        if (source || !window.EventSource || !listeners.size) return;
        source = new EventSource('/staff/live');
        source.addEventListener('change', notify);
    }
    // Reconcile missed events and environments without change-stream permissions.
    const reconcile = setInterval(notify, 15000);
    window.HoleInHubLive = {
        subscribe(listener) {
            listeners.add(listener); connect();
            return () => { listeners.delete(listener); if (!listeners.size) { source?.close(); source = null; } };
        }
    };
    document.addEventListener('visibilitychange', () => { if (!document.hidden) { connect(); notify(); } });
    window.addEventListener('pagehide', () => { clearTimeout(debounce); source?.close(); source = null; });
    window.addEventListener('pageshow', event => { if (event.persisted) { connect(); notify(); } });
})();
