from pathlib import Path
from zipfile import ZipFile,ZIP_DEFLATED
from copy import deepcopy
from lxml import etree as E
import openpyxl,json,re
B=Path('D:/DroneOps-Intelligence-/tmp/final-submission');NS={'s':'http://schemas.openxmlformats.org/spreadsheetml/2006/main'};S=NS['s']
for plan in json.loads((B/'sheet-plan.json').read_text(encoding='utf-8')):
 p=Path(plan['file']);auth=B/(plan['kind']+'-authored.xlsx')
 src=openpyxl.load_workbook(p);a=openpyxl.load_workbook(auth);cached=openpyxl.load_workbook(auth,data_only=True)
 with ZipFile(p) as z:infos=z.infolist();data={i.filename:z.read(i.filename) for i in infos}
 changed={}
 for si,sh in enumerate(src,1):
  changes=[c for c in plan['changes'] if c['sheet']==sh.title]
  if not changes:continue
  key=f'xl/worksheets/sheet{si}.xml';root=E.fromstring(data[key]);sd=root.find('s:sheetData',NS)
  for change in changes:
   addr=change['cell'];rn=int(re.search(r'\d+',addr)[0]);row=sd.find(f's:row[@r="{rn}"]',NS)
   if row is None:
    row=E.Element(f'{{{S}}}row',r=str(rn));sd.append(row)
   cell=row.find(f's:c[@r="{addr}"]',NS)
   if cell is None:
    cell=E.Element(f'{{{S}}}c',r=addr)
    # Reuse original template styles; no global style/visual replacement.
    model=(sh.cell(19,openpyxl.utils.column_index_from_string(re.sub(r'\d','',addr))) if plan['kind']=='tests' else sh.cell(12,openpyxl.utils.column_index_from_string(re.sub(r'\d','',addr))))
    if model.has_style:cell.set('s',str(model.style_id))
    row.append(cell)
   for child in list(cell):cell.remove(child)
   v=a[sh.title][addr].value
   if 'formula' in change:
    cell.pop('t',None) if False else None
    if 't' in cell.attrib:del cell.attrib['t']
    E.SubElement(cell,f'{{{S}}}f').text=v[1:]
    cv=cached[sh.title][addr].value
    if cv is not None:E.SubElement(cell,f'{{{S}}}v').text=str(cv)
   elif v is None:
    cell.attrib.pop('t',None)
   elif isinstance(v,str):
    cell.set('t','inlineStr');E.SubElement(E.SubElement(cell,f'{{{S}}}is'),f'{{{S}}}t').text=v
   else:
    cell.attrib.pop('t',None);E.SubElement(cell,f'{{{S}}}v').text=str(v)
  # Transplant only directly affected formula cache values.
  for row in sd:
   for c in row:
    addr=c.get('r')
    if c.find('s:f',NS) is not None:
     cv=cached[sh.title][addr].value
     if isinstance(cv,(int,float)):
      ve=c.find('s:v',NS)
      if ve is None:ve=E.SubElement(c,f'{{{S}}}v')
      ve.text=str(cv)
  for rn in ([38,40] if sh.title=='Project Schedule' else [18,19] if sh.title=='Plan Notes' else list(range(20,36)) if sh.title=='Telemetry' else []):
   row=sd.find(f's:row[@r="{rn}"]',NS)
   row.set('ht',str(a[sh.title].row_dimensions[rn].height or 100));row.set('customHeight','1')
  # Cell order must be numeric by column; rows numeric by row.
  for row in sd:row[:]=sorted(list(row),key=lambda c:openpyxl.utils.column_index_from_string(re.sub(r'\d','',c.get('r'))))
  sd[:]=sorted(list(sd),key=lambda r:int(r.get('r')))
  dim=root.find('s:dimension',NS)
  if dim is not None:dim.set('ref',a[sh.title].calculate_dimension())
  data[key]=E.tostring(root,encoding='UTF-8',xml_declaration=True,standalone=True);changed[key]=True
 # Recalculate affected summary formula caches without touching formatting or drawings.
 if plan['kind']=='tests':
  key='xl/worksheets/sheet3.xml';root=E.fromstring(data[key])
  for c in root.findall('.//s:c',NS):
   if c.find('s:f',NS) is not None:
    v=cached['Test Statistics'][c.get('r')].value
    if isinstance(v,(float,int)):
     ve=c.find('s:v',NS)
     if ve is None:ve=E.SubElement(c,f'{{{S}}}v')
     ve.text=str(v)
  data[key]=E.tostring(root,encoding='UTF-8',xml_declaration=True,standalone=True);changed[key]=True
 with ZipFile(p,'w',ZIP_DEFLATED) as z:
  for info in infos:z.writestr(info,data[info.filename])
 orig=B/'originals/docs/reports'/p.name
 with ZipFile(orig) as z0,ZipFile(p) as z1:
  assert all(z0.read(n)==z1.read(n) for n in z0.namelist() if n not in changed)
 final=openpyxl.load_workbook(p)
 effective={(c['sheet'],c['cell']):c for c in plan['changes']}
 assert all(final[c['sheet']][c['cell']].value==(c.get('formula') if 'formula' in c else c.get('value')) for c in effective.values())
 print(plan['kind'],'saved; original drawings/styles/history preserved; changed parts',list(changed))
