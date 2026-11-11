const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
test('changing a role asks for a reason; cancelling restores it and saving prevents repeats', () => {
    const node = value => ({ handlers: {}, addEventListener(k, fn) { this.handlers[k] = fn; }, ...value });
    const options = [{ value: 'Staff', textContent: 'Staff' }, { value: 'Manager', textContent: 'Manager' }];
    const select = node({ value: 'Staff', options, selectedOptions: [options[1]], focus() { this.focused = true; } });
    const reason = { disabled: true, value: '' }, cancel = node({}), submit = {}, description = {}, fallback = {};
    const dialog = node({ open: false, showModal() { this.open = true; }, close() { this.open = false; this.handlers.close(); },
        querySelector: s => ({ '[data-reason-input]': reason, '[data-reason-cancel]': cancel, '[data-reason-submit]': submit, '[data-reason-description]': description }[s]) });
    const form = node({ dataset: { currentValue: 'Staff', reasonTemplate: 'Change {from} to {to}' },
        querySelector: s => ({ '[data-confirm-change]': select, '[data-reason-dialog]': dialog, '[data-reason-fallback]': fallback }[s]) });
    vm.runInNewContext(fs.readFileSync(require.resolve('../HoleInHub.Web/wwwroot/js/reason-confirmation.js'), 'utf8'), { document: { querySelectorAll: () => [form] } });
    assert.equal(fallback.hidden, true);
    select.handlers.change(); assert.equal(dialog.open, false);
    select.value = 'Manager'; select.handlers.change();
    assert.equal(dialog.open, true); assert.equal(reason.disabled, false); assert.equal(description.textContent, 'Change Staff to Manager');
    reason.value = 'test'; cancel.handlers.click();
    assert.equal(select.value, 'Staff'); assert.equal(reason.value, ''); assert.equal(reason.disabled, true); assert.equal(select.focused, true);
    select.value = 'Manager'; select.handlers.change(); reason.value = 'Roster change';
    let prevented = false;
    form.handlers.submit({ preventDefault() { prevented = true; } });
    assert.equal(prevented, false); assert.equal(submit.disabled, true);
    form.handlers.submit({ preventDefault() { prevented = true; } }); assert.equal(prevented, true);
    cancel.handlers.click(); assert.equal(dialog.open, true);
    let cancelled = false; dialog.handlers.cancel({ preventDefault() { cancelled = true; } }); assert.equal(cancelled, true);
});
