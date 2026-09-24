"""Export a reviewed Blender source without changing it, then reimport both formats.
blender -b ArtSource/DS_Sedan_A_closed_shell.blend --python tools/export_sedan_review.py
Outputs are versioned; the previous canonical exports are preserved.
"""
import bpy, sys, json, hashlib
from pathlib import Path
from mathutils import Vector
sys.path.insert(0,str(Path(__file__).parent))
import build_art as art

root=bpy.data.objects['DS_Sedan_A']
source=Path(bpy.data.filepath)
outname=source.stem
reportname='cabin-v3-export-check.json' if outname.endswith('cabin_v3') else 'shell-export-check.json'
source_hash=hashlib.sha256(source.read_bytes()).hexdigest()
def measure(objects):
    bpy.context.view_layer.update()
    deps=bpy.context.evaluated_depsgraph_get()
    points=[];triangles=0
    for obj in objects:
        if obj.type in {'MESH','FONT','CURVE'}:
            ev=obj.evaluated_get(deps)
            me=ev.to_mesh()
            if me:
                # FONT/CURVE bound_box can be a stale placeholder before export.
                # Evaluated mesh vertices are the actual exchanged geometry.
                points.extend(ev.matrix_world@v.co for v in me.vertices)
                me.calc_loop_triangles();triangles+=len(me.loop_triangles)
            ev.to_mesh_clear()
    lo=[min(p[i] for p in points) for i in range(3)]
    hi=[max(p[i] for p in points) for i in range(3)]
    return {'dimensions': [hi[i]-lo[i] for i in range(3)],'min':lo,'max':hi,'evaluatedTriangles':triangles,'names':sorted(o.name for o in objects)}
baseline=measure([root]+list(root.children_recursive))
art.export(outname,root)
required=['Socket_DriverEye','Wheel_FL','Wheel_FR','Wheel_RL','Wheel_RR','SteeringWheel_Pivot','Pedal_Clutch','Pedal_Brake','Pedal_Throttle','MirrorSurface_L','MirrorSurface_R','MirrorSurface_Centre']
# Only preserve requirements actually authored; report absent source names explicitly.
report={'source':str(source),'sourceSha256':source_hash,'sourceMeasurement':baseline,'formats':{},'sourceMissingRequired':[n for n in required if n not in baseline['names']]}
for fmt,path in [('GLB',art.WEB/'models'/f'{outname}.glb'),('FBX',art.OUT/f'{outname}.fbx')]:
    bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
    # Blender keeps unused datablocks, but names of imported scene objects are free.
    if fmt=='GLB':bpy.ops.import_scene.gltf(filepath=str(path))
    else:bpy.ops.import_scene.fbx(filepath=str(path))
    actual=measure(list(bpy.context.scene.objects))
    expected=sorted(baseline['dimensions']);dims=sorted(actual['dimensions'])
    errors=[abs(a-b) for a,b in zip(expected,dims)]
    missing=[n for n in required if n in baseline['names'] and n not in actual['names']]
    triangle_match=actual['evaluatedTriangles']==baseline['evaluatedTriangles']
    report['formats'][fmt]={'path':str(path),'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'measurement':actual,'dimensionErrorsM':errors,'missingPivots':missing,'triangleCountMatches':triangle_match,'pass':max(errors)<.003 and not missing and triangle_match}
report['sourceUnmodified']=hashlib.sha256(source.read_bytes()).hexdigest()==source_hash
report['scope']='Blender reimport only. This does not certify Unity rendering, collision, suspension travel or VR.'
report['pass']=report['sourceUnmodified'] and not report['sourceMissingRequired'] and all(x['pass'] for x in report['formats'].values())
(art.ROOT/'artifacts/reports'/reportname).write_text(json.dumps(report,indent=2),encoding='utf-8')
print('SHELL_EXPORT_CHECK',report['pass'],flush=True)
if not report['pass']:raise RuntimeError('Export/reimport verification failed')
