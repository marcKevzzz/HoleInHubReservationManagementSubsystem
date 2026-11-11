(() => {
    function mount() {
    const page = document.querySelector('[data-staff-schedule]');
    if (!page) return;
    for (const panel of page.querySelectorAll('.staff-booking-drawer')) {
        let pressedOutside = false;
        function outside(event) {
            const bounds = panel.getBoundingClientRect();
            return event.target === panel && (event.clientX < bounds.left || event.clientX > bounds.right
                || event.clientY < bounds.top || event.clientY > bounds.bottom);
        }
        panel.addEventListener('pointerdown', event => { pressedOutside = outside(event); });
        panel.addEventListener('click', event => {
            if (!pressedOutside || !outside(event)) return;
            if (typeof panel.requestClose === 'function') panel.requestClose();
            else if (panel.dispatchEvent(new Event('cancel', { cancelable: true }))) panel.close();
        });
    }
    const drawer = page.querySelector('[data-booking-details]');
    let opener, actionsRequest;
    if (typeof drawer.showModal === 'function') {
        page.addEventListener('click', event => {
            const booking = event.target.closest('[data-staff-booking]');
            if (!booking || event.button || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
            event.preventDefault();
            opener = booking;
            drawer.querySelectorAll('[data-drawer]').forEach(field => {
                field.textContent = booking.dataset[field.dataset.drawer];
            });
            drawer.querySelector('[data-drawer-link]').href = booking.href;
            const status = drawer.querySelector('[data-drawer="state"]');
            status.dataset.status = booking.dataset.status;
            drawer.showModal();
            const actions = drawer.querySelector('[data-drawer-actions]');
            actionsRequest?.abort();
            actionsRequest = new AbortController();
            const { signal } = actionsRequest;
            actions.classList.add('review-detail');
            actions.textContent = 'Loading available actions…';
            const url = new URL(booking.dataset.actionsUrl, window.location.href);
            url.searchParams.set('drawer', 'true');
            fetch(url, { signal, cache: 'no-store', redirect: 'error' }).then(async response => {
                if (!response.ok) throw new Error();
                const html = await response.text();
                if (!signal.aborted) {
                    actions.innerHTML = html;
                    actions.querySelector('#reject-reason-choice')?.addEventListener('change', event => {
                        event.target.form.elements.reason.required = event.target.value === 'Other';
                    });
                }
            }).catch(() => { if (!signal.aborted) actions.textContent = 'Actions are unavailable. Open the full booking to try again.'; });
        });
        drawer.addEventListener('close', () => { actionsRequest?.abort(); opener?.focus({ preventScroll: true }); });
    }

    const timeline = page.querySelector('.staff-timeline');
    if (!timeline) return;
    const scroller = page.querySelector('.staff-timeline-scroll');
    const axis = timeline.querySelector('.staff-timeline-hours');
    const nowLabel = timeline.querySelector('.staff-now-label');
    const viewControls = page.querySelector('.staff-view-controls');
    const views = [...viewControls.querySelectorAll('[data-schedule-view]')];
    const firstMinute = Number(page.dataset.firstHour) * 60;
    const clock = new Intl.DateTimeFormat('en-GB', {
        timeZone: 'Asia/Manila', year: 'numeric', month: '2-digit', day: '2-digit',
        hour: '2-digit', minute: '2-digit', hourCycle: 'h23',
    });
    let nowFraction;

    function scrollToNow() {
        if (nowFraction === undefined || !scroller.clientWidth) return;
        const bayWidth = timeline.querySelector('.staff-timeline-label').offsetWidth;
        const target = axis.clientWidth * nowFraction - (scroller.clientWidth - bayWidth) / 4;
        scroller.scrollTo({ left: Math.max(0, target), behavior: 'instant' });
    }

    function updateClock() {
        if (page.dataset.previewDate) { timeline.classList.remove('has-now'); return; }
        const parts = Object.fromEntries(clock.formatToParts(new Date()).map(({ type, value }) => [type, value]));
        const today = [parts.year, parts.month, parts.day].join('-') === page.dataset.date;
        const minute = Number(parts.hour) * 60 + Number(parts.minute);
        nowFraction = today && minute >= firstMinute ? (minute - firstMinute) / (1440 - firstMinute) : undefined;
        timeline.classList.toggle('has-now', nowFraction !== undefined);
        nowLabel.hidden = nowFraction === undefined;
        if (nowFraction !== undefined) {
            timeline.style.setProperty('--now', (nowFraction * 100) + '%');
            const hour = Number(parts.hour);
            nowLabel.textContent = (hour % 12 || 12) + ':' + parts.minute + (hour < 12 ? ' AM' : ' PM');
        }
    }

    function setView(view) {
        page.dataset.activeView = view;
        views.forEach(button => button.setAttribute('aria-pressed', String(button.dataset.scheduleView === view)));
        if (view === 'timeline') scrollToNow();
    }

    updateClock();
    setView('timeline');
    viewControls.hidden = false;
    views.forEach(button => button.addEventListener('click', () => setView(button.dataset.scheduleView)));
    const clockTimer = window.setInterval(updateClock, 15000);
    page.addEventListener('staff:schedule-unmount', () => { window.clearInterval(clockTimer); actionsRequest?.abort(); }, { once: true });
    }
    if (typeof document !== 'undefined') document.addEventListener('staff:schedule-mounted', mount);
    mount();
})();
