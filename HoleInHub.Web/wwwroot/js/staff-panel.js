(() => {
    function loading(body, label) {
        const message = document.createElement('p');
        message.className = 'staff-panel-feedback sr-only';
        message.setAttribute('role', 'status');
        message.textContent = label;
        const skeleton = document.createElement('div');
        skeleton.className = 'staff-panel-skeleton';
        skeleton.setAttribute('aria-hidden', 'true');
        for (let index = 0; index < 5; index++) skeleton.append(document.createElement('span'));
        body.replaceChildren(message, skeleton);
        body.setAttribute('aria-busy', 'true');
        return { message, skeleton };
    }
    window.HoleInHubPanels = { loading };
})();

(() => {
    document.addEventListener('keydown', event => {
        const help = event.target.closest('.staff-help');
        if (event.key !== 'Escape' || !help || help.hasAttribute('data-help-dismissed')) return;
        help.setAttribute('data-help-dismissed', '');
        event.preventDefault();
        event.stopPropagation();
    }, true);
    document.addEventListener('focusin', event => event.target.closest('.staff-help')?.removeAttribute('data-help-dismissed'));
    document.addEventListener('pointerover', event => {
        const help = event.target.closest('.staff-help');
        if (help && !help.contains(event.relatedTarget)) help.removeAttribute('data-help-dismissed');
    });
})();
