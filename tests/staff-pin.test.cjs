const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const vm = require('node:vm');

const script = () => readFileSync(require.resolve('../HoleInHub.Web/wwwroot/js/staff-pin.js'), 'utf8');

function mount({ invalid = false, reducedMotion = false } = {}) {
    const formEvents = {};
    const inputEvents = {};
    const slots = Array.from({ length: 6 }, () => ({ dataset: {} }));
    const boxes = { hidden: true, children: slots };
    const entry = { dataset: {} };
    const button = { textContent: 'Sign in' };
    const error = { hidden: !invalid };
    const input = {
        value: '', maxLength: 6,
        getAttribute: () => String(invalid),
        setAttribute() {},
        addEventListener: (name, fn) => { inputEvents[name] = fn; },
        focus() { this.focused = true; }
    };
    const form = {
        submissions: 0,
        querySelector: selector => ({
            '[data-pin-entry]': entry, '[data-pin-slots]': boxes, '[data-pin-error]': error,
            '#Pin': input, 'button[type="submit"]': button
        })[selector],
        addEventListener: (name, fn) => { formEvents[name] = fn; },
        setAttribute() {},
        requestSubmit() {
            const event = { preventDefault() { this.prevented = true; } };
            formEvents.submit(event);
            if (!event.prevented) this.submissions++;
        }
    };
    let shakes = 0;
    vm.runInNewContext(script(), {
        document: { querySelector: () => form },
        window: {
            matchMedia: () => ({ matches: reducedMotion }),
            addEventListener() {},
            gsap: { to: () => { shakes++; } }
        }
    });
    return { form, input, slots, boxes, shakes, type(value) { input.value = value; inputEvents.input(); } };
}

test('six digits submit once and preserve a leading zero', () => {
    const ui = mount();
    assert.equal(ui.boxes.hidden, false);
    ui.type('01234');
    assert.equal(ui.form.submissions, 0);
    ui.type('012345');
    ui.form.requestSubmit();
    assert.equal(ui.form.submissions, 1);
    assert.equal(ui.input.value, '012345');
    assert.equal(ui.slots.filter(slot => 'filled' in slot.dataset).length, 6);
});

test('a server error refocuses the PIN and respects reduced motion', () => {
    const normal = mount({ invalid: true });
    const reduced = mount({ invalid: true, reducedMotion: true });
    assert.equal(normal.input.focused, true);
    assert.equal(normal.shakes, 1);
    assert.equal(reduced.input.focused, true);
    assert.equal(reduced.shakes, 0);
});
