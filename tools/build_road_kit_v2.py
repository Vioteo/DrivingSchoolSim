"""Road Kit v2 (T55): multi-lane roads, a roundabout and a level crossing. Blender 5: -b --python tools/build_road_kit_v2.py

Same conventions and helpers as v1 (tools/build_road_kit.py): metres, X right / Y forward / Z up, road top = 0,
sidewalk top = 0.15, paint +0.006. Exports to Assets/DrivingSchool/Art/RoadKitV2 (catalog-v2.json); v1 is untouched.
The graph templates (Code/Simulation/RoadGraph/RoadKitTemplatesV2.cs) use the numbers below — change both together.

  RK2_Road_Urban4_20m      2+2 straight: lanes 3.5 m (centres ±2.0, ±5.5), double solid axis, carriageway ±7.5,
                           sidewalks 3 m (7.7…10.7).  Sockets Start (0,0), End (0,20).
  RK2_Cross_4x4_32m        2+2 × 2+2 crossroads, sockets ±16. Stop bars at 14.3, crosswalks centred at 11.75 (3 m).
  RK2_Cross_4x2_24x32m     2+2 main road along X (sockets E/W at ±12) × 1+1 side road along Y (sockets N/S at ±16).
                           Crosswalks: main arms at x = ±7.75, side arms at y = ±11; stop bars main 10.2, side 13.5.
  RK2_Roundabout_52m       1+1 arms, sockets at ±26. Island r 8, ring asphalt r 8…14 (one lane, centre r 11),
                           outer sidewalk r 14.2…16.2; crosswalks on the arms at 20; give-way lines at 15.2.
  RK2_Road_RailCrossing_20m 1+1 straight with a level crossing at y = 10 (rails ±0.8), concrete deck 8.2…11.8.
"""
import sys, math, json, hashlib
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import build_road_kit as k   # helpers; its main() does not run on import
from datetime import datetime, timezone

ROOT = k.ROOT
OUT = ROOT / 'Assets/DrivingSchool/Art/RoadKitV2'
SOURCE = ROOT / 'ArtSource/DS_RoadKit_v2.blend'
OUT.mkdir(parents=True, exist_ok=True)

HALF4, SIDE4_IN, SIDE4_OUT = 7.5, 7.7, 10.7
CORNER_R4, CORNER_R42 = 8.0, 7.5   # kerb corner radii (paving edge) of the 2+2 junctions
DASH = [(1.5 + 5 * i, 3) for i in range(4)]


def curb_run(m, x0, y0, x1, y1, top=.15, collision=True, piece=1.0):
    """Kerb 0.2 m wide along a straight line (visual pieces ~1 m, one collision box)."""
    L = math.hypot(x1 - x0, y1 - y0); ux, uy = (x1 - x0) / L, (y1 - y0) / L; nx, ny = -uy * .1, ux * .1
    n = max(1, round(L / piece))
    for i in range(n):
        a, b = i / n * L + .0035, (i + 1) / n * L - .0035
        p = [(x0 + ux * a + nx, y0 + uy * a + ny), (x0 + ux * b + nx, y0 + uy * b + ny), (x0 + ux * b - nx, y0 + uy * b - ny), (x0 + ux * a - nx, y0 + uy * a - ny)]
        m.p('Curb', 'Concrete').prism(p, -.22, top)
    if collision:
        p = [(x0 + nx, y0 + ny), (x1 + nx, y1 + ny), (x1 - nx, y1 - ny), (x0 - nx, y0 - ny)]
        m.p('Collision', 'Concrete').prism(p, -.22, top)


def round_corner(m, sx, sy, x0, y0, x1, y1, r, bands, steps=12):
    """Paving corner block from (x0, y0) to (x1, y1) in quadrant (sx, sy) whose corner towards the junction is a kerb
    radius r (face of the kerb at x0 - 0.2 / y0 - 0.2, like the straight kerbs). `bands` are (axis, lo, hi) ranges where
    the kerb is lowered for a crosswalk (axis 'x' or 'y' in absolute coordinates of the quadrant).
    Kerb face radius r + 0.2 around (x0 + r, y0 + r): a right turn from the outer lane stays on the carriageway (T55)."""
    cx, cy = x0 + r, y0 + r
    arc = [(cx - r * math.cos(a), cy - r * math.sin(a)) for a in [math.pi / 2 * i / steps for i in range(steps + 1)]]
    # arc runs from (x0, cy) to (cx, y0)
    poly = [(x0, y1)] + arc + [(x1, y0), (x1, y1)]
    poly = [(sx * x, sy * y) for x, y in poly]
    if sx * sy < 0: poly.reverse()                       # keep the winding counter-clockwise
    for g in ('Sidewalk', 'Collision'): m.p(g, 'Paving').prism(poly, -.22, .15)
    def lowered(x, y):
        return any((lo <= (x if ax == 'x' else y) <= hi) for ax, lo, hi in bands)
    # kerb: straight bits along x = x0 - .1 and y = y0 - .1, and the arc at radius r + .1
    kr = r + .1
    pts = [(x0 - .1, y1)] + [(cx - kr * math.cos(a), cy - kr * math.sin(a)) for a in [math.pi / 2 * i / steps for i in range(steps + 1)]] + [(x1, y0 - .1)]
    # split the long straight runs into ~1 m pieces so the crosswalk lowering can follow them
    dense = []
    for (ax, ay), (bx, by) in zip(pts, pts[1:]):
        n = max(1, round(math.hypot(bx - ax, by - ay)))
        dense += [(ax + (bx - ax) * i / n, ay + (by - ay) * i / n) for i in range(n)]
    dense.append(pts[-1])
    for (ax, ay), (bx, by) in zip(dense, dense[1:]):
        top = .02 if lowered((ax + bx) / 2, (ay + by) / 2) else .15
        curb_run(m, sx * ax, sy * ay, sx * bx, sy * by, top, piece=10)


def curb_face(r, x0, y0, t, axis):
    """Where the kerb face of a round corner crosses the line axis = t (other coordinate), for the graph templates."""
    cx, cy, R = x0 + r, y0 + r, r + .2
    if axis == 'y':   # line y = t, return x
        return cx - math.sqrt(max(0, R * R - (cy - t) ** 2)) if t < cy else x0 - .2
    return cy - math.sqrt(max(0, R * R - (cx - t) ** 2)) if t < cx else y0 - .2


def paint(m, x, y, w, l): m.p('Markings', 'White').box(x, y, w, l, .006, .009)


def dashed_y(m, x, y0, y1, dash=3.0, gap=2.0, w=.12):
    y = y0
    while y + dash <= y1 + 1e-6: paint(m, x, y + dash / 2, w, dash); y += dash + gap


def dashed_x(m, y, x0, x1, dash=3.0, gap=2.0, w=.12):
    x = x0
    while x + dash <= x1 + 1e-6: paint(m, x + dash / 2, y, dash, w); x += dash + gap


def straight4():
    m = k.Module('RK2_Road_Urban4_20m', 'Проспект 2+2', [('Socket_Start', (0, 0, 0)), ('Socket_End', (0, 20, 0))])
    for g in ('Surface', 'Collision'): m.p(g, 'Asphalt').box(0, 10, 2 * HALF4, 20, -.22, 0)
    for s in (-1, 1):
        for g in ('Sidewalk', 'Collision'): m.p(g, 'Paving').box(s * (SIDE4_IN + SIDE4_OUT) / 2, 10, SIDE4_OUT - SIDE4_IN, 20, -.22, .15)
        curb_run(m, s * 7.6, 0, s * 7.6, 20)
        paint(m, s * .12, 10, .12, 20)            # double solid axis
        paint(m, s * 7.2, 10, .12, 20)            # edge line
        for y, l in DASH: paint(m, s * 3.75, y, .12, l)   # lane divider
    return m.finish()


def crosswalk_y(m, yc, x0, x1):
    """Zebra across a road running along Y: stripes 0.5 m wide along X, 3 m long along Y."""
    x = x0 + .25
    while x <= x1 - .25 + 1e-6: paint(m, x, yc, .5, 3); x += 1.0


def crosswalk_x(m, xc, y0, y1):
    y = y0 + .25
    while y <= y1 - .25 + 1e-6: paint(m, xc, y, 3, .5); y += 1.0


def cross4x4():
    S, H = 16, HALF4
    m = k.Module('RK2_Cross_4x4_32m', 'Перекрёсток 2+2 × 2+2', [(f'Socket_{n}', p) for n, p in [('South', (0, -S, 0)), ('North', (0, S, 0)), ('East', (S, 0, 0)), ('West', (-S, 0, 0))]])
    # Asphalt under the whole module; the corner paving (kerb radius CORNER_R4) sits on it.
    for g in ('Surface', 'Collision'): m.p(g, 'Asphalt').box(0, 0, 2 * S, 2 * S, -.22, 0)
    zc, zw = 11.75, 3.0
    for sx in (-1, 1):
        for sy in (-1, 1):
            round_corner(m, sx, sy, SIDE4_IN, SIDE4_IN, S, S, CORNER_R4, [('x', zc - zw / 2, zc + zw / 2), ('y', zc - zw / 2, zc + zw / 2)])
    reach = curb_face(CORNER_R4, SIDE4_IN, SIDE4_IN, zc, 'y')    # kerb face at the crosswalk
    for sgn in (-1, 1):
        crosswalk_y(m, sgn * zc, -reach + .3, reach - .3)          # north / south arms
        crosswalk_x(m, sgn * zc, -reach + .3, reach - .3)          # east / west arms
        # Stop bars across the incoming half of each arm (right-hand traffic).
        paint(m, -sgn * 3.85, sgn * 14.3, 7.1, .4)   # N arm (sgn=1): incoming on x<0
        paint(m, sgn * 14.3, sgn * 3.85, .4, 7.1)    # E arm (sgn=1): incoming on y>0
        for s in (-1, 1):
            paint(m, s * .12, sgn * 15.25, .12, 1.5); paint(m, sgn * 15.25, s * .12, 1.5, .12)
            paint(m, s * 3.75, sgn * 15.25, .12, 1.5); paint(m, sgn * 15.25, s * 3.75, 1.5, .12)
    return m.finish()


def cross4x2():
    SX, SY, HM, HS = 12, 16, HALF4, 4.0   # main along X (half width 7.5), side along Y (half width 4)
    m = k.Module('RK2_Cross_4x2_24x32m', 'Перекрёсток 2+2 × 1+1',
                 [(f'Socket_{n}', p) for n, p in [('South', (0, -SY, 0)), ('North', (0, SY, 0)), ('East', (SX, 0, 0)), ('West', (-SX, 0, 0))]])
    for g in ('Surface', 'Collision'): m.p(g, 'Asphalt').box(0, 0, 2 * SX, 2 * SY, -.22, 0)
    mx, mw = 7.75, 3.0      # crosswalk over the main road (centre x), width
    sy, sw = 11.0, 3.0      # crosswalk over the side road (centre y)
    for sxn in (-1, 1):
        for syn in (-1, 1):
            # Paving from the side-road kerb (x = 4.2) and the main-road kerb (y = 7.7), kerb radius CORNER_R42.
            round_corner(m, sxn, syn, HS + .2, SIDE4_IN, SX, SY, CORNER_R42, [('x', mx - mw / 2, mx + mw / 2), ('y', sy - sw / 2, sy + sw / 2)])
    reach_main = curb_face(CORNER_R42, HS + .2, SIDE4_IN, mx, 'x')   # y of the kerb face at the main-road crosswalk
    reach_side = curb_face(CORNER_R42, HS + .2, SIDE4_IN, sy, 'y')   # x of the kerb face at the side-road crosswalk
    print('RK2 4x2 crosswalk reach', round(reach_main, 3), round(reach_side, 3), '4x4', round(curb_face(CORNER_R4, SIDE4_IN, SIDE4_IN, 11.75, 'y'), 3))
    for sgn in (-1, 1):
        crosswalk_x(m, sgn * mx, -reach_main + .3, reach_main - .3)          # over the main road
        crosswalk_y(m, sgn * sy, -reach_side + .3, reach_side - .3)          # over the side road
        paint(m, sgn * 10.2, sgn * 3.85, .4, 7.1)    # main-road stop bars (east arm: incoming on y>0)
        paint(m, -sgn * 1.9, sgn * 13.5, 3.5, .4)    # side-road stop bars (north arm: incoming on x<0)
        for s in (-1, 1):
            paint(m, sgn * 11.1, s * .12, 1.8, .12); paint(m, sgn * 11.1, s * 3.75, 1.8, .12)
        paint(m, 0, sgn * 14.75, .12, 2.5)
    return m.finish()


def ring_arc(m, group, mat, r0, r1, a0, a1, bottom, top, step=math.radians(3)):
    """Band between radii r0…r1 from angle a0 to a1 (radians, CCW from +X), centre at the origin."""
    n = max(1, math.ceil((a1 - a0) / step))
    outer = [(r1 * math.cos(a0 + (a1 - a0) * i / n), r1 * math.sin(a0 + (a1 - a0) * i / n)) for i in range(n + 1)]
    inner = [(r0 * math.cos(a0 + (a1 - a0) * i / n), r0 * math.sin(a0 + (a1 - a0) * i / n)) for i in reversed(range(n + 1))]
    m.p(group, mat).prism(outer + inner, bottom, top)


def roundabout():
    S, RI, RO, RS = 26.0, 8.0, 14.0, 16.2
    m = k.Module('RK2_Roundabout_52m', 'Кольцевое движение', [(f'Socket_{n}', p) for n, p in [('South', (0, -S, 0)), ('North', (0, S, 0)), ('East', (S, 0, 0)), ('West', (-S, 0, 0))]])
    # Ring asphalt in quarters, arms as polygons whose inner end follows the ring's outer circle (no overlaps).
    for q in range(4):
        for g in ('Surface', 'Collision'): ring_arc(m, g, 'Asphalt', RI, RO, q * math.pi / 2, (q + 1) * math.pi / 2, -.22, 0)
    ze = math.sqrt(RO * RO - 4 * 4)
    arcpts = [(RO * math.sin(t), RO * math.cos(t)) for t in [math.asin(4 / RO) * (1 - 2 * i / 12) for i in range(13)]]   # x from +4 to -4 on the circle, y > 0
    for ang in (0, 90, 180, 270):
        c, s_ = math.cos(math.radians(ang)), math.sin(math.radians(ang))
        def rot(p): return (p[0] * c - p[1] * s_, p[0] * s_ + p[1] * c)
        arm = [(-4, S)] + list(reversed(arcpts)) + [(4, S)]
        for g in ('Surface', 'Collision'): m.p(g, 'Asphalt').prism([rot(p) for p in arm], -.22, 0)
        # Arm sidewalks and kerbs (x = ±5.2 / ±4.1 in the arm frame), from the socket to the ring sidewalk.
        for sx in (-1, 1):
            for g in ('Sidewalk', 'Collision'):
                box = [(sx * 4.2, 13.6), (sx * 6.2, 13.6), (sx * 6.2, S), (sx * 4.2, S)]
                m.p(g, 'Paving').prism([rot(p) for p in box], -.22, .15)
            a0, a1 = rot((sx * 4.1, 13.5)), rot((sx * 4.1, 18.5))
            curb_run(m, *a0, *a1, top=.15)
            a0, a1 = rot((sx * 4.1, 18.5)), rot((sx * 4.1, 21.5)); curb_run(m, *a0, *a1, top=.02)   # lowered at the crosswalk
            a0, a1 = rot((sx * 4.1, 21.5)), rot((sx * 4.1, S)); curb_run(m, *a0, *a1, top=.15)
        # Crosswalk at 20, give-way line (dashed) across the entry lane at 15.2, solid axis on the arm.
        for i in range(8):
            x = -3.5 + i
            p = [rot((x - .25, 18.5)), rot((x + .25, 18.5)), rot((x + .25, 21.5)), rot((x - .25, 21.5))]
            m.p('Markings', 'White').prism(p, .006, .009)
        for i in range(6):
            x = -(.35 + i * .6) - .35      # inbound lane of the arm: heading to the centre, its right side is x < 0
            p = [rot((x, 15.0)), rot((x + .35, 15.0)), rot((x + .35, 15.4)), rot((x, 15.4))]
            m.p('Markings', 'White').prism(p, .006, .009)
        p = [rot((-.06, 15.6)), rot((.06, 15.6)), rot((.06, S)), rot((-.06, S))]
        m.p('Markings', 'White').prism(p, .006, .009)
    # Outer sidewalk ring between the arms (slightly lower top where it runs under the arm sidewalks) and its kerb.
    for q in range(4):
        a0 = q * math.pi / 2 + math.radians(17.5); a1 = (q + 1) * math.pi / 2 - math.radians(17.5)   # clear of the arm carriageway
        for g in ('Sidewalk', 'Collision'): ring_arc(m, g, 'Paving', RO + .2, RS, a0, a1, -.22, .145)
        n = 24
        for i in range(n):
            b0 = q * math.pi / 2 + math.radians(17) + (math.pi / 2 - math.radians(34)) * i / n
            b1 = q * math.pi / 2 + math.radians(17) + (math.pi / 2 - math.radians(34)) * (i + 1) / n
            ring_arc(m, 'Curb', 'Concrete', RO, RO + .2, b0 + .0005, b1 - .0005, -.22, .15)
        ring_arc(m, 'Collision', 'Concrete', RO, RO + .2, q * math.pi / 2 + math.radians(17), (q + 1) * math.pi / 2 - math.radians(17), -.22, .15)
    # Central island: kerb and a raised grass-coloured top; edge lines of the ring.
    for q in range(4):
        a0, a1 = q * math.pi / 2, (q + 1) * math.pi / 2
        ring_arc(m, 'Curb', 'Concrete', RI - .2, RI, a0 + .001, a1 - .001, -.22, .2)
        ring_arc(m, 'Collision', 'Concrete', RI - .2, RI, a0, a1, -.22, .2)
        disk = [((RI - .2) * math.cos(a0 + (a1 - a0) * i / 30), (RI - .2) * math.sin(a0 + (a1 - a0) * i / 30)) for i in range(31)] + [(0.0, 0.0)]
        m.p('Island', 'Grass').prism(disk, -.22, .18); m.p('Collision', 'Grass').prism(disk, -.22, .18)
        ring_arc(m, 'Markings', 'White', RI + .35, RI + .47, a0, a1, .006, .009)
    return m.finish()


def rail_crossing():
    m = k.Module('RK2_Road_RailCrossing_20m', 'Ж/д переезд в городе', [('Socket_Start', (0, 0, 0)), ('Socket_End', (0, 20, 0))])
    y0, y1 = 8.2, 11.8
    for a, b in [(0, y0), (y1, 20)]:
        for g in ('Surface', 'Collision'): m.p(g, 'Asphalt').box(0, (a + b) / 2, 8, b - a, -.22, 0)
        for s in (-1, 1):
            for g in ('Sidewalk', 'Collision'): m.p(g, 'Paving').box(s * 5.2, (a + b) / 2, 2, b - a, -.22, .15)
            curb_run(m, s * 4.1, a, s * 4.1, b)
            paint(m, s * 3.65, (a + b) / 2, .12, b - a)
        paint(m, 0, (a + b) / 2, .12, b - a)      # solid axis: no overtaking near the crossing
    # Concrete deck across road and sidewalks, rails flush with it.
    for g in ('Surface', 'Collision'): m.p(g, 'Deck').box(0, 10, 12.4, y1 - y0, -.22, 0.0)
    for r in (-.8, .8):
        m.p('Rails', 'Steel').box(0, 10 + r, 12.4, .07, -.05, .012)
        m.p('Rails', 'Steel').box(0, 10 + r - .09, 12.4, .02, -.05, .004)   # flangeway edge
    # Stop lines before the crossing (right-hand lane of each approach) — 5.6 m before the near rail.
    paint(m, 1.9, 3.6, 3.5, .4); paint(m, -1.9, 16.4, 3.5, .4)
    return m.finish()


def main():
    k.material('Asphalt', (.115, .13, .145), texture='RK_Asphalt.png')
    k.material('Paving', (.51, .53, .51), texture='RK_Paving.png')
    k.material('Concrete', (.63, .65, .62)); k.material('White', (.87, .88, .83), .78)
    k.material('Grass', (.2, .36, .16), .95); k.material('Deck', (.42, .43, .41), .85); k.material('Steel', (.33, .32, .31), .45)
    k.OUT = OUT     # exports go to the v2 folder; textures above were loaded from v1
    k.REVIEW = ROOT / 'artifacts/visual-review/road-kit-v2'; (k.REVIEW / 'models').mkdir(parents=True, exist_ok=True)
    roots = [straight4(), cross4x4(), cross4x2(), roundabout(), rail_crossing()]
    for r in roots: k.export(r)
    k.lighting()
    k.visibility([roots[1]]); k.render('cross-4x4', (-30, -36, 40), (0, 0, 0), 46)
    k.visibility([roots[2]]); k.render('cross-4x2', (-28, -34, 38), (0, 0, 0), 44)
    k.visibility([roots[3]]); k.render('roundabout', (-40, -48, 60), (0, 0, 0), 64)
    k.visibility([roots[4]]); k.render('rail-crossing', (-16, -6, 10), (0, 10, 0))
    k.visibility([roots[0]]); k.render('urban4', (-14, -3, 7), (0, 10, 0))
    import bpy
    bpy.ops.file.pack_all(); bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE))
    report = {'revision': 'v2', 'createdUtc': datetime.now(timezone.utc).isoformat(), 'exporter': bpy.app.version_string,
              'source': str(SOURCE.relative_to(ROOT)), 'sourceSha256': hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
              'units': 'metres', 'blenderAxes': 'X right / Y forward / Z up', 'laneWidth': 3.5, 'curbHeight': .15,
              'markingOffset': .006, 'moduleCount': len(k.CATALOG), 'modules': k.CATALOG}
    (OUT / 'catalog-v2.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    (ROOT / 'artifacts/reports/road-kit-v2-export.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print('ROAD_KIT_V2_COMPLETE', len(k.CATALOG), flush=True)


main()
