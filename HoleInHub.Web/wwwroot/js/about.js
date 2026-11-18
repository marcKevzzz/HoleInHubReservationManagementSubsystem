(() => {
    const viewport = document.querySelector('.about-story-window');
    const { gsap, ScrollTrigger } = window;
    if (!viewport || !gsap || !ScrollTrigger) return;

    const track = viewport.querySelector('.about-stories');
    const panels = [...track.children];

    gsap.matchMedia().add('(min-width: 801px) and (min-height: 600px) and (prefers-reduced-motion: no-preference)', () => {
        viewport.classList.add('about-scroll-active');
        const distance = () => track.scrollWidth - viewport.clientWidth;
        const story = gsap.timeline({
            defaults: { ease: 'none' },
            scrollTrigger: {
                trigger: viewport,
                start: 'top top',
                end: () => '+=' + distance(),
                pin: true,
                scrub: 0.35,
                snap: { snapTo: 1 / (panels.length - 1), duration: { min: 0.15, max: 0.45 } },
                invalidateOnRefresh: true,
            },
        });

        story.to(track, { x: () => -distance(), duration: panels.length - 1 }, 0);
        panels.slice(1).forEach((panel, index) => {
            const heading = panel.querySelector('h2');
            const description = panel.querySelector('.about-panel-copy p');
            const support = panel.querySelector('.about-support-photo');
            if (support) {
                const primary = panel.querySelector(':scope > img');
                const openFrame = 'inset(0% 0% 0% 0%)';
                story
                    .fromTo(primary, { xPercent: -9, clipPath: 'inset(0% 32% 0% 9%)' },
                        { xPercent: 0, clipPath: openFrame, duration: 1 }, index)
                    .fromTo(heading, { x: 28 }, { x: 0, duration: 0.72, ease: 'power2.out' }, index + 0.16)
                    .fromTo(description, { x: 16 }, { x: 0, duration: 0.64, ease: 'power2.out' }, index + 0.3)
                    .fromTo(support, { xPercent: -15, clipPath: 'inset(0% 46% 0% 15%)' },
                        { xPercent: 0, clipPath: openFrame, duration: 0.72 }, index + 0.28);
                return;
            }
            story
                .fromTo(panel.querySelector('img'), { scale: 1.08, xPercent: 2 }, { scale: 1, xPercent: 0, duration: 1 }, index)
                .fromTo(heading, { y: 32, opacity: 0.25 }, { y: 0, opacity: 1, duration: 0.55, ease: 'power3.out' }, index + 0.25)
                .fromTo(description, { y: 16, opacity: 0.25 }, { y: 0, opacity: 1, duration: 0.5, ease: 'power3.out' }, index + 0.45);
        });

        function revealFocusedPanel(event) {
            const index = panels.indexOf(event.target.closest('.about-panel'));
            if (index < 0) return;
            viewport.scrollLeft = 0;
            const trigger = story.scrollTrigger;
            const progress = index / (panels.length - 1);
            trigger.scroll(trigger.start + (trigger.end - trigger.start) * progress);
            ScrollTrigger.update();
            trigger.getTween()?.progress(1);
        }

        function refreshAfterImageLoad() {
            ScrollTrigger.refresh(true);
        }

        viewport.addEventListener('focusin', revealFocusedPanel);
        viewport.addEventListener('load', refreshAfterImageLoad, true);
        return () => {
            viewport.classList.remove('about-scroll-active');
            track.style.removeProperty('transform');
            viewport.removeEventListener('focusin', revealFocusedPanel);
            viewport.removeEventListener('load', refreshAfterImageLoad, true);
        };
    });
})();
