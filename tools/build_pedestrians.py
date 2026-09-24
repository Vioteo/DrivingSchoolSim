"""Original pedestrian art. Blender 5: -b --python tools/build_pedestrians.py.
Source: metres, Z up, -Y forward. Writes only dedicated pedestrian assets.
"""
import bpy, math, json, hashlib
from pathlib import Path
from mathutils import Vector
from datetime import datetime, timezone

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / 'ArtSource/Pedestrians'
OUT = ROOT / 'Assets/DrivingSchool/Art/Pedestrians'
WEB = ROOT / 'artifacts/visual-review/pedestrians'
for p in (ART, OUT, WEB, WEB/'models', ROOT/'artifacts/reports'):
    p.mkdir(parents=True, exist_ok=True)
M = {}
parts = []

def material(name, color):
    m = bpy.data.materials.new('Ped_' + name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    shader = m.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value = (*color, 1)
    shader.inputs['Roughness'].default_value = .78
    M[name] = m
    return m

def finish(o, name, mat, bone):
    o.name = name
    o.data.materials.append(M[mat])
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    if bone:
        o.vertex_groups.new(name=bone).add(list(range(len(o.data.vertices))), 1, 'REPLACE')
        parts.append(o)
    for f in o.data.polygons:
        f.use_smooth = True
    return o

def ellipsoid(name, loc, size, mat, bone, segments=16, rings=10):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, location=loc)
    o = bpy.context.object
    o.scale = size
    return finish(o, name, mat, bone)

def box(name, loc, size, mat, bone, bevel=.015):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    o = bpy.context.object
    o.dimensions = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        mod = o.modifiers.new('Soft seams', 'BEVEL')
        mod.width = bevel
        mod.segments = 2
        bpy.ops.object.modifier_apply(modifier=mod.name)
    return finish(o, name, mat, bone)

def loft(name, rows, mat, bone, sides=12):
    # Rows: centre x/y/z, horizontal radius, depth radius.
    vertices = [(x + rx*math.cos(i*math.tau/sides), y + ry*math.sin(i*math.tau/sides), z)
                for x,y,z,rx,ry in rows for i in range(sides)]
    faces = [tuple(reversed(range(sides)))]
    for j in range(len(rows)-1):
        for i in range(sides):
            a = j*sides+i; b = j*sides+(i+1)%sides
            faces.append((a,b,b+sides,a+sides))
    faces.append(tuple(range((len(rows)-1)*sides,len(rows)*sides)))
    me = bpy.data.meshes.new(name)
    me.from_pydata(vertices, [], faces); me.update()
    o = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(o)
    return finish(o, name, mat, bone)

def skin_transition(o, low, high, height, width):
    o.vertex_groups.clear()
    a = o.vertex_groups.new(name=low); b = o.vertex_groups.new(name=high)
    for v in o.data.vertices:
        weight = max(0,min(1,(v.co.z-height)/width+.5))
        if weight < 1: a.add([v.index],1-weight,'REPLACE')
        if weight > 0: b.add([v.index],weight,'REPLACE')

def rig_create():
    data = bpy.data.armatures.new('PedestrianSkeleton')
    rig = bpy.data.objects.new('PedestrianRig', data)
    bpy.context.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    rig.select_set(True); bpy.ops.object.mode_set(mode='EDIT')
    def bone(n,h,t,parent=None):
        b=data.edit_bones.new(n); b.head=h; b.tail=t
        if parent: b.parent=data.edit_bones[parent]
    bone('Root',(0,0,0),(0,0,.15))
    bone('Hips',(0,0,.91),(0,0,1.06),'Root')
    bone('Spine',(0,0,1.06),(0,0,1.27),'Hips')
    bone('Chest',(0,0,1.27),(0,0,1.46),'Spine')
    bone('Neck',(0,0,1.46),(0,0,1.54),'Chest')
    bone('Head',(0,0,1.54),(0,0,1.78),'Neck')
    for side,s in [('L',1),('R',-1)]:
        bone('UpperLeg_'+side,(s*.105,0,.94),(s*.105,0,.53),'Hips')
        bone('LowerLeg_'+side,(s*.105,0,.53),(s*.105,0,.12),'UpperLeg_'+side)
        bone('Foot_'+side,(s*.105,0,.12),(s*.105,-.15,.08),'LowerLeg_'+side)
        bone('Shoulder_'+side,(s*.04,0,1.44),(s*.215,0,1.43),'Chest')
        bone('UpperArm_'+side,(s*.215,0,1.43),(s*.29,-.006,1.17),'Shoulder_'+side)
        bone('Forearm_'+side,(s*.29,-.006,1.17),(s*.315,-.028,.95),'UpperArm_'+side)
        bone('Hand_'+side,(s*.315,-.028,.95),(s*.321,-.04,.84),'Forearm_'+side)
    bpy.ops.object.mode_set(mode='OBJECT')
    rig.select_set(False)
    return rig

def animate(rig):
    rig.animation_data_create()
    for action_name, frames in [('Idle',61),('Walk',31)]:
        for p in rig.pose.bones:
            p.rotation_mode='XYZ'; p.rotation_euler=(0,0,0); p.location=(0,0,0)
        act=bpy.data.actions.new(action_name)
        rig.animation_data.action=act
        for f in range(1,frames+1):
            phase=(f-1)/(frames-1)*math.tau
            for p in rig.pose.bones:
                p.rotation_euler=(0,0,0); p.location=(0,0,0)
            if action_name=='Walk':
                for side,offset in [('L',0),('R',math.pi)]:
                    q=phase+offset
                    rig.pose.bones['UpperLeg_'+side].rotation_euler.x=.36*math.cos(q)
                    rig.pose.bones['LowerLeg_'+side].rotation_euler.x=-.08-.52*max(0,math.sin(q))
                    rig.pose.bones['Foot_'+side].rotation_euler.x=.10*math.cos(q)
                    rig.pose.bones['UpperArm_'+side].rotation_euler.x=-.28*math.cos(q)
                    rig.pose.bones['Forearm_'+side].rotation_euler.x=.10+.08*(1+math.sin(q))
                rig.pose.bones['Chest'].rotation_euler.y=.04*math.cos(phase)
            else:
                rig.pose.bones['Chest'].rotation_euler.x=.009*math.sin(phase)
                rig.pose.bones['Head'].rotation_euler.z=.025*math.sin(phase)
            # Keep the lowest shoe vertex on the floor throughout either cycle.
            bpy.context.view_layer.update()
            deps=bpy.context.evaluated_depsgraph_get()
            evaluated=parts[0].evaluated_get(deps); me=evaluated.to_mesh()
            low=min(v.co.z for v in me.vertices); evaluated.to_mesh_clear()
            rig.pose.bones['Hips'].location.y=-low
            for p in rig.pose.bones:
                p.keyframe_insert('rotation_euler',frame=f,group=p.name)
                p.keyframe_insert('location',frame=f,group=p.name)
        track=rig.animation_data.nla_tracks.new(); track.name=action_name
        strip=track.strips.new(action_name,1,act)
        strip.action_frame_start=1; strip.action_frame_end=frames
        track.mute=True
    rig.animation_data.action=None
    for p in rig.pose.bones:
        p.rotation_euler=(0,0,0); p.location=(0,0,0)

def create_character(index):
    global parts
    parts=[]
    bpy.ops.object.select_all(action='DESELECT')
    female=index==1; backpack=index==2
    name=['DS_Pedestrian_A','DS_Pedestrian_B','DS_Pedestrian_C'][index]
    material('Skin',[(.58,.34,.22),(.75,.49,.34),(.27,.135,.075)][index])
    material('Hair',[(.045,.027,.019),(.10,.035,.018),(.018,.016,.015)][index])
    material('Outer',[(.045,.22,.25),(.62,.18,.075),(.49,.53,.32)][index])
    material('Trouser',[(.065,.095,.14),(.095,.075,.09),(.09,.13,.16)][index])
    material('Cream',(.78,.77,.67)); material('Dark',(.022,.027,.032))
    material('Accent',[(.26,.42,.41),(.86,.40,.17),(.18,.29,.32)][index])
    rig=rig_create(); rig.name=name
    waist=.135 if female else .155
    body=loft('Jacket',[(0,0,.94,.17,.112),(0,0,1.00,.18,.115),
        (0,0,1.13,waist,.10),(0,0,1.31,.195,.115),(0,0,1.40,.207,.10),
        (0,0,1.445,.163,.082),(0,0,1.47,.073,.062)],'Outer','Chest',16)
    skin_transition(body,'Spine','Chest',1.25,.26)
    ellipsoid('Pelvis',(0,0,.929),(.168,.108,.124),'Trouser','Hips',20,12)
    loft('Neck',[(0,0,1.44,.052,.051),(0,0,1.58,.052,.05)],'Skin','Neck')
    ellipsoid('Face',(0,-.009,1.645),(.102,.087,.137),'Skin','Head',20,14)
    # Hair cap follows the scalp, extends lower at the back, leaves the face open.
    rows=[]; vs=[]; fs=[]; segments=24; levels=7
    for j in range(levels):
        for i in range(segments):
            a=i*math.tau/segments
            back=(math.sin(a)+1)/2
            limit=1.25+(.83 if female else .43)*back
            theta=.025+(limit-.025)*j/(levels-1)
            vs.append((.106*math.sin(theta)*math.cos(a),-.006+.093*math.sin(theta)*math.sin(a),1.652+.14*math.cos(theta)))
    for j in range(levels-1):
        for i in range(segments):
            a=j*segments+i; b=j*segments+(i+1)%segments
            fs.append((a,b,b+segments,a+segments))
    fs.append(tuple(reversed(range(segments))))
    me=bpy.data.meshes.new('Hair'); me.from_pydata(vs,[],fs); me.update()
    hair=bpy.data.objects.new('Hair',me); bpy.context.collection.objects.link(hair)
    finish(hair,'Hair','Hair','Head')
    if not female:
        fringe=ellipsoid('Swept fringe',(-.032,-.061,1.746),(.073,.035,.037),'Hair','Head')
    if female:
        ellipsoid('Hair bun',(0,.085,1.71),(.068,.061,.063),'Hair','Head')
    for s in [-1,1]:
        ellipsoid('Ear',(s*.10,.0,1.64),(.019,.019,.031),'Skin','Head',12,8)
        ellipsoid('Eye',(s*.039,-.089,1.664),(.009,.005,.006),'Dark','Head',12,8)
        box('Brow',(s*.038,-.088,1.681),(.032,.006,.006),'Hair','Head',.002)
    ellipsoid('Nose',(0,-.095,1.638),(.014,.021,.024),'Skin','Head',12,8)
    box('Mouth',(0,-.091,1.604),(.028,.004,.004),'Hair','Head',.001)
    # Clothing details belong to the same skeleton and are joined for export.
    box('Zip',(0,-.114,1.245),(.012,.009,.38),'Cream','Chest',.003)
    for s in [-1,1]:
        box('Pocket welt',(s*.108,-.101,1.085),(.07,.014,.015),'Accent','Spine',.005)
    loft('Collar',[(0,0,1.43,.076,.072),(0,0,1.49,.069,.066)],'Accent','Chest')
    for side,s in [('L',1),('R',-1)]:
        leg=loft('Trousers_'+side,[(s*.105,0,.13,.057,.055),
            (s*.105,0,.23,.063,.060),(s*.105,0,.43,.070,.067),
            (s*.105,0,.53,.073,.073),(s*.105,0,.62,.079,.078),
            (s*.105,0,.80,.089,.09),(s*.103,0,.94,.09,.094)],'Trouser','UpperLeg_'+side)
        skin_transition(leg,'LowerLeg_'+side,'UpperLeg_'+side,.53,.16)
        box('Shoe sole_'+side,(s*.105,-.055,.032),(.13,.258,.058),'Cream','Foot_'+side,.022)
        ellipsoid('Sneaker_'+side,(s*.105,-.049,.092),(.065,.124,.064),'Dark','Foot_'+side)
        for y in [-.056,-.081,-.106]:
            box('Lace',(s*.105,y,.145),(.064,.007,.007),'Cream','Foot_'+side,.002)
        sleeve=loft('Sleeve_'+side,[(s*.314,-.027,.972,.042,.046),
            (s*.308,-.023,1.02,.048,.053),(s*.29,-.006,1.16,.061,.066),
            (s*.279,0,1.22,.066,.067),(s*.23,0,1.40,.072,.077),
            (s*.20,0,1.447,.054,.063)],'Outer','UpperArm_'+side)
        skin_transition(sleeve,'Forearm_'+side,'UpperArm_'+side,1.17,.13)
        loft('Cuff_'+side,[(s*.315,-.028,.95,.043,.044),(s*.311,-.025,.995,.043,.046)],'Accent','Forearm_'+side)
        ellipsoid('Hand_'+side,(s*.32,-.032,.898),(.034,.027,.064),'Skin','Hand_'+side)
        ellipsoid('Thumb_'+side,(s*.294,-.052,.918),(.017,.02,.031),'Skin','Hand_'+side,12,8)
    if backpack:
        box('Backpack',(0,.167,1.258),(.272,.16,.32),'Accent','Chest',.055)
        box('Pack pocket',(0,.255,1.21),(.22,.055,.15),'Outer','Chest',.025)
        for s in [-1,1]:
            box('Strap',(s*.126,-.107,1.32),(.028,.025,.26),'Accent','Chest',.011)
    bpy.ops.object.select_all(action='DESELECT')
    for o in parts: o.select_set(True)
    bpy.context.view_layer.objects.active=parts[0]; bpy.ops.object.join()
    body=bpy.context.object; body.name=name+'_Body'; parts=[body]
    # Consistent outward normals, including the scalp cap.
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.normals_make_consistent(inside=False); bpy.ops.object.mode_set(mode='OBJECT')
    body.parent=rig
    mod=body.modifiers.new('Pedestrian skin','ARMATURE'); mod.object=rig
    animate(rig)
    return name,rig,body

def select_asset(rig, body):
    bpy.ops.object.select_all(action='DESELECT'); rig.select_set(True); body.select_set(True)
    bpy.context.view_layer.objects.active=rig

def render(path, pos, target=(0,0,.95)):
    camera=bpy.data.objects.get('Review Camera')
    camera.location=pos; camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler()
    bpy.context.scene.render.filepath=str(path); bpy.ops.render.render(write_still=True)

def stage():
    scene=bpy.context.scene; scene.render.engine='CYCLES'; scene.cycles.samples=24
    scene.cycles.use_denoising=True
    scene.render.resolution_x=960; scene.render.resolution_y=720; scene.render.resolution_percentage=100
    scene.world.color=(.25,.25,.25)
    material('Stage',(.12,.155,.17))
    box('Review floor',(0,0,-.052),(200,200,.1),'Stage',None,0)
    for name,loc,power,size in [('Key',(-3,-4,6),700,5),('Fill',(4,-1,4),450,4),('Rim',(0,3,5),900,3)]:
        d=bpy.data.lights.new(name,'AREA'); d.energy=power; d.shape='DISK'; d.size=size
        o=bpy.data.objects.new(name,d); bpy.context.collection.objects.link(o); o.location=loc
        o.rotation_euler=(Vector((0,0,1))-o.location).to_track_quat('-Z','Y').to_euler()
    data=bpy.data.cameras.new('Review Camera'); cam=bpy.data.objects.new('Review Camera',data)
    bpy.context.collection.objects.link(cam); scene.camera=cam; data.type='ORTHO'; data.ortho_scale=2.9

def main():
    bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
    scene=bpy.context.scene; scene.render.fps=30; scene.unit_settings.system='METRIC'
    records=[]; assets=[]
    for index in range(3):
        # Avoid sharing animation names with another rig when baking FBX.
        for a in list(bpy.data.actions): bpy.data.actions.remove(a)
        if index:
            bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
        name,rig,body=create_character(index)
        scene.frame_set(1)
        select_asset(rig,body)
        bpy.ops.wm.save_as_mainfile(filepath=str(ART/(name+'.blend')))
        for track in rig.animation_data.nla_tracks: track.mute=False
        bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')),use_selection=True,
            object_types={'ARMATURE','MESH'},axis_forward='-Z',axis_up='Y',add_leaf_bones=False,
            bake_anim=True,bake_anim_use_all_actions=False,bake_anim_use_nla_strips=True,
            bake_anim_simplify_factor=0,apply_scale_options='FBX_SCALE_UNITS')
        bpy.ops.export_scene.gltf(filepath=str(WEB/'models'/(name+'.glb')),export_format='GLB',
            use_selection=True,export_animations=True,export_animation_mode='NLA_TRACKS',
            export_force_sampling=True,export_frame_range=False)
        for track in rig.animation_data.nla_tracks: track.mute=True
        rig.animation_data.action=None
        for p in rig.pose.bones: p.rotation_euler=(0,0,0); p.location=(0,0,0)
        body.data.calc_loop_triangles()
        records.append({'name':name,'triangles':len(body.data.loop_triangles),'bones':len(rig.data.bones),
            'materials':len(body.data.materials),'clips':['Idle','Walk'],
            'heightM':max(v.co.z for v in body.data.vertices)-min(v.co.z for v in body.data.vertices),
            'files':{str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest()
                for p in [ART/(name+'.blend'),OUT/(name+'.fbx'),WEB/'models'/(name+'.glb')]}})
        stage()
        render(WEB/(name+'-front.png'),(2,-6,2.3))
        render(WEB/(name+'-rear.png'),(-2,6,2.3))
        rig.animation_data.action=bpy.data.actions['Walk']; scene.frame_set(5)
        render(WEB/(name+'-walk.png'),(4,-6,2.2))
        assets.append(name)
    # Reimport the actual GLBs for a collective render and export verification.
    bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
    for i,name in enumerate(assets):
        old=set(bpy.context.scene.objects)
        bpy.ops.import_scene.gltf(filepath=str(WEB/'models'/(name+'.glb')))
        imported=set(bpy.context.scene.objects)-old
        for o in imported:
            if o.animation_data:
                o.animation_data.action=None
                for track in o.animation_data.nla_tracks: track.mute=True
            if o.type=='ARMATURE':
                for p in o.pose.bones: p.rotation_euler=(0,0,0); p.location=(0,0,0)
            if not o.parent: o.location.x+=(i-1)*.95
    stage(); scene.camera.data.ortho_scale=3.5
    render(WEB/'lineup.png',(2,-8,2.8),target=(0,0,.93))
    report={'revision':1,'utc':datetime.now(timezone.utc).isoformat(),'blender':bpy.app.version_string,
        'units':'metres','sourceAxes':'Z up, -Y forward','assets':records,
        'scope':'Authored meshes, generic skeleton and in-place animation. No pedestrian navigation, LODs or traffic AI.'}
    (ROOT/'artifacts/reports/pedestrians-manifest.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print('PEDESTRIANS_GENERATED',flush=True)

if __name__=='__main__': main()
