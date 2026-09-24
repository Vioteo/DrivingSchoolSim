"""Reimport pedestrian FBX/GLB and measure skinned animation, without changing exports."""
import bpy, json, hashlib, math
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
manifest=json.loads((ROOT/'artifacts/reports/pedestrians-manifest.json').read_text())
report={'scope':'Blender reimport of final FBX and GLB; Unity has a separate report.', 'assets':[]}

def vertices():
    bpy.context.view_layer.update()
    deps=bpy.context.evaluated_depsgraph_get(); points=[]
    for o in bpy.context.scene.objects:
        if o.type!='MESH' or not any(m.type=='ARMATURE' for m in o.modifiers): continue
        ev=o.evaluated_get(deps); mesh=ev.to_mesh()
        points.extend(ev.matrix_world@v.co for v in mesh.vertices)
        ev.to_mesh_clear()
    return points

for asset in manifest['assets']:
    for file,expected_hash in asset['files'].items():
        path=ROOT/file
        assert hashlib.sha256(path.read_bytes()).hexdigest()==expected_hash, 'Hash mismatch: '+file
        if path.suffix=='.blend': continue
        bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
        for action in list(bpy.data.actions): bpy.data.actions.remove(action)
        if path.suffix=='.fbx': bpy.ops.import_scene.fbx(filepath=str(path))
        else: bpy.ops.import_scene.gltf(filepath=str(path))
        rigs=[o for o in bpy.context.scene.objects if o.type=='ARMATURE']
        assert len(rigs)==1
        rig=rigs[0]
        for t in rig.animation_data.nla_tracks: t.mute=True
        # glTF import may retain bind-axis corrections in pose transforms.
        # Sample the actual rest-like Idle frame instead of zeroing those corrections.
        rig.animation_data.action=next(a for a in bpy.data.actions if 'Idle' in a.name)
        bpy.context.scene.frame_set(1)
        pts=vertices()
        bounds=[max(p[i] for p in pts)-min(p[i] for p in pts) for i in range(3)]
        assert abs(bounds[2]-asset['heightM'])<.005, bounds
        animations=[]
        for name in ['Idle','Walk']:
            action=next(a for a in bpy.data.actions if name in a.name)
            rig.animation_data.action=action
            first,last=action.frame_range
            bpy.context.scene.frame_set(int(first)); start=vertices()
            bpy.context.scene.frame_set(int(last)); end=vertices()
            seam=max((a-b).length for a,b in zip(start,end))
            max_motion=0; ground=[]
            for i in range(31):
                frame=first+(last-first)*i/30
                bpy.context.scene.frame_set(int(frame),subframe=frame-int(frame))
                pts=vertices()
                assert all(math.isfinite(v) for p in pts for v in p)
                max_motion=max(max_motion,max((a-b).length for a,b in zip(start,pts)))
                ground.append(min(p.z for p in pts))
            assert seam<.002, (file,name,'loop seam',seam)
            assert max_motion>(.08 if name=='Walk' else .0005), (file,name,'no motion',max_motion)
            assert min(ground)>-.025 and max(ground)<.04, (file,name,'ground',min(ground),max(ground))
            animations.append({'clip':name,'frames':[first,last],'loopSeamM':seam,
                'maxDisplacementM':max_motion,'groundRangeM':[min(ground),max(ground)],'pass':True})
        report['assets'].append({'file':file,'sha256':expected_hash,'dimensionsM':bounds,
            'bones':len(rig.data.bones),'animations':animations,'pass':True})
report['pass']=all(a['pass'] for a in report['assets'])
(ROOT/'artifacts/reports/pedestrians-export-check.json').write_text(json.dumps(report,indent=2))
print('PEDESTRIANS_EXPORT_PASS',flush=True)
