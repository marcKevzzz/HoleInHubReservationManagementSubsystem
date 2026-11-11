const { test } = require('node:test');
const assert = require('node:assert/strict');
const { zoomWidth, adjacentDay } = require('../HoleInHub.Web/wwwroot/js/staff-timeline-gestures.js');

test('cursor zoom stays within usable limits and reverses a wheel step', () => {
    assert.equal(zoomWidth(68, 10000), 68);
    assert.equal(zoomWidth(68, -10000), 204);
    assert.equal(zoomWidth(100, 10000, 100), 100);
    assert.equal(zoomWidth(100, -10000, 100), 300);
    assert.ok(zoomWidth(68, -100) > 68);
    assert.ok(Math.abs(zoomWidth(zoomWidth(68, -100), 100) - 68) < .0001);
});

test('date dragging crosses month, year and leap-day boundaries without timezone shifts', () => {
    assert.equal(adjacentDay('2026-12-31', 1), '2027-01-01');
    assert.equal(adjacentDay('2026-11-01', -1), '2026-10-31');
    assert.equal(adjacentDay('2028-02-28', 1), '2028-02-29');
    assert.equal(adjacentDay('2028-03-01', -1), '2028-02-29');
});
