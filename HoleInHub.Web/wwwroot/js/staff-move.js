(() => {
    function mount() {
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
    const submitContent = submit.innerHTML;
    const error = dialog.querySelector('[data-move-error]');
    const description = dialog.querySelector('[data-move-description]');
    const dragError = page.querySelector('[data-drag-error]');
    const scroller = page.querySelector('.staff-timeline-scroll');
    const gesture = page.timelineGesture;
    const first = Number(page.dataset.firstHour) * 60;
    const minimum = Number(page.dataset.openingHour) * 60;
    let move, saving = false;
    const time = minute => String(Math.floor(minute / 60)).padStart(2, '0') + ':' + String(minute % 60).padStart(2, '0');
    const timeLabel = minute => new Intl.DateTimeFormat('en', { hour: 'numeric', minute: '2-digit', timeZone: 'UTC' })
        .format(new Date(Date.UTC(2000, 0, 1, 0, minute)));

    function begin(bar) {
        dragError.hidden = true;
        const track = bar.closest('.staff-timeline-track');
        move = { bar, track, target: track, lastTarget: track, original: bar.style.getPropertyValue('--start'), start: Number(bar.dataset.startMinute),
            date: page.dataset.date, first, minimum, scroll: scroller?.scrollLeft || 0, duration: Number(bar.dataset.duration), command: crypto.randomUUID().replaceAll('-', '') };
    }
    function lift() {
        if (move.ghost) return;
        move.ghost = move.bar.cloneNode(true);
        move.ghost.removeAttribute('href');
        move.ghost.removeAttribute('id');
        move.ghost.setAttribute('aria-hidden', 'true');
        move.ghost.classList.add('staff-moving-ghost', 'is-moving');
        document.body.append(move.ghost);
        move.bar.style.visibility = 'hidden';
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
        if (target) move.lastTarget = target;
        move.range = moveRange(move.first, move.minimum, move.duration, occupied, minute);
        move.range.valid = move.range.valid && !!compatible && !alreadySelected;
        if (move.date && Date.parse(`${move.date}T${time(move.range.start)}:00+08:00`) <= Date.now()) move.range.valid = false;
        const left = Math.max(0, Math.min(100 - move.duration / (1440 - move.first) * 100, move.range.left));
        move.bar.style.setProperty('--start', left + '%');
        const offset = target ? target.getBoundingClientRect().top - move.track.getBoundingClientRect().top : 0;
        move.bar.style.translate = `0 ${offset}px`;
        move.bar.classList.toggle('is-unavailable', !move.range.valid);
        if (move.ghost && target) {
            const rect = target.getBoundingClientRect();
            move.ghost.style.left = rect.left + rect.width * left / 100 + 'px';
            move.ghost.style.top = rect.top + 3 + 'px';
            move.ghost.style.width = rect.width * move.duration / (1440 - move.first) + 'px';
            move.ghost.style.height = Math.max(48, rect.height / Number(target.style.getPropertyValue('--lanes') || 1) - 4) + 'px';
            const viewport = scroller.getBoundingClientRect();
            const canvasLeft = viewport.left + timeline.querySelector('.staff-timeline-label').offsetWidth;
            const ghostLeft = parseFloat(move.ghost.style.left);
            const ghostTop = parseFloat(move.ghost.style.top);
            const ghostRight = ghostLeft + parseFloat(move.ghost.style.width);
            const ghostBottom = ghostTop + parseFloat(move.ghost.style.height);
            move.ghost.style.clipPath = `inset(${Math.max(0, viewport.top - ghostTop)}px ${Math.max(0, ghostRight - viewport.right + 1)}px ${Math.max(0, ghostBottom - viewport.bottom + 1)}px ${Math.max(0, canvasLeft - ghostLeft)}px)`;
        }
        if (move.ghost) move.ghost.classList.toggle('is-unavailable', !move.range.valid);
    }
    function restore() {
        if (!move) return;
        clearTimeout(move.edgeTimer);
        move.request?.abort();
        if (move.originalRows) {
            timeline.replaceChildren(...move.originalRows);
            timeline.style.setProperty('--hours', (1440 - first) / 60);
            scroller.scrollLeft = move.originalScroll;
        }
        move.ghost?.remove();
        delete page.dataset.moving;
        delete page.dataset.previewDate;
        gesture?.hide();
        gesture?.setDate(page.dataset.date);
        timeline.style.removeProperty('--pan-offset');
        move.bar.style.setProperty('--start', move.original);
        move.bar.style.removeProperty('translate');
        move.bar.style.removeProperty('visibility');
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
        if (move.loading) { dragError.textContent = 'Wait for the destination day to load, then try the move again.'; dragError.hidden = false; restore(); return; }
        if (move.range.start === move.start && move.target === move.track && move.date === page.dataset.date) { restore(); return; }
        const { bar, range } = move;
        form.action = bar.dataset.moveBooking;
        form.elements.revision.value = bar.dataset.revision;
        form.elements.commandId.value = move.command;
        form.elements.startTime.value = time(range.start);
        form.elements.resourceId.value = move.target.dataset.createBay;
        form.elements.sourceResourceId.value = move.track.dataset.createBay;
        if (form.elements.date) form.elements.date.value = move.date;
        const destination = move.target.closest('[data-bay-type]').getAttribute('aria-label');
        description.textContent = `${bar.dataset.guest} · ${bar.dataset.reference}. Move from ${bar.dataset.location}, ${bar.dataset.interval} to ${destination}, ${gesture ? gesture.dayLabel(move.date) + ', ' : ''}${timeLabel(range.start)}–${timeLabel(range.end)}?`;
        error.hidden = true;
        dialog.showModal();
    }
    async function changeDay(direction) {
        const current = move;
        if (!current || current.loading) return;
        const date = gesture.adjacentDay(current.date, direction);
        if (date < page.dataset.today) { gesture.preview(current.date, 'Past dates are unavailable'); return; }
        current.loading = true;
        current.request = new AbortController();
        gesture.preview(date, 'Loading schedule');
        try {
            const destination = await gesture.load(date, current.request.signal);
            if (move !== current || current.request.signal.aborted) return;
            if (!current.originalRows) {
                current.originalRows = [...timeline.childNodes];
                current.originalScroll = scroller.scrollLeft;
                lift();
            }
            const incoming = destination.querySelector('.staff-timeline');
            timeline.replaceChildren(...incoming.childNodes);
            timeline.classList.remove('has-now');
            current.date = page.dataset.previewDate = date;
            gesture.setDate(date);
            timeline.style.removeProperty('--pan-offset');
            current.first = Number(destination.dataset.firstHour) * 60;
            current.minimum = Number(destination.dataset.openingHour) * 60;
            timeline.style.setProperty('--hours', (1440 - current.first) / 60);
            const target = [...timeline.querySelectorAll('[data-create-bay]')].find(track => track.dataset.createBay === current.lastTarget.dataset.createBay);
            const minute = Math.max(current.start, current.minimum);
            if (target) {
                const width = target.getBoundingClientRect().width;
                scroller.scrollLeft = Math.max(0, (minute - current.first) / (1440 - current.first) * width - scroller.clientWidth / 3);
            }
            update(minute, target || null);
            gesture.transition(direction);
            gesture.preview(date, 'Drop to review move');
        } catch (error) {
            if (move === current && !current.request.signal.aborted) { dragError.textContent = error.message; dragError.hidden = false; gesture.hide(); }
        } finally { current.loading = false; }
    }
    function edgeAt(event) {
        if (!gesture || !scroller) return;
        const rect = scroller.getBoundingClientRect();
        const label = timeline.querySelector('.staff-timeline-label').offsetWidth;
        const direction = event.clientX < rect.left + label + 24 ? -1 : event.clientX > rect.right - 28 ? 1 : 0;
        if (move.loading || (move.edge === direction && move.edgeTimer)) return;
        clearTimeout(move.edgeTimer);
        move.edge = direction;
        timeline.style.setProperty('--pan-offset', window.matchMedia('(prefers-reduced-motion: reduce)').matches ? '0px' : `${-direction * 16}px`);
        if (direction) {
            const advance = () => {
                if (!move || move.edge !== direction || move.pointer === undefined) return;
                const before = scroller.scrollLeft;
                scroller.scrollLeft += direction * 48;
                if (scroller.scrollLeft !== before) {
                    place(move.point);
                    move.edgeTimer = setTimeout(advance, 70);
                } else {
                    gesture.preview(gesture.adjacentDay(move.date, direction), 'Hold to switch day');
                    move.edgeTimer = setTimeout(() => { move.edgeTimer = undefined; void changeDay(direction); }, 650);
                }
            };
            move.edgeTimer = setTimeout(advance, 100);
        } else if (!move.originalRows) gesture.hide();
    }
    timeline.addEventListener('pointerdown', event => {
        const bar = event.target.closest('[data-move-booking]');
        if (!bar || move || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        if (page.dataset.date && Date.parse(`${page.dataset.date}T${time(Number(bar.dataset.startMinute))}:00+08:00`) <= Date.now()) {
            dragError.textContent = 'This reservation has already started and cannot be dragged.';
            dragError.hidden = false;
            return;
        }
        event.preventDefault();
        begin(bar);
        page.dataset.moving = 'true';
        move.pointer = event.pointerId;
        move.origin = event.clientX;
        move.originY = event.clientY;
        move.width = move.track.getBoundingClientRect().width;
        (gesture ? timeline : bar).setPointerCapture(event.pointerId);
    });
    function place(event) {
        const canvasLeft = scroller ? scroller.getBoundingClientRect().left + timeline.querySelector('.staff-timeline-label').offsetWidth : -Infinity;
        const target = Array.from(timeline.querySelectorAll('.staff-timeline-track')).find(track => {
            const rect = track.getBoundingClientRect();
            return event.clientY >= rect.top && event.clientY < rect.bottom
                && (scroller || (event.clientX >= rect.left && event.clientX < rect.right));
        });
        if (move.originalRows && target) {
            const rect = target.getBoundingClientRect();
            const x = Math.max(canvasLeft, Math.min(scroller.getBoundingClientRect().right, event.clientX));
            const minute = move.first + (x - rect.left) / rect.width * (1440 - move.first) - move.duration / 2;
            update(Math.max(move.minimum, Math.min(1440 - move.duration, minute)), target);
        } else update(move.start + (event.clientX - move.origin + (scroller?.scrollLeft || 0) - move.scroll) / move.width * (1440 - first), target || null);
    }
    timeline.addEventListener('pointermove', event => {
        if (move?.pointer !== event.pointerId) return;
        if (!move.dragged && Math.hypot(event.clientX - move.origin, event.clientY - move.originY) < 5) return;
        if (gesture) lift();
        move.dragged = true;
        move.point = { clientX: event.clientX, clientY: event.clientY };
        move.bar.classList.add('is-moving');
        place(event);
        edgeAt(event);
    });
    timeline.addEventListener('pointerup', event => {
        if (move?.pointer !== event.pointerId) return;
        const { bar, dragged } = move;
        move.pointer = undefined;
        clearTimeout(move.edgeTimer);
        (gesture ? timeline : bar).releasePointerCapture(event.pointerId);
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
    page.addEventListener('keydown', event => { if (event.key === 'Escape' && move && !dialog.open) restore(); });
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
        submit.innerHTML = submitContent;
    });
    for (const bar of timeline.querySelectorAll('[data-move-booking]')) {
        if (!bar.dataset.moveBooking) { bar.removeAttribute('data-move-booking'); continue; }
        bar.draggable = false;
        bar.setAttribute('aria-keyshortcuts', 'Alt+ArrowLeft Alt+ArrowRight Alt+ArrowUp Alt+ArrowDown');
        bar.title += '. Drag to another time or bay. Alt+Left/Right changes time, Alt+Up/Down changes bay.';

    }
    }
    if (typeof document !== 'undefined') document.addEventListener('staff:schedule-mounted', mount);
    mount();
})();
