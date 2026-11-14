const { test } = require('node:test');
const assert = require('node:assert/strict');
const range = require('../HoleInHub.Web/wwwroot/js/staff-move.js');
const vm = require('node:vm');
const fs = require('node:fs');

test('moving preserves duration, snaps to half hours and respects adjacent bookings and midnight', () => {
    const moved = range(600, 600, 90, [[50, 60]], 901);
    assert.equal(moved.start, 900);
    assert.equal(moved.end, 990);
    assert.equal(moved.valid, true);
    assert.equal(range(600, 600, 90, [[50, 60]], 990).valid, false);
    assert.equal(range(600, 600, 90, [], 1380).valid, false);
    assert.equal(range(600, 600, 90, [], 1350).valid, true);
    assert.equal(range(600, 600, 90, [], 570).valid, false);
});

for (const [name, interval, allowed, sourceType = "Regular", targetType = "Regular", bayCount = 1, reduced = false, retainedConflict = false] of [
    ['empty bay', null, true],
    ['later reservation', [840, 900], true],
    ['reservation ending at the proposed start', [660, 720], true],
    ['reservation starting at the proposed end', [780, 840], true],
    ['overlapping reservation', [750, 810], false],
    ['VIP upgrade', null, true, 'Regular', 'VIP'],
    ['VIP upgrade occupied', [750, 810], false, 'Regular', 'VIP'],
    ['multi-bay replacement with retained bay open', null, true, 'Regular', 'Regular', 2],
    ['multi-bay replacement with retained bay occupied', null, false, 'Regular', 'Regular', 2, false, true],
    ['multi-bay upgrade', null, false, 'Regular', 'VIP', 2],
    ['VIP downgrade', null, false, 'VIP', 'Regular'],
    ['reduced-motion rejection', [750, 810], false, 'Regular', 'Regular', 1, true],
]) {
test(`vertical dragging into a bay with ${name} validates the actual time`, async () => {
    const node = extra => ({ handlers: {}, addEventListener(type, handler) { this.handlers[type] = handler; }, removeEventListener() {}, ...extra });
    const style = { start: '14.2857%', getPropertyValue() { return this.start; }, setProperty(name, value) { this.start = value; }, removeProperty() { this.translate = ''; } };
    let shakes = 0, focused = 0;
    const bar = { animate() { shakes++; return { cancel() {} }; }, blur() {}, style, dataset: { bookingId: 'example', moveBooking: '/staff/reservations/example/move', revision: '3', bayCount: String(bayCount), startMinute: '720', duration: '60', guest: 'Alex', reference: 'HIH-TEST', location: 'Bay 1', interval: '12:00 PM–1:00 PM' },
        classList: { add() {}, toggle() {}, remove() {} }, focus() { focused++; }, setPointerCapture() {}, releasePointerCapture() {}, setAttribute() {}, title: '' };
    const track = (id, top, name) => ({ dataset: { createBay: id },
        getBoundingClientRect() { return { left: 0, right: 840, width: 840, top, bottom: top + 50 }; },
        querySelectorAll() { return (id === 'bay-3' && interval) || (id === 'bay-4' && retainedConflict) ? [{ dataset: { moveBooking: '/staff/reservations/other/move' },
            style: { getPropertyValue(property) { return String((property === '--start' ? (interval || [720, 780])[0] - 600 : (interval || [720, 780])[1] - (interval || [720, 780])[0]) / 840 * 100); } } }] : []; }, querySelector() { return null; },
        closest() { return { dataset: { bayType: id === 'bay-2' ? sourceType : targetType }, getAttribute() { return name; } }; } });
    const source = track('bay-2', 0, 'Bay 1'), destination = track('bay-3', 50, 'Bay 2');
    const retained = track('bay-4', 100, 'Bay 3');
    const peer = { dataset: { bookingId: 'example' }, style: { removeProperty() {} }, closest: () => retained };
    bar.closest = selector => selector === '[data-move-booking]' ? bar : source;
    const label = {}, heading = {}, consent = {};
    const checkbox = node({ checked: false });
    const submit = { querySelector: () => label };
    const notices = [];
    let requests = 0;
    const form = node({ elements: { confirmVipUpgrade: checkbox, revision: {}, commandId: {}, startTime: {}, resourceId: {}, sourceResourceId: {} }, querySelector() { return submit; } });
    const undo = node({}), error = {}, description = {}, dragError = {};
    const dialog = node({ open: false, showModal() { this.open = true; }, close() { this.open = false; this.handlers.close(); },
        querySelector(selector) { return { 'h2': heading, '[data-upgrade-consent]': consent, '[data-move-form]': form, '[data-move-undo]': undo, '[data-move-error]': error, '[data-move-description]': description }[selector]; } });
    const timeline = node({ style: { removeProperty() {} }, querySelectorAll(selector) { if (selector.startsWith('.staff-timeline-bar')) return bayCount > 1 ? [bar, peer] : [bar];
        return selector === '[data-move-booking]' ? [bar] : [source, destination, retained]; } });
    const page = { addEventListener() {}, dataset: { firstHour: '10', openingHour: '10' }, querySelector(selector) { return { '[data-move-confirmation]': dialog, '.staff-timeline': timeline, '[data-drag-error]': dragError }[selector]; } };
    vm.runInNewContext(fs.readFileSync(require.resolve('../HoleInHub.Web/wwwroot/js/staff-move.js'), 'utf8'), {
        window: { matchMedia: () => ({ matches: reduced }), HoleInHubToasts: { show: notice => notices.push(notice) } },
        FormData: class {}, fetch: async () => { requests++; return { ok: false, json: async () => ({ error: 'Destination changed' }) }; },
        document: { querySelector() { return page; }, addEventListener() {} }, crypto: { randomUUID() { return '12345678-1234-1234-1234-123456789abc'; } }, setTimeout() {}, clearTimeout() {},
    });
    const pointer = y => ({ target: bar, button: 0, pointerId: 1, clientX: 130, clientY: y, preventDefault() {} });
    timeline.handlers.pointerdown(pointer(25));
    timeline.handlers.pointermove(pointer(75));
    assert.equal(style.translate, '0 50px');
    timeline.handlers.pointerup(pointer(75));
    if (!allowed) {
        assert.equal(dialog.open, false);
        assert.equal(notices.length, 1);
        assert.equal(notices.at(-1).autoDismiss, true);
        assert.equal(shakes, reduced ? 0 : 1);
        assert.equal(focused, 0, "pointer rejection must not leave keyboard focus on the block");
        assert.equal(style.start, '14.2857%');
        return;
    }
    assert.equal(dialog.open, true);
    assert.equal(form.elements.sourceResourceId.value, 'bay-2');
    assert.equal(form.elements.resourceId.value, 'bay-3');
    assert.equal(form.elements.startTime.value, '12:00');
    assert.match(description.textContent, /to Bay 2/);
    const upgrade = sourceType !== targetType;
    assert.equal(consent.hidden, !upgrade);
    if (upgrade) {
        assert.match(heading.textContent, /Upgrade to VIP/);
        assert.equal(submit.disabled, true);
        await form.handlers.submit({ preventDefault() {} });
        assert.equal(requests, 0);
        checkbox.checked = true;
        checkbox.handlers.change();
        assert.equal(submit.disabled, false);
        await form.handlers.submit({ preventDefault() {} });
        assert.equal(requests, 1);
        assert.equal(notices.at(-1).message, 'Destination changed');
        assert.equal(dialog.open, false);
        assert.equal(shakes, 1);
    }
    undo.handlers.click();
    assert.equal(style.start, '14.2857%');
    assert.equal(style.translate, '');
    assert.equal(dialog.open, false);
    bar.dataset.moveRestriction = 'This reservation has already started and cannot be moved.';
    bar.closest = selector => selector === '[data-move-restriction]' ? bar : source;
    const noticeCount = notices.length;
    timeline.handlers.pointerdown(pointer(25));
    assert.equal(notices.length, noticeCount, 'ordinary clicks still open booking details');
    timeline.handlers.pointermove(pointer(75));
    timeline.handlers.pointermove(pointer(80));
    assert.equal(notices.length, noticeCount + 1);
    assert.equal(notices.at(-1).message, bar.dataset.moveRestriction);
    timeline.handlers.pointerup(pointer(80));
    assert.equal(dialog.open, false);
});
}

test('batch selection counts reservations by ID and retries the same shared offset after an uncertain response', async () => {
    const node = extra => ({
        dataset: {}, handlers: {}, attributes: {}, children: [],
        addEventListener(type, fn, capture) { this.handlers[capture ? type + "Capture" : type] = fn; },
        setAttribute(key, value) { this.attributes[key] = value; },
        getAttribute(key) { return this.attributes[key]; },
        append(...children) { this.children.push(...children); },
        replaceChildren(...children) { this.children = children; },
        focus() {}, blur() {}, remove() {}, removeAttribute() {},
        ...extra,
    });
    const style = () => ({ getPropertyValue() { return '10%'; }, setProperty() {}, removeProperty() {} });
    const classes = () => ({ add() {}, remove() {}, toggle(key, value) { this[key] = value; } });
    const bounds = top => ({ left: 0, right: 840, width: 840, height: 50, top, bottom: top + 50 });
    function bar(id, bay, minute) {
        const track = node({
            dataset: { createBay: bay },
            style: style(),
            getBoundingClientRect: () => bounds(0),
            closest: () => ({ getAttribute: () => bay }),
            querySelectorAll: () => [],
        });
        const bar = node({
            dataset: { bookingId: id, moveBooking: '/single', startMinute: String(minute), duration: '60', revision: '2',
                guest: 'Alex', reference: id, location: bay, interval: 'Original interval' },
            title: '', style: style(), classList: classes(),
            closest: selector => selector === '.staff-timeline-track' ? track : bar,
            cloneNode: () => node({ style: style(), classList: classes() }),
        });
        return bar;
    }
    const first = bar('first', 'Bay 1', 720);
    const peer = bar('first', 'Bay 2', 720);
    const second = bar('second', 'Bay 3', 780);
    const bars = [first, peer, second];
    const fields = { confirmVipUpgrade: node() };
    const submit = node({ querySelector: () => node() });
    const form = node({ elements: fields, dataset: { batchUrl: '/batch' }, querySelector: () => submit });
    const toggle = node(), count = node(), clear = node(), help = node();
    const toolbar = node({ querySelector: key => ({
        '[data-batch-toggle]': toggle, '[data-batch-count]': count,
        '[data-batch-clear]': clear, '[data-batch-help]': help,
    })[key] });
    const summary = node(), undo = node(), heading = node(), consent = node(), description = node();
    const dialog = node({ open: false, showModal() { this.open = true; }, close() { this.open = false; this.handlers.close(); },
        querySelector: key => ({ '[data-move-form]': form, '[data-move-undo]': undo, 'h2': heading,
            '[data-upgrade-consent]': consent, '[data-move-description]': description, '[data-batch-summary]': summary })[key] });
    const timeline = node({ querySelectorAll: () => bars, querySelector: () => ({ offsetWidth: 60 }), style: style() });
    const scroller = node({ getBoundingClientRect: () => bounds(0), scrollLeft: 0 });
    const page = node({ dataset: { firstHour: '10', openingHour: '10' }, querySelector: key => ({
        '.staff-timeline': timeline, '.staff-timeline-scroll': scroller, '[data-move-confirmation]': dialog, '[data-batch-toolbar]': toolbar,
    })[key] });
    const requests = [], notices = [];
    const window = { matchMedia: () => ({ matches: true }), HoleInHubToasts: { show: item => notices.push(item) },
        location: { assign: value => requests.push(value) } };
    vm.runInNewContext(fs.readFileSync(require.resolve('../HoleInHub.Web/wwwroot/js/staff-move.js'), 'utf8'), {
        window, document: { body: node(), createElement: () => node(), querySelector: () => page, addEventListener() {} },
        crypto: { randomUUID: () => '12345678-1234-1234-1234-123456789abc' },
        FormData: class extends Map { constructor() { super(); } }, setTimeout() {}, clearTimeout() {},
        async fetch(url, { body }) {
            requests.push([url, Object.fromEntries(body)]);
            if (requests.length === 1) throw new Error('Connection lost');
            return { ok: true, json: async () => ({ redirect: '/staff' }) };
        },
    });
    const activate = (bar, extra) => ({ target: bar, button: 0, preventDefault() {}, stopPropagation() {}, ...extra });
    page.handlers.clickCapture(activate(peer, { shiftKey: true }));
    assert.equal(notices.at(-1).title, '1 selected');
    assert.equal(first.classList['is-batch-selected'], true);
    assert.equal(peer.classList['is-batch-selected'], true);
    page.handlers.keydownCapture(activate(first, { key: ' ', shiftKey: true }));
    assert.equal(notices.at(-1).title, 'Booking selection cleared');
    page.handlers.clickCapture(activate(peer, { shiftKey: true }));
    page.handlers.clickCapture(activate(second, { shiftKey: true }));
    assert.equal(notices.at(-1).title, '2 selected');
    timeline.handlers.keydown(activate(peer, { altKey: true, key: 'ArrowRight' }));
    assert.equal(dialog.open, true);
    assert.equal(summary.children.length, 2);
    assert.match(summary.children[0].children[1].textContent, /Bay 1, Bay 2/);
    await form.handlers.submit(activate(null, {}));
    assert.equal(dialog.open, true);
    assert.match(notices.at(-1).message, /Retry/);
    await form.handlers.submit(activate(null, {}));
    assert.deepEqual(requests[0], requests[1]);
    assert.equal(requests[0][0], '/batch');
    assert.equal(requests[0][1].offsetMinutes, 30);
    assert.equal(requests[0][1]['bookings[1].Id'], 'second');
    assert.equal(requests[2], '/staff');
});
