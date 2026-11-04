from pathlib import Path
import shutil,json,time
import win32com.client
ROOT=Path('D:/DroneOps-Intelligence-');B=ROOT/'tmp/doc-refresh'
p=ROOT/'docs/reports/Report5_Unit Test.xls'
if not (B/'originals'/p.name).exists():shutil.copy2(p,B/'originals'/p.name)
working=B/'unit-tests-working.xls';shutil.copy2(B/'originals'/p.name,working)
cases=[
 ('FlightWindow','FN-11','InConfirmationWindow','UT-AF-01','B','T-5 minus one tick; T-5; start minus one tick; start','Only [T-5 minutes, start) is open using backend time.'),
 ('ReadinessBinding','FN-12','ReadAsync / ConfirmAsync','UT-AF-02','A','Change plan hash or connection after confirmation; expire presence or preflight','Readiness is false for mismatched/stale evidence; confirmation is scoped to owner/UAV/UID.'),
 ('NoDispatch','FN-13','ConfirmAsync / PlanAsync','UT-AF-03','N','Approved opted-in mission; fresh scoped connection, presence and passing preflight','dispatchEnabled=false; no aircraft command or readiness-driven InProgress transition.')]
app=win32com.client.DispatchEx('Excel.Application');app.Visible=False;app.DisplayAlerts=False;app.AutomationSecurity=3;app.EnableEvents=False
try:
 w=app.Workbooks.Open(str(working),UpdateLinks=0)
 for i,(name,code,method,case,kind,inputs,expected) in enumerate(cases):
  w.Worksheets('ReviewReject').Copy(After=w.Worksheets(w.Worksheets.Count))
  s=w.Worksheets(w.Worksheets.Count);s.Name=name
  for addr,value in {'C2':code,'L2':method,'C3':'ThongLQ','C5':'FR-AF-01–04: automatic flight preparation; dispatch disabled','F9':case,'D11':'Isolated mission automation fixture; controlled backend clock; no aircraft','D15':inputs,'D19':'Window, identity and freshness conditions from the current implementation','D23':'MF2 owner ThongLQ; MF1 support NghiaHQ; AI MF4 owner KhanhGNG','D32':expected,'F39':kind,'F40':'Untested','A7':0,'C7':0,'F7':1,'L7':1 if kind=='N' else 0,'M7':1 if kind=='A' else 0,'N7':1 if kind=='B' else 0,'O7':1}.items():s.Range(addr).Value=value
  for addr in ['D11','D15','D19','D23','D32']:s.Range(addr).WrapText=True
  for row in [11,15,19,23,32]:s.Rows(row).RowHeight=45
  # Reuse original matrix, record no execution or pass result.
  time.sleep(0.2)
  f=w.Worksheets('Functions');f.Rows(20).Copy(f.Rows(21+i))
  vals=[11+i,'FR-AF-01–04','MissionAutomationService',method,code,name,'Flight readiness: '+expected,'Controlled fixture; Untested; owner ThongLQ']
  f.Range(f'A{21+i}:H{21+i}').Value=(tuple(vals),);f.Rows(21+i).RowHeight=60;f.Range(f'A{21+i}:H{21+i}').WrapText=True
  app.CutCopyMode=False
  time.sleep(0.2)
 st=w.Worksheets('Statistics');st.Rows('22:24').Insert()
 for i,(_,code,_,_,kind,_,_) in enumerate(cases):
  row=22+i;st.Rows(21).Copy(st.Rows(row));st.Range(f'A{row}:I{row}').Value=((11+i,code,0,0,1,int(kind=='N'),int(kind=='A'),int(kind=='B'),1),)
 for col in 'CDEFGHI':st.Range(f'{col}26').Formula=f'=SUM({col}12:{col}24)'
 st.Cells.Find(What='Ten planned unit cases across ten functions; all Untested. Example sheet excluded.',LookAt=1).Value='Thirteen planned unit cases across thirteen functions; all Untested. Includes three readiness cases; no aircraft execution. Example sheet excluded.'
 app.CalculateFull()
 assert st.Range('E26').Value==13 and st.Range('I26').Value==13
 w.Save()
 w.Close(False)
 shutil.copy2(working,p)
 print('Saved legacy XLS; 13 Untested, 0 Passed; Cover/history untouched.')
finally:app.Quit()
