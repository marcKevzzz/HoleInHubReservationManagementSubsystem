// Reusable motion primitives. Page-specific choreography stays in motion.js.
(() => {
    const preference = window.matchMedia('(prefers-reduced-motion: reduce)');
    const running = new Set();
    const mounted = new WeakMap();
    const available = () => Boolean(window.gsap) && !preference.matches;

    function enter(target, { y = 14, opacity = .85, duration = .35, stagger = 0 } = {}) {
        if (!target || !available()) return null;
        let tween;
        let completed = false;
        const finish = () => { completed = true; running.delete(tween); };
        tween = gsap.fromTo(target, { y, opacity }, {
            y: 0, opacity: 1, duration, stagger, ease: 'power3.out', overwrite: 'auto',
            clearProps: 'transform,opacity',
            onComplete: finish,
            onInterrupt: finish,
        });
        if (!completed) running.add(tween);
        return tween;
    }

    function reveal(target, { trigger = target, start = 'top 88%', scale = 1, y = 24, duration = .7 } = {}) {
        if (!target || !available() || !window.ScrollTrigger) return null;
        return gsap.fromTo(target, { y, scale, opacity: 0 }, {
            y: 0, scale: 1, opacity: 1, duration, ease: 'power3.out',
            scrollTrigger: { trigger, start, once: true },
        });
    }

    function parallax(target, { trigger = target, depth = 7, scrub = .8 } = {}) {
        if (!target || !available() || !window.ScrollTrigger) return null;
        return gsap.fromTo(target, { yPercent: -depth }, {
            yPercent: depth, ease: 'none',
            scrollTrigger: { trigger, start: 'top bottom', end: 'bottom top', scrub },
        });
    }

    function mount(root = document) {
        if (mounted.has(root) || !window.gsap) return () => {};
        const select = selector => [...root.querySelectorAll(selector)].filter(element => {
            const scope = element.closest?.('[data-motion-scope]');
            return !scope || scope === root;
        });
        const context = gsap.context(() => {
            const media = gsap.matchMedia();
            media.add('(prefers-reduced-motion: no-preference)', () => {
                select('[data-motion="load"]').forEach(element => enter(element));
                select('[data-motion="stagger"]').forEach(group =>
                    enter(group.children, { y: 8, opacity: .7, stagger: .07 }));
                select('[data-motion="reveal"]').forEach(element => reveal(element));
                select('[data-parallax]').forEach(frame => {
                    const depth = Number(frame.dataset.parallax || 7);
                    parallax(frame.querySelector('img,video'), {
                        trigger: frame, depth: Number.isFinite(depth) ? Math.min(18, Math.max(0, depth)) : 7,
                    });
                });
            });
            return () => media.revert();
        }, root);
        const dispose = () => { context.revert(); mounted.delete(root); };
        mounted.set(root, dispose);
        return dispose;
    }

    preference.addEventListener('change', () => {
        if (!preference.matches) return;
        [...running].forEach(tween => {
            const targets = tween.targets();
            tween.kill();
            gsap.set(targets, { clearProps: 'transform,opacity' });
        });
        running.clear();
    });
    window.HoleInHubMotion = { enter, reveal, parallax, mount };
})();
