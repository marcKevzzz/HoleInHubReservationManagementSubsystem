(() => {
    function mount() {
    const zoomWidth = (width, delta, minimum = 68) => Math.max(minimum, Math.min(minimum * 3, width * Math.exp(-delta * .002)));
    const adjacentDay = (day, direction) => {
        const date = new Date(day + 'T12:00:00Z');
        date.setUTCDate(date.getUTCDate() + direction);
        return date.toISOString().slice(0, 10);
    };
    const panDayDirection = (overscroll, dx, dy, viewport) => {
        const threshold = Math.max(180, Math.min(320, viewport * .45));
        if (Math.abs(dx) < Math.abs(dy) * 2 || Math.abs(overscroll) < threshold) return 0;
        return Math.sign(overscroll);
    };
    if (typeof module !== 'undefined' && module.exports) module.exports = { zoomWidth, adjacentDay, panDayDirection };
    if (typeof document === 'undefined') return;
    const page = document.querySelector('[data-staff-schedule]');
    const scroller = page?.querySelector('.staff-timeline-scroll');
    if (!scroller) return;
    const timeline = scroller.querySelector('.staff-timeline');
    const hint = page.querySelector('[data-date-preview]');
    const hintLabel = hint.querySelector('[data-date-preview-label]');
    const zoom = page.querySelector('[data-timeline-zoom]');
    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const measuredWidth = timeline.querySelector('.staff-timeline-hours').getBoundingClientRect().width / (24 - Number(page.dataset.firstHour));
    const baseWidth = Number(page.dataset.zoomBaseWidth) || measuredWidth;
    page.dataset.zoomBaseWidth = baseWidth;
    let pan, zoomTimer, width = measuredWidth;
    zoom.value = Math.round(width / baseWidth * 100) + '%';
    const arrival = Number(sessionStorage.getItem('staff-day-direction'));
    sessionStorage.removeItem('staff-day-direction');
    function transition(direction) {
        if (reduced) return;
        for (const track of timeline.querySelectorAll('.staff-timeline-hours,.staff-timeline-track')) {
            track.animate([{ opacity: .4, translate: `${direction * 20}px 0` }, { opacity: 1, translate: '0 0' }], { duration: 260, easing: 'ease-out' });
        }
    }
    if (arrival) transition(arrival);
    page.addEventListener('staff:schedule-unmount', () => clearTimeout(zoomTimer), { once: true });
    const dayLabel = date => new Intl.DateTimeFormat('en', { weekday: 'short', day: 'numeric', month: 'short', timeZone: 'UTC' })
        .format(new Date(date + 'T12:00:00Z'));
    function preview(date, message = 'Release to open') {
        const current = page.dataset.previewDate || page.dataset.date;
        if (date !== current) hint.dataset.direction = date < current ? 'previous' : 'next';
        hintLabel.textContent = `${message} · ${dayLabel(date)}`;
        hint.hidden = false;
    }
    function setDate(date) {
        const input = page.querySelector('#staff-date');
        const label = page.querySelector('[data-open-date] time');
        if (input) input.value = date;
        if (label) { label.dateTime = date; label.textContent = date.split('-').reverse().join('.'); }
    }
    page.timelineGesture = { adjacentDay, dayLabel, preview, transition, setDate, hide: () => { hint.hidden = true; },
        async load(date, signal) {
            if (window.HoleInHubSchedule) return window.HoleInHubSchedule.load(date, signal);
            const url = new URL(window.location.href);
            url.searchParams.set('date', date);
            const response = await fetch(url, { signal, cache: 'no-store', redirect: 'error' });
            if (!response.ok) throw new Error('The next day could not be loaded. Try again.');
            const result = new DOMParser().parseFromString(await response.text(), 'text/html').querySelector('[data-staff-schedule]');
            if (result?.dataset.date !== date || !result.querySelector('.staff-timeline')) throw new Error('The schedule could not be verified. Reload and try again.');
            return result;
        }
    };
    scroller.addEventListener('wheel', event => {
        if (page.dataset.moving || pan) return;
        if (event.target.closest('.staff-timeline-label') && event.ctrlKey) { event.preventDefault(); return; }
        if (event.ctrlKey) {
            event.preventDefault();
            const label = timeline.querySelector('.staff-timeline-label').offsetWidth;
            const x = Math.max(label, event.clientX - scroller.getBoundingClientRect().left);
            const minute = (scroller.scrollLeft + x - label) / width;
            width = zoomWidth(width, event.deltaY, baseWidth * .65);
            timeline.style.setProperty('--hour-width', width + 'px');
            scroller.scrollLeft = minute * width - x + label;
            zoom.value = Math.round(width / baseWidth * 100) + '%';
            zoom.hidden = false;
            clearTimeout(zoomTimer);
            zoomTimer = setTimeout(() => { zoom.hidden = true; }, 1000);
        } else if (event.shiftKey) {
            event.preventDefault();
            scroller.scrollLeft += event.deltaX || event.deltaY;
        } else if (!event.deltaX && scroller.scrollHeight > scroller.clientHeight) {
            event.preventDefault();
            scroller.scrollTop += event.deltaY;
        }
    }, { passive: false });
    scroller.addEventListener('pointerdown', event => {
        if (page.dataset.moving || event.pointerType !== 'mouse' || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey
            || (page.dataset.customerProfileId && event.target.closest('[data-create-bay]'))
            || event.target.closest('a,button,input,select,.staff-timeline-label')) return;
        event.preventDefault();
        pan = { id: event.pointerId, x: event.clientX, y: event.clientY, left: scroller.scrollLeft, top: scroller.scrollTop };
        scroller.setPointerCapture(event.pointerId);
        scroller.classList.add('is-panning');
    });
    scroller.addEventListener('pointermove', event => {
        if (pan?.id !== event.pointerId) return;
        const dx = event.clientX - pan.x;
        scroller.scrollLeft = pan.left - dx;
        scroller.scrollTop = pan.top - (event.clientY - pan.y);
        const overscroll = pan.left - dx - scroller.scrollLeft;
        const direction = panDayDirection(overscroll, dx, event.clientY - pan.y, scroller.clientWidth);
        pan.date = direction ? adjacentDay(page.dataset.date, direction) : undefined;
        timeline.style.setProperty('--pan-offset', reduced ? '0px' : `${-Math.sign(overscroll) * Math.min(36, Math.abs(overscroll) * .25)}px`);
        if (pan.date) preview(pan.date);
        else hint.hidden = true;
    });
    function finish(event, navigate) {
        if (pan?.id !== event.pointerId) return;
        const date = navigate && pan.date;
        pan = undefined;
        timeline.style.removeProperty('--pan-offset');
        scroller.classList.remove('is-panning');
        hint.hidden = true;
        if (scroller.hasPointerCapture(event.pointerId)) scroller.releasePointerCapture(event.pointerId);
        if (date) {
            const url = new URL(window.location.href);
            url.searchParams.set('date', date);
            if (window.HoleInHubSchedule) void window.HoleInHubSchedule.go(date);
            else window.location.assign(url);
        }
    }
    scroller.addEventListener('pointerup', event => finish(event, true));
    for (const type of ['pointercancel', 'lostpointercapture']) scroller.addEventListener(type, event => finish(event, false));
    page.addEventListener('keydown', event => { if (event.key === 'Escape' && pan) finish({ pointerId: pan.id }, false); })
    }
    if (typeof document !== 'undefined') document.addEventListener('staff:schedule-mounted', mount);
    mount();
})();
