"""Failure controls for effective-scope coverage and candidate artifact integrity."""
import importlib.util
from pathlib import Path
import subprocess
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location('asset_usage', ROOT / 'build/game-ui/Validate-OfficialUiAssets.py')
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class EffectiveAssetUsageTests(unittest.TestCase):
    def setUp(self):
        self.usage = MODULE.load(ROOT / 'build/game-ui/asset-usage-official-v1.json')
        self.manifest = MODULE.load(ROOT / 'build/game-ui/asset-manifest.json')
        self.selection = MODULE.load(ROOT / 'build/game-ui/npc-shop-selection.json')
        self.recipe = MODULE.load(ROOT / 'build/game-ui/npc-shop-composition-recipe.json')

    def check(self, source_override=None):
        def read(path):
            text = (ROOT / path).read_text('utf-8-sig')
            return source_override(text) if source_override and path == self.usage['entrypoint'] else text
        return MODULE.validate_contract(self.usage, self.manifest, self.selection, self.recipe, read)

    def test_current_inventory_does_not_claim_scope_complete(self):
        report = self.check()
        self.assertEqual((report['sceneReferencedFrames'], report['packageFrames'], report['uninstalledButtonFrames']), (51, 93, 40))
        self.assertFalse(report['requirementCoverageComplete'])
        self.assertFalse(report['gateClosureAllowed'])
        self.assertEqual(report['outstanding'][0]['surface'], 'warehouse')

    def test_deleted_surface_fails(self):
        self.usage['surfaces'] = [s for s in self.usage['surfaces'] if s['id'] != 'warehouse']
        with self.assertRaisesRegex(ValueError, 'missing a surface'):
            self.check()

    def test_warehouse_gap_cannot_be_silently_erased(self):
        self.usage['outstanding'] = []
        with self.assertRaisesRegex(ValueError, 'Warehouse artwork is not frozen'):
            self.check()

    def test_uninstalled_art_cannot_be_reported_as_installed(self):
        self.usage['surfaces'][0]['buttonGroups'].append('hud-horse-icon')
        with self.assertRaisesRegex(ValueError, 'Scene button uses differ'):
            self.check()

    def test_unaccounted_packaged_art_fails(self):
        del self.usage['uninstalledButtonGroups']['hud-horse-icon']
        with self.assertRaisesRegex(ValueError, 'Uninstalled buttons not accounted'):
            self.check()

    def test_missing_live_role_fails(self):
        self.usage['surfaces'][0]['roles'].remove('hud-hp-fill')
        with self.assertRaisesRegex(ValueError, 'Scene frame uses differ'):
            self.check()

    def test_source_identity_swap_fails(self):
        self.manifest['assets'][0]['sourceIndex'] += 1
        with self.assertRaisesRegex(ValueError, 'Source identity differs'):
            self.check()

    def test_nonbinary_blend_declaration_fails(self):
        self.manifest['assets'][0]['blendMode'] = 'Additive'
        with self.assertRaisesRegex(ValueError, 'Frame render metadata differs'):
            self.check()

    def test_button_state_swap_fails(self):
        states = self.manifest['buttons']['hud-shop-icon']
        states['normal'], states['hover'] = states['hover'], states['normal']
        with self.assertRaisesRegex(ValueError, 'Button frame identity differs'):
            self.check()

    def test_tint_change_fails(self):
        self.recipe['buttonTint']['disabled'] = [100, 100]
        with self.assertRaisesRegex(ValueError, 'Unreviewed tint rules'):
            self.check()

    def test_stale_control_reuse_evidence_fails(self):
        self.usage['surfaces'][0]['reuse'][0]['anchor'] = 'missing-control-evidence'
        with self.assertRaisesRegex(ValueError, 'Reused-control evidence is stale'):
            self.check()

    def test_new_frame_binding_requires_review(self):
        with self.assertRaisesRegex(ValueError, 'Scene frame uses differ'):
            self.check(lambda text: text + '\nFrame("unexpected-art");')

    def test_dynamic_frame_expression_requires_review(self):
        with self.assertRaisesRegex(ValueError, 'Unrecognized frame expression'):
            self.check(lambda text: text + '\nFrame(unknownVariable);')

    def test_unknown_button_expression_requires_review(self):
        with self.assertRaisesRegex(ValueError, 'Unrecognized catalog button access'):
            self.check(lambda text: text + '\nGameUiOfficialV1Catalog.Buttons[unreviewed];')

    def test_runtime_activation_cannot_be_authorized(self):
        self.usage['productionBindingAllowed'] = True
        with self.assertRaisesRegex(ValueError, 'cannot authorize activation'):
            self.check()

    def test_excluded_npc_scope_cannot_expand(self):
        self.usage['scope']['npcCards'] = 'OfficialV1'
        with self.assertRaisesRegex(ValueError, 'Scope changed'):
            self.check()

    def test_require_complete_cli_refuses_current_candidate(self):
        result = subprocess.run([sys.executable, str(ROOT / 'build/game-ui/Validate-OfficialUiAssets.py'), '--require-complete'],
                                cwd=ROOT, text=True, capture_output=True, encoding='utf-8')
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn('Effective-scope acceptance is incomplete', result.stderr)

    def test_bc1_payload_mutation_fails_even_with_new_package_digest(self):
        # Synthetic one-pixel fixture tests the actual container parser independently of the real candidate.
        import struct
        payload = bytes(8)
        raw = struct.pack('<ii', 30, 1) + b'\1' + struct.pack('<ihhhhBhhhhhh', 34, 1, 1, *([0] * 9)) + payload
        manifest = dict(package=dict(byteLength=len(raw), sha256=MODULE.sha(raw), slotCount=1),
                        assets=[dict(role='one-pixel', outputIndex=0, logicalSize=[1, 1], dataSha256=MODULE.sha(payload))])
        self.assertEqual(MODULE.verify_package(raw, manifest), 1)
        corrupted = raw[:-1] + b'\1'
        manifest['package']['sha256'] = MODULE.sha(corrupted)
        with self.assertRaisesRegex(ValueError, 'Zl payload hash differs'):
            MODULE.verify_package(corrupted, manifest)


if __name__ == '__main__':
    unittest.main(verbosity=2)
