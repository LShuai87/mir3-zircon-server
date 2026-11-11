"""Exercise the prepared-Zl CLI as a process, including fail-closed inputs."""
import copy
import hashlib
import json
from pathlib import Path
import subprocess
import unittest
from PIL import Image
import test_composed_ui as fixture

EXE=Path(__file__).resolve().parents[3]/'Debug/ImageManager/ImageManager.exe'

class PreparedTests(unittest.TestCase):
    def setUp(self):
        self.fixture=fixture.CompositionTests(); self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        self.fixture.run_compose()
        self.art=self.fixture.output
        self.output=self.fixture.root/'package'
        self.path=self.art/'composed-manifest.json'
        self.manifest=json.loads(self.path.read_text())
    def run_builder(self,mode='prepared'):
        return subprocess.run([str(EXE),'--official-ui',mode,str(self.art),str(self.output),str(self.path)],capture_output=True,text=True,timeout=30)
    def save(self): self.path.write_text(json.dumps(self.manifest))
    def reject(self):
        self.save(); result=self.run_builder()
        self.assertNotEqual(0,result.returncode,result.stdout+result.stderr)
        self.assertFalse(self.output.exists()); self.assertFalse(list(self.output.parent.glob('package.staging-*')))
    def test_deterministic_package(self):
        result=self.run_builder(); self.assertEqual(0,result.returncode,result.stderr)
        first={p.name:p.read_bytes() for p in self.output.iterdir()}
        self.output=self.fixture.root/'second'; result=self.run_builder(); self.assertEqual(0,result.returncode,result.stderr)
        self.assertEqual(first,{p.name:p.read_bytes() for p in self.output.iterdir()})
        report=json.loads(first['prepared-manifest.json']); self.assertFalse(report['runtimeReady']); self.assertFalse(report['productionBindingAllowed'])
    def test_runtime_acceptance_flags_are_not_implicit_or_true(self):
        original=copy.deepcopy(self.manifest)
        for key in ('runtimeReady','productionBindingAllowed','alphaVerified'):
            for value in (True,None,'false','absent'):
                self.manifest=copy.deepcopy(original)
                if value=='absent': del self.manifest[key]
                else: self.manifest[key]=value
                with self.subTest(key=key,value=value): self.reject()
    def test_wrong_kind_rejected(self): self.manifest['kind']='official-ui-reference-zl'; self.reject()
    def test_duplicate_role_rejected(self): self.manifest['assets'][1]['role']=self.manifest['assets'][0]['role']; self.reject()
    def test_noncontiguous_index_rejected(self): self.manifest['assets'][0]['outputIndex']=1; self.reject()
    def test_unsafe_path_rejected(self): self.manifest['assets'][0]['png']='../outside.png'; self.reject()
    def test_nonzero_offset_rejected(self): self.manifest['assets'][0]['outputOffset']=[1,0]; self.reject()
    def test_wrong_texture_size_rejected(self): self.manifest['assets'][0]['textureSize']=[9,9]; self.reject()
    def test_corrupt_png_rejected(self): (self.art/self.manifest['assets'][0]['png']).write_bytes(b'bad'); self.reject()
    def test_rgba_hash_rejected(self): self.manifest['assets'][0]['rgbaSha256']='0'*64; self.reject()
    def test_fractional_alpha_rejected_even_with_matching_hashes(self):
        a=self.manifest['assets'][0]; path=self.art/a['png']
        with Image.open(path) as original: image=original.copy()
        image.putpixel((1,1),(70,80,90,128)); image.save(path)
        a['pngSha256']=hashlib.sha256(path.read_bytes()).hexdigest(); a['rgbaSha256']=hashlib.sha256(image.tobytes()).hexdigest(); self.reject()
    def test_missing_button_state_rejected(self): del self.manifest['buttons']['test-button']['disabled']; self.reject()
    def test_unbound_state_rejected(self): self.manifest['buttons']={}; self.reject()
    def test_invalid_grid_rejected(self):
        self.manifest['grids']['inventory']=dict(columns=6,rows=8,cellSize=[36,36],step=[35,35],origin=[0,0],size=[211,281],cells=[]); self.reject()
    def test_existing_output_untouched(self):
        self.output.mkdir(); marker=self.output/'keep'; marker.write_text('keep')
        self.assertNotEqual(0,self.run_builder().returncode); self.assertEqual('keep',marker.read_text())
    def test_unsupported_mode_rejected(self): self.assertNotEqual(0,self.run_builder('production').returncode)

if __name__=='__main__': unittest.main()
