"""Yard furniture of a Russian apartment-block courtyard: playground, benches,
waste site, barrier gate, lamp, metal garages, lawn fences, bollards.

Origin = centre of the footprint on the ground unless noted; front = author +Y
(Unity +Z): the side a person faces when using the object (bench seat, slide
exit, garage doors, gate boom side). Sizes are typical, not normative.
"""
import math
from mathutils import Vector, Matrix

from .core import MB, Asset, rng, text_mb, to_blender


def pipe(mb, pts, r, mat, seg=10):
    mb.tube(pts, [r] * len(pts), seg, mat, cap0=True, cap1=True)


def bend(p0, p1, p2, r=0.12, n=4):
    """Polyline p0 -> p1 -> p2 with the corner at p1 rounded."""
    a, b, c = Vector(p0), Vector(p1), Vector(p2)
    d0, d1 = (a - b).normalized(), (c - b).normalized()
    t0, t1 = b + d0 * r, b + d1 * r
    pts = [a]
    for i in range(n + 1):
        u = i / n
        pts.append(t0.lerp(b, u).lerp(b.lerp(t1, u), u))
    pts.append(c)
    return pts


def col_box(a, c, s, name='COL_Body'):
    col = MB()
    col.box(c, s, 'EN_Collision')
    return a.mesh(name, col)


# ---------------------------------------------------------------- seating and small items
def bench(M):
    a = Asset('EN_Yard_Bench', 'Скамейка со спинкой', 'yard', M)
    mb = MB()
    L = 1.8
    for x in (-0.7, 0.7):
        pipe(mb, [(x, 0.22, 0.0), (x, 0.2, 0.42)], 0.025, 'EN_Paint_Black')
        pipe(mb, [(x, -0.2, 0.0), (x, -0.18, 0.42), (x, -0.26, 0.85)], 0.025, 'EN_Paint_Black')
        mb.box((x, 0.01, 0.405), (0.05, 0.46, 0.04), 'EN_Paint_Black')
        mb.box((x, 0.21, 0.01), (0.08, 0.1, 0.02), 'EN_Paint_Black')
        mb.box((x, -0.2, 0.01), (0.08, 0.1, 0.02), 'EN_Paint_Black')
    for i in range(5):   # seat slats
        mb.box((0, 0.2 - i * 0.095, 0.445), (L, 0.08, 0.035), 'EN_Wood')
    for i in range(3):   # backrest slats, leaning back
        z = 0.56 + i * 0.11
        y = -0.2 - (z - 0.5) * 0.19
        mb.obox((0, y, z), (L, 0.025, 0.085), 0.0, 'EN_Wood', pitch=math.radians(-11))
    a.mesh(f'{a.id}_Mesh', mb)
    col_box(a, (0, 0, 0.45), (L, 0.5, 0.9))
    return a


def bin_(M):
    a = Asset('EN_Yard_Bin', 'Урна опрокидывающаяся', 'yard', M)
    mb = MB()
    for x in (-0.24, 0.24):
        pipe(mb, [(x, 0, -0.05), (x, 0, 0.8)], 0.022, 'EN_Paint_Green')
    mb.cyl((-0.24, 0, 0.6), (0.24, 0, 0.6), 0.012, seg=8, mat='EN_Steel_Dark')
    mb.lathe([(0.0, 0.25), (0.15, 0.25), (0.19, 0.3), (0.2, 0.62), (0.205, 0.66), (0.19, 0.66), (0.18, 0.63),
              (0.17, 0.3), (0.0, 0.29)], (0, 0, 0), 20, 'EN_Paint_Green', cap_top=False)
    a.mesh(f'{a.id}_Mesh', mb)
    col_box(a, (0, 0, 0.4), (0.55, 0.42, 0.8))
    return a


def bollard_sphere(M):
    a = Asset('EN_Yard_Bollard_Sphere', 'Бетонная полусфера', 'yard', M)
    mb = MB()
    prof = [(0.26 * math.cos(math.radians(t)), 0.26 * math.sin(math.radians(t)) * 0.95) for t in range(0, 91, 10)]
    mb.lathe([(0.27, -0.03)] + prof, (0, 0, 0), 20, 'EN_Concrete', cap_top=False)
    mb.lathe([(0.2665, 0.04), (0.255, 0.075)], (0, 0, 0.0), 20, 'EN_Paint_Yellow', cap_top=False)
    a.mesh(f'{a.id}_Mesh', mb)
    col = MB()
    col.ico((0, 0, 0), 0.26, 2, 'EN_Collision', scale=(1, 1, 0.95))
    a.mesh('COL_Body', col)
    return a


def tire_bed(M):
    R = rng(5)
    a = Asset('EN_Yard_TireFlowerbed', 'Клумба из покрышки', 'yard', M, collider=False)
    mb = MB()
    rr, w = 0.31, 0.2
    prof = [(0.2, -0.05), (0.22, w / 2 - 0.02), (rr - 0.02, w / 2), (rr, w / 2 - 0.03), (rr + 0.01, 0.0),
            (rr, -w / 2 + 0.03), (rr - 0.02, -w / 2), (0.24, -w / 2 + 0.02)]
    mb.lathe(prof, (0, 0, 0.05), 24, 'EN_Paint_White', cap_top=False)
    mb.lathe([(0.21, 0.13), (0.0, 0.15)], (0, 0, 0), 16, 'EN_Soil')
    for i in range(14):
        ang, rad = R.uniform(0, 6.28), R.uniform(0, 0.17)
        c = (math.cos(ang) * rad, math.sin(ang) * rad, 0.2 + R.uniform(0, 0.08))
        mb.ico(c, 0.045, 2, R.choice(('EN_Plastic_Red', 'EN_Plastic_Yellow', 'EN_Plastic_Red', 'EN_Plastic_White')),
               jitter=0.2, seed=i)
        mb.cyl((c[0], c[1], 0.13), c, 0.006, seg=4, mat='EN_Paint_Green', cap0=False)
    for i in range(6):
        ang = R.uniform(0, 6.28)
        mb.ico((math.cos(ang) * 0.1, math.sin(ang) * 0.1, 0.16), 0.08, 2, 'EN_Plastic_Green', scale=(1, 1, 0.6), jitter=0.3,
               seed=i)
    a.mesh(f'{a.id}_Mesh', mb)
    return a


def lawn_fence(M):
    a = Asset('EN_Yard_LawnFence_2m', 'Газонное ограждение (дуги), 2 м', 'yard', M,
              required=['Socket_Start', 'Socket_End'], note='идёт вдоль +X (Unity +X)')
    mb = MB()
    for x0 in (0.0, 1.0):
        pts = [(x0 + 0.5 - 0.5 * math.cos(math.pi * i / 12), 0, -0.05 + 0.55 * math.sin(math.pi * i / 12))
               for i in range(13)]
        pipe(mb, pts, 0.014, 'EN_Paint_Green', seg=8)
    for x0 in (0.5, 1.5):    # second row of lower hoops, offset by half a step
        pts = [(x0 - 0.5 + 0.5 * (1 - math.cos(math.pi * i / 12)), 0, -0.05 + 0.4 * math.sin(math.pi * i / 12))
               for i in range(13)]
        pipe(mb, pts, 0.012, 'EN_Paint_Green', seg=8)
    pipe(mb, [(0, 0, 0.3), (2.0, 0, 0.3)], 0.01, 'EN_Paint_Green', seg=6)
    a.mesh(f'{a.id}_Mesh', mb)
    col_box(a, (1.0, 0, 0.25), (2.0, 0.05, 0.5))
    a.socket('Socket_Start', (0, 0, 0))
    a.socket('Socket_End', (2.0, 0, 0))
    a.meta['sockets'] = {'Socket_Start': (0, 0, 0), 'Socket_End': to_blender((2.0, 0, 0))}
    return a


# ---------------------------------------------------------------- playground
def playground_pad(M):
    a = Asset('EN_Yard_PlaygroundPad', 'Площадка с резиновым покрытием 14 × 10 м', 'yard', M, min_z=-0.15)
    mb = MB()
    W, D = 14.0, 10.0
    mb.box((0, 0, -0.06), (W, D, 0.12), 'EN_Rubber_Crumb')
    for cx, cy, r in ((-3.5, 1.0, 2.2), (4.0, -1.5, 1.8)):
        mb.ngon([(cx + r * math.cos(2 * math.pi * i / 32), cy + r * math.sin(2 * math.pi * i / 32), 0.003)
                 for i in range(32)], 'EN_Rubber_Crumb_Green')
    for (x0, x1, y0, y1) in ((-W / 2 - 0.08, W / 2 + 0.08, D / 2, D / 2 + 0.08),
                             (-W / 2 - 0.08, W / 2 + 0.08, -D / 2 - 0.08, -D / 2),
                             (-W / 2 - 0.08, -W / 2, -D / 2, D / 2), (W / 2, W / 2 + 0.08, -D / 2, D / 2)):
        mb.box_between(x0, x1, y0, y1, -0.15, 0.08, 'EN_Curb')
    a.mesh(f'{a.id}_Mesh', mb)
    col = MB()
    col.box((0, 0, -0.05), (W, D, 0.1), 'EN_Collision')
    a.mesh('COL_DriveSurface', col)
    return a


def swing(M):
    a = Asset('EN_Yard_Swing', 'Качели двойные', 'yard', M)
    mb = MB()
    H, L = 2.25, 3.0
    for x in (-L / 2, L / 2):     # A-frames
        pipe(mb, [(x, -0.75, -0.05), (x, 0, H)], 0.04, 'EN_Paint_Blue')
        pipe(mb, [(x, 0.75, -0.05), (x, 0, H)], 0.04, 'EN_Paint_Blue')
        pipe(mb, [(x, -0.38, 1.1), (x, 0.38, 1.1)], 0.025, 'EN_Paint_Blue')
    pipe(mb, [(-L / 2 - 0.1, 0, H), (L / 2 + 0.1, 0, H)], 0.045, 'EN_Paint_Yellow')
    for cx, col in ((-0.7, 'EN_Plastic_Red'), (0.7, 'EN_Plastic_Green')):
        for dx in (-0.22, 0.22):
            mb.cyl((cx + dx, 0, H - 0.05), (cx + dx, 0, 0.5), 0.008, seg=5, mat='EN_Chain')
            mb.box((cx + dx, 0, H - 0.045), (0.03, 0.06, 0.05), 'EN_Steel_Dark')
        mb.box((cx, 0, 0.47), (0.5, 0.22, 0.04), col)
        if cx < 0:   # toddler seat with a backrest
            mb.box((cx, -0.1, 0.62), (0.5, 0.02, 0.26), col)
    a.mesh(f'{a.id}_Mesh', mb)
    col = MB()
    for x in (-L / 2, L / 2):
        col.box((x, 0, H / 2), (0.1, 1.5, H), 'EN_Collision')
    col.box((0, 0, H), (L, 0.1, 0.1), 'EN_Collision')
    a.mesh('COL_Frame', col)
    return a


def slide(M):
    a = Asset('EN_Yard_Slide', 'Горка с лесенкой и крышей', 'yard', M)
    mb = MB()
    h, s = 1.2, 0.6
    for x in (-s, s):
        for y in (-s, s):
            pipe(mb, [(x, y, -0.05), (x, y, h + 1.1)], 0.04, 'EN_Paint_Red')
    mb.box((0, 0, h - 0.02), (2 * s + 0.1, 2 * s + 0.1, 0.05), 'EN_Wood_Light')
    for x in (-s, s):
        pipe(mb, [(x, -s, h + 0.6), (x, s, h + 0.6)], 0.025, 'EN_Paint_Yellow')
        mb.box((x, 0, h + 0.3), (0.03, 2 * s, 0.5), 'EN_Plastic_Blue')
    rv = [(-s - 0.15, -s - 0.15, h + 1.1), (s + 0.15, -s - 0.15, h + 1.1), (s + 0.15, s + 0.15, h + 1.1),
          (-s - 0.15, s + 0.15, h + 1.1), (0, 0, h + 1.65)]
    mb.add(rv, [(0, 1, 4), (1, 2, 4), (2, 3, 4), (3, 0, 4), (3, 2, 1, 0)], 'EN_Plastic_Red')
    for x in (-0.3, 0.3):     # ladder at the back (-Y)
        pipe(mb, [(x, -s - 0.9, -0.05), (x, -s, h + 0.7)], 0.025, 'EN_Paint_Yellow')
    for i in range(5):
        t = (i + 1) / 6
        mb.box((0, -s - 0.9 + 0.9 * t, h * t + 0.05), (0.6, 0.12, 0.03), 'EN_Wood_Light')
    # chute towards +Y
    L, n = 2.8, 12
    fr = []
    for i in range(n + 1):
        t = i / n
        z = 0.3 + (h - 0.3) * (1 - t) ** 1.6 if t < 0.9 else 0.3
        fr.append((Vector((0, s + L * t, z)), Vector((1, 0, 0)), Vector((0, 0, 1))))
    prof = [(-0.33, 0.22), (-0.3, 0.22), (-0.27, 0.0), (0.27, 0.0), (0.3, 0.22), (0.33, 0.22), (0.3, -0.03),
            (-0.3, -0.03)]
    from .core import cw
    mb.sweep(fr, cw(prof), 'EN_Steel_Galv', closed=True)
    for x in (-0.25, 0.25):
        pipe(mb, [(x, s + L - 0.2, -0.05), (x, s + L - 0.2, 0.27)], 0.03, 'EN_Paint_Red')
    a.mesh(f'{a.id}_Mesh', mb)
    col = MB()
    col.box((0, 0, (h + 1.65) / 2), (1.4, 1.4, h + 1.65), 'EN_Collision')
    col.box((0, s + L / 2, 0.45), (0.7, L, 0.9), 'EN_Collision')
    a.mesh('COL_Body', col)
    return a


def sandbox(M):
    a = Asset('EN_Yard_Sandbox', 'Песочница с грибком', 'yard', M)
    mb = MB()
    S = 1.0
    for i in range(4):   # boards with a seat plank on top
        t = MB()
        t.box((0, S, 0.15), (2 * S + 0.1, 0.05, 0.3), 'EN_Paint_Yellow' if i % 2 else 'EN_Paint_Green')
        t.box((0, S - 0.05, 0.31), (2 * S + 0.1, 0.22, 0.03), 'EN_Wood_Light')
        mb.extend(t, Matrix.Rotation(i * math.pi / 2, 4, 'Z'))
    mb.box((0, 0, 0.1), (2 * S - 0.05, 2 * S - 0.05, 0.18), 'EN_Sand')
    mb.ico((0.3, -0.25, 0.2), 0.25, 3, 'EN_Sand', scale=(1, 1, 0.3), jitter=0.2)
    mb.cyl((0, 0, 0.15), (0, 0, 2.0), 0.05, seg=10, mat='EN_Paint_White')
    prof = [(1.25, 1.85), (1.1, 1.97), (0.7, 2.18), (0.2, 2.3), (0.0, 2.32)]
    mb.lathe([(0.0, 1.85), prof[0]], (0, 0, 0), 16, 'EN_Paint_White', cap_top=False)
    mb.lathe(prof, (0, 0, 0), 16, 'EN_Paint_Red', cap_top=False)
    R = rng(3)
    for i in range(9):   # white spots on the cap
        ang, rad = R.uniform(0, 6.28), R.uniform(0.35, 0.95)
        z = 2.32 - 0.47 * (rad / 1.25) ** 1.5
        mb.ico((math.cos(ang) * rad, math.sin(ang) * rad, z), 0.09, 2, 'EN_Paint_White', scale=(1, 1, 0.35))
    a.mesh(f'{a.id}_Mesh', mb)
    col = MB()
    col.box((0, 0, 0.16), (2 * S + 0.1, 2 * S + 0.1, 0.32), 'EN_Collision')
    col.cyl((0, 0, 0), (0, 0, 2.0), 0.06, seg=6, mat='EN_Collision')
    a.mesh('COL_Body', col)
    return a


def carousel(M):
    a = Asset('EN_Yard_Carousel', 'Карусель', 'yard', M)
    mb = MB()
    mb.cyl((0, 0, -0.05), (0, 0, 0.35), 0.08, seg=12, mat='EN_Steel_Dark')
    mb.lathe([(0.0, 0.32), (0.85, 0.32), (0.88, 0.35), (0.88, 0.38), (0.0, 0.38)], (0, 0, 0), 32, 'EN_Steel_Galv')
    for k in range(4):
        ang = k * math.pi / 2
        c, s_ = math.cos(ang), math.sin(ang)
        col = ('EN_Paint_Red', 'EN_Paint_Yellow', 'EN_Paint_Blue', 'EN_Paint_Green')[k]
        mb.ngon([(0, 0, 0.385)] + [(0.86 * math.cos(ang - 0.785 + 1.57 * j / 6), 0.86 * math.sin(ang - 0.785 + 1.57 * j / 6),
                                    0.385) for j in range(7)], col)
        pts = bend((0.15 * c, 0.15 * s_, 0.38), (0.15 * c, 0.15 * s_, 0.95), (0.7 * c, 0.7 * s_, 0.95), 0.1)
        pipe(mb, pts, 0.022, 'EN_Paint_Red')
        pipe(mb, bend((0.4 * c, 0.4 * s_, 0.95), (0.7 * c, 0.7 * s_, 0.95), (0.7 * c, 0.7 * s_, 0.38), 0.1), 0.022,
             'EN_Paint_Red')
    pipe(mb, [(0, 0, 0.38), (0, 0, 1.0)], 0.04, 'EN_Paint_Red')
    mb.lathe([(0.07, 1.0), (0.0, 1.06)], (0, 0, 0), 12, 'EN_Paint_Yellow')
    a.mesh(f'{a.id}_Mesh', mb)
    col = MB()
    col.cyl((0, 0, 0), (0, 0, 1.0), 0.88, seg=12, mat='EN_Collision')
    a.mesh('COL_Body', col)
    return a


def sports_frame(M):
    a = Asset('EN_Yard_SportsFrame', 'Турник с рукоходом', 'yard', M)
    mb = MB()
    L, H = 3.2, 2.2
    for x in (-L / 2, L / 2):
        for y in (-0.3, 0.3):
            pipe(mb, [(x, y, -0.05), (x, y, H)], 0.045, 'EN_Paint_Green')
    for y in (-0.3, 0.3):
        pipe(mb, [(-L / 2, y, H), (L / 2, y, H)], 0.03, 'EN_Paint_Green')
    for i in range(10):
        x = -L / 2 + 0.2 + i * (L - 0.4) / 9
        pipe(mb, [(x, -0.3, H), (x, 0.3, H)], 0.016, 'EN_Steel_Galv', seg=8)
    pipe(mb, [(L / 2 + 0.6, 0, -0.05), (L / 2 + 0.6, 0, 2.1)], 0.045, 'EN_Paint_Green')
    pipe(mb, [(L / 2, 0.3, 2.0), (L / 2 + 0.6, 0, 2.0)], 0.016, 'EN_Steel_Galv', seg=8)
    pipe(mb, [(L / 2 + 0.6, 0, 1.6), (L / 2, -0.3, 1.6)], 0.016, 'EN_Steel_Galv', seg=8)
    a.mesh(f'{a.id}_Mesh', mb)
    col = MB()
    for x in (-L / 2, L / 2):
        col.box((x, 0, H / 2), (0.1, 0.7, H), 'EN_Collision')
    col.box((L / 2 + 0.6, 0, 1.05), (0.1, 0.1, 2.1), 'EN_Collision')
    a.mesh('COL_Frame', col)
    return a


# ---------------------------------------------------------------- utilities
def corrugated(mb, x0, y0, x1, y1, z0, z1, mat, pitch=0.115, depth=0.02):
    """Profiled sheet wall from (x0, y0) to (x1, y1), double-sided."""
    d = Vector((x1 - x0, y1 - y0, 0))
    L = d.length
    d.normalize()
    nrm = Vector((-d.y, d.x, 0))
    n = max(2, int(L / pitch))
    prof = [Vector((x0, y0, 0)) + d * (L * i / n) + nrm * (depth if i % 2 else 0.0) for i in range(n + 1)]
    verts = [(p.x, p.y, z0) for p in prof] + [(p.x, p.y, z1) for p in prof]
    m = len(prof)
    faces = []
    for i in range(m - 1):
        faces.append((i, i + 1, m + i + 1, m + i))
        faces.append((m + i, m + i + 1, i + 1, i))
    mb.add(verts, faces, mat)


def euro_container(body, lid):
    c = MB()
    c.add([(-0.62, -0.5, 0.2), (0.62, -0.5, 0.2), (0.62, 0.45, 0.2), (-0.62, 0.45, 0.2),
           (-0.68, -0.55, 1.3), (0.68, -0.55, 1.3), (0.68, 0.55, 1.3), (-0.68, 0.55, 1.3)],
          [(3, 2, 1, 0), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)], body)
    c.add([(-0.72, -0.6, 1.3), (0.72, -0.6, 1.3), (0.72, 0.6, 1.3), (-0.72, 0.6, 1.3),
           (-0.72, -0.6, 1.36), (0.72, -0.6, 1.36), (0.72, 0.4, 1.42), (-0.72, 0.4, 1.42),
           (0.72, 0.6, 1.36), (-0.72, 0.6, 1.36)],
          [(3, 2, 1, 0), (0, 1, 5, 4), (4, 5, 6, 7), (7, 6, 8, 9), (1, 2, 8, 6, 5), (3, 0, 4, 7, 9),
           (2, 3, 9, 8)], lid)
    c.box((0, 0.56, 1.1), (1.1, 0.06, 0.05), 'EN_Plastic_Grey')
    for wx in (-0.5, 0.5):
        for wy in (-0.35, 0.35):
            c.cyl((wx - 0.03, wy, 0.1), (wx + 0.03, wy, 0.1), 0.095, seg=12, mat='EN_Rubber')
            c.box((wx, wy, 0.17), (0.08, 0.1, 0.06), 'EN_Steel_Dark')
    return c


def waste_site(M):
    a = Asset('EN_Yard_WasteSite', 'Контейнерная площадка (3 евроконтейнера)', 'yard', M, min_z=-0.12)
    mb = MB()
    W, D, H = 5.6, 2.2, 1.9
    mb.box((0, 0, -0.05), (W + 0.3, D + 0.4, 0.14), 'EN_Concrete')
    corrugated(mb, -W / 2, -D / 2, W / 2, -D / 2, 0.1, H, 'EN_Profiled_Sheet')
    corrugated(mb, -W / 2, D / 2, -W / 2, -D / 2, 0.1, H, 'EN_Profiled_Sheet')
    corrugated(mb, W / 2, -D / 2, W / 2, D / 2, 0.1, H, 'EN_Profiled_Sheet')
    for x, y in ((-W / 2, -D / 2), (W / 2, -D / 2), (-W / 2, D / 2), (W / 2, D / 2), (0, -D / 2)):
        mb.box((x, y, H / 2), (0.06, 0.06, H + 0.1), 'EN_Paint_Grey')
    for z in (0.35, H - 0.1):
        mb.box((0, -D / 2 + 0.03, z), (W, 0.03, 0.04), 'EN_Paint_Grey')
    for i, x in enumerate((-1.75, 0.0, 1.75)):
        c = euro_container('EN_Plastic_Green' if i != 1 else 'EN_Plastic_Blue',
                           'EN_Plastic_Grey' if i != 1 else 'EN_Plastic_Yellow')
        c.transform(Matrix.Translation((x, 0.02, 0.02)))
        mb.extend(c)
    a.mesh(f'{a.id}_Mesh', mb)
    col = MB()
    col.box((0, -D / 2, H / 2), (W, 0.06, H), 'EN_Collision')
    col.box((-W / 2, 0, H / 2), (0.06, D, H), 'EN_Collision')
    col.box((W / 2, 0, H / 2), (0.06, D, H), 'EN_Collision')
    col.box((0, 0.02, 0.7), (5.0, 1.2, 1.4), 'EN_Collision')
    a.mesh('COL_Body', col)
    return a


def gate(M):
    a = Asset('EN_Yard_BarrierGate', 'Шлагбаум въезда во двор (стрела 4,6 м)', 'yard', M,
              required=['Boom', 'COL_Boom'],
              note='начало координат объекта Boom — ось стрелы; подъём — поворот Boom вокруг оси въезда (author Y, Unity Z); 0° = закрыт')
    mb = MB()
    mb.box((0, 0, 0.05), (0.55, 0.45, 0.12), 'EN_Concrete')
    mb.box((0, 0, 0.6), (0.36, 0.3, 1.0), 'EN_Paint_Grey')
    mb.box((0, 0, 1.11), (0.38, 0.32, 0.03), 'EN_Paint_Grey')
    mb.box((0, 0.155, 0.75), (0.22, 0.01, 0.26), 'EN_Paint_Yellow')
    mb.cyl((0, 0.15, 0.95), (0, 0.22, 0.95), 0.06, seg=14, mat='EN_Steel_Galv')
    mb.cyl((0, -0.1, 1.125), (0, -0.1, 1.22), 0.04, seg=10, mat='EN_Reflector_Amber')    # signal lamp
    pipe(mb, [(4.55, 0.18, -0.05), (4.55, 0.18, 0.9)], 0.04, 'EN_Paint_Grey')            # rest post
    for x in (4.45, 4.65):
        pipe(mb, [(x, 0.18, 0.92), (x, 0.18, 1.02)], 0.012, 'EN_Paint_Grey')
    mb.box((4.55, 0.18, 0.91), (0.28, 0.06, 0.03), 'EN_Paint_Grey')
    a.mesh(f'{a.id}_Housing', mb)
    pivot = (0, 0.25, 0.95)
    boom = MB()
    L, n = 4.6, 9
    for i in range(n):        # red-white stripes
        x0, x1 = 0.1 + (L - 0.1) * i / n, 0.1 + (L - 0.1) * (i + 1) / n
        boom.box(((x0 + x1) / 2, 0.0, 0.0), (x1 - x0, 0.06, 0.1), 'EN_Paint_Red' if i % 2 else 'EN_Paint_White')
    boom.box((0.0, 0.0, 0.0), (0.25, 0.08, 0.14), 'EN_Paint_Grey')
    boom.box((-0.25, 0.0, 0.0), (0.3, 0.09, 0.2), 'EN_Paint_Grey')      # counterweight
    for x in (1.0, 2.5, 4.0):
        boom.box((x, 0.031, 0.0), (0.06, 0.004, 0.04), 'EN_Reflector_Red')
    # vertices are relative to the hinge; the object origin sits on it (no nested pivot: FBX round trips of
    # meshes parented to empties came back displaced)
    a.mesh('Boom', boom).location = to_blender(pivot)
    col = MB()
    col.box((0, 0, 0.6), (0.4, 0.35, 1.2), 'EN_Collision')
    a.mesh('COL_Housing', col)
    colb = MB()
    colb.box((2.4, 0.0, 0.0), (4.6, 0.08, 0.12), 'EN_Collision')
    a.mesh('COL_Boom', colb).location = to_blender(pivot)
    return a


def lamp(M):
    a = Asset('EN_Yard_Lamp', 'Дворовый светильник на опоре 6 м', 'yard', M, required=['Lamp_Head'])
    mb = MB()
    mb.cyl((0, 0, -0.05), (0, 0, 0.5), 0.1, 0.09, seg=14, mat='EN_Paint_Grey')
    mb.cyl((0, 0, 0.5), (0, 0, 6.0), 0.085, 0.05, seg=14, mat='EN_Paint_Grey')
    pipe(mb, bend((0, 0, 5.6), (0, 0, 6.2), (0, 1.0, 6.2), 0.35, 6), 0.03, 'EN_Paint_Grey')
    a.mesh(f'{a.id}_Pole', mb)
    head = MB()
    head.add([(-0.14, 0.95, 6.14), (0.14, 0.95, 6.14), (0.16, 1.55, 6.12), (-0.16, 1.55, 6.12),
              (-0.12, 0.95, 6.27), (0.12, 0.95, 6.27), (0.13, 1.5, 6.22), (-0.13, 1.5, 6.22)],
             [(3, 2, 1, 0), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)], 'EN_Paint_Grey')
    head.box((0, 1.25, 6.125), (0.22, 0.5, 0.01), 'EN_Lamp_White')
    a.mesh('Lamp_Head', head)
    col = MB()
    col.cyl((0, 0, 0), (0, 0, 6.0), 0.1, seg=8, mat='EN_Collision')
    a.mesh('COL_Pole', col)
    return a


def garage(M, aid, paint, seed):
    R = rng(seed)
    a = Asset(aid, 'Металлический гараж', 'yard', M)
    mb = MB()
    W, L, H = 3.0, 6.0, 2.25
    ridge = 0.2
    y0, y1 = -L / 2, L / 2
    for sx in (-1, 1):
        corrugated(mb, sx * W / 2, y0 if sx > 0 else y1, sx * W / 2, y1 if sx > 0 else y0, 0.0, H, paint, 0.25, 0.025)
    corrugated(mb, W / 2, y0, -W / 2, y0, 0.0, H, paint, 0.25, 0.025)
    mb.add([(-W / 2, y0, H), (W / 2, y0, H), (0, y0, H + ridge)], [(0, 1, 2), (2, 1, 0)], paint)
    mb.add([(-W / 2, y1, H), (W / 2, y1, H), (0, y1, H + ridge)], [(2, 1, 0), (0, 1, 2)], paint)
    roof = 'EN_Steel_Rusty' if R.random() < 0.5 else paint
    for sx in (-1, 1):
        a_ = (sx * (W / 2 + 0.12), y0 - 0.12, H - 0.02)
        b_ = (0, y0 - 0.12, H + ridge + 0.03)
        c_ = (0, y1 + 0.12, H + ridge + 0.03)
        d_ = (sx * (W / 2 + 0.12), y1 + 0.12, H - 0.02)
        quad = [a_, b_, c_, d_] if sx < 0 else [d_, c_, b_, a_]
        mb.ngon(quad, roof)
        mb.ngon(list(reversed(quad)), 'EN_Steel_Dark')
    for sx in (-1, 1):        # double doors at the front (+Y)
        mb.box((sx * 0.72, y1 + 0.012, 1.0), (1.4, 0.024, 1.98), paint)
        mb.box((sx * 0.72, y1 + 0.026, 1.0), (1.3, 0.006, 1.85), paint)
        for z in (0.35, 1.65):
            mb.cyl((sx * 1.42, y1 + 0.01, z), (sx * 1.42, y1 + 0.05, z), 0.02, seg=8, mat='EN_Steel_Dark')
    mb.box((0.12, y1 + 0.05, 1.0), (0.04, 0.04, 0.25), 'EN_Steel_Dark')
    mb.box((-0.25, y1 + 0.05, 1.0), (0.22, 0.03, 0.03), 'EN_Steel_Dark')
    mb.box((0, y1 + 0.03, 2.03), (2.9, 0.04, 0.06), paint)
    t = text_mb(str(R.randint(12, 98)), 0.28, 'EN_Paint_White', extrude=0.0)
    t.transform(Matrix.Translation((-0.72, y1 + 0.031, 1.55)))
    mb.extend(t)
    mb.box((0, 0, 0.02), (W + 0.05, L + 0.05, 0.04), 'EN_Concrete')
    a.mesh(f'{a.id}_Mesh', mb)
    col_box(a, (0, 0, (H + ridge) / 2), (W + 0.1, L + 0.1, H + ridge))
    return a


ALL = [bench, bin_, bollard_sphere, tire_bed, lawn_fence, playground_pad, swing, slide, sandbox, carousel,
       sports_frame, waste_site, gate, lamp,
       lambda M: garage(M, 'EN_Yard_Garage_Teal', 'EN_Garage_Teal', 4),
       lambda M: garage(M, 'EN_Yard_Garage_Beige', 'EN_Garage_Beige', 9)]
