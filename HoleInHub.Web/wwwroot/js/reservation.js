const ReservationCalendar = {
 reviewDeadline(service,now){let local=new Date(new Date(now).getTime()+8*3600000);let remaining=(service==='Regular'?30:60)*60000;if(service==='WholeShop')return new Date(now).getTime()+remaining;while(true){const midnight=Date.UTC(local.getUTCFullYear(),local.getUTCMonth(),local.getUTCDate()),opening=midnight+(local.getUTCDay()===1?11:10)*3600000,closing=midnight+86400000;let current=Math.max(local.getTime(),opening);if(remaining<=closing-current)return current+remaining-8*3600000;remaining-=closing-current;local=new Date(closing);}},
 canStart(service,date,minute,now){const stamp=Date.parse(date+'T'+String(Math.floor(minute/60)).padStart(2,'0')+':'+String(minute%60).padStart(2,'0')+':00+08:00');return Number.isFinite(stamp)&&stamp>=this.reviewDeadline(service,now);},
 startFits(service,date,minute,duration,now){return duration>=30&&minute+duration<=1440&&this.canStart(service,date,minute,now);},
 sessionRange(a,b){const range=this.range(a,b);return{...range,duration:range.duration};},
 scheduleReady(date,time,duration){return /^\d{4}-\d{2}-\d{2}$/.test(date)&&/^\d{2}:\d{2}(:00)?$/.test(time)&&Number(duration)>=30;},
 period(minute){return minute<720?0:minute<1080?1:2;},
 validSelection(selection,now){
  if(!selection||!Number.isFinite(selection.savedAt)||selection.savedAt>now||now-selection.savedAt>1800000)return false;
  if(!['Regular','VIP','PhilippineCoach','KpgaPro','KpgaGroup','WholeShop'].includes(selection.Service?.split(':')[0])||!this.scheduleReady(selection.Date,selection.StartTime,selection.DurationMinutes))return false;
  const date=new Date(selection.Date+'T00:00:00Z'),[hour,minute]=selection.StartTime.split(':').map(Number),duration=Number(selection.DurationMinutes);
  return Number.isFinite(date.getTime())&&date.toISOString().slice(0,10)===selection.Date&&hour<24&&minute<60&&minute%30===0&&Number.isInteger(duration)&&duration<=840&&duration%30===0&&hour*60+minute+duration<=1440&&Array.isArray(selection.ResourceIds)&&selection.ResourceIds.length<=8&&selection.ResourceIds.every(id=>typeof id==='string')&&new Set(selection.ResourceIds).size===selection.ResourceIds.length;
 },
 errorStep(keys){
  if(!keys.length)return 0;
  const steps={Service:0,Date:1,StartTime:1,DurationMinutes:1,ResourceIds:2,Participants:2,FirstName:3,LastName:3,Phone:3,Email:3,Attendees:3,Notes:3,PrivacyAccepted:3};
  const targets=keys.map(key=>steps[key.split(/[.\[]/)[0]]??4);
  return targets.includes(4)?4:Math.min(...targets);
 },
 invalidFields(fields){return fields.filter(input=>!input.disabled&&!input.validity.valid);},
 serviceDayAllowed(service,date){const day=new Date(date+'T00:00:00Z').getUTCDay();return service==='KpgaPro'?[2,4,6].includes(day):service!=='KpgaGroup'||day===2;},
 fixedSessions(service,date){if(service==='KpgaGroup')return[{start:900,duration:60,label:'Tuesday class · 3–4 PM'}];if(service==='WholeShop'){const opening=new Date(date+'T00:00:00Z').getUTCDay()===1?660:600;return[{start:900,duration:360,label:'Half day · 3–9 PM'},{start:opening,duration:1440-opening,label:'Whole day · opening–midnight'}];}return[];},
 bayRange(items,anchor,end){const a=items.indexOf(anchor),b=items.indexOf(end);return a<0||b<0?[]:items.slice(Math.min(a,b),Math.max(a,b)+1);},
 shiftMonth(month,offset){const [y,m]=month.split('-').map(Number);return new Date(Date.UTC(y,m-1+offset,1)).toISOString().slice(0,7);},
 monthDays(month){const first=new Date(month+'-01T00:00:00Z'),count=new Date(Date.UTC(first.getUTCFullYear(),first.getUTCMonth()+1,0)).getUTCDate(),days=Array(first.getUTCDay()).fill(null);for(let d=1;d<=count;d++)days.push(month+'-'+String(d).padStart(2,'0'));while(days.length%7)days.push(null);return days;},
 range(a,b){return{start:Math.min(a,b),duration:Math.abs(a-b)+30};},
 extend(start,duration,slot){if(!duration)return{start:slot,duration:60};const end=start+duration;if(slot===start-30)return{start:slot,duration:duration+30};if(slot===end)return{start,duration:duration+30};if(slot>=start&&slot<end)return{start,duration:0};return{start:slot,duration:60};}
};
if(typeof module!=='undefined')module.exports=ReservationCalendar;
if(typeof document!=='undefined')(()=>{
 const t=window.HoleInHubText?.t ?? (text=>text),locale=window.HoleInHubText?.locale ?? 'en-PH';
 const form=document.querySelector('#reservation-form');if(!form)return;
 const steps=[...form.querySelectorAll('.booking-step')],tabs=[...form.querySelectorAll('.step-tab')],error=document.querySelector('#booking-error'),status=document.querySelector('#availability-status'),inputs=[...form.querySelectorAll('[name=ResourceIds]')],field=name=>form.elements[name],today=new Intl.DateTimeFormat('en-CA',{timeZone:'Asia/Manila',year:'numeric',month:'2-digit',day:'2-digit'}).format(new Date());
 const experience=()=>form.querySelector('[name=Service]:checked');
 const bookingType=()=>experience()?.dataset.bookingType ?? experience()?.value.split(':')[0];
 const venueOpening=date=>new Date(date+'T00:00:00Z').getUTCDay()===1?660:600;
 function serviceWindows(date){
  const raw=experience()?.dataset.availability;
  if(!raw)return null;
  const rules=JSON.parse(raw);
  if(!rules.length)return null;
  const overrides=rules.filter(rule=>rule.date===date);
  const matching=overrides.length?overrides:rules.filter(rule=>rule.weekday===new Date(date+'T00:00:00Z').getUTCDay());
  return matching.filter(rule=>!rule.closed && rule.endMinute>venueOpening(date));
 }
 function allowsInterval(date,start,duration){
  const windows=serviceWindows(date);
  return windows===null || windows.some(rule=>rule.fixedSession
   ? rule.startMinute===start && rule.endMinute===start+duration
   : rule.startMinute<=start && rule.endMinute>=start+duration);
 }
 function fixedSessions(type,date){
  const windows=serviceWindows(date);
  if(type==='WholeShop')return ReservationCalendar.fixedSessions(type,date).filter(slot=>allowsInterval(date,slot.start,slot.duration));
  if(!experience()?.dataset.availability)return ReservationCalendar.fixedSessions(type,date);
  return (windows??[]).filter(rule=>rule.fixedSession && rule.startMinute>=venueOpening(date))
   .map(rule=>({start:rule.startMinute,duration:rule.endMinute-rule.startMinute,label:`${timeLabel(rule.startMinute)}–${timeLabel(rule.endMinute)}`}));
 }
 function serviceDayAllowed(type,date){
  if(!experience()?.dataset.availability)return ReservationCalendar.serviceDayAllowed(type,date);
  if(type==='WholeShop')return fixedSessions(type,date).length>0;
  const windows=serviceWindows(date);
  return windows===null || windows.some(rule=>!rule.fixedSession || rule.startMinute>=venueOpening(date));
 }
 const loadedAt=Date.now(),serverNow=Date.parse(form.dataset.nowUtc)||loadedAt,now=()=>new Date(serverNow+Date.now()-loadedAt),canStart=minute=>ReservationCalendar.canStart(bookingType(),field('Date').value,minute,now());
 let index=0,completed=0,month=field('Date').value.slice(0,7),pending,sequence=0,availabilityReady=false,quote=null,quoteSequence=0,quotePending=false,timePeriod=null,timeDrag=null,suppressTimeClick=false;
 const contactFields=[...form.querySelectorAll('.booking-contact input:not([type=hidden]),.booking-contact textarea')];
 function contactFeedback(input){const feedback=document.getElementById(input.getAttribute('aria-describedby'));if(!feedback)return;input.setAttribute('aria-invalid',String(!input.validity.valid));feedback.textContent=input.validity.valid?'':input.validationMessage;}
 contactFields.forEach(input=>{let timer;input.addEventListener('input',()=>{clearTimeout(timer);timer=setTimeout(()=>contactFeedback(input),450);});input.addEventListener('blur',()=>{clearTimeout(timer);contactFeedback(input);});input.addEventListener('invalid',()=>contactFeedback(input));});
 const timeValue=n=>`${String(Math.floor(n/60)).padStart(2,'0')}:${String(n%60).padStart(2,'0')}`;
 const minuteValue=value=>{const [h,m]=value.split(':').map(Number);return h*60+m;};
 const timeLabel=n=>new Intl.DateTimeFormat(locale,{hour:'numeric',minute:'2-digit',timeZone:'Asia/Manila'}).format(new Date('2026-01-01T00:00:00+08:00').getTime()+n*60000);
 const selected=()=>inputs.filter(x=>x.checked&&!x.disabled);
 function summary(){const bayCapacity=selected().reduce((sum,input)=>sum+Number(input.dataset.maxGuests),0);const serviceCap=Number(experience()?.dataset.maxPax)||(bookingType()==='KpgaGroup'?8:Infinity);field('Attendees').max=String(Math.min(bayCapacity||Number.MAX_SAFE_INTEGER,serviceCap,640));const choice=form.querySelector('[name=Service]:checked'),service=choice.closest('label').querySelector('strong').textContent;document.querySelector('#selection-service').textContent=service;const image=document.querySelector('#selection-image');image.src=choice.closest('label').querySelector('img').getAttribute('src');image.alt=service;const pairs=[['Date',field('Date').value?new Intl.DateTimeFormat(locale,{dateStyle:'long',timeZone:'Asia/Manila'}).format(new Date(field('Date').value+'T12:00:00+08:00')):t('Choose a date')],['Time',field('StartTime').value?`${timeLabel(minuteValue(field('StartTime').value))}–${timeLabel(minuteValue(field('StartTime').value)+Number(field('DurationMinutes').value))}`:t('Choose your hours')],['Duration',`${Number(field('DurationMinutes').value)/60} ${Number(field('DurationMinutes').value)===60?t('hour'):t('hours')}`],['Bays',selected().map(x=>x.closest('label').querySelector('strong').textContent).join(', ')||t('Choose your bays')],['Guests',field('Attendees').value],[t('Guest name'),`${field('FirstName').value} ${field('LastName').value}`.trim()||t('Your details')]];if(quote){const money=new Intl.NumberFormat(locale,{style:'currency',currency:'PHP'});pairs.push([t('Booking total'),money.format(quote.rentalTotal)]);if(quote.depositAmount>0)pairs.push([t('Deposit'),money.format(quote.depositAmount)]);}else{pairs.push([t('Indicative rate'),choice.closest('label').querySelector('.service-rate').dataset.rate]);}for(const selector of ['#live-selection','#review-selection']){const target=document.querySelector(selector);target.replaceChildren();for(const [label,value]of (selector==='#review-selection'?[['Experience',service],...pairs]:pairs)){const row=document.createElement('div'),dt=document.createElement('dt'),dd=document.createElement('dd');dt.textContent=t(label);dd.textContent=value;row.append(dt,dd);target.append(row);}}}
 function calendar(){const title=document.querySelector('#calendar-month'),days=document.querySelector('#calendar-days');title.textContent=new Intl.DateTimeFormat(locale,{month:'long',year:'numeric',timeZone:'UTC'}).format(new Date(month+'-01T00:00:00Z'));days.replaceChildren();for(const name of [t('Sun'),t('Mon'),t('Tue'),t('Wed'),t('Thu'),t('Fri'),t('Sat')]){const el=document.createElement('span');el.className='weekday';el.textContent=t(name);days.append(el);}for(const date of ReservationCalendar.monthDays(month)){if(!date){days.append(document.createElement('span'));continue;}const b=document.createElement('button');b.type='button';b.textContent=Number(date.slice(-2));b.disabled=date<today||!serviceDayAllowed(bookingType(),date);b.classList.toggle('selected',date===field('Date').value);b.setAttribute('aria-pressed',String(date===field('Date').value));b.setAttribute('aria-label',new Intl.DateTimeFormat(locale,{dateStyle:'full',timeZone:'UTC'}).format(new Date(date+'T12:00:00Z')));if(date===today)b.setAttribute('aria-current','date');b.onclick=()=>{field('Date').value=date;timePeriod=null;const fixed=fixedSessions(bookingType(),date);if(fixed.length){const choice=fixed.find(x=>ReservationCalendar.canStart(bookingType(),date,x.start,now()));field('StartTime').value=choice?timeValue(choice.start):'';field('DurationMinutes').value=(choice||fixed[0]).duration;}inputs.forEach(x=>x.checked=false);calendar();slots();summary();refresh();};days.append(b);}document.querySelector('#previous-month').disabled=month<=today.slice(0,7);}
 function slots(){
  const target=document.querySelector('#time-slots'),tabs=document.querySelector('#time-period-tabs');target.replaceChildren();tabs.replaceChildren();
  const date=field('Date').value,service=bookingType(),opening=new Date(date+'T00:00:00Z').getUTCDay()===1?660:600,duration=Number(field('DurationMinutes').value)||60;
  const fixed=fixedSessions(service,date);
  document.querySelector('.time-selection-heading h3').textContent=fixed.length?t('Choose your session'):t('Choose your times');
  tabs.hidden=!!fixed.length;target.classList.toggle('fixed-sessions',!!fixed.length);
  if(!serviceDayAllowed(service,date)){target.textContent=t('Choose an available program date in the calendar.');return;}
  if(timePeriod===null){const chosen=field('StartTime').value?minuteValue(field('StartTime').value):Array.from({length:(1440-opening)/30},(_,i)=>opening+i*30).find(minute=>ReservationCalendar.startFits(service,date,minute,duration,now()));timePeriod=ReservationCalendar.period(chosen??opening);}
  if(!fixed.length){
   [t('Morning'),t('Afternoon'),t('Evening')].forEach((label,i)=>{
    const button=document.createElement('button');button.type='button';button.id='time-tab-'+i;button.textContent=t(label);button.setAttribute('role','tab');button.setAttribute('aria-selected',String(timePeriod===i));button.setAttribute('aria-controls','time-slots');button.tabIndex=timePeriod===i?0:-1;
    button.onclick=()=>{timePeriod=i;slots();document.querySelector('#time-tab-'+i).focus();};
    button.onkeydown=event=>{if(!['ArrowLeft','ArrowRight',t('Home'),'End'].includes(event.key))return;event.preventDefault();timePeriod=event.key===t('Home')?0:event.key==='End'?2:(i+(event.key==='ArrowRight'?1:2))%3;slots();document.querySelector('#time-tab-'+timePeriod).focus();};
    tabs.append(button);
   });
  }
  target.setAttribute('role',fixed.length?'group':'tabpanel');
  if(fixed.length)target.removeAttribute('aria-labelledby');else target.setAttribute('aria-labelledby','time-tab-'+timePeriod);
  const options=fixed.length?fixed:Array.from({length:(1440-opening)/30},(_,i)=>({start:opening+i*30,duration:30,label:timeLabel(opening+i*30)})).filter(option=>ReservationCalendar.period(option.start)===timePeriod);
  for(const option of options){
   const button=document.createElement('button');button.type='button';button.className='time-slot';button.textContent=t(option.label);
   button.disabled=!ReservationCalendar.startFits(service,date,option.start,option.duration,now()) || !allowsInterval(date,option.start,option.duration);
   const currentStart=minuteValue(field('StartTime').value),active=fixed.length?currentStart===option.start:option.start>=currentStart&&option.start<currentStart+duration;
   button.classList.toggle('selected',active&&!button.disabled);button.setAttribute('aria-pressed',String(active&&!button.disabled));
   button.dataset.minute=option.start;
   button.onclick=event=>{if(suppressTimeClick&&event.detail)return;if(fixed.length){field('StartTime').value=timeValue(option.start);field('DurationMinutes').value=option.duration;inputs.forEach(input=>input.checked=false);slots();summary();refresh();}else selectTimeRange({start:option.start,duration:30},true);};
   if(!fixed.length){
    button.onpointerdown=event=>{if(event.pointerType==='mouse'&&event.button===0&&!button.disabled)timeDrag={anchor:option.start,moved:false};};
    button.onpointerenter=event=>{if(timeDrag&&(event.buttons&1)&&!button.disabled){timeDrag.moved=true;selectTimeRange(ReservationCalendar.sessionRange(timeDrag.anchor,option.start),false);}};
    button.onkeydown=event=>{if(!event.shiftKey||!['ArrowLeft','ArrowRight','ArrowUp','ArrowDown'].includes(event.key))return;event.preventDefault();const start=minuteValue(field('StartTime').value);if(!Number.isFinite(start))return;const end=start+Number(field('DurationMinutes').value)-30,offset=event.key==='ArrowLeft'?-30:event.key==='ArrowRight'?30:event.key==='ArrowUp'?-90:90;selectTimeRange(ReservationCalendar.sessionRange(start,end+offset),true);};
   }
   target.append(button);
  }
  const start=minuteValue(field('StartTime').value);
  document.querySelector('#selected-time-range').textContent=field('StartTime').value?`${timeLabel(start)}–${timeLabel(start+duration)} · ${duration/60} ${duration===60?t('hour'):t('hours')}`:t('Choose your start time');
 }
 function selectTimeRange(range,commit){
  const opening=new Date(field('Date').value+'T00:00:00Z').getUTCDay()===1?660:600;
  if(!allowsInterval(field('Date').value,range.start,range.duration)||range.start<opening||range.duration>840||!ReservationCalendar.startFits(bookingType(),field('Date').value,range.start,range.duration,now()))return;
  field('StartTime').value=timeValue(range.start);field('DurationMinutes').value=range.duration;
  inputs.forEach(input=>{input.checked=false;input.disabled=true;});availabilityReady=false;pending?.abort();sequence++;
  document.querySelectorAll('#time-slots [data-minute]').forEach(button=>{const minute=Number(button.dataset.minute),active=minute>=range.start&&minute<range.start+range.duration;button.classList.toggle('selected',active);button.setAttribute('aria-pressed',String(active));});
  document.querySelector('#selected-time-range').textContent=`${timeLabel(range.start)}–${timeLabel(range.start+range.duration)} · ${range.duration===30?t('30 minutes'):range.duration/60+t(' hours')}`;
  summary();if(commit)refresh();
 }
 window.addEventListener('pointerup',()=>{if(!timeDrag)return;suppressTimeClick=timeDrag.moved;timeDrag=null;if(suppressTimeClick)refresh();setTimeout(()=>suppressTimeClick=false,0);});
 window.addEventListener('pointercancel',()=>{if(timeDrag?.moved)refresh();timeDrag=null;});window.addEventListener('blur',()=>{if(timeDrag?.moved)refresh();timeDrag=null;});
 async function refresh(){pending?.abort();pending=new AbortController();const token=++sequence;availabilityReady=false;quoteSequence++;quotePending=false;quote=null;field('QuoteKey').value='';inputs.forEach(x=>{x.disabled=true;x.closest('label').hidden=bookingType()!=='WholeShop'&&x.dataset.service!==(bookingType()==='VIP'?'VIP':'Regular');});if(!ReservationCalendar.scheduleReady(field('Date').value,field('StartTime').value,field('DurationMinutes').value)){inputs.forEach(input=>input.checked=false);status.textContent=t('Choose a date and start time to check availability.');summary();return;}status.textContent=t('Checking your selected session…');const query=new URLSearchParams({date:field('Date').value,startTime:field('StartTime').value,durationMinutes:field('DurationMinutes').value,service:experience().value});try{const response=await fetch('/Reservations/CheckAvailability?'+query,{signal:pending.signal});if(!response.ok){const problem=await response.json();throw Error(problem.message||t('Choose a valid session within venue opening hours.'));}const data=await response.json();if(token!==sequence)return;for(const input of inputs){const item=data.find(x=>x.resource.id===input.value);input.disabled=!item?.available;const label=input.closest('label');label.classList.toggle('unavailable',input.disabled);label.querySelector('.resource-status').textContent=item?.available?t('Available for your session'):t('Unavailable');if(input.disabled)input.checked=false;}if(bookingType()==='WholeShop'){const free=data.length===inputs.length&&data.every(x=>x.available);inputs.forEach(x=>{x.checked=free;x.disabled=!free;});}availabilityReady=true;status.textContent=bookingType()==='WholeShop'?(selected().length===inputs.length?t('All active bays are available together.'):t('Whole-shop rental is unavailable for this session. Choose another time.')):(data.some(x=>x.available)?t('Available bays for your complete session.'):t('No bays available for this session. Choose another time.'));summary();void updateQuote();}catch(e){if(e.name==='AbortError'||token!==sequence)return;status.textContent=e.message==='Failed to fetch'?'Availability could not be loaded. Please retry.':t(e.message);summary();}}
 async function updateQuote(){
  const token=++quoteSequence;
  const bayCount=selected().length;
  field('QuoteKey').value='';
  quote=null;
  quotePending=availabilityReady&&bayCount>0&&(bookingType()!=='WholeShop'||form.dataset.demoPayments==='true');
  summary();
  if(!quotePending)return;
  const query=new URLSearchParams({date:field('Date').value,startTime:field('StartTime').value,durationMinutes:field('DurationMinutes').value,service:experience().value,bayCount});
  try {
   const response=await fetch('/Reservations/RentalQuote?'+query);
   if(!response.ok)throw new Error(t('The quote could not be loaded. Select your bays again to retry.'));
   const result=await response.json();
   if(token!==quoteSequence)return;
   quote=result;
   field('QuoteKey').value=result.quoteKey;
  }catch(error){
   if(token===quoteSequence)status.textContent=error.message;
  }finally{
   if(token===quoteSequence){quotePending=false;summary();}
  }
 }
 function validate(step){error.textContent='';if(step===3){contactFields.forEach(contactFeedback);const invalid=ReservationCalendar.invalidFields(contactFields);if(invalid.length){invalid[0].focus();return false;}return true;}if(step===1){const date=field('Date').value,start=minuteValue(field('StartTime').value),duration=Number(field('DurationMinutes').value),opening=new Date(date+'T00:00:00Z').getUTCDay()===1?660:600;if(!serviceDayAllowed(bookingType(),date)){error.textContent='Choose an available program date in the calendar.';return false;}if(!date||!field('StartTime').value||date<today||!Number.isFinite(start)||start<opening||start+duration>1440||duration<30||!canStart(start)||!allowsInterval(date,start,duration)){error.textContent='Choose a later start with the full review window and at least 30 minutes within opening hours.';return false;}}if(step===2&&selected().length&&!quote){error.textContent=quotePending?t('Wait for the quote to finish loading.'):t('The quote could not be loaded. Select your bays again to retry.');return false;}if(step===2&&(!availabilityReady||!selected().length)){error.textContent=availabilityReady?'Select at least one available bay.':'Wait for availability to load, or choose another time.';return false;}for(const input of steps[step].querySelectorAll('input,select,textarea'))if(!input.disabled&&!input.reportValidity())return false;return true;}
 function show(next,focus=true){index=next;const shopPolicy=document.getElementById('whole-shop-policy');if(shopPolicy)shopPolicy.hidden=bookingType()!=='WholeShop';document.querySelector('#pending-policy').hidden=bookingType()==='WholeShop';for(const id of ['booking-submit','booking-verify-required']){const button=document.getElementById(id);if(button){button.hidden=index!==4;if(id==='booking-submit')button.disabled=bookingType()==='WholeShop'&&form.dataset.demoPayments!=='true';}}const note=document.querySelector('#service-capacity-note');note.hidden=['Regular','VIP'].includes(bookingType());note.textContent=bookingType()==='WholeShop'?'All active bays are held together. The 50% simulated deposit must succeed within 60 minutes. No real money is collected.':'Staff must record coach confirmation and approve the bay within 60 minutes. Remaining package sessions are arranged with the venue.';document.querySelector('#bay-selection-help').textContent=bookingType()==='WholeShop'?'All active bays are selected together for exclusive use. If any bay is unavailable, choose a different session.':'Choose one bay or bring your group together across several. Click a bay or drag across available bays to select several.';steps.forEach((s,i)=>s.hidden=i!==index);tabs.forEach((tab,i)=>{tab.disabled=i>completed;tab.classList.toggle('current',i===index);tab.classList.toggle('complete',i<index);if(i===index)tab.setAttribute('aria-current','step');else tab.removeAttribute('aria-current');});document.querySelector('#step-back').hidden=index===0;document.querySelector('#step-next').hidden=index===4;summary();window.ScrollTrigger?.refresh();if(focus){const heading=steps[index].querySelector('legend');heading.tabIndex=-1;heading.focus({preventScroll:true});if(!matchMedia('(prefers-reduced-motion: reduce)').matches){window.HoleInHubMotion?.enter(steps[index],{y:12,opacity:.5,duration:.4});const smoother=window.ScrollSmoother?.get();if(smoother)smoother.scrollTo(120,true);else window.scrollTo({top:120,behavior:'smooth'});}}}
 document.querySelector('#previous-month').onclick=()=>{month=ReservationCalendar.shiftMonth(month,-1);calendar();};document.querySelector('#next-month').onclick=()=>{month=ReservationCalendar.shiftMonth(month,1);calendar();};document.querySelector('#step-back').onclick=()=>show(index-1);document.querySelector('#step-next').onclick=()=>{if(validate(index)){completed=Math.max(completed,index+1);show(index+1);}};tabs.forEach((tab,i)=>tab.onclick=()=>{if(i<=completed)show(i);});form.addEventListener('input',summary);form.addEventListener('change',event=>{if(event.target.name==='ResourceIds'&&bookingType()==='WholeShop')inputs.forEach(x=>x.checked=!x.disabled);if(event.target.name==='ResourceIds')void updateQuote();if(['Service','Date','StartTime','DurationMinutes'].includes(event.target.name)){if(event.target.name==='Service'||event.target.name==='Date'){timePeriod=null;const fixed=fixedSessions(bookingType(),field('Date').value);if(fixed.length){const choice=fixed.find(x=>canStart(x.start));field('StartTime').value=choice?timeValue(choice.start):'';field('DurationMinutes').value=(choice||fixed[0]).duration;}else if(event.target.name==='Service'||Number(field('DurationMinutes').value)>840)field('DurationMinutes').value=60;}inputs.forEach(x=>x.checked=false);calendar();slots();refresh();}summary();});form.addEventListener('invalid',event=>{const i=steps.indexOf(event.target.closest('.booking-step'));if(i>=0&&i!==index)show(i);},true);form.addEventListener('submit',event=>{if(event.submitter?.name==='intent')return;for(let i=0;i<4;i++)if(!validate(i)){event.preventDefault();show(i);return;}const submit=document.querySelector('#booking-submit');if(submit){submit.disabled=true;submit.setAttribute('aria-busy','true');}});
 const authDialog=document.querySelector('#booking-auth-dialog');
 authDialog?.addEventListener('close',()=>window.ScrollSmoother?.get()?.paused(Boolean(document.querySelector('dialog:modal, #site-menu[open]'))));
 function continueAsGuest(){try{sessionStorage.setItem('holeinhub-guest-mode','true');}catch{}authDialog?.close();}
 document.querySelector('#booking-browse')?.addEventListener('click',continueAsGuest);
 authDialog?.querySelector('.dialog-close').addEventListener('click',continueAsGuest);
 authDialog?.addEventListener('cancel',()=>{try{sessionStorage.setItem('holeinhub-guest-mode','true');}catch{}});
 document.querySelectorAll('a[href^="/account/sign-in"],a[href^="/account/sign-up"],a[href^="/account/verify-email"]').forEach(link=>{
  const destination=new URL(link.getAttribute('href'),location.origin);destination.searchParams.set('returnUrl','/Reservations/Create');link.href=destination.pathname+destination.search;
  link.addEventListener('click',()=>{try{const selection={savedAt:Date.now(),Service:experience().value,ResourceIds:selected().map(x=>x.value)};for(const name of ['Date','StartTime','DurationMinutes'])selection[name]=field(name).value;sessionStorage.setItem('holeinhub-signin-selection',JSON.stringify(selection));}catch{}});
 });
 let bayDrag=null,suppressBayClick=false;
 const tiles=inputs.map(input=>input.closest('label'));
 tiles.forEach(tile=>{
  tile.addEventListener('pointerdown',event=>{const input=tile.querySelector('input');if(event.pointerType!=='mouse'||event.button!==0||input.disabled||bookingType()==='WholeShop')return;bayDrag={anchor:input,value:!input.checked,before:new Map(inputs.map(x=>[x,x.checked])),moved:false};});
  tile.addEventListener('pointerenter',event=>{if(!bayDrag||!(event.buttons&1))return;const input=tile.querySelector('input');if(input.disabled)return;bayDrag.moved=true;const available=inputs.filter(x=>!x.disabled&&!x.closest('label').hidden),range=ReservationCalendar.bayRange(available,bayDrag.anchor,input);inputs.forEach(x=>x.checked=range.includes(x)?bayDrag.value:bayDrag.before.get(x));summary();});
  tile.addEventListener('click',event=>{if(suppressBayClick)event.preventDefault();});
 });
 window.addEventListener('pointerup',()=>{suppressBayClick=!!bayDrag?.moved;bayDrag=null;if(suppressBayClick)void updateQuote();setTimeout(()=>suppressBayClick=false,0);});
 window.addEventListener('pointercancel',()=>bayDrag=null);window.addEventListener('blur',()=>bayDrag=null);

 try {
  const draft=JSON.parse(sessionStorage.getItem('holeinhub-signin-selection'));
  if(form.dataset.signedIn==='true'&&draft){
   sessionStorage.removeItem('holeinhub-signin-selection');
   const service=[...form.querySelectorAll('[name=Service]')].find(option=>option.value===draft.Service);
   if(service&&ReservationCalendar.validSelection(draft,Date.now())&&draft.ResourceIds.every(id=>inputs.some(input=>input.value===id))){
    service.checked=true;
    for(const name of ['Date','StartTime','DurationMinutes'])field(name).value=draft[name];
    inputs.forEach(input=>input.checked=draft.ResourceIds.includes(input.value));
    month=field('Date').value.slice(0,7);
   }else error.textContent=t('Your previous selection expired. Choose your session again.');
  }
 }catch{}
 document.querySelector('#booking-calendar').hidden=false;document.querySelector('#enhanced-time').hidden=false;document.querySelector('#step-controls').hidden=false;form.classList.add('is-enhanced');form.noValidate=true;field('Date').min=today;calendar();slots();completed=form.dataset.feedback==='true'?4:ReservationCalendar.errorStep(JSON.parse(form.dataset.errorFields));show(completed,false);refresh();
 if(authDialog){let guestMode=false;try{guestMode=sessionStorage.getItem('holeinhub-guest-mode')==='true';}catch{}if(!guestMode){authDialog.showModal();window.ScrollSmoother?.get()?.paused(true,true);window.HoleInHubMotion?.enter(authDialog);}}
 setInterval(()=>{if(timeDrag)return;slots();if(field('StartTime').value&&!canStart(minuteValue(field('StartTime').value))){availabilityReady=false;status.textContent=t('This start time is no longer bookable. Choose a later time.');inputs.forEach(input=>{input.checked=false;input.disabled=true;});summary();}},30000);
})();
