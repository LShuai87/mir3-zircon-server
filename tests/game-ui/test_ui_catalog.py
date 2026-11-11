"""Independent catalog/package preparation checks. Requires Windows .NET csc.

Runs the actual ImageManager Mir3Library writer and compiles the generated C#.
No production Client.csproj or official asset source is changed.
"""
import copy
import importlib.util
import json
import os
from pathlib import Path
import struct
import subprocess
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location('catalog_preparation', ROOT / 'build/game-ui/Prepare-UiCatalog.py')
CAT = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CAT)


class CatalogTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        compiler = os.environ.get('GAMEUI_CSC')
        if not compiler:
            vswhere = Path(os.environ.get('ProgramFiles(x86)', r'C:\Program Files (x86)')) / 'Microsoft Visual Studio/Installer/vswhere.exe'
            if vswhere.is_file():
                found = subprocess.run([str(vswhere), '-latest', '-products', '*', '-requires', 'Microsoft.Component.MSBuild',
                                        '-find', r'MSBuild\**\Bin\Roslyn\csc.exe'], check=True, capture_output=True, text=True)
                compiler = next((line.strip() for line in found.stdout.splitlines() if line.strip()), None)
        if not compiler or not Path(compiler).is_file():
            raise RuntimeError('Set GAMEUI_CSC to the Visual Studio Roslyn C# compiler; compile verification is required.')
        cls.compiler = compiler
        cls.work = tempfile.TemporaryDirectory(prefix='ZirconUiCatalogTests-')
        cls.folder = Path(cls.work.name)
        fixture = ROOT / 'tests/game-ui/OfficialUiCatalogFixture.cs'
        cls.fixture = fixture
        writer = cls.folder / 'FixtureWriter.exe'
        subprocess.run([compiler, '/nologo', '/target:exe', '/out:' + str(writer), str(fixture),
                        str(ROOT / 'ImageManager/Mir3Library.cs')], check=True, capture_output=True, text=True)
        cls.package_path = cls.folder / 'source.Zl'
        subprocess.run([str(writer), str(cls.package_path)], check=True, capture_output=True)
        cls.package = cls.package_path.read_bytes()
        _, images = CAT.read_zl(cls.package)
        roles = ['fixture.background', 'fixture.close.normal', 'fixture.close.hover', 'fixture.close.pressed', 'fixture.close.disabled']
        cls.contract = dict(schemaVersion=1, kind='synthetic-ui-catalog-preparation', productionBindingAllowed=False,
                            roles=roles, buttons=['fixture.close'], layoutIds=['fixture.window', 'fixture.close'], bindingIds=['fixture.attack'])
        cls.manifest = dict(schemaVersion=1, kind='synthetic-ui-catalog-preparation', productionBindingAllowed=False,
                            package=dict(fileName='Fixture.Zl', sha256=CAT.sha(cls.package)), assets=[],
                            buttons={'fixture.close': dict(zip(('normal', 'hover', 'pressed', 'disabled'), roles[1:]))},
                            layouts=[dict(width=w, height=h, rects={'fixture.window': [10, 10, 200, 200], 'fixture.close': [200, 10, 4, 4]}) for w, h in CAT.RESOLUTIONS],
                            bindings={'fixture.attack': dict(page='character', source='User.Stats.MinDC')})
        for role, (index, image) in zip(roles, images.items()):
            cls.manifest['assets'].append(dict(role=role, outputIndex=index,
                logicalSize=[image['width'], image['height']], textureSize=[(image['width'] + 3) // 4 * 4, (image['height'] + 3) // 4 * 4],
                sourceOffset=list(image['offset']) if index == 0 else [-8, 5], pivot=[2, 1],
                offsetMode='source' if index == 0 else 'baked', alphaMode='Opaque', blendMode='Normal', dataSha256=CAT.sha(image['data'])))

    @classmethod
    def tearDownClass(cls):
        cls.work.cleanup()

    def reject(self, mutate, expected):
        contract, manifest = copy.deepcopy(self.contract), copy.deepcopy(self.manifest)
        mutate(contract, manifest)
        with self.assertRaisesRegex(ValueError, expected):
            CAT.validate(contract, manifest, self.package)

    def inputs(self, folder, manifest=None):
        contract = folder / 'contract.json'
        manifest_path = folder / 'manifest.json'
        contract.write_text(json.dumps(self.contract), encoding='utf-8')
        manifest_path.write_text(json.dumps(self.manifest if manifest is None else manifest), encoding='utf-8')
        return contract, manifest_path, self.package_path

    def test_actual_writer_catalog_roundtrip(self):
        with tempfile.TemporaryDirectory(dir=self.folder) as temp:
            folder = Path(temp)
            inputs = self.inputs(folder)
            output = folder / 'prepared'
            report = CAT.prepare(*inputs, output)
            self.assertIs(report['productionBindingAllowed'], False)
            self.assertEqual(report['assetCount'], 5)
            runner = folder / 'ReadCatalog.exe'
            subprocess.run([self.compiler, '/nologo', '/target:exe', '/define:CATALOG_VERIFY', '/out:' + str(runner),
                            str(self.fixture), str(output / 'PreparedUiCatalog.cs')], check=True, capture_output=True, text=True)
            result = subprocess.run([str(runner), str(output / 'Fixture.Zl')], check=True, capture_output=True, text=True)
            self.assertIn('checks passed: 51', result.stdout)
            print(result.stdout.strip())
            other = folder / 'repeated'
            CAT.prepare(*inputs, other)
            self.assertEqual(sorted(p.name for p in output.iterdir()), sorted(p.name for p in other.iterdir()))
            for path in output.iterdir():
                self.assertEqual(path.read_bytes(), (other / path.name).read_bytes(), path.name)

    def test_manifest_negative_contracts(self):
        cases = [
            ('unknown role', lambda c,m: m['assets'][0].update(role='unknown.asset'), 'Unknown/duplicate role'),
            ('missing asset', lambda c,m: m['assets'].pop(), 'Missing/extra assets'),
            ('duplicate role', lambda c,m: m['assets'][1].update(role=m['assets'][0]['role']), 'Unknown/duplicate role'),
            ('conflicting index', lambda c,m: m['assets'][1].update(outputIndex=0), 'Conflicting/empty'),
            ('empty index', lambda c,m: m['assets'][1].update(outputIndex=1), 'Conflicting/empty'),
            ('bool index', lambda c,m: m['assets'][0].update(outputIndex=False), 'integer outside range'),
            ('float size', lambda c,m: m['assets'][0].update(logicalSize=[5.0,7]), 'integer outside range'),
            ('wrong logical size', lambda c,m: m['assets'][0].update(logicalSize=[6,7]), 'logical dimensions mismatch'),
            ('wrong padding', lambda c,m: m['assets'][0].update(textureSize=[5,7]), 'padded texture size'),
            ('wrong offset', lambda c,m: m['assets'][0].update(sourceOffset=[3,2]), 'offset mismatch'),
            ('double offset', lambda c,m: m['assets'][0].update(offsetMode='baked'), 'offset mismatch'),
            ('unsupported continuous alpha', lambda c,m: m['assets'][0].update(alphaMode='Continuous'), 'Unsupported alpha'),
            ('unknown blend', lambda c,m: m['assets'][0].update(blendMode='Additive'), 'Unsupported blend'),
            ('wrong data hash', lambda c,m: m['assets'][0].update(dataSha256='0'*64), 'DXT1 SHA256 mismatch'),
            ('wrong package hash', lambda c,m: m['package'].update(sha256='0'*64), 'Package SHA256 mismatch'),
            ('missing state', lambda c,m: m['buttons']['fixture.close'].pop('disabled'), 'unexpected or missing keys'),
            ('unknown state role', lambda c,m: m['buttons']['fixture.close'].update(hover='unknown.asset'), 'Unknown button state'),
            ('moved state pivot', lambda c,m: m['assets'][2].update(pivot=[3,1]), 'inconsistent geometry'),
            ('state offset policy', lambda c,m: m['assets'][2].update(sourceOffset=[0,0]), 'inconsistent geometry'),
            ('missing resolution', lambda c,m: m['layouts'].pop(), 'Six layouts required'),
            ('duplicate resolution', lambda c,m: m['layouts'].__setitem__(1, copy.deepcopy(m['layouts'][0])), 'Unknown/duplicate resolution'),
            ('overflow rect', lambda c,m: m['layouts'][0]['rects'].update({'fixture.window': [1000,0,200,10]}), 'out-of-bounds'),
            ('zero rect', lambda c,m: m['layouts'][0]['rects'].update({'fixture.window': [0,0,0,10]}), 'out-of-bounds'),
            ('missing layout id', lambda c,m: m['layouts'][0]['rects'].pop('fixture.close'), 'layout IDs'),
            ('missing binding', lambda c,m: m['bindings'].clear(), 'Missing/unknown bindings'),
            ('unknown page', lambda c,m: m['bindings']['fixture.attack'].update(page='legacy'), 'Unknown binding page'),
            ('code injection', lambda c,m: m['bindings']['fixture.attack'].update(source='x"; throw; //'), 'Invalid binding source'),
            ('unknown contract role', lambda c,m: c['roles'].__setitem__(0, 'different.asset'), 'Unknown/duplicate role'),
            ('duplicate contract id', lambda c,m: c['roles'].__setitem__(0, c['roles'][1]), 'duplicate identifier'),
            ('production flag', lambda c,m: m.update(productionBindingAllowed=True), 'production binding'),
            ('contract production flag', lambda c,m: c.update(productionBindingAllowed=True), 'production binding'),
            ('runtime output name', lambda c,m: m['package'].update(fileName='OfficialV1.Zl'), 'Only Fixture.Zl'),
            ('unknown metadata', lambda c,m: m['assets'][0].update(unverified=True), 'unexpected or missing keys'),
            ('boolean schema', lambda c,m: m.update(schemaVersion=True), 'integer outside range'),
        ]
        for name, mutate, expected in cases:
            with self.subTest(case=name):
                self.reject(mutate, expected)
        print('Manifest negative cases passed: ' + str(len(cases)))

    def test_binary_structure_and_alpha(self):
        header_size = struct.unpack_from('<i', self.package)[0]
        variants = []
        variants.append(('truncated', self.package[:-1]))
        variants.append(('trailing', self.package + b'\x00'))
        def changed(offset, fmt, value):
            data = bytearray(self.package)
            struct.pack_into(fmt, data, offset, value)
            return bytes(data)
        variants.extend([
            ('bad header', changed(0, '<i', 4)), ('bad count', changed(4, '<i', -1)),
            ('presence', changed(8, '<B', 2)), ('payload overlap', changed(9, '<i', header_size + 3)),
            ('payload gap', changed(9, '<i', header_size + 5)), ('negative size', changed(13, '<h', -5)),
            ('shadow', changed(21, '<B', 1)), ('overlay', changed(30, '<h', 1)),
        ])
        for name, data in variants:
            with self.subTest(case=name), self.assertRaises(ValueError):
                CAT.read_zl(data)
        # Make first logical texel transparent but keep otherwise valid headers/hashes.
        _, images = CAT.read_zl(self.package)
        pos = images[0]['position']
        for name, offset, selector in [('opaque logical transparency', pos + 4, 3),
                                       ('opaque padding', pos + 12, 0)]:
            with self.subTest(case=name):
                data = bytearray(self.package)
                current = struct.unpack_from('<I', data, offset)[0]
                if name == 'opaque logical transparency':
                    current = current | selector
                else:
                    # Block 1 has only x=4 logical, x=5/6/7 must stay transparent.
                    current = current & ~(3 << 2)
                struct.pack_into('<I', data, offset, current)
                manifest = copy.deepcopy(self.manifest)
                manifest['package']['sha256'] = CAT.sha(data)
                manifest['assets'][0]['dataSha256'] = CAT.sha(CAT.read_zl(data)[1][0]['data'])
                with self.assertRaisesRegex(ValueError, 'transparent'):
                    CAT.validate(self.contract, manifest, data)
        print('Binary/alpha negative cases passed: 12')

    def test_binary_mask_and_repeated_state_are_explicit(self):
        manifest = copy.deepcopy(self.manifest)
        _, images = CAT.read_zl(self.package)
        data = bytearray(self.package)
        position = images[0]['position']
        selectors = struct.unpack_from('<I', data, position + 4)[0] | 3
        struct.pack_into('<I', data, position + 4, selectors)
        manifest['package']['sha256'] = CAT.sha(data)
        manifest['assets'][0]['dataSha256'] = CAT.sha(CAT.read_zl(data)[1][0]['data'])
        manifest['assets'][0]['alphaMode'] = 'BinaryMask'
        # Explicit repeated states are permitted; no implicit index+1 is used.
        manifest['buttons']['fixture.close']['disabled'] = 'fixture.close.normal'
        CAT.validate(self.contract, manifest, data)

    def test_duplicate_and_nonfinite_json(self):
        with tempfile.TemporaryDirectory(dir=self.folder) as temp:
            path = Path(temp) / 'bad.json'
            for raw in ('{"a":1,"a":2}', '{"nested":{"a":1,"a":2}}', '{"a":NaN}', '{"a":Infinity}', '{"a":-Infinity}'):
                path.write_text(raw, encoding='utf-8')
                with self.subTest(raw=raw), self.assertRaises(ValueError):
                    CAT.read_json(path)

    def test_publication_rejects_and_rolls_back(self):
        with tempfile.TemporaryDirectory(dir=self.folder) as temp:
            folder = Path(temp)
            inputs = self.inputs(folder)
            output = folder / 'prepared'
            output.mkdir()
            marker = output / 'keep.txt'
            marker.write_text('preserve', encoding='utf-8')
            with self.assertRaisesRegex(ValueError, 'new directory'):
                CAT.prepare(*inputs, output)
            self.assertEqual(marker.read_text(), 'preserve')
            failing = folder / 'failed'
            with mock.patch.object(CAT.os, 'rename', side_effect=OSError('injected publish failure')):
                with self.assertRaisesRegex(OSError, 'injected'):
                    CAT.prepare(*inputs, failing)
            self.assertFalse(failing.exists())
            self.assertEqual(list(folder.glob('.*.staging-*')), [])
            bad_manifest = copy.deepcopy(self.manifest)
            bad_manifest['buttons']['fixture.close'].pop('normal')
            inputs = self.inputs(folder, bad_manifest)
            with self.assertRaises(ValueError):
                CAT.prepare(*inputs, failing)
            self.assertFalse(failing.exists())
            self.assertEqual(list(folder.glob('.*.staging-*')), [])

    def test_cli_rejects_without_publication(self):
        with tempfile.TemporaryDirectory(dir=self.folder) as temp:
            folder = Path(temp)
            inputs = self.inputs(folder)
            inputs[1].write_text('{"schemaVersion":1,"schemaVersion":1}', encoding='utf-8')
            output = folder / 'prepared'
            import sys
            result = subprocess.run([sys.executable, str(SPEC.origin), '--contract', str(inputs[0]), '--manifest', str(inputs[1]),
                                     '--package', str(inputs[2]), '--output', str(output)], capture_output=True, text=True)
            self.assertEqual(result.returncode, 1)
            self.assertIn('Duplicate JSON key', result.stderr)
            self.assertFalse(output.exists())


if __name__ == '__main__':
    unittest.main(verbosity=2)
