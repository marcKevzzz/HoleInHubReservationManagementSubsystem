const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
test('merge selection guidance is shown once as a toast', () => {
    const notices = [];
    let guidance = { remove() { guidance = null; } };
    const document = { querySelector: selector => selector === '[data-merge-guidance]' ? guidance : null };
    const window = { HoleInHubToasts: { show: notice => notices.push(notice) } };
    vm.runInNewContext(fs.readFileSync(require.resolve('../HoleInHub.Web/wwwroot/js/customer-merge.js'), 'utf8'), { document, window });
    window.HoleInHubCustomerMerge.mount();
    assert.equal(notices.length, 1);
    assert.equal(notices[0].kind, 'info');
    assert.match(notices[0].message, /same person/);
});
test('merge counts follow all versus selected bookings and prevent an empty selection', () => {
    let scope = 'all', selected = 2;
    const output = {}, submit = {}, description = {}, handlers = {};
    const form = { dataset: { sourceCount: '2', targetCount: '3' },
        querySelector(s) { return { '[data-merge-count]': output, '[data-reason-fallback]': submit, '[data-reason-description]': description, '[name="Form.BookingScope"]:checked': { value: scope } }[s] || null; },
        querySelectorAll() { return Array(selected).fill({}); }, addEventListener(k, fn) { handlers[k] = fn; } };
    vm.runInNewContext(fs.readFileSync(require.resolve('../HoleInHub.Web/wwwroot/js/customer-merge.js'), 'utf8'), { document: { querySelector: selector => selector === '[data-customer-merge]' ? form : null } });
    assert.equal(output.value, 5); assert.equal(submit.disabled, false);
    scope = 'selected'; selected = 1; handlers.change();
    assert.equal(output.value, 4); assert.match(description.textContent, /Combine 1 reservation/);
    selected = 0; handlers.change(); assert.equal(submit.disabled, true);
});


test('modal reservation checkboxes set the scope and keep empty selection available for feedback', () => {
    let selected = 2;
    const scope = {}, count = {}, included = {}, review = { disabled: false }, description = {};
    const handlers = {};
    const form = {
        dataset: { sourceCount: '2', targetCount: '3' },
        querySelector(selector) {
            return {
                'input[type="hidden"][name="Form.BookingScope"]': scope,
                '[data-merge-count]': count,
                '[data-merge-included]': included,
                '[data-reason-fallback]': review,
                '[data-reason-description]': description,
            }[selector];
        },
        querySelectorAll() { return Array(selected).fill({}); },
        addEventListener(type, handler) { handlers[type] = handler; },
    };
    vm.runInNewContext(fs.readFileSync(require.resolve('../HoleInHub.Web/wwwroot/js/customer-merge.js'), 'utf8'), { document: { querySelector: selector => selector === '[data-customer-merge]' ? form : null } });
    assert.equal(scope.value, 'all');
    assert.equal(count.value, 5);
    selected = 1;
    handlers.change();
    assert.equal(scope.value, 'selected');
    assert.equal(count.value, 4);
    assert.equal(included.textContent, '1 included');
    selected = 0;
    handlers.change();
    assert.equal(form.dataset.mergeIncluded, '0');
    assert.equal(review.disabled, false);
});
