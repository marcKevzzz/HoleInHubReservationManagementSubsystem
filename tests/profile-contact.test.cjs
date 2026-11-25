const { test } = require('node:test');
const assert = require('node:assert/strict');
const phone = require('../HoleInHub.Web/wwwroot/js/profile-contact.js');
const fs = require('node:fs');
const vm = require('node:vm');

test('profile country options normalize local and pasted international numbers without dropping digits', () => {
    for (const [country, local, international] of [
        ['PH', '0917 123 4567', '639171234567'],
        ['KR', '010-1234-5678', '821012345678'],
        ['CN', '138 1234 5678', '8613812345678'],
        ['US', '(415) 555-2671', '14155552671'],
        ['JP', '090-1234-5678', '819012345678'],
    ]) {
        assert.equal(phone.full(country, local), international);
        assert.equal(phone.full(country, '+' + international), international);
        const saved = phone.saved(international);
        assert.equal(saved.country, country);
        assert.equal(phone.full(saved.country, saved.national), international);
    }
});

function setup(reduced = false, extras = false, locked = false) {
    const input = value => ({ value, pattern: " *[\\p{L}\\p{M}][\\p{L}\\p{M} .,’'\\-]*", events: {}, attributes: {},
        addEventListener(name, fn) { this.events[name] = fn; },
        getAttribute(name) { return this.attributes[name]; }, setAttribute(name, value) { this.attributes[name] = value; },
        focus() { this.focused = true; }, closest() { return this; }, dispatchEvent() {},
    });
    const first = input('Joshua'), last = input('Andres'), shown = input('09171234567'), country = input('PH');
    const rule = fs.readFileSync(require.resolve('../HoleInHub.Business/Accounts/ContactRules.cs'), 'utf8').match(/PhonePattern = @"([^"]+)"/)[1];
    shown.dataset = { phonePattern: rule };
    const age=input(''),address=input('');address.pattern='[^\\p{C}]{3,300}';
    const fields = { '[name="FirstName"]': first, '[name="LastName"]': last, '#PhoneDisplay': shown, '#PhoneCountry': country, '[name="Age"]': extras?age:null, '[name="Address"]':extras?address:null };
    const gate = { disabled: true }, edit = input(''), save = { hidden: true };
    if (locked) Object.assign(fields, { '[data-profile-fields]': gate, '[data-profile-edit]': edit, '[data-profile-save]': save });
    const form = { events: {}, querySelector: key => fields[key], addEventListener(name, fn) { this.events[name] = fn; } };
    const errors = Object.fromEntries(['FirstName-error', 'LastName-error', 'Phone-error','Age-error','Address-error'].map(id => [id, {}]));
    const shakes = [];
    vm.runInNewContext(fs.readFileSync(require.resolve('../HoleInHub.Web/wwwroot/js/profile-contact.js'), 'utf8'), {
        document: { querySelector: () => form, getElementById: id => errors[id] }, Event: class {},
        window: { matchMedia: () => ({ matches: reduced }), gsap: { fromTo: (...args) => shakes.push(args) }, addEventListener() {} },
    });
    return { first, last, shown, country, form, errors, shakes, age, address, gate, edit, save };
}

test('profile stays locked until Edit information is selected', () => {
    const ui = setup(false, true, true);
    let prevented = false;
    ui.form.events.submit({ preventDefault() { prevented = true; } });
    assert(prevented); assert(ui.gate.disabled); assert(ui.save.hidden);
    ui.edit.events.click({ preventDefault() {} });
    assert.equal(ui.gate.disabled, false); assert(ui.edit.hidden); assert.equal(ui.save.hidden, false); assert(ui.first.focused);
});

test('invalid submission marks fields, shows errors and shakes, while reduced motion stays static', () => {
    for (const reduced of [false, true]) {
        const ui = setup(reduced);
        ui.first.value = '';
        ui.shown.value = '9';
        let prevented = false;
        ui.form.events.submit({ preventDefault() { prevented = true; } });
        assert(prevented);
        assert(ui.first.focused);
        assert.equal(ui.first.attributes['aria-invalid'], 'true');
        assert.equal(ui.shown.attributes['aria-invalid'], 'true');
        assert(ui.errors['Phone-error'].textContent);
        assert.equal(ui.shakes.length, reduced ? 0 : 2);
    }
});

test('all five country selections submit the full number accepted by the existing server rule', () => {
    for (const [country, value, expected] of [
        ['PH', '9171234567', '639171234567'], ['KR', '01012345678', '821012345678'],
        ['CN', '13812345678', '8613812345678'], ['US', '4155552671', '14155552671'], ['JP', '09012345678', '819012345678'],
    ]) {
        const ui = setup();
        ui.country.value = country;
        ui.shown.value = value;
        ui.form.events.submit({ preventDefault() { assert.fail('valid number was rejected'); } });
        assert.equal(ui.shown.value, expected);
        assert.equal(ui.shown.attributes['aria-invalid'], 'false');
    }
});

test('supported saved countries are retained and Philippines is the default without discarding unknown digits', () => {
    assert.deepEqual(phone.saved('09171234567'), { country: 'PH', national: '9171234567' });
    assert.deepEqual(phone.saved('01012345678'), { country: 'KR', national: '1012345678' });
    const saved = phone.saved('442079460123');
    assert.equal(saved.country, 'PH');
    assert.equal(saved.national, '442079460123');
    assert.equal(phone.fits(saved.country, saved.national), false);
    assert.deepEqual(phone.saved(''), { country: 'PH', national: '' });
    assert.equal(phone.full('CN', '1381234567890123456'), '861381234567890123456');
});

test('wrong character attempts show immediate feedback and do not edit the field', () => {
    const ui = setup();
    for (const [input, text, id] of [[ui.first, '2', 'FirstName-error'], [ui.last, '٣', 'LastName-error'], [ui.shown, 'a', 'Phone-error']]) {
        let prevented = false;
        const before = input.value;
        input.events.beforeinput({ data: text, preventDefault() { prevented = true; } });
        assert(prevented);
        assert.equal(input.value, before);
        assert.equal(input.attributes['aria-invalid'], 'true');
        assert.match(ui.errors[id].textContent, /Invalid input/);
    }
    let prevented = false;
    ui.shown.events.paste({ clipboardData: { getData: () => 'number 123' }, preventDefault() { prevented = true; } });
    assert(prevented);
    assert.match(ui.errors['Phone-error'].textContent, /Numbers only/);
});

test('each country blocks extra typed or pasted digits without truncating a saved value', () => {
    for (const [country, value] of [['PH','9171234567'],['KR','1012345678'],['CN','13812345678'],['US','4155552671'],['JP','9012345678']]) {
        const ui = setup();
        ui.country.value = country;
        ui.country.events.change();
        ui.shown.value = value;
        let prevented = false;
        ui.shown.events.beforeinput({ data: '9', preventDefault() { prevented = true; } });
        assert(prevented, country);
        assert.equal(ui.shown.value, value);
        assert.match(ui.errors['Phone-error'].textContent, /no more than/);
        ui.shown.value = value + '99';
        ui.form.events.submit({ preventDefault() { prevented = true; } });
        assert.equal(ui.shown.attributes['aria-invalid'], 'true');
        assert.equal(ui.shown.value, value + '99');
    }
});

test('optional age and address reject invalid ranges and control characters',()=>{for(const [age,address,valid] of [['','','true'],['25','50 Holy Spirit Drive','true'],['0','AB','false'],['121','123 Main Street','false'],['2.5','123 Main Street','false'],['25','A\u0000B','false']]){const ui=setup(false,true);ui.age.value=age;ui.address.value=address;let blocked=false;ui.form.events.submit({preventDefault(){blocked=true;}});assert.equal(!blocked,valid==='true',age+' '+address);}});
