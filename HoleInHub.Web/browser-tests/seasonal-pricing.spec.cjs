const { test, expect } = require('@playwright/test');
const { signInManager } = require('./fixture.cjs');

test('seasonal rates and rental dates align and save from Services', async ({ page, isMobile }) => {
    await signInManager(page);
    await page.goto('/admin/services/ChristmasWholeShop/edit?activate=true');
    const first = page.getByLabel('Monday–Thursday rate');
    const second = page.getByLabel('Friday–Sunday & holidays');
    await expect(first).toHaveValue('30000');
    const left = await first.boundingBox();
    const right = await second.boundingBox();
    if (isMobile) expect(right.y).toBeGreaterThan(left.y);
    else expect(Math.abs(right.y - left.y)).toBeLessThan(2);
    await page.getByLabel('First rental date').fill('2026-12-01');
    await page.getByLabel('Last rental date').fill('2026-12-31');
    await page.getByRole('button', {name:'Save service'}).click();
    await expect(page).toHaveURL(/\/admin\/services$/);
});
