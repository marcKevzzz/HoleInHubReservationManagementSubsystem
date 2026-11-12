// Shared by home, reservation, and account controls so their motion stays identical.
(() => {
    const active = new WeakMap();
    const mounted = new WeakMap();
    const selector = '.button, .flow-back, button.staff-hover-link';

    function prepare(button, left = false) {
        if (button.querySelector('[data-button-label]')) return;
        const text = button.textContent.replace(/[→←]/g, '').trim();
        button.setAttribute('data-button-ready', '');
        button.setAttribute('aria-label', button.getAttribute('aria-label') || text);
        button.replaceChildren();
        const label = document.createElement('span');
        label.className = 'button-label';
        label.dataset.buttonLabel = 'true';
        for (const key of ['a', 'b']) {
            const row = document.createElement('span');
            row.className = 'button-label-' + key;
            row.dataset[key === 'a' ? 'buttonLabelA' : 'buttonLabelB'] = 'true';
            if (key === 'b') row.setAttribute('aria-hidden', 'true');
            for (const ch of text) {
                const char = document.createElement('span');
                char.className = 'button-motion-char';
                char.textContent = ch === ' ' ? '\u00a0' : ch;
                row.append(char);
            }
            label.append(row);
        }
        button.append(label);
        if (button.hasAttribute('data-no-icon') && !button.closest?.('.staff-shell')) return;
        const icon = document.createElement('span');
        icon.className = left ? 'button-icon button-icon-left' : 'button-icon';
        icon.setAttribute('aria-hidden', 'true');
        icon.innerHTML = '<svg class="button-icon-a" viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="1.2"><path d="M4 12h15M13 6l6 6-6 6"/></svg><svg class="button-icon-b" viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="1.2"><path d="M4 12h15M13 6l6 6-6 6"/></svg>';
        if (left) {
            icon.querySelectorAll('path').forEach(path => path.setAttribute('d', 'M20 12H5M11 6l-6 6 6 6'));
            button.prepend(icon);
        } else button.append(icon);
    }
    function animate(button) {
        if (!button || !window.gsap || matchMedia('(prefers-reduced-motion: reduce)').matches || active.has(button)) return () => {};
        const labelA = button.querySelector('[data-button-label-a], .button-label-a');
        const labelB = button.querySelector('[data-button-label-b], .button-label-b');
        const iconA = button.querySelector('[data-button-icon-a], .button-icon-a');
        const iconB = button.querySelector('[data-button-icon-b], .button-icon-b');

        if (!labelA || !labelB) return () => {};

        const charsA = labelA.querySelectorAll('.button-motion-char');
        const charsB = labelB.querySelectorAll('.button-motion-char');

        const tl = gsap.timeline({ paused: true, defaults: { ease: 'power2.inOut' } });

        if (charsA.length && charsB.length) {
            gsap.set(charsA, { yPercent: 0 });
            gsap.set(charsB, { yPercent: 125 });
            gsap.set(labelB, { opacity: 0, visibility: 'hidden' });

            tl.to(labelB, { opacity: 1, visibility: 'visible', duration: 0.02 }, 0);
            tl.to(charsA, { yPercent: -125, duration: 0.32, stagger: 0.015 }, 0);
            tl.to(charsB, { yPercent: 0, duration: 0.32, stagger: 0.015 }, 0.03);
        } else {
            gsap.set(labelA, { yPercent: 0 });
            gsap.set(labelB, { yPercent: 125, opacity: 0, visibility: 'hidden' });

            tl.to(labelB, { opacity: 1, visibility: 'visible', duration: 0.02 }, 0);
            tl.to(labelA, { yPercent: -125, duration: 0.32 }, 0);
            tl.to(labelB, { yPercent: 0, duration: 0.32 }, 0.03);
        }

        if (iconA && iconB) {
            const isLeft = button.querySelector('.button-icon-left');
            if (isLeft) {
                gsap.set(iconA, { autoAlpha: 1, x: 0 });
                gsap.set(iconB, { autoAlpha: 0, x: 28 });
                tl.to(iconA, { autoAlpha: 0, x: -28, duration: 0.3 }, 0);
                tl.to(iconB, { autoAlpha: 1, x: 0, duration: 0.3 }, 0.04);
            } else {
                gsap.set(iconA, { autoAlpha: 1, x: 0 });
                gsap.set(iconB, { autoAlpha: 0, x: -28 });
                tl.to(iconA, { autoAlpha: 0, x: 28, duration: 0.3 }, 0);
                tl.to(iconB, { autoAlpha: 1, x: 0, duration: 0.3 }, 0.04);
            }
        }

        const enter = () => { if (!button.disabled && button.getAttribute?.('aria-disabled') !== 'true') tl.play(); };
        const leave = () => tl.reverse();

        button.addEventListener('pointerenter', enter);
        button.addEventListener('pointerleave', leave);
        button.addEventListener('focus', enter);
        button.addEventListener('blur', leave);

        const dispose = () => {
            active.delete(button);
            button.removeEventListener('pointerenter', enter);
            button.removeEventListener('pointerleave', leave);
            button.removeEventListener('focus', enter);
            button.removeEventListener('blur', leave);
            tl.kill();
            if (charsA.length && charsB.length) {
                gsap.set([...charsA, ...charsB], { clearProps: 'transform' });
            } else {
                gsap.set([labelA, labelB], { clearProps: 'transform' });
            }
            gsap.set(labelB, { clearProps: 'opacity,visibility' });
            if (iconA && iconB) {
                gsap.set([iconA, iconB], { clearProps: 'transform,opacity,visibility' });
            }
        };
        active.set(button, dispose);
        return dispose;
    }


    function mount(root = document) {
        if (mounted.has(root) || !window.gsap) return () => {};
        const buttons = [...root.querySelectorAll(selector)];
        if (root.matches?.(selector)) buttons.unshift(root);
        buttons.forEach(button => prepare(button, button.matches('.flow-back, [data-icon-left]')));
        const media = gsap.matchMedia();
        media.add('(prefers-reduced-motion: no-preference)', () => {
            const cleanup = buttons.map(animate);
            return () => cleanup.forEach(dispose => dispose());
        });
        const dispose = () => { media.revert(); mounted.delete(root); };
        mounted.set(root, dispose);
        return dispose;
    }

    function setLabel(button, text) {
        active.get(button)?.();
        button.textContent = text;
        button.setAttribute('aria-label', text);
        prepare(button, button.matches('.flow-back, [data-icon-left]'));
        animate(button);
    }
    window.HoleInHubButtons = { prepare, animate, mount, setLabel };
    function start() {
        const dispose = mount(document);
        window.addEventListener('pagehide', event => { if (!event.persisted) dispose(); });
    }

    if (typeof document !== 'undefined') {
        if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true });
        else start();
    }
})();
