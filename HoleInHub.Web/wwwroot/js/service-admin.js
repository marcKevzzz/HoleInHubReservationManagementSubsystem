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
    async function save(form, buttons, submitter) {
        if (saving) return;
        const body = new FormData(form);
        if (submitter?.name) body.set(submitter.name, submitter.value);
        saving = true;
        form.setAttribute("aria-busy", "true");
        buttons.forEach(button => button.disabled = true);
        try {
            const response = await fetch(form.action, { method: 'POST', body, headers: { Accept: 'application/json' } });
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

    function cloneServiceRow(row) {
        const ghost = row.cloneNode(true);
        const rowStyle = getComputedStyle(row);
        for (const property of ['gridTemplateColumns', 'gap', 'padding', 'borderWidth', 'boxSizing']) {
            ghost.style[property] = rowStyle[property];
        }
        [...row.children].forEach((child, index) => {
            const style = getComputedStyle(child), copy = ghost.children[index];
            if (copy) {
                copy.style.gridColumn = style.gridColumn;
                copy.style.gridRow = style.gridRow;
            }
        });
        return ghost;
    }

    function mountRowReorder(list, { rowSelector, canReorder, commitOrder, createGhost = cloneServiceRow }) {
        let drag, dragFrame;
        const rows = () => [...list.querySelectorAll(rowSelector)];
        const number = () => rows().forEach((row, index) => row.querySelector('[data-service-position]').textContent = String(index + 1).padStart(2, '0'));
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
            if (commit) void commitOrder(current.row, current.before);
        }
        list.addEventListener('pointerdown', event => {
            const row=event.target.closest(rowSelector), handle=event.target.closest('.service-row-grip');
            if (!row || drag || !canReorder() || handle?.disabled || event.button || event.isPrimary === false || (!handle && event.target.closest('a,button,input,select,textarea')) || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
            const rect=row.getBoundingClientRect();
            drag={row,id:event.pointerId,startX:event.clientX,startY:event.clientY,x:event.clientX,y:event.clientY,offsetX:event.clientX-rect.left,offsetY:event.clientY-rect.top,width:Math.min(rect.width,innerWidth-16),before:rows()};
            list.setPointerCapture(event.pointerId);
        });
        list.addEventListener('pointermove', event => {
            if (drag?.id !== event.pointerId) return; drag.x=event.clientX;drag.y=event.clientY;
            if (!drag.active && Math.hypot(drag.x-drag.startX,drag.y-drag.startY)>=6) {
                event.preventDefault(); drag.active=true; window.getSelection()?.removeAllRanges();
                drag.ghost=createGhost(drag.row);drag.ghost.classList.add('service-row-floating');drag.ghost.setAttribute('aria-hidden','true');drag.ghost.inert=true;
                drag.ghost.style.width=drag.width+'px';
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
        list.addEventListener('keydown', event => {
            const handle = event.target.closest('.service-row-grip');
            if (!handle || handle.disabled || drag || !canReorder() || !event.altKey || !['ArrowUp', 'ArrowDown'].includes(event.key)) return;
            event.preventDefault();
            const row = handle.closest(rowSelector), before = rows(), up = event.key === 'ArrowUp';
            const target = before[before.indexOf(row) + (up ? -1 : 1)];
            if (!target) return;
            list.insertBefore(row, up ? target : target.nextSibling);
            number();
            void commitOrder(row, before);
        });
        return () => endDrag(false);
    }
    window.HoleInHubRowReorder = mountRowReorder;

    const list = document.querySelector('[data-service-list]');
    if (list) {
        const reorderForm = document.querySelector('[data-service-reorder]');
        let busy = false;
        const rows = () => [...list.querySelectorAll('[data-service-id]')];
        const number = () => rows().forEach((row, index) => row.querySelector('[data-service-position]').textContent = String(index + 1).padStart(2, '0'));
        async function reorder(row, before) {
            const ordered = rows();
            if (busy || before.every((item, index) => item === ordered[index])) return;
            busy = true;
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
        mountRowReorder(list, {
            rowSelector: '[data-service-id]',
            canReorder: () => !busy,
            commitOrder: reorder
        });
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

    const bookingRules = form.querySelector('[name=BookingService]');
    const baySelection = form.querySelector('[data-bay-selection]');
    if (bookingRules && baySelection) {
        const selection = baySelection.querySelector('[type=checkbox]');
        let explicitSelection = selection.dataset.explicit === 'true';
        selection.addEventListener('change', () => { explicitSelection = true; });
        const wholeShopNote = form.querySelector('[data-whole-shop-bays]');
        function syncBaySelection() {
            const wholeShop = bookingRules.value === 'WholeShop';
            baySelection.hidden = wholeShop;
            selection.disabled = wholeShop;
            wholeShopNote.hidden = !wholeShop;
            if (!explicitSelection) selection.checked = bookingRules.value !== 'KpgaGroup';
        }
        bookingRules.addEventListener('change', syncBaySelection);
        syncBaySelection();
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

    const { count, gallery, imageError, input } = window.HoleInHubEditorGallery(form, notify, shake);
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
    const setup = form.querySelector('[data-lesson-setup]');
    if (setup) {
        const update = () => {
            setup.hidden = form.elements.BookingService.value !== 'KpgaGroup';
            form.querySelector('#service-availability').hidden = !setup.hidden;
            form.querySelector('label[for="MaxPax"]').textContent = setup.hidden ? 'Maximum guests per booking (optional)' : 'Maximum learners per booking (optional)';
            const action = setup.querySelector('button');
            if (action) action.textContent = document.querySelector('#Active').checked ? 'Save and schedule dates' : 'Activate and schedule dates';
        };
        form.elements.BookingService.addEventListener('change', update);
        document.querySelector('#Active').addEventListener('change', update);
        update();
    }
    form.addEventListener('submit', event => {
        event.preventDefault();
        if (!count()) { imageError.textContent = 'Add at least one image.'; notify(imageError.textContent); shake(gallery); input.focus(); return; }
        void save(form, [...document.querySelectorAll('[form="service-editor-form"][type=submit], [name=scheduleLessons]')], event.submitter);
    });
})();
