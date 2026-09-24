"""Railway crossing kit v2 (level crossing, automatic barrier, crossing signal, signs 1.1-1.4).

Blender 5:  blender -b --python tools/build_railway_assets.py            (models + review renders)
            blender -b --python tools/build_railway_assets.py -- --no-render   (models only)

Units: metres, Z up, front -Y (the side a driver sees), X to the driver's right.

Naming contract with the Unity builders:
  TrainingKit (TK_*):  Axis_Forward (0,1,0) / Axis_Up (0,0,1) sockets.
    DriveSurface_*  -> MeshCollider on the drivable layer (9).
    Detail_*        -> no collider (rails, sleepers, ballast, seams, fastenings).
    everything else -> BoxCollider on the solid-prop layer (10).
    Boom_Pivot / Boom_Tip empties: the boom rotates about the axis perpendicular to
      (Boom_Tip - Boom_Pivot) and world up; +angle lifts the tip.
    Lamp_* are emissive discs in front of the dark Lens_* discs, for runtime switching.
  Traffic (DS_Sign_*): Socket_Front (0,-1,0) / Socket_Up (0,0,1), Post, Plate_Back
    and Traffic_* materials, as in tools/build_traffic_assets.py.
  RW_Materials.json next to the kit FBX lists the colours of every RW_* material, so
  Unity no longer depends on the colour stored inside the FBX.
"""
import bpy, bmesh, math, json, hashlib, sys, os
from pathlib import Path
from datetime import datetime, timezone
from mathutils import Vector

ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
RENDER = '--no-render' not in ARGS

ROOT = Path(__file__).resolve().parents[1]
OUT_TRAFFIC = ROOT / 'Assets/DrivingSchool/Art/Traffic'
OUT_KIT = ROOT / 'Assets/DrivingSchool/Art/TrainingKit'
REVIEW = ROOT / 'artifacts/visual-review/railway'
REPORTS = ROOT / 'artifacts/reports'
DESKTOP = Path('C:/Users/AVSok/Desktop/Unity_Screenshots') if os.name == 'nt' else None

for p in [OUT_TRAFFIC, OUT_KIT, REVIEW, REPORTS, ROOT / 'ArtSource']:
    p.mkdir(parents=True, exist_ok=True)

# v1 exported the crossing signal into Traffic/ as well. That copy has no Socket_Front,
# which makes TrafficAssetBuilder fail, and the kit copy is the one the scene uses.
for stale in [OUT_TRAFFIC / 'TK_RailwaySignal.fbx', OUT_TRAFFIC / 'TK_RailwaySignal.fbx.meta']:
    try:
        stale.unlink()
    except OSError:
        pass

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.unit_settings.system = 'METRIC'

# ==============================================================================
# Materials
# ==============================================================================
M = {}
SPECS = []          # RW_* materials for Unity: linear RGB, metallic, smoothness, emission


def mat(name, color, metal=0.0, rough=0.5, emission=0.0, export_spec=True):
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
    if export_spec:
        SPECS.append(dict(name=name, color=[round(c, 4) for c in color], metallic=metal,
                          smoothness=round(1 - rough, 3), emission=emission))
    return m


# Track and deck
mat('RW_Ballast', (.20, .185, .17), 0, .95)
mat('RW_Sleeper', (.33, .33, .31), 0, .85)            # reinforced-concrete sleepers
mat('RW_RailSteel', (.16, .11, .08), .55, .6)         # weathered web and foot
mat('RW_RailHead', (.60, .61, .62), .95, .22)         # polished running surface
mat('RW_Fastening', (.035, .035, .04), .6, .5)
mat('RW_Deck', (.028, .030, .033), 0, .82)            # rubber-cord panels
mat('RW_DeckSeam', (.006, .006, .007), 0, .9)
mat('RW_Asphalt', (.033, .038, .043), 0, .9)          # approach ramps, matches the practice surface
mat('RW_Concrete', (.42, .42, .40), 0, .8)
# Barrier and signal
mat('RW_Cabinet', (.46, .48, .49), .35, .42)
mat('RW_Steel', (.40, .44, .47), .8, .35)
mat('RW_Red', (.70, .02, .02), 0, .38)
mat('RW_White', (.86, .88, .86), 0, .38)
mat('RW_Black', (.010, .011, .013), .1, .45)
mat('RW_Housing', (.018, .020, .024), .2, .35)
mat('RW_Gasket', (.006, .006, .008), 0, .9)
mat('RW_LensRed', (.12, .004, .004), .1, .15)
mat('RW_LensWhite', (.20, .21, .23), .1, .15)
mat('RW_RedOn', (1.0, .03, .01), 0, .2, 3.0)
mat('RW_WhiteOn', (.92, .94, 1.0), 0, .2, 3.0)
# Signs reuse the materials of the existing traffic kit (Materials/Traffic/Traffic_*.mat).
mat('Traffic_White', (.92, .95, .92), export_spec=False)
mat('Traffic_Red', (.72, .014, .025), export_spec=False)
mat('Traffic_Ink', (.008, .012, .018), export_spec=False)
mat('Traffic_Steel', (.39, .46, .5), .78, .32, export_spec=False)

# ==============================================================================
# Geometry helpers
# ==============================================================================
current_root = None
roots = []


def link(o):
    bpy.context.collection.objects.link(o)
    return o


def parent_keep(o, p):
    o.parent = p
    o.matrix_parent_inverse = p.matrix_world.inverted()


def finish(o, name, material, parent=None):
    o.name = name
    o.data.name = name
    parent_keep(o, parent or current_root)
    if material:
        mats = material if isinstance(material, (list, tuple)) else [material]
        for mm in mats:
            o.data.materials.append(M[mm])
    return o


def empty(name, loc, parent=None):
    e = link(bpy.data.objects.new(name, None))
    e.location = loc
    bpy.context.view_layer.update()
    if parent is not None or current_root is not None:
        parent_keep(e, parent or current_root)
    return e


def root(name, kind, sockets='kit', label=''):
    global current_root
    current_root = link(bpy.data.objects.new(name, None))
    current_root['catalogId'] = name
    current_root['kind'] = kind
    current_root['label'] = label
    if sockets == 'kit':
        pairs = [('Axis_Forward', (0, 1, 0)), ('Axis_Up', (0, 0, 1))]
    else:
        pairs = [('Socket_Front', (0, -1, 0)), ('Socket_Up', (0, 0, 1))]
    for n, p in pairs:
        s = link(bpy.data.objects.new(n, None))
        s.parent = current_root
        s.location = p
    roots.append(current_root)
    return current_root


def box(name, loc, dim, material, bevel=0.0, parent=None):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    o = bpy.context.object
    o.scale = dim
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        b = o.modifiers.new('Bevel', 'BEVEL')
        b.width = bevel
        b.segments = 2
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.modifier_apply(modifier=b.name)
    return finish(o, name, material, parent)


def cyl(name, loc, r, depth, material, axis='Z', n=32, parent=None):
    bpy.ops.mesh.primitive_cylinder_add(vertices=n, radius=r, depth=depth, location=loc)
    o = bpy.context.object
    if axis == 'Y':
        o.rotation_euler.x = math.pi / 2
    elif axis == 'X':
        o.rotation_euler.y = math.pi / 2
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    for p in o.data.polygons:
        p.use_smooth = len(p.vertices) == 4
    return finish(o, name, material, parent)


def mesh_object(name, verts, faces, material, parent=None, face_mats=None, triangulate=True):
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.update()
    bm = bmesh.new()
    bm.from_mesh(me)
    if face_mats:
        bm.faces.ensure_lookup_table()
        for i, f in enumerate(bm.faces):
            f.material_index = face_mats[i]
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    if triangulate:
        big = [f for f in bm.faces if len(f.verts) > 4]
        if big:
            bmesh.ops.triangulate(bm, faces=big, quad_method='BEAUTY', ngon_method='EAR_CLIP')
    bm.to_mesh(me)
    bm.free()
    o = link(bpy.data.objects.new(name, me))
    return finish(o, name, material, parent)


def prism_y(name, profile, y0, y1, material, parent=None, side_mats=None):
    """Extrude a closed (x, z) profile along Y from y0 to y1."""
    n = len(profile)
    verts = [(x, y0, z) for x, z in profile] + [(x, y1, z) for x, z in profile]
    faces = [tuple(range(n)), tuple(range(2 * n - 1, n - 1, -1))]
    faces += [(i, (i + 1) % n, (i + 1) % n + n, i + n) for i in range(n)]
    fm = None
    if side_mats:
        fm = [0, 0] + [side_mats(i) for i in range(n)]
    return mesh_object(name, verts, faces, material, parent, fm)


def plate(name, coords, y, depth, material, parent=None):
    """Flat sign plate: closed (x, z) outline, from y to y + depth."""
    n = len(coords)
    verts = [(x, y + dy, z) for dy in (0, depth) for x, z in coords]
    faces = [tuple(range(n)), tuple(reversed(range(n, 2 * n)))]
    faces += [(i, (i + 1) % n, (i + 1) % n + n, i + n) for i in range(n)]
    return mesh_object(name, verts, faces, material, parent)


def join(objs, name):
    objs = [o for o in objs if o is not None]
    if not objs:
        return None
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1:
        bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    o.name = name
    o.data.name = name
    return o


def rect_rot(cx, cz, length, width, angle):
    """Rectangle outline (x, z) centred on (cx, cz), long axis at `angle` from horizontal."""
    ca, sa = math.cos(angle), math.sin(angle)
    pts = []
    for u, v in [(-1, -1), (1, -1), (1, 1), (-1, 1)]:
        x = u * length / 2
        z = v * width / 2
        pts.append((cx + x * ca - z * sa, cz + x * sa + z * ca))
    return pts


def clip_poly(poly, a, b, c):
    """Keep the part of a convex polygon where a*x + b*z <= c (Sutherland-Hodgman)."""
    out = []
    for i in range(len(poly)):
        p, q = poly[i], poly[(i + 1) % len(poly)]
        fp, fq = a * p[0] + b * p[1] - c, a * q[0] + b * q[1] - c
        if fp <= 0:
            out.append(p)
        if fp * fq < 0:
            t = fp / (fp - fq)
            out.append((p[0] + t * (q[0] - p[0]), p[1] + t * (q[1] - p[1])))
    return out


def shape(n, r, z, angle=0.0):
    return [(r * math.cos(angle + i * 2 * math.pi / n), z + r * math.sin(angle + i * 2 * math.pi / n)) for i in range(n)]


def scale_about(pts, cx, cz, k):
    return [(cx + (x - cx) * k, cz + (z - cz) * k) for x, z in pts]


def half_tube_visor(name, x, z, y_front, y_back, r, material, parent=None):
    """Hollow upper half-tube visor (open at the front, so the lens stays visible)."""
    n = 20
    verts = []
    for y in (y_front, y_back):
        verts.extend((x + r * math.cos(i * math.pi / n), y, z + r * math.sin(i * math.pi / n)) for i in range(n + 1))
    faces = [(i, i + 1, i + n + 2, i + n + 1) for i in range(n)]
    o = mesh_object(name, verts, faces, material, parent, triangulate=False)
    mod = o.modifiers.new('Thickness', 'SOLIDIFY')
    mod.thickness = .006
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_apply(modifier=mod.name)
    return o


# ==============================================================================
# 1. LEVEL CROSSING TRACK MODULE (TK_RailwayCrossing_Tracks)
#    Road runs along X (driver approaches from -X), the single 1520 mm track along Y.
# ==============================================================================
RAIL_C = 0.7975          # rail centre line: 1520 mm gauge + 75 mm head
RAIL_FOOT_Z = 0.060      # sleeper top 0.045 + tie plate 0.015
RAIL_H = 0.180           # P65-like height
RAIL_TOP = RAIL_FOOT_Z + RAIL_H          # 0.240
DECK_TOP = RAIL_TOP - 0.005              # rail heads stand 5 mm proud of the rubber deck
DECK_HALF_X = 1.80       # deck incl. concrete edge beams, across the rails
DECK_HALF_Y = 3.60       # along the rails: 6 m road + 0.6 m each side
RAMP_LEN = 3.00          # asphalt approach ramp on each side, ~7.3 %
RAMP_END_Z = 0.018       # just above the practice road surface (+0.015 in Unity)
TRACK_HALF = 5.50        # visible track length each side of the road centre
SLEEPER_STEP = 0.545     # 1840 sleepers/km

r = root('TK_RailwayCrossing_Tracks', 'facility', label='Одноколейный ж/д переезд с резинокордовым настилом')

# Ballast prism (only its shoulders outside the road are visible).
prism_y('Detail_Ballast', [(-2.5, -.06), (2.5, -.06), (1.85, .012), (-1.85, .012)],
        -TRACK_HALF - .25, TRACK_HALF + .25, 'RW_Ballast')

# Concrete sleepers with tie plates and rail clips.
sleepers, fastenings = [], []
k_max = int(TRACK_HALF / SLEEPER_STEP)
for k in range(-k_max, k_max + 1):
    y = k * SLEEPER_STEP
    sleepers.append(prism_y('Sleeper', [(-1.375, -.145), (1.375, -.145), (1.375, .025), (1.33, .045), (-1.33, .045), (-1.375, .025)],
                            y - .13, y + .13, 'RW_Sleeper'))
    if abs(y) < DECK_HALF_Y - .15:
        continue            # hidden under the deck
    for sx in (-1, 1):
        cx = sx * RAIL_C
        fastenings.append(box('TiePlate', (cx, y, .0525), (.36, .18, .015), 'RW_Fastening'))
        for dx in (-.095, .095):
            fastenings.append(box('Clip', (cx + dx, y, .075), (.05, .12, .03), 'RW_Fastening'))
join(sleepers, 'Detail_Sleepers')
join(fastenings, 'Detail_Fastenings')

# Rails: continuous P65-like profile; the top faces of the head use the polished material.
RAIL_PROFILE = [(-.075, 0), (.075, 0), (.075, .011), (.012, .030), (.009, .130), (.0375, .140),
                (.0375, .180), (-.0375, .180), (-.0375, .140), (-.009, .130), (-.012, .030), (-.075, .011)]
HEAD_TOP_EDGE = 6        # edge index (0.0375, .180) -> (-0.0375, .180)
for side, sx in (('L', -1), ('R', 1)):
    prof = [(sx * RAIL_C + x, RAIL_FOOT_Z + z) for x, z in RAIL_PROFILE]
    prism_y(f'Detail_Rail_{side}', prof, -TRACK_HALF, TRACK_HALF, ['RW_RailSteel', 'RW_RailHead'],
            side_mats=lambda i: 1 if i == HEAD_TOP_EDGE else 0)

# Drivable deck: one continuous rubber-cord surface (no gaps under the wheel ray-casts),
# concrete edge beams and asphalt approach ramps.
EDGE = .15
box('DriveSurface_Deck', (0, 0, DECK_TOP / 2), (2 * (DECK_HALF_X - EDGE), 2 * DECK_HALF_Y, DECK_TOP), 'RW_Deck')
for side, sx in (('W', -1), ('E', 1)):
    box(f'DriveSurface_EdgeBeam_{side}', (sx * (DECK_HALF_X - EDGE / 2), 0, DECK_TOP / 2), (EDGE, 2 * DECK_HALF_Y, DECK_TOP), 'RW_Concrete')
    x0, x1 = sx * DECK_HALF_X, sx * (DECK_HALF_X + RAMP_LEN)
    v = [(x0, -DECK_HALF_Y, 0), (x0, DECK_HALF_Y, 0), (x1, DECK_HALF_Y, 0), (x1, -DECK_HALF_Y, 0),
         (x0, -DECK_HALF_Y, DECK_TOP), (x0, DECK_HALF_Y, DECK_TOP), (x1, DECK_HALF_Y, RAMP_END_Z), (x1, -DECK_HALF_Y, RAMP_END_Z)]
    f = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (3, 7, 6, 2), (0, 4, 7, 3), (1, 2, 6, 5)]
    mesh_object(f'DriveSurface_Ramp_{side}', v, f, 'RW_Asphalt')

# Surface details (no colliders): flangeway grooves inside each rail, panel joints, bolts.
details = []
FL = .07
for sx in (-1, 1):
    x_in = sx * (RAIL_C - .0375 - FL / 2)
    details.append(box('Flangeway', (x_in, 0, DECK_TOP + .0008), (FL, 2 * DECK_HALF_Y, .0016), 'RW_DeckSeam'))
    # outer panel joint, 0.75 m outside the rail
    details.append(box('PanelJointLong', (sx * (RAIL_C + .0375 + .75), 0, DECK_TOP + .0008), (.015, 2 * DECK_HALF_Y, .0016), 'RW_DeckSeam'))
panel = 1.2
n_panels = int(round(2 * DECK_HALF_Y / panel))
for i in range(1, n_panels):
    y = -DECK_HALF_Y + i * 2 * DECK_HALF_Y / n_panels
    for x0, x1 in [(-(RAIL_C - .0375 - FL), RAIL_C - .0375 - FL),
                   (RAIL_C + .0375, DECK_HALF_X - EDGE), (-(DECK_HALF_X - EDGE), -(RAIL_C + .0375))]:
        details.append(box('PanelJoint', ((x0 + x1) / 2, y, DECK_TOP + .0008), (abs(x1 - x0), .015, .0016), 'RW_DeckSeam'))
for i in range(n_panels):
    y = -DECK_HALF_Y + (i + .5) * 2 * DECK_HALF_Y / n_panels
    for x in (-.45, .45, -1.25, 1.25):
        details.append(cyl('Bolt', (x, y + .45, DECK_TOP + .003), .018, .006, 'RW_Fastening', n=8))
        details.append(cyl('Bolt', (x, y - .45, DECK_TOP + .003), .018, .006, 'RW_Fastening', n=8))
join(details, 'Detail_DeckSurface')

# ==============================================================================
# 2. AUTOMATIC BARRIER (TK_RailwayBarrier)
#    Origin at the cabinet base; faces -Y; stands on the driver's right and the boom
#    spans the lane to the driver's left (-X). Authored lowered (closed).
# ==============================================================================
r = root('TK_RailwayBarrier', 'barrier', label='Автоматический шлагбаум')
BOOM_LEN = 4.5
PIVOT = (0.0, -0.27, 0.95)
box('Foundation', (0, 0, .04), (.75, .75, .08), 'RW_Concrete', .01)
box('Cabinet', (0, 0, .08 + .5), (.42, .38, 1.0), 'RW_Cabinet', .015)
box('CabinetLid', (0, 0, 1.1), (.46, .42, .04), 'RW_Cabinet', .01)
box('CabinetDoor', (0, -.192, .52), (.32, .01, .70), 'RW_Steel', .005)
box('DoorHandle', (.12, -.2, .56), (.02, .02, .08), 'RW_Black')
box('WarningBand', (0, 0, .30), (.425, .385, .05), 'RW_Red')           # red band for visibility
cyl('Shaft', (0, -.23, PIVOT[2]), .045, .12, 'RW_Steel', 'Y')
pivot = empty('Boom_Pivot', PIVOT)
BP = dict(parent=pivot)
cyl('Hub', PIVOT, .09, .10, 'RW_Steel', 'Y', **BP)
# Striped boom: alternating 0.5 m segments (separate faces, no overlapping stripes).
x = -.12
seg = []
i = 0
while x > -BOOM_LEN:
    x2 = max(x - .5, -BOOM_LEN)
    seg.append(box('BoomSeg', ((x + x2) / 2, PIVOT[1], PIVOT[2]), (x - x2, .08, .12),
                   'RW_Red' if i % 2 == 0 else 'RW_White', **BP))
    x, i = x2, i + 1
boom = join(seg, 'Boom')
parent_keep(boom, pivot)
cyl('BoomEndCap', (-BOOM_LEN - .01, PIVOT[1], PIVOT[2]), .045, .02, 'RW_Black', 'X', 16, **BP)
# Counterweight arm on the other side of the hinge.
box('CounterArm', (.25, PIVOT[1], PIVOT[2]), (.34, .06, .08), 'RW_Steel', **BP)
box('Counterweight', (.52, PIVOT[1], PIVOT[2]), (.26, .12, .24), 'RW_Black', .01, **BP)
# Three red lamps on the boom, visible from both approaches.
for j, lx in enumerate((-1.3, -2.8, -BOOM_LEN + .18)):
    z = PIVOT[2] + .06 + .06
    box(f'LampBody_{j}', (lx, PIVOT[1], z), (.10, .10, .12), 'RW_Housing', .01, **BP)
    for sy, face in ((-1, 'F'), (1, 'B')):
        yy = PIVOT[1] + sy * .051
        cyl(f'Lens_Boom_{j}{face}', (lx, yy + sy * .002, z), .035, .004, 'RW_LensRed', 'Y', 20, **BP)
        cyl(f'Lamp_Boom_{j}{face}', (lx, yy + sy * .005, z), .031, .002, 'RW_RedOn', 'Y', 20, **BP)
empty('Boom_Tip', (-BOOM_LEN, PIVOT[1], PIVOT[2]), pivot)

# ==============================================================================
# 3. CROSSING SIGNAL (TK_RailwaySignal) with sign 1.3.1 on the mast
#    Two red lamps side by side (alternate flashing), lunar-white lamp above.
# ==============================================================================
r = root('TK_RailwaySignal', 'signal', label='Переездный светофор со знаком 1.3.1')
MAST_H = 3.95
box('Foundation', (0, .02, .04), (.5, .5, .08), 'RW_Concrete', .01)
cyl('Mast', (0, .07, MAST_H / 2), .057, MAST_H, 'RW_Steel')
cyl('MastCap', (0, .07, MAST_H + .01), .062, .02, 'RW_Steel')
for zz in (.35, .7, 1.05, 1.4):   # black-and-white banding on the lower mast
    cyl('MastBand', (0, .07, zz), .059, .175, 'RW_Black')
RED_Z, WHITE_Z = 2.30, 2.72
# Contrast screen: black with a white edge.
box('Screen_Edge', (0, .004, RED_Z), (1.04, .018, .50), 'RW_White', .02)
box('Screen', (0, -.006, RED_Z), (.98, .012, .44), 'RW_Black', .015)
box('ScreenBracket', (0, .045, RED_Z), (.14, .07, .30), 'RW_Steel')
box('WhiteBracket', (0, .045, WHITE_Z), (.12, .07, .12), 'RW_Steel')


def signal_head(tag, x, z, lens, lamp, r_lens=.105):
    box(f'Module_{tag}', (x, -.06, z), (.28, .10, .28), 'RW_Housing', .02)
    cyl(f'LensSeal_{tag}', (x, -.115, z), r_lens + .014, .012, 'RW_Gasket', 'Y', 40)
    cyl(f'Lens_{tag}', (x, -.125, z), r_lens, .010, lens, 'Y', 40)
    cyl(f'Lamp_{tag}', (x, -.132, z), r_lens - .008, .003, lamp, 'Y', 40)
    half_tube_visor(f'Visor_{tag}', x, z, -.12, -.30, r_lens + .03, 'RW_Housing')


signal_head('Red_L', -.30, RED_Z, 'RW_LensRed', 'RW_RedOn')
signal_head('Red_R', .30, RED_Z, 'RW_LensRed', 'RW_RedOn')
signal_head('White', 0, WHITE_Z, 'RW_LensWhite', 'RW_WhiteOn', .085)
cyl('Bell', (0, -.02, 1.95), .10, .08, 'RW_Black', 'Y', 24)            # audible warning
box('BellBracket', (0, .03, 1.95), (.06, .10, .06), 'RW_Steel')

# ==============================================================================
# 4. SIGNS 1.1 - 1.4 (GOST R 52290-2004, size II)
# ==============================================================================
SIGN_Z = 2.45


def post(height, rad=.038):
    cyl('Post', (0, .055, height / 2), rad, height, 'Traffic_Steel')
    box('BasePlate', (0, .055, .017), (.24, .24, .034), 'Traffic_Steel', .012)
    for xx in (-.083, .083):
        for yy in (-.028, .138):
            cyl('AnchorBolt', (xx, yy, .045), .012, .023, 'Traffic_Steel', n=6)
    cyl('PostCap', (0, .055, height + .008), rad + .003, .016, 'Traffic_Steel')


def mount(zs, width=.40):
    for h in zs:
        box('RearRail', (0, .008, h), (width, .035, .04), 'Traffic_Steel', .005)
        box('PostClamp', (0, .06, h), (.105, .11, .032), 'Traffic_Steel', .005)


def sign_root(name, label, height=2.75):
    root('DS_Sign_' + name, 'sign', sockets='traffic', label=label)
    post(height)


def cross_arms(cz, arm_len=1.20, arm_w=.18, angle_deg=35, border=.035):
    """Saint Andrew's cross (1.3.1): white arms with a red edge; the second arm sits in front."""
    a = math.radians(angle_deg)
    depth = [(-.081, -.091), (-.097, -.103)]            # (red, white) plane per arm, no coplanar faces
    for idx, ang in enumerate((a, -a)):
        outline = rect_rot(0, cz, arm_len, arm_w, ang)
        plate('Plate_Back', rect_rot(0, cz, arm_len + .01, arm_w + .01, ang), -.065, .026, 'Traffic_Steel')
        plate(f'Face_Border_{idx}', outline, depth[idx][0], .004, 'Traffic_Red')
        plate(f'Face_Field_{idx}', rect_rot(0, cz, arm_len - 2 * border, arm_w - 2 * border, ang), depth[idx][1], .004, 'Traffic_White')


def chevron(cz_apex, arm_len, arm_w, angle_deg, y, material, name):
    """Inverted-V element under the cross of sign 1.3.2, as one concave outline."""
    a = math.radians(angle_deg)
    ca, sa = math.cos(a), math.sin(a)
    h = arm_w / (2 * ca)
    ex, ez = arm_len * ca, cz_apex - arm_len * sa           # centre of the right-hand arm end
    nx, nz = sa * arm_w / 2, ca * arm_w / 2                  # half-width normal of that arm
    pts = [(0, cz_apex + h), (ex + nx, ez + nz), (ex - nx, ez - nz), (0, cz_apex - h),
           (-ex + nx, ez - nz), (-ex - nx, ez + nz)]
    return plate(name, pts, y, .004, material)


# 1.1 Railway crossing with barrier: red-edged triangle, fence pictogram.
tri = shape(3, .50, SIGN_Z, math.pi / 2)


def triangle_face():
    mount([SIGN_Z - .16, SIGN_Z + .10])
    plate('Plate_Back', scale_about(tri, 0, SIGN_Z, 1.01), -.065, .026, 'Traffic_Steel')
    plate('Face_Border', tri, -.081, .008, 'Traffic_Red')
    plate('Face_Field', scale_about(tri, 0, SIGN_Z, .70), -.091, .004, 'Traffic_White')


sign_root('RailwayBarrier', 'Знак 1.1 «Железнодорожный переезд со шлагбаумом»')
triangle_face()
fz = SIGN_Z - .06
for zz in (fz - .045, fz + .045):
    plate('Pictogram_Rail', [(-.17, zz - .012), (.17, zz - .012), (.17, zz + .012), (-.17, zz + .012)], -.098, .003, 'Traffic_Ink')
for px in (-.15, -.075, 0, .075, .15):
    plate('Pictogram_Picket', [(px - .016, fz - .10), (px + .016, fz - .10), (px + .016, fz + .085), (px, fz + .11), (px - .016, fz + .085)],
          -.099, .003, 'Traffic_Ink')

# 1.2 Railway crossing without barrier: steam locomotive facing left.
sign_root('RailwayNoBarrier', 'Знак 1.2 «Железнодорожный переезд без шлагбаума»')
triangle_face()
# Locomotive drawn in local units (origin at the rails line), then fitted into the white field.
LOCO_K, LOCO_Z = .72, SIGN_Z - .054          # scale and height of the local origin
field_tri = scale_about(tri, 0, SIGN_Z, .70)
loco_pts = []


def loco(name, pts, y=-.098, material='Traffic_Ink'):
    pts = [(LOCO_K * x, LOCO_Z + LOCO_K * z) for x, z in pts]
    loco_pts.extend(pts)
    plate(name, pts, y, .003, material)


loco('Loco_Boiler', [(-.16, -.02), (.06, -.02), (.06, .07), (-.16, .07), (-.175, .045), (-.175, .005)])
loco('Loco_Chimney', [(-.14, .06), (-.105, .06), (-.098, .15), (-.147, .15)])
loco('Loco_Dome', [(-.05, .06), (0, .06), (-.008, .095), (-.042, .095)])
loco('Loco_Cab', [(.05, -.02), (.17, -.02), (.17, .16), (.04, .16), (.04, .145), (.05, .145)])
loco('Loco_Window', [(.075, .08), (.145, .08), (.145, .13), (.075, .13)], -.1, 'Traffic_White')
loco('Loco_Frame', [(-.19, -.05), (.18, -.05), (.18, -.02), (-.19, -.02)])
loco('Loco_Pilot', [(-.19, -.05), (-.16, -.05), (-.16, -.02), (-.215, -.085)])
for wx in (-.11, -.015, .08):
    loco('Loco_Wheel', [(x + wx, z) for x, z in shape(20, .042, -.085)])


def inside_triangle(p, t, margin):
    for i in range(3):
        (ax, az), (bx, bz_) = t[i], t[(i + 1) % 3]
        ex, ez = bx - ax, bz_ - az
        # signed distance to the edge; the triangle is counter-clockwise
        d = (ex * (p[1] - az) - ez * (p[0] - ax)) / math.hypot(ex, ez)
        if d < margin:
            return False
    return True


LOCO_FIT = all(inside_triangle(p, field_tri, .02) for p in loco_pts)

# 1.3.1 Single-track railway.
sign_root('SingleTrack', 'Знак 1.3.1 «Однопутная железная дорога»', 2.4)
mount([SIGN_Z - .18, SIGN_Z + .18], .30)
cross_arms(SIGN_Z)

# 1.3.2 Multi-track railway: cross with an inverted V below.
sign_root('MultiTrack', 'Знак 1.3.2 «Многопутная железная дорога»', 2.4)
cz = SIGN_Z + .18
mount([cz - .15, cz + .15, SIGN_Z - .42], .30)
cross_arms(cz)
apex = SIGN_Z - .28
chevron(apex + .004, .62, .19, 35, -.065, 'Traffic_Steel', 'Plate_Back')
chevron(apex, .60, .18, 35, -.081, 'Traffic_Red', 'Face_Border_V')
chevron(apex - .002, .565, .11, 35, -.091, 'Traffic_White', 'Face_Field_V')

# 1.4.1-1.4.6 Approach to a railway crossing: 350 x 700 mm, red stripes sloping towards the road.
W, H = .35, .70


def dist_marker(name, label, stripes, right_side):
    sign_root(name, label)
    mount([SIGN_Z - .2, SIGN_Z + .2], .26)
    rect = [(-W / 2, SIGN_Z - H / 2), (W / 2, SIGN_Z - H / 2), (W / 2, SIGN_Z + H / 2), (-W / 2, SIGN_Z + H / 2)]
    plate('Plate_Back', scale_about(rect, 0, SIGN_Z, 1.02), -.065, .026, 'Traffic_Steel')
    plate('Face_Field', rect, -.081, .008, 'Traffic_White')
    slope = math.tan(math.radians(30)) * (1 if right_side else -1)
    t = .085                                    # vertical band thickness
    offsets = {3: (-.20, 0, .20), 2: (-.10, .10), 1: (0,)}[stripes]
    for idx, off in enumerate(offsets):
        c = SIGN_Z + off
        band = [(-W / 2, SIGN_Z - H), (W / 2, SIGN_Z - H), (W / 2, SIGN_Z + H), (-W / 2, SIGN_Z + H)]
        band = clip_poly(band, -slope, 1, c + t / 2)          # z - slope*x <= c + t/2
        band = clip_poly(band, slope, -1, -(c - t / 2))       # z - slope*x >= c - t/2
        band = clip_poly(band, 0, 1, SIGN_Z + H / 2 - .01)
        band = clip_poly(band, 0, -1, -(SIGN_Z - H / 2 + .01))
        plate(f'Stripe_{idx}', band, -.087, .003, 'Traffic_Red')


dist_marker('RailwayDist3_R', 'Знак 1.4.1 (три полосы, справа)', 3, True)
dist_marker('RailwayDist2_R', 'Знак 1.4.2 (две полосы, справа)', 2, True)
dist_marker('RailwayDist1_R', 'Знак 1.4.3 (одна полоса, справа)', 1, True)
dist_marker('RailwayDist3_L', 'Знак 1.4.4 (три полосы, слева)', 3, False)
dist_marker('RailwayDist2_L', 'Знак 1.4.5 (две полосы, слева)', 2, False)
dist_marker('RailwayDist1_L', 'Знак 1.4.6 (одна полоса, слева)', 1, False)
current_root = None

# ==============================================================================
# 5. Checks, export, manifest
# ==============================================================================
bpy.context.view_layer.update()


def measure(o):
    pts, tris, mats = [], 0, set()
    for ch in o.children_recursive:
        if ch.type != 'MESH':
            continue
        ch.data.calc_loop_triangles()
        tris += len(ch.data.loop_triangles)
        pts.extend(ch.matrix_world @ v.co for v in ch.data.vertices)
        mats.update(m.name for m in ch.data.materials)
    lo = [min(p[i] for p in pts) for i in range(3)]
    hi = [max(p[i] for p in pts) for i in range(3)]
    return dict(boundsMin=[round(v, 4) for v in lo], boundsMax=[round(v, 4) for v in hi],
                dimensionsM=[round(hi[i] - lo[i], 4) for i in range(3)], triangles=tris, materials=sorted(mats))


def top_z(obj_name):
    o = bpy.data.objects[obj_name]
    return max((o.matrix_world @ v.co).z for v in o.data.vertices)


def require(cond, msg):
    if not cond:
        raise RuntimeError('RAILWAY CHECK FAILED: ' + msg)


checks = []
rail_top = top_z('Detail_Rail_L')
deck_top = top_z('DriveSurface_Deck')
require(abs(rail_top - deck_top - .005) < 1e-4, f'rail head {rail_top:.4f} vs deck {deck_top:.4f}')
ramp = bpy.data.objects['DriveSurface_Ramp_W']
ramp_z = sorted({round((ramp.matrix_world @ v.co).z, 4) for v in ramp.data.vertices})
require(ramp_z[-1] == round(DECK_TOP, 4) and RAMP_END_Z in ramp_z, 'ramp heights ' + str(ramp_z))
checks.append(f'rail head {rail_top:.3f} m, deck {deck_top:.3f} m, ramp {DECK_TOP:.3f} -> {RAMP_END_Z:.3f} m over {RAMP_LEN} m')
track = bpy.data.objects['TK_RailwayCrossing_Tracks']
for ch in track.children:
    if ch.type == 'MESH':
        require(ch.name.startswith(('DriveSurface_', 'Detail_')), 'track part without collider prefix: ' + ch.name)
checks.append('track parts are DriveSurface_* or Detail_* only')
pv = bpy.data.objects['Boom_Pivot']
require(all(c.name.startswith(('Boom', 'Hub', 'Counter', 'Lamp', 'Lens')) for c in pv.children), 'boom hierarchy')
require(len(pv.children) >= 10, 'boom children missing')
checks.append(f'boom pivot at {tuple(round(v, 3) for v in pv.matrix_world.translation)}, {len(pv.children)} children')
sig = bpy.data.objects['TK_RailwaySignal']
lamps = sorted(c.name for c in sig.children if c.name.startswith('Lamp_'))
require(lamps == ['Lamp_Red_L', 'Lamp_Red_R', 'Lamp_White'], 'signal lamps ' + str(lamps))
checks.append('signal lamps: ' + ', '.join(lamps))
for o in roots:
    kids = [c.name for c in o.children]
    if o.name.startswith('DS_Sign_'):
        require(sum(k.startswith('Socket_Front') for k in kids) == 1 and any(k.startswith('Plate_Back') for k in kids)
                and any(k == 'Post' or k.startswith('Post.') for k in kids), 'sign contract ' + o.name)
        h = measure(o)['boundsMax'][2]
        require(2.7 < h < 3.5, f'{o.name} height {h}')
require(LOCO_FIT, 'locomotive pictogram leaves the white field of sign 1.2')
checks.append('pictogram of 1.2 fits the white field with a 20 mm margin')
checks.append('signs: Socket_Front, Post, Plate_Back present; heights 2.7-3.5 m')

# Put the cross of 1.3.1 on the signal mast as a copy of the sign face (crossing without barrier).
src = bpy.data.objects['DS_Sign_SingleTrack']
for ch in list(src.children):
    if ch.type == 'MESH' and ch.name.startswith(('Plate_Back', 'Face_')):
        cp = ch.copy()
        cp.data = ch.data.copy()
        link(cp)
        cp.parent = sig
        cp.matrix_parent_inverse = sig.matrix_world.inverted()
        cp.location.z += 3.42 - SIGN_Z
        cp.name = 'Sign131_' + ch.name.split('.')[0]
current_root = sig
box('Sign131_Bracket', (0, 0, 3.42), (.12, .08, .5), 'RW_Steel')
current_root = None
bpy.context.view_layer.update()

manifest = dict(revision='railway-v2', utc=datetime.now(timezone.utc).isoformat(), exporter=bpy.app.version_string,
                units='metres', sourceAxes='X right, Z up, front -Y', checks=checks, assets=[])
for o in roots:
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    for ch in o.children_recursive:
        ch.select_set(True)
    bpy.context.view_layer.objects.active = o
    target = OUT_KIT if o['kind'] in ('facility', 'barrier', 'signal') else OUT_TRAFFIC
    fbx = target / (o.name + '.fbx')
    glb = REVIEW / (o.name + '.glb')
    bpy.ops.export_scene.fbx(filepath=str(fbx), use_selection=True, object_types={'MESH', 'EMPTY'},
                             axis_forward='-Z', axis_up='Y', apply_unit_scale=True, add_leaf_bones=False, bake_anim=False)
    bpy.ops.export_scene.gltf(filepath=str(glb), export_format='GLB', use_selection=True, export_apply=True)
    manifest['assets'].append(dict(catalogId=o.name, label=o['label'], kind=o['kind'], **measure(o),
                                   fbx=str(fbx.relative_to(ROOT)).replace('\\', '/'),
                                   fbxSha256=hashlib.sha256(fbx.read_bytes()).hexdigest()))
    print(f'Exported: {o.name} -> {fbx.relative_to(ROOT)}', flush=True)

(OUT_KIT / 'RW_Materials.json').write_text(json.dumps(dict(materials=SPECS), indent=2), encoding='utf8')
(REPORTS / 'railway-manifest.json').write_text(json.dumps(manifest, indent=2, ensure_ascii=False), encoding='utf8')
for c in checks:
    print('CHECK', c, flush=True)

# ==============================================================================
# 6. Review renders
# ==============================================================================
blend_path = ROOT / 'ArtSource/DS_RailwayKit.blend'
if not RENDER:
    bpy.ops.wm.save_as_mainfile(filepath=str(blend_path))
    print('RAILWAY_ASSETS_BUILD_PASS (no renders)', flush=True)
    raise SystemExit(0)

scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.samples = 48
scene.cycles.use_denoising = True
if not scene.world:
    scene.world = bpy.data.worlds.new('World')
scene.world.color = (.42, .52, .66)
if scene.world.use_nodes and scene.world.node_tree.nodes.get('Background'):
    bg = scene.world.node_tree.nodes['Background']
    bg.inputs['Color'].default_value = (.42, .52, .66, 1)
    bg.inputs['Strength'].default_value = .8
scene.view_settings.view_transform = 'AgX'
scene.render.image_settings.file_format = 'PNG'

mat('Show_Ground', (.16, .17, .15), 0, .95, export_spec=False)
mat('Show_Road', (.035, .04, .045), 0, .9, export_spec=False)
mat('Show_Paint', (.8, .8, .78), 0, .5, export_spec=False)
stage = []
bpy.ops.mesh.primitive_plane_add(size=1, location=(0, 0, 0))
g = bpy.context.object
g.scale = (80, 60, 1)
g.data.materials.append(M['Show_Ground'])
stage.append(g)
bpy.ops.mesh.primitive_plane_add(size=1, location=(0, 0, .015))
rd = bpy.context.object
rd.scale = (60, 6, 1)
rd.data.materials.append(M['Show_Road'])
stage.append(rd)
for yy in (-2.9, 2.9):
    bpy.ops.mesh.primitive_plane_add(size=1, location=(0, yy, .017))
    p = bpy.context.object
    p.scale = (60, .12, 1)
    p.data.materials.append(M['Show_Paint'])
    stage.append(p)
bpy.ops.mesh.primitive_plane_add(size=1, location=(-5.0, -1.5, .017))
p = bpy.context.object
p.scale = (.4, 3.0, 1)
p.data.materials.append(M['Show_Paint'])
stage.append(p)

sun = bpy.data.lights.new('Sun', 'SUN')
sun.energy = 3.2
sun.angle = math.radians(2)
so = link(bpy.data.objects.new('Sun', sun))
so.rotation_euler = (math.radians(50), math.radians(10), math.radians(-35))

cam_data = bpy.data.cameras.new('ReviewCamera')
cam = link(bpy.data.objects.new('ReviewCamera', cam_data))
scene.camera = cam


def show(names, stage_on=True):
    for o in roots:
        on = o.name in names
        for ob in [o] + list(o.children_recursive):
            ob.hide_render = not on
    for s in stage:
        s.hide_render = not stage_on


def lamps_on(obj_names, on=True):
    for n in obj_names:
        for ob in bpy.data.objects[n].children_recursive:
            if ob.name.startswith('Lamp_'):
                ob.hide_render = not on


def render(name, pos, target, lens=35, w=1600, h=900, ortho=None):
    cam.location = pos
    cam.rotation_euler = (Vector(target) - Vector(pos)).to_track_quat('-Z', 'Y').to_euler()
    cam_data.type = 'ORTHO' if ortho else 'PERSP'
    if ortho:
        cam_data.ortho_scale = ortho
    else:
        cam_data.lens = lens
    scene.render.resolution_x, scene.render.resolution_y = w, h
    scene.render.resolution_percentage = 100
    out = REVIEW / (name + '.png')
    scene.render.filepath = str(out)
    bpy.ops.render.render(write_still=True)
    if DESKTOP:
        try:
            DESKTOP.mkdir(parents=True, exist_ok=True)
            (DESKTOP / out.name).write_bytes(out.read_bytes())
        except OSError:
            pass
    print('Rendered:', out.name, flush=True)


for old in REVIEW.glob('*.png'):          # v1 renders used other names
    try:
        old.unlink()
    except OSError:
        pass

track = bpy.data.objects['TK_RailwayCrossing_Tracks']
barrier = bpy.data.objects['TK_RailwayBarrier']
signal = bpy.data.objects['TK_RailwaySignal']
sig_l = signal.copy()           # second signal on the driver's left
link(sig_l)
for ch in signal.children:
    c2 = ch.copy()
    if ch.type == 'MESH':
        c2.data = ch.data
    link(c2)
    c2.parent = sig_l
roots.append(sig_l)
# Driver approaches along +X; right-hand side of the road is -Y.
rot = -math.pi / 2
signal.location, signal.rotation_euler = (-6.3, -4.3, 0), (0, 0, rot)
sig_l.location, sig_l.rotation_euler = (-6.3, 4.3, 0), (0, 0, rot)
barrier.location, barrier.rotation_euler = (-4.6, -3.7, 0), (0, 0, rot)
stop = bpy.data.objects['DS_Sign_RailwayBarrier']
stop.location, stop.rotation_euler = (-16, -4.0, 0), (0, 0, rot)
bpy.context.view_layer.update()

crossing = ['TK_RailwayCrossing_Tracks', 'TK_RailwayBarrier', 'TK_RailwaySignal', sig_l.name]
show(crossing)
lamps_on(crossing, True)
render('01_crossing_overview', (-15, -12, 7.5), (0, 0, .4), 30)
show(crossing + ['DS_Sign_RailwayBarrier'])
lamps_on(crossing, True)
render('02_driver_view', (-22, -1.5, 1.25), (0, -.8, 1.0), 42)
show(['TK_RailwayCrossing_Tracks'])
render('03_track_deck_ramp', (-5.5, -7.5, 1.1), (-1.2, -1.5, .1), 32)
render('04_track_top', (0, 0, 22), (0, 0, 0), ortho=14, w=1400, h=1000)
show(['TK_RailwaySignal', 'TK_RailwayBarrier'], stage_on=True)
lamps_on(['TK_RailwaySignal', 'TK_RailwayBarrier'], True)
render('05_signal_barrier_closeup', (-10.5, -6.2, 2.4), (-5.2, -2.8, 1.9), 40)
pv.rotation_euler.y = math.radians(82)
render('06_barrier_raised', (-10.5, -7.5, 2.8), (-4.6, -2.0, 1.8), 34)
pv.rotation_euler.y = 0
lamps_on(['TK_RailwaySignal'], False)
render('07_signal_lamps_off', (-10.5, -6.2, 2.4), (-5.2, -2.8, 1.9), 40)

signs = ['DS_Sign_RailwayBarrier', 'DS_Sign_RailwayNoBarrier', 'DS_Sign_SingleTrack', 'DS_Sign_MultiTrack']
dists = ['DS_Sign_RailwayDist3_R', 'DS_Sign_RailwayDist2_R', 'DS_Sign_RailwayDist1_R',
         'DS_Sign_RailwayDist3_L', 'DS_Sign_RailwayDist2_L', 'DS_Sign_RailwayDist1_L']
stop.rotation_euler = (0, 0, 0)
for i, n in enumerate(signs):
    bpy.data.objects[n].location = ((i - 1.5) * 1.6, 30, 0)
for i, n in enumerate(dists):
    bpy.data.objects[n].location = ((i - 2.5) * .85, 30, 0)
show(signs, stage_on=False)
render('08_signs_1_1_to_1_3', (0, 20, 2.45), (0, 30, 2.45), ortho=6.6, w=1600, h=700)
show(dists, stage_on=False)
render('09_signs_1_4', (0, 20, 2.45), (0, 30, 2.45), ortho=5.4, w=1600, h=700)

# Restore the export layout in the editable source.
for o in roots:
    o.location, o.rotation_euler = (0, 0, 0), (0, 0, 0)
roots.remove(sig_l)
for ob in list(sig_l.children):
    bpy.data.objects.remove(ob)
bpy.data.objects.remove(sig_l)
for s in stage:
    bpy.data.objects.remove(s)
bpy.ops.wm.save_as_mainfile(filepath=str(blend_path))
print('RAILWAY_ASSETS_BUILD_PASS', flush=True)
