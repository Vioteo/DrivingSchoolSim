"""Country road (two lanes, outside settlements), road signs and the dangerous-curve furniture.

Road modules run along author +Y (Unity +Z) from Socket_Start (0,0,0) to Socket_End.
Curves turn right (towards +X); a left curve is the same module driven the other way.

Cross-section from the centre line: lane 3.5 m | edge line 1.2 + paved strip to 4.25 m
(asphalt top Y = 0 on straights) | gravel shoulder to 6.2 m | slope | ditch bottom -0.78 m |
back slope | ground 0 at 12.5 m. Total width 25 m.

Curves are built like a real road: transition spirals (clothoids, curvature grows linearly)
on both sides of the circular arc, superelevation (вираж: the carriageway and shoulders are
rotated about the centre line towards the inside of the curve, rising linearly along the
spiral to the full value on the arc) and widening of the inner lane. Straight ends are flat,
so every module joins any other at Y = 0.

Markings after ГОСТ Р 51256-2018: 1.5 dash 3 / gap 9 m, 1.6 dash 6 / gap 2 m, 1.1 solid,
1.2 edge line 0.15 m. Signs: ГОСТ Р 52290-2004 типоразмер II (triangle 900 mm, circle 700 mm,
1.34.1/2 500 x 615 and 500 x 1160 mm, 6.13 300 x 200 mm); faces are textures from
make_textures.py. Delineator С1 after ГОСТ Р 50970-2011 (white post, black band 250 mm
inclined 30 deg towards the road, red reflector to traffic for which the post is on the right).
Front of signs and delineators = author +Y (Unity +Z), as in the traffic kit.
"""
import math
from mathutils import Vector, Matrix, noise

from .core import MB, Asset, rng, ccw, to_blender, split_by_planes
from . import barrier

LANE, PAVED, SHOULDER, EDGE = 3.5, 4.25, 6.2, 12.5
HALF = [(0.0, 0.0), (LANE, 0.0), (PAVED, 0.0), (PAVED + 0.02, -0.035), (SHOULDER, -0.09), (7.0, -0.33),
        (7.75, -0.62), (8.35, -0.78), (9.15, -0.78), (9.8, -0.6), (10.6, -0.22), (11.5, -0.03), (EDGE, 0.0)]
Z = Vector((0, 0, 1))


def profile():
    pts = [(-x, h) for x, h in reversed(HALF[1:])] + HALF
    mats = []
    for (a, _), (b, _) in zip(pts, pts[1:]):
        m = abs((a + b) / 2)
        mats.append('EN_Asphalt' if m < PAVED + 0.02 else 'EN_Gravel' if m < SHOULDER else
                    'EN_Grass_Dry' if 7.75 < m < 9.8 else 'EN_Grass')
    return pts, mats


def ground_h(lat):
    """Profile height (without superelevation) at a lateral offset."""
    a = abs(lat)
    for (x0, h0), (x1, h1) in zip(HALF, HALF[1:]):
        if a <= x1:
            return h0 + (h1 - h0) * (a - x0) / max(1e-9, x1 - x0)
    return 0.0


# ---------------------------------------------------------------- road path
class RoadPath:
    """Plan line from segments ('line', L) / ('spiral', L, k0, k1) / ('arc', L, k), curvature k > 0 turns
    right. Integrated numerically; heading psi is measured from +Y towards +X."""

    def __init__(self, segments, e_max=0.0, widen=0.0, ds=0.02):
        self.e_max, self.widen = e_max, widen
        s, x, y, psi = 0.0, 0.0, 0.0, 0.0
        self.S, self.X, self.Y, self.P, self.K = [0.0], [0.0], [0.0], [0.0], [0.0]
        for seg in segments:
            kind, L = seg[0], seg[1]
            n = max(1, int(round(L / ds)))
            h = L / n
            for i in range(n):
                u0, u1 = i / n, (i + 1) / n
                if kind == 'line':
                    k0 = k1 = 0.0
                elif kind == 'arc':
                    k0 = k1 = seg[2]
                else:
                    k0 = seg[2] + (seg[3] - seg[2]) * u0
                    k1 = seg[2] + (seg[3] - seg[2]) * u1
                km = (k0 + k1) / 2
                pm = psi + km * h / 2
                x += math.sin(pm) * h
                y += math.cos(pm) * h
                psi += km * h
                s += h
                self.S.append(s)
                self.X.append(x)
                self.Y.append(y)
                self.P.append(psi)
                self.K.append(k1)
        self.L = s
        self.kmax = max(abs(k) for k in self.K) or 1.0

    def _i(self, s):
        if s <= 0:
            return 0, 0.0
        if s >= self.L:
            return len(self.S) - 2, 1.0
        lo, hi = 0, len(self.S) - 1
        while hi - lo > 1:
            m = (lo + hi) // 2
            if self.S[m] <= s:
                lo = m
            else:
                hi = m
        return lo, (s - self.S[lo]) / max(1e-12, self.S[lo + 1] - self.S[lo])

    def at(self, s):
        """(position, tangent, right, curvature) at s; beyond the ends the path continues straight."""
        if s < 0 or s > self.L:
            e = 0 if s < 0 else len(self.S) - 1
            p0 = Vector((self.X[e], self.Y[e], 0))
            psi = self.P[e]
            t = Vector((math.sin(psi), math.cos(psi), 0))
            d = s if s < 0 else s - self.L
            return p0 + t * d, t, Vector((t.y, -t.x, 0)), 0.0
        i, f = self._i(s)
        x = self.X[i] + (self.X[i + 1] - self.X[i]) * f
        y = self.Y[i] + (self.Y[i + 1] - self.Y[i]) * f
        psi = self.P[i] + (self.P[i + 1] - self.P[i]) * f
        k = self.K[i] + (self.K[i + 1] - self.K[i]) * f
        t = Vector((math.sin(psi), math.cos(psi), 0))
        return Vector((x, y, 0)), t, Vector((t.y, -t.x, 0)), k

    def heading(self, s):
        return self.at(s)[1]

    def e(self, k):
        return self.e_max * k / self.kmax

    def point(self, s, lat, h):
        """Surface point: lateral offset with widening on the inside, superelevation about the centre line."""
        p, t, r, k = self.at(s)
        sgn = 1 if k >= 0 else -1
        w = self.widen * abs(k) / self.kmax
        lat_e = lat
        if lat * sgn > 0:
            lat_e = lat + sgn * w * min(1.0, abs(lat) / LANE)
        e = self.e(k)
        a = abs(lat)
        if a <= SHOULDER:
            zb = -lat * e
        else:
            zb = -math.copysign(SHOULDER, lat) * e * max(0.0, (EDGE - a) / (EDGE - SHOULDER))
        return p + r * lat_e + Z * (h + zb)

    def end_yaw(self):
        return -self.P[-1]


def straight(L):
    return RoadPath([('line', L)])


def curve(R, deg, Ls, e_max, widen):
    k = 1.0 / R
    arc = math.radians(deg) - Ls * k        # each spiral turns by Ls / (2R)
    return RoadPath([('spiral', Ls, 0.0, k), ('arc', arc * R, k), ('spiral', Ls, k, 0.0)], e_max, widen)


# ---------------------------------------------------------------- road modules
def surface(path, step, prof, mats, jitter=True, seed=0):
    mb = MB()
    n = max(1, int(math.ceil(path.L / step)))
    np_ = len(prof)
    verts = []
    for i in range(n + 1):
        s = path.L * i / n
        for lat, h in prof:
            if jitter and abs(abs(lat) - PAVED) < 0.03:      # broken asphalt edge
                lat = lat + math.copysign(0.05 * noise.noise(Vector((s * 0.35, lat, seed))), lat)
            verts.append(path.point(s, lat, h))
    faces, fms = [], []
    for i in range(n):
        for j in range(np_ - 1):
            faces.append((i * np_ + j, i * np_ + j + 1, (i + 1) * np_ + j + 1, (i + 1) * np_ + j))
            fms.append(mats[j])
    mb.add(verts, faces, mats[0], face_mats=fms)
    return mb


def line(mb, path, s0, s1, lat, w, mat='EN_Marking_White', z=0.006, step=1.0):
    s0, s1 = max(0.0, s0), min(path.L, s1)
    if s1 - s0 < 0.05:
        return
    n = max(1, int(math.ceil((s1 - s0) / step)))
    verts = []
    for i in range(n + 1):
        s = s0 + (s1 - s0) * i / n
        verts += [path.point(s, lat - w / 2, z), path.point(s, lat + w / 2, z)]
    faces = [(2 * i, 2 * i + 1, 2 * i + 3, 2 * i + 2) for i in range(n)]
    mb.add(verts, faces, mat)


def dashed(mb, path, lat, w, dash, gap):
    s = 0.0
    while s < path.L:
        line(mb, path, s, s + dash, lat, w)
        s += dash + gap


def road_module(M, aid, title, path, centre):
    a = Asset(aid, title, 'road', M, min_z=-1.1 if path.e_max else -0.8, centre_line=centre, length=round(path.L, 3),
              superelevation=path.e_max, widening=path.widen,
              required=['COL_DriveSurface', 'Socket_Start', 'Socket_End'])
    prof, mats = profile()
    curved = path.kmax > 1e-6 and any(abs(k) > 1e-6 for k in path.K)
    a.mesh(f'{aid}_Surface', surface(path, 1.0 if curved else 4.0, prof, mats, seed=len(aid)))
    mk = MB()
    for side in (-1, 1):
        line(mk, path, 0, path.L, side * (LANE + 0.075), 0.15)
    if centre == '1.1':
        line(mk, path, 0, path.L, 0.0, 0.12)
    elif centre == '1.5':
        dashed(mk, path, 0.0, 0.12, 3.0, 9.0)
    elif centre == '1.6':
        dashed(mk, path, 0.0, 0.12, 6.0, 2.0)
    a.mesh(f'{aid}_Markings', mk)
    # repaired patches (lighter asphalt) and darker wheel tracks
    R = rng(sum(map(ord, aid)))
    pt = MB()
    for i in range(int(path.L / 12) + 1):
        s = R.uniform(1.0, path.L - 3.0)
        lat = R.choice((-1, 1)) * R.uniform(0.6, 2.6)
        ln, wd = R.uniform(1.0, 3.0), R.uniform(0.5, 1.4)
        line(pt, path, s, s + ln, lat, wd, 'EN_Asphalt_Worn', z=0.002, step=0.5)
    a.mesh(f'{aid}_Patches', pt)
    col = MB()
    col.extend(surface(path, 2.0 if curved else 8.0, prof, ['EN_Collision'] * len(mats), jitter=False))
    a.mesh('COL_DriveSurface', col)
    a.socket('Socket_Start', (0, 0, 0))
    end, _, _, _ = path.at(path.L)
    a.socket('Socket_End', tuple(end), yaw=path.end_yaw())
    a.meta['sockets'] = {'Socket_Start': (0, 0, 0), 'Socket_End': to_blender(tuple(end))}
    a.meta['end_heading_deg'] = round(math.degrees(path.P[-1]), 3)
    return a


# ---------------------------------------------------------------- signs
SIGN_FRONT_Y = 0.0
PLATE = 0.022          # folded edge depth
POST_R = 0.038         # pipe 76 mm


def rounded_triangle(A, r, n=8):
    rho = A / (2 * math.sqrt(3))
    d_c = 2 * (rho - r)
    pts = []
    for k in range(3):
        a = math.radians(90 + 120 * k)
        cx, cy = math.cos(a) * d_c, math.sin(a) * d_c
        for i in range(n + 1):
            t = a - math.radians(60) + math.radians(120) * i / n
            pts.append((cx + r * math.cos(t), cy + r * math.sin(t)))
    return pts


def rounded_rect(W, H, r, n=5):
    pts = []
    for cx, cy, a0 in ((W / 2 - r, -H / 2 + r, -90), (W / 2 - r, H / 2 - r, 0), (-W / 2 + r, H / 2 - r, 90),
                       (-W / 2 + r, -H / 2 + r, 180)):
        for i in range(n + 1):
            t = math.radians(a0 + 90 * i / n)
            pts.append((cx + r * math.cos(t), cy + r * math.sin(t)))
    return pts


def sign_shape(kind, w_mm, h_mm):
    """Outline (m, relative to the texture bbox centre) and the texture bbox (W, H) in m."""
    if kind == 'triangle':
        A = w_mm
        H = A * math.sqrt(3) / 2 + 2
        dy = -H / 2 + A / (2 * math.sqrt(3))
        pts = [(x, y + dy) for x, y in rounded_triangle(A, 58.5)]
        return [(x / 1000, y / 1000) for x, y in pts], (A / 1000, H / 1000)
    if kind == 'circle':
        D = w_mm
        return [(D / 2000 * math.cos(2 * math.pi * i / 48), D / 2000 * math.sin(2 * math.pi * i / 48))
                for i in range(48)], (D / 1000, D / 1000)
    r = 30 if h_mm >= 400 else 12
    return [(x / 1000, y / 1000) for x, y in rounded_rect(w_mm, h_mm, r)], (w_mm / 1000, h_mm / 1000)


def sign_plate_mb(kind, w_mm, h_mm, face):
    """Sign plate in its own frame: centre of the texture bbox at the origin, face in the XZ plane at y = 0
    looking +Y, folded edge PLATE deep behind it."""
    pts, (W, H) = sign_shape(kind, w_mm, h_mm)
    pts = ccw(pts)
    mb = MB()
    # front: viewer looks along -Y, his right is -X -> x_world = -a
    front = [(-a, SIGN_FRONT_Y, b) for a, b in pts]
    uv_front = [((a + W / 2) / W, (b + H / 2) / H) for a, b in pts]
    n = len(pts)
    o = mb.add(front, [tuple(range(n))], face, uvs=[uv_front])
    backv = [(-a, SIGN_FRONT_Y - PLATE, b) for a, b in pts]
    mb.add(backv, [tuple(reversed(range(n)))], 'EN_Sign_Back')
    rim_v = front + backv
    rim_f = [((i + 1) % n, i, n + i, n + (i + 1) % n) for i in range(n)]
    mb.add(rim_v, rim_f, 'EN_Sign_Back')
    return mb, (W, H), pts


def mount(mb, z_rails, half_widths, y_post):
    """Mounting rails behind the plate and band clamps round the post."""
    yb = SIGN_FRONT_Y - PLATE
    for z, hw in zip(z_rails, half_widths):
        mb.box((0, yb - 0.012, z), (2 * hw, 0.024, 0.035), 'EN_Steel_Galv')
        mb.cyl((0, y_post, z - 0.022), (0, y_post, z + 0.022), POST_R + 0.007, seg=14, mat='EN_Steel_Galv')


def post(mb, top, y, ground=0.0):
    mb.cyl((0, y, ground - 0.05), (0, y, top), POST_R, seg=16, mat='EN_Steel_Galv', cap0=False, cap1=False)
    mb.lathe([(POST_R + 0.003, top), (POST_R + 0.003, top + 0.012), (0.02, top + 0.03), (0.0, top + 0.034)],
             (0, y, 0), 16, 'EN_Plastic_Grey')


def sign_mb(kind, w_mm, h_mm, face, bottom, back=None, post_extra=0.08):
    """Complete sign on a post in the asset frame (post on the ground near the origin, face looking +Y).
    back: a second sign on the other side of the same post, facing -Y (opposite direction of travel)."""
    plate, (W, H), pts = sign_plate_mb(kind, w_mm, h_mm, face)
    ymin = min(b for _, b in pts)
    ymax = max(b for _, b in pts)
    lift = bottom - ymin
    y_post = SIGN_FRONT_Y - PLATE - 0.024 - POST_R
    h = ymax - ymin
    zr = [bottom + h * 0.25, bottom + h * 0.72] if h > 0.35 else [bottom + h * 0.5]

    def half_width(z):
        zz = z - lift
        xs = []
        for (a0, b0), (a1, b1) in zip(pts, pts[1:] + pts[:1]):
            if (b0 - zz) * (b1 - zz) <= 0 and b0 != b1:
                xs.append(a0 + (a1 - a0) * (zz - b0) / (b1 - b0))
        return 0.8 * (max(xs) - min(xs)) / 2 if len(xs) >= 2 else 0.1
    one = MB()
    plate.transform(Matrix.Translation((0, 0, lift)))
    one.extend(plate)
    mount(one, zr, [half_width(z) for z in zr], y_post)
    mb = MB()
    mb.extend(one)
    if back:
        plate2, _, _ = sign_plate_mb(kind, w_mm, h_mm, back)
        two = MB()
        plate2.transform(Matrix.Translation((0, 0, lift)))
        two.extend(plate2)
        mount(two, zr, [half_width(z) for z in zr], y_post)
        # mirror position through the post axis: turn 180 deg about the post
        mb.extend(two, Matrix.Translation((0, 2 * y_post, 0)) @ Matrix.Rotation(math.pi, 4, 'Z'))
    top = lift + ymax + post_extra
    post(mb, top, y_post)
    return mb, y_post, top


def sign_asset(M, aid, title, kind, w_mm, h_mm, face, bottom, back=None, **meta):
    a = Asset(aid, title, 'sign', M, **meta)
    mb, y_post, top = sign_mb(kind, w_mm, h_mm, face, bottom, back)
    a.mesh(f'{aid}_Mesh', mb)
    col = MB()
    col.box((0, y_post, top / 2), (0.1, 0.1, top), 'EN_Collision')
    a.mesh('COL_Post', col)
    return a


# ---------------------------------------------------------------- delineator
def delineator_mb():
    """Сигнальный столбик С1: white post 1.0 m above the ground, black band 250 mm wide inclined 30 deg,
    falling towards the road (+X in the asset frame: the post stands to the right of the traffic it faces),
    red reflector on the front band, white on the back."""
    mb = MB()
    wb, db, wt, dt = 0.12, 0.10, 0.10, 0.085
    z0, z1 = -0.05, 1.0
    v = [(-wb / 2, -db / 2, z0), (wb / 2, -db / 2, z0), (wb / 2, db / 2, z0), (-wb / 2, db / 2, z0),
         (-wt / 2, -dt / 2, z1), (wt / 2, -dt / 2, z1 - 0.025), (wt / 2, dt / 2, z1 - 0.025), (-wt / 2, dt / 2, z1)]
    f = [(3, 2, 1, 0), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    mb.add(v, f, 'EN_Plastic_White')
    k = math.tan(math.radians(30))
    n = Vector((k, 0, 1)).normalized()
    zc = 0.80
    band = 0.25 / math.cos(math.radians(30))
    mb = split_by_planes(mb, [((0, 0, zc - band / 2), tuple(n)), ((0, 0, zc + band / 2), tuple(n))], 'EN_Sign_Black')
    for y, m in ((dt / 2 + 0.0035, 'EN_Reflector_Red'), (-(dt / 2 + 0.0035), 'EN_Reflector_White')):
        mb.box((0, y, zc), (0.04, 0.004, 0.13), m)
    return mb


def delineator(M):
    a = Asset('EN_Road_Delineator', 'Сигнальный столбик С1', 'sign', M, height=1.0)
    a.mesh(f'{a.id}_Mesh', delineator_mb())
    col = MB()
    col.box((0, 0, 0.5), (0.12, 0.1, 1.0), 'EN_Collision')
    a.mesh('COL_Post', col)
    return a


# ---------------------------------------------------------------- dangerous curve furniture
def frame_matrix(pos, x_axis, y_axis):
    x, y = Vector(x_axis).normalized(), Vector(y_axis).normalized()
    return Matrix(((x.x, y.x, 0, pos[0]), (x.y, y.y, 0, pos[1]), (x.z, y.z, 1, pos[2]), (0, 0, 0, 1)))


def curve_furniture(M, aid, title, path, ext=14.0):
    """Barrier along the outer edge (exactly on the curve, with superelevation), direction signs 1.34.1
    (back-to-back with 1.34.2 for the opposite direction) behind it, delineators on the inner edge and
    beyond the barrier. Same origin as the road module: place both at the same point."""
    a = Asset(aid, title, 'barrier', M, min_z=-1.1, budget=90000, road=aid.replace('_Furniture', ''))
    sgn = 1 if max(path.K) > 0 else -1           # right curve: outer edge is on the left (lat < 0)
    lat_b = -sgn * 5.85
    # --- barrier: sample its post line, parametrise by its own length, run against the traffic
    ss = []
    s = path.L + ext
    while s > -ext:
        ss.append(s)
        s -= 0.25
    ss.append(-ext)
    P = [path.point(s_, lat_b, ground_h(lat_b)) for s_ in ss]
    acc = [0.0]
    for i in range(1, len(P)):
        acc.append(acc[-1] + (P[i] - P[i - 1]).length)
    Lb = acc[-1]

    def bframe(t):
        t = max(0.0, min(Lb, t))
        i = max(0, min(len(acc) - 2, next((k for k in range(len(acc) - 1) if acc[k + 1] >= t), len(acc) - 2)))
        f = (t - acc[i]) / max(1e-9, acc[i + 1] - acc[i])
        p = P[i].lerp(P[i + 1], f)
        yv = (P[i + 1] - P[i])
        yv.z = 0
        yv.normalize()
        _, _, r, _ = path.at(ss[i])
        xv = Vector((r.x, r.y, 0)) * (-sgn)              # away from the road
        # keep the frame orthogonal to the running direction
        xv = (xv - yv * xv.dot(yv)).normalized()
        return p, xv, yv
    a.mesh(f'{aid}_Barrier', barrier.run_mb(bframe, Lb))
    col = MB()
    t = 1.0
    while t < Lb:
        p, xv, yv = bframe(t)
        tmp = MB()
        tmp.box((-(barrier.SPACER + 0.04), 0, barrier.BEAM_C - 0.05), (0.13, 2.05, 0.45), 'EN_Collision')
        tmp.box((0.0, 0, 0.2), (0.06, 2.05, 0.4), 'EN_Collision')
        col.extend(tmp, frame_matrix(p, xv, yv))
        t += 2.0
    a.mesh('COL_Barrier', col)
    # --- direction signs on the outer edge, facing the traffic that turns into the curve
    signs = MB()
    lat_s = -sgn * 6.6
    s = 10.0
    count = 0
    while s < path.L - 8.0:
        p = path.point(s, lat_s, ground_h(lat_s))
        _, tng, r, _ = path.at(s)
        face_dir = (-tng + r * sgn * 0.3).normalized()          # towards drivers coming into the curve
        yv = face_dir
        xv = yv.cross(Z).normalized()
        mbs, _, _ = sign_mb('rect', 615, 500, 'EN_SignFace_1_34_1_x1' if sgn > 0 else 'EN_SignFace_1_34_2_x1', 1.25,
                            back='EN_SignFace_1_34_2_x1' if sgn > 0 else 'EN_SignFace_1_34_1_x1')
        signs.extend(mbs, frame_matrix(p, xv, yv))
        col2 = MB()
        col2.box((0, -0.07, 0.8), (0.1, 0.1, 1.6), 'EN_Collision')
        col.extend(col2, frame_matrix(p, xv, yv))
        count += 1
        s += 12.5
    a.mesh(f'{aid}_Signs', signs)
    # --- delineators: inner edge all along, outer edge beyond the barrier
    dl = MB()
    base = delineator_mb()
    s = -ext - 24.0
    while s <= path.L + ext + 24.0:
        for side in (1, -1):
            lat = side * sgn * 6.0       # side = 1: inner edge
            if side == -1 and -ext - 2 < s < path.L + ext + 2:
                continue
            p = path.point(s, lat, ground_h(lat))
            _, tng, r, _ = path.at(s)
            on_right = lat > 0
            yv = -tng if on_right else tng          # front (red) towards the traffic for which it is on the right
            xv = yv.cross(Z).normalized()
            dl.extend(base, frame_matrix(p, xv, yv))
        s += 6.0 if 0 <= s <= path.L else 12.0
    a.mesh(f'{aid}_Delineators', dl)
    a.socket('Socket_Start', (0, 0, 0))
    a.meta.update(barrier_length=round(Lb, 2), direction_signs=count)
    return a


# ---------------------------------------------------------------- catalogue
P_STRAIGHT = straight(24)
P_CURVE90 = curve(50, 90, 35, 0.06, 0.7)
P_CURVE30 = curve(160, 30, 25, 0.03, 0.0)

ALL = [
    lambda M: road_module(M, 'EN_Road_Country2_24m', 'Загородная дорога 2 полосы, прямая 24 м, разметка 1.5',
                          P_STRAIGHT, '1.5'),
    lambda M: road_module(M, 'EN_Road_Country2_24m_Solid', 'Загородная дорога, прямая 24 м, разметка 1.1',
                          P_STRAIGHT, '1.1'),
    lambda M: road_module(M, 'EN_Road_Country2_24m_Approach', 'Загородная дорога, прямая 24 м, линия приближения 1.6',
                          P_STRAIGHT, '1.6'),
    lambda M: road_module(M, 'EN_Road_Country2_Curve90_R50',
                          'Загородная дорога, опасный поворот 90° R50: переходные кривые 35 м, вираж 6 %, уширение 0,7 м',
                          P_CURVE90, '1.1'),
    lambda M: curve_furniture(M, 'EN_Road_Country2_Curve90_R50_Furniture',
                              'Обустройство опасного поворота R50: ограждение, знаки 1.34, столбики', P_CURVE90),
    lambda M: road_module(M, 'EN_Road_Country2_Curve30_R160',
                          'Загородная дорога, плавный поворот 30° R160: переходные кривые 25 м, вираж 3 %',
                          P_CURVE30, '1.5'),
    delineator,
    lambda M: sign_asset(M, 'EN_Sign_1_11_1_CurveRight', 'Знак 1.11.1 «Опасный поворот» (направо)', 'triangle',
                         900, 0, 'EN_SignFace_1_11_1', 1.6, gost='1.11.1', size_group='II'),
    lambda M: sign_asset(M, 'EN_Sign_1_11_2_CurveLeft', 'Знак 1.11.2 «Опасный поворот» (налево)', 'triangle',
                         900, 0, 'EN_SignFace_1_11_2', 1.6, gost='1.11.2', size_group='II'),
    lambda M: sign_asset(M, 'EN_Sign_1_34_1_Right', 'Знак 1.34.1 «Направление поворота», 500 × 615', 'rect',
                         615, 500, 'EN_SignFace_1_34_1_x1', 1.0, gost='1.34.1', size_group='II'),
    lambda M: sign_asset(M, 'EN_Sign_1_34_2_Left', 'Знак 1.34.2 «Направление поворота», 500 × 615', 'rect',
                         615, 500, 'EN_SignFace_1_34_2_x1', 1.0, gost='1.34.2', size_group='II'),
    lambda M: sign_asset(M, 'EN_Sign_1_34_1_Right_x2', 'Знак 1.34.1 «Направление поворота», 500 × 1160', 'rect',
                         1160, 500, 'EN_SignFace_1_34_1_x2', 1.0, gost='1.34.1', size_group='II'),
    lambda M: sign_asset(M, 'EN_Sign_1_34_2_Left_x2', 'Знак 1.34.2 «Направление поворота», 500 × 1160', 'rect',
                         1160, 500, 'EN_SignFace_1_34_2_x2', 1.0, gost='1.34.2', size_group='II'),
    lambda M: sign_asset(M, 'EN_Sign_3_20_NoOvertaking', 'Знак 3.20 «Обгон запрещён»', 'circle', 700, 700,
                         'EN_SignFace_3_20', 1.6, gost='3.20', size_group='II'),
    lambda M: sign_asset(M, 'EN_Sign_6_13_Km17', 'Знак 6.13 «Километровый знак» (17 / 23)', 'rect', 300, 200,
                         'EN_SignFace_6_13_17', 1.3, back='EN_SignFace_6_13_23', gost='6.13', size_group='II'),
]
