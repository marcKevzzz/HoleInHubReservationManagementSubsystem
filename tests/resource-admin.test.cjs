const {test}=require('node:test');
const assert=require('node:assert/strict');
const vm=require('node:vm');
const fs=require('node:fs');
class Element {
    constructor(){this.events={};this.map={};this.controls=[];this.dataset={};this.hidden=false;this.disabled=false;}
    querySelector(selector){return this.map[selector];}
    querySelectorAll(){return this.controls;}
    addEventListener(name,fn){this.events[name]=fn;}
    append(child){this.content=child;}
    showModal(){this.open=true;}
    close(){this.open=false;this.events.close?.();}
    focus(){this.focused=true;}
    setAttribute(){}
    removeAttribute(){}
    reportValidity(){return true;}
}
function setup(fetch){
    const page=new Element(),add=new Element(),confirm=new Element(),link=new Element(),form=new Element(),state=new Element(),trigger=new Element(),commit=new Element();
    for(const dialog of [add,confirm]){dialog.map['[data-resource-cancel]']=new Element();dialog.map['[data-resource-error]']=new Element();dialog.controls=[dialog.map['[data-resource-cancel]']];}
    add.map.form=form;add.map['[name=name]']=new Element();
    confirm.map.h2=new Element();confirm.map['#resource-confirm-description']=new Element();confirm.map['[data-resource-commit]']=commit;confirm.controls.push(commit);
    state.elements={active:{value:'false'}};state.dataset={bayName:'Bay 1',bayId:'bay-1'};state.map.button=trigger;
    page.map={'[data-resource-add-dialog]':add,'[data-resource-confirm]':confirm,'[data-resource-add-content]':new Element(),'[data-resource-add]':link};page.controls=[state];
    const notices=[],navigations=[];
    vm.runInNewContext(fs.readFileSync(require('node:path').join(__dirname,'../HoleInHub.Web/wwwroot/js/resource-admin.js'),'utf8'),{document:{querySelector:()=>page},window:{HoleInHubToasts:{show:n=>notices.push(n)},location:{assign:url=>navigations.push(url)}},location:{origin:'http://localhost'},fetch,FormData:class{},AbortController,setTimeout,clearTimeout,URL});
    return {add,confirm,link,form,state,trigger,commit,notices,navigations};
}
const event=target=>({currentTarget:target,preventDefault(){}});
const settle=()=>new Promise(resolve=>setImmediate(resolve));
test('add modal focuses name; cancel and Escape restore focus without submitting',()=>{
    const ui=setup(()=>{throw Error('Unexpected submission');});
    ui.link.events.click(event(ui.link));assert.equal(ui.add.open,true);assert.equal(ui.add.map['[name=name]'].focused,true);
    ui.add.map['[data-resource-cancel]'].events.click();assert.equal(ui.add.open,false);assert.equal(ui.link.focused,true);
    ui.state.events.submit(event(ui.state));assert.equal(ui.confirm.open,true);assert.match(ui.confirm.map.h2.textContent,/Retire Bay 1/);assert.match(ui.confirm.map['#resource-confirm-description'].textContent,/whole-shop/);
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
    ui.commit.events.click();await settle();assert.deepEqual(ui.navigations,['http://localhost/admin/resources']);
});
test('network failure preserves form and explains uncertain save',async()=>{
    const ui=setup(async()=>{throw Error('network');});
    ui.link.events.click(event(ui.link));ui.form.events.submit(event(ui.form));await settle();
    assert.equal(ui.add.open,true);assert.match(ui.add.map['[data-resource-error]'].textContent,/may already have saved/);assert.equal(ui.notices.length,1);
});
