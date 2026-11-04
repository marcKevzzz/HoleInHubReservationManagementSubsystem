const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync(require('node:path').join(__dirname, '../HoleInHub.Web/wwwroot/js/services.js'), 'utf8');

function setup() {
    const events = {};
    let interval;
    let calls = 0;
    let response = { ok: true, text: async () => '<article>Updated offering</article>' };
    const catalog = { innerHTML: '<article>Last good offering</article>', focused: false, contains() { return this.focused; } };
    const status = { textContent: '' };
    const page = { dataset: { refreshUrl: '/Home/ServiceCatalog' }, querySelector: selector => selector.includes('catalog') ? catalog : status };
    const document = { hidden: false, activeElement: {}, querySelector: () => page, addEventListener: (name, fn) => events[name] = fn };
    vm.runInNewContext(source, {
        document, window: {}, AbortController,
        fetch: async (url, options) => { calls++; assert.equal(options.cache, 'no-store'); return response; },
        setInterval: (fn, ms) => { assert.equal(ms, 45000); interval = fn; },
        setTimeout: () => 1, clearTimeout: () => {}
    });
    return { document, catalog, status, refresh: () => interval(), events, calls: () => calls, response: value => response = value };
}

test('visible refresh updates content and failed refresh retains last good offerings', async () => {
    const ui = setup();
    await ui.refresh();
    assert.match(ui.catalog.innerHTML, /Updated offering/);
    ui.response({ ok: false });
    await ui.refresh();
    assert.match(ui.catalog.innerHTML, /Updated offering/);
    assert.match(ui.status.textContent, /couldn’t refresh/);
});

test('hidden tabs and focused interactions are not refreshed', async () => {
    const ui = setup();
    ui.document.hidden = true;
    await ui.refresh();
    assert.equal(ui.calls(), 0);
    ui.document.hidden = false;
    ui.catalog.focused = true;
    await ui.refresh();
    assert.equal(ui.calls(), 0);
    ui.catalog.focused = false;
    await ui.refresh();
    assert.equal(ui.calls(), 1);
});

test('focus acquired during an in-flight fetch preserves the interaction', async () => {
    const ui = setup();
    let finish;
    let reading;
    const started = new Promise(resolve => reading = resolve);
    ui.response({ ok: true, text: () => { reading(); return new Promise(resolve => finish = resolve); } });
    const pending = ui.refresh();
    await started;
    ui.catalog.focused = true;
    finish('<article>New content</article>');
    await pending;
    assert.match(ui.catalog.innerHTML, /Last good offering/);
});
