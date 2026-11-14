(() => {
    const section = document.querySelector('[data-service-availability]');
    if (!section) return;
    const rows = section.querySelector('[data-availability-rows]');
    const template = section.querySelector('[data-availability-template]');
    const add = section.querySelector('[data-add-availability]');
    let nextIndex = rows.children.length;

    rows.addEventListener('change', event => {
        const row = event.target.closest('[data-availability-row]');
        if (!row) return;
        if (event.target.name.endsWith('.Date') && event.target.value)
            row.querySelector('[name$=".Weekday"]').value = '';
        if (event.target.name.endsWith('.Weekday') && event.target.value)
            row.querySelector('[name$=".Date"]').value = '';
    });
    add.addEventListener('click', () => {
        rows.insertAdjacentHTML('beforeend', template.innerHTML.replaceAll('__index__', String(nextIndex++)));
        rows.lastElementChild.querySelector('select').focus();
    });
    rows.addEventListener('click', event => {
        const remove = event.target.closest('[data-remove-availability]');
        if (!remove) return;
        const row = remove.closest('[data-availability-row]');
        const neighbor = row.nextElementSibling || row.previousElementSibling;
        row.remove();
        (neighbor?.querySelector('select') || add).focus();
    });
})();
