const { test, expect } = require('@playwright/test');
const { randomUUID } = require('node:crypto');
const { signInManager, verificationCode } = require('./fixture.cjs');

test('guest receives a receipt and the approved reservation is visible to the guest', async ({ page, browser, request }, info) => {
    if (process.env.HIH_REPORT_BENCHMARK === '1') {
        // Before any page visit, confirm the background worker expired the historical pending request.
        const recipient = process.env.HIH_BROWSER_RUN_ID.replaceAll('-', '') + '@browser.example.test';
        await expect.poll(async () => {
            const response = await request.get('http://127.0.0.1:8025/api/v1/search', { params: { query: 'to:' + recipient } });
            return (await response.json()).messages.some(message => message.Subject === 'Reservation request expired');
        }, { timeout: 60_000 }).toBe(true);
    }
    const email = 'guest-' + randomUUID() + '@browser.example.test';
    await page.goto('/Reservations/Create');
    await page.locator('#booking-browse').click();
    await page.locator('#step-next').click();
    // The next calendar month always contains both project-specific future sessions.
    await page.getByRole('button', { name: 'Next month' }).click();
    await page.locator('#calendar-days button:enabled').nth(info.project.name === 'mobile' ? 3 : 2).click();
    await page.getByRole('tab', { name: 'Afternoon' }).click();
    await page.locator('#time-slots [data-minute="840"]').click();
    const availableBay = page.locator('.resource-option:has(input:enabled)').first();
    await expect(availableBay.locator('.resource-status')).toHaveText('Available for your session');
    await page.locator('#step-next').click();
    await availableBay.click();
    await expect(page.locator('[name=QuoteKey]')).toHaveValue(/.+/);
    await page.locator('#step-next').click();
    await page.locator('#FirstName').fill('Browser');
    await page.locator('#LastName').fill('Guest');
    await page.locator('#Phone').fill('09123456789');
    await page.locator('#Email').fill(email);
    await page.locator('#ContactAge').fill('32');
    await page.locator('#ContactAddress').fill('Quezon City');
    const playerLimit = Number(await availableBay.locator('input').getAttribute('data-max-players'));
    const totalLimit = Number(await availableBay.locator('input').getAttribute('data-max-guests'));
    await page.locator('#Attendees').fill(String(Math.min(playerLimit + 1, totalLimit)));
    if (totalLimit > playerLimit) await page.locator('#NonPlayerGuests').fill('1');
    const privacy = page.getByRole('checkbox', { name: /privacy policy/i });
    await privacy.focus();
    await page.keyboard.press('Space');
    await expect(privacy).toBeChecked();
    await page.locator('#step-next').click();
    await expect(page.locator('#review-selection')).toContainText('Browser Guest');
    await page.getByRole('button', { name: 'Submit reservation' }).click();
    await expect(page.getByLabel('Verification code')).toBeVisible();
    await page.getByLabel('Verification code').fill(await verificationCode(request, email));
    async function submitVerifiedBooking() {
        await page.locator('.verification-actions button[type=submit]').first().click();
        await expect(page.getByRole('heading', { name: 'REQUEST RECEIVED' })).toBeVisible();
    }
    if (process.env.HIH_REPORT_BENCHMARK === '1') {
        const manager = await browser.newPage({ baseURL: info.project.use.baseURL, ignoreHTTPSErrors: true });
        try {
            await signInManager(manager);
            await require('./report-benchmark.cjs')(manager.request, submitVerifiedBooking);
        } finally {
            await manager.close();
        }
    } else {
        await submitVerifiedBooking();
    }
    await expect(page.locator('.booking-current-status')).toHaveText('Pending');
    const reference = await page.locator('.booking-reference').innerText();
    expect(reference).toMatch(/^HIH-[A-F0-9]{10}$/);

    const staffContext = await browser.newContext({ ...info.project.use, baseURL: info.project.use.baseURL, ignoreHTTPSErrors: true });
    try {
        const staff = await staffContext.newPage();
        await signInManager(staff);
        await staff.goto('/staff/review-queue');
        await staff.getByRole('link', { name: 'Open booking ' + reference + ' for Browser Guest' }).click();
        await expect(staff.locator('.review-reference')).toContainText(reference);
        await staff.locator('summary').filter({ hasText: 'Approve booking' }).click();
        await staff.getByRole('button', { name: 'Confirm approve booking' }).click();
        await expect(staff.locator('.review-reference [data-status]')).toHaveText('Confirmed');
    } finally {
        await staffContext.close();
    }
    await page.reload();
    await expect(page.getByRole('heading', { name: 'RESERVATION CONFIRMED' })).toBeVisible();
    await expect(page.locator('.booking-reference')).toHaveText(reference);
    await expect(page.locator('.booking-current-status')).toHaveText('Confirmed');
});
