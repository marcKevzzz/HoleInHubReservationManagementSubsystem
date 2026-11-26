(() => {
    const dialog = document.querySelector('#booking-media-dialog');
    if (!dialog) return;
    const stage = dialog.querySelector('[data-media-stage]');
    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
    let closing = false;
    function closeGallery() {
        if (closing || !dialog.open) return;
        if (reducedMotion.matches) { dialog.close(); return; }
        closing = true;
        dialog.animate([{ opacity: 1 }, { opacity: 0 }], { duration: 160, easing: 'ease-in' }).finished.then(() => { dialog.close(); closing = false; });
    }
    let media = [], index = 0;
    function render() {
        stage.querySelector('video')?.pause();
        const path = media[index];
        const video = /\.(mp4|webm)$/i.test(path);
        const element = document.createElement(video ? 'video' : 'img');
        element.src = path;
        if (video) {
            element.controls = true;
            element.preload = 'metadata';
            element.muted = true;
            element.playsInline = true;
        }
        else element.alt = dialog.dataset.title || 'Venue photo';
        stage.replaceChildren(element);
        if (video && !reducedMotion.matches) {
            // Controls remain usable if browser policy prevents automatic playback.
            element.play().catch(() => {});
        }
        const strip = dialog.querySelector('.media-thumbnails');
        strip.replaceChildren();
        if (media.length > 1) media.forEach((src, position) => {
            const button = document.createElement('button');
            button.type = 'button';
            button.setAttribute('aria-label', 'View media ' + (position + 1));
            button.setAttribute('aria-current', String(position === index));
            const isVideo = /\.(mp4|webm)$/i.test(src);
            const thumb = document.createElement('img');
            thumb.src = isVideo ? src.replace(/\.(mp4|webm)$/i, '.webp') : src;
            thumb.alt = '';
            if (isVideo) {
                button.classList.add('media-video-thumbnail');
                button.setAttribute('aria-label', 'Play video ' + (position + 1));
                thumb.addEventListener('error', () => thumb.remove(), { once: true });
            }
            button.append(thumb);
            button.onclick = () => { index = position; render(); strip.children[position].focus(); };
            strip.append(button);
        });
        dialog.querySelector('[data-media-count]').textContent = `${index + 1} / ${media.length}`;
        dialog.querySelectorAll('[data-media-prev], [data-media-next]').forEach(button => button.hidden = media.length < 2);
    }
    document.addEventListener('click', event => {
        const link = event.target.closest('[data-booking-media]');
        if (!link) return;
        const owner = link.closest('[data-media]');
        media = JSON.parse(owner?.dataset.media || '[]').filter(path => typeof path === 'string' && path.startsWith('/media/') && !path.includes('..') && !path.includes('\\'));
        if (!media.length) return;
        event.preventDefault(); event.stopPropagation();
        index = 0;
        dialog.dataset.title = owner.querySelector('strong, h3')?.textContent || 'Venue';
        render(); dialog.showModal();
        if (!reducedMotion.matches) dialog.animate([{ opacity: 0 }, { opacity: 1 }], { duration: 200, easing: 'ease-out' });
    });
    function move(direction) { index = (index + direction + media.length) % media.length; render(); }
    dialog.querySelector('[data-media-prev]').addEventListener('click', () => move(-1));
    dialog.querySelector('[data-media-next]').addEventListener('click', () => move(1));
    dialog.querySelector('[data-media-close]').addEventListener('click', closeGallery);
    dialog.addEventListener('click', event => { if (!event.target.closest('button,img,video')) closeGallery(); });
    dialog.addEventListener('cancel', event => { event.preventDefault(); closeGallery(); });
    dialog.addEventListener('close', () => { stage.querySelector('video')?.pause(); stage.replaceChildren(); });
    dialog.addEventListener('keydown', event => {
        if (event.target.matches('video')) return;
        if (event.key === 'ArrowLeft') { event.preventDefault(); move(-1); }
        if (event.key === 'ArrowRight') { event.preventDefault(); move(1); }
    });
})();
