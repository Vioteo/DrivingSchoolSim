"""Tram tracks, stops, stop signs and stop marking.

Kit frame as in vehicles.py: X right, Y forward, Z up; road top Z = 0.
Linear modules run from Socket_Start (0,0,0) along +Y (turned to -Y for
Blender by common.to_blender, i.e. +Z in Unity, like the road kit).
Track geometry uses the same rail centre line as the railway kit and the tram
wheels: 1520 mm gauge + 75 mm head -> rail head centre at +-0.7975 m.
"""
import math
from vehicle_kit.geom import Part, cylinder, merge, transform, tube
from .common import box, box_between, prism, mesh, empty, text, to_blender
from .vehicles import RAIL_C, TRAM_WHEEL_R, TRAM_FLANGE

# grooved (tram) rail, flush with the road surface
HEAD_W = 0.055          # running head
GROOVE_W = 0.050        # flangeway
GROOVE_D = 0.040        # flangeway depth (> tram flange 0.025 m)
LIP_W = 0.012           # guard lip on the gauge side
BASE = -0.22            # bottom of every surface slab (as the road kit)
MARK_Z = (0.006, 0.009)  # paint: 6 mm above the surface, 3 mm thick (road kit)
CURB_H = 0.15


def module(name, title, parts, sockets, meta):
    root = Part(name, kind='empty')
    for p in parts:
        if p.parent is None:
            p.parent = name
    names = [p.name for p in parts]
    dup = {n for n in names if names.count(n) > 1}
    if dup:
        raise ValueError(f'{name}: duplicate object names {sorted(dup)}')
    socks = [empty(n, loc, parent=name) for n, loc in sockets]
    socks += [empty('Socket_Front', (0, 1, 0), parent=name), empty('Socket_Up', (0, 0, 1), parent=name)]
    meta.update(id=name, title=title)
    return to_blender([root] + parts + socks, name), meta


class Bucket:
    """Collects geometry per (object name, material)."""

    def __init__(self):
        self.d = {}

    def add(self, name, mat, vf):
        self.d.setdefault((name, mat), []).append(vf)

    def parts(self):
        return [mesh(n, m, merge(*vfs)) for (n, m), vfs in self.d.items()]


# ------------------------------------------------------------------ straight tracks
def rail_strips(c):
    """Cross-section intervals [(x0, x1, top, material, name)] of one grooved
    track centred at x = c: lip, flangeway, head for both rails."""
    out = []
    for s in (-1, 1):
        rc = c + s * RAIL_C
        inner = -s                                   # towards the track centre
        head = sorted((rc - HEAD_W / 2, rc + HEAD_W / 2))
        g_end = rc + inner * (HEAD_W / 2 + GROOVE_W)
        groove = sorted((rc + inner * HEAD_W / 2, g_end))
        lip = sorted((g_end, g_end + inner * LIP_W))
        out += [(head[0], head[1], 0.0, 'PT_Rail_Steel', 'Rail_Heads'),
                (groove[0], groove[1], -GROOVE_D, 'PT_Groove', 'Rail_Grooves'),
                (lip[0], lip[1], 0.0, 'PT_Rail_Steel', 'Rail_Heads')]
    return out


def cross_section(intervals, y0, y1, B):
    for x0, x1, top, mat, name in intervals:
        if x1 - x0 > 1e-6:
            B.add(name, mat, box_between(BASE, top, x0, x1, y0, y1))


def fill(x0, x1, holes, top, mat, name):
    """Split [x0, x1] around the rail intervals so surfaces never overlap."""
    cuts = sorted((a, b) for a, b, *_ in holes if b > x0 and a < x1)
    out, x = [], x0
    for a, b in cuts:
        if a > x:
            out.append((x, a, top, mat, name))
        x = max(x, b)
    if x < x1:
        out.append((x, x1, top, mat, name))
    return out


def road_tram_urban(L=20.0):
    """PT_Road_TramUrban_20m: city street with a double tram track in the
    middle, at road level (cars may use it where rules allow)."""
    C = 1.75                                          # track centres +-1.75 m (3.5 m apart)
    ZONE, LANE_OUT, CURB_W, WALK = 3.5, 7.0, 0.2, 2.0
    B = Bucket()
    rails = rail_strips(C) + rail_strips(-C)
    xs = fill(-ZONE, ZONE, rails, 0.0, 'PT_TrackSlab', 'Surface_TrackZone')
    xs += [(-LANE_OUT, -ZONE, 0.0, 'PT_Asphalt', 'Surface_Asphalt'), (ZONE, LANE_OUT, 0.0, 'PT_Asphalt', 'Surface_Asphalt')]
    xs += rails
    cross_section(xs, 0.0, L, B)
    for s in (-1, 1):
        B.add('Curb', 'PT_Concrete', box_between(BASE, CURB_H, s * LANE_OUT, s * (LANE_OUT + CURB_W), 0, L))
        B.add('Sidewalk', 'PT_Paving', box_between(BASE, CURB_H, s * (LANE_OUT + CURB_W), s * (LANE_OUT + CURB_W + WALK), 0, L))
        # dashed line between the car lane and the track zone (stylised)
        for k in range(int(L // 5)):
            y = 1.5 + 5 * k
            B.add('Markings_White', 'PT_Marking_White', box_between(*MARK_Z, s * ZONE - 0.06, s * ZONE + 0.06, y - 1.5, y + 1.5))
    # slab joints across the track zone every 2 m (visual only)
    for k in range(1, int(L // 2)):
        y = 2.0 * k
        for x0, x1, top, mat, name in fill(-ZONE, ZONE, rails, 0.0, '', ''):
            B.add('Surface_Joints', 'PT_Groove', box_between(0.003, 0.004, x0, x1, y - 0.01, y + 0.01))
    W = LANE_OUT + CURB_W + WALK
    col = Bucket()
    col.add('COL_Road', 'PT_Collision', box_between(BASE, 0.0, -LANE_OUT, LANE_OUT, 0, L))
    for s in (-1, 1):
        col.add('COL_Road', 'PT_Collision', box_between(BASE, CURB_H, s * LANE_OUT, s * W, 0, L))
    sockets = [('Socket_Start', (0, 0, 0)), ('Socket_End', (0, L, 0)),
               ('Socket_Tram_R_Start', (C, 0, 0)), ('Socket_Tram_R_End', (C, L, 0)),
               ('Socket_Tram_L_Start', (-C, 0, 0)), ('Socket_Tram_L_End', (-C, L, 0))]
    meta = dict(kind='track', length=L, width=2 * W, track_centres=[-C, C], rail_top=0.0, groove_depth=GROOVE_D,
                rails=_rail_centres([-C, C]))
    return module('PT_Road_TramUrban_20m', 'Улица с трамвайными путями в одном уровне', B.parts() + col.parts(), sockets, meta)


def _rail_centres(centres):
    return sorted(c + s * RAIL_C for c in centres for s in (-1, 1))


def vignole_rail(x, y0, y1):
    """Visible part of a flat-bottom rail standing in the grass: head + web."""
    return merge(box_between(-0.045, 0.0, x - 0.035, x + 0.035, y0, y1), box_between(-0.12, -0.045, x - 0.008, x + 0.008, y0, y1))


# lawn track: same track centres as the street module, so the two join
GRASS_C, GRASS_BED, GRASS_CURB = 1.75, 3.25, 0.2


def track_grass(L=20.0):
    """PT_TramTrack_Grass_20m: separate double track on a lawn bed between curbs."""
    C, BED, CURB_W = GRASS_C, GRASS_BED, GRASS_CURB
    B = Bucket()
    B.add('Bed_Grass', 'PT_Grass', box_between(BASE, -0.02, -BED, BED, 0, L))
    for rc in _rail_centres([-C, C]):
        B.add('Rails', 'PT_Rail_Steel', vignole_rail(rc, 0, L))
    for s in (-1, 1):
        B.add('Curb', 'PT_Concrete', box_between(BASE, CURB_H, s * BED, s * (BED + CURB_W), 0, L))
    col = Bucket()
    col.add('COL_Bed', 'PT_Collision', box_between(BASE, -0.02, -BED, BED, 0, L))
    for s in (-1, 1):
        col.add('COL_Bed', 'PT_Collision', box_between(BASE, CURB_H, s * BED, s * (BED + CURB_W), 0, L))
    sockets = [('Socket_Start', (0, 0, 0)), ('Socket_End', (0, L, 0)),
               ('Socket_Tram_R_Start', (C, 0, 0)), ('Socket_Tram_R_End', (C, L, 0)),
               ('Socket_Tram_L_Start', (-C, 0, 0)), ('Socket_Tram_L_End', (-C, L, 0))]
    meta = dict(kind='track', length=L, width=2 * (BED + CURB_W), track_centres=[-C, C], rail_top=0.0,
                rails=_rail_centres([-C, C]))
    return module('PT_TramTrack_Grass_20m', 'Обособленный трамвайный путь на газоне', B.parts() + col.parts(), sockets, meta)


def annulus(r0, r1, a0, a1, z0, z1, centre, n):
    """Annular sector prism; angle a measured from -X around centre so that
    a = 0 is the start (heading +Y) and a = pi/2 the end (heading +X)."""
    cx, cy = centre

    def pt(r, a):
        return (cx - r * math.cos(a), cy + r * math.sin(a))
    poly = [pt(r0, a0 + (a1 - a0) * i / n) for i in range(n + 1)]
    poly += [pt(r1, a0 + (a1 - a0) * i / n) for i in reversed(range(n + 1))]
    return prism(poly, z0, z1)


def track_grass_curve(R=25.0):
    """PT_TramTrack_Grass_Curve90_R25: 90 deg right-hand curve of the lawn
    track, centre-line radius 25 m; exit heads +X (Unity: turn right)."""
    C, BED, CURB_W = GRASS_C, GRASS_BED, GRASS_CURB
    ctr = (R, 0.0)
    n = 64
    B = Bucket()
    B.add('Bed_Grass', 'PT_Grass', annulus(R - BED, R + BED, 0, math.pi / 2, BASE, -0.02, ctr, n))
    for tc in (R - C, R + C):
        for s in (-1, 1):
            r = tc + s * RAIL_C
            B.add('Rails', 'PT_Rail_Steel', annulus(r - 0.035, r + 0.035, 0, math.pi / 2, -0.045, 0.0, ctr, n))
            B.add('Rails', 'PT_Rail_Steel', annulus(r - 0.008, r + 0.008, 0, math.pi / 2, -0.12, -0.045, ctr, n))
    col = Bucket()
    col.add('COL_Bed', 'PT_Collision', annulus(R - BED, R + BED, 0, math.pi / 2, BASE, -0.02, ctr, n))
    for r0 in (R - BED - CURB_W, R + BED):
        B.add('Curb', 'PT_Concrete', annulus(r0, r0 + CURB_W, 0, math.pi / 2, BASE, CURB_H, ctr, n))
        col.add('COL_Bed', 'PT_Collision', annulus(r0, r0 + CURB_W, 0, math.pi / 2, BASE, CURB_H, ctr, n))

    def on(r, a):
        return (R - r * math.cos(a), r * math.sin(a), 0.0)
    sockets = [('Socket_Start', (0, 0, 0)), ('Socket_End', (R, R, 0)),
               ('Socket_Tram_R_Start', on(R - C, 0)), ('Socket_Tram_R_End', on(R - C, math.pi / 2)),
               ('Socket_Tram_L_Start', on(R + C, 0)), ('Socket_Tram_L_End', on(R + C, math.pi / 2))]
    meta = dict(kind='track_curve', radius=R, width=2 * (BED + CURB_W), track_centres=[-C, C], rail_top=0.0)
    return module('PT_TramTrack_Grass_Curve90_R25', 'Обособленный путь, поворот 90° R25', B.parts() + col.parts(),
                  sockets, meta)


# ------------------------------------------------------------------ signs
def sign_parts(kind, B, post=True, at=(0.0, 0.0), facing=1):
    """Stop sign 5.16 (bus/trolleybus) or 5.17 (tram), stylised: blue plate,
    white field, dark pictogram. facing: +1 face towards +Y, -1 towards -Y."""
    ox, oy = at
    f = facing
    z = 2.45
    if post:
        B.add('Post', 'PT_Steel', cylinder((ox, oy - f * 0.055, 0), (ox, oy - f * 0.055, 2.75), 0.038, 16))
        B.add('Post', 'PT_Steel', box((ox, oy - f * 0.055, 0.017), (0.24, 0.24, 0.034)))
    B.add('Plate_Back', 'PT_Steel', box((ox, oy + f * 0.0, z), (0.6, 0.02, 0.6)))
    B.add('Face_Blue', 'PT_Sign_Blue', box((ox, oy + f * 0.013, z), (0.59, 0.006, 0.59)))
    B.add('Face_White', 'PT_Sign_White', box((ox, oy + f * 0.017, z), (0.44, 0.004, 0.44)))
    y = oy + f * 0.021
    ink = []
    if kind == 'bus':
        ink.append(box((ox, y, z + 0.01), (0.32, 0.004, 0.13)))                 # body
        ink.append(cylinder((ox - 0.09, y - 0.002, z - 0.07), (ox - 0.09, y + 0.002, z - 0.07), 0.035, 16))
        ink.append(cylinder((ox + 0.10, y - 0.002, z - 0.07), (ox + 0.10, y + 0.002, z - 0.07), 0.035, 16))
        win = [box((ox - 0.11 + 0.07 * k, y + f * 0.002, z + 0.035), (0.05, 0.002, 0.04)) for k in range(4)]
    else:
        ink.append(box((ox, y, z - 0.005), (0.34, 0.004, 0.13)))
        ink.append(box((ox, y, z + 0.075), (0.2, 0.004, 0.02)))                 # roof equipment
        ink.append(tube([(ox - 0.05, y, z + 0.085), (ox + 0.02, y, z + 0.15), (ox - 0.03, y, z + 0.18)], 0.008, 6))
        ink.append(box((ox, y, z + 0.18), (0.12, 0.004, 0.01)))                 # contact wire
        ink += [cylinder((ox + dx, y - 0.002, z - 0.085), (ox + dx, y + 0.002, z - 0.085), 0.025, 12)
                for dx in (-0.12, -0.06, 0.06, 0.12)]
        win = [box((ox - 0.12 + 0.06 * k, y + f * 0.002, z + 0.02), (0.04, 0.002, 0.045)) for k in range(5)]
    B.add('Pictogram', 'PT_Ink', merge(*ink))
    B.add('Pictogram_Windows', 'PT_Sign_White', merge(*win))


def sign(kind):
    B = Bucket()
    sign_parts(kind, B)
    col = Bucket()
    col.add('COL_Post', 'PT_Collision', box((0, -0.055, 1.375), (0.08, 0.08, 2.75)))
    col.add('COL_Plate', 'PT_Collision', box((0, 0, 2.45), (0.6, 0.03, 0.6)))
    name = 'PT_Sign_5_16_BusStop' if kind == 'bus' else 'PT_Sign_5_17_TramStop'
    title = '5.16 «Место остановки автобуса и (или) троллейбуса»' if kind == 'bus' else '5.17 «Место остановки трамвая»'
    meta = dict(kind='sign', height=2.75, plate=0.6)
    return module(name, title + ' (стилизовано)', B.parts() + col.parts(), [], meta)


# ------------------------------------------------------------------ shelter
def shelter_geometry(B, width=4.4, depth=1.6, height=2.55, name_board=True, prefix=''):
    """Bus shelter, open side towards +Y, footprint centred on the origin."""
    hw, hd = width / 2, depth / 2
    P = prefix
    post = 0.08
    xs = (-hw + 0.1, hw - 0.1)
    ys = (-hd + 0.1, hd - 0.25)
    for x in xs:
        for y in ys:
            B.add(P + 'Frame', 'PT_Shelter_Frame', box_between(0.0, height - 0.1, x - post / 2, x + post / 2, y - post / 2, y + post / 2))
    # roof with a thin fascia
    B.add(P + 'Roof', 'PT_Shelter_Frame', box_between(height - 0.1, height, -hw, hw, -hd, hd + 0.1))
    B.add(P + 'Roof_Glass', 'PT_Glass_Clear', box_between(height, height + 0.01, -hw + 0.15, hw - 0.15, -hd + 0.15, hd - 0.05))
    # back wall and right side wall: glass panes in rails
    yb = ys[0]
    B.add(P + 'Glass', 'PT_Glass_Clear', box_between(0.15, height - 0.2, xs[0] + post / 2, xs[1] - post / 2, yb - 0.006, yb + 0.006))
    for z in (0.12, 1.05, height - 0.2):
        B.add(P + 'Frame', 'PT_Shelter_Frame', box_between(z - 0.03, z + 0.03, xs[0], xs[1], yb - 0.02, yb + 0.02))
    B.add(P + 'Glass', 'PT_Glass_Clear', box_between(0.15, height - 0.2, xs[1] - 0.006, xs[1] + 0.006, ys[0] + post / 2, ys[1] - post / 2))
    for z in (0.12, height - 0.2):
        B.add(P + 'Frame', 'PT_Shelter_Frame', box_between(z - 0.03, z + 0.03, xs[1] - 0.02, xs[1] + 0.02, ys[0], ys[1]))
    # bench
    bx = min(1.3, hw - 0.5)
    for k in range(4):
        y0 = yb + 0.12 + 0.085 * k
        B.add(P + 'Bench_Seat', 'PT_Wood', box_between(0.44, 0.48, -bx, bx, y0, y0 + 0.07))
    for x in (-bx + 0.15, bx - 0.15):
        B.add(P + 'Bench_Legs', 'PT_Shelter_Frame', box_between(0.0, 0.44, x - 0.03, x + 0.03, yb + 0.12, yb + 0.44))
    # timetable panel on the back wall (left)
    B.add(P + 'Timetable', 'PT_Sign_White', box_between(1.15, 1.85, xs[0] + 0.2, xs[0] + 0.85, yb + 0.012, yb + 0.02))
    B.add(P + 'Timetable_Frame', 'PT_Shelter_Frame', box_between(1.12, 1.88, xs[0] + 0.17, xs[0] + 0.88, yb + 0.006, yb + 0.012))
    if name_board:
        B.add(P + 'NameBoard', 'PT_Sign_Blue', box_between(height - 0.34, height - 0.12, -1.2, 1.2, hd + 0.02, hd + 0.06))


def bus_shelter():
    """PT_BusStop_Shelter: glazed shelter with bench and timetable, open to the
    road (front). Pivot on the ground at the footprint centre."""
    B = Bucket()
    shelter_geometry(B)
    parts = B.parts()
    parts.append(text('NameBoard_Text', 'ОСТАНОВКА', 0.12, (0.0, 0.8 + 0.063, 2.32), '+Y', 'PT_Sign_White'))
    parts.append(text('Timetable_Text', '42  107', 0.09, (-2.1 + 0.525, -0.7 + 0.022, 1.62), '+Y', 'PT_Ink'))
    # litter bin next to the shelter
    parts.append(mesh('Bin', 'PT_Shelter_Frame', cylinder((2.55, 0.4, 0.0), (2.55, 0.4, 0.75), 0.2, 16)))
    col = Bucket()
    col.add('COL_Shelter', 'PT_Collision', box_between(0.0, 2.55, -2.2, 2.2, -0.73, -0.67))
    col.add('COL_Shelter', 'PT_Collision', box_between(0.0, 2.55, 2.06, 2.14, -0.7, 0.55))
    for x in (-2.1, 2.1):
        col.add('COL_Shelter', 'PT_Collision', box_between(0.0, 2.45, x - 0.04, x + 0.04, 0.51, 0.59))
    col.add('COL_Shelter', 'PT_Collision', box_between(0.0, 0.48, -1.3, 1.3, -0.58, -0.25))
    col.add('COL_Bin', 'PT_Collision', box((2.55, 0.4, 0.375), (0.4, 0.4, 0.75)))
    meta = dict(kind='stop', width=4.4 + 0.55, depth=1.7, height=2.56)
    return module('PT_BusStop_Shelter', 'Остановочный павильон', parts + col.parts(), [], meta)


def tram_platform(L=30.0, W=2.5, H=0.2, ramp=1.6):
    """PT_TramStop_Platform_30m: raised boarding island to the right of the
    track (trams run along +Y with doors on the right): the tram edge is -X,
    the car lane side +X gets a railing. Ramps at both ends, small shelter
    open towards the tram, sign 5.17 at the far end facing arriving trams.
    Pivot: footprint centre at road level."""
    B = Bucket()
    y0, y1 = -L / 2, L / 2
    prof = [(y0, 0.0), (y0 + ramp, H), (y1 - ramp, H), (y1, 0.0)]      # (y, z) outline top
    tactile_x = (-W / 2 + 0.2, -W / 2 + 0.5)
    bands = [(-W / 2, -W / 2 + 0.2, 'PT_Concrete', 'Platform_Curb'),
             (tactile_x[0], tactile_x[1], 'PT_Marking_Yellow', 'Platform_Tactile'),
             (tactile_x[1], W / 2 - 0.2, 'PT_Paving', 'Platform_Top'),
             (W / 2 - 0.2, W / 2, 'PT_Concrete', 'Platform_Curb')]
    poly = [(y0, BASE)] + prof + [(y1, BASE)]
    for x0, x1, mat, name in bands:
        B.add(name, mat, _poly_x(poly, x0, x1))
    # railing on the car side
    rx = W / 2 - 0.12
    posts = [y0 + ramp + 0.3 + 2.5 * k for k in range(int((L - 2 * ramp - 0.6) / 2.5) + 1)]
    for y in posts:
        B.add('Railing', 'PT_Shelter_Frame', cylinder((rx, y, H), (rx, y, H + 1.0), 0.025, 8))
    for z in (H + 0.5, H + 1.0):
        B.add('Railing', 'PT_Shelter_Frame', cylinder((rx, posts[0], z), (rx, posts[-1], z), 0.022, 8))
    # small shelter opening towards the tram (-X): built facing +Y, turned +90 deg
    S = Bucket()
    shelter_geometry(S, width=4.0, depth=1.2, height=2.5, name_board=False, prefix='Shelter_')
    for (n, m), vfs in S.d.items():
        for vf in vfs:
            B.add(n, m, transform(transform(vf, rot=(0, 0, math.pi / 2)), loc=(0.25, 2.0, H)))
    # sign 5.17 at the far end, facing trams arriving from -Y
    sign_parts('tram', B, at=(W / 2 - 0.45, y1 - ramp - 0.8), facing=-1)
    col = Bucket()
    col.add('COL_Platform', 'PT_Collision', _poly_x(poly, -W / 2, W / 2))
    col.add('COL_Railing', 'PT_Collision', box_between(H, H + 1.0, rx - 0.03, rx + 0.03, posts[0], posts[-1]))
    meta = dict(kind='platform', length=L, width=W, height=H, ramp=ramp, tram_side='-X (Unity: left of the platform)')
    return module('PT_TramStop_Platform_30m', 'Трамвайная посадочная площадка 30 м', B.parts() + col.parts(), [], meta)


def _poly_x(poly_yz, x0, x1):
    """Extrude a (y, z) outline along X."""
    v, f = prism(poly_yz, x0, x1)
    return [(c, a, b) for a, b, c in v], f


# ------------------------------------------------------------------ marking
def zigzag(L=20.0, depth=1.0, period=2.0, width=0.1):
    """PT_Marking_1_17_Zigzag_20m: yellow zigzag at a route-vehicle stop
    (PDD Appendix 2, marking 1.17). Stylised dimensions. Starts at the curb
    line (x = 0) and extends into the lane (-X, i.e. left of +Y traffic)."""
    B = Bucket()
    n = int(L / (period / 2))
    pts = [(-(depth if k % 2 else 0.0), k * period / 2) for k in range(n + 1)]
    for (xa, ya), (xb, yb) in zip(pts, pts[1:]):
        dx, dy = xb - xa, yb - ya
        l = math.hypot(dx, dy)
        nx, ny = -dy / l * width / 2, dx / l * width / 2
        quad = [(xa + nx, ya + ny), (xa - nx, ya - ny), (xb - nx, yb - ny), (xb + nx, yb + ny)]
        B.add('Markings_Yellow', 'PT_Marking_Yellow', prism(quad, *MARK_Z))
    meta = dict(kind='marking', length=L, width=depth + width)
    return module('PT_Marking_1_17_Zigzag_20m', 'Разметка 1.17 (зигзаг), стилизовано', B.parts(),
                  [('Socket_Start', (0, 0, 0)), ('Socket_End', (0, L, 0))], meta)


ALL = (road_tram_urban, track_grass, track_grass_curve, lambda: sign('bus'), lambda: sign('tram'),
       bus_shelter, tram_platform, zigzag)
