const {test}=require('node:test');const assert=require('node:assert/strict');
const timing=require('../HoleInHub.Web/wwwroot/js/booking-verification.js');
test('verification cooldown and expiry use server deadlines',()=>{
 assert.deepEqual(timing.state(600000,60000,30000),{expired:false,seconds:570,resendSeconds:30});
 assert.deepEqual(timing.state(600000,60000,60000),{expired:false,seconds:540,resendSeconds:0});
 assert.deepEqual(timing.state(600000,60000,600001),{expired:true,seconds:0,resendSeconds:0});
});
test('only account verification can resend after expiry and both respect cooldown',()=>{
 const expired=timing.state(600000,60000,600001);
 assert.equal(timing.canResend(expired,false),false);
 assert.equal(timing.canResend(expired,true),true);
 assert.equal(timing.canResend(timing.state(600000,60000,30000),true),false);
});
