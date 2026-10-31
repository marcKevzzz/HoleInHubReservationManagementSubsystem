"""Generate the aligned academic diagrams: editable draw.io, SVG, and vector PDF.

Run with the bundled Python runtime (reportlab). No application dependencies change.
"""
from pathlib import Path
from dataclasses import dataclass
import math
import textwrap
import xml.etree.ElementTree as ET

from reportlab.pdfgen import canvas
from reportlab.lib.colors import HexColor
from reportlab.pdfbase.pdfmetrics import stringWidth

OUT = Path(__file__).resolve().parent
INK = '#172b35'
COLORS = {'process': '#edf6f3', 'decision': '#fff5df', 'external': '#edf2fa',
          'store': '#ffffff', 'note': '#fff8e9', 'terminal': '#dcece6', 'box': '#f8fafb'}


@dataclass
class Node:
    id: str
    label: str
    x: float
    y: float
    w: float = 280
    h: float = 90
    kind: str = 'process'

    def port(self, side):
        return {'l': (self.x, self.y+self.h/2), 'r': (self.x+self.w, self.y+self.h/2),
                't': (self.x+self.w/2, self.y), 'b': (self.x+self.w/2, self.y+self.h)}[side]


class Diagram:
    def __init__(self, slug, title, subtitle, w, h):
        self.slug, self.title, self.subtitle, self.w, self.h = slug, title, subtitle, w, h
        self.nodes, self.edges, self.bands = {}, [], []

    def n(self, id, label, x, y, w=280, h=90, kind='process'):
        assert id not in self.nodes, id
        self.nodes[id] = Node(id, label, x, y, w, h, kind)
        return id

    def e(self, a, b, label='', sides='bt', via=(), at=None, color=INK, dashed=False):
        assert a in self.nodes and b in self.nodes, (a, b)
        points = [self.nodes[a].port(sides[0]), *via, self.nodes[b].port(sides[1])]
        if at is None:
            at = ((points[0][0]+points[-1][0])/2, (points[0][1]+points[-1][1])/2)
        self.edges.append((a, b, label, points, at, color, dashed, sides))

    def band(self, label, x, y, w, h):
        self.bands.append((label, x, y, w, h))


def wrapped(label, width, font=19):
    result=[]
    for part in label.split('\n'):
        words=part.split(); line=''
        for word in words:
            candidate=(line+' '+word).strip()
            if line and stringWidth(candidate, 'Helvetica', font)>width:
                result.append(line); line=word
            else:
                line=candidate
        result.append(line)
    return result


def export(diagrams):
    mx=ET.Element('mxfile', host='app.diagrams.net', version='24.7.17')
    pdf=canvas.Canvas(str(OUT/'reservation-diagrams.pdf'))
    pdf.setTitle('Hole In Hub | Aligned System Diagrams')
    for d in diagrams:
        svg=ET.Element('svg', xmlns='http://www.w3.org/2000/svg', width=str(d.w), height=str(d.h), viewBox=f'0 0 {d.w} {d.h}')
        ET.SubElement(svg,'title').text=d.title
        ET.SubElement(svg,'desc').text=d.subtitle
        ET.SubElement(svg,'rect',width=str(d.w),height=str(d.h),fill='white')
        page=ET.SubElement(mx,'diagram',id=d.slug,name=d.title)
        model=ET.SubElement(page,'mxGraphModel',grid='1',gridSize='10',page='1',pageWidth=str(d.w),pageHeight=str(d.h))
        root=ET.SubElement(model,'root'); ET.SubElement(root,'mxCell',id='0'); ET.SubElement(root,'mxCell',id='1',parent='0')
        pdf.setPageSize((d.w*.5,d.h*.5)); pdf.saveState(); pdf.scale(.5,.5)

        def poly(points, fill=None, stroke=INK, dash=False, arrow=False):
            ET.SubElement(svg,'polyline',points=' '.join(f'{x},{y}' for x,y in points),fill=fill or 'none',stroke=stroke,**{'stroke-width':'2','stroke-dasharray':'8 5' if dash else 'none'})
            pdf.setStrokeColor(HexColor(stroke)); pdf.setLineWidth(2); pdf.setDash([8,5] if dash else [])
            p=pdf.beginPath(); p.moveTo(points[0][0],d.h-points[0][1])
            for x,y in points[1:]: p.lineTo(x,d.h-y)
            if fill: pdf.setFillColor(HexColor(fill))
            pdf.drawPath(p,stroke=1,fill=bool(fill))
            if arrow:
                x,y=points[-1]; prev=next(p for p in reversed(points[:-1]) if p != (x,y))
                a=math.atan2(y-prev[1],x-prev[0]); pts=[(x,y),(x-13*math.cos(a-.43),y-13*math.sin(a-.43)),(x-13*math.cos(a+.43),y-13*math.sin(a+.43)),(x,y)]
                poly(pts,stroke,stroke)

        def rect(x,y,w,h,fill,stroke=INK,rounding=0):
            ET.SubElement(svg,'rect',x=str(x),y=str(y),width=str(w),height=str(h),rx=str(rounding),fill=fill,stroke=stroke,**{'stroke-width':'2'})
            pdf.setFillColor(HexColor(fill)); pdf.setStrokeColor(HexColor(stroke)); pdf.setLineWidth(2); pdf.setDash([])
            pdf.roundRect(x,d.h-y-h,w,h,rounding,stroke=1,fill=1)

        def txt(label,cx,cy,width,font=19,bg=False,bold=False):
            lines=wrapped(label,width,font); leading=font*1.25
            if bg and label:
                tw=max(stringWidth(line,'Helvetica',font) for line in lines)+16
                rect(cx-tw/2,cy-len(lines)*leading/2-3,tw,len(lines)*leading+6,'#ffffff','#ffffff')
            for i,line in enumerate(lines):
                yy=cy+(i-(len(lines)-1)/2)*leading
                el=ET.SubElement(svg,'text',x=str(cx),y=str(yy),fill=INK,**{'font-family':'Arial, sans-serif','font-size':str(font),'text-anchor':'middle','dominant-baseline':'middle','font-weight':'bold' if bold else 'normal'})
                el.text=line
                pdf.setFillColor(HexColor(INK)); pdf.setFont('Helvetica-Bold' if bold else 'Helvetica',font)
                pdf.drawCentredString(cx,d.h-yy-font*.34,line)

        def vertex(id,label,x,y,w,h,style):
            cell=ET.SubElement(root,'mxCell',id=id,value=label,style=style,vertex='1',parent='1')
            ET.SubElement(cell,'mxGeometry',x=str(x),y=str(y),width=str(w),height=str(h),**{'as':'geometry'})

        txt(d.title,d.w/2,43,d.w-100,30,bold=True)
        txt(d.subtitle,d.w/2,87,d.w-100,17)
        vertex('title',d.title,35,12,d.w-70,50,'text;html=0;fontSize=30;fontStyle=1;align=center;')
        vertex('subtitle',d.subtitle,35,64,d.w-70,48,'text;html=0;fontSize=17;align=center;whiteSpace=wrap;')
        for i,(label,x,y,w,h) in enumerate(d.bands):
            rect(x,y,w,h,'#fbfcfd','#ced9dd',12); txt(label,x+w/2,y+25,w-40,21,bold=True)
            vertex(f'band{i}',label,x,y,w,h,'rounded=1;fillColor=#fbfcfd;strokeColor=#ced9dd;verticalAlign=top;spacingTop=12;fontSize=21;fontStyle=1;')
        for a,b,label,pts,at,color,dashed,sides in d.edges:
            poly(pts,stroke=color,dash=dashed,arrow=True)
        for n in d.nodes.values():
            fill=COLORS[n.kind]
            if n.kind=='decision':
                poly([(n.x+n.w/2,n.y),(n.x+n.w,n.y+n.h/2),(n.x+n.w/2,n.y+n.h),(n.x,n.y+n.h/2),(n.x+n.w/2,n.y)],fill)
                shape='rhombus;'
            elif n.kind=='store':
                rect(n.x,n.y,n.w,n.h,fill)
                poly([(n.x+48,n.y),(n.x+48,n.y+n.h)])
                txt(n.label.split('\n',1)[0],n.x+24,n.y+n.h/2,42,17,bold=True)
                txt(n.label.split('\n',1)[1],n.x+48+(n.w-48)/2,n.y+n.h/2,n.w-64,18)
                shape='shape=partialRectangle;right=0;'
            else:
                rect(n.x,n.y,n.w,n.h,fill,rounding=22 if n.kind=='terminal' else 8 if n.kind=='process' else 0)
                shape='rounded=1;arcSize=30;' if n.kind in ('process','terminal') else ''
            if n.kind!='store':
                textwidth=n.w*.65 if n.kind=='decision' else n.w-24
                font=18 if n.kind=='note' else 19
                while len(wrapped(n.label,textwidth,font))*font*1.25>n.h-10 and font>14: font-=1
                txt(n.label,n.x+n.w/2,n.y+n.h/2,textwidth,font,bold=n.kind=='terminal')
            vertex(n.id,n.label,n.x,n.y,n.w,n.h,shape+f'whiteSpace=wrap;html=0;fillColor={fill};strokeColor={INK};fontColor={INK};fontSize=19;spacing=10;')
        for i,(a,b,label,pts,at,color,dashed,sides) in enumerate(d.edges):
            if label:
                labelwidth=170 if len(pts)==2 and pts[0][1]==pts[-1][1] else 240
                txt(label,*at,labelwidth,14 if 'dfd' in d.slug else 16,bg=True)
            sidevalues={'l':('0','.5'),'r':('1','.5'),'t':('.5','0'),'b':('.5','1')}
            ex,ey=sidevalues[sides[0]]; ix,iy=sidevalues[sides[1]]
            cell=ET.SubElement(root,'mxCell',id=f'e{i}',value='',edge='1',source=a,target=b,parent='1',style=f'endArrow=block;html=0;rounded=0;strokeColor={color};strokeWidth=2;dashed={int(dashed)};exitX={ex};exitY={ey};entryX={ix};entryY={iy};')
            geo=ET.SubElement(cell,'mxGeometry',relative='1',**{'as':'geometry'})
            if len(pts)>2:
                arr=ET.SubElement(geo,'Array',**{'as':'points'})
                for x,y in pts[1:-1]: ET.SubElement(arr,'mxPoint',x=str(x),y=str(y))
            if label: vertex(f'el{i}',label,at[0]-135,at[1]-27,270,54,'text;html=0;whiteSpace=wrap;align=center;verticalAlign=middle;fillColor=#ffffff;fontSize=16;')
        txt('Hole In Hub | Revised Chapter I alignment | 27 October 2026 | Proposed design; client policy items remain open',d.w/2,d.h-24,d.w-50,15)
        ET.indent(svg); ET.ElementTree(svg).write(OUT/f'{d.slug}.svg',encoding='utf-8',xml_declaration=True)
        pdf.restoreState(); pdf.showPage()
    pdf.save(); ET.indent(mx); ET.ElementTree(mx).write(OUT/'reservation-diagrams.drawio',encoding='utf-8',xml_declaration=True)


def architecture():
    d=Diagram('01-architecture','System Architecture Diagram','Target architecture | solid arrows: calls/data exchange; dashed arrow: explicit operational command',1800,1270)
    d.band('Customer and authorized staff devices',40,140,1720,160)
    d.n('customer','Customer browser\nBrowse; verified-email submission; own records',110,195,470,80,'external')
    d.n('staff','Staff browser\nFront desk; supervisor; administrator',690,195,470,80,'external')
    d.n('web','HoleInHub.Web | Presentation\nMVC controllers, Razor views, HTTP validation,\nauthentication/authorization, local Tailwind and GSAP',390,380,740,110)
    d.n('business','HoleInHub.Business | Application policy\nBooking/allocation; review/expiry; staff attendance;\nchanges; reports; notification/POS coordination',390,580,740,120)
    d.n('data','HoleInHub.Data | Persistence\nMongoDB context, validated documents, indexes,\ntransactions, resource/date guards and history',390,800,740,110)
    d.n('db','MongoDB Atlas\nCustomers, accounts, resources, reservations,\nallocations, history, delivery and POS references',390,1000,740,110,'store')
    # The database is an architectural store, not a DFD identifier.
    d.nodes['db'].kind='external'
    d.n('pos','External POS Subsystem\nBilling, deposits, vouchers,\npayments and settlement',1300,580,400,120,'external')
    d.n('mail','Messaging Provider\nVerification/confirmation email;\nreminders and delivery results',1300,800,400,110,'external')
    d.n('cli','HoleInHub.Migrator\nExplicit CLI; exclusive lease\nNever on Web startup',70,1000,260,110)
    d.n('note','POLICY TO AGREE\nReview coverage/outside-hours handling;\nwhole-shop deposit terms/deadline; POS contract.\nNo simulator occupancy sensing.',1290,1000,430,145,'note')
    d.e('customer','web','HTTPS','bt',[(345,335),(760,335)],(550,335))
    d.e('staff','web','HTTPS','bt',[(925,345),(760,345)],(995,342))
    d.e('web','business','Application services',at=(760,535))
    d.e('business','data','Narrow persistence operations',at=(760,755))
    d.e('data','db','MongoDB driver / transactions',at=(760,955))
    d.e('business','pos','Reservation / service / allocation details','rl',at=(1215,600))
    d.e('pos','business','Transaction references; payment/deposit status','tb',[(1500,530),(1190,530),(1190,720),(760,720)],(1300,530))
    d.e('business','mail','Outbound messages','rb',[(1210,640),(1210,950),(1500,950)],(1210,860))
    d.e('mail','business','Delivery results','tl',[(1500,750),(1245,750),(1245,560),(350,560),(350,640)],(1225,750))
    d.e('cli','db','CLI migrations','rl',at=(210,965),dashed=True)
    return d


def system_flow():
    d=Diagram('02-system-flowchart','System Flowchart | Booking through Final Outcome','One connected workflow | numbered references map to the Level 2 DFD | diamonds are decisions',2450,3350)
    d.band('A. Access and atomic booking',35,130,740,3060)
    d.band('B. Pending review and confirmation',820,130,790,3060)
    d.band('C. Attendance, changes and operational outputs',1655,130,760,3060)
    def n(id,label,col,y,kind='process',h=90): return d.n(id,label,[210,1040,1875][col],y,360,h,kind)
    n('start','Start',0,195,'terminal',55)
    n('browse','Browse services / availability\nOptional predefined FAQ [2.1 / 6.1-6.2]',0,290)
    n('channel','Staff-assisted booking?',0,445,'decision',115)
    d.n('staffauth','Authenticate staff and check role [1.3-1.4]\nCapture customer without requiring an account',65,620,280,120)
    d.n('custauth','Sign in / register and verify email\nEnforce verified account [1.1-1.4]',455,620,280,120)
    n('capture','Capture organizer/contact, attendees, service,\ntime and requested resources [2.2]',0,835,h=110)
    n('duplicate','Submission key already processed?',0,1020,'decision',110)
    d.n('existing','Return original result/reference\nNo duplicate reservation',55,1185,280,95)
    n('atomic','Validate and atomically create Pending\nand ALL allocations; guards, hours and conflicts [2.3]',0,1390,h=110)
    n('success','Allocation committed?',0,1570,'decision',110)
    d.n('conflict','Return validation/conflict feedback\nNo partial reservation or allocation',55,1740,280,100)
    n('pending','Display committed Pending reference\nand held-resource summary [2.3]',0,1950,h=110)
    n('whole','Whole-shop reservation?',1,195,'decision',110)
    d.n('deposit','Exchange deposit status with POS [5.1-5.4]\nVerify agreed evidence; failures stay unverified\nApply separate agreed deposit deadline',880,380,290,145)
    d.n('ordinary','Ordinary ONLINE bay request?\nApply approved review coverage; alerts at 15/25 min\n30-min deadline; outside-hours rule to agree',1270,380,290,145)
    n('review','Staff review current Pending state [2.4]\nWhole shop: verified deposit required to confirm\nStaff-assisted: no assumed online expiry',1,660,h=130)
    n('decision','Current review outcome?',1,900,'decision',130)
    d.n('expiry','Eligible online request reaches deadline\nAtomically mark Expired; release ALL allocations\nNotify customer and staff [2.4 / 3.1-3.3]',865,1170,320,135)
    d.n('cancel','Staff cancels\nRecord Cancelled; release remaining allocations\nSend notice [2.4 / 3.1-3.3]',1240,1170,320,135)
    n('confirm','Recheck Pending state and required deposit\nStaff confirms atomically [2.4]\nExpiry race/stale state: reload review',1,1490,h=125)
    n('confirmed','Confirmed\nSend approval email; schedule reminder\nabout 15 min before start [3.1-3.3]',1,1760,h=120)
    n('terminal','Expired / Cancelled\nRetain history',1,2110,'terminal',80)
    n('arrival','Confirmed booking\nStaff records arrival? [2.4]',2,195,'decision',120)
    d.n('checkin','Record arrival / attendance\nNo physical occupancy sensing',1700,420,285,100)
    d.n('flag','No arrival: flag at earlier of\n(start + 1 hour) or scheduled end\nRetain allocations up to that deadline',2060,420,285,120)
    n('follow','Staff follows up with customer\nDetermine attendance outcome [2.4]',2,690,h=100)
    n('noshow','Staff marks No-show?',2,880,'decision',115)
    d.n('missed','Record No-show and release\nremaining allocations; retain history',2060,1090,285,100)
    d.n('keep','Record staff decision / arrival\nNo automatic extension or renewed hold\nExceptions require approved policy',1700,1090,285,120)
    n('session','Staff manages session and records\ncompletion under agreed criteria [2.4]\nPayment alone never completes a booking',2,1450,h=120)
    n('end','Reservation outcome recorded',2,1700,'terminal',65)
    n('change','Customer own-record change request\nor authorized staff change [2.5]',2,1980,h=100)
    n('changeok','Staff approves and replacement\nallocations commit together?',2,2160,'decision',130)
    d.n('preserve','Rejected / failed change\nPreserve original reservation and allocations',1700,2420,285,110)
    d.n('replace','Successful change\nCommit new allocations and history\nRevise notices/reminders and POS details',2060,2420,285,120)
    n('outputs','Authorized staff reports [4.1-4.3]\nCounts, services, trends, cancellations/no-shows\nand RESERVED-TIME utilization',0,2680,h=130)
    n('policies','CLIENT POLICY REQUIRED\nReview hours and outside-hours clock;\nwhole-shop deposit deadline/evidence;\nchange/attendance exceptions and completion rules.',1,2670,'note',160)
    n('changenote','Change path is available for an existing\nreservation under approved permissions.\nSuccessful edits preserve lifecycle rules;\nchanged terms may require staff reconfirmation.',2,2700,'note',150)
    d.e('start','browse'); d.e('browse','channel')
    d.e('channel','staffauth','Yes','bl',[(390,585),(45,585),(45,680)],(110,585))
    d.e('channel','custauth','No','br',[(390,585),(755,585),(755,680)],(675,585))
    d.e('staffauth','capture','Authorized','bt',[(205,790),(390,790)],(235,790))
    d.e('custauth','capture','Verified account','bt',[(595,800),(390,800)],(615,800))
    d.e('capture','duplicate'); d.e('duplicate','existing','Yes','bl',[(390,1155),(40,1155),(40,1232)],(105,1155))
    d.e('duplicate','atomic','No','bt',[(390,1330)],(390,1320))
    d.e('atomic','success'); d.e('success','conflict','No','bl',[(390,1715),(40,1715),(40,1790)],(100,1715))
    d.e('conflict','capture','Choose alternatives','lt',[(45,1790),(45,800),(390,800)],(115,1420))
    d.e('success','pending','Yes','bt',[(390,1880)],(390,1880))
    d.e('pending','whole','Saved Pending record','rt',[(795,2005),(795,165),(1220,165)],(795,1460))
    d.e('whole','deposit','Yes','bl',[(1220,340),(1025,340)],(1040,340))
    d.e('whole','ordinary','No','br',[(1220,340),(1415,340),(1580,340),(1580,452)],(1450,340))
    d.e('deposit','review','Verification outcome','bt',[(1025,590),(1220,590)],(1025,585))
    d.e('ordinary','review','Review policy / deadline','bt',[(1415,605),(1220,605)],(1430,600))
    d.e('review','decision')
    d.e('decision','expiry','Eligible expiry','bl',[(1220,1085),(1025,1085),(850,1085),(850,1237)],(1010,1085))
    d.e('decision','cancel','Cancel','br',[(1220,1100),(1400,1100),(1580,1100),(1580,1237)],(1430,1100))
    d.e('decision','review','Still awaiting action / deposit','rl',[(1595,965),(1595,830),(1000,830),(1000,725)],(1430,830))
    d.e('decision','confirm','Approve + deposit gate satisfied','bt',[(1220,1380)],(1220,1380))
    d.e('expiry','terminal','Committed expiry','bt',[(1025,2040),(1220,2040)],(1030,1950))
    d.e('cancel','terminal','Committed cancellation','bt',[(1400,2055),(1220,2055)],(1420,1950))
    d.e('confirm','confirmed','Confirmation committed',at=(1220,1680))
    d.e('confirmed','arrival','Confirmed reservation','rt',[(1630,1820),(1630,165),(2055,165)],(1630,1470))
    d.e('arrival','checkin','Yes','bl',[(2055,365),(1842,365)],(1850,365))
    d.e('arrival','flag','No','br',[(2055,365),(2202,365),(2380,365),(2380,480)],(2210,365))
    d.e('flag','follow','Deadline reached','bt',[(2202,620),(2055,620)],(2220,620))
    d.e('follow','noshow'); d.e('noshow','missed','Yes','br',[(2055,1040),(2380,1040),(2380,1140)],(2240,1040))
    d.e('noshow','keep','No','bl',[(2055,1040),(1680,1040),(1680,1150)],(1810,1040))
    d.e('checkin','session','','bl',[(1680,470),(1680,1510)])
    d.e('keep','session','','bt',[(1842,1340),(2055,1340)])
    d.e('session','end'); d.e('missed','end','','br',[(2380,1140),(2380,1732)])
    d.e('confirmed','change','Existing reservation / permitted change','br',[(1220,1920),(2390,1920),(2390,2030)],(2000,1920),dashed=True)
    d.e('change','changeok'); d.e('changeok','preserve','No','bl',[(2055,2350),(1680,2350),(1680,2475)],(1820,2350))
    d.e('changeok','replace','Yes','br',[(2055,2350),(2380,2350),(2380,2480)],(2230,2350))
    d.e('preserve','changenote'); d.e('replace','changenote')
    return d


def context():
    d=Diagram('03-dfd-context','DFD Context Diagram','System boundary | arrows carry data, not decisions or execution order',1800,1150)
    d.n('r','0\nReservation Management Subsystem',650,450,500,180)
    d.n('c','Customer / Visitor',70,180,350,90,'external'); d.n('s','Authorized Staff',70,800,350,90,'external')
    d.n('p','POS Subsystem',1380,180,350,90,'external'); d.n('m','Messaging Provider',1380,800,350,90,'external')
    d.e('c','r','Registration / verification / sign-in details; availability and booking/change requests; FAQ questions','rb',[(500,225),(500,680),(900,680)],(500,390))
    d.e('r','c','Access results; slots; own records; booking/change outcomes; notifications; FAQ answers','tl',[(900,140),(40,140),(40,225)],(900,140))
    d.e('s','r','Credentials; assisted bookings; review/attendance/change decisions; report filters','rt',[(510,845),(510,400),(900,400)],(510,750))
    d.e('r','s','Access results; schedules; review alerts; verification/booking outcomes; reports','bl',[(900,970),(40,970),(40,845)],(900,970))
    d.e('r','p','Reservation reference; relevant customer/service/time/allocation details','rt',[(1240,540),(1240,150),(1555,150)],(1240,330))
    d.e('p','r','Transaction references; payment/deposit statuses; exchange results','bl',[(1555,350),(600,350),(600,540)],(1090,350))
    d.e('r','m','Verification and reservation messages; recipient details','br',[(900,720),(1760,720),(1760,845)],(1280,720))
    d.e('m','r','Delivery results','lt',[(1290,845),(1290,380),(900,380)],(1290,820))
    return d


# Stable identifiers match the submitted draft's six parent processes.
SPECS = [
 ('1','Manage Accounts and Access',[
  ('1.1','Register Customer Account'),('1.2','Verify Customer Email'),('1.3','Authenticate Customer / Staff'),('1.4','Authorize Role and Ownership')],
  [('C','Customer / Staff','external'),('D1','Accounts and Roles','store'),('M','Messaging Provider','external')],
  [('C','1.1','registration details'),('1.1','D1','new unverified account'),('1.1','1.2','verification details'),('C','1.2','verification token'),('1.2','D1','verified-email state'),('1.2','M','verification email'),('M','1.2','delivery result'),('C','1.3','sign-in credentials'),('D1','1.3','account authentication data'),('1.3','1.4','authenticated identity'),('D1','1.4','roles / ownership link'),('1.4','C','access result')]),
 ('2','Manage Reservations',[
  ('2.1','Query Service / Resource Availability'),('2.2','Capture Booking and Check Access'),('2.3','Validate and Commit All Allocations'),('2.4','Review / Expire / Record Attendance'),('2.5','Process Staff-Reviewed Changes')],
  [('C','Customer / Staff','external'),('D2','Customer Records','store'),('D3','Reservations / Allocations','store'),('D4','Services / Schedules / Blocks','store'),('D7','History / Change Requests','store')],
  [('C','2.1','availability query'),('D3','2.1','existing allocations'),('D4','2.1','services / resource schedules'),('2.1','C','available slots / services'),('C','2.2','booking / contact / attendee details'),('2.2','D2','customer record / account link'),('2.2','2.3','authorized booking + submission key'),('D3','2.3','allocations / prior submission result'),('D4','2.3','hours / closures / resource eligibility'),('2.3','D3','atomic Pending record + ALL allocations'),('2.3','C','reference / prior result / conflict'),('D3','2.4','Pending state / deadlines / attendance'),('C','2.4','staff approval / cancellation / attendance'),('2.4','D3','confirmed / expired / attendance outcome; releases'),('2.4','D7','actor / time / outcome history'),('2.3','D7','creation / allocation history'),('2.4','C','review / attendance / reservation outcome'),('C','2.5','customer change request / staff decision'),('D3','2.5','original reservation / allocations'),('2.5','2.3','approved proposed allocations + original version'),('2.3','2.5','commit result; original retained on failure'),('2.5','D7','change request / decision history'),('2.5','C','change outcome')]),
 ('3','Send Notifications',[
  ('3.1','Prepare Due / Committed Messages'),('3.2','Deliver Reservation Notifications'),('3.3','Record Delivery Outcomes')],
  [('C','Customer / Staff','external'),('D2','Customer Contacts','store'),('D3','Reservations / Due Alerts','store'),('D8','Notification Delivery','store'),('M','Messaging Provider','external')],
  [('D3','3.1','current status / schedule / review deadlines'),('D2','3.1','recipient contact details'),('D8','3.1','prior delivery / duplicate keys'),('3.1','3.2','eligible confirmation / expiry / reminder / alert'),('3.2','M','message payload / recipients'),('M','3.3','delivery result'),('3.2','C','confirmation / reminder / expiry / staff alert'),('3.3','D8','delivery status / retry information')]),
 ('4','Generate Reports and Analytics',[
  ('4.1','Validate Report Request'),('4.2','Aggregate Reservation Measures'),('4.3','Present Search Results / Reports')],
  [('S','Authorized Staff','external'),('D2','Customer Records','store'),('D3','Reservations / Statuses','store'),('D4','Services / Bookable Schedules','store')],
  [('S','4.1','report filters / record search'),('4.1','4.2','authorized reporting criteria'),('D2','4.2','customer booking links'),('D3','4.2','reservation / status / allocated-time data'),('D4','4.2','service / bookable-time data'),('4.2','4.3','counts / trends / reserved-time utilization'),('4.3','S','searchable records / reports')]),
 ('5','Exchange POS Status and Verify Whole-Shop Deposit',[
  ('5.1','Prepare Reservation POS Exchange'),('5.2','Receive and Validate POS Result'),('5.3','Check Deposit Evidence / Staff Review'),('5.4','Persist Transaction / Verification Outcome')],
  [('P','POS Subsystem','external'),('S','Authorized Staff','external'),('D5','POS References / Verification','store'),('D3','Reservation References','store')],
  [('D3','5.1','reference / customer / service / allocations'),('5.1','P','reservation details / status query'),('P','5.2','transaction reference / payment or deposit status'),('D5','5.2','prior processed result identifiers'),('5.2','5.3','validated status / failure / duplicate result'),('S','5.3','verification decision / agreed evidence'),('5.3','S','deposit evidence / exchange outcome for review'),('5.3','5.4','verified / unverified / failed outcome'),('5.4','D5','correlated POS reference / verification history')]),
 ('6','Answer Basic Inquiries',[
  ('6.1','Match Inquiry to Predefined Content'),('6.2','Return Answer or Staff Referral')],
  [('C','Customer / Visitor','external'),('D6','Approved FAQ Content','store')],
  [('C','6.1','inquiry text'),('D6','6.1','approved services / policies / FAQ answers'),('6.1','6.2','matched answer / no-match result'),('6.2','C','predefined answer / staff referral')]),
]


def level2():
    master=Diagram('05-dfd-level2-combined','Level 2 DFD | Combined Decomposition of Processes 1.0-6.0','Data movement, not execution order | repeated store/entity IDs denote the same logical endpoint | blue: inter-process exchange',3300,8150)
    details=[]
    for idx,(num,title,procs,ends,flows) in enumerate(SPECS):
        detail=Diagram(f'06-dfd-level2-{num}',f'Level 2 DFD | {num}.0 {title}','Same process IDs and boundary flows as the combined DFD | see README for process rules and policies',3300,1450)
        details.append(detail)
        for d,base in [(master,150+idx*1300),(detail,150)]:
            d.band(f'{num}.0 | {title}',35,base,2790,1220)
            xs=[185+i*520 for i in range(len(procs))]
            for (id,label),x in zip(procs,xs): d.n(id,f'{id}\n{label}',x,base+450,330,105)
            # External entities sit above the processes; stores below them.
            stores=[t for t in ends if t[2]=='store']; external=[t for t in ends if t[2]!='store']
            for j,(id,label,kind) in enumerate(stores):
                x=180+j*650
                d.n(f'{num}-{id}',f'{id}\n{label}',x,base+1100,430,75,kind)
            for j,(id,label,kind) in enumerate(external):
                d.n(f'{num}-{id}',label,180+j*1000,base+65,430,65,kind)
            def ident(v): return v if '.' in v else f'{num}-{v}'
            storelane=0; externallane=0
            for fi,(a,b,label) in enumerate(flows):
                aa,bb=ident(a),ident(b); na,nb=d.nodes[aa],d.nodes[bb]
                if '.' in a and '.' in b:
                    if nb.x-na.x==520:
                        d.e(aa,bb,label,'rl',at=((na.x+na.w+nb.x)/2,base+502))
                    else:
                        yy=base+590+(fi%2)*45
                        d.e(aa,bb,label,'bb',[(na.x+na.w/2,yy),(nb.x+nb.w/2,yy)],((na.x+nb.x)/2+165,yy))
                elif na.kind=='store' or nb.kind=='store':
                    srcstore=na.kind=='store'; proc=nb if srcstore else na; store=na if srcstore else nb
                    lane=base+665+storelane*34; storelane+=1; px=proc.x+proc.w/2; sx=store.x+store.w/2
                    pts=[(sx,lane),(px,lane)] if srcstore else [(px,lane),(sx,lane)]
                    d.e(aa,bb,label,'tb' if srcstore else 'bt',pts,((sx+px)/2,lane))
                else:
                    srcext=na.kind=='external'; proc=nb if srcext else na; ext=na if srcext else nb
                    lane=base+165+externallane*27; externallane+=1; px=proc.x+proc.w/2; ex=ext.x+ext.w/2
                    pts=[(ex,lane),(px,lane)] if srcext else [(px,lane),(ex,lane)]
                    d.e(aa,bb,label,'bt' if srcext else 'tb',pts,((ex+px)/2,lane))
    # Actual linked flows between decompositions; dedicated right-side routing corridor.
    links=[('1.4','2.2','F1 Authorized identity / role / ownership',2860),
           ('1.4','4.1','F2 Staff authorization result',2900),
           ('2.4','3.1','F3 Committed reservation events',2940),
           ('2.4','5.1','F4 Reservation / whole-shop deposit request',2980),
           ('5.4','2.4','F5 POS status / deposit verification outcome',3020),
           ('2.5','3.1','F6 Committed change outcome',3060)]
    for a,b,label,x in links:
        na,nb=master.nodes[a],master.nodes[b]
        sy=na.y+na.h/2; ty=nb.y+nb.h/2
        sourcebase=na.y-450; targetbase=nb.y-450
        master.e(a,b,label,'rt',[(na.x+na.w+15,sy),(na.x+na.w+15,sourcebase+600),(x,sourcebase+600),(x,targetbase+420),(nb.x+nb.w/2,targetbase+420)],(x-95,sy+95+(links.index((a,b,label,x))%3)*60),color='#245f9a')
    # External decomposition boundary connectors in detail sheets match master links.
    for detail,(num,_,_,_,_) in zip(details,SPECS):
        related=[l for l in links if l[0].startswith(num+'.') or l[1].startswith(num+'.')]
        for i,(a,b,label,_) in enumerate(related):
            id=f'boundary{i}'; incoming=b.startswith(num+'.'); target=b if incoming else a
            detail.n(id,('From '+a if incoming else 'To '+b)+'\n'+label,2840,145+i*145,235,110,'external')
            if incoming: detail.e(id,target,'','lt',[(2810-i*15,200+i*145),(2810-i*15,560),(detail.nodes[target].x+165,560)],color='#245f9a')
            else: detail.e(target,id,'','br',[(detail.nodes[target].x+165,745+i*12),(2800-i*15,745+i*12),(2800-i*15,200+i*145)],color='#245f9a')
    return master,details


def level1():
    d=Diagram('04-dfd-level1','Level 1 DFD | Parent Process Boundary Reference','Six parent processes | repeated IDs identify the same entity, store or process | each panel shows one parent boundary',3100,8150)
    cross=[('1','2','authorized identity / role / ownership'),('1','4','staff authorization'),('2','3','committed reservation / change events'),('2','5','reservation / whole-shop deposit request'),('5','2','POS status / deposit verification outcome')]
    names={num:title for num,title,_,_,_ in SPECS}
    for idx,(num,title,procs,ends,flows) in enumerate(SPECS):
        base=150+idx*1300; d.band(num+'.0 | '+title,35,base,3030,1220)
        center=num+'.0'; d.n(center,center+'\n'+title,1260,base+520,480,130)
        enddict={id:(label,kind) for id,label,kind in ends}
        incoming={}; outgoing={}
        for a,b,label in flows:
            if '.' not in a: incoming.setdefault(a,[]).append(label)
            if '.' not in b: outgoing.setdefault(b,[]).append(label)
        for a,b,label in cross:
            if b==num:
                incoming['P'+a]=[label]; enddict['P'+a]=(a+'.0 '+names[a],'process')
            if a==num:
                outgoing['P'+b]=[label]; enddict['P'+b]=(b+'.0 '+names[b],'process')
        for direction,entries in [('in',incoming),('out',outgoing)]:
            count=len(entries)
            for j,(id,labels) in enumerate(entries.items()):
                label,kind=enddict[id]; x=100 if direction=='in' else 2530
                yy=base+100+j*min(150,980/max(count-1,1)); nid=f'{num}-{direction}-{id}'
                d.n(nid,(id+'\n' if kind=='store' else '')+label,x,yy,440,90,kind)
                text='; '.join(dict.fromkeys(labels)); cy=yy+45
                if direction=='in':
                    lane=1080+j*20
                    d.e(nid,center,text,'rl',[(lane,cy),(lane,base+585)],(800,cy),color='#245f9a' if id.startswith('P') else INK)
                else:
                    lane=1920+j*20
                    d.e(center,nid,text,'rl',[(lane,base+585),(lane,cy)],(2270,cy),color='#245f9a' if id.startswith('P') else INK)
    return d


def integration():
    d=Diagram('07-pos-integration','Integration Flow Diagram | Reservation and POS','Logical sequence; transport/authentication/retries to be agreed | payment processing remains in POS',2600,2550)
    d.band('Reservation Subsystem',35,135,1750,2335); d.band('External POS Subsystem',1830,135,735,2335)
    d.n('start','Committed reservation / staff deposit inquiry',260,210,460,75,'terminal')
    d.n('prepare','5.1 Prepare agreed exchange\nReference, relevant customer/service details,\ninterval and resource allocations',260,360,460,110)
    d.n('pos','Retrieve / process POS transaction\nPOS owns billing, payment/deposit collection,\nvouchers, receipts and settlement',1950,360,550,125,'external')
    d.n('received','POS result received?',260,610,460,120,'decision')
    d.n('missing','Missing / timeout / failure\nRecord exchange failure; deposit stays UNVERIFIED\nStaff follow-up or agreed retry policy',1160,640,580,120)
    d.n('valid','5.2 Response authentic, correlated\nand valid under agreed contract?',260,850,460,130,'decision')
    d.n('bad','Reject / quarantine invalid response\nRecord reason; no verified-deposit update',1160,910,580,100)
    d.n('duplicate','Already processed result?',260,1100,460,115,'decision')
    d.n('dedupe','Return existing outcome\nNo duplicate record or repeated effect',1160,1140,580,100)
    d.n('evidence','5.3 Deposit evidence accepted\nunder agreed verification rules?',260,1360,460,135,'decision')
    d.n('unverified','Record applicable transaction status\nDeposit remains UNVERIFIED\nStaff review / agreed deposit deadline',1160,1400,580,120)
    d.n('save','5.4 Persist correlated transaction reference\nand VERIFIED deposit outcome once\nRetain verification history',260,1630,460,110)
    d.n('review','Return verification outcome to 2.4\nStaff reviews current Pending reservation\nDeposit verification alone is not confirmation',260,1880,460,120)
    d.n('approved','Staff approves + booking eligible?',260,2110,460,115,'decision')
    d.n('confirm','Commit Confirmed state\nSend approval email after commit [3.1-3.3]',260,2330,460,95,'terminal')
    d.n('pending','Use recorded state / staff review as applicable\nNo duplicate effects or automatic confirmation;\nno invented deposit-deadline cancellation',1160,2110,580,130,'note')
    d.n('contract','CONTRACT TO AGREE\nProtocol, authorization, identifiers, fields,\nevidence, ordering, retries and duplicate rules.\nOrdinary online expiry is separate from\nthe whole-shop deposit-verification deadline.',1160,1660,580,170,'note')
    d.e('start','prepare'); d.e('prepare','pos','Reservation details / status query','rl',at=(970,410))
    d.e('pos','received','Transaction reference / payment or deposit status','bl',[(2225,550),(210,550),(210,670)],(950,550))
    d.e('received','missing','No','rl',at=(950,680)); d.e('received','valid','Yes',at=(490,790))
    d.e('valid','bad','No','rl',at=(950,930)); d.e('valid','duplicate','Yes',at=(490,1040))
    d.e('duplicate','dedupe','Yes','rl',at=(950,1160)); d.e('duplicate','evidence','No',at=(490,1290))
    d.e('evidence','unverified','No','rl',at=(950,1430)); d.e('evidence','save','Yes',at=(490,1560))
    d.e('save','review'); d.e('review','approved'); d.e('approved','confirm','Yes',at=(490,2280))
    d.e('approved','pending','No','rl',at=(950,2165))
    for id in ['missing','bad','dedupe','unverified']:
        n=d.nodes[id]; xx={'missing':1850,'bad':1815,'dedupe':1780,'unverified':1745}[id]
        d.e(id,'pending','','rb',[(xx,n.y+n.h/2),(xx,2270),(1450,2270)],(xx,2020),dashed=True)
    return d


def validate(diagrams):
    for d in diagrams:
        for n in d.nodes.values():
            assert n.x>=0 and n.y>=0 and n.x+n.w<=d.w and n.y+n.h<d.h-35, (d.slug,n.id,'bounds')
            lines=wrapped(n.label,n.w-24,18 if n.kind=='note' else 19)
            assert len(wrapped(n.label,n.w-24,14))*17.5<=n.h+5,(d.slug,n.id,'text overflow',lines)
        for a,b,_,pts,at,_,_,_ in d.edges:
            assert a in d.nodes and b in d.nodes
            assert all(0<=x<=d.w and 0<=y<=d.h for x,y in pts),(d.slug,a,b,'edge bounds')
    expected={id for _,_,procs,_,_ in SPECS for id,_ in procs}
    master=next(d for d in diagrams if d.slug=='05-dfd-level2-combined')
    assert expected <= master.nodes.keys()
    for id in expected:
        assert any(e[0]==id for e in master.edges),(id,'missing output')
        assert any(e[1]==id for e in master.edges),(id,'missing input')
    print(f'PASS: {len(diagrams)} sheets; {len(expected)} Level 2 processes; bounds, text height, and process input/output checks.')


if __name__=='__main__':
    master,details=level2()
    diagrams=[architecture(),system_flow(),context(),level1(),master,*details,integration()]
    validate(diagrams)
    export(diagrams)
    assert len(ET.parse(OUT/'reservation-diagrams.drawio').getroot().findall('diagram'))==len(diagrams)
    print('Created editable draw.io, SVG sheets and reservation-diagrams.pdf')

