#!/usr/bin/env python3
"""Independent BC1/Zl verification against composed PNGs; emits GPU oracle PNGs."""
import argparse
import hashlib
import json
from pathlib import Path
import struct
from PIL import Image
import numpy as np

def sha(raw): return hashlib.sha256(raw).hexdigest()
def require(value, reason):
    if not value: raise ValueError(reason)

def bc1(raw,width,height):
    w,h=(width+3)//4*4,(height+3)//4*4
    require(len(raw)==w*h//2,'BC1 payload size mismatch')
    blocks=np.frombuffer(raw,dtype=np.dtype([('a','<u2'),('b','<u2'),('indices','<u4')]))
    def expand(values):
        r=(values>>11).astype(np.int32); g=((values>>5)&63).astype(np.int32); b=(values&31).astype(np.int32)
        return np.stack([(r<<3)|(r>>2),(g<<2)|(g>>4),(b<<3)|(b>>2),np.full_like(r,255)],axis=1)
    a,b=expand(blocks['a']),expand(blocks['b']); four=blocks['a']>blocks['b']
    palette=np.stack([a,b,(2*a+b)//3,(a+2*b)//3],axis=1)
    palette[:,:,3]=255
    palette[~four,2,:3]=((a+b)//2)[~four,:3]
    palette[~four,3,:]=0
    codes=((blocks['indices'][:,None]>>np.arange(16,dtype=np.uint32)[None,:]*2)&3).astype(np.int32)
    values=palette[np.arange(len(blocks))[:,None],codes].astype(np.uint8)
    return values.reshape(h//4,w//4,4,4,4).transpose(0,2,1,3,4).reshape(h,w,4)

def verify(art, package, output):
    require(not output.exists(),'Verification output must be new')
    manifest_raw=(art/'composed-manifest.json').read_bytes(); manifest=json.loads(manifest_raw)
    prepared=json.loads((package/'prepared-manifest.json').read_text())
    require(prepared['kind']=='official-ui-prepared-zl' and prepared['runtimeReady'] is False and prepared['productionBindingAllowed'] is False,'Unaccepted prepared output required')
    require(prepared['sourceManifestSha256']==sha(manifest_raw) and all(prepared[key]==manifest[key] for key in ('assets','buttons','grids','compositions')),'Package provenance mismatch')
    data=(package/'OfficialV1.Zl').read_bytes(); require(prepared['sha256']==sha(data),'Zl hash mismatch')
    header,count=struct.unpack_from('<ii',data)
    require(count==len(manifest['assets']) and header==4+count*26,'Unexpected image table')
    position=8; expected_payload=header+4; frames=[]
    for asset in manifest['assets']:
        require(data[position]==1,'Missing image'); position+=1
        start,w,h,ox,oy,shadow,sw,sh,sx,sy,ow,oh=struct.unpack_from('<ihhhhBhhhhhh',data,position); position+=25
        require([w,h]==asset['logicalSize'] and (ox,oy,shadow,sw,sh,sx,sy,ow,oh)==(0,)*9,'Image dimensions/offset/auxiliary layers mismatch')
        size=((w+3)//4)*((h+3)//4)*8
        require(start==expected_payload and start+size<=len(data),'Invalid or overlapping image payload')
        frames.append((asset,bc1(data[start:start+size],w,h))); expected_payload+=size
    require(position==header+4 and expected_payload==len(data),'Trailing/short data')
    output.mkdir(parents=True); (output/'GpuExpected').mkdir()
    pixels=opaque_black=padding=0; errors=[]; decoded={}
    for asset, rgba in frames:
        with Image.open(art/asset['png']) as image: source=np.array(image.convert('RGBA'))
        h,w=source.shape[:2]; logical=rgba[:h,:w]
        require(np.array_equal(logical[:,:,3],source[:,:,3]),asset['role']+': alpha mismatch')
        pad=np.ones(rgba.shape[:2],dtype=bool); pad[:h,:w]=False
        require(np.all(rgba[:,:,3][pad]==0),asset['role']+': opaque texture padding')
        black=np.all(source[:,:,:3]==0,axis=2)&(source[:,:,3]==255)
        require(np.all(logical[:,:,3][black]==255),asset['role']+': black interior lost')
        opaque=source[:,:,3]==255
        error=np.abs(source[:,:,:3].astype(np.int16)-logical[:,:,:3].astype(np.int16))[opaque]
        errors.append(dict(role=asset['role'],meanAbsoluteRgbError=round(float(error.mean()),4) if error.size else 0,maxChannelError=int(error.max()) if error.size else 0))
        Image.fromarray(logical).save(output/'GpuExpected'/(asset['role']+'.png'))
        decoded[asset['role']]=logical
        pixels+=w*h; opaque_black+=int(black.sum()); padding+=int(pad.sum())
    for button,states in manifest['buttons'].items():
        for state,role in states.items():
            require(np.array_equal(decoded[button][:,:,3],decoded[role][:,:,3]),'Button alpha changed: '+role)
    hole=decoded['minimap-frame'][21:173,11:161,3]
    require(np.all(hole==0),'Minimap viewport is obstructed')
    require(len(manifest['grids']['inventory']['cells'])==49 and len(manifest['grids']['quickbar-base']['cells'])==10,'Cell count changed')
    for composition in manifest['compositions']:
        shell=decoded['hud-orb-shell']; h,w=shell.shape[:2]
        preview=Image.new('RGB',(w*3,h))
        for col,ratio in enumerate((0,.5,1)):
            frame=Image.new('RGBA',(w,h),(40,80,120,255))
            for layer in composition['layers']:
                part=Image.fromarray(decoded[layer['role']]); at=layer['offset'][:]
                if layer.get('fill'):
                    visible=int(part.height*ratio+.5)
                    if not visible: continue
                    top=part.height-visible; part=part.crop((0,top,part.width,part.height)); at[1]+=top
                frame.alpha_composite(part,tuple(at))
            preview.paste(frame.convert('RGB'),(col*w,0))
        preview.save(output/'hud-orb-bc1-states.png')
    result=dict(kind='official-ui-bc1-verification',releaseAccepted=False,frames=len(frames),logicalAlphaPixelsChecked=pixels,
        transparentPaddingPixelsChecked=padding,opaqueBlackInteriorPixelsPreserved=opaque_black,buttonStateSets=len(manifest['buttons']),
        sourceManifestSha256=sha(manifest_raw),zlSha256=sha(data),rgbCompressionError=errors)
    (output/'verification.json').write_text(json.dumps(result,indent=2)+'\n')
    print(f"BC1: {len(frames)} frames; {pixels} logical alpha pixels; {padding} padding pixels; {opaque_black} opaque black pixels preserved; {len(manifest['buttons'])} button state masks unchanged.")
    return result

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    for name in ('art','package','output'): parser.add_argument('--'+name,required=True,type=Path)
    args=parser.parse_args(); verify(args.art,args.package,args.output)
