(() => {
    function mount() {
    const form = document.querySelector('[data-staff-search]');
    const results = document.querySelector('[data-staff-search-results]');
    if (!form || !results) return;

    const input = form.elements.reference;
    let timer;
    let request;
    const enhanced = typeof results.showPopover === 'function';
    let changing = false;
    if (enhanced) results.setAttribute('popover', 'auto');
    input.setAttribute('aria-expanded', 'false');
    function onScroll(event) { if (!results.contains(event.target)) position(); }
    function position() {
        if (!enhanced) return;
        const rect = form.getBoundingClientRect();
        const panel = document.querySelector('.staff-workspace')?.getBoundingClientRect?.() || { left: 0, width: window.innerWidth };
        const width = Math.min(window.innerWidth - 32, panel.width * .9);
        const left = Math.max(16, Math.min(window.innerWidth - width - 16, panel.left + (panel.width - width) / 2));
        results.style.cssText = `left:${left}px;top:${rect.bottom + 12}px;width:${width}px;max-height:${Math.max(120, window.innerHeight - rect.bottom - 30)}px`;
    }
    function reveal() {
        if (changing || !input.value.trim() || !results.innerHTML.trim()) return;
        if (enhanced) {
            changing = true;
            try { position(); if (!results.matches(':popover-open')) results.showPopover(); }
            finally { changing = false; }
        }
        input.setAttribute('aria-expanded', 'true');
    }
    function close() {
        if (enhanced) {
            changing = true;
            try { results.hidePopover(); } finally { changing = false; }
        }
        input.setAttribute('aria-expanded', 'false');
    }
    if (enhanced) {
        results.addEventListener('toggle', event => input.setAttribute('aria-expanded', String(event.newState === 'open')));
        results.addEventListener('click', event => { if (event.target.closest('a,[data-close-search]')) close(); });
        window.addEventListener('resize', position);
        window.addEventListener('scroll', onScroll, true);
        results.addEventListener('keydown', event => {
            if (event.key === 'Escape') { close(); input.focus(); return; }
            if (!['ArrowDown', 'ArrowUp'].includes(event.key)) return;
            event.preventDefault();
            const links = [...results.querySelectorAll('a')];
            const next = links.indexOf(document.activeElement) + (event.key === 'ArrowDown' ? 1 : -1);
            if (next < 0) input.focus();
            else links[Math.min(next, links.length - 1)]?.focus();
        });
    }
    input.addEventListener('focus', reveal);
    input.addEventListener('click', reveal);
    input.addEventListener('keydown', event => {
        if (event.key === 'Escape') { close(); return; }
        if (event.key === 'ArrowDown') { event.preventDefault(); reveal(); results.querySelector('a')?.focus(); }
    });
    input.addEventListener('compositionend', () => { reset(); timer = setTimeout(search, 300); });

    async function search() {
        const reference = input.value.trim();
        if (!reference) return;
        request = new AbortController();
        const { signal } = request;
        const url = new URL(form.dataset.staffSearch, window.location.href);
        url.searchParams.set('reference', reference);
        url.searchParams.set('date', form.elements.date.value);
        results.setAttribute('aria-busy', 'true');
        try {
            const response = await fetch(url, { signal, redirect: 'error' });
            if (!response.ok) throw new Error('Search failed');
            const html = await response.text();
            if (!signal.aborted) { results.innerHTML = html; reveal(); }
        } catch {
            if (!signal.aborted) {
                const message = document.createElement('p');
                message.textContent = 'Booking search is unavailable. Press Enter to try again.';
                results.replaceChildren(message);
                reveal();
            }
        } finally {
            if (!signal.aborted) results.removeAttribute('aria-busy');
        }
    }

    function reset() {
        clearTimeout(timer);
        request?.abort();
        results.replaceChildren();
        results.removeAttribute('aria-busy');
        close();
    }

    input.addEventListener('input', event => {
        reset();
        if (!event.isComposing) timer = setTimeout(search, 300);
    });
    form.addEventListener('submit', event => {
        event.preventDefault();
        reset();
        void search();
    });
    form.closest?.('[data-staff-schedule]')?.addEventListener('staff:schedule-unmount', () => {
        clearTimeout(timer);
        request?.abort();
        window.removeEventListener('resize', position);
        window.removeEventListener('scroll', onScroll, true);
    }, { once: true });
    reveal();

    }
    if (typeof document !== 'undefined') document.addEventListener('staff:schedule-mounted', mount);
    mount();
})();
