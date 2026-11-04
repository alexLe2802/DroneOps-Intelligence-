from pathlib import Path
import win32com.client,json
ROOT=Path('D:/DroneOps-Intelligence-');BUILD=ROOT/'tmp/doc-refresh'
word=win32com.client.DispatchEx('Word.Application');word.Visible=False;word.DisplayAlerts=0
results=[]
try:
    files=list((ROOT/'docs/reports').glob('*.docx'))+list(ROOT.glob('*.docx'))
    for p in files:
        if 'RecordOfChanges' in p.name:continue
        target=BUILD/'word-qa'/p.stem;target.mkdir(parents=True,exist_ok=True)
        d=word.Documents.Open(str(p),ReadOnly=True,AddToRecentFiles=False,Visible=False)
        d.ExportAsFixedFormat(str(target/'render.pdf'),17,OpenAfterExport=False)
        results.append({'file':str(p),'pages':d.ComputeStatistics(2),'pdf':str(target/'render.pdf')})
        d.Close(False)
        print(p.name,results[-1]['pages'],flush=True)
finally:word.Quit()
BUILD.joinpath('word-qa.json').write_text(json.dumps(results,indent=2),encoding='utf-8')
excel=win32com.client.DispatchEx('Excel.Application');excel.Visible=False;excel.DisplayAlerts=False
try:
    p=ROOT/'docs/reports/Report5_Unit Test.xls';wb=excel.Workbooks.Open(str(p),ReadOnly=True,UpdateLinks=0)
    details=[]
    for s in wb.Worksheets:
        rows=s.UsedRange.Value
        details.append({'sheet':s.Name,'rows':len(rows),'cols':len(rows[0]),'values':rows})
        print('XLS',s.Name,len(rows),len(rows[0]),flush=True)
    BUILD.joinpath('xls-extracted.json').write_text(json.dumps(details,ensure_ascii=False,indent=2,default=str),encoding='utf-8')
    wb.Close(False)
finally:excel.Quit()
