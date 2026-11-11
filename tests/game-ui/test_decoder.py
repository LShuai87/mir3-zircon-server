"""Isolated behavioral tests for the production MIX/MIZ decoder."""
import importlib.util
import json
from pathlib import Path
import struct
import tempfile
import unittest
import zlib

MODULE = Path(__file__).resolve().parents[2] / 'build/game-ui/Decode-OfficialUi.py'
spec = importlib.util.spec_from_file_location('official_ui_decoder', MODULE)
decoder = importlib.util.module_from_spec(spec)
spec.loader.exec_module(decoder)


class DecoderTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='ZirconGameUiDecoder-')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source = self.root / 'source'
        self.source.mkdir()
        self.output = self.root / 'output'
        self.baseline = self.root / 'baseline.json'
        # 3x2 forces row padding; distinct rows expose vertical inversion.
        self.raw = struct.pack('<8H', 0, 0xffff, 0x07e0, 0, 0xf800, 0x001f, 0, 0)
        self.miz = b'\0' * 24 + struct.pack('<HHhhII', 3, 2, -7, 11, len(zlib.compress(self.raw)), 0x1234) + zlib.compress(self.raw)
        self.mix = decoder.MIX_SIGNATURE + struct.pack('<II3I', 3, 0, 0, 24, 0)
        self.write_fixture()

    def write_fixture(self):
        (self.source / 'UI.MIX').write_bytes(self.mix)
        (self.source / 'UI.MIZ').write_bytes(self.miz)
        self.baseline.write_text(json.dumps({'schemaVersion': 1, 'libraries': [dict(name='UI', slots=3, present=1, mixSha256=decoder.sha256(self.mix), mizSha256=decoder.sha256(self.miz))]}), encoding='utf-8')

    def decode(self):
        return decoder.decode_libraries(self.source, self.output, self.baseline)

    def assert_failure(self, message):
        with self.assertRaisesRegex(decoder.DecodeError, message):
            self.decode()
        self.assertFalse(self.output.exists())
        self.assertEqual([], list(self.root.glob('.output-*')))

    def test_pixels_padding_orientation_offsets_and_empty_slots(self):
        before = {p.name: p.read_bytes() for p in self.source.iterdir()}
        result = self.decode()['libraries'][0]
        self.assertIsNone(result['items'][0]); self.assertIsNone(result['items'][2])
        item = result['items'][1]
        self.assertEqual((-7, 11), (item['offsetX'], item['offsetY']))
        self.assertFalse(item['alphaVerified'])
        with decoder.Image.open(self.output / item['png']) as image:
            self.assertEqual((3, 2), image.size)
            self.assertEqual([(255,0,0,255),(0,0,255,255),(0,0,0,255),
                              (0,0,0,255),(255,255,255,255),(0,255,0,255)], list(image.get_flattened_data()))
        self.assertEqual(before, {p.name:p.read_bytes() for p in self.source.iterdir()})

    def test_repeat_conversion_is_byte_deterministic(self):
        self.decode(); second = self.root / 'second'
        decoder.decode_libraries(self.source, second, self.baseline)
        self.assertEqual({p.relative_to(self.output):p.read_bytes() for p in self.output.rglob('*') if p.is_file()},
                         {p.relative_to(second):p.read_bytes() for p in second.rglob('*') if p.is_file()})

    def test_existing_output_is_preserved(self):
        self.output.mkdir(); marker=self.output/'marker'; marker.write_bytes(b'keep')
        with self.assertRaisesRegex(decoder.DecodeError,'already exists'): self.decode()
        self.assertEqual(b'keep',marker.read_bytes())

    def test_output_inside_source_is_rejected(self):
        self.output=self.source/'output'
        self.assert_failure('disjoint')

    def test_output_ancestor_of_source_is_rejected(self):
        with self.assertRaisesRegex(decoder.DecodeError,'disjoint'):
            decoder.validate_paths(self.source,self.root)

    def test_hash_mismatch_publishes_nothing(self):
        (self.source/'UI.MIZ').write_bytes(self.miz+b'x')
        self.assert_failure('SHA256 mismatch')

    def test_truncated_mix_header(self):
        self.mix=self.mix[:20]; self.write_fixture(); self.assert_failure('invalid MIX header')

    def test_invalid_mix_signature(self):
        self.mix=b'x'+self.mix[1:]; self.write_fixture(); self.assert_failure('invalid MIX header')

    def test_count_does_not_match_table(self):
        self.mix=self.mix[:-1]; self.write_fixture(); self.assert_failure('count/length mismatch')

    def test_image_offset_outside_miz(self):
        self.mix=self.mix[:28]+struct.pack('<I',len(self.miz)-10)+self.mix[32:]
        self.write_fixture(); self.assert_failure(r'UI\[1\].*header outside')

    def test_payload_truncated(self):
        self.miz=self.miz[:-1]; self.write_fixture(); self.assert_failure('Compressed image outside')

    def test_invalid_dimensions(self):
        self.miz=self.miz[:24]+b'\0\0'+self.miz[26:]
        self.write_fixture(); self.assert_failure('dimensions')

    def test_wrong_uncompressed_length(self):
        compressed=zlib.compress(self.raw+b'\0')
        self.miz=self.miz[:32]+struct.pack('<I',len(compressed))+self.miz[36:40]+compressed
        self.write_fixture(); self.assert_failure('decompressed length')

    def test_zlib_bomb_is_bounded(self):
        compressed=zlib.compress(b'x'*2_000_000)
        self.miz=self.miz[:32]+struct.pack('<I',len(compressed))+self.miz[36:40]+compressed
        self.write_fixture(); self.assert_failure('decompressed length')

    def test_invalid_zlib(self):
        self.miz=self.miz[:40]+b'!'*(len(self.miz)-40)
        self.write_fixture(); self.assert_failure('Invalid zlib')

    def test_trailing_zlib_data_is_rejected(self):
        compressed=zlib.compress(self.raw)+b'extra'
        self.miz=self.miz[:32]+struct.pack('<I',len(compressed))+self.miz[36:40]+compressed
        self.write_fixture(); self.assert_failure('trailing compressed data')

    def test_unsafe_library_name_is_rejected(self):
        config=json.loads(self.baseline.read_text()); config['libraries'][0]['name']='../outside'
        self.baseline.write_text(json.dumps(config)); self.assert_failure('Unsafe')

    def test_later_library_failure_does_not_publish_partial_set(self):
        config=json.loads(self.baseline.read_text()); second=dict(config['libraries'][0],name='Missing')
        config['libraries'].append(second); self.baseline.write_text(json.dumps(config))
        self.assert_failure('missing or oversized')


if __name__ == '__main__':
    unittest.main(verbosity=2)
