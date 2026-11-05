from pathlib import Path
from zipfile import ZipFile,ZIP_DEFLATED
from lxml import etree as E
import shutil
R=Path('D:/DroneOps-Intelligence-');B=R/'tmp/doc-refresh';p=R/'docs/slides/DroneOps_Review2.pptx'
shutil.copy2(p,B/'originals'/p.name)
P='http://schemas.openxmlformats.org/presentationml/2006/main';REL='http://schemas.openxmlformats.org/package/2006/relationships';RNS='http://schemas.openxmlformats.org/officeDocument/2006/relationships';CT='http://schemas.openxmlformats.org/package/2006/content-types'
with ZipFile(p) as z:infos=z.infolist();data={i.filename:z.read(i.filename) for i in infos}
with ZipFile(B/'additional-slides.pptx') as z:new={n:z.read(n) for n in z.namelist()}
pres=E.fromstring(data['ppt/presentation.xml']);ids=pres.find(f'{{{P}}}sldIdLst');rels=E.fromstring(data['ppt/_rels/presentation.xml.rels']);ct=E.fromstring(data['[Content_Types].xml'])
layout_rels=E.fromstring(data['ppt/slides/_rels/slide1.xml.rels']);layout=next(r.get('Target') for r in layout_rels if r.get('Type').endswith('/slideLayout'))
maxid=max(int(i.get('id')) for i in ids)
for i in [1,2]:
 n=20+i;name=f'ppt/slides/slide{n}.xml';data[name]=new[f'ppt/slides/slide{i}.xml']
 # Text-only additions reuse the original blank layout, so no existing theme/media is rewritten.
 rr=E.Element(f'{{{REL}}}Relationships',nsmap={None:REL});E.SubElement(rr,f'{{{REL}}}Relationship',Id='rId1',Type=RNS+'/slideLayout',Target=layout)
 data[f'ppt/slides/_rels/slide{n}.xml.rels']=E.tostring(rr,xml_declaration=True,encoding='UTF-8',standalone=True)
 rid=f'rIdAddedReadiness{i}';E.SubElement(rels,f'{{{REL}}}Relationship',Id=rid,Type=RNS+'/slide',Target=f'slides/slide{n}.xml')
 slide_id=E.Element(f'{{{P}}}sldId',id=str(maxid+i));slide_id.set(f'{{{RNS}}}id',rid);ids.insert(len(ids)-1,slide_id)
 E.SubElement(ct,f'{{{CT}}}Override',PartName='/'+name,ContentType='application/vnd.openxmlformats-officedocument.presentationml.slide+xml')
for name,root in [('ppt/presentation.xml',pres),('ppt/_rels/presentation.xml.rels',rels),('[Content_Types].xml',ct)]:data[name]=E.tostring(root,xml_declaration=True,encoding='UTF-8',standalone=True)
with ZipFile(p,'w',ZIP_DEFLATED) as z:
 for name,content in data.items():z.writestr(name,content)
with ZipFile(B/'originals'/p.name) as before,ZipFile(p) as after:
 assert all(before.read(n)==after.read(n) for n in before.namelist() if n not in ['ppt/presentation.xml','ppt/_rels/presentation.xml.rels','[Content_Types].xml'])
print('Saved 22 slides; all original slide, diagram, image and media parts byte-identical.')
