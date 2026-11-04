const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../HoleInHub.Web/wwwroot/js/account.js'), 'utf8');
function setup(withContact=false) {
    const tasks = new Map(); let timer = 0;
    const errors = {};
    function input(name, type, rules) {
        const handlers = {};
        const field = { id: name, name, type, value: '', validity: {}, dataset: rules ? { passwordRules: 'true' } : {},
            addEventListener: (event, callback) => handlers[event] = callback,
            setAttribute: (key, value) => field[key] = value,
            focus: () => field.focused = true,
            fire: event => handlers[event]() };
        errors[name + '-error'] = { textContent: '', dataset: {} };
        return field;
    }
    const email = input('Email', 'email');
    const password = input('Password', 'password', true);
    const confirm = input('ConfirmPassword', 'password');
    const inputs = [email, password, confirm];
    const firstName=input('FirstName','text'),lastName=input('LastName','text'),phone=input('Phone','tel');
    if(withContact)inputs.unshift(firstName,lastName,phone);
    const toggles = [password, confirm].map(input => {
        const icons = { '.password-eye': { hidden: false }, '.password-eye-slash': { hidden: true } };
        Object.values(icons).forEach(icon => icon.toggleAttribute = (_, hidden) => icon.hidden = hidden);
        const toggle = { hidden: true, dataset: { passwordToggle: input.id },
            querySelector: selector => icons[selector],
            setAttribute: (key, value) => toggle[key] = value,
            addEventListener: (_, handler) => toggle.click = handler };
        return toggle;
    });
    const submitButton={disabled:false,setAttribute(name,value){this[name]=value;},removeAttribute(name){delete this[name];}};
    const windowEvents={};
    const form = { elements: { Password: password, ConfirmPassword: confirm }, querySelectorAll: selector => selector.includes("button") ? [submitButton] : inputs,
        addEventListener: (_, callback) => form.submit = callback };
    const content = { querySelector: () => form, querySelectorAll: selector => selector === '[data-password-toggle]' ? toggles : [] };
    vm.runInNewContext(source, { window: { matchMedia: () => ({ matches: true, addEventListener() {} }), addEventListener(name,handler) {(windowEvents[name]??=[]).push(handler);} },
        document: { querySelector: () => content, getElementById: id => inputs.find(input => input.id === id) || errors[id] },
        setTimeout: (callback, ms) => { assert.equal(ms, 450); tasks.set(++timer, callback); return timer; },
        clearTimeout: id => tasks.delete(id) });
    return { email, password, confirm, firstName,lastName,phone,errors, form, toggles, submitButton, restore:()=>windowEvents.pageshow?.forEach(handler=>handler({persisted:true})), flush: () => { const pending = [...tasks.values()]; tasks.clear(); pending.forEach(callback => callback()); } };
}
test('contact fields use contact-specific errors and validate phone format',()=>{
    const ui=setup(true);ui.firstName.value=' ';ui.firstName.fire('blur');assert.match(ui.errors['FirstName-error'].textContent,/first name/);
    ui.lastName.fire('blur');assert.match(ui.errors['LastName-error'].textContent,/last name/);
    ui.phone.value='invalid';ui.phone.validity.patternMismatch=true;ui.phone.fire('blur');assert.match(ui.errors['Phone-error'].textContent,/valid contact number/);
    ui.phone.value='09123456789';ui.phone.validity.patternMismatch=false;ui.phone.fire('blur');assert.equal(ui.errors['Phone-error'].textContent,'');
});
test('password toggles reveal and conceal each field independently without changing its value', () => {
    const ui = setup();
    ui.password.value = 'GoodPassword123!';
    assert.equal(ui.toggles[0].hidden, false);
    ui.toggles[0].click();
    assert.equal(ui.password.type, 'text');
    assert.equal(ui.confirm.type, 'password');
    assert.equal(ui.toggles[0]['aria-label'], 'Hide password');
    assert.equal(ui.toggles[0].querySelector('.password-eye-slash').hidden, false);
    ui.toggles[0].click();
    assert.equal(ui.password.type, 'password');
    assert.equal(ui.password.value, 'GoodPassword123!');
    assert.equal(ui.toggles[0].querySelector('.password-eye').hidden, false);
    ui.toggles[1].click();
    assert.equal(ui.confirm.type, 'text');
});
test('password guidance waits for the debounce and clears when requirements are met', () => {
    const ui = setup();
    assert.equal(ui.errors['Password-error'].textContent, '');
    ui.password.value = 'short'; ui.password.fire('input');
    assert.equal(ui.errors['Password-error'].textContent, '');
    ui.flush();
    assert.match(ui.errors['Password-error'].textContent, /12/);
    assert.equal(ui.password['aria-invalid'], 'true');
    ui.password.value = 'GoodPassword123!'; ui.password.fire('input'); ui.flush();
    assert.equal(ui.password['aria-invalid'], 'false');
    assert.match(ui.errors['Password-error'].textContent, /meets/);
});
test('confirmation updates when a previously matching password changes', () => {
    const ui = setup();
    ui.password.value = ui.confirm.value = 'GoodPassword123!'; ui.confirm.fire('blur');
    assert.equal(ui.confirm['aria-invalid'], 'false');
    ui.password.value = 'OtherPassword123!'; ui.password.fire('input'); ui.flush();
    assert.equal(ui.confirm['aria-invalid'], 'true');
    assert.match(ui.errors['ConfirmPassword-error'].textContent, /do not match/);
});
test('invalid submit stays on the form and focuses its first error', () => {
    const ui = setup(); let prevented = false;
    ui.form.submit({ preventDefault: () => prevented = true });
    assert.equal(prevented, true); assert.equal(ui.email.focused, true);
    ui.email.value = 'player@example.com'; ui.password.value = ui.confirm.value = 'GoodPassword123!';
    prevented = false; ui.form.submit({ preventDefault: () => prevented = true });
    assert.equal(prevented, false);
});


test('valid submit prevents duplicates and restores controls on back navigation',()=>{
 const ui=setup();ui.email.value='guest@example.test';ui.password.value='GoodPassword123!';ui.confirm.value=ui.password.value;
 ui.form.submit({preventDefault(){throw Error('first valid submit should proceed');}});
 assert.equal(ui.submitButton.disabled,true);
 let prevented=false;ui.form.submit({preventDefault(){prevented=true;}});assert.equal(prevented,true);
 ui.restore();assert.equal(ui.submitButton.disabled,false);
});
