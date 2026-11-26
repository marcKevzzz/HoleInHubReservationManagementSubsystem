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
function setup(fetch) {
    const page = new Element(), confirm = new Element(), state = new Element();
    const trigger = new Element(), commit = new Element(), cancel = new Element();
    confirm.map = {
        h2: new Element(),
        '#resource-confirm-description': new Element(),
        '[data-resource-commit]': commit,
        '[data-resource-cancel]': cancel,
        '[data-resource-error]': new Element()
    };
    confirm.controls = [commit, cancel];
    state.elements = { active: { value: 'false' } };
    state.dataset = { bayName: 'Bay 1', bayId: 'bay-1' };
    state.map.button = trigger;
    page.map['[data-resource-confirm]'] = confirm;
    page.lists = { '[data-resource-state]': [state] };
    const notices = [], navigations = [];
    vm.runInNewContext(fs.readFileSync(require('node:path').join(__dirname,'../HoleInHub.Web/wwwroot/js/resource-admin.js'),'utf8'), {
        document: { querySelector: selector => selector === '.resource-admin' ? page : null },
        window: { HoleInHubToasts: { show: notice => notices.push(notice) }, location: { assign: url => navigations.push(url) } },
        location: { origin: 'http://localhost' }, fetch, FormData: class {}, AbortController, setTimeout, clearTimeout, URL
    });
    return { confirm, state, trigger, commit, cancel, notices, navigations };
}
const event=target=>({currentTarget:target,preventDefault(){}});
const settle=()=>new Promise(resolve=>setImmediate(resolve));
test('retire confirmation explains availability impact and restores focus on cancel', () => {
    const ui = setup(() => { throw Error('Unexpected submission'); });
    ui.state.events.submit(event(ui.state));
    assert.equal(ui.confirm.open, true);
    assert.match(ui.confirm.map.h2.textContent, /Retire Bay 1/);
    assert.match(ui.confirm.map['#resource-confirm-description'].textContent, /whole-shop/);
    assert.equal(ui.commit.dataset.danger, 'true');
    ui.cancel.events.click();
    assert.equal(ui.confirm.open, false);
    assert.equal(ui.trigger.focused, true);
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
    ui.state.events.submit(event(ui.state));ui.commit.events.click();await settle();
    assert.equal(ui.confirm.open,true);assert.match(ui.confirm.map['[data-resource-error]'].textContent,/may already have saved/);assert.equal(ui.notices.length,1);
});
