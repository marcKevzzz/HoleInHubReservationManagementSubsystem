(() => {
    if (!window.ResizeObserver) return;
    for (const nav of document.querySelectorAll('.account-navigation, .account-reservation-tabs')) {
        const current = nav.querySelector('a[aria-current="page"]');
        if (!current) continue;

        let hovered;
        function position() {
            const link = hovered ?? nav.querySelector('a:focus-visible') ?? current;
            const bounds = link.getBoundingClientRect();
            const left = bounds.left - nav.getBoundingClientRect().left + nav.scrollLeft;
            nav.style.setProperty('--account-indicator-x', left + 'px');
            nav.style.setProperty('--account-indicator-width', bounds.width + 'px');
        }
        nav.addEventListener('pointerover', event => {
            if (event.pointerType === 'touch') return;
            hovered = event.target.closest('a');
            position();
        });
        nav.addEventListener('pointerleave', () => { hovered = null; position(); });
        nav.addEventListener('focusin', position);
        nav.addEventListener('focusout', () => requestAnimationFrame(position));
        const resize = new ResizeObserver(position);
        resize.observe(nav);
        nav.querySelectorAll('a').forEach(link => resize.observe(link));
        position();
        nav.setAttribute('data-hover-indicator', '');
    }
})();
