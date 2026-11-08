(() => {
    const gallery = document.querySelector('[data-gallery]');
    if (!gallery) return;
    const dialog = gallery.querySelector('dialog');
    const panel = dialog.querySelector('.gallery-story-scroll');
    const media = dialog.querySelector('.gallery-story-media');
    const image = media.querySelector('img');
    const video = media.querySelector('video');
    const morph = dialog.querySelector('.gallery-story-morph');
    const copy = dialog.querySelector('.gallery-story-copy');
    const closeButton = dialog.querySelector('.gallery-story-close');
    const filters = gallery.querySelector('.gallery-filters');
    const archive = gallery.querySelector('.gallery-archive');
    const cards = [...archive.querySelectorAll('[data-gallery-story]')];
    const preference = window.matchMedia('(prefers-reduced-motion: reduce)');
    let opener, timeline, closing = false;
    let pinMedia;
    const bounds = element => {
        const { top, left, width, height } = element.getBoundingClientRect();
        return { top, left, width, height };
    };

    function mountPins() {
        if (!window.gsap || !window.ScrollTrigger || archive.classList.contains('gallery-filtered')) return;
        pinMedia = gsap.matchMedia();
        pinMedia.add('(min-width: 701px) and (prefers-reduced-motion: no-preference)', () => {
            archive.classList.add('gallery-pinned');
            archive.querySelectorAll('.gallery-editorial').forEach(section => {
                const side = section.querySelector('.gallery-sticky');
                const stack = section.querySelector('.gallery-stack');
                ScrollTrigger.create({
                    trigger: side,
                    start: 'top 112px',
                    end: () => '+=' + Math.max(0, stack.offsetHeight - side.offsetHeight),
                    pin: true,
                    pinSpacing: false,
                    invalidateOnRefresh: true,
                });
            });
            return () => archive.classList.remove('gallery-pinned');
        });
    }

    mountPins();
    window.addEventListener('pagehide', event => { if (!event.persisted) pinMedia?.revert(); });
    filters.hidden = false;
    filters.addEventListener('click', event => {
        const filter = event.target.closest('[data-gallery-filter]');
        if (!filter) return;
        const category = filter.dataset.galleryFilter;
        if (filter.getAttribute('aria-pressed') === 'true') return;
        pinMedia?.revert();
        archive.classList.toggle('gallery-filtered', category !== 'All');
        cards.forEach(card => { card.hidden = category !== 'All' && card.dataset.category !== category; });
        filters.querySelector('[aria-pressed="true"]').setAttribute('aria-pressed', 'false');
        filter.setAttribute('aria-pressed', 'true');
        mountPins();
        window.ScrollTrigger?.refresh();
    });

    archive.addEventListener('click', event => {
        const card = event.target.closest('[data-gallery-story]');
        if (!card || event.button || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
        event.preventDefault();
        opener = card;
        const title = card.querySelector('strong').textContent;
        const clip = card.dataset.video;
        const source = card.querySelector('.gallery-frame');
        const origin = bounds(source);
        dialog.querySelector('h2').textContent = title;
        copy.querySelector('.gallery-category').textContent = card.dataset.category;
        copy.querySelector('.gallery-story-summary').textContent = card.dataset.summary;
        image.src = card.href;
        image.alt = title;
        image.hidden = Boolean(clip);
        video.hidden = !clip;
        if (clip) {
            video.src = '/media/facebook/' + clip;
            video.poster = card.href;
            video.setAttribute('aria-label', title);
        }
        dialog.showModal();
        panel.scrollTop = 0;
        window.ScrollSmoother?.get()?.paused(true, true);
        if (window.gsap && !preference.matches) {
            morph.src = card.href;
            morph.hidden = false;
            timeline = gsap.timeline({
                onComplete: () => { morph.hidden = true; },
                onReverseComplete: () => dialog.close(),
            })
                .set(media, { opacity: 0 }, 0)
                .fromTo(morph, { ...origin, opacity: 1 }, { ...bounds(media), opacity: 1, duration: .52, ease: 'power3.inOut' }, 0)
                .set(media, { opacity: 1 }, .52)
                .fromTo(panel, { opacity: 0 }, { opacity: 1, duration: .28, ease: 'power2.out' }, .26)
                .fromTo(copy, { opacity: 0, y: 22 }, { opacity: 1, y: 0, duration: .38, ease: 'power2.out' }, .30)
                .fromTo(closeButton, { opacity: 0, scale: .75 }, { opacity: 1, scale: 1, duration: .28, ease: 'back.out(1.5)' }, .36);
        }
        if (clip && !preference.matches) video.play().catch(() => {});
    });

    function close() {
        if (closing) return;
        closing = true;
        video.pause();
        if (timeline && !preference.matches && !panel.scrollTop) {
            morph.hidden = false;
            timeline.timeScale(1.35).reverse();
        } else {
            dialog.close();
        }
    }

    closeButton.addEventListener('click', close);
    dialog.addEventListener('click', event => { if (event.target === dialog) close(); });
    dialog.addEventListener('cancel', event => { event.preventDefault(); close(); });
    dialog.addEventListener('close', () => {
        timeline?.revert();
        timeline = null;
        morph.hidden = true;
        closing = false;
        if (video.hasAttribute('src')) {
            video.removeAttribute('src');
            video.load();
        }
        window.ScrollSmoother?.get()?.paused(Boolean(document.querySelector('dialog:modal, #site-menu[open]')));
        opener?.focus({ preventScroll: true });
    });
    preference.addEventListener('change', () => {
        if (!preference.matches || !dialog.open) return;
        if (closing) { dialog.close(); return; }
        timeline?.revert();
        timeline = null;
        morph.hidden = true;
        video.pause();
    });
})();
