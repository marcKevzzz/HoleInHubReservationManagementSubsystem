const {test}=require('node:test');
const assert=require('node:assert/strict');
const vm=require('node:vm');
const fs=require('node:fs');
const path=require('node:path');
class Element {
  constructor(){this.map={};this.events={};this.dataset={};this.hidden=false;this.open=false;this.textContent='';}
  querySelector(selector){return this.map[selector];}
  querySelectorAll(){return [];}
  addEventListener(name,fn){this.events[name]=fn;}
  contains(node){return node===this;}
  setAttribute(name,value){this[name]=value;}
  removeAttribute(name){delete this[name];}
  showModal(){this.open=true;}
  close(){this.open=false;this.events.close?.();}
  focus(){this.focused=true;}
  replaceWith(next){this.replacement=next;}
  replaceChildren(child){this.child=child;}
  append(){}
}
function makePage(){
  const page=new Element(),dialog=new Element(),body=new Element(),error=new Element(),content=new Element(),skeleton=new Element();
  skeleton.hidden=true;
  skeleton.map['[data-dashboard-skeleton-live]']=new Element();
  error.map.span=new Element();
  page.dataset={url:'/staff/dashboard?date=2026-11-13',live:'true',sliceBase:'/staff/dashboard/bookings?mode=today&date=2026-11-13'};
  page.map={'[data-dashboard-refresh]':new Element(),'[data-dashboard-dialog]':dialog,'[data-dashboard-close]':new Element(),'[data-dashboard-filter]':new Element(),'[data-dashboard-loading]':new Element(),'[data-dashboard-error]':error,'[data-dashboard-content]':content,'[data-dashboard-skeleton]':skeleton};
  dialog.map['[data-dashboard-dialog-body]']=body;
  return {page,dialog,body,error,content,skeleton};
}
function setup(fetch,configure){
  const ui=makePage(),events={},intervals=[],notices=[],pushes=[];
  const document={hidden:false,activeElement:null,querySelector:()=>ui.page,addEventListener:(name,fn)=>events[name]=fn,createElement:()=>new Element(),createTextNode:text=>text};
  const extra=configure?.(ui) || {};
  const windowEvents={},live=[],timers=new Map();let timerId=0;
  const media={matches:extra.reduced??true,addEventListener:(name,fn)=>windowEvents[name]=fn};
  const window={matchMedia:()=>media,gsap:extra.gsap,addEventListener:(name,fn)=>windowEvents[name]=fn,HoleInHubToasts:{show:notice=>notices.push(notice)},HoleInHubButtons:{mount(){}},HoleInHubLive:{subscribe:fn=>live.push(fn)}};
  window.Chart=extra.Chart;
  class Parser {parseFromString(){const next=makePage();extra.onReplace?.(next);return {querySelector:selector=>selector==='[data-dashboard]'?next.page:null};}}
  vm.runInNewContext(fs.readFileSync(path.join(__dirname,'../HoleInHub.Web/wwwroot/js/dashboard.js'),'utf8'),{document,window,location:{origin:'http://localhost',href:'http://localhost/staff/dashboard'},history:{pushState:(_a,_b,url)=>pushes.push(url)},fetch,AbortController,URL,URLSearchParams,Intl,Map,Chart:extra.Chart,getComputedStyle:()=>({getPropertyValue:()=>"#354f3b"}),DOMParser:Parser,CSS:{escape:x=>x},setTimeout:(fn,ms)=>{timers.set(++timerId,{fn,ms});return timerId;},clearTimeout:id=>timers.delete(id),setInterval:fn=>{intervals.push(fn);return intervals.length;},clearInterval(){}});
  function click(action,link){const target={closest:selector=>selector==='[data-dashboard-nav],[data-dashboard-slice]'?link:selector===action?{}:null};events.click({target,button:0,preventDefault(){},defaultPrevented:false});}
  return {...ui,document,events,windowEvents,intervals,notices,pushes,click,live,navigate:(url='http://localhost/staff/dashboard')=>{const link={href:url,hasAttribute:()=>false};ui.page.contains=node=>node===link||node===ui.page;click(null,link);},tick:(ms=500)=>{const entry=[...timers].find(([_id,t])=>t.ms===ms);if(entry){timers.delete(entry[0]);entry[1].fn();}}};
}
const settle=()=>new Promise(resolve=>setImmediate(resolve));
test('without chart runtime server content remains usable and live changes are subscribed',()=>{
  const ui=setup(()=>{throw Error('unexpected request');});
  assert.equal(ui.live.length,1);
  assert.equal(ui.page.replacement,undefined);
});
test('failed update preserves previous data and reports validation through a toast',async()=>{
  const ui=setup(async()=>({ok:false,status:400,text:async()=> 'Choose a range up to 90 days.'}));
  ui.navigate();await settle();
  assert.equal(ui.page.replacement,undefined);
  assert.match(ui.notices[0].message,/90 days/);assert.equal(ui.notices[0].kind,'warning');
});

test('slow manual changes show a matching skeleton and restore data on failure while live updates stay quiet',async()=>{
  let complete;
  const ui=setup(()=>new Promise(resolve=>complete=resolve));
  ui.navigate('http://localhost/staff/dashboard?mode=trends');assert.equal(ui.content.hidden,false);assert.equal(ui.skeleton.hidden,true);
  ui.tick(180);assert.equal(ui.content.hidden,true);assert.equal(ui.skeleton.hidden,false);
  assert.equal(ui.skeleton.map['[data-dashboard-skeleton-live]'].hidden,true);
  complete({ok:false,status:503});await settle();
  assert.equal(ui.content.hidden,false);assert.equal(ui.skeleton.hidden,true);assert.equal(ui.notices.length,1);
  ui.live[0]();ui.tick();ui.tick(180);
  assert.equal(ui.content.hidden,false);assert.equal(ui.skeleton.hidden,true);
  complete({ok:false,status:503});await settle();
});
test('an obsolete response cannot replace the newest requested dashboard',async()=>{
  const pending=[];const ui=setup((url,options)=>new Promise(resolve=>pending.push({url,options,resolve})));
  ui.navigate();ui.navigate();
  assert.equal(pending[0].options.signal.aborted,true);
  pending[1].resolve({ok:true,text:async()=> 'new page'});await settle();
  const newest=ui.page.replacement;assert.ok(newest);
  pending[0].resolve({ok:true,text:async()=> 'old page'});await settle();
  assert.equal(ui.page.replacement,newest);assert.equal(ui.notices.length,0);
});
test('live changes wait for open dialogs and filters, then reconcile without a toast',async()=>{
  let calls=0;const ui=setup(async()=>{calls++;return {ok:false,status:503};});
  ui.document.hidden=true;ui.live[0]();ui.tick();assert.equal(calls,0);
  ui.document.hidden=false;ui.dialog.open=true;ui.live[0]();ui.tick();assert.equal(calls,0);
  ui.dialog.open=false;ui.document.activeElement=ui.page.map['[data-dashboard-filter]'];ui.tick();assert.equal(calls,0);
  ui.document.activeElement=null;ui.tick();await settle();assert.equal(calls,1);assert.equal(ui.notices.length,1);
});
test('page exit aborts in-flight refresh',()=>{
  let signal;const ui=setup((_url,options)=>{signal=options.signal;return new Promise(()=>{});});
  ui.navigate();ui.windowEvents.pagehide();assert.equal(signal.aborted,true);
});

test('dashboard entrance plays for initial and manual loads, and stops for reduced motion',async()=>{
  const entrances=[];
  const configure=({page})=>{
    const stat=new Element();
    page.querySelectorAll=selector=>selector.includes('.dashboard-summary')?[stat]:[];
  };
  const ui=setup(async()=>({ok:true,text:async()=> 'dashboard'}),initial=>{
    configure(initial);
    return {reduced:false,onReplace:configure,gsap:{fromTo(targets){
      const tween={targets,revert(){this.reverted=true;}};
      entrances.push(tween);
      return tween;
    }}};
  });
  assert.equal(entrances.length,1);
  ui.live[0]();ui.tick();await settle();
  assert.equal(entrances.length,1);
  assert.equal(entrances[0].reverted,true);
  ui.windowEvents.popstate();await settle();
  assert.equal(entrances.length,2);
  ui.windowEvents.change({matches:true});
  assert.equal(entrances[1].reverted,true);
});

test('hourly insight uses one line and sums allocated hours without a service chart',()=>{
  const configs=[];
  const ui=setup(()=>{},({page})=>{
    const section=new Element(),frame=new Element(),table=new Element(),selection=new Element(),clear=new Element();
    section.dataset.chart='hourly';frame.map.canvas=new Element();table.open=true;
    selection.map={span:new Element(),a:new Element()};
    section.map={'[data-chart-frame]':frame,'[data-chart-table]':table,'[data-chart-selection]':selection,'[data-clear-selection]':clear};
    page.querySelectorAll=()=>[section];
    page.map['[data-dashboard-data]']={textContent:JSON.stringify({hours:[{Hour:11,Regular:2,Vip:1,Other:.5}]})};
    class Chart {constructor(_canvas,config){configs.push(config);this.options=config.options;}destroy(){}update(){}}
    return {Chart};
  });
  assert.equal(configs.length,1);assert.equal(configs[0].type,'line');
  assert.equal(configs[0].data.datasets.length,1);
  assert.equal(configs[0].data.datasets[0].data[0],3.5);
  assert.equal(configs[0].options.plugins.legend.display,false);
  assert.equal(configs[0].options.animation,false);
  assert.equal(configs[0].data.datasets[0].stepped,false);
  assert.equal(configs[0].data.datasets[0].cubicInterpolationMode,'monotone');
  configs[0].options.onClick(null,[{index:0}]);
  const section=ui.page.querySelectorAll()[0];
  assert.match(section.map['[data-chart-selection]'].map.a.href,/kind=hour/);
  assert.match(section.map['[data-chart-selection]'].map.a.href,/value=11/);
});

test('period menu reveals only the active date fields and Escape returns focus',()=>{
  let period,mode,day,range,dayInput,rangeInput,summary;
  setup(()=>{},({page})=>{
    period=new Element();mode=new Element();mode.value='today';day=new Element();range=new Element();dayInput=new Element();rangeInput=new Element();summary=new Element();
    day.querySelectorAll=()=>[dayInput];range.querySelectorAll=()=>[rangeInput];
    period.map={'[data-period-mode]':mode,'[data-period-day]':day,'[data-period-range]':range,summary};
    page.map['[data-dashboard-period]']=period;
  });
  assert.equal(day.hidden,false);assert.equal(range.hidden,true);assert.equal(rangeInput.disabled,true);
  mode.value='trends';mode.events.change();
  assert.equal(day.hidden,true);assert.equal(dayInput.disabled,true);assert.equal(range.hidden,false);assert.equal(rangeInput.disabled,false);
  period.open=true;period.events.keydown({key:'Escape',preventDefault(){}});
  assert.equal(period.open,false);assert.equal(summary.focused,true);
});
test('a live response arriving after a modal opens cannot replace the active dashboard',async()=>{
  let complete;
  const ui=setup(()=>new Promise(resolve=>complete=resolve));
  ui.live[0]();ui.tick();ui.dialog.open=true;
  complete({ok:true,text:async()=> 'updated page'});await settle();
  assert.equal(ui.page.replacement,undefined);
  assert.equal(ui.dialog.open,true);
});

test('repeated background failures emit one friendly toast per outage',async()=>{
  const ui=setup(async()=>{throw new TypeError('Failed to fetch');});
  ui.live[0]();ui.tick();await settle();ui.live[0]();ui.tick();await settle();
  assert.equal(ui.notices.length,1);
  assert.equal(ui.notices[0].title,'Dashboard could not update');
  assert.doesNotMatch(ui.notices[0].message,/Failed to fetch/);
  assert.match(ui.notices[0].message,/retry automatically/);
  assert.equal(ui.page.replacement,undefined);
});

test('service dialog closes only for a press and release on the backdrop',()=>{
  const ui=setup(()=>{});
  ui.dialog.getBoundingClientRect=()=>({left:200,right:800,top:100,bottom:600});
  const inside={target:ui.dialog,clientX:400,clientY:200};
  const outside={target:ui.dialog,clientX:50,clientY:200};
  ui.dialog.open=true;
  ui.dialog.events.pointerdown(inside);ui.dialog.events.click(outside);assert.equal(ui.dialog.open,true);
  ui.dialog.events.pointerdown(outside);ui.dialog.events.click(inside);assert.equal(ui.dialog.open,true);
  ui.dialog.events.pointerdown(outside);ui.dialog.events.click(outside);assert.equal(ui.dialog.open,false);
});

test('donut values show total and readable slice shares while retaining small slices in the list',()=>{
  let config;
  setup(()=>{},({page})=>{
    const frame=new Element();frame.map.canvas=new Element();page.map['[data-pie-frame]']=frame;
    page.map['[data-dashboard-data]']={textContent:JSON.stringify({services:[{Id:'Regular',Label:'Regular',Count:8},{Id:'VIP',Label:'VIP',Count:1},{Id:'WholeShop',Label:'Whole shop',Count:11}]})};
    class Chart {constructor(_canvas,options){config=options;}destroy(){}update(){}}
    return {Chart,reduced:false};
  });
  assert.equal(config.type,'doughnut');
  assert.ok(config.options.animation.duration > 0);
  const labels=[];
  const arc={x:100,y:100,startAngle:0,endAngle:1,innerRadius:50,outerRadius:90};
  config.plugins[0].afterDatasetsDraw({ctx:{save(){},restore(){},fillText:value=>labels.push(value)},getDatasetMeta:()=>({data:[arc,arc,arc]})});
  assert.deepEqual(labels,['20','booked','40%','55%']);
});
