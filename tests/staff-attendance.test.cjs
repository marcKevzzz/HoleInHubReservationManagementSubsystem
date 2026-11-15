const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

test('attendance needs explicit confirmation and rechecks the session window at submission', () => {
    let now = Date.parse('2026-11-11T05:30:00Z');
    const button = {};
    const expired = {};
    const listeners = {};
    const form = {
        dataset: { checkInOpens: '2026-11-11T04:00:00Z', sessionStart: '2026-11-11T05:00:00Z', sessionEnd: '2026-11-11T06:00:00Z' },
        elements: { Action: { value: 'check-in' }, ConfirmAttendance: { checked: false }, Reason: { checkValidity: () => true } },
        querySelector: selector => selector.startsWith('button') ? button : expired,
    };
    const document = { querySelectorAll: () => [form], addEventListener: (name, handler) => { listeners[name] = handler; } };
    vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../HoleInHub.Web/wwwroot/js/staff-attendance.js'), 'utf8'), { document, window: {}, Date: { now: () => now, parse: Date.parse } });
    assert.equal(button.disabled, true);
    form.elements.ConfirmAttendance.checked = true;
    listeners.input({ target: { closest: () => form } });
    assert.equal(button.disabled, false);
    now = Date.parse('2026-11-11T06:00:00Z');
    let prevented = false;
    listeners.submit({ target: { closest: () => form }, preventDefault: () => { prevented = true; } });
    assert.equal(prevented, true);
    assert.equal(button.disabled, true);
    assert.equal(expired.hidden, false);
    now = Date.parse('2026-11-11T04:59:00Z');
    listeners.input({ target: { closest: () => form } });
    assert.equal(button.disabled, false);
    now = Date.parse('2026-11-11T04:00:00Z');
    listeners.input({ target: { closest: () => form } });
    assert.equal(button.disabled, false);
    now -= 1;
    listeners.input({ target: { closest: () => form } });
    assert.equal(button.disabled, true);
    form.elements.Action.value = 'check-out';
    form.elements.Reason.checkValidity = () => false;
    listeners.input({ target: { closest: () => form } });
    assert.equal(button.disabled, true);
    form.elements.Reason.checkValidity = () => true;
    listeners.input({ target: { closest: () => form } });
    assert.equal(button.disabled, false);
});

test('attendance corrections require the actual times appropriate to the selected status', () => {
    const events = {};
    const button = {};
    const form = {
        dataset: {},
        elements: {
            Action: { value: 'correct' }, CorrectedStatus: { value: 'Completed' },
            ArrivalLocal: {}, DepartureLocal: {}, ConfirmAttendance: { checked: true },
            Reason: { checkValidity: () => true }
        },
        querySelector: selector => selector.startsWith('button') ? button : {}
    };
    vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../HoleInHub.Web/wwwroot/js/staff-attendance.js'), 'utf8'), {
        document: { querySelectorAll: () => [form], addEventListener: (name, handler) => events[name] = handler }, window: {}
    });
    assert.equal(form.elements.ArrivalLocal.required, true);
    assert.equal(form.elements.DepartureLocal.required, true);
    form.elements.CorrectedStatus.value = 'Checked-In';
    events.change({ target: { closest: () => form } });
    assert.equal(form.elements.ArrivalLocal.disabled, false);
    assert.equal(form.elements.DepartureLocal.disabled, true);
    form.elements.CorrectedStatus.value = 'No-show';
    events.change({ target: { closest: () => form } });
    assert.equal(form.elements.ArrivalLocal.disabled, true);
    assert.equal(form.elements.DepartureLocal.disabled, true);
    form.elements.ConfirmAttendance.checked = false;
    events.input({ target: { closest: () => form } });
    assert.equal(button.disabled, true);
});
