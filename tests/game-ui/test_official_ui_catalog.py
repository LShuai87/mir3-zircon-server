"""Strict real-catalog compilation tests; fixtures need no installed official client."""
import copy
import importlib.util
import json
from pathlib import Path
import struct
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('compile_official_catalog', ROOT / 'build/game-ui/Compile-UiCatalog.py')
tool = importlib.util.module_from_spec(spec)
spec.loader.exec_module(tool)


class CatalogTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='OfficialUiCatalog-')
        self.addCleanup(self.temp.cleanup)
        self.folder = Path(self.temp.name)
        names = ['test-button', 'test-button-hover', 'test-button-pressed', 'test-button-disabled']
        state_names = [None, 'hover', 'pressed', 'disabled']
        padded = sum(3 << (2 * (y * 4 + x)) for y in range(4) for x in range(4) if x >= 3 or y >= 3)
        self.payload = struct.pack('<HHI', 0, 65535, padded)
        header_end = 8 + 4 * 26
        self.package = struct.pack('<ii', header_end - 4, 4)
        for index in range(4):
            self.package += b'\x01' + struct.pack('<ihhhhBhhhhhh', header_end + index * 8, 3, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0)
        self.package += self.payload * 4
        assets = [dict(role=name, png='Assets/' + name + '.png', pngSha256='1' * 64, rgbaSha256='2' * 64,
                       sourceLibrary='UI', sourcePngSha256='3' * 64, offsetMode='baked', alphaMode='BinaryMask',
                       blendMode='Normal', sourceRole='test-button', state=state_names[i], outputIndex=i,
                       sourceIndex=i, logicalSize=[3, 3], textureSize=[4, 4], sourceOffset=[-1, -2],
                       outputOffset=[0, 0], pivot=[0, 0]) for i, name in enumerate(names)]
        buttons = {'test-button': dict(zip(tool.STATES, names))}
        self.prepared = dict(kind='official-ui-prepared-zl', runtimeReady=False, productionBindingAllowed=False,
                             alphaVerified=False, recipeSha256='', sha256=tool.sha(self.package), assets=copy.deepcopy(assets), buttons=copy.deepcopy(buttons))
        self.selection = dict(productionBindingAllowed=False)
        self.recipe = dict(productionBindingAllowed=False, selectionSha256='')
        self.libraries = dict(libraries=[dict(name='UI', slots=4, mixSha256='4' * 64, mizSha256='5' * 64)])
        self.manifest = dict(schemaVersion=1, kind='official-ui-asset-catalog', style='OfficialV1',
                             status='pending-scene-integration', runtimeReady=False, productionBindingAllowed=False,
                             package=dict(fileName='OfficialV1.Zl', sha256=tool.sha(self.package), byteLength=len(self.package), slotCount=4),
                             inputs={}, sourceLibraries=copy.deepcopy(self.libraries['libraries']), requiredRoles=names,
                             assets=[dict(a, dataSha256=tool.sha(self.payload)) for a in assets], buttons=buttons,
                             scope=dict(npcCards='Legacy', secondaryPopups='Legacy', displaySettings='unchanged', loginAndCharacterSelection='unchanged'),
                             activationBoundary='No scene activation')
        self.paths = [self.folder / name for name in ('manifest.json', 'prepared.json', 'OfficialV1.Zl', 'recipe.json', 'selection.json', 'libraries.json')]
        self.output = self.folder / 'GameUiOfficialV1Catalog.g.cs'

    def save(self):
        encode = lambda obj: (json.dumps(obj, sort_keys=True) + '\n').encode('utf-8')
        self.recipe['selectionSha256'] = tool.sha(encode(self.selection))
        self.prepared['recipeSha256'] = tool.sha(encode(self.recipe))
        raws = [None, encode(self.prepared), self.package, encode(self.recipe), encode(self.selection), encode(self.libraries)]
        self.manifest['inputs'] = dict(zip(('preparedManifestSha256', 'compositionRecipeSha256', 'selectionSha256', 'sourceLibrariesSha256'),
                                         (tool.sha(raws[i]) for i in (1, 3, 4, 5))))
        raws[0] = encode(self.manifest)
        for p, raw in zip(self.paths, raws):
            p.write_bytes(raw)

    def compile(self, check=False):
        return tool.compile_catalog(*self.paths, self.output, check=check)

    def reject(self, message):
        self.save()
        with self.assertRaisesRegex(ValueError, message):
            self.compile()
        self.assertFalse(self.output.exists(), 'Rejected build published output')

    def test_deterministic_compilation_and_stale_check(self):
        self.save()
        report = self.compile()
        original = self.output.read_bytes()
        self.assertEqual((report['assetCount'], report['buttonCount'], report['runtimeReady']), (4, 1, False))
        self.assertIn(b'public const bool RuntimeReady = false;', original)
        self.assertEqual(report, self.compile(check=True))
        self.output.write_bytes(b'stale')
        with self.assertRaisesRegex(ValueError, 'stale'):
            self.compile(check=True)
        self.assertEqual(self.output.read_bytes(), b'stale')
        with self.assertRaisesRegex(ValueError, 'already exists'):
            self.compile()

    def test_reject_invalid_contracts(self):
        mutations = [
            ('activation', lambda: self.manifest.update(runtimeReady=True), 'activation'),
            ('binding activation', lambda: self.manifest.update(productionBindingAllowed=True), 'activation'),
            ('prepared activation', lambda: self.prepared.update(alphaVerified=True), 'unaccepted'),
            ('unknown field', lambda: self.manifest.update(other=1), 'unexpected'),
            ('duplicate role', lambda: self.manifest['requiredRoles'].append('test-button'), 'duplicate role'),
            ('missing role', lambda: self.manifest['requiredRoles'].pop(), 'count mismatch'),
            ('package name', lambda: self.manifest['package'].update(fileName='Fixture.Zl'), 'filename'),
            ('package hash', lambda: self.manifest['package'].update(sha256='0' * 64), 'hash/length'),
            ('slot count', lambda: self.manifest['package'].update(slotCount=5), 'slot count'),
            ('source baseline', lambda: self.manifest['sourceLibraries'][0].update(slots=5), 'baseline mismatch'),
            ('scope', lambda: self.manifest['scope'].update(npcCards='OfficialV1'), 'Scope changed'),
            ('prepared descriptor', lambda: self.manifest['assets'][0].update(logicalSize=[4, 3]), 'descriptor mismatch'),
            ('frame data', lambda: self.manifest['assets'][0].update(dataSha256='0' * 64), 'payload mismatch'),
            ('state group', lambda: self.manifest['buttons']['test-button'].update(hover='test-button'), 'differ'),
        ]
        baseline = copy.deepcopy((self.manifest, self.prepared))
        for label, mutate, expected in mutations:
            with self.subTest(label=label):
                self.manifest, self.prepared = copy.deepcopy(baseline)
                mutate()
                self.reject(expected)

    def test_reject_matching_but_invalid_descriptors(self):
        baseline = copy.deepcopy((self.manifest, self.prepared))
        for key, value, expected in (
            ('outputIndex', True, 'integer'),
            ('outputIndex', 1, 'geometry/payload|duplicate frame'),
            ('textureSize', [8, 4], 'padded texture'),
            ('sourceOffset', [-32769, 0], 'outside range'),
            ('outputOffset', [1, 0], 'baked exactly once'),
            ('blendMode', 'Additive', 'rendering policy'),
            ('dataSha256', 'bad', 'invalid SHA256'),
            ('sourceIndex', 4, 'outside range'),
            ('state', 'normal', 'ambiguous state'),
        ):
            with self.subTest(key=key, value=value):
                self.manifest, self.prepared = copy.deepcopy(baseline)
                self.manifest['assets'][0][key] = value
                if key != 'dataSha256':
                    self.prepared['assets'][0][key] = value
                self.reject(expected)

    def test_duplicate_json_key_and_stale_input_hash(self):
        self.save()
        self.paths[0].write_text('{"schemaVersion":1,"schemaVersion":1}', encoding='utf-8')
        with self.assertRaisesRegex(ValueError, 'Duplicate JSON key'):
            self.compile()
        self.save()
        self.paths[1].write_bytes(self.paths[1].read_bytes() + b' ')
        with self.assertRaisesRegex(ValueError, 'input hashes'):
            self.compile()
        self.assertFalse(self.output.exists())

    def test_output_cannot_replace_build_input(self):
        self.save()
        original = self.paths[0].read_bytes()
        with self.assertRaisesRegex(ValueError, 'distinct'):
            tool.compile_catalog(*self.paths, self.paths[0], check=True)
        self.assertEqual(self.paths[0].read_bytes(), original)


if __name__ == '__main__':
    unittest.main()
