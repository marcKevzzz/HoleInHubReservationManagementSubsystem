const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync(require('node:path').join(__dirname, '../HoleInHub.Web/wwwroot/js/ui-motion.js'), 'utf8');

test('shared smooth scrolling respects the desktop motion query and starts paused behind a modal', () => {
    const content = {};
    const wrapper = { querySelector: () => content };
    let mount, options, pauseArgs, killed;
    const events = {};
    const gsap = {
        registerPlugin() {},
        matchMedia: () => ({
            add(query, callback) {
                assert.equal(query, '(min-width: 701px) and (prefers-reduced-motion: no-preference)');
                mount = callback;
            },
            revert() { killed = 'reverted'; },
        }),
    };
    const ScrollTrigger = {};
    const ScrollSmoother = {
        create(config) {
            options = config;
            return { paused: (...args) => { pauseArgs = args; }, kill() { killed = true; } };
        },
    };
    const scrollSource = fs.readFileSync(require('node:path').join(__dirname, '../HoleInHub.Web/wwwroot/js/scroll-motion.js'), 'utf8');
    vm.runInNewContext(scrollSource, {
        document: { querySelector: selector => selector === '.smooth-wrapper' ? wrapper : {} },
        window: { gsap, ScrollTrigger, ScrollSmoother, addEventListener: (name, handler) => { events[name] = handler; } },
        gsap, ScrollTrigger, ScrollSmoother,
    });
    assert.equal(options, undefined);
    const cleanup = mount();
    assert.equal(options.content, content);
    assert.equal(options.smooth, .9);
    assert.deepEqual(pauseArgs, [true, true]);
    events.pagehide({ persisted: true });
    assert.equal(killed, undefined);
    cleanup();
    assert.equal(killed, true);
    events.pagehide({ persisted: false });
    assert.equal(killed, 'reverted');
});
function setup(reduced = false, plugins = true) {
    const calls = [], sets = [], media = [], contexts = [];
    const preference = { matches: reduced, addEventListener(type, callback) { this.change = callback; } };
    const gsap = {
        fromTo(target, from, to) {
            const tween = { target, from, to, killed: false, targets: () => [target], kill() { this.killed = true; to.onInterrupt?.(); } };
            calls.push(tween); return tween;
        },
        set: (target, vars) => sets.push({ target, vars }),
        matchMedia() {
            const item = { add(query, callback) { this.callback = callback; if (!preference.matches) callback(); }, revert() { this.reverted = true; } };
            media.push(item); return item;
        },
        context(callback) {
            const cleanup = callback();
            const context = { revert() { this.reverted = true; cleanup?.(); } };
            contexts.push(context); return context;
        },
    };
    const window = { gsap, matchMedia: () => preference, ScrollTrigger: plugins ? {} : undefined };
    vm.runInNewContext(source, { window, gsap });
    return { ...window.HoleInHubMotion, calls, sets, preference, contexts, media };
}
test('entrance and scroll effects share defaults while retaining caller timing and depth', () => {
    const ui = setup(), image = {}, frame = {};
    ui.enter(frame); ui.reveal(frame, { scale: .82, y: 0, duration: 1.1 });
    ui.parallax(image, { trigger: frame, depth: 18, scrub: .2 });
    assert.equal(ui.calls[0].to.clearProps, 'transform,opacity');
    assert.equal(ui.calls[1].from.scale, .82);
    assert.equal(ui.calls[1].to.scrollTrigger.once, true);
    assert.equal(ui.calls[2].from.yPercent, -18);
    assert.equal(ui.calls[2].to.scrollTrigger.trigger, frame);
    assert.equal(ui.calls[2].to.scrollTrigger.scrub, .2);
});
test('reduced motion and missing scroll plugins leave content untouched', () => {
    const ui = setup(true); ui.enter({}); ui.reveal({}); ui.parallax({});
    assert.equal(ui.calls.length, 0);
    const withoutPlugins = setup(false, false);
    withoutPlugins.reveal({}); withoutPlugins.parallax({});
    assert.equal(withoutPlugins.calls.length, 0);
});
test('switching to reduced motion cancels transient tweens and restores visibility', () => {
    const ui = setup(), target = {};
    ui.enter(target); ui.preference.matches = true; ui.preference.change();
    assert.equal(ui.calls[0].killed, true);
    assert.equal(ui.sets[0].vars.clearProps, 'transform,opacity');
    assert.equal(ui.sets[0].target[0], target);
});
test('declarative mount is idempotent, clamps parallax and can be disposed and remounted', () => {
    const ui = setup(), image = {}, frame = { dataset: { parallax: '1000' }, querySelector: () => image };
    const root = { querySelectorAll: selector => selector === '[data-parallax]' ? [frame] : [] };
    const dispose = ui.mount(root); ui.mount(root)();
    assert.equal(ui.contexts.length, 1);
    assert.equal(ui.calls[0].to.yPercent, 18);
    dispose(); assert.equal(ui.contexts[0].reverted, true); assert.equal(ui.media[0].reverted, true);
    ui.mount(root); assert.equal(ui.contexts.length, 2);
});
