"""Readable companion diagrams. Reuse the existing vector/draw.io exporter."""
from pathlib import Path
import xml.etree.ElementTree as ET
import generate_diagrams as g

g.OUT = Path(__file__).resolve().parent / 'simple'
g.OUT.mkdir(exist_ok=True)
D = g.Diagram


def architecture():
    d=D('01-architecture','How the System Works','Read from top to bottom. POS handles payments; staff make reservation decisions.',1600,1150)
    for id,label,x,y,w,h,kind in [
        ('people','Customers and Staff',550,155,500,75,'external'),
        ('website','Website\nView schedules and manage bookings',550,330,500,90,'process'),
        ('rules','Reservation Services\nCheck bays, review requests and record attendance',550,540,500,100,'process'),
        ('records','Data Access\nSave and retrieve records',550,750,500,85,'process'),
        ('db','MongoDB Atlas\nCustomer and reservation records',550,940,500,85,'external'),
        ('pos','POS System\nBilling and payments',80,540,300,100,'external'),
        ('email','Email / SMS Provider\nMessages and delivery results',1220,540,300,100,'external')]: d.n(id,label,x,y,w,h,kind)
    d.e('people','website'); d.e('website','rules'); d.e('rules','records'); d.e('records','db')
    d.e('rules','pos','Booking details','lr',at=(465,550))
    d.e('pos','rules','Payment status','bb',[(230,690),(800,690)],(430,690))
    d.e('rules','email','Messages','rl',at=(1135,550))
    d.e('email','rules','Delivery results','bb',[(1370,705),(800,705)],(1160,705))
    return d


def booking():
    d=D('02-booking','System Flowchart 1 | Booking and Approval','Start here. After confirmation, continue to Flowchart 2. Use Flowchart 3 for changes.',1600,2230)
    def n(id,label,y,kind='process',h=75): d.n(id,label,565,y,470,h,kind)
    n('start','Start',145,'terminal',55)
    n('browse','Browse services and available times',255)
    n('access','Customer: sign in and verify email\nStaff: sign in with booking permission',395,h=90)
    n('details','Enter customer details, service, time and bays',550,h=85)
    n('available','All bays available?',710,'decision',120)
    d.n('retry','Choose another time or bay',65,735,315,75)
    n('save','Save ONE Pending booking\nand hold ALL requested bays',925,h=90)
    n('shop','Whole-shop booking?',1090,'decision',120)
    d.n('deposit','Verify deposit with POS\nIf unverified: keep Pending for staff follow-up',1150,1100,370,125)
    n('review','Staff reviews the Pending request\nWhole shop: verified deposit required to approve',1350,h=100)
    n('outcome','Review outcome?',1540,'decision',120)
    d.n('confirm','Confirm booking\nSend email confirmation',70,1760,380,95)
    d.n('release','Cancelled or Expired\nRelease all bays and notify customer/staff',1090,1760,440,100)
    d.n('wait','Keep Pending\nContinue staff review',610,1760,380,90)
    d.n('continue','Continue to Flowchart 2',70,1930,380,65,'terminal')
    d.n('timer','ORDINARY ONLINE REQUESTS\nStaff reminder: 15 minutes; escalation: 25 minutes.\nStill Pending at the approved 30-minute deadline: Expired.\nReview hours/outside-hours rules need client agreement.',545,1940,990,165,'note')
    for a,b in [('start','browse'),('browse','access'),('access','details'),('details','available'),('save','shop'),('review','outcome'),('confirm','continue')]: d.e(a,b)
    d.e('available','retry','No','ll',[(475,770)],(455,725))
    d.e('retry','details','','tl',[(222,590)])
    d.e('available','save','Yes',at=(800,875))
    d.e('shop','deposit','Yes','rl',at=(1090,1150))
    d.e('shop','review','No',at=(800,1280))
    d.e('deposit','review','Verified','bl',[(1335,1290),(515,1290),(515,1400)],(1190,1290))
    d.e('outcome','confirm','Approved','bl',[(800,1705),(260,1705)],(260,1705))
    d.e('outcome','release','Cancelled / expired','br',[(800,1710),(1555,1710),(1555,1810)],(1320,1710))
    d.e('outcome','wait','Awaiting decision',at=(800,1710))
    d.e('wait','review','','ll',[(520,1805),(520,1400)])
    return d


def attendance():
    d=D('03-attendance','System Flowchart 2 | Arrival and No-show','Attendance is entered by staff. The system does not sense whether a bay is occupied.',1600,1710)
    def n(id,label,y,kind='process',h=85): d.n(id,label,565,y,470,h,kind)
    n('start','Confirmed booking',160,'terminal',65)
    n('remind','Send reminder about 15 minutes before start',310)
    n('arrived','Arrival recorded?',475,'decision',120)
    d.n('present','Staff records arrival',70,720,350,85)
    d.n('deadline','No arrival recorded:\nFlag at the earlier of start + 1 hour\nor the scheduled end',1080,700,440,120)
    d.n('contact','Staff contacts the customer',1080,930,440,85)
    d.n('missed','Staff marks No-show?',1080,1110,440,120,'decision')
    d.n('release','Record No-show\nRelease remaining allocations',1080,1370,440,95)
    d.n('decision','Staff records attendance decision\nNo automatic extension or renewed hold',560,1110,430,110)
    d.n('complete','Staff records session completion',70,1370,390,85)
    d.n('end','Outcome saved in booking history',70,1540,1450,65,'terminal')
    for a,b in [('start','remind'),('remind','arrived'),('deadline','contact'),('contact','missed'),('present','complete'),('complete','end'),('release','end')]: d.e(a,b)
    d.e('arrived','present','Yes','bl',[(800,650),(245,650)],(245,650))
    d.e('arrived','deadline','No','br',[(800,650),(1560,650),(1560,760)],(1300,650))
    d.e('missed','release','Yes',at=(1300,1300))
    d.e('missed','decision','No','ll',[(1040,1170)],(1030,1120))
    d.e('decision','complete','Arrival / session outcome','bl',[(775,1280),(35,1280),(35,1412)],(535,1280))
    return d


def changes():
    d=D('04-changes','System Flowchart 3 | Change a Booking','Customers request changes; authorized staff approve them.',1500,1520)
    nodes=[('start','Existing reservation',160,'terminal',65),('request','Customer requests a change\nor staff starts an authorized change',315,'process',90),('review','Staff reviews the request',490,'process',75),('approve','Staff approves?',650,'decision',115),('check','Check and save ALL replacement bay allocations',850,'process',90),('success','Change saved?',1030,'decision',115)]
    for id,label,y,kind,h in nodes: d.n(id,label,515,y,470,h,kind)
    d.n('preserve','Keep the original booking and bays\nRecord rejection or failed change',55,1240,500,100)
    d.n('updated','Save changed booking and history\nUpdate reminders and relevant POS details',945,1240,500,100)
    d.n('end','Notify customer of the outcome',515,1400,470,65,'terminal')
    for a,b in [('start','request'),('request','review'),('review','approve'),('check','success')]: d.e(a,b)
    d.e('approve','check','Yes',at=(750,810))
    d.e('approve','preserve','No','ll',[(35,707),(35,1290)],(210,790))
    d.e('success','preserve','No','bl',[(750,1190),(25,1190),(25,1290)],(305,1190))
    d.e('success','updated','Yes','br',[(750,1190),(1470,1190),(1470,1290)],(1195,1190))
    d.e('preserve','end'); d.e('updated','end')
    return d


def overview():
    d=D('05-dfd-reading-map','DFD Reading Map | Six Areas of the System','Overview only. Open the numbered Level 2 pages to see the actual data flows.',1600,1250)
    labels=['1.0 Accounts and Access','2.0 Reservations','3.0 Notifications','4.0 Reports','5.0 POS and Deposits','6.0 Basic Inquiries']
    descriptions=['Sign up, verify email and sign in','Check bays, save bookings and manage changes','Send confirmations, reminders and staff alerts','Search records and show booking reports','Exchange POS status and verify deposits','Answer common questions from approved FAQs']
    for i,(label,desc) in enumerate(zip(labels,descriptions)):
        row,col=divmod(i,2); d.n('area'+str(i),label+'\n'+desc,70+col*770,180+row*310,690,180)
    return d


def dfd(num,title,steps,chain,inputs,outputs):
    h=400+len(steps)*340
    d=D(f'06-dfd-level2-{num}',f'Level 2 DFD | {num}.0 {title}','Read the arrow labels as information being passed. Repeated store IDs mean the same records.',1600,h)
    for i,(id,label) in enumerate(steps): d.n(id,id+'\n'+label,610,250+i*340,380,100)
    for a,b,label in chain: d.e(a,b,label,at=(800,(d.nodes[a].y+d.nodes[b].y+100)/2))
    def endpoint(id,label,kind,x,y):
        if kind=='store' and not label.startswith('D'): label=id.split('-')[-1]+'\n'+label
        d.n(id,label,x,y,335,80,kind)
    for j,(target,label,kind,text) in enumerate(inputs):
        id=f'in-{j}'; matches=[i for i,t in enumerate(inputs) if t[0]==target]; offset=matches.index(j)-(len(matches)-1)/2
        y=d.nodes[target].y+10+offset*110
        endpoint(id,label,kind,45,y)
        lane=420+matches.index(j)*30
        d.e(id,target,text,'rl',[(lane,y+40),(lane,d.nodes[target].y+50)],at=(495,y+40))
    for j,(source,id,label,kind,text) in enumerate(outputs):
        matches=[i for i,t in enumerate(outputs) if t[0]==source]; offset=matches.index(j)-(len(matches)-1)/2
        y=d.nodes[source].y+10+offset*110; key=f'out-{j}-{id}'
        endpoint(key,label,kind,1220,y)
        lane=1030+matches.index(j)*30
        d.e(source,key,text,'rl',[(lane,d.nodes[source].y+50),(lane,y+40)],at=(1110,y+40))
    return d


def level2():
    specs=[
      ('1','Accounts and Access',
       [('1.1','Register customer'),('1.2','Verify email'),('1.3','Check sign-in details'),('1.4','Check role and record ownership')],
       [('1.1','1.2','Verification request'),('1.3','1.4','Signed-in identity')],
       [('1.1','Customer','external','Account details'),('1.2','Customer','external','Verification code'),('1.3','Customer / Staff','external','Sign-in details'),('1.4','D1\nAccounts','store','Roles and account links'),('1.3','D1\nAccounts','store','Saved sign-in data')],
       [('1.1','D1','Accounts','store','New account'),('1.2','D1','Accounts','store','Verified email'),('1.1','M','Email Provider','external','Verification email'),('1.3','C','Customer / Staff','external','Sign-in result'),('1.4','P2','2.2 Booking details','process','Booking permission'),('1.4','P4','4.1 Report request','process','Staff permission')]),
      ('2','Reservations',
       [('2.1','Check available bays and times'),('2.2','Collect booking details'),('2.3','Check and save all bay allocations'),('2.4','Review booking and record outcomes'),('2.5','Process booking changes')],
       [('2.2','2.3','Booking request'),('2.3','2.4','Saved Pending booking')],
       [('2.1','Customer / Staff','external','Availability query'),('2.1','D3\nBookings','store','Existing bookings'),('2.1','D4\nResource schedules','store','Hours and blocks'),('2.2','Customer / Staff','external','Customer and booking details'),('2.2','1.4 Access check','process','Access permission'),('2.3','D3\nBookings','store','Existing allocations'),('2.3','D4\nResource schedules','store','Hours and blocks'),('2.4','Staff','external','Review / arrival decision'),('2.4','5.4 POS result','process','Deposit result'),('2.4','D3\nBookings','store','Booking state / deadline'),('2.5','Customer / Staff','external','Change request / decision'),('2.5','D3\nBookings','store','Original booking'),('2.5','2.3 Allocation check','process','Saved / failed change')],
       [('2.1','C','Customer / Staff','external','Available slots'),('2.2','D2','Customer records','store','Customer details'),('2.3','D3','Bookings and bay allocations','store','One saved booking; all bays'),('2.3','C','Customer / Staff','external','Reference / conflict result'),('2.4','D3','Bookings and outcomes','store','Status / released bays'),('2.4','P3','3.1 Prepare messages','process','Booking / expiry event'),('2.4','P5','5.1 POS exchange','process','POS exchange request'),('2.5','D7','Booking change history','store','Change outcome'),('2.5','P2','2.3 Allocation check','process','Approved change details'),('2.5','P3','3.1 Prepare messages','process','Change notice')]),
      ('3','Notifications',
       [('3.1','Prepare due messages'),('3.2','Send messages'),('3.3','Save delivery result')],
       [('3.1','3.2','Message and recipient')],
       [('3.1','D2\nCustomer contacts','store','Recipient details'),('3.1','D3\nBookings','store','Schedule and due alerts'),('3.1','2.0 Reservations','process','Confirmed / expired / changed'),('3.2','D8\nMessage delivery','store','Duplicate-message check'),('3.3','Email / SMS Provider','external','Delivery result')],
       [('3.1','D8','Message delivery records','store','Prepared message'),('3.2','M','Email / SMS Provider','external','Confirmation / reminder / alert'),('3.3','D8','Message delivery records','store','Sent / failed result')]),
      ('4','Reports',
       [('4.1','Check staff report request'),('4.2','Calculate report results'),('4.3','Show records and reports')],
       [('4.1','4.2','Approved filters'),('4.2','4.3','Report results')],
       [('4.1','Authorized Staff','external','Search / report filters'),('4.1','1.4 Access check','process','Staff permission'),('4.2','D2\nCustomers','store','Customer booking links'),('4.2','D3\nBookings','store','Booking data'),('4.2','D4\nResource schedules','store','Available-time data')],
       [('4.1','S','Authorized Staff','external','Access result'),('4.3','S','Authorized Staff','external','Records / booking reports')]),
      ('5','POS and Whole-Shop Deposits',
       [('5.1','Send booking details to POS'),('5.2','Check returned POS status'),('5.3','Review deposit evidence'),('5.4','Save transaction and verification result')],
       [('5.2','5.3','Valid status / failed exchange'),('5.3','5.4','Verified / unverified result')],
       [('5.1','2.4 Reservation review','process','Booking reference / details'),('5.2','POS System','external','Transaction and payment status'),('5.3','Authorized Staff','external','Verification evidence / decision'),('5.4','D5\nPOS records','store','Duplicate-result check')],
       [('5.1','P','POS System','external','Reservation details'),('5.2','D5','POS reference records','store','Valid / failed exchange'),('5.3','S','Authorized Staff','external','Evidence for review'),('5.4','P2','2.4 Reservation review','process','POS / deposit result'),('5.4','D5','POS reference records','store','Transaction / verification result')]),
      ('6','Basic Inquiries',
       [('6.1','Find a predefined answer'),('6.2','Show answer or refer to staff')],
       [('6.1','6.2','Answer / no match')],
       [('6.1','Customer / Visitor','external','Question'),('6.1','D6\nApproved FAQ content','store','Predefined answers')],
       [('6.2','C','Customer / Visitor','external','Answer / staff referral')]),
    ]
    return [dfd(*s) for s in specs]


def integration():
    d=D('07-pos-integration','Integration Flow | Booking and POS','Reservation manages bookings. POS handles money. A deposit result does not confirm a booking by itself.',1600,1890)
    def n(id,label,y,kind='process',h=85): d.n(id,label,565,y,470,h,kind)
    n('start','Booking needs a POS exchange',160,'terminal',65)
    n('send','Send booking reference and service details to POS',315)
    n('pos','POS handles billing / deposit processing',480)
    n('result','Valid POS response?',650,'decision',120)
    d.n('fail','No valid response\nKeep deposit unverified; record failure',60,855,370,100)
    n('duplicate','Result already recorded?',880,'decision',120)
    d.n('existing','Keep the recorded result\nDo not apply it twice',1150,1090,370,90)
    n('verified','Deposit evidence accepted?',1100,'decision',120)
    d.n('unverified','Record status; deposit stays unverified\nStaff follows up under agreed policy',55,1320,430,110)
    n('save','Save verified deposit and transaction reference',1330)
    n('review','Return result to staff booking review',1510)
    n('end','Staff may confirm only when\nall approval requirements are met',1685,'terminal',90)
    for a,b in [('start','send'),('send','pos'),('pos','result'),('save','review'),('review','end')]: d.e(a,b)
    d.e('result','duplicate','Yes',at=(800,820))
    d.e('result','fail','No','ll',[(35,710),(35,905)],(255,790))
    d.e('duplicate','existing','Yes','rr',[(1550,940),(1550,1135)],(1310,1020))
    d.e('duplicate','verified','No',at=(800,1050))
    d.e('verified','save','Yes',at=(800,1270))
    d.e('verified','unverified','No','ll',[(30,1160),(30,1375)],(270,1260))
    d.e('unverified','review','Unverified result','br',[(270,1630),(1080,1630),(1080,1552)],(1250,1550))
    d.e('fail','review','Failure result','bl',[(245,1460),(525,1460),(525,1552)],(250,1460))
    d.e('existing','review','Existing result','bl',[(1335,1460),(530,1460),(530,1552)],(1340,1460))
    return d


def check(diagrams):
    ids=set()
    for d in diagrams:
        for n in d.nodes.values():
            assert 0<=n.x and n.x+n.w<=d.w and 120<=n.y and n.y+n.h<d.h-35,(d.slug,n.id)
        if 'level2' in d.slug:
            for id in d.nodes:
                if id[0].isdigit() and '.' in id:
                    ids.add(id)
                    assert any(e[0]==id for e in d.edges),(id,'missing output')
                    assert any(e[1]==id for e in d.edges),(id,'missing input')
    assert len(ids)==21,ids
    print('PASS: 21 numbered Level 2 processes with inputs/outputs; all nodes within page bounds.')


if __name__=='__main__':
    diagrams=[architecture(),booking(),attendance(),changes(),overview(),*level2(),integration()]
    check(diagrams); g.export(diagrams)
    workbook=ET.parse(g.OUT/'reservation-diagrams.drawio').getroot()
    for page in workbook.findall('diagram'):
        root=ET.Element('mxfile',host='app.diagrams.net'); root.append(page)
        ET.ElementTree(root).write(g.OUT/(page.get('id')+'.drawio'),encoding='utf-8',xml_declaration=True)
    print('Created simple workbook, individual draw.io files, SVGs and PDF.')
