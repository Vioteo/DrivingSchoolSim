"""Cabin upholstery and trim of DS_Sedan_A, second pass (driver feedback 26.09: "the cabin could be finer").

Rebuilds with rounded, sculpted shapes instead of bevelled boxes:
- front seats: cushion with a raised front edge, side bolsters, fabric insert with pleats, backrest with lumbar
  bulge, shoulder taper and wrap-round bolsters, rounded headrest, outboard side cover with recline knob;
- rear bench: two seat dips, three fabric panels, rounded headrests;
- dashboard: soft upper pad with a lip over the gauges and a two-tone lower part with knee roll, glovebox, silver
  strip, a housing that carries the centre screen, a silver frame round the vents and climate controls;
- centre console with cup holders and an armrest, following the shift boot and the handbrake;
- door cards: shaped panel, fabric insert, silver strip, armrest with window switches, pull cup, inner handle,
  map pocket, speaker grille, rubber window ledge; the rear cards clear the wheel arch;
- headliner that follows the arched roof, grab handles.

Kept (names used by code): Instrument_Hood, gauges, Needle_*, Cluster_Display, Infotainment*, Vent*, Climate*,
SteeringWheel_Pivot, Pedal_*, GearLever_Pivot, Handbrake_Pivot, Transmission_*, Seat_Front*, Seat_BackFrame*,
Seat_RearBench, RearBench_Back (the empties stay, their meshes are rebuilt), belts, sun visors, roof lamp.

Run after tools/build_sedan_exterior.py (the headliner follows its roof):
  "C:\\Program Files\\Blender Foundation\\Blender 5.0\\blender.exe" -b ArtSource/DS_Sedan_A.blend --python-exit-code 1 --python tools/build_sedan_interior.py
Preview without saving: ... --python tools/build_sedan_interior.py -- --preview <folder>
"""
import bpy, bmesh, sys, math, json, shutil, hashlib, datetime
from pathlib import Path
from mathutils import Vector, Matrix

sys.path.insert(0, str(Path(__file__).parent))
import build_art as a
import build_sedan_exterior as ex

ROOT = a.ROOT
BLEND = ROOT / 'ArtSource/DS_Sedan_A.blend'
FBX = ROOT / 'Assets/DrivingSchool/Art/DS_Sedan_A.fbx'
BACKUP = ROOT / 'ArtSource_Backup'
REPORT = ROOT / 'artifacts/reports/sedan-interior.json'
ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
PREVIEW = Path(ARGS[ARGS.index('--preview') + 1]).resolve() if '--preview' in ARGS else None

REPLACED = ('Dashboard', 'Dash_', 'Glovebox', 'Centre_Tunnel', 'Console_', 'Armrest', 'Door_Trim_', 'Door_Armrest_',
            'DoorPull', 'RearDoorPull', 'DoorSpeaker', 'WindowSwitch', 'RearWindowSwitch', 'Headliner', 'RearHeadrest',
            'Door_', 'GrabHandle', 'Screen_Housing', 'Stack_')
KEEP = ('Door_Front', 'Door_Rear', 'RearHeadrestRod')          # contract prefixes (not in the model today, never delete them)
SEAT_EMPTIES = ('Seat_Front', 'Seat_BackFrame', 'Seat_RearBench', 'RearBench_Back')


def smooth(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def lerp(p, q, t):
    return p + (q - p) * t


def sp(w, m):
    return math.copysign(abs(w) ** m, w)


def superbox(bm, c, h, e=0.3, nu=14, nv=28, deform=None, rot=None, mat=0):
    """Superquadric 'rounded box' centred at c with half sizes h; e < 1 is boxy, e = 1 an ellipsoid.
    deform(p) moves local points before rotation."""
    c = Vector(c)
    top = None; bottom = None
    rings = []
    for i in range(1, nu):
        u = -math.pi / 2 + math.pi * i / nu
        ring = []
        for j in range(nv):
            v = -math.pi + 2 * math.pi * j / nv
            p = Vector((h[0] * sp(math.cos(u), e) * sp(math.cos(v), e),
                        h[1] * sp(math.cos(u), e) * sp(math.sin(v), e),
                        h[2] * sp(math.sin(u), e)))
            if deform:
                p = deform(p)
            if rot is not None:
                p = rot @ p
            ring.append(bm.verts.new(c + p))
        rings.append(ring)
    pb = Vector((0, 0, -h[2])); pt = Vector((0, 0, h[2]))
    if deform:
        pb, pt = deform(pb), deform(pt)
    if rot is not None:
        pb, pt = rot @ pb, rot @ pt
    vb, vt = bm.verts.new(c + pb), bm.verts.new(c + pt)
    fs = []
    for r0, r1 in zip(rings, rings[1:]):
        for j in range(nv):
            fs.append(bm.faces.new((r0[j], r0[(j + 1) % nv], r1[(j + 1) % nv], r1[j])))
    for j in range(nv):
        fs.append(bm.faces.new((vb, rings[0][(j + 1) % nv], rings[0][j])))
        fs.append(bm.faces.new((vt, rings[-1][j], rings[-1][(j + 1) % nv])))
    for f in fs:
        f.material_index = mat
    bmesh.ops.recalc_face_normals(bm, faces=fs)
    return fs


def obj(name, bm, mats, parent, local=True, sm=50):
    """Mesh object; with local=True the geometry is in the parent's space."""
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me); bm.free()
    for mn in mats:
        me.materials.append(a.M[mn])
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    ex.set_smooth(me, sm)
    wn = o.modifiers.new('Corner normals', 'WEIGHTED_NORMAL'); wn.keep_sharp = True
    o.parent = parent
    if local:
        o.matrix_parent_inverse = Matrix.Identity(4)
    else:
        o.matrix_parent_inverse = parent.matrix_world.inverted()
    return o


def rot_x(deg):
    return Matrix.Rotation(math.radians(deg), 3, 'X')


def rot_z(deg):
    return Matrix.Rotation(math.radians(deg), 3, 'Z')


def ensure_material(name, color, rough=0.9, metal=0.0):
    if name not in a.M:
        a.mat(name, color, metal, rough)


# ---------------------------------------------------------------------------------------------- seats
def front_seat(seat, back):
    """seat: Seat_Front<x> empty (origin on the rails' mount), back: its Seat_BackFrame (reclined)."""
    side = 1 if seat.matrix_world.translation.x > 0 else -1
    made = []
    # rails and mounts
    bm = bmesh.new()
    for sx in (-0.17, 0.17):
        ex.box(bm, (sx - 0.017, -0.255, -0.105), (sx + 0.017, 0.225, -0.06), 0)
        for y in (-0.18, 0.18):
            ex.box(bm, (sx - 0.024, y - 0.032, -0.072), (sx + 0.024, y + 0.032, 0.0), 1)
    made.append(obj('Seat_Rail', bm, ['Satin_Aluminium', 'Rubber'], seat))

    def cushion_deform(p):
        # front edge raised (thigh support), a slight dish in the middle, rear edge lower
        q = p.copy()
        if q.z > 0:
            q.z += 0.028 * smooth((q.y + 0.05) / 0.26) - 0.012 * (1 - abs(q.x) / 0.25) * smooth((0.12 - abs(q.y)) / 0.12)
        return q
    bm = bmesh.new()
    superbox(bm, (0, 0.0, 0.088), (0.245, 0.255, 0.068), 0.32, deform=cushion_deform)
    made.append(obj('Cushion', bm, ['Leather'], seat))
    bm = bmesh.new()
    for sx in (-0.198, 0.198):
        superbox(bm, (sx, 0.005, 0.148), (0.052, 0.225, 0.042), 0.5,
                 deform=lambda p: Vector((p.x, p.y, p.z + 0.02 * smooth((p.y + 0.05) / 0.25))))
    made.append(obj('Cushion_Bolster', bm, ['Leather'], seat))
    bm = bmesh.new()
    superbox(bm, (0, 0.02, 0.146), (0.14, 0.205, 0.014), 0.28,
             deform=lambda p: Vector((p.x, p.y, p.z + 0.028 * smooth((p.y + 0.07) / 0.26) - 0.01 * smooth((0.1 - abs(p.y)) / 0.1))))
    made.append(obj('Cushion_Insert', bm, ['Seat_Fabric'], seat))
    # pleats across the insert
    bm = bmesh.new()
    for y in (-0.10, -0.02, 0.06, 0.14):
        zt = 0.161 + 0.028 * smooth((y + 0.05) / 0.26) - 0.01 * smooth((0.1 - abs(y)) / 0.1)
        ex.tube(bm, [(-0.13, y, zt), (0.13, y, zt)], 0.0025, 5, 0)
    made.append(obj('SeatStitch', bm, ['Seat_Insert'], seat))
    # outboard side cover with the recline knob
    bm = bmesh.new()
    superbox(bm, (side * 0.252, -0.07, 0.07), (0.012, 0.19, 0.055), 0.35)
    superbox(bm, (side * 0.268, -0.19, 0.085), (0.012, 0.028, 0.028), 0.9, mat=1)
    made.append(obj('Seat_SideCover', bm, ['Interior_Graphite', 'Satin_Aluminium'], seat))

    def back_deform(p):
        q = p.copy()
        # shoulders narrower than the hips
        q.x *= 1 - 0.13 * smooth((q.z - 0.05) / 0.25)
        # lumbar bulge on the front face
        if q.y > 0:
            q.y += 0.022 * math.exp(-((q.z + 0.10) / 0.11) ** 2) * (1 - (abs(q.x) / 0.25) ** 2)
        return q
    bm = bmesh.new()
    superbox(bm, (0, -0.005, 0.02), (0.245, 0.06, 0.285), 0.34, deform=back_deform)
    made.append(obj('Backrest', bm, ['Leather'], back))
    bm = bmesh.new()
    for sx in (-1, 1):
        superbox(bm, (sx * 0.192, 0.048, -0.02), (0.05, 0.05, 0.235), 0.5,
                 deform=lambda p: Vector((p.x * (1 - 0.18 * smooth((p.z + 0.02) / 0.25)), p.y, p.z)), rot=rot_z(-sx * 14))
    made.append(obj('Backrest_Bolster', bm, ['Leather'], back))
    bm = bmesh.new()
    superbox(bm, (0, 0.058, -0.005), (0.138, 0.013, 0.225), 0.28,
             deform=lambda p: Vector((p.x * (1 - 0.1 * smooth((p.z + 0.02) / 0.22)), p.y + 0.02 * math.exp(-((p.z + 0.10) / 0.11) ** 2), p.z)))
    made.append(obj('Backrest_Insert', bm, ['Seat_Fabric'], back))
    bm = bmesh.new()
    for z in (-0.16, -0.08, 0.0, 0.08, 0.16):
        y = 0.072 + 0.02 * math.exp(-((z + 0.10) / 0.11) ** 2)
        w = 0.125 * (1 - 0.1 * smooth((z + 0.02) / 0.22))
        ex.tube(bm, [(-w, y, z), (w, y, z)], 0.0025, 5, 0)
    made.append(obj('Seat_FabricRib', bm, ['Seat_Insert'], back))
    bm = bmesh.new()
    for sx in (-0.075, 0.075):
        ex.tube(bm, [(sx, -0.01, 0.28), (sx, -0.01, 0.37)], 0.0075, 10, 0)
    made.append(obj('HeadrestRod', bm, ['Satin_Aluminium'], back))
    bm = bmesh.new()
    superbox(bm, (0, -0.012, 0.425), (0.128, 0.05, 0.08), 0.48,
             deform=lambda p: Vector((p.x * (1 - 0.1 * smooth(p.z / 0.08)), p.y + 0.012 * (1 - (p.x / 0.13) ** 2) * (p.y > 0), p.z)))
    made.append(obj('Headrest', bm, ['Leather'], back))
    return made


def rear_bench(rear, back, car):
    made = []

    def dip(p):
        q = p.copy()
        if q.z > 0:
            for x0 in (-0.37, 0.37):
                q.z -= 0.014 * math.exp(-((q.x - x0) / 0.16) ** 2) * smooth((0.15 - abs(q.y + 0.02)) / 0.15)
            q.z += 0.02 * smooth((q.y + 0.02) / 0.22)
        return q
    bm = bmesh.new()
    superbox(bm, (0, -0.05, 0.02), (0.615, 0.2, 0.06), 0.3)
    made.append(obj('RearBench_Base', bm, ['Interior_Graphite'], rear))
    bm = bmesh.new()
    superbox(bm, (0, -0.02, 0.105), (0.612, 0.225, 0.075), 0.32, nv=40, deform=dip)
    made.append(obj('RearBench_Cushion', bm, ['Leather'], rear))
    bm = bmesh.new()
    for x in (-0.37, 0.0, 0.37):
        w = 0.15 if x else 0.1
        superbox(bm, (x, 0.0, 0.172), (w, 0.18, 0.012), 0.28, deform=lambda p, x=x: dip(Vector((p.x + x, p.y, p.z + 0.07))) - Vector((x, 0, 0.07)))
    made.append(obj('RearBench_CushionInsert', bm, ['Seat_Fabric'], rear))
    bm = bmesh.new()
    superbox(bm, (0, 0.0, 0.0), (0.612, 0.055, 0.245), 0.3, nv=40,
             deform=lambda p: Vector((p.x, p.y + (0.015 * math.exp(-((p.z + 0.08) / 0.1) ** 2) if p.y > 0 else 0.0), p.z)))
    made.append(obj('RearBench_Backrest', bm, ['Leather'], back))
    bm = bmesh.new()
    for x in (-0.37, 0.0, 0.37):
        w = 0.15 if x else 0.1
        superbox(bm, (x, 0.052, 0.0), (w, 0.012, 0.19), 0.28,
                 deform=lambda p: Vector((p.x, p.y + 0.015 * math.exp(-((p.z + 0.08) / 0.1) ** 2), p.z)))
    made.append(obj('RearBench_Insert', bm, ['Seat_Fabric'], back))
    bm = bmesh.new()
    for x in (-0.37, 0.0, 0.37):
        w = 0.14 if x else 0.09
        for z in (-0.1, 0.0, 0.1):
            y = 0.066 + 0.015 * math.exp(-((z + 0.08) / 0.1) ** 2)
            ex.tube(bm, [(x - w + 0.01, y, z), (x + w - 0.01, y, z)], 0.0022, 5, 0)
    made.append(obj('RearSeatStitch', bm, ['Seat_Insert'], back))
    # headrests on the parcel shelf rods (rods are kept)
    bm = bmesh.new()
    for x in (-0.40, 0.0, 0.40):
        z = 1.125 if x else 1.10
        superbox(bm, (x, -0.98, z), (0.125, 0.048, 0.07), 0.48)
    made.append(obj('RearHeadrest', bm, ['Leather'], car, local=False))
    return made


# ---------------------------------------------------------------------------------------------- dashboard
# Dash profile in (y, z): windshield foot → top pad → lip over the face → face → knee roll → underside.
DASH_UPPER = [(1.24, 0.952), (1.10, 0.968), (0.96, 0.988), (0.84, 0.998), (0.76, 1.002), (0.66, 1.004), (0.60, 0.995), (0.565, 0.975),
              (0.54, 0.950), (0.528, 0.922)]
DASH_LOWER = [(0.528, 0.922), (0.524, 0.87), (0.526, 0.81), (0.538, 0.772), (0.57, 0.745), (0.64, 0.726),
              (0.78, 0.716), (0.995, 0.716)]
DASH_HALF = 0.80


def face_y(z):
    """y of the dash face at height z (lower part)."""
    pts = sorted(DASH_LOWER[:5], key=lambda p: p[1])
    for (y0, z0), (y1, z1) in zip(pts, pts[1:]):
        if z0 <= z <= z1:
            return lerp(y0, y1, (z - z0) / (z1 - z0))
    return 0.525


def dash_loft(profile, name, mat, car, closed_back=True):
    bm = bmesh.new()
    xs = [-DASH_HALF + 2 * DASH_HALF * k / 40 for k in range(41)]
    rings = []
    for x in xs:
        # the ends curve back towards the doors
        e = smooth((abs(x) - 0.70) / 0.10)
        ring = []
        for y, z in profile:
            xc = math.copysign(min(abs(x), ex.xs(max(z, 0.93)) - 0.04), x)     # stay inside the A-pillars
            ring.append(bm.verts.new((xc, y + 0.025 * e * (1 if y < 0.7 else 0), z)))
        rings.append(ring)
    n = len(profile)
    loop = closed_back
    for r0, r1 in zip(rings, rings[1:]):
        for k in range(n if loop else n - 1):
            bm.faces.new((r0[k], r0[(k + 1) % n], r1[(k + 1) % n], r1[k]))
    for r in (rings[0], rings[-1]):
        bm.faces.new(r)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return obj(name, bm, [mat], car, local=False, sm=40)


def dashboard(car):
    made = []
    up = DASH_UPPER + [(1.00, 0.922), (1.24, 0.93)]
    made.append(dash_loft(up, 'Dashboard', 'Interior_Graphite', car))
    made.append(dash_loft(DASH_LOWER + [(0.995, 0.922)], 'Dash_Lower', 'Interior_Stone', car))
    # screen housing: the centre screen (Infotainment, kept) stands in a hood that grows out of the top pad
    bm = bmesh.new()
    superbox(bm, (0.12, 0.555, 1.075), (0.165, 0.045, 0.092), 0.22)
    made.append(obj('Screen_Housing', bm, ['Interior_Graphite'], car, local=False))
    # silver frame round the centre vent and the climate controls, silver strip on the passenger side
    bm = bmesh.new()
    frame = ex.rounded_rect(-0.075, 0.775, 0.335, 0.995, 0.02)
    ex.tube(bm, [(u, 0.519 if v < 0.93 else 0.519 - (v - 0.93) * 0.35, v) for u, v in frame], 0.006, 8, 0, closed=True)
    ex.tube(bm, [(x, face_y(0.885) - 0.004, 0.885) for x in (0.36, 0.5, 0.64, 0.78)], 0.005, 8, 0)
    ex.tube(bm, [(x, face_y(0.885) - 0.004, 0.885) for x in (-0.78, -0.74)], 0.005, 8, 0)
    made.append(obj('Dash_Trim', bm, ['Satin_Aluminium'], car, local=False))
    # glovebox lid outline and handle
    bm = bmesh.new()
    lid = ex.rounded_rect(0.36, 0.745, 0.745, 0.862, 0.02)
    ex.tube(bm, [(u, face_y(v) - 0.0015, v) for u, v in lid], 0.0022, 5, 0, closed=True)
    made.append(obj('Glovebox', bm, ['Rubber'], car, local=False))
    bm = bmesh.new()
    superbox(bm, (0.55, face_y(0.835) - 0.008, 0.835), (0.055, 0.008, 0.011), 0.4)
    made.append(obj('GloveboxHandle', bm, ['Satin_Aluminium'], car, local=False))
    # knee panel below the steering column (driver side), slightly proud of the lower face
    bm = bmesh.new()
    superbox(bm, (-0.38, face_y(0.79) - 0.004, 0.79), (0.2, 0.012, 0.035), 0.3)
    made.append(obj('Dash_KneePanel', bm, ['Interior_Graphite'], car, local=False))
    return made


# ---------------------------------------------------------------------------------------------- console
CONSOLE_TOP = [(0.60, 0.742), (0.52, 0.740), (0.40, 0.712), (0.28, 0.690), (0.14, 0.680), (-0.03, 0.676),
               (-0.20, 0.652), (-0.40, 0.655), (-0.56, 0.66)]


def console(car):
    made = []
    bm = bmesh.new()
    rings = []
    for y, zt in CONSOLE_TOP:
        hw = 0.125 if y < 0.45 else lerp(0.125, 0.17, smooth((y - 0.45) / 0.15))
        sec = [(-hw, 0.30), (hw, 0.30), (hw, zt - 0.03), (hw - 0.01, zt - 0.008), (hw - 0.03, zt), (-hw + 0.03, zt),
               (-hw + 0.01, zt - 0.008), (-hw, zt - 0.03)]
        rings.append([bm.verts.new((x, y, z)) for x, z in sec])
    n = 8
    for r0, r1 in zip(rings, rings[1:]):
        for k in range(n):
            bm.faces.new((r0[k], r0[(k + 1) % n], r1[(k + 1) % n], r1[k]))
    for r in (rings[0], rings[-1]):
        bm.faces.new(r)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    made.append(obj('Console_Body', bm, ['Interior_Graphite'], car, local=False, sm=40))
    # silver side strips and two cup holders ahead of the gear lever
    bm = bmesh.new()
    for s in (-1, 1):
        ex.tube(bm, [(s * 0.127, y, zt - 0.045) for y, zt in CONSOLE_TOP[1:-1]], 0.004, 6, 0)
    made.append(obj('Console_Trim', bm, ['Satin_Aluminium'], car, local=False))
    bm = bmesh.new()
    for x in (-0.045, 0.045):
        ring = [(x + 0.036 * math.cos(t), 0.30 + 0.036 * math.sin(t)) for t in [2 * math.pi * i / 24 for i in range(24)]]
        ex.prism(bm, [(u, v, 0.6935) for u, v in ring], (0, 0, 1), 0.0, 0.003)
    made.append(obj('Console_Cupholders', bm, ['Rubber'], car, local=False))
    # armrest (lid of the storage box)
    bm = bmesh.new()
    superbox(bm, (0, -0.37, 0.692), (0.115, 0.16, 0.035), 0.35,
             deform=lambda p: Vector((p.x, p.y, p.z + 0.012 * (p.z > 0) * (1 - (p.x / 0.115) ** 2))))
    made.append(obj('Armrest', bm, ['Leather'], car, local=False))
    return made


# ---------------------------------------------------------------------------------------------- doors
X_CARD = 0.835          # cavity wall (the door's inner skin) below the shoulder
CARD_TOP = 0.856        # above it the window sill (body, x 0.79) takes over


def door_card(car, side, front):
    """Door card of one door. Outline in (y, z) seen from the cabin."""
    s = side
    made = []
    tag = ('Front' if front else 'Rear') + ('_R' if s > 0 else '_L')
    if front:
        yf, yr = 0.535, -0.14
        outline = [(yr, 0.33), (0.95, 0.33), (0.95, 0.72), (0.60, 0.735), (yf, 0.80), (yf, CARD_TOP), (yr, CARD_TOP)]
    else:
        yf, yr = -0.235, -1.07
        outline = [(yf, 0.33), (-0.895, 0.33), (-0.93, 0.47), (-0.99, 0.585), (yr, 0.66), (yr, CARD_TOP), (yf, CARD_TOP)]
    # the card: a 4 cm slab with softened inner edges
    bm = bmesh.new()
    poly = [(s * X_CARD, y, z) for y, z in outline]
    ex.prism(bm, poly, (-s, 0, 0), 0.0, 0.038)
    inner = [e for e in bm.edges if all(abs(v.co.x - s * (X_CARD - 0.038)) < 1e-4 for v in e.verts)]
    bmesh.ops.bevel(bm, geom=inner, offset=0.012, offset_type='OFFSET', segments=3, profile=0.5, affect='EDGES', clamp_overlap=True)
    made.append(obj('Door_Card_' + tag, bm, ['Interior_Graphite'], car, local=False, sm=40))
    xi = s * (X_CARD - 0.038)             # inner face of the card
    ym = (yf + yr) / 2
    span = abs(yf - yr)
    # fabric insert + silver strip above it
    bm = bmesh.new()
    y0, y1 = (yr + 0.05, yf - 0.06) if front else (yf - 0.05, yr + 0.08)
    yc, hy = (y0 + y1) / 2, abs(y1 - y0) / 2
    superbox(bm, (xi - s * 0.004, yc, 0.785), (0.008, hy, 0.055), 0.25)
    made.append(obj('Door_Insert_' + tag, bm, ['Seat_Fabric'], car, local=False))
    bm = bmesh.new()
    ex.tube(bm, [(xi - s * 0.004, yc - hy, 0.846), (xi - s * 0.004, yc + hy, 0.846)], 0.004, 6, 0)
    made.append(obj('Door_Strip_' + tag, bm, ['Satin_Aluminium'], car, local=False))
    # armrest with a pull cup and window switches
    bm = bmesh.new()
    ya, yb = (yr + 0.10, min(yf, 0.62) - 0.02) if front else (yf - 0.06, yr + 0.16)
    yc2, hy2 = (ya + yb) / 2, abs(yb - ya) / 2
    superbox(bm, (xi - s * 0.035, yc2, 0.675), (0.04, hy2, 0.028), 0.35,
             deform=lambda p: Vector((p.x, p.y, p.z + (0.02 * smooth((p.y - hy2 * 0.2) / (hy2 * 0.8)) if front else 0.0))))
    made.append(obj('Door_Armrest_' + tag, bm, ['Leather'], car, local=False))
    bm = bmesh.new()
    cup_y = yc2 + (0.08 if front else -0.06) * (1 if front else 1)
    superbox(bm, (xi - s * 0.012, cup_y, 0.735), (0.012, 0.075, 0.03), 0.4)
    made.append(obj('Door_PullCup_' + tag, bm, ['Rubber'], car, local=False))
    bm = bmesh.new()
    sw_y = yb - 0.05 if front else ya - 0.03
    for k in range(2 if front else 1):
        zs = 0.716 if front else 0.699
        ex.box(bm, (xi - s * 0.035 - 0.017, sw_y - 0.02 - k * 0.05, zs), (xi - s * 0.035 + 0.017, sw_y + 0.02 - k * 0.05, zs + 0.012), 0)
    made.append(obj('WindowSwitch_' + tag, bm, ['Rubber'], car, local=False))
    # inner handle in a chrome surround, towards the front of the card
    bm = bmesh.new()
    hy_ = (yf - 0.13) if front else (yf - 0.10)
    superbox(bm, (xi - s * 0.004, hy_, 0.80), (0.006, 0.055, 0.022), 0.3, mat=1)
    superbox(bm, (xi - s * 0.012, hy_ + 0.01, 0.80), (0.008, 0.04, 0.009), 0.5, mat=0)
    made.append(obj('DoorPull_' + tag, bm, ['Satin_Aluminium', 'Rubber'], car, local=False))
    # map pocket and speaker (front); speaker only (rear)
    bm = bmesh.new()
    if front:
        superbox(bm, (xi - s * 0.02, 0.28, 0.42), (0.02, 0.28, 0.06), 0.25)
    sp_y, sp_z = (0.72, 0.50) if front else (-0.55, 0.47)
    ring = [(sp_y + 0.07 * math.cos(t), sp_z + 0.07 * math.sin(t)) for t in [2 * math.pi * i / 32 for i in range(32)]]
    ex.prism(bm, [(xi, y, z) for y, z in ring], (-s, 0, 0), 0.0, 0.004)
    made.append(obj('DoorSpeaker_' + tag, bm, ['Rubber'], car, local=False))
    bm = bmesh.new()
    ex.tube(bm, [(xi - s * 0.004, y, z) for y, z in ring], 0.004, 6, 0, closed=True)
    for k in range(-2, 3):
        ex.tube(bm, [(xi - s * 0.005, sp_y - 0.055, sp_z + k * 0.02), (xi - s * 0.005, sp_y + 0.055, sp_z + k * 0.02)], 0.0018, 4, 0)
    made.append(obj('Door_SpeakerGrille_' + tag, bm, ['Satin_Aluminium'], car, local=False))
    # rubber window ledge along the belt
    bm = bmesh.new()
    pts = [(s * 0.792, y, ex.belt(y) - 0.004) for y in [lerp(yr, yf, k / 12) for k in range(13)]]
    ex.tube(bm, pts, 0.009, 6, 0)
    made.append(obj('Door_Ledge_' + tag, bm, ['Rubber'], car, local=False))
    return made


# ---------------------------------------------------------------------------------------------- headliner
def roof_inner(y):
    """Inner roof height at y (under the exterior's arched roof)."""
    return ex.roof_z(y) - ex.WALL - 0.006


def headliner(car):
    made = []
    bm = bmesh.new()
    ys = [0.50 - k * 0.05 for k in range(26)]       # 0.50 … -0.75
    rings = []
    for y in ys:
        zt = roof_inner(y)
        hw = ex.xs(zt) - 0.07
        ring = []
        for k in range(17):
            u = -1 + 2 * k / 16
            ring.append(bm.verts.new((u * hw, y, zt - 0.035 * abs(u) ** 6)))
        for k in range(16, -1, -1):
            u = -1 + 2 * k / 16
            ring.append(bm.verts.new((u * hw, y, zt - 0.012 - 0.035 * abs(u) ** 6)))
        rings.append(ring)
    n = len(rings[0])
    for r0, r1 in zip(rings, rings[1:]):
        for k in range(n):
            bm.faces.new((r0[k], r0[(k + 1) % n], r1[(k + 1) % n], r1[k]))
    for r in (rings[0], rings[-1]):
        bm.faces.new(r)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    made.append(obj('Headliner', bm, ['Interior_Stone'], car, local=False, sm=40))
    # grab handles above the passenger door and the rear doors
    bm = bmesh.new()
    for x_s, yc in ((1, 0.10), (1, -0.62), (-1, -0.62)):
        z0 = roof_inner(yc) - 0.03
        x0 = ex.xs(z0) - 0.06
        pts = [(x_s * (x0 - 0.035 * math.sin(math.pi * k / 10)), yc - 0.11 + 0.22 * k / 10, z0 - 0.045 * math.sin(math.pi * k / 10)) for k in range(11)]
        ex.tube(bm, pts, 0.011, 8, 0)
    made.append(obj('GrabHandle', bm, ['Interior_Graphite'], car, local=False))
    return made


# ---------------------------------------------------------------------------------------------- steering wheel
def steering(pivot):
    """Wheel in the pivot's XZ plane, facing the driver (-Y): leather rim with thicker grips at a quarter to three,
    three satin spokes, a rounded airbag cover. The DS badge (Wheel_Badge) is kept."""
    for c in list(pivot.children):
        if c.name.startswith(('Airbag', 'Steering_Rim', 'Steering_Spoke')):
            bpy.data.objects.remove(c, do_unlink=True)
    made = []
    bm = bmesh.new()
    R, n, m = 0.18, 72, 10
    rings = []
    for i in range(n):
        t = 2 * math.pi * i / n
        grip = math.exp(-((math.sin(t)) / 0.35) ** 2)          # thicker at 3 and 9 o'clock
        a_r, b_y = 0.0135 + 0.004 * grip, 0.017 + 0.004 * grip
        ring = []
        for k in range(m):
            q = 2 * math.pi * k / m
            r = R + a_r * math.cos(q)
            ring.append(bm.verts.new((r * math.cos(t), b_y * math.sin(q), r * math.sin(t))))
        rings.append(ring)
    for i in range(n):
        r0, r1 = rings[i], rings[(i + 1) % n]
        for k in range(m):
            bm.faces.new((r0[k], r0[(k + 1) % m], r1[(k + 1) % m], r1[k]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    made.append(obj('Steering_Rim', bm, ['Leather'], pivot, sm=60))
    bm = bmesh.new()
    for sx in (-1, 1):
        superbox(bm, (sx * 0.115, 0.004, -0.018), (0.062, 0.011, 0.02), 0.45, rot=rot_y(sx * 8))
    superbox(bm, (0, 0.004, -0.115), (0.024, 0.011, 0.06), 0.45)
    made.append(obj('Steering_Spoke', bm, ['Satin_Aluminium'], pivot))
    bm = bmesh.new()
    superbox(bm, (0, 0.0, -0.012), (0.072, 0.029, 0.058), 0.42,
             deform=lambda p: Vector((p.x * (1 - 0.18 * smooth(-p.z / 0.058)), p.y, p.z)))
    made.append(obj('Airbag', bm, ['Interior_Graphite'], pivot))
    return made


def rot_y(deg):
    return Matrix.Rotation(math.radians(deg), 3, 'Y')


# ---------------------------------------------------------------------------------------------- main
def sha(p):
    return hashlib.sha256(Path(p).read_bytes()).hexdigest() if Path(p).exists() else None


def backup():
    BACKUP.mkdir(exist_ok=True)
    stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S')
    out = {}
    for src in (BLEND, FBX):
        if src.exists():
            dst = BACKUP / f'{src.stem}_before-interior_{stamp}{src.suffix}'
            shutil.copy2(src, dst)
            out[src.name] = str(dst.relative_to(ROOT))
    return out


def main():
    if not PREVIEW and bpy.data.filepath and Path(bpy.data.filepath).resolve() != BLEND.resolve():
        raise SystemExit('Open ArtSource/DS_Sedan_A.blend, not ' + bpy.data.filepath)
    a.M = {m.name: m for m in bpy.data.materials}
    ensure_material('Seat_Fabric', (.105, .145, .15), .92)
    ensure_material('Seat_Insert', (.23, .30, .30), .94)
    car = bpy.data.objects['DS_Sedan_A']
    report = {'script': 'tools/build_sedan_interior.py', 'utc': datetime.datetime.utcnow().isoformat() + 'Z'}
    if not PREVIEW:
        report['backup'] = backup()
    removed = []
    for o in list(car.children_recursive):
        if o.name not in bpy.data.objects:
            continue
        if o.name.startswith(KEEP):
            continue
        in_seat = o.parent is not None and o.parent.name.startswith(SEAT_EMPTIES) and o.type == 'MESH'
        if in_seat or (o.name.startswith(REPLACED) and o.type in {'MESH', 'CURVE', 'FONT'}):
            removed.append(o.name)
            bpy.data.objects.remove(o, do_unlink=True)
    report['removed'] = sorted(removed)
    made = []
    fronts = [o for o in car.children if o.name.startswith('Seat_Front') and o.type == 'EMPTY']
    for seat in fronts:
        back = next(c for c in seat.children if c.name.startswith('Seat_BackFrame'))
        made += front_seat(seat, back)
    rear = bpy.data.objects['Seat_RearBench']
    rback = next(c for c in rear.children if c.name.startswith('RearBench_Back'))
    made += rear_bench(rear, rback, car)
    made += dashboard(car)
    made += console(car)
    for s in (-1, 1):
        for front in (True, False):
            made += door_card(car, s, front)
    made += headliner(car)
    made += steering(bpy.data.objects['SteeringWheel_Pivot'])
    report['created'] = sorted(o.name for o in made)
    if PREVIEW:
        ex.render_preview(PREVIEW, ARGS[ARGS.index('--views') + 1].split(',') if '--views' in ARGS else None)
        (PREVIEW / 'interior-report.json').write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding='utf-8')
        print('SEDAN_INTERIOR_PREVIEW', len(made), flush=True)
        return
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND))
    a.export('DS_Sedan_A', car)
    report['after'] = {'blend_sha256': sha(BLEND), 'fbx_sha256': sha(FBX)}
    REPORT.parent.mkdir(parents=True, exist_ok=True)
    REPORT.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding='utf-8')
    print('SEDAN_INTERIOR_COMPLETE', len(made), flush=True)


if __name__ == '__main__':
    main()
