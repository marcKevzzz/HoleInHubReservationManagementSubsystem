const { test } = require('node:test');
const assert = require('node:assert/strict');
const range = require('../HoleInHub.Web/wwwroot/js/staff-booking.js');

test('staff drag snaps to half-hour slots and accepts reverse selection', () => {
    const selection = range(600, 600, [], 820, 792);
    assert.equal(selection.start, 780);
    assert.equal(selection.end, 840);
    assert.equal(selection.valid, true);
});

test('staff drag rejects occupied time and allows immediately adjacent slots', () => {
    const occupied = [[25, 50]];
    assert.equal(range(0, 0, occupied, 330, 390).valid, false);
    assert.equal(range(0, 0, occupied, 300, 330).valid, true);
    assert.equal(range(0, 0, occupied, 720, 750).valid, true);
});

test('staff drag respects the earliest start and midnight', () => {
    assert.equal(range(600, 630, [], 605, 640).valid, false);
    const last = range(600, 600, [], 1415, 1460);
    assert.equal(last.start, 1410);
    assert.equal(last.end, 1440);
});

const vm = require('node:vm');
const fs = require('node:fs');

test('overdue requests keep staff actions until the session starts', () => {
    const due = '2026-11-10T06:30:00Z';
    const closes = '2026-11-10T08:00:00Z';
    let now = Date.parse(due);
    let tick;
    const countdown = {};
    const deadline = { dataset: { deadline: due }, querySelector: () => countdown };
    const action = { hidden: false };
    const button = { disabled: false };
    const notice = { hidden: true };
    const review = {
        dataset: { reviewCloses: closes },
        querySelectorAll: selector => selector.endsWith('button') ? [button] : [action],
        querySelector: () => notice
    };
    vm.runInNewContext(fs.readFileSync(require.resolve('../HoleInHub.Web/wwwroot/js/staff-review.js'), 'utf8'), {
        document: {
            querySelectorAll: selector => selector === '[data-deadline]' ? [deadline] : [],
            querySelector: selector => selector === '[data-review-closes]' ? review : null
        },
        Date: class extends Date { static now() { return now; } },
        setInterval: update => { tick = update; }
    });
    assert.equal(countdown.textContent, 'Overdue');
    assert.equal(action.hidden, false);
    assert.equal(button.disabled, false);
    now = Date.parse(closes);
    tick();
    assert.equal(action.hidden, true);
    assert.equal(button.disabled, true);
    assert.equal(notice.hidden, false);
});

test('block drawer keeps values on errors, prevents duplicate saves, and restores focus', async () => {
    function node() {
        return { handlers: {}, addEventListener(type, handler) { this.handlers[type] = handler; }, focus() { this.focused = true; } };
    }
    function pair() {
        const trigger = node(), error = node(), submit = node(), close = node();
        const form = { ...node(), elements: {}, action: '/blocks/new', reset() { this.resetCount = (this.resetCount || 0) + 1; },
            querySelector(selector) { return selector === '[data-booking-error]' ? error : submit; } };
        const drawer = { ...node(), querySelector(selector) { return selector === '.staff-create-form' ? form : close; }, showModal() { this.open = true; } };
        return { trigger, drawer, form, error, submit, close };
    }
    const booking = pair(), block = pair();
    const nodes = { '[data-create-booking]': booking.drawer, '[data-new-booking]': booking.trigger,
        '[data-create-block]': block.drawer, '[data-new-block]': block.trigger };
    let release, requests = 0;
    const notices = [];
    const page = { querySelector(selector) { return nodes[selector] || null; } };
    vm.runInNewContext(fs.readFileSync(require.resolve('../HoleInHub.Web/wwwroot/js/staff-booking.js'), 'utf8'), {
        window: { HoleInHubToasts: { show: notice => notices.push(notice) } },
        document: { addEventListener() {}, querySelector() { return page; } }, FormData: class {},
        fetch() { requests++; return new Promise(resolve => { release = resolve; }); },
    });
    const click = { preventDefault() {} };
    block.trigger.handlers.click(click);
    assert.equal(block.drawer.open, true);
    assert.equal(booking.drawer.open, undefined);
    block.form.elements.Reason = { value: 'Maintenance' };
    const save = block.form.handlers.submit(click);
    await block.form.handlers.submit(click);
    assert.equal(requests, 1);
    assert.equal(block.submit.disabled, true);
    let prevented = false;
    block.drawer.handlers.cancel({ preventDefault() { prevented = true; } });
    assert.equal(prevented, true);
    release({ ok: false, json: async () => ({ error: 'Bay is no longer free' }) });
    await save;
    assert.equal(notices.at(-1).message, 'Bay is no longer free');
    assert.equal(notices.at(-1).autoDismiss, true);
    assert.equal(block.form.elements.Reason.value, 'Maintenance');
    assert.equal(block.form.resetCount, 1);
    assert.equal(block.submit.disabled, false);
    block.drawer.handlers.close();
    assert.equal(block.trigger.focused, true);
});

test('Ctrl-click and double-click select bays, and dragging prefills one shared time', () => {
    const eventNode = extra => ({ handlers: {}, addEventListener(type, handler, options) { if (!options?.capture) this.handlers[type] = handler; }, removeEventListener() {}, ...extra });
    const error = { hidden: true }, submit = {}, close = {}, trigger = eventNode({ focus() {} });
    const bays = ['bay-2', 'bay-3'].map(value => ({ value, checked: false }));
    const form = eventNode({ reset() { bays.forEach(bay => bay.checked = false); },
        querySelectorAll(selector) { return selector.endsWith(':checked') ? bays.filter(bay => bay.checked) : bays; },
        elements: { StartTime: { defaultValue: '12:00' }, DurationMinutes: {} },
        querySelector(selector) { return selector === '[data-booking-error]' ? error : submit; } });
    const drawer = eventNode({ querySelector(selector) { return selector === '.staff-create-form' ? form : close; }, showModal() { this.open = true; } });
    const track = { dataset: { createBay: 'bay-2' }, getBoundingClientRect() { return { left: 0, width: 840 }; },
        setPointerCapture() {}, hasPointerCapture() { return false; }, append() {},
        querySelectorAll(selector) { assert.equal(selector, '[data-holds-capacity="true"]'); return []; } };
    track.classList = { toggle() {}, remove() {} };
    const other = { ...track, dataset: { createBay: 'bay-3' }, closest() { return this; } };
    other.closest = selector => selector === '[data-create-bay]' ? other : null;
    let blocked = false;
    other.querySelectorAll = () => blocked ? [{ style: { getPropertyValue: key => key === '--start' ? '18%' : '5%' } }] : [];
    const completed = { clicks: 0, click() { this.clicks++; } };
    const target = { closest(selector) { return selector === '[data-create-bay]' ? track : completed; } };
    const timeline = eventNode({ classList: { add() {} }, querySelector() { return track; }, querySelectorAll() { return [track, other]; } });
    const preview = { style: {}, setAttribute() {}, classList: { toggle() {}, add() {}, remove() {} }, remove() { this.removed = true; } };
    const nodes = { '[data-create-booking]': drawer, '[data-new-booking]': trigger, '.staff-timeline': timeline,
        '[data-drag-hint]': {}, '[data-drag-error]': {} };
    const page = { addEventListener() {}, dataset: { firstHour: '10', openingHour: '10' }, querySelector(selector) { return nodes[selector] || null; } };
    vm.runInNewContext(fs.readFileSync(require.resolve('../HoleInHub.Web/wwwroot/js/staff-booking.js'), 'utf8'), {
        window: { matchMedia: () => ({ matches: true }) }, document: { querySelector() { return page; }, createElement() { return preview; }, addEventListener() {} }, setTimeout() {},
    });
    const pointer = x => ({ target, pointerType: 'mouse', button: 0, ctrlKey: true, pointerId: 1, clientX: x, preventDefault() {} });
    timeline.handlers.pointerdown(pointer(150));
    timeline.handlers.pointerup(pointer(240));
    assert.equal(drawer.open, true);
    assert.equal(bays[0].checked, true);
    assert.equal(form.elements.StartTime.value, '12:30');
    assert.equal(form.elements.DurationMinutes.value, 120);
    assert.equal(completed.clicks, 0);
    assert.match(preview.textContent, /New booking/);
    assert.notEqual(preview.removed, true);
    preview.removed = false;
    drawer.handlers.close();
    assert.equal(preview.removed, true);
    drawer.open = false;
    timeline.handlers.pointerdown({ ...pointer(150), ctrlKey: false });
    timeline.handlers.pointerup(pointer(150));
    assert.equal(completed.clicks, 0);
    assert.equal(drawer.open, false);
    const click = (target, detail) => ({ target, ctrlKey: true, detail, preventDefault() {}, stopImmediatePropagation() {} });
    timeline.handlers.click(click(target, 1));
    timeline.handlers.click(click(target, 2));
    timeline.handlers.click(click(other, 1));
    blocked = true;
    timeline.handlers.pointerdown({ ...pointer(150), ctrlKey: false, target: other });
    timeline.handlers.pointerup({ ...pointer(240), ctrlKey: false, target: other });
    assert.equal(drawer.open, false);
    blocked = false;
    timeline.handlers.pointerdown({ ...pointer(150), ctrlKey: false, target: other });
    timeline.handlers.pointerup({ ...pointer(240), ctrlKey: false, target: other });
    assert.deepEqual(bays.map(bay => bay.checked), [true, true]);
    assert.equal(form.elements.StartTime.value, '12:30');
    drawer.handlers.close();
    drawer.open = false;
    page.dataset.customerProfileId = 'cp_demo';
    form.elements.FirstName = { value: 'Demo' };
    form.elements.LastName = { value: 'Customer' };
    form.elements.Phone = { value: '09990001003' };
    form.elements.CustomerProfileId = { value: 'cp_demo' };
    timeline.handlers.pointerdown({ ...pointer(150), ctrlKey: false });
    timeline.handlers.pointerup({ ...pointer(240), ctrlKey: false });
    assert.equal(drawer.open, true);
    assert.equal(bays[0].checked, true);
    assert.equal(form.elements.CustomerProfileId.value, 'cp_demo');
    assert.equal(form.elements.Phone.value, '09990001003');
    assert.match(preview.textContent, /Demo Customer/);
});

test('overnight booking review starts at opening and carries remaining time across closing', () => {
    const calendar = require('../HoleInHub.Web/wwwroot/js/reservation.js');
    assert.equal(calendar.canStart('Regular', '2026-11-03', 600, '2026-11-03T01:00:00+08:00'), false);
    assert.equal(calendar.canStart('Regular', '2026-11-03', 630, '2026-11-03T01:00:00+08:00'), true);
    assert.equal(calendar.canStart('VIP', '2026-11-03', 630, '2026-11-03T01:00:00+08:00'), false);
    assert.equal(calendar.canStart('VIP', '2026-11-03', 660, '2026-11-03T01:00:00+08:00'), true);
    assert.equal(calendar.reviewDeadline('Regular', '2026-11-01T23:50:00+08:00'), Date.parse('2026-11-02T11:20:00+08:00'));
    assert.equal(calendar.reviewDeadline('WholeShop', '2026-11-03T01:00:00+08:00'), Date.parse('2026-11-03T02:00:00+08:00'));
});
