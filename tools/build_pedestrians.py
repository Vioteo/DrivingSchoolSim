"""Pedestrian kit: adults, children, traffic police officer and their in-place motion clips.
Blender 5: -b --python tools/build_pedestrians.py   (or `python tools/build_pedestrians.py` with the bpy module).
Source: metres, Z up, -Y forward. Writes only dedicated pedestrian assets. Geometry: tools/pedestrian_body.py.

Clips (all loop, last frame == first frame, root stays in place, feet locked to Z = 0):
  Idle, Walk, Run, LookAround            - adults and children
  Idle, Walk, LookAround, Signal_*       - police officer (regulator gestures, see docs/pedestrians.md)
"""
import bpy, math, json, hashlib, sys
from pathlib import Path
from mathutils import Vector, Quaternion
from datetime import datetime, timezone
sys.path.insert(0, str(Path(__file__).resolve().parent))
import pedestrian_mh as mh
import fetch_makehuman as fetch
from pedestrian_mh import ARM_OUT

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / 'ArtSource/Pedestrians'
OUT = ROOT / 'Assets/DrivingSchool/Art/Pedestrians'
TEXTURES = OUT / 'Textures'
WEB = ROOT / 'artifacts/visual-review/pedestrians'
for p in (ART, OUT, TEXTURES, WEB, WEB/'models', ROOT/'artifacts/reports'):
    p.mkdir(parents=True, exist_ok=True)
FPS = 30
TAU = math.tau
parts = []

CIVIL_CLIPS = ['Idle', 'Walk', 'Run', 'LookAround']
POLICE_CLIPS = ['Idle', 'Walk', 'LookAround', 'Signal_ArmsSide', 'Signal_RightArmForward', 'Signal_ArmUp']
# Frames per clip including the closing frame that repeats frame 1.
FRAMES = {'adult': {'Idle': 61, 'Walk': 31, 'Run': 23, 'LookAround': 91},
          'child': {'Idle': 61, 'Walk': 25, 'Run': 19, 'LookAround': 91}}
SIGNAL_FRAMES = 61
RUN_FLIGHT = .045   # m, body rise while both feet are off the ground
# MakeHuman (CC0) characters: macro morph values, skin and fitted proxies from the system asset pack.
# build: 'child' selects the child clip timing. Age in years.
EYES = [('eyes', 'eyes/low-poly/low-poly.mhclo'), ('eyelashes', 'eyelashes/eyelashes01/eyelashes01.mhclo')]
CHARACTERS = [
    dict(name='DS_Pedestrian_A', build='male', skin='skins/young_caucasian_male/young_caucasian_male.mhmat',
         macro=dict(gender=1, age=27, muscle=.55, weight=.5, height=.55),
         proxies=[('suit', 'clothes/male_casualsuit05/male_casualsuit05.mhclo'), ('shoes', 'clothes/shoes01/shoes01.mhclo'),
                  ('hair', 'hair/short02/short02.mhclo'), ('eyebrows', 'eyebrows/eyebrow001/eyebrow001.mhclo')] + EYES),
    dict(name='DS_Pedestrian_B', build='female', skin='skins/young_caucasian_female/young_caucasian_female.mhmat',
         macro=dict(gender=0, age=32, muscle=.45, weight=.48, height=.5),
         proxies=[('suit', 'clothes/female_elegantsuit01/female_elegantsuit01.mhclo'), ('shoes', 'clothes/shoes03/shoes03.mhclo'),
                  ('hair', 'hair/ponytail01/ponytail01.mhclo'), ('eyebrows', 'eyebrows/eyebrow010/eyebrow010.mhclo')] + EYES),
    dict(name='DS_Pedestrian_C', build='male', backpack=True, skin='skins/young_african_male/young_african_male.mhmat',
         macro=dict(gender=1, age=35, muscle=.6, weight=.6, height=.4, race={'african': 1.}),
         proxies=[('suit', 'clothes/male_worksuit01/male_worksuit01.mhclo'), ('shoes', 'clothes/shoes02/shoes02.mhclo'),
                  ('hair', 'hair/afro01/afro01.mhclo'), ('eyebrows', 'eyebrows/eyebrow002/eyebrow002.mhclo')] + EYES),
    # Schoolboy with a satchel and schoolgirl with a braid, about 8 years old.
    dict(name='DS_Pedestrian_Child_A', build='child', backpack=True,
         skin='skins/young_caucasian_male/young_caucasian_male.mhmat', macro=dict(gender=1, age=8, weight=.5, height=.66),
         proxies=[('suit', 'clothes/male_casualsuit06/male_casualsuit06.mhclo'), ('shoes', 'clothes/shoes06/shoes06.mhclo'),
                  ('hair', 'hair/short03/short03.mhclo'), ('eyebrows', 'eyebrows/eyebrow001/eyebrow001.mhclo')] + EYES),
    dict(name='DS_Pedestrian_Child_B', build='child',
         skin='skins/young_caucasian_female/young_caucasian_female.mhmat', macro=dict(gender=0, age=8, weight=.5, height=.74),
         proxies=[('suit', 'clothes/female_casualsuit01/female_casualsuit01.mhclo'), ('shoes', 'clothes/shoes05/shoes05.mhclo'),
                  ('hair', 'hair/braid01/braid01.mhclo'), ('eyebrows', 'eyebrows/eyebrow010/eyebrow010.mhclo')] + EYES),
    # Traffic police officer: suit retinted to uniform navy, then vest, peaked cap and baton (tools/pedestrian_mh.py).
    dict(name='DS_Pedestrian_Police', build='male', police=True,
         skin='skins/middleage_caucasian_male/middleage_caucasian_male.mhmat',
         macro=dict(gender=1, age=38, muscle=.6, weight=.55, height=.45),
         proxies=[('suit', 'clothes/male_elegantsuit01/male_elegantsuit01.mhclo'), ('shoes', 'clothes/shoes03/shoes03.mhclo'),
                  ('hair', 'hair/short01/short01.mhclo'), ('eyebrows', 'eyebrows/eyebrow002/eyebrow002.mhclo')] + EYES,
         materials={'suit': {'tint': (.16, .22, .42), 'tris': 9000}}),
]

# ---------------------------------------------------------------- animation
# Rotations are authored about armature axes and converted to bone-local space, so the sign
# convention is anatomical and independent of bone roll:
#   flex(+)   rotates a bone pointing down forwards (-Y): hip/shoulder flexion, knee(-), elbow(+), toe up(+)
#   abduct(+) raises an arm or leg sideways, away from the body
#   yaw(+)    turns towards the character's left (+X)
FLEX = Vector((-1, 0, 0))
YAW = Vector((0, 0, 1))

def bump(q, centre, width):
    d = (q - centre + math.pi) % TAU - math.pi
    return math.exp(-(d/width)**2)

def smooth_keys(t, keys):
    for (t0,v0),(t1,v1) in zip(keys, keys[1:]):
        if t0 <= t <= t1:
            u = (t-t0)/(t1-t0) if t1 > t0 else 1
            u = u*u*(3-2*u)
            return v0 + (v1-v0)*u
    return keys[-1][1]

class Pose:
    def __init__(self):
        self.rot = {}
        self.lift = 0
    def turn(self, bone, axis, angle):
        # Later calls apply after earlier ones (in armature space).
        self.rot[bone] = Quaternion(axis, angle) @ self.rot.get(bone, Quaternion())
    def flex(self, bone, angle): self.turn(bone, FLEX, angle)
    def yaw(self, bone, angle): self.turn(bone, YAW, angle)
    def abduct(self, side, bone, angle):
        self.turn(bone+'_'+side, Vector((0, -1 if side == 'L' else 1, 0)), angle)

def breathe(p, phase, amount=1):
    p.flex('Chest', -.010*amount*math.sin(phase))
    p.flex('Neck', .006*amount*math.sin(phase))
    for side in 'LR':
        p.flex('Forearm_'+side, .06)
        p.abduct(side, 'UpperArm', .012*amount*(1+math.sin(phase)))

def pose_idle(t):
    p = Pose(); phase = t*TAU
    breathe(p, phase)
    p.yaw('Head', .03*math.sin(phase))
    p.turn('Hips', Vector((0,1,0)), .012*math.sin(phase))
    return p

def pose_look_around(t):
    # Kerbside check before crossing, right-hand traffic: left, right, left again, back to front.
    p = Pose(); breathe(p, t*TAU*1.5)
    look = smooth_keys(t, [(0,0),(.08,0),(.25,.95),(.38,.95),(.58,-.95),(.72,-.95),(.84,.45),(.9,.45),(.98,0),(1,0)])
    p.yaw('Spine', .10*look); p.yaw('Chest', .22*look)
    p.yaw('Neck', .25*look); p.yaw('Head', .45*look)
    p.flex('Head', .05*abs(look))
    return p

def pose_gait(t, run):
    p = Pose(); phase = t*TAU
    for side,off in (('L',0),('R',math.pi)):
        q = (phase+off) % TAU        # q = 0: heel strike of this leg
        if run:
            hip = .28 + .66*math.cos(q+.6)
            knee = -(.25 + .25*bump(q,.6,.4) + 1.65*bump(q,4.3,.9))
            foot = .08*bump(q,0,.5) - .80*bump(q,2.6,.45) + .10*bump(q,5.2,.8)
            shoulder = .10 - .75*math.cos(q+.6)
            elbow = 1.45 - .25*math.cos(q+.6)
        else:
            hip = .06 + .42*math.cos(q+.5)
            knee = -(.08 + .22*bump(q,.75,.45) + 1.00*bump(q,4.4,.85))
            foot = .20*bump(q,0,.45) - .65*bump(q,3.75,.45) + .12*bump(q,5.6,.7)
            shoulder = .03 - .36*math.cos(q+.5)
            elbow = .22 + .12*(1-math.cos(q+.5))/2
        p.flex('UpperLeg_'+side, hip)
        p.flex('LowerLeg_'+side, knee)
        p.flex('Foot_'+side, foot-hip-knee)   # foot pitch in the world = hip + knee + ankle
        p.flex('UpperArm_'+side, shoulder)
        p.flex('Forearm_'+side, elbow)
        if run: p.abduct(side, 'Forearm', -.30)
    if run:
        # Flight after each toe-off, before the other foot strikes (q = pi).
        p.lift = RUN_FLIGHT*(bump(phase,2.85,.3) + bump(phase,2.85+math.pi,.3))
    swing = math.cos(phase+(.6 if run else .5))
    p.yaw('Hips', -(.10 if run else .07)*swing)
    p.yaw('Chest', (.16 if run else .10)*swing)
    p.yaw('Head', -.02*swing)
    p.flex('Spine', -(.20 if run else .035))
    p.flex('Head', .16 if run else .03)
    return p

def pose_signal(kind):
    def build(t):
        p = Pose(); breathe(p, t*TAU, .6)
        if kind == 'ArmsSide':
            for side in 'LR':
                p.abduct(side, 'UpperArm', math.pi/2 - ARM_OUT); p.abduct(side, 'Forearm', .05)
        elif kind == 'RightArmForward':
            p.abduct('R', 'UpperArm', -ARM_OUT-.06); p.flex('UpperArm_R', 1.50)
            p.flex('Forearm_R', -.04)
        elif kind == 'ArmUp':
            p.abduct('R', 'UpperArm', -ARM_OUT-.02); p.flex('UpperArm_R', 2.98)
            p.flex('Forearm_R', -.04)
        return p
    return build

def clip_builder(name):
    if name == 'Idle': return pose_idle
    if name == 'LookAround': return pose_look_around
    if name == 'Walk': return lambda t: pose_gait(t, False)
    if name == 'Run': return lambda t: pose_gait(t, True)
    return pose_signal(name.split('_',1)[1])

def lowest_point():
    bpy.context.view_layer.update()
    deps = bpy.context.evaluated_depsgraph_get()
    evaluated = parts[0].evaluated_get(deps); me = evaluated.to_mesh()
    low = min(v.co.z for v in me.vertices); evaluated.to_mesh_clear()
    return low

def apply_pose(rig, pose, previous):
    for pb in rig.pose.bones:
        rest = pb.bone.matrix_local.to_3x3().to_quaternion()
        local = rest.inverted() @ pose.rot.get(pb.name, Quaternion()) @ rest
        pb.rotation_euler = local.to_euler('XYZ', previous.get(pb.name, pb.rotation_euler))
        previous[pb.name] = pb.rotation_euler.copy()
        pb.location = (0,0,0)
    hips = rig.pose.bones['Hips']
    rest = hips.bone.matrix_local.to_3x3()
    # Keep the lowest shoe vertex on the floor through every frame of every cycle.
    hips.location = rest.inverted() @ Vector((0, 0, pose.lift-lowest_point()))

def natural_speed(rig, frames):
    # In-place cycle: while a foot is planted it slides backwards (+Y) at the speed the body would travel.
    # Median over contact intervals (lower ankle within 3 cm of its lowest height): flight, heel strike
    # and toe-off do not skew it.
    track = []
    for f in range(1, frames+1):
        bpy.context.scene.frame_set(f)
        track.append([rig.matrix_world @ rig.pose.bones['Foot_'+s].head for s in 'LR'])
    floor = min(p.z for feet in track for p in feet)
    rates = []
    for a,b in zip(track, track[1:]):
        planted = 0 if a[0].z < a[1].z else 1
        if a[planted].z < floor+.03 and b[planted].z < floor+.03:
            rates.append((b[planted].y - a[planted].y)*FPS)
    rates.sort()
    return rates[len(rates)//2]

def animate(rig, spec):
    rig.animation_data_create()
    for pb in rig.pose.bones: pb.rotation_mode = 'XYZ'
    table = FRAMES['child' if spec['build'] == 'child' else 'adult']
    info = {}
    for clip in spec['clips']:
        frames = table.get(clip, SIGNAL_FRAMES)
        builder = clip_builder(clip)
        act = bpy.data.actions.new(clip)
        rig.animation_data.action = act
        previous = {}
        for f in range(1, frames+1):
            apply_pose(rig, builder((f-1)/(frames-1)), previous)
            for pb in rig.pose.bones:
                pb.keyframe_insert('rotation_euler', frame=f, group=pb.name)
                pb.keyframe_insert('location', frame=f, group=pb.name)
        info[clip] = {'frames': frames, 'lengthS': round((frames-1)/FPS, 4)}
        if clip in ('Walk', 'Run'):
            info[clip]['naturalSpeedMps'] = round(natural_speed(rig, frames), 3)
        track = rig.animation_data.nla_tracks.new(); track.name = clip
        strip = track.strips.new(clip, 1, act)
        strip.action_frame_start = 1; strip.action_frame_end = frames
        track.mute = True
    rig.animation_data.action = None
    for pb in rig.pose.bones:
        pb.rotation_euler = (0,0,0); pb.location = (0,0,0)
    return info

# ---------------------------------------------------------------- geometry
def create_character(spec):
    global parts
    kit = mh.Kit(TEXTURES)
    J, roles, W, co = mh.build(spec, kit, fetch.assets())
    rig = mh.rig_create(J, spec['name'])
    mh.relax_arms(rig, kit.parts)
    mh.extras(spec, kit, rig, roles)
    mh.activate(kit.parts[0])
    for o in kit.parts: o.select_set(True)
    bpy.ops.object.join()
    body = bpy.context.object; body.name = spec['name'] + '_Body'
    parts = [body]
    body.parent = rig
    mod = body.modifiers.new('Pedestrian skin', 'ARMATURE'); mod.object = rig
    used = {m.name for m in body.data.materials}
    (OUT/(spec['name']+'.materials.json')).write_text(json.dumps(
        {'materials': [r for r in kit.unity.values() if r['name'] in used]}, indent=2), encoding='utf-8')
    clips = animate(rig, spec)
    return rig, body, clips

# ---------------------------------------------------------------- review renders
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
    if scene.world is None: scene.world=bpy.data.worlds.new('Review')
    scene.world.color=(.25,.25,.25)
    bpy.ops.mesh.primitive_plane_add(size=200, location=(0,0,0)); floor=bpy.context.object; floor.name='Review floor'
    m=bpy.data.materials.new('Stage'); m.use_nodes=True
    m.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=(.12,.155,.17,1); floor.data.materials.append(m)
    for name,loc,power,size in [('Key',(-3,-4,6),700,5),('Fill',(4,-1,4),450,4),('Rim',(0,3,5),900,3)]:
        d=bpy.data.lights.new(name,'AREA'); d.energy=power; d.shape='DISK'; d.size=size
        o=bpy.data.objects.new(name,d); bpy.context.collection.objects.link(o); o.location=loc
        o.rotation_euler=(Vector((0,0,1))-o.location).to_track_quat('-Z','Y').to_euler()
    data=bpy.data.cameras.new('Review Camera'); cam=bpy.data.objects.new('Review Camera',data)
    bpy.context.collection.objects.link(cam); scene.camera=cam; data.type='ORTHO'; data.ortho_scale=2.9
    return data

def reset():
    # Fresh file per asset: no shared materials (`.001` suffixes) or actions leak into the next FBX.
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene=bpy.context.scene; scene.render.fps=FPS; scene.unit_settings.system='METRIC'
    return scene

def show_clip(rig, clip, frame):
    rig.animation_data.action=bpy.data.actions[clip]; bpy.context.scene.frame_set(frame)

def rel(p): return p.relative_to(ROOT).as_posix()

def main():
    records=[]
    for spec in CHARACTERS:
        spec['clips'] = POLICE_CLIPS if spec.get('police') else CIVIL_CLIPS
        scene=reset()
        name=spec['name']
        rig,body,clips=create_character(spec)
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
            'materials':len(body.data.materials),'clips':clips,
            'heightM':max(v.co.z for v in body.data.vertices)-min(v.co.z for v in body.data.vertices),
            'files':{rel(p):hashlib.sha256(p.read_bytes()).hexdigest()
                for p in [ART/(name+'.blend'),OUT/(name+'.fbx'),WEB/'models'/(name+'.glb')]}})
        camera=stage()
        child=spec['build']=='child'
        if child: camera.ortho_scale=2.2
        target=(0,0,.65) if child else (0,0,.95)
        render(WEB/(name+'-front.png'),(2,-6,2.3),target)
        render(WEB/(name+'-rear.png'),(-2,6,2.3),target)
        # Side views of the gait: contact and passing positions.
        show_clip(rig,'Walk',1); render(WEB/(name+'-walk.png'),(6,-1.5,1.4),target)
        if 'Run' in clips:
            show_clip(rig,'Run',4); render(WEB/(name+'-run.png'),(6,-1.5,1.4),target)
        show_clip(rig,'LookAround',27); render(WEB/(name+'-look.png'),(2,-6,2.3),target)
        for clip in clips:
            if clip.startswith('Signal_'):
                camera.ortho_scale=3.4
                show_clip(rig,clip,1); render(WEB/(name+'-'+clip.lower()+'.png'),(3,-5,2.2),(0,0,1.15))
    # Reimport the actual GLBs for a collective render and export verification.
    scene=reset()
    for i,rec in enumerate(records):
        old=set(scene.objects)
        bpy.ops.import_scene.gltf(filepath=str(WEB/'models'/(rec['name']+'.glb')))
        imported=set(scene.objects)-old
        for o in imported:
            if o.animation_data:
                o.animation_data.action=None
                for track in o.animation_data.nla_tracks: track.mute=True
            if o.type=='ARMATURE':
                for p in o.pose.bones: p.rotation_euler=(0,0,0); p.location=(0,0,0)
            if not o.parent: o.location.x+=(i-(len(records)-1)/2)*.95
    camera=stage(); camera.ortho_scale=6.2
    render(WEB/'lineup.png',(2,-8,2.8),target=(0,0,.93))
    report={'revision':2,'utc':datetime.now(timezone.utc).isoformat(),'blender':bpy.app.version_string,
        'units':'metres','sourceAxes':'Z up, -Y forward','fps':FPS,'assets':records,
        'scope':'Authored meshes, generic skeleton and in-place looping clips. No pedestrian navigation, LODs or traffic AI.'}
    (ROOT/'artifacts/reports/pedestrians-manifest.json').write_text(json.dumps(report,indent=2,ensure_ascii=False),encoding='utf-8')
    print('PEDESTRIANS_GENERATED',flush=True)

if __name__=='__main__': main()
