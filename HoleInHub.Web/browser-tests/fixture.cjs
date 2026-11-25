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

async function verificationCode(request, email) {
    const inbox = 'http://127.0.0.1:8025/api/v1';
    let message;
    await expect.poll(async () => {
        const response = await request.get(inbox + '/search', { params: { query: 'to:' + email } });
        expect(response.ok()).toBeTruthy();
        const result = await response.json();
        message = result.messages.find(item => item.Subject === 'Your booking verification code'
            && item.To.some(recipient => recipient.Address === email));
        return Boolean(message);
    }, { timeout: 45_000, intervals: [300, 1000] }).toBe(true);
    const response = await request.get(inbox + '/message/' + message.ID);
    const body = await response.json();
    const match = body.Text.match(/\b\d{6}\b/);
    if (!match) throw new Error('The fixture verification email contained no six-digit code.');
    return match[0];
}

module.exports = { signInManager, verificationCode };
