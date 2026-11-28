const { test, expect } = require('@playwright/test');
const { signInManager } = require('./fixture.cjs');

test.use({ reducedMotion: 'reduce' });

for (const occupied of [1, 2]) {
    test('chat handoff after staff block ' + occupied + ' VIP bay(s)', async ({ page, browser }, info) => {
        const offset = 10 + occupied + (info.project.name === 'mobile' ? 3 : 0);
        const date = new Date(Date.now() + (offset * 24 + 8) * 3600000).toISOString().slice(0, 10);
        await page.goto('/');
        await page.locator('#venue-chat summary').click();
        await page.getByRole('textbox', { name: 'Your question' }).fill('VIP available ' + date + ' at 3pm for 1 hour?');
        await page.getByRole('button', { name: 'Send message' }).click();
        const reply = page.locator('.chat-turn').filter({ has: page.getByRole('link', { name: 'Request a booking' }) });
        await expect(reply).toContainText('VIP 1');
        await expect(reply).toContainText('VIP 2');

        const staff = await browser.newPage({ baseURL: info.project.use.baseURL, ignoreHTTPSErrors: true });
        try {
            await signInManager(staff);
            for (let i = 1; i <= occupied; i++) {
                await staff.goto('/staff/reservations/blocks/new?date=' + date);
                const form = staff.locator('form.staff-create-form');
                const bay = await form.locator('#block-bay option').filter({ hasText: 'VIP ' + i }).getAttribute('value');
                const response = await staff.request.post('/staff/reservations/blocks/new', {
                    headers: { Accept: 'application/json' },
                    form: {
                        __RequestVerificationToken: await form.locator('[name=__RequestVerificationToken]').inputValue(),
                        IdempotencyKey: await form.locator('#block-key').inputValue(),
                        ResourceId: bay, Date: date, StartTime: '15:00', DurationMinutes: '60', Reason: 'Simulator maintenance'
                    }
                });
                expect(response.ok()).toBeTruthy();
            }
        } finally {
            await staff.close();
        }

        await reply.getByRole('link').click();
        await page.locator('#booking-browse').click();
        await Promise.all([
            page.waitForResponse(r => r.url().includes('/Reservations/BayDay?') && r.ok()),
            page.locator('#step-next').click()
        ]);
        await expect(page.locator('#time-slots')).toHaveAttribute('aria-busy', 'false');
        await expect(page.locator('[name=Date]')).toHaveValue(date);
        await expect(page.locator('[name=DurationMinutes]')).toHaveValue('60');
        if (occupied === 2) {
            await expect(page.locator('[name=StartTime]')).toHaveValue('');
            await expect(page.locator('#session-availability')).toContainText(/no bays are available/i);
            await expect(page.locator('#time-slots [data-minute="900"]:enabled')).toHaveCount(0);
            await page.locator('#time-slots [data-minute="960"]').click();
            await expect(page.locator('[name=StartTime]')).toHaveValue(/^16:00/);
            await expect(page.locator('[name=DurationMinutes]')).toHaveValue('60');
        } else {
            await expect(page.locator('[name=StartTime]')).toHaveValue(/^15:00/);
            await expect(page.locator('#time-slots [data-minute="900"]')).toBeVisible();
        }
        await expect(page.locator('input[name=ResourceIds][data-service=VIP]:enabled')).toHaveCount(occupied === 2 ? 2 : 1);
        await page.locator('#step-next').click();
        const first = page.locator('.resource-option').filter({ hasText: 'VIP 1' });
        const second = page.locator('.resource-option').filter({ hasText: 'VIP 2' });
        if (occupied === 1) await expect(first.locator('input')).toBeDisabled();
        await expect(second).toBeVisible();
        await second.getByText('VIP 2', { exact: true }).click();
        await expect(second.locator('input')).toBeChecked();
        await expect(page.locator('[name=Date]')).toHaveValue(date);
    });
}
