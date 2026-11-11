"""Read-only live MIX/MIZ survey. Outputs are review evidence, not runtime assets."""
import hashlib, importlib.util, json, struct
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
ROOT=Path(__file__).resolve().parents[5]
SOURCE=Path(r'E:\Program Files\ShengquGames\Mir3\Data')
OUT=Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location('ui_decoder',ROOT/'build/game-ui/Decode-OfficialUi.py')
decoder=importlib.util.module_from_spec(spec); spec.loader.exec_module(decoder)
font=ImageFont.truetype('C:/Windows/Fonts/consola.ttf',15)
small=ImageFont.truetype('C:/Windows/Fonts/consola.ttf',12)
def sha(b): return hashlib.sha256(b).hexdigest()
def sheet(rows, name, cols=6, cell=(198,272)):
 w,h=cell; canvas=Image.new('RGB',(cols*w,48+((len(rows)+cols-1)//cols)*h),'#171c23'); draw=ImageDraw.Draw(canvas)
 draw.text((12,12),name+' | native assets, thumbnail only',font=font,fill='white')
 for n,r in enumerate(rows):
  x,y=(n%cols)*w,48+(n//cols)*h
  with Image.open(OUT/r['png']) as im:
   im=im.convert('RGB'); im.thumbnail((w-12,h-48),Image.Resampling.LANCZOS); canvas.paste(im,(x+(w-im.width)//2,y+4))
  draw.text((x+8,y+h-40),f"{r['library']}[{r['index']}]",font=font,fill='#ffcc88')
  draw.text((x+8,y+h-20),f"{r['width']}x{r['height']} off={r['offsetX']},{r['offsetY']}",font=small,fill='#b9c7d6')
 canvas.save(OUT/(name+'.jpg'),quality=94)
manifest={'kind':'character-material-review-only','capturedAt':'2026-11-10','source':str(SOURCE),'runtimeModified':False,'libraries':[],'panels':[],'decodeErrors':[]}
for name in ['UI','Interface','GameInter','ProgUse']:
 mix=(SOURCE/(name+'.MIX')).read_bytes(); miz=(SOURCE/(name+'.MIZ')).read_bytes()
 assert mix[:16]==decoder.MIX_SIGNATURE
 count=struct.unpack_from('<I',mix,16)[0]; offsets=struct.unpack_from('<'+str(count)+'I',mix,24)
 assert len(mix)==24+count*4
 lib={'name':name,'slots':count,'present':sum(bool(o) for o in offsets),'mixSha256':sha(mix),'mizSha256':sha(miz),'items':[]}; manifest['libraries'].append(lib)
 for i,off in enumerate(offsets):
  if not off: continue
  if not 0<=off<=len(miz)-16:
   manifest['decodeErrors'].append({'library':name,'index':i,'error':'header out of bounds'}); continue
  w,h,x,y,packed,tag=struct.unpack_from('<HHhhII',miz,off)
  r={'library':name,'index':i,'width':w,'height':h,'offsetX':x,'offsetY':y}; lib['items'].append(r)
  panel=240<=w<=650 and 280<=h<=800 and x==0 and y==0
  neighbour=name=='UI' and (95<=i<=119 or 2760<=i<=2789)
  if not (panel or neighbour): continue
  try:
   im,meta=decoder.decode_image(miz,off)
   rel=f'raw/{name}/{i}.png'; p=OUT/rel; p.parent.mkdir(parents=True,exist_ok=True)
   im.save(p); im.close(); r.update(png=rel,pngSha256=sha(p.read_bytes()))
   if panel: manifest['panels'].append(r)
  except Exception as e: manifest['decodeErrors'].append({'library':name,'index':i,'error':str(e)})
 assert sha((SOURCE/(name+'.MIX')).read_bytes())==lib['mixSha256']
 assert sha((SOURCE/(name+'.MIZ')).read_bytes())==lib['mizSha256']
portrait=[r for r in manifest['panels'] if 280<=r['width']<=460 and 400<=r['height']<=650]
sheet(portrait,'portrait-panels',6)
for start in range(0,len(manifest['panels']),48): sheet(manifest['panels'][start:start+48],f'all-panels-{start//48+1:02}',8,(164,216))
(OUT/'inventory.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'output':str(OUT),'slots':sum(l['slots'] for l in manifest['libraries']),'present':sum(l['present'] for l in manifest['libraries']),'panels':len(manifest['panels']),'portrait':len(portrait),'errors':manifest['decodeErrors']},ensure_ascii=False))
