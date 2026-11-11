(() => {
    function mount(root = document) {
    const form = root.querySelector('.people-search');
    if (!form || form.customerSearch) return;
    const input = form.querySelector('input[name=search]');
    const results = root.querySelector('[data-customer-results]');
    const count = root.querySelector('[data-customer-count]');
    const status = root.querySelector('[data-customer-search-status]');
    let timer;
    let request;
    let composing = false;

    function cancel() {
        clearTimeout(timer);
        request?.abort();
        request = null;
        results.removeAttribute('aria-busy');
    }
    async function search(keepPage = false) {
        clearTimeout(timer);
        request?.abort();
        request = null;
        status.classList.remove('is-error');
        if (input.value.trim().length === 1) {
            results.hidden = count.hidden = true;
            status.textContent = 'Type one more character to search.';
            return;
        }
        const current = new AbortController();
        request = current;
        status.textContent = '';
        const timeout = setTimeout(() => current.abort(), 10000);
        const url = new URL(form.action);
        if (keepPage && typeof window !== 'undefined') {
            const currentUrl = new URL(window.location.href);
            if ((currentUrl.searchParams.get('search') || '') === input.value.trim() && currentUrl.searchParams.has('page')) url.searchParams.set('page', currentUrl.searchParams.get('page'));
        }
        if (input.value.trim()) url.searchParams.set('search', input.value.trim());
        results.setAttribute('aria-busy', 'true');
        try {
            const response = await fetch(url, { cache: 'no-store', signal: current.signal });
            if (!response.ok || response.redirected) throw new Error('Search unavailable');
            const page = new DOMParser().parseFromString(await response.text(), 'text/html');
            const replacement = page.querySelector('[data-customer-results]');
            if (!replacement) throw new Error('Search unavailable');
            if (current.signal.aborted || current !== request) return;
            document.dispatchEvent(new Event('staff:rows-replacing'));
            results.replaceChildren(...replacement.childNodes);
            results.hidden = count.hidden = false;
            count.textContent = page.querySelector('[data-customer-count]').textContent;
            if (!form.closest?.('dialog')) history.replaceState(null, '', url);
            status.textContent = '';
            document.dispatchEvent(new Event('staff:rows-updated'));
            return true;
        } catch {
            if (current === request) { status.classList.add('is-error'); status.textContent = 'Search is unavailable. Press Enter to try again.'; }
            return false;
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
        if (!composing && input.value.trim().length === 1) void search();
        else if (!composing) timer = setTimeout(search, 350);
    }
    form.customerSearch = { search, cancel };
    input.addEventListener('input', schedule);
    input.addEventListener('compositionstart', () => { composing = true; clearTimeout(timer); request?.abort(); });
    input.addEventListener('compositionend', () => { composing = false; schedule(); });
    form.addEventListener('submit', event => { event.preventDefault(); search(); });
    }
    if (typeof window !== 'undefined') window.HoleInHubCustomerSearch = {
        mount,
        refresh: () => document.querySelector('.people-search')?.customerSearch?.search(true),
        cancel: () => document.querySelector('.people-search')?.customerSearch?.cancel()
    };
    mount();
})();
