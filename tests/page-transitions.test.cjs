const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../HoleInHub.Web/wwwroot/js/page-transitions.js'), 'utf8');
function setup(reduced = false, storageFails = false, skipPageTransition = false, pathname = '/') {
    const events = {}, windowEvents = {}, navigations = [], stored = [];
    const cover = { setAttribute() {}, remove() { this.removed = true; } };
    const html = { dataset: { skipPageTransition: String(skipPageTransition) }, classList: { contains: () => false, remove() {} }, style: { removeProperty() {} } };
    const gsap = { fromTo(_, __, values) { values.onComplete(); return { kill() {} }; } };
    const location = { href: 'https://example.test'+pathname, origin: 'https://example.test', pathname, search: '', assign: value => navigations.push(value) };
    vm.runInNewContext(source, { window: { matchMedia: () => ({ matches: reduced, addEventListener() {} }), gsap, addEventListener: (event, callback) => windowEvents[event] = callback },
        gsap, location, URL, sessionStorage: { setItem(key, value) { if (storageFails) throw Error('Unavailable'); stored.push(JSON.parse(value)); } },
        document: { documentElement: html, addEventListener: (event, callback) => events[event] = callback, querySelector: () => null, createElement: () => cover, body: { append() {} } },
        setTimeout: () => 1, clearTimeout() {} });
    function click(href, options = {}) {
        const link = { href, target: options.target || '', hasAttribute: name => name === 'download' && options.download };
        const event = { ...options, target: { closest: () => link }, preventDefault() { this.defaultPrevented = true; } };
        events.click?.(event); return event;
    }
    return { click, navigations, stored, cover, windowEvents };
}
test('ordinary internal links navigate with an arrival marker', () => {
    for (const route of ['/account/sign-up', '/account/sign-in']) {
        const ui = setup();
        assert.equal(ui.click('https://example.test' + route).defaultPrevented, true);
        assert.equal(ui.navigations[0], 'https://example.test' + route);
        assert.equal(ui.stored[0].path, route);
    }
});
test('customer browsing and booking links transition to the destination', () => {
    for (const route of ['/', '/Home/Index', '/Home/About', '/Home/Gallery', '/Home/Services', '/Home/Contacts', '/Reservations/Create?service=VIP', '/about', '/gallery', '/services', '/contacts', '/appointment']) {
        const ui = setup(false, false, false, route === '/' ? '/Home/About' : '/');
        ui.click('https://example.test' + route);
        assert.equal(ui.stored[0].path, route);
    }
});
test('navigation within the account area and reservation filters stays native', () => {
    const ui = setup(false, false, false, '/my-profile');
    for (const route of ['/my-reservations?view=past', '/transactions', '/my-profile', '/notifications', '/Reservations/Index', '/guest/reservations?view=past']) {
        assert.equal(ui.click('https://example.test' + route).defaultPrevented, undefined);
    }
    assert.equal(setup(false, false, true).click('https://example.test/Home/About').defaultPrevented, undefined);
});
test('public-to-account and account-to-public links transition in both directions', () => {
    for (const route of ['/my-reservations','/transactions','/my-profile','/notifications']) {
        const incoming = setup(false, false, false, '/Home/Contacts');
        assert.equal(incoming.click('https://example.test'+route).defaultPrevented,true);
        assert.equal(incoming.stored[0].path,route);
        const outgoing = setup(false, false, false, route);
        assert.equal(outgoing.click('https://example.test/').defaultPrevented,true);
        assert.equal(outgoing.stored[0].path,'/');
    }
});
test('external, anchor, modified, download, and new-tab links stay native', () => {
    for (const [href, options] of [['https://elsewhere.test/', {}], ['https://example.test/#section', {}], ['https://example.test/next', { ctrlKey: true }], ['https://example.test/next', { download: true }], ['https://example.test/next', { target: '_blank' }], ['mailto:test@example.test', {}]]) {
        const ui = setup(); ui.click(href, options); assert.equal(ui.navigations.length, 0);
    }
});
test('reduced motion keeps native navigation and unavailable storage cannot block navigation', () => {
    const reduced = setup(true); reduced.click('https://example.test/next'); assert.equal(reduced.navigations.length, 0);
    const blocked = setup(false, true); blocked.click('https://example.test/next'); assert.equal(blocked.navigations.length, 1);
});
test('back-forward restoration removes the cover and permits later navigation', () => {
    const ui = setup(); ui.click('https://example.test/one'); ui.windowEvents.pageshow({ persisted: true });
    assert.equal(ui.cover.removed, true); ui.click('https://example.test/two'); assert.equal(ui.navigations.length, 2);
});

const arrivalSource = fs.readFileSync(path.join(__dirname, '../HoleInHub.Web/wwwroot/js/page-transition-start.js'), 'utf8');
for (const reduced of [false, true]) test(`sign-in arrival works without browser storage, reduced motion: ${reduced}`, () => {
    let visible = false;
    vm.runInNewContext(arrivalSource, {
        document: { documentElement: { dataset: { pageArrival: 'true' }, classList: { add() { visible = true; }, remove() {} } } },
        sessionStorage: { getItem() { throw Error('Storage unavailable'); } },
        window: { matchMedia: () => ({ matches: reduced }) }, setTimeout() {}
    });
    assert.equal(visible, !reduced);
});
