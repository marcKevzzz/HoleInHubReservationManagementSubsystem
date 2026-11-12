const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');

test('review confirmation protects focus, preserves the form, and requires Other details', () => {
    const listeners = {};
    function node(tag) {
        return { tag, children: [], attributes: {}, events: {},
            append(...items) { for (const item of items) { item.parent?.children.splice(item.parent.children.indexOf(item), 1); item.parent = this; this.children.push(item); } },
            setAttribute(k, v) { this.attributes[k] = v; },
            addEventListener(k, fn) { this.events[k] = fn; },
            remove() { this.parent?.children.splice(this.parent.children.indexOf(this), 1); },
            showModal() { this.shown = true; }, close() { this.events.close(); },
            querySelectorAll(selector) { return this.children.filter(n => selector === 'button[type="submit"]' && n.type === 'submit'); },
        };
    }
    const document = { body: node('body'), createElement: node,
        addEventListener(k, fn) { listeners[k] = fn; }, querySelectorAll() { return []; }, querySelector() { return null; } };
    const window = { location: { hash: '' }, addEventListener() {} };
    vm.runInNewContext(fs.readFileSync(require.resolve('../HoleInHub.Web/wwwroot/js/staff-table.js'), 'utf8'), { document, window });
    const choice = node('select'); choice.value = 'Other';
    const form = node('form'); form.elements = { reason: { required: false } };
    form.querySelector = selector => selector === '[name="reasonChoice"]' ? choice : form.children.find(n => n.className === 'review-confirmation-actions');
    const submit = node('button'); submit.type = 'submit'; form.append(submit);
    const summary = { getAttribute() { return 'Reject booking'; }, focus() { this.focused = true; } };
    const action = node('details'); action.open = true; action.append(form);
    action.matches = () => action.open;
    action.querySelector = selector => selector === 'form' ? form : summary;
    listeners.toggle({ target: action });
    const dialog = document.body.children[0];
    assert.equal(dialog.shown, true);
    assert.equal(action.open, false);
    assert.equal(dialog.attributes['aria-labelledby'], 'review-confirmation-title');
    assert.equal(form.elements.reason.required, true);
    choice.value = 'Bay unavailable'; choice.events.change();
    assert.equal(form.elements.reason.required, false);
    const footer = form.querySelector('.review-confirmation-actions');
    assert.equal(footer.children[0].autofocus, true);
    footer.children[0].events.click();
    assert.equal(action.children[0], form);
    assert.equal(form.children[0], submit);
    assert.equal(summary.focused, true);
    assert.equal(document.body.children.length, 0);
});
