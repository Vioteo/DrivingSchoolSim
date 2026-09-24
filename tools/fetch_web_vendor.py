"""Pinned rendering dependency, downloaded from the npm registry. No remote runtime scripts."""
from pathlib import Path
import urllib.request,tarfile,io,json,hashlib
R=Path(__file__).resolve().parents[1]/'artifacts/visual-review/vendor'
R.mkdir(parents=True,exist_ok=True)
url='https://registry.npmjs.org/three/-/three-0.180.0.tgz'
data=urllib.request.urlopen(url,timeout=45).read()
names={'package/build/three.module.js':'three.module.js','package/build/three.core.js':'three.core.js',
 'package/examples/jsm/loaders/GLTFLoader.js':'GLTFLoader.js',
 'package/examples/jsm/controls/OrbitControls.js':'OrbitControls.js',
 'package/examples/jsm/utils/BufferGeometryUtils.js':'BufferGeometryUtils.js','package/LICENSE':'LICENSE-three.txt'}
with tarfile.open(fileobj=io.BytesIO(data),mode='r:gz') as archive:
    for source,dest in names.items():
        content=archive.extractfile(source).read()
        if dest=='GLTFLoader.js':content=content.replace(b'../utils/BufferGeometryUtils.js',b'./BufferGeometryUtils.js')
        (R/dest).write_bytes(content)
(R/'provenance.json').write_text(json.dumps({'package':'three','version':'0.180.0','url':url,'sha256':hashlib.sha256(data).hexdigest(),'license':'MIT'},indent=2))
print('Vendored Three.js 0.180.0')
