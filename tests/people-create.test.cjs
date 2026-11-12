const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

test('creation drawer locks dismissal during save, reuses its command and navigates only after success', async () => {
    const handlers = {}, bodyHandlers = {}, panelHandlers = {};
    const button = { disabled: false, textContent: 'Add customer' };
    const input = { disabled: false, value: 'original-command' };
    const close = { disabled: false, addEventListener() {} };
    let focus = 0, destination, requests = 0, resolveSave;
    const form = {
        elements: [input, button], action: 'http://localhost/staff/customers/new',
        reportValidity: () => true, querySelector: () => button,
    };
    const content = { querySelector: selector => selector === '.validation-summary-errors' ? null : { focus() { focus++; } } };
    const body = {
        content: null, replaceChildren(value) { this.content = value; },
        setAttribute() {}, removeAttribute() {},
        querySelector: selector => selector === '[data-person-create-form]' ? (body.content === content ? form : null) : null,
        addEventListener: (name, handler) => { bodyHandlers[name] = handler; },
    };
    const panel = {
        open: false, showModal() { this.open = true; }, close() { this.open = false; panelHandlers.close(); },
        getBoundingClientRect: () => ({ left: 700, right: 1100, top: 0, bottom: 800 }),
        querySelector: selector => selector === '[data-people-create-body]' ? body : close,
        addEventListener: (name, handler) => { panelHandlers[name] = handler; },
    };
    const link = { href: form.action, focus() {} };
    const context = {
        document: {
            querySelector: () => panel, documentElement: { classList: { add() {}, remove() {} } },
            addEventListener: (name, handler) => { handlers[name] = handler; },
            createElement: () => ({ setAttribute() {}, classList: { remove() {} }, append() {} }),
        },
        DOMParser: class { parseFromString() { return { querySelector: () => content }; } },
        AbortController, URL, location: { origin: 'http://localhost' },
        window: { location: { assign(value) { destination = value; } } },
        FormData: class { constructor() { this.commandId = input.value; } },
        setTimeout: () => 1, clearTimeout() {},
        fetch: async (url, options) => {
            requests++;
            if (requests === 1) return { ok: true, text: async () => '<form></form>' };
            assert.equal(options.body.commandId, 'original-command');
            return new Promise(resolve => { resolveSave = resolve; });
        },
    };
    vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../HoleInHub.Web/wwwroot/js/people-create.js'), 'utf8'), context);
    await handlers.click({ target: { closest: () => link }, preventDefault() {} });
    assert.equal(panel.open, true);
    assert.equal(focus, 1);
    const pending = bodyHandlers.submit({ target: { closest: () => form }, preventDefault() {} });
    assert.equal(close.disabled, true);
    assert.equal(button.disabled, true);
    assert.equal(button.textContent, 'Adding…');
    let prevented = false;
    panelHandlers.cancel({ preventDefault() { prevented = true; } });
    assert.equal(prevented, true);
    const outside = { target: panel, clientX: 100, clientY: 100 };
    panelHandlers.pointerdown(outside); panelHandlers.click(outside);
    assert.equal(panel.open, true);
    resolveSave({ ok: true, headers: { get: () => 'application/json' }, json: async () => ({ redirectUrl: '/staff/customers/contact' }) });
    await pending;
    assert.equal(destination, 'http://localhost/staff/customers/contact');
    assert.equal(close.disabled, false);
    assert.equal(input.disabled, false);
    assert.equal(input.value, 'original-command');
});
