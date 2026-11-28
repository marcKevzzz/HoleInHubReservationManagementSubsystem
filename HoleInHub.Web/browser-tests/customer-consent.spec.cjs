const { test, expect } = require('@playwright/test');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');

test('customer chooses whether Ordering may read their profile once', async ({ page, request, isMobile }) => {
    const directory = path.join(os.tmpdir(), 'hih-browser-' + process.env.HIH_BROWSER_RUN_ID);
    const fixture = JSON.parse(await fs.readFile(path.join(directory, 'consent.json'), 'utf8'));
    const consent = '/my-profile/share/' + fixture.ordering.split('.')[0];
    const redeem = (key, code = '') => request.post('/api/v1/customers/shared-profile', {
        headers: { 'X-Api-Key': key, 'X-Customer-Approval': code }
    });

    await page.goto(consent);

    await page.getByLabel('Email', { exact: true }).fill(fixture.Email);
    await page.getByLabel('Password', { exact: true }).fill(fixture.password);
    await page.getByRole('button', { name: 'Sign in', exact: true }).click();
    await expect(page).toHaveURL(url => url.pathname === consent);
    await expect(page.getByText(/Share your account ID, first name, last name, email and phone number with Ordering/)).toBeVisible();
    const cookieAccept = page.locator('[data-cookie-accept]');
    if (await cookieAccept.isVisible()) await cookieAccept.click();
    let approvalPosts = 0;
    page.on('request', req => {
        if (req.method() === 'POST' && new URL(req.url()).pathname === consent) approvalPosts++;
    });
    const cancel = page.getByRole('link', { name: 'Cancel' });
    if (isMobile) await cancel.tap();
    else await cancel.click();
    await expect(page).toHaveURL(/\/my-profile$/);
    expect(approvalPosts).toBe(0);
    expect((await redeem(fixture.ordering)).status()).toBe(403);

    await page.goto(consent);
    await page.getByRole('button', { name: 'Approve and get code' }).click();
    const code = await page.locator('.account-hub code').innerText();
    expect(approvalPosts).toBe(1);
    expect((await redeem(fixture.other, code)).status()).toBe(403);
    const shared = await redeem(fixture.ordering, code);
    expect(shared.status()).toBe(200);
    expect(await shared.json()).toEqual({ accountId: fixture.accountId, firstName: 'Ana', lastName: 'Cruz',
        email: fixture.Email, phone: '09175550123' });
    expect((await redeem(fixture.ordering, code)).status()).toBe(403);
});
