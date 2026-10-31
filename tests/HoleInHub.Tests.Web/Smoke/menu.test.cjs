const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../../../HoleInHub.Web/wwwroot/js/menu.js'), 'utf8');
test('menu reversal restores explicit hamburger endpoints without clearing transforms', () => {
 const calls = [], attrs = {}, state = { open: false }, styles = { overflow: '' };
 const timeline = { from(){return this;},fromTo(target,from,to){calls.push({target,from,to});return this;},kill(){},eventCallback(name,fn){this.complete=fn;return this;},timeScale(){return this;},reverse(){this.complete();return this;} };
 const context = { menu: state, header:{classList:{add(){},remove(){}}},backdrop:{hidden:true},document:{body:{style:styles}},window:{ScrollSmoother:{get:()=>({paused(){}})}},trigger:{setAttribute(k,v){attrs[k]=v;},focus(){}},lines:[{},{}],nav:{querySelectorAll:()=>[]},photo:{},bottom:{children:[]},reduced:{matches:false},gsap:{set(target,value){calls.push({target,value});},timeline:()=>timeline} };
 vm.createContext(context);
 vm.runInContext(source.slice(source.indexOf('let timeline'), source.indexOf(" trigger.addEventListener('click'")), context);
 context.open();assert.equal(state.open,true);assert.equal(attrs['aria-expanded'],'true');
 assert.equal(calls.filter(x=>x.from?.rotate===0&&x.from?.y===0).length,2);
 context.close();assert.equal(state.open,false);assert.equal(attrs['aria-expanded'],'false');assert.equal(attrs['aria-label'],'Open navigation');
 assert.ok(calls.some(x=>x.target===context.lines&&x.value?.rotate===0&&x.value?.y===0));
 assert.ok(!calls.some(x=>x.value?.clearProps==='transform'));
 context.open();context.close();assert.equal(state.open,false);
});

test('social icons share the menu timeline and reset on reopening',()=>{assert.match(source,/\.from\(nav\.querySelectorAll\('\.social-links a'\),\{opacity:0,y:12,duration:\.4,stagger:\.07/);assert.ok(source.includes('.menu-bottom > *,.social-links a'));});
const testSource = source.slice(source.indexOf("nav.addEventListener('click'"),source.indexOf("document.addEventListener('click'"));
test('MVC menu handoff waits for motion readiness without native View Transitions',()=>{assert.ok(testSource.includes('duration:.22'));assert.ok(testSource.includes('location.assign'));assert.ok(source.includes('holeinhub:motion-ready'));assert.ok(source.includes("duration:.55"));assert.ok(!source.includes('startViewTransition'));});
