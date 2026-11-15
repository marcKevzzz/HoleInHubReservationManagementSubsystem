const ContactInputs={
 name:value=>value.replace(/[\p{N}]/gu,''),
 phone:value=>(value.trimStart().startsWith('+')?'+':'')+value.replace(/[^0-9]/g,''),
 error(value) {
  if (/^09/.test(value)) return 'Use 11 digits for a number starting with 09.';
  if (/^010/.test(value)) return 'Use 11 digits for a number starting with 010.';
  if (/^\+?639/.test(value)) return 'Use 12 digits for a number starting with 639 or +639.';
  if (/^\+?8210/.test(value)) return 'Use 12 digits for a number starting with 8210 or +8210.';
  if (/^\+?(?:63|82)0/.test(value)) return 'Drop the leading 0 after the country code.';
  if (/^02/.test(value)) return 'Use 9 or 10 digits for a landline starting with 02.';
  return 'Check the contact number’s prefix and digit count.';
 }
};
if(typeof window!=='undefined')window.HoleInHubContacts=ContactInputs;
if(typeof module!=='undefined')module.exports=ContactInputs;
if(typeof document!=='undefined'){
 document.querySelectorAll('input[autocomplete="given-name"],input[autocomplete="family-name"],input[autocomplete="tel"],input[type="tel"]').forEach(input=>{
  const isPhone=input.autocomplete==='tel'||input.type==='tel';
  const clean=isPhone?ContactInputs.phone:ContactInputs.name;
  function sanitize(){
   const before=input.value,after=clean(before);
   if(after===before)return;
   const caret=input.selectionStart;
   input.value=after;
   if(caret!==null&&document.activeElement===input){const next=clean(before.slice(0,caret)).length;input.setSelectionRange(next,next);}
  }
  sanitize();
  input.addEventListener('input',()=>{if(!input.composing)sanitize();});
  input.addEventListener('compositionstart',()=>input.composing=true);
  input.addEventListener('compositionend',()=>{input.composing=false;sanitize();});
  if(isPhone)input.addEventListener('paste',event=>{
   const pasted=event.clipboardData?.getData('text');
   if(!pasted)return;
   event.preventDefault();
   const start=input.selectionStart??input.value.length,end=input.selectionEnd??start;
   const prefix=clean(input.value.slice(0,start)+pasted);
   input.value=clean(input.value.slice(0,start)+pasted+input.value.slice(end));
   if(input.maxLength>0)input.value=input.value.slice(0,input.maxLength);
   input.setSelectionRange(prefix.length,prefix.length);
   input.dispatchEvent(new Event('input',{bubbles:true}));
  });
 });
}
