#!/usr/bin/env python3
"""Validate proposed OfficialV1 geometry and emit annotations/compiled candidates.

This does not load the game, infer screenshot/DPI transforms or enable a skin.
JSON is the build input. The generated C# deliberately remains non-production.
"""
import argparse
from copy import deepcopy
import hashlib
import json
from pathlib import Path
import re
import shutil
import tempfile

ROOT = Path(__file__).resolve().parents[2]
RESOLUTIONS = ((1024,768),(1366,768),(1280,800),(1440,900),(1600,900),(1920,1080))
ID = re.compile(r'[A-Za-z][A-Za-z0-9]*(?:\.[A-Za-z0-9]+)+\Z')


def require(value, message):
    if not value:
        raise ValueError(message)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read_json(path):
    def pairs(items):
        result = {}
        for k, v in items:
            require(k not in result, 'Duplicate JSON key: ' + k)
            result[k] = v
        return result
    def invalid(value):
        raise ValueError('Non-finite JSON value: ' + value)
    require(path.stat().st_size <= 8*1024*1024, 'JSON input exceeds 8 MiB')
    return json.loads(path.read_text(encoding='utf-8-sig'), object_pairs_hook=pairs, parse_constant=invalid)


def rect(value, context):
    require(type(value) is list and len(value) == 4 and all(type(v) is int for v in value), context + ': invalid rectangle')
    x, y, w, h = value
    require(0 <= x <= 8192 and 0 <= y <= 8192 and 0 < w <= 8192 and 0 < h <= 8192, context + ': invalid dimensions')
    return value


def contains(outer, inner):
    x,y,w,h = outer
    a,b,c,d = inner
    return x <= a and y <= b and a+c <= x+w and b+d <= y+h


def intersects(a, b):
    return a[0] < b[0]+b[2] and b[0] < a[0]+a[2] and a[1] < b[1]+b[3] and b[1] < a[1]+a[3]


def together(a, b):
    return a == b or a in ('chrome','all') or b in ('chrome','all')


def validate(data, bindings):
    require(data.get('schemaVersion') == 1 and data.get('kind') == 'official-ui-layout-contract' and data.get('style') == 'OfficialV1', 'Unknown layout schema/style')
    require(data.get('status') == 'proposed-coordinates' and all(data.get(k) is False for k in ('runtimeReady','productionBindingAllowed','layoutFrozen')), 'Proposed geometry cannot be promoted by changing a flag')
    require(data['referenceResolution'] == [1024,768], 'Reference resolution must remain user-confirmed 1024x768')
    require(data['scope'] == dict(npcCards='Legacy',secondaryPopups='Legacy',displaySettings='unchanged',loginAndCharacterSelection='unchanged'), 'Scope drift')
    expected_sizes = dict(character=[412,562],inventory=[276,412],quickbar=[459,44],minimap=[166,198],chat=[574,106],chatInput=[574,22],orb=[225,202],commands=[225,202],readouts=[574,16],experience=[574,8])
    require({k:v['size'] for k,v in data['owners'].items()} == expected_sizes, 'Unexpected owner set or geometry')
    pages = {a['id'] for a in bindings['tabs']}
    require(data['pages'] == bindings['tabs'], 'Four character tabs must preserve stable IDs and captions')
    ids = [n['id'] for n in data['nodes']]
    require(len(ids) == len(set(ids)) and all(ID.fullmatch(n) for n in ids), 'Duplicate or invalid node ID')
    by_id = {n['id']:n for n in data['nodes']}
    for n in data['nodes']:
        require(n['owner'] in expected_sizes, n['id'] + ': unknown owner')
        require(n['page'] in pages | {'all','chrome'}, n['id'] + ': unknown page')
        if n['owner'] != 'character':
            require(n['page'] == 'all', n['id'] + ': page scoped outside character')
        bounds = [0,0,*expected_sizes[n['owner']]]
        require(contains(bounds, rect(n['rect'], n['id'])), n['id'] + ': rectangle outside owner')
        require(n['kind'] in ('control','caption','preview','stat','decoration','fill','viewport','dynamic-tabs'), n['id'] + ': unknown node kind')
        for sub in ('hitRect','iconRect','valueRect','captionRect'):
            if sub in n:
                require(contains(n['rect'], rect(n[sub], n['id'] + '.' + sub)), n['id'] + ': sub-rectangle outside visual rectangle')
        require((n['kind'] == 'control') == ('hitRect' in n), n['id'] + ': interactive geometry must be explicit')
        if n['kind'] == 'control':
            require(bool(n.get('action')), n['id'] + ': missing original action')
    command_order = ['SpellButton','QuestButton','MailButton','GroupButton','InventoryButton','ConfigButton','CharacterButton','CashShopButton']
    require(data.get('commandPolicy') == dict(imageDrawOrder=command_order, overlap='normal-alpha-frontmost',
        centreMustRemainExclusive=True, lockImageOrderOnInput=True, shellOrigin=[0,27],
        geometrySource='adapted-relative-screenshot-matching'), 'Explicit command alpha/draw-order policy required')
    for name in command_order:
        require(by_id['commands.'+name].get('hitTest') == 'normal-frame-alpha', 'Wheel image requires normal-frame alpha: '+name)
    wheel = set(command_order) - {'CashShopButton'}
    def centre_inside(a, b):
        x,y,w,h = b; cx,cy = a[0]+a[2]//2,a[1]+a[3]//2
        return x <= cx < x+w and y <= cy < y+h
    controls = [n for n in data['nodes'] if n['kind'] == 'control']
    pair_checks = 0
    for i,a in enumerate(controls):
        for b in controls[i+1:]:
            if a['owner'] == b['owner'] and together(a['page'],b['page']):
                if intersects(a['hitRect'],b['hitRect']):
                    require(a['owner'] == 'commands' and a['binding'] in wheel and b['binding'] in wheel and
                            not centre_inside(a['hitRect'],b['hitRect']) and not centre_inside(b['hitRect'],a['hitRect']),
                            'Overlap outside alpha-tested wheel edges: ' + a['id'] + ', ' + b['id'])
                pair_checks += 1
    slots = [n for n in controls if n['id'].startswith('character.equipment.')]
    expected_slots = {a['slotId']:a for a in bindings['equipment']}
    require(len(slots) == 16 and {n['slotId'] for n in slots} == set(range(16)), 'Must bind each of 16 equipment slots once')
    for n in slots:
        source = expected_slots[n['slotId']]
        require(n['binding'] == source['control'] and n['enumName'] == source['enumName'] and n['page'] == source['targetTab'], 'Equipment backing/enum/page mismatch: ' + n['id'])
        require(n['rect'][2:] == [36,36] and n['iconRect'] == n['rect'], 'Global cell size must remain 36x36')
    # Plan section 6.2 fixes equipment semantics, independently of collision checks.
    # A valid non-overlapping grid can still put the helmet or shoes in the wrong place.
    preview = by_id['character.preview']['rect']
    helmet = by_id['character.equipment.Helmet']['rect']
    shoes = by_id['character.equipment.Shoes']['rect']
    require(helmet[1] + helmet[3] <= preview[1] and
            preview[0] <= helmet[0] and helmet[0] + helmet[2] <= preview[0] + preview[2],
            'Helmet must remain above the character preview')
    require(shoes[0] + shoes[2] <= preview[0] and shoes[1] >= preview[1] + preview[3],
            'Shoes must remain at the lower left of the character preview')
    for prefix,count,columns,start in [('inventory.cell.',49,7,(15,49)),('quickbar.cell.',10,10,(60,4))]:
        grid = [n for n in controls if n['id'].startswith(prefix)]
        require(len(grid) == count and {n['slotId'] for n in grid} == set(range(count)), prefix + ': missing or duplicate cell')
        for n in grid:
            i = n['slotId']; x = start[0]+i%columns*35; y = start[1]+i//columns*35
            require(n['rect'] == [x,y,36,36], prefix + ': visual stride/geometry changed')
            hit_w = 36 if i%columns == columns-1 else 35
            hit_h = 36 if i//columns == (count-1)//columns else 35
            require(n['hitRect'] == [x,y,hit_w,hit_h], prefix + ': shared-border ownership changed')
    actual = [n for n in data['nodes'] if n['id'].startswith('character.stat.')]
    require(len(actual) == 57 and {n['bindingId'] for n in actual} == {n['id'] for n in bindings['statBindings']}, 'Existing 57 stat bindings must remain complete')
    for source in bindings['statBindings']:
        n = by_id['character.stat.'+source['id']]
        require((n['binding'],n['dataSource'],n['page'],n['formatPolicy'],n['preserveTag']) == (source['control'],source['dataSource'],source['targetTab'],source['formatPolicy'],source['keepTagIcon']), 'Stat binding drift: ' + source['id'])
    for source in bindings['existingControls']:
        n = by_id['character.control.'+source['control']]
        require((n['bindingId'],n['binding'],n['dataSource'],n['page']) == (source['id'],source['control'],source['dataSource'],source['targetTab']), 'Existing control lost: ' + source['id'])
    for source in bindings['additionalBindings']:
        n = by_id['character.immediate.'+source['id']]
        status = source['status']
        require(status in ('not_implemented', 'implemented_opt_in'), 'Unaccepted readout implementation status')
        require((n['dataSource'],n['implementationStatus']) == (source['dataSource'],status), 'Immediate data implementation status must match binding source')
        if status == 'implemented_opt_in':
            proof = source.get('implementationEvidence', {})
            require(source.get('runtimeEnabled') is False and source.get('reuseHudFormatting') is True and
                    all(proof.get(key) for key in ('entrypoint', 'target', 'copy', 'subscribe', 'unsubscribe', 'test', 'verification')),
                    'Opt-in readout binding lacks implementation evidence or claims runtime activation')
    for action in bindings['actions'][:8]:
        n = by_id['character.action.'+action['id']]
        require(n['packet'] == action['packet'] and n['stat'] == action['stat'] and n['reuseExistingHandler'] is True, 'Hermit action drift')
    expected_hud = {'CharacterButton','InventoryButton','SpellButton','QuestButton','MailButton','GroupButton','ConfigButton','CashShopButton','BeltButton'}
    require({n['binding'] for n in controls if n['owner'] == 'commands'} == expected_hud, 'All nine original HUD entries need distinct targets')
    policy = data['chatPolicy']
    require(policy['preserveInstances'] is True and policy['restoreSavedTabsOnce'] is True and policy['rebuildOnResize'] is False and policy['preserveFontsColoursChannelsHistoryAndScroll'] is True and policy['separateInputRow'] is True and policy['nativeInputBounds'] == 'DXTextBox.DisplayArea', 'Chat preservation policy changed')
    history = by_id['chat.history']['rect']; tab = by_id['chat.tabs']['rect']
    require(not intersects(history,tab) and not intersects(history,by_id['chat.scrollbar']['hitRect']), 'Chat viewport overlaps tab header or scrollbar')
    require([(v['width'],v['height']) for v in data['resolutions']] == list(RESOLUTIONS), 'Six exact resolutions required')
    for v in data['resolutions']:
        screen = [0,0,v['width'],v['height']]
        require(v['safeRect'] == [0,0,v['width'],v['height']-202], 'HUD safe rectangle changed')
        require(set(v['placements']) == set(expected_sizes), 'Missing window placement')
        for owner,bounds in v['placements'].items():
            rect(bounds, owner)
            require(bounds[2:] == expected_sizes[owner] and contains(screen,bounds), owner + ': screen bounds/size mismatch')
            if owner in ('character','inventory'):
                require(contains(v['safeRect'],bounds), owner + ': unsafe default window')
        require(not intersects(v['placements']['character'],v['placements']['inventory']), 'Default character/inventory overlap')
        hud = ('orb','commands','quickbar','readouts','chat','chatInput','experience')
        for i,owner in enumerate(hud):
            for other in hud[i+1:]:
                require(not intersects(v['placements'][owner],v['placements'][other]), 'HUD groups overlap: ' + owner + '/' + other)
        require(v['placements']['chat'][1]+v['placements']['chat'][3] <= v['placements']['chatInput'][1], 'Chat must remain above separate input')
    return dict(nodeCount=len(ids),interactiveCount=len(controls),hitPairsChecked=pair_checks,equipmentSlots=len(slots),inventoryCells=49,quickbarCells=10,statBindings=57,existingControlBindings=8,additionalImmediateBindings=4,hermitPacketActions=8,hudEntries=9,resolutionCount=len(RESOLUTIONS))


def guard_checks(data, bindings):
    variants = []
    def altered(name, mutate):
        copy = deepcopy(data); mutate(copy); variants.append((name,copy))
    altered('missing-inventory-cell', lambda d: d['nodes'].remove(next(n for n in d['nodes'] if n['id']=='inventory.cell.48')))
    altered('duplicate-equipment-slot', lambda d: next(n for n in d['nodes'] if n['id']=='character.equipment.Weapon').update(slotId=1))
    altered('missing-existing-stat', lambda d: d['nodes'].remove(next(n for n in d['nodes'] if n['id']=='character.stat.DisplayStats.MaxAC')))
    altered('overlapping-hud-centres', lambda d: next(n for n in d['nodes'] if n['id']=='commands.InventoryButton').update(rect=[85,94,54,32],hitRect=[85,94,54,32]))
    altered('wheel-hit-mode-removed', lambda d: next(n for n in d['nodes'] if n['id']=='commands.InventoryButton').pop('hitTest'))
    altered('wheel-order-reversed', lambda d: d['commandPolicy']['imageDrawOrder'].reverse())
    altered('wheel-overlaps-belt', lambda d: next(n for n in d['nodes'] if n['id']=='commands.BeltButton').update(rect=[85,94,31,24],hitRect=[85,94,31,24]))
    altered('off-screen-window', lambda d: d['resolutions'][0]['placements']['inventory'].__setitem__(0,1000))
    altered('wrong-shared-grid-border', lambda d: next(n for n in d['nodes'] if n['id']=='inventory.cell.0').update(hitRect=[15,49,36,36]))
    altered('chat-recreated-on-resize', lambda d: d['chatPolicy'].update(rebuildOnResize=True))
    altered('scope-changed-to-npc', lambda d: d['scope'].update(npcCards='OfficialV1'))
    altered('production-flag-only', lambda d: d.update(productionBindingAllowed=True))
    altered('readout-status-drift', lambda d: next(n for n in d['nodes'] if n['id']=='character.immediate.new.level').update(implementationStatus='not_implemented'))
    altered('readout-activation-claim', lambda d: next(n for n in d['nodes'] if n['id']=='character.immediate.new.level').update(implementationStatus='runtime_enabled'))
    result = []
    for name,copy in variants:
        try:
            validate(copy,bindings)
        except ValueError as error:
            result.append(dict(case=name,result='rejected',reason=str(error)))
        else:
            raise ValueError('Invalid contract accepted: ' + name)
    return result


def cs_string(text):
    return json.dumps(text,ensure_ascii=True)


def emit_csharp(data, digest):
    lines = ['// Generated from layout-official-v1.json. Proposed geometry; not runtime enabled.',
             'using System;', 'using System.Collections.ObjectModel;', 'using System.Drawing;',
             'namespace Client.Models.GameUi', '{', '    public static class OfficialUiLayoutContract', '    {',
             '        public const bool ProductionBindingAllowed = false;',
             '        public const string SourceSha256 = '+cs_string(digest)+';',
             '        public sealed class Node', '        {',
             '            public readonly string Id, Owner, Page, Kind, Binding;',
             '            public readonly Rectangle Rect;', '            public readonly Rectangle? HitRect;',
             '            public Node(string id, string owner, string page, string kind, string binding, Rectangle rect, Rectangle? hit)',
             '            { Id=id; Owner=owner; Page=page; Kind=kind; Binding=binding; Rect=rect; HitRect=hit; }', '        }',
             '        public static readonly ReadOnlyCollection<Node> Nodes = new ReadOnlyCollection<Node>(new Node[]', '        {']
    for n in data['nodes']:
        strings = ', '.join(cs_string(n.get(k,'')) for k in ('id','owner','page','kind','binding'))
        r = 'new Rectangle('+', '.join(str(v) for v in n['rect'])+')'
        h = 'new Rectangle('+', '.join(str(v) for v in n['hitRect'])+')' if 'hitRect' in n else 'null'
        lines.append('            new Node('+strings+', '+r+', '+h+'),')
    lines += ['        });','        public static bool TryGetPlacement(int width, int height, string owner, out Rectangle bounds)','        {','            bounds = Rectangle.Empty;']
    for v in data['resolutions']:
        lines += ['            if (width == '+str(v['width'])+' && height == '+str(v['height'])+')','            {','                switch (owner)','                {']
        for owner,r in v['placements'].items():
            lines.append('                    case '+cs_string(owner)+': bounds = new Rectangle('+', '.join(str(n) for n in r)+'); return true;')
        lines += ['                }','                return false;','            }']
    lines += ['            return false;','        }','    }','}']
    return '\n'.join(lines)+'\n'


def annotations(data, output):
    from PIL import Image, ImageDraw, ImageFont
    # Annotation font only. Runtime must preserve the configured client font.
    font_path = Path('C:/Windows/Fonts/msyh.ttc')
    font = ImageFont.truetype(str(font_path),12) if font_path.exists() else ImageFont.load_default()
    def text(draw,at,value,colour=(224,203,153)):
        draw.text(at,value,font=font,fill=colour)
    palette={'control':(198,145,60),'stat':(92,155,192),'viewport':(80,169,139),'preview':(137,114,180),'fill':(160,85,85),'caption':(78,86,83),'dynamic-tabs':(177,134,79),'decoration':(73,74,59)}
    def window(image, owner, page, at, annotate=True):
        draw=ImageDraw.Draw(image);x,y=at;w,h=data['owners'][owner]['size']
        draw.rectangle((x,y,x+w-1,y+h-1),fill=(23,24,22),outline=(162,127,72),width=2)
        for n in data['nodes']:
            if n['owner'] != owner or n['page'] not in (page,'all','chrome'):
                continue
            a,b,c,d=n['rect']; colour=palette[n['kind']]
            draw.rectangle((x+a,y+b,x+a+c-1,y+b+d-1),outline=colour)
            if annotate and n['kind'] not in ('decoration','fill') and c>=18 and d>=16:
                label=n.get('label') or n.get('bindingId') or n['id'].split('.')[-1]
                if c<65:label=label[:4]
                # Clip annotation text within its own rectangle, just as hit boxes are bounded.
                layer=Image.new('RGBA',(c,d));ld=ImageDraw.Draw(layer);text(ld,(1,0),label)
                image.paste(layer,(x+a,y+b),layer)
    for v in data['resolutions']:
        image=Image.new('RGB',(v['width'],v['height']),(37,44,38))
        for owner in ('minimap','orb','commands','quickbar','readouts','chat','chatInput','experience','character','inventory'):
            window(image,owner,'character',v['placements'][owner][:2])
        draw=ImageDraw.Draw(image)
        text(draw,(435,250),'LAYOUT ANNOTATION')
        text(draw,(435,270),'NOT A GAME SCREENSHOT')
        text(draw,(435,294),str(v['width'])+' x '+str(v['height']))
        image.save(output/(str(v['width'])+'x'+str(v['height'])+'-character-inventory.png'),compress_level=9)
    for page in [p['id'] for p in data['pages']]:
        image=Image.new('RGB',(436,610),(37,44,38));window(image,'character',page,(12,36))
        text(ImageDraw.Draw(image),(12,8),'LAYOUT / '+page+' / NOT IN-GAME')
        image.save(output/('character-'+page+'.png'),compress_level=9)


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--layout',type=Path,default=ROOT/'build/game-ui/layout-official-v1.json')
    parser.add_argument('--output',type=Path,help='New output directory, never an existing directory')
    parser.add_argument('--guards',action='store_true')
    args=parser.parse_args()
    layout=args.layout.resolve();data=read_json(layout)
    binding_path=(ROOT/data['bindingSource']).resolve()
    require(ROOT in binding_path.parents and sha(binding_path)==data['bindingSourceSha256'], 'Binding source outside workspace or hash mismatch')
    bindings=read_json(binding_path)
    result=validate(data,bindings)
    guards=guard_checks(data,bindings) if args.guards else []
    if args.output:
        output=args.output.absolute()
        require(not output.exists(), 'Output directory already exists')
        output.parent.mkdir(parents=True,exist_ok=True)
        staging=Path(tempfile.mkdtemp(prefix='.'+output.name+'-',dir=output.parent))
        try:
            (staging/'OfficialUiLayout.Generated.cs').write_text(emit_csharp(data,sha(layout)),encoding='utf-8')
            annotation_path=staging/'annotations';annotation_path.mkdir();annotations(data,annotation_path)
            (staging/'validation.json').write_text(json.dumps(dict(kind='official-ui-layout-geometry-validation',releaseAccepted=False,sourceSha256=sha(layout),results=result,guardChecks=guards,limitations=['Geometry/coverage/overlap checks and design annotations only.','No actual renderer, preview pivot, native input, event or packet acceptance.','Generated C# is a proposed candidate, not registered with Client.csproj.']),ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
            require(read_json(layout)==data and sha(binding_path)==data['bindingSourceSha256'], 'Input changed during build')
            require(not output.exists(), 'Output appeared during build')
            staging.rename(output)
        except BaseException:
            shutil.rmtree(staging)
            raise
    print('Layout validated: '+', '.join(str(v)+' '+k for k,v in result.items())+'; '+str(len(guards))+' rejected invalid contracts; runtime not enabled.')


if __name__=='__main__':
    main()
