"""Forest and yard greenery: birch, spruce, pine, linden, bushes, stumps, logs, rocks.

Every tree is a skeleton (trunk, branches, foliage blobs) generated once from a
seed. The blobs are merged into one organic crown (voxel remesh -> cloud
displacement -> decimate, core.organic); LOD0/1/2 are made from the same
skeleton with a coarser voxel and fewer triangles, so the silhouette does not
jump between levels. Faces looking up get the lighter foliage shade.
Origin = trunk base on the ground. Heights are typical for central Russia
(birch 15-18 m, spruce 11-18 m, pine 18-21 m, yard linden 11-12 m).
"""
import math
from mathutils import Vector, Matrix, noise

from .core import MB, Asset, rng, organic

GOLDEN = math.pi * (3 - math.sqrt(5))

FOLIAGE = {  # kind -> (shade, light), up_bias, per LOD (voxel, target tris, displace, noise size)
    'birch': (('EN_Leaves_Birch_A', 'EN_Leaves_Birch_B'), 0.15,
              [(0.15, 6200, 0.32, 0.45), (0.3, 2200, 0.28, 0.55), (0.62, 520, 0.18, 0.8)]),
    'linden': (('EN_Leaves_Linden_A', 'EN_Leaves_Linden_B'), 0.2,
               [(0.17, 7000, 0.42, 0.55), (0.32, 2000, 0.36, 0.6), (0.65, 480, 0.2, 0.9)]),
    'pine': (('EN_Needles_Pine', 'EN_Needles_Pine_Light'), 0.35,
             [(0.14, 4200, 0.28, 0.4), (0.28, 1300, 0.24, 0.5), (0.55, 360, 0.15, 0.8)]),
    'spruce': (('EN_Needles_Spruce_A', 'EN_Needles_Spruce_B'), 0.25,
               [(0.12, 7000, 0.16, 0.28), (0.24, 2100, 0.14, 0.4), (0.5, 520, 0.1, 0.7)]),
    'bush': (('EN_Bush_A', 'EN_Bush_B'), 0.2,
             [(0.1, 1600, 0.14, 0.25), (0.2, 480, 0.12, 0.35), (0.4, 140, 0.08, 0.5)]),
}


# ---------------------------------------------------------------- helpers
def polyline(p0, d, L, n, bend=None):
    """n points from p0 along direction d; bend(k, d) returns the next direction."""
    pts, p, d = [Vector(p0)], Vector(p0), Vector(d).normalized()
    step = L / (n - 1)
    for k in range(1, n):
        if bend:
            d = bend(k, d).normalized()
        p = p + d * step
        pts.append(p.copy())
    return pts


def resample(pts, spacing):
    lens = [(pts[i + 1] - pts[i]).length for i in range(len(pts) - 1)]
    total = sum(lens)
    n = max(2, int(round(total / spacing)) + 1)
    return [point_at(pts, i / (n - 1))[0] for i in range(n)]


def point_at(pts, t):
    """Point and direction at fraction t of the polyline length."""
    lens = [(pts[i + 1] - pts[i]).length for i in range(len(pts) - 1)]
    s = max(0.0, min(1.0, t)) * sum(lens)
    for i, L in enumerate(lens):
        if s <= L or i == len(lens) - 1:
            f = 0 if L < 1e-9 else min(1.0, s / L)
            return pts[i].lerp(pts[i + 1], f), (pts[i + 1] - pts[i]).normalized()
        s -= L


def rotate_towards(d, axis_angle, azimuth):
    d = d.normalized()
    a = Vector((1, 0, 0)) if abs(d.x) < 0.9 else Vector((0, 1, 0))
    n = (Matrix.Rotation(azimuth, 3, d) @ d.cross(a).normalized())
    return (d * math.cos(axis_angle) + n * math.sin(axis_angle)).normalized()


def radii_for(pts, r0, r1, flare=0.0, flare_h=0.6):
    out = []
    n = len(pts)
    for i, p in enumerate(pts):
        t = i / (n - 1)
        r = r0 + (r1 - r0) * t ** 0.9
        if flare:
            r *= 1 + flare * max(0.0, 1 - (p.z - pts[0].z) / flare_h) ** 2
        out.append(r)
    return out


class Skeleton:
    def __init__(self, kind):
        self.kind = kind
        self.branches = []   # dict(pts, r0, r1, level, flare)
        self.blobs = []      # (centre, radius, (sx, sy, sz), yaw)
        self.split = None

    def blob(self, c, r, scale=(1, 1, 1), yaw=0.0):
        self.blobs.append((Vector(c), r, scale, yaw))


def wood_mb(S, lod, bark, twig=None, birch_seed=None):
    mb = MB()
    seg_by_level = [(10, 7, 4), (7, 5, 0), (5, 0, 0)][lod]
    for b in S.branches:
        seg = seg_by_level[min(b['level'], 2)]
        if seg == 0:
            continue
        pts = b['pts']
        if b['level'] == 0:
            pts = resample(pts, (0.12, 0.45, 1.5)[lod] if birch_seed is not None else (0.9, 1.4, 2.5)[lod])
        elif lod == 2:
            pts = [pts[0], pts[-1]]
        rr = radii_for(pts, b['r0'], b['r1'], b.get('flare', 0.0))
        mat = bark if (b['level'] < 2 or twig is None) else twig
        fm = None
        if birch_seed is not None and b['level'] == 0:
            top = pts[-1].z

            def fm(i, k, pts=pts, seg=seg, top=top):
                z = pts[i].z
                a = k * 2 * math.pi / seg
                # lenticels: short horizontal dashes -> noise stretched around the trunk, tight along it
                v = noise.noise(Vector((math.cos(a) * 0.9, math.sin(a) * 0.9, z * 4.2 + birch_seed)))
                if z < 1.3:   # dark rough bark at the base, cracked upwards
                    w = noise.noise(Vector((math.cos(a) * 2.5, math.sin(a) * 2.5, z * 1.5 + birch_seed)))
                    return 'EN_Bark_BirchMark' if w > -0.6 + z * 0.75 else 'EN_Bark_Birch'
                if z > top - 3.0:
                    return 'EN_Bark_BirchTwig' if v > 0.2 else 'EN_Bark_Birch'
                return 'EN_Bark_BirchMark' if v > 0.3 else 'EN_Bark_Birch'
        if S.split and b['level'] == 0:
            def fm(i, k, pts=pts):
                return 'EN_Bark_PineLow' if pts[i].z < S.split else 'EN_Bark_PineUp'
        mb.tube(pts, rr, seg, mat, cap0=False, cap1=True, face_mat=fm)
    return mb


def foliage_mb(S, lod, seed, tscale=1.0):
    mats, up, levels = FOLIAGE[S.kind]
    voxel, target, disp, nsize = levels[lod]
    target = int(target * tscale)
    if tscale < 0.6:
        voxel *= 0.6
    blobs = MB()
    for c, r, sc, yaw in S.blobs:
        start = len(blobs.v)
        blobs.ico((0, 0, 0), r, 1, mats[0], scale=sc, jitter=0.18, seed=seed)
        blobs.transform(Matrix.Translation(c) @ Matrix.Rotation(yaw, 4, 'Z'), start)
    out = organic(blobs, voxel, target, disp * (0.6 if tscale < 0.6 else 1.0), nsize, mats, seed=seed, up_bias=up)
    out.v = [(x, y, max(z, 0.03)) for x, y, z in out.v]   # foliage never dips into the ground
    return out


def tree_lods(S, seed, bark, twig=None, birch=False, tscale=1.0):
    out = []
    for lod in range(3):
        mb = wood_mb(S, lod, bark, twig, birch_seed=seed if birch else None)
        mb.extend(foliage_mb(S, lod, seed, tscale))
        out.append(mb)
    return out


# ---------------------------------------------------------------- deciduous
def deciduous_skeleton(seed, kind):
    R = rng(seed)
    S = Skeleton(kind)
    if kind == 'birch':
        H = R.uniform(15.5, 17.5)
        trunk_r = R.uniform(0.15, 0.18)
        crown_base, crown_r = H * R.uniform(0.36, 0.42), R.uniform(2.7, 3.3)
        n_prim, prim_pitch, droop = 16, (0.45, 0.75), 0.18
    else:  # linden
        H = R.uniform(11.0, 12.0)
        trunk_r = R.uniform(0.21, 0.25)
        crown_base, crown_r = R.uniform(2.6, 3.0), R.uniform(3.8, 4.3)
        n_prim, prim_pitch, droop = 11, (0.55, 0.95), -0.04
    lean = Vector((R.uniform(-1, 1), R.uniform(-1, 1), 0)) * 0.02
    wob = R.uniform(0, 6.28)

    def trunk_bend(k, d):
        return d + lean + Vector((math.sin(wob + k * 0.9), math.cos(wob + k * 1.1), 0)) * 0.025

    trunk_len = H * (0.92 if kind == 'birch' else 0.66)
    trunk = polyline((0, 0, 0), (0, 0, 1), trunk_len, 10, trunk_bend)
    S.branches.append(dict(pts=trunk, r0=trunk_r, r1=trunk_r * (0.1 if kind == 'birch' else 0.4), level=0,
                           flare=0.5 if kind == 'birch' else 0.7))
    zc = (crown_base + H) / 2 + (0.3 if kind == 'linden' else -0.4)
    hc = (H - crown_base) / 2

    def reach(z):
        u = (z - zc) / hc
        return crown_r * math.sqrt(max(0.08, 1 - u * u))

    t0 = (crown_base - 0.2) / trunk_len
    for i in range(n_prim):
        t = t0 + (0.96 - t0) * (i + R.uniform(0, 0.6)) / n_prim
        base, _ = point_at(trunk, t)
        az = i * GOLDEN * 2.3 + R.uniform(-0.3, 0.3)
        pitch = R.uniform(*prim_pitch)
        d0 = Vector((math.cos(az) * math.sin(pitch), math.sin(az) * math.sin(pitch), math.cos(pitch)))
        L = reach(base.z) / max(0.4, math.sin(pitch)) * R.uniform(0.8, 1.0)
        dr = droop * (0.6 + R.uniform(0, 0.8))
        pts = polyline(base, d0, L, 6, lambda k, d, dr=dr: d + Vector((0, 0, -dr * k * 0.6)))
        rb = min(trunk_r * 0.6, trunk_r * (1 - t) * 0.9 + 0.03)
        S.branches.append(dict(pts=pts, r0=rb, r1=0.012, level=1))
        if kind == 'birch':
            # long hanging twigs with leaf masses along them
            for j in range(R.choice((3, 3, 4))):
                sb, sd = point_at(pts, R.uniform(0.3, 0.95))
                dd = (rotate_towards(sd, R.uniform(0.5, 0.9), R.uniform(0, 6.28)) + Vector((0, 0, -0.55))).normalized()
                sl = R.uniform(1.6, 2.6)
                spts = polyline(sb, dd, sl, 4, lambda k, d: d + Vector((0, 0, -0.35)))
                S.branches.append(dict(pts=spts, r0=0.018, r1=0.005, level=2))
                for u in (0.3, 0.65, 1.0):
                    c, _ = point_at(spts, u)
                    S.blob(c, R.uniform(0.38, 0.52), (1.0, 1.0, R.uniform(1.3, 1.7)), R.uniform(0, 6.28))
            for u in (0.55, 0.8, 1.0):
                c, _ = point_at(pts, u)
                S.blob(c + Vector((0, 0, -0.2)), R.uniform(0.45, 0.6), (1, 1, 1.25))
        else:
            for j in range(R.choice((2, 3, 3))):
                sb, sd = point_at(pts, R.uniform(0.35, 0.85))
                dd = rotate_towards(sd, R.uniform(0.45, 0.8), R.uniform(0, 6.28))
                spts = polyline(sb, dd, L * R.uniform(0.32, 0.5), 4)
                S.branches.append(dict(pts=spts, r0=rb * 0.45, r1=0.008, level=2))
                S.blob(spts[-1], R.uniform(0.95, 1.25), (1, 1, 0.85))
            S.blob(pts[-1], R.uniform(1.0, 1.3), (1, 1, 0.85))
            c, _ = point_at(pts, 0.6)
            S.blob(c + Vector((0, 0, 0.3)), R.uniform(0.8, 1.0))
    if kind == 'birch':
        for k in range(24):
            u = R.uniform(-0.8, 0.85)
            a = R.uniform(0, 6.28)
            rr = crown_r * math.sqrt(max(0.0, 1 - u * u)) * R.uniform(0.45, 0.8)
            S.blob((math.cos(a) * rr, math.sin(a) * rr, zc + u * hc * 0.9), R.uniform(0.55, 0.75),
                   (1, 1, R.uniform(1.3, 1.6)), R.uniform(0, 6.28))
    if kind == 'linden':
        # fill the outer shell of the crown so it reads as one dense oval
        for k in range(26):
            u = R.uniform(-0.75, 0.95)
            a = R.uniform(0, 6.28)
            rr = crown_r * math.sqrt(max(0.0, 1 - u * u)) * R.uniform(0.6, 0.85)
            S.blob((math.cos(a) * rr, math.sin(a) * rr, zc + u * hc * 0.92), R.uniform(0.9, 1.2), (1, 1, 0.85))
    S.blob(trunk[-1] + Vector((0, 0, 0.2)), 0.7 if kind == 'birch' else 1.2, (1, 1, 1.2))
    return S


def trunk_collider(a, r, h=4.0):
    col = MB()
    col.cyl((0, 0, 0), (0, 0, h), r * 1.15, r * 0.9, 8, 'EN_Collision')
    a.mesh('COL_Trunk', col)


def birch(M, aid, seed):
    S = deciduous_skeleton(seed, 'birch')
    a = Asset(aid, 'Берёза', 'tree', M)
    a.lods(tree_lods(S, seed, 'EN_Bark_Birch', 'EN_Bark_BirchTwig', birch=True))
    trunk_collider(a, S.branches[0]['r0'])
    return a


def linden(M, aid, seed):
    S = deciduous_skeleton(seed, 'linden')
    a = Asset(aid, 'Липа (дворовое дерево)', 'tree', M)
    a.lods(tree_lods(S, seed, 'EN_Bark_Dark'))
    trunk_collider(a, S.branches[0]['r0'])
    return a


# ---------------------------------------------------------------- pine
def pine_skeleton(seed):
    R = rng(seed)
    S = Skeleton('pine')
    H = R.uniform(18.5, 21.0)
    r0 = R.uniform(0.25, 0.3)
    lean = Vector((R.uniform(-1, 1), R.uniform(-1, 1), 0)) * 0.02
    ph = R.uniform(0, 6)
    trunk = polyline((0, 0, 0), (0, 0, 1), H * 0.95, 12,
                     lambda k, d: d + lean + Vector((math.sin(ph + k * 0.7), math.cos(ph + k * 0.5), 0)) * 0.03)
    S.branches.append(dict(pts=trunk, r0=r0, r1=0.06, level=0, flare=0.45))
    crown0 = H * R.uniform(0.64, 0.68)
    for i in range(8):   # dead stubs on the bare trunk
        z = R.uniform(4.0, crown0 - 0.5)
        base, _ = point_at(trunk, z / (H * 0.95))
        az = R.uniform(0, 6.28)
        d = Vector((math.cos(az), math.sin(az), R.uniform(-0.2, 0.2)))
        S.branches.append(dict(pts=polyline(base, d, R.uniform(0.35, 0.9), 3), r0=0.035, r1=0.012, level=1))
    n = 9
    for i in range(n):
        t = (crown0 + (H * 0.93 - crown0) * (i + R.uniform(0, 0.7)) / n) / (H * 0.95)
        base, _ = point_at(trunk, t)
        az = i * GOLDEN * 2.3 + R.uniform(-0.4, 0.4)
        pitch = R.uniform(1.05, 1.35) - 0.45 * (i / n)
        d0 = Vector((math.cos(az) * math.sin(pitch), math.sin(az) * math.sin(pitch), math.cos(pitch)))
        L = R.uniform(3.2, 4.4) * (1 - 0.45 * i / n)
        pts = polyline(base, d0, L, 5, lambda k, d: d + Vector((0, 0, 0.1 * k)))
        S.branches.append(dict(pts=pts, r0=0.1 * (1 - 0.4 * i / n), r1=0.02, level=1))
        tip = pts[-1]
        # a pine 'pad' is a loose bunch of needle tufts around the end of a bough
        S.blob(tip + Vector((0, 0, 0.2)), R.uniform(1.0, 1.25), (1.25, 1.25, 0.62), az)
        for k in range(5):
            a = az + R.uniform(-2.2, 2.2)
            r = R.uniform(0.6, 1.1)
            S.blob(tip + Vector((math.cos(a) * r, math.sin(a) * r, R.uniform(-0.35, 0.25))), R.uniform(0.55, 0.8),
                   (1.15, 1.15, 0.62))
        for u in (0.45, 0.7):
            c, _ = point_at(pts, u)
            S.blob(c + Vector((0, 0, 0.3)), R.uniform(0.55, 0.75), (1.2, 1.2, 0.6))
    # fill the crown around the stem so the pads read as one irregular mass
    for k in range(7):
        z = R.uniform(crown0 + 1.0, H - 1.0)
        a = R.uniform(0, 6.28)
        base, _ = point_at(trunk, z / (H * 0.95))
        S.blob(base + Vector((math.cos(a) * 0.9, math.sin(a) * 0.9, 0)), R.uniform(0.8, 1.1), (1.2, 1.2, 0.7))
    S.blob(trunk[-1] + Vector((0, 0, 0.2)), 1.0, (1.1, 1.1, 0.7))
    S.split = H * 0.4
    return S


def pine(M, aid, seed):
    S = pine_skeleton(seed)
    a = Asset(aid, 'Сосна', 'tree', M)
    a.lods(tree_lods(S, seed, 'EN_Bark_PineUp'))
    trunk_collider(a, S.branches[0]['r0'])
    return a


# ---------------------------------------------------------------- spruce
def spruce_skeleton(seed, H):
    R = rng(seed)
    S = Skeleton('spruce')
    r0 = 0.04 * H / 3 + 0.05
    S.branches.append(dict(pts=polyline((0, 0, 0), (0, 0, 1), H * 0.97, 6), r0=r0, r1=0.02, level=0, flare=0.5))
    z0 = max(0.35, H * 0.035)
    Rmax = H * 0.2 + 0.4
    spacing = max(0.28, H * 0.028)
    z = z0
    while z < H - 0.7:
        t = (z - z0) / (H - z0)
        reach = Rmax * (1 - t) ** 0.95 + 0.25
        if t < 0.08:
            reach *= 0.85 + t * 1.5
        nb = R.choice((6, 7, 7, 8))
        rot = R.uniform(0, 6.28)
        for j in range(nb):
            az = rot + j * 2 * math.pi / nb + R.uniform(-0.25, 0.25)
            L = reach * R.uniform(0.82, 1.08)
            drop = L * (0.32 - 0.18 * t) * R.uniform(0.8, 1.2)
            d = Vector((math.cos(az), math.sin(az), 0))
            # needle mass: flattened blobs along a branch that rises a little, then droops at the tip
            for k, u in enumerate((0.3, 0.6, 0.88)):
                c = Vector((0, 0, z)) + d * (L * u) + Vector((0, 0, 0.06 * L * u - drop * u * u))
                w = (0.5 - 0.22 * u) * max(0.55, min(1.0, L / 2.2)) + 0.08
                S.blob(c, w, (1.35, 0.95, 0.42), az)
        z += spacing * R.uniform(0.85, 1.15)
    # leader
    S.blob((0, 0, H - 0.45), 0.28, (1, 1, 2.2))
    S.blob((0, 0, H - 1.0), 0.45, (1, 1, 1.3))
    return S


def spruce(M, aid, seed, H):
    S = spruce_skeleton(seed, H)
    a = Asset(aid, 'Ель' if H > 8 else 'Ель молодая', 'tree', M, height=H)
    a.lods(tree_lods(S, seed, 'EN_Bark_Spruce', tscale=min(1.0, (H / 14) ** 1.5)))
    trunk_collider(a, S.branches[0]['r0'], h=min(4.0, H * 0.6))
    return a


# ---------------------------------------------------------------- bush, stump, log, rock
def bush(M, aid, seed):
    R = rng(seed)
    S = Skeleton('bush')
    for i in range(7):
        az = i * GOLDEN * 2 + R.uniform(-0.3, 0.3)
        p = R.uniform(0.3, 0.7)
        d = Vector((math.cos(az) * math.sin(p), math.sin(az) * math.sin(p), math.cos(p)))
        pts = polyline((R.uniform(-.1, .1), R.uniform(-.1, .1), -0.05), d, R.uniform(1.0, 1.5), 4)
        S.branches.append(dict(pts=pts, r0=0.03, r1=0.01, level=1))
        S.blob(pts[-1], R.uniform(0.42, 0.6))
        c, _ = point_at(pts, 0.55)
        S.blob(c, R.uniform(0.38, 0.5))
    S.blob((0, 0, 0.5), 0.55, (1.3, 1.3, 0.9))
    a = Asset(aid, 'Куст', 'bush', M, collider=False)
    a.lods(tree_lods(S, seed, 'EN_Bark_Dark'))
    return a


def stump(M, aid, seed):
    R = rng(seed)
    a = Asset(aid, 'Пень', 'prop', M)
    out = []
    for lod in range(2):
        mb = MB()
        seg = (16, 7)[lod]
        h = 0.42
        mb.lathe([(0.36, -0.05), (0.34, 0.08), (0.27, 0.2), (0.26, h)], (0, 0, 0), seg, 'EN_Bark_Dark', cap_top=False)
        mb.lathe([(0.26, h), (0.2, h + 0.015), (0.0, h + 0.03)], (0, 0, 0), seg, 'EN_WoodCut', cap_top=False)
        if lod == 0:
            for i in range(5):
                az = i * 1.256 + R.uniform(-0.2, 0.2)
                d = Vector((math.cos(az), math.sin(az), -0.35))
                pts = polyline((math.cos(az) * 0.2, math.sin(az) * 0.2, 0.12), d, R.uniform(0.45, 0.7), 4,
                               lambda k, dd: dd + Vector((0, 0, 0.12)))
                mb.tube(pts, [0.11, 0.08, 0.05, 0.02], 6, 'EN_Bark_Dark')
        out.append(mb)
    a.lods(out)
    col = MB()
    col.cyl((0, 0, 0), (0, 0, 0.45), 0.36, 0.28, 8, 'EN_Collision')
    a.mesh('COL_Stump', col)
    return a


def log(M, aid, seed):
    a = Asset(aid, 'Бревно (валежник)', 'prop', M)
    L = 5.0
    pts = polyline((-L / 2, 0, 0.2), (1, 0, 0.0), L, 6, lambda k, d: d + Vector((0, 0.03 * math.sin(k), 0.0)))
    out = []
    for lod in range(2):
        mb = MB()
        rr = [0.21 - 0.08 * i / 5 for i in range(6)]
        mb.tube(pts, rr, (12, 6)[lod], 'EN_WoodCut', cap0=True, cap1=True, face_mat=lambda i, k: 'EN_Bark_PineLow')
        if lod == 0:
            b = point_at(pts, 0.4)[0]
            mb.tube(polyline(b, (0.4, 0.6, 0.5), 0.9, 3), [0.06, 0.04, 0.015], 5, 'EN_Bark_PineLow')
        out.append(mb)
    a.lods(out)
    col = MB()
    col.box((0, 0, 0.18), (L, 0.36, 0.36), 'EN_Collision')
    a.mesh('COL_Log', col)
    return a


def rock(M, aid, seed, size):
    R = rng(seed)
    a = Asset(aid, 'Валун', 'prop', M, size=size)
    sc = (R.uniform(1.2, 1.5), 1.0, R.uniform(0.55, 0.7))
    out = []
    for lod in range(2):
        mb = MB()
        mb.ico((0, 0, size * sc[2] * 0.5), size / 2, (4, 2)[lod], 'EN_Rock', scale=sc, jitter=0.28, seed=seed,
               flat_bottom=0.55)
        for fi, f in enumerate(mb.f):   # moss on faces looking up
            p = [Vector(mb.v[j]) for j in f]
            n = (p[1] - p[0]).cross(p[2] - p[0])
            if n.length > 0 and n.normalized().z > 0.7 and noise.noise(p[0] * 1.3 + Vector((seed, 0, 0))) > -0.25:
                mb.fm[fi] = mb._mi('EN_Moss')
        mb.transform(Matrix.Translation((0, 0, -0.06)))   # sunk into the ground
        out.append(mb)
    a.lods(out)
    col = MB()
    col.ico((0, 0, size * sc[2] * 0.5 - 0.06), size / 2 * 0.95, 1, 'EN_Collision', scale=sc)
    a.mesh('COL_Rock', col)
    return a


ALL = [
    lambda M: birch(M, 'EN_Tree_Birch_A', 11),
    lambda M: birch(M, 'EN_Tree_Birch_B', 23),
    lambda M: spruce(M, 'EN_Tree_Spruce_A', 5, 17.0),
    lambda M: spruce(M, 'EN_Tree_Spruce_B', 8, 11.5),
    lambda M: spruce(M, 'EN_Tree_Spruce_Young', 13, 3.2),
    lambda M: pine(M, 'EN_Tree_Pine_A', 31),
    lambda M: pine(M, 'EN_Tree_Pine_B', 47),
    lambda M: linden(M, 'EN_Tree_Linden_A', 3),
    lambda M: bush(M, 'EN_Bush_A', 2),
    lambda M: bush(M, 'EN_Bush_B', 9),
    lambda M: stump(M, 'EN_Stump', 4),
    lambda M: log(M, 'EN_Log_Fallen', 6),
    lambda M: rock(M, 'EN_Rock_A', 7, 1.4),
    lambda M: rock(M, 'EN_Rock_B', 12, 0.7),
]
