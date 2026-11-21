const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const vm=require('node:vm');
const path=require('node:path');
const source=fs.readFileSync(path.join(__dirname,'../../../HoleInHub.Web/wwwroot/js/motion.js'),'utf8');
const videoSource = source.slice(source.indexOf('function initBackgroundVideo'), source.indexOf('// One context owns'));

test('decorative video follows visibility and motion preferences and releases its listeners', () => {
    let observe, change, disconnected = false;
    const preference = {
        matches: false,
        addEventListener(name, handler) { change = handler; },
        removeEventListener(name, handler) { assert.equal(change, handler); change = null; }
    };
    const attrs = new Map([['tabindex', '0']]);
    const video = {
        removeAttribute: name => attrs.delete(name),
        setAttribute: (name, value) => attrs.set(name, value),
        play() { this.paused = false; return Promise.resolve(); },
        pause() { this.paused = true; }
    };
    const context = { IntersectionObserver: class {
        constructor(callback) { observe = callback; }
        observe(element) { assert.equal(element, video); }
        disconnect() { disconnected = true; }
    } };
    vm.runInNewContext(videoSource, context);
    const cleanup = context.initBackgroundVideo(video, preference);
    assert.equal(attrs.has('tabindex'), false);
    assert.equal(attrs.get('aria-hidden'), 'true');
    observe([{ isIntersecting: true }]);
    assert.equal(video.paused, false);
    preference.matches = true;
    change();
    assert.equal(video.paused, true);
    preference.matches = false;
    change();
    assert.equal(video.paused, false);
    observe([{ isIntersecting: false }]);
    assert.equal(video.paused, true);
    cleanup();
    assert.equal(disconnected, true);
    assert.equal(change, null);
});

test('videos with native controls keep keyboard playback', () => {
    const context = {};
    vm.runInNewContext(videoSource, context);
    context.initBackgroundVideo({ controls: true })();
});

test('hero text has no scroll-linked fade-out',()=>{assert.ok(!source.includes("gsap.to('.hero-content'"));});
test('photo parallax uses the shared motion layer and headings retain entry triggers', () => {
    assert.match(source, /HoleInHubMotion\.parallax\(image,/);
    assert.match(source, /\.mosaic-copy h2/);
    assert.match(source, /document\.fonts\.ready\.then\([\s\S]*?dispose = animatePage\(root\)/);
});
