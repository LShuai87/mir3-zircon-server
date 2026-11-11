"""Rebuild catalog inputs from the independently verified 93-frame candidate."""
import importlib.util, json, hashlib
from pathlib import Path
root=Path("E:/workspace/mir3-zircon-server")
work=Path("E:/workspace/mir3-ui-work/npc-shop-assets-20261108-v1")
spec=importlib.util.spec_from_file_location('catalog', root/'build/game-ui/Compile-UiCatalog.py')
tool=importlib.util.module_from_spec(spec); spec.loader.exec_module(tool)
p=work/'zl/prepared-manifest.json'
prepared=json.loads(p.read_text(encoding='utf-8-sig'))
package=(work/'zl/OfficialV1.Zl').read_bytes()
count,images=tool.read_zl(package)
f=root/'build/game-ui/asset-manifest.json'
m=json.loads(f.read_text(encoding='utf-8-sig'))
m['package']=dict(fileName='OfficialV1.Zl',sha256=tool.sha(package),byteLength=len(package),slotCount=count)
m['inputs']=dict(preparedManifestSha256=tool.sha(p.read_bytes()),compositionRecipeSha256=tool.sha((root/'build/game-ui/composition-recipe.json').read_bytes()),selectionSha256=tool.sha((root/'build/game-ui/reference-selection.json').read_bytes()),sourceLibrariesSha256=tool.sha((root/'build/game-ui/selection-source-libraries.json').read_bytes()))
m['requiredRoles']=[a['role'] for a in prepared['assets']]
m['assets']=[dict(a,dataSha256=tool.sha(images[a['outputIndex']]['data'])) for a in prepared['assets']]
m['buttons']=prepared['buttons']
f.write_text(json.dumps(m,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
# Compile to a separate output, then publish only after full validation.
out=root/'docs/design/assets/official-ingame-ui/acceptance/npc-shop-binding/GameUiOfficialV1Catalog.g.cs'
result=tool.compile_catalog(f,p,work/'zl/OfficialV1.Zl',root/'build/game-ui/composition-recipe.json',root/'build/game-ui/reference-selection.json',root/'build/game-ui/selection-source-libraries.json',out)
(root/'Client/Envir/GameUiOfficialV1Catalog.g.cs').write_bytes(out.read_bytes())
print(json.dumps(result,sort_keys=True))
