from pathlib import Path
from PIL import Image, ImageDraw
import json,hashlib,itertools
repo=Path('E:/workspace/mir3-zircon-server')
art=Path('E:/workspace/mir3-ui-work/command-shell-20261108-v1/art/Assets')
output=repo/'docs/design/assets/official-ingame-ui/acceptance/button-hitmask'
matching=repo/'docs/design/assets/official-ingame-ui/acceptance/reference/panel-matching.json'
matches=json.loads(matching.read_text(encoding='utf-8-sig'))['matches']
roles=[m for m in matches if m['role'].startswith('hud-command-')]
colors=['#f15b64','#f9cc51','#5fc9b5','#75baff','#c58cf0','#f4a66c','#d3e870']
layer=Image.new('RGB',(225,202),'#15191f'); draw=ImageDraw.Draw(layer)
regions=[]; overlay=[]
layout=json.loads((repo/'build/game-ui/layout-official-v1.json').read_text(encoding='utf-8-sig'))
for i,m in enumerate(roles):
 role=m['role']; placement=m['candidates'][0]['bestPlacements'][0]
 image=Image.open(art/(role+'.png')).convert('RGBA'); node=next(n for n in layout['nodes'] if n.get('role')==role and n['owner']=='commands')
 x,y,w,h=node['rect']; points={(placement['x']+px,placement['y']+py) for py in range(h) for px in range(w) if image.getpixel((px,py))[3]}
 for py in range(h):
  for px in range(w):
   if image.getpixel((px,py))[3]:layer.putpixel((x+px,y+py),Image.new('RGB',(1,1),colors[i]).getpixel((0,0)))
 draw.rectangle((x,y,x+w-1,y+h-1),outline=colors[i]); draw.text((x+2,y+2),str(i+1),fill='white')
 regions.append((role,points)); overlay.append({'role':role,'currentLocalRect':node['rect'],'matchedScreenshotOrigin':[placement['x'],placement['y']],'pngSha256':hashlib.sha256((art/(role+'.png')).read_bytes()).hexdigest()})
intersections=[{'a':a[0],'b':b[0],'opaqueOverlapPixels':len(a[1]&b[1])} for a,b in itertools.combinations(regions,2) if a[1]&b[1]]
layer.resize((675,606),Image.Resampling.NEAREST).save(output/'current-button-masks.png')
result={'kind':'button-alpha-and-reference-overlap-inspection','releaseAccepted':False,'matchingSha256':hashlib.sha256(matching.read_bytes()).hexdigest(),'coordinateSpace':'Matched original screenshot pixels including titlebar. CurrentLocalRect is existing candidate geometry, not recovered official hit geometry.','buttons':overlay,'matchedOpaqueOverlaps':intersections,'limitations':['Local JPEG placement is approximate; it does not establish unique coordinates or window client crop.','Nonzero opaque intersections prevent asserting disjoint official hit regions from alpha alone.','Current rectangles remain unchanged and disjoint; compact layout and edge ownership are pending.']}
(output/'reference-overlap-study.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'referencePairsWithOpaqueOverlap':len(intersections),'totalPairwiseOpaqueOverlapPixels':sum(p['opaqueOverlapPixels'] for p in intersections)}))
