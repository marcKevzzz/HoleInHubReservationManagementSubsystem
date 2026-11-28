function venueTopic(input) {
    const q = input.toLowerCase().trim();
    if (/\b(?:restart(?: the chat)?|start over|reset (?:the )?chat)\b/.test(q)) return 'Restart';
    if (/\b(?:main menu|menu|show options)\b/.test(q)) return 'Menu';
    if (/\b(staff|human|agent|contact|support)\b/.test(q)) return 'Staff';
    if (/\b(ratings?|reviews?|stars?)\b/.test(q)) return 'Ratings';
    if (/\b(popular|busiest|favorites?|favourites?|best bay|most booked)\b/.test(q)) return /\bbays?\b/.test(q) ? 'Popular bays' : 'Popular services';
    if (/\b(promo\w*|discounts?|deals?|offers|special offer)\b/.test(q)) return 'Promos';
    if (/\b(bays?|vip|regular)\b/.test(q) && /\b(?:sun|mon|tues|wednes|thurs|fri|satur)day\b|\b\d{4}-\d{2}-\d{2}\b/.test(q)) return 'Check dates';
    if (/\b(avail\w*|avial\w*|vacan\w*|dates?|slots?|free bay|today|tomorrow|book a bay)\b/.test(q)) return 'Check dates';
    if (/\b(pack\w*|bundles?)\b/.test(q)) return 'Packages';
    if (/\b(rates?|prices?|pric\w*|costs?|much|magkano|hm)\b/.test(q)) return 'Rates';
    if (/\b(servi\w*|servce\w*|coaching|lessons?|vip|regular|bays?)\b/.test(q)) return 'Services';
    if (/^(hi+|hey+|hello+|good (morning|evening|afternoon)|kumusta|kamusta)[!. ?]*$/.test(q)) return 'Greeting';
    if (/^(thanks?\b|thank you\b|salamat\b|awesome[!. ]*$|great[!. ]*$|love (it|this)|nice (place|venue)|you.re (great|helpful))/.test(q)) return 'Thanks';
    return 'Unknown';
}

function bayRequest(text, now = Date.now(), previous = {}) {
    const day = new Date(now + 8 * 3600000);
    const weekday = /\b(sunday|monday|tuesday|wednesday|thursday|friday|saturday)\b/i.exec(text);
    let date = text.match(/\b\d{4}-\d{2}-\d{2}\b/)?.[0] || '';
    if (!date && (/\b(today|tomorrow)\b/i.test(text) || weekday)) {
        const days = ['sunday','monday','tuesday','wednesday','thursday','friday','saturday'];
        let offset = /tomorrow/i.test(text) ? 1 : 0;
        if (weekday) {
            offset = (days.indexOf(weekday[1].toLowerCase()) - day.getUTCDay() + 7) % 7;
            if (!offset && /\bnext\s+/i.test(text)) offset = 7;
        }
        day.setUTCDate(day.getUTCDate() + offset);
        date = day.toISOString().slice(0, 10);
    }
    const clock = text.match(/\b(1[0-2]|0?[1-9])(?::([0-5]\d))?\s*([ap])\.?m\b/i);
    const military = text.match(/\b([01]?\d|2[0-3]):([0-5]\d)\b/);
    const hour = clock ? Number(clock[1]) % 12 + (/p/i.test(clock[3]) ? 12 : 0) : military && Number(military[1]);
    const startTime = hour == null ? '' : String(hour).padStart(2, '0') + ':' + (clock?.[2] || military?.[2] || '00');
    const length = text.match(/\b(\d+(?:\.\d+)?)\s*(hours?|hrs?|minutes?|mins?)\b/i);
    const durationMinutes = length ? Number(length[1]) * (/^h/i.test(length[2]) ? 60 : 1) : previous.durationMinutes ?? 60;
    const service = /\bvip\b/i.test(text) ? 'VIP' : /\bregular\b/i.test(text) ? 'Regular' : previous.service || '';
    return { service, date: date || previous.date || '', startTime: startTime || previous.startTime || '', durationMinutes };
}

function displayDate(date) {
    return new Intl.DateTimeFormat('en-US', { dateStyle: 'full', timeZone: 'UTC' }).format(new Date(date + 'T12:00:00Z'));
}
function displayTime(time) {
    const [hour, minute] = time.split(':');
    return (Number(hour) % 12 || 12) + ':' + minute + (Number(hour) < 12 ? ' AM' : ' PM');
}
if (typeof module !== 'undefined') module.exports = { venueTopic, bayRequest };
if (typeof document !== 'undefined') (() => {
    const chat = document.querySelector('#venue-chat');
    if (!chat) return;
    const get = selector => chat.querySelector(selector);
    const log = get('[role=log]'), options = get('[data-options]'), slots = get('[data-slots]');
    const compose = get('[data-compose]'), input = compose.elements.message;
    const topics = ['Check dates', 'Services', 'Rates', 'Packages', 'Promos', 'Popular bays', 'Popular services', 'Ratings'];
    let generation = 0, services = [], lastBooking = null;
    const tags = new Set();
    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)');
    const typing = get('[data-typing]');
    const timeFormat = new Intl.DateTimeFormat(undefined, { hour: 'numeric', minute: '2-digit' });
    const scroll = () => { get('.chat-scroll').scrollTop = get('.chat-scroll').scrollHeight; };
    async function beginReply(question) {
        const turn = generation;
        say(question, true);
        typing.hidden = false;
        scroll();
        if (!reduced.matches) await new Promise(resolve => setTimeout(resolve, 850));
        return turn === generation;
    }
    function say(message, customer = false) {
        typing.hidden = true;
        const p = document.createElement('p');
        p.textContent = message;
        p.className = customer ? 'chat-question' : 'chat-answer';
        const row = document.createElement('div');
        row.className = 'chat-turn';
        const time = document.createElement('time');
        const now = new Date();
        time.dateTime = now.toISOString();
        time.textContent = timeFormat.format(now);
        const author = document.createElement('span');
        author.className = 'chat-author';
        author.textContent = customer ? 'You' : 'Assistant';
        row.append(author, p, time);
        log.append(row);
        scroll();
    }
    function choice(title, action) {
        const button = document.createElement('button');
        button.type = 'button';
        button.textContent = title;
        button.onclick = action;
        options.append(button);
    }
    function link(label, href) {
        const a = document.createElement('a');
        a.href = href;
        a.textContent = label;
        const message = log.lastElementChild;
        let links = message.querySelector('.chat-links');
        if (!links) {
            links = document.createElement('div');
            links.className = 'chat-links';
            message.insertBefore(links, message.querySelector('time'));
        }
        links.append(a);
        scroll();
        return a;
    }
    function clearOptions() {
        generation++;
        typing.hidden = true;
        slots.hidden = true;
        options.replaceChildren();
        options.scrollLeft = 0;
    }
    function menu() {
        clearOptions();
        topics.forEach(topic => choice(topic, () => ask(topic)));
        scroll();
    }
    function details(item) {
        say([item.title, item.rateText && 'Indicative rate: ' + item.rateText, item.summary || item.details].filter(Boolean).join('\n'));
        link('Service details', '/Home/Services');
        link('Booking options', item.actionUrl);
    }
    function handoff() {
        say('Open an email draft with this chat, or save the conversation to share with staff. You can review it before sending.');
        const transcript = () => ['Topics: ' + [...tags].join(', '), ...[...log.querySelectorAll('p')].map(p =>
            (p.classList.contains('chat-question') ? 'Customer: ' : 'Assistant: ') + p.textContent)].join('\n\n');
        choice('Draft email with chat', async () => {
            const turn = generation;
            try {
                const response = await fetch('/Home/Contacts', { cache: 'no-store' });
                if (!response.ok) throw new Error();
                const contacts = new DOMParser().parseFromString(await response.text(), 'text/html');
                const email = contacts.querySelector('.venue-contact a[href^="mailto:"]')?.getAttribute('href');
                if (!email || !/^mailto:[^\s?#]+@[^\s?#]+$/i.test(email)) throw new Error();
                if (turn !== generation) return;
                const body = encodeURIComponent(transcript());
                if (body.length > 1600) {
                    say('This chat is long. Save the conversation and attach the file to your email.');
                    link('Open staff email', email);
                } else {
                    link('Review email draft', email + '?subject=Venue%20chat%20enquiry&body=' + body);
                }
                scroll();
            } catch {
                if (turn === generation) say('Use Open staff contacts to reach the venue team.');
            }
        });
        choice('Save conversation', () => {
            const url = URL.createObjectURL(new Blob([transcript()], { type: 'text/plain' }));
            const a = document.createElement('a');
            a.href = url;
            a.download = 'hole-in-hub-chat.txt';
            a.click();
            setTimeout(() => URL.revokeObjectURL(url), 1000);
        });
        const contact = link('Open staff contacts', '/Home/Contacts');
        contact.target = '_blank';
        contact.rel = 'noopener';
    }
    async function ask(question, display = question) {
        clearOptions();
        const turn = generation;
        if (!await beginReply(display)) return;
        let topic = venueTopic(question);
        const named = services.find(s => s.title.toLowerCase() === question.toLowerCase());
        if (named) topic = 'Services';
        const changes = bayRequest(question);
        if (lastBooking && (topic === 'Unknown' || (topic === 'Services' && !named)) &&
            (changes.date || changes.startTime || /\b\d+\s*(hours?|hrs?|minutes?|mins?)\b/i.test(question) ||
            (changes.service && /^(?:(?:what|how) about )?(?:vip|regular)(?: bays?)?[?.! ]*$/i.test(question)))) topic = 'Check dates';
        if (topic !== 'Check dates') lastBooking = null;
        if (topics.includes(topic)) tags.add(topic);
        if (topic === 'Menu') { say('Choose a topic.'); menu(); }
        else if (topic === 'Restart') {
            say('Start a fresh conversation?');
            choice('Restart chat', () => { log.replaceChildren(); tags.clear(); services = []; slots.reset(); compose.reset(); menu(); input.focus(); });
        } else if (topic === 'Staff') handoff();
        else if (topic === 'Greeting' || topic === 'Thanks') {
            say(topic === 'Greeting' ? 'Hi! Planning a visit? Choose a topic or ask me a question.' : 'Thank you! What else can I help with?');
            menu();
        } else if (topic === 'Check dates') {
            lastBooking = bayRequest(question, Date.now(), lastBooking || {});
            if (!lastBooking.service) {
                say('Which bay would you like to check?');
                for (const type of ['Regular', 'VIP']) choice(type, () => ask(type + ' availability', type));
            } else if (!lastBooking.date) {
                say('Which day would you like to visit?');
                slots.hidden = false;
                slots.elements.date.min = bayRequest('today').date;
                slots.elements.date.value = '';
                for (const day of ['Today', 'Tomorrow']) choice(day, () => ask(day));
            } else if (!lastBooking.startTime) {
                say('What time on ' + displayDate(lastBooking.date) + '? Choose or type a start time, such as 3:30 PM.');
                const response = await fetch('/Reservations/BayDay?' + new URLSearchParams({ date: lastBooking.date, service: lastBooking.service })).catch(() => null);
                const times = response?.ok ? await response.json().catch(() => ({})) : {};
                if (turn !== generation) return;
                for (const minute of Object.keys(times).map(Number).sort((a,b) => a-b)) {
                    if (minute + lastBooking.durationMinutes > 1440) continue;
                    const value = String(Math.floor(minute / 60)).padStart(2,'0') + ':' + String(minute % 60).padStart(2,'0');
                    if (new Date(lastBooking.date + 'T' + value + ':00+08:00') <= new Date()) continue;
                    choice(displayTime(value), () => ask(displayTime(value)));
                }
            } else await checkBays(new URLSearchParams(lastBooking), turn);
        } else if (topic.startsWith('Popular ')) {
            const response = await fetch('/Home/ChatPopular').catch(() => null);
            const ranks = response?.ok ? await response.json().catch(() => null) : null;
            if (turn !== generation) return;
            const kind = topic === 'Popular bays' ? 'bays' : 'services';
            const names = ranks?.[kind];
            say(!names ? 'I couldn’t load the booking rankings right now.' : names.length
                ? 'Most booked among current ' + kind + ': ' + names.join(', ') + '. Based on completed visits in the last 30 days.'
                : 'There are no completed visits for current ' + kind + ' in the last 30 days to rank yet.');
            choice('See services', () => ask('Services'));
            choice('Check dates', () => ask('Check dates'));
        } else if (topic === 'Ratings') {
            say('The venue hasn’t published a verified guest rating in its catalog. You can ask staff about guest feedback.');
            link('Contact the venue', '/Home/Contacts');
        } else if (['Services', 'Rates', 'Packages', 'Promos'].includes(topic)) {
            try {
                const response = await fetch('/Home/ChatServices', { cache: 'no-store' });
                if (!response.ok) throw new Error();
                const items = await response.json();
                if (turn !== generation) return;
                services = items;
                const filter = topic === 'Packages' ? /package|bundle|\d+\s*sessions/i : topic === 'Promos' ? /\b(promo\w*|discount|special offer)\b/i : null;
                let matches = items.filter(s => !filter || filter.test([s.title,s.summary,s.rateText,s.details].join(' ')));
                const exact = matches.find(s => question.toLowerCase().includes(s.title.toLowerCase()));
                const kind = question.match(/\b(vip|regular|coaching|junior)\b/i);
                if (kind) matches = matches.filter(s => s.title.toLowerCase().includes(kind[0].toLowerCase()));
                if (exact || matches.length === 1) details(exact || matches[0]);
                else if (matches.length) {
                    say((topic === 'Packages' ? 'Current packages' : topic === 'Promos' ? 'Published promotions' : 'Our services') + '\n' +
                        matches.map(s => '• ' + s.title + (topic === 'Rates' && s.rateText ? ' — ' + s.rateText : '')).join('\n'));
                    matches.forEach(item => choice(item.title, () => ask(item.title)));
                } else {
                    say(topic === 'Promos' ? 'There are no promotions listed in the current service catalog.' : topic === 'Packages' ? 'There are no matching packages listed right now.' : 'There are no matching services listed right now.');
                    link('Browse services', '/Home/Services');
                }
            } catch {
                if (turn === generation) say('The service catalog is unavailable right now. Try again or contact staff.');
            }
        } else {
            say('I can’t confirm that. Would you like to check a venue topic or contact staff?');
            menu();
        }
        if (turn === generation && !options.children.length && slots.hidden && topic !== 'Check dates') {
            choice('Check dates', () => ask('Check dates'));
            choice('Services', () => ask('Services'));
        }
        scroll();
    }
    slots.onsubmit = async event => {
        event.preventDefault();
        const date = slots.elements.date.value;
        await ask(date, displayDate(date));
    };
    async function checkBays(params, turn) {
        try {
            const [response, quote] = await Promise.all([
                fetch('/Reservations/CheckAvailability?' + params, { cache: 'no-store' }),
                fetch('/Reservations/RentalQuote?' + params, { cache: 'no-store' })
                    .then(r => r.ok ? r.json() : null).catch(() => null)
            ]);
            const result = await response.json();
            if (turn !== generation) return;
            if (response.status === 400 && result.message) say(result.message);
            else {
                if (!response.ok) throw new Error();
                const names = result.filter(b => b.available).map(b => b.resource.name);
                const session = params.get('service') + ' · ' + displayDate(params.get('date')) + ' at ' + displayTime(params.get('startTime')) + ' · ' + params.get('durationMinutes') + ' min';
                const rate = Number.isFinite(quote?.rentalTotal) ? new Intl.NumberFormat('en-PH', { style: 'currency', currency: quote.currency, maximumFractionDigits: 2, minimumFractionDigits: 0 }).format(quote.rentalTotal) + ' per bay for this session.' : 'I couldn’t confirm the rate.';
                say((names.length ? 'Available: ' + names.join(', ') : 'No bays available') + '\n' + session + '\n' + rate);
                if (names.length) link('Request a booking', '/Reservations/Create?' + params);
            }
        } catch {
            if (turn !== generation) return;
            say('I couldn’t check live availability. Try again or contact staff.');
        }
        choice('Check another time', () => { lastBooking.startTime = ''; ask('Check dates', 'Check another time'); });
        scroll();
    }
    compose.onsubmit = event => {
        event.preventDefault();
        const text = input.value.trim();
        if (text) { input.value = ''; ask(text); }
    };
    const panel = get('.chat-panel'), launcher = get('summary');
    let expanded = false;
    function setOpen(open, restoreFocus = true) {
        expanded = open;
        if (open) chat.open = true;
        else if (restoreFocus) launcher.focus();
        const finish = () => {
            chat.open = expanded;
            panel.style.removeProperty('transform');
            panel.style.removeProperty('opacity');
        };
        const gsap = window.gsap;
        const moving = gsap?.isTweening(panel);
        gsap?.killTweensOf(panel);
        if (reduced.matches || !gsap) { finish(); return; }
        if (open && !moving) gsap.set(panel, { scale: .9, y: 9, opacity: 0 });
        gsap.to(panel, { scale: open ? 1 : .9, y: open ? 0 : 9, opacity: open ? 1 : 0,
            duration: open ? .25 : .15, ease: open ? 'back.out(1.1)' : 'power2.in', onComplete: finish });
    }
    launcher.onclick = event => { event.preventDefault(); setOpen(!expanded); };
    document.addEventListener('click', event => {
        if (expanded && !event.composedPath().includes(chat)) setOpen(false, false);
    }, true);
    get('[data-close]').onclick = () => setOpen(false);
    chat.addEventListener('keydown', event => { if (event.key === 'Escape' && expanded) setOpen(false); });
    reduced.addEventListener('change', () => {
        if (reduced.matches && window.gsap?.isTweening(panel)) setOpen(expanded);
    });
    compose.hidden = false;
    menu();
})();
