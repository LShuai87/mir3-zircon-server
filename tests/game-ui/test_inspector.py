"""Diagnostic inventories must expose unsupported frames without becoming build input."""
import hashlib
import importlib.util
import json
from pathlib import Path
import struct
import tempfile
import unittest
from unittest import mock
import zlib

MODULE = Path(__file__).resolve().parents[2] / 'build/game-ui/Inspect-OfficialUi.py'
SPEC = importlib.util.spec_from_file_location('official_ui_inspector', MODULE)
inspector = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(inspector)


class InspectorTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='ZirconUiInspection-')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source = self.root / 'source'
        self.source.mkdir()
        self.output = self.root / 'inspection'
        self.baseline = self.root / 'baseline.json'
        self.raw = struct.pack('<2H', 0x07e0, 0x001f)
        first = zlib.compress(self.raw)
        # The second source record has extra bytes: inspection must not truncate it.
        second = zlib.compress(self.raw + b'unknown')
        self.miz = (b'\0' * 24 + struct.pack('<HHhhII', 2, 1, -3, 7, len(first), 0)
                    + first + struct.pack('<HHhhII', 2, 1, 0, 0, len(second), 4) + second)
        self.mix = inspector.decoder.MIX_SIGNATURE + struct.pack('<II3I', 3, 0, 0, 24, 40 + len(first))
        self.write_source()

    def write_source(self):
        (self.source / 'UI.MIX').write_bytes(self.mix)
        (self.source / 'UI.MIZ').write_bytes(self.miz)
        self.baseline.write_text(json.dumps(dict(schemaVersion=1, libraries=[dict(
            name='UI', slots=3, present=2,
            mixSha256=hashlib.sha256(self.mix).hexdigest(),
            mizSha256=hashlib.sha256(self.miz).hexdigest())])), encoding='utf-8')

    def run_inspection(self):
        return inspector.inspect_libraries(self.source, self.output, self.baseline)

    def assert_no_partial_output(self):
        self.assertFalse(self.output.exists())
        self.assertEqual([], list(self.root.glob('.inspection-*')))

    def test_explicit_rejection_preserves_index_offsets_and_valid_pixels(self):
        before = {p.name: p.read_bytes() for p in self.source.iterdir()}
        result = self.run_inspection()
        self.assertEqual('official-ui-diagnostic-inventory', result['kind'])
        self.assertFalse(result['buildable'])
        self.assertFalse(result['alphaVerified'])
        self.assertEqual((1, 1), (result['decodedCount'], result['rejectedCount']))
        items = result['libraries'][0]['items']
        self.assertIsNone(items[0])
        self.assertEqual((-3, 7), (items[1]['offsetX'], items[1]['offsetY']))
        with inspector.decoder.Image.open(self.output / items[1]['png']) as image:
            self.assertEqual([(0,255,0,255),(0,0,255,255)], list(image.get_flattened_data()))
        self.assertEqual('rejected', items[2]['status'])
        self.assertNotIn('png', items[2])
        self.assertIn('decompressed length', items[2]['error'])
        self.assertFalse((self.output / 'manifest.json').exists())
        self.assertEqual(before, {p.name:p.read_bytes() for p in self.source.iterdir()})

    def test_repeat_is_byte_deterministic(self):
        self.run_inspection()
        other = self.root / 'other'
        inspector.inspect_libraries(self.source, other, self.baseline)
        left = {p.relative_to(self.output):p.read_bytes() for p in self.output.rglob('*') if p.is_file()}
        right = {p.relative_to(other):p.read_bytes() for p in other.rglob('*') if p.is_file()}
        self.assertEqual(left, right)

    def test_hash_mismatch_aborts_without_publishing(self):
        (self.source / 'UI.MIZ').write_bytes(self.miz + b'x')
        with self.assertRaisesRegex(inspector.decoder.DecodeError, 'SHA256 mismatch'):
            self.run_inspection()
        self.assert_no_partial_output()

    def test_bad_table_aborts_instead_of_becoming_per_image_rejection(self):
        self.mix = self.mix[:-1]
        self.write_source()
        with self.assertRaisesRegex(inspector.decoder.DecodeError, 'count/length mismatch'):
            self.run_inspection()
        self.assert_no_partial_output()

    def test_existing_output_and_overlap_are_rejected(self):
        self.output.mkdir()
        marker = self.output / 'marker'
        marker.write_bytes(b'keep')
        with self.assertRaisesRegex(inspector.decoder.DecodeError, 'already exists'):
            self.run_inspection()
        self.assertEqual(b'keep', marker.read_bytes())
        with self.assertRaisesRegex(inspector.decoder.DecodeError, 'disjoint'):
            inspector.inspect_libraries(self.source, self.source / 'child', self.baseline)

    def test_source_change_during_inspection_aborts(self):
        original = inspector.decoder.read_pinned
        reads = []
        def read_changed(path, expected):
            reads.append(path)
            if len(reads) == 3:
                raise inspector.decoder.DecodeError('source SHA256 mismatch')
            return original(path, expected)
        with mock.patch.object(inspector.decoder, 'read_pinned', side_effect=read_changed):
            with self.assertRaisesRegex(inspector.decoder.DecodeError, 'SHA256 mismatch'):
                self.run_inspection()
        self.assert_no_partial_output()

    def test_pixel_budget_failure_does_not_publish_partial_inventory(self):
        with mock.patch.object(inspector.decoder, 'MAX_TOTAL_PIXELS', 1):
            with self.assertRaisesRegex(inspector.decoder.DecodeError, 'pixel budget exceeded'):
                self.run_inspection()
        self.assert_no_partial_output()


if __name__ == '__main__':
    unittest.main(verbosity=2)
