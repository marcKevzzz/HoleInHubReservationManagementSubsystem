(() => {
    const form = document.querySelector('[data-family-booking]');
    if (!form) return;
    const learners = [...form.querySelectorAll('[data-family-learner]')];
  function isMinor(row) {
    const age = row.querySelector('[data-age]').valueAsNumber;
    return Number.isInteger(age) && age >= 0 && age < 18;
  }
    function update() {
        const session = form.elements.ClassSessionId;
        if (!session) return;
        const remaining = Number(session.selectedOptions[0]?.dataset.remaining || 0);
        const quantity = form.elements.learnerCount;
        for (const option of quantity.options) option.disabled = remaining > 0 && Number(option.value) > remaining;
        const submit = form.querySelector('[data-family-submit]');
        if (submit) {
            submit.disabled = session.value !== form.elements.ReviewedClassSessionId.value ||
                quantity.value !== form.elements.ReviewedLearnerCount.value || Number(quantity.value) > remaining;
        }
        for (const [index, learner] of learners.entries()) {
            const name = learner.querySelector('[data-learner-name]');
            name.hidden = learner.querySelector('[data-participating]').checked;
            for (const input of name.querySelectorAll('input')) input.required = !name.hidden;
            const minor = isMinor(learner);
            learner.querySelector('[data-guardian]').hidden = !minor;
            const source = learner.querySelector('[data-guardian-index]');
            for (const option of source.options) {
                const previous = Number(option.value);
                option.disabled = previous !== index && (!isMinor(learners[previous])
                    || Number(learners[previous].querySelector('[data-guardian-index]').value) !== previous);
            }
            if (source.selectedOptions[0]?.disabled) source.value = String(index);
            const own = Number(source.value) === index;
            learner.querySelector('[data-guardian-fields]').hidden = !own;
            const guardianName = learner.querySelector('[data-guardian-name]');
            guardianName.hidden = learner.querySelector('[data-contact-guardian]').checked;
            guardianName.querySelector('input').required = minor && own && !guardianName.hidden;
            learner.querySelector('[name$=".Relationship"]').required = minor && own;
        }
    }
    const review = form.querySelector('.family-review');
    if (review && form.dataset.customerFlow === 'true') {
        const next = form.querySelector('[data-family-review-next]');
        const back = form.querySelector('[data-family-review-back]');
        const sections = [...form.querySelectorAll(':scope > fieldset')];
        const showReview = active => {
            for (const tab of document.querySelectorAll('[data-family-step]')) {
                const current = Number(tab.dataset.familyStep) === (active ? 3 : 2);
                tab.classList.toggle('current', current);
                if (current) tab.setAttribute('aria-current', 'step');
                else tab.removeAttribute('aria-current');
            }
            sections.forEach(section => section.hidden = active);
            review.hidden = !active;
            next.hidden = active;
            back.hidden = !active;
        };
        next.onclick = () => {
            if (form.querySelector('[data-family-submit]')?.disabled) {
                form.requestSubmit(form.querySelector('[value="familyForm"]'));
                return;
            }
            for (const section of sections) {
                const invalid = section.querySelector(':invalid');
                if (invalid && !invalid.reportValidity()) return;
            }
            review.querySelector('[data-family-review-names]').textContent = learners.map(learner =>
                learner.querySelector('[data-participating]').checked
                    ? [form.elements.FirstName.value, form.elements.LastName.value].join(' ')
                    : [...learner.querySelectorAll('[data-learner-name] input')].map(input => input.value.trim()).join(' ')).join(', ');
            showReview(true);
            back.focus();
        };
        back.onclick = () => { showReview(false); next.focus(); };
        showReview(false);
    }
    form.addEventListener('change', update);
    update();
})();
