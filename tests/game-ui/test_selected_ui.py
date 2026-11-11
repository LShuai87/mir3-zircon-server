"""Behavioral regressions for selected official UI conversion; no real player data."""
import importlib.util
import json
from pathlib import Path
import struct
import tempfile
import unittest
from unittest.mock import patch
import zlib

MODULE = Path(__file__).resolve().parents[2] / 'build/game-ui/Select-OfficialUi.py'
spec = importlib.util.spec_from_file_location('selected_ui_decoder', MODULE)
selected = importlib.util.module_from_spec(spec)
spec.loader.exec_module(selected)


class SelectionTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='ZirconSelectedUi-')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source = self.root / 'source'
        self.source.mkdir()
        self.output = self.root / 'output'
        self.baseline = self.root / 'baseline.json'
        self.selection = self.root / 'selection.json'
        raw = struct.pack('<8H', 0, 0xffff, 0x07e0, 0, 0xf800, 0x001f, 0, 0)
        compressed = zlib.compress(raw)
        self.good = struct.pack('<HHhhII', 3, 2, -7, 11, len(compressed), 0x1234) + compressed
        # Deliberately invalid selected-image format at another present slot.
        self.bad = struct.pack('<HHhhII', 3, 2, 0, 0, 5, 4) + b'wrong'
        self.miz = b'\0'*24 + self.good + self.bad
        self.mix = selected.decoder.MIX_SIGNATURE + struct.pack('<II4I', 4, 0, 0, 24, 24+len(self.good), 0)
        (self.source/'UI.MIX').write_bytes(self.mix)
        (self.source/'UI.MIZ').write_bytes(self.miz)
        self.baseline.write_text(json.dumps(dict(schemaVersion=1, libraries=[dict(name='UI', slots=4, present=2,
            mixSha256=selected.decoder.sha256(self.mix), mizSha256=selected.decoder.sha256(self.miz))])), encoding='utf-8')
        image, _ = selected.decoder.decode_image(self.miz, 24)
        with image:
            png = self.root/'expected.png'
            image.save(png, format='PNG', compress_level=9)
        self.asset = dict(role='character-frame', sourceLibrary='UI', sourceIndex=1,
            logicalSize=[3,2], sourceOffset=[-7,11], pngSha256=selected.decoder.sha256(png.read_bytes()))
        self.data = dict(schemaVersion=1, kind='official-ui-reference-selection', productionBindingAllowed=False,
            baselineSha256=selected.decoder.sha256(self.baseline.read_bytes()), assets=[self.asset])
        self.save()

    def save(self):
        self.selection.write_text(json.dumps(self.data), encoding='utf-8')

    def run_decode(self, output=None):
        return selected.selected_decode(self.source, output or self.output, self.baseline, self.selection)

    def failure(self, pattern):
        self.save()
        with self.assertRaisesRegex(ValueError, pattern):
            self.run_decode()
        self.assertFalse(self.output.exists())
        self.assertEqual([], list(self.root.glob('.output-*')))

    def test_unselected_unsupported_record_does_not_block_but_is_not_marked_decoded(self):
        before = {p.name:p.read_bytes() for p in self.source.iterdir()}
        result = self.run_decode()
        lib = result['libraries'][0]
        self.assertEqual((4,1,2,1), (lib['slots'],lib['present'],lib['sourcePresent'],lib['unselectedPresent']))
        self.assertEqual([None,None,None], [lib['items'][i] for i in (0,2,3)])
        item = lib['items'][1]
        self.assertEqual((-7,11), (item['offsetX'],item['offsetY']))
        self.assertEqual(24,item['sourceOffset'])
        self.assertEqual('opaque',item['alphaMode'])
        self.assertFalse(item['alphaVerified'])
        self.assertFalse(result['productionBindingAllowed'])
        self.assertEqual(dict(library='UI',index=1,png='UI/1.png'),result['semanticRoles']['character-frame'])
        self.assertEqual(before,{p.name:p.read_bytes() for p in self.source.iterdir()})
        self.assertEqual(self.selection.read_bytes(),(self.output/'selection.json').read_bytes())
        # Whole-library decoder must still reject this same unsupported record.
        with self.assertRaises(selected.decoder.DecodeError):
            selected.decoder.decode_libraries(self.source,self.root/'whole',self.baseline)

    def test_selecting_the_unsupported_record_fails_atomically(self):
        self.asset['sourceIndex']=2
        self.failure(r'UI\[2\].*zlib')

    def test_deterministic_bytes_and_sparse_indices(self):
        self.run_decode()
        second=self.root/'second'; self.run_decode(second)
        def contents(root):
            return {str(p.relative_to(root)):p.read_bytes() for p in root.rglob('*') if p.is_file()}
        self.assertEqual(contents(self.output),contents(second))

    def test_source_hash_mismatch_is_not_bypassed(self):
        (self.source/'UI.MIZ').write_bytes(self.miz+b'x')
        self.failure('source SHA256 mismatch')

    def test_baseline_association_mismatch(self):
        self.data['baselineSha256']='0'*64
        self.failure('baseline SHA256 mismatch')

    def test_decoded_png_must_match_reviewed_source(self):
        self.asset['pngSha256']='0'*64
        self.failure('PNG SHA256 differs')

    def test_source_size_must_match(self):
        self.asset['logicalSize']=[4,2]
        self.failure('geometry differs')

    def test_signed_offsets_must_match(self):
        self.asset['sourceOffset']=[7,11]
        self.failure('geometry differs')

    def test_empty_slot_cannot_be_substituted(self):
        self.asset['sourceIndex']=0
        self.failure('selected slot is empty')

    def test_duplicate_roles_rejected(self):
        self.data['assets'].append(dict(self.asset))
        self.failure('duplicate semantic role')

    def test_duplicate_source_rejected(self):
        self.data['assets'].append(dict(self.asset,role='inventory-frame'))
        self.failure('Duplicate source record')

    def test_boolean_index_is_rejected(self):
        self.asset['sourceIndex']=True
        self.failure('out-of-range index')

    def test_unsafe_library_is_rejected(self):
        self.asset['sourceLibrary']='../UI'
        self.failure('Unknown library')

    def test_duplicate_json_key_rejected(self):
        self.selection.write_text('{"schemaVersion":1,"schemaVersion":1}',encoding='utf-8')
        with self.assertRaisesRegex(ValueError,'Duplicate JSON key'):
            self.run_decode()

    def test_reference_selection_cannot_enable_runtime(self):
        self.data['productionBindingAllowed']=True
        self.failure('Invalid reference selection schema')

    def test_existing_output_preserved(self):
        self.output.mkdir(); (self.output/'keep').write_bytes(b'keep')
        with self.assertRaisesRegex(ValueError,'already exists'):
            self.run_decode()
        self.assertEqual(b'keep',(self.output/'keep').read_bytes())

    def test_output_must_be_disjoint_from_source(self):
        self.output=self.source/'out'
        self.failure('disjoint')

    def test_source_mutation_during_decode_is_detected(self):
        original=selected.decoder.decode_image
        def change(*args):
            image, metadata=original(*args)
            (self.source/'UI.MIX').write_bytes(self.mix+b'changed')
            return image,metadata
        with patch.object(selected.decoder,'decode_image',side_effect=change):
            self.failure('source SHA256 mismatch')

    def test_input_metadata_mutation_is_detected(self):
        original=selected.decoder.decode_image
        def change(*args):
            image, metadata=original(*args)
            self.selection.write_bytes(self.selection.read_bytes()+b' ')
            return image,metadata
        with patch.object(selected.decoder,'decode_image',side_effect=change):
            self.failure('metadata changed during conversion')

    def test_pixel_budget_failure_publishes_nothing(self):
        with patch.object(selected.decoder,'MAX_TOTAL_PIXELS',5):
            self.failure('pixel budget exceeded')


if __name__ == '__main__':
    unittest.main(verbosity=2)
