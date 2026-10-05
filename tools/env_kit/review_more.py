"""Review scenes: country road with a dangerous curve, barriers, signs, yard, parking."""
import math, random
from mathutils import Vector, Matrix

from .review import scatter_forest

MOD = {  # local end point and heading change of road modules (author frame)
    'EN_Road_Country2_24m': ((0, 24), 0.0), 'EN_Road_Country2_24m_Solid': ((0, 24), 0.0),
    'EN_Road_Country2_24m_Approach': ((0, 24), 0.0),
    'EN_Road_Country2_Curve90_R50': ((50, 50), -math.pi / 2),
    'EN_Road_Country2_Curve30_R160': ((160 * (1 - math.cos(math.pi / 6)), 160 * math.sin(math.pi / 6)), -math.pi / 6),
}


def rot(v, yaw):
    c, s = math.cos(yaw), math.sin(yaw)
    return Vector((v[0] * c - v[1] * s, v[0] * s + v[1] * c))


class Chain:
    """Lays road modules end to end and keeps a sampled centre line with frames."""

    def __init__(self, S, area, start=(0, 0), heading=0.0):
        self.S, self.area = S, area
        self.p, self.h = Vector(start), heading
        self.samples = []       # (s, pos, forward, right)
        self.s = 0.0

    def add(self, aid):
        self.S.put(aid, self.area, (self.p.x, self.p.y), self.h)
        (ex, ey), dh = MOD[aid]
        if dh == 0:
            L = ey
            for i in range(int(L) + 1):
                f = rot((0, 1), self.h)
                self.samples.append((self.s + i, self.p + f * i, f, rot((1, 0), self.h)))
        else:
            R = abs(ex / (1 - math.cos(dh))) if abs(dh) > 1e-6 else 0
            L = R * abs(dh)
            for i in range(int(L) + 1):
                t = i / R
                lp = Vector((R * (1 - math.cos(t)), R * math.sin(t)))
                f = rot((math.sin(t), math.cos(t)), self.h)
                self.samples.append((self.s + i, self.p + rot(lp, self.h), f, Vector((f.y, -f.x))))
        self.p = self.p + rot((ex, ey), self.h)
        self.h += dh
        self.s += L
        return self

    def at(self, s):
        s = max(0.0, min(s, self.samples[-1][0]))
        i = min(int(s), len(self.samples) - 2)
        a, b = self.samples[i], self.samples[i + 1]
        u = (s - a[0]) / max(1e-9, b[0] - a[0])
        f = a[2].lerp(b[2], u).normalized()
        return a[1].lerp(b[1], u), f, Vector((f.y, -f.x))

    def dist(self, x, y):
        q = Vector((x, y))
        return min((p - q).length for _, p, _, _ in self.samples[::3])


def yaw_for(front_dir):
    """Yaw that turns the asset front (+Y) to face direction front_dir."""
    return math.atan2(-front_dir[0], front_dir[1])


def scene_road(S):
    if not S.has('EN_Road_Country2_Curve90_R50'):
        return []
    A = 'road'
    S.ground(A, 1500)
    ch = Chain(S, A, (0, -96))
    for m in ('EN_Road_Country2_24m', 'EN_Road_Country2_24m', 'EN_Road_Country2_24m_Approach',
              'EN_Road_Country2_24m_Approach', 'EN_Road_Country2_Curve90_R50', 'EN_Road_Country2_24m_Solid',
              'EN_Road_Country2_24m', 'EN_Road_Country2_Curve30_R160', 'EN_Road_Country2_24m'):
        ch.add(m)
    c0, c1 = 96.0, 96.0 + 50 * math.pi / 2       # curve 90 R50 along the chain
    # signs before the dangerous curve (right side, facing approaching traffic)
    for s, aid in ((c0 - 70, 'EN_Sign_1_11_1_CurveRight'), (c0 - 45, 'EN_Sign_3_20_NoOvertaking'),
                   (14, 'EN_Sign_6_13_Km17')):
        p, f, r = ch.at(s)
        q = p + r * 6.6
        S.put(aid, A, (q.x, q.y), yaw_for(-f))
    # chevrons on the outer side of the curve, facing the traffic that turns
    for k in range(5):
        s = c0 + 14 + k * 14
        p, f, r = ch.at(s)
        q = p - r * 7.3
        S.put('EN_Sign_1_34_1_Right', A, (q.x, q.y), yaw_for(-f))
    # barrier on the outer side: segments run against the traffic (local +Y = -forward), face to the road
    # outer edge: straight sections on the straights, R56 arc sections in the curve (road inside the bend)
    s = c0 - 12
    while s < c1 + 12:
        ds = 4 * 50 / 55.9          # 4 m of barrier at R55.9 = 3.58 m of the centre line
        in_curve = c0 <= s + 0.01 and s + ds <= c1 + 0.01
        if in_curve:
            p1, f1, r1 = ch.at(s + ds)
            a = p1 - r1 * 5.9
            S.put('EN_Barrier_WBeam_4m_R56', A, (a.x, a.y), math.atan2(f1.x, -f1.y))
            s += ds
        else:
            p1, f1, r1 = ch.at(s + 4)
            a = p1 - r1 * 5.9
            p0, f0, r0 = ch.at(s)
            b = p0 - r0 * 5.9
            d = (b - a).normalized()
            S.put('EN_Barrier_WBeam_4m', A, (a.x, a.y), math.atan2(-d.x, d.y))
            s += 4
    # ramps: the run goes against the traffic, so its local start is at the far end of the curve
    for s_, aid in ((c0 - 12, 'EN_Barrier_WBeam_EndRamp'), (s, 'EN_Barrier_WBeam_EndRamp_Start')):
        p, f, r = ch.at(s_)
        a = p - r * 5.9
        S.put(aid, A, (a.x, a.y), math.atan2(f.x, -f.y))
    # delineators
    s = 0.0
    while s < ch.s:
        dense = c0 - 30 < s < c1 + 30
        p, f, r = ch.at(s)
        q = p + r * 6.0
        S.put('EN_Road_Delineator', A, (q.x, q.y), yaw_for(-f))
        if not (c0 - 13 < s < c1 + 13):
            q = p - r * 6.0
            S.put('EN_Road_Delineator', A, (q.x, q.y), yaw_for(f))
        s += 10 if dense else 25
    scatter_forest(S, A, (-150, 260, -110, 230), 0.012, 7, avoid=lambda x, y: ch.dist(x, y) < 15.5)
    # forest edge: bushes and young spruce between the ditch and the trees
    scatter_forest(S, A, (-150, 260, -110, 230), 0.006, 9, avoid=lambda x, y: not (13.0 < ch.dist(x, y) < 16.0),
                   mix=[('EN_Bush_A', 3), ('EN_Bush_B', 3), ('EN_Tree_Spruce_Young', 2), ('EN_Tree_Birch_B', 1)])
    return [('road-curve-driver', A, (1.75, -28, 1.35), (9, 30, 0.6), 26),
            ('road-curve-aerial', A, (-48, -52, 46), (24, 26, 0), 30),
            ('road-curve-barrier', A, (5.0, 26.0, 1.5), (22, 48.4, 0.5), 28)]


def scene_barrier(S):
    if not S.has('EN_Barrier_WBeam_4m'):
        return []
    A = 'barrier'
    S.ground(A, 1200)
    S.put('EN_Barrier_WBeam_EndRamp_Start', A, (0, 0))
    for i in range(4):
        S.put('EN_Barrier_WBeam_4m', A, (0, i * 4))
    S.put('EN_Barrier_WBeam_EndCap', A, (0, 16))
    for i in range(4):
        S.put('EN_Barrier_Concrete_NJ_4m', A, (-5, i * 4.02))
    S.put('EN_Road_Delineator', A, (-2.5, 3), math.pi)
    return [('barrier-overview', A, (-9, -6, 2.2), (-1, 8, 0.4), 30),
            ('barrier-detail', A, (-1.6, 1.0, 0.95), (-0.1, 3.6, 0.55), 30),
            ('barrier-end-ramp', A, (-3.5, -6, 1.4), (0.3, -1, 0.2), 32),
            ('barrier-end-cap', A, (-2.2, 20, 1.3), (0, 16.3, 0.5), 32)]


def scene_signs(S):
    ids = ['EN_Sign_1_11_1_CurveRight', 'EN_Sign_1_11_2_CurveLeft', 'EN_Sign_1_34_1_Right', 'EN_Sign_1_34_2_Left',
           'EN_Sign_3_20_NoOvertaking', 'EN_Sign_6_13_Km17', 'EN_Road_Delineator']
    if not S.has(ids[0]):
        return []
    A = 'props'
    S.ground(A, 1200)
    for i, aid in enumerate(ids):
        S.put(aid, A, (-5.4 + i * 1.8, 0), 0.0)
    return [('signs-front', A, (0, 10.5, 1.9), (0, 0, 1.6), 28)]


SCENES = [scene_road, scene_barrier, scene_signs]


# ---------------------------------------------------------------- context models (not part of the kit)
_IMPORTED = {}


def import_fbx(path, name, fix_front=False, auto_rotate=True):
    """Imports an existing project FBX (cars, houses) for context in the review renders only."""
    import bpy
    from pathlib import Path
    if name in _IMPORTED:
        return _IMPORTED[name]
    if not Path(path).exists():
        print('CONTEXT_MISSING', path)
        return None
    tmp = bpy.data.scenes.new('tmp_import')
    before = set(bpy.data.objects)
    with bpy.context.temp_override(scene=tmp, view_layer=tmp.view_layers[0]):
        bpy.ops.import_scene.fbx(filepath=str(path))
    new = [o for o in bpy.data.objects if o not in before]
    coll = bpy.data.collections.new('CTX_' + name)
    for o in new:
        for c in list(o.users_collection):
            c.objects.unlink(o)
        coll.objects.link(o)
    bpy.data.scenes.remove(tmp)
    root = bpy.data.objects.new('CTXROOT_' + name, None)
    coll.objects.link(root)
    for o in new:
        if o.parent is None:
            o.parent = root
    bpy.context.view_layer.update()
    deps = bpy.context.evaluated_depsgraph_get()
    lo, hi = [1e9] * 3, [-1e9] * 3
    heads = []
    for o in new:
        if o.type != 'MESH':
            continue
        mw = o.matrix_world
        for c in o.bound_box:
            w = mw @ Vector(c)
            lo = [min(a, b) for a, b in zip(lo, w)]
            hi = [max(a, b) for a, b in zip(hi, w)]
        if 'Headlight' in o.name:
            heads.append((mw @ Vector(o.bound_box[0])))
    size = [b - a for a, b in zip(lo, hi)]
    yaw = 0.0
    if auto_rotate and size[0] > size[1]:
        yaw = math.pi / 2
    root.rotation_euler = (0, 0, yaw)
    bpy.context.view_layer.update()
    if fix_front and heads:
        # front must face Blender -Y
        hy_rot = Matrix.Rotation(yaw, 4, 'Z') @ Vector((sum(h.x for h in heads) / len(heads),
                                                         sum(h.y for h in heads) / len(heads), 0))
        c_rot = Matrix.Rotation(yaw, 4, 'Z') @ Vector(((lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, 0))
        if hy_rot.y > c_rot.y:
            yaw += math.pi
            root.rotation_euler = (0, 0, yaw)
    bpy.context.view_layer.update()
    lo, hi = [1e9] * 3, [-1e9] * 3
    for o in new:
        if o.type != 'MESH':
            continue
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            lo = [min(a, b) for a, b in zip(lo, w)]
            hi = [max(a, b) for a, b in zip(hi, w)]
    root.location = (-(lo[0] + hi[0]) / 2, -(lo[1] + hi[1]) / 2, -lo[2])
    print('CONTEXT', name, [round(b - a, 2) for a, b in zip(lo, hi)])

    class Ctx:
        pass
    c = Ctx()
    c.coll = coll
    _IMPORTED[name] = c
    return c


def put_ctx(S, ctx, area, loc, yaw=0.0):
    if ctx is None:
        return
    key = '__ctx_' + ctx.coll.name
    S.assets[key] = ctx
    S.put(key, area, loc, yaw)
    del S.assets[key]


def cars():
    from pathlib import Path
    root = Path(__file__).resolve().parents[2] / 'Assets/DrivingSchool/Art/Vehicles'
    out = []
    for n in ('DS_Sedan_A_Traffic', 'DS_Crossover_A_Traffic', 'DS_Police_A_Traffic', 'DS_Sedan_A_Traffic'):
        c = import_fbx(root / f'{n}.fbx', n, fix_front=True)
        if c:
            out.append(c)
    return out


def house(name='DS_House_Brick5'):
    from pathlib import Path
    root = Path(__file__).resolve().parents[2] / 'Assets/DrivingSchool/Art/Houses'
    return import_fbx(root / f'{name}.fbx', name, auto_rotate=False)


def asphalt_strip(S, area, pts, w, mat='EN_Asphalt'):
    from .core import MB, mesh_object
    from .review import AREAS
    ox, oy = AREAS[area]
    mb = MB()
    fr = []
    for i, p in enumerate(pts):
        q = Vector(pts[min(i + 1, len(pts) - 1)]) - Vector(pts[max(i - 1, 0)])
        q.normalize()
        fr.append((Vector((p[0] + ox, p[1] + oy, -0.005)), Vector((q.y, -q.x, 0)), Vector((0, 0, 1))))
    mb.sweep(fr, [(-w / 2, 0), (w / 2, 0)], mat)
    mesh_object(f'Strip_{area}_{len(S.area(area).collection.objects)}', mb, S.M, coll=S.area(area).collection)


def scene_yard(S):
    if not S.has('EN_Yard_Swing'):
        return []
    A = 'yard'
    S.ground(A, 1200)
    R = random.Random(4)
    S.put('EN_Yard_PlaygroundPad', A, (0, 0))
    S.put('EN_Yard_Swing', A, (-4.0, 2.5))
    S.put('EN_Yard_Slide', A, (3.5, 1.5), math.pi)
    S.put('EN_Yard_Sandbox', A, (-3.5, -2.5))
    S.put('EN_Yard_Carousel', A, (1.0, -2.8))
    S.put('EN_Yard_SportsFrame', A, (5.0, -3.0), 0.3)
    for x in (-5.0, -1.5, 2.0):
        S.put('EN_Yard_Bench', A, (x, -6.0), 0.0)
    S.put('EN_Yard_Bin', A, (0.2, -6.0))
    S.put('EN_Yard_Bin', A, (-3.3, -6.0))
    for i in range(12):
        S.put('EN_Yard_LawnFence_2m', A, (-12 + i * 2.0, -7.2))
    for x in (-10.0, -8.6, 9.0):
        S.put('EN_Yard_TireFlowerbed', A, (x, -8.3))
    for p, aid in (((-11, 6), 'EN_Tree_Linden_A'), ((11, 7), 'EN_Tree_Birch_A'), ((-12, -3), 'EN_Tree_Birch_B'),
                   ((12.5, -2), 'EN_Tree_Linden_A'), ((20, 10), 'EN_Tree_Linden_A'), ((-20, 12), 'EN_Tree_Birch_A')):
        S.put(aid, A, p, R.uniform(0, 6.28))
    for x in (-16, -14.5, 15):
        S.put('EN_Bush_A', A, (x, -8.5), R.uniform(0, 6))
    S.put('EN_Yard_Lamp', A, (-8, -7.8), math.pi)
    S.put('EN_Yard_Lamp', A, (10, -7.8), math.pi)
    # yard drive with parking and the waste site, house behind
    asphalt_strip(S, A, [(-40, -12.5), (40, -12.5)], 5.5)
    for x in (-22.5, -10.0):
        S.put('EN_Park_Bay_Perp_5x', A, (x, -15.25), 0.0)
    cs = cars()
    for i, x in enumerate((-27.5, -25.0, -20.0, -12.5, -7.5)):
        if cs:
            put_ctx(S, cs[i % len(cs)], A, (x, -18.0), math.pi * (i % 2))
    S.put('EN_Yard_WasteSite', A, (8.0, -17.0), 0.0)
    for x in (16.0, 19.2, 22.4):
        S.put('EN_Yard_Garage_Teal' if x != 19.2 else 'EN_Yard_Garage_Beige', A, (x, -19.5), 0.0)
    S.put('EN_Yard_BarrierGate', A, (31.0, -9.6), 0.0)
    for x in (-2.0, 0.0, 2.0):
        S.put('EN_Yard_Bollard_Sphere', A, (x + 27, -9.6))
    h = house()
    put_ctx(S, h, A, (0, 22), math.pi)
    return [('yard-overview', A, (-26, -40, 16), (2, -4, 0), 28),
            ('yard-playground', A, (-9, -12, 3.2), (0.5, 0.5, 1.0), 30),
            ('yard-drive', A, (36, -11.5, 1.6), (6, -16, 1.2), 30)]


def scene_parking(S):
    if not S.has('EN_Park_Lot_20'):
        return []
    A = 'parking'
    S.ground(A, 1200)
    R = random.Random(8)
    S.put('EN_Park_Lot_20', A, (0, 0))
    cs = cars()
    occupied = [i for i in range(20) if R.random() < 0.55]
    for i in occupied:
        row = i // 10
        x = -11.25 + (i % 10) * 2.5
        y = (5.4 if row == 0 else -5.4)
        if cs:
            put_ctx(S, cs[i % len(cs)], A, (x, y), 0.0 if row == 0 else math.pi)
    for i in range(10):
        x = -11.25 + i * 2.5
        if (i + 0) not in occupied:
            S.put('EN_Park_WheelStop', A, (x, 7.4))
    for p in ((-13.5, 5.5), (13.5, -5.5)):
        S.put('EN_Tree_Linden_A', A, p, R.uniform(0, 6))
    for p in ((-13.5, -5.5), (13.5, 5.5)):
        S.put('EN_Bush_B', A, p, R.uniform(0, 6))
    S.put('EN_Park_PayStation', A, (-15.2, 3.6), -math.pi / 2)
    for y in (-2.0, 0.0, 2.0):
        S.put('EN_Park_Bollard', A, (17.5, y))
    S.put('EN_Yard_Lamp', A, (0, 8.6), math.pi)
    S.put('EN_Yard_Lamp', A, (0, -8.6), 0.0)
    # bays along a street: angled, perpendicular, parallel pocket
    asphalt_strip(S, A, [(-40, -16), (40, -16)], 7.0)
    S.put('EN_Park_Bay_Angle45_4x', A, (-18, -19.5), 0.0)
    S.put('EN_Park_Bay_Perp_5x', A, (2, -19.5), 0.0)
    S.put('EN_Park_Pocket_Parallel_3x', A, (12, -19.5), -math.pi / 2)
    if cs:
        put_ctx(S, cs[1], A, (-19.6, -22.0), math.radians(45))
        put_ctx(S, cs[0], A, (4.5, -22.2), 0.0)
        put_ctx(S, cs[2], A, (22, -20.7), -math.pi / 2)
    return [('parking-overview', A, (-24, -36, 18), (0, -3, 0), 30),
            ('parking-lot-eye', A, (-17, 0.5, 1.6), (6, -1, 0.8), 30),
            ('parking-street-bays', A, (-30, -10, 4), (5, -21, 0), 30)]


def scene_yard_props(S):
    ids = ['EN_Yard_Bench', 'EN_Yard_Bin', 'EN_Yard_Bollard_Sphere', 'EN_Yard_TireFlowerbed', 'EN_Park_WheelStop',
           'EN_Park_PayStation', 'EN_Park_Bollard', 'EN_Yard_Lamp']
    if not S.has(ids[0]):
        return []
    A = 'props'
    xs = [-14, -12, -10.7, -9.5, -8, -6, -4.7, -3]
    for x, aid in zip(xs, ids):
        S.put(aid, A, (x, -10), 0.0)
    S.ground(A, 1200) if 'EN_Sign_1_11_1_CurveRight' not in S.assets else None
    return [('yard-props', A, (-8.5, -4.5, 1.6), (-8.5, -10, 0.5), 36)]


SCENES = SCENES + [scene_yard, scene_parking, scene_yard_props]
