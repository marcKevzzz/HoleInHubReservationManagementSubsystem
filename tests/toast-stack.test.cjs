const { test } = require('node:test');
const assert = require('node:assert/strict');
const { createDismissTimer } = require('../HoleInHub.Web/wwwroot/js/toast-stack.js');

test('toast countdown preserves remaining time when hidden behind a newer card or while expanded', () => {
    let now = 0, nextId = 0, dismissed = 0;
    const pending = new Map();
    const clock = {
        now: () => now,
        schedule(callback, delay) { pending.set(++nextId, { callback, delay }); return nextId; },
        cancel(id) { pending.delete(id); }
    };
    const timer = createDismissTimer(() => dismissed++, 5000, clock);
    timer.resume();
    timer.resume();
    assert.equal(pending.size, 1);
    assert.equal(pending.get(1).delay, 5000);

    now = 1200;
    timer.pause();
    timer.pause();
    now = 9000;
    assert.equal(pending.size, 0);
    assert.equal(dismissed, 0);
    timer.resume();
    assert.equal(pending.get(2).delay, 3800);
    pending.get(2).callback();
    assert.equal(dismissed, 1);
});

test('toast stack keeps the newest card in front and expands, dismisses, and restores older cards', () => {
    const vm = require('node:vm');
    const fs = require('node:fs');
    const makeNode = () => ({
        children: [], listeners: {}, attributes: {}, dataset: {}, style: { setProperty() {} },
        classList: { toggle() {} }, append(...children) { children.forEach(child => { child.parent = this; this.children.push(child); }); },
        prepend(child) { child.parent = this; this.children.unshift(child); },
        remove() { this.parent.children.splice(this.parent.children.indexOf(this), 1); },
        contains(node) { return this === node || this.children.some(child => child.contains(node)); },
        addEventListener(name, handler) { this.listeners[name] = handler; },
        setAttribute(name, value) { this.attributes[name] = value; },
    });
    const host = makeNode(), cards = makeNode(), toggle = makeNode(), count = makeNode();
    const status = makeNode(), alert = makeNode(), icon = { content: { cloneNode: makeNode } };
    host.append(cards, toggle, count, status, alert);
    host.querySelector = selector => ({
        '[data-toast-cards]': cards, '[data-toast-toggle]': toggle, '[data-toast-count]': count,
        '[data-toast-status]': status, '[data-toast-alert]': alert, '[data-toast-action-icon]': icon
    }[selector] || (selector.startsWith('[data-toast-icon=') ? icon : null));
    const window = {};
    const document = { querySelector: () => host, createElement: makeNode, addEventListener() {}, activeElement: null };
    let nextId = 0;
    const pending = new Map();
    vm.runInNewContext(fs.readFileSync(require.resolve('../HoleInHub.Web/wwwroot/js/toast-stack.js'), 'utf8'), {
        window, document, URL, location: { origin: 'http://localhost' }, performance: { now: () => 0 },
        setTimeout(callback, delay) { pending.set(++nextId, { callback, delay }); return nextId; },
        clearTimeout(id) { pending.delete(id); }
    });

    for (const title of ['One', 'Two', 'Three', 'Four']) window.HoleInHubToasts.show({ kind: 'warning', title });
    assert.deepEqual(cards.children.map(card => card.children[0].attributes['aria-label']), [
        'Dismiss notification: Four', 'Dismiss notification: Three', 'Dismiss notification: Two', 'Dismiss notification: One'
    ]);
    assert.equal(count.textContent, '+3');
    assert.equal(cards.children[1].inert, true);
    assert.equal(cards.children[3].hidden, true);
    host.listeners.pointerenter({ pointerType: 'mouse' });
    assert.equal(toggle.attributes['aria-expanded'], 'true');
    assert.equal(cards.children[3].hidden, false);
    assert.equal(cards.children[3].inert, false);
    cards.children[0].children[0].listeners.click();
    assert.equal(cards.children.length, 3);
    host.listeners.pointerleave({ pointerType: 'mouse' });
    assert.equal(toggle.attributes['aria-expanded'], 'false');
    assert.equal(count.textContent, '+2');
    assert.equal(cards.children[1].inert, true);

    window.HoleInHubToasts.show({ kind: 'success', title: 'Booking confirmed', href: '/staff', action: 'View booking' });
    const action = cards.children[0].children[1];
    assert.equal(action.attributes['aria-label'], 'View booking');
    assert.equal(action.children.length, 1);
    assert.equal(action.href.pathname, '/staff');
    assert.equal(pending.size, 0, 'a toast with an action waits for dismissal');

    window.HoleInHubToasts.show({ kind: 'error', title: 'Another error' });
    assert.equal(pending.size, 0, 'errors stay visible by default');
    window.HoleInHubToasts.show({ kind: 'error', title: 'Merge blocked', autoDismiss: true });
    assert.equal(pending.size, 1);
    assert.equal(pending.get(1).delay, 5000);
    pending.get(1).callback();
    assert.equal(cards.children[0].children[0].attributes['aria-label'], 'Dismiss notification: Another error');
    assert.equal(pending.size, 0);
});
