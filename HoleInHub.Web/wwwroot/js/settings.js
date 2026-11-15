(() => {
 const health=document.querySelector('[data-settings-health]');
 if(health) {
   const refresh=health.querySelector('[data-health-refresh]'),status=health.querySelector('[data-health-status]'),time=health.querySelector('[data-health-time]');
   async function checkHealth() {
     if(refresh.disabled)return;
     refresh.disabled=true;status.textContent='Checking…';delete status.dataset.ready;
     const controller=new AbortController(),timeout=setTimeout(()=>controller.abort(),10000);
     try {
       const response=await fetch(health.dataset.healthUrl,{cache:'no-store',redirect:'error',signal:controller.signal});
       if(!response.ok)throw new Error('Health check failed');
       const result=await response.json();
       if(typeof result.ready!=='boolean')throw new Error('Invalid health response');
       status.dataset.ready=String(result.ready);
       status.textContent=result.ready?'Ready':'Needs attention';
       time.textContent='Checked '+new Date(result.checkedUtc).toLocaleTimeString();
     } catch {
       status.dataset.ready='false';status.textContent='Check unavailable';
       time.textContent='Refresh to try again.';
     } finally {clearTimeout(timeout);refresh.disabled=false;}
   }
   refresh.addEventListener('click',checkHealth);
   void checkHealth();
 }
 const form=document.querySelector('[data-settings-form]'),dialog=document.querySelector('[data-settings-confirm]');
 if(!form || !dialog)return;
 const destinations = [...document.querySelectorAll('.settings-nav a[href^="#"]')]
   .map(link => ({ link, section: document.getElementById(link.hash.slice(1)) }))
   .filter(({ section }) => section);
 if (destinations.length) {
   const root = document.scrollingElement;
   let frame;
   function updateCurrentSection() {
     frame = undefined;
     const padding = parseFloat(getComputedStyle(root).scrollPaddingTop) || 0;
     const margin = parseFloat(getComputedStyle(destinations[0].section).scrollMarginTop) || 0;
     const readingLine = padding + margin + 1;
     const positions = destinations.map(({ link, section }) => ({ link, top: section.getBoundingClientRect().top }));
     let current = positions.findLast(({ top }) => top <= readingLine) ?? positions[0];
     const maxScroll = root.scrollHeight - root.clientHeight;
     if (maxScroll - root.scrollTop <= 1) {
       // Two sections can share the same landing position when the page ends.
       const requested = positions.find(({ link, top }) =>
         link.hash === window.location.hash && top >= readingLine - 2 && top < root.clientHeight);
       current = requested ?? (maxScroll > 1 ? positions.at(-1) : current);
     }
     for (const { link } of positions) {
       if (link === current.link) link.setAttribute('aria-current', 'location');
       else link.removeAttribute('aria-current');
     }
   }
   function scheduleUpdate() {
     if (frame === undefined) frame = requestAnimationFrame(updateCurrentSection);
   }
   window.addEventListener('scroll', scheduleUpdate, { passive: true });
   window.addEventListener('resize', scheduleUpdate);
   window.addEventListener('hashchange', scheduleUpdate);
   window.addEventListener('pageshow', scheduleUpdate);
   scheduleUpdate();
 }
 let confirmed=false;
 form.addEventListener('submit',event=>{
   if(!confirmed && dialog.showModal){event.preventDefault();dialog.showModal();return;}
   form.setAttribute('aria-busy','true');document.querySelectorAll('[form="settings-form"][type=submit]').forEach(button=>{button.disabled=true;window.HoleInHubButtons?.setLabel(button,'Saving…');});
 });
 dialog.querySelector('[data-settings-cancel]').addEventListener('click',()=>dialog.close());
 dialog.querySelector('[data-settings-save]').addEventListener('click',()=>{confirmed=true;dialog.close();form.requestSubmit();});
 form.addEventListener('invalid',()=>window.HoleInHubToasts?.show({kind:'error',title:'Check the highlighted fields',autoDismiss:true}),true);
})();
