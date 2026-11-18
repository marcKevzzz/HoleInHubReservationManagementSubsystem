(function () {
    const root = document.querySelector('[data-policy-tabs]');
    const tablist = root?.querySelector('[data-policy-tablist]');
    if (!root || !tablist) return;

    const tabs = [...tablist.querySelectorAll('[data-policy-tab]')];
    const panels = [...root.querySelectorAll('[data-policy-panel]')];
    const panelsById = new Map(panels.map(panel => [panel.id, panel]));
    const panelIds = new Set(panelsById.keys());
    if (!tabs.length || !panels.length) return;

    tablist.setAttribute('role', 'tablist');
    tablist.setAttribute('aria-orientation', 'horizontal');
    tabs.forEach((tab, index) => {
        const panelId = tab.hash.slice(1);
        const panel = panelsById.get(panelId);
        if (!panel) return;
        tab.id = `policy-tab-${panelId}`;
        tab.setAttribute('role', 'tab');
        tab.setAttribute('aria-controls', panelId);
        panel.setAttribute('role', 'tabpanel');
        panel.setAttribute('aria-labelledby', tab.id);
        panel.setAttribute('tabindex', '0');
        tab.dataset.policyTabIndex = String(index);
    });

    function resolveId(value) {
        if (value === 'booking') return 'terms';
        return panelIds.has(value) ? value : null;
    }

    function activate(panelId, { updateHash = false, focus = false } = {}) {
        const selectedId = resolveId(panelId) ?? panels[0].id;
        tabs.forEach(tab => {
            const selected = tab.hash.slice(1) === selectedId;
            tab.setAttribute('aria-selected', String(selected));
            tab.tabIndex = selected ? 0 : -1;
        });
        panels.forEach(panel => { panel.hidden = panel.id !== selectedId; });

        const selectedTab = tabs.find(tab => tab.hash.slice(1) === selectedId);
        if (focus) selectedTab?.focus();
        if (updateHash && selectedTab) {
            const nextUrl = new URL(window.location.href);
            nextUrl.hash = selectedId;
            window.history.replaceState(null, '', nextUrl);
        }
    }

    function currentPanelId() {
        const queryTab = new URLSearchParams(window.location.search).get('tab');
        return resolveId(queryTab ?? window.location.hash.slice(1)) ?? 'privacy';
    }

    tabs.forEach((tab, index) => {
        tab.addEventListener('click', event => {
            event.preventDefault();
            activate(tab.hash.slice(1), { updateHash: true });
        });
        tab.addEventListener('keydown', event => {
            let nextIndex = index;
            if (event.key === 'ArrowRight') nextIndex = (index + 1) % tabs.length;
            else if (event.key === 'ArrowLeft') nextIndex = (index - 1 + tabs.length) % tabs.length;
            else if (event.key === 'Home') nextIndex = 0;
            else if (event.key === 'End') nextIndex = tabs.length - 1;
            else return;

            event.preventDefault();
            activate(tabs[nextIndex].hash.slice(1), { updateHash: true, focus: true });
        });
    });

    window.addEventListener('hashchange', () => activate(currentPanelId()));
    activate(currentPanelId());
})();
