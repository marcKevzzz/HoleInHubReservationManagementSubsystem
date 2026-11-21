const { expect } = require('@playwright/test');

let managerCookies;

async function signInManager(page) {
    if (managerCookies) {
        await page.context().addCookies(managerCookies);
        return;
    }
    await page.goto('/staff/sign-in');
    await page.getByLabel('Staff PIN').fill(process.env.HIH_BROWSER_MANAGER_PIN);
    await expect(page).toHaveURL(/\/manager|\/staff\/dashboard/);
    managerCookies = await page.context().cookies();
}

module.exports = { signInManager };
