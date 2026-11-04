const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../HoleInHub.Web/wwwroot/js/account-menu.js'), 'utf8');
function setup() {
    const triggerEvents={}, popupEvents={}, modalEvents={}, formEvents={}, windowEvents={};
    const trigger={setAttribute(key,value){this[key]=value;},addEventListener:(name,callback)=>triggerEvents[name]=callback};
    const popup={id:'account-menu',open:false,showPopover(){this.open=true;popupEvents.toggle?.({newState:'open'});},hidePopover(){this.open=false;popupEvents.toggle?.({newState:'closed'});},matches(){return this.open;},contains:()=>false,addEventListener:(name,callback)=>popupEvents[name]=callback};
    const cancel={},logout={focus(){this.focused=true;}};
    const confirmation={open:false,showModal(){this.open=true;},close(){this.open=false;modalEvents.close();},addEventListener:(name,callback)=>modalEvents[name]=callback,querySelectorAll:()=>[{addEventListener:(_,callback)=>cancel.click=callback}]};
    const form={addEventListener:(name,callback)=>formEvents[name]=callback,querySelector:()=>logout};
    vm.runInNewContext(source,{document:{querySelector:selector=>({'.profile-link':trigger,'#account-menu':popup,'#logout-confirmation':confirmation,'#account-logout-form':form}[selector]||null)},window:{matchMedia:()=>({matches:true,addEventListener(){}}),addEventListener:(name,callback)=>windowEvents[name]=callback}});
    const open=(options={})=>{const event={...options,preventDefault(){this.prevented=true;}};triggerEvents.click(event);return event;};
    return {trigger,popup,confirmation,formEvents,popupEvents,windowEvents,cancel,logout,open,pointerDown:()=>triggerEvents.pointerdown()};
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
