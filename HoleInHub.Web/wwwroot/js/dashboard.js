(() => {
    let page = document.querySelector('[data-dashboard]');
    if (!page) return;
    let charts = [], request, sliceRequest, opener, activeSlice, updateTimer, pendingUpdate = false, updateFailureNotified = false;
    let entrance;
    const preferences = new Map();
    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)');
    const number = value => new Intl.NumberFormat('en-PH', { maximumFractionDigits: 2 }).format(value);
    const dateLabel = value => new Intl.DateTimeFormat('en-PH', { day: 'numeric', month: 'short', timeZone: 'UTC' }).format(new Date(value + 'T00:00:00Z'));
    function color(token) { return getComputedStyle(document.documentElement).getPropertyValue('--color-chart-' + token).trim(); }
    function sliceUrl(kind, value, group) {
        const url = new URL(page.dataset.sliceBase, location.origin);
        url.searchParams.set('kind', kind); url.searchParams.set('value', value);
        if (group) url.searchParams.set('group', group); else url.searchParams.delete('group');
        return url.pathname + url.search;
    }
    function select(section, kind, value, label, group) {
        const selection = section.querySelector('[data-chart-selection]');
        selection.hidden = false; selection.querySelector('span').textContent = label;
        selection.querySelector('a').href = sliceUrl(kind, value, group);
        preferences.set(section.dataset.chart + ':selection', { kind, value, label, group });
    }
    function mountCharts(animation) {
        if (!window.Chart) return;
        const data = JSON.parse(page.querySelector('[data-dashboard-data]').textContent);
        for (const section of page.querySelectorAll('[data-chart]')) {
            const daily = section.dataset.chart === 'daily';
            const series = daily ? data.days : data.hours;
            const values = series.map(row => daily ? row.Booked : row.Regular + row.Vip + row.Other);
            if (!values.some(value => value > 0)) continue;
            const unit = daily ? 'Booked sessions' : 'Scheduled bay-hours';
            const frame = section.querySelector('[data-chart-frame]'); frame.hidden = false;
            const chart = new Chart(frame.querySelector('canvas'), {
                type: 'line',
                data: {
                    labels: series.map(row => daily ? dateLabel(row.Date) : String(row.Hour).padStart(2, '0') + ':00'),
                    datasets: [{ label: unit, data: values, borderColor: color('booked'), backgroundColor: color('booked'),
                        borderWidth: 2, pointRadius: series.length > 31 ? 0 : 3, pointHoverRadius: 5, pointHitRadius: 18,
                        tension: .35, cubicInterpolationMode: 'monotone', stepped: false, fill: false }]
                },
                options: {
                    responsive: true, maintainAspectRatio: false, animation,
                    interaction: { mode: 'index', intersect: false },
                    plugins: { legend: { display: false }, tooltip: { callbacks: {
                        label: context => number(context.parsed.y) + (daily ? ' booked sessions' : ' scheduled bay-hours')
                    } } },
                    scales: {
                        x: { grid: { display: false }, border: { display: false },
                            ticks: { color: color('neutral'), maxRotation: 0, autoSkip: true, maxTicksLimit: 7 } },
                        y: { beginAtZero: true, border: { display: false },
                            grid: { color: getComputedStyle(page).getPropertyValue('--color-line') },
                            ticks: { color: color('neutral'), precision: daily ? 0 : undefined, maxTicksLimit: 4 },
                            title: { display: true, text: daily ? 'Sessions' : 'Bay-hours', color: color('neutral') } }
                    },
                    onClick: (_event, elements) => {
                        if (!elements.length) return;
                        const row = series[elements[0].index];
                        if (daily) select(section, 'day', row.Date, dateLabel(row.Date), 'booked');
                        else select(section, 'hour', String(row.Hour), String(row.Hour).padStart(2, '0') + ':00');
                    }
                }
            });
            charts.push(chart);

            const oldSelection = preferences.get(section.dataset.chart + ':selection');
            if (oldSelection) select(section, oldSelection.kind, oldSelection.value, oldSelection.label, oldSelection.group);
            section.querySelector('[data-clear-selection]').addEventListener('click', () => {
                section.querySelector('[data-chart-selection]').hidden = true;
                preferences.delete(section.dataset.chart + ':selection');
            });
        }
    }
    function mountPie(animation) {
        if (!window.Chart) return;
        const data = JSON.parse(page.querySelector('[data-dashboard-data]').textContent).services;
        if (!data?.length) return;
        const frame = page.querySelector('[data-pie-frame]');
        if (!frame) return;
        frame.hidden = false;
        const tokens = { Regular: 'booked', VIP: 'blue', PhilippineCoach: 'purple', KpgaPro: 'pending', KpgaGroup: 'arrived', WholeShop: 'neutral' };
        const total = data.reduce((sum, row) => sum + row.Count, 0);
        charts.push(new Chart(frame.querySelector('canvas'), {
            type: 'doughnut',
            plugins: [{ id: 'serviceValues', afterDatasetsDraw(chart) {
                const { ctx } = chart;
                const arcs = chart.getDatasetMeta(0).data;
                if (!arcs.length) return;
                ctx.save(); ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
                ctx.fillStyle = getComputedStyle(page).getPropertyValue('--color-ink').trim();
                ctx.font = '500 22px ' + getComputedStyle(page).fontFamily;
                ctx.fillText(number(total), arcs[0].x, arcs[0].y - 7);
                ctx.fillStyle = color('neutral'); ctx.font = '11px ' + getComputedStyle(page).fontFamily;
                ctx.fillText('booked', arcs[0].x, arcs[0].y + 13);
                ctx.fillStyle = getComputedStyle(page).getPropertyValue('--color-paper').trim();
                ctx.font = '500 11px ' + getComputedStyle(page).fontFamily;
                arcs.forEach((arc, index) => {
                    const share = data[index].Count / total;
                    // Small slices keep their exact values in the adjacent labeled list.
                    if (share < .1 || arc.endAngle - arc.startAngle < .5) return;
                    const angle = (arc.startAngle + arc.endAngle) / 2;
                    const radius = (arc.innerRadius + arc.outerRadius) / 2;
                    ctx.fillText(Math.round(share * 100) + '%', arc.x + Math.cos(angle) * radius, arc.y + Math.sin(angle) * radius);
                });
                ctx.restore();
            } }],
            data: { labels: data.map(row => row.Label), datasets: [{ data: data.map(row => row.Count),
                backgroundColor: data.map(row => color(tokens[row.Id] || 'neutral')), borderColor: getComputedStyle(page).getPropertyValue('--color-paper').trim(), borderWidth: 3 }] },
            options: { responsive: true, maintainAspectRatio: false, animation, cutout: '60%',
                plugins: { legend: { display: false }, tooltip: { callbacks: { label: context => context.label + ': ' + number(context.parsed) + ' booked sessions' } } },
                onClick: (_event, elements) => { if (elements.length) void inspect(sliceUrl('service', data[elements[0].index].Id), frame.querySelector('canvas')); }
            }
        }));
    }
    function queueUpdate() {
        clearTimeout(updateTimer);
        if (!pendingUpdate || document.hidden) return;
        updateTimer = setTimeout(() => {
            if (request || page.querySelector('[data-dashboard-dialog]').open || page.querySelector('[data-dashboard-period]')?.open
                || page.querySelector('[data-dashboard-filter]').contains(document.activeElement)) { queueUpdate(); return; }
            pendingUpdate = false;
            void load(page.dataset.url, false, false);
        }, 500);
    }
    window.HoleInHubLive?.subscribe(() => { pendingUpdate = true; queueUpdate(); });
    function mountPeriod() {
        const period = page.querySelector('[data-dashboard-period]');
        if (!period) return;
        const mode = period.querySelector('[data-period-mode]');
        function sync() {
            const range = mode?.value === 'trends';
            for (const [selector, visible] of [['[data-period-day]', !range], ['[data-period-range]', range]]) {
                const group = period.querySelector(selector);
                if (!group) continue;
                group.hidden = !visible;
                group.querySelectorAll('input').forEach(input => input.disabled = !visible);
            }
        }
        mode?.addEventListener('change', sync);
        sync();
        period.addEventListener('keydown', event => {
            if (event.key !== 'Escape') return;
            event.preventDefault(); period.open = false; period.querySelector('summary').focus();
        });
    }
    function mount(animate = true) {
        window.HoleInHubButtons?.mount(page);
        mountPeriod();
        const playEntrance = animate && !reduced.matches;
        const animation = playEntrance ? { duration: 520, easing: 'easeOutQuart' } : false;
        mountCharts(animation);
        mountPie(animation);
        if (playEntrance && window.gsap) {
            const targets = page.querySelectorAll('.dashboard-summary > div, .dashboard-main > section, .dashboard-bay-insight');
            entrance = window.gsap.fromTo(targets, { opacity: .65, y: 8 }, {
                opacity: 1, y: 0, duration: .4, stagger: .035, ease: 'power3.out', clearProps: 'opacity,transform'
            });
        }
        const dialog = page.querySelector('[data-dashboard-dialog]');
        page.querySelector('[data-dashboard-close]').addEventListener('click', () => dialog.close());
        dialog.addEventListener('close', () => { sliceRequest?.abort(); opener?.focus({ preventScroll: true }); });
        let backdropPressed = false;
        const outside = event => {
            const rect = dialog.getBoundingClientRect();
            return event.clientX < rect.left || event.clientX > rect.right || event.clientY < rect.top || event.clientY > rect.bottom;
        };
        dialog.addEventListener('pointerdown', event => { backdropPressed = event.target === dialog && outside(event); });
        dialog.addEventListener('click', event => {
            if (backdropPressed && event.target === dialog && outside(event)) dialog.close();
            backdropPressed = false;
        });
    }
    async function load(url, push = true, manual = true) {
        request?.abort();
        const current = request = new AbortController();
        let feedback = 'Your last loaded data is still shown. Updates will retry automatically.';
        const timer = setTimeout(() => current.abort(), 25000);
        const oldPage = page;
        const content = oldPage.querySelector('[data-dashboard-content]');
        const skeleton = oldPage.querySelector('[data-dashboard-skeleton]');
        const skeletonTimer = manual ? setTimeout(() => {
            if (request !== current || !content || !skeleton) return;
            const liveSections = skeleton.querySelector('[data-dashboard-skeleton-live]');
            if (liveSections) liveSections.hidden = new URL(url, location.origin).searchParams.get('mode') === 'trends';
            content.hidden = true; skeleton.hidden = false;
        }, 180) : null;
        oldPage.setAttribute('aria-busy', 'true');
        oldPage.querySelector('[data-dashboard-loading]').textContent = manual ? 'Updating…' : '';
        try {
            const response = await fetch(url, { headers: { 'X-Dashboard': 'true' }, signal: current.signal, redirect: 'error', cache: 'no-store' });
            if (!response.ok) {
                const text = response.status === 400 ? await response.text() : '';
                if (text && text.length < 300) feedback = text;
                throw new Error(text && text.length < 300 ? text : 'Could not refresh the dashboard. Your last loaded data is still shown.');
            }
            const next = new DOMParser().parseFromString(await response.text(), 'text/html').querySelector('[data-dashboard]');
            if (!next) throw new Error('Could not read the updated dashboard. Reload the page to sign in again.');
            if (request !== current) return;
            if (!manual && (page.querySelector('[data-dashboard-dialog]').open || page.querySelector('[data-dashboard-period]')?.open
                || page.querySelector('[data-dashboard-filter]').contains(document.activeElement))) {
                pendingUpdate = true; queueUpdate(); return;
            }
            const focusName = manual ? document.activeElement?.getAttribute('name') : null;
            entrance?.revert(); entrance = undefined;
            charts.forEach(chart => chart.destroy()); charts = [];
            oldPage.replaceWith(next); page = next;
            if (push) history.pushState(null, '', next.dataset.url);
            mount(manual);
            updateFailureNotified = false;
            if (focusName) {
                const field = page.querySelector('[name="' + CSS.escape(focusName) + '"]');
                const period = field?.closest('[data-dashboard-period]');
                (period && !period.open ? period.querySelector('summary') : field)?.focus({ preventScroll: true });
            }
            if (manual) page.querySelector('[data-dashboard-loading]').textContent = 'Dashboard updated.';
        } catch (failure) {
            if (request !== current) return;
            if (manual || !updateFailureNotified) {
                window.HoleInHubToasts?.show({ kind: 'warning', title: 'Dashboard could not update',
                    message: failure.name === 'AbortError' ? 'The update timed out. Updates will retry automatically.' : feedback,
                    replaceKey: 'dashboard-update', autoDismiss: true });
                updateFailureNotified = true;
            }
        } finally {
            clearTimeout(timer);
            clearTimeout(skeletonTimer);
            if (request === current) {
                request = null; oldPage.removeAttribute('aria-busy'); oldPage.querySelector('[data-dashboard-loading]').textContent = '';
                if (content) content.hidden = false;
                if (skeleton) skeleton.hidden = true;
            }
        }
    }
    async function inspect(url, trigger) {
        const dialog = page.querySelector('[data-dashboard-dialog]');
        if (typeof dialog.showModal !== 'function') { location.assign(url); return; }
        if (!dialog.open) { opener = trigger; dialog.showModal(); }
        sliceRequest?.abort(); const current = sliceRequest = new AbortController(); activeSlice = url;
        const timer = setTimeout(() => current.abort(), 25000);
        const body = dialog.querySelector('[data-dashboard-dialog-body]');
        body.setAttribute('aria-busy', 'true'); body.textContent = 'Loading matching reservations…';
        try {
            const response = await fetch(url, { headers: { 'X-Dashboard-Slice': 'true' }, signal: current.signal, redirect: 'error', cache: 'no-store' });
            if (!response.ok) throw new Error();
            const content = new DOMParser().parseFromString(await response.text(), 'text/html').querySelector('[data-dashboard-bookings]');
            if (!content) throw new Error();
            if (sliceRequest !== current || !dialog.open) return;
            body.replaceChildren(content);
            content.querySelector('h3').tabIndex = -1; content.querySelector('h3').focus({ preventScroll: true });
        } catch {
            if (sliceRequest !== current || !dialog.open || current.signal.aborted && !body.isConnected) return;
            body.textContent = 'Could not load reservations. ';
            const retry = document.createElement('button'); retry.type = 'button'; retry.textContent = 'Retry'; retry.addEventListener('click', () => inspect(activeSlice, opener));
            const fallback = document.createElement('a'); fallback.href = url; fallback.textContent = 'Open full page';
            body.append(retry, document.createTextNode(' · '), fallback);
        } finally { clearTimeout(timer); if (sliceRequest === current) body.removeAttribute('aria-busy'); }
    }
    document.addEventListener('pointerdown', event => {
        const period = page.querySelector('[data-dashboard-period]');
        if (period?.open && !period.contains(event.target)) period.open = false;
    });
    document.addEventListener('click', event => {
        if (event.defaultPrevented || event.button || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        const link = event.target.closest('[data-dashboard-nav],[data-dashboard-slice]');
        if (link && page.contains(link)) {
            event.preventDefault();
            if (link.hasAttribute('data-dashboard-slice')) void inspect(link.href, link);
            else { preferences.clear(); void load(link.href); }
        }

    });
    document.addEventListener('submit', event => {
        const form = event.target.closest('[data-dashboard-filter]');
        if (!form) return;
        event.preventDefault();
        const url = new URL(form.action); url.search = new URLSearchParams(new FormData(form)).toString();
        preferences.clear(); void load(url.href);
    });
    window.addEventListener('popstate', () => { preferences.clear(); void load(location.href, false); });
    reduced.addEventListener('change', event => {
        if (!event.matches) return;
        entrance?.revert(); entrance = undefined;
        charts.forEach(chart => { chart.stop(); chart.options.animation = false; chart.update('none'); });
    });
    window.addEventListener('pagehide', () => {
        clearTimeout(updateTimer); request?.abort(); sliceRequest?.abort();
        entrance?.revert(); charts.forEach(chart => chart.destroy());
    });
    mount();
})();
