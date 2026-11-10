(() => {
    const form = document.querySelector('.people-search');
    if (!form) return;
    const input = form.querySelector('input[name=search]');
    const results = document.querySelector('[data-customer-results]');
    const count = document.querySelector('[data-customer-count]');
    const status = document.querySelector('[data-customer-search-status]');
    let timer;
    let request;
    let composing = false;

    async function search() {
        clearTimeout(timer);
        request?.abort();
        const current = new AbortController();
        request = current;
        status.textContent = '';
        const timeout = setTimeout(() => current.abort(), 10000);
        const url = new URL(form.action);
        if (input.value.trim()) url.searchParams.set('search', input.value.trim());
        results.setAttribute('aria-busy', 'true');
        try {
            const response = await fetch(url, { cache: 'no-store', signal: current.signal });
            if (!response.ok || response.redirected) throw new Error('Search unavailable');
            const page = new DOMParser().parseFromString(await response.text(), 'text/html');
            const replacement = page.querySelector('[data-customer-results]');
            if (!replacement) throw new Error('Search unavailable');
            if (current.signal.aborted || current !== request) return;
            results.replaceChildren(...replacement.childNodes);
            count.textContent = page.querySelector('[data-customer-count]').textContent;
            history.replaceState(null, '', url);
            status.textContent = '';
            document.dispatchEvent(new Event('staff:rows-updated'));
        } catch {
            if (current === request) status.textContent = 'Search is unavailable. Press Enter to try again.';
        } finally {
            clearTimeout(timeout);
            if (current === request) results.removeAttribute('aria-busy');
        }
    }
    function schedule() {
        clearTimeout(timer);
        request?.abort();
        request = null;
        results.removeAttribute('aria-busy');
        if (!composing) timer = setTimeout(search, 350);
    }
    input.addEventListener('input', schedule);
    input.addEventListener('compositionstart', () => { composing = true; clearTimeout(timer); request?.abort(); });
    input.addEventListener('compositionend', () => { composing = false; schedule(); });
    form.addEventListener('submit', event => { event.preventDefault(); search(); });
})();
