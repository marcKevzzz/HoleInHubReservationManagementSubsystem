(() => {
    function mount() {
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
        const notify = message => window.HoleInHubToasts?.show({ autoDismiss: true, kind: 'error', title: 'Could not save', message });
        const submit = form.querySelector('[type=submit]');
        const close = drawer.querySelector('[method=dialog] button');
        let saving = false;

        function open(range) {
            form.reset();
            if (range) {
                for (const bay of form.querySelectorAll?.('[data-booking-bay]') ?? []) {
                    bay.checked = range.bays.includes(bay.value);
                }
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
        form.addEventListener('invalid', event => {
            event.preventDefault();
            if (event.target === form.querySelector(':invalid')) {
                notify(event.target.validationMessage);
                event.target.focus();
            }
        }, true);
        form.addEventListener('submit', async event => {
            event.preventDefault();
            if (saving) return;
            const bays = [...(form.querySelectorAll?.('[data-booking-bay]') ?? [])];
            const selected = bays.filter(bay => bay.checked);
            if (bays.length && (!selected.length || new Set(selected.map(bay => bay.dataset.bayService)).size > 1)) {
                notify('Choose one or more Regular bays or one or more VIP bays.');
                (selected[0] || bays[0]).focus();
                return;
            }
            saving = submit.disabled = close.disabled = true;
            try {
                const response = await fetch(form.action, {
                    method: 'POST', body: new FormData(form), headers: { Accept: 'application/json' },
                });
                const result = await response.json();
                if (response.ok && result.redirect) {
                    window.location.assign(result.redirect);
                    return;
                }
                notify(result.error || 'The request could not be saved. Check the form and try again.');
            } catch {
                notify('The save could not be verified. Retry without changing the form to check whether it saved.');
            }
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
    const selectedTracks = new Set();
    const previews = new Map();
    timeline.classList.add('staff-drag-enabled');
    let drag;

    function timeValue(minute) {
        return String(Math.floor(minute / 60)).padStart(2, '0') + ':' + String(minute % 60).padStart(2, '0');
    }
    const bayType = track => track.closest?.('[data-bay-type]')?.dataset.bayType;
    const bayName = track => track.closest?.('[data-bay-type]')?.getAttribute('aria-label') ?? track.dataset.createBay;
    const occupied = track => [...track.querySelectorAll('[data-holds-capacity="true"]')].map(bar => {
        const start = parseFloat(bar.style.getPropertyValue('--start'));
        return [start, start + parseFloat(bar.style.getPropertyValue('--span'))];
    });
    const isPast = start => page.dataset.date && Date.parse(page.dataset.date + 'T' + timeValue(start) + ':00+08:00') <= Date.now();
    const warn = message => window.HoleInHubToasts?.show({ autoDismiss: true, kind: 'warning', title: 'Booking unavailable', message });

    function renderPreviews(tracks, range, label, draft) {
        for (const [track, preview] of previews) {
            if (!tracks.includes(track)) { preview.remove(); previews.delete(track); }
        }
        for (const track of tracks) {
            let preview = previews.get(track);
            if (!preview) {
                preview = document.createElement('span');
                preview.className = 'staff-drag-preview';
                preview.setAttribute('aria-hidden', 'true');
                previews.set(track, preview);
                track.append(preview);
            }
            preview.style.left = range.left + '%';
            preview.style.width = (range.right - range.left) + '%';
            preview.textContent = label;
            preview.classList.toggle('is-selected', draft);
            preview.classList.toggle('is-unavailable', range.invalidTracks.has(track));
        }
    }

    function selectTrack(track) {
        if (!selectedTracks.delete(track)) {
            if ([...selectedTracks].some(other => bayType(other) !== bayType(track))) {
                warn('Choose bays of the same type. Press Escape to clear the current bays.');
                return;
            }
            selectedTracks.add(track);
        }
        track.classList.toggle('is-create-bay-selected', selectedTracks.has(track));
        const count = selectedTracks.size;
        window.HoleInHubToasts?.show({ kind: 'info', replaceKey: 'timeline-selection', durationMs: 2500, title: count ? count + (count === 1 ? ' bay selected' : ' bays selected') : 'Bay selection cleared',
            message: count ? 'Ctrl-click another bay, or drag across a time on a selected bay. Escape clears the selection.' : undefined });
    }

    function clearSelection() {
        for (const track of selectedTracks) track.classList.remove('is-create-bay-selected');
        selectedTracks.clear();
    }

    function minuteAt(track, x) {
        const rect = track.getBoundingClientRect();
        return firstMinute + (x - rect.left) / rect.width * total;
    }

    function update(x) {
        const current = minuteAt(drag.track, x);
        const range = selection(firstMinute, minimum, [], drag.anchor, current);
        const invalidTracks = new Set(drag.tracks.filter(track =>
            !selection(firstMinute, minimum, drag.occupied.get(track), drag.anchor, current).valid || isPast(range.start)));
        drag.range = { ...range, invalidTracks, bays: drag.tracks.map(track => track.dataset.createBay) };
        renderPreviews(drag.tracks, drag.range, timeValue(range.start) + '–' + timeValue(range.end), false);
    }

    function release() {
        const { track, pointer } = drag;
        drag = undefined;
        if (track.hasPointerCapture(pointer)) track.releasePointerCapture(pointer);
    }

    timeline.addEventListener('pointerdown', event => {
        const track = event.target.closest('[data-create-bay]');
        const completed = event.target.closest('a[data-status="Completed"]');
        if (drag || page.dataset.moving || event.pointerType !== 'mouse' || event.button !== 0 || !track
            || (event.target !== track && !completed) || (!event.ctrlKey && !selectedTracks.has(track) && !page.dataset.customerProfileId)
            || event.metaKey || event.shiftKey || event.altKey) return;
        event.preventDefault();
        const tracks = [...new Set([...selectedTracks, track])];
        drag = { track, tracks, pointer: event.pointerId, origin: event.clientX, anchor: minuteAt(track, event.clientX),
            ctrl: event.ctrlKey, occupied: new Map(tracks.map(track => [track, occupied(track)])) };
        track.setPointerCapture(event.pointerId);
    });
    timeline.addEventListener('pointermove', event => {
        if (drag?.pointer === event.pointerId && (drag.range || Math.abs(event.clientX - drag.origin) >= 5)) update(event.clientX);
    });
    timeline.addEventListener('click', event => {
        if (!event.ctrlKey || event.metaKey || event.altKey || event.shiftKey || event.button) return;
        const track = event.target.closest('[data-create-bay]');
        const label = !track && event.target.closest('.staff-timeline-label');
        const bay = track || (label && label.closest('[data-bay-type]')?.querySelector('[data-create-bay]'));
        if (!bay || (!label && event.target !== bay && !event.target.closest('a[data-status="Completed"]'))) return;
        event.preventDefault();
        event.stopImmediatePropagation();
        if (event.detail < 2) selectTrack(bay);
    });
    timeline.addEventListener('pointerup', event => {
        if (drag?.pointer !== event.pointerId) return;
        if (!drag.range && Math.abs(event.clientX - drag.origin) < 5 && (drag.ctrl || !page.dataset.customerProfileId)) {
            release();
            return;
        }
        update(event.clientX);
        const { range, tracks } = drag;
        release();
        const suppress = click => { click.preventDefault(); click.stopImmediatePropagation(); };
        timeline.addEventListener('click', suppress, { capture: true, once: true });
        setTimeout(() => timeline.removeEventListener('click', suppress, true), 0);
        const conflict = range.invalidTracks.values().next().value;
        if (conflict || new Set(tracks.map(bayType)).size > 1) {
            warn(conflict ? 'Choose a future, open range for ' + bayName(conflict) + '.' : 'Choose bays of the same type.');
            if (!conflict || window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
                renderPreviews([]);
            } else {
                const rejected = previews.get(conflict);
                rejected.animate([{ translate: '0' }, { translate: '-6px' }, { translate: '6px' },
                    { translate: '0' }], { duration: 300, easing: 'ease-out' }).finished.then(() => {
                    if (!drag && !drawer.open && previews.get(conflict) === rejected) renderPreviews([]);
                });
            }
            return;
        }
        clearSelection();
        openBooking(range);
        updateDraft();
    });

    function updateDraft() {
        if (!drawer.open || drag) return;
        const [hour, minute] = form.elements.StartTime.value.split(':').map(Number);
        const start = hour * 60 + minute;
        const end = start + Number(form.elements.DurationMinutes.value);
        const ids = [...(form.querySelectorAll?.('[data-booking-bay]:checked') ?? [])].map(bay => bay.value);
        const tracks = [...timeline.querySelectorAll('[data-create-bay]')].filter(track => ids.includes(track.dataset.createBay));
        if ((form.elements.Date && form.elements.Date.value !== page.dataset.date)
            || !Number.isFinite(start + end) || start < firstMinute || end <= start || end > 1440) {
            renderPreviews([]);
            return;
        }
        const left = (start - firstMinute) / total * 100;
        const right = (end - firstMinute) / total * 100;
        const invalidTracks = new Set(tracks.filter(track => start < minimum || isPast(start)
            || occupied(track).some(([from, to]) => from < right - .0001 && to > left + .0001)));
        const guest = [form.elements.FirstName?.value, form.elements.LastName?.value].filter(Boolean).join(' ').trim();
        renderPreviews(tracks, { left, right, invalidTracks }, (guest || 'New booking') + ' · ' + timeValue(start) + '–' + timeValue(end), true);
    }
    form.addEventListener('input', updateDraft);
    form.addEventListener('change', updateDraft);
    drawer.addEventListener('close', () => renderPreviews([]));
    for (const type of ['pointercancel', 'lostpointercapture']) {
        timeline.addEventListener(type, event => {
            if (drag?.pointer === event.pointerId) { release(); renderPreviews([]); }
        });
    }
    page.addEventListener('keydown', event => {
        if (event.key !== 'Escape') return;
        if (drag) { release(); renderPreviews([]); }
        clearSelection();
    });
    }
    if (typeof document !== 'undefined') document.addEventListener('staff:schedule-mounted', mount);
    mount();
})();
