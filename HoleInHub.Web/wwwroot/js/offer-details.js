document.querySelector('.venue-promotions')?.addEventListener('click', event => {
    const trigger = event.target.closest('[data-offer-details]');
    if (!trigger) return;
    document.getElementById(trigger.getAttribute('aria-controls'))?.showModal();
});
