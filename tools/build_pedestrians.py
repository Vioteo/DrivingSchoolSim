"""Pedestrian kit: adults, children, traffic police officer and their in-place motion clips.
Blender 5: -b --python tools/build_pedestrians.py   (or `python tools/build_pedestrians.py` with the bpy module).
Source: metres, Z up, -Y forward. Writes only dedicated pedestrian assets.

Clips (all loop, last frame == first frame, root stays in place, feet locked to Z = 0):
  Idle, Walk, Run, LookAround            - adults and children
  Idle, Walk, LookAround, Signal_*       - police officer (regulator gestures, see docs/pedestrians.md)
"""
import bpy, math, json, hashlib
from pathlib import Path
from mathutils import Vector, Quaternion
from datetime import datetime, timezone

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / 'ArtSource/Pedestrians'
OUT = ROOT / 'Assets/DrivingSchool/Art/Pedestrians'
WEB = ROOT / 'artifacts/visual-review/pedestrians'
for p in (ART, OUT, WEB, WEB/'models', ROOT/'artifacts/reports'):
    p.mkdir(parents=True, exist_ok=True)
FPS = 30
TAU = math.tau
M = {}
parts = []

CIVIL_CLIPS = ['Idle', 'Walk', 'Run', 'LookAround']
POLICE_CLIPS = ['Idle', 'Walk', 'LookAround', 'Signal_ArmsSide', 'Signal_RightArmForward', 'Signal_ArmUp']
# Frames per clip including the closing frame that repeats frame 1.
FRAMES = {'adult': {'Idle': 61, 'Walk': 31, 'Run': 23, 'LookAround': 91},
          'child': {'Idle': 61, 'Walk': 25, 'Run': 19, 'LookAround': 91}}
RUN_FLIGHT = .045   # m, body rise while both feet are off the ground
SIGNAL_FRAMES = 61
CHARACTERS = [
    dict(name='DS_Pedestrian_A', skin=(.58,.34,.22), hair=(.045,.027,.019), outer=(.045,.22,.25),
         trouser=(.065,.095,.14), accent=(.26,.42,.41)),
    dict(name='DS_Pedestrian_B', female=True, skin=(.75,.49,.34), hair=(.10,.035,.018), outer=(.62,.18,.075),
         trouser=(.095,.075,.09), accent=(.86,.40,.17)),
    dict(name='DS_Pedestrian_C', backpack=True, skin=(.27,.135,.075), hair=(.018,.016,.015), outer=(.49,.53,.32),
         trouser=(.09,.13,.16), accent=(.18,.29,.32)),
    # Schoolboy with a bright satchel and schoolgirl in a pink jacket (about 8 years, 1.26 m).
    dict(name='DS_Pedestrian_Child_A', child=True, backpack=True, skin=(.70,.45,.31), hair=(.16,.09,.04),
         outer=(.80,.33,.03), trouser=(.07,.12,.26), accent=(.08,.30,.72)),
    dict(name='DS_Pedestrian_Child_B', child=True, female=True, skin=(.78,.52,.38), hair=(.28,.13,.05),
         outer=(.72,.18,.40), trouser=(.12,.10,.16), accent=(.95,.78,.12)),
    # Traffic police officer: dark-blue uniform, lime vest with reflective bands, peaked cap, striped baton.
    dict(name='DS_Pedestrian_Police', police=True, skin=(.66,.42,.29), hair=(.06,.04,.03), outer=(.05,.07,.12),
         trouser=(.045,.06,.10), accent=(.035,.045,.07)),
]

def material(name, color, roughness=.78):
    m = bpy.data.materials.new('Ped_' + name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    shader = m.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value = (*color, 1)
    shader.inputs['Roughness'].default_value = roughness
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

def cylinder(name, a, b, radius, mat, bone, sides=10):
    a, b = Vector(a), Vector(b)
    bpy.ops.mesh.primitive_cylinder_add(vertices=sides, radius=radius, depth=(b-a).length, location=(a+b)/2)
    o = bpy.context.object
    o.rotation_mode = 'QUATERNION'
    o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(b-a)
    return finish(o, name, mat, bone)

def loft(name, rows, mat, bone, sides=12):
    # Rows: centre x/y/z, horizontal radius, depth radius.
    vertices = [(x + rx*math.cos(i*TAU/sides), y + ry*math.sin(i*TAU/sides), z)
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

def label(name, text, loc, size, mat, bone):
    # Cyrillic lettering as mesh from Blender's built-in font, facing +Y (the back).
    bpy.ops.object.text_add(location=loc)
    o = bpy.context.object
    o.data.body = text; o.data.size = size; o.data.extrude = .0015; o.data.resolution_u = 3
    o.data.align_x = 'CENTER'; o.data.align_y = 'CENTER'
    o.rotation_euler = (math.pi/2, 0, math.pi)
    bpy.ops.object.convert(target='MESH')
    return finish(bpy.context.object, name, mat, bone)

def skin_transition(o, low, high, height, width):
    o.vertex_groups.clear()
    a = o.vertex_groups.new(name=low); b = o.vertex_groups.new(name=high)
    for v in o.data.vertices:
        weight = max(0,min(1,(v.co.z-height)/width+.5))
        if weight < 1: a.add([v.index],1-weight,'REPLACE')
        if weight > 0: b.add([v.index],weight,'REPLACE')

# Children are authored in adult coordinates and warped: shorter, slightly stockier, larger head.
CHILD_SCALE, CHILD_WIDTH, CHILD_HEAD = .68, 1.07, 1.26
HEAD_PIVOT = Vector((0, 0, 1.54))

def child_warp(co, head):
    if head:
        return HEAD_PIVOT*CHILD_SCALE + (co-HEAD_PIVOT)*CHILD_SCALE*CHILD_HEAD
    return Vector((co.x*CHILD_SCALE*CHILD_WIDTH, co.y*CHILD_SCALE*CHILD_WIDTH, co.z*CHILD_SCALE))

def rig_create(child):
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
    if child:
        for b in data.edit_bones:
            head = b.name == 'Head'
            b.head, b.tail = child_warp(b.head.copy(), head), child_warp(b.tail.copy(), head)
    bpy.ops.object.mode_set(mode='OBJECT')
    rig.select_set(False)
    return rig

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
                p.abduct(side, 'UpperArm', 1.30); p.abduct(side, 'Forearm', .06)
        elif kind == 'RightArmForward':
            p.abduct('R', 'UpperArm', -.24); p.flex('UpperArm_R', 1.50)
            p.flex('Forearm_R', -.04)
        elif kind == 'ArmUp':
            p.abduct('R', 'UpperArm', -.20); p.flex('UpperArm_R', 2.98)
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
    table = FRAMES['child' if spec.get('child') else 'adult']
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
def police_kit():
    # Lime vest over the jacket with two reflective bands and lettering on the back.
    vest = loft('Vest',[(0,0,.985,.188,.125),(0,0,1.13,.17,.114),(0,0,1.31,.209,.129),
        (0,0,1.40,.219,.114),(0,0,1.43,.19,.10)],'Vest','Chest',16)
    skin_transition(vest,'Spine','Chest',1.25,.26)
    for z,rx,ry in ((1.085,.176,.117),(1.215,.188,.121)):
        # Radii follow the vest at this height plus the band thickness.
        band = loft('Vest band',[(0,0,z-.02,rx+.006,ry+.006),(0,0,z+.02,rx+.008,ry+.007)],'Reflect','Chest',16)
        skin_transition(band,'Spine','Chest',1.25,.26)
    label('Vest lettering','ДПС',(0,.133,1.315),.075,'Reflect','Chest')
    # Peaked cap: crown, red band, black visor and badge.
    ellipsoid('Cap crown',(0,.004,1.772),(.135,.14,.042),'Outer','Head',24,10)
    loft('Cap band',[(0,-.004,1.70,.106,.098),(0,-.002,1.752,.112,.104)],'CapBand','Head',20)
    ellipsoid('Cap visor',(0,-.098,1.708),(.085,.055,.012),'Dark','Head',16,8)
    ellipsoid('Cap badge',(0,-.106,1.733),(.013,.006,.016),'Badge','Head',10,6)
    # Striped traffic baton held in the right hand.
    a, b = Vector((-.322,-.045,.905)), Vector((-.334,-.215,.515))
    for i in range(8):
        cylinder('Baton',a.lerp(b,i/8),a.lerp(b,(i+1)/8),.016,'Dark' if i%2==0 else 'Reflect','Hand_R')
    for s in (-1, 1):
        box('Epaulette',(s*.16,0,1.447),(.10,.05,.012),'Accent','Chest',.004)

def create_character(spec):
    global parts
    parts = []
    bpy.ops.object.select_all(action='DESELECT')
    name = spec['name']
    female = spec.get('female', False); child = spec.get('child', False); police = spec.get('police', False)
    material('Skin',spec['skin']); material('Hair',spec['hair'])
    material('Outer',spec['outer']); material('Trouser',spec['trouser'])
    material('Cream',(.78,.77,.67)); material('Dark',(.022,.027,.032))
    material('Accent',spec['accent'])
    if police:
        material('Vest',(.55,.78,.04),.6); material('Reflect',(.70,.72,.72),.35)
        material('CapBand',(.55,.03,.03)); material('Badge',(.80,.58,.12),.35)
    rig = rig_create(child); rig.name = name
    waist = .135 if female else .155
    body = loft('Jacket',[(0,0,.94,.17,.112),(0,0,1.00,.18,.115),
        (0,0,1.13,waist,.10),(0,0,1.31,.195,.115),(0,0,1.40,.207,.10),
        (0,0,1.445,.163,.082),(0,0,1.47,.073,.062)],'Outer','Chest',16)
    skin_transition(body,'Spine','Chest',1.25,.26)
    ellipsoid('Pelvis',(0,0,.929),(.168,.108,.124),'Trouser','Hips',20,12)
    loft('Neck',[(0,0,1.44,.052,.051),(0,0,1.58,.052,.05)],'Skin','Neck')
    ellipsoid('Face',(0,-.009,1.645),(.102,.087,.137),'Skin','Head',20,14)
    # Hair cap follows the scalp, extends lower at the back, leaves the face open.
    vs=[]; fs=[]; segments=24; levels=7
    for j in range(levels):
        for i in range(segments):
            a=i*TAU/segments
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
    if not female and not police:
        ellipsoid('Swept fringe',(-.032,-.061,1.746),(.073,.035,.037),'Hair','Head')
    if female and child:
        for s in [-1,1]:
            ellipsoid('Pigtail',(s*.118,.035,1.60),(.034,.036,.085),'Hair','Head',12,8)
            ellipsoid('Hair tie',(s*.112,.03,1.675),(.022,.022,.016),'Accent','Head',10,6)
    elif female:
        ellipsoid('Hair bun',(0,.085,1.71),(.068,.061,.063),'Hair','Head')
    for s in [-1,1]:
        ellipsoid('Ear',(s*.10,.0,1.64),(.019,.019,.031),'Skin','Head',12,8)
        ellipsoid('Eye',(s*.039,-.089,1.664),(.009,.005,.006),'Dark','Head',12,8)
        box('Brow',(s*.038,-.088,1.681),(.032,.006,.006),'Hair','Head',.002)
    ellipsoid('Nose',(0,-.095,1.638),(.014,.021,.024),'Skin','Head',12,8)
    box('Mouth',(0,-.091,1.604),(.028,.004,.004),'Hair','Head',.001)
    # Clothing details belong to the same skeleton and are joined for export.
    if not police:
        box('Zip',(0,-.114,1.245),(.012,.009,.38),'Cream','Chest',.003)
        for s in [-1,1]:
            box('Pocket welt',(s*.108,-.101,1.085),(.07,.014,.015),'Accent','Spine',.005)
    loft('Collar',[(0,0,1.43,.076,.072),(0,0,1.49,.069,.066)],'Accent','Chest')
    sole = 'Dark' if police else 'Cream'
    for side,s in [('L',1),('R',-1)]:
        leg=loft('Trousers_'+side,[(s*.105,0,.13,.057,.055),
            (s*.105,0,.23,.063,.060),(s*.105,0,.43,.070,.067),
            (s*.105,0,.53,.073,.073),(s*.105,0,.62,.079,.078),
            (s*.105,0,.80,.089,.09),(s*.103,0,.94,.09,.094)],'Trouser','UpperLeg_'+side)
        skin_transition(leg,'LowerLeg_'+side,'UpperLeg_'+side,.53,.16)
        box('Shoe sole_'+side,(s*.105,-.055,.032),(.13,.258,.058),sole,'Foot_'+side,.022)
        ellipsoid('Sneaker_'+side,(s*.105,-.049,.092),(.065,.124,.064),'Dark','Foot_'+side)
        if not police:
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
    if spec.get('backpack'):
        box('Backpack',(0,.167,1.258),(.272,.16,.32),'Accent','Chest',.055)
        box('Pack pocket',(0,.255,1.21),(.22,.055,.15),'Outer' if not child else 'Cream','Chest',.025)
        for s in [-1,1]:
            box('Strap',(s*.126,-.107,1.32),(.028,.025,.26),'Accent','Chest',.011)
    if police:
        police_kit()
    bpy.ops.object.select_all(action='DESELECT')
    for o in parts: o.select_set(True)
    bpy.context.view_layer.objects.active=parts[0]; bpy.ops.object.join()
    body=bpy.context.object; body.name=name+'_Body'; parts=[body]
    if child:
        head = body.vertex_groups['Head'].index
        for v in body.data.vertices:
            v.co = child_warp(v.co.copy(), any(g.group == head and g.weight > .5 for g in v.groups))
    # Consistent outward normals, including the scalp cap.
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.normals_make_consistent(inside=False); bpy.ops.object.mode_set(mode='OBJECT')
    body.parent=rig
    mod=body.modifiers.new('Pedestrian skin','ARMATURE'); mod.object=rig
    clips=animate(rig, spec)
    return rig,body,clips

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
    material('Stage',(.12,.155,.17))
    box('Review floor',(0,0,-.052),(200,200,.1),'Stage',None,0)
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
    M.clear()
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
        if spec.get('child'): camera.ortho_scale=2.2
        target=(0,0,.65) if spec.get('child') else (0,0,.95)
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
    camera=stage(); camera.ortho_scale=5.2
    render(WEB/'lineup.png',(2,-8,2.8),target=(0,0,.93))
    report={'revision':2,'utc':datetime.now(timezone.utc).isoformat(),'blender':bpy.app.version_string,
        'units':'metres','sourceAxes':'Z up, -Y forward','fps':FPS,'assets':records,
        'scope':'Authored meshes, generic skeleton and in-place looping clips. No pedestrian navigation, LODs or traffic AI.'}
    (ROOT/'artifacts/reports/pedestrians-manifest.json').write_text(json.dumps(report,indent=2,ensure_ascii=False),encoding='utf-8')
    print('PEDESTRIANS_GENERATED',flush=True)

if __name__=='__main__': main()
