"""Pixel preservation and original warehouse geometry contract checks."""
import importlib.util
import json
from pathlib import Path
import unittest
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('warehouse_composer', ROOT/'build/game-ui/Compose-OfficialUi.py')
ui = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ui)

class WarehousePanelTests(unittest.TestCase):
    def setUp(self):
        self.image = Image.new('RGBA', (11, 13))
        for y in range(13):
            for x in range(11):
                self.image.putpixel((x,y), (x*13,y*11,0,255 if x and y else 0))
        self.spec = dict(type='panel-resize',size=[19,21],sliceInsets=[2,3,4,5],sampling='nearest')

    def test_corners_input_and_binary_alpha_preserved(self):
        before = self.image.tobytes()
        result, grid = ui.transform(self.image,self.spec)
        self.assertIsNone(grid)
        self.assertEqual(before,self.image.tobytes())
        for source,target in [((0,0,2,3),(0,0,2,3)),((7,0,11,3),(15,0,19,3)),
                              ((0,8,2,13),(0,16,2,21)),((7,8,11,13),(15,16,19,21))]:
            self.assertEqual(self.image.crop(source).tobytes(),result.crop(target).tobytes())
        self.assertEqual({0,255},set(result.getchannel('A').tobytes()))

    def test_every_region_matches_independent_nearest_reference(self):
        result,_ = ui.transform(self.image,self.spec)
        # Independent per-pixel inverse coordinates; catches gaps and strip swaps.
        for y in range(21):
            sy = y if y<3 else y-8 if y>=16 else 3+int((y-3+0.5)*5/13)
            for x in range(19):
                sx = x if x<2 else x-8 if x>=15 else 2+int((x-2+0.5)*5/13)
                self.assertEqual(self.image.getpixel((sx,sy)),result.getpixel((x,y)))

    def test_identity_size_preserves_all_pixels(self):
        self.spec['size']=[11,13]
        self.assertEqual(self.image.tobytes(),ui.transform(self.image,self.spec)[0].tobytes())

    def test_ambiguous_or_clipped_geometry_rejected(self):
        for change in [dict(size=[True,21]),dict(size=[4097,21]),dict(size=[6,21]),
                       dict(sliceInsets=[2,3,9,5]),dict(sliceInsets=[2,0,4,5]),
                       dict(sliceInsets=[2,3,False,5]),dict(sampling='bilinear'),dict(extra=1)]:
            with self.subTest(change=change),self.assertRaises(ValueError):
                ui.transform(self.image,dict(self.spec,**change))

    def test_candidate_reserves_original_grid_and_keeps_activation_off(self):
        c=json.loads((ROOT/'build/game-ui/warehouse-layout-candidate.json').read_text())
        self.assertEqual([456,351],c['grid'][2:])
        self.assertEqual([13*35+1,10*35+1],c['grid'][2:])
        self.assertEqual([36,36],c['cellSize'])
        self.assertEqual(13,c['columns'])
        for key in ('layoutFrozen','runtimeReady','productionBindingAllowed'):
            self.assertIs(c[key],False)
        rects=[c[key] for key in ('title','close','filter','grid','scrollbar')]
        for i,(x,y,w,h) in enumerate(rects):
            self.assertGreater(w,0);self.assertGreater(h,0)
            self.assertTrue(0<=x<x+w<=c['windowSize'][0] and 0<=y<y+h<=c['windowSize'][1])
            for a,b,d,e in rects[:i]:
                self.assertTrue(x+w<=a or a+d<=x or y+h<=b or b+e<=y)

if __name__=='__main__':
    unittest.main()
