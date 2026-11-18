const { test } = require('node:test');
const assert = require('node:assert/strict');
const { getChoices, restoreChoices } = require('../HoleInHub.Web/wwwroot/js/customer-row-merge.js');
const input = (name, label, checked, type = 'radio') => ({ name, type, dataset: { choiceLabel: label }, checked });
const form = inputs => ({ querySelectorAll: () => inputs });

test('changing the main profile keeps the selected primary values rather than source/target positions', () => {
    const old = form([input('Form.NameFrom','Alex',true),input('Form.NameFrom','Alexander',false),input('Form.PhoneFrom','+63 917 555 0123',false,'hidden')]);
    const choices = getChoices(old);
    const nameA = input('Form.NameFrom','Alexander',false), nameB = input('Form.NameFrom','Alex',true);
    restoreChoices(form([nameA,nameB]), choices);
    assert.equal(nameA.checked, false);
    assert.equal(nameB.checked, true);
    assert.equal(choices['Form.PhoneFrom'], '+63 917 555 0123');
});

test('a choice removed by concurrent edits leaves the fresh server default selected', () => {
    const fresh = input('Form.EmailFrom','new@example.test',true);
    restoreChoices(form([fresh]), { 'Form.EmailFrom': 'old@example.test' });
    assert.equal(fresh.checked, true);
    assert.deepEqual(getChoices(null), {});
});

test('dragging checks eligibility before opening review and reports rejected drops on the source row', async () => {
    const vm = require('node:vm'), fs = require('node:fs');
    let cloned = 0;
    const classes = { add() {}, remove() {}, toggle() {} };
    const rowClasses = new Set(), rowAttributes = new Map();
    const toasts = [], toastStack = {};
    const moves = [];
    const toastHome = {
        querySelector: () => toastStack,
        moveBefore(stack, reference) { moves.push({ parent: this, stack, reference }); stack.parentElement = this; }
    };
    toastStack.parentElement = toastHome;
    const node = extra => ({ handlers: {}, classList: classes, addEventListener(k,fn) { this.handlers[k]=fn; }, ...extra });
    const make = () => ({ setAttribute() {}, style: {}, children: [], offsetWidth: 300, offsetHeight: 70, append(...children) { this.children.push(...children); this.lastElementChild=this.children.at(-1); }, remove() {} });
    let comparisonReadyAtOpen = false;
    const body = { replaceChildren(panel) { this.panel = panel; }, querySelector: () => null }, footer = { replaceChildren() {}, append() {}, querySelectorAll: () => [] }, status = { classList: classes }, fallback = {}, close = { focus() {} };
    const dialog = node({ setAttribute() {}, removeAttribute() {}, showModal() { comparisonReadyAtOpen = Boolean(body.panel); this.open=true; },
        append(stack) { stack.parentElement = this; }, moveBefore: toastHome.moveBefore, querySelector: s => ({ '[data-row-merge-body]':body,'[data-row-merge-footer]':footer,'[data-row-merge-status]':status,'[data-row-merge-fallback]':fallback,'[data-row-merge-close]':close }[s]) });
    const search = node({ action:'http://localhost/staff/customers' }), result = {}, rowFeedback = {}, dropStatus = {};
    const source = { dataset:{customerKey:'source',customerName:'First',mergeUrl:'/staff/customers/source/merge'},classList:{ ...classes, toggle(name, enabled) { if (enabled) rowClasses.add(name); else rowClasses.delete(name); } },getBoundingClientRect:()=>({left:0,top:0,width:900,height:70}),cloneNode(){cloned++;return {classList:classes,style:{},removeAttribute(){},querySelectorAll:()=>[]};},querySelector:selector=>selector === '[data-merge-row-feedback]' ? rowFeedback : {focus(){}} };
    const target = { getBoundingClientRect:()=>({left:0,top:70,width:900,height:70}),dataset:{customerKey:'target',customerName:'Second',mergeUrl:'/staff/customers/target/merge'},classList:classes };
    source.setAttribute = (name, value) => rowAttributes.set(name, value);
    source.removeAttribute = name => rowAttributes.delete(name);
    source.closest=selector=>selector === '[data-customer-key]' ? source : null; target.closest=()=>target;
    const page = node({ parentElement: toastHome, before(stack) { stack.parentElement = toastHome; }, querySelector: s => ({ '[data-customer-row-merge]':dialog,'[data-row-merge-result]':result,'[data-merge-drop-status]':dropStatus,'.people-search':search }[s]), querySelectorAll:()=>[handle],contains:()=>true });
    let captured=false, pointed=source;
    const handle = { hidden:true,closest:()=>source,setPointerCapture(){captured=true;},hasPointerCapture:()=>captured,releasePointerCapture(id){captured=false;page.handlers.lostpointercapture({pointerId:id});} };
    source.setPointerCapture=handle.setPointerCapture; source.hasPointerCapture=handle.hasPointerCapture; source.releasePointerCapture=handle.releasePointerCapture;
    const events={}, requests=[], timers=new Map();
    let timerId = 0;
    const candidate = { dataset: { matchingDetails: 'false' }, querySelector() {} };
    const responsePanel = { dataset: {}, querySelector: selector => selector === '[data-customer-merge]' ? candidate : null };
    class DOMParser { parseFromString() { return { querySelector: () => responsePanel }; } }
    vm.runInNewContext(fs.readFileSync(require.resolve('../HoleInHub.Web/wwwroot/js/customer-row-merge.js'),'utf8'), {
        document:{querySelector:()=>page,addEventListener:(k,fn)=>{events[k]=fn;},removeEventListener(){},documentElement:{classList:classes},createElement:make,elementFromPoint:()=>pointed,body:{append(){}}},
        window:{getComputedStyle:()=>({gridTemplateColumns:'300px 300px 250px 50px'}),location:{href:'http://localhost/staff/customers',origin:'http://localhost'},HoleInHubCustomerSearch:{cancel(){}},HoleInHubCustomerMerge:{mount(){}},HoleInHubToasts:{show(notice) { toasts.push({ ...notice, host: toastStack.parentElement }); return true; }},matchMedia:()=>({matches:true}),getSelection:()=>({removeAllRanges(){}}),innerWidth:1000,innerHeight:800,scrollBy(){}},
        fetch(url,options){return new Promise(resolve=>requests.push({url:new URL(url),options,resolve}));},AbortController,URL,DOMParser,setTimeout(callback, delay){timers.set(++timerId, { callback, delay }); return timerId;},clearTimeout(id){timers.delete(id);},requestAnimationFrame:()=>1,cancelAnimationFrame(){}
    });
    const pointer=(x,y)=>({target:{closest:selector=>selector === '[data-customer-key]' ? source : null},pointerType:'mouse',button:0,pointerId:1,clientX:x,clientY:y,preventDefault(){}});
    const interactive = pointer(10,10);
    interactive.target = { closest: selector => selector === '[data-customer-key]' ? source : {} };
    page.handlers.pointerdown(interactive); page.handlers.pointermove(pointer(50,50)); page.handlers.pointerup(pointer(50,50));
    assert.equal(cloned,0,'links and form controls never start a row drag');
    page.handlers.pointerdown(pointer(10,10));page.handlers.pointermove(pointer(50,50));events.keydown({key:'Escape'});events.pointerup?.(pointer(50,50));page.handlers.pointerup(pointer(50,50));
    let cancelledClick=false; events.click?.({preventDefault(){cancelledClick=true;},stopImmediatePropagation(){}});
    assert.equal(cancelledClick,true);
    assert.equal(requests.length,0);
    page.handlers.pointerdown(pointer(10,10));page.handlers.pointermove(pointer(50,50));page.handlers.pointerup(pointer(50,50));
    assert.equal(requests.length,0);
    pointed=target;
    page.handlers.pointerdown(pointer(10,10));page.handlers.pointermove(pointer(80,80));page.handlers.pointerup(pointer(80,80));
    assert.equal(cloned,3,'the full source row is lifted for each drag');
    assert.equal(dialog.open, undefined);
    assert.match(rowFeedback.textContent, /Checking customer match/);
    assert.equal(requests.length,1);
    assert.equal(requests[0].options.method,undefined);
    assert.equal(requests[0].url.pathname,'/staff/customers/target/merge');
    assert.equal(requests[0].url.searchParams.get('target'),'source');
    requests[0].resolve({ok:true,text:async()=>''});
    await new Promise(setImmediate);
    assert.equal(dialog.open, undefined);
    assert.equal(rowFeedback.textContent, '');
    assert.equal(dropStatus.textContent, '');
    assert.equal(rowClasses.has('is-merge-rejected'), true);
    assert.equal(rowAttributes.has('data-merge-invalid'), true);
    [...timers.values()].find(timer => timer.delay === 1500).callback();
    assert.equal(rowAttributes.has('data-merge-invalid'), false);
    assert.equal(toasts[0].kind, 'error');
    assert.equal(toasts[0].autoDismiss, true);
    assert.match(toasts[0].message, /two name words/);
    assert.equal(toasts[0].host, toastHome);

    candidate.dataset.matchingDetails = 'true';
    page.handlers.pointerdown(pointer(10,10));page.handlers.pointermove(pointer(80,80));page.handlers.pointerup(pointer(80,80));
    events.keydown({key:'Escape'});
    assert.equal(requests[1].options.signal.aborted, true);
    requests[1].resolve({ok:true,text:async()=>''});
    await new Promise(setImmediate);
    assert.equal(dialog.open, undefined, 'a late response cannot open a cancelled review');

    page.handlers.pointerdown(pointer(10,10));page.handlers.pointermove(pointer(80,80));page.handlers.pointerup(pointer(80,80));
    requests[2].resolve({ok:true,text:async()=>''});
    await new Promise(setImmediate);
    assert.equal(dialog.open, true);
    assert.equal(rowFeedback.textContent, '');
    assert.equal(requests.length, 3, 'a valid drop uses the checked comparison without a second fetch');
    assert.equal(rowClasses.has('is-merge-rejected'), false);
    assert.equal(toastStack.parentElement, dialog, 'the toast stack can be seen and reached in the modal');
    assert.equal(comparisonReadyAtOpen, true, 'the comparison is mounted before opening');
    assert.deepEqual(moves[0], { parent: dialog, stack: toastStack, reference: null });
    assert.equal(toasts[1].title, 'Customers match');
    assert.equal(toasts[1].kind, 'info');
    assert.equal(toasts[1].host, dialog);

    const attributes = new Map();
    const reason = { name: 'Form.Reason', value: '', focus() {},
        setAttribute: (name, value) => attributes.set(name, value),
        removeAttribute: name => attributes.delete(name) };
    let prevented = false;
    dialog.handlers.invalid({ target: reason, preventDefault() { prevented = true; } });
    assert.equal(prevented, true);
    assert.equal(toasts[2].title, 'Reason required');
    assert.equal(toasts[2].autoDismiss, true);
    assert.equal(toasts[2].host, dialog);
    assert.equal(status.hidden, true);
    assert.equal(attributes.get('aria-invalid'), 'true');
    assert.equal(attributes.has('data-merge-invalid'), true);
    events.pointerdown();
    assert.equal(attributes.has('data-merge-invalid'), false);
    assert.equal(attributes.get('aria-invalid'), 'true');
    assert.equal([...timers.values()].some(timer => timer.delay === 1500), false);
    reason.value = 'Duplicate record';
    dialog.handlers.input({ target: reason });
    assert.equal(attributes.size, 0);
    dialog.handlers.close();
    assert.equal(toastStack.parentElement, toastHome);
    assert.deepEqual(moves[1], { parent: toastHome, stack: toastStack, reference: page });
});
