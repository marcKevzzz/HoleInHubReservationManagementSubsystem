(() => {
    const dialog = document.querySelector('#issued-key');
    const key = dialog.querySelector('[data-key]').textContent;
    const done = dialog.querySelector('[data-done]');
    const notify = (kind, title) => window.HoleInHubToasts?.show({ kind, title, autoDismiss: true });

    dialog.removeAttribute('open');
    dialog.showModal();
    notify('success', 'API key created');
    dialog.addEventListener('cancel', event => {
        event.preventDefault();
        window.location.replace(done.href);
    });
    dialog.querySelector('[data-copy]').addEventListener('click', async () => {
        try {
            await navigator.clipboard.writeText(key);
            notify('success', 'API key copied');
        } catch {
            notify('error', 'Select the key and copy it manually');
        }
    });
    dialog.querySelector('[data-download]').addEventListener('click', () => {
        const guide = document.querySelector('#key-guide').content.textContent;
        const file = new Blob([guide, '\n\nSite address: ', window.location.origin, '\n'], { type: 'text/plain;charset=utf-8' });
        const url = URL.createObjectURL(file);
        const link = document.createElement('a');
        link.href = url;
        link.download = 'hole-in-hub-key-and-guide.txt';
        link.click();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
        notify('success', 'Key and guide download started');
    });
})();
