const {test}=require('node:test');
const assert=require('node:assert/strict');
const contact=require('../HoleInHub.Web/wwwroot/js/contact-inputs.js');
test('names remove numbers while preserving international letters and punctuation',()=>{
 assert.equal(contact.name('María2-José٣'),'María-José');
 assert.equal(contact.name('王小明 4'),'王小明 ');
 assert.equal(contact.name('O’Connor'),'O’Connor');
});
test('phone typing and paste keep only ASCII digits',()=>{
 assert.equal(contact.phone('+63 (912) 345-6789abc'),'639123456789');
 assert.equal(contact.phone('１２３٤٥٦123'),'123');
});
