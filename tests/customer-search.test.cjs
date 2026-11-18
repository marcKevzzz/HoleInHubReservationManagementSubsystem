const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
test('one character is a neutral search hint and clearing restores recent contacts', async () => {
    const handlers = {}, input = { value: 'm', addEventListener(k, fn) { handlers[k] = fn; } };
    const formHandlers = {}, form = { action: 'http://localhost/staff/customers', querySelector: () => input, addEventListener(k, fn) { formHandlers[k] = fn; } };
    const results = { hidden: false, setAttribute() {}, removeAttribute() {}, replaceChildren(...children) { this.children = children; } };
    const count = { hidden: false }, status = { textContent: '', classList: { add() {}, remove() {} } };
    let requests = 0;
    const replacement = { childNodes: ['recent'] };
    vm.runInNewContext(fs.readFileSync(require.resolve('../HoleInHub.Web/wwwroot/js/customer-search.js'), 'utf8'), {
        document: { querySelector: s => ({ '.people-search': form, '[data-customer-results]': results, '[data-customer-count]': count, '[data-customer-search-status]': status }[s]), dispatchEvent() {} },
        AbortController, URL, Event, setTimeout() {}, clearTimeout() {}, history: { replaceState() {} },
        DOMParser: class { parseFromString() { return { querySelector: s => s === '[data-customer-results]' ? replacement : { textContent: '1 shown' } }; } },
        fetch: async () => { requests++; return { ok: true, text: async () => '' }; },
    });
    handlers.input();
    assert.equal(requests, 0);
    assert.equal(results.hidden, true);
    assert.equal(count.hidden, true);
    assert.equal(status.textContent, 'Type one more character to search.');
    input.value = '';
    formHandlers.submit({ preventDefault() {} });
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(requests, 1);
    assert.equal(results.hidden, false);
    assert.equal(count.hidden, false);
    assert.equal(results.children[0], 'recent');
    assert.equal(status.textContent, '');
});

test('duplicate lookup waits for typing to settle and keeps its merge route', async () => {
    const handlers = {}, formHandlers = {}, timers = new Map();
    let sequence = 0;
    const input = { value: '', addEventListener(k, fn) { handlers[k] = fn; } };
    const form = { action: 'http://localhost/staff/customers/cp_main/merge', querySelector: () => input, addEventListener(k, fn) { formHandlers[k] = fn; } };
    const results = { setAttribute() {}, removeAttribute() {}, replaceChildren() {} };
    const count = {}, status = { classList: { add() {}, remove() {} } };
    const requests = [];
    vm.runInNewContext(fs.readFileSync(require.resolve('../HoleInHub.Web/wwwroot/js/customer-search.js'), 'utf8'), {
        document: { querySelector: s => ({ '.people-search': form, '[data-customer-results]': results, '[data-customer-count]': count, '[data-customer-search-status]': status }[s]), dispatchEvent() {} },
        AbortController, URL, Event, history: { replaceState() {} },
        setTimeout(fn, ms) { timers.set(++sequence, { fn, ms }); return sequence; }, clearTimeout(id) { timers.delete(id); },
        DOMParser: class { parseFromString() { return { querySelector: s => s === '[data-customer-results]' ? { childNodes: [] } : { textContent: '1 shown' } }; } },
        fetch: async url => { requests.push(new URL(url)); return { ok: true, text: async () => '' }; },
    });
    input.value = 'ma'; handlers.input();
    input.value = 'marc'; handlers.input();
    assert.equal(requests.length, 0);
    assert.equal(timers.size, 1);
    const pending = [...timers.values()][0];
    assert.equal(pending.ms, 350);
    await pending.fn();
    assert.equal(requests.length, 1);
    assert.equal(requests[0].pathname, '/staff/customers/cp_main/merge');
    assert.equal(requests[0].searchParams.get('search'), 'marc');
});
