from pathlib import Path
from zipfile import ZipFile,ZIP_DEFLATED
from lxml import etree as E
from copy import deepcopy
H={};exec(Path('tmp/doc-refresh/edit_docs.py').read_text(encoding='utf-8').split('files=list(')[0],H)
W=H['W'];N=H['NS'];txt=H['text'];rep=H['replace'];para=H['paragraph']
def setcell(cell,value):
 ps=cell.findall('w:p',N)
 if ps:
  p=ps[0];ts=p.xpath('.//w:t',namespaces=N)
  if ts:
   ts[0].text=value
   for t in ts[1:]:t.text=''
  else:p.append(para(value).find('w:r',N))
  for p in ps[1:]:cell.remove(p)
 else:cell.append(para(value))
def appendrow(table,values):
 row=deepcopy(table.findall('w:tr',N)[-1])
 for c,v in zip(row.findall('w:tc',N),values):setcell(c,v)
 for tag in ['trHeight','tblHeader']:
  for x in row.findall('.//w:'+tag,N):x.getparent().remove(x)
 table.append(row)
entities=[
 ('MissionUpdateRequest','Mission review','Proposed plan and AutomaticFlightRequested payload; pending/decision scope and base/proposed hashes.'),
 ('MissionValidationRun','Mission review','Timestamped validation/preflight evidence, stage, current plan hash, result and source revisions.'),
 ('DroneConnection','Gateway authorization','Mission/UAV/owner/UID binding, lease expiry, revocation and latest heartbeat/sample times.'),
 ('TrustedGateway enrollment','Gateway authorization','Revocable gateway credential binding to UAV/controller UID; credentials are not browser plan data.'),
 ('MissionAutomationState','Flight automation','Consent actor/time, presence, planHash and connection binding, automation readiness/outcome.'),
 ('Dispatch command and outcome','Flight automation','Durable commandId, execution binding, compiled hash/profile, not-before/deadline, delivery and observed outcome; deduplicated.'),
 ('Flight execution evidence','Flight execution','Immutable approved snapshot, trusted start/landed/terminal evidence, partial outcome and post-flight linkage.')]
for p in [*list(Path('docs/reports').glob('*.docx')),*list(Path('.').glob('*.docx'))]:
 if 'RecordOfChanges' in p.name:continue
 with ZipFile(p) as z:infos=z.infolist();data={i.filename:z.read(i.filename) for i in infos}
 r=E.fromstring(data['word/document.xml']);body=r.find('w:body',N)
 for t in r.findall('.//w:tbl',N):
  rows=t.findall('w:tr',N)
  if not rows:continue
  head=txt(rows[0])
  if 'Change Description' in head:continue
  if (p.name.startswith('Report3_') or p.name.startswith('Report4_')) and 'Entity' in head and 'Domain' in head and 'Description' in head:
   known={txt(row.findall('w:tc',N)[1]) for row in rows[1:]}
   seq=len(rows)
   for name,domain,description in entities:
    if name not in known:appendrow(t,[str(seq),name,domain,description]);seq+=1
  if p.name.startswith('Report3_') and head=='IDUse CaseActorsOutcome':
   for vals in [
    ['UC-AF-01','Prepare and verify automatic mission','Operator; trusted gateway','Compiled approved mission uploaded and normalized read-back verified, or blocked with reason.'],
    ['UC-AF-02','Confirm and execute at schedule','Operator; scheduler; trusted gateway','One eligible expiring dispatch; observed flight creates one immutable execution.'],
    ['UC-AF-03','Reconcile and finalize execution','Gateway; Operator; post-flight service','Traceable outcome and one report; partial/aborted coverage preserved.']]:appendrow(t,vals)
  if p.name.startswith('Report3_') and 'Function' in head and 'Description' in head and 'Screen' not in head and rows[1:] and 'NSF-' in txt(rows[1]):
   for vals in [
    ['NSF-AF-01','FE-02/05','Mission compiler and transfer','Compile approved plan by supported profile, upload and verify normalized read-back.'],
    ['NSF-AF-02','FE-03/06','Scheduled dispatch','Atomically enforce all gates and issue a unique bounded command envelope.'],
    ['NSF-AF-03','FE-05/06','Gateway execution and reconciliation','Deduplicate, validate local checks, apply profile-specific execution and observe actual state.'],
    ['NSF-AF-04','FE-08/10','Execution finalization','Finalize trusted outcome and report idempotently; retain partial coverage.']]:appendrow(t,vals)
 # Remove superseded broad exclusions in normal requirements, while preserving historical review evidence.
 if 'SelfReview' not in p.name:
  for pa in body.findall('w:p',N):
   rep(pa,'This is telemetry monitoring, not flight-control command.','Telemetry monitoring is separate from the approved automatic-execution command workflow.')
   rep(pa,'No model training or low-level autopilot algorithm development is planned.','Custom model training and low-level autopilot algorithm development are excluded.')
 data['word/document.xml']=E.tostring(r,encoding='UTF-8',xml_declaration=True,standalone=True)
 with ZipFile(p,'w',ZIP_DEFLATED) as z:
  for i in infos:z.writestr(i,data[i.filename])
print('Final use-case, non-screen service and entity catalogues completed.')
