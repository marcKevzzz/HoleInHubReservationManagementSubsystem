/**
 * Hole In Hub Luxury Motion & Interaction Engine
 * Inspired by Spyker Cars precision engineering & Apple fluid motion design.
 * Respects WCAG 2.2 AA and OS prefers-reduced-motion.
 */
(() => {
    'use strict';

    const isReducedMotion = () => {
        return window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    };

    const hasGsap = () => typeof window.gsap !== 'undefined';

    const HoleInHubMotion = {
        isReducedMotion,

        /**
         * Smooth cinematic entrance reveal with velocity-aware easing.
         */
        staggerIn(selectorOrElements, options = {}) {
            const elements = typeof selectorOrElements === 'string'
                ? document.querySelectorAll(selectorOrElements)
                : selectorOrElements;

            if (!elements || elements.length === 0) return;

            if (isReducedMotion() || !hasGsap()) {
                elements.forEach(el => {
                    el.style.opacity = '1';
                    el.style.transform = 'none';
                });
                return;
            }

            const {
                duration = 0.8,
                stagger = 0.06,
                y = 20,
                ease = 'power3.out',
                delay = 0.05
            } = options;

            window.gsap.fromTo(
                elements,
                { opacity: 0, y },
                { opacity: 1, y: 0, duration, stagger, ease, delay, clearProps: 'transform' }
            );
        },

        /**
         * Precision card tilt and elevation hover effect.
         */
        addCardHover(element) {
            if (isReducedMotion() || !hasGsap() || !element) return;

            element.addEventListener('pointerenter', () => {
                window.gsap.to(element, {
                    y: -4,
                    borderColor: 'rgba(212, 175, 55, 0.4)',
                    boxShadow: '0 12px 30px -10px rgba(0, 0, 0, 0.8), 0 0 1px 1px rgba(212, 175, 55, 0.2)',
                    duration: 0.35,
                    ease: 'power2.out'
                });
            });

            element.addEventListener('pointerleave', () => {
                window.gsap.to(element, {
                    y: 0,
                    borderColor: '',
                    boxShadow: 'none',
                    duration: 0.35,
                    ease: 'power2.inOut'
                });
            });

            // Apple design rule: instant feedback on pointer down
            element.addEventListener('pointerdown', () => {
                window.gsap.to(element, { scale: 0.985, duration: 0.1, ease: 'power1.out' });
            });

            element.addEventListener('pointerup', () => {
                window.gsap.to(element, { scale: 1.0, duration: 0.2, ease: 'power2.out' });
            });
        },

        /**
         * Automatically binds data-motion attributes across the DOM.
         */
        init() {
            if (document.readyState === 'loading') {
                document.addEventListener('DOMContentLoaded', () => this.init());
                return;
            }

            const staggerContainers = document.querySelectorAll('[data-motion="stagger"]');
            staggerContainers.forEach(container => {
                const items = container.querySelectorAll('[data-motion-item]');
                if (items.length > 0) {
                    this.staggerIn(items);
                }
            });

            const cards = document.querySelectorAll('[data-motion="hover-card"]');
            cards.forEach(card => this.addCardHover(card));
        }
    };

    window.HoleInHubMotion = HoleInHubMotion;
    HoleInHubMotion.init();
})();
