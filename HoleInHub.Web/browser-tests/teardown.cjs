const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const { setTimeout } = require('node:timers/promises');

module.exports = async function teardown() {
    const directory = path.join(os.tmpdir(), 'hih-browser-' + process.env.HIH_BROWSER_RUN_ID);
    try {
        await fs.writeFile(path.join(directory, 'shutdown'), '');
    } catch (error) {
        if (error.code === 'ENOENT') return;
        throw error;
    }
    // Let the fixture stop Kestrel and release its database before Playwright forcibly reaps the server on Windows.
    for (let i = 0; i < 100; i++) {
        try { await fs.access(directory); }
        catch (error) { if (error.code === 'ENOENT') return; throw error; }
        await setTimeout(150);
    }
    throw new Error('The browser fixture did not complete isolated cleanup.');
};
