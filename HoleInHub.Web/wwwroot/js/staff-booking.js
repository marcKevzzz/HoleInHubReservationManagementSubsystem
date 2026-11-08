(() => {
    function selection(firstMinute, minimum, occupied, anchor, current) {
        const slot = minute => Math.min(1410, Math.max(firstMinute, Math.floor(minute / 30 + 0.000001) * 30));
        const start = slot(Math.min(anchor, current));
        const end = slot(Math.max(anchor, current)) + 30;
        const total = 1440 - firstMinute;
        const left = (start - firstMinute) / total * 100;
        const right = (end - firstMinute) / total * 100;
        const collision = occupied.some(([from, to]) => from < right - 0.0001 && to > left + 0.0001);
        return { start, end, left, right, valid: start >= minimum && !collision };
    }

    if (typeof module !== 'undefined' && module.exports) module.exports = selection;
    if (typeof document === 'undefined') return;

    const page = document.querySelector('[data-staff-schedule]');
    const drawer = page?.querySelector('[data-create-booking]');
    if (typeof drawer?.showModal !== 'function') return;

    function setupDrawer(drawer, trigger) {
        if (!trigger || typeof drawer?.showModal !== 'function') return;
        const form = drawer.querySelector('.staff-create-form');
        const error = form.querySelector('[data-booking-error]');
        const submit = form.querySelector('[type=submit]');
        const close = drawer.querySelector('[method=dialog] button');
        let saving = false;

        function open(range) {
            form.reset();
            error.hidden = true;
            if (range) {
                form.elements.ResourceId.value = range.bay;
                form.elements.StartTime.value = timeValue(range.start);
                form.elements.DurationMinutes.value = range.end - range.start;
            }
            drawer.showModal();
        }

        trigger.addEventListener('click', event => {
            if (event.button || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
            event.preventDefault();
            open();
        });
        drawer.addEventListener('close', () => trigger.focus({ preventScroll: true }));
        drawer.addEventListener('cancel', event => { if (saving) event.preventDefault(); });
        form.addEventListener('submit', async event => {
            event.preventDefault();
            if (saving) return;
            saving = submit.disabled = close.disabled = true;
            error.hidden = true;
            try {
                const response = await fetch(form.action, {
                    method: 'POST', body: new FormData(form), headers: { Accept: 'application/json' },
                });
                const result = await response.json();
                if (response.ok && result.redirect) {
                    window.location.assign(result.redirect);
                    return;
                }
                error.textContent = result.error || 'The request could not be saved. Check the form and try again.';
            } catch {
                error.textContent = 'The save could not be verified. Retry without changing the form to check whether it saved.';
            }
            error.hidden = false;
            error.focus();
            saving = submit.disabled = close.disabled = false;
        });
        return open;
    }

    const openBooking = setupDrawer(drawer, page.querySelector('[data-new-booking]'));
    setupDrawer(page.querySelector('[data-create-block]'), page.querySelector('[data-new-block]'));
    const form = drawer.querySelector('.staff-create-form');

    const timeline = page.querySelector('.staff-timeline');
    if (!timeline?.querySelector('[data-create-bay]')) return;

    const firstMinute = Number(page.dataset.firstHour) * 60;
    const [defaultHour, defaultMinute] = form.elements.StartTime.defaultValue.split(':').map(Number);
    const minimum = Math.max(Number(page.dataset.openingHour) * 60, defaultHour * 60 + defaultMinute);
    const total = 1440 - firstMinute;
    const dragError = page.querySelector('[data-drag-error]');
    const preview = document.createElement('span');
    preview.className = 'staff-drag-preview';
    preview.setAttribute('aria-hidden', 'true');
    timeline.classList.add('staff-drag-enabled');
    let drag;

    function timeValue(minute) {
        return String(Math.floor(minute / 60)).padStart(2, '0') + ':' + String(minute % 60).padStart(2, '0');
    }

    function minuteAt(track, x) {
        const rect = track.getBoundingClientRect();
        return firstMinute + (x - rect.left) / rect.width * total;
    }

    function update(x) {
        const range = selection(firstMinute, minimum, drag.occupied, drag.anchor, minuteAt(drag.track, x));
        const { start, end, left, right, valid } = range;
        drag.range = { ...range, bay: drag.track.dataset.createBay };
        preview.style.left = left + '%';
        preview.style.width = (right - left) + '%';
        preview.textContent = timeValue(start) + '–' + timeValue(end);
        preview.classList.toggle('is-unavailable', !valid);
    }

    function clear() {
        if (!drag) return;
        const { track, pointer } = drag;
        drag = undefined;
        preview.remove();
        if (track.hasPointerCapture(pointer)) track.releasePointerCapture(pointer);
    }

    timeline.addEventListener('pointerdown', event => {
        const track = event.target.closest('[data-create-bay]');
        const completed = event.target.closest('a[data-status="Completed"]');
        if (drag || event.pointerType !== 'mouse' || event.button !== 0 || !track
            || (event.target !== track && !completed) || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        event.preventDefault();
        dragError.hidden = true;
        const occupied = Array.from(track.querySelectorAll('[data-holds-capacity="true"]'), bar => {
            const start = parseFloat(bar.style.getPropertyValue('--start'));
            return [start, start + parseFloat(bar.style.getPropertyValue('--span'))];
        });
        drag = { track, pointer: event.pointerId, anchor: minuteAt(track, event.clientX), occupied, completed, originX: event.clientX };
        track.setPointerCapture(event.pointerId);
        track.append(preview);
        update(event.clientX);
    });
    timeline.addEventListener('pointermove', event => {
        if (drag?.pointer === event.pointerId) update(event.clientX);
    });
    timeline.addEventListener('pointerup', event => {
        if (drag?.pointer !== event.pointerId) return;
        update(event.clientX);
        const { range, completed, originX } = drag;
        clear();
        if (completed) {
            const clicked = Math.abs(event.clientX - originX) < 5;
            if (clicked) completed.click();
            // Capture prevents the synthetic click after a drag from opening a second drawer.
            const suppress = click => { click.preventDefault(); click.stopImmediatePropagation(); };
            timeline.addEventListener('click', suppress, { capture: true, once: true });
            setTimeout(() => timeline.removeEventListener('click', suppress, true), 0);
            if (clicked) return;
        }
        if (range.valid) openBooking(range);
        else {
            dragError.textContent = 'Choose an open range within booking hours.';
            dragError.hidden = false;
        }
    });
    for (const type of ['pointercancel', 'lostpointercapture']) {
        timeline.addEventListener(type, event => { if (drag?.pointer === event.pointerId) clear(); });
    }
    document.addEventListener('keydown', event => { if (event.key === 'Escape') clear(); });
})();
