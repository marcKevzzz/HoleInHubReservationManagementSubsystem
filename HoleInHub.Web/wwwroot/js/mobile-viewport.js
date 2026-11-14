(() => {
    const viewport = window.visualViewport;
    if (!viewport) return;
    const root = document.documentElement;
    function sync() {
        root.style.setProperty('--app-viewport-height', viewport.height + 'px');
        root.style.setProperty('--app-viewport-top', viewport.offsetTop + 'px');
    }
    viewport.addEventListener('resize', sync, {passive:true});
    viewport.addEventListener('scroll', sync, {passive:true});
    sync();
})();
