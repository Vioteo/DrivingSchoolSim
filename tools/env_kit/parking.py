"""Parking: bays off a yard drive, a parallel pocket along a street, a small car
park, wheel stops, pay station, bollards.

Asphalt top Y = 0, curbs (бортовой камень) 0.15 m above it. Space size 2.5 × 5.3 m
(perpendicular), 2.5 × 6.0 m (parallel), 2.5 × 5.0 m in the car park; lines
0.1 m, white (ГОСТ Р 51256 line 1.1). Modules that tile carry half of the edge
line on each side, so two modules side by side give one full line.
Front = author +Y (Unity +Z) = the drive / aisle side of a bay.
"""
import math
from mathutils import Vector

from .core import MB, Asset, ccw, text_mb, to_blender

CURB_W, CURB_H = 0.15, 0.15


def pad(mb, poly, curb_edges, mat='EN_Asphalt'):
    """Asphalt slab (top at 0, skirt to -0.15) with curbs on the listed edges (outside the polygon)."""
    poly = ccw(poly)
    n = len(poly)
    mb.ngon([(x, y, 0.0) for x, y in poly], mat)
    for i in range(n):
        (x0, y0), (x1, y1) = poly[i], poly[(i + 1) % n]
        mb.ngon([(x1, y1, 0.0), (x0, y0, 0.0), (x0, y0, -0.15), (x1, y1, -0.15)], mat)
    for i in curb_edges:
        (x0, y0), (x1, y1) = poly[i % n], poly[(i + 1) % n]
        d = Vector((x1 - x0, y1 - y0, 0)).normalized()
        out = Vector((d.y, -d.x, 0)) * (CURB_W / 2)          # CCW polygon: outside is to the right
        a = Vector((x0, y0, 0)) + out - d * CURB_W / 2
        b = Vector((x1, y1, 0)) + out + d * CURB_W / 2
        zc = (CURB_H - 0.2) / 2
        mb.beam((a.x, a.y, zc), (b.x, b.y, zc), CURB_W, CURB_H + 0.2, 'EN_Curb')
    return poly


def stripe(mb, a, b, w=0.1, mat='EN_Marking_White', z=0.004):
    a, b = Vector((a[0], a[1], z)), Vector((b[0], b[1], z))
    d = (b - a).normalized()
    n = Vector((-d.y, d.x, 0)) * (w / 2)
    mb.ngon([a - n, b - n, b + n, a + n], mat)


def collider(a, poly, curbs):
    col = MB()
    col.prism(poly, -0.1, 0.0, 'EN_Collision')
    a.mesh('COL_DriveSurface', col)
    cc = MB()
    poly = ccw(poly)
    n = len(poly)
    for i in curbs:
        (x0, y0), (x1, y1) = poly[i % n], poly[(i + 1) % n]
        d = Vector((x1 - x0, y1 - y0, 0)).normalized()
        out = Vector((d.y, -d.x, 0)) * (CURB_W / 2)
        cc.beam(Vector((x0, y0, 0.0)) + out, Vector((x1, y1, 0.0)) + out, CURB_W, CURB_H * 2, 'EN_Collision')
    if not cc.empty:
        a.mesh('COL_Curb', cc)


def bay_perp(M):
    n, w, D = 5, 2.5, 5.3
    a = Asset('EN_Park_Bay_Perp_5x', 'Парковочный карман: 5 мест перпендикулярно (2,5 × 5,3 м)', 'parking', M,
              min_z=-0.2, spaces=n, tiles_along='X 12,5 м')
    W = n * w
    poly = [(-W / 2, -D), (W / 2, -D), (W / 2, 0), (-W / 2, 0)]
    mb = MB()
    p = pad(mb, poly, [0])
    mk = MB()
    for i in range(n + 1):
        x = -W / 2 + i * w
        if i == 0:
            stripe(mk, (x + 0.025, -D + 0.1), (x + 0.025, -0.3), 0.05)
        elif i == n:
            stripe(mk, (x - 0.025, -D + 0.1), (x - 0.025, -0.3), 0.05)
        else:
            stripe(mk, (x, -D + 0.1), (x, -0.3))
    a.mesh(f'{a.id}_Mesh', mb)
    a.mesh(f'{a.id}_Markings', mk)
    collider(a, poly, [0])
    a.meta['expect_size'] = {'x': (12.5, 12.7), 'y': (5.45, 5.5)}
    return a


def bay_angle(M):
    n, w, Dp = 4, 2.5, 4.6
    p = w / math.sin(math.radians(45))
    a = Asset('EN_Park_Bay_Angle45_4x', 'Парковочный карман: 4 места под углом 45°', 'parking', M, min_z=-0.2,
              spaces=n, tiles_along=f'X {n * p:.3f} м')
    x0 = -n * p / 2 + Dp / 2
    poly = [(x0 - Dp, -Dp), (x0 - Dp + n * p, -Dp), (x0 + n * p, 0), (x0, 0)]
    mb = MB()
    pad(mb, poly, [0])
    mk = MB()
    for i in range(n + 1):
        x = x0 + i * p
        off = 0.035 if i == 0 else -0.035 if i == n else 0.0
        ww = 0.05 if i in (0, n) else 0.1
        stripe(mk, (x - Dp + 0.1 + off, -Dp + 0.1), (x - 0.3 + off, -0.3), ww)
    a.mesh(f'{a.id}_Mesh', mb)
    a.mesh(f'{a.id}_Markings', mk)
    collider(a, poly, [0])
    return a


def pocket_parallel(M):
    a = Asset('EN_Park_Pocket_Parallel_3x', 'Заездной карман вдоль улицы: 3 места параллельно (6 × 2,5 м)', 'parking', M,
              min_z=-0.2, spaces=3, note='открытая сторона x = 0 — край проезжей части; карман уходит в +X (справа)',
              required=['Socket_Start', 'Socket_End'])
    T, L, D = 4.0, 18.0, 2.5
    poly = [(0, 0), (D, T), (D, T + L), (0, 2 * T + L)]
    mb = MB()
    pad(mb, poly, [0, 1, 2])
    mk = MB()
    for y in (T, T + 6, T + 12, T + 18):
        stripe(mk, (0.1, y), (D - 0.1, y))
    a.mesh(f'{a.id}_Mesh', mb)
    a.mesh(f'{a.id}_Markings', mk)
    collider(a, poly, [0, 1, 2])
    a.socket('Socket_Start', (0, 0, 0))
    a.socket('Socket_End', (0, 2 * T + L, 0))
    a.meta['sockets'] = {'Socket_Start': (0, 0, 0), 'Socket_End': to_blender((0, 2 * T + L, 0))}
    return a


def lot(M):
    a = Asset('EN_Park_Lot_20', 'Автостоянка на 20 мест (2 ряда, проезд 6 м)', 'parking', M, min_z=-0.2, spaces=20,
              note='въезды с обоих торцов проезда (±X)')
    mb = MB()
    poly = [(-14.5, -3), (-12.5, -3), (-12.5, -8), (12.5, -8), (12.5, -3), (14.5, -3), (14.5, 3), (12.5, 3),
            (12.5, 8), (-12.5, 8), (-12.5, 3), (-14.5, 3)]
    p = pad(mb, poly, [])
    # perimeter curbs along the backs of the rows and round the end islands
    curb = MB()
    for x0, x1, y0, y1 in ((-14.5, 14.5, 8.0, 8.15), (-14.5, 14.5, -8.15, -8.0)):
        curb.box_between(x0, x1, y0, y1, -0.2, CURB_H, 'EN_Curb')
    for sx in (-1, 1):
        for sy in (-1, 1):
            xa, xb = sorted((sx * 12.5, sx * 14.5))
            ya, yb = sorted((sy * 3.0, sy * 8.0))
            mb.box_between(xa + 0.15, xb - 0.15, ya + 0.15, yb - 0.15, -0.1, 0.12, 'EN_Soil')
            mb.ngon([(xa + 0.15, ya + 0.15, 0.121), (xb - 0.15, ya + 0.15, 0.121), (xb - 0.15, yb - 0.15, 0.121),
                     (xa + 0.15, yb - 0.15, 0.121)], 'EN_Grass')
            curb.box_between(xa, xb, ya, ya + 0.15, -0.2, CURB_H, 'EN_Curb')
            curb.box_between(xa, xb, yb - 0.15, yb, -0.2, CURB_H, 'EN_Curb')
            curb.box_between(xa, xa + 0.15, ya, yb, -0.2, CURB_H, 'EN_Curb')
            curb.box_between(xb - 0.15, xb, ya, yb, -0.2, CURB_H, 'EN_Curb')
    mb.extend(curb)
    mk = MB()
    for sy in (-1, 1):
        for i in range(11):
            x = -12.5 + i * 2.5
            stripe(mk, (x, sy * 7.9), (x, sy * 3.3))
        stripe(mk, (-12.5, sy * 3.0 - sy * 0.05), (12.5, sy * 3.0 - sy * 0.05))     # row front lines
    # direction arrows in the aisle (stylised)
    for x, s in ((-7.0, 1), (7.0, -1)):
        stripe(mk, (x - 1.5 * s, 0), (x + 1.0 * s, 0), 0.15)
        tri = ccw([(x + 1.0 * s, 0.35), (x + 1.8 * s, 0.0), (x + 1.0 * s, -0.35)])
        mk.ngon([(px, py, 0.004) for px, py in tri], 'EN_Marking_White')
    a.mesh(f'{a.id}_Mesh', mb)
    a.mesh(f'{a.id}_Markings', mk)
    col = MB()
    col.prism(poly, -0.1, 0.0, 'EN_Collision')
    a.mesh('COL_DriveSurface', col)
    cc = MB()
    cc.extend(curb)
    for i in range(len(cc.fm)):
        cc.fm[i] = cc._mi('EN_Collision')
    a.mesh('COL_Curb', cc)
    a.meta['expect_size'] = {'x': (29.0, 29.0), 'y': (16.3, 16.3)}
    return a


def wheel_stop(M):
    a = Asset('EN_Park_WheelStop', 'Колесоотбойник 1,8 м', 'parking', M)
    mb = MB()
    L = 1.8
    prof = [(-0.075, 0.0), (0.075, 0.0), (0.05, 0.1), (-0.05, 0.1)]
    n = 6
    for i in range(n):
        y0, y1 = -L / 2 + L * i / n, -L / 2 + L * (i + 1) / n
        m = 'EN_Paint_Yellow' if i % 2 else 'EN_Rubber'
        v = [(x, y0, z) for x, z in prof] + [(x, y1, z) for x, z in prof]
        f = [(0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
        if i == 0:
            f.append((3, 2, 1, 0))
        if i == n - 1:
            f.append((4, 5, 6, 7))
        mb.add(v, f, m)
    for y in (-0.6, 0.6):
        mb.cyl((0, y, 0.09), (0, y, 0.105), 0.02, seg=8, mat='EN_Steel_Dark')
    # turned so the stop runs across a parking space: along author X
    from mathutils import Matrix
    mb.transform(Matrix.Rotation(math.pi / 2, 4, 'Z'))
    a.mesh(f'{a.id}_Mesh', mb)
    col = MB()
    col.box((0, 0, 0.05), (L, 0.15, 0.1), 'EN_Collision')
    a.mesh('COL_Body', col)
    return a


def pay_station(M):
    a = Asset('EN_Park_PayStation', 'Паркомат', 'parking', M)
    mb = MB()
    mb.box((0, 0, 0.02), (0.6, 0.45, 0.04), 'EN_Concrete')
    mb.box((0, 0, 0.12), (0.4, 0.28, 0.2), 'EN_Paint_Grey')
    mb.box((0, 0, 0.9), (0.46, 0.32, 1.4), 'EN_Paint_Grey')
    mb.box((0, 0, 1.72), (0.5, 0.36, 0.28), 'EN_Sign_Blue')
    t = text_mb('P', 0.2, 'EN_Sign_White', extrude=0.0)
    from mathutils import Matrix
    t.transform(Matrix.Translation((0, 0.181, 1.72)))
    mb.extend(t)
    mb.box((0, 0.162, 1.32), (0.3, 0.006, 0.22), 'EN_Display')
    for i in range(4):
        for j in range(3):
            mb.box((-0.06 + j * 0.06, 0.165, 1.12 - i * 0.05), (0.045, 0.012, 0.035), 'EN_Steel_Galv')
    mb.box((0.14, 0.165, 1.05), (0.09, 0.012, 0.02), 'EN_Steel_Dark')    # card slot
    mb.box((0, 0.165, 0.62), (0.22, 0.02, 0.06), 'EN_Steel_Dark')        # ticket slot
    mb.box((0, 0, 1.87), (0.54, 0.4, 0.02), 'EN_Paint_Grey')
    a.mesh(f'{a.id}_Mesh', mb)
    col = MB()
    col.box((0, 0, 0.95), (0.5, 0.36, 1.9), 'EN_Collision')
    a.mesh('COL_Body', col)
    return a


def bollard(M):
    a = Asset('EN_Park_Bollard', 'Парковочный столбик', 'parking', M)
    mb = MB()
    mb.cyl((0, 0, -0.05), (0, 0, 0.88), 0.055, seg=16, mat='EN_Paint_Grey', cap0=False)
    mb.lathe([(0.055, 0.88), (0.045, 0.91), (0.0, 0.92)], (0, 0, 0), 16, 'EN_Paint_Grey')
    for z, m in ((0.68, 'EN_Reflector_White'), (0.76, 'EN_Reflector_Red')):
        mb.cyl((0, 0, z), (0, 0, z + 0.06), 0.0565, seg=16, mat=m, cap0=False, cap1=False)
    a.mesh(f'{a.id}_Mesh', mb)
    col = MB()
    col.cyl((0, 0, 0), (0, 0, 0.92), 0.06, seg=8, mat='EN_Collision')
    a.mesh('COL_Body', col)
    return a


ALL = [bay_perp, bay_angle, pocket_parallel, lot, wheel_stop, pay_station, bollard]
