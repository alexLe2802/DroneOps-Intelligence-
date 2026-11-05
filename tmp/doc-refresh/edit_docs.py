from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED
from copy import deepcopy
from lxml import etree as E
import json, hashlib

ROOT=Path('D:/DroneOps-Intelligence-')
BUILD=ROOT/'tmp/doc-refresh'
W='http://schemas.openxmlformats.org/wordprocessingml/2006/main'
NS={'w':W}
TH='Le Quoc Thong'; KH='Giang Nguyen Gia Khanh'
def text(e): return ''.join(e.xpath('.//w:t/text()',namespaces=NS))
def replace(e,a,b):
    # Preserve existing run formatting, fields, drawings and unrelated text.
    for p in e.xpath('.//w:p',namespaces=NS) if e.tag!=f'{{{W}}}p' else [e]:
        if p.xpath('.//w:drawing|.//w:pict',namespaces=NS): continue
        ts=p.xpath('.//w:t',namespaces=NS)
        old=''.join(t.text or '' for t in ts)
        if a not in old: continue
        search_from=0
        while a in old[search_from:]:
            start=old.index(a,search_from);end=start+len(a);pos=0
            for t in ts:
                v=t.text or '';lo=pos;hi=pos+len(v);pos=hi
                if hi<=start or lo>=end:continue
                t.text=v[:max(0,start-lo)]+(b if lo<=start<hi else '')+v[max(0,end-lo):]
                t.set('{http://www.w3.org/XML/1998/namespace}space','preserve')
            old=''.join(t.text or '' for t in ts)
            search_from=start+len(b)
def swap_names(e):
    for a,b in [(TH,'__TH__'),(KH,TH),('__TH__',KH),('ThongLQ','__TL__'),('KhanhGNG','ThongLQ'),('__TL__','KhanhGNG')]:replace(e,a,b)
def paragraph(s,style=None,bold=False):
    p=E.Element(f'{{{W}}}p');pr=E.SubElement(p,f'{{{W}}}pPr')
    if style:E.SubElement(pr,f'{{{W}}}pStyle').set(f'{{{W}}}val',style)
    E.SubElement(pr,f'{{{W}}}spacing',attrib={f'{{{W}}}after':'100'})
    r=E.SubElement(p,f'{{{W}}}r')
    if bold:E.SubElement(E.SubElement(r,f'{{{W}}}rPr'),f'{{{W}}}b')
    E.SubElement(r,f'{{{W}}}t').text=s
    return p
COMMON=[
('Implemented scope','AutomaticFlightRequested is optional and defaults to false. Changes follow the mission proposal and approval workflow. This release prepares readiness; dispatchEnabled is false. Neither confirmation nor gateway enrollment uploads a mission, arms motors, sends TAKEOFF/AUTO/MISSION_START, or changes the mission to InProgress.'),
('Confirmation conditions','For an approved, opted-in mission with a schedule, the backend opens confirmation at T minus 5 minutes and closes it at the scheduled start. The authenticated operator must keep a visible browser tab online. Presence is sent every 10 seconds and expires after 30 seconds. The selected, unrevoked mission connection must match the active UAV and flight-controller UID, with gateway heartbeat within 30 seconds and telemetry within 15 seconds.'),
('Readiness evidence','Confirmation reruns preflight and binds readiness to the exact plan hash and connection ID. Readiness requires a passing preflight for that hash from the last minute. Changed plan/connection, stale presence or missing telemetry invalidates the conditions; the operator must review and confirm again. Dismissal, a hidden/offline tab or a missed window is not flight consent.'),
('Future execution scope','Autopilot execution remains a separate development phase: firmware/airframe capability detection, mission compilation and upload/read-back, command IDs/deadlines and replay protection, scheduled condition rechecks, local pre-arm/failsafe enforcement, and ACK plus observed flight-state verification. Validate ArduPilot/PX4 SITL, propeller-free bench operation and supervised flight before enabling dispatch. AI advice cannot authorize launch.')]
SRS=[
('FR-AF-01 Opt in and review','Actor: mission owner; reviewer: authorized Manager. Saving a changed automatic-flight option creates reviewed proposed content. Acceptance: the option defaults off, unapproved changes do not replace approved content, and readiness is unavailable for a non-opted-in or non-approved mission.'),
('FR-AF-02 Confirm readiness','Actor: authenticated mission owner with uav_operator role. Acceptance: only T-5 minutes <= backend now < start permits confirmation; another owner cannot access the mission; missing/stale presence, heartbeat, telemetry or preflight blocks readiness. Success stores the current plan hash and connection, without sending aircraft commands.'),
('FR-AF-03 Cancel and invalidate','Acceptance: cancelling clears confirmation and presence. A changed hash/connection or expired presence must not reuse earlier confirmation. A missed start window requires rescheduling and review; reconnection must not imply late launch consent.'),
('FR-AF-04 Export plan','GET /api/missions/{id}/automation/plan returns an authorized plan envelope, including mission/UAV IDs, planHash, UTC schedule, speed in m/s, altitude reference and ordered waypoints with WGS84 degrees/E7 and altitude metres. dispatchEnabled remains false; this is not a compiled MAVLink mission.'),
('Traceability','FR-AF-01 extends FE-02/03 and MF1; FR-AF-02/03 extend MF2 monitoring and preparation; FR-AF-04 bridges planning and gateway design. MF2 owner is ThongLQ, supported by NghiaHQ for planning/preflight. AI MF4 remains owned by KhanhGNG. Tests AF-01 through AF-08 below are design targets, not execution results.')]
TESTS=[
('AF-01 Opt-in review','Create a mission without the option, then propose an option change. Expect false by default and the approved plan unchanged until review.'),
('AF-02 Window boundaries','Check immediately before T-5, at T-5, immediately before start, and at start using backend time. Expect confirmation only within [T-5, start).'),
('AF-03 Identity and scope','Try another owner, wrong UAV/UID, revoked/expired connection and inactive UAV. Expect denial or blocked readiness; no aircraft command.'),
('AF-04 Freshness','Exercise stale operator presence (>30 s), gateway heartbeat (>30 s) and telemetry (>15 s), and restore them independently. Missing prerequisites must remain visible; restoring one must not bypass the others.'),
('AF-05 Evidence changes','Confirm, then change plan hash or connection, or expire the passing preflight (>1 minute). Expect earlier evidence not to authorize readiness for changed/stale inputs.'),
('AF-06 Dismiss and disconnect','Dismiss/cancel; hide the tab; lose Internet/session; reconnect after the window. Expect no implied consent or late launch, and cancel clears saved confirmation/presence.'),
('AF-07 Plan contract','Export as owner and non-owner; verify units, waypoint order, altitude reference, E7 conversion and dispatchEnabled=false. Reject unauthorized export; do not interpret a missing altitude as zero.'),
('AF-08 No dispatch','Confirm with all prerequisites and inspect gateway traffic and mission state. Expect no mission upload, ARM, TAKEOFF, mode/start command or readiness-driven InProgress transition.')]

files=list((ROOT/'docs/reports').glob('*.docx'))+list(ROOT.glob('*.docx'))
audit=[]
for p in files:
    if 'RecordOfChanges' in p.name:continue
    with ZipFile(p) as z: data={i.filename:z.read(i.filename) for i in z.infolist()}; infos=z.infolist()
    root=E.fromstring(data['word/document.xml']);body=root.find('w:body',NS)
    tables=root.findall('.//w:tbl',NS)
    # History tables are evidence of previous authorship, not current assignments.
    protected=set()
    for t in tables:
        h=text(t)[:130]
        if ('Change Description' in h or 'Change description' in h): protected.add(t)
    for t in tables:
        if t in protected:continue
        for row in t.findall('w:tr',NS):
            s=text(row)
            if p.name.startswith('Report1_') and ('Leader; MF4' in s or 'Member; MF2' in s):continue
            if p.name.endswith('DesignBrief.docx') and tables.index(t)==8:continue
            if any(k in s for k in ['MF2','WF-02','MF4','WF-04','ThongLQ owns mitigation','All members; ThongLQ leads']):
                # Per-member identity tables keep names/student IDs/leadership fixed.
                if p.name.endswith('Commitment.docx') and tables.index(t) in [29,30,31,39,40,41,50]:continue
                swap_names(row)
            elif p.name.endswith('DesignBrief.docx') and any(k in s for k in ['D3Normalize','D5Keep','R1Registered','R2Realtime','G-01','G-06','G-08']):swap_names(row)
            elif 'SelfReview' in p.name and any(k in s for k in ['SR-01Scope','SR-06Architecture','SR-08States']):swap_names(row)
    # Inline allocation summaries, outside the historical change tables.
    for para in body.findall('w:p',NS):
        s=text(para)
        if any(k in s for k in ['Confirmed mainflow allocation:','Team-confirmed allocation']):swap_names(para)
    if p.name.startswith('Report1_'):
        for t in tables:
            if t in protected:continue
            for row in t.findall('w:tr',NS):
                if TH in text(row):replace(row,'Leader; MF4','Leader; MF2')
                if KH in text(row):replace(row,'Member; MF2','Member; MF4')
    if p.name.startswith('Report2_'):
        for row in tables[7].findall('w:tr',NS):
            if text(row).startswith(('MF2','MF4')):
                cells=row.findall('w:tc',NS)
                a,b=deepcopy(cells[1]),deepcopy(cells[3]);row.replace(cells[1],b);row.replace(cells[3],a)
        # Dossier coordination remains with the team leader, separate from AI ownership.
        for row in tables[10].findall('w:tr',NS):
            if 'MF4 / dossier coordination' in text(row):
                replace(row,'MF4 / dossier coordination','MF4')
                replace(row,'Confirm AI registered scope; review draw.io, English SRS/SDD, CI and demo readiness.','Own AI context, provider integration and failure handling. ThongLQ retains dossier/CI coordination.')
    if p.name.endswith('Commitment.docx'):
        replace(tables[29],'Leader; MF4 owner','Leader; MF2 owner');replace(tables[39],'Member; MF2 owner','Member; MF4 owner')
        for a,b in [(30,40),(31,41)]:
            aa,bb=deepcopy(tables[a]),deepcopy(tables[b]);body.replace(tables[a],bb);body.replace(tables[b],aa)
        rows=tables[50].findall('w:tr',NS)
        for ri in [1,3]:
            for cell in rows[ri].findall('w:tc',NS)[1:]:
                other=rows[3 if ri==1 else 1].findall('w:tc',NS)
        c1=rows[1].findall('w:tc',NS);c3=rows[3].findall('w:tc',NS)
        for j in range(1,len(c1)):
            a,b=deepcopy(c1[j]),deepcopy(c3[j]);rows[1].replace(c1[j],b);rows[3].replace(c3[j],a)
        # Move AI-only corrective action ownership, keep team representation unchanged.
        for row in tables[53].findall('w:tr',NS):
            if 'FIX-01' in text(row):replace(row,TH,KH)
    if p.name.endswith('DesignBrief.docx'):
        for row in tables[8].findall('w:tr',NS):
            if TH in text(row):
                replace(row,'MF4','MF2');replace(row,'AI adapter','gateway/live monitoring');replace(row,'WF-04','WF-02');replace(row,'D1, D5; 48aea3c','D1, D3; current implementation evidence to verify')
            elif KH in text(row):
                replace(row,'Realtime/MF2 owner','AI/MF4 owner');replace(row,'Gateway/telemetry; incidents; live monitoring','AI context; provider adapter; advisory/failure handling');replace(row,'WF-02','WF-04');replace(row,'D3; telemetry/incident lifecycle','D5; AI assessment evidence to verify')
    if p.name.startswith('Report5_'):
        for t in tables:
            for row in t.findall('w:tr',NS):
                if TH in text(row) and 'MF2 owner' in text(row):replace(row,'Telemetry, incident, timer and reconnect tests.','Telemetry, incident, timer, reconnect and readiness tests; FE/CI and dossier coordination.')
                if KH in text(row) and 'MF4 / coordination' in text(row):replace(row,'MF4 / coordination','MF4 owner');replace(row,'AI context/failure tests, FE/CI evidence and dossier coordination.','AI context, provider, provenance and failure tests.')
    # Append content on a separate page so source diagrams and page geometry remain untouched.
    heading=paragraph('Automatic flight preparation and workflow ownership','Heading1')
    E.SubElement(heading.find('w:pPr',NS),f'{{{W}}}pageBreakBefore')
    blocks=[heading,paragraph('Current ownership: MF1 / WF-01 — NghiaHQ (Ha Quang Nghia), planning and preflight; MF2 / WF-02 — ThongLQ (Le Quoc Thong), realtime monitoring, incidents and flight preparation; MF3 / WF-03 — DucDDA (Dang Dao Anh Duc), post-flight; MF4 / WF-04 — KhanhGNG (Giang Nguyen Gia Khanh), AI assessment. ThongLQ remains team leader and dossier coordinator.')]
    entries=COMMON.copy()
    if 'Report3_' in p.name:entries+=SRS
    if 'Report4_' in p.name or 'DesignBrief' in p.name:
        entries += [('Service and API boundary','MissionAutomationService and MissionAutomationController provide GET /api/missions/automation/due and GET /api/missions/{id}/automation, POST presence/confirm/cancel and GET plan below that mission automation route. Owner authorization and uav_operator role apply. MissionAutomationState retains presence/confirmation, confirmed plan hash and connection ID; trusted enrollment and mission leases remain separate. FlightPreparationPrompt polls only when visible/online. Migration: 20261104115300_gateway_enrollment_and_flight_preparation.sql; deploy backend, frontend and gateway package together.'),('Diagram maintenance','Existing images and diagrams are intentionally unchanged. Architecture/deployment, logical ERD, mission state, planning/review and telemetry/gateway sequences require alignment with readiness and enrollment. Model readiness separately from aircraft execution: confirmation is not an InProgress transition.')]
    if 'Report2_' in p.name:entries += [('Delivery and accountability','ThongLQ owns the MF2 gateway/readiness increment and its integration evidence; NghiaHQ reviews planning/hash/preflight dependencies; KhanhGNG owns MF4 AI risks and provider tests. Current schedule dates and effort totals are historical planning estimates, not evidence that command execution is implemented. Plan SITL/upload/dispatch work separately before committing execution dates.')]
    if 'Report5_' in p.name:entries += [('Test execution status','The following flight-preparation cases are planned acceptance/regression targets. No execution date, pass result or physical-aircraft evidence is claimed by this document update. MF2 owner ThongLQ coordinates execution; NghiaHQ reviews preflight fixtures; KhanhGNG verifies that AI advice cannot bypass readiness.')] + TESTS
    if 'Commitment' in p.name:entries += [('Additional verification commitment','MF2 owner ThongLQ coordinates readiness window, identity, freshness, plan/connection invalidation, cancellation and no-dispatch verification with MF1 owner NghiaHQ. These are additional planned checks; existing historical test totals/results are not automatically increased or marked executed. Aircraft execution and revised delivery dates require a separate agreed implementation/test baseline.')]
    if 'SelfReview' in p.name:entries += [('Readiness review follow-up','Readiness is implemented; aircraft dispatch is disabled. Existing review findings and dates remain historical. Additional review must check the preparation state/API against the schema, boundaries and UI; verify fresh scoped evidence and absence of motor/start commands. Diagram updates and physical-aircraft acceptance remain outstanding, without claiming new pass evidence.')]
    for title,content in entries:blocks += [paragraph(title,'Heading2'),paragraph(content)]
    sect=body.find('w:sectPr',NS);index=list(body).index(sect) if sect is not None else len(body)
    for b in blocks:body.insert(index,b);index+=1
    updated=E.tostring(root,xml_declaration=True,encoding='UTF-8',standalone=True)
    backup=BUILD/'originals'/p.relative_to(ROOT);backup.parent.mkdir(parents=True,exist_ok=True)
    if not backup.exists():backup.write_bytes(p.read_bytes())
    data['word/document.xml']=updated
    with ZipFile(p,'w',ZIP_DEFLATED) as z:
        for info in infos:z.writestr(info,data[info.filename])
    with ZipFile(p) as z:
        assert all(z.read(n)==v for n,v in data.items() if n!='word/document.xml')
    assert all(E.tostring(t)==E.tostring(E.fromstring(ZipFile(backup).read('word/document.xml')).findall('.//w:tbl',NS)[tables.index(t)]) for t in protected)
    audit.append({'file':str(p.relative_to(ROOT)),'images_preserved':True,'history_tables_preserved':len(protected),'added_sections':len(entries)})
BUILD.joinpath('doc_audit.json').write_text(json.dumps(audit,indent=2),encoding='utf-8')
print(json.dumps(audit,indent=2))
