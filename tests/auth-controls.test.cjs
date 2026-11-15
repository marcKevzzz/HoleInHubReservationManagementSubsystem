const {test}=require('node:test');
const assert=require('node:assert/strict');
const strength=require('../HoleInHub.Web/wwwroot/js/auth-feedback.js');
const code=require('../HoleInHub.Web/wwwroot/js/code-input.js');
test('different authentication notices stack while repeated messages replace themselves',()=>{
 const notices=[];
 const window={HoleInHubToasts:{show:notice=>{notices.push(notice);return true;}}};
 require('node:vm').runInNewContext(require('node:fs').readFileSync(require.resolve('../HoleInHub.Web/wwwroot/js/auth-feedback.js'),'utf8'),{
  window,document:{querySelector:()=>null}
 });
 window.HoleInHubAuthFeedback.notify('Check your email.');
 window.HoleInHubAuthFeedback.notify('Code resent.','success');
 window.HoleInHubAuthFeedback.notify('Check your email.');
 assert.notEqual(notices[0].replaceKey,notices[1].replaceKey);
 assert.equal(notices[0].replaceKey,notices[2].replaceKey);
});
test('code input keeps leading zeroes, filters paste and caps at six digits',()=>{
 assert.equal(code('001234'),'001234');
 assert.equal(code('12 34-56 78'),'123456');
 assert.equal(code('１２３abc'),'');
});
test('password strength reflects validity, repetition, length and variety',()=>{
 assert.equal(strength('').score,0);
 assert.match(strength('short').label,/requirements/);
 assert.match(strength('Password123!').label,/requirements/);
 assert.equal(strength('aaaaaaaa').score,1);
 assert.equal(strength('Abcdef12').score,2);
 assert.equal(strength('GolfRound123').score,3);
 assert.equal(strength('MyLongPassword12345').score,4);
});
