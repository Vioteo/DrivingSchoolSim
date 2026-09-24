"""Independent FBX/GLB reimport checks, run in Blender after build_houses.py."""
import bpy, json, math
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[1]
manifest=json.loads((ROOT/'artifacts/reports/houses/manifest.json').read_text(encoding='utf8'))
results=[]
for asset in manifest['assets']:
    for format in ('fbx','glb'):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        path=ROOT/('Assets/DrivingSchool/Art/Houses' if format=='fbx' else 'artifacts/visual-review/houses')/(asset['id']+'.'+format)
        if format=='fbx': bpy.ops.import_scene.fbx(filepath=str(path))
        else: bpy.ops.import_scene.gltf(filepath=str(path))
        meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
        assert len(meshes)==(3 if format=='fbx' else 1),(asset['id'],format,len(meshes))
        for obj in meshes:
            obj.data.calc_loop_triangles()
            level=int(obj.name[-1]); expected=asset['lods'][level]
            assert len(obj.data.loop_triangles)==expected['triangles'],obj.name
            assert len(obj.data.uv_layers)>0,obj.name
            assert all(math.isfinite(c) for v in obj.data.vertices for c in v.co),obj.name
            vertices=[obj.matrix_world@v.co for v in obj.data.vertices]
            measured={'min':[min(v[i] for v in vertices) for i in range(3)],'max':[max(v[i] for v in vertices) for i in range(3)]}
            for key in ('min','max'):
                assert all(abs(a-b)<.002 for a,b in zip(measured[key],expected['bounds'][key])),(obj.name,format,measured,expected['bounds'])
            assert all(p.area>1e-10 for p in obj.data.polygons),obj.name
        results.append({'asset':asset['id'],'format':format,'meshCount':len(meshes),'status':'PASS'})
(ROOT/'artifacts/reports/houses/export-check.json').write_text(json.dumps({'checks':results,'scope':'FBX and GLB reimport: LOD count, triangle count, UV presence, finite vertices, no zero-area polygons, world bounds within 2 mm'},indent=2),encoding='utf8')
print('HOUSES_REIMPORT_PASS')
