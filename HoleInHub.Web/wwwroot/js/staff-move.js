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
    const consent = dialog.querySelector('[data-upgrade-consent]');
    const heading = dialog.querySelector('h2');
    const submitLabel = submit.querySelector('[data-move-submit-label]');
    const setSubmitLabel = text => window.HoleInHubButtons ? window.HoleInHubButtons.setLabel(submit, text) : submitLabel.textContent = text;
    const notify = message => window.HoleInHubToasts?.show({ autoDismiss: true, kind: 'error', title: 'Booking not moved', message });
    const description = dialog.querySelector('[data-move-description]');
    const scroller = page.querySelector('.staff-timeline-scroll');
    const gesture = page.timelineGesture;
    const first = Number(page.dataset.firstHour) * 60;
    const minimum = Number(page.dataset.openingHour) * 60;
    let move, saving = false, rejection;
    const summary = dialog.querySelector('[data-batch-summary]');
    const selected = new Set();
    const labels = new WeakMap();
    let lastBatch;
    const movableBars = () => [...timeline.querySelectorAll('[data-move-booking]')];
    const bookingBars = () => [...timeline.querySelectorAll('.staff-timeline-bar[data-booking-id]')];

    function renderSelection() {
        for (const bar of bookingBars()) {
            const active = selected.has(bar.dataset.bookingId);
            bar.classList.toggle('is-batch-selected', active);
            if (!labels.has(bar)) labels.set(bar, bar.getAttribute('aria-label'));
            const label = labels.get(bar);
            if (label) bar.setAttribute('aria-label', active ? label + ', selected for move' : label);
        }
        const count = selected.size;
        window.HoleInHubToasts?.show({ kind: 'info', replaceKey: 'timeline-selection', durationMs: 2500, title: count ? count + ' selected' : 'Booking selection cleared',
            message: count > 1 ? 'Drag a selected booking or use Alt + Left or Right. Escape clears the selection.'
                : count ? 'Select another booking to move them together. Escape clears the selection.' : undefined });
    }
    function toggleBooking(bar) {
        if (!selected.delete(bar.dataset.bookingId)) {
            if (!bar.dataset.moveBooking || !isFuture(page.dataset.date, Number(bar.dataset.startMinute))) {
                notify('Only upcoming, confirmed Regular or VIP bookings can be selected.');
                return;
            }
            if (selected.size >= 20) { notify('Select up to 20 bookings at a time.'); return; }
            selected.add(bar.dataset.bookingId);
        }
        renderSelection();
    }
    page.addEventListener('click', event => {
        const bar = event.target.closest('.staff-timeline-bar[data-booking-id]');
        if (!bar || !event.shiftKey || event.button || event.ctrlKey || event.metaKey || event.altKey) return;
        event.preventDefault();
        event.stopPropagation();
        toggleBooking(bar);
    }, true);
    page.addEventListener('keydown', event => {
        const bar = event.target.closest('.staff-timeline-bar[data-booking-id]');
        if (bar && event.shiftKey && !event.ctrlKey && !event.metaKey && !event.altKey
            && (event.key === ' ' || event.key === 'Enter')) {
            event.preventDefault();
            if (!event.repeat) toggleBooking(bar);
        }
    }, true);
    function shake(bar) {
        if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;
        rejection?.cancel();
        rejection = bar.animate?.([
            { transform: 'translateX(0)' }, { transform: 'translateX(-5px)' },
            { transform: 'translateX(5px)' }, { transform: 'translateX(-3px)' }, { transform: 'translateX(0)' },
        ], { duration: 280, easing: 'ease-out' });
    }
    const time = minute => String(Math.floor(minute / 60)).padStart(2, '0') + ':' + String(minute % 60).padStart(2, '0');
    const isFuture = (date, minute) => !date || Date.parse(date + 'T' + time(minute) + ':00+08:00') > Date.now();
    const timeLabel = minute => new Intl.DateTimeFormat('en', { hour: 'numeric', minute: '2-digit', timeZone: 'UTC' })
        .format(new Date(Date.UTC(2000, 0, 1, 0, minute)));

    function begin(bar) {
        const track = bar.closest('.staff-timeline-track');
        let batch = null;
        if (selected.size > 1 && selected.has(bar.dataset.bookingId)) {
            const groups = new Map();
            for (const candidate of movableBars()) {
                const id = candidate.dataset.bookingId;
                if (!selected.has(id)) continue;
                if (!groups.has(id)) groups.set(id, { id, bars: [], booking: candidate });
                groups.get(id).bars.push(candidate);
            }
            batch = [...groups.values()];
        }
        const peers = !batch && Number(bar.dataset.bayCount) > 1 && bar.dataset.bookingId
            ? [...timeline.querySelectorAll('.staff-timeline-bar[data-booking-id]')]
                .filter(other => other !== bar && other.dataset.bookingId === bar.dataset.bookingId)
                .map(other => ({ bar: other, resource: other.closest('.staff-timeline-track').dataset.createBay }))
            : [];
        move = { bar, batch, peers, track, target: track, lastTarget: track, original: bar.style.getPropertyValue('--start'), start: Number(bar.dataset.startMinute),
            date: page.dataset.date, first, minimum, scroll: scroller?.scrollLeft || 0, duration: Number(bar.dataset.duration), command: crypto.randomUUID().replaceAll('-', '') };
    }
    function makeGhost(bar) {
        const ghost = bar.cloneNode(true);
        ghost.removeAttribute('href');
        ghost.removeAttribute('id');
        ghost.setAttribute('aria-hidden', 'true');
        ghost.classList.add('staff-moving-ghost', 'is-moving');
        document.body.append(ghost);
        bar.style.visibility = 'hidden';
        return ghost;
    }
    function lift() {
        if (move.ghost) return;
        move.ghost = makeGhost(move.bar);
        if (move.batch) {
            for (const item of move.batch) {
                item.ghosts = item.bars.map(bar => ({
                    bar, track: bar.closest('.staff-timeline-track'),
                    ghost: bar === move.bar ? move.ghost : makeGhost(bar),
                }));
            }
        }
        for (const peer of move.peers) peer.ghost = makeGhost(peer.bar);
    }
    function updateBatch(minute) {
        const offset = Math.round((minute - move.start) / 30) * 30;
        move.offset = offset;
        move.conflict = '';
        const movingIds = new Set(move.batch.map(item => item.id));
        for (const item of move.batch) {
            const start = Number(item.booking.dataset.startMinute) + offset;
            const duration = Number(item.booking.dataset.duration);
            item.start = start;
            item.end = start + duration;
            for (const entry of item.ghosts) {
                const occupied = [...entry.track.querySelectorAll('[data-holds-capacity="true"]')]
                    .filter(bar => !movingIds.has(bar.dataset.bookingId)).map(bar => {
                        const start = parseFloat(bar.style.getPropertyValue('--start'));
                        return [start, start + parseFloat(bar.style.getPropertyValue('--span'))];
                    });
                const range = moveRange(first, minimum, duration, occupied, start);
                entry.invalid = !range.valid || !entry.track.dataset.createBay || !isFuture(move.date, start);
                if (entry.invalid && !move.conflict) {
                    const bay = entry.track.closest('[data-bay-type]').getAttribute('aria-label');
                    move.conflict = item.booking.dataset.reference + ' cannot move to ' + timeLabel(start) + ' on ' + bay + '.';
                }
                const left = Math.max(0, Math.min(100 - duration / (1440 - first) * 100, range.left));
                positionGhost(entry.ghost, entry.track, left, duration, entry.invalid);
            }
        }
    }
    function update(minute, target = move.track) {
        const tracks = move.peers.length ? [...timeline.querySelectorAll('[data-create-bay]')] : [];
        const retained = move.peers.map(peer => tracks.find(track => track.dataset.createBay === peer.resource));
        const occupied = [target || move.track, ...retained.filter(Boolean)]
            .flatMap(track => [...track.querySelectorAll('[data-holds-capacity="true"]')])
            .filter(other => other.dataset.moveBooking !== move.bar.dataset.moveBooking).map(other => {
                const start = parseFloat(other.style.getPropertyValue('--start'));
                return [start, start + parseFloat(other.style.getPropertyValue('--span'))];
            });
        const sourceType = move.track.closest('[data-bay-type]').dataset.bayType;
        const targetType = target?.closest('[data-bay-type]').dataset.bayType;
        move.upgrade = sourceType === 'Regular' && targetType === 'VIP';
        const compatible = target?.dataset.createBay && (sourceType === targetType || (move.upgrade && Number(move.bar.dataset.bayCount) === 1));
        const alreadySelected = retained.includes(target);
        move.target = target;
        if (target) move.lastTarget = target;
        move.range = moveRange(move.first, move.minimum, move.duration, occupied, minute);
        move.range.valid = move.range.valid && !!compatible && !alreadySelected && retained.every(Boolean);
        if (move.date && Date.parse(`${move.date}T${time(move.range.start)}:00+08:00`) <= Date.now()) move.range.valid = false;
        const left = Math.max(0, Math.min(100 - move.duration / (1440 - move.first) * 100, move.range.left));
        move.bar.style.setProperty('--start', left + '%');
        const offset = target ? target.getBoundingClientRect().top - move.track.getBoundingClientRect().top : 0;
        move.bar.style.translate = `0 ${offset}px`;
        move.bar.classList.toggle('is-unavailable', !move.range.valid);
        if (move.ghost) {
            positionGhost(move.ghost, target, left);
            move.peers.forEach((peer, index) => positionGhost(peer.ghost, retained[index], left));
        }
    }
    function positionGhost(ghost, target, left, duration = move.duration, invalid = !move.range.valid) {
        ghost.hidden = !target;
        if (target) {
            const rect = target.getBoundingClientRect();
            ghost.style.left = rect.left + rect.width * left / 100 + 'px';
            ghost.style.top = rect.top + 'px';
            ghost.style.width = rect.width * duration / (1440 - move.first) + 'px';
            ghost.style.height = Math.max(48, rect.height / Number(target.style.getPropertyValue('--lanes') || 1)) + 'px';
            const viewport = scroller.getBoundingClientRect();
            const canvasLeft = viewport.left + timeline.querySelector('.staff-timeline-label').offsetWidth;
            const ghostLeft = parseFloat(ghost.style.left);
            const ghostTop = parseFloat(ghost.style.top);
            const ghostRight = ghostLeft + parseFloat(ghost.style.width);
            const ghostBottom = ghostTop + parseFloat(ghost.style.height);
            ghost.style.clipPath = `inset(${Math.max(0, viewport.top - ghostTop)}px ${Math.max(0, ghostRight - viewport.right + 1)}px ${Math.max(0, ghostBottom - viewport.bottom + 1)}px ${Math.max(0, canvasLeft - ghostLeft)}px)`;
        }
        ghost.classList.toggle('is-unavailable', invalid);
    }
    function restore() {
        if (!move) return;
        clearTimeout(move.edgeTimer);
        clearTimeout(move.holdTimer);
        move.request?.abort();
        if (move.originalRows) {
            timeline.replaceChildren(...move.originalRows);
            timeline.style.setProperty('--hours', (1440 - first) / 60);
            scroller.scrollLeft = move.originalScroll;
        }
        if (!move.batch) move.ghost?.remove();
        for (const item of move.batch || []) {
            for (const entry of item.ghosts || []) {
                entry.ghost.remove();
                entry.bar.style.removeProperty('visibility');
            }
        }
        for (const peer of move.peers) {
            peer.ghost?.remove();
            peer.bar.style.removeProperty('visibility');
        }
        delete page.dataset.moving;
        delete page.dataset.previewDate;
        gesture?.hide();
        gesture?.setDate(page.dataset.date);
        timeline.style.removeProperty('--pan-offset');
        move.bar.style.setProperty('--start', move.original);
        move.bar.style.removeProperty('translate');
        move.bar.style.removeProperty('visibility');
        move.bar.classList.remove('is-moving', 'is-unavailable');
        if (move.keyboard) move.bar.focus({ preventScroll: true });
        else move.bar.blur?.();
        move = undefined;
    }
    function confirmBatch() {
        if (!move.offset) { restore(); return; }
        if (move.conflict) {
            const { bar, conflict } = move;
            restore();
            shake(bar);
            notify(conflict);
            return;
        }
        const key = JSON.stringify([move.date, move.offset,
            move.batch.map(({ id, booking }) => [id, booking.dataset.revision]).sort()]);
        if (lastBatch?.key !== key) lastBatch = { key, command: move.command };
        move.command = lastBatch.command;
        heading.textContent = 'Move ' + move.batch.length + ' bookings?';
        description.textContent = 'Each booking keeps its assigned bays and duration.';
        summary.replaceChildren(...move.batch.map(item => {
            const row = document.createElement('li');
            const name = document.createElement('strong');
            const details = document.createElement('span');
            name.textContent = item.booking.dataset.guest + ' · ' + item.booking.dataset.reference;
            const bays = item.bars.map(bar => bar.dataset.location).join(', ');
            details.textContent = bays + ' · ' + item.booking.dataset.interval + ' → ' + timeLabel(item.start) + '–' + timeLabel(item.end);
            row.append(name, details);
            return row;
        }));
        summary.hidden = false;
        consent.hidden = true;
        form.elements.confirmVipUpgrade.checked = false;
        form.elements.confirmVipUpgrade.required = false;
        submit.disabled = false;
        setSubmitLabel('Confirm batch move');
        dialog.showModal();
    }
    function confirm() {
        if (move.batch) { confirmBatch(); return; }
        if (summary) summary.hidden = true;
        if (!move.range.valid) {
            const bar = move.bar;
            restore();
            shake(bar);
            notify(Number(bar.dataset.bayCount) > 1
                ? 'Choose a time when every assigned bay is open. To switch a bay, choose an unused bay of the same type.'
                : 'Choose an open bay within booking hours. Only single-bay Regular reservations can upgrade to VIP.');
            return;
        }
        if (move.loading) { const bar = move.bar; restore(); shake(bar); notify('Wait for the destination day to load, then try the move again.'); return; }
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
        if (move.peers.length) description.textContent += ' The other assigned bays move to the same time.';
        heading.textContent = move.upgrade ? 'Upgrade to VIP?' : 'Move booking?';
        setSubmitLabel(move.upgrade ? 'Confirm VIP upgrade' : 'Confirm move');
        consent.hidden = !move.upgrade;
        form.elements.confirmVipUpgrade.checked = false;
        form.elements.confirmVipUpgrade.required = !!move.upgrade;
        submit.disabled = !!move.upgrade;
        if (move.upgrade) description.textContent += ' This changes the reservation from Regular to VIP. Confirm the VIP rate with the customer.';
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
            if (move === current && !current.request.signal.aborted) { notify(error.message); gesture.hide(); }
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
            shake(bar);
            notify('This reservation has already started and cannot be dragged.');
            return;
        }
        event.preventDefault();
        begin(bar);
        page.dataset.moving = 'true';
        move.pointer = event.pointerId;
        move.origin = event.clientX;
        move.originY = event.clientY;
        if (event.pointerType === 'touch') {
            move.holdTimer = setTimeout(() => {
                if (!move || move.bar !== bar || move.dragged) return;
                move.held = true;
                toggleBooking(bar);
            }, 450);
        }
        move.width = move.track.getBoundingClientRect().width;
        (gesture ? timeline : bar).setPointerCapture(event.pointerId);
    });
    function place(event) {
        if (move.batch) {
            updateBatch(move.start + (event.clientX - move.origin + scroller.scrollLeft - move.scroll) / move.width * (1440 - first));
            return;
        }
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
    timeline.addEventListener('contextmenu', event => {
        if (move?.holdTimer || (event.pointerType === 'touch' && event.target.closest('[data-move-booking]'))) event.preventDefault();
    });
    timeline.addEventListener('pointermove', event => {
        if (move?.pointer !== event.pointerId || move.held) return;
        if (!move.dragged && Math.hypot(event.clientX - move.origin, event.clientY - move.originY) < 5) return;
        clearTimeout(move.holdTimer);
        if (gesture || move.batch) lift();
        move.dragged = true;
        move.point = { clientX: event.clientX, clientY: event.clientY };
        move.bar.classList.add('is-moving');
        place(event);
        if (!move.batch) edgeAt(event);
    });
    timeline.addEventListener('pointerup', event => {
        if (move?.pointer !== event.pointerId) return;
        const { bar, dragged, held } = move;
        move.pointer = undefined;
        clearTimeout(move.edgeTimer);
        (gesture ? timeline : bar).releasePointerCapture(event.pointerId);
        if (!dragged) { restore(); if (!held) bar.click(); }
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
        if (!bar || move || !event.altKey || !['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'PageUp', 'PageDown'].includes(event.key)) return;
        event.preventDefault();
        begin(bar);
        move.keyboard = true;
        if (event.key === 'PageUp' || event.key === 'PageDown') {
            if (!gesture || move.batch) { restore(); return; }
            const current = move;
            void changeDay(event.key === 'PageUp' ? -1 : 1).then(() => {
                if (move !== current) return;
                if (move.range) confirm();
                else restore();
            });
            return;
        }
        if (move.batch) {
            if (event.key === 'ArrowUp' || event.key === 'ArrowDown') { restore(); return; }
            lift();
            updateBatch(move.start + (event.key === 'ArrowLeft' ? -30 : 30));
            confirm();
            return;
        }
        if (gesture && move.peers.length) lift();
        if (event.key === 'ArrowUp' || event.key === 'ArrowDown') {
            const tracks = Array.from(timeline.querySelectorAll('[data-create-bay]'))
                .filter(track => {
                    const source = move.track.closest('[data-bay-type]').dataset.bayType;
                    const target = track.closest('[data-bay-type]').dataset.bayType;
                    return source === target || (source === 'Regular' && target === 'VIP' && Number(bar.dataset.bayCount) === 1);
                });
            const index = tracks.indexOf(move.track) + (event.key === 'ArrowUp' ? -1 : 1);
            update(move.start, tracks[index] || null);
        } else {
            update(move.start + (event.key === 'ArrowLeft' ? -30 : 30));
        }
        confirm();
    });
    page.addEventListener('keydown', event => {
        if (event.key !== 'Escape' || dialog.open) return;
        if (move) restore();
        if (selected.size) { selected.clear(); renderSelection(); }
    });
    form.elements.confirmVipUpgrade.addEventListener('change', () => { submit.disabled = saving || (move?.upgrade && !form.elements.confirmVipUpgrade.checked); });
    undo.addEventListener('click', () => { if (!saving) dialog.close(); });
    dialog.addEventListener('close', restore);
    dialog.addEventListener('cancel', event => { if (saving) event.preventDefault(); });
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (saving || !move) return;
        if (move.upgrade && !form.elements.confirmVipUpgrade.checked) { notify("Confirm the customer’s agreement to VIP pricing."); return; }
        saving = submit.disabled = undo.disabled = true;
        setSubmitLabel('Moving…');
        try {
            const body = new FormData(form);
            let url = form.action;
            if (move.batch) {
                url = form.dataset.batchUrl;
                body.set('commandId', move.command);
                body.set('date', move.date);
                body.set('offsetMinutes', move.offset);
                move.batch.forEach(({ id, booking }, index) => {
                    body.set('bookings[' + index + '].Id', id);
                    body.set('bookings[' + index + '].Revision', booking.dataset.revision);
                });
            }
            const response = await fetch(url, { method: 'POST', body, headers: { Accept: 'application/json' } });
            const result = await response.json();
            if (response.ok && result.redirect) { window.location.assign(result.redirect); return; }
            const bar = move.bar;
            dialog.close();
            shake(bar);
            notify(result.error || 'This booking could not be moved. Reload the schedule before trying again.');
        } catch {
            shake(move.ghost || move.bar);
            notify('The move could not be verified. Retry to check whether it saved, or reload the schedule.');
        }
        saving = undo.disabled = false;
        submit.disabled = !!move?.upgrade && !form.elements.confirmVipUpgrade.checked;
        setSubmitLabel(move?.batch ? 'Confirm batch move' : move?.upgrade ? 'Confirm VIP upgrade' : 'Confirm move');
    });
    for (const bar of timeline.querySelectorAll('[data-move-booking]')) {
        if (!bar.dataset.moveBooking) { bar.removeAttribute('data-move-booking'); continue; }
        bar.draggable = false;
        bar.setAttribute('aria-keyshortcuts', 'Alt+ArrowLeft Alt+ArrowRight Alt+ArrowUp Alt+ArrowDown Alt+PageUp Alt+PageDown');
        bar.title += '. Drag to another time or bay. Alt+Left/Right changes time, Alt+Up/Down changes bay, Alt+Page Up/Down changes day.';

    }
    }
    if (typeof document !== 'undefined') document.addEventListener('staff:schedule-mounted', mount);
    mount();
})();
