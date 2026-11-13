const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

test('zoom feedback starts hidden, replaces the previous value and disappears after idle', () => {
    const handlers = {}, timers = new Map();
    let next = 0;
    const zoom = { hidden: true };
    const label = { offsetWidth: 50 };
    const timeline = {
        querySelector: selector => selector === '.staff-timeline-hours' ? { getBoundingClientRect: () => ({ width: 700 }) } : label,
        style: { setProperty() {} },
    };
    const hint = { querySelector: () => ({}) };
    const scroller = {
        scrollLeft: 0, querySelector: () => timeline,
        getBoundingClientRect: () => ({ left: 0 }),
        addEventListener: (name, handler) => { handlers[name] = handler; },
    };
    const page = {
        dataset: { firstHour: '10' },
        querySelector: selector => ({ '.staff-timeline-scroll': scroller, '[data-date-preview]': hint, '[data-timeline-zoom]': zoom })[selector],
        addEventListener() {},
    };
    const context = {
        document: { querySelector: () => page, addEventListener() {} },
        window: { matchMedia: () => ({ matches: true }) },
        sessionStorage: { getItem: () => null, removeItem() {} },
        setTimeout: action => { timers.set(++next, action); return next; }, clearTimeout: id => timers.delete(id),
    };
    vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../HoleInHub.Web/wwwroot/js/staff-timeline-gestures.js'), 'utf8'), context);
    assert.equal(zoom.hidden, true);
    assert.equal(zoom.value, '100%');
    const wheel = { target: { closest: () => null }, ctrlKey: true, clientX: 100, deltaY: -80, preventDefault() {} };
    handlers.wheel(wheel);
    assert.equal(zoom.hidden, false);
    assert.equal(zoom.value, '117%');
    handlers.wheel(wheel);
    assert.equal(zoom.value, '138%');
    assert.equal(timers.size, 1);
    [...timers.values()][0]();
    assert.equal(zoom.hidden, true);
});
