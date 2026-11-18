(() => {
  document.querySelectorAll('[data-booking-tooltip]').forEach(tip => {
    const button = tip.querySelector('button');
    const bubble = tip.querySelector('[role="tooltip"]');
    if (!button || !bubble) return;

    const position = () => {
      if (!tip.classList.contains('is-open')) return;
      const trigger = button.getBoundingClientRect();
      bubble.style.position = 'fixed';
      bubble.style.inset = 'auto';
      bubble.style.left = '0px';
      bubble.style.top = '0px';
      bubble.style.right = 'auto';
      bubble.style.bottom = 'auto';
      bubble.style.maxWidth = 'calc(100vw - 24px)';
      bubble.style.maxHeight = 'calc(100dvh - 24px)';
      bubble.style.overflowY = 'auto';
      bubble.style.zIndex = '10000';
      const size = bubble.getBoundingClientRect();
      const left = Math.max(12, Math.min(trigger.left, window.innerWidth - size.width - 12));
      const above = trigger.top - size.height - 10;
      const below = trigger.bottom + 10;
      const top = above >= 12 ? above : Math.min(below, window.innerHeight - size.height - 12);
      bubble.style.left = `${left}px`;
      bubble.style.top = `${Math.max(12, top)}px`;
    };

    const setOpen = open => {
      if (open && !tip.classList.contains('is-open')) {
        const style = getComputedStyle(bubble);
        for (const property of ['width', 'padding', 'fontFamily', 'fontSize', 'fontWeight', 'letterSpacing', 'lineHeight', 'textTransform', 'border', 'backgroundColor', 'color', 'boxShadow']) bubble.style[property] = style[property];
        document.body.appendChild(bubble);
        bubble.classList.add('is-floating');
      }
      tip.classList.toggle('is-open', open);
      button.setAttribute('aria-expanded', String(open));
      if (open) position();
      else {
        bubble.classList.remove('is-floating');
        bubble.removeAttribute('style');
        tip.appendChild(bubble);
      }
    };

    button.setAttribute('aria-expanded', 'false');
    button.addEventListener('click', event => {
      event.preventDefault();
      event.stopPropagation();
      setOpen(!tip.classList.contains('is-open'));
    });
    document.addEventListener('click', event => {
      if (!tip.contains(event.target) && !bubble.contains(event.target)) setOpen(false);
    });
    tip.addEventListener('keydown', event => {
      if (event.key === 'Escape') {
        setOpen(false);
        button.focus({ preventScroll: true });
      }
    });
    window.addEventListener('resize', position, { passive: true });
    window.addEventListener('scroll', position, { passive: true, capture: true });
  });
})();
