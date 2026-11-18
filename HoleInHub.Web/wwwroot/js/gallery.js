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
    const sections = gallery.querySelectorAll('[data-gallery-category]');
    const cards = gallery.querySelectorAll('[data-gallery-story]');
    const categoryGrids = [...gallery.querySelectorAll('[data-gallery-category] .gallery-masonry')]
        .map(grid => ({ grid, cards: [...grid.children] }));
    const allGrid = document.createElement('div');
    allGrid.className = 'gallery-group gallery-masonry gallery-all';
    filters.after(allGrid);

    function showCategory(category) {
        const showAll = category === 'All';
        allGrid.hidden = !showAll;
        sections.forEach(section => {
            section.hidden = showAll || section.dataset.galleryCategory !== category;
        });
        categoryGrids.forEach(({ grid, cards }) => {
            (showAll ? allGrid : grid).append(...cards);
        });
    }

    showCategory('All');
    const preference = window.matchMedia('(prefers-reduced-motion: reduce)');
    let opener, timeline, closing = false;
    const bounds = element => {
        const { top, left, width, height } = element.getBoundingClientRect();
        return { top, left, width, height };
    };

    function fitMasonryCard(card) {
        const height = card.getBoundingClientRect().height;
        if (height) {
            const gap = parseFloat(getComputedStyle(card).marginBottom);
            // Each implicit grid row is one pixel, including the space below the card.
            card.style.gridRowEnd = `span ${Math.ceil(height + gap)}`;
        }
    }

    if (window.ResizeObserver) {
        gallery.querySelectorAll('.gallery-masonry').forEach(grid => grid.classList.add('is-packed'));
        const observer = new ResizeObserver(entries => entries.forEach(({ target }) => fitMasonryCard(target)));
        cards.forEach(card => {
            fitMasonryCard(card);
            observer.observe(card);
        });
    }

    filters.hidden = false;
    const tabs = [...filters.querySelectorAll('[data-gallery-filter]')];
    let hoveredTab;

    function positionIndicator() {
        const tab = hoveredTab ?? filters.querySelector('[aria-pressed="true"]');
        const { left, width } = tab.getBoundingClientRect();
        const x = left - filters.getBoundingClientRect().left + filters.scrollLeft;
        filters.style.setProperty('--gallery-indicator-x', `${x}px`);
        filters.style.setProperty('--gallery-indicator-width', `${width}px`);
    }

    filters.addEventListener('pointerover', event => {
        if (event.pointerType === 'touch') return;
        hoveredTab = event.target.closest('[data-gallery-filter]');
        positionIndicator();
    });
    filters.addEventListener('pointerleave', () => {
        hoveredTab = null;
        positionIndicator();
    });
    filters.addEventListener('scroll', () => {
        filters.style.setProperty('--gallery-filters-scroll', `${filters.scrollLeft}px`);
    }, { passive: true });
    const indicatorObserver = new ResizeObserver(positionIndicator);
    [filters, ...tabs].forEach(tab => indicatorObserver.observe(tab));
    positionIndicator();

    filters.addEventListener('click', event => {
        const filter = event.target.closest('[data-gallery-filter]');
        if (!filter) return;
        const category = filter.dataset.galleryFilter;
        if (filter.getAttribute('aria-pressed') === 'true') return;
        showCategory(category);
        filters.querySelector('[aria-pressed="true"]').setAttribute('aria-pressed', 'false');
        filter.setAttribute('aria-pressed', 'true');
        positionIndicator();
        if (window.ResizeObserver) cards.forEach(fitMasonryCard);
        window.ScrollTrigger?.refresh();

        const images = gallery.querySelectorAll('.gallery-group:not([hidden]) .gallery-frame');
        window.HoleInHubMotion?.enter(images, { y: 16, opacity: 1 });
    });

    gallery.addEventListener('click', event => {
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
