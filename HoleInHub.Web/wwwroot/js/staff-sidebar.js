(() => {
    const toggle = document.querySelector('.staff-rail-toggle');
    if (!toggle) return;
    const rail = document.querySelector('.staff-rail');
    const drawer = document.getElementById('staff-rail-content');
    const mobile = window.matchMedia('(max-width: 720px)');
    const key = 'holeinhub-staff-sidebar';
    let desktopCollapsed = false;
    try { desktopCollapsed = sessionStorage.getItem(key) === 'collapsed'; } catch { /* Storage is optional. */ }

    function render(collapsed) {
        const label = mobile.matches
            ? (collapsed ? 'Open staff navigation' : 'Close staff navigation')
            : (collapsed ? 'Expand sidebar' : 'Collapse sidebar');
        document.body.toggleAttribute('data-staff-collapsed', collapsed);
        drawer.inert = mobile.matches && collapsed;
        toggle.setAttribute('aria-expanded', String(!collapsed));
        toggle.setAttribute('aria-label', label);
        toggle.title = label;
    }

    toggle.hidden = false;
    render(mobile.matches || desktopCollapsed);
    toggle.addEventListener('click', () => {
        document.body.setAttribute('data-staff-nav-interactive', '');
        const collapsed = toggle.getAttribute('aria-expanded') === 'true';
        if (!mobile.matches) {
            desktopCollapsed = collapsed;
            try { sessionStorage.setItem(key, collapsed ? 'collapsed' : 'expanded'); } catch { /* Storage is optional. */ }
        }
        render(collapsed);
    });
    mobile.addEventListener('change', () => {
        if (mobile.matches && rail.contains(document.activeElement)) toggle.focus();
        render(mobile.matches || desktopCollapsed);
    });
    rail.addEventListener('keydown', event => {
        if (event.key !== 'Escape' || !mobile.matches || toggle.getAttribute('aria-expanded') !== 'true') return;
        event.preventDefault();
        render(true);
        toggle.focus();
    });
})();
