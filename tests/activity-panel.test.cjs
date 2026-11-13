const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

test('activity closes only when both press and release are outside the panel', () => {
    const handlers = {};
    const windowHandlers = {};
    let closes = 0, focused = 0;
    const panel = {
        open: true, showModal() {}, close() { closes++; this.open = false; },
        addEventListener: (name, handler) => { handlers[name] = handler; },
        getBoundingClientRect: () => ({ left: 700, right: 1100, top: 0, bottom: 800 }),
    };
    const results = { dataset: { listUrl: '/staff/activity' }, removeAttribute() {} };
    const body = { removeAttribute() {} };
    const page = {
        dataset: {},
        querySelector: selector => ({ '[data-activity-panel]': panel, '[data-activity-results]': results, '[data-activity-panel-body]': body })[selector] ?? null,
        querySelectorAll: () => [{ dataset: { eventKey: "first" }, scrollIntoView() {}, focus() { focused++; } }],
        addEventListener() {},
    };
    const location = { href: 'http://localhost/staff/activity', origin: 'http://localhost', pathname: '/staff/activity', search: '' };
    // No filter form is needed on the embedded team-member feed.
    const context = {
        document: { querySelector: () => page, documentElement: { classList: { remove() {} } } },
        window: { scrollTo() {}, addEventListener: (name, handler) => { windowHandlers[name] = handler; } },
        location, URL, scrollY: 0, sessionStorage: { getItem: () => JSON.stringify({ y: 0, key: "first" }), setItem() {} },
        history: { replaceState() {}, state: null }, matchMedia: () => ({ matches: true }),
    };
    vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../HoleInHub.Web/wwwroot/js/activity.js'), 'utf8'), context);
    assert.equal(focused, 0, "stored history must not focus an activity on initial load");
    const inside = { target: panel, clientX: 850, clientY: 100 };
    const outside = { target: panel, clientX: 300, clientY: 100 };
    handlers.pointerdown(inside);
    handlers.click(outside);
    assert.equal(closes, 0);
    handlers.pointerdown(outside);
    handlers.click(inside);
    assert.equal(closes, 0);
    handlers.pointerdown(outside);
    handlers.click(outside);
    assert.equal(closes, 1);
});
