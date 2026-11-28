const { test, expect } = require('@playwright/test');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');

test.describe('session expiry', () => {
    test.skip(process.env.HIH_BROWSER_EXPIRY !== '1', 'Requires the isolated 15-second session fixture');
    async function passwordLogin(page, email, destination) {
        const file = path.join(os.tmpdir(), 'hih-browser-' + process.env.HIH_BROWSER_RUN_ID, 'consent.json');
        const { password } = JSON.parse(await fs.readFile(file, 'utf8'));
        await page.goto('/account/sign-in?returnUrl=' + encodeURIComponent(destination));
        await page.getByLabel('Email', { exact: true }).fill(email);
        await page.getByLabel('Password', { exact: true }).fill(password);
        await page.getByRole('button', { name: 'Sign in', exact: true }).click();
        await expect(page).toHaveURL(url => url.pathname === destination);
    }

    for (const [role, destination] of [['customer', '/my-profile'], ['admin', '/admin/api-access/keys']]) {
        test(role + ' is redirected once when the open page expires', async ({ page }) => {
            await passwordLogin(page, role + '@browser.example.test', destination);
            await expect(page).toHaveURL(url => url.pathname === '/account/sign-in' &&
                url.searchParams.get('returnUrl') === destination, { timeout: 30000 });
            const notice = page.locator('.toast-card').filter({ hasText: 'Session expired' });
            await expect(notice).toHaveCount(1);
            await page.reload();
            await expect(page.locator('[data-toast-seed][data-title="Session expired"]')).toHaveCount(0);
            await expect(notice).toHaveCount(0);
        });
    }

    for (const role of ['staff', 'manager']) {
        test(role + ' remains signed in beyond the expiry interval', async ({ page }) => {
            const managerPin = process.env.HIH_BROWSER_MANAGER_PIN;
            const pin = role === 'manager' ? managerPin : String(Number(managerPin) + 1).padStart(6, '0').slice(-6);
            await page.goto('/staff/sign-in');
            await page.getByLabel('Staff PIN').fill(pin);
            const destination = role === 'manager' ? '/staff/dashboard' : '/staff';
            await expect(page).toHaveURL(url => url.pathname === destination);
            await expect(page.locator('script[data-status-url]')).toHaveCount(0);
            await new Promise(resolve => setTimeout(resolve, 16000));
            const response = await page.reload();
            expect(response.status()).toBe(200);
            await expect(page).toHaveURL(url => url.pathname === destination);
        });
    }

    test('manual logout clears the expiry notice', async ({ page }) => {
        await passwordLogin(page, 'customer@browser.example.test', '/my-profile');
        await page.locator('#account-logout-form').evaluate(form => form.requestSubmit());
        await page.getByRole('dialog', { name: 'Log out?' }).getByRole('button', { name: 'Log out', exact: true }).click();
        await expect(page).toHaveURL(url => url.pathname === '/');
        await page.goto('/account/sign-in');
        await expect(page.locator('.toast-card').filter({ hasText: 'Session expired' })).toHaveCount(0);
        expect((await page.context().cookies()).some(cookie => cookie.name === 'HihSessionNotice')).toBe(false);
    });
});
