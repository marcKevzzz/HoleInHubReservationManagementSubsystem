(() => {
    let report = document.querySelector('[data-reports]');
    if (!report) return;
    let request;
    let charts = [];
    const announcement = document.querySelector('[data-report-announcement]');
    const reduced = matchMedia('(prefers-reduced-motion: reduce)');

    function mount() {
        if (!window.Chart) return;
        const donut = report.querySelector('[data-customer-donut][data-empty="false"]');
        if (donut) {
            const facts = donut.parentElement.querySelector('.reports-facts');
            const canvas = donut.querySelector('canvas');
            const colors = getComputedStyle(donut);
            canvas.hidden = false;
            charts.push(new Chart(canvas, {
                type: 'doughnut',
                data: {
                    labels: [...facts.querySelectorAll('dt')].map(label => label.textContent),
                    datasets: [{
                        data: [...facts.querySelectorAll('dd')].map(count => Number(count.textContent)),
                        backgroundColor: ['--color-chart-booked', '--color-ink'].map(color => colors.getPropertyValue(color).trim()),
                        borderWidth: 0,
                        hoverOffset: 6
                    }]
                },
                options: {
                    cutout: '78%',
                    maintainAspectRatio: false,
                    animation: reduced.matches ? false : { duration: 420 },
                    plugins: { legend: { display: false } }
                }
            }));
            donut.classList.add('reports-customer-donut-enhanced');
        }
        for (const panel of report.querySelectorAll('[data-report-chart]')) {
            const frame = panel.querySelector('.dashboard-chart-frame');
            if (!frame) continue;
            const rows = [...panel.querySelectorAll('tbody tr')];
            const color = getComputedStyle(panel).getPropertyValue('--color-chart-booked').trim();
            const trend = panel.dataset.trend === 'true';
            frame.hidden = false;
            charts.push(new Chart(frame.querySelector('canvas'), {
                type: trend ? 'line' : 'bar',
                data: {
                    labels: rows.map(row => row.cells[0].textContent),
                    datasets: [{
                        label: panel.querySelector('[data-chart-unit]').dataset.chartUnit,
                        data: rows.map(row => Number(row.cells[1].dataset.value)),
                        backgroundColor: color, borderColor: color, tension: 0.25, pointRadius: 2
                    }]
                },
                options: {
                    responsive: true, maintainAspectRatio: false,
                    indexAxis: panel.dataset.horizontal === 'true' ? 'y' : 'x',
                    animation: reduced.matches ? false : { duration: 420 },
                    plugins: { legend: { display: false } },
                    scales: { y: { beginAtZero: true }, x: { grid: { display: false } } }
                }
            }));
            panel.querySelector('[data-report-values]').classList.add('sr-only');
        }
    }

    const listSelector = '#report-reservations';
    let renderedUrl = new URL(location.href);
    const samePeriod = (url, previous) => ['from', 'to', 'preset', 'service']
        .every(key => (url.searchParams.get(key) || '') === (previous.searchParams.get(key) || ''));

    async function load(url, push = true, listOnly = samePeriod(url, renderedUrl)) {
        request?.abort();
        const current = request = new AbortController();
        const list = report.querySelector(listSelector);
        report.toggleAttribute('data-report-list-loading', listOnly);
        report.setAttribute('aria-busy', 'true');
        report.querySelector('[data-report-results]').inert = !listOnly;
        list.setAttribute('aria-busy', 'true');
        announcement.textContent = 'Loading report…';
        try {
            const response = await fetch(url, { headers: { 'X-Reports': 'true' }, signal: current.signal, cache: 'no-store' });
            if (!response.ok) throw new Error();
            const content = new DOMParser().parseFromString(await response.text(), 'text/html').querySelector('[data-reports]');
            if (!content) throw new Error();
            if (request !== current) return;
            if (listOnly) {
                list.replaceWith(content.querySelector(listSelector));
                report.querySelector('[data-report-export]').href = content.querySelector('[data-report-export]').href;
                report.querySelector('[data-report-filter] input[name="status"]')?.remove();
                const status = content.querySelector('[data-report-filter] input[name="status"]');
                if (status) report.querySelector('[data-report-filter]').append(status);
                for (const [index, link] of report.querySelectorAll('.dashboard-presets a').entries()) {
                    link.href = content.querySelectorAll('.dashboard-presets a')[index].href;
                }
                report.removeAttribute('aria-busy');
                report.removeAttribute('data-report-list-loading');
            } else {
                charts.forEach(chart => chart.destroy());
                charts = [];
                report.replaceWith(content);
                report = content;
                mount();
            }
            renderedUrl = url;
            if (push) history.pushState(null, '', url);
            const focus = url.hash ? report.querySelector('#report-reservations') : report.querySelector('summary');
            focus.focus({ preventScroll: !url.hash });
            announcement.textContent = 'Report updated.';
        } catch {
            if (!current.signal.aborted) location.assign(url);
        }
    }

    document.addEventListener('click', event => {
        const period = report.querySelector('.dashboard-period');
        if (!period.contains(event.target)) period.open = false;
        const link = event.target.closest('[data-report-link]');
        if (!link || event.defaultPrevented || event.button || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        event.preventDefault();
        void load(new URL(link.href), true, link.hash === listSelector);
    });
    document.addEventListener('keydown', event => {
        const period = report.querySelector('.dashboard-period');
        if (event.key === 'Escape' && period.open) {
            period.open = false;
            period.querySelector('summary').focus();
        }
    });
    document.addEventListener('submit', event => {
        const form = event.target.closest('[data-report-filter]');
        if (!form) return;
        event.preventDefault();
        const url = new URL(form.action);
        url.search = new URLSearchParams(new FormData(form)).toString();
        void load(url);
    });
    window.addEventListener('popstate', () => void load(new URL(location.href), false));
    reduced.addEventListener('change', () => {
        if (reduced.matches) charts.forEach(chart => {
            chart.stop();
            chart.options.animation = false;
            chart.update('none');
        });
    });
    mount();
})();
