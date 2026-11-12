const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const vm = require('node:vm');

function mount(popover = false) {
    const formEvents = {}, inputEvents = {}, timers = new Map(), requests = [];
    const input = { value: '', setAttribute() {}, addEventListener: (type, callback) => { inputEvents[type] = callback; } };
    const results = { querySelector: () => null, innerHTML: '', replaceChildren() { this.innerHTML = ''; }, setAttribute() {}, removeAttribute() {} };
    let open = false, shows = 0;
    if (popover) Object.assign(results, {
        style: {}, addEventListener() {}, matches() { return open; },
        showPopover() {
            shows++;
            if (shows > 4) throw new Error('Recursive popover opening');
            inputEvents.focus();
            open = true;
        },
        hidePopover() { open = false; }
    });
    const form = {
        dataset: { staffSearch: '/staff/reservations/search' }, getBoundingClientRect() { return { right: 900, bottom: 160 }; },
        elements: { reference: input, date: { value: '2026-11-08' } },
        addEventListener: (type, callback) => { formEvents[type] = callback; }
    };
    let timerId = 0;
    vm.runInNewContext(readFileSync(require.resolve('../HoleInHub.Web/wwwroot/js/staff-search.js'), 'utf8'), {
        document: { addEventListener() {}, querySelector: selector => selector === '[data-staff-search]' ? form : results },
        window: { location: { href: 'https://example.test/staff' }, innerWidth: 1200, innerHeight: 800, addEventListener() {} },
        URL, AbortController,
        setTimeout: callback => { timers.set(++timerId, callback); return timerId; },
        clearTimeout: id => timers.delete(id),
        fetch: (url, options) => new Promise(resolve => requests.push({ url, signal: options.signal, resolve }))
    });
    return {
        formEvents, requests, results, inputEvents, shows: () => shows,
        type(value) { input.value = value; inputEvents.input({}); },
        async fire() {
            const callback = timers.values().next().value;
            timers.clear();
            return callback?.();
        }
    };
}

test('debounces typing and ignores a response for a superseded query', async () => {
    const ui = mount();
    ui.type('Al');
    ui.type('Alex');
    assert.equal(ui.requests.length, 0);
    const stale = ui.fire();
    assert.equal(ui.requests.length, 1);
    assert.equal(ui.requests[0].url.searchParams.get('reference'), 'Alex');
    assert.equal(ui.requests[0].url.searchParams.get('date'), '2026-11-08');
    ui.type('Jamie');
    assert.equal(ui.requests[0].signal.aborted, true);
    const latest = ui.fire();
    ui.requests[1].resolve({ ok: true, text: async () => '<p>Jamie</p>' });
    await latest;
    ui.requests[0].resolve({ ok: true, text: async () => '<p>Alex</p>' });
    await stale;
    assert.equal(ui.results.innerHTML, '<p>Jamie</p>');
    ui.type('');
    await ui.fire();
    assert.equal(ui.results.innerHTML, '');
    assert.equal(ui.requests.length, 2);
});

test('opening the search overlay tolerates focus reentry and click reopens dismissed results', async () => {
    const ui = mount(true);
    ui.type('Alex');
    const query = ui.fire();
    ui.requests[0].resolve({ ok: true, text: async () => '<a>Preview guest</a>' });
    await query;
    assert.equal(ui.shows(), 1);
    ui.results.hidePopover();
    ui.inputEvents.click();
    assert.equal(ui.shows(), 2);
    ui.type('');
    ui.inputEvents.click();
    assert.equal(ui.shows(), 2);
});
