(() => {
    const wrapper = document.querySelector('.smooth-wrapper');
    const content = wrapper?.querySelector('.smooth-content');
    if (!content || !window.gsap || !window.ScrollTrigger || !window.ScrollSmoother) return;

    gsap.registerPlugin(ScrollTrigger, ScrollSmoother);
    const media = gsap.matchMedia();
    const minimumWidth = content.querySelector('.about-story-window') ? 801 : 701;
    media.add(`(min-width: ${minimumWidth}px) and (prefers-reduced-motion: no-preference)`, () => {
        const smoother = ScrollSmoother.create({ wrapper, content, smooth: .9, effects: true });
        if (document.querySelector('dialog:modal, #site-menu[open]')) smoother.paused(true, true);
        return () => smoother.kill();
    });
    window.addEventListener('pagehide', event => { if (!event.persisted) media.revert(); });
})();
