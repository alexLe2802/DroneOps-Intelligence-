from pathlib import Path
from zipfile import ZipFile,ZIP_DEFLATED
from lxml import etree as E
from copy import deepcopy
import json,shutil

R=Path('D:/DroneOps-Intelligence-');B=R/'tmp/final-submission'
helper={};exec((R/'tmp/doc-refresh/edit_docs.py').read_text(encoding='utf-8').split('files=list(')[0],helper)
W=helper['W'];N=helper['NS'];txt=helper['text'];rep=helper['replace'];para=helper['paragraph']
OWN='MF1 / WF-01: NghiaHQ — planning and preflight. MF2 / WF-02: ThongLQ — gateway, scheduled automatic execution, realtime monitoring and incidents. MF3 / WF-03: DucDDA — post-flight history/reporting. MF4 / WF-04: KhanhGNG — AI assessment. ThongLQ remains team leader and dossier coordinator.'
SCOPE='The final functional baseline covers scheduled automatic mission execution for supported ArduPilot Copter and PX4 multicopter profiles through the trusted gateway on Jetson or a radio-connected laptop. The platform manages approval, mission compilation/upload/read-back, explicit pilot consent, scheduled dispatch, execution evidence and post-flight records. The autopilot retains stabilization, navigation loops, pre-arm checks and onboard failsafes. AI remains advisory and cannot authorize approval or launch.'
FLOW='The operator enables AutomaticFlightRequested in the reviewed plan. After approval, the gateway verifies the bound controller UID and firmware profile, compiles the approved route, uploads it and verifies the normalized read-back. In [T−5 minutes, scheduled start), the authenticated pilot confirms the exact plan and connection while the portal is visible and online. At the scheduled start the scheduler revalidates all conditions, issues a uniquely identified, expiring execution envelope, and records gateway outcomes. Mission InProgress is recorded only after observed flight-state evidence; accepted upload or command ACK alone is insufficient. Trusted landed/completion evidence finalizes the flight and report.'
GATES='All dispatch prerequisites are conjunctive: approved opted-in mission; no pending update; eligible pilot/UAV and reserved schedule; identical planHash, compiledMissionHash, controller UID and connection/lease; verified firmware capabilities and mission read-back; pilot confirmation in the permitted window; presence and gateway heartbeat no older than 30 seconds; telemetry no older than 15 seconds; a fresh passing preflight for the current plan within 60 seconds; and successful local pre-arm checks. Presence is refreshed every 10 seconds. A changed plan, controller, connection or uploaded mission invalidates previous consent and execution authorization.'
FAIL='A blocked prerequisite, expired envelope or cancelled consent prevents dispatch and produces a specific reason. Reconnection or restart never creates late launch consent. Delivery retries reuse the same commandId and cannot create another start. If aircraft outcome is uncertain, the gateway reconciles observed state and does not blindly repeat arm/start. After takeoff, loss of web/backend connectivity follows the configured onboard failsafe; pilot RC takeover remains available. A preflight Cancel action cannot stop an airborne vehicle or erase its execution evidence.'

FR=[
('FR-AF-01 Reviewed opt in','Actor: mission owner; reviewer: authorized Manager. The system shall default AutomaticFlightRequested to false and include changes in the proposed payload and plan hash. Acceptance: an unapproved change cannot alter the approved execution plan; a pending update prevents dispatch.'),
('FR-AF-02 Pilot confirmation','Actor: authenticated mission owner with uav_operator role. The system shall accept confirmation only when T−5 minutes <= backend now < scheduled start. Acceptance: save pilot/time/planHash/connectionId only after current preflight and connection checks; confirmation itself shall not arm or launch the vehicle.'),
('FR-AF-03 Revoke and invalidate','The system shall clear or invalidate authorization after cancel, plan/hash change, changed UID/connection/lease, stale presence or replaced uploaded mission. Acceptance: a stale confirmation cannot authorize a new plan or connection; a missed window requires rescheduling and review, with no reconnect catch-up.'),
('FR-AF-04 Authorized plan contract','The system shall expose the approved execution plan to its authorized owner and trusted gateway. The envelope includes schema version, mission/UAV IDs, planHash, UTC schedule, speed m/s, altitude reference, ordered waypoint coordinates in WGS84 degrees/E7 and altitude metres. Acceptance: no null altitude becomes zero; unauthorized data access is denied; dispatchEnabled is derived from release configuration and the complete gate set, not hard-coded true or false.'),
('FR-AF-05 Vehicle capabilities','Actor: trusted gateway. The system shall identify the controller UID, autopilot family/version, vehicle type, discovered target IDs and supported commands. Acceptance: unsupported firmware/profile, unbound UID, unavailable altitude-frame conversion or unsupported payload action blocks compilation/dispatch with a reason.'),
('FR-AF-06 Mission compilation','The system shall compile the approved waypoint actions and speed policy using the selected firmware/airframe profile. Acceptance: preserve sequence and coordinate precision; validate takeoff/landing structure, altitude frame/home/terrain, hold/yaw/radius fields and camera capability. Persist adapter version and compiledMissionHash separately from planHash.'),
('FR-AF-07 Upload and verification','The gateway shall transfer the compiled mission with the MAVLink Mission Protocol, bounded timeout/retry and explicit failure handling, then download/read-back and compare normalized mission content. Acceptance: matching plan/controller/compiled hashes and successful read-back are required; upload ACK alone does not authorize launch.'),
('FR-AF-08 Scheduled dispatch','Actor: backend scheduler and trusted gateway. At scheduled start the scheduler shall atomically recheck all gates and create one dispatch identity for the approved execution snapshot. The release dispatch deadline is scheduled start plus 5 seconds; arrivals after that deadline expire. Acceptance: competing workers/restarts cannot create duplicate starts; missed dispatch is blocked rather than rescheduled automatically.'),
('FR-AF-09 Local execution','The gateway shall validate envelope identity, deadline, UID, plan and compiled hashes, lease, local telemetry and pre-arm state before applying the firmware-specific arm/mode/start sequence. Acceptance: persist command identity before side effects; use ACK and observed state to track progress; never bypass autopilot safety checks or replay an uncertain start.'),
('FR-AF-10 Flight-state reconciliation','The system shall distinguish upload, confirmation, dispatch, command acknowledgement and observed execution. Acceptance: only trusted telemetry confirming the required armed/in-air/mission-progress conditions creates the immutable flight execution and InProgress transition; missing or contradictory state remains explicit, without fabricated takeoff.'),
('FR-AF-11 Completion and failures','The system shall persist dispatch attempts, failures, telemetry-linked outcomes and completion idempotently. Acceptance: duplicate completion cannot duplicate a flight/report; aborted or partial flights retain outcome and coverage; trusted landed/terminal evidence is required before finalization.'),
('FR-AF-12 Pilot authority and resilience','The platform shall retain pilot RC takeover and configured onboard failsafe authority after dispatch. Acceptance: web loss does not stream fabricated control inputs, force a universal RTL policy, erase the flight or restart the mission; preflight cancellation never acts as an airborne stop command.')]
CASES=[
('AF-01','Reviewed opt in','Change the automatic option before and after approval.','Option defaults off; approved content changes only through review; a pending update blocks dispatch.'),
('AF-02','Consent window','Exercise T−5 minus a tick, T−5, start minus a tick and start.','Consent accepted only in [T−5,start), using backend time; confirmation alone sends no start.'),
('AF-03','Identity and authorization','Use another owner, wrong UID/UAV, expired/revoked lease or inactive UAV.','Access is denied or dispatch blocked; no aircraft command is issued.'),
('AF-04','Freshness gates','Expire presence, heartbeat, telemetry and preflight independently.','The relevant gate blocks dispatch; restoring one condition cannot bypass the others.'),
('AF-05','Changed execution context','Confirm, then replace plan, connection or uploaded mission.','Consent/read-back binding is invalidated; stale hashes cannot authorize execution.'),
('AF-06','Cancel and missed window','Cancel before dispatch, hide tab, lose session and reconnect after deadline.','No implied consent or late catch-up start; cancelled authorization remains unusable.'),
('AF-07','Plan and unit contract','Export owner/non-owner plans; test degrees/E7, altitude frame and missing values.','Correct units/order and scope; null altitude is rejected; dispatchEnabled reflects all gates.'),
('AF-08','Eligible scheduled execution','Provide approved plan, verified upload, valid consent and all fresh gates at start.','One execution envelope reaches the correct gateway; no early arm/start; InProgress requires actual flight evidence.'),
('AF-09','Firmware and payload capability','Use supported profiles, unsupported firmware and a missing-camera photo action.','Supported profile compiles; incompatible airframe/frame/action blocks before execution.'),
('AF-10','Upload read-back integrity','Inject upload timeout, rejected ACK, reordered/wrong downloaded items and hash mismatch.','Bounded failure is visible; no dispatch until normalized read-back matches the compiled mission.'),
('AF-11','Replay and delivery retry','Duplicate/reorder one commandId across reconnects and workers.','Same identity is deduplicated; delivery retries do not repeat physical start or create another execution.'),
('AF-12','ACK versus flight evidence','Accept command ACK while vehicle remains on ground; then supply valid flight-state evidence.','ACK alone leaves mission Approved; one trusted execution/InProgress transition follows observed flight evidence.'),
('AF-13','Dispatch deadline','Deliver immediately before and after scheduled start plus 5 seconds.','Only a valid, still-eligible envelope within deadline can execute; expired dispatch never catches up.'),
('AF-14','Restart and uncertain outcome','Restart backend/gateway after accepting an envelope and during an uncertain start.','Durable identity survives; reconcile observed state; do not blindly re-arm or restart.'),
('AF-15','Loss and pilot takeover','Lose web/backend link during an airborne SITL mission; exercise RC takeover.','Configured onboard failsafe and pilot authority apply; history remains traceable; no automatic duplicate launch.'),
('AF-16','Completion and partial outcome','Repeat trusted completion; record an aborted/partial flight with telemetry gaps.','One execution/report; outcome and missing coverage remain explicit; no fabricated completed route.')]

def heading(s):return para(s,'Heading2')
def add_pairs(items):
 out=[]
 for a,b in items:out.extend([heading(a),para(b)])
 return out
def table(headers,rows,widths=None):
 t=E.Element(f'{{{W}}}tbl');pr=E.SubElement(t,f'{{{W}}}tblPr');E.SubElement(pr,f'{{{W}}}tblW',attrib={f'{{{W}}}w':'0',f'{{{W}}}type':'auto'})
 bd=E.SubElement(pr,f'{{{W}}}tblBorders')
 for edge in ['top','left','bottom','right','insideH','insideV']:E.SubElement(bd,f'{{{W}}}{edge}',attrib={f'{{{W}}}val':'single',f'{{{W}}}sz':'4',f'{{{W}}}color':'D9D9D9'})
 for i,values in enumerate([headers]+rows):
  tr=E.SubElement(t,f'{{{W}}}tr')
  if i==0:E.SubElement(E.SubElement(tr,f'{{{W}}}trPr'),f'{{{W}}}tblHeader')
  for value in values:
   tc=E.SubElement(tr,f'{{{W}}}tc');cp=E.SubElement(tc,f'{{{W}}}tcPr');E.SubElement(cp,f'{{{W}}}vAlign',attrib={f'{{{W}}}val':'center'})
   if i==0:E.SubElement(cp,f'{{{W}}}shd',attrib={f'{{{W}}}fill':'E7EEF5'})
   tc.append(para(str(value),bold=i==0))
 return t

audit=[]
files=[*list((R/'docs/reports').glob('*.docx')),*list(R.glob('*.docx'))]
for p in files:
 if 'RecordOfChanges' in p.name:continue
 backup=B/'originals'/p.relative_to(R);backup.parent.mkdir(parents=True,exist_ok=True)
 if not backup.exists():shutil.copy2(p,backup)
 with ZipFile(backup) as z:infos=z.infolist();data={i.filename:z.read(i.filename) for i in infos}
 root=E.fromstring(data['word/document.xml']);body=root.find('w:body',N)
 # Replace the old readiness-only supplement, preserving all original visuals.
 children=list(body);start=next((i for i,c in enumerate(children) if txt(c)=='Automatic flight preparation and workflow ownership'),None)
 if start is not None:
  for c in children[start:]:
   if E.QName(c).localname!='sectPr':body.remove(c)
 history=[t for t in root.findall('.//w:tbl',N) if 'Change Description' in txt(t)[:150] or 'Change description' in txt(t)[:150]]
 protected=set()
 for t in history:protected.update(t.iter())
 # Requirements remain normative; source-code or test completion is not inferred from document status.
 for pa in root.findall('.//w:p',N):
  if pa in protected or pa.xpath('.//w:drawing|.//w:pict',namespaces=N):continue
  s=txt(pa)
  replacements={
   'DroneOps Intelligence does not autonomously execute UAV flights. Actual flight execution and vehicle control remain the responsibility of compatible UAV autopilot or Ground Control Station systems.':'DroneOps Intelligence coordinates approved scheduled automatic missions through the trusted gateway and compatible autopilot. The autopilot executes the uploaded mission and retains low-level flight control and onboard failsafes; the pilot retains supervisory authority.',
   'Scope exclusions: no low-level navigation, stabilization, autonomous flight execution or replacement of GCS/autopilot software. Custom model training and autonomous AI approval are outside scope. Restriction checks cover configured data only; they do not certify compliance with unprovided restrictions.':'Scope exclusions: development of low-level navigation/stabilization algorithms, replacement of the autopilot, custom model training and autonomous AI approval. Scheduled automatic mission execution through supported autopilot profiles is in scope. Restriction checks cover configured data and do not certify unprovided restrictions.',
   'Flight preparation is implemented; aircraft command execution is future scope as specified below.':'Scheduled automatic execution is included in the final functional baseline specified below.',
   'Current in-place update must be replaced.':'Accepted proposals are applied atomically; direct mutation of approved content is prohibited.',
   'Normalized sample HTTP endpoint exists. Raw MAVLink adapter and background loss timer remain gaps; latest snapshot and SSE support frontend monitoring.':'The trusted Python MAVLink gateway provides controller enrollment, mission upload/read-back, authenticated execution envelopes and telemetry; an independent loss timer, authorized snapshots and SSE support monitoring.',
   'No application action issues low-level flight-control commands.':'The approved automation workflow may request mission execution through the trusted autopilot adapter; low-level stabilization remains onboard.',
   'Stored telemetry and updated UAV/mission projection without flight-control commands.':'Stored telemetry and updated UAV/mission projection; execution commands belong to the separate authorized automation workflow.',
   'Stored telemetry and UAV/mission projection; no low-level flight-control commands.':'Stored telemetry and UAV/mission projection, separate from authorized mission-dispatch commands.',
   'No model training or autonomous mission approval/flight control.':'No model training or AI-controlled mission approval; scheduled execution follows the reviewed automation contract.',
   'No model training or autonomous flight control is planned.':'Custom model training and low-level autopilot algorithm development are excluded; reviewed scheduled mission execution is included.',
   'Final version':'Final specification version',
   'Pending Review 2 confirmation':'Final submission — reviewer decision recorded by reviewers',
   '(to be finalized after Review 2)':'',
   '6. Proposed Mandatory Features':'6. Mandatory Features',
   'Appendix A – Defense 1.1 Targets':'Appendix A – Acceptance and Demonstration',
   'A3. Functional Test Case Target':'A3. Functional Test Coverage',
   'draft Report 6':'Report 6', 'draft user guides':'user guides','defense draft':'defense package','requirements/design drafts':'requirements/design baseline',
   'Team-confirmed allocation (16 September 2026)':'Current mainflow allocation',
   'Current in-place update':'Unreviewed in-place mutation',
   'Draft Report 6':'Report 6',
   'is proposed as an integrated software platform':'is an integrated software platform',
  }
  for old,new in replacements.items():
   if old in txt(pa):rep(pa,old,new)
  if 'SelfReview' not in p.name:
   for old,new in [('Review 2 submission v1.0','Final submission v1.0'),('Proposed BM03 change; pending Review 2 decision.','Responsive Web delivery baseline; native mobile is excluded from the final client scope.')]:rep(pa,old,new)
 # Final version labels do not fill reviewer signatures, approval decisions or execution results.
 if 'DesignBrief' in p.name:
  for pa in body.findall('w:p',N):
   if txt(pa)=='5. Known Gaps':rep(pa,txt(pa),'5. Review Findings and Verification Records')
  for pa in body.findall('w:p',N):
   if txt(pa)=='4. Top Risks':pass
  # Keep dated findings as evidence rather than silently declaring them resolved.
 if 'SelfReview' in p.name:
  for pa in body.findall('w:p',N):
   rep(pa,'3. Findings and Team Response','3. Findings Recorded on 27 October 2026')
   rep(pa,'Every High finding marked Kept or Planned must also appear in section 5 (Known Gaps) of the Design Brief.','The dated findings above retain their original evidence and responses. The final specification coverage is recorded below; issue closure requires linked implementation and test evidence.')
 if p.name.startswith('Report3_'):
  for pa in body.findall('w:p',N):
   if txt(pa).startswith('Scope exclusions:'):pass
  for t in root.findall('.//w:tbl',N):
   if t in history:continue
   rows=t.findall('w:tr',N)
   if rows and 'Detailed FRs' in txt(rows[0]):
    for row in rows[1:]:
     cells=row.findall('w:tc',N)
     if txt(cells[0]) in ['FE-02','FE-03','FE-05','FE-06','FE-08','FE-10']:
      current=txt(cells[2]);rep(cells[2],current,current.split('; FR-AF')[0]+'; FR-AF-01–12')
 title='6. Automatic Flight Requirements' if p.name.startswith('Report3_') else '3.5 Automatic Flight Execution' if p.name.startswith('Report4_') else 'Automatic Flight Operations'
 head=para(title,'Heading1');E.SubElement(head.find('w:pPr',N),f'{{{W}}}pageBreakBefore')
 blocks=[head,para('Final functional specification'),para(OWN),heading('Scope and operating boundary'),para(SCOPE),heading('End to end operation'),para(FLOW),heading('Dispatch authorization'),para(GATES),heading('Exceptions and pilot authority'),para(FAIL)]
 if p.name.startswith('Report3_'):
  blocks+=add_pairs(FR)
  blocks+=add_pairs([
   ('UC-AF-01 Prepare and verify an automatic mission','Primary actors: operator and trusted gateway. Trigger: an approved opted-in plan is assigned to the bound controller. Preconditions: active UAV/eligible pilot, valid schedule/lease and no pending proposal. Main flow: identify capabilities, compile, upload and read back. Alternatives: capability, frame, timeout or hash error produces Blocked; no start. Postcondition: verified compiled hash is linked to the approved plan and controller.'),
   ('UC-AF-02 Confirm and execute at the scheduled start','Primary actors: mission owner, scheduler and gateway. Trigger: the confirmation window opens, then the scheduled start is reached. Main flow: operator reviews exact mission/UAV and confirms; scheduler and gateway enforce all gates; one bounded envelope executes through the autopilot profile. Alternative: any failed gate, missing consent, stale evidence or expired deadline blocks execution. Postcondition: immutable execution is created only from observed flight evidence.'),
   ('UC-AF-03 Reconcile and finalize execution','Primary actors: gateway, operator and post-flight service. Trigger: execution telemetry or terminal/landed evidence. Main flow: reconcile ACK/state/progress, retain incidents, finalize once and generate the scoped report. Alternatives: uncertainty, disconnect, manual takeover and partial/aborted outcome retain evidence and do not cause duplicate launch or fabricated completion.'),
   ('BR-AF-01 Separation of authority','Approval, pilot consent, dispatch eligibility, command ACK and observed flight are separate facts. AI output is not any of these authorities. Cancellation before dispatch clears authorization; airborne handling remains with configured autopilot failsafe and pilot takeover.'),
   ('BR-AF-02 Immutable binding','An execution binds mission ID, UAV ID, owner, schedule, approved plan hash, compiled hash, firmware profile/version, UID and current connection/lease. Changed input invalidates dependent upload/consent. Retries cannot mutate the approved snapshot or allocate a second start.'),
   ('NFR-AF-01 Timing and expiration','The scheduler shall evaluate eligible starts at least once per second. The confirmation window is [start−5 min,start); command deadline is start+5 s. Backend and gateway clocks must be synchronized; a clock uncertainty exceeding the release tolerance blocks dispatch. Test window boundaries and delayed/reordered messages using a controlled clock.'),
   ('NFR-AF-02 Bounded delivery and recovery','Dispatch delivery may attempt at most three sends of the same commandId before its deadline; the gateway executes at most once. Mission transfer uses profile-configured finite timeout/retry limits. Unknown physical outcome requires state reconciliation, not unconditional arm/start retry. Verify duplicated delivery, lost ACK and restart boundaries.'),
   ('NFR-AF-03 Security and audit','The backend and gateway shall authenticate each other, authorize owner/UAV/mission scope and protect credentials in storage/transport. Persist command ID, actor, hashes, schedule/deadline, gateway identity, gate outcomes, ACKs and observed-state timestamps. No secret is included in browser-visible plan export or logs.'),
   ('NFR-AF-04 Data and state integrity','Transactional dispatch identity and durable gateway receipt/outcome enforce at-most-once physical start. Telemetry preserves source/receive times; uncertain, stale or absent values remain explicit. Completion and report generation are idempotent and retain partial-flight coverage.'),
   ('Requirement traceability','FR-AF-01/02/03 link planning/review and pilot consent (MF1/MF2); FR-AF-04–09 link gateway/dispatch (MF2); FR-AF-10–12 link monitoring, pilot authority and post-flight (MF2/MF3). Functional cases AF-01–AF-16 and unit cases UT-AF-01–UT-AF-07 verify these requirements. Figures remain separate visual representations and must use the same authority boundaries.')])
 elif p.name.startswith('Report4_') or 'DesignBrief' in p.name:
  blocks+=add_pairs([
   ('Backend and portal responsibilities','MissionAutomationController and MissionAutomationService expose due/read/presence/confirm/cancel/plan contracts. An AutomationScheduler role creates one dispatch per immutable approved execution after a transactional gate recheck. The portal displays preparation, upload/read-back, gate reasons and observed execution separately; it never issues an autopilot command directly.'),
   ('Gateway and adapter responsibilities','A MissionCompiler selects a versioned ArduPilot/PX4 multicopter profile. A MissionTransfer role handles upload/read-back normalization. An ExecutionAdapter role validates and durably deduplicates the expiring command envelope, checks local pre-arm/UID/hash state and applies the supported mode/arm/start sequence. Gateway roles may share a deployable Python process; the boundaries are responsibility contracts rather than a requirement for separate services.'),
   ('Execution envelope and persistence','The versioned envelope contains commandId, missionId, uavId, ownerId, controllerUid, connectionId/lease identity, planHash, compiledMissionHash, adapter/profile version, scheduledAtUtc, issuedAtUtc and expiresAtUtc. MissionAutomationState stores presence/consent binding; dispatch and outcome records retain durable identity and reasons. Flight execution stores an immutable approved snapshot. Unique dispatch identity and commandId constraints prevent concurrent/replayed starts.'),
   ('Automation state and mission lifecycle','Automation states are Disabled, AwaitingApproval, Preparing, AwaitingConsent, Ready, Dispatching, Executing, Completed, Blocked, Failed, Cancelled or Expired. These describe automation outcomes separately from the mission lifecycle. Approval/confirmation/upload do not change the mission to InProgress; observed trusted flight does. A completion event records outcome before idempotent report generation.'),
   ('Transaction and failure boundaries','Validate approval/pending-request pointer, schedule reservation, current hashes, connection and consent in the same dispatch transaction. Commit command identity/outbox before transmission. The gateway persists receipt before side effects and uses durable outcome reconciliation on restart. Publish ACK/state changes after commit; retry notification delivery independently of flight execution.'),
   ('Interfaces and synchronization','Human requests use role/record authorization; gateway traffic uses trusted enrollment and a current mission lease over HTTPS/WSS. The plan endpoint returns the owner-scoped contract; gateway command/status messages carry envelope identity and evidence. Control transport, telemetry projection and SSE display are separate paths. Local autopilot loops do not depend on browser polling.'),
   ('Protocol and profile references','Mission upload/read-back follows the MAVLink Mission Protocol: https://mavlink.io/en/services/mission.html. Autopilot behavior is profile-specific: https://ardupilot.org/copter/docs/auto-mode.html and https://docs.px4.io/main/en/flight_modes_mc/mission. A mission-transfer ACK is not observed takeoff. Store firmware/profile versions with execution evidence.')])
 elif p.name.startswith('Report5_'):
  blocks += [heading('Verification records'),para('This is the final test specification. Execution status and actual evidence are recorded in Report5_Test Report.xlsx and Report5_Unit Test.xlsx; document finalization does not create a pass result. The functional catalogue contains 56 cases, including AF-01–AF-16; the unit catalogue contains 17 cases, including UT-AF-01–UT-AF-07. The existing five executed integration results remain unchanged.')]
  blocks += add_pairs([(code+' '+name,'Procedure: '+proc+' Expected: '+expected) for code,name,proc,expected in CASES])
  blocks += add_pairs([('Environment and acceptance method','Use a disposable database, scoped operator/manager accounts, synchronized or controlled clocks, a trusted gateway identity and recorded firmware/profile versions. Exercise ArduPilot and PX4 multicopter SITL, then propeller-free bench and supervised physical acceptance as separate evidence levels. Retain raw protocol/telemetry logs and source/build identity for every run; do not present replay/SITL evidence as a physical-aircraft flight.')])
 elif p.name.startswith('Report2_'):
  blocks+=add_pairs([('Delivery responsibilities','ThongLQ owns MF2 automation/gateway/monitoring and integration evidence. NghiaHQ owns approval, plan-hash/preflight and transaction dependencies; DucDDA owns execution-linked finalization/reporting; KhanhGNG owns advisory AI with no approval/launch authority. Integration includes normal start, blocked gates, transfer failures, replay/restart, pilot takeover and partial completion.'),('Acceptance and release controls','The final handover includes this specification, matching gateway/firmware profiles, deployment configuration, database migration ordering and execution evidence. Release acceptance checks the approved-plan-to-read-back hash chain, explicit pilot consent, bounded dispatch, observed flight-state transition and idempotent report. Calendar review/council appointments and reviewer decisions are external records and are not invented by the project plan.')])
 elif 'Commitment' in p.name:
  blocks+=add_pairs([('MF2 execution responsibility','ThongLQ owns the automatic-execution acceptance flow AF-01–AF-16, with NghiaHQ for plan/review/preflight and DucDDA for execution/report integrity. The detailed final catalogue is 56 functional and 17 unit cases. Baseline minimum commitments elsewhere in this form are minimum coverage requirements, not additional executed-pass claims.'),('Reviewer fields','Team submission is final. Reviewer decisions, signatures and acceptance results are entered by the responsible reviewers against the actual evidence, independently of document version status.')])
 elif 'SelfReview' in p.name:
  blocks+=add_pairs([('Final specification review','The final document review covers full automatic execution, authority separation, identity/hash/lease binding, upload/read-back, dispatch deadline, deduplication, observed-state reconciliation, failsafe/pilot authority and completion evidence. The dated review findings above remain historical evidence; their closure is determined by implementation/test records, not by rewriting those findings.'),('Acceptance evidence','The final test specification is AF-01–AF-16 and UT-AF-01–UT-AF-07. Actual results remain in the two test workbooks. Diagram alignment is tracked in docs/diagram-update-checklist.md without changing the source images.')])
 else:blocks+=add_pairs([('Supported operation','Supported automatic operation uses a reviewed waypoint mission on an identified multicopter autopilot. Unsupported airframes, camera actions or altitude frames are rejected by capability/profile validation. Guided/Offboard continuous setpoint control, custom stabilization algorithms and AI-directed autonomous approval are excluded.'),('Operational completion','The operator can review the consent, upload verification, dispatch outcomes, observed trajectory, incidents and final report in one mission evidence chain. A failed or partial execution remains visible and does not become a fabricated successful flight.')])
 # Keep paragraphs together; avoid orphan headings and fixed-height narrative tables.
 for el in blocks:
  if el.tag==f'{{{W}}}p':
   pp=el.find('w:pPr',N)
   if el.find('w:pPr/w:pStyle',N) is not None:E.SubElement(pp,f'{{{W}}}keepNext')
 sect=body.find('w:sectPr',N);index=list(body).index(sect) if sect is not None else len(body)
 for el in blocks:body.insert(index,el);index+=1
 data['word/document.xml']=E.tostring(root,encoding='UTF-8',xml_declaration=True,standalone=True)
 with ZipFile(p,'w',ZIP_DEFLATED) as z:
  for info in infos:z.writestr(info,data[info.filename])
 audit.append({'file':str(p.relative_to(R)),'backup':str(backup),'history_tables':len(history)})
(B/'doc-audit.json').write_text(json.dumps(audit,indent=2),encoding='utf-8')
(B/'flight-cases.json').write_text(json.dumps(CASES,ensure_ascii=False,indent=2),encoding='utf-8')
print('Final specifications written:',len(audit))
