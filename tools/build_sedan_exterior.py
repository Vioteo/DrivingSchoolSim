"""Exterior of DS_Sedan_A: a modern B-class sedan in the spirit of the Lada Vesta (proportions and lamp graphics,
no brand logo): arched roof, belt line rising to a high short boot, shoulder line, a concave "X" sweep across the
doors, narrow wrap-around headlamps with chrome strokes forming an X with the grille and fog lamps, wide tail lamps
wrapping onto the rear wings, body-colour bumpers with a black lower intake and diffuser.

Replaces every old exterior part (wings, doors, quarters, hood, trunk, pillar panels and trims, roof, seals, lamp
boxes, grille, bumper facias, wheel-arch liners, undertray, side and rear glass, door mirrors). The cabin, wheels,
windshield, wipers and every name code looks up stay (Wheel_*, Glass_*, MirrorSurface_*, Socket_*, Pedal_*...).

How the body is made: the lower body is a loft of cross-sections along the car; the greenhouse is the intersection of
the glass planes (windshield, rear window, tumblehome sides, a faceted arched roof) with rounded edges. Both are
united with an exact boolean; then the cabin cavity, window openings and wheel arches are cut out. Lamp pockets are
cut with solids projected onto that body (so they wrap round the corners), and lenses, chrome strokes and panel gaps
are projected the same way. Cutters carry their own materials (rubber window reveals, black arch liners...).

Run from the project root (Blender 5, background):
  "C:\\Program Files\\Blender Foundation\\Blender 5.0\\blender.exe" -b ArtSource/DS_Sedan_A.blend --python-exit-code 1 --python tools/build_sedan_exterior.py
Preview without saving (renders to the given folder):
  ... --python tools/build_sedan_exterior.py -- --preview artifacts/visual-review/sedan-exterior

Coordinates as in build_art.py: X right, Y forward (+Y = nose), Z up, metres. Idempotent; the .blend and FBX are
copied to ArtSource_Backup/ first.
"""
import bpy, bmesh, sys, math, json, shutil, hashlib, datetime
from pathlib import Path
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

sys.path.insert(0, str(Path(__file__).parent))
import build_art as a

ROOT = a.ROOT
BLEND = ROOT / 'ArtSource/DS_Sedan_A.blend'
FBX = ROOT / 'Assets/DrivingSchool/Art/DS_Sedan_A.fbx'
BACKUP = ROOT / 'ArtSource_Backup'
REPORT = ROOT / 'artifacts/reports/sedan-exterior.json'
ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
PREVIEW = Path(ARGS[ARGS.index('--preview') + 1]).resolve() if '--preview' in ARGS else None

# Old exterior (and anything this script made before). Prefix match on the object name.
REPLACED = ('FrontWing', 'DoorFront_', 'DoorRear_', 'RearQuarter', 'Hood', 'Trunk', 'BumperFacia', 'Grille',
            'NumberPlate', 'LampUnit', 'DRL', 'Projector', 'Indicator', 'Shell_FrontUpperFacia',
            'Shell_RearUpperFacia', 'Shell_Undertray', 'Shell_Wheelhouse', 'WheelArchLiner', 'PanelGap',
            'Handle_Door', 'A_Pillar', 'B_Pillar', 'C_Pillar', 'Seal_', 'Glass_Front_', 'Glass_Rear',
            'MirrorHousing_', 'MirrorArm_', 'MirrorBase_', 'MirrorRepeater_', 'MirrorRecess_', 'MirrorSurface_L',
            'MirrorSurface_R', 'Glass_Windshield', 'Body_', 'Ext_', 'Headlight', 'Taillight', 'TurnSignal', 'ReverseLight',
            'Diffuser', 'Intake_Lower', 'FrontLamp', 'FogLamp', 'Chrome_')
REPLACED_EXACT = ('Roof',)
KEEP = ('RoofLamp',)
CONTRACT = ('Wheel_FL', 'Wheel_FR', 'Wheel_RL', 'Wheel_RR', 'SteeringWheel_Pivot', 'Pedal_Clutch', 'Pedal_Brake',
            'Pedal_Throttle', 'MirrorSurface_L', 'MirrorSurface_R', 'MirrorSurface_Centre', 'Socket_DriverEye',
            'Seat_RearBench', 'Glass_Windshield', 'Glass_Rear', 'Glass_Front_L', 'Glass_Front_R', 'Glass_Rear_L',
            'Glass_Rear_R')

# ---------------------------------------------------------------------------------------------- dimensions
W0 = 0.872            # half width of the body side between the wheel-arch flares (flares reach ~0.894)
Y_NOSE, Y_TAIL = 2.27, -2.27
R_NOSE, R_TAIL = 0.46, 0.34          # plan-view corner radii: a rounded nose, a softer tail
WHEEL_Y, WHEEL_Z = (1.36, -1.36), 0.34
ARCH_R = 0.388                       # tyre radius 0.327
COWL_Y = 1.30                        # windshield foot: the A-pillar starts right above the front wheel
DECK_Y = -1.62                       # rear window foot / boot lid front
WALL = 0.03                          # greenhouse wall thickness
B_Y = -0.31                          # B-pillar centre (the driver's head is ~12 cm ahead of it)


def smooth(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def lerp(p, q, t):
    return p + (q - p) * t


def belt(y):
    """Window line: high, rising towards the C-pillar."""
    return lerp(0.945, 1.0, smooth((1.2 - y) / 2.6))


def z_top(y):
    """Height of the lower body's upper surface along the car (hood, belt, boot lid)."""
    if y >= COWL_Y:                       # hood: slopes down to a rounded nose over the lamps
        z = lerp(0.955, 0.875, smooth((y - COWL_Y) / (Y_NOSE - 0.12 - COWL_Y)))
        if y > Y_NOSE - 0.25:
            z -= 0.07 * ((y - (Y_NOSE - 0.25)) / 0.25) ** 2
        return z
    if y >= DECK_Y + 0.04:
        return belt(y)
    # boot lid: short high deck with a ducktail lip, rounded trailing edge
    z = lerp(belt(DECK_Y + 0.04), 1.025, smooth((DECK_Y + 0.04 - y) / 0.30))
    if y < Y_TAIL + 0.30:
        z -= 0.10 * ((Y_TAIL + 0.30 - y) / 0.30) ** 2
    return z


def z_bottom(y):
    if y > 0:
        return 0.215 + 0.085 * smooth((y - 1.80) / 0.45)
    return 0.215 + 0.095 * smooth((-y - 1.78) / 0.45)


def half_width(y):
    if y > Y_NOSE - R_NOSE:
        d = y - (Y_NOSE - R_NOSE)
        return W0 - R_NOSE + math.sqrt(max(0.0, R_NOSE ** 2 - d * d))
    if y < Y_TAIL + R_TAIL:
        d = (Y_TAIL + R_TAIL) - y
        return W0 - R_TAIL + math.sqrt(max(0.0, R_TAIL ** 2 - d * d))
    return W0


def shoulder_z(y):
    """Shoulder crease: from the headlamp it rises along the car to the tail lamps."""
    return lerp(0.79, 0.875, smooth((1.9 - y) / 3.9))


def x_crease(y):
    """Upper edge of the concave sweep across the doors (None outside it) and its fade weight."""
    if y > 1.05 or y < -1.25:
        return None, 0.0
    t = (1.0 - y) / 2.2
    w = smooth((1.05 - y) / 0.22) * smooth((y + 1.25) / 0.25)
    return lerp(0.44, 0.74, t), w


def arch_flare(y, z):
    """0..1: how much the wheel-arch flare pushes the skin out at (y, z)."""
    f = 0.0
    for wy in WHEEL_Y:
        r = math.hypot(y - wy, z - WHEEL_Z)
        if z > 0.26:
            f = max(f, smooth((ARCH_R + 0.21 - r) / 0.13))
    return f


def side_offset(z, y):
    """Inward offset of the side surface from the half width at height z (negative = outwards)."""
    zs = shoulder_z(y)
    o = 0.022 * ((z - 0.58) / 0.30) ** 2 if z < 0.88 else 0.022 + (z - 0.88) * 0.1   # barrel-shaped side
    if z > zs:
        o += 0.004 + (z - zs) * 0.15                                                        # tumblehome above the shoulder
    elif z > zs - 0.015:
        o += 0.004 * smooth((z - (zs - 0.015)) / 0.015)
    if z < 0.34:
        o += 0.028 * smooth((0.34 - z) / 0.10)                                              # rocker tucks under
    o -= 0.024 * arch_flare(y, z) * (1.0 - smooth((z - zs + 0.03) / 0.06))                  # flared arches
    zc, w = x_crease(y)
    if zc is not None and w > 0 and z < zc:                                                 # concave X sweep
        o += 0.014 * w * smooth((zc - z) / 0.012) * smooth((z - (zc - 0.26)) / 0.14)
    return o


SIDE_ROWS = [0.30 + 0.015 * i for i in range(46)]      # 0.30 ... 0.975


def section(y, n_top=8):
    """Right half of the cross-section, from the bottom centre over the side to the top centre: [(x, z)]."""
    w, zb, zt = half_width(y), z_bottom(y), z_top(y)
    pts = [(0.0, zb), (w - 0.11, zb), (w - 0.065, zb + 0.004), (w - 0.035, zb + 0.022), (w - 0.018, zb + 0.05)]
    lo, hi, eps = zb + 0.075, zt - 0.05, 0.002
    n = len(SIDE_ROWS)
    for i, t in enumerate(SIDE_ROWS):
        z = min(max(t, lo + (i + 1) * eps), hi - (n - 1 - i) * eps)
        pts.append((w - side_offset(z, y), z))
    # rounded upper edge into the top surface
    xo, zo = pts[-1]
    r = max(0.02, min(0.06, zt - zo))
    cx, cz = xo - r * 0.9, zt - r
    for k in range(1, 7):
        t = k / 6 * math.pi / 2
        pts.append((cx + r * 0.9 * math.cos(t), cz + r * math.sin(t)))
    top_w = cx
    crown = 0.02 if y > COWL_Y or y < DECK_Y else 0.0
    for k in range(1, n_top + 1):
        x = top_w * (1 - k / n_top)
        pts.append((x, zt + crown * (1 - (x / max(top_w, 1e-3)) ** 2)))
    return pts


def stations():
    ys = []
    for k in range(17):
        t = k / 16 * math.pi / 2
        ys.append(Y_TAIL + R_TAIL - R_TAIL * math.cos(t))
    y = Y_TAIL + R_TAIL + 0.035
    while y < Y_NOSE - R_NOSE - 0.02:
        ys.append(y)
        y += 0.04
    for k in range(17):
        t = k / 16 * math.pi / 2
        ys.append(Y_NOSE - R_NOSE + R_NOSE * math.sin(t))
    return sorted(set(round(v, 5) for v in ys))


# ---------------------------------------------------------------------------------------------- bmesh helpers
def new_obj(name, bm, mats, parent=None, smooth_angle=None, weighted=True):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for mn in mats:
        me.materials.append(a.M[mn])
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    if smooth_angle is not None:
        set_smooth(me, smooth_angle)
        if weighted:
            wn = o.modifiers.new('Corner normals', 'WEIGHTED_NORMAL')
            wn.keep_sharp = True
    if parent is not None:
        o.parent = parent
        o.matrix_parent_inverse = parent.matrix_world.inverted()
    return o


def set_smooth(me, angle_deg):
    bm = bmesh.new(); bm.from_mesh(me)
    lim = math.radians(angle_deg)
    for f in bm.faces:
        f.smooth = True
    for e in bm.edges:
        if len(e.link_faces) != 2 or e.calc_face_angle(0) > lim:
            e.smooth = False
    bm.to_mesh(me); bm.free()


def recentre(o):
    c = sum((Vector(v) for v in o.bound_box), Vector()) / 8
    o.data.transform(Matrix.Translation(-c))
    o.location = o.location + c


def loft(bm, rings):
    vr = [[bm.verts.new(p) for p in r] for r in rings]
    n = len(rings[0])
    for r0, r1 in zip(vr, vr[1:]):
        for i in range(n):
            bm.faces.new((r0[i], r0[(i + 1) % n], r1[(i + 1) % n], r1[i]))
    for r in (vr[0], vr[-1]):
        c = bm.verts.new(sum((v.co for v in r), Vector()) / n)
        for i in range(n):
            bm.faces.new((r[i], r[(i + 1) % n], c))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)


def prism(bm, outline, direction, d0, d1, mat=0):
    d = Vector(direction).normalized()
    ra = [bm.verts.new(Vector(p) + d * d0) for p in outline]
    rb = [bm.verts.new(Vector(p) + d * d1) for p in outline]
    n = len(outline)
    fs = [bm.faces.new(ra[::-1]), bm.faces.new(rb)]
    for i in range(n):
        fs.append(bm.faces.new((ra[i], ra[(i + 1) % n], rb[(i + 1) % n], rb[i])))
    for f in fs:
        f.material_index = mat
    bmesh.ops.recalc_face_normals(bm, faces=fs)
    return fs


def cylinder_x(bm, cy, cz, r, x0, x1, seg=48, zmin=None, mat=0):
    pts = []
    for k in range(seg):
        t = 2 * math.pi * k / seg
        z = cz + r * math.sin(t)
        if zmin is not None:
            z = max(z, zmin)
        pts.append((x0, cy + r * math.cos(t), z))
    out = []
    for p in pts:
        if not out or (Vector(p) - Vector(out[-1])).length > 1e-4:
            out.append(p)
    if (Vector(out[0]) - Vector(out[-1])).length < 1e-4:
        out.pop()
    return prism(bm, out, (1, 0, 0), 0.0, x1 - x0, mat)


def box(bm, lo, hi, mat=0):
    x0, y0, z0 = lo; x1, y1, z1 = hi
    return prism(bm, [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0)], (0, 0, 1), 0.0, z1 - z0, mat)


def rounded_rect(u0, v0, u1, v1, r, seg=4):
    r = min(r, (u1 - u0) / 2 - 1e-4, (v1 - v0) / 2 - 1e-4)
    cs = [(u1 - r, v0 + r, -90), (u1 - r, v1 - r, 0), (u0 + r, v1 - r, 90), (u0 + r, v0 + r, 180)]
    pts = []
    for cu, cv, a0 in cs:
        for k in range(seg + 1):
            t = math.radians(a0 + 90 * k / seg)
            pts.append((cu + r * math.cos(t), cv + r * math.sin(t)))
    return pts


def convex_solid(planes, bevel=0.0, bevel_skip=None, seg=4):
    """Intersection of half-spaces n.p <= n.p0 (n outward), optionally with rounded edges."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=8.0)
    for n, p in planes:
        n = Vector(n).normalized()
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=Vector(p), plane_no=n, clear_outer=True, dist=1e-6)
        boundary = [e for e in bm.edges if len(e.link_faces) < 2]
        if boundary:
            bmesh.ops.holes_fill(bm, edges=boundary, sides=0)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(0.5), verts=bm.verts, edges=bm.edges)
    if bevel > 0:
        edges = [e for e in bm.edges if not (bevel_skip and bevel_skip(e.link_faces[0].normal, e.link_faces[1].normal))]
        bmesh.ops.bevel(bm, geom=edges, offset=bevel, offset_type='OFFSET', segments=seg, profile=0.5,
                        affect='EDGES', clamp_overlap=True)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
    return bm


def inset_polygon(pts, normal, d):
    """Shrink a planar polygon (convex corners) by distance d."""
    n = Vector(normal).normalized()
    P = [Vector(p) for p in pts]
    c = sum(P, Vector()) / len(P)
    m = len(P)
    lines = []
    for i in range(m):
        p, q = P[i], P[(i + 1) % m]
        e = (q - p).normalized()
        inward = n.cross(e)
        if inward.dot(c - p) < 0:
            inward = -inward
        lines.append((p + inward * d, e))
    out = []
    for i in range(m):
        (p1, e1), (p2, e2) = lines[i - 1], lines[i]
        w = p2 - p1
        cr = e1.cross(e2)
        s = w.cross(e2).dot(cr) / max(cr.length_squared, 1e-12)
        out.append(p1 + e1 * s)
    return [tuple(v) for v in out]


def sweep(bm, path, profile, up=Vector((0, 0, 1)), mat=0):
    """Sweep a 2D profile [(o, v)] (o along the per-point side vector, v along up) along [(point, side)]."""
    rings = []
    for p, side in path:
        s = Vector(side).normalized()
        rings.append([bm.verts.new(Vector(p) + s * o + up * v) for (o, v) in profile])
    n = len(profile)
    fs = []
    for r0, r1 in zip(rings, rings[1:]):
        for k in range(n):
            fs.append(bm.faces.new((r0[k], r0[(k + 1) % n], r1[(k + 1) % n], r1[k])))
    fs.append(bm.faces.new(rings[0][::-1]))
    fs.append(bm.faces.new(rings[-1]))
    for f in fs:
        f.material_index = mat
    bmesh.ops.recalc_face_normals(bm, faces=fs)
    return fs


def tube(bm, pts, radius, sides=6, mat=0, closed=False):
    P = [Vector(p) for p in pts]
    rings = []
    for i, p in enumerate(P):
        a_ = P[i - 1] if closed else P[max(i - 1, 0)]
        b_ = P[(i + 1) % len(P)] if closed else P[min(i + 1, len(P) - 1)]
        t = (b_ - a_).normalized()
        ref = Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((1, 0, 0))
        u = t.cross(ref).normalized(); v = t.cross(u).normalized()
        rings.append([bm.verts.new(p + (u * math.cos(2 * math.pi * k / sides) + v * math.sin(2 * math.pi * k / sides)) * radius)
                      for k in range(sides)])
    fs = []
    m = len(rings)
    for i in range(m if closed else m - 1):
        r0, r1 = rings[i], rings[(i + 1) % m]
        for k in range(sides):
            fs.append(bm.faces.new((r0[k], r0[(k + 1) % sides], r1[(k + 1) % sides], r1[k])))
    if not closed:
        fs.append(bm.faces.new(rings[0][::-1])); fs.append(bm.faces.new(rings[-1]))
    for f in fs:
        f.material_index = mat
    return fs


# ---------------------------------------------------------------------------------------------- glass planes
def xs(z):
    """Outer side plane of the greenhouse (right side): strong tumblehome from the belt to the roof."""
    return 0.80 - 0.38 * (z - 0.93)


SIDE_N = Vector((1.0, 0.0, 0.38)).normalized()
# Windshield: raked, from the cowl above the front wheel (y 1.28, z 0.955) to the roof (y 0.40, z 1.432).
WS_A, WS_B = Vector((0, 1.28, 0.955)), Vector((0, 0.36, 1.458))
_ws_dir = (WS_B - WS_A).normalized()
WS_N = Vector((0, -_ws_dir.z, _ws_dir.y))
if WS_N.y < 0:
    WS_N = -WS_N
WS_D = WS_N.dot(WS_A)


def y_ws(z):
    return (WS_D - WS_N.z * z) / WS_N.y


# Rear window: raked, from the boot lid to the roof.
RW_A, RW_B = Vector((0, DECK_Y, 0.995)), Vector((0, -0.84, 1.442))
_rw_dir = (RW_B - RW_A).normalized()
RW_N = Vector((0, -_rw_dir.z, _rw_dir.y)).normalized()
if RW_N.y > 0:
    RW_N = -RW_N


def y_rw(z):
    return lerp(RW_A.y, RW_B.y, (z - RW_A.z) / (RW_B.z - RW_A.z))


# Arched roof: z = ROOF_PEAK - ROOF_K (y - ROOF_Y0)^2, built from tangent planes.
ROOF_PEAK, ROOF_Y0 = 1.49, -0.17
ROOF_K = (ROOF_PEAK - (WS_B.z + 0.004)) / (WS_B.y - ROOF_Y0) ** 2


def roof_z(y):
    return ROOF_PEAK - ROOF_K * (y - ROOF_Y0) ** 2


def roof_planes(o):
    out = []
    for y in (0.42, 0.26, 0.10, -0.05, -0.17, -0.30, -0.46, -0.62, -0.82):
        dz = -2 * ROOF_K * (y - ROOF_Y0)
        n = Vector((0, -dz, 1.0)).normalized()
        out.append((n, Vector((0, y, roof_z(y))) + n * o))
    return out


def windshield_quad():
    zb, zt = 0.955, WS_B.z - 0.004
    return [(-(xs(zb) - 0.075), y_ws(zb), zb), (xs(zb) - 0.075, y_ws(zb), zb),
            (xs(zt) - 0.07, y_ws(zt), zt), (-(xs(zt) - 0.07), y_ws(zt), zt)]


def side_glass_outlines():
    """Door glass outlines (y, z) of the right side."""
    zt = 1.42
    yf = y_ws(0.95) - 0.07
    front = [(yf, belt(yf) - 0.02), (y_ws(zt) - 0.06, zt), (B_Y + 0.045, zt), (B_Y + 0.045, belt(B_Y + 0.045) - 0.02)]
    yr = -1.08
    rear = [(B_Y - 0.045, belt(B_Y - 0.045) - 0.02), (B_Y - 0.045, zt), (-0.72, zt), (yr, belt(yr) - 0.02)]
    return front, rear


def on_side(yz, side, inset=0.006):
    y, z = yz
    return (side * (xs(z) - inset / SIDE_N.x), y, z)


# ---------------------------------------------------------------------------------------------- body
def lower_body():
    bm = bmesh.new()
    rings = []
    for y in stations():
        half = section(y)
        right = [(x, y, z) for x, z in half]
        left = [(-x, y, z) for x, z in half[1:-1]][::-1]
        rings.append(right + left)
    loft(bm, rings)
    return bm


def gh_planes(o, floor):
    return [
        (WS_N, WS_A + WS_N * (0.006 + o)),
        (RW_N, RW_A + RW_N * (0.006 + o)),
        (SIDE_N, Vector((xs(0.93), 0, 0.93)) + SIDE_N * o),
        (Vector((-SIDE_N.x, 0, SIDE_N.z)), Vector((-xs(0.93), 0, 0.93)) + Vector((-SIDE_N.x, 0, SIDE_N.z)) * o),
        (Vector((0, 0, -1)), Vector((0, 0, floor))),
    ] + roof_planes(o)


def greenhouse():
    skip = lambda na, nb: na.z < -0.99 or nb.z < -0.99 or (na.z > 0.95 and nb.z > 0.95)
    return convex_solid(gh_planes(0.0, 0.925), bevel=0.05, bevel_skip=skip, seg=6)


def cabin_cavity():
    """The cabin in three convex pieces: below the shoulder (to the door skins), the band up to the belt
    (window sills), and the greenhouse."""
    ends = [(Vector((0, 1, 0)), Vector((0, 1.04, 0))), (Vector((0, -1, 0)), Vector((0, -1.36, 0)))]
    glass = [(WS_N, WS_A + WS_N * (0.006 - WALL)), (RW_N, RW_A + RW_N * (0.006 - WALL))]
    def piece(half, z0, z1, more=()):
        return [(Vector((1, 0, 0)), Vector((half, 0, 0))), (Vector((-1, 0, 0)), Vector((-half, 0, 0))),
                (Vector((0, 0, -1)), Vector((0, 0, z0))), (Vector((0, 0, 1)), Vector((0, 0, z1)))] + ends + glass + list(more)
    out = []
    for planes in (piece(0.835, 0.29, 0.86), piece(0.79, 0.85, 0.95),
                   gh_planes(-WALL, 0.90) + [(Vector((0, -1, 0)), Vector((0, -1.60, 0)))]):
        out.append(convex_solid(planes))
    return out


def cowl_cavity():
    """Space under the raked windshield, above the cowl top (the dashboard's top pad sits in it)."""
    extra = [(Vector((1, 0, 0)), Vector((0.80, 0, 0))), (Vector((-1, 0, 0)), Vector((-0.80, 0, 0))),
             (Vector((0, -1, 0)), Vector((0, 0.9, 0)))]
    return convex_solid(gh_planes(-WALL, belt(1.1) + 0.004) + extra)


def rear_glass_quad():
    zb, zt = 1.003, RW_B.z - 0.006
    ya, yb = y_rw(zb), y_rw(zt)
    xa, xb = xs(zb) - 0.075, xs(zt) - 0.065
    return [(-xa, ya, zb), (xa, ya, zb), (xb, yb, zt), (-xb, yb, zt)]


def window_cutters(bm):
    prism(bm, inset_polygon(windshield_quad(), WS_N, 0.022), WS_N, -0.2, 0.2)
    prism(bm, inset_polygon(rear_glass_quad(), RW_N, 0.024), RW_N, -0.2, 0.2)
    front, rear = side_glass_outlines()
    for side in (-1, 1):
        n = Vector((side * SIDE_N.x, 0, SIDE_N.z))
        for yz in (front, rear):
            poly = [on_side(p, side, 0.0) for p in yz]
            ins = inset_polygon(poly, n, 0.016)
            ins = [(x, y, max(z, belt(y) + 0.010)) for x, y, z in ins]
            prism(bm, ins, n, -0.15, 0.15)


def arch_cutters(bm):
    for wy in WHEEL_Y:
        for side in (-1, 1):
            x0, x1 = (0.60, 1.3) if side > 0 else (-1.3, -0.60)
            cylinder_x(bm, wy, WHEEL_Z, ARCH_R, x0, x1)


def wheel_tubs(bm):
    for wy in WHEEL_Y:
        for side in (-1, 1):
            x0, x1 = (0.575, 0.87) if side > 0 else (-0.87, -0.575)
            cylinder_x(bm, wy, WHEEL_Z, ARCH_R + 0.045, x0, x1, zmin=0.235)


def bool_apply(target, cutter_objs, op, name):
    coll = bpy.data.collections.new('ExtCut_' + name)
    bpy.context.scene.collection.children.link(coll)
    for c in cutter_objs:
        for uc in list(c.users_collection):
            uc.objects.unlink(c)
        coll.objects.link(c)
    mod = target.modifiers.new(name, 'BOOLEAN')
    mod.operation = op
    mod.solver = 'EXACT'
    mod.operand_type = 'COLLECTION'
    mod.collection = coll
    if hasattr(mod, 'material_mode'):
        mod.material_mode = 'TRANSFER'
    deps = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(target.evaluated_get(deps), preserve_all_data_layers=True, depsgraph=deps)
    target.modifiers.remove(mod)
    old = target.data
    target.data = me
    bpy.data.meshes.remove(old)
    for c in cutter_objs:
        me_c = c.data
        bpy.data.objects.remove(c, do_unlink=True)
        bpy.data.meshes.remove(me_c)
    bpy.data.collections.remove(coll)


def cutter_obj(name, bm, mat_name):
    for f in bm.faces:
        f.material_index = 0
    return new_obj(name, bm, [mat_name])


# ---------------------------------------------------------------------------------------------- surface queries
class Surface:
    def __init__(self, obj):
        deps = bpy.context.evaluated_depsgraph_get()
        self.bvh = BVHTree.FromObject(obj, deps)

    def hit(self, origin, direction, max_d=4.0):
        loc, nor, idx, dist = self.bvh.ray_cast(Vector(origin), Vector(direction).normalized(), max_d)
        if loc is None:
            return None, None
        return loc, nor

    def side(self, side, y, z):
        return self.hit((side * 1.6, y, z), (-side, 0, 0))

    def end(self, front, x, z):
        return self.hit((x, 3.5 if front else -3.5, z), (0, -1 if front else 1, 0))


class Strip:
    """A lamp-like region on the nose or tail seen from the front/back: x from x0 to x1, between bottom(u) and
    top(u) heights (u = 0..1 along x). Projected onto the body along Y it wraps round the corners."""

    def __init__(self, x0, x1, bottom, top, front=True):
        self.x0, self.x1, self.bottom, self.top, self.front = x0, x1, bottom, top, front

    def grid(self, surf, nu=28, nv=6, side=1, inset=0.0, u_range=(0.0, 1.0), v_range=(0.0, 1.0)):
        """Rows of projected points (point, normal) — None where the probe misses."""
        rows = []
        for j in range(nv + 1):
            v = lerp(v_range[0], v_range[1], j / nv)
            row = []
            for i in range(nu + 1):
                u = lerp(u_range[0], u_range[1], i / nu)
                x = lerp(self.x0, self.x1, u)
                z = lerp(self.bottom(u), self.top(u), v)
                loc, nor = surf.end(self.front, side * x, z)
                row.append(None if loc is None else (loc - nor * inset, nor))
            rows.append(row)
        return rows


def patch_mesh(bm, rows, thickness=0.0, mat=0, flip=False):
    """Surface patch from a grid of (point, normal); with thickness it becomes a closed slab (for cutters)."""
    if any(p is None for r in rows for p in r):
        rows = [[p for p in r if p is not None] for r in rows]
        m = min(len(r) for r in rows)
        rows = [r[:m] for r in rows]
    top = [[bm.verts.new(p) for p, n in r] for r in rows]
    fs = []
    nu, nv = len(rows[0]), len(rows)
    for j in range(nv - 1):
        for i in range(nu - 1):
            q = (top[j][i], top[j][i + 1], top[j + 1][i + 1], top[j + 1][i])
            fs.append(bm.faces.new(q[::-1] if flip else q))
    if thickness:
        bot = [[bm.verts.new(p - n * thickness) for p, n in r] for r in rows]
        for j in range(nv - 1):
            for i in range(nu - 1):
                fs.append(bm.faces.new((bot[j][i], bot[j + 1][i], bot[j + 1][i + 1], bot[j][i + 1])))
        ring_t = [top[0][i] for i in range(nu)] + [top[j][nu - 1] for j in range(1, nv)] + \
                 [top[nv - 1][i] for i in range(nu - 2, -1, -1)] + [top[j][0] for j in range(nv - 2, 0, -1)]
        ring_b = [bot[0][i] for i in range(nu)] + [bot[j][nu - 1] for j in range(1, nv)] + \
                 [bot[nv - 1][i] for i in range(nu - 2, -1, -1)] + [bot[j][0] for j in range(nv - 2, 0, -1)]
        k = len(ring_t)
        for i in range(k):
            fs.append(bm.faces.new((ring_t[i], ring_t[(i + 1) % k], ring_b[(i + 1) % k], ring_b[i])))
        bmesh.ops.recalc_face_normals(bm, faces=fs)
    for f in fs:
        f.material_index = mat
    return fs


def pocket_rows(strip, surf, side, out=0.04, depth=0.045, nu=28, nv=6):
    """Cutter rows: the projected region raised `out` above the skin; slab thickness out+depth."""
    rows = strip.grid(surf, nu, nv, side)
    return [[None if p is None else (p[0] + p[1] * out, p[1]) for p in r] for r in rows], out + depth


# Lamp graphics (right side; x across the car, z height). Headlamp: narrow, rising outwards and wrapping onto the
# wing. Tail lamp: wide, following the shoulder, wrapping onto the rear wing.
def _pw(keys):
    def f(u):
        for (u0, z0), (u1, z1) in zip(keys, keys[1:]):
            if u <= u1:
                return lerp(z0, z1, (u - u0) / (u1 - u0))
        return keys[-1][1]
    return f


HEAD = Strip(0.28, 0.86, _pw([(0, 0.622), (0.4, 0.61), (0.8, 0.622), (1.0, 0.665)]),
             _pw([(0, 0.722), (0.12, 0.752), (0.7, 0.768), (1.0, 0.758)]))
GRILLE = Strip(-0.265, 0.265, _pw([(0, 0.596), (0.5, 0.585), (1, 0.596)]), _pw([(0, 0.70), (0.5, 0.715), (1, 0.70)]))
INTAKE = Strip(-0.52, 0.52, _pw([(0, 0.36), (0.2, 0.33), (0.8, 0.33), (1, 0.36)]),
               _pw([(0, 0.49), (0.28, 0.535), (0.72, 0.535), (1, 0.49)]))
FOG = Strip(0.60, 0.73, _pw([(0, 0.40), (0.5, 0.37), (1, 0.39)]), _pw([(0, 0.43), (0.5, 0.46), (1, 0.44)]))
TAIL = Strip(0.30, 0.88, _pw([(0, 0.765), (0.5, 0.755), (0.85, 0.775), (1.0, 0.80)]),
             _pw([(0, 0.895), (0.25, 0.92), (0.8, 0.925), (1.0, 0.91)]), front=False)
DIFFUSER = Strip(-0.62, 0.62, _pw([(0, 0.31), (1, 0.31)]), _pw([(0, 0.40), (0.5, 0.415), (1, 0.40)]), front=False)
PLATE_REAR_Z = (0.636, 0.748)


def lamp_cutters(surf):
    """Pocket solids projected onto the body: (name, bmesh, material)."""
    out = []
    for name, strip, sides, mat, depth in (('head', HEAD, (-1, 1), 'Interior_Graphite', 0.05), ('grille', GRILLE, (1,), 'Rubber', 0.05),
                                           ('intake', INTAKE, (1,), 'Rubber', 0.06), ('fog', FOG, (-1, 1), 'Rubber', 0.03),
                                           ('tail', TAIL, (-1, 1), 'Rubber', 0.03), ('diffuser', DIFFUSER, (1,), 'Interior_Graphite', 0.012)):
        bm = bmesh.new()
        for s in sides:
            rows, th = pocket_rows(strip, surf, s, depth=depth)
            patch_mesh(bm, rows, thickness=th)
        out.append((name, bm, mat))
    # rear plate recess, flat on the boot lid
    bm = bmesh.new()
    prism(bm, [(x, Y_TAIL, z) for x, z in rounded_rect(-0.27, PLATE_REAR_Z[0], 0.27, PLATE_REAR_Z[1], 0.012)], (0, -1, 0), -0.015, 0.3)
    out.append(('plate', bm, 'Interior_Graphite'))
    return out


def build_shell(root):
    body = new_obj('Body_Shell', lower_body(), ['Paint_Atlantic'])
    gh = new_obj('Ext_Greenhouse', greenhouse(), ['Paint_Atlantic'])
    bool_apply(body, [gh], 'UNION', 'greenhouse')
    bool_apply(body, [cutter_obj('Ext_Cavity%d' % i, bm, 'Interior_Stone') for i, bm in enumerate(cabin_cavity())] +
               [cutter_obj('Ext_CowlCavity', cowl_cavity(), 'Interior_Graphite')], 'DIFFERENCE', 'cavity')
    bm = bmesh.new(); wheel_tubs(bm)
    bool_apply(body, [cutter_obj('Ext_Tubs', bm, 'Rubber')], 'UNION', 'tubs')
    cut = []
    bm = bmesh.new(); arch_cutters(bm); cut.append(cutter_obj('Ext_Arches', bm, 'Rubber'))
    bm = bmesh.new(); window_cutters(bm); cut.append(cutter_obj('Ext_Windows', bm, 'Rubber'))
    bool_apply(body, cut, 'DIFFERENCE', 'openings')
    # lamp pockets are projected onto the finished skin
    surf = Surface(body)
    cut = [cutter_obj('Ext_Pocket_' + n, bm, m) for n, bm, m in lamp_cutters(surf)]
    bool_apply(body, cut, 'DIFFERENCE', 'pockets')
    bm = bmesh.new(); bm.from_mesh(body.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.dissolve_degenerate(bm, edges=bm.edges, dist=1e-6)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
    bm.to_mesh(body.data); bm.free()
    set_smooth(body.data, 38)
    wn = body.modifiers.new('Corner normals', 'WEIGHTED_NORMAL'); wn.keep_sharp = True
    body.parent = root
    body.matrix_parent_inverse = root.matrix_world.inverted()
    return body, surf


# ---------------------------------------------------------------------------------------------- projected details
def project_line(surf, probes, lift=0.0009):
    out = []
    for origin, direction in probes:
        loc, nor = surf.hit(origin, direction)
        if loc is not None:
            out.append(loc + nor * lift)
    return out


def dense(poly, step=0.015):
    out = []
    for p, q in zip(poly, poly[1:]):
        p, q = Vector(p), Vector(q)
        n = max(1, int((q - p).length / step))
        for k in range(n):
            out.append(p.lerp(q, k / n))
    out.append(Vector(poly[-1]))
    return out


def arc(cy, cz, r, a0, a1, step_deg=4):
    n = max(2, int(abs(a1 - a0) / step_deg))
    return [(cy + r * math.cos(math.radians(a0 + (a1 - a0) * k / n)), cz + r * math.sin(math.radians(a0 + (a1 - a0) * k / n)))
            for k in range(n + 1)]


def side_probes(s, yz):
    return [((s * 1.6, p.x, p.y), (-s, 0, 0)) for p in dense([(y, z, 0.0) for y, z in yz])]


def top_probes(xy):
    return [((p.x, p.y, 2.2), (0, 0, -1)) for p in dense([(x, y, 0.0) for x, y in xy])]


def end_probes(front, xz):
    return [((p.x, 3.5 if front else -3.5, p.y), (0, -1 if front else 1, 0)) for p in dense([(x, z, 0.0) for x, z in xz])]


def panel_gaps(root, surf):
    bm = bmesh.new()
    lines = []
    for s in (-1, 1):
        # doors
        ya = y_ws(belt(1.2)) - 0.075
        lines.append(side_probes(s, [(ya, belt(ya) - 0.028), (1.02, 0.80), (0.975, 0.62), (0.905, 0.44), (0.895, 0.30)]))
        lines.append(side_probes(s, [(B_Y, belt(B_Y) - 0.03), (B_Y, 0.30)]))
        lines.append(side_probes(s, [(0.895, 0.30), (B_Y, 0.30)]))
        rear = [(-1.085, belt(-1.085) - 0.03), (-1.095, 0.80)] + arc(-1.36, WHEEL_Z, ARCH_R + 0.055, 56, 12) + [(-0.89, 0.30)]
        lines.append(side_probes(s, rear))
        lines.append(side_probes(s, [(-0.89, 0.30), (B_Y, 0.30)]))
        # front bumper / wing joint behind the headlamp, down to the arch
        lines.append(side_probes(s, [(1.93, 0.735), (1.86, 0.64), (1.80, 0.56)] + [(1.36 + (ARCH_R + 0.04) * math.cos(math.radians(a_)), WHEEL_Z + (ARCH_R + 0.04) * math.sin(math.radians(a_))) for a_ in (38, 30)]))
        # rear bumper / wing joint under the tail lamp
        lines.append(side_probes(s, [(-1.92, 0.76), (-1.85, 0.62)] + [(-1.36 - (ARCH_R + 0.04) * math.cos(math.radians(a_)), WHEEL_Z + (ARCH_R + 0.04) * math.sin(math.radians(a_))) for a_ in (32, 26)]))
        # hood sides and boot lid sides (from above)
        lines.append(top_probes([(s * 0.70, COWL_Y + 0.035), (s * 0.70, Y_NOSE - 0.33), (s * 0.60, Y_NOSE - 0.17)]))
        lines.append(top_probes([(s * 0.66, DECK_Y - 0.035), (s * 0.66, -1.98)]))
    # fuel flap on the right rear wing
    lines.append(side_probes(1, [(-1.80 + 0.052 * math.cos(t), 0.70 + 0.052 * math.sin(t)) for t in [k / 28 * 2 * math.pi for k in range(29)]]))
    # hood rear edge, hood leading edge over the lamps and the grille
    lines.append(top_probes([(-0.70, COWL_Y + 0.035), (0.70, COWL_Y + 0.035)]))
    lines.append(top_probes([(-0.60, Y_NOSE - 0.17), (-0.3, Y_NOSE - 0.105), (0.3, Y_NOSE - 0.105), (0.60, Y_NOSE - 0.17)]))
    # front bumper upper joint under the lamps and grille
    lines.append(end_probes(True, [(-0.86, 0.64), (-0.53, 0.593), (-0.30, 0.59), (-0.275, 0.575), (0.275, 0.575), (0.30, 0.59), (0.53, 0.593), (0.86, 0.64)]))
    # boot lid front edge and its lower edge (runs below the plate recess, climbs to the lamps)
    lines.append(top_probes([(-0.66, DECK_Y - 0.035), (0.66, DECK_Y - 0.035)]))
    lines.append(end_probes(False, [(-0.29, 0.765), (-0.285, 0.62), (0.285, 0.62), (0.29, 0.765)]))
    # rear bumper upper joint
    lines.append(end_probes(False, [(-0.86, 0.59), (-0.4, 0.575), (0.4, 0.575), (0.86, 0.59)]))
    n = 0
    for probes in lines:
        pts = project_line(surf, probes)
        if len(pts) >= 2:
            tube(bm, pts, 0.0026, 5, 0)
            n += len(pts)
    o = new_obj('PanelGap_Body', bm, ['Rubber'], root, smooth_angle=60, weighted=False)
    recentre(o)
    return o, n


def ribbon(bm, surf, front, xz, width, lift=0.002, step=0.01, mat=0):
    """A flat band of the given width lying on the nose/tail surface along a polyline seen from the front."""
    pts = dense([(x, z, 0.0) for x, z in xz], step)
    rows = [[], []]
    for i, p in enumerate(pts):
        q0, q1 = pts[max(i - 1, 0)], pts[min(i + 1, len(pts) - 1)]
        t = Vector((q1.x - q0.x, q1.y - q0.y)).normalized()
        nrm = Vector((-t.y, t.x))
        for k, sgn in enumerate((-1, 1)):
            x, z = p.x + nrm.x * sgn * width / 2, p.y + nrm.y * sgn * width / 2
            loc, n = surf.end(front, x, z)
            rows[k].append(None if loc is None else (loc + n * lift, n))
    rows = [[r0, r1] for r0, r1 in zip(rows[0], rows[1]) if r0 is not None and r1 is not None]
    if len(rows) >= 2:
        patch_mesh(bm, rows, thickness=0.004, mat=mat)


def chrome_strokes(root, surf):
    """Chrome strokes that make an X with the grille: from the headlamp's inner end down and out to the fog lamp;
    a chrome bar along the grille and the boot lid."""
    bm = bmesh.new()
    for s in (-1, 1):
        stroke = [(s * 0.272, 0.615), (s * 0.32, 0.575), (s * 0.41, 0.52), (s * 0.51, 0.48), (s * 0.585, 0.455), (s * 0.60, 0.40), (s * 0.63, 0.35)]
        ribbon(bm, surf, True, stroke, 0.032, lift=0.0025)
    for zz in (0.625, 0.65, 0.675):
        pts = project_line(surf, end_probes(True, [(-0.25, zz), (0.25, zz)]), lift=-0.012)
        tube(bm, pts, 0.005, 8, 0)
    pts = project_line(surf, end_probes(False, [(-0.28, 0.83), (0.28, 0.83)]), lift=0.003)
    tube(bm, pts, 0.005, 8, 0)
    o = new_obj('Chrome_Strokes', bm, ['Chrome'], root, smooth_angle=50)
    recentre(o)
    return o


def arch_lips(root, surf):
    bm = bmesh.new()
    for wy in WHEEL_Y:
        for s in (-1, 1):
            path = []
            for ang in [-2 + k * 4 for k in range(47)]:
                y = wy + (ARCH_R + 0.004) * math.cos(math.radians(ang))
                z = WHEEL_Z + (ARCH_R + 0.004) * math.sin(math.radians(ang))
                if z < z_bottom(y) + 0.01:
                    continue
                loc, nor = surf.side(s, y, z)
                if loc is None:
                    continue
                radial = Vector((0, math.cos(math.radians(ang)), math.sin(math.radians(ang))))
                path.append((loc + Vector((-s * 0.004, 0, 0)), radial))
            if len(path) < 3:
                continue
            rings = []
            for p, radial in path:
                out = Vector((s, 0, 0))
                ring = [p + out * o_ + radial * r_ for o_, r_ in [(-0.004, -0.004), (0.010, 0.0), (0.016, 0.014), (0.009, 0.034), (-0.004, 0.040)]]
                rings.append([bm.verts.new(v) for v in ring])
            for r0, r1 in zip(rings, rings[1:]):
                for k in range(4):
                    bm.faces.new((r0[k], r0[k + 1], r1[k + 1], r1[k]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = new_obj('Body_ArchLips', bm, ['Paint_Atlantic'], root, smooth_angle=50)
    recentre(o)
    return o


def sills(root, surf):
    """Black sill mouldings between the arches."""
    bm = bmesh.new()
    for s in (-1, 1):
        path = []
        for k in range(40):
            y = lerp(0.93, -0.93, k / 39)
            loc, nor = surf.side(s, y, 0.27)
            if loc is not None:
                path.append((Vector((loc.x, y, 0.0)), Vector((s, 0, 0))))
        prof = [(-0.03, 0.215), (0.004, 0.212), (0.012, 0.235), (0.010, 0.275), (0.0, 0.290), (-0.03, 0.29)]
        sweep(bm, path, prof)
    o = new_obj('Body_Sills', bm, ['Interior_Graphite'], root, smooth_angle=40)
    recentre(o)
    return o


def door_handles(root, surf):
    made = []
    for s in (-1, 1):
        for tag, yc in (('Front', B_Y + 0.10), ('Rear', -1.00)):
            zc = shoulder_z(yc) - 0.035
            loc, nor = surf.side(s, yc, zc)
            if loc is None:
                continue
            bm = bmesh.new()
            prism(bm, [(loc.x - s * 0.002, y, z) for y, z in rounded_rect(yc - 0.075, zc - 0.022, yc + 0.075, zc + 0.022, 0.014)],
                  (s, 0, 0), 0.0, 0.004, 1)
            prism(bm, [(loc.x, y, z) for y, z in rounded_rect(yc - 0.07, zc - 0.012, yc + 0.07, zc + 0.012, 0.011)], (s, 0, 0), 0.004, 0.02, 0)
            o = new_obj(f'Handle_Door{tag}_' + ('R' if s > 0 else 'L'), bm, ['Paint_Atlantic', 'Rubber'], root, smooth_angle=40)
            recentre(o)
            made.append(o)
    return made


def b_pillar_trim(root):
    bm = bmesh.new()
    for s in (-1, 1):
        yz = [(B_Y + 0.05, belt(B_Y + 0.05) + 0.004), (B_Y - 0.05, belt(B_Y - 0.05) + 0.004), (B_Y - 0.05, 1.46), (B_Y + 0.05, 1.46)]
        poly = [(s * (xs(z) + 0.0015 / SIDE_N.x), y, z) for y, z in yz]
        prism(bm, poly, (s * SIDE_N.x, 0, SIDE_N.z), 0.0, 0.002)
    o = new_obj('Body_BPillarTrim', bm, ['Rubber'], root, smooth_angle=30, weighted=False)
    recentre(o)
    return o


def glass(root):
    made = []
    front, rear = side_glass_outlines()
    for s, tag in ((1, 'R'), (-1, 'L')):
        for name, yz in (('Glass_Front_', front), ('Glass_Rear_', rear)):
            bm = bmesh.new()
            vs = [bm.verts.new(on_side(p, s)) for p in yz]
            f = bm.faces.new(vs)
            f.normal_update()
            if f.normal.x * s < 0:
                f.normal_flip()
            made.append(new_obj(name + tag, bm, ['Glass'], root))
    bm = bmesh.new()
    f = bm.faces.new([bm.verts.new(p) for p in windshield_quad()])
    f.normal_update()
    if f.normal.dot(WS_N) < 0:
        f.normal_flip()
    made.append(new_obj('Glass_Windshield', bm, ['Glass'], root))
    bm = bmesh.new()
    q = [Vector(p) - RW_N * 0.0 for p in rear_glass_quad()]
    f = bm.faces.new([bm.verts.new(p) for p in q])
    f.normal_update()
    if f.normal.dot(RW_N) < 0:
        f.normal_flip()
    made.append(new_obj('Glass_Rear', bm, ['Glass'], root))
    return made


def lamps(root, surf):
    made = []

    def add(name, bm, mats, sm=40):
        o = new_obj(name, bm, mats, root, smooth_angle=sm)
        recentre(o); made.append(o)
        return o

    for s, tag in ((-1, 'L'), (1, 'R')):
        # headlamp: chrome reflector at the back of the pocket, LED daytime strip along the top, two projector
        # lenses, clear cover following the skin
        bm = bmesh.new()
        patch_mesh(bm, HEAD.grid(surf, 28, 5, s, inset=0.046), mat=0)
        add('FrontLampReflector_' + tag, bm, ['Interior_Graphite'])
        bm = bmesh.new()
        patch_mesh(bm, HEAD.grid(surf, 28, 1, s, inset=0.02, u_range=(0.08, 0.97), v_range=(0.80, 0.93)), thickness=0.006, mat=0)
        for uc in (0.30, 0.52):
            x = s * lerp(HEAD.x0, HEAD.x1, uc)
            zc = (HEAD.bottom(uc) + HEAD.top(uc)) / 2 - 0.006
            loc, nor = surf.end(True, x, zc)
            if loc is None:
                continue
            c = loc - Vector((0, 0.035, 0))
            ring_o = [(c.x + 0.026 * math.cos(t), c.z + 0.026 * math.sin(t)) for t in [2 * math.pi * i / 24 for i in range(24)]]
            prism(bm, [(x_, c.y - 0.006, z_) for x_, z_ in ring_o], (0, 1, 0), 0.0, 0.006, 1)
            ring_i = [(c.x + 0.019 * math.cos(t), c.z + 0.019 * math.sin(t)) for t in [2 * math.pi * i / 24 for i in range(24)]]
            prism(bm, [(x_, c.y, z_) for x_, z_ in ring_i], (0, 1, 0), 0.0, 0.01, 0)
        add('FrontLamp_' + tag, bm, ['Lamp_White', 'Chrome'])
        bm = bmesh.new()
        patch_mesh(bm, HEAD.grid(surf, 28, 5, s, inset=0.004), mat=0)
        add('FrontLampCover_' + tag, bm, ['Glass'])
        # front indicator: amber segment at the outer end of the headlamp, under the cover
        bm = bmesh.new()
        patch_mesh(bm, HEAD.grid(surf, 8, 2, s, inset=0.012, u_range=(0.86, 0.985), v_range=(0.12, 0.75)), thickness=0.004)
        add('Indicator_Front_' + tag, bm, ['Lamp_Amber'])
        # fog lamp: chrome-ringed round lens in the black pocket
        bm = bmesh.new()
        xc = s * (FOG.x0 + FOG.x1) / 2
        loc, nor = surf.end(True, xc, 0.41)
        if loc is not None:
            c = loc - Vector((0, 0.02, 0))
            ring = [(c.x + 0.036 * math.cos(t), c.z + 0.03 * math.sin(t)) for t in [2 * math.pi * i / 28 for i in range(28)]]
            prism(bm, [(x_, c.y, z_) for x_, z_ in ring], (0, 1, 0), 0.0, 0.012, 0)
            add('FogLamp_' + tag, bm, ['Satin_Aluminium'])
        # tail lamp: red body, amber indicator band and white reverse segment, all following the skin
        bm = bmesh.new()
        patch_mesh(bm, TAIL.grid(surf, 30, 5, s, inset=0.003, u_range=(0.36, 1.0)), thickness=0.004)
        add('Taillight_' + tag, bm, ['Lamp_Red'])
        bm = bmesh.new()
        patch_mesh(bm, TAIL.grid(surf, 8, 4, s, inset=0.003, u_range=(0.18, 0.345)), thickness=0.004)
        add('TurnSignal_Rear_' + tag, bm, ['Lamp_Amber'])
        bm = bmesh.new()
        patch_mesh(bm, TAIL.grid(surf, 8, 4, s, inset=0.003, u_range=(0.0, 0.165)), thickness=0.004)
        add('ReverseLight_' + tag, bm, ['Lamp_White'])

    # grille: black pocket + chrome bar (strokes); intake: horizontal slats
    bm = bmesh.new()
    for k in range(4):
        v = (k + 0.5) / 4
        pts = [r[0] for r in INTAKE.grid(surf, 30, 1, 1, inset=0.03, u_range=(0.03, 0.97), v_range=(v, v))[0] if r is not None]
        if len(pts) > 2:
            tube(bm, pts, 0.006, 6, 0)
    for k in range(3):
        v = (k + 0.5) / 3
        pts = [r[0] for r in GRILLE.grid(surf, 24, 1, 1, inset=0.028, u_range=(0.03, 0.97), v_range=(v, v))[0] if r is not None]
        if len(pts) > 2:
            tube(bm, pts, 0.004, 6, 0)
    add('Grille_Slats', bm, ['Interior_Graphite'], 60)
    # centre badge (a plain oval, no brand)
    bm = bmesh.new()
    loc, nor = surf.end(True, 0.0, 0.74)
    if loc is not None:
        badge = [(0.045 * math.cos(t), 0.74 + 0.028 * math.sin(t)) for t in [2 * math.pi * i / 32 for i in range(32)]]
        prism(bm, [(x, loc.y - 0.01, z) for x, z in badge], (0, 1, 0), 0.0, 0.022, 0)
        add('Grille_Badge', bm, ['Chrome'])
    return made


def plates(root, surf):
    made = []
    # front plate on the intake, rear plate in the boot lid recess
    loc, nor = surf.end(True, 0.0, 0.415)
    yf = (loc.y if loc is not None else Y_NOSE) + 0.012
    for front, y, zc in ((True, yf, 0.415), (False, Y_TAIL + 0.004, (PLATE_REAR_Z[0] + PLATE_REAR_Z[1]) / 2)):
        s = 1 if front else -1
        bm = bmesh.new()
        prism(bm, [(x, y, z) for x, z in rounded_rect(-0.26, zc - 0.056, 0.26, zc + 0.056, 0.008)], (0, s, 0), 0.0, 0.008, 0)
        o = new_obj('NumberPlate_' + ('1' if front else '-1'), bm, ['Paint_White'], root, smooth_angle=40); recentre(o); made.append(o)
        rot = (math.pi / 2, 0, math.pi if front else 0)
        made.append(a.text3('NumberPlateText', 'DS  01', (0, y + s * 0.0095, zc - 0.022), .066, 'Interior_Graphite', rot, root))
    return made


def exhaust(root):
    bm = bmesh.new()
    tube(bm, [(-0.48, Y_TAIL + 0.40, 0.24), (-0.48, Y_TAIL + 0.12, 0.24), (-0.49, Y_TAIL + 0.02, 0.235)], 0.026, 12, 0)
    o = new_obj('Ext_Exhaust', bm, ['Chrome'], root, smooth_angle=60); recentre(o)
    return o


def mirrors(root):
    import build_sedan_mirrors as mr
    made = []
    ya = y_ws(belt(1.2)) - 0.085            # front edge of the door glass at the belt
    zb = belt(ya)
    sail_x = xs(zb) - 0.006
    y0, y1 = ya - 0.17, ya
    bottom = [(0.000, y0, zb - 0.05), (0.000, y1, zb - 0.05), (0.030, y1 - 0.025, zb - 0.01), (0.030, y0 + 0.015, zb - 0.01)]
    top = [(0.004, y0 + 0.015, zb + 0.02), (0.004, y1 - 0.05, zb + 0.01), (0.078, y1 - 0.085, zb + 0.05), (0.078, y0 + 0.025, zb + 0.055)]
    for s in (-1, 1):
        made += mr.door_mirror(root, s, anchor_xyz=(sail_x + 0.07, y0 + 0.015, zb + 0.05), sail=(sail_x, bottom, top))
    return made


def wipers(root):
    """The windshield moved: park the wiper pivots on the new cowl (blades are rebuilt by build_sedan_cabin.py)."""
    moved = []
    for o in root.children:
        if o.name.startswith('Wiper_Pivot_'):
            o.location.y, o.location.z = WIPER_PIVOT
            moved.append(o.name)
    return moved


WIPER_PIVOT = (1.235, 0.972)


# ---------------------------------------------------------------------------------------------- main
def sha(p):
    return hashlib.sha256(Path(p).read_bytes()).hexdigest() if Path(p).exists() else None


def backup():
    BACKUP.mkdir(exist_ok=True)
    stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S')
    out = {}
    for src in (BLEND, FBX):
        if src.exists():
            dst = BACKUP / f'{src.stem}_before-exterior_{stamp}{src.suffix}'
            shutil.copy2(src, dst)
            out[src.name] = str(dst.relative_to(ROOT))
    return out


def replaced(o):
    n = o.name
    if n.startswith(KEEP):
        return False
    return n.startswith(REPLACED) or n.split('.')[0] in REPLACED_EXACT


def render_preview(out, views=None):
    out.mkdir(parents=True, exist_ok=True)
    sc = bpy.context.scene
    engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
    sc.render.engine = 'BLENDER_EEVEE' if 'BLENDER_EEVEE' in engines else engines[0]
    sc.render.resolution_x, sc.render.resolution_y = 1100, 700
    if not sc.world:
        sc.world = bpy.data.worlds.new('W')
    sc.world.use_nodes = True
    bg = sc.world.node_tree.nodes.get('Background')
    if bg:
        bg.inputs[0].default_value = (0.55, 0.6, 0.65, 1); bg.inputs[1].default_value = 1.2
    ld = bpy.data.lights.new('_PSun', 'SUN'); ld.energy = 3.5
    lo = bpy.data.objects.new('_PSun', ld); sc.collection.objects.link(lo); lo.rotation_euler = (math.radians(40), 0, math.radians(30))
    cd = bpy.data.cameras.new('_PCam'); cam = bpy.data.objects.new('_PCam', cd); sc.collection.objects.link(cam); sc.camera = cam

    def shot(name, loc, target, lens=35):
        if views and name not in views:
            return
        cam.location = loc
        cam.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
        cd.lens = lens; cd.clip_start = 0.02
        sc.render.filepath = str(out / f'{name}.png')
        bpy.ops.render.render(write_still=True)
    shot('front34', (4.2, 5.8, 1.9), (0, 0, 0.75))
    shot('hero', (3.3, 5.6, 1.15), (0.1, 0.4, 0.62), 40)
    shot('rear34', (-4.2, -5.8, 2.1), (0, 0, 0.75))
    shot('side', (7.5, 0, 1.0), (0, 0, 0.7), 40)
    shot('top', (0.01, 0, 7.5), (0, 0, 0.5), 35)
    shot('front', (0, 7, 1.0), (0, 0, 0.8), 45)
    shot('rear', (0, -7, 1.1), (0, 0, 0.8), 45)
    shot('mirrorL', (-2.2, 1.6, 1.4), (-0.95, 0.75, 1.0), 40)
    shot('lamps', (1.7, 4.0, 1.05), (0.35, 2.2, 0.62), 45)
    shot('tail', (-1.8, -4.0, 1.2), (-0.4, -2.2, 0.75), 45)
    eye = bpy.data.objects.get('Socket_DriverEye')
    if eye:
        e = eye.matrix_world.translation
        shot('cabin', tuple(e), tuple(e + Vector((0.1, 1, -0.18))), 18)
        shot('cabin_left', tuple(e), tuple(e + Vector((-1, 0.6, -0.2))), 20)
        shot('cabin_right', tuple(e), tuple(e + Vector((1, 0.7, -0.25))), 20)
        shot('rear_seat', (0.0, -0.9, 1.25), (0, 1.2, 0.7), 18)
        shot('back_view', tuple(e), tuple(e + Vector((0.3, -1, -0.15))), 18)


def main():
    if not PREVIEW and bpy.data.filepath and Path(bpy.data.filepath).resolve() != BLEND.resolve():
        raise SystemExit('Open ArtSource/DS_Sedan_A.blend, not ' + bpy.data.filepath)
    a.M = {m.name: m for m in bpy.data.materials}
    root = bpy.data.objects['DS_Sedan_A']
    report = {'script': 'tools/build_sedan_exterior.py', 'utc': datetime.datetime.utcnow().isoformat() + 'Z',
              'before': {'blend_sha256': sha(BLEND), 'fbx_sha256': sha(FBX)}}
    if not PREVIEW:
        report['backup'] = backup()
    removed = []
    for o in list(root.children_recursive):
        if o.name in bpy.data.objects and replaced(o):
            removed.append(o.name)
            for c in list(o.children_recursive):
                bpy.data.objects.remove(c, do_unlink=True)
            bpy.data.objects.remove(o, do_unlink=True)
    report['removed'] = sorted(removed)

    made = []
    body, skin = build_shell(root); made.append(body.name)    # skin: the body before the lamp pockets
    surf = Surface(body)
    gaps, n_gap = panel_gaps(root, surf); made.append(gaps.name)
    for f in (lambda: chrome_strokes(root, skin), lambda: arch_lips(root, surf), lambda: sills(root, surf),
              lambda: b_pillar_trim(root), lambda: exhaust(root)):
        made.append(f().name)
    made += [o.name for o in door_handles(root, surf)]
    made += [o.name for o in glass(root)]
    made += [o.name for o in lamps(root, skin)]
    made += [o.name for o in plates(root, skin)]
    made += [o.name for o in mirrors(root)]
    report['wipersMoved'] = wipers(root)
    report['created'] = sorted(made)
    report['gapPointsProjected'] = n_gap

    names = {o.name for o in root.children_recursive}
    missing = [n for n in CONTRACT if n not in names]
    if missing:
        raise SystemExit('Contract objects missing: ' + ', '.join(missing))

    bpy.context.view_layer.update()
    deps = bpy.context.evaluated_depsgraph_get()
    lo = Vector((1e9,) * 3); hi = Vector((-1e9,) * 3); tris = 0
    report['body'] = {'vertices': len(body.data.vertices), 'faces': len(body.data.polygons),
                      'materials': [m.name for m in body.data.materials]}
    for o in [root] + list(root.children_recursive):
        if o.type != 'MESH' or o.hide_render:
            continue
        ev = o.evaluated_get(deps); me = ev.to_mesh(); me.calc_loop_triangles()
        tris += len(me.loop_triangles)
        for v in me.vertices:
            p = o.matrix_world @ v.co
            lo = Vector(map(min, lo, p)); hi = Vector(map(max, hi, p))
        ev.to_mesh_clear()
    report['bounds_m'] = {'min': list(lo), 'max': list(hi), 'size': list(hi - lo)}
    report['triangles_visible'] = tris

    if PREVIEW:
        views = ARGS[ARGS.index('--views') + 1].split(',') if '--views' in ARGS else None
        render_preview(PREVIEW, views)
        (PREVIEW / 'report.json').write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding='utf-8')
        if '--save-preview-blend' in ARGS:
            bpy.ops.wm.save_as_mainfile(filepath=str(PREVIEW / 'preview.blend'), copy=True)
        print('SEDAN_EXTERIOR_PREVIEW', json.dumps({k: report[k] for k in ('bounds_m', 'triangles_visible')}), flush=True)
        return
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND))
    a.export('DS_Sedan_A', root)
    report['after'] = {'blend_sha256': sha(BLEND), 'fbx_sha256': sha(FBX)}
    REPORT.parent.mkdir(parents=True, exist_ok=True)
    REPORT.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding='utf-8')
    print('SEDAN_EXTERIOR_COMPLETE', json.dumps({k: report[k] for k in ('bounds_m', 'triangles_visible')}), flush=True)


if __name__ == '__main__':
    main()
