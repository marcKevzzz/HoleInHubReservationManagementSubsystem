window.HoleInHubImageViewer = function (viewer) {
    const content = viewer.querySelector('[data-viewer-content]');
    const dismiss = viewer.querySelector('[data-viewer-close]');
    const canAnimate = () => window.gsap && !window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    let motion;
    function close() {
        if (!viewer.open) return;
        motion?.kill();
        if (canAnimate()) {
            motion = window.gsap.to(content, {opacity:0, scale:.96, duration:.2, ease:'power2.in', onComplete:()=>viewer.close()});
        } else viewer.close();
    }
    function show(figures) {
        if (!figures.length || viewer.open) return;
        motion?.kill();
        window.gsap?.set(content, {clearProps:'all'});
        content.replaceChildren(...figures);
        figures[0].append(dismiss);
        viewer.showModal();
        if (canAnimate()) {
            motion = window.gsap.fromTo(content, {opacity:0, scale:.94, y:12, transformOrigin:'center'},
                {opacity:1, scale:1, y:0, duration:.36, ease:'power3.out', clearProps:'all'});
        }
    }
    dismiss.addEventListener('click', close);
    viewer.addEventListener('click', event => { if (event.target === viewer) close(); });
    viewer.addEventListener('cancel', event => { event.preventDefault(); close(); });
    return { show, close };
};

window.HoleInHubEditorGallery = function (form, notify, shake) {
    const gallery = form.querySelector('[data-service-gallery]');
    const add = form.querySelector('[data-image-add]');
    const input = form.querySelector('[data-service-uploads]');
    const imageError = form.querySelector('[data-images-error]');
    const viewer = document.querySelector('[data-image-viewer]');
    const imageViewer = window.HoleInHubImageViewer(viewer);
    const details = form.querySelector('.service-detail-fields');
    if (details && typeof ResizeObserver === 'function') {
        const observer = new ResizeObserver(() => {
            if (window.innerWidth <= 760) { gallery.style.removeProperty('--service-gallery-height'); return; }
            const optionalHeight = [...form.querySelectorAll('[data-deposit-value], [data-new-type]')].reduce((sum, element) => {
                if (!element || element.hidden) return sum;
                const gap = element.parentElement ? parseFloat(getComputedStyle(element.parentElement).rowGap) || 0 : 0;
                return sum + element.getBoundingClientRect().height + gap;
            }, 0);
            const height = details.getBoundingClientRect().bottom - gallery.getBoundingClientRect().top - optionalHeight;
            gallery.style.setProperty('--service-gallery-height', Math.max(280, height) + 'px');
        });
        observer.observe(details);
    }
    const icon = name => name === 'delete' ? '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" aria-hidden="true"><path d="M4 7h16M9 7V4h6v3M6 7l1 13h10l1-13M10 10v7m4-7v7"/></svg>' : '';
    let items = [...gallery.querySelectorAll('[data-service-image]')].map(row => ({ path: row.dataset.path, src: row.querySelector('img').src, caption: row.querySelector('figcaption')?.textContent }));
    let dragged = -1;
    function move(from, to) {
        items.splice(to, 0, items.splice(from, 1)[0]); render();
    }
    function showImages(index, all) {
        const figures = (all ? items : [items[index]]).map((item, offset) => {
            const figure = document.createElement('figure');
            const img = document.createElement('img'); img.src = item.src; img.alt = 'Gallery photo';
            const primary = document.createElement('button'); primary.type = 'button'; primary.className = 'service-viewer-primary'; primary.textContent = 'Set as primary';
            primary.addEventListener('click', () => { move(all ? offset : index, 0); imageViewer.close(); });
            figure.append(img, primary); addImageId(figure, item);
            return figure;
        });
        const image = figures[0]?.querySelector('img');
        if (image?.decode) image.decode().then(() => imageViewer.show(figures), () => imageViewer.show(figures));
        else imageViewer.show(figures);
    }
    function addImageId(figure, item) {
        if (!item.caption) return;
        const caption = document.createElement('figcaption');
        caption.className = 'service-image-id';
        caption.textContent = item.caption;
        figure.append(caption);
    }
    function render() {
        gallery.replaceChildren();
        const transfer = new DataTransfer();
        const limit = !gallery.dataset.fallback && items.length > 5 ? 4 : items.length;
        gallery.dataset.count = String(limit);
        gallery.style.setProperty('--gallery-rows', String(Math.max(1, limit)));
        items.forEach((item, index) => {
            const order = document.createElement('input'); order.type = 'hidden'; order.name = 'ImageOrder';
            if (item.file) { order.value = 'upload:' + transfer.files.length; transfer.items.add(item.file); }
            else { order.value = item.path; const retained = document.createElement('input'); retained.type = 'hidden'; retained.name = 'Images'; retained.value = item.path; gallery.append(retained); }
            gallery.append(order);
            if (index >= limit) return;
            const tile = document.createElement('figure'); tile.className = 'service-image-tile'; tile.draggable = true;
            const open = document.createElement('button'); open.type = 'button'; open.className = 'service-image-open'; open.setAttribute('aria-label', 'Enlarge image ' + (index + 1));
            const image = document.createElement('img'); image.src = item.src; image.alt = 'Gallery photo ' + (index + 1); open.append(image);
            const overflow = !gallery.dataset.fallback && items.length > 5 && index === 3;
            if (overflow) { const more = document.createElement('span'); more.className = 'service-image-more'; more.textContent = '+' + (items.length - 4); open.append(more); open.setAttribute('aria-label', 'Show all ' + items.length + ' images'); }
            open.addEventListener('click', () => showImages(index, overflow));
            const remove = document.createElement('button'); remove.type = 'button'; remove.className = 'service-image-delete'; remove.innerHTML = icon('delete'); remove.setAttribute('aria-label', 'Delete image ' + (index + 1));
            remove.addEventListener('click', () => { if (item.file) URL.revokeObjectURL(item.src); items.splice(index, 1); render(); });
            const primary = document.createElement('button'); primary.type = 'button'; primary.className = 'service-image-primary'; primary.textContent = index === 0 ? 'Primary' : 'Set primary'; primary.disabled = index === 0;
            primary.addEventListener('click', () => move(index, 0));
            tile.append(open, remove, primary);
            addImageId(tile, item);
            tile.addEventListener('dragstart', event => { dragged = index; event.dataTransfer.effectAllowed = 'move'; event.dataTransfer.setData('text/plain', String(index)); });
            tile.addEventListener('dragover', event => { if (dragged >= 0) { event.preventDefault(); event.dataTransfer.dropEffect = 'move'; } });
            tile.addEventListener('drop', event => { event.preventDefault(); if (dragged >= 0) move(dragged, index); dragged = -1; });
            tile.addEventListener('dragend', () => { dragged = -1; });
            tile.addEventListener('keydown', event => { if (event.altKey && ['ArrowLeft','ArrowRight'].includes(event.key)) { event.preventDefault(); move(index, Math.max(0, Math.min(items.length - 1, index + (event.key === 'ArrowLeft' ? -1 : 1)))); } });
            tile.title = 'Drag to reorder · Alt + Left or Right to reorder with keyboard';
            gallery.append(tile);
        });
        if (!items.length && gallery.dataset.fallback) {
            const cover = document.createElement('img');
            cover.src = gallery.dataset.fallback; cover.alt = 'Venue overview';
            cover.className = 'bay-default-cover'; gallery.append(cover);
        }
        input.files = transfer.files;
        gallery.append(add);
    }
    input.addEventListener('change', () => {
        const files = [...input.files];
        if (items.length + files.length > 8 || files.some(file => !['image/jpeg','image/png','image/webp'].includes(file.type) || file.size > 4194304)
            || files.concat(items.filter(item => item.file).map(item => item.file)).reduce((sum, file) => sum + file.size, 0) > 20971520) {
            imageError.textContent = 'Use up to 8 JPEG, PNG or WebP images, 4 MB each and 20 MB total.'; notify(imageError.textContent, 'Images not added');
            shake(gallery);
        } else { items.push(...files.map(file => ({ file, src: URL.createObjectURL(file) }))); imageError.textContent = ''; }
        render();
    });
    render();
    window.addEventListener('pagehide', () => items.filter(item => item.file).forEach(item => URL.revokeObjectURL(item.src)));
    return { count: () => items.length, gallery, imageError, input };
};
