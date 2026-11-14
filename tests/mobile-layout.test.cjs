const {test}=require('node:test');
const assert=require('node:assert/strict');
const vm=require('node:vm');
const fs=require('node:fs');
function setup(){
    let document;
    function element(){return {attributes:{},events:{},style:{removeProperty(){}},querySelector(){return null;},querySelectorAll(){return [];},setAttribute(name,value){this.attributes[name]=value;},getAttribute(name){return this.attributes[name];},removeAttribute(name){delete this.attributes[name];},toggleAttribute(name,value){if(value)this.attributes[name]='';else delete this.attributes[name];},addEventListener(name,fn){this.events[name]=fn;},getClientRects(){return [{}];},closest(){return null;},focus(){document.activeElement=this;},contains(){return false;}};}
    const root=element(),toggle=element(),rail=element(),drawer=element(),main=element(),backdrop=element(),tooltip=element(),status=element(),skeleton=element(),first=element(),last=element();
    rail.querySelectorAll=selector=>selector==='[data-request-count]'?[]:[first,toggle,last];
    main.querySelector=()=>skeleton;
    document={documentElement:root,querySelector:selector=>({'.staff-rail-toggle':toggle,'.staff-rail':rail,'[data-staff-nav-backdrop]':backdrop,'[data-staff-tooltip]':tooltip,'[data-staff-nav-status]':status})[selector],getElementById:id=>id==='main-content'?main:drawer,addEventListener(){}};
    const media={matches:true,events:{},addEventListener(name,fn){this.events[name]=fn;}},reduced={matches:true,addEventListener(){}};
    const writes=[];
    vm.runInNewContext(fs.readFileSync(require.resolve('../HoleInHub.Web/wwwroot/js/staff-sidebar.js'),'utf8'),{document,window:{matchMedia:query=>query.includes('reduced')?reduced:media,addEventListener(){},location:{origin:'http://localhost',href:'http://localhost/staff/dashboard'}},sessionStorage:{getItem:()=>null,setItem:(...args)=>writes.push(args)},clearTimeout(){},setTimeout(){},URL});
    return {root,toggle,rail,drawer,main,backdrop,first,last,media,writes,document};
}
test('phone navigation opens over content, closes by backdrop, and preserves desktop preference',()=>{
    const ui=setup();assert.equal(ui.drawer.inert,true);assert.equal(ui.main.inert,false);
    ui.toggle.events.click();assert.equal(ui.main.inert,true);assert.equal(ui.drawer.inert,false);assert.equal(ui.backdrop.hidden,false);
    assert.equal(ui.toggle.attributes['aria-expanded'],'true');assert.equal(ui.writes.length,0);
    ui.backdrop.events.click();assert.equal(ui.main.inert,false);assert.equal(ui.document.activeElement,ui.toggle);assert.equal(ui.backdrop.hidden,true);
});
test('open phone navigation traps keyboard focus and releases it on Escape or wider screens',()=>{
    const ui=setup();ui.toggle.events.click();ui.document.activeElement=ui.last;
    let prevented=0;ui.rail.events.keydown({key:'Tab',preventDefault(){prevented++;}});assert.equal(ui.document.activeElement,ui.first);
    ui.rail.events.keydown({key:'Tab',shiftKey:true,preventDefault(){prevented++;}});assert.equal(ui.document.activeElement,ui.last);assert.equal(prevented,2);
    ui.rail.events.keydown({key:'Escape',preventDefault(){}});assert.equal(ui.main.inert,false);
    ui.toggle.events.click();ui.media.matches=false;ui.media.events.change();assert.equal(ui.main.inert,false);assert.equal(ui.backdrop.hidden,true);
});
test('visible viewport tracks the keyboard and browser pan without changing zoom settings',()=>{
    const values={},events={};const viewport={height:700,offsetTop:0,addEventListener:(name,fn)=>events[name]=fn};
    vm.runInNewContext(fs.readFileSync(require.resolve('../HoleInHub.Web/wwwroot/js/mobile-viewport.js'),'utf8'),{window:{visualViewport:viewport},document:{documentElement:{style:{setProperty:(name,value)=>values[name]=value}}}});
    assert.equal(values['--app-viewport-height'],'700px');viewport.height=360;viewport.offsetTop=90;events.resize();
    assert.equal(values['--app-viewport-height'],'360px');assert.equal(values['--app-viewport-top'],'90px');
});
