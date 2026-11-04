const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../HoleInHub.Web/wwwroot/js/button-motion.js'), 'utf8');
function setup(left, reduced = false) {
    const events = {}; const moves = []; const sets = [];
    const labelA = { querySelectorAll: () => ['a1', 'a2'] };
    const labelB = { querySelectorAll: () => ['b1', 'b2'] };
    const iconA = {}; const iconB = {};
    const timeline = { to(target, values) { moves.push({ target, values }); return this; }, play() { this.played = true; }, reverse() { this.reversed = true; }, kill() { this.killed = true; } };
    const gsap = { set(target, values) { sets.push({ target, values }); }, timeline: () => timeline };
    const button = { querySelector(selector) {
        if (selector.includes('label-a')) return labelA;
        if (selector.includes('label-b')) return labelB;
        if (selector.includes('icon-a')) return iconA;
        if (selector.includes('icon-b')) return iconB;
        if (selector === '.button-icon-left') return left;
    }, addEventListener: (event, handler) => events[event] = handler, removeEventListener: event => delete events[event] };
    const context = { window: { gsap }, gsap, matchMedia: () => ({ matches: reduced }) };
    vm.runInNewContext(source, context);
    return { ...context.window.HoleInHubButtons, button, events, moves, sets, timeline, iconA, iconB };
}
for (const left of [false, true]) test(`${left ? 'back' : 'forward'} controls use shared stagger and arrow direction`, () => {
    const ui = setup(left); const dispose = ui.animate(ui.button);
    assert.equal(ui.moves.find(move => move.target === ui.iconA).values.x, left ? -28 : 28);
    assert.equal(ui.moves.find(move => Array.isArray(move.target)).values.stagger, .015);
    ui.events.focus(); assert.equal(ui.timeline.played, true);
    ui.events.blur(); assert.equal(ui.timeline.reversed, true);
    dispose(); assert.equal(ui.timeline.killed, true);
    assert.equal(Object.keys(ui.events).length, 0);
});
test('reduced motion leaves controls static and visible', () => {
    const ui = setup(false, true); ui.animate(ui.button)();
    assert.equal(ui.sets.length, 0); assert.equal(ui.moves.length, 0);
    assert.equal(Object.keys(ui.events).length, 0);
});

test('repeated animation initialization does not duplicate handlers or timelines', () => {
    const ui = setup(false);
    const dispose = ui.animate(ui.button), count = ui.moves.length;
    ui.animate(ui.button)();
    assert.equal(ui.moves.length, count);
    assert.equal(Object.keys(ui.events).length, 4);
    dispose();
    ui.animate(ui.button);
    assert.ok(ui.moves.length > count);
});

test('preparing an already enhanced button leaves its markup unchanged', () => {
    const ui = setup(false);
    const button = { querySelector: () => ({}), replaceChildren() { assert.fail('must not rebuild prepared content'); } };
    ui.prepare(button);
});
test('disabled controls do not play hover or focus motion', () => {
    const ui = setup(false); ui.button.disabled = true;
    ui.animate(ui.button); ui.events.focus(); ui.events.pointerenter();
    assert.equal(ui.timeline.played, undefined);
});

test('automatic button mounting waits for DOM readiness and preserves back-forward cache handlers', () => {
    const documentEvents = {}, windowEvents = {};
    let mediaCleanup, mediaReverts = 0, preparations = 0;
    const button = {
        querySelector: () => null,
        matches: () => false,
        get textContent() { preparations++; return 'Continue'; },
        setAttribute() {}, getAttribute: () => null, replaceChildren() {}, hasAttribute: () => true,
        append() {}, addEventListener() {}, removeEventListener() {},
    };
    const element = () => ({ dataset: {}, setAttribute() {}, append() {} });
    const document = {
        readyState: 'loading', querySelectorAll: () => [button], createElement: element,
        addEventListener: (name, callback) => documentEvents[name] = callback,
    };
    const gsap = { matchMedia: () => ({
        add(query, callback) { mediaCleanup = callback(); },
        revert() { mediaReverts++; mediaCleanup(); },
    }) };
    const window = { gsap, addEventListener: (name, callback) => windowEvents[name] = callback };
    vm.runInNewContext(source, { window, document, gsap, matchMedia: () => ({ matches: true }) });
    assert.equal(preparations, 0);
    documentEvents.DOMContentLoaded();
    assert.equal(preparations, 1);
    windowEvents.pagehide({ persisted: true });
    assert.equal(mediaReverts, 0);
    windowEvents.pagehide({ persisted: false });
    assert.equal(mediaReverts, 1);
});
