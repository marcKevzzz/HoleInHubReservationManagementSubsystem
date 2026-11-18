const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm'),path=require('node:path');
test('settings save confirms once before submitting and exposes progress',()=>{
 const events={},buttons={},form={addEventListener:(name,fn)=>events[name]=fn,setAttribute(name,value){this[name]=value;},requestSubmit(){events.submit({preventDefault(){throw Error('Confirmed submit must proceed');}});}};
 const dialog={showModal(){this.open=true;},close(){this.open=false;},querySelector:s=>({addEventListener:(name,fn)=>buttons[s]=fn})};
 vm.runInNewContext(fs.readFileSync(path.join(__dirname,'../HoleInHub.Web/wwwroot/js/settings.js'),'utf8'),{document:{querySelector:s=>s==='[data-settings-health]'?null:s==='[data-settings-form]'?form:dialog,querySelectorAll:()=>[]},window:{}});
 let prevented=false;events.submit({preventDefault(){prevented=true;}});assert.equal(prevented,true);assert.equal(dialog.open,true);
 buttons['[data-settings-save]']();assert.equal(dialog.open,false);assert.equal(form['aria-busy'],'true');
});

test('System health becomes current when its heading cannot reach the top of the viewport', () => {
 const events = {};
 const frames = [];
 const root = { scrollHeight: 1800, clientHeight: 700, scrollTop: 0 };
 const sections = Object.fromEntries(Object.entries({ operations: 100, rules: 1150, health: 1500 })
   .map(([id, top]) => [id, { getBoundingClientRect: () => ({ top: top - root.scrollTop }) }]));
 const links = Object.keys(sections).map(id => ({
   hash: '#' + id,
   setAttribute(name, value) { this[name] = value; },
   removeAttribute(name) { delete this[name]; }
 }));
 const location = { hash: '' };
 const form = { addEventListener() {} };
 const dialog = { querySelector: () => ({ addEventListener() {} }) };
 vm.runInNewContext(fs.readFileSync(path.join(__dirname,'../HoleInHub.Web/wwwroot/js/settings.js'),'utf8'), {
   document: {
     scrollingElement: root,
     querySelector: selector => ({ '[data-settings-form]': form, '[data-settings-confirm]': dialog })[selector],
     querySelectorAll: () => links,
     getElementById: id => sections[id]
   },
   window: { location, addEventListener: (name, handler) => events[name] = handler },
   getComputedStyle: () => ({ scrollPaddingTop: '0px', scrollMarginTop: '24px' }),
   requestAnimationFrame: handler => frames.push(handler)
 });
 const current = () => links.find(link => link['aria-current'] === 'location')?.hash;
 frames.shift()();
 assert.equal(current(), '#operations');
 root.scrollTop = 1100;
 location.hash = '#rules';
 events.scroll();
 frames.shift()();
 assert.equal(current(), '#rules');
 location.hash = '#health';
 events.hashchange();
 frames.shift()();
 assert.equal(current(), '#health');
});
