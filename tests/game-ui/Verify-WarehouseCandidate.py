#!/usr/bin/env python3
"""Check the isolated warehouse PNG and Zl with an independent BC1 decoder."""
import argparse
import hashlib
import json
from pathlib import Path
import struct
from PIL import Image


def sha(data):
    return hashlib.sha256(data).hexdigest()


def check(value, reason):
    if not value:
        raise ValueError(reason)


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--work',required=True,type=Path)
    parser.add_argument('--output',required=True,type=Path)
    args=parser.parse_args()
    check(not args.output.exists(),'Output must be new')
    root=Path(__file__).resolve().parents[2]
    manifest_path=args.work/'art-v2/composed-manifest.json'
    manifest=json.loads(manifest_path.read_bytes())
    prepared=json.loads((args.work/'zl-v2/prepared-manifest.json').read_bytes())
    raw=(args.work/'zl-v2/OfficialV1.Zl').read_bytes()
    check(sha(raw)==prepared['sha256'],'Package digest differs')
    check(sha(manifest_path.read_bytes())==prepared['sourceManifestSha256'],'Manifest digest differs')
    check(manifest['assets']==prepared['assets'] and len(manifest['assets'])==1,'Frame map differs')
    check(prepared['runtimeReady'] is False and prepared['productionBindingAllowed'] is False,'Activation must remain off')
    entry=manifest['assets'][0]
    check(entry['role']=='warehouse-background' and entry['logicalSize']==[508,480],'Unexpected warehouse frame')
    source=(args.work/'decoded/UI/511.png').read_bytes()
    check(sha(source)==entry['sourcePngSha256']=='f574ddf094e5f1a0b4d78ccd31da1f9e9b3402ec6be8f9fb9b025358239c85d1','Source identity differs')
    selection=root/'build/game-ui/warehouse-selection.json'
    recipe_path=root/'build/game-ui/warehouse-composition-recipe.json'
    recipe=json.loads(recipe_path.read_bytes())
    check(recipe['selectionSha256']==sha(selection.read_bytes()) and manifest['recipeSha256']==sha(recipe_path.read_bytes()),'Recipe provenance differs')
    check(struct.unpack_from('<ii',raw)==(30,1) and raw[8]==1,'Invalid Zl table')
    header=struct.unpack_from('<ihhhhBhhhhhh',raw,9)
    check(header==(34,508,480,0,0,0,0,0,0,0,0,0),'Invalid Zl geometry')
    check(len(raw)==34+508*480//2,'Invalid Zl payload size')
    png=args.work/'art-v2'/entry['png']
    check(sha(png.read_bytes())==entry['pngSha256'],'PNG digest differs')
    with Image.open(png) as image:
        source_pixels=image.convert('RGBA').tobytes()
    check(sha(source_pixels)==entry['rgbaSha256'],'RGBA digest differs')
    # Independent BC1 decode: endpoint expansion and two/three-colour branches.
    def colour(value):
        r,g,b=(value>>11)&31,(value>>5)&63,value&31
        return ((r<<3)|(r>>2),(g<<2)|(g>>4),(b<<3)|(b>>2),255)
    transparent=error_sum=error_count=max_error=0
    for by in range(0,480,4):
        for bx in range(0,508,4):
            at=34+((by//4)*127+bx//4)*8
            c0,c1,indices=struct.unpack_from('<HHI',raw,at)
            first,second=colour(c0),colour(c1)
            if c0>c1:
                palette=[first,second,tuple((2*first[k]+second[k])//3 for k in range(3))+(255,),
                         tuple((first[k]+2*second[k])//3 for k in range(3))+(255,)]
            else:
                palette=[first,second,tuple((first[k]+second[k])//2 for k in range(3))+(255,),(0,0,0,0)]
            for pixel in range(16):
                decoded=palette[(indices>>(pixel*2))&3]
                start=((by+pixel//4)*508+bx+pixel%4)*4
                original=source_pixels[start:start+4]
                check(original[3] in (0,255) and decoded[3]==original[3],'BC1 alpha differs')
                if original[3]==0:
                    transparent+=1
                    check(original==bytes(4),'Noncanonical transparent PNG pixels')
                else:
                    for k in range(3):
                        error=abs(decoded[k]-original[k]);error_sum+=error;error_count+=1
                        max_error=max(max_error,error)
    output=dict(schemaVersion=1,releaseAccepted=False,frames=1,logicalAlphaPixelsChecked=508*480,
                transparentPixels=transparent,zlBytes=len(raw),zlSha256=sha(raw),
                sourcePngSha256=entry['sourcePngSha256'],pngSha256=entry['pngSha256'],
                bc1PayloadSha256=sha(raw[34:]),maxPngToBc1ChannelError=max_error,
                meanPngToBc1ChannelError=round(error_sum/error_count,4),gpuValidated=False)
    args.output.write_text(json.dumps(output,indent=2)+'\n',encoding='utf8')
    print('Warehouse candidate verified: 1 frame, 243840 alpha pixels; isolated package; GPU/live acceptance pending.')


if __name__=='__main__':
    main()
