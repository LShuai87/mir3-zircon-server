"""Verify unchanged 93-frame content and the appended warehouse in a 94-frame Zl."""
import argparse, hashlib, json, struct
from pathlib import Path
from PIL import Image
ROOT=Path(__file__).resolve().parents[6]
HERE=Path(__file__).parent
OLD=Path('E:/workspace/mir3-ui-work/npc-shop-assets-20261108-v1')
CANDIDATE=Path('E:/workspace/mir3-ui-work/warehouse-assets-20261108-v1')
NEW=Path('E:/workspace/mir3-ui-work/warehouse-combined-20261108-v2')
def sha(raw): return hashlib.sha256(raw).hexdigest()
def load(p): return json.loads(p.read_text('utf-8-sig'))
def require(ok,message):
    if not ok: raise ValueError(message)
def frames(raw):
    require(len(raw)>=8,'Truncated header')
    table,count=struct.unpack_from('<ii',raw)
    require(0<count<=4096 and table==4+count*26 and table+4<=len(raw),'Invalid table')
    result=[];cursor=table+4
    for i in range(count):
        at=8+i*26
        require(raw[at]==1,'Missing frame')
        start,w,h,*metadata=struct.unpack_from('<ihhhhBhhhhhh',raw,at+1)
        require(w>0 and h>0 and metadata==[0]*9,'Invalid geometry or offset')
        size=((w+3)//4)*((h+3)//4)*8
        require(start==cursor and start+size<=len(raw),'Invalid payload bounds')
        result.append(dict(width=w,height=h,data=raw[start:start+size]))
        cursor+=size
    require(cursor==len(raw),'Trailing bytes')
    return result
def verify():
    old=load(HERE/'source-before/build/game-ui/asset-manifest.json')
    prepared=load(NEW/'zl/prepared-manifest.json')
    original_raw=(OLD/'zl/OfficialV1.Zl').read_bytes()
    candidate_raw=(CANDIDATE/'zl-v2/OfficialV1.Zl').read_bytes()
    combined_raw=(NEW/'zl/OfficialV1.Zl').read_bytes()
    require(sha(original_raw)==old['package']['sha256'],'Original baseline changed')
    require(sha(candidate_raw)=='471169dfa093b521f3c700c5a993e4e763aa17a45b2e16f440d961c0c7bf4cec','Isolated candidate changed')
    original,candidate,combined=map(frames,(original_raw,candidate_raw,combined_raw))
    require((len(original),len(candidate),len(combined),len(prepared['assets']))==(93,1,94,94),'Unexpected frame count')
    require(prepared['sha256']==sha(combined_raw),'Prepared package digest mismatch')
    checks=[]
    for index,a in enumerate(prepared['assets']):
        role=a['role']; new_png=NEW/'art'/a['png']
        require(a['outputIndex']==index,'Noncontiguous semantic order')
        require(sha(new_png.read_bytes())==a['pngSha256'],'PNG hash mismatch: '+role)
        with Image.open(new_png) as image:
            rgba=image.convert('RGBA');pixels=rgba.tobytes()
            require(list(rgba.size)==a['logicalSize'] and sha(pixels)==a['rgbaSha256'],'RGBA differs: '+role)
        require([combined[index]['width'],combined[index]['height']]==a['logicalSize'],'Zl geometry differs: '+role)
        if index<93:
            prior=old['assets'][index]
            require({k:v for k,v in prior.items() if k!='dataSha256'}==a,'Existing metadata/index differs: '+role)
            require(original[index]==combined[index],'Existing BC1 payload differs: '+role)
            require((OLD/'art'/prior['png']).read_bytes()==new_png.read_bytes(),'Existing PNG differs: '+role)
        else:
            require(role=='warehouse-background' and a['sourceLibrary']=='UI' and a['sourceIndex']==511 and a['logicalSize']==[508,480], 'Warehouse identity differs')
            require(candidate[0]==combined[index],'Warehouse BC1 differs from isolated candidate')
            require((CANDIDATE/'art-v2/Assets/warehouse-background.png').read_bytes()==new_png.read_bytes(),'Warehouse PNG differs from isolated candidate')
        checks.append(dict(role=role,index=index,pngSha256=a['pngSha256'],rgbaSha256=a['rgbaSha256'],bc1Sha256=sha(combined[index]['data'])))
    return dict(kind='warehouse-merged-package-content-verification',originalFramesUnchanged=93,appendedWarehouseIndex=93,packageFrames=94,
        package=dict(path=str(NEW/'zl/OfficialV1.Zl'),sha256=sha(combined_raw),byteLength=len(combined_raw)),
        warehouseSource='Explicit reuse of UI[511] shop trim, not identified native warehouse artwork.',
        frames=checks,runtimeReady=False,productionBindingAllowed=False,gpuComparisonExecuted=False)
if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--output',type=Path,required=True);parser.add_argument('--repeat',action='store_true');args=parser.parse_args()
    report=verify()
    if args.repeat:
        require((NEW/'zl-repeat/OfficialV1.Zl').read_bytes()==(NEW/'zl/OfficialV1.Zl').read_bytes(),'Repeated package differs')
        first,second=load(NEW/'art/composed-manifest.json'),load(NEW/'art-repeat/composed-manifest.json')
        require(first==second,'Repeated composition manifest differs')
        for a in first['assets']:
            require((NEW/'art'/a['png']).read_bytes()==(NEW/'art-repeat'/a['png']).read_bytes(),'Repeated PNG differs: '+a['role'])
        report['repeatedPngsByteIdentical']=94
        report['repeatedPackageByteIdentical']=True
    require(not args.output.exists(),'Report output must be new')
    args.output.write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({k:v for k,v in report.items() if k!='frames'},indent=2))
