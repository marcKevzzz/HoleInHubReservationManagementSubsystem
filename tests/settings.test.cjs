const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm'),path=require('node:path');
test('settings save confirms once before submitting and exposes progress',()=>{
 const events={},buttons={},form={addEventListener:(name,fn)=>events[name]=fn,setAttribute(name,value){this[name]=value;},requestSubmit(){events.submit({preventDefault(){throw Error('Confirmed submit must proceed');}});}};
 const dialog={showModal(){this.open=true;},close(){this.open=false;},querySelector:s=>({addEventListener:(name,fn)=>buttons[s]=fn})};
 vm.runInNewContext(fs.readFileSync(path.join(__dirname,'../HoleInHub.Web/wwwroot/js/settings.js'),'utf8'),{document:{querySelector:s=>s==='[data-settings-form]'?form:dialog,querySelectorAll:()=>[]},window:{}});
 let prevented=false;events.submit({preventDefault(){prevented=true;}});assert.equal(prevented,true);assert.equal(dialog.open,true);
 buttons['[data-settings-save]']();assert.equal(dialog.open,false);assert.equal(form['aria-busy'],'true');
});
