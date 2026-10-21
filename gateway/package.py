"""Build the source gateway download served by the web app."""
from pathlib import Path
import zipfile
root=Path(__file__).resolve().parent
output=root.parent/'fe/public/downloads/droneops-gateway.zip'
output.parent.mkdir(parents=True,exist_ok=True)
with zipfile.ZipFile(output,'w',zipfile.ZIP_DEFLATED) as archive:
    for name in ('gateway.py','requirements.txt','README.md'):
        # Fixed metadata makes the package reproducible.
        info=zipfile.ZipInfo('droneops-gateway/'+name,date_time=(2026,1,1,0,0,0))
        info.compress_type=zipfile.ZIP_DEFLATED
        info.external_attr=0o644<<16
        archive.writestr(info,(root/name).read_bytes())
print('Gateway source download generated.')
