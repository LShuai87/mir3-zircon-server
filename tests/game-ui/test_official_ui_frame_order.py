"""Stable semantic frame indices when adding a warehouse to the shared package."""
import importlib.util
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location('frame_order', ROOT/'build/game-ui/Compose-OfficialUi.py')
UI = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(UI)

class FrameOrderTests(unittest.TestCase):
    def test_reorder_preserves_payload_metadata_and_input(self):
        records = [dict(role='new', outputIndex=0, payload='warehouse'), dict(role='old', outputIndex=1, payload='existing')]
        actual = UI.order_frames(records, ['old', 'new'])
        self.assertEqual(actual, [dict(role='old', outputIndex=0, payload='existing'), dict(role='new', outputIndex=1, payload='warehouse')])
        self.assertEqual(records[0], dict(role='new', outputIndex=0, payload='warehouse'))
        self.assertIsNot(actual[1], records[0])

    def test_no_order_preserves_legacy_composition(self):
        records = [dict(role='old', outputIndex=0)]
        self.assertIs(UI.order_frames(records, None), records)

    def test_incomplete_duplicate_unknown_and_invalid_orders_fail(self):
        records = [dict(role='old', outputIndex=0), dict(role='new', outputIndex=1)]
        for order in [[], ['old'], ['old','old'], ['old','unknown'], ['old','new','extra'], 'old', [False,'new']]:
            with self.subTest(order=order), self.assertRaises(ValueError):
                UI.order_frames(records, order)

    def test_combined_recipe_keeps_all_existing_indices(self):
        import json
        recipe = json.loads((ROOT/'build/game-ui/warehouse-combined-composition-recipe.json').read_text('utf-8'))
        old = json.loads((ROOT/'docs/design/assets/official-ingame-ui/acceptance/warehouse-package/source-before/build/game-ui/asset-manifest.json').read_text('utf-8'))
        self.assertEqual(recipe['frameOrder'], [a['role'] for a in old['assets']] + ['warehouse-background'])
        self.assertEqual(len(recipe['frameOrder']), 94)
