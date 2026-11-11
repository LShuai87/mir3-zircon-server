import copy
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
TOOL = ROOT / 'build/game-ui/Assemble-CharacterBindings.py'
spec = importlib.util.spec_from_file_location('assembled_character_bindings', TOOL)
assembler = importlib.util.module_from_spec(spec)
spec.loader.exec_module(assembler)


class CharacterContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.layout = assembler.geometry.read_json(ROOT / assembler.LAYOUT)
        cls.routing = assembler.routing.expected_contract()

    def test_sources_and_complete_join(self):
        result = assembler.expected()
        self.assertEqual([r['slotId'] for r in result['equipment']], list(range(16)))
        self.assertEqual(sum(r['targetTab'] == 'character' for r in result['equipment']), 9)
        self.assertEqual(len(result['statBindings']), 57)
        self.assertEqual(len(result['existingControls']), 8)
        self.assertEqual(len(result['additionalBindings']), 4)
        self.assertEqual(len(result['actions']), 9)
        self.assertEqual(len(result['placements']), 6)
        self.assertFalse(result['runtimeReady'])
        self.assertFalse(result['productionBindingAllowed'])
        self.assertFalse(result['layoutFrozen'])
        self.assertEqual(result, json.loads((ROOT / assembler.OUTPUT).read_text(encoding='utf-8')))
        for row in result['equipment']:
            self.assertEqual(row['visualRect'], row['hitRect'])
            self.assertEqual(row['visualRect'], row['iconRect'])
            self.assertEqual(row['visualRect'][2:], [36, 36])
        physical = [r for r in result['statBindings'] if r['stat'] == 'PhysicalResistance']
        self.assertTrue(physical)
        self.assertTrue(all(r['targetTab'] == 'status' for r in physical))
        self.assertTrue(all(r['reuseExistingHandler'] for r in result['actions']))

    def test_reject_independent_routing_or_geometry_drift(self):
        cases = [
            lambda d: d['nodes'].append(copy.deepcopy(d['nodes'][0])),
            lambda d: d['nodes'].remove(next(n for n in d['nodes'] if n['id'] == 'character.equipment.Weapon')),
            lambda d: next(n for n in d['nodes'] if n['id'] == 'character.equipment.Weapon').update(slotId=1),
            lambda d: next(n for n in d['nodes'] if n['id'] == 'character.equipment.Weapon').update(page='extraEquipment'),
            lambda d: next(n for n in d['nodes'] if n['id'] == 'character.stat.DisplayStats.MaxAC').update(dataSource='User.HermitStats'),
            lambda d: next(n for n in d['nodes'] if n['id'] == 'character.stat.DisplayStats.MaxAC').update(formatPolicy='new-calculation'),
            lambda d: next(n for n in d['nodes'] if n['id'] == 'character.action.hermit.MaxAC').update(packet='C.HelmetToggle'),
            lambda d: next(n for n in d['nodes'] if n['id'] == 'character.action.hermit.MaxAC').update(page='character'),
            lambda d: next(n for n in d['nodes'] if n['id'] == 'character.control.ShowHelmetBox').update(action='existing-window-close'),
            lambda d: d.update(runtimeReady=True),
            lambda d: d.update(referenceResolution=[1920, 1080]),
            lambda d: d['scope'].update(npcCards='OfficialV1'),
            lambda d: d['resolutions'].pop(),
        ]
        for mutate in cases:
            with self.subTest(mutation=cases.index(mutate)):
                changed = copy.deepcopy(self.layout)
                mutate(changed)
                with self.assertRaises((ValueError, KeyError)):
                    assembler.join(changed, self.routing)

    def test_reject_non_overlapping_but_semantically_wrong_slots(self):
        # The previous two locations passed bounds, overlap and identity checks.
        for name, old_rect in [('Helmet', [16, 96, 36, 36]), ('Shoes', [110, 340, 36, 36])]:
            with self.subTest(slot=name):
                changed = copy.deepcopy(self.layout)
                node = next(n for n in changed['nodes'] if n['id'] == 'character.equipment.' + name)
                for key in ('rect', 'hitRect', 'iconRect'):
                    node[key] = old_rect[:]
                with self.assertRaisesRegex(ValueError, name + ' must remain'):
                    assembler.join(changed, self.routing)

    def test_stable_packet_and_equipment_identities(self):
        changed = copy.deepcopy(self.routing)
        changed['equipment'][0]['control'] = 'Grid[1]'
        with self.assertRaises(ValueError):
            assembler.join(self.layout, changed)
        changed = copy.deepcopy(self.routing)
        changed['actions'][0]['reuseExistingHandler'] = False
        with self.assertRaises(ValueError):
            assembler.join(self.layout, changed)

    def test_deterministic_publication_and_stale_refusal(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / 'bindings.json'
            command = [sys.executable, str(TOOL), '--output', str(output)]
            self.assertEqual(subprocess.run(command, capture_output=True).returncode, 0)
            content = output.read_bytes()
            self.assertEqual(subprocess.run(command + ['--check'], capture_output=True).returncode, 0)
            self.assertNotEqual(subprocess.run(command, capture_output=True).returncode, 0)
            self.assertEqual(output.read_bytes(), content)
            output.write_bytes(content + b' ')
            self.assertNotEqual(subprocess.run(command + ['--check'], capture_output=True).returncode, 0)
            self.assertEqual(output.read_bytes(), content + b' ')
            self.assertEqual(content, (ROOT / assembler.OUTPUT).read_bytes())

    def test_cannot_replace_input(self):
        original = (ROOT / assembler.LAYOUT).read_bytes()
        command = [sys.executable, str(TOOL), '--output', str(ROOT / assembler.LAYOUT)]
        result = subprocess.run(command, capture_output=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn(b'Output aliases source input', result.stderr)
        self.assertEqual(original, (ROOT / assembler.LAYOUT).read_bytes())


if __name__ == '__main__':
    unittest.main()
