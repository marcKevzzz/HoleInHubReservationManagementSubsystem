const {test}=require('node:test');const assert=require('node:assert/strict');const calendar=require('../../../HoleInHub.Web/wwwroot/js/reservation.js');
test('month calendar uses Sunday alignment and leap days',()=>{const days=calendar.monthDays('2028-02');assert.equal(days.filter(Boolean).length,29);assert.equal(days[2],'2028-02-01');assert.equal(days.length%7,0);assert.equal(calendar.shiftMonth('2026-12',1),'2027-01');});
test('half-hour click selection extends and clears selected slots',()=>{assert.deepEqual(calendar.extend(660,60,720),{start:660,duration:90});assert.deepEqual(calendar.extend(660,60,630),{start:630,duration:90});assert.deepEqual(calendar.extend(660,60,660),{start:660,duration:0});assert.deepEqual(calendar.extend(660,60,900),{start:900,duration:60});});
test('drag selection is contiguous in either direction',()=>{assert.deepEqual(calendar.range(720,660),{start:660,duration:90});assert.deepEqual(calendar.range(660,720),{start:660,duration:90});});
test('bay drag selects either direction and rejects missing endpoints',()=>{assert.deepEqual(calendar.bayRange(['a','b','c'],'c','a'),['a','b','c']);assert.deepEqual(calendar.bayRange(['a','c'],'b','c'),[]);});

test('prototype programs and rentals use their fixed schedules',()=>{assert.equal(calendar.serviceDayAllowed('KpgaGroup','2026-11-03'),true);assert.equal(calendar.serviceDayAllowed('KpgaGroup','2026-11-04'),false);assert.equal(calendar.serviceDayAllowed('KpgaPro','2026-11-05'),true);assert.deepEqual(calendar.fixedSessions('KpgaGroup','2026-11-03').map(x=>[x.start,x.duration]),[[900,60]]);assert.deepEqual(calendar.fixedSessions('WholeShop','2026-11-02').map(x=>[x.start,x.duration]),[[900,360],[660,780]]);});

test('clicking any selected slot clears the range without moving to the next slot',()=>{for(const slot of [660,690,720])assert.deepEqual(calendar.extend(660,90,slot),{start:660,duration:0});assert.deepEqual(calendar.extend(660,30,660),{start:660,duration:0});assert.deepEqual(calendar.extend(NaN,30,720),{start:720,duration:60});});

test('contact validation collects every invalid field and ignores disabled fields',()=>{
 const first={validity:{valid:false}},last={validity:{valid:false}},phone={validity:{valid:false}},privacy={validity:{valid:false}},players={validity:{valid:true}},disabled={disabled:true,validity:{valid:false}};
 assert.deepEqual(calendar.invalidFields([first,last,phone,players,privacy,disabled]),[first,last,phone,privacy]);
 assert.deepEqual(calendar.invalidFields([players]),[]);
});

test('past times and service lead windows cannot be selected in Manila',()=>{
 const now='2026-11-03T08:10:00Z';
 assert.equal(calendar.canStart('Regular','2026-11-03',660,now),false);
 assert.equal(calendar.canStart('Regular','2026-11-03',990,now),false);
 assert.equal(calendar.canStart('Regular','2026-11-03',1020,now),true);
 assert.equal(calendar.canStart('VIP','2026-11-03',1020,now),false);
 assert.equal(calendar.canStart('VIP','2026-11-03',1050,now),true);
 assert.equal(calendar.canStart('WholeShop','2026-11-04',900,now),true);
});

test('start-time choices respect selected duration and closing',()=>{
 assert.equal(calendar.startFits('Regular','2026-11-04',1380,60,'2026-11-03T08:00:00Z'),true);
 assert.equal(calendar.startFits('Regular','2026-11-04',1410,60,'2026-11-03T08:00:00Z'),false);
 assert.equal(calendar.startFits('VIP','2026-11-03',990,90,'2026-11-03T08:00:00Z'),false);
});

test('dragging across noon keeps a consecutive half-hour range',()=>{
 assert.deepEqual(calendar.sessionRange(690,750),{start:690,duration:90});
 assert.deepEqual(calendar.sessionRange(750,690),{start:690,duration:90});
 assert.deepEqual(calendar.sessionRange(660,660),{start:660,duration:30});
});

test('availability waits for a complete schedule',()=>{
 assert.equal(calendar.scheduleReady('2026-11-04','',60),false);
 assert.equal(calendar.scheduleReady('','11:00',60),false);
 assert.equal(calendar.scheduleReady('2026-11-04','11:30',60),true);
});
test('time periods split at noon and six in the evening',()=>{
 assert.equal(calendar.period(690),0);assert.equal(calendar.period(720),1);assert.equal(calendar.period(1080),2);
});

test('one tile is thirty minutes and two tiles are one hour',()=>{
 assert.deepEqual(calendar.sessionRange(660,690),{start:660,duration:60});
 assert.equal(calendar.scheduleReady('2026-11-04','11:00',30),true);
 assert.equal(calendar.startFits('Regular','2026-11-04',1410,30,'2026-11-03T08:00:00Z'),true);
});

test('booking selection expires after thirty minutes and rejects malformed saved fields',()=>{
 const now=Date.parse('2026-11-04T03:00:00Z'),selection={savedAt:now-1000,Service:'Regular',Date:'2026-11-05',StartTime:'11:30',DurationMinutes:'30',ResourceIds:['bay-2']};
 assert.equal(calendar.validSelection(selection,now),true);
 assert.equal(calendar.validSelection({...selection,savedAt:now-1800001},now),false);
 assert.equal(calendar.validSelection({...selection,Date:'2026-02-31'},now),false);
 assert.equal(calendar.validSelection({...selection,Service:'unknown'},now),false);
 assert.equal(calendar.validSelection({...selection,StartTime:'11:15'},now),false);
});

test('server feedback routes to Review and field errors to their own step',()=>{
 assert.equal(calendar.errorStep([]),0);
 for(const key of ['', 'IdempotencyKey', 'unexpected'])assert.equal(calendar.errorStep([key]),4);
 for(const [key,step] of Object.entries({Service:0,Date:1,StartTime:1,DurationMinutes:1,ResourceIds:2,'Participants[0].Players':2,FirstName:3,LastName:3,Phone:3,Email:3,Attendees:3,Notes:3,PrivacyAccepted:3}))assert.equal(calendar.errorStep([key]),step);
 assert.equal(calendar.errorStep(['Email','']),4);
 assert.equal(calendar.errorStep(['Email','Date']),1);
});
