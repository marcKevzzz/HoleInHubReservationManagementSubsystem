(() => {
    function moveRange(first, minimum, duration, occupied, minute) {
        const start = Math.round(minute / 30) * 30;
        const end = start + duration;
        const left = (start - first) / (1440 - first) * 100;
        const right = (end - first) / (1440 - first) * 100;
        return { start, end, left, valid: start >= minimum && end <= 1440
            && !occupied.some(([from, to]) => from < right - .0001 && to > left + .0001) };
    }
    if (typeof module !== 'undefined' && module.exports) module.exports = moveRange;
    if (typeof document === 'undefined') return;
    const page = document.querySelector('[data-staff-schedule]');
    const dialog = page?.querySelector('[data-move-confirmation]');
    if (typeof dialog?.showModal !== 'function') return;
    const timeline = page.querySelector('.staff-timeline');
    if (!timeline) return;
    const form = dialog.querySelector('[data-move-form]');
    const undo = dialog.querySelector('[data-move-undo]');
    const submit = form.querySelector('[type=submit]');
    const error = dialog.querySelector('[data-move-error]');
    const description = dialog.querySelector('[data-move-description]');
    const dragError = page.querySelector('[data-drag-error]');
    const first = Number(page.dataset.firstHour) * 60;
    const minimum = Number(page.dataset.openingHour) * 60;
    let move, saving = false;
    const time = minute => String(Math.floor(minute / 60)).padStart(2, '0') + ':' + String(minute % 60).padStart(2, '0');
    const timeLabel = minute => new Intl.DateTimeFormat('en', { hour: 'numeric', minute: '2-digit', timeZone: 'UTC' })
        .format(new Date(Date.UTC(2000, 0, 1, 0, minute)));

    function begin(bar) {
        dragError.hidden = true;
        const track = bar.closest('.staff-timeline-track');
        move = { bar, track, target: track, original: bar.style.getPropertyValue('--start'), start: Number(bar.dataset.startMinute),
            duration: Number(bar.dataset.duration), command: crypto.randomUUID().replaceAll('-', '') };
    }
    function update(minute, target = move.track) {
        const occupied = Array.from((target || move.track).querySelectorAll('[data-holds-capacity="true"]'))
            .filter(other => other.dataset.moveBooking !== move.bar.dataset.moveBooking).map(other => {
                const start = parseFloat(other.style.getPropertyValue('--start'));
                return [start, start + parseFloat(other.style.getPropertyValue('--span'))];
            });
        const compatible = target?.dataset.createBay && target.closest('[data-bay-type]').dataset.bayType === move.track.closest('[data-bay-type]').dataset.bayType;
        const alreadySelected = target !== move.track && target?.querySelector(`[data-move-booking="${move.bar.dataset.moveBooking}"]`);
        move.target = target;
        move.range = moveRange(first, minimum, move.duration, occupied, minute);
        move.range.valid = move.range.valid && !!compatible && !alreadySelected;
        move.bar.style.setProperty('--start', move.range.left + '%');
        const offset = target ? target.getBoundingClientRect().top - move.track.getBoundingClientRect().top : 0;
        move.bar.style.translate = `0 ${offset}px`;
        move.bar.classList.toggle('is-unavailable', !move.range.valid);
    }
    function restore() {
        if (!move) return;
        move.bar.style.setProperty('--start', move.original);
        move.bar.style.removeProperty('translate');
        move.bar.classList.remove('is-moving', 'is-unavailable');
        move.bar.focus({ preventScroll: true });
        move = undefined;
    }
    function confirm() {
        if (!move.range.valid) {
            dragError.textContent = 'Choose an open bay of the same type within booking hours.';
            dragError.hidden = false;
            restore();
            return;
        }
        if (move.range.start === move.start && move.target === move.track) { restore(); return; }
        const { bar, range } = move;
        form.action = bar.dataset.moveBooking;
        form.elements.revision.value = bar.dataset.revision;
        form.elements.commandId.value = move.command;
        form.elements.startTime.value = time(range.start);
        form.elements.resourceId.value = move.target.dataset.createBay;
        form.elements.sourceResourceId.value = move.track.dataset.createBay;
        const destination = move.target.closest('[data-bay-type]').getAttribute('aria-label');
        description.textContent = `${bar.dataset.guest} · ${bar.dataset.reference}. Move from ${bar.dataset.location}, ${bar.dataset.interval} to ${destination}, ${timeLabel(range.start)}–${timeLabel(range.end)}?`;
        error.hidden = true;
        dialog.showModal();
    }
    timeline.addEventListener('pointerdown', event => {
        const bar = event.target.closest('[data-move-booking]');
        if (!bar || move || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        event.preventDefault();
        begin(bar);
        move.pointer = event.pointerId;
        move.origin = event.clientX;
        move.originY = event.clientY;
        move.width = move.track.getBoundingClientRect().width;
        bar.setPointerCapture(event.pointerId);
    });
    timeline.addEventListener('pointermove', event => {
        if (move?.pointer !== event.pointerId) return;
        if (!move.dragged && Math.hypot(event.clientX - move.origin, event.clientY - move.originY) < 5) return;
        move.dragged = true;
        move.bar.classList.add('is-moving');
        const target = Array.from(timeline.querySelectorAll('.staff-timeline-track')).find(track => {
            const rect = track.getBoundingClientRect();
            return event.clientY >= rect.top && event.clientY < rect.bottom && event.clientX >= rect.left && event.clientX < rect.right;
        });
        update(move.start + (event.clientX - move.origin) / move.width * (1440 - first), target || null);
    });
    timeline.addEventListener('pointerup', event => {
        if (move?.pointer !== event.pointerId) return;
        const { bar, dragged } = move;
        move.pointer = undefined;
        bar.releasePointerCapture(event.pointerId);
        if (!dragged) { restore(); bar.click(); }
        else confirm();
        const suppress = click => { click.preventDefault(); click.stopImmediatePropagation(); };
        timeline.addEventListener('click', suppress, { capture: true, once: true });
        setTimeout(() => timeline.removeEventListener('click', suppress, true), 0);
    });
    for (const type of ['pointercancel', 'lostpointercapture']) {
        timeline.addEventListener(type, event => { if (move?.pointer === event.pointerId) restore(); });
    }
    timeline.addEventListener('keydown', event => {
        const bar = event.target.closest('[data-move-booking]');
        if (!bar || move || !event.altKey || !['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key)) return;
        event.preventDefault();
        begin(bar);
        if (event.key === 'ArrowUp' || event.key === 'ArrowDown') {
            const tracks = Array.from(timeline.querySelectorAll('[data-create-bay]'))
                .filter(track => track.closest('[data-bay-type]').dataset.bayType === move.track.closest('[data-bay-type]').dataset.bayType);
            const index = tracks.indexOf(move.track) + (event.key === 'ArrowUp' ? -1 : 1);
            update(move.start, tracks[index] || null);
        } else {
            update(move.start + (event.key === 'ArrowLeft' ? -30 : 30));
        }
        confirm();
    });
    document.addEventListener('keydown', event => { if (event.key === 'Escape' && move && !dialog.open) restore(); });
    undo.addEventListener('click', () => { if (!saving) dialog.close(); });
    dialog.addEventListener('close', restore);
    dialog.addEventListener('cancel', event => { if (saving) event.preventDefault(); });
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (saving || !move) return;
        saving = submit.disabled = undo.disabled = true;
        submit.textContent = 'Moving…';
        error.hidden = true;
        try {
            const response = await fetch(form.action, { method: 'POST', body: new FormData(form), headers: { Accept: 'application/json' } });
            const result = await response.json();
            if (response.ok && result.redirect) { window.location.assign(result.redirect); return; }
            error.textContent = result.error || 'This booking could not be moved. Undo and reload the schedule.';
        } catch {
            error.textContent = 'The move could not be verified. Continue again to check whether it saved, or reload the schedule.';
        }
        error.hidden = false;
        error.focus();
        saving = submit.disabled = undo.disabled = false;
        submit.textContent = 'Continue';
    });
    for (const bar of timeline.querySelectorAll('[data-move-booking]')) {
        bar.draggable = false;
        bar.setAttribute('aria-keyshortcuts', 'Alt+ArrowLeft Alt+ArrowRight Alt+ArrowUp Alt+ArrowDown');
        bar.title += '. Drag to another time or bay. Alt+Left/Right changes time, Alt+Up/Down changes bay.';
    }
})();
