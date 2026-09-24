"""Procedural exterior skin for DS_Sedan_A. Pure Python (no bpy): the same
geometry is used by the Blender build script and by offline previews.

Source axes (as in the existing DS_Sedan_A.blend): X right, Y forward (+Y is
the nose), Z up, metres. Wheel centres: (+-0.80, +-1.36, 0.34).
"""
import math

# ---------------------------------------------------------------- dimensions
HALF_W = 0.905          # half body width at the shoulder (1.81 m)
Y_NOSE = 2.255          # front face
Y_TAIL = -2.265         # rear face
NOSE_CORNER = (1.58, 3.4)   # plan rounding: start Y, superellipse exponent
TAIL_CORNER = (-1.66, 4.2)
WHEEL_Y = (1.36, -1.36)
WHEEL_Z = 0.34
ARCH_R = 0.405          # skin opening radius (tyre radius 0.327)
SILL_Z = 0.215
NOSE_BOTTOM = 0.205
TAIL_BOTTOM = 0.235
COWL_Y = 1.0            # hood rear edge
DECK_Y = -1.31          # boot lid front edge
SPLIT_FRONT_DOOR = 0.955   # wing / front door (clear of the arch)
SPLIT_B = -0.20            # front / rear door
SPLIT_REAR_DOOR = -1.24    # rear door / rear quarter
SPLIT_NOSE = 1.96          # wing / front bumper
SPLIT_TAIL = -2.0          # rear quarter / rear bumper


def smooth(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def interp(keys, v):
    if v <= keys[0][0]:
        return keys[0][1]
    for (a, fa), (b, fb) in zip(keys, keys[1:]):
        if v <= b:
            return fa + (fb - fa) * smooth((v - a) / (b - a))
    return keys[-1][1]


# ------------------------------------------------------------- plan outline
def _quadrant(y0, y_end, n, count):
    """Superellipse quarter from (HALF_W, y0) to (0, y_end)."""
    b = y_end - y0
    pts = []
    for i in range(count + 1):
        th = (math.pi / 2) * i / count
        c, s = math.cos(th), math.sin(th)
        x = HALF_W * (c ** (2.0 / n))
        y = y0 + b * (s ** (2.0 / n))
        pts.append((x, y))
    return pts


def _resample(poly, step):
    d = [0.0]
    for (x0, y0), (x1, y1) in zip(poly, poly[1:]):
        d.append(d[-1] + math.hypot(x1 - x0, y1 - y0))
    total = d[-1]
    n = max(2, int(round(total / step)))
    out, j = [], 0
    for i in range(n + 1):
        t = total * i / n
        while j < len(d) - 2 and d[j + 1] < t:
            j += 1
        f = (t - d[j]) / (d[j + 1] - d[j] or 1)
        out.append((poly[j][0] + (poly[j + 1][0] - poly[j][0]) * f,
                    poly[j][1] + (poly[j + 1][1] - poly[j][1]) * f))
    return out


def right_outline(step=0.028):
    """Right half outline from rear centre to front centre, (x, y, nx, ny)."""
    rear = _quadrant(TAIL_CORNER[0], Y_TAIL, TAIL_CORNER[1], 400)[::-1]  # centre -> side
    front = _quadrant(NOSE_CORNER[0], Y_NOSE, NOSE_CORNER[1], 400)       # side -> centre
    poly = rear + front[1:]
    # extra density across the arches keeps the opening round
    pts = _resample(poly, step)
    extra = []
    for p, q in zip(pts, pts[1:]):
        extra.append(p)
        mid_y = (p[1] + q[1]) / 2
        if p[0] > HALF_W - 0.01 and any(abs(mid_y - wy) < ARCH_R + 0.05 for wy in WHEEL_Y):
            extra.append(((p[0] + q[0]) / 2, mid_y))
    extra.append(pts[-1])
    out = []
    for i, (x, y) in enumerate(extra):
        a = extra[max(i - 1, 0)]
        b = extra[min(i + 1, len(extra) - 1)]
        tx, ty = b[0] - a[0], b[1] - a[1]
        l = math.hypot(tx, ty) or 1
        nx, ny = ty / l, -tx / l          # outward for this winding
        if i == 0:
            nx, ny = 0.0, -1.0
        if i == len(extra) - 1:
            nx, ny = 0.0, 1.0
        out.append((x, y, nx, ny))
    return out


# ------------------------------------------------------------ heights
TOP_KEYS = [(-2.27, .895), (-1.95, .912), (-1.30, .92), (-.20, .912),
            (1.0, .905), (1.55, .872), (1.95, .835), (2.26, .805)]


def z_top(y):
    return interp(TOP_KEYS, y)


def z_bottom(x, y, ny):
    end = abs(ny)
    base = SILL_Z
    if y > 0:
        base = SILL_Z + (NOSE_BOTTOM - SILL_Z) * smooth((y - 1.85) / 0.35)
    else:
        base = SILL_Z + (TAIL_BOTTOM - SILL_Z) * smooth((-y - 1.85) / 0.35)
    if abs(x) > 0.55:  # the arch cut only exists on the flanks
        for wy in WHEEL_Y:
            d = abs(y - wy)
            if d < ARCH_R:
                base = max(base, WHEEL_Z + math.sqrt(ARCH_R ** 2 - d * d))
            elif d < ARCH_R + 0.06:    # jamb rounds into the sill (r = 6 cm)
                k = 1 - (d - ARCH_R) / 0.06
                base = max(base, base + (WHEEL_Z - base) * (1 - math.sqrt(max(0.0, 1 - k * k))))
    return base


# ------------------------------------------------------------ section
SIDE_KEYS = [(0.0, -.075), (.215, -.062), (.26, -.035), (.31, -.014), (.40, -.003),
             (.46, 0.0), (.655, 0.0), (.685, -.004), (.74, -.009), (.82, -.016), (.93, -.028)]


def offset(z, ztop, end, x, y):
    o = interp(SIDE_KEYS, z)
    # Ends: stronger tuck at the valance and a rounded leading edge into the lid.
    if end > 0:
        k = end ** 1.5
        o -= k * 0.05 * smooth((0.36 - z) / 0.16)
        top = smooth((z - 0.62) / max(ztop - 0.62, 1e-3))
        o -= k * 0.065 * top * top
    # Bumpers: a body-colour band standing proud of the ends, wrapping round
    # the corners and dying out before the wheel arches.
    q = smooth((y - 1.80) / 0.30) if y > 0 else smooth((-y - 1.86) / 0.30)
    if q > 0:
        lo, hi = (.225, .565) if y > 0 else (.245, .585)
        band = smooth((z - lo) / 0.035) * smooth((hi - z) / 0.022)
        o += q * 0.02 * band
    # Rolled lip around the wheel openings.
    if abs(x) > 0.6:
        for wy in WHEEL_Y:
            r = math.hypot(y - wy, z - WHEEL_Z)
            if z > WHEEL_Z - 0.05 and r < ARCH_R + 0.14:
                o += 0.016 * math.exp(-((r - ARCH_R - 0.012) / 0.045) ** 2)
    return o


def skin_point(pt, t):
    x, y, nx, ny = pt
    zb = z_bottom(x, y, ny)
    zt = z_top(y)
    z = zb + (zt - zb) * t
    o = offset(z, zt, abs(ny), x, y)
    return (x + nx * o, y + ny * o, z)


ROWS = [i / 18 for i in range(19)]


def full_loop():
    right = right_outline()
    left = [(-x, y, -nx, ny) for x, y, nx, ny in right]
    # left: rear-centre -> front-centre as well; loop = left reversed + right
    return right, left


# ------------------------------------------------------------ mesh helpers
class Mesh:
    def __init__(self, name, material, solidify=0.0, smooth=True):
        self.name, self.material, self.solidify, self.smooth = name, material, solidify, smooth
        self.v, self.f = [], []
        self.tube_axis = None

    def grid(self, rows, flip=False):
        """rows: list of lists of points (same length)."""
        base = len(self.v)
        m = len(rows[0])
        for r in rows:
            self.v.extend(r)
        for i in range(len(rows) - 1):
            for j in range(m - 1):
                a = base + i * m + j
                q = (a, a + 1, a + m + 1, a + m)
                self.f.append(tuple(reversed(q)) if flip else q)
        return self


def tube_mesh(name, pts, radius, material, sides=6, closed=False):
    me = Mesh(name, material)
    rings = []
    n = len(pts)
    for i, p in enumerate(pts):
        a = pts[max(i - 1, 0)] if not closed else pts[(i - 1) % n]
        b = pts[min(i + 1, n - 1)] if not closed else pts[(i + 1) % n]
        t = [b[k] - a[k] for k in range(3)]
        l = math.sqrt(sum(c * c for c in t)) or 1
        t = [c / l for c in t]
        ref = (0, 0, 1) if abs(t[2]) < 0.9 else (1, 0, 0)
        u = _cross(t, ref); u = _norm(u)
        w = _cross(t, u)
        ring = []
        for k in range(sides + 1):
            an = 2 * math.pi * k / sides
            ring.append(tuple(p[c] + radius * (math.cos(an) * u[c] + math.sin(an) * w[c]) for c in range(3)))
        rings.append(ring)
    if closed:
        rings.append(rings[0])
    me.grid(rings)
    me.tube_axis = pts[0]
    f = me.f[0]
    n = _face_normal(me.v, f)
    c = [sum(me.v[i][k] for i in f) / len(f) for k in range(3)]
    ctr = [(pts[0][k] + pts[1][k]) / 2 for k in range(3)]
    if sum(n[k] * (c[k] - ctr[k]) for k in range(3)) < 0:
        me.f = [tuple(reversed(q)) for q in me.f]
    return me


def _cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def _norm(a):
    l = math.sqrt(sum(c * c for c in a)) or 1
    return tuple(c / l for c in a)


# ------------------------------------------------------------ builder
def build():
    right, left = full_loop()
    meshes = []

    def side_columns(pts):
        return [[skin_point(p, t) for t in ROWS] for p in pts]

    def panel(name, pts, flip, flange=False):
        cols = side_columns(pts)
        if flange:  # window-sill return towards the glass
            for c, p in zip(cols, pts):
                x, y, z = c[-1]
                c.append((x - p[2] * 0.028, y - p[3] * 0.028, z - 0.004))
        rows = [list(r) for r in zip(*cols)]
        me = Mesh(name, 'Paint_Atlantic', 0.012).grid(rows, flip)
        meshes.append(me)
        return cols

    def select(pts, lo, hi, side_only=True):
        out = [p for p in pts if lo <= p[1] <= hi and (not side_only or p[0] > 0.3 or p[0] < -0.3)]
        return out

    seam_cols = {}
    for side, pts, flip in (('R', right, False), ('L', left, True)):
        def rng(lo, hi):
            # contiguous index range; boundary points shared with neighbours
            ia = min(range(len(pts)), key=lambda i: abs(pts[i][1] - lo) + (0 if abs(pts[i][0]) > 0.6 else 9))
            ib = min(range(len(pts)), key=lambda i: abs(pts[i][1] - hi) + (0 if abs(pts[i][0]) > 0.6 else 9))
            return pts[ia:ib + 1]
        panel('FrontWing_' + side, rng(SPLIT_FRONT_DOOR, SPLIT_NOSE), flip)
        c = panel('DoorFront_' + side, rng(SPLIT_B, SPLIT_FRONT_DOOR), flip, True)
        seam_cols[('front', side)] = c[-1]
        seam_cols[('b', side)] = c[0]
        c = panel('DoorRear_' + side, rng(SPLIT_REAR_DOOR, SPLIT_B), flip, True)
        seam_cols[('rear', side)] = c[0]
        panel('RearQuarter_' + side, rng(SPLIT_TAIL, SPLIT_REAR_DOOR), flip)

    # Bumper caps: both halves in one object.
    iN = min(range(len(right)), key=lambda i: abs(right[i][1] - SPLIT_NOSE) + (0 if right[i][0] > 0.6 else 9))
    iT = min(range(len(right)), key=lambda i: abs(right[i][1] - SPLIT_TAIL) + (0 if right[i][0] > 0.6 else 9))
    for label, sl in (('BumperFacia_1', slice(iN, None)), ('BumperFacia_-1', slice(0, iT + 1))):
        r = right[sl]
        l = left[sl]
        if label.endswith('_-1'):
            r, l = r[::-1], l[::-1]
        loop = l + r[::-1][1:]          # left flank -> centre -> right flank
        cols = [[skin_point(p, t) for t in ROWS] for p in loop]
        rows = [list(rr) for rr in zip(*cols)]
        flip = label.endswith('_1')
        meshes.append(Mesh(label, 'Paint_Atlantic', 0.012).grid(rows, not flip))

    # Hood and boot lid follow the top edge of the skin with a crowned section.
    def lid(name, pts_r, crown, reverse):
        edge = [skin_point(p, 1.0) for p in pts_r]
        rows = []
        for (x, y, z) in edge:
            row = []
            for j in range(25):
                u = -1 + j / 12
                c = crown * min(1.0, (x / 0.86)) ** 1.3
                row.append((u * x, y, z + c * (1 - u * u)))
            rows.append(row)
        meshes.append(Mesh(name, 'Paint_Atlantic', 0.012).grid(rows, reverse))
        return edge

    hood_pts = [p for p in right if p[1] >= COWL_Y]
    hood_edge = lid('Hood', hood_pts, 0.042, True)
    deck_pts = [p for p in right if p[1] <= DECK_Y][::-1]
    deck_edge = lid('Trunk', deck_pts, 0.034, False)

    # Shut lines and door gaps as fine dark tubes, generated once each.
    gaps = []
    def col_line(col, lift=0.0015):
        return [(x, y, z) for x, y, z in col]
    for side, pts in (('R', right), ('L', left)):
        for key in ('front', 'b', 'rear'):
            col = seam_cols[(key, side)]
            gaps.append(tube_mesh('PanelGap_' + key + '_' + side, col_line(col[:len(ROWS)]), 0.0022, 'Rubber', 5))
        for y0 in (SPLIT_NOSE, SPLIT_TAIL):
            p = min(pts, key=lambda q: abs(q[1] - y0))
            col = [skin_point(p, t) for t in ROWS]
            gaps.append(tube_mesh('PanelGap_bumper_' + ('F' if y0 > 0 else 'R') + '_' + side, col, 0.0022, 'Rubber', 5))
    for name, edge in (('Hood', hood_edge), ('Trunk', deck_edge)):
        line = [(-x, y, z + 0.002) for x, y, z in reversed(edge)] + [(x, y, z + 0.002) for x, y, z in edge[1:]]
        gaps.append(tube_mesh('PanelGap_' + name, line, 0.0025, 'Rubber', 5))
    meshes.extend(gaps)

    # Wheel-arch liners from the skin opening edge inboard to the wheelhouse.
    for side, pts in (('R', right), ('L', left)):
        for wy, label in ((WHEEL_Y[0], 'Front'), (WHEEL_Y[1], 'Rear')):
            edge = [skin_point(p, 0.0) for p in pts if abs(p[1] - wy) <= ARCH_R and abs(p[0]) > 0.55]
            edge = [e for e in edge if e[2] > WHEEL_Z - 0.001]
            rows = [edge, [(math.copysign(0.65, e[0]), e[1], e[2]) for e in edge]]
            me = Mesh('WheelArchLiner_' + label + '_' + side, 'Rubber')
            me.grid(rows, side == 'R')
            meshes.append(me)

    # Flat undertray inset from the tucked bottom edge.
    ring = [skin_point(p, 0.0) for p in right]
    ring = [(max(0.0, x - 0.02) if abs(x) > 0.55 else x, y, z) for x, y, z in ring]
    under = Mesh('Shell_Undertray', 'Interior_Graphite')
    z0 = 0.245
    pts_r = [(min(x, 0.84), y) for x, y, z in ring if abs(p_y := y) < 2.2 or True]
    # strip across the car between the two halves
    rows = [[(-x, y, z0) for x, y in pts_r], [(x, y, z0) for x, y in pts_r]]
    under.grid([[(-x * 0.97, y, z0) for x, y in pts_r], [(x * 0.97, y, z0) for x, y in pts_r]], True)
    meshes.append(under)

    meshes.extend(decals(right, left))
    for m in meshes:
        orient(m)
    return meshes


def _face_normal(V, f):
    a, b, c = V[f[0]], V[f[1]], V[f[2]]
    n = _cross([b[k] - a[k] for k in range(3)], [c[k] - a[k] for k in range(3)])
    if abs(n[0]) + abs(n[1]) + abs(n[2]) < 1e-14 and len(f) > 3:
        d = V[f[3]]
        n = _cross([c[k] - a[k] for k in range(3)], [d[k] - b[k] for k in range(3)])
    return n


def orient(m):
    """Make every mesh face the viewer (Unity culls back faces)."""
    V = m.v
    score = 0.0
    for f in m.f:
        n = _face_normal(V, f)
        c = [sum(V[i][k] for i in f) / len(f) for k in range(3)]
        if m.name.startswith('WheelArchLiner'):
            wy = WHEEL_Y[0] if c[1] > 0 else WHEEL_Y[1]
            ref = (c[0] * 0, wy - c[1], WHEEL_Z - c[2])        # towards the tyre
        elif m.name == 'Shell_Undertray':
            ref = (0, 0, -1)
        elif m.tube_axis is not None:
            ref = (c[0] - m.tube_axis[0], c[1] - m.tube_axis[1], c[2] - m.tube_axis[2]) if False else None
            continue
        else:
            ref = (c[0], c[1] - max(-1.2, min(1.2, c[1])), c[2] - 0.55)
        score += sum(n[k] * ref[k] for k in range(3))
    if score < 0:
        m.f = [tuple(reversed(f)) for f in m.f]


# ------------------------------------------------------------ decals
def _frac_point(pts, x_target, front):
    """Outline point on the end cap nearest to |x| = x_target (right half)."""
    cand = [p for p in pts if (p[1] > 1.5 if front else p[1] < -1.5)]
    return min(cand, key=lambda p: abs(p[0] - x_target))


def conform_patch(name, material, right, s_from, s_to, z_lo, z_hi, lift, front, both=True,
                  nu=14, nv=5, shape=None):
    """A patch lying on the skin. s_from/s_to: outline indices on the right half."""
    out = []
    for side in ((1, -1) if both else (1,)):
        me = Mesh(name + ('_R' if side > 0 else '_L') if both else name, material)
        rows = []
        for j in range(nv + 1):
            v = j / nv
            row = []
            for i in range(nu + 1):
                u = i / nu
                idx = s_from + (s_to - s_from) * u
                k = int(math.floor(idx)); f = idx - k
                k2 = min(k + 1, len(right) - 1)
                p = tuple(right[k][c] + (right[k2][c] - right[k][c]) * f for c in range(4))
                lo, hi = (z_lo, z_hi) if shape is None else shape(u)
                z = lo + (hi - lo) * v
                x, y, nx, ny = p
                zt = z_top(y)
                o = offset(z, zt, abs(ny), x, y) + lift
                pt = (side * (x + nx * o), y + ny * o, z)
                row.append(pt)
            rows.append(row)
        flip = (side > 0) != front
        me.grid(rows, flip)
        out.append(me)
    return out


def index_near(right, pred):
    return min(range(len(right)), key=pred)


def decals(right, left):
    out = []
    n = len(right)
    fi = lambda xt: index_near(right, lambda i: abs(right[i][0] - xt) + (0 if right[i][1] > 1.5 else 9))
    ri = lambda xt: index_near(right, lambda i: abs(right[i][0] - xt) + (0 if right[i][1] < -1.5 else 9))
    fy = lambda yt: index_near(right, lambda i: abs(right[i][1] - yt) + (0 if right[i][0] > 0.6 else 9))

    # ---- front
    # Headlamp: wraps from x=0.40 round the corner to the wing, swept top edge.
    s0, s1 = fi(0.40), fy(1.93)
    out += conform_patch('Headlight_Bezel', 'Rubber', right, s0, s1, .640, .742, .003, True,
                         shape=lambda u: (.640 + .010 * u, .742 - .006 * u * u))
    out += conform_patch('Headlight', 'Lamp_White', right, s0 + 0.3 * (s1 - s0) / 3, s1 - 1.5, .655, .728, .006, True,
                         nu=10, nv=3, shape=lambda u: (.655 + .010 * u, .728 - .006 * u * u))
    out += conform_patch('TurnSignal_Front', 'Lamp_Amber', right, s1 - 1.4, s1 - 0.2, .655, .700, .0065, True, nu=4, nv=2)
    # Grille between the lamps, bars and lower intake.
    out += conform_patch('Grille_Recess', 'Rubber', right, fi(0.0), fi(0.34), .515, .612, .002, True, both=True,
                         nu=8, nv=4)
    for k in range(4):
        z = .530 + k * .024
        pts = []
        for i in range(17):
            u = -1 + i / 8
            p = right[fi(abs(u) * 0.33)]
            o = offset(z, z_top(p[1]), 1, p[0], p[1]) + .006
            pts.append((u * 0.33, p[1] + p[3] * o, z))
        out.append(tube_mesh('GrilleBar', pts, .0045, 'Chrome', 6))
    out += conform_patch('Intake_Lower', 'Rubber', right, fi(0.0), fi(0.52), .252, .352, .002, True, nu=10, nv=3,
                         shape=lambda u: (.252 + .02 * u * u, .352 - .015 * u * u))
    # ---- rear
    t0, t1 = ri(0.42), fy(-1.97)
    out += conform_patch('Taillight', 'Lamp_Red', right, t1 + 0.2, t0, .705, .825, .005, False,
                         shape=lambda u: (.705 + .012 * (1 - u), .825 - .004 * (1 - u)))
    out += conform_patch('TurnSignal_Rear', 'Lamp_Amber', right, t0 - 0.9, t0, .705, .825, .0055, False, nu=3, nv=3)
    out += conform_patch('ReverseLight', 'Lamp_White', right, ri(0.30), ri(0.40), .725, .805, .004, False, nu=4, nv=3)
    out += conform_patch('Diffuser', 'Rubber', right, ri(0.0), ri(0.62), .262, .322, .002, False, nu=12, nv=2)
    # Protective side mouldings on the doors (typical of the era, breaks up the flank).
    for label, lo, hi in (('DoorFront', SPLIT_B, SPLIT_FRONT_DOOR), ('DoorRear', -0.985, SPLIT_B)):
        j0 = index_near(right, lambda i: abs(right[i][1] - lo - 0.012) + (0 if right[i][0] > .6 else 9))
        j1 = index_near(right, lambda i: abs(right[i][1] - hi + 0.012) + (0 if right[i][0] > .6 else 9))
        for m in conform_patch('SideMoulding_' + label, 'Rubber', right, j0, j1, .468, .512, .007, False,
                               nu=12, nv=3, shape=lambda u: (.468, .512)):
            out.append(m)
    # Door handles (recess + grip), just under the shoulder line.
    for side, pts in (('R', right), ('L', left)):
        for label, y0 in (('DoorFront', -0.02), ('DoorRear', -1.02)):
            i0 = index_near(pts, lambda i: abs(pts[i][1] - y0) + (0 if abs(pts[i][0]) > .6 else 9))
            i1 = index_near(pts, lambda i: abs(pts[i][1] - y0 - 0.17) + (0 if abs(pts[i][0]) > .6 else 9))
            sgn = 1 if side == 'R' else -1
            sub = right
            j0 = index_near(right, lambda i: abs(right[i][1] - y0) + (0 if right[i][0] > .6 else 9))
            j1 = index_near(right, lambda i: abs(right[i][1] - y0 - 0.17) + (0 if right[i][0] > .6 else 9))
            rec = conform_patch('Handle_' + label + '_Recess', 'Rubber', right, j0, j1, .775, .815, .001, False, both=False, nu=4, nv=2)
            grip = conform_patch('Handle_' + label + '_' + side, 'Paint_Atlantic', right, j0 + 0.4, j1 - 0.4, .783, .806, .009, False,
                                 both=False, nu=4, nv=2)
            for m in rec + grip:
                if sgn < 0:
                    m.v = [(-x, y, z) for x, y, z in m.v]
                    m.f = [tuple(reversed(f)) for f in m.f]
                m.name = m.name if m.name.endswith(side) else m.name + '_' + side
                out.append(m)
    return out


# Flat number plates (GOST 520 x 112 mm); text is added by the Blender script.
PLATES = {
    'front': dict(center=(0.0, None, 0.455), size=(0.52, 0.112)),
    'rear': dict(center=(0.0, None, 0.475), size=(0.52, 0.112)),
}


def plate_quads():
    right, _ = full_loop()
    res = {}
    for key, front in (('front', True), ('rear', False)):
        c = PLATES[key]
        z = c['center'][2]
        p = right[-1] if front else right[0]
        o = offset(z, z_top(p[1]), 1, p[0], p[1])
        y = p[1] + p[3] * o + (0.008 if front else -0.008)
        w, h = c['size']
        res[key] = (y, z, w, h)
    return res
