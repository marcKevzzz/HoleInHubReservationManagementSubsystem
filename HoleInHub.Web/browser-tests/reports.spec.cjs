const { test, expect } = require('@playwright/test');
const fs = require('node:fs/promises');
const { signInManager } = require('./fixture.cjs');

test.beforeEach(async ({ page }) => {
    await signInManager(page);
    await page.goto('/manager/reports');
    await expect(page.getByRole('heading', { name: 'Reports', exact: true })).toBeVisible();
});

test('customer slices support pointer, touch and keyboard with reduced motion', async ({ page }, testInfo) => {
    await page.emulateMedia({ reducedMotion: 'reduce' });
    const canvas = page.locator('[data-customer-donut] canvas');
    await expect(canvas).toBeVisible();
    await canvas.scrollIntoViewIfNeeded();
    await expect.poll(() => canvas.evaluate(el => Chart.getChart(el).options.animation)).toBe(false);
    for (const [index, label] of ['New customers', 'Returning customers'].entries()) {
        const point = await canvas.evaluate((el, slice) => {
            const arc = Chart.getChart(el).getDatasetMeta(0).data[slice];
            const position = arc.getCenterPoint();
            const bounds = el.getBoundingClientRect();
            return { x: bounds.left + position.x, y: bounds.top + position.y };
        }, index);
        if (testInfo.project.use.hasTouch || await page.evaluate(() => navigator.maxTouchPoints > 0)) {
            await page.touchscreen.tap(point.x, point.y);
        } else {
            await page.mouse.move(point.x, point.y);
        }
        const tooltip = () => canvas.evaluate(el => {
            const tip = Chart.getChart(el).tooltip;
            return tip.opacity > 0 ? tip.dataPoints[0]?.label : null;
        });
        await expect.poll(tooltip).toBe(label);
        const button = page.getByRole('button', { name: new RegExp(label) });
        await button.focus();
        await page.keyboard.press('Enter');
        await expect(button).toBeFocused();
        await expect.poll(tooltip).toBe(label);
    }
    const period = page.locator('.dashboard-period summary');
    await period.focus();
    await page.keyboard.press('Enter');
    await expect(page.getByRole('link', { name: 'Today', exact: true })).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(period).toBeFocused();
    await expect(page.locator('.dashboard-period')).not.toHaveAttribute('open', '');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('status and Back preserve charts, focus the list and export the filtered workbook', async ({ page }) => {
    const canvas = page.locator('[data-customer-donut] canvas');
    await expect(canvas).toBeVisible();
    const original = await canvas.elementHandle();
    const list = page.locator('#report-reservations');
    const all = list.getByRole('link', { name: /^All \(/ });
    const originalCount = await all.innerText();
    const completed = list.getByRole('link', { name: /^Completed \(/ });
    const response = page.waitForResponse(res => res.request().headers()['x-reports'] === 'list');
    await completed.focus();
    await page.keyboard.press('Enter');
    expect((await response).ok()).toBe(true);
    await expect(completed).toHaveAttribute('aria-current', 'true');
    await expect(list).toBeFocused();
    const statuses = await list.locator('tbody [data-status]').allTextContents();
    expect(statuses.length).toBeGreaterThan(0);
    expect(statuses.every(status => status === 'Completed')).toBe(true);
    expect(await original.evaluate(el => el.isConnected)).toBe(true);
    const exportLink = page.getByRole('link', { name: /Export Excel/ });
    await expect(exportLink).toHaveAttribute('href', /status=Completed/);
    const downloading = page.waitForEvent('download');
    await exportLink.click();
    const download = await downloading;
    expect(download.suggestedFilename()).toMatch(/^report_.*\.xlsx$/);
    expect(await download.failure()).toBeNull();
    const bytes = await fs.readFile(await download.path());
    expect(bytes.subarray(0, 4).toString('hex')).toBe('504b0304');
    await page.goBack();
    await expect(all).toHaveAttribute('aria-current', 'true');
    await expect(all).toHaveText(originalCount);
    expect(await original.evaluate(el => el.isConnected)).toBe(true);
    await expect(exportLink).not.toHaveAttribute('href', /status=Completed/);
});

test('changing the period shows the loading state and restores the previous range on Back', async ({ page }) => {
    const report = page.locator('[data-reports]');
    const period = report.locator('.dashboard-period summary');
    const start = await page.locator('#report-from').inputValue();
    const end = await page.locator('#report-to').inputValue();
    expect(start).not.toBe(end);
    let resume;
    const release = new Promise(resolve => { resume = resolve; });
    await page.route('**/manager/reports?preset=1*', async route => {
        await release;
        await route.continue();
    });
    await period.click();
    await page.getByRole('link', { name: 'Today', exact: true }).click();
    try {
        await expect(report.locator('.reports-skeleton')).toBeVisible();
        await expect(report.locator('[data-report-results]')).toBeHidden();
    } finally {
        resume();
    }
    await expect(page.locator('#report-from')).toHaveValue(end);
    await expect(period).toBeFocused();
    await page.goBack();
    await expect(page.locator('#report-from')).toHaveValue(start);
    await expect(page.locator('#report-to')).toHaveValue(end);
    await expect(report.locator('.reports-skeleton')).toBeHidden();
});
