const {test,expect}=require('@playwright/test');
const {signInManager}=require('./fixture.cjs');
test('lightbox plays and stops videos and shows thumbnail previews',async({page})=>{
 await signInManager(page);await page.goto('/reservations/create');
 // Exercise the gallery independently of the wizard's current step.
 const open=()=>page.locator('[data-resource="bay-2"] [data-booking-media]').evaluate(link=>link.click());
 await open();
 const dialog=page.locator('#booking-media-dialog'),video=dialog.locator('[data-media-stage] video');
 await expect(dialog).toBeVisible();
 await expect.poll(()=>video.evaluate(v=>!v.paused && v.muted && v.currentTime>0)).toBe(true);
 const previews=dialog.locator('.media-thumbnails img');await expect(previews).toHaveCount(2);
 await expect.poll(()=>previews.evaluateAll(images=>images.every(img=>img.complete && img.naturalWidth>0))).toBe(true);
 const previous=await video.elementHandle();await dialog.locator('[data-media-next]').click();
 await expect(video).toHaveAttribute('src','/media/bays/bays-1-2-3-b.mp4');
 await expect.poll(()=>video.evaluate(v=>!v.paused && v.currentTime>0)).toBe(true);
 expect(await previous.evaluate(v=>v.paused)).toBe(true);
 await dialog.locator('.media-thumbnails button').first().click();
 await expect(video).toHaveAttribute('src','/media/bays/bays-1-2-3-a.mp4');
 await expect.poll(()=>video.evaluate(v=>!v.paused && v.currentTime>0)).toBe(true);
 const current=await video.elementHandle();await dialog.locator('[data-media-close]').click();
 await expect(dialog).not.toBeVisible();expect(await current.evaluate(v=>v.paused)).toBe(true);
 await page.emulateMedia({reducedMotion:'reduce'});await open();expect(await video.evaluate(v=>v.paused)).toBe(true);
});
