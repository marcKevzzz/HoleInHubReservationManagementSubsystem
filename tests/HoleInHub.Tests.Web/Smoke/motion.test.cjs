const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const vm=require('node:vm');
const path=require('node:path');
const source=fs.readFileSync(path.join(__dirname,'../../../HoleInHub.Web/wwwroot/js/motion.js'),'utf8');
const buttonSource=source.slice(source.indexOf('function initButtonMotion'),source.indexOf('// One context owns'));
test('prototype character rollover responds to pointer and keyboard, and reverses',()=>{
 const handlers={},calls=[],chars=[{},{}];
 const row={querySelectorAll:()=>chars};
 const tl={to(target,vars,at){calls.push({target,vars,at});return this;},play(){calls.push('play');},reverse(){calls.push('reverse');},kill(){calls.push('kill');}};
 const gsap={timeline:()=>tl,set(){}};
 const context={gsap,matchMedia:()=>({matches:false})};vm.createContext(context);vm.runInContext(buttonSource,context);
 const button={querySelector(selector){return selector.includes('label')?row:{};},addEventListener(name,handler){handlers[name]=handler;},removeEventListener(name){delete handlers[name];}};
 const dispose=context.initButtonMotion(button);
 assert.ok(calls.some(x=>x.vars?.duration===.32&&x.vars?.stagger===.015&&x.vars?.yPercent===-125));
 handlers.pointerenter();handlers.pointerleave();handlers.focus();handlers.blur();
 assert.deepEqual(calls.filter(x=>typeof x==='string'),['play','reverse','play','reverse']);
 dispose();assert.equal(Object.keys(handlers).length,0);assert.equal(calls.at(-1),'kill');
});
test('reduced motion never constructs a rollover timeline',()=>{
 const context={matchMedia:()=>({matches:true}),gsap:{timeline(){throw Error('unexpected motion');}}};vm.createContext(context);vm.runInContext(buttonSource,context);
 assert.doesNotThrow(()=>context.initButtonMotion({})());
});

test('background videos toggle with keyboard and pause offscreen',()=>{
 const text=fs.readFileSync(path.join(__dirname,'../../../HoleInHub.Web/wwwroot/js/motion.js'),'utf8');
 const fn=text.slice(text.indexOf('function initVideoControl'),text.indexOf('function initButtonMotion'));
 const handlers={},attrs={};let observer;
 const video={paused:true,parentElement:{addEventListener(){}},setAttribute(k,v){attrs[k]=v;},addEventListener(k,v){handlers[k]=v;},play(){this.paused=false;handlers.play?.();return Promise.resolve();},pause(){this.paused=true;handlers.pause?.();}};
 const context={IntersectionObserver:class{constructor(fn){observer=fn;}observe(){}}};vm.createContext(context);vm.runInContext(fn,context);context.initVideoControl(video,{matches:false,addEventListener(){}});
 let prevented=false;handlers.keydown({key:' ',preventDefault(){prevented=true;}});assert.equal(prevented,true);assert.equal(video.paused,false);assert.equal(attrs['aria-label'],'Pause background video');
 observer([{isIntersecting:false}]);assert.equal(video.paused,true);
});
test('hero text has no scroll-linked fade-out',()=>{assert.ok(!source.includes("gsap.to('.hero-content'"));});
test('photo entrance pops are separate from continuous translation parallax and headings retain entry triggers',()=>{
 assert.match(source,/gsap\.fromTo\(hero, \{ scale: 1 \}, \{\s*scale: 0\.8/);
 assert.match(source,/scale: 0\.82, opacity: 0/);
 assert.doesNotMatch(source,/yPercent: -18,\s*scale:/);
 assert.match(source,/yPercent:\s*-18/);
 assert.match(source,/\.mosaic-copy h2/);
 assert.match(source,/document\.fonts\.ready\.then\(\(\)=>\{dispose=animatePage/);
});
