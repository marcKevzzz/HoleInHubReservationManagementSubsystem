const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');

test('side panel dismisses only after an outside press and respects cancellation while saving', () => {
    const handlers = {};
    const panel = {
        open: true,
        addEventListener(name, callback) { handlers[name] = callback; },
        getBoundingClientRect() { return { left: 600, right: 1000, top: 0, bottom: 800 }; },
        dispatchEvent(event) { if (this.saving) event.preventDefault(); return !event.defaultPrevented; },
        close() { this.open = false; },
        showModal() {},
    };
    const page = {
        querySelectorAll() { return [panel]; },
        querySelector(selector) { return { '#staff-date': {}, '[data-booking-details]': panel }[selector] || null; },
        addEventListener() {},
    };
    vm.runInNewContext(fs.readFileSync(require.resolve('../HoleInHub.Web/wwwroot/js/staff-schedule.js'), 'utf8'), {
        document: { addEventListener() {}, querySelector() { return page; } }, Event,
    });
    const point = x => ({ target: panel, clientX: x, clientY: 200 });
    handlers.pointerdown(point(700));
    handlers.click(point(700));
    assert.equal(panel.open, true);
    handlers.click(point(300));
    assert.equal(panel.open, true);
    panel.saving = true;
    handlers.pointerdown(point(300));
    handlers.click(point(300));
    assert.equal(panel.open, true);
    panel.saving = false;
    handlers.pointerdown(point(300));
    handlers.click(point(300));
    assert.equal(panel.open, false);
});
