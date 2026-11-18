const { test } = require('node:test');
const assert = require('node:assert/strict');
const { zoomWidth, adjacentDay, panDayDirection } = require('../HoleInHub.Web/wwwroot/js/staff-timeline-gestures.js');

test('cursor zoom stays within usable limits and reverses a wheel step', () => {
    assert.equal(zoomWidth(68, 10000), 68);
    assert.equal(zoomWidth(68, -10000), 340);
    assert.equal(zoomWidth(100, 10000, 100), 100);
    assert.equal(zoomWidth(100, -10000, 100), 500);
    assert.ok(zoomWidth(68, -100) > 68);
    assert.ok(Math.abs(zoomWidth(zoomWidth(68, -100), 100) - 68) < .0001);
});

test('date dragging crosses month, year and leap-day boundaries without timezone shifts', () => {
    assert.equal(adjacentDay('2026-12-31', 1), '2027-01-01');
    assert.equal(adjacentDay('2026-11-01', -1), '2026-10-31');
    assert.equal(adjacentDay('2028-02-28', 1), '2028-02-29');
    assert.equal(adjacentDay('2028-03-01', -1), '2028-02-29');
});

test('day navigation requires a deliberate horizontal pull beyond the scrolling boundary', () => {
    assert.equal(panDayDirection(80, -240, 10, 375), 0);
    assert.equal(panDayDirection(179, -179, 0, 375), 0);
    assert.equal(panDayDirection(200, -200, 160, 375), 0);
    assert.equal(panDayDirection(200, -200, 10, 375), 1);
    assert.equal(panDayDirection(-200, 200, 10, 375), -1);
    assert.equal(panDayDirection(250, -250, 0, 1200), 0);
    assert.equal(panDayDirection(321, -321, 0, 1200), 1);
});
