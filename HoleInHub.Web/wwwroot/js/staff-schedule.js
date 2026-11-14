(() => {
    function scrollToBooking(scroller, left, top, dayChanged) {
        left = Math.max(0, Math.min(left, scroller.scrollWidth - scroller.clientWidth));
        top = Math.max(0, Math.min(top, scroller.scrollHeight - scroller.clientHeight));
        if (Math.abs(left - scroller.scrollLeft) < 1 && Math.abs(top - scroller.scrollTop) < 1) {
            return dayChanged ? new Promise(resolve => setTimeout(resolve, 260)) : Promise.resolve();
        }
        return new Promise(resolve => {
            const finish = () => {
                clearTimeout(timeout);
                scroller.removeEventListener('scrollend', finish);
                resolve();
            };
            const timeout = setTimeout(finish, 750);
            scroller.addEventListener('scrollend', finish, { once: true });
            scroller.scrollTo({ left, top, behavior: 'smooth' });
        });
    }
    function mount() {
    const page = document.querySelector('[data-staff-schedule]');
    if (!page) return;
    const notice = page.querySelector('[data-customer-booking-notice]');
    if (notice && !window.HoleInHubSchedule) {
        window.HoleInHubToasts?.show({
            kind: 'info', title: notice.dataset.title,
            message: 'Drag an open time on any bay, or choose New booking, then review the prefilled details.',
            href: notice.dataset.href, action: 'Back to customer', autoDismiss: true,
            replaceKey: 'customer-booking-context'
        });
    }
    function highlight(booking) {
        const id = booking?.dataset.multiBay === 'true' ? booking.dataset.bookingId : null;
        for (const bar of page.querySelectorAll('.staff-timeline-bar[data-booking-id]')) {
            bar.classList.toggle('is-reservation-group', !!id && bar.dataset.bookingId === id);
        }
    }
    page.addEventListener('pointerover', event => {
        if (event.pointerType !== 'touch') highlight(event.target.closest('[data-staff-booking]'));
    });
    page.addEventListener('pointerleave', () => highlight(null));
    page.addEventListener('focusin', event => highlight(event.target.closest('[data-staff-booking]')));
    page.addEventListener('focusout', event => highlight(event.relatedTarget?.closest('[data-staff-booking]')));
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
    let opener, actionsRequest, disposeActions;
    if (typeof drawer?.showModal === 'function') {
        function openBooking(booking) {
            if (drawer.open) return;
            opener = booking;
            for (const bar of page.querySelectorAll('.staff-timeline-bar[data-booking-id]')) {
                bar.classList.toggle('is-booking-open', bar.dataset.bookingId === booking.dataset.bookingId);
            }
            drawer.querySelectorAll('[data-drawer]').forEach(field => {
                field.textContent = booking.dataset[field.dataset.drawer];
            });
            const multiple = Number(booking.dataset.bayCount) > 1;
            drawer.querySelector('[data-drawer-bay-label]').textContent = multiple ? 'Bays' : 'Bay';
            drawer.querySelector('[data-drawer-linked-bays]').hidden = !multiple;
            if (multiple && booking.dataset.bookedBays) {
                drawer.querySelector('[data-drawer="location"]').textContent = booking.dataset.bookedBays;
            }
            drawer.querySelector('[data-drawer-link]').href = booking.href;
            const status = drawer.querySelector('[data-drawer="state"]');
            status.dataset.status = booking.dataset.status;
            drawer.showModal();
            const actions = drawer.querySelector('[data-drawer-actions]');
            actionsRequest?.abort();
            disposeActions?.();
            actionsRequest = new AbortController();
            const { signal } = actionsRequest;
            actions.classList.add('review-detail');
            window.HoleInHubPanels.loading(actions, 'Loading booking actions…');
            const url = new URL(booking.dataset.actionsUrl, window.location.href);
            url.searchParams.set('drawer', 'true');
            fetch(url, { signal, cache: 'no-store', redirect: 'error' }).then(async response => {
                if (!response.ok) throw new Error();
                const html = await response.text();
                if (!signal.aborted) {
                    actions.innerHTML = html;
                    disposeActions = window.HoleInHubButtons?.mount(actions);
                    window.HoleInHubAttendance?.mount(actions);
                    actions.querySelector('#reject-reason-choice')?.addEventListener('change', event => {
                        event.target.form.elements.reason.required = event.target.value === 'Other';
                    });
                }
            }).catch(() => {
                if (!signal.aborted) {
                    actions.replaceChildren();
                    window.HoleInHubToasts?.show({ autoDismiss: true, kind: 'error', title: 'Actions unavailable', message: 'Open the full booking to try again.' });
                }
            }).finally(() => { if (actionsRequest?.signal === signal) actions.removeAttribute('aria-busy'); });
        }
        page.addEventListener('staff:open-booking', event => openBooking(event.detail));
        page.addEventListener('click', async event => {
            const booking = event.target.closest('[data-staff-booking]');
            if (event.defaultPrevented || !booking || event.button || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
            event.preventDefault();
            if (booking.dataset.searchDay) {
                const date = booking.dataset.searchDay;
                const destination = date === page.dataset.date ? page : await window.HoleInHubSchedule?.go(date);
                if (!destination) return;
                const bars = [...destination.querySelectorAll('.staff-timeline-bar[data-booking-id]')]
                    .filter(bar => bar.dataset.bookingId === booking.dataset.bookingId);
                if (bars.length) {
                    if (destination.dataset?.activeView !== 'timeline') destination.querySelector('[data-schedule-view="timeline"]').click();
                    const scroller = destination.querySelector('.staff-timeline-scroll');
                    const viewport = scroller.getBoundingClientRect();
                    const label = destination.querySelector('.staff-timeline-label').offsetWidth;
                    const axis = destination.querySelector('.staff-timeline-axis').offsetHeight;
                    const bounds = bars[0].getBoundingClientRect();
                    bars[0].focus({ preventScroll: true });
                    bars.forEach(bar => bar.classList.add('is-search-target'));
                    const left = scroller.scrollLeft + bounds.left - viewport.left - label - 16;
                    const top = scroller.scrollTop + bounds.top - viewport.top - axis - 12;
                    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
                    if (reduced) scroller.scrollTo({ left, top, behavior: 'instant' });
                    else await scrollToBooking(scroller, left, top, destination !== page);
                    if (destination.isConnected && !destination.dataset.moving && !destination.querySelector('dialog[open]')) {
                        destination.dispatchEvent(new CustomEvent('staff:open-booking', { detail: bars[0] }));
                    }
                    return;
                }
                // Expired and cancelled bookings have no timeline bar, but still open in the drawer.
                destination.dispatchEvent(new CustomEvent('staff:open-booking', { detail: booking }));
                return;
            }
            openBooking(booking);
        });
        drawer.addEventListener('close', () => { actionsRequest?.abort();
            for (const bar of page.querySelectorAll('.is-booking-open, .is-search-target')) {
                bar.classList.remove('is-booking-open', 'is-search-target');
            }
            opener?.focus({ preventScroll: true }); });
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
