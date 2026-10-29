"""Build the source gateway download served by the web app."""
from pathlib import Path
import zipfile
root=Path(__file__).resolve().parent
output=root.parent/'fe/public/downloads/droneops-gateway.zip'
output.parent.mkdir(parents=True,exist_ok=True)
with zipfile.ZipFile(output,'w',zipfile.ZIP_DEFLATED) as archive:
    files=(
        (root/'gateway.py','gateway.py'),
        (root/'requirements.txt','requirements.txt'),
        (root/'README.md','README.md'),
        (root/'droneops-gateway.service.example','droneops-gateway.service.example'),
        (root/'install-jetson.sh','install-jetson.sh'),
        (root/'pair-jetson.sh','pair-jetson.sh'),
        (root.parent/'docs/jetson-orin-nano-pixhawk-6c-setup.md','JETSON_SETUP.md'),
    )
    for source, name in files:
        # Fixed metadata makes the package reproducible.
        info=zipfile.ZipInfo('droneops-gateway/'+name,date_time=(2026,1,1,0,0,0))
        info.compress_type=zipfile.ZIP_DEFLATED
        mode=0o755 if name.endswith('.sh') else 0o644
        info.external_attr=mode<<16
        archive.writestr(info,source.read_bytes())
print('Gateway source download generated.')
