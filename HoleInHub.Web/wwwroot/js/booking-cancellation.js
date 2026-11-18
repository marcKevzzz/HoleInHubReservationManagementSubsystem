(() => {
    const disclosure = document.querySelector('[data-booking-cancel]');
    if (!disclosure || typeof HTMLDialogElement.prototype.showModal !== 'function') return;

    const trigger = disclosure.querySelector('summary');
    const form = disclosure.querySelector('form');
    const dialog = document.createElement('dialog');
    dialog.className = 'logout-confirmation booking-cancel-dialog';
    const title = document.createElement('h2');
    title.id = 'booking-cancel-title';
    title.textContent = trigger.textContent.trim();
    dialog.setAttribute('aria-labelledby', title.id);
    const actions = document.createElement('div');
    actions.className = 'logout-actions';
    const back = document.createElement('button');
    back.type = 'button';
    back.className = 'button button-tertiary';
    back.setAttribute('data-no-icon', '');
    back.textContent = window.HoleInHubText?.t('Cancel') ?? 'Cancel';
    back.autofocus = true;
    actions.append(back, form.querySelector('button[type="submit"]'));
    form.append(actions);
    dialog.append(title, form);
    disclosure.after(dialog);
    trigger.addEventListener('click', event => {
        event.preventDefault();
        dialog.showModal();
    });
    back.addEventListener('click', () => dialog.close());
    dialog.addEventListener('close', () => trigger.focus({ preventScroll: true }));
})();
