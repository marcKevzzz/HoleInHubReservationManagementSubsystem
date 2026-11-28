const test = require('node:test');
const assert = require('node:assert/strict');
const { venueTopic } = require('../HoleInHub.Web/wwwroot/js/venue-chat.js');
test('customer phrasing routes to grounded answers or fallback', () => {
    for (const [question, expected] of [
        ['hi, any avialable VIP bays tomorrow?', 'Check dates'], ['hm regular bay?', 'Rates'],
        ['got promos?', 'Promos'], ['popular bays?', 'Popular bays'], ['servces?', 'Services'],
        ['package rates?', 'Packages'], ['hellooo!', 'Greeting'], ['thanks!', 'Thanks'],
        ['talk to a human', 'Staff'], ['can you refund my card?', 'Unknown']
    ]) assert.equal(venueTopic(question), expected, question);
});
test('bay questions resolve in Manila across midnight and year boundaries', () => {
    const { bayRequest } = require('../HoleInHub.Web/wwwroot/js/venue-chat.js');
    const now = Date.parse('2026-12-30T17:01:00Z');
    assert.deepEqual(bayRequest('any VIP bay available tomorrow at 3pm?', now),
        { service: 'VIP', date: '2027-01-01', startTime: '15:00', durationMinutes: 60 });
    assert.deepEqual(bayRequest('regular available Friday at 15:30 for 2 hours?', now),
        { service: 'Regular', date: '2027-01-01', startTime: '15:30', durationMinutes: 120 });
    assert.equal(bayRequest('any VIP available today?', now).startTime, '');
    assert.equal(bayRequest('VIP at 12am?', now).startTime, '00:00');
    assert.equal(venueTopic('show me the menu'), 'Menu');
    assert.equal(venueTopic('start over'), 'Restart');
});
test('follow-ups preserve the other details and common inquiries route correctly', () => {
    const { bayRequest } = require('../HoleInHub.Web/wwwroot/js/venue-chat.js');
    const now = Date.parse('2026-11-27T08:00:00Z');
    const first = bayRequest('VIP tomorrow at 3pm for 2 hours', now);
    const later = bayRequest('what about 4pm?', now, first);
    assert.deepEqual(later, { ...first, startTime: '16:00' });
    assert.deepEqual(bayRequest('how about regular bays?', now, later), { ...later, service: 'Regular' });
    for (const [q, topic] of [['whats services you offer','Services'], ['packaes','Packages'], ['popular service','Popular services'], ['ratings','Ratings']])
        assert.equal(venueTopic(q), topic);
});
