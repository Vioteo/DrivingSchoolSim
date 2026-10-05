"""Forest and yard greenery: birch, spruce, pine, linden, bushes, grass clumps, stumps, logs, rocks.

Trees are a branch skeleton (bark tubes with cylindrical UVs, bark textures) carrying
foliage cards: alpha-cut quads with a leafy sprig / spruce frond / pine tuft texture
(make_textures.py). Card vertices get custom normals pointing out of the crown, so the
crown is lit as a volume, not as a pile of flat quads. Every tree is generated once from
a seed; LOD1/LOD2 reuse the same skeleton with every 2nd / 5th card (enlarged) and fewer
segments. Origin = trunk base on the ground. Heights are typical for central Russia.
"""
import math
from mathutils import Vector, Matrix, noise

from .core import MB, Asset, rng

GOLDEN = math.pi * (3 - math.sqrt(5))
Z = Vector((0, 0, 1))


# ---------------------------------------------------------------- helpers
def polyline(p0, d, L, n, bend=None):
    """n points from p0 along d; bend(k, u, d) returns the next direction (u = k / (n - 1))."""
    pts, p, d = [Vector(p0)], Vector(p0), Vector(d).normalized()
    step = L / (n - 1)
    for k in range(1, n):
        if bend:
            d = bend(k, k / (n - 1), d).normalized()
        p = p + d * step
        pts.append(p.copy())
    return pts


def point_at(pts, t):
    lens = [(pts[i + 1] - pts[i]).length for i in range(len(pts) - 1)]
    s = max(0.0, min(1.0, t)) * sum(lens)
    for i, L in enumerate(lens):
        if s <= L or i == len(lens) - 1:
            f = 0 if L < 1e-9 else min(1.0, s / L)
            return pts[i].lerp(pts[i + 1], f), (pts[i + 1] - pts[i]).normalized()
        s -= L


def resample(pts, spacing):
    total = sum((pts[i + 1] - pts[i]).length for i in range(len(pts) - 1))
    n = max(2, int(round(total / spacing)) + 1)
    return [point_at(pts, i / (n - 1))[0] for i in range(n)]


def horiz(az):
    return Vector((math.cos(az), math.sin(az), 0))


def perp_h(d):
    """Horizontal vector perpendicular to d."""
    h = Vector((-d.y, d.x, 0))
    return h.normalized() if h.length > 1e-6 else Vector((1, 0, 0))


def atlas2x2(k):
    k %= 4
    u0, v1 = (k % 2) * 0.5, 1.0 - (k // 2) * 0.5
    return (u0, v1 - 0.5, u0 + 0.5, v1)


def cell8(k):
    """Spruce atlas: 2 columns x 4 rows of 2:1 sprays."""
    k %= 8
    u0, v1 = (k % 2) * 0.5, 1.0 - (k // 2) * 0.25
    return (u0, v1 - 0.25, u0 + 0.5, v1)


def card_corners(c):
    R_, U = c['right'].normalized(), c['up'].normalized()
    C, w, h = c['p'], c['w'], c['h']
    if c['anchor'] == 'top':
        C = C - U * h / 2
    elif c['anchor'] == 'bottom':
        C = C + U * h / 2
    elif c['anchor'] == 'left':
        C = C + R_ * w / 2
    return [C + R_ * (sx * w / 2) + U * (sy * h / 2) for sx in (-1, 1) for sy in (-1, 1)]


class Tree:
    """Skeleton: branches (pts, r0, r1, level, mat) and cards (dict)."""

    def __init__(self, kind):
        self.kind = kind
        self.branches = []
        self.cards = []

    def branch(self, pts, r0, r1, level, mat, flare=0.0):
        self.branches.append(dict(pts=pts, r0=r0, r1=r1, level=level, mat=mat, flare=flare))

    def card(self, p, right, up, w, h, mat, uv, anchor):
        self.cards.append(dict(p=Vector(p), right=Vector(right), up=Vector(up), w=w, h=h, mat=mat, uv=uv,
                               anchor=anchor))

    def lift(self, zmin=0.06):
        """Drops cards that would dip into the ground (the lowest spruce whorls) and keeps branch
        points above it, so nothing pokes through the terrain."""
        self.cards = [c for c in self.cards if min(v.z for v in card_corners(c)) >= zmin]
        for b in self.branches:
            if b['level'] > 0:
                b['pts'] = [Vector((p.x, p.y, max(p.z, zmin + 0.02))) for p in b['pts']]


def radii(pts, r0, r1, flare):
    out = []
    n = len(pts)
    for i, p in enumerate(pts):
        t = i / (n - 1)
        r = r0 + (r1 - r0) * t ** 0.85
        if flare:
            r *= 1 + flare * max(0.0, 1 - (p.z - pts[0].z) / 0.7) ** 2
        out.append(r)
    return out


BARK_TILE = {'EN_Bark_Birch': (0.6, 1.2), 'EN_Bark_BirchBase': (0.6, 3.0), 'EN_Bark_PineLow': (0.8, 1.6),
             'EN_Bark_PineUp': (0.8, 1.6), 'EN_Bark_Spruce': (0.5, 0.5), 'EN_Bark_Dark': (0.6, 1.2)}


def mesh_tree(T, lod, centre, radii_xyz):
    mb = MB()
    # sides per branch level; LOD1 drops twigs (the cards carry their own), LOD2 keeps trunk + limbs
    seg_lv = [(12, 6, 3, 3), (8, 4, 0, 0), (5, 3, 0, 0)][lod]
    for b in T.branches:
        seg = seg_lv[min(b['level'], 3)]
        if seg == 0:
            continue
        pts = b['pts']
        if lod == 2 and len(pts) > 3:
            pts = [pts[0], pts[len(pts) // 2], pts[-1]]
        elif lod == 1 and len(pts) > 5:
            pts = pts[::2] + ([pts[-1]] if (len(pts) - 1) % 2 else [])
        rr = radii(pts, b['r0'], b['r1'], b['flare'])
        mb.tube(pts, rr, seg, b['mat'], cap0=False, cap1=True, uv_tile=BARK_TILE.get(b['mat'], (0.6, 1.2)))
    keep, grow = [(1, 1.0), (2, 1.38), (5, 2.05)][lod]
    C = Vector(centre)
    rx, ry, rz = radii_xyz
    for i, c in enumerate(T.cards):
        if i % keep:
            continue
        p = c['p']
        q = p - C
        n = Vector((q.x / rx, q.y / ry, q.z / rz))
        n = (n.normalized() * 0.78 + Z * 0.32).normalized() if n.length > 1e-6 else Z
        mb.card(p, c['right'], c['up'], c['w'] * grow, c['h'] * grow, c['mat'], c['uv'], normal=n,
                anchor=c['anchor'])
    return mb


def crown_frame(T):
    pts = [c['p'] for c in T.cards] or [Vector((0, 0, 1))]
    C = sum(pts, Vector()) / len(pts)
    rx = max(0.5, max(abs(p.x - C.x) for p in pts))
    ry = max(0.5, max(abs(p.y - C.y) for p in pts))
    rz = max(0.5, max(abs(p.z - C.z) for p in pts))
    return C, (rx, ry, rz)


def trunk_collider(a, r, h=4.0):
    col = MB()
    col.cyl((0, 0, 0), (0, 0, h), r * 1.15, r * 0.9, 8, 'EN_Collision')
    a.mesh('COL_Trunk', col)


def tree_asset(M, aid, title, T, trunk_r, **meta):
    a = Asset(aid, title, 'tree', M, cards=len(T.cards), **meta)
    C, rad = crown_frame(T)
    a.lods([mesh_tree(T, l, C, rad) for l in range(3)])
    trunk_collider(a, trunk_r)
    return a


# ---------------------------------------------------------------- birch
def birch(M, aid, seed):
    """Silver birch: white trunk with a dark fissured base, limbs rising at 35-60 deg and arching
    out, long pendulous twigs hanging from their whole length (the 'weeping curtain')."""
    R = rng(seed)
    T = Tree('birch')
    H = R.uniform(15.5, 17.5)
    r0 = R.uniform(0.15, 0.18)
    lean = Vector((R.uniform(-1, 1), R.uniform(-1, 1), 0)) * 0.02
    ph = R.uniform(0, 6.28)
    trunk = polyline((0, 0, 0), Z, H * 0.95, 14,
                     lambda k, u, d: d + lean + Vector((math.sin(ph + k * 0.8), math.cos(ph + k * 1.1), 0)) * 0.02)
    trunk = resample(trunk, 0.5)
    k3 = next(i for i, p in enumerate(trunk) if p.z >= 3.0)
    base = trunk[:k3] + [trunk[k3 - 1].lerp(trunk[k3], (3.0 - trunk[k3 - 1].z) / (trunk[k3].z - trunk[k3 - 1].z))]
    upper = [base[-1]] + trunk[k3:]
    rb = r0 * (1 - 0.88 * 3.0 / (H * 0.95)) ** 0.85
    T.branch(base, r0, rb, 0, 'EN_Bark_BirchBase', flare=0.45)
    T.branch(upper, rb, 0.025, 0, 'EN_Bark_Birch')
    cb = H * R.uniform(0.32, 0.36)
    Rc = R.uniform(3.0, 3.5)
    n = 34
    for i in range(n):
        z = cb + (H * 0.94 - cb) * (i + R.uniform(0, 0.8)) / n
        u = (z - cb) / (H - cb)
        p0, _ = point_at(trunk, z / (H * 0.95))
        az = i * GOLDEN + R.uniform(-0.25, 0.25)
        pitch = math.radians(R.uniform(55, 68) - 30 * u)          # from vertical
        reach = Rc * max(0.22, math.sin(math.pi * min(1.0, 0.2 + u * 0.85)) ** 0.6)
        L = reach / max(0.45, math.sin(pitch)) * R.uniform(0.85, 1.05)
        d0 = Vector((math.cos(az) * math.sin(pitch), math.sin(az) * math.sin(pitch), math.cos(pitch)))
        droop = R.uniform(0.08, 0.16)
        pts = polyline(p0, d0, L, 7, lambda k, uu, d, dr=droop: d - Z * dr * uu)
        rr = min(r0 * 0.5, 0.022 + 0.05 * (1 - u))
        T.branch(pts, rr, 0.008, 1, 'EN_Bark_Birch')
        out = horiz(az)
        nt = 3 + int(L / 0.38)
        for j in range(nt):
            sp, sd = point_at(pts, 0.18 + 0.82 * (j + R.uniform(0, 1)) / nt)
            dd = (out * R.uniform(0.15, 0.45) + perp_h(out) * R.uniform(-0.5, 0.5) - Z).normalized()
            tl = R.uniform(0.9, 1.7) * (0.75 + 0.5 * (j + 1) / nt)
            tip = sp + dd * tl
            T.branch([sp, tip], 0.008, 0.003, 2, 'EN_Bark_BirchBase')
            m = max(2, int(tl / 0.24))
            for k in range(m):
                cp = sp.lerp(tip, (k + 0.3) / m)
                a = R.uniform(0, 6.28)
                rgt = horiz(a)
                tilt = R.uniform(0.0, 1.25)            # up to ~70 deg: the crown also reads from above
                upv = (Z * math.cos(tilt) + horiz(a + 1.57) * math.sin(tilt) * R.choice((-1, 1))).normalized()
                sz = R.uniform(0.62, 0.86)
                T.card(cp, rgt, upv, sz, sz, 'EN_Leaves_Birch', atlas2x2(R.randrange(4)), 'top')
        for k in range(4):     # sprigs on the limb itself fill the inside of the crown
            cp, cd = point_at(pts, R.uniform(0.35, 1.0))
            sz = R.uniform(0.6, 0.8)
            T.card(cp, horiz(R.uniform(0, 6.28)), (Z + out * 0.2).normalized(), sz, sz, 'EN_Leaves_Birch',
                   atlas2x2(R.randrange(4)), 'top')
    T.lift()
    return tree_asset(M, aid, 'Берёза', T, r0, height=round(H, 2))


# ---------------------------------------------------------------- linden
def linden(M, aid, seed):
    R = rng(seed)
    T = Tree('linden')
    H = R.uniform(11.0, 12.0)
    r0 = R.uniform(0.22, 0.26)
    split = H * 0.42
    trunk = polyline((0, 0, 0), Z, split, 6, lambda k, u, d: d + Vector((0.03, 0.01, 0)))
    T.branch(trunk, r0, r0 * 0.7, 0, 'EN_Bark_Dark', flare=0.6)
    top = trunk[-1]
    Rc = R.uniform(4.0, 4.5)
    zc = split + (H - split) * 0.5
    hc = (H - split) * 0.5 + 0.6

    def leaf_cluster(p, out, count, size):
        for k in range(count):
            off = Vector((R.uniform(-1, 1), R.uniform(-1, 1), R.uniform(-0.6, 0.8))) * 0.45
            rgt = horiz(R.uniform(0, 6.28))
            upv = (Z * R.uniform(0.3, 1.0) + out * R.uniform(0.2, 0.9) + perp_h(rgt) * R.uniform(-0.4, 0.4)).normalized()
            s = size * R.uniform(0.85, 1.15)
            T.card(p + off, rgt, upv, s, s, 'EN_Leaves_Linden', atlas2x2(R.randrange(4)), 'bottom')

    limbs = []
    for i in range(6):
        az = i * 2 * math.pi / 6 + R.uniform(-0.3, 0.3)
        pitch = math.radians(R.uniform(28, 48))
        d0 = Vector((math.cos(az) * math.sin(pitch), math.sin(az) * math.sin(pitch), math.cos(pitch)))
        L = R.uniform(4.2, 5.4)
        pts = polyline(top, d0, L, 6, lambda k, u, d: d + Z * 0.05)
        T.branch(pts, r0 * 0.5, 0.03, 1, 'EN_Bark_Dark')
        limbs.append((pts, az))
    for i in range(5):     # lower side branches from the trunk
        z = R.uniform(2.8, split - 0.3)
        p0, _ = point_at(trunk, z / split)
        az = R.uniform(0, 6.28)
        d0 = (horiz(az) + Z * R.uniform(0.2, 0.6)).normalized()
        pts = polyline(p0, d0, R.uniform(2.6, 3.6), 5, lambda k, u, d: d - Z * 0.06)
        T.branch(pts, 0.06, 0.015, 1, 'EN_Bark_Dark')
        limbs.append((pts, az))
    for pts, az in limbs:
        for j in range(7):
            sp, sd = point_at(pts, R.uniform(0.25, 1.0))
            az2 = az + R.uniform(-1.4, 1.4)
            dd = (horiz(az2) + Z * R.uniform(-0.2, 0.7)).normalized()
            L2 = R.uniform(1.2, 2.4)
            tw = polyline(sp, dd, L2, 4, lambda k, u, d: d - Z * 0.05)
            # keep the crown an oval: pull twig tips that leave it back inwards
            tip = tw[-1]
            q = Vector((tip.x / Rc, tip.y / Rc, (tip.z - zc) / hc))
            if q.length > 1.0:
                tw = [tw[0]] + [tw[0].lerp(t, 1 / q.length) for t in tw[1:]]
            T.branch(tw, 0.018, 0.005, 2, 'EN_Bark_Dark')
            for k in range(3):
                cp, _ = point_at(tw, 0.35 + 0.3 * k)
                leaf_cluster(cp, horiz(az2), 2, 0.85)
            leaf_cluster(tw[-1], horiz(az2), 3, 0.95)
    return tree_asset(M, aid, 'Липа (дворовое дерево)', T, r0)


# ---------------------------------------------------------------- spruce
def spruce(M, aid, seed, H):
    """Norway spruce: narrow cone of whorls, branches droop and turn up at the tip; flat sprays lie
    along every branch and second-order sprays hang from it in curtains (the 'comb' habit)."""
    R = rng(seed)
    T = Tree('spruce')
    r0 = 0.045 * H / 3 + 0.05
    T.branch(polyline((0, 0, 0), Z, H * 0.98, 8), r0, 0.015, 0, 'EN_Bark_Spruce', flare=0.5)
    z0 = max(0.3, H * 0.035)
    Rmax = H * 0.2 + 0.3
    spacing = max(0.14, H * 0.022)
    small = H < 6
    z = z0
    while z < H - 0.45:
        t = (z - z0) / (H - z0)
        reach = Rmax * (1 - t) ** 0.92 + 0.22
        nb = R.choice((6, 7, 7, 8))
        rot = R.uniform(0, 6.28)
        for j in range(nb):
            az = rot + j * 2 * math.pi / nb + R.uniform(-0.25, 0.25)
            L = reach * R.uniform(0.82, 1.08)
            out = horiz(az)
            sag = 0.24 * (1 - t) + 0.05

            def bend(k, u, d, sag=sag):
                return d - Z * sag * (1.0 - 1.6 * u)       # droops, then the tip turns up
            pts = polyline((0, 0, z), (out - Z * 0.1).normalized(), L, 5, bend)
            # the bare twig ends inside the last spray, otherwise it sticks out as a whisker
            T.branch([pts[0], pts[2], point_at(pts, 0.82)[0]], min(0.05, 0.012 + 0.02 * L), 0.006, 2, 'EN_Bark_Spruce')
            lc = min(1.3, 0.38 + 0.5 * L) * (0.8 if small else 1.0)
            m = max(1, int(math.ceil(L / (lc * 0.62))))
            for k in range(m):
                u = min(1.0, (k + 0.6) / m)
                cp, cd = point_at(pts, max(0.0, u - 0.55 * lc / max(L, 0.1)))
                side = perp_h(cd)
                roll = R.uniform(-0.3, 0.3)
                upv = (side * math.cos(roll) + Z * math.sin(roll)).normalized()
                T.card(cp, cd, upv, lc, lc * 0.5, 'EN_Needles_Spruce', cell8(R.randrange(8)), 'left')
            if L > 0.8 and not small:
                for k in range(2 if L < 1.8 else 3):                 # hanging curtain
                    cp, cd = point_at(pts, R.uniform(0.25, 0.85))
                    hl = lc * R.uniform(0.5, 0.7)
                    dn = (cd * 0.7 - Z * 0.75 + perp_h(cd) * R.uniform(-0.4, 0.4)).normalized()
                    T.card(cp, dn, perp_h(cd), hl, hl * 0.5, 'EN_Needles_Spruce', cell8(R.randrange(8)), 'left')
        z += spacing * R.uniform(0.85, 1.15)
    for k in range(3):   # leader: upright sprays
        a = k * math.pi / 3
        T.card((0, 0, H - 0.8), Z, horiz(a), 0.85, 0.42, 'EN_Needles_Spruce', cell8(k), 'left')
    T.lift()
    title = 'Ель' if H > 8 else 'Ель молодая'
    a = Asset(aid, title, 'tree', M, height=H, cards=len(T.cards))
    C, rad = crown_frame(T)
    a.lods([mesh_tree(T, l, C, rad) for l in range(3)])
    trunk_collider(a, r0, h=min(4.0, H * 0.6))
    return a


# ---------------------------------------------------------------- pine
def pine(M, aid, seed):
    """Scots pine (forest form): tall bare trunk, grey-brown fissured below, orange flaky above;
    a high irregular crown of limbs ending in dense needle brushes."""
    R = rng(seed)
    T = Tree('pine')
    H = R.uniform(18.5, 21.0)
    r0 = R.uniform(0.25, 0.3)
    lean = Vector((R.uniform(-1, 1), R.uniform(-1, 1), 0)) * 0.02
    ph = R.uniform(0, 6)
    trunk = polyline((0, 0, 0), Z, H * 0.96, 14,
                     lambda k, u, d: d + lean + Vector((math.sin(ph + k * 0.7), math.cos(ph + k * 0.5), 0)) * 0.03)
    trunk = resample(trunk, 0.6)
    split = H * 0.45
    k = next(i for i, p in enumerate(trunk) if p.z >= split)
    low = trunk[:k + 1]
    up = trunk[k:]
    rs = r0 * (1 - 0.8 * split / (H * 0.96)) ** 0.85
    T.branch(low, r0, rs, 0, 'EN_Bark_PineLow', flare=0.4)
    T.branch(up, rs, 0.05, 0, 'EN_Bark_PineUp')
    for i in range(6):    # short dead stubs on the bare trunk
        z = R.uniform(5.0, H * 0.58)
        p0, _ = point_at(trunk, z / (H * 0.96))
        az = R.uniform(0, 6.28)
        T.branch(polyline(p0, (horiz(az) + Z * R.uniform(-0.3, 0.1)).normalized(), R.uniform(0.15, 0.4), 3), 0.04,
                 0.018, 1, 'EN_Bark_PineLow')
    c0 = H * R.uniform(0.6, 0.64)

    def clump(p, d, size, n=2):
        """n crossed brush cards around the shoot direction d."""
        d = d.normalized()
        ref = perp_h(d) if abs(d.z) < 0.95 else Vector((1, 0, 0))
        a0 = R.uniform(0, 3.14)
        for k in range(n):
            rgt = (Matrix.Rotation(a0 + k * math.pi / n, 3, d) @ ref).normalized()
            T.card(p - d * size * 0.3, rgt, d, size, size, 'EN_Needles_Pine', atlas2x2(R.randrange(4)), 'bottom')
        # one card lying across the shoot, so the brush is not edge-on when seen from above
        fl = (horiz(R.uniform(0, 6.28)) + d * 0.35).normalized()
        T.card(p - fl * size * 0.35, perp_h(fl), fl, size * 0.9, size * 0.9, 'EN_Needles_Pine',
               atlas2x2(R.randrange(4)), 'bottom')

    n = 30
    for i in range(n):
        z = c0 + (H * 0.96 - c0) * (i + R.uniform(0, 0.8)) / n
        u = (z - c0) / (H - c0)
        p0, _ = point_at(trunk, z / (H * 0.96))
        az = i * GOLDEN + R.uniform(-0.4, 0.4)
        pitch = math.radians(R.uniform(72, 92) - 42 * u)
        d0 = Vector((math.cos(az) * math.sin(pitch), math.sin(az) * math.sin(pitch), math.cos(pitch)))
        L = R.uniform(3.0, 4.4) * (1 - 0.6 * u)
        pts = polyline(p0, d0, L, 6, lambda k, uu, d: d + Z * 0.14 * uu)
        T.branch(pts, 0.09 * (1 - 0.45 * u), 0.02, 1, 'EN_Bark_PineUp')
        clump(pts[-1], pts[-1] - pts[-2] + Z * 0.7, R.uniform(1.35, 1.7), 3)
        for u2 in (0.55, 0.78):                          # brushes along the outer half of the limb
            cp, cd = point_at(pts, u2 + R.uniform(-0.08, 0.08))
            clump(cp, cd + Z * 0.9, R.uniform(1.0, 1.25), 2)
        for s_ in range(R.choice((4, 5, 6, 7)) if L > 1.6 else 2):
            sp, sd = point_at(pts, R.uniform(0.3, 0.9))
            dd = (sd + perp_h(sd) * R.uniform(-1.1, 1.1) + Z * 0.45).normalized()
            sl = R.uniform(0.6, 1.4)
            tw = polyline(sp, dd, sl, 3, lambda k, uu, d: d + Z * 0.2)
            T.branch([tw[0], tw[-1]], 0.03, 0.012, 2, 'EN_Bark_PineUp')
            clump(tw[-1], tw[-1] - tw[0] + Z * 0.6, R.uniform(1.1, 1.4), 2)
            cp, cd = point_at(tw, 0.45)
            clump(cp, cd + Z * 0.8, R.uniform(0.85, 1.05), 2)
    clump(trunk[-1], Z, 1.2, 3)
    T.lift()
    return tree_asset(M, aid, 'Сосна', T, r0)


# ---------------------------------------------------------------- bush, grass
def bush(M, aid, seed):
    R = rng(seed)
    T = Tree('bush')
    for i in range(10):
        az = i * GOLDEN * 2 + R.uniform(-0.3, 0.3)
        p = R.uniform(0.25, 0.7)
        d = Vector((math.cos(az) * math.sin(p), math.sin(az) * math.sin(p), math.cos(p)))
        pts = polyline((R.uniform(-.1, .1), R.uniform(-.1, .1), -0.05), d, R.uniform(1.0, 1.6), 4,
                       lambda k, u, dd: dd - Z * 0.05)
        T.branch(pts, 0.025, 0.008, 2, 'EN_Bark_Dark')
        for k in range(9):
            cp, cd = point_at(pts, R.uniform(0.25, 1.0))
            rgt = horiz(R.uniform(0, 6.28))
            upv = (Z * R.uniform(0.4, 1.0) + horiz(az) * R.uniform(0.2, 0.8)).normalized()
            s = R.uniform(0.5, 0.7)
            T.card(cp - Z * 0.2, rgt, upv, s, s, 'EN_Leaves_Bush', atlas2x2(R.randrange(4)), 'bottom')
    a = Asset(aid, 'Куст', 'bush', M, collider=False, cards=len(T.cards))
    C, rad = crown_frame(T)
    a.lods([mesh_tree(T, l, C, rad) for l in range(3)])
    return a


def grass_clump(M, aid, seed, size):
    R = rng(seed)
    a = Asset(aid, 'Пучок травы', 'prop', M, collider=False)
    out = []
    for lod, n in ((0, 3), (1, 1)):
        mb = MB()
        for k in range(n):
            az = k * math.pi / n + R.uniform(-0.2, 0.2)
            mb.card((0, 0, -0.02), horiz(az), Z, size * 2, size, 'EN_Grass_Blades',
                    (0, 0, 1, 1), normal=(0, 0, 1), anchor='bottom')
        out.append(mb)
    a.lods(out)
    return a


# ---------------------------------------------------------------- stump, log, rock
def stump(M, aid, seed):
    R = rng(seed)
    a = Asset(aid, 'Пень', 'prop', M)
    out = []
    for lod in range(2):
        mb = MB()
        seg = (16, 7)[lod]
        h = 0.42
        prof = [(0.36, -0.05), (0.34, 0.08), (0.27, 0.2), (0.26, h)]
        pts = [Vector((0, 0, z)) for _, z in prof]
        mb.tube(pts, [r for r, _ in prof], seg, 'EN_Bark_PineLow', cap0=False, cap1=False, uv_tile=(0.8, 1.6))
        mb.lathe([(0.26, h), (0.2, h + 0.015), (0.0, h + 0.03)], (0, 0, 0), seg, 'EN_WoodCut', cap_top=False)
        if lod == 0:
            for i in range(5):
                az = i * 1.256 + R.uniform(-0.2, 0.2)
                d = Vector((math.cos(az), math.sin(az), -0.35))
                pts = polyline((math.cos(az) * 0.2, math.sin(az) * 0.2, 0.12), d, R.uniform(0.45, 0.7), 4,
                               lambda k, u, dd: dd + Z * 0.12)
                mb.tube(pts, [0.11, 0.08, 0.05, 0.02], 6, 'EN_Bark_PineLow', uv_tile=(0.8, 1.6))
        out.append(mb)
    a.lods(out)
    col = MB()
    col.cyl((0, 0, 0), (0, 0, 0.45), 0.36, 0.28, 8, 'EN_Collision')
    a.mesh('COL_Stump', col)
    return a


def log(M, aid, seed):
    a = Asset(aid, 'Бревно (валежник)', 'prop', M)
    L = 5.0
    pts = polyline((-L / 2, 0, 0.2), (1, 0, 0.0), L, 6, lambda k, u, d: d + Vector((0, 0.03 * math.sin(k), 0.0)))
    out = []
    for lod in range(2):
        mb = MB()
        rr = [0.21 - 0.08 * i / 5 for i in range(6)]
        mb.tube(pts, rr, (12, 6)[lod], 'EN_WoodCut', cap0=True, cap1=True, face_mat=lambda i, k: 'EN_Bark_PineLow',
                uv_tile=(0.8, 1.6))
        if lod == 0:
            b = point_at(pts, 0.4)[0]
            mb.tube(polyline(b, (0.4, 0.6, 0.5), 0.9, 3), [0.06, 0.04, 0.015], 5, 'EN_Bark_PineLow',
                    uv_tile=(0.8, 1.6))
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
        mb.transform(Matrix.Translation((0, 0, -0.06)))
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
    lambda M: grass_clump(M, 'EN_Grass_Clump_A', 1, 0.45),
    lambda M: grass_clump(M, 'EN_Grass_Clump_B', 2, 0.3),
    lambda M: stump(M, 'EN_Stump', 4),
    lambda M: log(M, 'EN_Log_Fallen', 6),
    lambda M: rock(M, 'EN_Rock_A', 7, 1.4),
    lambda M: rock(M, 'EN_Rock_B', 12, 0.7),
]
