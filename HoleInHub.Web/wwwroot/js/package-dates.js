(() => {
    const dates = document.querySelector('[data-package-count]');
    if (!dates) return;
    const preview = dates.form.querySelector('[data-package-preview]');
    const publish = document.getElementById('publish-package');
    const required = Number(dates.dataset.packageCount);
    function update() {
        const selected = [...dates.querySelectorAll(':checked')];
        publish.disabled = selected.length !== required;
        preview.textContent = publish.disabled
            ? selected.length + ' of ' + required + ' lessons selected'
            : Math.min(...selected.map(input => Number(input.dataset.remaining))) + ' seats available for the full package';
    }
    dates.addEventListener('change', update);
    update();
})();
