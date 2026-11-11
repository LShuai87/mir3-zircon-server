import importlib.util,hashlib,inspect,json
from pathlib import Path
from PIL import Image
root=Path(__file__).resolve().parents[6]
spec=importlib.util.spec_from_file_location('compose',root/'build/game-ui/Compose-OfficialUi.py');m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
recipe=json.loads((root/'build/game-ui/warehouse-combined-composition-recipe.json').read_text('utf-8'))
asset=next(a for a in recipe['assets'] if a['role']=='character-main-background')
source=Path('E:/workspace/mir3-ui-work/warehouse-combined-20261108-v1/decoded/UI/2765.png')
with Image.open(source) as im: image=m.mask_edges(im,[])
expected='07c3a427e7cc60cb913862653e19fad9d3655b2f0eb02bfa90ec2654b64c491e'
for i in range(3):
 output,_=m.transform(image,asset['transform']);print('original',i,hashlib.sha256(output.tobytes()).hexdigest(),flush=True)
code=inspect.getsource(m.match_repair_edges)
code=code.replace('illumination = original.filter(ImageFilter.GaussianBlur(18)).load()','illumination_image = original.filter(ImageFilter.GaussianBlur(18)); illumination = illumination_image.load()')
code=code.replace('texture_base = tiled.filter(ImageFilter.GaussianBlur(8)).load()','texture_image = tiled.filter(ImageFilter.GaussianBlur(8)); texture_base = texture_image.load()')
exec(code,m.__dict__)
for i in range(3):
 output,_=m.transform(image,asset['transform']);actual=hashlib.sha256(output.tobytes()).hexdigest();print('retained-image',i,actual,'matches-frozen',actual==expected,flush=True)
