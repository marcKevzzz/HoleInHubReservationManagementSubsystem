(() => {
 const form=document.querySelector('[data-settings-form]'),dialog=document.querySelector('[data-settings-confirm]');
 if(!form || !dialog)return;
 let confirmed=false;
 form.addEventListener('submit',event=>{
   if(!confirmed && dialog.showModal){event.preventDefault();dialog.showModal();return;}
   form.setAttribute('aria-busy','true');document.querySelectorAll('[form="settings-form"][type=submit]').forEach(button=>{button.disabled=true;window.HoleInHubButtons?.setLabel(button,'Saving…');});
 });
 dialog.querySelector('[data-settings-cancel]').addEventListener('click',()=>dialog.close());
 dialog.querySelector('[data-settings-save]').addEventListener('click',()=>{confirmed=true;dialog.close();form.requestSubmit();});
 form.addEventListener('invalid',()=>window.HoleInHubToasts?.show({kind:'error',title:'Check the highlighted fields',autoDismiss:true}),true);
})();
