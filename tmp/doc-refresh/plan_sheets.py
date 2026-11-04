from pathlib import Path
import openpyxl,json,ast
ROOT=Path('D:/DroneOps-Intelligence-');B=ROOT/'tmp/doc-refresh'
def swap(s):return s.replace('ThongLQ','__X__').replace('KhanhGNG','ThongLQ').replace('__X__','KhanhGNG')
plans=[]
p=ROOT/'docs/reports/FA26SE238_Project_Schedule.xlsx';w=openpyxl.load_workbook(p)
changes=[]
for row in [10,12,23,25,36,37,38,39,40,44,49]:
 for col in ['H','I']:
  old=w['Project Schedule'][f'{col}{row}'].value
  if isinstance(old,str):changes.append({'sheet':'Project Schedule','cell':f'{col}{row}','value':swap(old)})
for row in [7,9]:
 for col in ['B','D']:changes.append({'sheet':'Responsibility Matrix','cell':f'{col}{row}','value':'D' if (row==7 and col=='B') or (row==9 and col=='D') else 'S'})
changes += [
 {'sheet':'Plan Notes','cell':'B9','value':'Current ownership: MF1 NghiaHQ; MF2 ThongLQ; MF3 DucDDA; MF4 KhanhGNG. Each flow owner coordinates requirements, design, implementation and tests. ThongLQ retains team leadership, frontend/CI and dossier coordination.'},
 {'sheet':'Project Schedule','cell':'I38','value':swap(w['Project Schedule']['I38'].value)+' Readiness scope: optional AutomaticFlightRequested; reviewed plan; T-5 minute confirmation; scoped fresh presence/telemetry/preflight; dispatchEnabled=false. No aircraft command execution.'},
 {'sheet':'Project Schedule','cell':'I40','value':swap(w['Project Schedule']['I40'].value)+' Include AF-01 through AF-08 readiness tests; statuses remain Pending until executed.'},
 {'sheet':'Plan Notes','cell':'A18','value':'Automatic flight preparation'},
 {'sheet':'Plan Notes','cell':'B18','value':'Implemented readiness only: approved opt-in; backend window [T-5 min,start); visible online authenticated operator; presence/heartbeat within 30 s; telemetry within 15 s; passing preflight for current plan hash within 1 min; confirmation bound to connection. Owner ThongLQ with NghiaHQ support.'},
 {'sheet':'Plan Notes','cell':'A19','value':'Aircraft execution roadmap'},
 {'sheet':'Plan Notes','cell':'B19','value':'Not implemented: mission upload/read-back, ARM, TAKEOFF/AUTO/MISSION_START, command replay protection and scheduled dispatch. dispatchEnabled=false. Schedule/effort for that phase remains to be agreed after SITL and bench validation; existing dates are not proof of readiness to fly.'}]
plans.append({'file':str(p),'changes':changes,'kind':'schedule'})
tree=ast.parse((B/'edit_docs.py').read_text(encoding='utf-8'))
cases=next(ast.literal_eval(n.value) for n in tree.body if isinstance(n,ast.Assign) and any(isinstance(t,ast.Name) and t.id=='TESTS' for t in n.targets))
p=ROOT/'docs/reports/Report5_Test Report.xlsx';changes=[]
for i,(title,procedure) in enumerate(cases,20):
 code,desc=title.split(' ',1)
 vals=[code,desc,procedure,'Satisfy the acceptance criteria in FR-AF-01 through FR-AF-04; dispatchEnabled=false; no aircraft command.','Approved opted-in scheduled mission; controlled backend clock; scoped owner/UAV/UID/connection; mock telemetry/preflight.','Pending',None,None,'Pending',None,None,'Pending',None,None,'MF2 owner ThongLQ; NghiaHQ supports preflight; planned, not executed.']
 for j,v in enumerate(vals,1):changes.append({'sheet':'Telemetry','cell':f'{openpyxl.utils.get_column_letter(j)}{i}','value':v})
changes += [{'sheet':'Telemetry','cell':'B3','value':'UC-13–14 / MF2-CR-01; FR-AF-01–04 / readiness; owner ThongLQ'}, {'sheet':'Test Cases','cell':'E10','value':'Telemetry and AF-01–AF-08 flight-preparation cases; owner ThongLQ; new cases Pending.'}]
for cell in ['B4','B6','C6','D6','E6','B7','C7','D7','E7','B8','C8','D8','E8']:
 old=openpyxl.load_workbook(p)['Telemetry'][cell].value
 changes.append({'sheet':'Telemetry','cell':cell,'formula':old.replace('19','27')})
plans.append({'file':str(p),'changes':changes,'kind':'tests'})
B.joinpath('sheet-plan.json').write_text(json.dumps(plans,ensure_ascii=False,indent=2),encoding='utf-8')
print('Planned cells',[(x['kind'],len(x['changes'])) for x in plans])
