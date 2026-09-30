"""Rolling stock kit (prefix RS_, T69): a suburban EMU (head, motor and trailer cars) and a freight train
(diesel locomotive section, box car, tank car, gondola with coal, covered hopper), plus a catenary mast.

    "C:\\Program Files\\Blender Foundation\\Blender 5.0\\blender.exe" -b --factory-startup ^
        --python-exit-code 1 --python tools/build_trains.py

Writes
  ArtSource/DS_RollingStock.blend                      source (all assets, laid out along X)
  Assets/DrivingSchool/Art/Trains/RS_*.fbx             one FBX per asset (Unity: TrainKitBuilder)
  Assets/DrivingSchool/Art/Trains/RS_Materials.json    palette for TrainKitBuilder (linear RGB)
  artifacts/reports/trains-manifest.json               SHA256, size, triangles, sockets
  artifacts/visual-review/trains/*.png                 review renders (Workbench)

Frame (docs/art-pipeline.md): metres, Z up, the car's front faces -Y. Origin: on the track axis at rail top, at the
FRONT coupler face; the car runs from y = 0 back to y = pitch (Socket_Rear, coupler face of the next car).
Dimensions are stylised after typical Russian 1520 mm stock (EMU car 21.5 m, 3.4 m wide; freight cars 12-17.5 m);
colours are generic, no operator's logos or names.
"""
import bpy, bmesh, sys, math, json, hashlib, datetime
from pathlib import Path
from mathutils import Vector, Matrix

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / 'Assets/DrivingSchool/Art/Trains'
SOURCE = ROOT / 'ArtSource/DS_RollingStock.blend'
REPORTS = ROOT / 'artifacts/reports'
REVIEW = ROOT / 'artifacts/visual-review/trains'

# name -> (linear RGB, metallic, roughness, emission)
PALETTE = {
    'RS_Paint_White': ((0.74, 0.76, 0.75), 0.1, 0.35, 0),
    'RS_Paint_Red': ((0.52, 0.018, 0.02), 0.15, 0.35, 0),
    'RS_Paint_Grey': ((0.16, 0.17, 0.18), 0.2, 0.4, 0),
    'RS_Paint_LocoRed': ((0.36, 0.025, 0.018), 0.15, 0.4, 0),
    'RS_Paint_Yellow': ((0.75, 0.45, 0.01), 0.1, 0.45, 0),
    'RS_Paint_Brown': ((0.15, 0.045, 0.025), 0.05, 0.7, 0),
    'RS_Paint_Tank': ((0.018, 0.02, 0.022), 0.25, 0.45, 0),
    'RS_Paint_Gondola': ((0.07, 0.09, 0.075), 0.1, 0.75, 0),
    'RS_Paint_Hopper': ((0.36, 0.37, 0.36), 0.15, 0.6, 0),
    'RS_Underframe': ((0.03, 0.032, 0.034), 0.4, 0.7, 0),
    'RS_Steel': ((0.30, 0.31, 0.32), 0.85, 0.35, 0),
    'RS_Wheel': ((0.16, 0.15, 0.14), 0.8, 0.45, 0),
    'RS_Rubber': ((0.012, 0.013, 0.014), 0.0, 0.85, 0),
    'RS_Glass': ((0.012, 0.02, 0.026), 0.4, 0.05, 0),
    'RS_Lamp_White': ((0.9, 0.95, 1.0), 0.0, 0.2, 2.0),
    'RS_Lamp_Red': ((0.85, 0.02, 0.015), 0.0, 0.25, 1.5),
    'RS_Coal': ((0.012, 0.012, 0.013), 0.0, 0.95, 0),
    'RS_Ceramic': ((0.45, 0.2, 0.08), 0.0, 0.3, 0),
}

X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))
# Contact wire and messenger over rail top. Stylised low (real lines: 5.75-6.8 m): the town's overpass over the
# railway leaves 5.36 m under its deck, and the wire must pass under it.
WIRE, MESSENGER = 5.3, 6.5


class Asset:
    """Geometry of one asset, one bmesh per material; joined into one object per material on output."""

    def __init__(self, name, title, kind, pitch):
        self.name, self.title, self.kind, self.pitch = name, title, kind, pitch
        self.bms = {}
        self.sockets = {'Socket_Front': (0, 0, 0), 'Socket_Rear': (0, pitch, 0)}

    def bm(self, mat):
        if mat not in PALETTE:
            raise KeyError(mat)
        if mat not in self.bms:
            self.bms[mat] = bmesh.new()
        return self.bms[mat]

    # ---------------------------------------------------------------- primitives
    def box(self, mat, c, s, rot=None):
        """Box centre c, size s (x, y, z); optional rotation matrix (3x3) about its centre."""
        bm = self.bm(mat)
        m = Matrix.Translation(Vector(c))
        if rot is not None:
            m = m @ rot.to_4x4()
        m = m @ Matrix.Diagonal((s[0], s[1], s[2], 1))
        bmesh.ops.create_cube(bm, size=1.0, matrix=m)

    def span(self, mat, x0, x1, y0, y1, z0, z1):
        self.box(mat, ((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), (abs(x1 - x0), abs(y1 - y0), abs(z1 - z0)))

    def cyl(self, mat, c, r, depth, axis='Z', seg=16, r2=None):
        bm = self.bm(mat)
        rot = {'Z': Matrix.Identity(4), 'X': Matrix.Rotation(math.pi / 2, 4, 'Y'), 'Y': Matrix.Rotation(math.pi / 2, 4, 'X')}[axis]
        bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=seg, radius1=r, radius2=r if r2 is None else r2,
                              depth=depth, matrix=Matrix.Translation(Vector(c)) @ rot)

    def rod(self, mat, a, b, r, seg=8):
        """Cylinder from point a to point b."""
        a, b = Vector(a), Vector(b)
        d = b - a
        q = Z.rotation_difference(d.normalized())
        bm = self.bm(mat)
        bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=seg, radius1=r, radius2=r, depth=d.length,
                              matrix=Matrix.Translation((a + b) / 2) @ q.to_matrix().to_4x4())

    def loft(self, mat, rings, cap0=True, cap1=True, cap_mat0=None, cap_mat1=None):
        """Skin between rings (lists of Vector, same count, same winding). Caps as n-gons (optionally other materials).
        Faces are wound outwards: away from the ring's centroid for the skin, along the loft axis for the caps."""
        bm = self.bm(mat)
        vr = [[bm.verts.new(p) for p in ring] for ring in rings]
        n = len(rings[0])
        cen = [sum(r, Vector()) / len(r) for r in rings]
        for k, (a, b) in enumerate(zip(vr, vr[1:])):
            for i in range(n):
                j = (i + 1) % n
                quad = [a[i], a[j], b[j], b[i]]
                if len({v.co.to_tuple(6) for v in quad}) < 3:
                    continue
                f = bm.faces.new(quad)
                f.normal_update()
                mid = sum((v.co for v in quad), Vector()) / 4
                if f.normal.dot(mid - (cen[k] + cen[k + 1]) / 2) < 0:
                    f.normal_flip()
        axis = (cen[-1] - cen[0]).normalized()
        for do, ring, cm, out in ((cap0, rings[0], cap_mat0, -axis), (cap1, rings[-1], cap_mat1, axis)):
            if not do:
                continue
            tb = self.bm(cm or mat)
            f = tb.faces.new([tb.verts.new(p) for p in ring])
            f.normal_update()
            if f.normal.dot(out) < 0:
                f.normal_flip()

    def patch(self, mat, rings, k0, k1, i0, i1, offset):
        """A skin patch over rings k0..k1 and profile points i0..i1 of a loft, moved by `offset` (glass on a nose)."""
        bm = self.bm(mat)
        off = Vector(offset)
        vr = [[bm.verts.new(rings[k][i] + off) for i in range(i0, i1 + 1)] for k in range(k0, k1 + 1)]
        for a, b in zip(vr, vr[1:]):
            for i in range(len(a) - 1):
                f = bm.faces.new((a[i], a[i + 1], b[i + 1], b[i]))
                f.normal_update()
                if f.normal.dot(off) < 0:
                    f.normal_flip()

    def extrude(self, mat, profile, y0, y1, **kw):
        """Profile [(x, z)] (counter-clockwise seen from +Y) swept along Y from y0 to y1."""
        self.loft(mat, [[Vector((x, y0, z)) for x, z in profile], [Vector((x, y1, z)) for x, z in profile]], **kw)

    def plate_x(self, mat, side, x, y0, y1, z0, z1, t=0.012):
        """Thin plate on a side wall at |x| (side +1 right, -1 left), standing out by t."""
        xa = side * x
        self.span(mat, xa, xa + side * t, y0, y1, z0, z1)


# ---------------------------------------------------------------- shared parts
def section(half_w, z_bot, z_side_top, z_top, r_corner, tumble=0.1, z_tumble=None, n_arc=5):
    """Car body cross-section, counter-clockwise seen from +Y (x right, z up): flat bottom, sides with
    tumblehome, rounded roof corners, slight roof crown."""
    zt = z_tumble if z_tumble is not None else z_bot + 0.55
    right = [(half_w - tumble, z_bot), (half_w, zt), (half_w, z_side_top)]
    cx, cz = half_w - r_corner, z_side_top
    for i in range(1, n_arc + 1):
        a = math.pi / 2 * i / n_arc
        right.append((cx + r_corner * math.cos(a), cz + r_corner * math.sin(a) * ((z_top - 0.08 - z_side_top) / r_corner)))
    right.append((cx * 0.5, z_top - 0.03))
    pts = right + [(0.0, z_top)] + [(-x, z) for x, z in reversed(right)]
    return pts  # bottom-right ... top ... bottom-left: counter-clockwise when seen from +Y? (x right, z up) yes


def ring(profile, y, scale_x=1.0, z_from=None, z_to=None, z_top=None):
    """Profile at y; points above z_from are squeezed so that the top reaches z_to (nose shaping)."""
    out = []
    for x, z in profile:
        if z_from is not None and z > z_from:
            z = z_from + (z - z_from) * (z_to - z_from) / (z_top - z_from)
        out.append(Vector((x * scale_x, y, z)))
    return out


def wheelset(a, y, r=0.475, gauge=1.52, z_axle=None, mat='RS_Wheel'):
    z = r if z_axle is None else z_axle
    for s in (-1, 1):
        a.cyl(mat, (s * gauge / 2, y, z), r, 0.13, 'X', 20)
        a.cyl('RS_Steel', (s * (gauge / 2 - 0.09), y, z), r + 0.03, 0.03, 'X', 20)   # flange
        a.cyl('RS_Steel', (s * (gauge / 2 + 0.2), y, z), 0.13, 0.28, 'X', 10)        # axle box
    a.cyl('RS_Steel', (0, y, z), 0.08, gauge + 0.2, 'X', 8)


def bogie_passenger(a, yc, base=2.5):
    """Two-axle passenger bogie: side frames, bolster, springs."""
    for y in (yc - base / 2, yc + base / 2):
        wheelset(a, y, r=0.475)
    for s in (-1, 1):
        a.span('RS_Underframe', s * 1.0, s * 1.22, yc - base / 2 - 0.55, yc + base / 2 + 0.55, 0.42, 0.72)
        a.span('RS_Underframe', s * 1.0, s * 1.22, yc - 0.35, yc + 0.35, 0.72, 0.95)
        for y in (yc - base / 2, yc + base / 2):
            a.cyl('RS_Steel', (s * 1.11, y, 0.82), 0.09, 0.2, 'Z', 10)
    a.span('RS_Underframe', -1.1, 1.1, yc - 0.3, yc + 0.3, 0.62, 0.86)
    for s in (-1, 1):
        a.span('RS_Paint_Grey', s * 0.6, s * 0.9, yc - base / 2 - 0.2, yc + base / 2 + 0.2, 0.2, 0.3)  # brake beams


def bogie_freight(a, yc, base=1.85):
    """Freight bogie 18-100 style: cast side frames with a window, spring set, bolster."""
    for y in (yc - base / 2, yc + base / 2):
        wheelset(a, y, r=0.475)
    for s in (-1, 1):
        x0, x1 = s * 0.95, s * 1.08
        a.span('RS_Underframe', x0, x1, yc - base / 2 - 0.35, yc + base / 2 + 0.35, 0.55, 0.72)   # top chord
        for d in (-1, 1):   # sloped lower chords
            p, q = (s * 1.015, yc + d * (base / 2 + 0.3), 0.62), (s * 1.015, yc + d * 0.45, 0.25)
            a.rod('RS_Underframe', p, q, 0.07, 6)
        a.span('RS_Underframe', x0, x1, yc - 0.45, yc + 0.45, 0.2, 0.3)
        for dy in (-0.2, 0, 0.2):
            a.cyl('RS_Steel', (s * 1.015, yc + dy, 0.42), 0.07, 0.24, 'Z', 8)
    a.span('RS_Underframe', -1.0, 1.0, yc - 0.22, yc + 0.22, 0.55, 0.78)


def coupler(a, y, facing, z=1.06):
    """SA-3 automatic coupler whose knuckle face is at y (the car's end, facing ±Y): head, the shank back into the
    draft gear and the uncoupling lever."""
    a.span('RS_Steel', -0.16, 0.16, y, y - facing * 0.28, z - 0.16, z + 0.12)
    a.span('RS_Steel', -0.09, 0.09, y - facing * 0.28, y - facing * 0.9, z - 0.08, z + 0.08)
    a.rod('RS_Steel', (-0.25, y - facing * 0.5, z + 0.05), (-1.25, y - facing * 0.5, z + 0.05), 0.02, 6)


def handrail_v(a, x, y, z0, z1, off=0.08, mat='RS_Paint_Yellow'):
    a.rod(mat, (x, y, z0), (x, y, z1), 0.018, 6)


# ---------------------------------------------------------------- EMU
EMU_W, EMU_BOT, EMU_SIDE, EMU_TOP = 1.70, 1.05, 3.35, 4.12
EMU_PROFILE = section(EMU_W, EMU_BOT, EMU_SIDE, EMU_TOP, 0.62, tumble=0.12, z_tumble=1.62)


def emu_sides(a, y0, y1, doors, windows, cab=False):
    """Livery, doors and windows on both sides between y0 and y1."""
    for s in (-1, 1):
        a.plate_x('RS_Paint_Red', s, EMU_W, y0, y1, 1.66, 1.96, 0.008)          # red band under the windows
        a.plate_x('RS_Paint_Grey', s, EMU_W, y0, y1, 1.62, 1.66, 0.008)
        a.plate_x('RS_Paint_Red', s, EMU_W, y0, y1, 3.12, 3.2, 0.008)            # thin red line over the windows
        for yc in doors:                                                        # double sliding doors, 1.3 m
            a.plate_x('RS_Paint_Grey', s, EMU_W, yc - 0.72, yc + 0.72, 1.62, 3.3, 0.014)
            a.plate_x('RS_Paint_Red', s, EMU_W + 0.014, yc - 0.66, yc + 0.66, 1.64, 3.24, 0.01)
            a.plate_x('RS_Rubber', s, EMU_W + 0.024, yc - 0.012, yc + 0.012, 1.64, 3.24, 0.004)
            for d in (-1, 1):
                a.plate_x('RS_Glass', s, EMU_W + 0.024, yc + d * 0.36 - 0.24, yc + d * 0.36 + 0.24, 2.3, 3.05, 0.004)
            a.span('RS_Steel', s * (EMU_W - 0.05), s * (EMU_W + 0.12), yc - 0.7, yc + 0.7, 1.05, 1.12)   # step
        for yc, w in windows:
            a.plate_x('RS_Rubber', s, EMU_W, yc - w / 2 - 0.04, yc + w / 2 + 0.04, 2.08, 3.07, 0.01)
            a.plate_x('RS_Glass', s, EMU_W + 0.01, yc - w / 2, yc + w / 2, 2.12, 3.03, 0.006)
        # under-floor skirt and equipment boxes
        a.span('RS_Underframe', s * 1.35, s * 1.5, y0 + 4.6, y1 - 4.6, 0.62, 1.05)


def emu_roof(a, y0, y1, pantograph=False, ac=True):
    for yc in ((y0 + 2.2, y1 - 2.2) if ac else ()):
        a.span('RS_Paint_Grey', -0.75, 0.75, yc - 1.1, yc + 1.1, EMU_TOP - 0.04, EMU_TOP + 0.3)    # air conditioner
        for dx in (-0.4, 0.4):
            a.cyl('RS_Underframe', (dx, yc, EMU_TOP + 0.31), 0.24, 0.02, 'Z', 16)
    a.span('RS_Paint_Grey', -0.35, 0.35, y0 + 3.6, y1 - 3.6, EMU_TOP - 0.02, EMU_TOP + 0.03)       # roof walkway
    if pantograph:
        yc = (y0 + y1) / 2
        a.span('RS_Paint_Grey', -1.0, 1.0, yc - 1.7, yc + 1.7, EMU_TOP, EMU_TOP + 0.12)             # base frame
        for dx in (-0.8, 0.8):
            for dy in (-1.4, 1.4):
                a.cyl('RS_Ceramic', (dx, yc + dy, EMU_TOP + 0.2), 0.07, 0.28, 'Z', 10)             # insulators
        zb = EMU_TOP + 0.35
        # half-pantograph raised to the contact wire
        knee = (0, yc + 1.45, zb + 0.55)
        head = (0, yc - 0.05, WIRE - 0.06)
        for dx in (-0.35, 0.35):
            a.rod('RS_Steel', (dx, yc - 1.2, zb), (dx * 0.4, knee[1], knee[2]), 0.045)
        a.rod('RS_Steel', (0, knee[1], knee[2]), head, 0.035)
        a.rod('RS_Steel', (0, yc - 1.0, zb), (0, knee[1] - 0.3, knee[2] - 0.1), 0.02)
        a.span('RS_Steel', -0.95, 0.95, head[1] - 0.06, head[1] + 0.06, head[2] - 0.04, head[2] + 0.04)  # collector head
        for sx in (-1, 1):
            a.rod('RS_Steel', (sx * 0.95, head[1], head[2]), (sx * 1.1, head[1], head[2] - 0.15), 0.02)
        a.box('RS_Paint_Grey', (0, yc - 1.25, zb - 0.05), (0.9, 0.35, 0.18))


def emu_underframe(a, y0, y1, motor):
    a.span('RS_Underframe', -1.45, 1.45, y0 + 0.2, y1 - 0.2, 0.98, 1.08)
    bogie_passenger(a, y0 + 3.15)
    bogie_passenger(a, y1 - 3.15)
    ys = [y0 + 5.3, y0 + 7.4, (y0 + y1) / 2, y1 - 7.4, y1 - 5.3]
    for i, yc in enumerate(ys):
        w = 1.5 if motor and i % 2 == 0 else 1.0
        a.span('RS_Paint_Grey', -1.25, 1.25, yc - w / 2, yc + w / 2, 0.55, 0.98)


def emu_end(a, y, facing):
    """Gangway end: bellows and coupler."""
    a.span('RS_Rubber', -0.72, 0.72, y, y + facing * 0.3, 1.25, 3.45)
    a.span('RS_Paint_Grey', -0.8, 0.8, y + facing * 0.28, y + facing * 0.34, 1.2, 3.5)
    coupler(a, y + facing * 0.34, facing, 1.0)


def emu_car(name, title, motor):
    L = 21.5
    a = Asset(name, title, 'emu-motor' if motor else 'emu-trailer', L)
    y0, y1 = 0.34, L - 0.34
    a.extrude('RS_Paint_White', EMU_PROFILE, y0, y1)
    doors = (y0 + 2.2, y1 - 2.2)
    wins = [(y0 + 3.95 + i * 1.55, 1.25) for i in range(9)]
    emu_sides(a, y0, y1, doors, wins)
    emu_roof(a, y0, y1, pantograph=motor)
    emu_underframe(a, y0, y1, motor)
    emu_end(a, y0, -1)
    emu_end(a, y1, 1)
    return a


def emu_head():
    L = 22.3
    a = Asset('RS_EMU_Head', 'Электропоезд: головной вагон', 'emu-head', L)
    y_nose, y_cab = 0.45, 3.2
    y1 = L - 0.34
    P = EMU_PROFILE
    zf = 2.25
    rings = [ring(P, y_nose, .86, zf, 2.5, EMU_TOP),
             ring(P, y_nose + 0.12, .9, zf, 2.78, EMU_TOP),
             ring(P, y_nose + 0.5, .95, zf, 3.38, EMU_TOP),
             ring(P, y_nose + 1.0, .985, zf, 3.86, EMU_TOP),
             ring(P, y_nose + 1.6, 1.0, zf, 4.06, EMU_TOP),
             ring(P, y_nose + 2.2, 1.0, zf, EMU_TOP, EMU_TOP),
             ring(P, y1, 1.0)]
    a.loft('RS_Paint_White', rings, cap0=True, cap1=True, cap_mat0='RS_Paint_Red')
    # red nose mask: plates on the lower nose, following the first rings
    for s in (-1, 1):
        a.plate_x('RS_Paint_Red', s, EMU_W * .985, y_nose + 0.6, y_cab, 1.66, 2.2, 0.01)
    # windscreen: glass laid on the nose skin between rings 1 and 3 (profile points over the roof, |x| <= 1.27)
    a.patch('RS_Rubber', rings, 1, 3, 5, 13, (0, -0.008, 0.008))
    a.patch('RS_Glass', rings, 1, 3, 6, 12, (0, -0.014, 0.014))
    # route display over the windscreen and the lamps
    a.patch('RS_Rubber', rings, 3, 4, 7, 11, (0, -0.01, 0.012))
    for s in (-1, 1):
        a.cyl('RS_Lamp_White', (s * 1.02, y_nose - 0.01, 1.95), 0.11, 0.05, 'Y', 16)
        a.cyl('RS_Rubber', (s * 1.02, y_nose + 0.01, 1.95), 0.15, 0.04, 'Y', 16)
        a.cyl('RS_Lamp_Red', (s * 1.3, y_nose + 0.02, 1.95), 0.06, 0.05, 'Y', 12)
    a.cyl('RS_Lamp_White', (0, y_nose + 1.33, 4.0 + 0.07), 0.07, 0.05, 'Y', 12)
    # snow plough and coupler
    a.loft('RS_Paint_Grey', [[Vector((x, y_nose - 0.35 + (0.25 if z < .6 else 0), z)) for x, z in ((-1.3, .25), (1.3, .25), (1.45, 1.0), (-1.45, 1.0))],
                             [Vector((x, y_nose + 0.2, z)) for x, z in ((-1.3, .25), (1.3, .25), (1.45, 1.0), (-1.45, 1.0))]])
    coupler(a, 0.0, -1, 0.95)
    a.sockets['Socket_Front'] = (0, 0, 0)
    doors = (y_cab + 1.5, y1 - 2.2)
    wins = [(y_cab + 3.25 + i * 1.55, 1.25) for i in range(9)]
    emu_sides(a, y_nose + 0.6, y1, doors, wins)
    for s in (-1, 1):    # cab side window and door
        a.plate_x('RS_Glass', s, EMU_W, y_nose + 1.9, y_nose + 2.5, 2.3, 3.05, 0.008)
    emu_roof(a, y_cab, y1, pantograph=False)
    emu_underframe(a, y_nose + 0.6, y1, False)
    emu_end(a, y1, 1)
    return a


# ---------------------------------------------------------------- freight
def freight_underframe(a, L, y0, y1, base=1.85, pivot=None):
    pv = pivot if pivot is not None else min(4.3, (L - 2) / 2 - 1.8)
    a.span('RS_Underframe', -1.4, 1.4, y0, y1, 1.0, 1.2)
    a.span('RS_Underframe', -0.25, 0.25, y0, y1, 0.8, 1.0)       # centre sill
    for s in (-1, 1):
        a.span('RS_Underframe', s * 1.3, s * 1.45, y0, y1, 0.9, 1.2)
    bogie_freight(a, L / 2 - pv - (L / 2 - (y0 + y1) / 2) * 0, base)
    bogie_freight(a, L / 2 + pv, base)
    # brake cylinder and reservoir
    a.cyl('RS_Steel', (0.6, L / 2 - 1.2, 0.75), 0.18, 0.5, 'Y', 12)
    a.cyl('RS_Steel', (-0.5, L / 2 + 0.6, 0.78), 0.25, 1.1, 'Y', 12)
    coupler(a, 0.0, -1)
    coupler(a, L, 1)
    for y, f in ((y0, 1), (y1, -1)):       # end platforms / steps
        for s in (-1, 1):
            a.span('RS_Steel', s * 1.2, s * 1.5, y + f * 0.05, y + f * 0.45, 0.65, 0.7)
            handrail_v(a, s * 1.52, y + f * 0.3, 0.7, 1.9)


def boxcar():
    L = 17.5
    a = Asset('RS_Wagon_Box', 'Крытый вагон', 'wagon-box', L)
    y0, y1 = 0.9, L - 0.9
    prof = section(1.63, 1.2, 3.95, 4.62, 0.4, tumble=0.0, z_tumble=1.25, n_arc=4)
    a.extrude('RS_Paint_Brown', prof, y0, y1)
    for s in (-1, 1):
        for i in range(12):                     # vertical ribs
            y = y0 + 0.35 + i * (y1 - y0 - 0.7) / 11
            if abs(y - L / 2) < 2.1:
                continue
            a.plate_x('RS_Paint_Brown', s, 1.63, y - 0.05, y + 0.05, 1.25, 3.95, 0.05)
        a.plate_x('RS_Paint_Brown', s, 1.63, L / 2 - 1.95, L / 2 + 1.95, 1.3, 3.85, 0.07)   # sliding door
        for dz in (1.6, 2.6, 3.5):
            a.plate_x('RS_Paint_Brown', s, 1.70, L / 2 - 1.9, L / 2 + 1.9, dz, dz + 0.08, 0.03)
        a.plate_x('RS_Steel', s, 1.63, L / 2 - 2.05, L / 2 + 2.05, 3.92, 3.99, 0.1)          # door rail
        a.plate_x('RS_Paint_Yellow', s, 1.64, y0 + 0.5, y0 + 1.6, 2.5, 3.0, 0.004)          # data plate (plain)
    for y in (y0, y1):
        for dz in (1.7, 2.6, 3.5):
            a.span('RS_Paint_Brown', -1.5, 1.5, y - 0.04, y + 0.04, dz, dz + 0.12)
    freight_underframe(a, L, y0, y1, pivot=6.1)
    a.span('RS_Steel', -0.3, 0.3, y1 - 0.1, y1 + 0.02, 1.4, 4.4)   # end ladder base
    for dz in (1.6, 2.1, 2.6, 3.1, 3.6, 4.1):
        a.rod('RS_Steel', (-0.25, y1 + 0.08, dz), (0.25, y1 + 0.08, dz), 0.015)
    return a


def tank():
    L = 12.02
    a = Asset('RS_Wagon_Tank', 'Цистерна', 'wagon-tank', L)
    y0, y1 = 0.9, L - 0.9
    R, zc = 1.5, 2.72
    a.cyl('RS_Paint_Tank', (0, L / 2, zc), R, 10.3, 'Y', 32)
    for y in (L / 2 - 5.15, L / 2 + 5.15):     # dished heads
        f = 1 if y > L / 2 else -1
        r_in, r_out = (R * 0.75, R * 0.97) if f > 0 else (R * 0.97, R * 0.75)   # radius1 lies on +Y after the turn
        a.cyl('RS_Paint_Tank', (0, y + f * 0.12, zc), r_in, 0.24, 'Y', 32, r2=r_out)
    for yb in (L / 2 - 3.2, L / 2 + 3.2):       # bands
        a.cyl('RS_Steel', (0, yb, zc), R + 0.02, 0.08, 'Y', 32)
    a.cyl('RS_Paint_Tank', (0, L / 2, zc + R + 0.1), 0.45, 0.4, 'Z', 20)   # dome
    a.cyl('RS_Steel', (0, L / 2, zc + R + 0.32), 0.3, 0.08, 'Z', 16)
    a.span('RS_Steel', -0.7, 0.7, L / 2 - 0.9, L / 2 + 0.9, zc + R + 0.05, zc + R + 0.09)   # platform
    for s in (-1, 1):
        a.rod('RS_Steel', (s * 0.7, L / 2 - 0.9, zc + R + 0.9), (s * 0.7, L / 2 + 0.9, zc + R + 0.9), 0.02)
        for y in (L / 2 - 0.9, L / 2 + 0.9):
            a.rod('RS_Steel', (s * 0.7, y, zc + R + 0.07), (s * 0.7, y, zc + R + 0.9), 0.02)
        a.plate_x('RS_Paint_Yellow', s, R, L / 2 - 4.0, L / 2 + 4.0, zc - 0.3, zc - 0.18, 0.01)   # hazard band
    for y in (L / 2 - 1.6, L / 2 + 1.6):   # cradles
        a.span('RS_Underframe', -1.2, 1.2, y - 0.2, y + 0.2, 1.15, 1.5)
    freight_underframe(a, L, y0, y1, pivot=3.9)
    for s in (-1, 1):      # ladder to the dome
        for dz in (1.4, 1.8, 2.2, 2.6, 3.0, 3.4, 3.8):
            a.rod('RS_Steel', (s * (R - 0.02 + max(0, (zc - dz)) * 0.0), L / 2 - 1.0, dz), (s * (R - 0.02), L / 2 - 1.4, dz), 0.012)
    return a


def gondola():
    L = 13.92
    a = Asset('RS_Wagon_Gondola', 'Полувагон с углём', 'wagon-gondola', L)
    y0, y1 = 0.75, L - 0.75
    zb, zt, hw = 1.2, 3.45, 1.6
    # open box: floor, sides and ends as slabs
    a.span('RS_Paint_Gondola', -hw, hw, y0, y1, zb, zb + 0.12)
    for s in (-1, 1):
        a.span('RS_Paint_Gondola', s * (hw - 0.06), s * hw, y0, y1, zb, zt)
        for i in range(9):
            y = y0 + 0.2 + i * (y1 - y0 - 0.4) / 8
            a.span('RS_Paint_Gondola', s * hw, s * (hw + 0.09), y - 0.07, y + 0.07, zb, zt)      # stakes
        a.span('RS_Paint_Gondola', s * hw, s * (hw + 0.07), y0, y1, zt - 0.14, zt)           # top chord
        for i in range(7):   # hatch hinges along the bottom
            y = y0 + 1 + i * 1.6
            a.span('RS_Steel', s * (hw - 0.3), s * (hw - 0.02), y - 0.15, y + 0.15, zb - 0.12, zb)
    for y, f in ((y0, 1), (y1, -1)):
        a.span('RS_Paint_Gondola', -hw, hw, y, y + f * 0.08, zb, zt)
        for dz in (1.8, 2.5, 3.1):
            a.span('RS_Paint_Gondola', -hw, hw, y - f * 0.06, y, dz, dz + 0.1)
    # coal heap: a lofted mound
    heap = []
    for k, (dy, top) in enumerate(((0.15, zt - 0.1), (1.6, zt + 0.25), ((y1 - y0) / 2, zt + 0.45), (y1 - y0 - 1.6, zt + 0.25), (y1 - y0 - 0.15, zt - 0.1))):
        heap.append([Vector((x, y0 + dy, z)) for x, z in ((-hw + .07, zt - .2), (hw - .07, zt - .2), (hw - .07, zt - .05), (hw * .45, top - .05), (0, top), (-hw * .45, top - .05), (-hw + .07, zt - .05))])
    a.loft('RS_Coal', heap)
    freight_underframe(a, L, y0, y1, pivot=4.4)
    return a


def hopper():
    L = 14.72
    a = Asset('RS_Wagon_Hopper', 'Хоппер', 'wagon-hopper', L)
    y0, y1 = 0.9, L - 0.9
    hw, zt = 1.55, 4.3
    # body: side walls, sloped end bins, rounded roof with hatches
    prof = [(-hw, 1.9), (hw, 1.9), (hw, 3.9), (hw * 0.85, 4.2), (0, zt), (-hw * 0.85, 4.2), (-hw, 3.9)]
    ring_at = lambda y, zbot: [Vector((x, y, (zbot if z < 2 else z))) for x, z in prof]
    a.loft('RS_Paint_Hopper', [ring_at(y0 + 0.1, 2.6), ring_at(y0 + 1.5, 1.9), ring_at(y1 - 1.5, 1.9), ring_at(y1 - 0.1, 2.6)])
    for yc in (L / 2 - 3.3, L / 2, L / 2 + 3.3):      # discharge hoppers under the body
        a.loft('RS_Paint_Hopper', [[Vector((x, yc + dy, z)) for x, z in ((-1.2, 1.9), (1.2, 1.9), (0.35, 0.95), (-0.35, 0.95))] for dy in (-1.2, 1.2)])
        a.span('RS_Underframe', -0.4, 0.4, yc - 0.5, yc + 0.5, 0.82, 0.95)
    for s in (-1, 1):
        for i in range(10):
            y = y0 + 1.5 + i * (y1 - y0 - 3.0) / 9
            a.plate_x('RS_Paint_Hopper', s, hw, y - 0.06, y + 0.06, 1.9, 3.9, 0.06)
    for i in range(4):
        yc = y0 + 2.2 + i * (y1 - y0 - 4.4) / 3
        a.cyl('RS_Paint_Hopper', (0, yc, zt + 0.03), 0.3, 0.12, 'Z', 16)      # loading hatches
    a.span('RS_Steel', -0.3, 0.3, y0 + 1, y1 - 1, zt + 0.02, zt + 0.05)       # roof walkway
    freight_underframe(a, L, y0, y1, pivot=4.9)
    return a


def loco():
    L = 20.8
    a = Asset('RS_Loco_Diesel', 'Тепловоз (секция)', 'loco', L)
    y_nose, y1 = 0.9, L - 0.6
    W, zb, zs, zt = 1.6, 1.35, 3.95, 4.72
    P = section(W, zb, zs, zt, 0.45, tumble=0.0, z_tumble=zb + 0.05, n_arc=4)
    zf = 2.4
    rings = [ring(P, y_nose, .92, zf, 3.15, zt), ring(P, y_nose + 0.3, .96, zf, 3.95, zt),
             ring(P, y_nose + 0.75, 1.0, zf, 4.5, zt), ring(P, y_nose + 1.4, 1.0, zf, zt, zt), ring(P, y1, 1.0)]
    a.loft('RS_Paint_LocoRed', rings, cap_mat0='RS_Paint_LocoRed')
    # grey roof strip and the yellow-white stripe
    for s in (-1, 1):
        a.plate_x('RS_Paint_Grey', s, W, y_nose + 1.6, y1, 3.75, 3.95, 0.008)
        a.plate_x('RS_Paint_Yellow', s, W, y_nose + 0.4, y1, 1.95, 2.1, 0.01)
        a.plate_x('RS_Paint_White', s, W, y_nose + 0.4, y1, 1.9, 1.95, 0.01)
        # cab side window and door
        a.plate_x('RS_Glass', s, W, y_nose + 1.0, y_nose + 1.9, 2.9, 3.55, 0.01)
        a.plate_x('RS_Rubber', s, W, y_nose + 2.2, y_nose + 2.95, 1.45, 3.55, 0.006)
        handrail_v(a, s * (W + 0.06), y_nose + 2.1, 1.55, 3.4)
        handrail_v(a, s * (W + 0.06), y_nose + 3.05, 1.55, 3.4)
        # engine room: grilles, small windows
        for i in range(6):
            yc = y_nose + 4.4 + i * 2.25
            a.plate_x('RS_Underframe', s, W, yc - 0.85, yc + 0.85, 2.45, 3.55, 0.012)
            for k in range(7):
                z = 2.52 + k * 0.145
                a.plate_x('RS_Paint_Grey', s, W + 0.012, yc - 0.8, yc + 0.8, z, z + 0.035, 0.012)
        for yc in (y1 - 2.5,):
            a.plate_x('RS_Glass', s, W, yc - 0.3, yc + 0.3, 3.0, 3.5, 0.01)
    # windscreens: glass on the nose skin between rings 0 and 1, split by a centre pillar
    a.patch('RS_Rubber', rings, 0, 1, 4, 12, (0, -0.008, 0.004))
    a.patch('RS_Glass', rings, 0, 1, 5, 11, (0, -0.014, 0.007))
    a.span('RS_Paint_LocoRed', -0.05, 0.05, y_nose - 0.02, y_nose + 0.32, 3.1, 3.98)
    # front: lamps, buffer beam, plough, handrails
    for s in (-1, 1):
        a.cyl('RS_Lamp_White', (s * 0.95, y_nose - 0.01, 2.35), 0.1, 0.05, 'Y', 16)
        a.cyl('RS_Lamp_Red', (s * 1.25, y_nose - 0.01, 2.35), 0.06, 0.05, 'Y', 12)
    a.cyl('RS_Lamp_White', (0, y_nose + 0.95, 4.55), 0.12, 0.06, 'Y', 16)
    a.span('RS_Paint_Yellow', -1.55, 1.55, y_nose - 0.12, y_nose, 1.2, 1.5)
    a.loft('RS_Underframe', [[Vector((x, y_nose - 0.55 + (0.3 if z < .6 else 0), z)) for x, z in ((-1.3, .2), (1.3, .2), (1.45, 1.2), (-1.45, 1.2))],
                             [Vector((x, y_nose, z)) for x, z in ((-1.3, .2), (1.3, .2), (1.45, 1.2), (-1.45, 1.2))]])
    for s in (-1, 1):
        a.rod('RS_Paint_Yellow', (s * 1.0, y_nose - 0.08, 2.7), (s * 1.0, y_nose - 0.08, 3.2), 0.02)
    coupler(a, 0.0, -1)
    # roof: fans, exhaust, radiators
    for i, yc in enumerate((y1 - 2.2, y1 - 4.2, y1 - 6.2)):
        a.cyl('RS_Paint_Grey', (0, yc, zt + 0.02), 0.72, 0.12, 'Z', 24)
        a.cyl('RS_Underframe', (0, yc, zt + 0.1), 0.66, 0.02, 'Z', 24)
    a.span('RS_Paint_Grey', -1.1, 1.1, y_nose + 5.0, y_nose + 9.0, zt - 0.1, zt + 0.12)
    a.cyl('RS_Underframe', (0.3, y_nose + 7.0, zt + 0.25), 0.18, 0.4, 'Z', 12)
    # underframe, fuel tank, 3-axle bogies
    a.span('RS_Underframe', -1.5, 1.5, y_nose, y1, 1.1, 1.4)
    a.span('RS_Underframe', -1.3, 1.3, L / 2 - 3.2, L / 2 + 3.2, 0.55, 1.1)
    for yc in (y_nose + 4.3, y1 - 4.0):
        for dy in (-1.85, 0, 1.85):
            wheelset(a, yc + dy, r=0.525)
        for s in (-1, 1):
            a.span('RS_Underframe', s * 1.0, s * 1.2, yc - 2.6, yc + 2.6, 0.5, 0.85)
            for dy in (-1.85, 0, 1.85):
                a.cyl('RS_Steel', (s * 1.1, yc + dy, 0.95), 0.12, 0.2, 'Z', 10)
        a.span('RS_Underframe', -1.1, 1.1, yc - 0.3, yc + 0.3, 0.7, 1.1)
    coupler(a, L, 1)
    a.span('RS_Rubber', -0.7, 0.7, y1, y1 + 0.15, 1.5, 3.4)   # rear gangway door
    a.sockets['Socket_Rear'] = (0, L, 0)
    return a


# ---------------------------------------------------------------- catenary mast
def mast():
    a = Asset('RS_Catenary_Mast', 'Опора контактной сети', 'catenary', 0.0)
    a.sockets = {'Socket_Wire': (0, 0, WIRE), 'Socket_Messenger': (0, 0, MESSENGER)}
    # the mast stands 3.1 m to the side (+X) of the track axis; the cantilever reaches over the track
    xm = 3.1
    a.cyl('RS_Paint_Grey', (xm, 0, 0.5), 0.3, 1.0, 'Z', 16)                    # foundation
    top = MESSENGER + 0.6
    a.cyl('RS_Steel', (xm, 0, (1.0 + top) / 2), 0.14, top - 1.0, 'Z', 12, r2=0.1)   # conical mast
    a.cyl('RS_Steel', (xm, 0, top + 0.02), 0.12, 0.05, 'Z', 12)
    a.rod('RS_Steel', (xm, 0, MESSENGER + 0.05), (-0.3, 0, MESSENGER + 0.05), 0.035)   # upper rod
    a.rod('RS_Steel', (xm, 0, WIRE + 0.35), (0.0, 0, WIRE + 0.6), 0.04)               # cantilever tube
    a.rod('RS_Steel', (0.0, 0, WIRE + 0.6), (0.0, 0, WIRE + 0.02), 0.02)               # dropper to the wire
    a.rod('RS_Steel', (0.0, 0, MESSENGER + 0.05), (0.0, 0, WIRE + 0.6), 0.015)
    for x in (xm - 0.3, 0.25):
        a.cyl('RS_Ceramic', (x, 0, MESSENGER + 0.05), 0.07, 0.3, 'X', 10)
    a.cyl('RS_Ceramic', (xm - 0.3, 0, WIRE + 0.38), 0.07, 0.3, 'X', 10)
    a.span('RS_Paint_White', xm - 0.16, xm + 0.16, -0.16, 0.16, 2.2, 2.5)    # number plate
    return a


ALL = [emu_head, lambda: emu_car('RS_EMU_Motor', 'Электропоезд: моторный вагон', True),
       lambda: emu_car('RS_EMU_Trailer', 'Электропоезд: прицепной вагон', False),
       loco, boxcar, tank, gondola, hopper, mast]


# ---------------------------------------------------------------- Blender side
def materials():
    M = {}
    for name, (col, metal, rough, emis) in PALETTE.items():
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        m.diffuse_color = (*col, 1)
        p = m.node_tree.nodes.get('Principled BSDF')
        p.inputs['Base Color'].default_value = (*col, 1)
        p.inputs['Metallic'].default_value = metal
        p.inputs['Roughness'].default_value = rough
        if emis:
            p.inputs['Emission Color'].default_value = (*col, 1)
            p.inputs['Emission Strength'].default_value = emis
        M[name] = m
    return M


def planar_uv(me):
    uv = me.uv_layers.new(name='UVMap')
    for poly in me.polygons:
        n = poly.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        for li in poly.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            a, b = [(co.y, co.z), (co.x, co.z), (co.x, co.y)][ax]
            uv.data[li].uv = (a, b)


def instantiate(asset, M):
    coll = bpy.context.scene.collection
    root = bpy.data.objects.new(asset.name, None)
    coll.objects.link(root)
    root['catalogId'] = asset.name
    root['label'] = asset.title
    root['kind'] = asset.kind
    root['pitch'] = asset.pitch
    short = asset.name[3:]
    for mat, bm in asset.bms.items():
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
        bmesh.ops.triangulate(bm, faces=list(bm.faces))
        me = bpy.data.meshes.new(f'{short}_{mat[3:]}')
        bm.to_mesh(me)
        bm.free()
        for poly in me.polygons:
            poly.use_smooth = True
        me.set_sharp_from_angle(angle=math.radians(40))
        planar_uv(me)
        o = bpy.data.objects.new(f'{short}_{mat[3:]}', me)
        o.data.materials.append(M[mat])
        coll.objects.link(o)
        o.parent = root
    for sname, p in asset.sockets.items():
        e = bpy.data.objects.new(f'{sname}_{short}', None)
        e.empty_display_size = 0.3
        coll.objects.link(e)
        e.location = p
        e.parent = root
    return root


def measure(objs):
    tris, lo, hi = 0, [1e9] * 3, [-1e9] * 3
    for o in objs:
        if o.type != 'MESH':
            continue
        me = o.data
        me.calc_loop_triangles()
        tris += len(me.loop_triangles)
        for v in me.vertices:
            w = o.matrix_world @ v.co
            lo = [min(a, b) for a, b in zip(lo, w)]
            hi = [max(a, b) for a, b in zip(hi, w)]
    return dict(triangles=tris, min=[round(v, 3) for v in lo], max=[round(v, 3) for v in hi],
                size=[round(b - a, 3) for a, b in zip(lo, hi)])


def export(root):
    bpy.ops.object.select_all(action='DESELECT')
    objs = [root] + list(root.children_recursive)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = root
    fbx = ART / f'{root.name}.fbx'
    bpy.ops.export_scene.fbx(filepath=str(fbx), use_selection=True, object_types={'MESH', 'EMPTY'},
                             axis_forward='-Z', axis_up='Y', apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_UNITS', use_mesh_modifiers=True,
                             mesh_smooth_type='FACE', add_leaf_bones=False, bake_anim=False)
    return fbx


def sha(p):
    return hashlib.sha256(Path(p).read_bytes()).hexdigest()


def render(name, pos, target, lens=35):
    cam = bpy.data.cameras.new(name)
    ob = bpy.data.objects.new(name, cam)
    bpy.context.scene.collection.objects.link(ob)
    ob.location = pos
    ob.rotation_euler = (Vector(target) - Vector(pos)).to_track_quat('-Z', 'Y').to_euler()
    cam.lens = lens
    cam.clip_end = 1000
    bpy.context.scene.camera = ob
    bpy.context.scene.render.filepath = str(REVIEW / f'{name}.png')
    bpy.ops.render.render(write_still=True)
    print('RENDER', name, flush=True)


def main():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    for d in (ART, REVIEW, REPORTS, SOURCE.parent):
        d.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.unit_settings.system, sc.unit_settings.scale_length = 'METRIC', 1.0
    M = materials()
    report = dict(kit='rolling stock', prefix='RS_', script='tools/build_trains.py',
                  utc=datetime.datetime.now(datetime.timezone.utc).isoformat(), blender=bpy.app.version_string,
                  frame='Blender: X right, front -Y, Z up; origin on the track axis at rail top, front coupler face',
                  assets=[])
    roots, fails = [], []
    for fn in ALL:
        asset = fn()
        root = instantiate(asset, M)
        bpy.context.view_layer.update()
        m = measure(root.children_recursive)
        fbx = export(root)
        budget = 30000 if asset.kind.startswith(('emu', 'loco')) else 15000
        checks = {
            'triangles <= %d' % budget: m['triangles'] <= budget,
            'width <= 3.75 m (gabarit)': m['size'][0] <= 3.75 or asset.kind == 'catenary',
            'height <= 5.2 m, pantograph under the wire': m['max'][2] <= (WIRE + 0.01 if asset.kind == 'emu-motor' else 5.2) or asset.kind == 'catenary',
            'wheels on the rail (flanges down to -0.04)': m['min'][2] >= -0.04,
        }
        bad = [k for k, ok in checks.items() if not ok]
        fails += [(asset.name, k) for k in bad]
        report['assets'].append(dict(id=asset.name, title=asset.title, kind=asset.kind, pitch=asset.pitch,
                                     fbx=str(fbx.relative_to(ROOT)), fbx_sha256=sha(fbx), **m,
                                     sockets={k: list(v) for k, v in asset.sockets.items()},
                                     checks={k: ('PASS' if ok else 'FAIL') for k, ok in checks.items()}))
        roots.append(root)
        print(f'ASSET_OK {asset.name} tris={m["triangles"]} size={m["size"]} fails={bad}', flush=True)
    (ART / 'RS_Materials.json').write_text(json.dumps(
        {'materials': [dict(name=n, color=list(c), metallic=mt, smoothness=round(1 - r, 3), emission=e)
                       for n, (c, mt, r, e) in PALETTE.items()]}, indent=2), encoding='utf-8')
    # lay out: EMU and freight consists on two tracks for the source file and the renders
    y = 0.0
    for r in roots[:3] + [roots[1]]:
        r.location = (0, y, 0); y += r['pitch']
    y = 0.0
    for r in roots[3:8]:
        r.location = (8, y, 0); y += r['pitch']
    roots[8].location = (-6, 30, 0)
    if '--no-render' not in argv:
        sc.render.engine = 'BLENDER_WORKBENCH'
        sc.display.shading.light = 'STUDIO'
        sc.display.shading.color_type = 'MATERIAL'
        sc.display.shading.show_shadows = True
        sc.display.shading.show_cavity = True
        sc.render.resolution_x, sc.render.resolution_y = 1600, 900
        render('emu-front', (-9, -12, 4.5), (0, 8, 2.4), 32)
        render('emu-side', (-26, 44, 3.0), (0, 44, 2.4), 28)
        render('freight-front', (-3, -14, 4.5), (8, 8, 2.4), 32)
        render('freight-side', (-20, 50, 3.0), (8, 50, 2.4), 24)
        render('bogie', (-4.2, 3.2, 1.0), (0, 3.8, 0.6), 35)
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE))
    report['source'] = str(SOURCE.relative_to(ROOT))
    report['failed_checks'] = fails
    (REPORTS / 'trains-manifest.json').write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding='utf-8')
    print('TRAINS_COMPLETE', len(report['assets']), 'assets; failed checks:', fails, flush=True)
    if fails:
        raise SystemExit(1)


main()
