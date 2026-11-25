const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');

function setup(fail = false) {
    let submit, posted, removed = false, appended, prevented = false;
    const button = { disabled: false };
    const focus = { focus() {} };
    const replacement = {};
    const feedback = { dataset: {}, setAttribute() {} };
    const hub = {
        querySelector: selector => selector.includes('summary') ? focus : null,
        querySelectorAll: selector => selector.includes('[open]') ? [] : [{ remove() { removed = true; } }],
        append(node) { appended = node; },
    };
    const form = { action: 'https://example.test/notifications/action', matches: () => true, querySelectorAll: () => [button] };
    const next = { querySelector: () => replacement, querySelectorAll: () => [replacement] };
    vm.runInNewContext(fs.readFileSync(require.resolve('../HoleInHub.Web/wwwroot/js/account-inbox-actions.js'), 'utf8'), {
        document: { querySelector: () => hub, addEventListener: (_, fn) => submit = fn, createElement: () => feedback, documentElement: { lang: 'en' }, dispatchEvent() {} },
        window: {}, FormData: class { set(key, value) { posted = [key,value]; } }, URL, AbortSignal,
        DOMParser: class { parseFromString() { return { querySelector: () => next }; } },
        CustomEvent: class {},
        fetch: async () => { if (fail) throw new Error(); return { ok: true, url: 'https://example.test/notifications', text: async () => '', json: async () => ({ unread: 0 }) }; },
    });
    return { run: () => submit({ target: form, preventDefault() { prevented = true; }, submitter: { name: 'operation', value: 'delete' } }),
        state: () => ({ posted, removed, appended, prevented, button, feedback }) };
}
test('notification actions replace inbox content without navigation and retain the clicked operation', async () => {
    const ui = setup(); await ui.run(); const state = ui.state();
    assert(state.prevented); assert(state.removed); assert(state.appended);
    assert.deepEqual(state.posted, ['operation','delete']); assert.equal(state.button.disabled, false);
});
test('failed notification actions retain content and show retry feedback', async () => {
    const ui = setup(true); await ui.run(); const state = ui.state();
    assert.equal(state.removed, false); assert.match(state.feedback.textContent, /try again/);
    assert.equal(state.button.disabled, false);
});
