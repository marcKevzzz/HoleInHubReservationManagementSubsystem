const {test}=require('node:test');
const assert=require('node:assert/strict');
const vm=require('node:vm');
const fs=require('node:fs');
test('password confirmation updates when either new password field changes',()=>{
    const password={value:'FirstPassword123',events:{},addEventListener(name,fn){this.events[name]=fn;}};
    const confirm={value:'OtherPassword123',events:{},setCustomValidity(value){this.error=value;},addEventListener(name,fn){this.events[name]=fn;}};
    vm.runInNewContext(fs.readFileSync('wwwroot/js/staff-profile.js','utf8'),{document:{querySelector:()=>({
        querySelectorAll:()=>[],querySelector:selector=>selector==='[name="Password"]'?password:confirm
    })}});
    confirm.events.input();
    assert.equal(confirm.error,'Passwords do not match.');
    password.value=confirm.value;password.events.input();
    assert.equal(confirm.error,'');
    confirm.value='';confirm.events.input();
    assert.equal(confirm.error,'');
});
