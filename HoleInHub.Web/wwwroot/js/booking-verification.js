const VerificationTiming={
 state(expires,resend,now){return{expired:now>=expires,seconds:Math.max(0,Math.ceil((expires-now)/1000)),resendSeconds:Math.max(0,Math.ceil((resend-now)/1000))};},
 canResend(state,allowExpired){return state.resendSeconds===0&&(!state.expired||allowExpired);}
};
if(typeof module!=='undefined')module.exports=VerificationTiming;
if(typeof document!=='undefined')(()=>{
 const t=window.HoleInHubText?.t ?? (text=>text);
 const f=window.HoleInHubText?.f ?? ((text,...args)=>text.replace(/\{(\d+)\}/g,(_,index)=>args[index]));
 const form=document.querySelector('form[data-expires]');if(!form)return;
 const expires=Date.parse(form.dataset.expires),resend=Date.parse(form.dataset.resend),serverNow=Date.parse(form.dataset.now),loadedAt=Date.now();
 const status=document.querySelector('#verification-timing'),send=form.querySelector('[name=intent]'),submit=form.querySelector('button:not([name=intent])');
 function update(){const state=VerificationTiming.state(expires,resend,serverNow+Date.now()-loadedAt);send.disabled=!VerificationTiming.canResend(state,form.dataset.resendExpired==='true');submit.disabled=state.expired;
  send.textContent=state.resendSeconds>0?f('Resend code ({0}s)',state.resendSeconds):t('Resend code');
  if(state.expired)status.setAttribute('aria-live','polite');
  status.textContent=state.expired?(form.dataset.resendExpired==='true'?t('Code expired. Resend a code.'):t('Code expired. Return to your booking.')):f('Code expires in {0}:{1}.',Math.floor(state.seconds/60),String(state.seconds%60).padStart(2,'0'));
 }
 update();setInterval(update,1000);
})();
