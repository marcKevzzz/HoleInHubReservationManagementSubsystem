(() => {
    function getChoices(form) {
        const values = {};
        for (const input of form?.querySelectorAll('[data-choice-label]') || []) {
            if (input.type === 'hidden' || input.checked) values[input.name] = input.dataset.choiceLabel;
        }
        return values;
    }
    function restoreChoices(form, values) {
        for (const [name, label] of Object.entries(values)) {
            const inputs = [...form.querySelectorAll('[data-choice-label]')].filter(input => input.name === name);
            const match = inputs.find(input => input.dataset.choiceLabel === label);
            if (match?.type === 'radio') for (const input of inputs) input.checked = input === match;
        }
    }
    if (typeof module !== 'undefined' && module.exports) module.exports = { getChoices, restoreChoices };
    if (typeof document === 'undefined') return;
    const page = document.querySelector('[data-customer-directory-page]');
    const dialog = page?.querySelector('[data-customer-row-merge]');
    if (typeof dialog?.showModal !== 'function') return;
    const body = dialog.querySelector('[data-row-merge-body]');
    const footer = dialog.querySelector('[data-row-merge-footer]');
    const status = dialog.querySelector('[data-row-merge-status]');
    const fallback = dialog.querySelector('[data-row-merge-fallback]');
    const close = dialog.querySelector('[data-row-merge-close]');
    const result = page.querySelector('[data-row-merge-result]');
    const dropStatus = page.querySelector('[data-merge-drop-status]');
    const directoryPath = new URL(page.querySelector('.people-search').action).pathname.replace(/\/$/, '');
    const toastStack = page.parentElement?.querySelector(':scope > [data-toast-stack]');
    const matchError = 'Match two name words, an email, or a phone number.';
    let disposeButtons = [], shake, checkingRow, invalidControl, outlined, outlineTimer;
    let controller, requestId = 0, returnTo, saving = false, retryBody, form, drag, frame, cancelledPointer, disabledButtons = [];

    function internalUrl(value) {
        const url = new URL(value, window.location.href);
        if (url.origin !== window.location.origin || !url.pathname.startsWith(directoryPath + '/')) throw new Error('Choose a customer from this directory.');
        return url;
    }
    function clearOutline() {
        clearTimeout(outlineTimer);
        outlined?.removeAttribute('data-merge-invalid');
        outlined = undefined;
    }
    function flashOutline(control) {
        clearOutline();
        outlined = control;
        control.setAttribute('data-merge-invalid', '');
        outlineTimer = setTimeout(clearOutline, 1500);
    }
    function announce(message, error = false) {
        clearOutline();
        invalidControl?.removeAttribute('aria-invalid');
        invalidControl = undefined;
        shake?.cancel();
        status.hidden = !message;
        status.textContent = message;
        status.classList.toggle('is-error', error);
    }
    function errorToast(title, message) {
        return window.HoleInHubToasts?.show({ kind: 'error', title, message, autoDismiss: true });
    }
    function dialogError(title, message, control = close) {
        const shown = errorToast(title, message);
        announce(shown ? '' : [title, message].filter(Boolean).join('. '), !shown);
        invalidControl = control;
        flashOutline(control);
        if (control.name === 'Form.Reason') control.setAttribute('aria-invalid', 'true');
        if (!window.matchMedia('(prefers-reduced-motion: reduce)').matches) shake = shakeElement(control);
        (shown ? control : status).focus();
    }
    function moveToastStack(parent, before = null) {
        if (!toastStack) return;
        if (parent.moveBefore) parent.moveBefore(toastStack, before);
        else parent.insertBefore(toastStack, before);
    }
    function restoreToastStack() {
        if (toastStack?.parentElement === dialog) moveToastStack(page.parentElement, page);
    }
    function shakeElement(element) {
        return element.animate?.(['0px', '-6px', '5px', '-3px', '0px'].map(translate => ({ translate })), { duration: 260 });
    }
    function rowFeedback(row, message, errorTitle) {
        const text = errorTitle && errorToast(errorTitle, message) ? '' : message;
        row.querySelector('[data-merge-row-feedback]').textContent = text;
        dropStatus.textContent = text ? row.dataset.customerName + ': ' + text : '';
        row.classList.toggle('is-merge-rejected', Boolean(errorTitle));
        if (errorTitle) {
            flashOutline(row);
            if (!window.matchMedia('(prefers-reduced-motion: reduce)').matches) shakeElement(row);
        }
    }
    function cancelDropCheck() {
        if (!checkingRow) return;
        controller?.abort();
        ++requestId;
        rowFeedback(checkingRow, '');
        checkingRow = undefined;
    }
    function mountHandles() {
        for (const handle of page.querySelectorAll('[data-merge-handle]')) handle.hidden = false;
    }
    function lock(locked) {
        saving = locked;
        close.disabled = locked;
        dialog.toggleAttribute('aria-busy', locked);
        if (locked) {
            disabledButtons = [...footer.querySelectorAll('button')].map(button => [button, button.disabled]);
            for (const [button] of disabledButtons) button.disabled = true;
        } else {
            for (const [button, disabled] of disabledButtons) if (button.isConnected) button.disabled = disabled;
            disabledButtons = [];
        }
        const reason = form?.querySelector('[name="Form.Reason"]');
        if (reason) reason.readOnly = locked;
    }
    function reviewStep(confirm) {
        if (!form || saving) return;
        if (confirm) {
            if (form.dataset.matchingDetails === 'false') {
                dialogError('Customers don’t match', matchError, footer.querySelector('[data-merge-review]'));
                return;
            }
            if (form.dataset.mergeIncluded === '0') {
                const history = form.querySelector('.merge-booking-review');
                history.open = true;
                dialogError('Select a reservation', 'Choose at least one reservation to merge.', history.querySelector('summary'));
                return;
            }
        }
        const reason = form.querySelector('[name="Form.Reason"]');
        form.querySelector('[data-merge-review-content]').hidden = confirm;
        body.querySelector('[data-merge-pair-review]').hidden = confirm;
        form.querySelector('[data-merge-confirmation]').hidden = !confirm;
        footer.querySelector('[data-review-actions]').hidden = confirm;
        footer.querySelector('[data-confirm-actions]').hidden = !confirm;
        reason.disabled = !confirm;
        if (confirm) {
            const summary = form.querySelector('[data-merge-final-fields]');
            summary.replaceChildren();
            const details = { 'Main profile': body.querySelector('.merge-main-profile h3').textContent, ...Object.fromEntries(Object.entries(getChoices(form)).map(([key, value]) => [key.replace('Form.', '').replace('From', ''), value])) };
            for (const [label, value] of Object.entries(details)) {
                const term = document.createElement('dt'), description = document.createElement('dd');
                term.textContent = label; description.textContent = value;
                summary.append(term, description);
            }
            announce('Review the chosen details and enter a reason.');
            reason.focus();
        } else {
            retryBody = undefined;
            announce('Review the records and primary details before continuing.');
            dialog.querySelector('[data-merge-review]').focus();
        }
        dialog.querySelector('.customer-row-merge-scroll').scrollTop = 0;
    }
    function stopButtonMotion() {
        for (const dispose of disposeButtons) dispose();
        disposeButtons = [];
    }
    function render(panel, remembered, reason) {
        stopButtonMotion();
        footer.replaceChildren();
        body.replaceChildren(panel);
        body.hidden = false;
        form = panel.querySelector('[data-customer-merge]');
        retryBody = undefined;
        if (form) {
            if (remembered) restoreChoices(form, remembered);
            if (reason) form.querySelector('[name="Form.Reason"]').value = reason;
            window.HoleInHubCustomerMerge.mount(panel);
            footer.append(form.querySelector('[data-review-actions]'), form.querySelector('[data-confirm-actions]'));
        } else window.HoleInHubCustomerSearch.mount(panel);
        footer.hidden = !form;
        if (window.HoleInHubButtons) {
            const buttons = dialog.querySelectorAll('[data-merge-switch], [data-merge-cancel], [data-merge-review], [data-merge-back]');
            disposeButtons = [...buttons].map(button => window.HoleInHubButtons.mount(button));
        }
    }
    async function load(value, preserve = false, sourceRow, trigger) {
        let url;
        try { url = internalUrl(value); } catch (error) { dialogError('Invalid comparison', error.message, trigger); return; }
        if (!url.pathname.endsWith('/merge') || saving) return;
        const remembered = preserve ? getChoices(form) : undefined;
        const reason = preserve ? form?.querySelector('[name="Form.Reason"]')?.value : '';
        body.querySelector('.people-search')?.customerSearch?.cancel();
        controller?.abort();
        const current = controller = new AbortController();
        const id = ++requestId;
        const timeout = setTimeout(() => current.abort(), 15000);
        fallback.href = url; fallback.textContent = 'Open full comparison'; fallback.hidden = true;
        if (sourceRow) {
            checkingRow = sourceRow;
            rowFeedback(sourceRow, 'Checking customer match…');
        } else {
            body.hidden = true; footer.hidden = true;
            dialog.classList.add('is-loading');
            dialog.setAttribute('aria-busy', 'true');
            announce('Loading comparison…');
        }
        try {
            const response = await fetch(url, { headers: { 'X-Customer-Merge': 'dialog' }, cache: 'no-store', signal: current.signal, redirect: 'error' });
            if (!response.ok) throw new Error(response.status === 401 || response.status === 403 ? 'Sign in again to continue.' : 'Try again or open the full comparison.');
            const panel = new DOMParser().parseFromString(await response.text(), 'text/html').querySelector('[data-row-merge-content]');
            if (!panel) throw new Error('The comparison could not be loaded. Open the full comparison to continue.');
            if (id !== requestId) return;
            if (sourceRow) {
                const candidate = panel.querySelector('[data-customer-merge]');
                if (candidate?.dataset.matchingDetails !== 'true') {
                    rowFeedback(sourceRow, candidate ? matchError : panel.dataset.mergeError,
                        candidate ? 'Customers don’t match' : 'Merge unavailable');
                    return;
                }
                rowFeedback(sourceRow, '');
            } else if (!dialog.open) return;
            render(panel, remembered, reason);
            fallback.hidden = true;
            if (sourceRow) openDialog(sourceRow.querySelector('.customer-profile-link'));
            if (form?.dataset.matchingDetails === 'false') {
                dialogError('Customers don’t match', matchError, footer.querySelector('[data-merge-review]'));
            } else if (panel.dataset.mergeError) {
                dialogError('Merge unavailable', panel.dataset.mergeError);
            } else {
                announce('');
                close.focus();
                if (sourceRow) window.HoleInHubToasts?.show({ kind: 'info', title: 'Customers match', message: 'Review the details before merging.' });
            }
        } catch (error) {
            if (id === requestId && sourceRow) {
                const message = error.name === 'AbortError' ? 'Match check timed out. Drop the row again.'
                    : 'Could not check the match. Try again or reopen Customers.';
                rowFeedback(sourceRow, message, 'Match check failed');
            } else if (id === requestId && dialog.open) {
                body.hidden = false; footer.hidden = !form; fallback.hidden = false;
                dialogError('Could not load comparison', error.name === 'AbortError' ? 'Try again or open the full comparison.' : error.message, trigger);
            }
        } finally {
            clearTimeout(timeout);
            if (id === requestId) {
                if (sourceRow) checkingRow = undefined;
                dialog.classList.remove('is-loading');
                dialog.removeAttribute('aria-busy');
            }
        }
    }
    function openDialog(trigger) {
        returnTo = trigger;
        dialog.showModal();
        moveToastStack(dialog);
        document.documentElement.classList.add('customer-merge-open');
    }
    function suppressClick() {
        const suppress = event => { event.preventDefault(); event.stopImmediatePropagation(); };
        document.addEventListener('click', suppress, { capture: true, once: true });
        setTimeout(() => document.removeEventListener('click', suppress, true), 0);
    }
    function cancelDrag() {
        if (drag?.active) cancelledPointer = drag.id;
        clearDrag();
    }
    function clearDrag(landingTarget) {
        const current = drag;
        drag = undefined;
        if (!current) return;
        if (current.handle.hasPointerCapture?.(current.id)) current.handle.releasePointerCapture(current.id);
        current.source.classList.remove('is-merge-source');
        current.target?.classList.remove('is-merge-target');
        if (current.ghost?.animate && !window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
            const bounds = (landingTarget || current.source).getBoundingClientRect();
            const flight = current.ghost.getBoundingClientRect();
            const landing = current.ghost.animate([{ transform: 'translate(0,0)', opacity: 1 }, { transform: `translate(${bounds.left - flight.left}px,${bounds.top - flight.top}px)`, opacity: 0 }], { duration: 160, easing: 'ease-out' });
            landing.onfinish = () => current.ghost.remove();
        } else current.ghost?.remove();
        cancelAnimationFrame(frame);
        document.documentElement.classList.remove('customer-is-merging');
    }
    function dragFrame() {
        if (!drag?.active) return;
        const edge = drag.y < 70 ? -12 : drag.y > window.innerHeight - 70 ? 12 : 0;
        if (edge) window.scrollBy({ top: edge, behavior: 'instant' });
        const row = document.elementFromPoint(drag.x, drag.y)?.closest('[data-customer-key]');
        const target = row && page.contains(row) && row !== drag.source ? row : undefined;
        if (target !== drag.target) {
            drag.target?.classList.remove('is-merge-target');
            drag.target = target;
            target?.classList.add('is-merge-target');
            drag.ghost.lastElementChild.textContent = target ? `Review merge with ${target.dataset.customerName}` : 'Drop on another customer to review';
        }
        drag.ghost.style.left = Math.max(8, Math.min(drag.x - drag.offsetX, window.innerWidth - drag.ghost.offsetWidth - 8)) + 'px';
        drag.ghost.style.top = Math.max(8, Math.min(drag.y - drag.offsetY - 6, window.innerHeight - drag.ghost.offsetHeight - 8)) + 'px';
        frame = requestAnimationFrame(dragFrame);
    }
    page.addEventListener('pointerdown', event => {
        const source = event.target.closest('[data-customer-key]');
        if (!source || !source.querySelector('[data-merge-handle]') || event.target.closest('a,button,input,select,textarea,summary') || event.pointerType !== 'mouse' || event.button || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || dialog.open) return;
        const bounds = source.getBoundingClientRect();
        drag = { handle: source, source, id: event.pointerId, startX: event.clientX, startY: event.clientY, x: event.clientX, y: event.clientY, offsetX: event.clientX - bounds.left, offsetY: event.clientY - bounds.top, width: bounds.width };
        source.setPointerCapture(event.pointerId);
    });
    page.addEventListener('pointermove', event => {
        if (drag?.id !== event.pointerId) return;
        drag.x = event.clientX; drag.y = event.clientY;
        if (!drag.active && Math.hypot(drag.x - drag.startX, drag.y - drag.startY) >= 7) {
            event.preventDefault();
            cancelDropCheck();
            rowFeedback(drag.source, '');
            window.HoleInHubCustomerSearch.cancel();
            window.getSelection()?.removeAllRanges();
            drag.active = true;
            drag.source.classList.add('is-merge-source');
            document.documentElement.classList.add('customer-is-merging');
            drag.ghost = document.createElement('div'); drag.ghost.className = 'people-page customer-directory-merge-enabled customer-merge-ghost';
            drag.ghost.setAttribute('aria-hidden', 'true'); drag.ghost.inert = true;
            drag.ghost.style.width = Math.min(drag.width, window.innerWidth - 16) + 'px';
            const lifted = drag.source.cloneNode(true);
            lifted.classList.remove('is-merge-source', 'staff-view-row');
            lifted.removeAttribute('tabindex');
            lifted.style.gridTemplateColumns = window.getComputedStyle(drag.source).gridTemplateColumns;
            for (const element of lifted.querySelectorAll('[id]')) element.removeAttribute('id');
            const hint = document.createElement('span'); hint.className = 'customer-merge-drag-hint';
            drag.ghost.append(lifted, hint); document.body.append(drag.ghost);
            if (!window.matchMedia('(prefers-reduced-motion: reduce)').matches) drag.ghost.animate([{ scale: .985, opacity: .7 }, { scale: 1, opacity: 1 }], { duration: 180, easing: 'ease-out' });
            dragFrame();
        }
    });
    page.addEventListener('pointerup', event => {
        if (drag?.id !== event.pointerId) return;
        const { active, source } = drag;
        const target = document.elementFromPoint(event.clientX, event.clientY)?.closest('[data-customer-key]');
        clearDrag(target && page.contains(target) ? target : undefined);
        if (!active) return;
        event.preventDefault();
        suppressClick();
        if (!target || !page.contains(target) || target.dataset.customerKey === source.dataset.customerKey) return;
        const url = internalUrl(target.dataset.mergeUrl);
        url.searchParams.set('target', source.dataset.customerKey);
        void load(url, false, source);
    });
    page.addEventListener('pointercancel', event => { if (drag?.id === event.pointerId) clearDrag(); cancelledPointer = undefined; });
    page.addEventListener('lostpointercapture', event => { if (drag?.id === event.pointerId) cancelDrag(); });
    document.addEventListener('pointerdown', () => {
        cancelledPointer = undefined;
        clearOutline();
    }, true);
    document.addEventListener('click', event => {
        if (event.detail === 0) clearOutline();
    }, true);
    document.addEventListener('pointerup', event => {
        if (event.pointerId === cancelledPointer) { cancelledPointer = undefined; suppressClick(); }
    }, true);
    function cancelGesture() {
        cancelDrag();
        cancelDropCheck();
    }
    document.addEventListener('keydown', event => { if (event.key === 'Escape') cancelGesture(); });
    document.addEventListener('staff:rows-replacing', cancelGesture);
    document.addEventListener('staff:rows-updated', mountHandles);
    page.querySelector('.people-search').addEventListener('input', cancelGesture);
    dialog.addEventListener('click', event => {
        if (event.target.closest('[data-row-merge-close],[data-merge-cancel]')) { if (!saving) dialog.close(); return; }
        if (event.target.closest('[data-merge-review]')) { reviewStep(true); return; }
        if (event.target.closest('[data-merge-back]')) { reviewStep(false); return; }
        const link = event.target.closest('a[href]');
        if (!link || link.target || event.button || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || saving) return;
        if (new URL(link.href).pathname.endsWith('/merge')) {
            event.preventDefault();
            void load(link.href, link.hasAttribute('data-merge-switch') || link.hasAttribute('data-merge-reload'), undefined, link);
        }
    });
    dialog.addEventListener('cancel', event => { if (saving) event.preventDefault(); });
    dialog.addEventListener('close', () => {
        stopButtonMotion();
        announce('');
        restoreToastStack();
        ++requestId; controller?.abort();
        body.querySelector('.people-search')?.customerSearch?.cancel();
        document.documentElement.classList.remove('customer-merge-open');
        returnTo?.focus({ preventScroll: true });
    });
    dialog.addEventListener('change', event => {
        if (event.target.name === 'Form.ReservationIds' && form?.dataset.matchingDetails !== 'false' && form?.dataset.mergeIncluded !== '0') {
            announce('');
        }
    });
    dialog.addEventListener('input', event => {
        if (event.target === invalidControl && event.target.name === 'Form.Reason' && event.target.value.trim()) announce('');
    });
    dialog.addEventListener('invalid', event => {
        if (event.target.name !== 'Form.Reason') return;
        event.preventDefault();
        dialogError('Reason required', 'Enter a reason for the merge.', event.target);
    }, true);
    dialog.addEventListener('submit', async event => {
        if (event.target !== form) return;
        event.preventDefault();
        if (saving || !form.reportValidity()) return;
        const reason = form.querySelector('[name="Form.Reason"]');
        if (!reason.value.trim()) {
            dialogError('Reason required', 'Enter a reason for the merge.', reason);
            return;
        }
        const payload = retryBody || new FormData(form);
        const button = footer.querySelector('[data-merge-save]'), label = button.innerHTML;
        let url;
        try { url = internalUrl(form.action); } catch (error) { dialogError('Merge unavailable', error.message, button); return; }
        lock(true); button.textContent = 'Saving…'; announce('Saving the reviewed customer merge…');
        controller = new AbortController();
        const timeout = setTimeout(() => controller.abort(), 25000);
        try {
            const response = await fetch(url, { method: 'POST', headers: { 'X-Customer-Merge': 'dialog' }, body: payload, signal: controller.signal, redirect: 'error' });
            if (response.status === 409) {
                const panel = new DOMParser().parseFromString(await response.text(), 'text/html').querySelector('[data-row-merge-content]');
                if (!panel) throw new Error('The save result could not be verified.');
                const errors = panel.querySelector('.validation-summary-errors');
                const message = errors?.querySelector('li')?.textContent.trim();
                errors?.remove();
                lock(false);
                render(panel);
                dialogError('Merge blocked', message || 'Reload the comparison and try again.',
                    panel.querySelector('[data-merge-reload]') || close);
                return;
            }
            if (response.status === 401 || response.status === 403) {
                retryBody = undefined;
                fallback.href = url;
                fallback.textContent = 'Open full comparison';
                fallback.hidden = false;
                lock(false);
                dialogError('Session expired', 'Sign in again to continue.', fallback);
                return;
            }
            if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) throw new Error('The save result could not be verified.');
            const receipt = await response.json();
            const refreshed = await window.HoleInHubCustomerSearch.refresh()?.catch(() => false);
            const toast = receipt.toast;
            if (!refreshed) toast.message += ' Refresh the directory to see the updated rows.';
            returnTo = [...page.querySelectorAll('[data-customer-key]')]
                .find(row => row.dataset.customerKey === receipt.rowKey)?.querySelector('.customer-profile-link')
                || page.querySelector('#customer-search');
            lock(false); dialog.close(); restoreToastStack();
            result.hidden = Boolean(window.HoleInHubToasts?.show(toast));
            result.textContent = result.hidden ? '' : toast.title + '. ' + toast.message;
        } catch {
            retryBody = payload;
            fallback.href = internalUrl(form.dataset.historyUrl);
            fallback.textContent = 'Check merge history';
            fallback.hidden = false;
            lock(false);
            dialogError('Merge status unknown', 'Retry save or check merge history before starting another merge.', button);
        } finally {
            clearTimeout(timeout); lock(false);
            if (button.isConnected) button.innerHTML = retryBody ? 'Retry save' : label;
            if (retryBody && form?.isConnected) { form.querySelector('[name="Form.Reason"]').readOnly = true; footer.querySelector('[data-merge-back]').disabled = true; }
        }
    });
    mountHandles();
})();
