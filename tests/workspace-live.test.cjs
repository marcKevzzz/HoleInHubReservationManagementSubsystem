const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const vm=require('node:vm');
const path=require('node:path');
function setup(){
  const connections=[],documentEvents={},windowEvents={},timers=new Map();let index=0,reconcile;
  class Source {
    constructor(url){this.url=url;this.events={};connections.push(this);}
    addEventListener(name,fn){this.events[name]=fn;}
    close(){this.closed=true;}
  }
  const document={hidden:false,addEventListener:(name,fn)=>documentEvents[name]=fn};
  const window={EventSource:Source,addEventListener:(name,fn)=>windowEvents[name]=fn};
  vm.runInNewContext(fs.readFileSync(path.join(__dirname,'../HoleInHub.Web/wwwroot/js/workspace-live.js'),'utf8'),{document,window,EventSource:Source,Set,setTimeout:fn=>{timers.set(++index,fn);return index;},clearTimeout:id=>timers.delete(id),setInterval:fn=>reconcile=fn});
  return {window,document,connections,documentEvents,windowEvents,reconcile:()=>reconcile(),flush:()=>{const pending=[...timers.values()];timers.clear();pending.forEach(fn=>fn());}};
}
test('one authorized event stream serves all subscribers and coalesces action bursts',()=>{
  const ui=setup();let first=0,second=0;
  const stop=ui.window.HoleInHubLive.subscribe(()=>first++);ui.window.HoleInHubLive.subscribe(()=>second++);
  assert.equal(ui.connections.length,1);assert.equal(ui.connections[0].url,'/staff/live');
  ui.connections[0].events.change();ui.connections[0].events.change();ui.flush();assert.equal(first,1);assert.equal(second,1);
  stop();ui.connections[0].events.change();ui.flush();assert.equal(first,1);assert.equal(second,2);
});
test('reconciliation pauses while hidden and resumes on visibility without manual refresh',()=>{
  const ui=setup();let calls=0;ui.window.HoleInHubLive.subscribe(()=>calls++);
  ui.document.hidden=true;ui.reconcile();ui.flush();assert.equal(calls,0);
  ui.document.hidden=false;ui.documentEvents.visibilitychange();ui.flush();assert.equal(calls,1);
});
test('page exit closes the stream and bfcache restoration reconnects and reconciles',()=>{
  const ui=setup();let calls=0;ui.window.HoleInHubLive.subscribe(()=>calls++);
  ui.windowEvents.pagehide();assert.equal(ui.connections[0].closed,true);
  ui.windowEvents.pageshow({persisted:true});ui.flush();assert.equal(ui.connections.length,2);assert.equal(calls,1);
});
