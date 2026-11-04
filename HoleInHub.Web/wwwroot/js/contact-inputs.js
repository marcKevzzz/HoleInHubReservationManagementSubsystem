const ContactInputs={
 name:value=>value.replace(/[\p{N}]/gu,''),
 phone:value=>value.replace(/[^0-9]/g,'')
};
if(typeof module!=='undefined')module.exports=ContactInputs;
if(typeof document!=='undefined'){
 document.querySelectorAll('input[autocomplete="given-name"],input[autocomplete="family-name"],input[autocomplete="tel"]').forEach(input=>{
  const clean=input.autocomplete==='tel'?ContactInputs.phone:ContactInputs.name;
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
 });
}
