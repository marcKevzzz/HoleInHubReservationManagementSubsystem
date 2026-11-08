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

for (const [name, interval, allowed] of [
    ['empty bay', null, true],
    ['later reservation', [840, 900], true],
    ['reservation ending at the proposed start', [660, 720], true],
    ['reservation starting at the proposed end', [780, 840], true],
    ['overlapping reservation', [750, 810], false],
]) {
test(`vertical dragging into a bay with ${name} validates the actual time`, () => {
    const node = extra => ({ handlers: {}, addEventListener(type, handler) { this.handlers[type] = handler; }, removeEventListener() {}, ...extra });
    const style = { start: '14.2857%', getPropertyValue() { return this.start; }, setProperty(name, value) { this.start = value; }, removeProperty() { this.translate = ''; } };
    const bar = { style, dataset: { moveBooking: '/staff/reservations/example/move', revision: '3', startMinute: '720', duration: '60', guest: 'Alex', reference: 'HIH-TEST', location: 'Bay 1', interval: '12:00 PM–1:00 PM' },
        classList: { add() {}, toggle() {}, remove() {} }, focus() {}, setPointerCapture() {}, releasePointerCapture() {}, setAttribute() {}, title: '' };
    const track = (id, top, name) => ({ dataset: { createBay: id },
        getBoundingClientRect() { return { left: 0, right: 840, width: 840, top, bottom: top + 50 }; },
        querySelectorAll() { return id === 'bay-3' && interval ? [{ dataset: { moveBooking: '/staff/reservations/other/move' },
            style: { getPropertyValue(property) { return String((property === '--start' ? interval[0] - 600 : interval[1] - interval[0]) / 840 * 100); } } }] : []; }, querySelector() { return null; },
        closest() { return { dataset: { bayType: 'Regular' }, getAttribute() { return name; } }; } });
    const source = track('bay-2', 0, 'Bay 1'), destination = track('bay-3', 50, 'Bay 2');
    bar.closest = selector => selector === '[data-move-booking]' ? bar : source;
    const form = node({ elements: { revision: {}, commandId: {}, startTime: {}, resourceId: {}, sourceResourceId: {} }, querySelector() { return {}; } });
    const undo = node({}), error = {}, description = {}, dragError = {};
    const dialog = node({ open: false, showModal() { this.open = true; }, close() { this.open = false; this.handlers.close(); },
        querySelector(selector) { return { '[data-move-form]': form, '[data-move-undo]': undo, '[data-move-error]': error, '[data-move-description]': description }[selector]; } });
    const timeline = node({ querySelectorAll(selector) { return selector === '[data-move-booking]' ? [bar] : [source, destination]; } });
    const page = { dataset: { firstHour: '10', openingHour: '10' }, querySelector(selector) { return { '[data-move-confirmation]': dialog, '.staff-timeline': timeline, '[data-drag-error]': dragError }[selector]; } };
    vm.runInNewContext(fs.readFileSync(require.resolve('../HoleInHub.Web/wwwroot/js/staff-move.js'), 'utf8'), {
        document: { querySelector() { return page; }, addEventListener() {} }, crypto: { randomUUID() { return '12345678-1234-1234-1234-123456789abc'; } }, setTimeout() {},
    });
    const pointer = y => ({ target: bar, button: 0, pointerId: 1, clientX: 130, clientY: y, preventDefault() {} });
    timeline.handlers.pointerdown(pointer(25));
    timeline.handlers.pointermove(pointer(75));
    assert.equal(style.translate, '0 50px');
    timeline.handlers.pointerup(pointer(75));
    if (!allowed) {
        assert.equal(dialog.open, false);
        assert.equal(dragError.hidden, false);
        assert.equal(style.start, '14.2857%');
        return;
    }
    assert.equal(dialog.open, true);
    assert.equal(form.elements.sourceResourceId.value, 'bay-2');
    assert.equal(form.elements.resourceId.value, 'bay-3');
    assert.equal(form.elements.startTime.value, '12:00');
    assert.match(description.textContent, /to Bay 2/);
    undo.handlers.click();
    assert.equal(style.start, '14.2857%');
    assert.equal(style.translate, '');
    assert.equal(dialog.open, false);
});
}
