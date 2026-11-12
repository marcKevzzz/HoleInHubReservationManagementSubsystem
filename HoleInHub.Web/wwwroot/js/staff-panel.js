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
