"""Run one check, preserving its exact output and exit code in a fresh receipt."""
import argparse, json, subprocess
from pathlib import Path
parser=argparse.ArgumentParser()
parser.add_argument('log')
parser.add_argument('command',nargs=argparse.REMAINDER)
args=parser.parse_args()
path=Path(__file__).parent/args.log
if path.exists(): raise SystemExit('Refusing to replace an existing log: '+str(path))
result=subprocess.run(args.command,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
path.write_bytes(result.stdout)
print(result.stdout.decode('utf-8',errors='replace')[-3500:])
receipt=dict(command=args.command,exitCode=result.returncode,log=path.as_posix())
path.with_suffix('.receipt.json').write_text(json.dumps(receipt,indent=2)+'\n',encoding='utf-8')
print(json.dumps(receipt))
raise SystemExit(result.returncode)
