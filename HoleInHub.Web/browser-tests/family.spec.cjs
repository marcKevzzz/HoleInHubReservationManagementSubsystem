const {test, expect} = require('@playwright/test');
const {signInManager,verificationCode} = require('./fixture.cjs');

for (const packageBooking of [false, true]) {
test(packageBooking ? 'book two children into a four-date lesson package' : 'publish a shared class and prepare a two-child roster', async ({page, browser, request}, info) => {
    await signInManager(page);
    await page.goto('/admin/services');
    await page.locator('[data-service-id="KpgaGroup"]').getByRole('link',{name:'Publish a class'}).click();
    const day = new Date();
    day.setUTCDate(day.getUTCDate() + (packageBooking ? 7 : 0) + 14 + (info.project.name === 'mobile' ? 42 : 0));
    const weekday = 2;
    while (day.getUTCDay() !== weekday) day.setUTCDate(day.getUTCDate() + 1);
    const sessions = [];
    for (let i = 0; i < (packageBooking ? 4 : 1); i++) {
    if (i) await page.goto('/staff/group-classes/publish?offering=KpgaGroup');
    const date = new Date(day);
    date.setUTCDate(date.getUTCDate() + 7*i);
    await page.locator('#Date').fill(date.toISOString().slice(0,10));
    await page.locator('[name=BayIds]').first().check();
    await page.locator('#Capacity').fill('3');
    await page.locator('#Price').fill('500');
    await page.getByRole('button',{name:'Publish class'}).click();
    await expect(page.getByRole('heading',{name:'Registered learners'})).toBeVisible();
    sessions.push(page.url().split('/').at(-2));
    }
    if (packageBooking) {
        await page.goto('/admin/services/KpgaGroup/packages');
        for (const session of sessions) await page.locator('[name=sessions][value="'+session+'"]').check();
        await page.locator('#registration-deadline').fill(new Date(day.getTime()-86400000).toISOString().slice(0,10));
        await page.getByRole('button',{name:'Publish package'}).click();
        await expect(page.getByRole('heading',{name:/PKG-/}).last()).toBeVisible();
        await page.goto('/staff/group-classes/'+sessions[0]+'/roster');
    }
    const guest = await browser.newPage({...info.project.use, baseURL: info.project.use.baseURL, reducedMotion: 'reduce', ignoreHTTPSErrors:true});
    try {

        await guest.goto('/Reservations/Create');
        await guest.locator('#booking-browse').click();
        const experience = packageBooking ? '.service-option:has(input[id^="service-PKG-"])' : '.service-option:has(input[id^="service-CLS-"])';
        const choice = guest.locator(experience).locator(`input[type=radio][data-dates^="${day.toISOString().slice(0,10)}T"]`);
        await choice.focus();
        await choice.press('Space');
        await expect(choice).toBeChecked();
        await guest.locator('#step-next').click();
        await expect(guest.locator('.step-tab.current')).toContainText('Date & time');
        await expect(guest.locator('#Date')).toHaveValue(day.toISOString().slice(0,10));
        await expect(guest.locator('#time-slots span')).toHaveCount(packageBooking ? 4 : 1);
        await guest.locator('#step-next').click();
        await expect(guest.locator('.booking-contact')).toBeVisible();
        await guest.locator('#step-next').click();
        await expect(guest.locator('#FirstName-feedback')).not.toBeEmpty();
        await guest.locator('#Attendees').fill('2');
        await expect(guest.locator('#learner-first-1')).toBeVisible();
        const email = 'family-' + require('node:crypto').randomUUID() + '@example.test';
        await guest.locator('#FirstName').fill('Parent');
        await guest.locator('#LastName').fill('Guest');
        await guest.locator('#Phone').fill('09123456789');
        await guest.locator('#Email').fill(email);
        for (const [i,name] of ['Alex','Sam'].entries()) {
            await guest.locator('#learner-first-'+i).fill(name);
            await guest.locator('#learner-last-'+i).fill('Cruz');
            await guest.locator('#age-'+i).fill('12');
        }
        await guest.locator('[data-contact-guardian]').first().check();
        await guest.locator('#relationship-0').fill('Parent');
        await guest.locator('#guardian-index-1').selectOption('0');

        await guest.locator('#PrivacyAccepted').focus();
        await guest.keyboard.press('Space');
        {
            await guest.locator('#step-next').click();
            await expect(guest.locator('#review-selection')).toContainText(packageBooking ? '5,000' : '1,000');
            await expect(guest.locator('#review-selection')).toContainText('Alex Cruz, Sam Cruz');
        }
        await guest.getByRole('button',{name:'Submit reservation'}).click();
        await expect(guest.getByLabel('Verification code')).toBeVisible();
        await guest.getByLabel('Verification code').fill(await verificationCode(request,email));
        await guest.locator('.verification-actions button[type=submit]').first().click();
        await expect(guest.locator('.booking-reference')).toContainText('HIH-');
        await page.reload();
        await expect(page.getByRole('rowheader',{name:'Alex Cruz'})).toBeVisible();
        await expect(page.getByRole('rowheader',{name:'Sam Cruz'})).toBeVisible();
        if (!packageBooking) {
        await guest.getByRole('button',{name:'Notify me about the next class'}).click();
        await guest.getByRole('button',{name:'Turn off reminder'}).click();
        await expect(guest.getByRole('button',{name:'Notify me about the next class'})).toBeVisible();
        }
        const alex = guest.locator('.family-learner-detail').filter({has:guest.getByRole('heading',{name:'Alex Cruz',exact:true})});
        if (packageBooking) {
            await alex.getByRole('button',{name:/Request absence/}).first().click();
            await page.reload();
            await page.getByRole('button',{name:'Approve absence'}).click();
            await page.getByRole('dialog').getByRole('button',{name:'Confirm',exact:true}).click();
            await expect(page.locator('.lesson-facts').getByText(/1 of 3/)).toBeVisible();
            for (const session of sessions.slice(1)) {
                await page.goto('/staff/group-classes/'+session+'/roster');
                await expect(page.locator('.lesson-facts').getByText(/2 of 3/)).toBeVisible();
            }
            return;
        }
        await alex.locator('summary').click();
        await alex.getByRole('button',{name:'Confirm seat cancellation'}).click();
        await expect(alex).toContainText('Cancelled');
        await page.reload();
        await expect(page.getByRole('row').filter({has:page.getByRole('rowheader',{name:'Sam Cruz',exact:true})})).toContainText('Enrolled');
        await expect(page.locator('.lesson-facts').getByText(/1 of 3/)).toBeVisible();
    } finally { await guest.close(); }
});
}
