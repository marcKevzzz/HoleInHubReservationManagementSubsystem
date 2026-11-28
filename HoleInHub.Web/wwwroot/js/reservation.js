const ReservationCalendar = {
  reviewDeadline(service, now) { let local = new Date(new Date(now).getTime() + 8 * 3600000); let remaining = (service === 'Regular' ? 30 : 60) * 60000; if (service === 'WholeShop') return new Date(now).getTime() + remaining; while (true) { const midnight = Date.UTC(local.getUTCFullYear(), local.getUTCMonth(), local.getUTCDate()), opening = midnight + (local.getUTCDay() === 1 ? 11 : 10) * 3600000, closing = midnight + 86400000; let current = Math.max(local.getTime(), opening); if (remaining <= closing - current) return current + remaining - 8 * 3600000; remaining -= closing - current; local = new Date(closing); } },
  canStart(service, date, minute, now) { const stamp = Date.parse(date + 'T' + String(Math.floor(minute / 60)).padStart(2, '0') + ':' + String(minute % 60).padStart(2, '0') + ':00+08:00'); return Number.isFinite(stamp) && stamp >= this.reviewDeadline(service, now); },
  startFits(service, date, minute, duration, now) { return duration >= 30 && minute + duration <= 1440 && this.canStart(service, date, minute, now); },
  sessionRange(a, b) { const range = this.range(a, b); return { ...range, duration: range.duration }; },
  scheduleReady(date, time, duration) { return /^\d{4}-\d{2}-\d{2}$/.test(date) && /^\d{2}:\d{2}(:00)?$/.test(time) && Number(duration) >= 30; },
  sessionIssue(service, bays) {
    if (service === 'WholeShop' && (!bays.length || bays.some(bay => !bay.available))) return 'The bays aren’t fully available for this whole-shop session. Choose another date or time.';
    return bays.some(bay => bay.available) ? '' : 'No bays are available for this session. Choose another date or time.';
  },
  wholeShopDayAvailable(day) { return Boolean(day?.sessions?.some(session => session.available)); },
  nearestDate(anchor, today, available) {
    const start = Date.parse((anchor && anchor >= today ? anchor : today) + 'T00:00:00Z');
    if (!Number.isFinite(start)) return null;
    for (let offset = 0; offset <= 366; offset++) {
      for (const direction of offset ? [1, -1] : [1]) {
        const date = new Date(start + offset * direction * 86400000).toISOString().slice(0, 10);
        if (date >= today && available(date)) return date;
      }
    }
    return null;
  },
  period(minute) { return minute < 720 ? 0 : minute < 1080 ? 1 : 2; },
  validSelection(selection, now) {
    if (!selection || !Number.isFinite(selection.savedAt) || selection.savedAt > now || now - selection.savedAt > 1800000) return false;
    if (!['Regular', 'VIP', 'PhilippineCoach', 'KpgaPro', 'KpgaGroup', 'WholeShop'].includes(selection.Service?.split(':')[0]) || !this.scheduleReady(selection.Date, selection.StartTime, selection.DurationMinutes)) return false;
    const date = new Date(selection.Date + 'T00:00:00Z'), [hour, minute] = selection.StartTime.split(':').map(Number), duration = Number(selection.DurationMinutes);
    return Number.isFinite(date.getTime()) && date.toISOString().slice(0, 10) === selection.Date && hour < 24 && minute < 60 && minute % 30 === 0 && Number.isInteger(duration) && duration <= 840 && duration % 30 === 0 && hour * 60 + minute + duration <= 1440 && Array.isArray(selection.ResourceIds) && selection.ResourceIds.length <= 8 && selection.ResourceIds.every(id => typeof id === 'string') && new Set(selection.ResourceIds).size === selection.ResourceIds.length;
  },
  errorStep(keys) {
    if (!keys.length) return 0;
    const steps = { Service: 0, Date: 1, StartTime: 1, DurationMinutes: 1, ResourceIds: 2, Participants: 2, FirstName: 3, LastName: 3, Phone: 3, Email: 3, Attendees: 3, Notes: 3, PrivacyAccepted: 3 };
    const targets = keys.map(key => steps[key.split(/[.\[]/)[0]] ?? 4);
    return targets.includes(4) ? 4 : Math.min(...targets);
  },
  invalidFields(fields) { return fields.filter(input => !input.disabled && !input.validity.valid); },
  serviceDayAllowed(service, date) { const day = new Date(date + 'T00:00:00Z').getUTCDay(); return service === 'KpgaPro' ? [2, 4, 6].includes(day) : service !== 'KpgaGroup' || day === 2; },
  fixedSessions(service, date) { if (service === 'KpgaGroup') return [{ start: 900, duration: 60, label: 'Tuesday class · 3–4 PM' }]; if (service === 'WholeShop') { const opening = new Date(date + 'T00:00:00Z').getUTCDay() === 1 ? 660 : 600; return [{ start: 900, duration: 360, label: 'Half day · 3–9 PM' }, { start: opening, duration: 1440 - opening, label: 'Whole day · opening–midnight' }]; } return []; },
  bayRange(items, anchor, end) { const a = items.indexOf(anchor), b = items.indexOf(end); return a < 0 || b < 0 ? [] : items.slice(Math.min(a, b), Math.max(a, b) + 1); },
  shiftMonth(month, offset) { const [y, m] = month.split('-').map(Number); return new Date(Date.UTC(y, m - 1 + offset, 1)).toISOString().slice(0, 7); },
  monthDays(month) { const first = new Date(month + '-01T00:00:00Z'), count = new Date(Date.UTC(first.getUTCFullYear(), first.getUTCMonth() + 1, 0)).getUTCDate(), days = Array(first.getUTCDay()).fill(null); for (let d = 1; d <= count; d++)days.push(month + '-' + String(d).padStart(2, '0')); while (days.length % 7) days.push(null); return days; },
  range(a, b) { return { start: Math.min(a, b), duration: Math.abs(a - b) + 30 }; },
  extend(start, duration, slot) { if (!duration) return { start: slot, duration: 60 }; const end = start + duration; if (slot === start - 30) return { start: slot, duration: duration + 30 }; if (slot === end) return { start, duration: duration + 30 }; if (slot >= start && slot < end) return { start, duration: 0 }; return { start: slot, duration: 60 }; }
};
if (typeof module !== 'undefined') module.exports = ReservationCalendar;
if (typeof document !== 'undefined') (() => {
  const t = window.HoleInHubText?.t ?? (text => text), locale = window.HoleInHubText?.locale ?? 'en-PH';
  const change = document.querySelector('[data-change-schedule]');
  if (change) {
    const calendar = document.querySelector('#booking-calendar');
    const date = change.elements.Date;
    const today = new Date(Date.now() + 8 * 3600000).toISOString().slice(0, 10);
    const title = calendar.querySelector('#calendar-month');
    const days = calendar.querySelector('#calendar-days');
    const previous = calendar.querySelector('#previous-month');
    const next = calendar.querySelector('#next-month');
    let month = (date.value || today).slice(0, 7);
    function render() {
      title.textContent = new Intl.DateTimeFormat(locale, { month: 'long', year: 'numeric', timeZone: 'UTC' })
        .format(new Date(month + '-01T00:00:00Z'));
      days.replaceChildren();
      for (const weekday of ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']) {
        const label = document.createElement('span');
        label.className = 'weekday';
        label.textContent = t(weekday);
        days.append(label);
      }
      for (const day of ReservationCalendar.monthDays(month)) {
        const cell = document.createElement(day ? 'button' : 'span');
        if (day) {
          cell.type = 'button';
          cell.textContent = Number(day.slice(-2));
          cell.disabled = day < today;
          cell.classList.toggle('selected', date.value === day);
          cell.setAttribute('aria-pressed', String(date.value === day));
          cell.setAttribute('aria-label', new Intl.DateTimeFormat(locale, { dateStyle: 'full', timeZone: 'UTC' })
            .format(new Date(day + 'T00:00:00Z')));
          cell.addEventListener('click', () => {
            date.value = day;
            render();
            days.querySelector('.selected')?.focus();
          });
        }
        days.append(cell);
      }
      previous.disabled = month <= today.slice(0, 7);
    }
    previous.addEventListener('click', () => { month = ReservationCalendar.shiftMonth(month, -1); render(); });
    next.addEventListener('click', () => { month = ReservationCalendar.shiftMonth(month, 1); render(); });
    date.addEventListener('change', () => { month = (date.value || today).slice(0, 7); render(); });
    date.min = today;
    render();
    calendar.hidden = false;
    return;
  }
  const form = document.querySelector('#reservation-form'); if (!form) return;
  const steps = [...form.querySelectorAll('.booking-step')], tabs = [...form.querySelectorAll('.step-tab')], error = document.querySelector('#booking-error'), status = document.querySelector('#availability-status'), inputs = [...form.querySelectorAll('[name=ResourceIds]')], field = name => form.elements[name], today = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Manila', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date());
  const experience = () => form.querySelector('[name=Service]:checked');
  const bookingType = () => experience()?.dataset.bookingType ?? experience()?.value.split(':')[0];
  const program = () => experience()?.dataset.program ? experience().dataset : null;
  const dates = () => program()?.dates.split(',') || [];
  const roster = form.querySelector('#program-learners');
  function selectFirstLesson() {
    const first = dates()[0];
    field('Date').value = first.slice(0, 10);
    field('StartTime').value = first.slice(11);
    field('DurationMinutes').value = program().minutes;
    month = first.slice(0, 7);
  }
  function isMinor(row) {
    const age = row.querySelector('[data-age]').valueAsNumber;
    return Number.isInteger(age) && age >= 0 && age < 18;
  }
  function syncLearners() {
    const data = program();
    roster.hidden = roster.disabled = !data;
    const count = Number(field('Attendees').value);
    form.querySelector('label[for=Attendees]').textContent = t(data ? 'Number of learners *' : 'Number of guests *');
    const junior = data?.junior === 'true';
    form.querySelector('.booking-contact > legend').textContent = t(junior ? 'Parent or guardian details' : 'YOUR DETAILS');
    roster.querySelector('legend').hidden = junior;
    const relationship = field('JuniorRelationship');
    relationship.closest('.field').hidden = relationship.disabled = !junior;
    relationship.required = junior;
    if (junior) form.querySelector('label[for=Attendees]').textContent = t('Number of children attending *');
    if (!data) return;
    field('ClassSessionId').value = field('ReviewedClassSessionId').value = data.program;
    field('learnerCount').value = field('ReviewedLearnerCount').value = count;
    for (const [i, row] of [...roster.querySelectorAll('[data-family-learner]')].entries()) {
      row.hidden = row.disabled = i >= count;
      const participating = row.querySelector('[data-participating]');
      participating.closest('label').hidden = participating.disabled = junior;
      if (junior) participating.checked = false;
      row.querySelector('[data-age]').max = junior ? '17' : '120';
      row.querySelector('legend').hidden = junior && i > 0;
      row.querySelector('legend').textContent = junior ? t('Learner') : t('Learner') + ' ' + (i + 1);
      for (const [id, standard, learnerLabel] of [['learner-first','First name *','Learner first name *'],['learner-last','Last name *','Learner last name *'],['age','Age in years *','Learner age in years *']]) {
        row.querySelector('label[for="' + id + '-' + i + '"]').textContent = t(junior ? learnerLabel : standard);
      }
      const minor = !junior && isMinor(row);
      const guardian = row.querySelector('[data-guardian]');
      guardian.disabled = junior;
      guardian.hidden = !minor;
      const guardianIndex = row.querySelector('[data-guardian-index]');
      for (const option of guardianIndex.options) {
        const source = roster.querySelector('[data-family-learner="' + option.value + '"]');
        option.disabled = Number(option.value) !== i && (!isMinor(source) || source.querySelector('[data-guardian-index]').value !== option.value);
      }
      if (guardianIndex.selectedOptions[0]?.disabled) guardianIndex.value = String(i);
      const own = guardianIndex.value === String(i);
      row.querySelector('[data-guardian-fields]').hidden = !own;
      for (const [selector,needed] of [['[data-learner-name]',!row.querySelector('[data-participating]').checked],['[data-guardian-name]',minor && own && !row.querySelector('[data-contact-guardian]').checked]]) {
        const wrap = row.querySelector(selector);
        wrap.hidden = !needed;
        for (const input of wrap.querySelectorAll('input')) input.required = needed;
      }
      row.querySelector('[name$=".Relationship"]').required = minor && own;
    }
  }
  const skipBays = () => autoBay() || bookingType() === 'WholeShop';
  const autoBay = () => experience()?.dataset.autoBay === 'true';
  const venueOpening = date => new Date(date + 'T00:00:00Z').getUTCDay() === 1 ? 660 : 600;
  function serviceWindows(date) {
    const raw = experience()?.dataset.availability;
    if (!raw) return null;
    const rules = JSON.parse(raw);
    if (!rules.length) return null;
    const overrides = rules.filter(rule => rule.date === date);
    const matching = overrides.length ? overrides : rules.filter(rule => rule.weekday === new Date(date + 'T00:00:00Z').getUTCDay());
    return matching.filter(rule => !rule.closed && rule.endMinute > venueOpening(date));
  }
  function allowsInterval(date, start, duration) {
    const windows = serviceWindows(date);
    return windows === null || windows.some(rule => rule.fixedSession
      ? rule.startMinute === start && rule.endMinute === start + duration
      : rule.startMinute <= start && rule.endMinute >= start + duration);
  }
  function fixedSessions(type, date) {
    const windows = serviceWindows(date);
    if (type === 'WholeShop') return (wholeShopDay(date)?.sessions || []).filter(slot => slot.available).map(slot => ({start: slot.startMinute, duration: slot.duration, label: timeLabel(slot.startMinute) + '–' + timeLabel(slot.startMinute + slot.duration)}));
    if (!experience()?.dataset.availability) return ReservationCalendar.fixedSessions(type, date);
    return (windows ?? []).filter(rule => rule.fixedSession && rule.startMinute >= venueOpening(date))
      .map(rule => ({ start: rule.startMinute, duration: rule.endMinute - rule.startMinute, label: `${timeLabel(rule.startMinute)}–${timeLabel(rule.endMinute)}` }));
  }
  function serviceDayAllowed(type, date) {
    if (!experience()?.dataset.availability) return ReservationCalendar.serviceDayAllowed(type, date);
    if (type === 'WholeShop') return fixedSessions(type, date).length > 0;
    const windows = serviceWindows(date);
    return windows === null || windows.some(rule => !rule.fixedSession || rule.startMinute >= venueOpening(date));
  }
  const loadedAt = Date.now(), serverNow = Date.parse(form.dataset.nowUtc) || loadedAt, now = () => new Date(serverNow + Date.now() - loadedAt), canStart = minute => ReservationCalendar.canStart(bookingType(), field('Date').value, minute, now());
  let index = 0, completed = 0, month = field('Date').value.slice(0, 7), pending, sequence = 0, availabilityReady = false, quote = null, quoteSequence = 0, quotePending = false, timePeriod = null, timeDrag = null, suppressTimeClick = false;
  let sessionProblem = '', availableCapacity = 0, autoBays = [], quoteError = '';
  const sessionStatus = document.querySelector('#session-availability');
  let lostSessionKey;
  function showSessionStatus(message, invalid = false) {
    sessionStatus.textContent = invalid ? message : '';
    sessionStatus.classList.toggle('booking-session-invalid', invalid);
    document.querySelector('#time-slots').setAttribute('aria-invalid', String(invalid));
  }
  const monthAvailability = new Map();
  let calendarRequest, calendarRequestKey, chooseWholeShopDate = false;
  const calendarHelp = document.createElement('p');
  calendarHelp.className = 'booking-session-status calendar-feedback'; calendarHelp.setAttribute('role', 'status');
  document.querySelector('#booking-calendar').append(calendarHelp);
  const calendarRetry = document.createElement('button');
  calendarRetry.type = 'button'; calendarRetry.className = 'account-text-link'; calendarRetry.textContent = t('Retry availability'); calendarRetry.hidden = true;
  document.querySelector('#booking-calendar').append(calendarRetry);
  const calendarKey = () => experience().value + ':' + month;
  const wholeShopDay = date => monthAvailability.get(experience().value + ':' + date.slice(0, 7))?.days?.find(day => day.date === date);
  function checkCalendar() {
    if (bookingType() !== 'WholeShop') { calendarHelp.textContent = ''; calendarRetry.hidden = true; return; }
    const key = calendarKey(), cached = monthAvailability.get(key);
    if (cached && Date.now() - cached.at < 20000) {
      calendarHelp.textContent = cached.failed ? t('Calendar availability could not be loaded. Please retry.') :
        t('Only dates with an available whole-shop session can be selected.');
      calendarRetry.hidden = !cached.failed; return;
    }
    if (calendarRequestKey === key) return;
    calendarRequest?.abort(); calendarRequest = new AbortController(); calendarRequestKey = key;
    monthAvailability.delete(key); calendarHelp.textContent = t('Checking available whole-shop dates…'); calendarRetry.hidden = true;
    const [year, monthNumber] = month.split('-');
    const query = new URLSearchParams({ year, month: monthNumber, service: experience().value });
    fetch('/Reservations/WholeShopCalendar?' + query, { signal: calendarRequest.signal })
      .then(async response => { if (!response.ok) throw new Error('Calendar unavailable'); return response.json(); })
      .then(days => {
        monthAvailability.set(key, { at: Date.now(), days });
        if (bookingType() === 'WholeShop' && calendarKey() === key) {
          if (chooseWholeShopDate) {
            const previous = field('Date').value;
            const nearest = ReservationCalendar.nearestDate(previous, today, date => ReservationCalendar.wholeShopDayAvailable(wholeShopDay(date)));
            field('Date').value = nearest || '';
            chooseWholeShopDate = false;
            if (nearest) {
              const session = wholeShopDay(nearest).sessions.find(item => item.available);
              field('StartTime').value = timeValue(session.startMinute); field('DurationMinutes').value = session.duration;
              if (nearest !== previous) window.HoleInHubToasts?.show({ kind: 'info', title: t('Date updated'), message: t('Selected the nearest available date for this service.'), replaceKey: 'booking-date' });
              refresh();
            } else refresh();
          }
          const selected = wholeShopDay(field('Date').value);
          const minute = minuteValue(field('StartTime').value), duration = Number(field('DurationMinutes').value);
          if (selected && !selected.sessions.some(session => session.available && session.startMinute === minute && session.duration === duration)) {
            field('StartTime').value = ''; refresh();
          }
        }
      })
      .catch(error => { if (error.name !== 'AbortError') monthAvailability.set(key, { at: Date.now(), failed: true }); })
      .finally(() => {
        if (calendarRequestKey === key) { calendarRequestKey = null; if (bookingType() === 'WholeShop' && calendarKey() === key) { calendar(); slots(); summary(); } }
      });
  }
  calendarRetry.onclick = () => { monthAvailability.delete(calendarKey()); calendar(); slots(); };
  function serviceDateAvailable(date) {
    if (date < today || !serviceDayAllowed(bookingType(), date)) return false;
    const fixed = fixedSessions(bookingType(), date);
    if (fixed.length) return fixed.some(slot => ReservationCalendar.startFits(bookingType(), date, slot.start, slot.duration, now()));
    for (let minute = venueOpening(date); minute < 1440; minute += 30) {
      if (allowsInterval(date, minute, 30) && ReservationCalendar.startFits(bookingType(), date, minute, 30, now())) return true;
    }
    return false;
  }
  function alignServiceDate() {
    chooseWholeShopDate = bookingType() === 'WholeShop';
    const previous = field('Date').value;
    const nearest = ReservationCalendar.nearestDate(previous, today, serviceDateAvailable);
    field('Date').value = nearest || '';
    const start = minuteValue(field('StartTime').value), duration = Number(field('DurationMinutes').value);
    if (!nearest || nearest !== previous || !allowsInterval(nearest, start, duration) || !ReservationCalendar.startFits(bookingType(), nearest, start, duration, now())) field('StartTime').value = '';
    if (nearest) {
      month = nearest.slice(0, 7);
      if (chooseWholeShopDate) monthAvailability.delete(calendarKey());
      if (nearest !== previous) window.HoleInHubToasts?.show({ kind: 'info', title: t('Date updated'), message: t('Selected the nearest available date for this service.'), replaceKey: 'booking-date' });
    }
  }
  const contactFields = [...form.querySelectorAll('.booking-contact input:not([type=hidden]),.booking-contact textarea,#program-learners select')];
  const phone = field('Phone');
  const country = form.querySelector('#PhoneCountry');
  const phonePattern = new RegExp('^(?:' + phone.dataset.phonePattern + ')$');
  const savedPhone = ProfilePhone.saved(phone.value);
  country.value = savedPhone.country;
  phone.value = savedPhone.national;
  country.disabled = false;
  country.hidden = false;
  country.dispatchEvent(new Event('change', { bubbles: true }));
  country.addEventListener('change', () => { if (phone.value) contactFeedback(phone); });
  function blockInvalidPhoneText(event, text) {
    if (text && /[^0-9+ ()-]/.test(text)) event.preventDefault();
  }
  phone.addEventListener('beforeinput', event => {
    if (!event.isComposing) blockInvalidPhoneText(event, event.data);
  });
  phone.addEventListener('paste', event => blockInvalidPhoneText(event, event.clipboardData?.getData('text')));
  function cleanPhoneInput(event) {
    if (!event.isComposing) phone.value = phone.value.replace(/[^0-9+ ()-]/g, '');
  }
  phone.addEventListener('input', cleanPhoneInput);
  phone.addEventListener('compositionend', cleanPhoneInput);
  form.addEventListener('formdata', event => {
    event.formData.set('Phone', ProfilePhone.full(country.value, phone.value));
  });

  const contactAge = field('ContactAge');
  contactAge.addEventListener('beforeinput', event => {
    if (event.data && /[^0-9]/.test(event.data)) event.preventDefault();
  });
  contactAge.addEventListener('input', () => { contactAge.value = contactAge.value.replace(/[^0-9]/g, ''); });
  const reviewCard = form.querySelector('.review-card');
  new ResizeObserver(() => {
    if (reviewCard.offsetHeight) form.style.setProperty('--review-photo-height', reviewCard.offsetHeight + 'px');
  }).observe(reviewCard);
  function contactError(input) {
    const value = input.value.trim();
    if (input.required && !value) {
      const prompts = { FirstName: 'Enter your first name.', LastName: 'Enter your last name.', ContactAge: 'Enter your age.', ContactAddress: 'Enter your address.', JuniorRelationship: 'Enter your relationship to the learner.', Age: 'Enter the learner’s age.', Email: 'Enter your email address.', Phone: 'Enter your mobile number.', Attendees: 'Enter the number attending.', Relationship: 'Enter your relationship to the learner.', Name: 'Enter the guardian’s name.' };
      const key = input.name.split('.').pop();
      if (input.closest('[data-family-learner]') && ['FirstName','LastName'].includes(key)) return t(key === 'FirstName' ? 'Enter the learner’s first name.' : 'Enter the learner’s last name.');
      if (prompts[key]) return t(prompts[key]);
    }
    if (input.name === 'ContactAge' && value) {
      if (!/^\d{1,3}$/.test(value) || Number(value) > 120) return t('Enter a valid age between 18 and 120.');
      if (Number(value) < 18) return t('You must be of legal age (18 or older) to make a reservation.');
    }
    if (input.name === 'NonPlayerGuests' && !input.disabled && (!value || !Number.isInteger(Number(input.value)) || Number(input.value) < Number(input.min) || Number(input.value) > Number(input.max))) return t('Enter the number of non-playing guests') + ' (' + input.min + '�' + input.max + ').';
    if (input.matches('[data-age]') && (!value || !input.validity.valid)) return t(input.max === '17' ? 'Junior learners must be 17 or younger.' : 'Enter a whole number from 0 to 120.');
    if (input.closest('#program-learners') && input.required && /\.(FirstName|LastName|Name|Relationship)$/.test(input.name) && !/^[\p{L}\p{M}][\p{L}\p{M} .,’'\-]*$/u.test(value)) return t('Enter a name or relationship using letters.');
    if (['FirstName', 'LastName'].includes(input.name)) {
      if (!value) return t(input.name === 'FirstName' ? 'Enter your first name.' : 'Enter your last name.');
      if (!/^[\p{L}\p{M}][\p{L}\p{M} .,’'\-]*$/u.test(value) || [...value.matchAll(/\p{L}/gu)].length < 2)
        return t('Enter at least two letters. Leave out numbers.');
    }
    if (input.name === 'Attendees') {
      if (!value) return t('Enter the number of guests.');
      if (!/^[0-9]+$/.test(value)) return t('Use numbers only for guest count.');
      const count = Number(value), maximum = Number(input.max) || 640;
      if (!Number.isSafeInteger(count) || count < 1 || count > maximum)
        return t('Guest count must be between 1 and') + ' ' + maximum + '.';
    }
    if (input.name === 'Email' && !input.readOnly) {
      if (!value) return t('Enter your email address.');
      if (input.validity.typeMismatch) return t('Enter a valid email address.');
    }
    if (input === phone) {
      if (!value) return t('Enter your mobile number.');
      if (/[^0-9+ ()-]/.test(value)) return t('Use digits for your mobile number.');
      const rule = ProfilePhone.countries[country.value];
      if (value.startsWith('+') && !value.replace(/[^0-9+]/g, '').startsWith('+' + rule.code))
        return t('Select the country code that matches your mobile number.');
      if (!ProfilePhone.fits(country.value, value) || !phonePattern.test(ProfilePhone.full(country.value, value)))
        return t('Check your mobile number and country code.');
    }
    if (input.validity.valueMissing) return input.name === 'PrivacyAccepted' ? t('Accept the privacy policy to continue.') : input.validationMessage;
    if (input.validity.tooLong) return t('This field is too long.');
    return '';
  }
  function contactFeedback(input) {
    let feedback = document.getElementById(input.getAttribute('aria-describedby'));
    if (!feedback && input.id && input.closest('#program-learners .field')) {
      feedback = document.createElement('span'); feedback.className = 'field-error'; feedback.id = input.id + '-feedback';
      feedback.setAttribute('aria-live','polite'); input.closest('.field').append(feedback); input.setAttribute('aria-describedby',feedback.id);
    }
    input.setCustomValidity('');
    const message = contactError(input);
    input.setCustomValidity(message);
    input.setAttribute('aria-invalid', String(!input.validity.valid));
    if (feedback) feedback.textContent = input.validity.valid ? '' : message || input.validationMessage;
  }
  contactFields.forEach(input => { let timer; input.addEventListener('input', () => { clearTimeout(timer); timer = setTimeout(() => contactFeedback(input), 450); }); input.addEventListener('blur', () => { clearTimeout(timer); contactFeedback(input); }); input.addEventListener('invalid', () => contactFeedback(input)); });
  const guestCount = field('Attendees');
  function showGuestDigitsToast() {
    window.HoleInHubToasts?.show({ kind: 'warning', title: t('Digits only'), message: t('Use numbers for guest count.'), autoDismiss: true, durationMs: 5000, replaceKey: 'booking-guest-digits' });
  }
  guestCount?.addEventListener('keydown', event => {
    if (event.key.length === 1 && !/[0-9]/.test(event.key) && !event.ctrlKey && !event.metaKey && !event.altKey) {
      event.preventDefault(); showGuestDigitsToast();
    }
  });
  guestCount?.addEventListener('beforeinput', event => {
    if (event.data && /[^0-9]/.test(event.data)) { event.preventDefault(); showGuestDigitsToast(); }
  });
  guestCount?.addEventListener('paste', event => {
    const pasted = event.clipboardData?.getData('text') ?? '';
    if (/[^0-9]/.test(pasted)) { event.preventDefault(); showGuestDigitsToast(); }
  });
  guestCount?.addEventListener('input', () => {
    if (/[^0-9]/.test(guestCount.value)) {
      guestCount.value = guestCount.value.replace(/[^0-9]/g, '').slice(0, 3);
      contactFeedback(guestCount);
      showGuestDigitsToast();
    }
  });
  const timeValue = n => `${String(Math.floor(n / 60)).padStart(2, '0')}:${String(n % 60).padStart(2, '0')}`;
  const minuteValue = value => { const [h, m] = value.split(':').map(Number); return h * 60 + m; };
  const timeLabel = n => new Intl.DateTimeFormat(locale, { hour: 'numeric', minute: '2-digit', timeZone: 'Asia/Manila' }).format(new Date('2026-01-01T00:00:00+08:00').getTime() + n * 60000);
  const selected = () => inputs.filter(x => x.checked && !x.disabled);
  const durationLabel = minutes => { const m = Number(minutes) || 0; if (m < 60) return `${m} ${t('minutes')}`; const h = m / 60; return `${h} ${h === 1 ? t('hour') : t('hours')}`; };
  function summary() {
    syncLearners();
    const bayCapacity = autoBay() ? availableCapacity : selected().reduce((sum, input) => sum + Number(input.dataset.maxGuests), 0);
    const visitors = field('NonPlayerGuests');
    const autoSelected = autoBays.find(b => b.maxTotalGuests >= Number(field('Attendees').value));
    const playerLimit = autoBay() ? Number(autoSelected?.maxPlayers || 0) : selected().reduce((sum, input) => sum + Number(input.dataset.maxPlayers), 0);
    const minimum = Number(field('Attendees').value) - playerLimit;
    const needsSplit = !program() && (autoBay() ? Boolean(autoSelected) : selected().length > 0) && minimum > 0;
    visitors.closest('.field').hidden = visitors.disabled = !needsSplit;
    visitors.required = needsSplit;
    visitors.min = String(Math.max(0, minimum));
    visitors.max = field('Attendees').value;
    const serviceCap = Number(experience()?.dataset.maxPax) || (bookingType() === 'KpgaGroup' ? 8 : Infinity);
    field('Attendees').max = String(program() ? Number(program().seats) : Math.min(bayCapacity || Number.MAX_SAFE_INTEGER, serviceCap, 640));
    const choice = form.querySelector('[name=Service]:checked'), card = choice.closest('label'), service = card.querySelector('strong').textContent, imageSrc = card.querySelector('img').getAttribute('src');
    document.querySelector('#selection-service').textContent = service;
    document.querySelector('#selection-rate').textContent = card.querySelector('.service-rate').textContent;
    document.querySelector('#selection-description').textContent = card.querySelector('.service-description > span:not(.service-rate)')?.textContent || '';
    document.querySelector('#selection-details').textContent = card.dataset.details || '';
    const summaryMedia = document.querySelector('.reservation-summary');
    summaryMedia.dataset.media = card.dataset.media || JSON.stringify([imageSrc]);
    summaryMedia.querySelector('a[data-booking-media]').href = imageSrc;
    const image = document.querySelector('#selection-image'); image.src = imageSrc; image.alt = service;
    const startValue = field('StartTime').value, duration = Number(field('DurationMinutes').value);
    const dateText = field('Date').value ? new Intl.DateTimeFormat(locale, { weekday: 'short', month: 'long', day: 'numeric', year: 'numeric', timeZone: 'Asia/Manila' }).format(new Date(field('Date').value + 'T12:00:00+08:00')) : t('Choose a date');
    const timeText = startValue ? `${timeLabel(minuteValue(startValue))}–${timeLabel(minuteValue(startValue) + duration)}` : t('Choose your hours');
    const baysText = autoBay() ? t('Assigned automatically') : selected().map(x => x.closest('label').querySelector('strong').textContent).join(', ') || t('Choose your bays');
    const guests = field('Attendees').value, name = `${field('FirstName').value} ${field('LastName').value}`.trim();
    const money = quote ? new Intl.NumberFormat(locale, { style: 'currency', currency: 'PHP' }) : null;
    const total = program() ? new Intl.NumberFormat(locale,{style:'currency',currency:'PHP'}).format(Number(program().price) * Number(guests || 0)) : quote ? money.format(quote.rentalTotal) : card.querySelector('.service-rate').dataset.rate;
    const rows = [
      ['Date', t('Date'), dateText],
      ['Time', t('Time'), timeText],
      ['Duration', t('Duration'), startValue ? durationLabel(duration) : '—'],
      ['Bays', t('Bays'), baysText],
      ['Guests', t('Guests'), guests || '—'],
      ['Name', t('Name'), name || '—'],
      ['Total', quote ? t('Booking total') : t('Indicative rate'), total || '—']
    ];
    if (program()) {
      rows.splice(rows.findIndex(row => row[0] === 'Bays'), 1);
      const names = [...roster.querySelectorAll('[data-family-learner]:not([hidden])')].map(row => row.querySelector('[data-participating]').checked ? name : [...row.querySelectorAll('[data-learner-name] input')].map(input => input.value.trim()).join(' ')).filter(Boolean);
      rows.find(row => row[0] === 'Guests').splice(1,2,t('Learners'),names.join(', ') || guests);
      rows.splice(2, 0, ['Lessons',t('Included lessons'), dates().map(formatLesson).join(' · ')]);
    }
    if (quote && quote.depositAmount > 0) rows.push(['Deposit', t('Deposit due'), money.format(quote.depositAmount)], ['Balance', t('Remaining balance'), money.format(quote.rentalTotal - quote.depositAmount)]);
    if (experience()?.dataset.inclusions) rows.push(['Inclusions', t('Included'), experience().dataset.inclusions]);
    const render = (target, keys, merge) => {
      if (!target) return;
      target.replaceChildren();
      for (const [key, label, value] of rows) {
        if (keys && !keys.includes(key)) continue;
        const row = document.createElement('div'), dt = document.createElement('dt'), dd = document.createElement('dd');
        row.className = 'summary-row'; row.dataset.summaryKey = key;
        const missing = value === t('Choose a date') || value === t('Choose your hours') || value === t('Choose your bays') || value === '—';
        row.classList.toggle('is-empty', missing);
        dt.textContent = label; dd.textContent = merge && key === 'Time' && startValue ? `${value} · ${durationLabel(duration)}` : value; row.append(dt, dd); target.append(row);
      }
    };
    render(document.querySelector('#live-selection'), ['Date', 'Time', 'Guests']);
    const review = document.querySelector('#review-selection');
    if (review) {
      document.querySelector('#review-service').textContent = service;
      document.querySelector('#review-image').src = imageSrc;
      if (bookingType() === 'WholeShop') {
        const compact = [['Total', quote ? t('Booking total') : t('Indicative rate'), total], ['Time',t('Session'),dateText + ' · ' + timeText], ['Guests',t('Booked for'),[name, guests + ' ' + t('Guests')].filter(Boolean).join(' · ')]];
        if (quote?.depositAmount > 0) compact.push(['Deposit',t('Payment'),money.format(quote.depositAmount) + ' ' + t('Deposit due') + ' · ' + money.format(quote.rentalTotal - quote.depositAmount) + ' ' + t('Remaining balance')]);
        if (experience().dataset.inclusions) compact.push(['Inclusions',t('Included'),experience().dataset.inclusions]);
        rows.splice(0, rows.length, ...compact);
      }
      render(review, ['Date', 'Time', 'Lessons', 'Bays', 'Guests', 'Name', 'Inclusions', 'Total', 'Deposit', 'Balance'], bookingType() !== 'WholeShop');
    }
    submitState();
  }
  function submitState() {
    const submit = document.querySelector('#booking-submit');
    if (!submit || submit.getAttribute('aria-busy') === 'true') return;
    let reason = '';
    if (bookingType() === 'WholeShop' && form.dataset.demoPayments !== 'true') reason = t('Whole-shop online checkout is unavailable. Contact the venue.');
    else if (sessionProblem) reason = sessionProblem;
    else if (!availabilityReady) reason = status.textContent || t('Checking your selected session…');
    else if (quotePending) reason = t('Wait for the quote to finish loading.');
    submit.disabled = Boolean(reason);
    if (reason) submit.title = reason; else submit.removeAttribute('title');
  }
  function formatLesson(value) {
    return new Intl.DateTimeFormat(locale, {month:'short',day:'numeric',hour:'numeric',minute:'2-digit',timeZone:'Asia/Manila'}).format(new Date(value+':00+08:00'));
  }
  function displayRange(start, minutes) {
    const range = document.createElement('strong');
    range.textContent = durationLabel(minutes);
    document.querySelector('#selected-time-range').replaceChildren(range, timeLabel(start) + '–' + timeLabel(start + minutes));
  }
  const dayChecks = new Map();
  const dayKey = date => experience().value + '/' + date;
  function candidates(date) {
    const fixed = fixedSessions(bookingType(), date);
    const open = venueOpening(date);
    const choices = fixed.length ? fixed : Array.from({ length: (1440 - open) / 30 }, (_, i) => ({ start: open + i * 30, duration: 30 }));
    return choices.filter(x => ReservationCalendar.startFits(bookingType(), date, x.start, x.duration, now()) && allowsInterval(date, x.start, x.duration));
  }
  function noStarts(date) {
    if (program() || bookingType() === 'WholeShop' || date < today) return false;
    const checks = dayChecks.get(dayKey(date));
    if (date !== today && !checks) return false;
    return candidates(date).every(x => checks?.slots.get(x.start)?.length === 0);
  }
  async function loadDay(date) {
    if (!date || program() || bookingType() === 'WholeShop') return;
    const key = dayKey(date), service = experience().value;
    const prior = dayChecks.get(key);
    if (prior && Date.now() - prior.started < 60000) return;
    const checks = { started: Date.now(), slots: new Map(), failed: false, loading: true };
    dayChecks.set(key, checks);
    const queue = candidates(date);
    async function worker() {
      while (queue.length && key === dayKey(field('Date').value)) {
        const option = queue.shift();
        try {
          const query = new URLSearchParams({date, service, startTime: timeValue(option.start), durationMinutes: option.duration});
          const response = await fetch('/Reservations/CheckAvailability?' + query);
          if (!response.ok && response.status !== 400) throw Error();
          const bays = response.ok ? await response.json() : [];
          checks.slots.set(option.start, bays.filter(b => b.available).map(b => b.resource.id));
        } catch { checks.failed = true; }
      }
    }
    if (['Regular','VIP'].includes(bookingType()) && !fixedSessions(bookingType(), date).length) {
      const query = new URLSearchParams({date, service:bookingType()});
      try {
        const response = await fetch('/Reservations/BayDay?' + query);
        if (!response.ok) throw Error();
        const available = await response.json();
        for (const option of queue) checks.slots.set(option.start, available[option.start] || []);
      } catch { checks.failed = true; }
      queue.length = 0;
    } else await Promise.all([worker(),worker(),worker()]);
    if (queue.length) dayChecks.delete(key);
    checks.loading = false;
    if (key === dayKey(field('Date').value)) {
      const start = minuteValue(field('StartTime').value);
      if (field('StartTime').value && (!candidates(date).some(option => option.start === start) || !rangeAvailable(date, start, Number(field('DurationMinutes').value)))) {
        field('StartTime').value = ''; inputs.forEach(input => input.checked = false); refresh();
        if (['Regular', 'VIP'].includes(bookingType()) && !checks.failed) {
          lostSessionKey = key;
          showSessionStatus(t('No bays are available for this session. Choose another date or time.'), true);
        }
      }
      calendar(); slots(); summary();
    }
  }
  function rangeAvailable(date, start, duration) {
    const checks = dayChecks.get(dayKey(date));
    if (!checks) return true;
    let common;
    const step = fixedSessions(bookingType(),date).length ? duration : 30;
    for (let minute = start; minute < start + duration; minute += step) {
      const bays = checks.slots.get(minute);
      if (!bays) return false;
      common = common ? common.filter(id => bays.includes(id)) : bays;
    }
    return !common || common.length > 0;
  }
  function calendar() { checkCalendar(); const title = document.querySelector('#calendar-month'), days = document.querySelector('#calendar-days'); title.textContent = new Intl.DateTimeFormat(locale, { month: 'long', year: 'numeric', timeZone: 'UTC' }).format(new Date(month + '-01T00:00:00Z')); days.replaceChildren(); for (const name of [t('Sun'), t('Mon'), t('Tue'), t('Wed'), t('Thu'), t('Fri'), t('Sat')]) { const el = document.createElement('span'); el.className = 'weekday'; el.textContent = name; days.append(el); } for (const date of ReservationCalendar.monthDays(month)) { if (!date) { days.append(document.createElement('span')); continue; } const b = document.createElement('button'); b.type = 'button'; b.textContent = Number(date.slice(-2)); b.disabled = program() ? !dates().some(value => value.startsWith(date)) : date < today || noStarts(date) || !serviceDayAllowed(bookingType(), date) || (bookingType() === 'WholeShop' && !ReservationCalendar.wholeShopDayAvailable(wholeShopDay(date))); b.classList.toggle('program-date', Boolean(program()) && !b.disabled); b.classList.toggle('selected', !b.disabled && date === field('Date').value); b.setAttribute('aria-pressed', String(!b.disabled && date === field('Date').value)); b.setAttribute('aria-label', new Intl.DateTimeFormat(locale, { dateStyle: 'full', timeZone: 'UTC' }).format(new Date(date + 'T12:00:00Z'))); if (date === today) b.setAttribute('aria-current', 'date'); b.onclick = () => { if (program()) { document.querySelectorAll('#time-slots span')[dates().findIndex(value => value.startsWith(date))]?.focus(); return; } field('Date').value = date; timePeriod = null; const fixed = fixedSessions(bookingType(), date); if (fixed.length) { const choice = fixed.find(x => ReservationCalendar.canStart(bookingType(), date, x.start, now())); field('StartTime').value = choice ? timeValue(choice.start) : ''; field('DurationMinutes').value = (choice || fixed[0]).duration; } inputs.forEach(x => x.checked = false); calendar(); slots(); summary(); refresh(); }; days.append(b); } document.querySelector('#previous-month').disabled = month <= today.slice(0, 7); }
  function slots() {
    if (program()) {
      document.querySelector('.time-selection-heading h3').textContent = t('Included lessons');
      document.querySelector('#time-slots').classList.add('fixed-sessions');
      document.querySelector('#time-period-tabs').replaceChildren();
      document.querySelector('#time-slots').replaceChildren(...dates().map(value => {
        const slot = document.createElement('span'); slot.tabIndex = -1; slot.className = 'program-session'; slot.textContent = formatLesson(value); return slot;
      }));
      document.querySelector('#selected-time-range').textContent = t('All listed lessons are included · Manila time');
      return;
    }
    const target = document.querySelector('#time-slots'), tabs = document.querySelector('#time-period-tabs'); target.replaceChildren(); tabs.replaceChildren();
    const date = field('Date').value, service = bookingType(), opening = new Date(date + 'T00:00:00Z').getUTCDay() === 1 ? 660 : 600, duration = Number(field('DurationMinutes').value) || 60;
    const fixed = fixedSessions(service, date);
    if (index === 1) void loadDay(date);
    document.querySelector('.time-selection-heading h3').textContent = fixed.length ? t('Choose your session') : t('Choose your times');
    const periodTabs = !fixed.length || service === 'WholeShop';
    tabs.hidden = !periodTabs; target.classList.toggle('rental-slots', service === 'WholeShop'); target.classList.toggle('fixed-sessions', !!fixed.length);
    if (!serviceDayAllowed(service, date)) { target.textContent = t('Choose an available program date in the calendar.'); return; }
    if (timePeriod === null) { const chosen = field('StartTime').value ? minuteValue(field('StartTime').value) : Array.from({ length: (1440 - opening) / 30 }, (_, i) => opening + i * 30).find(minute => ReservationCalendar.startFits(service, date, minute, duration, now())); timePeriod = ReservationCalendar.period(chosen ?? opening); }
    if (periodTabs) {
      [t('Morning'), t('Afternoon'), t('Evening')].forEach((label, i) => {
        const button = document.createElement('button'); button.type = 'button'; button.id = 'time-tab-' + i; button.textContent = t(label); button.setAttribute('role', 'tab'); button.setAttribute('aria-selected', String(timePeriod === i)); button.setAttribute('aria-controls', 'time-slots'); button.tabIndex = timePeriod === i ? 0 : -1;
        button.onclick = () => { timePeriod = i; slots(); document.querySelector('#time-tab-' + i).focus(); };
        button.onkeydown = event => { if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return; event.preventDefault(); timePeriod = event.key === 'Home' ? 0 : event.key === 'End' ? 2 : (i + (event.key === 'ArrowRight' ? 1 : 2)) % 3; slots(); document.querySelector('#time-tab-' + timePeriod).focus(); };
        tabs.append(button);
      });
    }
    target.setAttribute('role', periodTabs ? 'tabpanel' : 'group');
    if (!periodTabs) target.removeAttribute('aria-labelledby'); else target.setAttribute('aria-labelledby', 'time-tab-' + timePeriod);
    const options = fixed.length ? fixed.filter(option => !periodTabs || ReservationCalendar.period(option.start) === timePeriod) : Array.from({ length: (1440 - opening) / 30 }, (_, i) => ({ start: opening + i * 30, duration: 30, label: timeLabel(opening + i * 30) })).filter(option => ReservationCalendar.period(option.start) === timePeriod);
    for (const option of options) {
      const button = document.createElement('button'); button.type = 'button'; button.className = 'time-slot'; button.textContent = t(option.label);
      button.disabled = !ReservationCalendar.startFits(service, date, option.start, option.duration, now()) || !allowsInterval(date, option.start, option.duration) || service === 'WholeShop' && !wholeShopDay(date)?.sessions.some(session => session.available && session.startMinute === option.start && session.duration === option.duration);
      if (button.disabled || !rangeAvailable(date, option.start, option.duration) || dayChecks.get(dayKey(date))?.loading) continue;
      const currentStart = minuteValue(field('StartTime').value), active = fixed.length ? currentStart === option.start : option.start >= currentStart && option.start < currentStart + duration;
      button.classList.toggle('selected', active && !button.disabled); button.setAttribute('aria-pressed', String(active && !button.disabled));
      button.dataset.minute = option.start;
      button.onclick = event => { if (suppressTimeClick && event.detail) return; if (fixed.length) { field('StartTime').value = timeValue(option.start); field('DurationMinutes').value = option.duration; inputs.forEach(input => input.checked = false); slots(); summary(); refresh(); } else toggleTime(option.start); };
      if (!fixed.length) {
        button.onpointerdown = event => { if (event.pointerType === 'mouse' && event.button === 0 && !button.disabled) timeDrag = { anchor: option.start, moved: false }; };
        button.onpointerenter = event => { if (timeDrag && (event.buttons & 1) && !button.disabled) { timeDrag.moved = true; selectTimeRange(ReservationCalendar.sessionRange(timeDrag.anchor, option.start), false); } };
        button.onkeydown = event => { if (!event.shiftKey || !['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key)) return; event.preventDefault(); const start = minuteValue(field('StartTime').value); if (!Number.isFinite(start)) return; const end = start + Number(field('DurationMinutes').value) - 30, offset = event.key === 'ArrowLeft' ? -30 : event.key === 'ArrowRight' ? 30 : event.key === 'ArrowUp' ? -90 : 90; selectTimeRange(ReservationCalendar.sessionRange(start, end + offset), true); };
      }
      target.append(button);
    }
    const loading = Boolean(dayChecks.get(dayKey(date))?.loading);
    target.setAttribute('aria-busy', String(loading));
    if (index === 1) document.querySelector('#step-next').disabled = loading;
    if (loading) {
      for (let i = 0; i < Math.min(9, Math.max(3, options.length)); i++) {
        const placeholder = document.createElement('span');
        placeholder.className = 'slot-skeleton'; placeholder.setAttribute('aria-hidden','true'); target.append(placeholder);
      }
    } else if (!target.children.length) {
      const checks = dayChecks.get(dayKey(date));
      const message = document.createElement('p'); message.className = 'slot-empty'; message.textContent = t(checks?.failed ? 'Availability could not be loaded. Please refresh and try again.' : 'No available times in this period.'); target.append(message);
    }
    const start = minuteValue(field('StartTime').value);
    if (field('StartTime').value) displayRange(start, duration); else document.querySelector('#selected-time-range').textContent = t('Choose your start time');
  }
  function toggleTime(minute) {
    const start = minuteValue(field('StartTime').value);
    if (!field('StartTime').value && lostSessionKey === dayKey(field('Date').value)) {
      const duration = Number(field('DurationMinutes').value);
      if (rangeAvailable(field('Date').value, minute, duration) && allowsInterval(field('Date').value, minute, duration) &&
          ReservationCalendar.startFits(bookingType(), field('Date').value, minute, duration, now())) {
        selectTimeRange({ start: minute, duration }, true);
      } else showSessionStatus(t('No bays are available for this session. Choose another date or time.'), true);
      return;
    }
    const end = start + Number(field('DurationMinutes').value);
    if (field('StartTime').value) {
      if (minute === start - 30 || minute === end) {
        selectTimeRange(ReservationCalendar.sessionRange(Math.min(start, minute), Math.max(end - 30, minute)), true);
        return;
      }
      if (minute >= start && minute < end) {
        if (minute > start) selectTimeRange({ start, duration: minute - start }, true);
        else if (end - start > 30) selectTimeRange({ start: start + 30, duration: end - start - 30 }, true);
        else {
          field('StartTime').value = '';
          inputs.forEach(input => input.checked = false);
          slots(); summary(); refresh();
        }
        return;
      }
    }
    selectTimeRange({ start: minute, duration: 30 }, true);
  }
  function selectTimeRange(range, commit) {
    const opening = new Date(field('Date').value + 'T00:00:00Z').getUTCDay() === 1 ? 660 : 600;
    if (!rangeAvailable(field('Date').value, range.start, range.duration) || !allowsInterval(field('Date').value, range.start, range.duration) || range.start < opening || range.duration > 840 || !ReservationCalendar.startFits(bookingType(), field('Date').value, range.start, range.duration, now())) return;
    field('StartTime').value = timeValue(range.start); field('DurationMinutes').value = range.duration;
    lostSessionKey = undefined;
    inputs.forEach(input => { input.checked = false; input.disabled = true; }); availabilityReady = false; pending?.abort(); sequence++;
    document.querySelectorAll('#time-slots [data-minute]').forEach(button => { const minute = Number(button.dataset.minute), active = minute >= range.start && minute < range.start + range.duration; button.classList.toggle('selected', active); button.setAttribute('aria-pressed', String(active)); });
    displayRange(range.start, range.duration);
    summary(); if (commit) refresh();
  }
  window.addEventListener('pointerup', () => { if (!timeDrag) return; suppressTimeClick = timeDrag.moved; timeDrag = null; if (suppressTimeClick) refresh(); setTimeout(() => suppressTimeClick = false, 0); });
  window.addEventListener('pointercancel', () => { if (timeDrag?.moved) refresh(); timeDrag = null; }); window.addEventListener('blur', () => { if (timeDrag?.moved) refresh(); timeDrag = null; });
  async function refresh() {
    pending?.abort(); pending = new AbortController(); const token = ++sequence;
    autoBays = []; availabilityReady = false; sessionProblem = ''; availableCapacity = 0; quoteSequence++; quotePending = false; quote = null; quoteError = ''; field('QuoteKey').value = '';
    inputs.forEach(input => { input.disabled = true; input.closest('label').hidden = autoBay() || bookingType() !== 'WholeShop' && input.dataset.service !== (bookingType() === 'VIP' ? 'VIP' : 'Regular'); });
    if (program()) {
      availableCapacity = Number(program().seats); availabilityReady = true; quote = {rentalTotal:Number(program().price),depositAmount:0}; status.textContent = ''; summary(); return;
    }
    if (!ReservationCalendar.scheduleReady(field('Date').value, field('StartTime').value, field('DurationMinutes').value)) {
      inputs.forEach(input => input.checked = false); status.textContent = ''; showSessionStatus(''); summary(); return;
    }
    error.textContent = ''; showSessionStatus(t('Checking your selected session…'));
    const query = new URLSearchParams({ date: field('Date').value, startTime: field('StartTime').value, durationMinutes: field('DurationMinutes').value, service: experience().value });
    try {
      const response = await fetch('/Reservations/CheckAvailability?' + query, { signal: pending.signal });
      if (!response.ok) { const problem = await response.json().catch(() => null); throw Error(problem?.message || t('Choose a valid session within venue opening hours.')); }
      const data = await response.json(); if (token !== sequence) return;
      const wholeShop = bookingType() === 'WholeShop';
      sessionProblem = t(ReservationCalendar.sessionIssue(bookingType(), data));
      if (wholeShop && !sessionProblem && (data.some(bay => !inputs.some(input => input.value === bay.resource.id)))) {
        sessionProblem = t('The bay catalog changed. Reload and select the whole-shop session again.');
      }
      autoBays = data.filter(b => b.available).map(b => b.resource).sort((a,b) => a.maxTotalGuests - b.maxTotalGuests || a.id.localeCompare(b.id));
      availableCapacity = Math.max(0, ...data.filter(bay => bay.available).map(bay => bay.resource.maxTotalGuests));
      for (const input of inputs) {
        const item = data.find(bay => bay.resource.id === input.value);
        input.disabled = autoBay() || !item?.available || (wholeShop && Boolean(sessionProblem));
        input.closest('label').classList.toggle('unavailable', !item?.available);
        input.closest('label').querySelector('.resource-status').textContent = item?.available ? '' : t('Unavailable');
        if (wholeShop || input.disabled) input.checked = wholeShop && !input.disabled;
      }
      availabilityReady = true;
      if (wholeShop && sessionProblem) { monthAvailability.delete(calendarKey()); calendar(); slots(); }
      const message = sessionProblem || (wholeShop ? t('All included bays are available together.') : autoBay() ? t('We’ll assign an available bay for your group.') : '');
      status.textContent = message; showSessionStatus(message, Boolean(sessionProblem)); summary(); void updateQuote();
    } catch (e) {
      if (e.name === 'AbortError' || token !== sequence) return;
      sessionProblem = e.message === 'Failed to fetch' ? t('Availability could not be loaded. Please retry.') : t(e.message);
      status.textContent = sessionProblem; showSessionStatus(sessionProblem, true); summary();
    }
  }
  async function updateQuote() {
    const token = ++quoteSequence;
    const bayCount = autoBay() ? 1 : selected().length;
    field('QuoteKey').value = '';
    quote = null;
    quoteError = '';
    quotePending = availabilityReady && !sessionProblem && bayCount > 0 && (bookingType() !== 'WholeShop' || form.dataset.demoPayments === 'true');
    summary();
    if (!quotePending) return;
    const query = new URLSearchParams({ date: field('Date').value, startTime: field('StartTime').value, durationMinutes: field('DurationMinutes').value, service: experience().value, bayCount });
    try {
      const response = await fetch('/Reservations/RentalQuote?' + query);
      if (!response.ok) { const failure = await response.json().catch(() => null); throw new Error(failure?.message || t('The quote could not be loaded. Select your bays again to retry.')); }
      const result = await response.json();
      if (token !== quoteSequence) return;
      quote = result;
      field('QuoteKey').value = result.quoteKey;
    } catch (error) {
      if (token === quoteSequence) { quoteError = error.message; status.textContent = quoteError; }
    } finally {
      if (token === quoteSequence) { quotePending = false; summary(); }
    }
  }
  function showMissingToast(message) {
    window.HoleInHubToasts?.show({ kind: 'warning', title: t('Check your reservation details'), message, autoDismiss: true, durationMs: 5000, replaceKey: 'booking-validation' });
  }
  function shakeFields(fields) {
    fields.forEach(input => { const target = input.closest('.field,.booking-privacy') || input; target.classList.remove('field--shake'); void target.offsetWidth; target.classList.add('field--shake'); });
  }
  function validate(step) {
    if (step === 1 && dayChecks.get(dayKey(field('Date').value))?.loading) return false;
    error.textContent = '';
    if (bookingType() === 'WholeShop' && form.dataset.demoPayments !== 'true') {
      error.textContent = t('Whole-shop online checkout is unavailable. Contact the venue.'); return false;
    }
    if (step === 1 && program()) return true;
    if (step === 1) {
      const date = field('Date').value, start = minuteValue(field('StartTime').value), duration = Number(field('DurationMinutes').value);
      if (!date || !field('StartTime').value) { showSessionStatus(t('Choose a date and start time to check availability.'), true); return false; }
      if (bookingType() === 'WholeShop' && !ReservationCalendar.wholeShopDayAvailable(wholeShopDay(date))) {
        showSessionStatus(t('Choose an available whole-shop date in the calendar.'), true); return false;
      }
      if (!serviceDayAllowed(bookingType(), date)) { showSessionStatus(t('Choose an available program date in the calendar.'), true); return false; }
      if (date < today || !Number.isFinite(start) || start < venueOpening(date) || start + duration > 1440 || duration < 30 || !canStart(start) || !allowsInterval(date, start, duration)) {
        showSessionStatus(t('Choose a later start with the full review window and at least 30 minutes within opening hours.'), true); return false;
      }
      if (sessionProblem || !availabilityReady) { showSessionStatus(sessionProblem || t('Checking your selected session…'), Boolean(sessionProblem)); return false; }
    }
    if (step === 2 && skipBays()) return true;
    if (step === 2 && !selected().length) {
      showMissingToast(t('Select at least one bay to continue.')); return false;
    }
    if (step === 3) {
      contactFields.filter(input => !input.matches(':disabled')).forEach(contactFeedback);
      const invalid = ReservationCalendar.invalidFields(contactFields.filter(input => !input.matches(':disabled')));
      if (invalid.length) {
        shakeFields(invalid);
        showMissingToast(invalid[0].validationMessage);
        invalid[0].focus(); return false;
      }
    }
    if ((step === 2 || step === 3 && skipBays()) && !quote) {
      error.textContent = quoteError || t(quotePending ? 'Wait for the quote to finish loading.' : autoBay() ? 'The quote could not be loaded. Choose your time again.' : 'The quote could not be loaded. Select your bays again to retry.'); return false;
    }
    const missing = [...steps[step].querySelectorAll('input,select,textarea')].filter(input => input.willValidate && !input.validity.valid);
    if (missing.length) { shakeFields(missing); showMissingToast(t('Please complete the highlighted fields.')); missing[0].focus(); return false; }
    return true;
  }
  function show(next, focus = true) { index = next === 2 && skipBays() ? 1 : next; document.querySelector('#step-next').disabled = false; form.classList.toggle('is-review-step', index === 4); if (index === 1) { calendar(); slots(); } const shopPolicy = document.getElementById('whole-shop-policy'); if (shopPolicy) shopPolicy.hidden = bookingType() !== 'WholeShop'; const wholeShop = bookingType() === 'WholeShop'; document.querySelector('#coaching-policy-tip').hidden = wholeShop || ['Regular', 'VIP'].includes(bookingType()); document.querySelector('#review-policy-tip').hidden = wholeShop; for (const id of ['booking-submit', 'booking-verify-required']) { const button = document.getElementById(id); if (button) { button.hidden = index !== 4; if (id === 'booking-submit') button.disabled = wholeShop && form.dataset.demoPayments !== 'true'; } } const note = document.querySelector('#service-capacity-note'); note.hidden = !wholeShop; note.textContent = form.dataset.demoPayments !== 'true' ? t('Whole-shop online checkout is unavailable. Contact the venue.') : t('Your bays are held together for 60 minutes while you complete the 50% deposit.'); document.querySelector('#bay-selection-help').textContent = bookingType() === 'WholeShop' ? t('All active bays are selected together for exclusive use. If any bay is unavailable, choose a different session.') : t('Choose one bay or bring your group together across several. Click a bay or drag across available bays to select several.'); steps.forEach((s, i) => s.hidden = i !== index); tabs.forEach((tab, i) => { tab.hidden = skipBays() && i === 2; tab.querySelector(".step-tab-num").textContent = String((skipBays() && i > 2 ? i : i + 1)).padStart(2, "0"); tab.disabled = i > completed; tab.classList.toggle('current', i === index); tab.classList.toggle('complete', i < index); if (i === index) tab.setAttribute('aria-current', 'step'); else tab.removeAttribute('aria-current'); }); document.querySelector('#step-back').hidden = index === 0; document.querySelector('#step-next').hidden = index === 4; summary(); window.ScrollTrigger?.refresh(); if (focus) { const heading = steps[index].querySelector('legend'); heading.tabIndex = -1; heading.focus({ preventScroll: true }); if (!matchMedia('(prefers-reduced-motion: reduce)').matches) { window.HoleInHubMotion?.enter(steps[index], { y: 12, opacity: .5, duration: .4 }); const smoother = window.ScrollSmoother?.get(); if (smoother) smoother.scrollTo(120, true); else window.scrollTo({ top: 120, behavior: 'smooth' }); } } }
  let stepTransition = 0;
  function transitionTo(next) {
    const target = next === 2 && skipBays() ? 1 : next;
    const current = steps[index];
    const token = ++stepTransition;
    const gsap = window.gsap;
    if (target === index || !gsap || matchMedia('(prefers-reduced-motion: reduce)').matches) { show(next); return; }
    gsap.killTweensOf(steps);
    gsap.to(current, { autoAlpha: 0, y: -8, duration: .16, ease: 'power1.in', onComplete() {
      if (token !== stepTransition) return;
      show(next, false);
      const incoming = steps[index];
      gsap.fromTo(incoming, { autoAlpha: 0, y: 12 }, { autoAlpha: 1, y: 0, duration: .34, ease: 'power2.out', clearProps: 'opacity,visibility,transform' });
      const heading = incoming.querySelector('legend');
      heading.tabIndex = -1;
      heading.focus({ preventScroll: true });
      const smoother = window.ScrollSmoother?.get();
      if (smoother) smoother.scrollTo(120, true); else window.scrollTo({ top: 120, behavior: 'smooth' });
    } });
  }
  document.querySelector('#previous-month').onclick = () => { month = ReservationCalendar.shiftMonth(month, -1); calendar(); }; document.querySelector('#next-month').onclick = () => { month = ReservationCalendar.shiftMonth(month, 1); calendar(); }; document.querySelector('#step-back').onclick = () => transitionTo(index === 3 && skipBays() ? 1 : index - 1); document.querySelector('#step-next').onclick = () => { if (validate(index)) { const next = index === 1 && skipBays() ? 3 : index + 1; completed = Math.max(completed, next); transitionTo(next); } }; tabs.forEach((tab, i) => tab.onclick = () => { if (i <= completed && !(index === 1 && i > 1 && !validate(1))) transitionTo(i); }); form.addEventListener('input', summary); form.addEventListener('change', event => { if (event.target.name === 'ResourceIds' && bookingType() === 'WholeShop') inputs.forEach(x => x.checked = !x.disabled); if (event.target.name === 'ResourceIds') void updateQuote(); if (['Service', 'Date', 'StartTime', 'DurationMinutes'].includes(event.target.name)) { if (program()) {
      selectFirstLesson();
      inputs.forEach(input => input.checked = false); calendar(); slots(); refresh(); return;
    }
    if (event.target.name === 'Service') alignServiceDate(); if (event.target.name === 'Service' || event.target.name === 'Date') { timePeriod = null; const fixed = fixedSessions(bookingType(), field('Date').value); if (fixed.length) { const choice = fixed.find(x => canStart(x.start)); field('StartTime').value = choice ? timeValue(choice.start) : ''; field('DurationMinutes').value = (choice || fixed[0]).duration; } else if (event.target.name === 'Service' || Number(field('DurationMinutes').value) > 840) field('DurationMinutes').value = 60; } inputs.forEach(x => x.checked = false); calendar(); slots(); refresh(); } summary(); }); form.addEventListener('invalid', event => { const i = steps.indexOf(event.target.closest('.booking-step')); if (i >= 0 && i !== index) transitionTo(i); }, true); form.addEventListener('submit', event => { if (event.submitter?.name === 'intent') return; for (let i = 0; i < 4; i++)if (!validate(i)) { event.preventDefault(); transitionTo(i); return; } const submit = document.querySelector('#booking-submit'); if (submit) { submit.disabled = true; submit.setAttribute('aria-busy', 'true'); } });
  const authDialog = document.querySelector('#booking-auth-dialog');
  authDialog?.addEventListener('close', () => window.ScrollSmoother?.get()?.paused(Boolean(document.querySelector('dialog:modal, #site-menu[open]'))));
  function continueAsGuest() { try { sessionStorage.setItem('holeinhub-guest-mode', 'true'); } catch { } authDialog?.close(); }
  document.querySelector('#booking-browse')?.addEventListener('click', continueAsGuest);
  authDialog?.querySelector('.dialog-close').addEventListener('click', continueAsGuest);
  authDialog?.addEventListener('cancel', () => { try { sessionStorage.setItem('holeinhub-guest-mode', 'true'); } catch { } });
  document.querySelectorAll('a[href^="/account/sign-in"],a[href^="/account/sign-up"],a[href^="/account/verify-email"]').forEach(link => {
    const destination = new URL(link.getAttribute('href'), location.origin); destination.searchParams.set('returnUrl', '/Reservations/Create'); link.href = destination.pathname + destination.search;
    link.addEventListener('click', () => { try { const selection = { savedAt: Date.now(), Service: experience().value, classId: program()?.program, ResourceIds: selected().map(x => x.value) }; for (const name of ['Date', 'StartTime', 'DurationMinutes']) selection[name] = field(name).value; sessionStorage.setItem('holeinhub-signin-selection', JSON.stringify(selection)); } catch { } });
  });
  let bayDrag = null, suppressBayClick = false;
  const tiles = inputs.map(input => input.closest('label'));
  tiles.forEach(tile => {
    tile.addEventListener('dragstart', event => event.preventDefault());
    tile.addEventListener('pointerdown', event => { const input = tile.querySelector('input'); if (event.pointerType !== 'mouse' || event.button !== 0 || input.disabled || bookingType() === 'WholeShop') return; bayDrag = { anchor: input, value: !input.checked, before: new Map(inputs.map(x => [x, x.checked])), moved: false }; });
    tile.addEventListener('pointerenter', event => { if (!bayDrag || !(event.buttons & 1)) return; const input = tile.querySelector('input'); if (input.disabled) return; bayDrag.moved = true; const available = inputs.filter(x => !x.disabled && !x.closest('label').hidden), range = ReservationCalendar.bayRange(available, bayDrag.anchor, input); inputs.forEach(x => x.checked = range.includes(x) ? bayDrag.value : bayDrag.before.get(x)); summary(); });
    tile.addEventListener('click', event => { if (suppressBayClick) { event.preventDefault(); event.stopPropagation(); } });
  });
  window.addEventListener('pointerup', () => { suppressBayClick = !!bayDrag?.moved; bayDrag = null; if (suppressBayClick) void updateQuote(); setTimeout(() => suppressBayClick = false, 0); });
  window.addEventListener('pointercancel', () => bayDrag = null); window.addEventListener('blur', () => bayDrag = null);

  try {
    const query = new URLSearchParams(location.search);
    if (['service', 'date', 'startTime', 'durationMinutes'].every(key => query.has(key))) {
      sessionStorage.removeItem('holeinhub-signin-selection');
    }
    const draft = JSON.parse(sessionStorage.getItem('holeinhub-signin-selection'));
    if (form.dataset.signedIn === 'true' && draft) {
      sessionStorage.removeItem('holeinhub-signin-selection');
      const service = [...form.querySelectorAll('[name=Service]')].find(option => !option.disabled && option.value === draft.Service && (!draft.classId || option.dataset.program === draft.classId));
      if (service && ReservationCalendar.validSelection(draft, Date.now()) && draft.ResourceIds.every(id => inputs.some(input => input.value === id))) {
        service.checked = true;
        for (const name of ['Date', 'StartTime', 'DurationMinutes']) field(name).value = draft[name];
        inputs.forEach(input => input.checked = draft.ResourceIds.includes(input.value));
        month = field('Date').value.slice(0, 7);
      } else error.textContent = t('Your previous selection expired. Choose your session again.');
    }
  } catch { }
  document.querySelector('#booking-calendar').hidden = false; document.querySelector('#enhanced-time').hidden = false; document.querySelector('#step-controls').hidden = false; form.classList.add('is-enhanced'); form.noValidate = true; field('Date').min = today; calendar(); slots(); completed = form.dataset.feedback === 'true' ? 4 : ReservationCalendar.errorStep(JSON.parse(form.dataset.errorFields)); if (program()) {
    selectFirstLesson();
    calendar(); slots(); if (!completed) completed = 1;
  }
  show(completed, false); refresh();
  if (authDialog) {
    let guestMode = false;
    try { guestMode = sessionStorage.getItem('holeinhub-guest-mode') === 'true'; } catch { }
    if (!guestMode) {
      const root = document.documentElement;
      const showPrompt = () => {
        authDialog.showModal();
        window.ScrollSmoother?.get()?.paused(true, true);
      };
      if (root.classList.contains('mvc-arriving')) {
        const arrival = new MutationObserver(() => {
          if (root.classList.contains('mvc-arriving')) return;
          arrival.disconnect();
          if (!root.classList.contains('mvc-leaving')) showPrompt();
        });
        arrival.observe(root, { attributes: true, attributeFilter: ['class'] });
      } else showPrompt();
    }
  }
  document.querySelectorAll('.info-tip').forEach(tip => {
    const button = tip.querySelector('.info-tip-btn'), bubble = tip.querySelector('.info-tip-bubble');
    if (!button || !bubble) return;
    const position = () => {
      if (!tip.classList.contains('is-open')) return;
      const trigger = button.getBoundingClientRect();
      bubble.style.position = 'fixed';
      bubble.style.inset = 'auto';
      bubble.style.left = '0px';
      bubble.style.top = '0px';
      bubble.style.right = 'auto';
      bubble.style.bottom = 'auto';
      bubble.style.maxWidth = 'calc(100vw - 24px)';
      bubble.style.maxHeight = 'calc(100dvh - 24px)';
      bubble.style.overflowY = 'auto';
      bubble.style.zIndex = '10000';
      const size = bubble.getBoundingClientRect();
      const left = Math.max(12, Math.min(trigger.left, window.innerWidth - size.width - 12));
      const above = trigger.top - size.height - 10;
      const below = trigger.bottom + 10;
      const top = above >= 12 ? above : Math.min(below, window.innerHeight - size.height - 12);
      bubble.style.left = `${left}px`;
      bubble.style.top = `${Math.max(12, top)}px`;
    };
    const set = open => {
      if (open && !tip.classList.contains('is-open')) {
        const style = getComputedStyle(bubble);
        for (const property of ['width', 'padding', 'fontFamily', 'fontSize', 'fontWeight', 'letterSpacing', 'lineHeight', 'textTransform', 'border', 'backgroundColor', 'color', 'boxShadow']) bubble.style[property] = style[property];
        document.body.appendChild(bubble);
        bubble.classList.add('is-floating');
      }
      tip.classList.toggle('is-open', open);
      button.setAttribute('aria-expanded', String(open));
      if (open) position();
      else {
        bubble.classList.remove('is-floating');
        bubble.removeAttribute('style');
        tip.appendChild(bubble);
      }
    };
    button.addEventListener('click', event => { event.preventDefault(); event.stopPropagation(); set(!tip.classList.contains('is-open')); });
    document.addEventListener('click', event => { if (!tip.contains(event.target) && !bubble.contains(event.target)) set(false); });
    tip.addEventListener('keydown', event => { if (event.key === 'Escape') { set(false); button.focus(); } });
    window.addEventListener('resize', position, { passive: true });
    window.addEventListener('scroll', position, { passive: true, capture: true });
  });
  setInterval(() => { if (timeDrag || program()) return; slots(); if (field('StartTime').value && !canStart(minuteValue(field('StartTime').value))) { availabilityReady = false; status.textContent = t('This start time is no longer bookable. Choose a later time.'); inputs.forEach(input => { input.checked = false; input.disabled = true; }); summary(); } }, 30000);
})();
