const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

function setup(arriving = false, reduced = false, motion = false) {
    const events = {}, windowEvents = {}, timers = new Map();
    let appended, next = 0;
    const animations = [];
    const heading = { querySelectorAll: () => [{}], querySelector: () => ({}) };
    const hub = { querySelector: () => heading, attrs: {}, append(node) { appended = node; }, setAttribute(k,v) { this.attrs[k]=v; }, removeAttribute(k) { delete this.attrs[k]; } };
    const node = () => ({ children: [], attrs: {}, append(n) { this.children.push(n); }, setAttribute(k,v) { this.attrs[k]=v; }, remove() { this.removed=true; } });
    vm.runInNewContext(fs.readFileSync(require.resolve('../HoleInHub.Web/wwwroot/js/account-loading.js'), 'utf8'), {
        document: { querySelector: () => hub, documentElement: { lang: 'en', classList: { contains: () => arriving, remove() {} } }, fonts: { load: () => Promise.resolve() }, createElement: node, addEventListener: (k,v) => events[k]=v },
        window: { gsap: motion ? { fromTo: (...args) => animations.push(args) } : null, matchMedia: () => ({ matches: reduced }), addEventListener: (k,v) => windowEvents[k]=v }, URL, location: { pathname: '/my-profile', href: 'https://example.test/my-profile', origin: 'https://example.test' },
        setTimeout: fn => { const id=++next;timers.set(id,fn);return id; }, clearTimeout: id => timers.delete(id),
    });
    const click = (href, options={}) => events.click({ target: { closest: () => ({ href, hasAttribute: () => false }) }, ...options });
    return { hub, events, windowEvents, timers, click, animations, skeleton: () => appended };
}
test('account heading and description animate after loading unless reduced motion is enabled', async () => {
    for (const arriving of [false, true]) for (const reduced of [false, true]) {
        const ui = setup(arriving, reduced, true);
        await new Promise(resolve => setImmediate(resolve));
        assert.equal(ui.animations.length, reduced ? 0 : 2);
    }
});

test('tab navigation shows an accessible skeleton without intercepting navigation or delaying content', () => {
    const ui=setup();ui.click('https://example.test/transactions');
    assert.equal(ui.hub.attrs['aria-busy'],'true');
    assert.equal(ui.skeleton().attrs.role,'status');
    assert.equal(ui.skeleton().children.length,6);
    ui.windowEvents.pageshow();
    assert(ui.skeleton().removed);assert.equal(ui.hub.attrs['aria-busy'],undefined);
});
test('incoming account pages keep the skeleton until their fonts are ready', async () => {
    for (const reduced of [false, true]) {
        const ui = setup(true, reduced);
        assert.equal(ui.hub.attrs['aria-busy'], 'true');
        await new Promise(resolve => setImmediate(resolve));
        assert.equal(ui.hub.attrs['aria-busy'], undefined);
        assert(ui.skeleton().removed);
    }
});
test('same-page, modified and cancelled navigation do not replace content', () => {
    for(const [url,opts] of [['https://example.test/my-profile',{}],['https://example.test/transactions',{ctrlKey:true}],['https://example.test/transactions',{defaultPrevented:true}],['https://other.test/transactions',{}]]){
        const ui=setup();ui.click(url,opts);assert.equal(ui.skeleton(),undefined);
    }
});
test('skeletons match the destination page layout', () => {
    for (const [route, shape] of [['my-profile','profile'],['transactions','empty-title'],['notifications','notification'],['my-reservations','reservation']]) {
        const ui=setup();
        ui.click('https://example.test/'+route+(route==='my-profile'?'?edit=1':''));
        assert(ui.skeleton().children.some(node => node.className?.includes('account-skeleton-'+shape)));
    }
});
test('a stalled navigation restores usable content and invalid forms keep their errors visible', () => {
    const ui=setup();ui.events.submit({defaultPrevented:true,target:{matches:()=>true}});assert.equal(ui.skeleton(),undefined);
    ui.click('https://example.test/notifications');[...ui.timers.values()][0]();assert(ui.skeleton().removed);
    assert.equal(ui.hub.attrs['data-loading'],undefined);
});
