"""Composition contract regressions with small art fixtures and no live client."""
import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('composed_ui', ROOT/'build/game-ui/Compose-OfficialUi.py')
ui = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ui)

class CanvasTests(unittest.TestCase):
    def test_native_art_and_enclosed_black_survive_padding(self):
        image = Image.new('RGB', (5, 5), 'black')
        for x, y in [(1,1),(2,1),(3,1),(1,2),(3,2),(1,3),(2,3),(3,3)]:
            image.putpixel((x,y),(90,70,40))
        source = ui.mask_edges(image, []); before = source.tobytes()
        result, grid = ui.transform(source, dict(type='canvas',size=[8,9],origin=[2,1]))
        self.assertIsNone(grid)
        self.assertEqual(before, source.tobytes())
        self.assertEqual(before, result.crop((2,1,7,6)).tobytes())
        self.assertEqual((0,0,0,255), result.getpixel((4,3)))
        for y in range(9):
            for x in range(8):
                if not (2 <= x < 7 and 1 <= y < 6):
                    self.assertEqual((0,0,0,0),result.getpixel((x,y)))
    def test_rejects_clipping_fractional_or_ambiguous_geometry(self):
        image = Image.new('RGBA',(5,5),(80,60,20,255))
        valid = dict(type='canvas',size=[8,9],origin=[2,1])
        for change in [dict(extra=1),dict(size=[True,9]),dict(size=[0,9]),dict(size=[4097,9]),
                       dict(size=[8]),dict(origin=[-1,0]),dict(origin=[0.5,0]),
                       dict(origin=[True,0]),dict(origin=[4,1]),dict(origin=[2,5])]:
            with self.subTest(change=change), self.assertRaises(ValueError):
                ui.transform(image,dict(valid,**change))


class ProgressResizeTests(unittest.TestCase):
    def test_build_resize_preserves_binary_alpha_source_colours_and_source(self):
        image = Image.new('RGBA', (611, 10), (0, 0, 0, 0))
        for y in range(1, 9):
            for x in range(1, 610):
                image.putpixel((x, y), (210, 180, 100, 255) if x % 61 == 0 else (30, 130, 40, 255))
        before = image.tobytes()
        result = ui.derive_frame(image, dict(type='segmented-frame', role='progress', sourceRole='source', size=[574, 8], segments=10, sampling='nearest'))
        self.assertEqual((574, 8), result.size)
        self.assertEqual(before, image.tobytes())
        colours = lambda im: {im.getpixel((x, y)) for y in range(im.height) for x in range(im.width)}
        self.assertTrue(colours(result) <= colours(image))
        self.assertEqual({0, 255}, set(result.getchannel('A').tobytes()))
        # All nine internal dividers survive at this exact source/target ratio.
        dividers = [x for x in range(result.width) if result.getpixel((x, 4)) == (210, 180, 100, 255)]
        self.assertEqual(9, len(dividers))
    def test_invalid_resize_is_rejected(self):
        image = Image.new('RGBA', (611, 10))
        valid = dict(type='segmented-frame', role='progress', sourceRole='source', size=[574, 8], segments=10, sampling='nearest')
        for invalid in ({'size': [True, 8]}, {'size': [0, 8]}, {'size': [4097, 8]}, {'size': [8]}, {'size': '8x8'}, {'sampling': 'linear'}, {'extra': 1}, {'segments': True}, {'segments': 0}, {'segments': 64}, {'size': [30, 8]}):
            with self.subTest(invalid=invalid), self.assertRaises(ValueError):
                ui.derive_frame(image, dict(valid, **invalid))


class PanelRepairTests(unittest.TestCase):
    def fixture(self):
        image = Image.new('RGBA', (12, 12))
        for y in range(12):
            for x in range(12): image.putpixel((x, y), (x*15, y*15, 60, 255))
        spec = dict(type='panel-repair', repairBlend='edge-matched-rgb', sourcePatch=[0,0,2,2], targets=[[4,4,3,3]],
                    stamps=[dict(source=[4,4,2,2], target=[8,8,2,2])], crop=[1,1,10,10])
        return image, spec
    def test_repairs_only_declared_areas_and_samples_original(self):
        image, spec = self.fixture(); before = image.tobytes()
        result, grid = ui.transform(image, spec)
        self.assertIsNone(grid); self.assertEqual((10,10), result.size)
        self.assertEqual(before, image.tobytes())
        for y in range(1,11):
            for x in range(1,11):
                expected = image.getpixel((x,y))
                if 4 <= x < 7 and 4 <= y < 7:
                    self.assertEqual(255, result.getpixel((x-1,y-1))[3])
                    continue
                if 8 <= x < 10 and 8 <= y < 10: expected = image.getpixel((x-4,y-4))
                self.assertEqual(expected, result.getpixel((x-1,y-1)))
    def test_edge_matching_preserves_original_boundary_and_alpha(self):
        original = Image.new('RGBA', (7,9), (80,60,40,255))
        texture = Image.new('RGBA', original.size, (10,20,30,255))
        texture.putpixel((3,4),(18,25,33,255))
        result = ui.match_repair_edges(original,texture)
        for x in range(7):
            for y in range(9):
                self.assertEqual(255,result.getpixel((x,y))[3])
                if x in (0,6) or y in (0,8): self.assertEqual(original.getpixel((x,y)),result.getpixel((x,y)))
        self.assertGreater(result.getpixel((3,4))[0],80)
        self.assertEqual((10,20,30,255),texture.getpixel((0,0)))

    def test_rejects_ambiguous_or_out_of_bounds_stamps(self):
        image, valid = self.fixture()
        changes = [dict(repairBlend='unknown'), dict(targets=[[4,4,1,3]]), dict(crop=[0,0,13,12]), dict(extra=True), dict(stamps=[]),
                   dict(stamps=[dict(source=[0,0,2,2],target=[11,11,2,2])]),
                   dict(stamps=valid['stamps']*2), dict(stamps=[dict(source=[0,0,2,2],target=[8,8,2,2],extra=True)])]
        for change in changes:
            with self.subTest(change=change), self.assertRaises(ValueError): ui.transform(image, dict(valid, **change))
    def test_rejects_transparent_sampling_and_preserves_binary_alpha_on_resize(self):
        image, spec = self.fixture(); image.putpixel((4,4),(0,0,0,0))
        with self.assertRaisesRegex(ValueError, 'opaque'): ui.transform(image, spec)
        image, spec = self.fixture(); spec['stamps'][0]['target']=[8,8,3,3]
        result, _ = ui.transform(image, spec)
        self.assertEqual({255}, set(result.getchannel('A').tobytes()))


class CompositionTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory(prefix='ZirconComposition-')
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.decoded = self.root/'decoded'
        (self.decoded/'UI').mkdir(parents=True)
        self.output = self.root/'output'
        image = Image.new('RGB',(9,9),'black')
        for y in range(1,8):
            for x in range(1,8):
                if x in (1,7) or y in (1,7): image.putpixel((x,y),(70,80,90))
        png = self.decoded/'UI/1.png'
        image.save(png)
        self.source = dict(role='test-button',sourceLibrary='UI',sourceIndex=1,logicalSize=[9,9],sourceOffset=[-2,3],pngSha256=ui.sha(png.read_bytes()))
        self.selection = dict(schemaVersion=1,assets=[self.source])
        (self.decoded/'selection.json').write_text(json.dumps(self.selection))
        item = dict(png='UI/1.png',pngSha256=self.source['pngSha256'],width=9,height=9,offsetX=-2,offsetY=3)
        self.manifest = dict(schemaVersion=1,kind='official-ui-decoded-reference',alphaVerified=False,libraries=[dict(name='UI',items=[None,item])])
        (self.decoded/'manifest.json').write_text(json.dumps(self.manifest))
        self.recipe = dict(schemaVersion=1,kind='official-ui-composition-recipe',productionBindingAllowed=False,
            selectionSha256=ui.sha((self.decoded/'selection.json').read_bytes()),buttonTint={'hover':[115,100],'pressed':[75,100],'disabled':[60,100]},
            assets=[dict(role='test-button',sourceRole='test-button',alphaPolicy='edge-connected-exact-black',clearRects=[],transform={'type':'identity'},button=True)],compositions=[])
        self.recipe_path = self.root/'recipe.json'
        self.save()
    def save(self): self.recipe_path.write_text(json.dumps(self.recipe))
    def run_compose(self, output=None):
        return ui.compose(self.decoded, output or self.output, self.recipe_path)
    def reject(self, pattern):
        self.save()
        with self.assertRaisesRegex(ValueError,pattern): self.run_compose()
        self.assertFalse(self.output.exists())
        self.assertFalse(list(self.root.glob('.output.staging-*')))
    def test_edge_mask_preserves_enclosed_black_and_source_rgb(self):
        result=self.run_compose()
        with Image.open(self.output/'Assets/test-button.png') as image:
            self.assertEqual((0,0,0,0),image.getpixel((0,0)))
            self.assertEqual((0,0,0,255),image.getpixel((4,4)))
            self.assertEqual((70,80,90,255),image.getpixel((1,1)))
        self.assertEqual([0,0],result['assets'][0]['outputOffset'])
        self.assertEqual([-2,3],result['assets'][0]['sourceOffset'])
        self.assertFalse(result['runtimeReady'])
    def test_black_diagonal_is_not_edge_connected(self):
        image=Image.new('RGB',(3,3),'white'); image.putpixel((0,0),(0,0,0)); image.putpixel((1,1),(0,0,0))
        out=ui.mask_edges(image,[])
        self.assertEqual(255,out.getpixel((1,1))[3])
    def test_near_black_is_not_erased(self):
        image=Image.new('RGB',(3,3),(1,0,0)); out=ui.mask_edges(image,[])
        self.assertEqual((255,255),out.getchannel('A').getextrema())
    def test_explicit_hole_is_bounded_and_black_only(self):
        self.recipe['assets'][0]['clearRects']=[[2,2,5,5]]
        self.save(); self.run_compose()
        with Image.open(self.output/'Assets/test-button.png') as image: self.assertEqual(0,image.getpixel((4,4))[3])
    def test_explicit_hole_rejects_artwork(self):
        self.recipe['assets'][0]['clearRects']=[[1,1,7,7]]; self.reject('non-black')
    def test_explicit_hole_rejects_oob(self):
        self.recipe['assets'][0]['clearRects']=[[8,8,2,2]]; self.reject('outside')
    def test_clear_rects_requires_list(self):
        self.recipe['assets'][0]['clearRects']={}; self.reject('must be a list')
    def test_button_alpha_is_identical_in_all_states(self):
        result=self.run_compose(); masks=[]
        for a in result['assets']:
            with Image.open(self.output/a['png']) as im: masks.append(im.getchannel('A').tobytes())
        self.assertEqual(4,len(masks)); self.assertTrue(all(m==masks[0] for m in masks))
        self.assertEqual({'normal','hover','pressed','disabled'},set(result['buttons']['test-button']))
    def test_deterministic_outputs(self):
        self.run_compose(); other=self.root/'other'; self.run_compose(other)
        files=lambda p:{str(f.relative_to(p)):f.read_bytes() for f in p.rglob('*') if f.is_file()}
        self.assertEqual(files(self.output),files(other))
    def test_changed_png_fails(self):
        (self.decoded/'UI/1.png').write_bytes(b'changed'); self.reject('PNG hash')
    def test_changed_selection_fails(self):
        (self.decoded/'selection.json').write_text('{}'); self.reject('selection hash')
    def test_wrong_source_offset_fails(self):
        self.manifest['libraries'][0]['items'][1]['offsetX']=0
        (self.decoded/'manifest.json').write_text(json.dumps(self.manifest)); self.reject('metadata')
    def test_duplicate_source_fails(self):
        asset=copy.deepcopy(self.recipe['assets'][0]); asset['role']='different'; self.recipe['assets'].append(asset); self.reject('Duplicate source')
    def test_nonbutton_source_can_supply_separate_page_layers(self):
        self.recipe['assets'][0]['button'] = False
        layer = copy.deepcopy(self.recipe['assets'][0])
        layer['role'] = 'page-layer'
        self.recipe['assets'].append(layer)
        self.save()
        result = self.run_compose()
        self.assertEqual(['test-button', 'page-layer'], [a['role'] for a in result['assets']])
        self.assertEqual({}, result['buttons'])
        self.assertEqual(result['assets'][0]['rgbaSha256'], result['assets'][1]['rgbaSha256'])

    def test_mixed_button_nonbutton_source_reuse_fails(self):
        layer = copy.deepcopy(self.recipe['assets'][0])
        layer.update(role='page-layer', button=False)
        self.recipe['assets'].append(layer)
        self.reject('Duplicate source')

    def test_unsafe_role_fails(self):
        self.recipe['assets'][0]['role']='../unsafe'; self.reject('semantic asset')
    def test_implicit_alpha_fails(self):
        self.recipe['assets'][0]['alphaPolicy']='black-key'; self.reject('alpha policy')
    def test_implicit_tint_fails(self):
        self.recipe['buttonTint']['hover']=[116,100]; self.reject('tint policy')
    def test_existing_output_fails_without_changes(self):
        self.output.mkdir(); marker=self.output/'keep'; marker.write_text('unchanged')
        with self.assertRaisesRegex(ValueError,'must be new'): self.run_compose()
        self.assertEqual('unchanged',marker.read_text())
    def test_nested_output_fails(self):
        with self.assertRaisesRegex(ValueError,'disjoint'): self.run_compose(self.decoded/'output')
    def test_input_mutation_before_publication_fails_atomically(self):
        original=ui.transform
        def mutate(image,spec):
            self.recipe_path.write_bytes(b'changed'); return original(image,spec)
        with patch.object(ui,'transform',side_effect=mutate):
            with self.assertRaisesRegex(ValueError,'changed during'): self.run_compose()
        self.assertFalse(self.output.exists()); self.assertFalse(list(self.root.glob('.output.staging-*')))
    def test_inventory_geometry_and_native_header_footer(self):
        image=Image.new('RGBA',(276,494),(50,60,70,255))
        image.putpixel((12,20),(1,2,3,255)); image.putpixel((12,480),(4,5,6,255))
        spec=dict(type='inventory-grid',sourceArea=[15,49,246,328],sourceCell=[15,49,41,41],columns=7,rows=7,cellSize=36,step=35)
        out,grid=ui.transform(image,spec)
        self.assertEqual((276,412),out.size); self.assertEqual(49,len(grid['cells']))
        self.assertEqual([225,259,36,36],grid['cells'][-1])
        self.assertEqual((1,2,3,255),out.getpixel((12,20))); self.assertEqual((4,5,6,255),out.getpixel((12,398)))
    def test_quickbar_geometry_and_native_caps(self):
        image=Image.new('RGBA',(518,44),(50,60,70,255)); image.putpixel((4,20),(1,2,3,255)); image.putpixel((500,20),(4,5,6,255))
        spec=dict(type='quickbar-grid',sourceArea=[60,3,410,40],sourceCell=[60,3,41,40],columns=10,cellSize=36,step=35)
        out,grid=ui.transform(image,spec)
        self.assertEqual((459,44),out.size); self.assertEqual(10,len(grid['cells']))
        self.assertEqual((1,2,3,255),out.getpixel((4,20))); self.assertEqual((4,5,6,255),out.getpixel((441,20)))
    def test_grid_rejects_changed_count_pitch_and_unknown_fields(self):
        base=dict(type='inventory-grid',sourceArea=[15,49,246,328],sourceCell=[15,49,41,41],columns=7,rows=7,cellSize=36,step=35)
        for key,value in [('columns',6),('step',36),('cellSize',35),('extra',1)]:
            spec={**base,key:value}
            with self.assertRaises(ValueError): ui.transform(Image.new('RGBA',(276,494)),spec)

    def derived_spec(self, **changes):
        return dict(dict(type='border-frame', role='chat-history-frame', sourceRole='test-button', size=[34, 26], sliceInsets=[1, 1, 1, 1], horizontalSample=[1, 5]), **changes)
    def derived_recipe(self, **changes):
        self.recipe['assets'][0]['button'] = False
        self.recipe['derivedFrames'] = [self.derived_spec(**changes)]
        self.save()
    def test_derived_frame_keeps_corners_edges_and_transparent_interior(self):
        source = Image.new('RGBA', (9, 9))
        for y in range(9):
            for x in range(9): source.putpixel((x,y), (20+x*15,30+y*15,40,255))
        spec = self.derived_spec()
        result = ui.border_frame(source, spec)
        for target, original in [((0,0),(0,0)),((33,0),(8,0)),((0,25),(0,8)),((33,25),(8,8))]:
            self.assertEqual(source.getpixel(original),result.getpixel(target))
        self.assertEqual((0,0,0,0), result.getpixel((16,13)))
        self.assertTrue(all(a == 0 for a in result.crop((1,1,33,25)).getchannel('A').tobytes()))
        # A fully opaque source interior must never cover messages or buttons.
        self.assertEqual(2*34+2*24, sum(a == 255 for a in result.getchannel('A').tobytes()))
        source_edge_colours = {source.getpixel((x,0)) for x in range(1,6)}
        self.assertTrue(all(result.getpixel((x,0)) in source_edge_colours for x in range(1,33)))
    def test_derived_frame_keeps_binary_alpha_and_transparent_corners(self):
        source = Image.new('RGBA', (9,9), (40,50,60,255))
        source.putpixel((0,0),(0,0,0,0)); source.putpixel((2,0),(0,0,0,0))
        result = ui.border_frame(source, self.derived_spec())
        self.assertEqual((0,0,0,0),result.getpixel((0,0)))
        self.assertEqual({0,255},set(result.getchannel('A').tobytes()))
    def test_derived_frame_pipeline_records_provenance_and_zero_offset(self):
        self.derived_recipe()
        result = self.run_compose()
        self.assertEqual(2,len(result['assets']))
        record = result['assets'][1]
        self.assertEqual('chat-history-frame',record['role'])
        self.assertEqual('test-button',record['sourceRole'])
        self.assertEqual(self.source['pngSha256'],record['sourcePngSha256'])
        self.assertEqual([-2,3],record['sourceOffset'])
        self.assertEqual([0,0],record['outputOffset'])
        self.assertEqual([36,28],record['textureSize'])
        self.assertEqual(self.recipe['derivedFrames'],result['derivedFrames'])
        self.assertFalse(result['productionBindingAllowed'])
    def test_derived_frame_rejects_button_as_source(self):
        self.recipe['derivedFrames'] = [self.derived_spec()]
        self.reject('non-button')
        self.assertFalse(self.output.exists())
    def test_derived_frame_rejects_unknown_or_derived_source(self):
        self.derived_recipe(sourceRole='missing')
        self.reject('non-button')
        self.recipe['derivedFrames'] = [self.derived_spec(),self.derived_spec(role='second',sourceRole='chat-history-frame')]
        self.reject('non-button')
    def test_derived_frame_rejects_duplicate_unsafe_role_and_fields(self):
        for changes in [dict(role='test-button'),dict(role='../bad'),dict(role=42)]:
            with self.subTest(changes=changes):
                self.derived_recipe(**changes); self.reject('derived role')
        self.derived_recipe(extra=1); self.reject('frame fields')
    def test_derived_frame_rejects_unbounded_or_empty_geometry(self):
        for changes in [dict(size=[0,22]),dict(size=[4097,22]),dict(size=[True,22]),dict(size=[2,22]),dict(size='22'),dict(sliceInsets=[0,1,1,1]),dict(sliceInsets=[1,4,1,5]),dict(sliceInsets=[1,1,1]),dict(horizontalSample=[0,2]),dict(horizontalSample=[1,0]),dict(horizontalSample=[1,8]),dict(horizontalSample=[True,3])]:
            with self.subTest(changes=changes):
                self.derived_recipe(**changes); self.reject('derived frame|Derived frame')
                self.assertFalse(self.output.exists())
    def test_derived_frame_list_limit_and_type(self):
        for value in [None,{},[self.derived_spec()]*33]:
            self.recipe['derivedFrames']=value; self.reject('frame list')
    def test_derived_frame_is_deterministic(self):
        self.derived_recipe(); self.run_compose(); second=self.root/'second'; self.run_compose(second)
        for relative in ['composed-manifest.json','Assets/chat-history-frame.png','Preview/chat-history-frame.png']:
            self.assertEqual((self.output/relative).read_bytes(),(second/relative).read_bytes())
    def test_actual_recipe_surface_sizes_match_logical_layout(self):
        recipe=json.loads((ROOT/'build/game-ui/composition-recipe.json').read_text(encoding='utf-8-sig'))
        layout=json.loads((ROOT/'build/game-ui/layout-official-v1.json').read_text(encoding='utf-8-sig'))
        owners=layout['owners']
        for role, owner in [('chat-history-frame','chat'),('chat-input-frame','chatInput'),('hud-experience-track','experience'),('hud-experience-fill','experience')]:
            frame=next(f for f in recipe['derivedFrames'] if f['role']==role)
            self.assertEqual(owners[owner]['size'],frame['size'])
        command = next(a for a in recipe['assets'] if a['role'] == 'hud-command-frame')
        self.assertEqual('hud-command-shell', command['sourceRole'])
        self.assertEqual(dict(type='canvas', size=owners['commands']['size'], origin=[0,27]), command['transform'])
        self.assertFalse(command['button'])
        self.assertFalse(any(f['role'] == 'hud-command-frame' for f in recipe['derivedFrames']))

    def cover_spec(self, **changes):
        return dict(dict(type='texture-cover', sourcePatch=[1,1,2,3], targets=[[1,1,7,7]]), **changes)
    def test_texture_cover_tiles_native_pixels_and_samples_original_once(self):
        image=Image.new('RGBA',(9,9),(20,30,40,255))
        for y in range(3):
            for x in range(2): image.putpixel((1+x,1+y),(50+x*30,60+y*20,70,255))
        original=image.tobytes()
        result,grid=ui.transform(image,self.cover_spec())
        self.assertIsNone(grid)
        self.assertEqual(original,image.tobytes())
        for y in range(9):
            for x in range(9):
                expected=image.getpixel((1+(x-1)%2,1+(y-1)%3)) if 1 <= x < 8 and 1 <= y < 8 else image.getpixel((x,y))
                self.assertEqual(expected,result.getpixel((x,y)))
    def test_texture_cover_separate_targets_leave_every_other_pixel_unchanged(self):
        image=Image.new('RGBA',(9,9),(40,50,60,255)); image.putpixel((0,0),(0,0,0,0))
        image.putpixel((1,1),(120,130,140,255))
        result,_=ui.transform(image,self.cover_spec(sourcePatch=[1,1,1,1],targets=[[2,2,2,2],[6,5,2,3]]))
        for y in range(9):
            for x in range(9):
                covered=(2 <= x < 4 and 2 <= y < 4) or (6 <= x < 8 and 5 <= y < 8)
                self.assertEqual(image.getpixel((1,1)) if covered else image.getpixel((x,y)),result.getpixel((x,y)))
    def test_texture_cover_rejects_unknown_fields(self):
        for extra in [dict(extra=1),dict(size=[10,10])]:
            with self.subTest(extra=extra):
                with self.assertRaisesRegex(ValueError,'texture-cover fields'): ui.transform(Image.new('RGBA',(9,9)),self.cover_spec(**extra))
    def test_texture_cover_rejects_invalid_source_and_target_rectangles(self):
        image=Image.new('RGBA',(9,9),(40,50,60,255))
        for rect in [None,[1,1,0,2],[-1,1,2,2],[1,1,9,2],[True,1,2,2],[1,1,2]]:
            for key,value in [('sourcePatch',rect),('targets',[rect])]:
                with self.subTest(key=key,rect=rect):
                    with self.assertRaisesRegex(ValueError,'rectangle'): ui.transform(image,self.cover_spec(**{key:value}))
    def test_texture_cover_rejects_unbounded_target_lists(self):
        for targets in [None,{},[],[[1,1,1,1]]*65]:
            with self.subTest(targets=targets):
                with self.assertRaisesRegex(ValueError,'targets'): ui.transform(Image.new('RGBA',(9,9),(40,50,60,255)),self.cover_spec(targets=targets))
    def test_texture_cover_rejects_transparent_patch_and_silhouette_overwrite(self):
        for alpha in [0,128]:
            image=Image.new('RGBA',(9,9),(40,50,60,255)); image.putpixel((1,1),(0,0,0,alpha))
            with self.assertRaisesRegex(ValueError,'patch must be opaque'): ui.transform(image,self.cover_spec())
            with self.assertRaisesRegex(ValueError,'target must be opaque'): ui.transform(image,self.cover_spec(sourcePatch=[2,2,1,1]))
    def test_texture_cover_rejects_overlap_but_allows_adjacent_targets(self):
        image=Image.new('RGBA',(9,9),(40,50,60,255))
        with self.assertRaisesRegex(ValueError,'overlap'): ui.transform(image,self.cover_spec(targets=[[1,1,3,3],[3,3,3,3]]))
        ui.transform(image,self.cover_spec(targets=[[1,1,3,3],[4,1,3,3]]))
    def test_texture_cover_pipeline_preserves_alpha_and_original_source_provenance(self):
        self.recipe['assets'][0].update(button=False,transform=self.cover_spec(sourcePatch=[1,1,1,1],targets=[[2,2,5,5]]))
        self.save(); result=self.run_compose()
        with Image.open(self.output/'Assets/test-button.png') as image:
            self.assertEqual((70,80,90,255),image.getpixel((4,4)))
            self.assertEqual((0,0,0,0),image.getpixel((0,0)))
        self.assertEqual(self.source['pngSha256'],result['assets'][0]['sourcePngSha256'])
        self.assertEqual(ui.sha(self.recipe_path.read_bytes()),result['recipeSha256'])
        self.assertFalse(result['runtimeReady'])
    def crop_spec(self, **changes):
        return dict(dict(type='crop-frame',role='equipment-cell-frame',sourceRole='test-button',sourceArea=[1,1,4,5]),**changes)
    def test_cropped_frame_keeps_native_pixels_and_binary_alpha(self):
        image=Image.new('RGBA',(9,9),(40,50,60,255)); image.putpixel((2,2),(0,0,0,0))
        original=image.tobytes(); result=ui.derive_frame(image,self.crop_spec())
        self.assertEqual((4,5),result.size)
        self.assertEqual(image.crop((1,1,5,6)).tobytes(),result.tobytes())
        self.assertEqual(original,image.tobytes())
        self.assertEqual({0,255},set(result.getchannel('A').tobytes()))
    def test_cropped_frame_rejects_invalid_fields_type_and_geometry(self):
        image=Image.new('RGBA',(9,9),(40,50,60,255))
        for changes in [dict(extra=1),dict(sourceArea=[8,8,2,2]),dict(sourceArea=[1,True,2,2]),dict(type='unknown'),dict(sourceArea=[1,1,0,2])]:
            with self.subTest(changes=changes):
                with self.assertRaises(ValueError): ui.derive_frame(image,self.crop_spec(**changes))
        with self.assertRaisesRegex(ValueError,'too large'): ui.derive_frame(Image.new('RGBA',(4097,1)),self.crop_spec(sourceArea=[0,0,4097,1]))
    def test_cropped_frame_pipeline_keeps_source_provenance_and_zero_output_offset(self):
        self.recipe['assets'][0]['button']=False; self.recipe['derivedFrames']=[self.crop_spec()]
        self.save(); result=self.run_compose(); record=result['assets'][1]
        self.assertEqual([4,5],record['logicalSize']); self.assertEqual([4,8],record['textureSize'])
        self.assertEqual(self.source['pngSha256'],record['sourcePngSha256'])
        self.assertEqual([-2,3],record['sourceOffset']); self.assertEqual([0,0],record['outputOffset'])
        self.assertEqual(self.recipe['derivedFrames'],result['derivedFrames'])
    def test_cropped_frame_and_cover_pipeline_is_deterministic(self):
        self.recipe['assets'][0].update(button=False,transform=self.cover_spec(sourcePatch=[1,1,1,1],targets=[[2,2,5,5]]))
        self.recipe['derivedFrames']=[self.crop_spec()]; self.save(); self.run_compose()
        second=self.root/'second'; self.run_compose(second)
        files=lambda p:{str(f.relative_to(p)):f.read_bytes() for f in p.rglob('*') if f.is_file()}
        self.assertEqual(files(self.output),files(second))
    def test_actual_character_cleanup_and_slot_frame_are_explicit(self):
        recipe=json.loads((ROOT/'build/game-ui/composition-recipe.json').read_text(encoding='utf-8-sig'))
        panel=next(a for a in recipe['assets'] if a['role']=='character')
        self.assertEqual(dict(type='texture-cover',sourcePatch=[24,452,56,72],targets=[[8,90,396,461]]),panel['transform'])
        frame=next(f for f in recipe['derivedFrames'] if f['role']=='equipment-cell-frame')
        self.assertEqual(dict(type='crop-frame',role='equipment-cell-frame',sourceRole='inventory',sourceArea=[15,49,36,36]),frame)

if __name__=='__main__': unittest.main()
