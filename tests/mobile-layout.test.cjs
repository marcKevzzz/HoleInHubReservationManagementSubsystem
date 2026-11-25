const {test}=require('node:test');
const assert=require('node:assert/strict');
const vm=require('node:vm');
const fs=require('node:fs');
function setup({savedScroll=null,signedIn=false}={}){
    let document;
    function element(){return {attributes:{},events:{},style:{removeProperty(){}},querySelector(){return null;},querySelectorAll(){return [];},setAttribute(name,value){this.attributes[name]=value;},getAttribute(name){return this.attributes[name];},removeAttribute(name){delete this.attributes[name];},toggleAttribute(name,value){if(value)this.attributes[name]='';else delete this.attributes[name];},addEventListener(name,fn){this.events[name]=fn;},getClientRects(){return [{}];},closest(){return null;},focus(){document.activeElement=this;},contains(){return false;}};}
    const root=element(),toggle=element(),rail=element(),drawer=element(),main=element(),backdrop=element(),tooltip=element(),status=element(),skeleton=element(),first=element(),last=element();
    if(signedIn)root.setAttribute('data-staff-sign-in','true');
    main.getBoundingClientRect=()=>({top:-500});
    const scrollbar=element();
    rail.querySelector=selector=>selector==='[data-staff-rail-scrollbar]'?scrollbar:null;
    rail.getBoundingClientRect=()=>({left:0,top:0});
    drawer.getBoundingClientRect=()=>({right:300,top:64});
    drawer.firstElementChild=element();
    drawer.clientHeight=400;
    drawer.scrollHeight=400;
    const scrollListeners=[];
    drawer.addEventListener=(name,listener)=>{
        if (name==='scroll') scrollListeners.push(listener);
        else drawer.events[name]=listener;
    };
    drawer.events.scroll=()=>scrollListeners.forEach(listener=>listener());
    scrollbar.offsetWidth=10;
    let updateScrollbar;
    class ResizeObserver {
        constructor(update){ updateScrollbar=update; }
        observe(){}
    }
    rail.querySelectorAll=selector=>selector==='[data-request-count]'?[]:[first,toggle,last];
    const content=element();content.inert=false;main.children=[skeleton,content];
    skeleton.replaceChildren=()=>{};
    const template={content:{cloneNode(){return {};}}};
    main.querySelector=selector=>selector.includes('template')?template:skeleton;
    const timers=new Map(),windowEvents={},scrolls=[];let timerId=0;
    document={documentElement:root,querySelector:selector=>({'.staff-rail-toggle':toggle,'.staff-rail':rail,'[data-staff-nav-backdrop]':backdrop,'[data-staff-tooltip]':tooltip,'[data-staff-nav-status]':status})[selector],getElementById:id=>({'main-content':main,'staff-rail-content':drawer})[id]??null,events:{},addEventListener(name,fn){this.events[name]=fn;}};
    const media={matches:true,events:{},addEventListener(name,fn){this.events[name]=fn;}},reduced={matches:true,addEventListener(){}};
    const writes=[];
    vm.runInNewContext(fs.readFileSync(require.resolve('../HoleInHub.Web/wwwroot/js/staff-sidebar.js'),'utf8'),{document,window:{matchMedia:query=>query.includes('reduced')?reduced:media,addEventListener:(name,fn)=>windowEvents[name]=fn,scrollTo:value=>scrolls.push(value),location:{origin:'http://localhost',href:'http://localhost/staff/dashboard'}},sessionStorage:{getItem:key=>key.endsWith('-scroll')?savedScroll:null,removeItem:key=>writes.push([key,null]),setItem:(...args)=>writes.push(args)},clearTimeout:id=>timers.delete(id),setTimeout:(fn,delay)=>{const id=++timerId;timers.set(id,{fn,delay});return id;},URL,ResizeObserver});
    return {root,toggle,rail,drawer,main,backdrop,first,last,media,writes,document,scrollbar,updateScrollbar,skeleton,content,windowEvents,scrolls,tick:delay=>{for(const [id,timer] of timers)if(timer.delay===delay){timers.delete(id);timer.fn();}}};
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
test('sidebar scroll thumb follows overflow and hides when the drawer closes',()=>{
    const ui=setup();
    ui.toggle.events.click();
    assert.equal(ui.scrollbar.hidden,true);
    ui.drawer.scrollHeight=800;
    ui.updateScrollbar();
    assert.equal(ui.scrollbar.hidden,false);
    assert.equal(ui.scrollbar.style.height,'200px');
    ui.drawer.scrollTop=400;
    ui.drawer.events.scroll();
    assert.equal(ui.scrollbar.style.top,'264px');
    ui.toggle.events.click();
    assert.equal(ui.scrollbar.hidden,true);
});
test('visible viewport tracks the keyboard and browser pan without changing zoom settings',()=>{
    const values={},events={};const viewport={height:700,offsetTop:0,addEventListener:(name,fn)=>events[name]=fn};
    vm.runInNewContext(fs.readFileSync(require.resolve('../HoleInHub.Web/wwwroot/js/mobile-viewport.js'),'utf8'),{window:{visualViewport:viewport},document:{documentElement:{style:{setProperty:(name,value)=>values[name]=value}}}});
    assert.equal(values['--app-viewport-height'],'700px');viewport.height=360;viewport.offsetTop=90;events.resize();
    assert.equal(values['--app-viewport-height'],'360px');assert.equal(values['--app-viewport-top'],'90px');
});

test('navigation keeps the page still while placing its skeleton in view and restores controls',()=>{
 const ui=setup(),link={href:'http://localhost/staff/settings',dataset:{navLayout:'settings'},getAttribute:()=> 'Settings'};
 ui.document.events.click({target:{closest:()=>link}});ui.tick(160);
 assert.equal(ui.skeleton.hidden,false);assert.equal(ui.content.inert,true);assert.equal(ui.scrolls.length,0);assert.equal(ui.skeleton.style.top,'500px');
 ui.tick(15000);assert.equal(ui.skeleton.hidden,true);assert.equal(ui.content.inert,false);
 ui.document.events.click({target:{closest:()=>link}});ui.tick(160);ui.windowEvents.pageshow({persisted:true});
 assert.equal(ui.skeleton.hidden,true);assert.equal(ui.content.inert,false);
});

test('sidebar position survives navigation and reload, and resets only after sign-in',()=>{
 const ui=setup({savedScroll:'180'});
 assert.equal(ui.drawer.scrollTop,180);
 ui.windowEvents.pageshow({persisted:false});assert.equal(ui.drawer.scrollTop,180);assert.equal(ui.scrolls.length,0);
 ui.drawer.scrollTop=220;ui.drawer.events.scroll();
 assert.deepEqual(ui.writes.at(-1),['holeinhub-staff-sidebar-scroll','220']);
 const login=setup({savedScroll:'220',signedIn:true});
 assert.equal(login.drawer.scrollTop,0);
 login.windowEvents.pageshow({persisted:false});
 assert.equal(login.scrolls[0].top,0);assert.equal(login.scrolls[0].behavior,'instant');
 login.drawer.scrollTop=90;login.windowEvents.pageshow({persisted:true});assert.equal(login.drawer.scrollTop,90);
});
