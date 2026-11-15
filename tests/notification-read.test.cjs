const {test}=require('node:test');
const assert=require('node:assert/strict');
const vm=require('node:vm'),fs=require('node:fs');
function setup(observe=true){
 const events={},timers=[],requests=[],updates=[];let intersect;
 const row={dataset:{unreadId:'notice-1',activityTicks:'638900000000000000'},removed:false,
  querySelector:()=>({remove(){row.removed=true;}}),removeAttribute(){delete row.dataset.unreadId;}};
 const document={hidden:false,querySelector:()=>({action:'/staff/notifications/read-visible',elements:{__RequestVerificationToken:{value:'csrf'}}}),
  querySelectorAll:()=>[row],addEventListener:(type,handler)=>events[type]=handler,dispatchEvent:event=>updates.push(event.detail)};
 class Observer{constructor(callback){intersect=callback;}observe(){}unobserve(){}}
 vm.runInNewContext(fs.readFileSync(require.resolve('../HoleInHub.Web/wwwroot/js/notification-read.js'),'utf8'),{
  document,window:{IntersectionObserver:observe?Observer:undefined},IntersectionObserver:Observer,
  setTimeout:fn=>{timers.push(fn);return timers.length;},URLSearchParams,AbortSignal,
  CustomEvent:class{constructor(type,options){this.type=type;this.detail=options.detail;}},
  fetch:async(url,options)=>{requests.push({url,body:options.body});return{ok:true,json:async()=>({unread:0})};}
 });
 return{row,document,events,requests,updates,visible:ratio=>intersect([{target:row,intersectionRatio:ratio}]),
  flush:async()=>{timers.shift()?.();await new Promise(resolve=>setImmediate(resolve));}};
}
test('any visible portion marks a notification read and publishes the remaining badge count',async()=>{
 const ui=setup();ui.visible(0);await ui.flush();assert.equal(ui.requests.length,0);
 ui.visible(.01);await ui.flush();assert.equal(ui.requests.length,1);assert.equal(ui.row.removed,true);
 assert.equal(ui.row.dataset.unreadId,undefined);assert.equal(ui.updates[0],0);
 assert.equal(ui.requests[0].body.get('rows[0].Id'),'notice-1');
});
test('hidden pages wait until visible before acknowledging notifications',async()=>{
 const ui=setup();ui.document.hidden=true;ui.visible(1);await ui.flush();assert.equal(ui.requests.length,0);
 ui.document.hidden=false;ui.events.visibilitychange();await ui.flush();assert.equal(ui.requests.length,1);
});
test('clicks acknowledge a notification even without IntersectionObserver',async()=>{
 const ui=setup(false);ui.events.click({target:{closest:()=>ui.row}});await ui.flush();
 assert.equal(ui.requests.length,1);assert.equal(ui.updates[0],0);
});
