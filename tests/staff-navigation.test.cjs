const { test } = require('node:test');
const assert = require('node:assert/strict');
const cache = require('../HoleInHub.Web/wwwroot/js/staff-navigation.js');
test('adjacent-day reads share in-flight work, expire, and retry failures', async () => {
    let now = 0, calls = 0, fail = false;
    const load = cache(async date => { calls++; if (fail) throw Error('offline'); return date; }, () => now);
    const first = load('2026-11-12');
    assert.equal(first, load('2026-11-12'));
    assert.equal(await first, '2026-11-12');
    await load('2026-11-12'); assert.equal(calls, 1);
    now = 15000; await load('2026-11-12'); assert.equal(calls, 2);
    fail = true; await assert.rejects(load('2026-11-13'));
    fail = false; assert.equal(await load('2026-11-13'), '2026-11-13'); assert.equal(calls, 4);
});
test('the schedule cache retains at most five days', async () => {
    let calls = 0;
    const load = cache(async date => { calls++; return date; });
    for (let day = 1; day <= 6; day++) await load(String(day));
    await load('6'); assert.equal(calls, 6);
    await load('1'); assert.equal(calls, 7);
});

test('date-link intent prefetches one day and preserves customer context', async () => {
    const vm = require('node:vm'), fs = require('node:fs');
    const fetched = [], events = {}, documentEvents = {};
    let timer;
    let reloads = 0;
    const page = { isConnected: true, dataset: { date: '2026-11-18', customerProfileId: 'cp_demo' }, querySelector() { return null; } };
    const location = { pathname: '/staff', origin: 'http://localhost', href: 'http://localhost/staff?date=2026-11-18&customerProfileId=cp_demo', reload() { reloads++; } };
    vm.runInNewContext(fs.readFileSync(require.resolve('../HoleInHub.Web/wwwroot/js/staff-navigation.js'), 'utf8'), {
        document: { querySelector() { return page; }, addEventListener(k, fn) { documentEvents[k] = fn; } }, URL,
        setTimeout(fn) { timer = fn; return 1; }, clearTimeout() { timer = undefined; },
        window: { location, addEventListener(k, fn) { events[k] = fn; } },
        fetch: async url => { fetched.push(new URL(url)); return { ok: true, text: async () => url.searchParams.get('date') }; },
        DOMParser: class { parseFromString(date) { return { querySelector: () => ({ dataset: { date }, querySelector: () => ({}) }) }; } },
    });
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(fetched.length, 0, 'opening the schedule does not request either neighbor');
    const link = { href: 'http://localhost/staff?date=2026-11-19', contains: () => false };
    const intent = { target: { closest: () => link }, pointerType: 'mouse' };
    documentEvents.pointerover(intent);
    documentEvents.pointerout(intent);
    assert.equal(timer, undefined, 'leaving the link cancels the planned read');
    documentEvents.focusin(intent);
    timer();
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(fetched.length, 1);
    assert.ok(fetched.every(url => url.searchParams.get('customerProfileId') === 'cp_demo'));
    location.href = 'http://localhost/staff?date=2026-11-18';
    events.popstate();
    assert.equal(reloads, 1);
});
