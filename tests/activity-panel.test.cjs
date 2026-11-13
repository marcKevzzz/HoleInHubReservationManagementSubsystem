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

test('dashboard deep link loads the Activity list and opens its side panel with reversible history', async () => {
    const handlers = {}, windowHandlers = {}, requests = [], pushed = [];
    let backs = 0;
    const panel = {
        open: false, showModal() { this.open = true; }, close() { this.open = false; },
        addEventListener: (name, handler) => handlers[name] = handler,
    };
    const results = { dataset: { listUrl: '/staff/activity?range=week' }, removeAttribute() {} };
    const body = { setAttribute() {}, removeAttribute() {}, replaceChildren() {} };
    const status = {};
    const page = {
        dataset: {}, querySelector: selector => ({ '[data-activity-panel]': panel, '[data-activity-results]': results, '[data-activity-panel-body]': body, '[data-activity-status]': status })[selector] ?? null,
        querySelectorAll: () => [], addEventListener() {},
    };
    const location = { href: 'http://localhost/staff/activity?open=event-123', origin: 'http://localhost', pathname: '/staff/activity', search: '?open=event-123' };
    const history = { state: null, replaceState(state) { this.state = state; }, pushState(state, _title, url) { this.state = state; pushed.push(url); }, back() { backs++; } };
    const context = {
        document: { querySelector: () => page, documentElement: { classList: { add() {}, remove() {} } } },
        window: { HoleInHubPanels: { loading: () => ({}) }, addEventListener: (name, handler) => windowHandlers[name] = handler, scrollTo() {} },
        location, history, URL, AbortController, scrollY: 0, sessionStorage: { getItem: () => null, setItem() {} },
        matchMedia: () => ({ matches: true }), setTimeout: () => 1, clearTimeout() {},
        fetch: async (url, options) => { requests.push({ url, options }); return { ok: true, text: async () => 'event fragment' }; },
        DOMParser: class { parseFromString() { return { querySelector: () => ({ querySelector: () => null }) }; } },
    };
    vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../HoleInHub.Web/wwwroot/js/activity.js'), 'utf8'), context);
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(panel.open, true);
    assert.equal(requests[0].url, '/staff/activity/event-123?range=week');
    assert.equal(requests[0].options.headers['X-Activity'], 'panel');
    assert.equal(pushed[0], '/staff/activity?range=week&open=event-123');
    handlers.cancel({ preventDefault() {} });
    assert.equal(backs, 1);
    location.href = 'http://localhost/staff/activity?range=week'; location.search = '?range=week';
    windowHandlers.popstate();
    assert.equal(panel.open, false);
});
