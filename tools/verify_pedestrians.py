"""Reimport pedestrian FBX/GLB and measure skinned animation, without changing exports.
Blender 5: -b --python tools/verify_pedestrians.py   (or `python tools/verify_pedestrians.py` with the bpy module).
"""
import bpy, json, hashlib, math
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
manifest=json.loads((ROOT/'artifacts/reports/pedestrians-manifest.json').read_text(encoding='utf-8'))
report={'scope':'Blender reimport of final FBX and GLB; Unity has a separate report.', 'assets':[]}
# Minimum displacement of any vertex over a cycle, per clip family (m): catches clips that did not export.
MOTION={'Walk':.08,'Run':.15,'LookAround':.05,'Idle':.0005}
SIGNAL_MOTION=.0005
# The lowest vertex must stay on the floor: feet are locked to Z = 0 by the generator; Run has flight.
GROUND=(-.025,.04)
GROUND_RUN=(-.025,.08)

def vertices():
    bpy.context.view_layer.update()
    deps=bpy.context.evaluated_depsgraph_get(); points=[]
    for o in bpy.context.scene.objects:
        if o.type!='MESH' or not any(m.type=='ARMATURE' for m in o.modifiers): continue
        ev=o.evaluated_get(deps); mesh=ev.to_mesh()
        points.extend(ev.matrix_world@v.co for v in mesh.vertices)
        ev.to_mesh_clear()
    return points

def action_for(clip):
    # FBX import names actions "<rig>|<take>", glTF keeps the NLA track name.
    return next(a for a in bpy.data.actions if clip in a.name.split('|'))

for asset in manifest['assets']:
    clips=asset['clips']
    for file,expected_hash in asset['files'].items():
        path=ROOT/file
        assert hashlib.sha256(path.read_bytes()).hexdigest()==expected_hash, 'Hash mismatch: '+file
        if path.suffix=='.blend': continue
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.context.scene.render.fps=manifest['fps']   # glTF import converts seconds to scene frames
        if path.suffix=='.fbx': bpy.ops.import_scene.fbx(filepath=str(path))
        else: bpy.ops.import_scene.gltf(filepath=str(path))
        rigs=[o for o in bpy.context.scene.objects if o.type=='ARMATURE']
        assert len(rigs)==1
        rig=rigs[0]
        if path.suffix=='.fbx':
            # The FBX importer stretches Root to its only child and connects Hips, and Blender ignores
            # the location of connected bones. Unity has plain transforms; undo it to see the real pose.
            bpy.context.view_layer.objects.active=rig; bpy.ops.object.mode_set(mode='EDIT')
            for bone in rig.data.edit_bones: bone.use_connect=False
            bpy.ops.object.mode_set(mode='OBJECT')
        for t in rig.animation_data.nla_tracks: t.mute=True
        # glTF import may retain bind-axis corrections in pose transforms.
        # Sample the actual rest-like Idle frame instead of zeroing those corrections.
        rig.animation_data.action=action_for('Idle')
        bpy.context.scene.frame_set(1)
        pts=vertices()
        bounds=[max(p[i] for p in pts)-min(p[i] for p in pts) for i in range(3)]
        assert abs(bounds[2]-asset['heightM'])<.005, (file,bounds)
        animations=[]
        for name,info in clips.items():
            action=action_for(name)
            rig.animation_data.action=action
            first,last=action.frame_range
            assert round(last-first)+1==info['frames'], (file,name,'frames',action.frame_range)
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
            need=MOTION.get(name,SIGNAL_MOTION)
            assert seam<.002, (file,name,'loop seam',seam)
            assert max_motion>need, (file,name,'no motion',max_motion)
            low,high=GROUND_RUN if name=='Run' else GROUND
            assert low<min(ground) and max(ground)<high, (file,name,'ground',min(ground),max(ground))
            animations.append({'clip':name,'frames':[first,last],'loopSeamM':seam,
                'maxDisplacementM':max_motion,'groundRangeM':[min(ground),max(ground)],'pass':True})
        assert {a['clip'] for a in animations}==set(clips), (file,'clip set')
        report['assets'].append({'file':file,'sha256':expected_hash,'dimensionsM':bounds,
            'bones':len(rig.data.bones),'animations':animations,'pass':True})
report['pass']=all(a['pass'] for a in report['assets'])
(ROOT/'artifacts/reports/pedestrians-export-check.json').write_text(json.dumps(report,indent=2))
print('PEDESTRIANS_EXPORT_PASS',flush=True)
