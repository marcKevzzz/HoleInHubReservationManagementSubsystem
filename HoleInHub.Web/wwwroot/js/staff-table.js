(() => {
    document.addEventListener('toggle', event => {
        const action = event.target;
        if (!action.matches?.('.review-header-action[open]')) return;
        const form = action.querySelector('form');
        const bounds = action.querySelector('summary').getBoundingClientRect();
        const width = Math.min(340, window.innerWidth - 24);
        Object.assign(form.style, {
            position: 'fixed', width: width + 'px', right: 'auto',
            left: Math.max(12, Math.min(bounds.right - width, window.innerWidth - width - 12)) + 'px',
            top: Math.min(bounds.bottom + 6, window.innerHeight - 100) + 'px',
            maxHeight: Math.max(80, window.innerHeight - bounds.bottom - 18) + 'px', overflow: 'auto'
        });
    }, true);
    function mountRows() {
        for (const row of document.querySelectorAll('.staff-workspace [role="row"], .staff-workspace tbody tr, .customer-bookings > li')) {
            const link = row.querySelector('a[href]:not([href^="#"])');
            if (row.classList.contains('staff-view-row')) continue;
            const cells = [...row.querySelectorAll(':scope > th, :scope > td')];
            if (!link && !cells.length) continue;
            row.classList.add('staff-view-row');
            row.tabIndex = 0;
            row.setAttribute('aria-label', link
                ? link.getAttribute('aria-label') || link.textContent.trim()
                : 'View ' + cells[0].textContent.trim());
            function navigate(event) {
                if (event.target.closest('a,button,input,select,textarea,summary,form,[popover]')) return;
                if (window.getSelection()?.toString()) return;
                if (event.type === 'keydown' && (event.key !== 'Enter' || event.target !== row)) return;
                if (event.type === 'click' && event.button !== 0) return;
                if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
                event.preventDefault();
                if (link) {
                    link.click();
                    return;
                }
                const dialog = document.createElement('dialog');
                dialog.className = 'staff-row-details';
                const title = document.createElement('h2');
                title.id = 'staff-row-details-title';
                title.textContent = cells[0].textContent.trim();
                dialog.setAttribute('aria-labelledby', title.id);
                const facts = document.createElement('dl');
                const headers = row.closest('table').querySelectorAll('thead th');
                cells.forEach((cell, index) => {
                    if (cell.querySelector('form,button')) return;
                    const label = document.createElement('dt');
                    label.textContent = headers[index]?.textContent.trim() || 'Details';
                    const value = document.createElement('dd');
                    value.textContent = cell.textContent.trim();
                    facts.append(label, value);
                });
                const close = document.createElement('button');
                close.type = 'button';
                close.className = 'button button-secondary';
                close.textContent = 'Close details';
                close.addEventListener('click', () => dialog.close());
                dialog.addEventListener('close', () => { dialog.remove(); row.focus(); });
                dialog.append(title, facts, close);
                document.body.append(dialog);
                dialog.showModal();
            }
            row.addEventListener('click', navigate);
            row.addEventListener('keydown', navigate);
        }
    }
    mountRows();
    document.addEventListener('staff:rows-updated', mountRows);

    for (const group of document.querySelectorAll('[data-primary-actions], .review-row-actions')) {
        if (group.querySelector(':scope > .staff-more-actions')) continue;
        const actions = [...group.children].filter(item => item.matches('a,button,details') && !item.matches('.review-view-link'));
        if (actions.length <= 2 || typeof HTMLElement.prototype.showPopover !== 'function') continue;
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'staff-icon-button staff-more-actions';
        button.textContent = '⋮';
        button.setAttribute('aria-label', 'More actions');
        button.setAttribute('aria-expanded', 'false');
        const menu = document.createElement('div');
        menu.id = 'staff-actions-' + document.querySelectorAll('.staff-action-menu').length;
        menu.className = 'staff-action-menu';
        menu.popover = 'auto';
        button.setAttribute('aria-controls', menu.id);
        button.popoverTargetElement = menu;
        group.append(button, menu);
        for (const action of actions.slice(2)) menu.append(action);
        function position() {
            const bounds = button.getBoundingClientRect();
            const width = Math.min(320, window.innerWidth - 24);
            menu.style.width = width + 'px';
            menu.style.left = Math.max(12, Math.min(bounds.right - width, window.innerWidth - width - 12)) + 'px';
            const below = window.innerHeight - bounds.bottom - 12;
            const above = bounds.top - 12;
            const down = below >= Math.min(menu.scrollHeight, 240) || below >= above;
            menu.style.maxHeight = Math.max(44, down ? below : above) + 'px';
            menu.style.top = down ? bounds.bottom + 4 + 'px' : '';
            menu.style.bottom = down ? '' : window.innerHeight - bounds.top + 4 + 'px';
        }
        menu.addEventListener('toggle', () => {
            const open = menu.matches(':popover-open');
            button.setAttribute('aria-expanded', String(open));
            if (open) position();
        });
        menu.addEventListener('toggle', position, true);
        window.addEventListener('resize', () => { if (menu.matches(':popover-open')) position(); });
        window.addEventListener('scroll', event => {
            if (menu.contains(event.target)) return;
            if (menu.matches(':popover-open')) menu.hidePopover();
        }, true);
    }

    function revealHistory() {
        if (window.location.hash !== '#booking-history') return;
        const history = document.querySelector('details#booking-history');
        if (history) {
            history.open = true;
            history.scrollIntoView({ block: 'start' });
        }
    }
    window.addEventListener('hashchange', revealHistory);
    revealHistory();

    document.addEventListener('click', async event => {
        const button = event.target.closest('[data-queue-decision]');
        if (!button || button.disabled) return;
        button.disabled = true;
        try {
            const response = await fetch(button.dataset.actionsUrl, { cache: 'no-store' });
            if (!response.ok || response.redirected) throw new Error('Reload the page and sign in again if required.');
            const dialog = document.createElement('dialog');
            dialog.className = 'staff-row-details staff-workspace review-detail review-action-dialog';
            dialog.setAttribute('aria-label', 'Review reservation action');
            const content = document.createElement('div');
            content.innerHTML = await response.text();
            const close = document.createElement('button');
            close.type = 'button';
            close.className = 'button button-secondary';
            close.textContent = 'Close';
            dialog.append(close, content);
            close.addEventListener('click', () => dialog.close());
            dialog.addEventListener('close', () => { dialog.remove(); button.focus(); });
            const command = button.dataset.queueDecision.replace('-change', '');
            const actionButton = content.querySelector('button[value="' + command + '"]');
            const action = actionButton?.closest('details');
            if (action) action.open = true;
            if (!actionButton) {
                const message = document.createElement('p');
                message.textContent = 'This action is currently unavailable. Review the latest booking details.';
                content.prepend(message);
            }
            content.querySelector('#reject-reason-choice')?.addEventListener('change', change => {
                change.target.form.elements.reason.required = change.target.value === 'Other';
            });
            document.body.append(dialog);
            dialog.showModal();
        } catch (error) {
            const notice = document.createElement('p');
            notice.setAttribute('role', 'alert');
            notice.textContent = error.message;
            button.closest('[role=row]').append(notice);
        } finally { button.disabled = false; }
    });
})();
