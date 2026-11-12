const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');

test('linked bay highlighting follows reservation identity across hover and keyboard focus', () => {
    const bar = (id, multi = true) => ({ dataset: { bookingId: id, multiBay: String(multi) },
        classList: { toggle(name, value) { this[name] = value; } },
        closest() { return this; } });
    const first = bar('shared'), second = bar('shared'), sameCustomerOtherBooking = bar('different'), single = bar('single', false);
    const bars = [first, second, sameCustomerOtherBooking, single];
    const handlers = {};
    const page = { querySelectorAll: selector => selector.startsWith('.staff-timeline-bar') ? bars : [],
        querySelector: () => null, addEventListener: (type, callback) => { handlers[type] = callback; } };
    vm.runInNewContext(fs.readFileSync(require.resolve('../HoleInHub.Web/wwwroot/js/staff-schedule.js'), 'utf8'), {
        document: { querySelector: () => page, addEventListener() {} },
    });
    const active = () => bars.filter(bar => bar.classList['is-reservation-group']);
    handlers.pointerover({ target: first, pointerType: 'mouse' });
    assert.deepEqual(active(), [first, second]);
    handlers.focusin({ target: sameCustomerOtherBooking });
    assert.deepEqual(active(), [sameCustomerOtherBooking]);
    handlers.focusout({ relatedTarget: second });
    assert.deepEqual(active(), [first, second]);
    handlers.pointerleave();
    assert.deepEqual(active(), []);
    handlers.focusin({ target: single });
    assert.deepEqual(active(), []);
});

test('search loads the booking day, reveals its timeline, and highlights only matching bays', async () => {
    const handlers = {};
    const calls = [];
    const booking = { dataset: { searchDay: '2026-11-13', bookingId: 'match' }, closest() { return this; } };
    const bar = id => ({
        dataset: { bookingId: id },
        classList: new Set(),
        focus(options) { calls.push(['focus', options.preventScroll]); },
        getBoundingClientRect() { return { left: 280, top: 200 }; },
        addEventListener() {},
    });
    const primary = bar('match'), peer = bar('match'), unrelated = bar('other');
    for (const entry of [primary, peer, unrelated]) entry.classList.remove = entry.classList.delete;
    const scroller = { scrollLeft: 20, scrollTop: 40, getBoundingClientRect() { return { left: 100, top: 100 }; },
        scrollTo({ left, top, behavior }) { calls.push(['scroll', left, top, behavior]); } };
    const destination = {
        isConnected: true, dataset: {},
        dispatchEvent(event) { calls.push([event.type, event.detail]); },
        querySelectorAll: () => [primary, peer, unrelated],
        querySelector: selector => ({
            '[data-schedule-view="timeline"]': { click() { calls.push(['timeline']); } },
            '.staff-timeline-scroll': scroller,
            '.staff-timeline-label': { offsetWidth: 60 },
            '.staff-timeline-axis': { offsetHeight: 30 },
        })[selector],
    };
    const page = {
        dataset: { date: '2026-11-12' },
        querySelectorAll: () => [],
        querySelector: selector => selector === '[data-booking-details]' ? { showModal() {}, addEventListener() {} } : null,
        addEventListener(type, handler) { handlers[type] = handler; },
    };
    vm.runInNewContext(fs.readFileSync(require.resolve('../HoleInHub.Web/wwwroot/js/staff-schedule.js'), 'utf8'), {
        document: { querySelector: () => page, addEventListener() {} },
        CustomEvent: class { constructor(type, { detail }) { this.type = type; this.detail = detail; } },
        window: {
            matchMedia: () => ({ matches: true }),
            HoleInHubSchedule: { async go(date) { calls.push(['date', date]); return destination; } },
        },
    });
    await handlers.click({ target: booking, button: 0, preventDefault() {} });
    assert.deepEqual(calls, [['date', '2026-11-13'], ['timeline'], ['focus', true], ['scroll', 124, 98, 'instant'], ['staff:open-booking', primary]]);
    assert.equal(primary.classList.has('is-search-target'), true);
    assert.equal(peer.classList.has('is-search-target'), true);
    assert.equal(unrelated.classList.has('is-search-target'), false);
});
