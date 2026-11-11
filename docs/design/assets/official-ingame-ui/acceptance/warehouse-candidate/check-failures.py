"""Run bounded negative controls against the isolated warehouse verifier."""
import hashlib
import json
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import tempfile

root=Path(__file__).resolve().parents[6]
work=Path('E:/workspace/mir3-ui-work/warehouse-assets-20261108-v1')
verifier=root/'tests/game-ui/Verify-WarehouseCandidate.py'
results=[]
for label,expected in [('package-digest','Package digest differs'),('source-png','Source identity differs'),('png-digest','PNG digest differs'),('bc1-alpha','BC1 alpha differs')]:
    with tempfile.TemporaryDirectory(prefix='warehouse-control-') as folder:
        target=Path(folder)/'work'
        for name in ('decoded','art-v2','zl-v2'):
            shutil.copytree(work/name,target/name)
        if label in ('package-digest','bc1-alpha'):
            package=target/'zl-v2/OfficialV1.Zl'
            raw=bytearray(package.read_bytes())
            raw[34:42]=struct.pack('<HHI',65535,0,0)
            package.write_bytes(raw)
            if label=='bc1-alpha':
                path=target/'zl-v2/prepared-manifest.json';data=json.loads(path.read_bytes())
                data['sha256']=hashlib.sha256(raw).hexdigest()
                path.write_text(json.dumps(data),encoding='utf8')
        else:
            path=target/('decoded/UI/511.png' if label=='source-png' else 'art-v2/Assets/warehouse-background.png')
            path.write_bytes(path.read_bytes()+b'tamper')
        run=subprocess.run([sys.executable,str(verifier),'--work',str(target),'--output',str(Path(folder)/'result.json')],capture_output=True,text=True)
        assert run.returncode!=0 and expected in run.stderr,(label,run.stdout,run.stderr)
        results.append(dict(control=label,rejected=True,reason=expected))
print(json.dumps(results,indent=2))
