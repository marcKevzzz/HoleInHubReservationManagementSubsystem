const {test}=require('node:test');
const assert=require('node:assert/strict');
const contact=require('../HoleInHub.Web/wwwroot/js/contact-inputs.js');
const vm=require('node:vm');
const fs=require('node:fs');
test('names remove numbers while preserving international letters and punctuation',()=>{
 assert.equal(contact.name('María2-José٣'),'María-José');
 assert.equal(contact.name('王小明 4'),'王小明 ');
 assert.equal(contact.name('O’Connor'),'O’Connor');
});
test('formatted phone paste is cleaned before the browser maxlength can truncate digits',()=>{
 const events={};
 const input={autocomplete:'tel',value:'',maxLength:16,selectionStart:0,selectionEnd:0,
  addEventListener:(name,handler)=>events[name]=handler,
  setSelectionRange(start,end){this.selectionStart=start;this.selectionEnd=end;},
  dispatchEvent:event=>events[event.type]()};
 vm.runInNewContext(fs.readFileSync(require.resolve('../HoleInHub.Web/wwwroot/js/contact-inputs.js'),'utf8'),{
  document:{querySelectorAll:()=>[input]},Event:class{constructor(type){this.type=type;}}
 });
 let prevented=false;
 events.paste({clipboardData:{getData:()=>'+82 (10) 1234-5678'},preventDefault(){prevented=true;}});
 assert.equal(prevented,true);
 assert.equal(input.value,'+821012345678');
 assert.equal(input.selectionStart,13);
});
test('phone typing and paste preserve one leading plus and ASCII digits',()=>{
 assert.equal(contact.phone('+63 (912) 345-6789abc'),'+639123456789');
 assert.equal(contact.phone('+82 (10) 1234-5678'),'+821012345678');
 assert.equal(contact.phone('010-1234-5678'),'01012345678');
 assert.equal(contact.phone('02-123-4567'),'021234567');
 assert.equal(contact.phone('++63+9123456789'),'+639123456789');
 assert.equal(contact.phone('１２３٤٥٦123'),'123');
});

test('HTML phone pattern enforces the same PH and KR digit boundaries as the server',()=>{
 const source=fs.readFileSync(require.resolve('../HoleInHub.Business/Accounts/ContactRules.cs'),'utf8');
 const pattern=new RegExp('^(?:'+source.match(/PhonePattern = @"([^"]+)"/)[1]+')$','v');
 for(const value of ['09123456789','639123456789','+639123456789','01012345678','+821012345678','821012345678','021234567','0212345678','+8221234567','+63281234567','+14155552671'])assert.equal(pattern.test(value),true,value);
 for(const value of ['0912345678','091234567890','+63912345678','6391234567890','0101234567','010123456789','+82101234567','8210123456789','+6309123456789','+8201012345678','02123456789'])assert.equal(pattern.test(value),false,value);
 assert.match(contact.error('09123'),/11 digits/);
 assert.match(contact.error('+8210123'),/12 digits/);
 assert.match(contact.error('+8201012345678'),/leading 0/);
});
