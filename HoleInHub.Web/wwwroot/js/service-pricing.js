(() => {
    const section = document.querySelector('[data-service-pricing]');
    if (!section) return;
    const form = section.closest('form');
    const type = form.elements.BookingService;
    const mode = () => type.value === 'WholeShop' ? 'rental'
        : ['KpgaPro', 'KpgaGroup'].includes(type.value) ? 'package' : 'hourly';
    const sync = () => {
        for (const group of section.querySelectorAll('[data-price-format]')) {
            group.hidden = !group.dataset.priceFormat.split(' ').includes(mode());
            for (const input of group.querySelectorAll('input, select, button')) input.disabled = group.hidden;
        }
        section.querySelector('[data-base-price-label]').textContent = {
            hourly: 'Base rate per bay/hour (₱)', package: 'Package price (₱)', rental: 'Half-day rate (₱)'
        }[mode()];
    };
    function indexRows() {
        for (const group of section.querySelectorAll('[data-price-rows]')) {
            [...group.children].forEach((row, index) => {
                for (const input of row.querySelectorAll('[name]')) {
                    if (input.name.endsWith('.Index')) input.value = String(index);
                    else input.name = input.name.replace(/\[\d+\]/, `[${index}]`);
                }
            });
        }
    }
    type.addEventListener('change', sync);
    sync();
    const compactPeso = new Intl.NumberFormat('en-PH', { style: 'currency', currency: 'PHP', minimumFractionDigits: 0 });
    const rateLabel = input => Number(input.value) > 0 ? compactPeso.format(input.value) : 'Set rate';
    section.addEventListener('change', event => {
        const row = event.target.closest('[data-price-row]');
        if (!row) return;
        const field = name => row.querySelector(`[name$=".${name}"]`);
        const day = field('Day');
        const date = field('Date');
        if (event.target === date && date.value) day.value = '';
        if (event.target === day && day.value) date.value = '';
        const rate = field('Rate');
        row.querySelector('[data-rule-day]').textContent = date.value || day.value || (rate ? 'Rate band' : 'Rental override');
        if (rate) {
            const start = field('Start').value;
            const end = field('EndsAtMidnight').checked ? '24:00' : field('End').value;
            row.querySelector('[data-rule-rate]').textContent = (start && end ? `${start}–${end} · ` : '') + rateLabel(rate);
        } else {
            row.querySelector('[data-rule-rate]').textContent = `${rateLabel(field('HalfDayRate'))} / ${rateLabel(field('WholeDayRate'))}`;
        }
    });
    let index = 1000;
    section.addEventListener('click', event => {
        const add = event.target.closest('[data-add-price]');
        const remove = event.target.closest('[data-remove-price]');
        if (add) {
            const key = add.dataset.addPrice;
            const rows = section.querySelector(`[data-price-rows="${key}"]`);
            const template = section.querySelector(`[data-price-template="${key}"]`);
            rows.insertAdjacentHTML('beforeend', template.innerHTML.replaceAll('__index__', String(index++)));
            indexRows();
            rows.lastElementChild.open = true;
            rows.lastElementChild.querySelector('select').focus();
        }
        if (remove) {
            const row = remove.closest('[data-price-row]');
            const key = row.parentElement.dataset.priceRows;
            row.remove();
            indexRows();
            section.querySelector(`[data-add-price="${key}"]`).focus();
        }
    });
})();
