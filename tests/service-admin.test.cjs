const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const vm=require('node:vm');
const readScript = name => fs.readFileSync(require('node:path').join(__dirname, '../HoleInHub.Web/wwwroot/js', name), 'utf8');

test('pricing summaries update after switching a band to a specific date', () => {
    const fields = {
        Day: { value: 'Monday' }, Date: { value: '2026-11-17' }, Start: { value: '15:00' },
        End: { value: '' }, EndsAtMidnight: { checked: true }, Rate: { value: '800' }
    };
    const dayLabel = {}, rateLabel = {}, baseLabel = {};
    const row = {
        querySelector(selector) {
            if (selector === '[data-rule-day]') return dayLabel;
            if (selector === '[data-rule-rate]') return rateLabel;
            return fields[selector.split('"')[1].slice(1)];
        }
    };
    const form = { elements: { BookingService: { value: 'Regular', addEventListener() {} } } };
    const events = {};
    const section = {
        closest: () => form, querySelectorAll: () => [],
        querySelector: selector => selector === '[data-base-price-label]' ? baseLabel : null,
        addEventListener(name, fn) { events[name] = fn; }
    };
    vm.runInNewContext(readScript('service-pricing.js'), {
        document: { querySelector: () => section }
    });
    fields.Date.closest = () => row;
    events.change({ target: fields.Date });
    assert.equal(fields.Day.value, '');
    assert.equal(dayLabel.textContent, '2026-11-17');
    assert.match(rateLabel.textContent, /15:00–24:00 · ₱800/);
});

class Element {
    constructor(){this.children=[];this.events={};this.dataset={};this.style={setProperty:(name,value)=>this.style[name]=value,removeProperty:name=>delete this.style[name]};this.value='';this.checked=false;this.files=[];}
    after(element){this.nextElementSibling=element;}
    getAttribute(name){return this[name] || "Unit";}
    dispatchEvent(event){this.events[event.type]?.(event);}
    focus(){}
    append(...children){this.children.push(...children);}
    replaceChildren(...children){this.children=children;}
    setAttribute(name,value){this[name]=value;}
    addEventListener(name,fn){this.events[name]=fn;}
    querySelector(selector){return this.map?.[selector];}
}
function setup(count, enhance=false, motion, reduced=false, layout=false){
    const gallery=new Element(), add=new Element(), uploads=new Element(), error=new Element();
    gallery.querySelectorAll=()=>Array.from({length:count},(_,i)=>({dataset:{path:'/media/image'+i},querySelector:()=>({src:'/media/image'+i})}));
    const deposit=new Element(), enabled=new Element(), unit=new Element(), wrap=new Element(), amount=new Element();unit.value='Peso';wrap.map={input:amount};
    const viewer=new Element(), content=new Element(), close=new Element();viewer.map={'[data-viewer-content]':content,'[data-viewer-close]':close};viewer.showModal=()=>viewer.open=true;viewer.close=()=>viewer.open=false;
    const form=new Element(),rate=new Element(),rateUnit=new Element();rateUnit.value='Peso';
    const typePicker=new Element(),newType=new Element(),newTypeInput=new Element();typePicker.value='Regular bay';newType.map={input:newTypeInput};
    if(enhance){unit.options=rateUnit.options=[{value:'Peso',textContent:'₱'},{value:'Percentage',textContent:'%'}];form.querySelectorAll=()=>[unit,rateUnit];}
    form.map={'[data-deposit-type]':deposit,'[data-deposit-enabled]':enabled,'[data-deposit-unit]':unit,'[data-deposit-value]':wrap,'[name=RateAmount]':rate,'[name=RateUnit]':rateUnit,'[data-service-gallery]':gallery,'[data-image-add]':add,'[data-service-uploads]':uploads,'[data-images-error]':error};
    form.map['[data-booking-type]']=typePicker;form.map['[data-new-booking-type]']=newType;
    const resizeCallbacks=[];
    if(layout){const details=new Element();details.getBoundingClientRect=()=>({bottom:600+(enabled.checked?100:0)+(typePicker.value==='__new'?80:0)});gallery.getBoundingClientRect=()=>({top:100});wrap.getBoundingClientRect=()=>({height:100});newType.getBoundingClientRect=()=>({height:80});form.map['.service-detail-fields']=details;}
    class Transfer{constructor(){this.files=[];this.items={add:file=>this.files.push(file)};}}
    vm.runInNewContext(fs.readFileSync(require('node:path').join(__dirname,'../HoleInHub.Web/wwwroot/js/service-admin.js'),'utf8'),{document:{addEventListener(){},querySelector:s=>s==='[data-service-editor]'?form:s==='[data-image-viewer]'?viewer:null,createElement:()=>new Element()},window:{innerWidth:1200,gsap:motion,matchMedia:()=>({matches:reduced}),addEventListener(){}},ResizeObserver:class{constructor(callback){resizeCallbacks.push(callback);}observe(){}},DataTransfer:Transfer,URL:{revokeObjectURL(){},createObjectURL:()=>'/blob/new'},Event:class {constructor(type){this.type=type;}},HTMLElement:Element});
    return {gallery,viewer,enabled,unit,amount,amountWrap:wrap,deposit,uploads,error,form,rate,typePicker,newType,newTypeInput,resize:()=>resizeCallbacks.forEach(callback=>callback())};
}
test('image composition fills the base details column without growing for optional fields',()=>{
    const ui=setup(1,false,undefined,true,true);ui.resize();
    assert.equal(ui.gallery.style['--service-gallery-height'],'500px');
    ui.enabled.checked=true;ui.enabled.events.change();ui.resize();
    assert.equal(ui.gallery.style['--service-gallery-height'],'500px');
    ui.typePicker.value='__new';ui.typePicker.events.change();ui.resize();
    assert.equal(ui.gallery.style['--service-gallery-height'],'500px');
});
test('choosing Add new reveals a required booking type and switching back disables it',()=>{
    let transitions=0;
    const ui=setup(1,false,{set(){},killTweensOf(){},fromTo(){transitions++;}},false);
    assert.equal(ui.newType.hidden,true);assert.equal(ui.newTypeInput.disabled,true);
    ui.typePicker.value='__new';ui.typePicker.events.change();
    assert.equal(ui.newType.hidden,false);assert.equal(ui.newTypeInput.required,true);assert.equal(transitions,1);
    ui.typePicker.value='VIP bay';ui.typePicker.events.change();
    assert.equal(ui.newType.hidden,true);assert.equal(ui.newTypeInput.required,false);
});
test('gallery caps previews, exposes overflow, and persists a reordered primary',()=>{
    const ui=setup(6);
    assert.equal(ui.gallery.dataset.count,'4');
    let tiles=ui.gallery.children.filter(x=>x.className==='service-image-tile');
    assert.equal(tiles.length,4);
    tiles[3].children[0].events.click();assert.equal(ui.viewer.open,true);
    tiles[2].children[2].events.click();
    assert.equal(ui.gallery.children.find(x=>x.name==='ImageOrder').value,'/media/image2');
    assert.equal(ui.gallery.children.filter(x=>x.name==='Images').length,6);
});
test('deposit switch sets unit and validation; invalid uploads keep the gallery',()=>{
    const ui=setup(1);
    assert.equal(ui.deposit.value,'None');assert.equal(ui.amount.disabled,true);
    ui.enabled.checked=true;ui.unit.value='Percentage';ui.unit.events.change();
    assert.equal(ui.deposit.value,'Percentage');assert.equal(ui.amount.max,'100');assert.equal(ui.amount.required,true);
    ui.uploads.files=[{type:'image/svg+xml',size:10}];ui.uploads.events.change();
    assert.match(ui.error.textContent,/JPEG/);assert.equal(ui.gallery.children.filter(x=>x.name==='Images').length,1);
});
for (const failed of [false,true]) {
test(failed?'failed service reorder restores the list and shakes the affected row':'keyboard service reorder saves positions and refreshes revisions',async()=>{
    const list=new Element(), form=new Element();form.action='/admin/services/reorder';
    let shakes=0;
    const make=id=>{const row=new Element();row.dataset={serviceId:id,revision:'1'};row.offsetWidth=400;row.classList={add(){},remove(){}};row.position=new Element();row.revision=new Element();row.handle={focus(){},closest:()=>row};row.querySelector=s=>s==='[data-service-position]'?row.position:row.handle;row.querySelectorAll=()=>[row.revision];return row;};
    const first=make('Regular'),second=make('VIP');list.children=[first,second];
    first.animate=()=>{shakes++;};
    list.querySelectorAll=s=>s==='[data-service-id]'?list.children:[];
    list.insertBefore=(row,before)=>{list.children=list.children.filter(x=>x!==row);const index=list.children.indexOf(before);list.children.splice(index<0?list.children.length:index,0,row);};
    list.append=row=>{list.children=list.children.filter(x=>x!==row);list.children.push(row);};
    list.removeAttribute=()=>{};
    const sent=[];
    class Form{set(name,value){sent.push(JSON.parse(value));}}
    vm.runInNewContext(fs.readFileSync(require('node:path').join(__dirname,'../HoleInHub.Web/wwwroot/js/service-admin.js'),'utf8'),{document:{addEventListener(){},querySelector:s=>s==='[data-service-list]'?list:s==='[data-service-reorder]'?form:null},window:{addEventListener(){},matchMedia:()=>({matches:false}),HoleInHubToasts:{show(){}}},FormData:Form,fetch:async()=>({ok:!failed,json:async()=>({rows:[{id:'VIP',revision:2},{id:'Regular',revision:2}]})})});
    list.events.keydown({altKey:true,key:'ArrowDown',preventDefault(){},target:{closest:()=>first.handle}});
    await new Promise(resolve=>setImmediate(resolve));
    assert.deepEqual(list.children.map(row=>row.dataset.serviceId),failed?['Regular','VIP']:['VIP','Regular']);
    assert.deepEqual(sent[0].map(row=>row.id),['VIP','Regular']);
    assert.equal(first.dataset.revision,failed?'1':'2');assert.equal(second.position.textContent,failed?'02':'01');
    assert.equal(shakes,failed?1:0);
});
}

test('pointer drag previews order and numbers before release, and cancellation restores them',()=>{
    const list=new Element(), post=new Element();let scheduled,ghostPreview;
    const documentEvents={};const rootClass={add(){},remove(){}};
    const make=id=>{const row=new Element();row.dataset={serviceId:id,revision:'1'};Object.defineProperty(row,'nextElementSibling',{get:()=>list.children[list.children.indexOf(row)+1]||null});row.position=new Element();row.classList=rootClass;row.handle={closest:()=>row,focus(){}};row.getBoundingClientRect=()=>({left:0,right:500,width:500,height:100,top:list.children.indexOf(row)*100});row.setPointerCapture=()=>{throw Error('Capture must stay on the stable list');};row.querySelector=s=>s==='[data-service-position]'?row.position:row.handle;row.querySelectorAll=()=>[];row.cloneNode=()=>{const ghost=new Element();ghostPreview=ghost;ghost.classList=rootClass;ghost.querySelector=()=>new Element();ghost.querySelectorAll=()=>[];ghost.getBoundingClientRect=()=>({left:0,top:0});ghost.remove=()=>{};return ghost;};return row;};
    const first=make('Regular'),second=make('VIP'),third=make('Coach');list.children=[first,second,third];
    let captured;list.setPointerCapture=id=>{captured=id;};list.hasPointerCapture=id=>captured===id;list.releasePointerCapture=()=>{captured=undefined;};
    list.querySelectorAll=s=>s==='[data-service-id]'?list.children:[];list.getBoundingClientRect=()=>({left:0,right:500,top:0,bottom:300});
    list.insertBefore=(row,before)=>{list.children=list.children.filter(item=>item!==row);const at=list.children.indexOf(before);list.children.splice(at<0?list.children.length:at,0,row);};list.append=row=>{list.children=list.children.filter(item=>item!==row);list.children.push(row);};
    vm.runInNewContext(fs.readFileSync(require('node:path').join(__dirname,'../HoleInHub.Web/wwwroot/js/service-admin.js'),'utf8'),{document:{body:{append(){}},documentElement:{classList:rootClass},addEventListener:(name,fn)=>documentEvents[name]=fn,removeEventListener(){},querySelector:s=>s==='[data-service-list]'?list:s==='[data-service-reorder]'?post:null},window:{addEventListener(){},matchMedia:()=>({matches:true}),getSelection:()=>null,scrollBy(){}},getComputedStyle:()=>({gridTemplateColumns:'44px 140px 1fr auto',gap:'24px',padding:'24px 0px',borderWidth:'0px 0px 1px',boxSizing:'border-box'}),innerWidth:800,innerHeight:600,requestAnimationFrame:fn=>{scheduled=fn;return 1;},cancelAnimationFrame(){},setTimeout(){},fetch(){throw Error('Must not save before release');}});
    list.events.pointerdown({target:{closest:s=>s==='[data-service-id]'?first:first.handle},button:0,pointerId:1,clientX:20,clientY:20});
    list.events.pointermove({pointerId:1,clientX:20,clientY:280,preventDefault(){}});
    assert.deepEqual(list.children.map(row=>row.dataset.serviceId),['VIP','Coach','Regular']);assert.equal(second.position.textContent,'01');assert.equal(first.position.textContent,'03');assert.equal(typeof scheduled,'function');assert.equal(ghostPreview.style.padding,'24px 0px');assert.equal(ghostPreview.style.gap,'24px');
    assert.equal(captured,1);
    // Reordering may emit capture loss on the moved row; the list keeps the real capture.
    list.events.lostpointercapture({pointerId:1,target:first});
    list.events.pointermove({pointerId:1,clientX:20,clientY:10,preventDefault(){}});scheduled();
    assert.equal(captured,1);assert.deepEqual(list.children.map(row=>row.dataset.serviceId),['Regular','VIP','Coach']);
    list.events.pointermove({pointerId:1,clientX:20,clientY:280,preventDefault(){}});scheduled();
    assert.equal(captured,1);assert.equal(first.position.textContent,'03');
    list.events.pointercancel();assert.equal(captured,undefined);assert.deepEqual(list.children.map(row=>row.dataset.serviceId),['Regular','VIP','Coach']);assert.equal(first.position.textContent,'01');
});

test('deposit expands and collapses with easing and hides only after the closing animation',()=>{
    const animations=[];const motion={killTweensOf(){},set(){},to(target,options){animations.push({target,options});}};
    const ui=setup(1,false,motion);
    assert.equal(ui.amountWrap.hidden,true);
    ui.enabled.checked=true;ui.enabled.events.change();
    assert.equal(ui.amountWrap.hidden,false);assert.equal(animations[0].options.height,'auto');assert.equal(animations[0].options.ease,'power2.out');
    ui.enabled.checked=false;ui.enabled.events.change();
    assert.equal(ui.amountWrap.hidden,false);assert.equal(animations[1].options.height,0);
    animations[1].options.onComplete();assert.equal(ui.amountWrap.hidden,true);
});

for (const reduced of [false,true]) {
test(`invalid service fields and images respect reduced motion (${reduced})`,()=>{
    const ui=setup(1,false,undefined,reduced);
    let fieldShakes=0, galleryShakes=0;
    ui.rate.animate=()=>{fieldShakes++;};
    const section = { open: false, parentElement: { closest: () => null } };
    ui.rate.name='RateAmount';ui.rate.validationMessage='Enter a rate.';ui.rate.form=ui.form;
    ui.rate.closest=selector=>selector==='details'?section:null;
    ui.form.querySelector=()=>ui.rate;
    ui.form.events.invalid({target:ui.rate,preventDefault(){}});
    assert.equal(section.open,true);
    assert.equal(fieldShakes,reduced?0:1);
    ui.gallery.animate=()=>{galleryShakes++;};
    ui.uploads.files=[{type:'image/svg+xml',size:10}];ui.uploads.events.change();
    assert.equal(galleryShakes,reduced?0:1);
});
}
