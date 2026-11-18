(() => {
    let collapsed = window.matchMedia('(max-width: 900px)').matches;
    try { collapsed ||= sessionStorage.getItem('holeinhub-staff-sidebar') === 'collapsed'; }
    catch { /* Storage is optional. */ }
    document.documentElement.toggleAttribute('data-staff-collapsed', collapsed);
    document.documentElement.setAttribute('data-staff-nav-ready', '');
})();
