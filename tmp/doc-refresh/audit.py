from pathlib import Path
from zipfile import ZipFile
from lxml import etree as E
import openpyxl,json,re,subprocess
R=Path('D:/DroneOps-Intelligence-');B=R/'tmp/doc-refresh';N={'w':'http://schemas.openxmlformats.org/wordprocessingml/2006/main'}
checks=[]
for item in json.loads((B/'doc_audit.json').read_text()):
 p=R/item['file'];old=B/'originals'/p.relative_to(R)
 with ZipFile(old) as za,ZipFile(p) as zb:
  assert all(za.read(n)==zb.read(n) for n in za.namelist() if n!='word/document.xml'),p
  a=E.fromstring(za.read('word/document.xml'));b=E.fromstring(zb.read('word/document.xml'))
  for kind in ['drawing','pict','ins','del']:
   assert [E.tostring(x) for x in a.findall('.//w:'+kind,N)]==[E.tostring(x) for x in b.findall('.//w:'+kind,N)],(p,kind)
  oldhist=[t for t in a.findall('.//w:tbl',N) if 'Change Description' in ''.join(t.xpath('.//w:t/text()',namespaces=N))[:130]]
  newhist=[t for t in b.findall('.//w:tbl',N) if 'Change Description' in ''.join(t.xpath('.//w:t/text()',namespaces=N))[:130]]
  assert [E.tostring(t) for t in oldhist]==[E.tostring(t) for t in newhist],p
  txt=' '.join(b.xpath('//w:t/text()',namespaces=N))
  assert 'dispatchEnabled is false' in txt
 checks.append(p.name+': diagrams, media, history and tracked-change markup preserved')
old=subprocess.check_output(['git','-c','safe.directory=D:/DroneOps-Intelligence-','show','HEAD:docs/SWP490_FA26SE238_R2_RecordOfChanges.docx'])
assert old==(R/'docs/reports/SWP490_FA26SE238_R2_RecordOfChanges.docx').read_bytes()
with ZipFile(B/'originals/DroneOps_Review2.pptx') as za,ZipFile(R/'docs/slides/DroneOps_Review2.pptx') as zb:
 changed={'ppt/presentation.xml','ppt/_rels/presentation.xml.rels','[Content_Types].xml','ppt/slides/slide20.xml'}
 assert all(za.read(n)==zb.read(n) for n in za.namelist() if n not in changed)
 for n in [n for n in za.namelist() if '/media/' in n or '/diagrams/' in n]:assert za.read(n)==zb.read(n)
before=openpyxl.load_workbook(B/'unit-modern-source.xlsx');after=openpyxl.load_workbook(R/'docs/reports/Report5_Unit Test.xlsx')
for s in before:
 if s.title in ['Functions','Statistics']:continue
 for row in s:
  for c in row:assert (c.value or None)==(after[s.title][c.coordinate].value or None),(s.title,c.coordinate,c.value,after[s.title][c.coordinate].value)
assert len(after.worksheets)==len(before.worksheets)+1
assert [after['FlightPreparation'][f'G{r}'].value for r in [5,6,7]]==['Untested']*3
for p in [R/'README.md',*list((R/'docs').glob('*.md'))]:
 for link in re.findall(r'\]\(([^)]+)\)',p.read_text(encoding='utf-8')):
  if '://' not in link:assert (p.parent/link.split('#')[0]).exists(),(p,link)
print('\n'.join(checks))
print('RecordOfChanges byte-identical; slide media/diagrams unchanged; legacy test sheets preserved; local links valid.')
