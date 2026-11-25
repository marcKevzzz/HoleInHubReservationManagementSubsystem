const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../HoleInHub.Web/wwwroot/js/account-menu.js'), 'utf8');
function setup(withPopup = true, fetch = undefined, deferred = false) {
    const triggerEvents={}, popupEvents={}, modalEvents={}, formEvents={}, windowEvents={}, documentEvents={};
    const badges=[{hidden:deferred},{hidden:deferred}];badges.forEach(badge=>badge.hasAttribute=name=>deferred&&name==='data-unread-deferred');
    const trigger={setAttribute(key,value){this[key]=value;},addEventListener:(name,callback)=>triggerEvents[name]=callback};
    const popup={id:'account-menu',open:false,showPopover(){this.open=true;popupEvents.toggle?.({newState:'open'});},hidePopover(){this.open=false;popupEvents.toggle?.({newState:'closed'});},matches(){return this.open;},contains:()=>false,addEventListener:(name,callback)=>popupEvents[name]=callback};
    const cancel={},logout={hasAttribute:()=>false,focus(){this.focused=true;}};
    const confirmation={open:false,showModal(){this.open=true;},close(){this.open=false;modalEvents.close();},addEventListener:(name,callback)=>modalEvents[name]=callback,querySelectorAll:()=>[{addEventListener:(_,callback)=>cancel.click=callback}]};
    const form={addEventListener:(name,callback)=>formEvents[name]=callback,querySelector:()=>logout};
    const document={hidden:false,querySelector:selector=>({'.profile-link':withPopup ? trigger : null,'#account-menu':withPopup ? popup : null,'#logout-confirmation':confirmation,'#account-logout-form':form}[selector]||null),querySelectorAll:()=>badges,addEventListener:(name,callback)=>documentEvents[name]=callback};
    vm.runInNewContext(source,{document,fetch,AbortSignal,window:{matchMedia:()=>({matches:true,addEventListener(){}}),addEventListener:(name,callback)=>windowEvents[name]=callback,setInterval:callback=>windowEvents.poll=callback}});
    const open=(options={})=>{const event={...options,preventDefault(){this.prevented=true;}};triggerEvents.click(event);return event;};
    return {trigger,popup,confirmation,formEvents,popupEvents,windowEvents,documentEvents,document,badges,cancel,logout,open,pointerDown:()=>triggerEvents.pointerdown()};
}
test('account popup is nonmodal and profile control toggles it',()=>{
    const ui=setup();assert.equal(ui.open().prevented,true);assert.equal(ui.popup.open,true);assert.equal(ui.confirmation.open,false);assert.equal(ui.trigger['aria-expanded'],'true');
    ui.open();assert.equal(ui.popup.open,false);assert.equal(ui.trigger['aria-expanded'],'false');
});
test('modified profile clicks stay native and placeholder links stay on the page',()=>{
    const ui=setup();ui.open({ctrlKey:true});assert.equal(ui.popup.open,false);
    let prevented=false;ui.popupEvents.click({target:{closest:()=>true},preventDefault(){prevented=true;}});assert.equal(prevented,true);
});
test('logout requires confirmation, cancel restores popup, confirmed submit is allowed',()=>{
    const ui=setup();ui.open();let prevented=false;
    ui.formEvents.submit({preventDefault(){prevented=true;}});
    assert.equal(prevented,true);assert.equal(ui.confirmation.open,true);assert.equal(ui.popup.open,false);
    ui.cancel.click();assert.equal(ui.confirmation.open,false);assert.equal(ui.popup.open,true);assert.equal(ui.logout.focused,true);
    prevented=false;ui.formEvents.submit({submitter:{hasAttribute:()=>true},preventDefault(){prevented=true;}});assert.equal(prevented,false);
});
test('staff logout uses the same confirmation without a profile popup',()=>{
    const ui=setup(false);
    let prevented=false;
    ui.formEvents.submit({submitter:ui.logout,preventDefault(){prevented=true;}});
    assert.equal(prevented,true);
    assert.equal(ui.confirmation.open,true);
    ui.cancel.click();
    assert.equal(ui.confirmation.open,false);
    assert.equal(ui.logout.focused,true);
    assert.equal(ui.popup.open,false);
});
test('back-forward restoration closes overlays without reopening the account popup',()=>{
    const ui=setup();ui.open();ui.formEvents.submit({preventDefault(){}});ui.windowEvents.pageshow();
    assert.equal(ui.popup.open,false);assert.equal(ui.confirmation.open,false);
});

test('pointer toggle closes even if browser light-dismiss runs before click',()=>{
    const ui=setup();ui.open();
    // Browser dismisses an auto popover between pointerdown and click.
    // Capture the open state before dismissal rather than reopening on click.
    ui.pointerDown();ui.popup.hidePopover();ui.open({detail:1});assert.equal(ui.popup.open,false);
});

test('scroll dismisses the account popup',()=>{
    const ui=setup();ui.open();ui.windowEvents.scroll();assert.equal(ui.popup.open,false);
});

test('account links close the popup before navigation but preserve modified clicks',()=>{
    const ui=setup();ui.open();
    const link={hasAttribute:()=>false};
    const event={target:{closest:selector=>selector==='a[href]'?link:null}};
    ui.popupEvents.click({...event,ctrlKey:true});assert.equal(ui.popup.open,true);
    ui.popupEvents.click(event);assert.equal(ui.popup.open,false);assert.equal(ui.trigger['aria-expanded'],'false');
});

test('customer unread dots stay in sync when the inbox marks rows read',()=>{
    const ui=setup();ui.documentEvents['notifications:read']({detail:0});
    assert.ok(ui.badges.every(badge=>badge.hidden));
    ui.documentEvents['notifications:read']({detail:1});
    assert.ok(ui.badges.every(badge=>!badge.hidden));
});

test('visible pages pick up new unread notifications without a reload',async()=>{
    let calls=0;
    const ui=setup(true,async url=>{
        assert.equal(url,'/notifications/unread');calls++;
        return {ok:true,json:async()=>({unread:true})};
    });
    ui.documentEvents['notifications:read']({detail:0});
    ui.document.hidden=true;await ui.windowEvents.poll();assert.equal(calls,0);
    ui.document.hidden=false;await ui.documentEvents.visibilitychange();
    assert.equal(calls,1);assert.ok(ui.badges.every(badge=>!badge.hidden));
});

test('deferred account badges refresh immediately once for all four tabs',async()=>{let calls=0;const ui=setup(true,async url=>{calls++;assert.equal(url,'/notifications/unread');return {ok:true,redirected:false,json:async()=>({unread:true})};},true);await new Promise(resolve=>setImmediate(resolve));assert.equal(calls,1);assert.ok(ui.badges.every(b=>!b.hidden));});
