from pathlib import Path
from zipfile import ZipFile,ZIP_DEFLATED
from lxml import etree as E
scope={};exec(Path('tmp/doc-refresh/edit_docs.py').read_text(encoding='utf-8').split('files=list(')[0],scope)
NS=scope['NS'];W=scope['W'];rep=scope['replace'];txt=scope['text']
for p in list(Path('docs/reports').glob('*.docx'))+list(Path('.').glob('*.docx')):
 if 'RecordOfChanges' in p.name:continue
 with ZipFile(p) as z:infos=z.infolist();data={i.filename:z.read(i.filename) for i in infos}
 root=E.fromstring(data['word/document.xml']);body=root.find('w:body',NS)
 if p.name.startswith('Report3_'):
  ps=body.findall('w:p',NS)
  for i,pa in enumerate(ps):
   if txt(pa)=='Traceability' and i>len(ps)-10:
    body.remove(pa);body.remove(ps[i+1])
  for t in root.findall('.//w:tbl',NS):
   rows=t.findall('w:tr',NS)
   if not rows or 'Detailed FRs' not in txt(rows[0]):continue
   for row in rows[1:]:
    cells=row.findall('w:tc',NS);feature=txt(cells[0])
    extra={'FE-02':'; FR-AF-01/04','FE-03':'; FR-AF-01','FE-05':'; FR-AF-03/04','FE-06':'; FR-AF-02/03'}.get(feature)
    if extra and extra not in txt(cells[2]):
     ts=cells[2].xpath('.//w:t',namespaces=NS);ts[-1].text=(ts[-1].text or '')+extra
 if p.name.startswith('Report4_') or 'DesignBrief' in p.name:
  for pa in body.findall('w:p',NS):
   if txt(pa).startswith('Existing images and diagrams are intentionally unchanged.'):
    rep(pa,txt(pa),'Existing diagrams are unchanged. Update architecture, ERD, mission state and planning/gateway sequences for enrollment and readiness. Confirmation must not transition the mission to InProgress.')
 if 'Report5_Test Documentation' in p.name:
  rep(root,'Report5_Unit Test.xls','Report5_Unit Test.__NEW__')
  rep(root,'Report5_Unit Test.__NEW__','Report5_Unit Test.xlsx')
 if 'SelfReview' in p.name:
  # The original findings table exceeds the printable page width; keep every finding and wrap it.
  sec=body.find('w:sectPr',NS);pg=sec.find('w:pgSz',NS);m=sec.find('w:pgMar',NS)
  available=int(pg.get(f'{{{W}}}w'))-int(m.get(f'{{{W}}}left'))-int(m.get(f'{{{W}}}right'))
  for t in root.findall('.//w:tbl',NS):
   rows=t.findall('w:tr',NS)
   if not rows or 'Team response' not in txt(rows[0]):continue
   ratios=[.06,.10,.13,.24,.07,.21,.19];widths=[int(available*r) for r in ratios]
   grid=t.find('w:tblGrid',NS)
   if grid is not None:
    for c,width in zip(grid,widths):c.set(f'{{{W}}}w',str(width))
   pr=t.find('w:tblPr',NS)
   tw=pr.find('w:tblW',NS)
   if tw is None:tw=E.SubElement(pr,f'{{{W}}}tblW')
   tw.set(f'{{{W}}}w',str(available));tw.set(f'{{{W}}}type','dxa')
   for row in rows:
    for c,width in zip(row.findall('w:tc',NS),widths):
     cp=c.find('w:tcPr',NS);cw=cp.find('w:tcW',NS)
     if cw is None:cw=E.SubElement(cp,f'{{{W}}}tcW')
     cw.set(f'{{{W}}}w',str(width));cw.set(f'{{{W}}}type','dxa')
    for rr in row.findall('.//w:r',NS):
     rp=rr.find('w:rPr',NS)
     if rp is None:rp=E.SubElement(rr,f'{{{W}}}rPr')
     sz=rp.find('w:sz',NS)
     if sz is None:sz=E.SubElement(rp,f'{{{W}}}sz')
     sz.set(f'{{{W}}}val','16')
    for h in row.findall('.//w:trHeight',NS):h.getparent().remove(h)
 data['word/document.xml']=E.tostring(root,encoding='UTF-8',xml_declaration=True,standalone=True)
 with ZipFile(p,'w',ZIP_DEFLATED) as z:
  for info in infos:z.writestr(info,data[info.filename])
print('Polished appendix pagination, requirement links, test filename and findings-table fit.')
