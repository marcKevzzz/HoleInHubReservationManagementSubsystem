(() => {
    const deadlines = [...document.querySelectorAll('[data-deadline]')];
    const history = [...document.querySelectorAll('[data-relative]')];
    const review = document.querySelector('.review-decision');
    const relative = new Intl.RelativeTimeFormat('en', { numeric: 'auto' });

    function updateTimes() {
        const now = Date.now();
        for (const deadline of deadlines) {
            const left = Date.parse(deadline.dataset.deadline) - now;
            const minutes = Math.ceil(left / 60000);
            deadline.dataset.urgency = minutes <= 0 ? 'overdue' : minutes <= 5 ? 'critical' : minutes <= 15 ? 'soon' : 'normal';
            deadline.querySelector('[data-countdown]').textContent = minutes <= 0 ? 'Overdue'
                : minutes < 60 ? `${minutes}m left` : `${Math.floor(minutes / 60)}h ${minutes % 60}m left`;
            if (review && minutes <= 0) {
                review.querySelectorAll('[data-pending-action] button').forEach(button => { button.disabled = true; });
                review.querySelectorAll('[data-pending-action]').forEach(action => { action.hidden = true; });
                const expired = review.querySelector('[data-review-expired]');
                if (expired) expired.hidden = false;
            }
        }
        for (const time of history) {
            const minutes = Math.max(0, Math.floor((now - Date.parse(time.dateTime)) / 60000));
            const [value, unit] = minutes < 60 ? [minutes, 'minute'] : minutes < 1440
                ? [Math.floor(minutes / 60), 'hour'] : [Math.floor(minutes / 1440), 'day'];
            time.textContent = relative.format(-value, unit);
        }
    }
    if (deadlines.length || history.length) {
        updateTimes();
        setInterval(updateTimes, 15000);
    }

    const choice = document.querySelector('#reject-reason-choice');
    choice?.addEventListener('change', () => {
        choice.form.elements.reason.required = choice.value === 'Other';
    });
})();
