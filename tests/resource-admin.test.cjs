const {test}=require('node:test');
const assert=require('node:assert/strict');
const vm=require('node:vm');
const fs=require('node:fs');
class Element {
    constructor(){this.events={};this.map={};this.controls=[];this.dataset={};this.hidden=false;this.disabled=false;}
    querySelector(selector){return this.map[selector];}
    querySelectorAll(selector){return this.lists?.[selector] || this.controls;}
    addEventListener(name,fn){const previous=this.events[name];this.events[name]=previous?event=>{previous(event);fn(event);}:fn;}
    append(child){this.content=child;}
    showModal(){this.open=true;}
    close(){this.open=false;this.events.close?.();}
    focus(){this.focused=true;}
    dispatchEvent(event){this.events[event.type]?.(event);}
    getBoundingClientRect(){return {left:700,right:1200,top:0,bottom:800};}
    setAttribute(){}
    removeAttribute(){}
    reportValidity(){return true;}
}
function setup(fetch,motion,reduced=true){
    const page=new Element(),add=new Element(),edit=new Element(),editForm=new Element(),editButton=new Element(),confirm=new Element(),link=new Element(),form=new Element(),state=new Element(),trigger=new Element(),commit=new Element();
    for(const dialog of [add,edit,confirm]){dialog.map['[data-resource-cancel]']=new Element();dialog.map['[data-resource-error]']=new Element();dialog.controls=[dialog.map['[data-resource-cancel]']];}
    for(const dialog of [add,edit])dialog.map['[data-resource-close]']=new Element();
    edit.map.form=editForm;
    for(const bayForm of [form,editForm]){
        bayForm.elements=Object.fromEntries(['id','name','service','expectedRevision','maxPlayers','maxTotalGuests','maxNonPlayers','capacityAcknowledgement'].map(name=>[name,{value:''}]));
        bayForm.elements.name=new Element();
    }
    editButton.dataset={bayId:'studio',bayName:'VIP Studio',bayService:'VIP',bayRevision:'3',bayMaxPlayers:'4',bayMaxTotalGuests:'12',bayMaxNonPlayers:'2'};
    add.map.form=form;add.map['[name=name]']=new Element();
    confirm.map.h2=new Element();confirm.map['#resource-confirm-description']=new Element();confirm.map['[data-resource-commit]']=commit;confirm.controls.push(commit);
    state.elements={active:{value:'false'}};state.dataset={bayName:'Bay 1',bayId:'bay-1'};state.map.button=trigger;
    page.map={'[data-resource-edit-dialog]':edit,'[data-resource-add-dialog]':add,'[data-resource-confirm]':confirm,'[data-resource-add-content]':new Element(),'[data-resource-add]':link};page.lists={'[data-resource-state]':[state],'[data-resource-edit]':[editButton]};
    const notices=[],navigations=[];
    vm.runInNewContext(fs.readFileSync(require('node:path').join(__dirname,'../HoleInHub.Web/wwwroot/js/resource-admin.js'),'utf8'),{document:{querySelector:()=>page},window:{gsap:motion,matchMedia:()=>({matches:reduced}),HoleInHubToasts:{show:n=>notices.push(n)},location:{assign:url=>navigations.push(url)}},location:{origin:'http://localhost'},fetch,FormData:class{},Event,AbortController,setTimeout,clearTimeout,URL});
    return {add,edit,editForm,editButton,confirm,link,form,state,trigger,commit,notices,navigations};
}
const event=target=>({currentTarget:target,preventDefault(){}});
const settle=()=>new Promise(resolve=>setImmediate(resolve));
for(const reduced of [false,true])test(`bay panel outside dismissal and transition honor reduced motion (${reduced})`,()=>{
    let entrances=0,exits=0;
    const motion={killTweensOf(){},set(){},fromTo(){entrances++;},to(_target,options){exits++;options.onComplete();}};
    const ui=setup(()=>{throw Error('No save expected');},motion,reduced);
    ui.link.events.click(event(ui.link));assert.equal(entrances,reduced?0:1);
    const inside={target:ui.add,clientX:900,clientY:100},outside={target:ui.add,clientX:200,clientY:100};
    ui.add.events.pointerdown(inside);ui.add.events.click(outside);assert.equal(ui.add.open,true);
    ui.add.events.pointerdown(outside);ui.add.events.click(outside);assert.equal(ui.add.open,false);
    assert.equal(exits,reduced?0:1);assert.equal(ui.link.focused,true);
});
test('edit panel prefills identity and original values and saves through the shared error handling',async()=>{
    const ui=setup(async()=>({ok:false,json:async()=>({error:'Reload the inventory.'})}));
    ui.editButton.events.click();
    assert.equal(ui.edit.open,true);assert.equal(ui.editForm.elements.id.value,'studio');assert.equal(ui.editForm.elements.expectedRevision.value,'3');assert.equal(ui.editForm.elements.service.value,'VIP');assert.equal(ui.editForm.elements.name.focused,true);
    assert.equal(ui.editForm.elements.maxTotalGuests.value,'12');
    assert.equal(ui.editForm.elements.maxPlayers.value,'4');
    assert.equal(ui.editForm.elements.maxNonPlayers.value,'2');
    ui.editForm.events.submit(event(ui.editForm));await settle();
    assert.equal(ui.edit.open,true);assert.equal(ui.notices[0].message,'Reload the inventory.');
    ui.edit.map['[data-resource-close]'].events.click();assert.equal(ui.editButton.focused,true);
});
test('capacity warning requires a second save and is cleared on edits',async()=>{
    const ui=setup(async()=>({ok:false,json:async()=>({error:'Affects HIH-1.',acknowledgement:'revision-3'})}));
    ui.editButton.events.click();
    ui.editForm.events.submit(event(ui.editForm));await settle();
    assert.equal(ui.editForm.elements.capacityAcknowledgement.value,'revision-3');
    assert.match(ui.edit.map['[data-resource-error]'].textContent,/HIH-1.*Save bay again/);
    ui.editForm.events.input();
    assert.equal(ui.editForm.elements.capacityAcknowledgement.value,'');
});
test('add modal focuses name; cancel and Escape restore focus without submitting',()=>{
    const ui=setup(()=>{throw Error('Unexpected submission');});
    ui.link.events.click(event(ui.link));assert.equal(ui.add.open,true);assert.equal(ui.add.map['[name=name]'].focused,true);
    ui.add.map['[data-resource-close]'].events.click();assert.equal(ui.add.open,false);assert.equal(ui.link.focused,true);
    ui.state.events.submit(event(ui.state));assert.equal(ui.confirm.open,true);assert.match(ui.confirm.map.h2.textContent,/Retire Bay 1/);assert.match(ui.confirm.map['#resource-confirm-description'].textContent,/whole-shop/);
    assert.equal(ui.commit.dataset.danger,'true');
    ui.confirm.close();assert.equal(ui.trigger.focused,true);
});
test('conflicts stay in modal with inline error and shared toast; submission cannot repeat while saving',async()=>{
    let release,calls=0;
    const ui=setup(()=>{calls++;return new Promise(resolve=>release=resolve);});
    ui.state.events.submit(event(ui.state));ui.commit.events.click();ui.commit.events.click();assert.equal(calls,1);assert.equal(ui.commit.disabled,true);
    release({ok:false,json:async()=>({error:'Resolve upcoming reservations first.'})});await settle();
    assert.equal(ui.confirm.open,true);assert.equal(ui.commit.disabled,false);assert.equal(ui.confirm.map['[data-resource-error]'].hidden,false);assert.equal(ui.notices[0].message,'Resolve upcoming reservations first.');
});
test('restore uses distinct confirmation; success navigates to inventory',async()=>{
    const ui=setup(async()=>({ok:true,json:async()=>({redirect:'/admin/resources'})}));
    ui.state.elements.active.value='true';ui.state.events.submit(event(ui.state));assert.match(ui.confirm.map.h2.textContent,/Restore Bay 1/);
    assert.equal(ui.commit.dataset.danger,'false');
    ui.commit.events.click();await settle();assert.deepEqual(ui.navigations,['http://localhost/admin/resources']);
});
test('network failure preserves form and explains uncertain save',async()=>{
    const ui=setup(async()=>{throw Error('network');});
    ui.link.events.click(event(ui.link));ui.form.events.submit(event(ui.form));await settle();
    assert.equal(ui.add.open,true);assert.match(ui.add.map['[data-resource-error]'].textContent,/may already have saved/);assert.equal(ui.notices.length,1);
});
