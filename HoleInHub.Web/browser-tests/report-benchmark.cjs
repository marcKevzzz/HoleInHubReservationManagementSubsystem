const { expect } = require('@playwright/test');

module.exports = async function measureReports(request, submitBooking) {
    const today = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Manila' }).format(new Date());
    const start = new Date(today + 'T00:00:00Z');
    start.setUTCDate(start.getUTCDate() - 89);
    const range = '?from=' + start.toISOString().slice(0, 10) + '&to=' + today;
    const endpoints = [
        ['page', '/manager/reports' + range],
        ['workbook', '/manager/reports/export.xlsx' + range],
        ['status', '/manager/reports' + range + '&status=Completed']
    ];
    async function run([name, url]) {
        const before = performance.now();
        const response = await request.get(url, { headers: name === 'status' ? { 'X-Reports': 'list' } : {} });
        expect(response.status()).toBe(200);
        const body = await response.body();
        const ms = Math.round(performance.now() - before);
        if (name === 'workbook') expect(body.subarray(0, 2).toString()).toBe('PK');
        else expect(body.toString()).toContain('report-reservations');
        await response.dispose();
        return { ms, bytes: body.length };
    }
    for (const endpoint of endpoints) await run(endpoint);
    const samples = {};
    for (const endpoint of endpoints) {
        samples[endpoint[0]] = { solo: [], concurrent: [] };
        for (let i = 0; i < 5; i++) samples[endpoint[0]].solo.push(await run(endpoint));
    }
    const started = performance.now();
    const [guestMs] = await Promise.all([
        submitBooking().then(() => Math.round(performance.now() - started)),
        ...endpoints.map(async endpoint => {
            for (let i = 0; i < 5; i++) samples[endpoint[0]].concurrent.push(await run(endpoint));
        })
    ]);
    console.log('Report benchmark', JSON.stringify({ from: range, guestMs, samples }));
};
