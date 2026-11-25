const {test, expect} = require('@playwright/test');
const {signInManager,verificationCode} = require('./fixture.cjs');

for (const packageBooking of [false, true]) {
test(packageBooking ? 'book two children into a four-date lesson package' : 'a scheduled class alone cannot be booked', async ({page, browser, request}, info) => {
    await signInManager(page);
    await page.goto('/admin/services');
    await page.locator('[data-service-id="KpgaGroup"]').getByRole('link',{name:'Edit service'}).click();
    if (packageBooking) {
        await page.getByLabel(/Junior program/).check();
        await page.getByRole('button', {name:'Save service'}).click();
        await expect(page).toHaveURL(/admin\/services$/);
        await page.locator('[data-service-id="KpgaGroup"]').getByRole('link',{name:'Edit service'}).click();
    }
    await page.getByRole("link", {name:"Manage lesson schedule"}).click();
    await expect(page.getByRole("heading",{name:"Publish lesson package"})).toBeVisible();
    const day = new Date();
    day.setUTCDate(day.getUTCDate() + (packageBooking ? 7 : 0) + 14 + (info.project.name === 'mobile' ? 42 : 0));
    const weekday = 3;
    while (day.getUTCDay() !== weekday) day.setUTCDate(day.getUTCDate() + 1);
    const sessions = [];
    const priorCount = await page.locator('[name=sessions]').count();
    for (let i = 0; i < (packageBooking ? 4 : 1); i++) {
    const date = new Date(day);
    date.setUTCDate(date.getUTCDate() + 7*i);
    await page.locator('#Date').fill(date.toISOString().slice(0,10));
    await page.locator('.staff-create-bays label').first().click();
    await page.locator('#StartTime').fill('13:30');
    await page.locator('#EndTime').fill('15:00');
    await page.locator('#Capacity').fill('3');
    await page.getByRole('button',{name:'Add class date'}).click();
    const session = page.locator('[name=sessions]').nth(priorCount+i);
    await expect(session).toBeAttached();
    sessions.push(await session.inputValue());
    }
    await page.getByRole('link', {name:'Edit class'}).last().click();
    await expect(page.locator('#Capacity')).toHaveValue('3');
    await expect(page.locator('#StartTime')).toHaveValue('13:30');
    await page.getByRole('button', {name:'Save class changes'}).click();
    await expect(page.getByRole('button', {name:'Add class date'})).toBeVisible();
    if (packageBooking) {
        await page.goto('/admin/services/KpgaGroup/packages');
        for (const session of sessions) await page.locator('[name=sessions][value="'+session+'"]').check();
        await page.locator('#registration-deadline').fill(new Date(day.getTime()-86400000).toISOString().slice(0,10));
        await page.getByRole('button',{name:'Publish package'}).click();
        await expect(page.getByRole('heading',{name:'Published package'}).last()).toBeVisible();
        await page.goto('/staff/group-classes/'+sessions[0]+'/roster');
    }
    if (!packageBooking) {
        await page.goto('/Reservations/Create');
        await expect(page.locator('input[data-program="'+sessions[0]+'"]')).toHaveCount(0);
        const response = await page.request.get('/staff/group-classes/'+sessions[0]+'/enroll');
        expect(response.status()).toBe(404);
        return;
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
        await guest.locator('#ContactAge').fill('30');
        await guest.locator('#ContactAddress').fill('Quezon City');
        if (await guest.locator('#JuniorRelationship').isVisible()) await guest.locator('#JuniorRelationship').fill('Parent');
        await guest.locator('#LastName').fill('Guest');
        await guest.locator('#Phone').fill('09123456789');
        await guest.locator('#Email').fill(email);
        for (const [i,name] of ['Alex','Sam'].entries()) {
            await guest.locator('#learner-first-'+i).fill(name);
            await guest.locator('#learner-last-'+i).fill('Cruz');
            await guest.locator('#age-'+i).fill('12');
        }
        await expect(guest.locator('#age-0')).toHaveAttribute('max','17');
        await expect(guest.getByLabel("I'm participating").first()).toBeHidden();
        await expect(guest.locator('.booking-contact > legend')).toHaveText('Parent or guardian details');

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

test('new Korean lesson program continues directly to scheduling', async ({ page }) => {
    await signInManager(page);
    await page.goto('/admin/services/new');
    await expect(page.getByRole('heading', {name:'Lesson dates and enrollment'})).toBeHidden();
    await page.locator('#BookingService').selectOption('KpgaGroup');
    await expect(page.getByRole('heading', {name:'Lesson dates and enrollment'})).toBeVisible();
    await page.locator('#Title').fill('Junior lesson program');
    await page.locator('#Summary').fill('Four Tuesday lessons.');
    await page.locator('#Uploads').setInputFiles(require.resolve('../wwwroot/media/facebook/kpga-lessons.webp'));
    await page.locator('#service-pricing > summary').click();
    await page.locator('#RateAmount').fill('2500');
    await page.locator('#ClassCount').fill('4');
    await page.getByRole('button', {name:/and schedule dates/}).click();
    await expect(page.getByRole('heading', {name:'Junior lesson program'})).toBeVisible();
    await expect(page.locator('#Date')).toBeVisible();
    await expect(page.getByRole('button', {name:'Publish package'})).toBeDisabled();
});
