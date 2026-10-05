"""Country road (two lanes, outside settlements) and roadside furniture.

Road modules run along author +Y (Unity +Z) from Socket_Start (0,0,0) to
Socket_End; curves turn right (towards +X), a left curve is the same module
driven the other way. Cross-section (each side from the centre line):
lane 3.5 m | edge line 1.2 + paved strip 0.75 m (asphalt top Y = 0) |
gravel shoulder 1.95 m falling to -0.09 | slope 1:3 | ditch bottom -0.78 m |
back slope | ground 0 at 12.5 m. Total width 25 m.

Markings (ГОСТ Р 51256-2018, line widths and dash/gap lengths of the table;
module lengths chosen so the dash period closes on the module):
1.5 dash 3 / gap 9 m, 1.6 (approach) dash 6 / gap 2 m, 1.1 solid, 1.2 edge.
Signs and delineators: shapes and colours after ГОСТ Р 52290-2004 and
ГОСТ Р 50970-2011, size group II; pictograms are stylised (not traced).
Front of signs and delineators = author +Y (Unity +Z), as in the traffic kit.
"""
import math
from mathutils import Vector, Matrix

from .core import MB, Asset, rng, text_mb, stroke, ccw, arc_pts, to_blender

LANE, PAVED, SHOULDER = 3.5, 4.25, 6.2
HALF = [(0.0, 0.0), (LANE, 0.0), (PAVED, 0.0), (PAVED + 0.02, -0.035), (SHOULDER, -0.09), (7.0, -0.33),
        (7.75, -0.62), (8.35, -0.78), (9.15, -0.78), (9.8, -0.6), (10.6, -0.22), (11.5, -0.03), (12.5, 0.0)]


def profile():
    pts = [(-x, h) for x, h in reversed(HALF[1:])] + HALF
    mats = []
    for (a, _), (b, _) in zip(pts, pts[1:]):
        m = abs((a + b) / 2)
        mats.append('EN_Asphalt' if m < PAVED + 0.02 else 'EN_Gravel' if m < SHOULDER else
                    'EN_Grass_Dry' if 7.75 < m < 9.8 else 'EN_Grass')
    return pts, mats


class Straight:
    def __init__(self, L):
        self.L = L

    def frame(self, s):
        return Vector((0, s, 0)), Vector((1, 0, 0)), Vector((0, 0, 1))

    def heading(self, s):
        return 0.0


class CurveRight:
    def __init__(self, R, deg):
        self.R, self.A = R, math.radians(deg)
        self.L = R * self.A

    def frame(self, s):
        t = s / self.R
        return (Vector((self.R * (1 - math.cos(t)), self.R * math.sin(t), 0)),
                Vector((math.cos(t), -math.sin(t), 0)), Vector((0, 0, 1)))

    def heading(self, s):
        return -s / self.R


def frames(path, step):
    n = max(1, int(math.ceil(path.L / step)))
    return [path.frame(path.L * i / n) for i in range(n + 1)]


def line(mb, path, s0, s1, lat, w, mat='EN_Marking_White', z=0.005, step=1.0):
    s0, s1 = max(0.0, s0), min(path.L, s1)
    if s1 - s0 < 0.05:
        return
    n = max(1, int(math.ceil((s1 - s0) / step)))
    fr = [path.frame(s0 + (s1 - s0) * i / n) for i in range(n + 1)]
    mb.sweep(fr, [(lat - w / 2, z), (lat + w / 2, z)], mat)


def dashed(mb, path, lat, w, dash, gap, phase=0.0):
    s = -phase
    while s < path.L:
        line(mb, path, s, s + dash, lat, w)
        s += dash + gap


def road_module(M, aid, title, path, centre):
    a = Asset(aid, title, 'road', M, min_z=-0.8, centre_line=centre, length=round(path.L, 3),
              required=['COL_DriveSurface', 'Socket_Start', 'Socket_End'])
    prof, mats = profile()
    step = 4.0 if isinstance(path, Straight) else 1.5
    mb = MB()
    mb.sweep(frames(path, step), prof, mats)
    a.mesh(f'{aid}_Surface', mb)
    mk = MB()
    for side in (-1, 1):
        line(mk, path, 0, path.L, side * (LANE + 0.075), 0.15)          # 1.2 edge lines
    if centre == '1.1':
        line(mk, path, 0, path.L, 0.0, 0.12)
    elif centre == '1.5':
        dashed(mk, path, 0.0, 0.12, 3.0, 9.0)
    elif centre == '1.6':
        dashed(mk, path, 0.0, 0.12, 6.0, 2.0)
    a.mesh(f'{aid}_Markings', mk)
    # worn asphalt patches (repairs), a few per module
    R = rng(sum(map(ord, aid)))
    pt = MB()
    for i in range(int(path.L / 14) + 1):
        s = R.uniform(1.5, path.L - 3.0)
        lat = R.uniform(-3.0, 3.0)
        ln, wd = R.uniform(1.0, 2.6), R.uniform(0.6, 1.6)
        if abs(lat) < wd / 2 + 0.1:
            lat = (wd / 2 + 0.2) * (1 if lat >= 0 else -1)
        pt.sweep([path.frame(s), path.frame(s + ln)], [(lat - wd / 2, 0.002), (lat + wd / 2, 0.002)],
                 'EN_Asphalt_Worn')
    a.mesh(f'{aid}_Patches', pt)
    col = MB()
    col.sweep(frames(path, 8.0 if isinstance(path, Straight) else 4.0), prof, 'EN_Collision')
    a.mesh('COL_DriveSurface', col)
    a.socket('Socket_Start', (0, 0, 0))
    end, _, _ = path.frame(path.L)
    a.socket('Socket_End', tuple(end), yaw=path.heading(path.L))
    a.meta['sockets'] = {'Socket_Start': (0, 0, 0), 'Socket_End': to_blender(tuple(end))}
    a.meta['end_heading_deg'] = round(math.degrees(-path.heading(path.L)), 3)
    a.meta['expect_size'] = {'z': (0.78, 0.8)}
    return a


# ---------------------------------------------------------------- roadside
def sign_plate(mb, outer, layers, cz, y=0.0, back='EN_Sign_Back'):
    """outer: polygon in sign coords (a = viewer's right, b = up). Plate face at author y, facing +Y."""
    O, U, V = Vector((0, y, cz)), Vector((-1, 0, 0)), Vector((0, 0, 1))
    mb.plate(ccw(outer), O, U, V, 0.018, back, side_mat=back, back_mat=back)
    for i, (poly, mat) in enumerate(layers):
        off = 0.0012 * (i + 1)
        pts = [O + U * p[0] + V * p[1] + Vector((0, off, 0)) for p in ccw(poly)]
        mb.ngon(pts, mat)


def post(mb, h, y=-0.06, r=0.038):
    mb.cyl((0, y, -0.05), (0, y, h), r, seg=14, mat='EN_Steel_Galv', cap0=False)
    mb.cyl((0, y, h - 0.005), (0, y, h + 0.012), r + 0.004, seg=14, mat='EN_Plastic_Grey')


def clamps(mb, zs, y=-0.06):
    for z in zs:
        mb.box((0, y + 0.025, z), (0.09, 0.07, 0.035), 'EN_Steel_Galv')


def tri(R, rot=90):
    return [(R * math.cos(math.radians(rot + 120 * k)), R * math.sin(math.radians(rot + 120 * k))) for k in range(3)]


def arrow_head(tip, d, size):
    d = Vector(d).normalized()
    n = Vector((-d.y, d.x))
    t = Vector(tip)
    b = t - d * size
    return [tuple(t), tuple(b + n * size * 0.55), tuple(b - n * size * 0.55)]


def curve_pictogram(right=True):
    s = 1 if right else -1
    w = 0.05
    cx = -0.035 * s + 0.1 * s
    pts = [(-0.035 * s, -0.17), (-0.035 * s, -0.02)]
    a0, a1 = (math.pi, math.pi * 0.55) if right else (0.0, math.pi * 0.45)
    pts += arc_pts(cx, -0.02, 0.1, a0, a1, 6)[1:]
    end = Vector(pts[-1])
    d = (end - Vector(pts[-2])).normalized()
    body = stroke(pts[:-1] + [tuple(end - d * 0.03)], w)
    head = arrow_head(tuple(end + d * 0.06), tuple(d), 0.11)
    return [(body, 'EN_Sign_Black'), (head, 'EN_Sign_Black')]


def warning_sign(M, aid, title, right):
    a = Asset(aid, title, 'sign', M, gost='1.11.1' if right else '1.11.2', size_group='II (сторона 900 мм)')
    mb = MB()
    post(mb, 2.75)
    cz = 2.35
    Rout = 0.9 / math.sqrt(3)
    sign_plate(mb, tri(Rout), [(tri(Rout - 0.004), 'EN_Sign_Red'), (tri(Rout - 0.14), 'EN_Sign_White')] +
               curve_pictogram(right), cz)
    clamps(mb, (cz - 0.2, cz + 0.12))
    a.mesh(f'{aid}_Mesh', mb)
    col = MB()
    col.box((0, -0.06, 1.4), (0.1, 0.1, 2.8), 'EN_Collision')
    a.mesh('COL_Post', col)
    return a


def chevron_sign(M, aid, title, right):
    a = Asset(aid, title, 'sign', M, gost='1.34.1' if right else '1.34.2', size_group='II',
              note='пропорции и шевроны стилизованы')
    mb = MB()
    post(mb, 1.95)
    cz = 1.55
    W, H = 0.9, 0.6
    rect = [(-W / 2, -H / 2), (W / 2, -H / 2), (W / 2, H / 2), (-W / 2, H / 2)]
    layers = [([(x * 0.995, y * 0.99) for x, y in rect], 'EN_Sign_Red')]
    s = 1 if right else -1
    for cx in (-0.15, 0.17):
        layers.append((stroke([((cx - 0.09) * s, 0.19), ((cx + 0.08) * s, 0.0), ((cx - 0.09) * s, -0.19)], 0.1),
                       'EN_Sign_White'))
    sign_plate(mb, rect, layers, cz)
    clamps(mb, (cz - 0.18, cz + 0.18))
    a.mesh(f'{aid}_Mesh', mb)
    col = MB()
    col.box((0, -0.06, 1.0), (0.1, 0.1, 2.0), 'EN_Collision')
    a.mesh('COL_Post', col)
    return a


def car_rear(cx, mat):
    body = [(-0.085, -0.06), (0.085, -0.06), (0.085, 0.005), (0.065, 0.015), (0.045, 0.07), (-0.045, 0.07),
            (-0.065, 0.015), (-0.085, 0.005)]
    out = [([(x + cx, y) for x, y in body], mat)]
    for wx in (-0.06, 0.06):
        out.append(([(cx + wx - 0.02, -0.088), (cx + wx + 0.02, -0.088), (cx + wx + 0.02, -0.055),
                     (cx + wx - 0.02, -0.055)], mat))
    return out


def circle(r, n=40, cx=0.0, cy=0.0):
    return [(cx + r * math.cos(2 * math.pi * i / n), cy + r * math.sin(2 * math.pi * i / n)) for i in range(n)]


def no_overtaking(M):
    a = Asset('EN_Sign_3_20_NoOvertaking', 'Знак 3.20 «Обгон запрещён»', 'sign', M, gost='3.20',
              size_group='II (Ø 700 мм)', note='силуэты автомобилей стилизованы')
    mb = MB()
    post(mb, 2.75)
    cz = 2.4
    layers = [(circle(0.348), 'EN_Sign_Red'), (circle(0.27), 'EN_Sign_White')]
    layers += car_rear(-0.1, 'EN_Sign_Red') + car_rear(0.1, 'EN_Sign_Black')
    sign_plate(mb, circle(0.35), layers, cz)
    for cx in (-0.1, 0.1):   # car rear windows (white) on top of the silhouettes
        O = Vector((0, 0.0075, cz))
        poly = ccw([(cx - 0.035, 0.018), (cx + 0.035, 0.018), (cx + 0.028, 0.055), (cx - 0.028, 0.055)])
        mb.ngon([O + Vector((-p[0], 0, p[1])) for p in poly], 'EN_Sign_White')
    clamps(mb, (cz - 0.2, cz + 0.2))
    a.mesh(f'{a.id}_Mesh', mb)
    col = MB()
    col.box((0, -0.06, 1.4), (0.1, 0.1, 2.8), 'EN_Collision')
    a.mesh('COL_Post', col)
    return a


def km_post(M, front='17', back='23'):
    a = Asset(f'EN_Sign_6_13_Km{front}', f'Знак 6.13 «Километровый знак» ({front} / {back})', 'sign', M, gost='6.13')
    mb = MB()
    post(mb, 2.2, y=0.0)
    cz = 1.85
    W, H = 0.42, 0.32
    for y, txt, facing in ((0.045, front, 1), (-0.045, back, -1)):
        O, U, V = Vector((0, y, cz)), Vector((-facing, 0, 0)), Vector((0, 0, 1))
        rect = ccw([(-W / 2, -H / 2), (W / 2, -H / 2), (W / 2, H / 2), (-W / 2, H / 2)])
        mb.plate(rect, O, U, V, 0.012, 'EN_Sign_White', side_mat='EN_Sign_Back', back_mat='EN_Sign_Back')
        nrm = U.cross(V)
        border = [(x * 0.92, yy * 0.88) for x, yy in rect]
        inner = [(x * 0.86, yy * 0.8) for x, yy in rect]
        for poly, m, off in ((border, 'EN_Sign_Black', 0.001), (inner, 'EN_Sign_White', 0.002)):
            mb.ngon([O + U * p[0] + V * p[1] + nrm * off for p in poly], m)
        t = text_mb(txt, 0.2, 'EN_Sign_Black', extrude=0.0)
        rot = Matrix.Rotation(0 if facing > 0 else math.pi, 4, 'Z')
        t.transform(Matrix.Translation(O + nrm * 0.003 + Vector((0, 0, -0.005))) @ rot)
        mb.extend(t)
    a.mesh(f'{a.id}_Mesh', mb)
    col = MB()
    col.box((0, 0, 1.1), (0.1, 0.12, 2.2), 'EN_Collision')
    a.mesh('COL_Post', col)
    return a


def delineator(M):
    """Сигнальный столбик С1 (ГОСТ Р 50970): white post, black band, reflectors —
    red facing traffic for which the post is on the right (front, +Y), white on the back."""
    a = Asset('EN_Road_Delineator', 'Сигнальный столбик С1', 'sign', M, height=1.0)
    mb = MB()
    wb, db = 0.12, 0.1          # width (across the road, X) x depth (along the road, Y) at the bottom
    wt, dt = 0.1, 0.085
    zs = [-0.05, 0.72, 0.94, 1.0]
    mats = ['EN_Plastic_White', 'EN_Sign_Black', 'EN_Plastic_White']
    for i in range(3):
        z0, z1 = zs[i], zs[i + 1]
        f0, f1 = (z0 + 0.05) / 1.05, (z1 + 0.05) / 1.05
        w0, d0 = wb + (wt - wb) * f0, db + (dt - db) * f0
        w1, d1 = wb + (wt - wb) * f1, db + (dt - db) * f1
        top_slope = 0.04 if i == 2 else 0.0      # top falls towards the road (-X)
        v = [(-w0 / 2, -d0 / 2, z0), (w0 / 2, -d0 / 2, z0), (w0 / 2, d0 / 2, z0), (-w0 / 2, d0 / 2, z0),
             (-w1 / 2, -d1 / 2, z1 - top_slope), (w1 / 2, -d1 / 2, z1), (w1 / 2, d1 / 2, z1),
             (-w1 / 2, d1 / 2, z1 - top_slope)]
        f = [(3, 2, 1, 0), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
        mb.add(v, f, mats[i])
    for y, m in ((0.0465, 'EN_Reflector_Red'), (-0.0465, 'EN_Reflector_White')):
        mb.box((0, y, 0.83), (0.045, 0.006, 0.17), m)
    a.mesh(f'{a.id}_Mesh', mb)
    col = MB()
    col.box((0, 0, 0.5), (0.12, 0.1, 1.0), 'EN_Collision')
    a.mesh('COL_Post', col)
    return a


ALL = [
    lambda M: road_module(M, 'EN_Road_Country2_24m', 'Загородная дорога 2 полосы, прямая 24 м, разметка 1.5', Straight(24), '1.5'),
    lambda M: road_module(M, 'EN_Road_Country2_24m_Solid', 'Загородная дорога, прямая 24 м, разметка 1.1', Straight(24), '1.1'),
    lambda M: road_module(M, 'EN_Road_Country2_24m_Approach', 'Загородная дорога, прямая 24 м, линия приближения 1.6', Straight(24), '1.6'),
    lambda M: road_module(M, 'EN_Road_Country2_Curve90_R50', 'Загородная дорога, опасный поворот 90° R50, разметка 1.1', CurveRight(50, 90), '1.1'),
    lambda M: road_module(M, 'EN_Road_Country2_Curve30_R160', 'Загородная дорога, плавный поворот 30° R160, разметка 1.5', CurveRight(160, 30), '1.5'),
    delineator,
    lambda M: warning_sign(M, 'EN_Sign_1_11_1_CurveRight', 'Знак 1.11.1 «Опасный поворот направо»', True),
    lambda M: warning_sign(M, 'EN_Sign_1_11_2_CurveLeft', 'Знак 1.11.2 «Опасный поворот налево»', False),
    lambda M: chevron_sign(M, 'EN_Sign_1_34_1_Right', 'Знак 1.34.1 «Направление поворота» (направо)', True),
    lambda M: chevron_sign(M, 'EN_Sign_1_34_2_Left', 'Знак 1.34.2 «Направление поворота» (налево)', False),
    no_overtaking,
    lambda M: km_post(M),
]
