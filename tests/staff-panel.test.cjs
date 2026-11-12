const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

test('all panel loaders share an announced message and an aria-hidden five-row skeleton', () => {
    const node = () => ({ attributes: {}, children: [], setAttribute(name, value) { this.attributes[name] = value; },
        append(child) { this.children.push(child); }, replaceChildren(...children) { this.children = children; } });
    const body = node();
    const window = {};
    vm.runInNewContext(fs.readFileSync(require.resolve('../HoleInHub.Web/wwwroot/js/staff-panel.js'), 'utf8'), {
        window, document: { createElement: node },
    });
    for (const label of ['Loading activity…', 'Loading form…', 'Loading booking actions…']) {
        const { message, skeleton } = window.HoleInHubPanels.loading(body, label);
        assert.equal(body.children.length, 2);
        assert.equal(message.textContent, label);
        assert.equal(message.attributes.role, 'status');
        assert.match(message.className, /sr-only/);
        assert.doesNotMatch(message.className, /activity-loading/);
        assert.equal(skeleton.attributes['aria-hidden'], 'true');
        assert.equal(skeleton.children.length, 5);
        assert.equal(body.attributes['aria-busy'], 'true');
    }
});
