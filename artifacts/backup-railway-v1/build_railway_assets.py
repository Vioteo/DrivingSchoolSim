"""Original Railway Crossing kit and ГОСТ Railway Traffic Signs.
Blender 5: -b --python tools/build_railway_assets.py.
Metres, Z up, front -Y. Exports FBX, GLB, and high-resolution review renders.
"""
import bpy, math, json, hashlib
from pathlib import Path
from datetime import datetime, timezone
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
OUT_TRAFFIC = ROOT / 'Assets/DrivingSchool/Art/Traffic'
OUT_KIT = ROOT / 'Assets/DrivingSchool/Art/TrainingKit'
REVIEW = ROOT / 'artifacts/visual-review/railway'
DESKTOP = Path('C:/Users/AVSok/Desktop/Unity_Screenshots')

for p in [OUT_TRAFFIC, OUT_KIT, REVIEW, DESKTOP, ROOT/'ArtSource']:
    p.mkdir(parents=True, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.unit_settings.system = 'METRIC'

M = {}
def mat(name, color, metal=0, rough=.4, emission=0):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    bs = m.node_tree.nodes.get('Principled BSDF')
    bs.inputs['Base Color'].default_value = (*color, 1)
    bs.inputs['Metallic'].default_value = metal
    bs.inputs['Roughness'].default_value = rough
    bs.inputs['Emission Color'].default_value = (*color, 1)
    bs.inputs['Emission Strength'].default_value = emission
    M[name] = m
    return m

# Color palette
mat('White', (.93, .95, .93), 0, .35)
mat('Red', (.82, .02, .03), 0, .35)
mat('RedEmissive', (1.0, .03, .01), 0, .2, 4.0)
mat('WhiteEmissive', (.95, .95, 1.0), 0, .2, 3.5)
mat('Steel', (.42, .48, .52), .8, .28)
mat('DarkSteel', (.18, .20, .24), .85, .32)
mat('RailSteel', (.65, .68, .70), .9, .22)
mat('Wood', (.28, .18, .12), 0, .85)
mat('Concrete', (.55, .56, .54), 0, .75)
mat('Gravel', (.38, .36, .34), 0, .92)
mat('RubberDeck', (.12, .13, .14), 0, .68)
mat('Housing', (.03, .04, .05), .2, .35)
mat('Gasket', (.01, .01, .01), 0, .9)
mat('Yellow', (1.0, .75, .02), 0, .35)
mat('Ink', (.01, .01, .02), 0, .4)

current_root = None
roots = []

def root(name, kind='model'):
    global current_root
    current_root = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(current_root)
    current_root['catalogId'] = name
    current_root['kind'] = kind
    # Forward and Up sockets
    for n, p in [('Axis_Forward', (0, 1, 0)), ('Axis_Up', (0, 0, 1))]:
        s = bpy.data.objects.new(n, None)
        bpy.context.collection.objects.link(s)
        s.parent = current_root
        s.location = p
    roots.append(current_root)
    return current_root

def finish(o, name, material):
    o.name = name
    o.parent = current_root
    if material:
        o.data.materials.append(M[material])
    return o

def box(name, loc, dim, material, bevel=0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    o = finish(bpy.context.object, name, material)
    o.scale = dim
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        b = o.modifiers.new('Bevel', 'BEVEL')
        b.width = bevel
        b.segments = 2
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.modifier_apply(modifier=b.name)
    return o

def cyl(name, loc, r, depth, material, axis='Z', n=32):
    bpy.ops.mesh.primitive_cylinder_add(vertices=n, radius=r, depth=depth, location=loc)
    o = finish(bpy.context.object, name, material)
    if axis == 'Y': o.rotation_euler.x = math.pi / 2
    elif axis == 'X': o.rotation_euler.y = math.pi / 2
    for p in o.data.polygons: p.use_smooth = len(p.vertices) == 4
    return o

def polygon(name, coords, y, depth, material):
    import bmesh
    n = len(coords)
    verts = [(x, y + dy, z) for dy in (0, depth) for x, z in coords]
    faces = [tuple(range(n)), tuple(reversed(range(n, 2 * n)))] + [(i, (i + 1) % n, (i + 1) % n + n, i + n) for i in range(n)]
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.update()
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(o)
    finish(o, name, material)
    return o

def post(height, r=0.038):
    cyl('Post', (0, 0.055, height / 2), r, height, 'Steel')
    box('BasePlate', (0, 0.055, 0.017), (0.24, 0.24, 0.034), 'Steel', 0.012)
    for x in [-0.083, 0.083]:
        for y in [-0.028, 0.138]:
            cyl('AnchorBolt', (x, y, 0.045), 0.012, 0.023, 'Steel', n=6)
    cyl('PostCap', (0, 0.055, height + 0.008), r + 0.003, 0.016, 'Steel')

# ==============================================================================
# 1. 3D MODELS: RAILWAY CROSSING FACILITY
# ==============================================================================

# 1.1 Rail Track Module (TK_RailwayCrossing_Tracks)
r = root('TK_RailwayCrossing_Tracks', 'facility')
# Ballast bed
box('DriveSurface_Ballast', (0, 0, 0.08), (7.4, 4.8, 0.16), 'Gravel')
# Ties (Шпалы) - Russian 1520mm gauge standard (length 2.7m)
for y in [-1.8, -1.2, -0.6, 0.0, 0.6, 1.2, 1.8]:
    box('Tie', (0, y, 0.16), (3.0, 0.26, 0.18), 'Wood')
    # Steel tie plates (подкладки КБ)
    for rx in [-0.76, 0.76]:
        box('TiePlate', (rx, y, 0.26), (0.22, 0.32, 0.025), 'DarkSteel')

# Dual Steel Rails (Standard P65 profile approx)
for rx in [-0.76, 0.76]:
    # Rail base
    box('RailBase', (rx, 0, 0.28), (0.15, 4.8, 0.02), 'RailSteel')
    # Rail web
    box('RailWeb', (rx, 0, 0.35), (0.022, 4.8, 0.12), 'RailSteel')
    # Rail head
    box('RailHead', (rx, 0, 0.42), (0.075, 4.8, 0.045), 'RailSteel')

# Rubber-cord level crossing decking (Настил переезда) flush with rail head
# Inner deck (between rails with wheel flange grooves)
box('DriveSurface_DeckCenter', (0, 0, 0.40), (1.36, 4.8, 0.04), 'RubberDeck')
# Outer approach ramp decks
box('DriveSurface_DeckLeft', (-1.45, 0, 0.39), (1.2, 4.8, 0.04), 'RubberDeck')
box('DriveSurface_DeckRight', (1.45, 0, 0.39), (1.2, 4.8, 0.04), 'RubberDeck')
# Approach transition edge strips
box('ApproachStripL', (-2.1, 0, 0.36), (0.15, 4.8, 0.03), 'Concrete')
box('ApproachStripR', (2.1, 0, 0.36), (0.15, 4.8, 0.03), 'Concrete')

# 1.2 Automatic Railway Barrier (TK_RailwayBarrier)
r = root('TK_RailwayBarrier', 'barrier')
# Heavy pedestal cabinet
box('Cabinet', (0, 0, 0.55), (0.46, 0.46, 1.1), 'DarkSteel', 0.02)
box('CabinetDoor', (0, -0.235, 0.55), (0.38, 0.015, 0.85), 'Steel')
box('BaseFlange', (0, 0, 0.03), (0.58, 0.58, 0.06), 'DarkSteel')
# Pivot assembly
cyl('PivotShaft', (0, 0.28, 0.92), 0.06, 0.18, 'Steel', 'Y')
box('PivotFork', (0, 0.28, 0.92), (0.16, 0.32, 0.24), 'Steel')
# Counterweight box
box('Counterweight', (-0.65, 0.28, 0.92), (0.55, 0.28, 0.32), 'DarkSteel')
# Barrier boom (5.2m total length, red/white diagonal stripes)
boom_len = 5.2
boom_x = boom_len / 2 + 0.1
# Aluminum boom spar
box('BarrierBoom', (boom_x, 0.28, 0.92), (boom_len, 0.06, 0.14), 'White')
# Retroreflective red stripes
num_stripes = 7
for k in range(num_stripes):
    bx = 0.4 + k * 0.72
    box(f'RedStripe_{k}', (bx, 0.28, 0.92), (0.36, 0.065, 0.142), 'Red')
# Boom warning LED lights
for bx in [1.5, 3.2, 5.0]:
    cyl('BoomLED', (bx, 0.28, 1.02), 0.035, 0.05, 'RedEmissive', 'Z')

# 1.3 Russian Railway Crossing Signal (TK_RailwaySignal)
r = root('TK_RailwaySignal', 'signal')
# Sturdy vertical mast
cyl('SignalMast', (0, 0, 1.6), 0.05, 3.2, 'Steel')
box('SignalBase', (0, 0, 0.04), (0.32, 0.32, 0.08), 'DarkSteel')
# Sound alarm gong/bell on top
cyl('AlarmGong', (0, 0, 3.28), 0.16, 0.12, 'DarkSteel')
# Black background contrast shield (фоновый щит)
box('ContrastShield', (0, -0.06, 2.5), (1.15, 0.015, 0.65), 'Housing', 0.02)
# Two horizontal red flashing lamps
for lx, name in [(-0.35, 'Left'), (0.35, 'Right')]:
    box(f'Head_{name}', (lx, -0.12, 2.5), (0.32, 0.15, 0.32), 'Housing')
    cyl(f'Visor_{name}', (lx, -0.26, 2.5), 0.14, 0.22, 'Housing', 'Y')
    cyl(f'RedLens_{name}', (lx, -0.19, 2.5), 0.11, 0.02, 'RedEmissive', 'Y')
# Upper lunar-white lamp
box('Head_White', (0, -0.12, 2.85), (0.26, 0.15, 0.26), 'Housing')
cyl('Visor_White', (0, -0.24, 2.85), 0.12, 0.20, 'Housing', 'Y')
cyl('WhiteLens', (0, -0.18, 2.85), 0.09, 0.02, 'WhiteEmissive', 'Y')

# ==============================================================================
# 2. COMPLETE SET OF RUSSIAN RAILWAY SIGNS (ГОСТ Р 52290-2004)
# ==============================================================================

def make_sign_base(name, label):
    root('DS_Sign_' + name, label)
    post(2.75)
    return current_root

# 2.1 Знак 1.1: Железнодорожный переезд со шлагбаумом
r = make_sign_base('RailwayBarrier', 'Знак 1.1: Железнодорожный переезд со шлагбаумом')
z = 2.45
h_tri = 0.72
w_tri = 0.82
# Triangle pointing UP
outline_tri = [(-w_tri/2, z - h_tri*0.35), (w_tri/2, z - h_tri*0.35), (0, z + h_tri*0.65)]
polygon('Plate_Back', outline_tri, -0.065, 0.025, 'Steel')
polygon('Face_Border', outline_tri, -0.081, 0.008, 'Red')
inner_tri = [(-w_tri*0.36, z - h_tri*0.25), (w_tri*0.36, z - h_tri*0.25), (0, z + h_tri*0.48)]
polygon('Face_Field', inner_tri, -0.091, 0.004, 'White')
# Fence / Barrier picket pictogram (заборчик)
box('Fence_Rail1', (0, -0.098, z - 0.02), (0.34, 0.003, 0.025), 'Ink')
box('Fence_Rail2', (0, -0.098, z + 0.06), (0.34, 0.003, 0.025), 'Ink')
for px in [-0.14, -0.07, 0.0, 0.07, 0.14]:
    box(f'Picket_{px}', (px, -0.10, z + 0.02), (0.024, 0.004, 0.18), 'Ink')

# 2.2 Знак 1.2: Железнодорожный переезд без шлагбаума
r = make_sign_base('RailwayNoBarrier', 'Знак 1.2: Железнодорожный переезд без шлагбаума')
polygon('Plate_Back', outline_tri, -0.065, 0.025, 'Steel')
polygon('Face_Border', outline_tri, -0.081, 0.008, 'Red')
polygon('Face_Field', inner_tri, -0.091, 0.004, 'White')
# Steam locomotive pictogram (паровоз)
box('Loco_Boiler', (-0.02, -0.098, z - 0.01), (0.26, 0.004, 0.10), 'Ink')
box('Loco_Cab', (0.12, -0.098, z + 0.04), (0.11, 0.004, 0.18), 'Ink')
box('Loco_Chimney', (-0.12, -0.098, z + 0.08), (0.04, 0.004, 0.08), 'Ink')
box('Loco_Dome', (-0.02, -0.098, z + 0.06), (0.045, 0.004, 0.05), 'Ink')
cyl('Wheel_Front', (-0.10, -0.098, z - 0.09), 0.045, 0.004, 'Ink', 'Y', 16)
cyl('Wheel_Mid', (0.0, -0.098, z - 0.09), 0.045, 0.004, 'Ink', 'Y', 16)
cyl('Wheel_Rear', (0.10, -0.098, z - 0.09), 0.045, 0.004, 'Ink', 'Y', 16)

# 2.3 Знак 1.3.1: Однопутная железная дорога (Андреевский крест)
r = make_sign_base('SingleTrack', 'Знак 1.3.1: Однопутная железная дорога')
cross_len = 1.15
cross_w = 0.18
for angle, name in [(-30, 'CrossA'), (30, 'CrossB')]:
    a = math.radians(angle)
    ob = box(f'{name}_Back', (0, -0.065, z), (cross_len, 0.025, cross_w), 'Steel')
    ob.rotation_euler.y = a
    ob = box(f'{name}_White', (0, -0.082, z), (cross_len - 0.02, 0.008, cross_w - 0.015), 'White')
    ob.rotation_euler.y = a
    ob = box(f'{name}_Red1', (0, -0.088, z + cross_w*0.42*math.cos(a)), (cross_len - 0.04, 0.006, 0.025), 'Red')
    ob.rotation_euler.y = a
    ob = box(f'{name}_Red2', (0, -0.088, z - cross_w*0.42*math.cos(a)), (cross_len - 0.04, 0.006, 0.025), 'Red')
    ob.rotation_euler.y = a

# 2.4 Знак 1.3.2: Многопутная железная дорога
r = make_sign_base('MultiTrack', 'Знак 1.3.2: Многопутная железная дорога')
z_cross = z + 0.15
for angle, name in [(-30, 'CrossA'), (30, 'CrossB')]:
    a = math.radians(angle)
    ob = box(f'{name}_Back', (0, -0.065, z_cross), (cross_len, 0.025, cross_w), 'Steel')
    ob.rotation_euler.y = a
    ob = box(f'{name}_White', (0, -0.082, z_cross), (cross_len - 0.02, 0.008, cross_w - 0.015), 'White')
    ob.rotation_euler.y = a
    ob = box(f'{name}_Red1', (0, -0.088, z_cross + cross_w*0.42*math.cos(a)), (cross_len - 0.04, 0.006, 0.025), 'Red')
    ob.rotation_euler.y = a
    ob = box(f'{name}_Red2', (0, -0.088, z_cross - cross_w*0.42*math.cos(a)), (cross_len - 0.04, 0.006, 0.025), 'Red')
    ob.rotation_euler.y = a
z_chev = z - 0.25
chev_len = 0.55
for angle, name, cx in [(-30, 'ChevL', -0.24), (30, 'ChevR', 0.24)]:
    a = math.radians(angle)
    ob = box(f'{name}_Back', (cx, -0.065, z_chev), (chev_len, 0.025, cross_w), 'Steel')
    ob.rotation_euler.y = a
    ob = box(f'{name}_White', (cx, -0.082, z_chev), (chev_len - 0.02, 0.008, cross_w - 0.015), 'White')
    ob.rotation_euler.y = a
    ob = box(f'{name}_Red', (cx, -0.088, z_chev), (chev_len - 0.04, 0.006, 0.025), 'Red')
    ob.rotation_euler.y = a

# 2.5 Знаки 1.4.1–1.4.6: Приближение к Ж/Д переезду
def make_dist_marker(name, label, stripes, right_side):
    r = make_sign_base(name, label)
    w_plaque = 0.35
    h_plaque = 0.95
    box('Plate_Back', (0, -0.065, z), (w_plaque, 0.025, h_plaque), 'Steel', 0.01)
    box('Plate_Face', (0, -0.082, z), (w_plaque - 0.015, 0.008, h_plaque - 0.015), 'White')
    angle = -35 if right_side else 35
    a = math.radians(angle)
    stripe_w = 0.055
    stripe_l = 0.38
    if stripes == 3:
        z_offsets = [-0.26, 0.0, 0.26]
    elif stripes == 2:
        z_offsets = [-0.18, 0.18]
    else:
        z_offsets = [0.0]
    for idx, sz in enumerate(z_offsets):
        ob = box(f'Stripe_{idx}', (0, -0.091, z + sz), (stripe_l, 0.004, stripe_w), 'Red')
        ob.rotation_euler.y = a

make_dist_marker('RailwayDist3_R', 'Знак 1.4.1: Приближение к Ж/Д переезду (3 полосы, право)', 3, True)
make_dist_marker('RailwayDist2_R', 'Знак 1.4.2: Приближение к Ж/Д переезду (2 полосы, право)', 2, True)
make_dist_marker('RailwayDist1_R', 'Знак 1.4.3: Приближение к Ж/Д переезду (1 полоса, право)', 1, True)
make_dist_marker('RailwayDist3_L', 'Знак 1.4.4: Приближение к Ж/Д переезду (3 полосы, лево)', 3, False)
make_dist_marker('RailwayDist2_L', 'Знак 1.4.5: Приближение к Ж/Д переезду (2 полосы, лево)', 2, False)
make_dist_marker('RailwayDist1_L', 'Знак 1.4.6: Приближение к Ж/Д переезду (1 полоса, лево)', 1, False)

# ==============================================================================
# 3. EXPORT ASSETS (FBX & GLB)
# ==============================================================================
print(f'Total roots created: {len(roots)}')
for o in roots:
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    for child in o.children_recursive: child.select_set(True)
    bpy.context.view_layer.objects.active = o
    target_dir = OUT_KIT if o.get('kind') in ('facility', 'barrier') else OUT_TRAFFIC
    fbx = target_dir / (o.name + '.fbx')
    glb = REVIEW / (o.name + '.glb')
    bpy.ops.export_scene.fbx(filepath=str(fbx), use_selection=True, object_types={'MESH', 'EMPTY'},
                             axis_forward='-Z', axis_up='Y', apply_unit_scale=True, add_leaf_bones=False, bake_anim=False)
    bpy.ops.export_scene.gltf(filepath=str(glb), export_format='GLB', use_selection=True, export_apply=True)
    print(f'Exported: {o.name} -> {fbx.name}')

# ==============================================================================
# 4. PHOTOREALISTIC PRESENTATION RENDERS
# ==============================================================================
scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.samples = 32
if not scene.world:
    scene.world = bpy.data.worlds.new('World')
scene.world.color = (0.25, 0.28, 0.32)
if scene.world.use_nodes:
    bg = scene.world.node_tree.nodes.get('Background')
    if bg:
        bg.inputs['Color'].default_value = (0.25, 0.28, 0.32, 1)

# Ground plinth
bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, -0.05))
g = bpy.context.object
g.name = 'ShowcaseGround'
g.scale = (60, 40, 0.1)
bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
g.data.materials.append(M['RubberDeck'])

# Studio Lights
def studio_light(name, loc, target, energy, size):
    data = bpy.data.lights.new(name, 'AREA')
    data.energy = energy
    data.size = size
    data.color = (1.0, 0.98, 0.94)
    o = bpy.data.objects.new(name, data)
    scene.collection.objects.link(o)
    o.location = loc
    o.rotation_euler = (Vector(target) - o.location).to_track_quat('-Z', 'Y').to_euler()
    return o

studio_light('KeyLight', (-8, -12, 12), (0, 0, 1.5), 3500, 8.0)
studio_light('FillLight', (10, -8, 8), (0, 0, 1.5), 1800, 6.0)
studio_light('RimLight', (0, 12, 10), (0, 0, 1.5), 2800, 7.0)

# Camera
cam_data = bpy.data.cameras.new('ShowcaseCamera')
cam = bpy.data.objects.new('ShowcaseCamera', cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
scene.render.image_settings.file_format = 'PNG'
scene.view_settings.view_transform = 'AgX'

def render_image(name, cam_pos, target_pos, fov=45):
    cam.location = cam_pos
    cam.rotation_euler = (Vector(target_pos) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    cam_data.type = 'PERSP'
    cam_data.angle = math.radians(fov)
    scene.render.resolution_x = 1920
    scene.render.resolution_y = 1080
    scene.render.resolution_percentage = 100
    
    out_rev = REVIEW / (name + '.png')
    out_desk = DESKTOP / (name + '.png')
    scene.render.filepath = str(out_rev)
    bpy.ops.render.render(write_still=True)
    out_desk.write_bytes(out_rev.read_bytes())
    print(f'Rendered: {name}.png')

# 4.1 Arrangement for Railway Crossing Facility (Track, Barrier, Signal)
def set_hierarchy_hidden(obj, hidden):
    obj.hide_render = hidden
    for child in obj.children_recursive:
        child.hide_render = hidden

for o in roots:
    set_hierarchy_hidden(o, True)

tk_track = bpy.data.objects.get('TK_RailwayCrossing_Tracks')
tk_barrier = bpy.data.objects.get('TK_RailwayBarrier')
tk_signal = bpy.data.objects.get('TK_RailwaySignal')

set_hierarchy_hidden(tk_track, False)
tk_track.location = (0, 0, 0)
set_hierarchy_hidden(tk_barrier, False)
tk_barrier.location = (-1.8, -2.8, 0)
tk_barrier.rotation_euler = (0, 0, 0)
set_hierarchy_hidden(tk_signal, False)
tk_signal.location = (-2.6, -3.2, 0)
tk_signal.rotation_euler = (0, 0, 0)

# Render crossing facility overview
render_image('01_railway_crossing_facility', (0.0, -8.5, 4.5), (0.0, 0.0, 1.2), 48)
# Close up of signal and barrier mechanism
render_image('02_railway_signal_barrier_closeup', (-2.2, -6.2, 2.8), (-2.2, -3.0, 1.8), 40)

# 4.2 Complete Set of Warning & Cross Signs (1.1, 1.2, 1.3.1, 1.3.2)
for o in roots:
    set_hierarchy_hidden(o, True)

row1 = ['DS_Sign_RailwayBarrier', 'DS_Sign_RailwayNoBarrier', 'DS_Sign_SingleTrack', 'DS_Sign_MultiTrack']
for idx, sname in enumerate(row1):
    so = bpy.data.objects.get(sname)
    if so:
        set_hierarchy_hidden(so, False)
        so.location = ((idx - 1.5) * 1.6, 0, 0)

render_image('03_railway_warning_and_cross_signs', (0.0, -7.5, 2.6), (0.0, 0.0, 2.2), 42)

# 4.3 Complete Set of Distance Plaques (1.4.1–1.4.6)
for o in roots:
    set_hierarchy_hidden(o, True)

row2 = ['DS_Sign_RailwayDist3_R', 'DS_Sign_RailwayDist2_R', 'DS_Sign_RailwayDist1_R',
        'DS_Sign_RailwayDist3_L', 'DS_Sign_RailwayDist2_L', 'DS_Sign_RailwayDist1_L']
for idx, sname in enumerate(row2):
    so = bpy.data.objects.get(sname)
    if so:
        set_hierarchy_hidden(so, False)
        so.location = ((idx - 2.5) * 1.15, 0, 0)

render_image('04_railway_distance_plaques', (0.0, -7.0, 2.2), (0.0, 0.0, 2.0), 45)

# 4.4 Comprehensive Grand Showcase of all Railway Crossing Models and Signs Together
for o in roots:
    set_hierarchy_hidden(o, False)

# Position track, barrier, signal in center
tk_track.location = (0, 1.5, 0)
tk_barrier.location = (-1.8, -1.2, 0)
tk_signal.location = (-2.6, -1.6, 0)

# Position signs neatly along the approach roadway
for idx, sname in enumerate(row1):
    so = bpy.data.objects.get(sname)
    if so: so.location = (-4.8 + idx * 1.5, -4.5, 0)

for idx, sname in enumerate(row2):
    so = bpy.data.objects.get(sname)
    if so: so.location = ((idx - 2.5) * 1.3, -7.0, 0)

render_image('05_railway_full_kit_showcase', (0.0, -16.0, 8.5), (0.0, -2.5, 1.5), 45)

# Save blend file
blend_path = ROOT / 'ArtSource/DS_RailwayKit.blend'
bpy.ops.wm.save_as_mainfile(filepath=str(blend_path))
print('RAILWAY_ASSETS_BUILD_PASS', flush=True)
