const { test, expect } = require('@playwright/test');
test.use({ actionTimeout: 6000 });
const { randomUUID } = require('node:crypto');
const { signInManager, verificationCode } = require('./fixture.cjs');

function nextMonthDates(weekday) {
    const manila = new Date(Date.now() + 8 * 3600000);
    const dayOff = new Date(Date.UTC(manila.getUTCFullYear(), manila.getUTCMonth() + 1, 1));
    while (dayOff.getUTCDay() !== weekday) dayOff.setUTCDate(dayOff.getUTCDate() + 1);
    return { dayOff, workingDay: new Date(dayOff.getTime() + 7 * 86400000) };
}

async function createCoach(page, name, weekday, dayOff) {
    await page.getByRole('link', { name: 'Add coach' }).click();
    await page.getByLabel('Coach name').fill(name);
    await page.getByLabel('Professional title').fill('KPGA Pro');
    await page.locator('summary').filter({ hasText: 'Weekly hours' }).click();
    await page.locator('summary').filter({ hasText: 'Date exceptions' }).click();
    const remove = page.getByRole('button', { name: 'Remove shift 7', exact: true });
    await remove.click();
    await page.getByRole('dialog').getByRole('button', {name:'Cancel'}).click();
    await remove.click();
    await page.getByRole('dialog').getByRole('button', {name:'Remove',exact:true}).click();
    await expect(remove).toHaveCount(0);
    if (!(await page.locator('[name="Exceptions[0].Date"]').isVisible())) await page.locator('summary').filter({ hasText: 'Date exceptions' }).click();
    await page.getByLabel('Contact number').fill('09123456789');
    await page.getByLabel('Email address').fill('coach@example.test');
    const shift = page.locator('fieldset').filter({
        has: page.locator('select option:checked[value="' + weekday + '"]')
    });
    await shift.getByLabel('Start').fill('13:00');
    await shift.getByLabel('End').fill('17:00');
    await page.locator('[name="Exceptions[0].Date"]').fill(dayOff.toISOString().slice(0, 10));
    await page.getByRole('button', { name: 'Save coach' }).press('Enter');
    const card = page.locator('article').filter({ has: page.getByRole('heading', { name, exact: true }) });
    await expect(card.getByRole('link', { name: 'Open ' + name, exact: true })).toBeVisible();
    await card.getByRole('link', { name: 'Edit ' + name }).click();
    await expect(page.getByLabel('Professional title')).toHaveValue('KPGA Pro');
    await expect(page.getByLabel('Contact number')).toHaveValue('9123456789');
    await expect(page.getByLabel('Email address')).toHaveValue('coach@example.test');
    await page.getByRole('link', { name: 'Back to coaches' }).click();
}

async function openService(page, service) {
    await page.goto('/Reservations/Create');
    if (await page.locator('#booking-browse').isVisible()) await page.locator('#booking-browse').click();
    await page.locator('label.service-option').filter({ has: page.locator('#service-' + service) }).click();
    await page.locator('#step-next').click();
    await page.getByRole('button', { name: 'Next month' }).click();
}

async function selectSession(page, date) {
    const label = new Intl.DateTimeFormat('en-US', { dateStyle: 'full', timeZone: 'UTC' }).format(date);
    await page.getByRole('button', { name: label, exact: true }).click();
    await page.getByRole('tab', { name: 'Afternoon' }).click();
    await page.locator('#time-slots [data-minute="840"]').click();
}

test('staff schedule, customer coach assignment and reassignment', async ({ page, browser, request }, info) => {
    const weekday = info.project.name === 'mobile' ? 4 : 2;
    const { dayOff, workingDay } = nextMonthDates(weekday);
    const names = ['Ada', 'Bea'].map(name => name + ' ' + info.project.name);
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await signInManager(page);
    await page.goto('/staff/coaches');
    for (const name of names) await createCoach(page, name, weekday, dayOff);
    const guest = await browser.newPage({ ...info.project.use, reducedMotion: 'reduce', ignoreHTTPSErrors: true });
    try {
        await openService(guest, 'KpgaPro');
        await selectSession(guest, dayOff);
        await expect(guest.locator('.resource-status').filter({ hasText: 'Available for your session' }).first()).toBeAttached();

        await openService(guest, 'PhilippineCoach');
        await selectSession(guest, dayOff);
        await expect(guest.locator('#session-availability')).toContainText(/no .*available|unavailable/i);
        await selectSession(guest, workingDay);
        const availableBay = guest.locator('.resource-option:has(input:enabled)').first();
        await expect(availableBay.locator('.resource-status')).toHaveText('Available for your session');
        await guest.locator('#step-next').click();
        await availableBay.click();
        await expect(guest.locator('[name=QuoteKey]')).toHaveValue(/.+/);
        await guest.locator('#step-next').click();
        const email = 'coaching-' + randomUUID() + '@browser.example.test';
        await guest.locator('#FirstName').fill('Coach');
        await guest.locator('#ContactAge').fill('30');
        await guest.locator('#ContactAddress').fill('Quezon City');
        await guest.locator('#LastName').fill('Guest');
        await guest.locator('#Phone').fill('09123456789');
        await guest.locator('#Email').fill(email);
        await guest.locator('#Attendees').fill('1');
        await guest.getByRole('checkbox', { name: /privacy policy/i }).press('Space');
        await guest.locator('#step-next').click();
        await guest.getByRole('button', { name: 'Submit reservation' }).click();
        await guest.getByLabel('Verification code').fill(await verificationCode(request, email));
        await guest.locator('.verification-actions button[type=submit]').first().click();
        await expect(guest.locator('.booking-current-status')).toHaveText('Pending');
        const reference = await guest.locator('.booking-reference').innerText();
        const assigned = guest.getByText(/^Assigned coach:/);
        await expect(assigned).toHaveText(new RegExp('Assigned coach: (' + names.join('|') + ')'));
        const original = (await assigned.innerText()).replace('Assigned coach: ', '');
        const replacement = names.find(name => name !== original);
        await page.goto('/staff/review-queue');
        await page.getByRole('link', { name: 'Open booking ' + reference + ' for Coach Guest' }).click();
        await page.getByText('Booking details & notes', { exact: true }).press('Enter');
        await page.getByRole('link', { name: 'Change assignment' }).click();
        await expect(page.getByText('Current coach: ' + original)).toBeVisible();
        await page.getByRole('button', { name: /^Available coach/ }).click();
        await page.getByRole('option', { name: replacement, exact: true }).click();
        await page.getByRole('button', { name: 'Save assignment' }).press('Enter');
        await expect(page.locator('.review-reference')).toContainText(reference);
        await guest.reload();
        await expect(assigned).toHaveText('Assigned coach: ' + replacement);
    } finally {
        await guest.close();
    }
});
test('coach navigation shows the destination skeleton during a delayed request', async ({page}) => {
    await signInManager(page);
    await page.goto('/staff/dashboard');
    for (const [name, path, layout] of [
        ['Coaches', '/staff/coaches', 'coaches'],
        ['Add coach', '/staff/coaches/new', 'coach-editor']
    ]) {
        let release;
        const gate = new Promise(resolve => { release = resolve; });
        await page.route(url => url.pathname === path, async route => {
            await gate;
            await route.continue();
        }, {times: 1});
        const link = page.getByRole('link', {name, exact: true});
        if (!(await link.isVisible())) await page.locator('[aria-controls="staff-rail-content"]').first().click();
        const shown = page.evaluate(expected => new Promise(resolve => {
            const overlay = document.querySelector('[data-staff-nav-loading]');
            const observer = new MutationObserver(() => {
                if (!overlay.hidden && overlay.firstElementChild?.dataset.skeletonLayout === expected) {
                    observer.disconnect();
                    resolve(true);
                }
            });
            observer.observe(overlay, { attributes: true, childList: true });
            setTimeout(() => { observer.disconnect(); resolve(false); }, 5000);
        }), layout);
        const click = link.click({noWaitAfter: true});
        try { expect(await shown).toBe(true); }
        finally { release(); await click; }
        await expect(page).toHaveURL(new RegExp(path + '$'));
    }
});
