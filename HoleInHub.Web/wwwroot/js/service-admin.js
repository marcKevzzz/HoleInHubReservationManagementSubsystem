(() => {
    const notify = (message, title = 'Could not save') => window.HoleInHubToasts?.show({ kind: 'error', autoDismiss: true, title, message });
    function shake(component) {
        if (window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? true) return;
        const target = component?.closest?.('.auth-input-wrap') || component;
        target?.animate?.([
            {transform:'translateX(0)'},{transform:'translateX(-5px)'},{transform:'translateX(5px)'},
            {transform:'translateX(-3px)'},{transform:'translateX(0)'},
        ],{duration:280,easing:'ease-out'});
    }
    const dialog = document.querySelector('[data-service-confirm]');
    let pending, saving = false;
    async function save(form, buttons) {
        if (saving) return;
        saving = true;
        form.setAttribute("aria-busy", "true");
        buttons.forEach(button => button.disabled = true);
        try {
            const response = await fetch(form.action, { method: 'POST', body: new FormData(form), headers: { Accept: 'application/json' } });
            const result = await response.json();
            if (response.ok && result.redirect) { window.location.assign(result.redirect); return; }
            if (form.elements.ImpactAcknowledgement) {
                form.elements.ImpactAcknowledgement.value = result.acknowledgement || '';
            }
            if (result.fields && Object.keys(result.fields).length) {
                for (const [name, message] of Object.entries(result.fields)) {
                    const field = form.elements.namedItem(name);
                    if (!(field instanceof HTMLElement)) continue;
                    openSections(field);
                    fieldError(field, message || 'Check this value.');
                    shake(field);
                }
            }
            else shake(form.closest?.('.service-admin-row') || form);
            const summary = form.querySelector('[data-save-error]');
            if (summary) {
                summary.textContent = result.error || 'Check the form and try again.';
                for (const [name, message] of Object.entries(result.fields || {})) {
                    const field = form.elements.namedItem(name);
                    if (!(field instanceof HTMLElement) || !field.id) continue;
                    const link = document.createElement('a');
                    link.className = 'block underline';
                    link.href = `#${field.id}`;
                    link.textContent = message;
                    summary.append(link);
                }
            }
            notify(result.error || 'Check the form and try again.');
        } catch {
            shake(form.closest?.('.service-admin-row') || form);
            notify('The save could not be verified. Retry with the same details to check whether it saved.');
        }
        saving = false;
        form.removeAttribute("aria-busy");
        buttons.forEach(button => button.disabled = false);
    }
    if (dialog) {
        const cancel = dialog.querySelector('[data-service-cancel]');
        const commit = dialog.querySelector('[data-service-commit]');
        for (const form of document.querySelectorAll('[data-service-state]')) {
            form.addEventListener('submit', event => {
                if (typeof dialog.showModal !== 'function') return;
                event.preventDefault();
                if (saving) return;
                pending = form;
                const operation = form.dataset.serviceOperation;
                commit.dataset.danger = String(operation !== 'activate');
                const verb = operation === 'activate' ? 'Activate' : operation === 'delete' ? 'Delete' : 'Deactivate';
                dialog.querySelector('h2').textContent = verb + ' ' + form.dataset.serviceTitle + '?';
                dialog.querySelector('[data-service-confirm-description]').textContent = operation === 'activate'
                    ? 'This service will appear on the customer services page.'
                    : operation === 'delete' ? 'This removes the service from the catalog. Existing reservations keep their records.'
                    : 'This hides the service from the customer services page.';
                window.HoleInHubButtons?.setLabel(commit, verb + ' service');
                dialog.showModal();
            });
        }
        cancel.addEventListener('click', () => { if (!saving) dialog.close(); });
        commit.addEventListener('click', () => { if (pending) void save(pending, [commit, cancel]); });
        dialog.addEventListener('cancel', event => { if (saving) event.preventDefault(); });
        dialog.addEventListener('close', () => pending?.querySelector('button').focus({ preventScroll: true }));
    }

    const list = document.querySelector('[data-service-list]');
    if (list) {
        const reorderForm = document.querySelector('[data-service-reorder]');
        let busy = false, drag, dragFrame;
        const rows = () => [...list.querySelectorAll('[data-service-id]')];
        const number = () => rows().forEach((row, index) => row.querySelector('[data-service-position]').textContent = String(index + 1).padStart(2, '0'));
        async function reorder(row, target, beforeOrder) {
            if (busy || (!beforeOrder && (!target || row === target))) return;
            busy = true;
            const before = beforeOrder || rows();
            const forward = before.indexOf(row) < before.indexOf(target);
            if (target) list.insertBefore(row, forward ? target.nextSibling : target); number();
            if (before.every((item,index) => item === rows()[index])) { busy = false; return; }
            list.setAttribute('aria-busy', 'true');
            const controls = [...list.querySelectorAll('button,input,a')];
            controls.forEach(control => { if ('disabled' in control) control.disabled = true; });
            try {
                const body = new FormData(reorderForm);
                body.set('positions', JSON.stringify(rows().map(row => ({ id: row.dataset.serviceId, revision: Number(row.dataset.revision) }))));
                const response = await fetch(reorderForm.action, {method:'POST',body,headers:{Accept:'application/json'}});
                const result = await response.json();
                if (!response.ok) throw new Error(result.error);
                result.rows.forEach(item => { const current = rows().find(row => row.dataset.serviceId === item.id); if (!current) return; current.dataset.revision = String(item.revision); current.querySelectorAll('[name=revision]').forEach(input => input.value = item.revision); });
                window.HoleInHubToasts?.show({kind:'success',title:'Service order saved',autoDismiss:true});
            } catch { before.forEach(row => list.append(row)); number(); shake(row); notify('Reload services to verify the saved order before trying again.', 'Order could not be verified'); }
            finally { busy = false; list.removeAttribute('aria-busy'); controls.forEach(control => { if ('disabled' in control) control.disabled = false; }); row.querySelector('.service-row-grip').focus({preventScroll:true}); }
        }
        const reduced = () => window.matchMedia('(prefers-reduced-motion: reduce)').matches;
        function previewOrder() {
            if (!drag?.active) return;
            const bounds = list.getBoundingClientRect();
            drag.ghost.style.left = Math.max(8, Math.min(drag.x-drag.offsetX, innerWidth-drag.width-8))+'px';
            drag.ghost.style.top = (drag.y-drag.offsetY-6)+'px';
            const edge = drag.y < 64 ? -12 : drag.y > innerHeight-64 ? 12 : 0;
            if (edge) window.scrollBy({top:edge,behavior:'instant'});
            if (drag.x >= bounds.left && drag.x <= bounds.right) {
                const others = rows().filter(row => row !== drag.row);
                const target = others.find(row => { const rect = row.getBoundingClientRect(); return drag.y < rect.top+rect.height/2; });
                if (drag.row.nextElementSibling !== target && !(target === undefined && drag.row === rows().at(-1))) {
                    const positions = new Map(rows().map(row => [row,row.getBoundingClientRect().top]));
                    list.insertBefore(drag.row,target || null); number();
                    drag.ghost.querySelector('[data-service-position]').textContent = drag.row.querySelector('[data-service-position]').textContent;
                    if (!reduced()) rows().filter(row => row !== drag.row).forEach(row => {const delta=positions.get(row)-row.getBoundingClientRect().top; if (delta) row.animate([{transform:'translateY('+delta+'px)'},{transform:'translateY(0)'}],{duration:180,easing:'ease-out'});});
                }
            }
            dragFrame = requestAnimationFrame(previewOrder);
        }
        function endDrag(commit) {
            const current = drag; if (!current) return; drag = null;
            cancelAnimationFrame(dragFrame);
            if (list.hasPointerCapture?.(current.id)) list.releasePointerCapture(current.id);
            current.row.classList.remove('is-sorting-source'); document.documentElement.classList.remove('service-is-sorting');
            if (!current.active) return;
            if (!commit) { current.before.forEach(row => list.append(row)); number(); }
            const ghost = current.ghost, destination = current.row.getBoundingClientRect(), origin = ghost.getBoundingClientRect();
            if (!reduced()) { const flight=ghost.animate([{transform:'translate(0,0)',opacity:1},{transform:'translate('+(destination.left-origin.left)+'px,'+(destination.top-origin.top)+'px)',opacity:0}],{duration:160,easing:'ease-out'}); flight.onfinish=()=>ghost.remove(); } else ghost.remove();
            const suppress = event => {event.preventDefault();event.stopImmediatePropagation();}; document.addEventListener('click',suppress,{capture:true,once:true}); setTimeout(()=>document.removeEventListener('click',suppress,true),0);
            if (commit) void reorder(current.row,null,current.before);
        }
        list.addEventListener('pointerdown', event => {
            const row=event.target.closest('[data-service-id]'), handle=event.target.closest('.service-row-grip');
            if (!row || busy || event.button || event.isPrimary === false || (!handle && event.target.closest('a,button,input,select,textarea')) || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
            const rect=row.getBoundingClientRect();
            drag={row,id:event.pointerId,startX:event.clientX,startY:event.clientY,x:event.clientX,y:event.clientY,offsetX:event.clientX-rect.left,offsetY:event.clientY-rect.top,width:Math.min(rect.width,innerWidth-16),before:rows()};
            list.setPointerCapture(event.pointerId);
        });
        list.addEventListener('pointermove', event => {
            if (drag?.id !== event.pointerId) return; drag.x=event.clientX;drag.y=event.clientY;
            if (!drag.active && Math.hypot(drag.x-drag.startX,drag.y-drag.startY)>=6) {
                event.preventDefault(); drag.active=true; window.getSelection()?.removeAllRanges();
                drag.ghost=drag.row.cloneNode(true);drag.ghost.classList.add('service-row-floating');drag.ghost.setAttribute('aria-hidden','true');drag.ghost.inert=true;
                const rowStyle=getComputedStyle(drag.row);
                drag.ghost.style.width=drag.width+'px';
                for (const property of ['gridTemplateColumns','gap','padding','borderWidth','boxSizing']) drag.ghost.style[property]=rowStyle[property];
                [...drag.row.children].forEach((child,index)=>{const style=getComputedStyle(child),copy=drag.ghost.children[index];if(copy){copy.style.gridColumn=style.gridColumn;copy.style.gridRow=style.gridRow;}});
                drag.ghost.querySelectorAll('[id]').forEach(element=>element.removeAttribute('id')); document.body.append(drag.ghost);
                drag.row.classList.add('is-sorting-source');document.documentElement.classList.add('service-is-sorting');previewOrder();
            }
        });
        list.addEventListener('pointerup', event => {if(drag?.id!==event.pointerId)return; const bounds=list.getBoundingClientRect();endDrag(event.clientX>=bounds.left && event.clientX<=bounds.right && event.clientY>=bounds.top-40 && event.clientY<=bounds.bottom+40);});
        list.addEventListener('pointercancel',()=>endDrag(false));
        list.addEventListener('lostpointercapture',event=>{if(event.target===list && drag?.id===event.pointerId)endDrag(false);});
        window.addEventListener('blur',()=>endDrag(false));
        document.addEventListener('keydown',event=>{if(event.key==='Escape' && drag){event.preventDefault();endDrag(false);}});
        list.addEventListener('dragstart',event=>event.preventDefault());
        list.addEventListener('keydown', event => {const handle = event.target.closest('.service-row-grip'); if (!handle || !event.altKey || !['ArrowUp','ArrowDown'].includes(event.key)) return; event.preventDefault(); const row = handle.closest('[data-service-id]'); const current = rows(); void reorder(row, current[current.indexOf(row)+(event.key === 'ArrowUp' ? -1 : 1)]);});
    }
    const form = document.querySelector('[data-service-editor]');
    if (!form) return;
    function openSections(element) {
        for (let section = element.closest('details'); section; section = section.parentElement.closest('details')) {
            section.open = true;
        }
    }
    function revealSection(hash) {
        if (!hash) return;
        const target = document.getElementById(hash.slice(1));
        if (target && form.contains(target)) openSections(target);
    }
    form.addEventListener('click', event => {
        const link = event.target.closest('a[href^="#"]');
        if (link) revealSection(link.hash);
    });
    revealSection(window.location?.hash);

    const typePicker = form.querySelector('[data-booking-type]');
    const newType = form.querySelector('[data-new-booking-type]');
    if (typePicker && newType) {
        const input = newType.querySelector('input');
        function syncType(animate = false) {
            const show = typePicker.value === '__new';
            const motion = window.gsap;
            motion?.killTweensOf(newType);
            newType.hidden = !show;
            input.disabled = !show;
            input.required = show;
            if (show && animate && motion && !window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
                motion.fromTo(newType, {height:0,opacity:0}, {height:'auto',opacity:1,duration:.25,ease:'power2.out',clearProps:'height,opacity,overflow'});
            } else motion?.set(newType, {clearProps:'height,opacity,overflow'});
            if (show && animate) input.focus();
        }
        typePicker.addEventListener('change', () => syncType(true));
        syncType();
    }

    const depositType = form.querySelector('[data-deposit-type]');
    const enabled = form.querySelector('[data-deposit-enabled]');
    const unit = form.querySelector('[data-deposit-unit]');
    const amountWrap = form.querySelector('[data-deposit-value]');
    const amount = amountWrap.querySelector('input');
    let depositMounted = false;
    function syncDeposit() {
        const show=enabled.checked, motion=window.gsap, reduce=window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? true;
        motion?.killTweensOf(amountWrap);
        if (!depositMounted || reduce || !motion) {
            amountWrap.hidden=!show;
            motion?.set(amountWrap,{clearProps:'height,opacity,overflow'});
        } else if (show) {
            const wasHidden=amountWrap.hidden; amountWrap.hidden=false;
            if (wasHidden) motion.set(amountWrap,{height:0,opacity:0});
            motion.to(amountWrap,{height:'auto',opacity:1,overflow:'hidden',duration:.28,ease:'power2.out',clearProps:'height,opacity,overflow'});
        } else if (!amountWrap.hidden) {
            motion.to(amountWrap,{height:0,opacity:0,overflow:'hidden',duration:.24,ease:'power2.inOut',onComplete:()=>{amountWrap.hidden=true;motion.set(amountWrap,{clearProps:'height,opacity,overflow'});}});
        }
        depositMounted=true;
        amount.disabled = !enabled.checked;
        amount.required = enabled.checked;
        unit.disabled = !enabled.checked;
        const unitButton = unit.nextElementSibling?.querySelector('button');
        if (unitButton) unitButton.disabled = unit.disabled;
        depositType.value = enabled.checked ? unit.value : 'None';
        amount.max = unit.value === 'Percentage' ? '100' : '1000000';
    }
    enabled.addEventListener('change', syncDeposit);
    unit.addEventListener('change', syncDeposit);
    syncDeposit();

    const gallery = form.querySelector('[data-service-gallery]');
    const add = form.querySelector('[data-image-add]');
    const input = form.querySelector('[data-service-uploads]');
    const imageError = form.querySelector('[data-images-error]');
    const viewer = document.querySelector('[data-image-viewer]');
    const viewerContent = viewer.querySelector('[data-viewer-content]');
    const details = form.querySelector('.service-detail-fields');
    if (details && typeof ResizeObserver === 'function') {
        const observer = new ResizeObserver(() => {
            if (window.innerWidth <= 760) { gallery.style.removeProperty('--service-gallery-height'); return; }
            const optionalHeight = [amountWrap, newType].reduce((sum, element) => {
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
    let items = [...gallery.querySelectorAll('[data-service-image]')].map(row => ({ path: row.dataset.path, src: row.querySelector('img').src }));
    let dragged = -1;
    function move(from, to) {
        items.splice(to, 0, items.splice(from, 1)[0]); render();
    }
    const reduceMotion = () => window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? true;
    let viewerMotion;
    function closeViewer() {
        if (window.gsap && !reduceMotion()) { viewerMotion?.kill(); viewerMotion = window.gsap.to(viewerContent, {opacity:0,scale:.96,duration:.2,ease:'power2.in',onComplete:()=>viewer.close()}); }
        else viewer.close();
    }
    function showImages(index, all) {
        viewerMotion?.kill();
        window.gsap?.set(viewerContent,{clearProps:'all'});

        const previousClose = viewer.querySelector('[data-viewer-close]');
        if (previousClose) viewer.append(previousClose);
        viewerContent.replaceChildren();
        (all ? items : [items[index]]).forEach((item, offset) => {
            const figure = document.createElement('figure');
            const img = document.createElement('img'); img.src = item.src; img.alt = 'Service image';
            const primary = document.createElement('button'); primary.type = 'button'; primary.className = 'service-viewer-primary'; primary.textContent = 'Set as primary';
            primary.addEventListener('click', () => { move(all ? offset : index, 0); closeViewer(); });
            figure.append(img, primary); viewerContent.append(figure);
        });
        const close = viewer.querySelector('[data-viewer-close]');
        viewerContent.children[0]?.append(close);
        const animateOpen = () => {
            viewer.showModal();
            if (window.gsap && !reduceMotion()) {
                viewerMotion = window.gsap.fromTo(viewerContent,{opacity:0,scale:.94,y:12,transformOrigin:'center'}, {opacity:1,scale:1,y:0,duration:.36,ease:'power3.out',clearProps:'all'});
            }
        };
        const firstImage = viewerContent.children[0]?.querySelector('img');
        if (firstImage?.decode) firstImage.decode().then(animateOpen, animateOpen);
        else animateOpen();
    }
    viewer.querySelector('[data-viewer-close]').addEventListener('click', closeViewer);
    viewer.addEventListener('click', event => { if (event.target === viewer) closeViewer(); });
    viewer.addEventListener('cancel', event => { event.preventDefault(); closeViewer(); });
    function render() {
        gallery.replaceChildren();
        const transfer = new DataTransfer();
        const limit = items.length > 5 ? 4 : items.length;
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
            const image = document.createElement('img'); image.src = item.src; image.alt = 'Service image ' + (index + 1); open.append(image);
            const overflow = items.length > 5 && index === 3;
            if (overflow) { const more = document.createElement('span'); more.className = 'service-image-more'; more.textContent = '+' + (items.length - 4); open.append(more); open.setAttribute('aria-label', 'Show all ' + items.length + ' images'); }
            open.addEventListener('click', () => showImages(index, overflow));
            const remove = document.createElement('button'); remove.type = 'button'; remove.className = 'service-image-delete'; remove.innerHTML = icon('delete'); remove.setAttribute('aria-label', 'Delete image ' + (index + 1));
            remove.addEventListener('click', () => { if (item.file) URL.revokeObjectURL(item.src); items.splice(index, 1); render(); });
            const primary = document.createElement('button'); primary.type = 'button'; primary.className = 'service-image-primary'; primary.textContent = index === 0 ? 'Primary' : 'Set primary'; primary.disabled = index === 0;
            primary.addEventListener('click', () => move(index, 0));
            tile.append(open, remove, primary);
            tile.addEventListener('dragstart', event => { dragged = index; event.dataTransfer.effectAllowed = 'move'; event.dataTransfer.setData('text/plain', String(index)); });
            tile.addEventListener('dragover', event => { if (dragged >= 0) { event.preventDefault(); event.dataTransfer.dropEffect = 'move'; } });
            tile.addEventListener('drop', event => { event.preventDefault(); if (dragged >= 0) move(dragged, index); dragged = -1; });
            tile.addEventListener('dragend', () => { dragged = -1; });
            tile.addEventListener('keydown', event => { if (event.altKey && ['ArrowLeft','ArrowRight'].includes(event.key)) { event.preventDefault(); move(index, Math.max(0, Math.min(items.length - 1, index + (event.key === 'ArrowLeft' ? -1 : 1)))); } });
            tile.title = 'Drag to reorder · Alt + Left or Right to reorder with keyboard';
            gallery.append(tile);
        });
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
    function fieldError(field, message) {
        const target = form.querySelector('[data-error-for="' + field.name + '"]');
        if (target) target.textContent = message;
        field.setAttribute('aria-invalid', message ? 'true' : 'false');
        if (target) { target.id = 'error-' + field.name; field.setAttribute('aria-describedby', target.id); }
    }
    form.addEventListener('input', event => {
        const field = event.target;
        if (field.form === form && field.name) fieldError(field, field.validity.valid ? '' : field.validationMessage);
    });
    form.addEventListener('invalid', event => {
        if (event.target.form !== form) return;
        event.preventDefault(); fieldError(event.target, event.target.validationMessage);
        openSections(event.target);
        shake(event.target);
        if (event.target === form.querySelector(':invalid')) { notify('Check the highlighted fields.'); event.target.focus(); }
    }, true);
    form.addEventListener('submit', event => {
        event.preventDefault();
        if (!items.length) { imageError.textContent = 'Add at least one image.'; notify(imageError.textContent); shake(gallery); input.focus(); return; }
        void save(form, [...document.querySelectorAll('[form="service-editor-form"][type=submit]')]);
    });
    window.addEventListener('pagehide', () => items.filter(item => item.file).forEach(item => URL.revokeObjectURL(item.src)));
})();
