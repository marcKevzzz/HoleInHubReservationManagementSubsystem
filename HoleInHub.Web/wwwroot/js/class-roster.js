(() => {
    const dialog = document.querySelector('#class-decision');
    let button;
    document.querySelector('.class-attendance-page').addEventListener('click', event => {
        const action = event.target.closest('form button');
        if (!action) return;
        event.preventDefault();
        button = action;
        dialog.querySelector('h2').textContent = (action.getAttribute('aria-label') || action.textContent.trim()) + '?';
        dialog.returnValue = '';
        dialog.showModal();
    });
    dialog.addEventListener('close', () => {
        if (dialog.returnValue === 'confirm') button.form.requestSubmit(button);
        else button.focus();
    });
})();
