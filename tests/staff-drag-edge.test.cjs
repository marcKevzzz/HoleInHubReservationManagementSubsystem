const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');

test('edge drag clips the floating booking and updates the preview date before confirmation', async () => {
    const classes = { add() {}, remove() {}, toggle() {} };
    const node = extra => ({ handlers: {}, classList: classes, addEventListener(k, fn) { this.handlers[k] = fn; }, removeEventListener() {}, ...extra });
    const style = () => ({ values: {}, setProperty(k, v) { this.values[k] = v; }, getPropertyValue(k) { return this.values[k] || ''; }, removeProperty(k) { delete this.values[k]; } });
    const timers = new Map(); let timerId = 0, displayedDate, transition;
    const bounds = { left: 72, right: 1000, top: 100, bottom: 180, width: 928, height: 80 };
    const track = id => ({ dataset: { createBay: id }, style: style(), getBoundingClientRect: () => bounds,
        querySelectorAll: () => [], querySelector: () => null,
        closest: () => ({ dataset: { bayType: 'Regular' }, getAttribute: () => 'Bay 1' }) });
    const source = track('bay-1'), destination = track('bay-1');
    const bar = { style: style(), dataset: { moveBooking: '/move', startMinute: '720', duration: '90', revision: '1' }, classList: classes,
        closest: selector => selector === '[data-move-booking]' ? bar : source, setAttribute() {}, title: '', focus() {},
        cloneNode() { return { style: style(), classList: classes, removeAttribute() {}, setAttribute() {}, remove() {} }; } };
    const submit = { querySelector: () => ({}) };
    const form = node({ elements: Object.fromEntries(['revision','commandId','date','startTime','resourceId','sourceResourceId','confirmVipUpgrade'].map(k => [k, node({})])), querySelector: () => submit });
    const undo = node({}), description = {}, error = {}, dragError = {};
    const dialog = node({ open: false, showModal() { this.open = true; }, querySelector: s => ({ 'h2': {}, '[data-upgrade-consent]': {}, '[data-move-form]': form, '[data-move-undo]': undo, '[data-move-description]': description, '[data-move-error]': error }[s]) });
    let scroll = 0;
    const scroller = { get scrollLeft() { return scroll; }, set scrollLeft(_) { scroll = 0; }, getBoundingClientRect: () => ({ left: 0, right: 900, top: 50, bottom: 600 }) };
    let active = source;
    const timeline = node({ style: style(), childNodes: [source], querySelector: () => ({ offsetWidth: 72 }),
        querySelectorAll: s => s === '[data-move-booking]' ? [bar] : [active],
        replaceChildren(...nodes) { active = nodes[0]; }, setPointerCapture() {}, releasePointerCapture() {} });
    const gesture = { adjacentDay: () => '2099-11-13', dayLabel: d => d, preview() {}, hide() {}, setDate: d => { displayedDate = d; }, transition: d => { transition = d; },
        load: async () => ({ dataset: { firstHour: '10', openingHour: '10' }, querySelector: () => ({ childNodes: [destination] }) }) };
    let ghost;
    const page = { addEventListener() {}, dataset: { date: '2099-11-12', today: '2099-11-11', firstHour: '10', openingHour: '10' }, timelineGesture: gesture,
        querySelector: s => ({ '[data-move-confirmation]': dialog, '.staff-timeline': timeline, '.staff-timeline-scroll': scroller, '[data-drag-error]': dragError }[s]) };
    vm.runInNewContext(fs.readFileSync(require.resolve('../HoleInHub.Web/wwwroot/js/staff-move.js'), 'utf8'), {
        document: { querySelector: () => page, addEventListener() {}, body: { append: n => { ghost = n; } } },
        window: { matchMedia: () => ({ matches: false }) }, AbortController, crypto: { randomUUID: () => 'test' },
        setTimeout(fn) { timers.set(++timerId, fn); return timerId; }, clearTimeout(id) { timers.delete(id); },
    });
    const pointer = x => ({ target: bar, button: 0, pointerId: 1, clientX: x, clientY: 130, preventDefault() {} });
    timeline.handlers.pointerdown(pointer(220));
    timeline.handlers.pointermove(pointer(895));
    assert.match(ghost.style.clipPath, /^inset\(0px [1-9][\d.]*px /, 'right edge must be clipped');
    const tick = async () => { const [id, fn] = timers.entries().next().value; timers.delete(id); await fn(); await new Promise(resolve => setImmediate(resolve)); };
    await tick(); await tick();
    assert.equal(displayedDate, '2099-11-13');
    assert.equal(page.dataset.previewDate, '2099-11-13');
    assert.equal(transition, 1);
    timeline.handlers.pointerup(pointer(700));
    assert.equal(form.elements.date.value, '2099-11-13');
    assert.equal(dialog.open, true);
    dialog.handlers.close();
    assert.equal(displayedDate, '2099-11-12');
    assert.equal(page.dataset.previewDate, undefined);
});
