const {test}=require('node:test');
const assert=require('node:assert/strict');
const vm=require('node:vm');
const fs=require('node:fs');
function setup(){
    const mode={value:'false',dataset:{},events:{},addEventListener(name,fn){this.events[name]=fn;}};
    const inputs=[{},{}],timed=inputs.map(input=>({querySelectorAll:()=>[input]}));
    const form={elements:{WholeDay:mode,ResourceId:{value:'bay-1',dispatchEvent(){}}},querySelectorAll:()=>timed,events:{},addEventListener(name,fn){this.events[name]=fn;}};
    mode.form=form;mode.dispatchEvent=event=>mode.events[event.type]?.();
    const panel={querySelector:()=>form};
    let opened=0;
    const time={click(){opened++;mode.value='false';}};
    const day={events:{},addEventListener(name,fn){this.events[name]=fn;},click(){this.events.click({preventDefault(){}});}};
    const page={dataset:{},events:{},querySelector:selector=>({'[data-new-block]':time,'[data-new-block-day]':day,'[data-create-block]':panel})[selector],addEventListener(name,fn){this.events[name]=fn;}};
    const document={querySelectorAll:()=>[mode],querySelector:()=>page,addEventListener(){}};
    vm.runInNewContext(fs.readFileSync(require.resolve('../HoleInHub.Web/wwwroot/js/staff-block.js'),'utf8'),{document,Event,queueMicrotask});
    return {mode,form,inputs,timed,page,day,opened:()=>opened};
}
test('whole-day mode disables only time inputs and Block day opens an unsaved all-bay form',()=>{
    const ui=setup();assert.equal(ui.inputs[0].disabled,false);
    ui.day.click();assert.equal(ui.opened(),1);assert.equal(ui.mode.value,'true');assert.equal(ui.form.elements.ResourceId.value,'*');
    assert.equal(ui.inputs.every(input=>input.disabled),true);
    ui.mode.value='false';ui.mode.events.change();assert.equal(ui.timed.some(field=>field.hidden),false);
});
test('timeline shortcuts ignore typing and modifier combinations',()=>{
    const ui=setup();
    function press(key,typing=false,extra={}){let prevented=false;ui.page.events.keydown({key,shiftKey:true,target:{closest:selector=>selector==='.staff-timeline-scroll'?{}:typing?{}:null},preventDefault(){prevented=true;},...extra});return prevented;}
    assert.equal(press('B',true),false);assert.equal(press('D',false,{ctrlKey:true}),false);
    assert.equal(ui.opened(),0);assert.equal(press('B'),true);assert.equal(ui.opened(),1);
    assert.equal(press('D'),true);assert.equal(ui.mode.value,'true');assert.equal(ui.opened(),2);
});
