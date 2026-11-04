from pathlib import Path
import win32com.client,json
R=Path('D:/DroneOps-Intelligence-');B=R/'tmp/doc-refresh'
a=win32com.client.DispatchEx('Word.Application');a.Visible=False;a.DisplayAlerts=0
try:
 for name in ['SWP490_FA26SE238_R2_Commitment','SWP490_FA26SE238_R2_SelfReview']:
  d=a.Documents.Open(str(R/'docs/reports'/f'{name}.docx'),ReadOnly=True,AddToRecentFiles=False,Visible=False)
  d.ExportAsFixedFormat(str(B/'word-qa'/name/'render.pdf'),17,OpenAfterExport=False)
  print(name,d.ComputeStatistics(2),'pages',flush=True);d.Close(False)
finally:a.Quit()
a=win32com.client.DispatchEx('Excel.Application');a.Visible=False;a.DisplayAlerts=False
try:
 for name in ['FA26SE238_Project_Schedule.xlsx','Report5_Test Report.xlsx','Report5_Unit Test.xlsx']:
  w=a.Workbooks.Open(str(R/'docs/reports'/name),ReadOnly=True,UpdateLinks=0)
  a.CalculateFull()
  if name.startswith('FA26'):
   assert w.Worksheets('Responsibility Matrix').Range('B7').Value=='D'
   assert w.Worksheets('Responsibility Matrix').Range('D9').Value=='D'
  elif name=='Report5_Test Report.xlsx':
   assert w.Worksheets('Test Statistics').Range('D18').Value==5
   assert w.Worksheets('Test Statistics').Range('F18').Value==43
   assert w.Worksheets('Test Statistics').Range('H18').Value==48
  else:
   assert w.Worksheets('Statistics').Range('I26').Value==13
   assert w.Worksheets('Statistics').Range('E26').Value==13
   assert w.Worksheets('Statistics').Range('C26').Value==0
  print(name,'native Excel checks passed',flush=True);w.Close(False)
finally:a.Quit()
a=win32com.client.DispatchEx('PowerPoint.Application')
try:
 p=a.Presentations.Open(str(R/'docs/slides/DroneOps_Review2.pptx'),ReadOnly=True,Untitled=False,WithWindow=False)
 assert p.Slides.Count==22
 for i in [20,21,22]:p.Slides(i).Export(str(B/f'final-slide-{i}.png'),'PNG',1600,900)
 print('PPTX native open passed; 22 slides',flush=True);p.Close()
finally:a.Quit()
