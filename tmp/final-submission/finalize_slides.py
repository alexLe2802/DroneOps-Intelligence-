from pathlib import Path
from zipfile import ZipFile,ZIP_DEFLATED
from lxml import etree as E
import shutil
R=Path('D:/DroneOps-Intelligence-');B=R/'tmp/final-submission';p=R/'docs/slides/DroneOps_Review2.pptx'
backup=B/'originals/docs/slides'/p.name;backup.parent.mkdir(parents=True,exist_ok=True)
if not backup.exists():shutil.copy2(p,backup)
with ZipFile(backup) as z:infos=z.infolist();d={i.filename:z.read(i.filename) for i in infos}
A='http://schemas.openxmlformats.org/drawingml/2006/main';P='http://schemas.openxmlformats.org/presentationml/2006/main'
mapping={
 'Review 2 · 19 Oct 2026':'Final submission · 04 Nov 2026',
 'Review 2 project capstone':'Final project submission',
 'PROJECT CONTEXT · REVIEW 2':'PROJECT CONTEXT · FINAL SPECIFICATION',
 'Changes since Review 1':'Final scope',
 'Change marketing drone to lab drone':'Reviewed planning, scheduled execution, telemetry and post-flight evidence',
 'Open scope item: registered Top-k similarity evidence versus current provider-only integration.':'Assessment provenance binds source evidence, captured context and provider output.',
 'Target actions still require API and FE integration.':'Actions enforce role, record scope and append-only incident evidence.',
 '40 integration/system cases + 10 unit designs; selected regression run':'56 functional cases + 17 unit cases; results retain execution evidence',
 'Decisions and next actions for Review 2':'Final acceptance and handover checks',
 'Complete gateway, monitoring and incident integration.':'Verify scheduled execution, gateway, monitoring and incident evidence.',
 'Workflow demonstration sequence':'Final workflow demonstration sequence',
 '1. Create a mission and submit it for pre-flight validation.':'1. Create an opted-in mission and submit it for pre-flight validation.',
 '4. Monitor telemetry and inspect incident evidence.':'4. Confirm consent, execute at schedule and monitor telemetry/incidents.',
 'Readiness is implemented; aircraft dispatch is disabled':'Approved missions execute through verified scheduled dispatch',
 'AUTOMATIC FLIGHT PREPARATION':'AUTOMATIC FLIGHT EXECUTION',
 'Opt in and approve':'Review the plan',
 'AutomaticFlightRequested defaults off; changes require plan review.':'Approved opt-in, eligible pilot/UAV and immutable plan hash.',
 'Confirm in the window':'Compile and verify',
 'Backend time: T−5 minutes up to start; visible, online authenticated tab.':'Profile-aware mission upload and normalized read-back verification.',
 'Check fresh evidence':'Confirm pilot consent',
 'Presence and heartbeat ≤30 s; telemetry ≤15 s; passing preflight ≤1 min.':'T−5 minutes to start; fresh presence, telemetry and passing preflight.',
 'Bind the confirmation':'Dispatch at schedule',
 'Exact plan hash, mission connection and bound UAV/controller UID.':'All gates rechecked; expiring commandId bound to UID, lease and hashes.',
 'Keep execution separate':'Observe and finalize',
 'dispatchEnabled=false; no upload, ARM, TAKEOFF or AUTO/MISSION_START.':'ACK plus observed flight; one trusted execution and completion report.',
 'Next phase: firmware adapters, upload/read-back, command safeguards, SITL and supervised validation.':'ArduPilot Copter / PX4 multicopter profiles; onboard failsafe and pilot RC authority.',
 'Realtime monitoring, incidents and flight preparation':'Scheduled execution, realtime monitoring and incidents',
}
changed=[]
for name,data in list(d.items()):
 if not name.startswith('ppt/slides/slide') or not name.endswith('.xml'):continue
 root=E.fromstring(data);touched=False
 for pa in root.findall('.//{%s}p'%A):
  if any(E.QName(a).localname=='grpSp' for a in pa.iterancestors()):continue
  ts=pa.findall('.//{%s}t'%A);old=''.join(t.text or '' for t in ts);new=old
  for a,b in mapping.items():new=new.replace(a,b)
  if old!=new and ts:
   ts[0].text=new
   for t in ts[1:]:t.text=''
   touched=True
 if touched:
  d[name]=E.tostring(root,encoding='UTF-8',xml_declaration=True,standalone=True);changed.append(name)
with ZipFile(p,'w',ZIP_DEFLATED) as z:
 for i in infos:z.writestr(i,d[i.filename])
with ZipFile(backup) as a,ZipFile(p) as b:
 for n in a.namelist():
  if '/media/' in n or '/diagrams/' in n:assert a.read(n)==b.read(n)
  if n.startswith('ppt/slides/slide') and n.endswith('.xml'):
   aa=E.fromstring(a.read(n));bb=E.fromstring(b.read(n))
   assert [E.tostring(x) for x in aa.findall('.//{%s}pic'%P)]==[E.tostring(x) for x in bb.findall('.//{%s}pic'%P)]
print('Final slide text updated:',len(changed),'parts; images and diagrams preserved.')
