"""Geometry core of the vehicle kit. Pure Python, no bpy.

Axes (same as the existing DS_Sedan_A source): X right, Y forward (+Y is the
nose), Z up, metres. Every generated face is wound so that its normal points
towards the viewer who is meant to see it (Unity culls back faces).
"""
import math


# ------------------------------------------------------------------ scalar
def clamp(v, a=0.0, b=1.0):
    return a if v < a else b if v > b else v


def smooth(t):
    t = clamp(t)
    return t * t * (3 - 2 * t)


def lerp(a, b, t):
    return a + (b - a) * t


def lin(keys, v):
    """Piecewise-linear lookup: sharp character lines stay sharp."""
    if v <= keys[0][0]:
        return keys[0][1]
    for (a, fa), (b, fb) in zip(keys, keys[1:]):
        if v <= b:
            return fa + (fb - fa) * (v - a) / (b - a)
    return keys[-1][1]


def smooth_keys(keys, v):
    if v <= keys[0][0]:
        return keys[0][1]
    for (a, fa), (b, fb) in zip(keys, keys[1:]):
        if v <= b:
            return fa + (fb - fa) * smooth((v - a) / (b - a))
    return keys[-1][1]


# ------------------------------------------------------------------ vectors
def add(a, b):
    return (a[0] + b[0], a[1] + b[1], a[2] + b[2])


def sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def mul(a, s):
    return (a[0] * s, a[1] * s, a[2] * s)


def dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def norm(a):
    l = math.sqrt(dot(a, a)) or 1.0
    return (a[0] / l, a[1] / l, a[2] / l)


def vlerp(a, b, t):
    return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t)


def rot_euler(p, e):
    """Blender XYZ Euler (applied X, then Y, then Z)."""
    x, y, z = p
    cx, sx = math.cos(e[0]), math.sin(e[0])
    y, z = y * cx - z * sx, y * sx + z * cx
    cy, sy = math.cos(e[1]), math.sin(e[1])
    x, z = x * cy + z * sy, -x * sy + z * cy
    cz, sz = math.cos(e[2]), math.sin(e[2])
    x, y = x * cz - y * sz, x * sz + y * cz
    return (x, y, z)


# ------------------------------------------------------------------ parts
class Part:
    """A node of the vehicle hierarchy. Mesh vertices are in the part's local
    frame (relative to loc/rot). kind: 'mesh', 'empty' or 'text'."""

    def __init__(self, name, kind='mesh', material=None, parent=None, loc=(0, 0, 0), rot=(0, 0, 0)):
        self.name, self.kind, self.material = name, kind, material
        self.parent, self.loc, self.rot = parent, tuple(loc), tuple(rot)
        self.v, self.f = [], []
        self.solidify = 0.0          # sheet thickness (grows inwards)
        self.smooth = True
        self.hidden = False          # hide_render (e.g. automatic gearbox)
        self.text = None             # for kind == 'text': (body, size, extrude)

    # -- building
    def add(self, vf):
        v, f = vf
        base = len(self.v)
        self.v.extend(v)
        self.f.extend(tuple(i + base for i in q) for q in f)
        return self

    def flip(self):
        self.f = [tuple(reversed(q)) for q in self.f]
        return self

    def tris(self):
        for q in self.f:
            for k in range(1, len(q) - 1):
                yield q[0], q[k], q[k + 1]


def face_normal(V, q):
    a = V[q[0]]
    n = (0.0, 0.0, 0.0)
    for i in range(1, len(q) - 1):
        n = add(n, cross(sub(V[q[i]], a), sub(V[q[i + 1]], a)))
    return n


def signed_volume(V, F):
    vol = 0.0
    for q in F:
        a = V[q[0]]
        for i in range(1, len(q) - 1):
            vol += dot(a, cross(V[q[i]], V[q[i + 1]]))
    return vol / 6.0


def make_outward(vf):
    """Closed shells: flip if the enclosed volume is negative."""
    v, f = vf
    if signed_volume(v, f) < 0:
        f = [tuple(reversed(q)) for q in f]
    return v, f


def orient_to(vf, ref):
    """Open surfaces: orient so normals agree with ref(centroid) on average."""
    v, f = vf
    s = 0.0
    for q in f:
        n = face_normal(v, q)
        c = mul(tuple(sum(v[i][k] for i in q) for k in range(3)), 1.0 / len(q))
        s += dot(n, ref(c))
    if s < 0:
        f = [tuple(reversed(q)) for q in f]
    return v, f


# ------------------------------------------------------------------ builders
def grid(rows, closed_u=False, closed_v=False):
    """rows: list of rows (each a list of points, equal length)."""
    v = [p for r in rows for p in r]
    m = len(rows[0])
    n = len(rows)
    f = []
    for i in range(n - 1 + (1 if closed_v else 0)):
        i2 = (i + 1) % n
        for j in range(m - 1 + (1 if closed_u else 0)):
            j2 = (j + 1) % m
            f.append((i * m + j, i * m + j2, i2 * m + j2, i2 * m + j))
    return v, f


def fan(points, center=None):
    if center is None:
        center = mul(tuple(sum(p[k] for p in points) for k in range(3)), 1.0 / len(points))
    v = [center] + list(points)
    f = [(0, i + 1, (i + 1) % len(points) + 1) for i in range(len(points))]
    return v, f


def superellipsoid(center, half, exp=0.3, seg_u=20, seg_v=10):
    """Rounded box. exp -> 0: sharp box, exp = 1: ellipsoid."""
    cx, cy, cz = center
    a, b, c = half

    def sp(w, m):
        cw = math.cos(w)
        return math.copysign(abs(cw) ** m, cw)

    def ss(w, m):
        sw = math.sin(w)
        return math.copysign(abs(sw) ** m, sw)

    v = [(cx, cy, cz - c)]
    for i in range(1, seg_v):
        th = -math.pi / 2 + math.pi * i / seg_v
        for j in range(seg_u):
            ph = -math.pi + 2 * math.pi * j / seg_u
            v.append((cx + a * sp(th, exp) * sp(ph, exp), cy + b * sp(th, exp) * ss(ph, exp), cz + c * ss(th, exp)))
    v.append((cx, cy, cz + c))
    top = len(v) - 1
    f = []
    for j in range(seg_u):
        f.append((0, 1 + (j + 1) % seg_u, 1 + j))
    for i in range(seg_v - 2):
        r0 = 1 + i * seg_u
        r1 = r0 + seg_u
        for j in range(seg_u):
            j2 = (j + 1) % seg_u
            f.append((r0 + j, r0 + j2, r1 + j2, r1 + j))
    last = 1 + (seg_v - 2) * seg_u
    for j in range(seg_u):
        f.append((last + j, last + (j + 1) % seg_u, top))
    return make_outward((v, f))


def rbox(center, size, r=0.02, seg=3):
    """Box with rounded edges (radius r): a superellipsoid is too soft for trim,
    so build it as a rounded-corner prism in all three axes."""
    ex = 2.0 / (2.0 + 10.0 * (1 - min(1.0, 2 * r / max(1e-6, min(size)))))
    half = (size[0] / 2, size[1] / 2, size[2] / 2)
    return superellipsoid(center, half, exp=max(0.08, min(0.6, ex * 0.5)), seg_u=16, seg_v=8)


def _frame(axis):
    axis = norm(axis)
    ref = (0, 0, 1) if abs(axis[2]) < 0.9 else (1, 0, 0)
    u = norm(cross(axis, ref))
    w = cross(axis, u)
    return axis, u, w


def lathe(profile, origin, axis, seg=32, cap_start=False, cap_end=False):
    """profile: [(radius, distance_along_axis)], revolved around axis."""
    ax, u, w = _frame(axis)
    rows = []
    for r, d in profile:
        c = add(origin, mul(ax, d))
        rows.append([add(c, add(mul(u, r * math.cos(2 * math.pi * k / seg)), mul(w, r * math.sin(2 * math.pi * k / seg))))
                     for k in range(seg)])
    v, f = grid(rows, closed_u=True)
    if cap_start:
        c = add(origin, mul(ax, profile[0][1]))
        v.append(c)
        ci = len(v) - 1
        f += [(ci, (k + 1) % seg, k) for k in range(seg)]
    if cap_end:
        c = add(origin, mul(ax, profile[-1][1]))
        v.append(c)
        ci = len(v) - 1
        b = (len(profile) - 1) * seg
        f += [(ci, b + k, b + (k + 1) % seg) for k in range(seg)]
    return make_outward((v, f)) if (cap_start and cap_end) else (v, f)


def cylinder(p0, p1, r, seg=24, caps=True):
    d = sub(p1, p0)
    L = math.sqrt(dot(d, d))
    return lathe([(r, 0), (r, L)], p0, d, seg, caps, caps)


def torus(center, axis, R, r, seg=48, seg2=12):
    ax, u, w = _frame(axis)
    rows = []
    for i in range(seg):
        a = 2 * math.pi * i / seg
        radial = add(mul(u, math.cos(a)), mul(w, math.sin(a)))
        ring = []
        for j in range(seg2):
            b = 2 * math.pi * j / seg2
            p = add(center, add(mul(radial, R + r * math.cos(b)), mul(ax, r * math.sin(b))))
            ring.append(p)
        rows.append(ring)
    return make_outward(grid(rows, closed_u=True, closed_v=True))


def tube(pts, r, sides=6, closed=False, caps=True):
    rings = []
    n = len(pts)
    for i, p in enumerate(pts):
        a = pts[(i - 1) % n] if closed else pts[max(i - 1, 0)]
        b = pts[(i + 1) % n] if closed else pts[min(i + 1, n - 1)]
        ax, u, w = _frame(sub(b, a))
        rings.append([add(p, add(mul(u, r * math.cos(2 * math.pi * k / sides)), mul(w, r * math.sin(2 * math.pi * k / sides))))
                      for k in range(sides)])
    v, f = grid(rings, closed_u=True, closed_v=closed)
    if caps and not closed:
        for idx, ring in ((0, rings[0]), (n - 1, rings[-1])):
            c = pts[idx]
            v.append(c)
            ci = len(v) - 1
            base = idx * sides
            for k in range(sides):
                f.append((ci, base + (k + 1) % sides, base + k) if idx == 0 else (ci, base + k, base + (k + 1) % sides))
    if caps and not closed:
        return make_outward((v, f))
    return v, f


def prism_x(poly_yz, x0, x1):
    """Extrude a (y, z) polygon along X from x0 to x1 (dashboard, consoles).
    The polygon must be star-shaped around its centroid."""
    n = len(poly_yz)
    v = [(x0, y, z) for y, z in poly_yz] + [(x1, y, z) for y, z in poly_yz]
    f = [(i, (i + 1) % n, n + (i + 1) % n, n + i) for i in range(n)]
    cy = sum(p[0] for p in poly_yz) / n
    cz = sum(p[1] for p in poly_yz) / n
    v += [(x0, cy, cz), (x1, cy, cz)]
    a, b = 2 * n, 2 * n + 1
    f += [(a, (i + 1) % n, i) for i in range(n)]
    f += [(b, n + i, n + (i + 1) % n) for i in range(n)]
    return make_outward((v, f))


def quad(center, right, up, w, h):
    """Flat rectangle; normal = right x up."""
    c = center
    r = mul(norm(right), w / 2)
    u = mul(norm(up), h / 2)
    v = [sub(sub(c, r), u), sub(add(c, r), u), add(add(c, r), u), add(sub(c, r), u)]
    return v, [(0, 1, 2, 3)]


def slab(center, right, up, w, h, t):
    """Thin plate (quad with thickness t along its normal)."""
    n = norm(cross(right, up))
    r = mul(norm(right), w / 2)
    u = mul(norm(up), h / 2)
    front = add(center, mul(n, t / 2))
    back = sub(center, mul(n, t / 2))
    v = []
    for c in (back, front):
        v += [sub(sub(c, r), u), sub(add(c, r), u), add(add(c, r), u), add(sub(c, r), u)]
    f = [(4, 5, 6, 7), (3, 2, 1, 0), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    return make_outward((v, f))


def transform(vf, rot=(0, 0, 0), loc=(0, 0, 0)):
    v, f = vf
    return [add(rot_euler(p, rot), loc) for p in v], f


def mirror_x(vf):
    v, f = vf
    return [(-p[0], p[1], p[2]) for p in v], [tuple(reversed(q)) for q in f]


def merge(*vfs):
    V, F = [], []
    for v, f in vfs:
        b = len(V)
        V.extend(v)
        F.extend(tuple(i + b for i in q) for q in f)
    return V, F
