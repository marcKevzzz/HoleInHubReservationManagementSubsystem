const { defineConfig, devices } = require('@playwright/test');

const { randomInt, randomUUID } = require('node:crypto');

process.env.HIH_BROWSER_MANAGER_PIN ||= String(randomInt(100000, 1000000));
process.env.HIH_BROWSER_RUN_ID ||= randomUUID();
const baseURL = 'https://localhost:' + (process.env.HIH_BROWSER_PORT || '54893');

module.exports = defineConfig({
    globalTeardown: require.resolve('./browser-tests/teardown.cjs'),
    testDir: './browser-tests',
    outputDir: '../.local/browser-test-results',
    workers: 1,
    timeout: 90_000,
    use: { baseURL, ignoreHTTPSErrors: true },
    projects: [
        { name: 'desktop', use: { ...devices['Desktop Chrome'] } },
        { name: 'mobile', use: { ...devices['Pixel 7'], defaultBrowserType: 'chromium' } }
    ],
    webServer: {
        command: 'dotnet run -c ' + (process.env.HIH_BROWSER_CONFIGURATION || 'Release') + ' --project ../tests/HoleInHub.BrowserFixture --no-launch-profile',
        url: baseURL + '/health/ready',
        ignoreHTTPSErrors: true,
        timeout: 120_000,
        gracefulShutdown: { signal: 'SIGTERM', timeout: 15_000 },
        reuseExistingServer: false
    }
});
