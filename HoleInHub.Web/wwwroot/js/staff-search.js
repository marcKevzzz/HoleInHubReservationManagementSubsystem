(() => {
    const form = document.querySelector('[data-staff-search]');
    const results = document.querySelector('[data-staff-search-results]');
    if (!form || !results) return;

    const input = form.elements.reference;
    let timer;
    let request;

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
            if (!signal.aborted) results.innerHTML = html;
        } catch {
            if (!signal.aborted) {
                const message = document.createElement('p');
                message.textContent = 'Booking search is unavailable. Press Enter to try again.';
                results.replaceChildren(message);
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
})();
