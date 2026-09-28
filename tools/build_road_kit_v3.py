"""Road Kit v3 (T66): pieces that close the town into loops. Blender 5: -b --python tools/build_road_kit_v3.py

Same cross-section and helpers as v2 (tools/build_road_kit_v2.py, imported). Exports to Assets/DrivingSchool/Art/RoadKitV3;
v1 and v2 are untouched. The graph templates (Code/Simulation/RoadGraph/RoadKitTemplatesV2.cs, "v3" section) use the numbers
below — change both together.

  RK3_Cross_4x2_Plain_24x32m  as RK2_Cross_4x2_24x32m without zebras (crossings are mid-block, T66).
  RK3_Tee_4x2_24x32m          T junction: 2+2 main road along X (sockets E/W at ±12), 1+1 side road to the north
                              (socket N at +16); the south edge runs straight through (kerb 7.6, sidewalk 7.7…10.7).
  RK3_Curve4_90_R26           2+2 road turning 90° right: axis radius 26 around (26, 0); Start (0,0) heading +Y,
                              End (26, 26) heading +X. Carriageway r 18.5…33.5, sidewalks 15.3…18.3 and 33.7…36.7.
  RK3_Road_Urban4_Crosswalk_20m  2+2 straight with a zebra across it at y = 10 (3 m wide), kerbs lowered there.
"""
import sys, math, json, hashlib
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import build_road_kit as k
import build_road_kit_v2 as v2
from datetime import datetime, timezone

ROOT = k.ROOT
OUT = ROOT / 'Assets/DrivingSchool/Art/RoadKitV3'
SOURCE = ROOT / 'ArtSource/DS_RoadKit_v3.blend'
OUT.mkdir(parents=True, exist_ok=True)

R4 = 26.0                       # axis radius of the 2+2 curve
HALF4, SIDE_IN, SIDE_OUT = v2.HALF4, v2.SIDE4_IN, v2.SIDE4_OUT
paint, curb_run = v2.paint, v2.curb_run


def arc_paint(m, r, w, a0, a1, c):
    m.p('Markings', 'White').arc(r - w / 2, r + w / 2, a0, a1, .006, .009, center=c)


def curve4():
    c = (R4, 0)
    m = k.Module('RK3_Curve4_90_R26', 'Поворот 2+2 90° / R26', [('Socket_Start', (0, 0, 0)), ('Socket_End', (R4, R4, 0))])
    q = math.pi / 2
    for g in ('Surface', 'Collision'): m.p(g, 'Asphalt').arc(R4 - HALF4, R4 + HALF4, 0, q, -.22, 0, center=c)
    for r0, r1 in ((R4 - SIDE_OUT, R4 - SIDE_IN), (R4 + SIDE_IN, R4 + SIDE_OUT)):
        for g in ('Sidewalk', 'Collision'): m.p(g, 'Paving').arc(r0, r1, 0, q, -.22, .15, center=c)
    for r in (R4 - 7.7, R4 + 7.5):          # kerbs 0.2 m, faces at ±7.5 like the straight
        m.p('Collision', 'Concrete').arc(r, r + .2, 0, q, -.22, .15, center=c)
        n = math.ceil((r + .1) * q)
        for i in range(n): m.p('Curb', 'Concrete').arc(r, r + .2, i * q / n + .0002, (i + 1) * q / n - .0002, -.22, .15, center=c)
    for s in (-1, 1):
        arc_paint(m, R4 + s * .12, .12, 0, q, c)     # double solid axis
        arc_paint(m, R4 + s * 7.2, .12, 0, q, c)     # edge lines
        r = R4 + s * 3.75                          # lane dividers: 3 m dash, 2 m gap along the arc
        a = 0.0
        while (a + 3 / r) <= q + 1e-6:
            arc_paint(m, r, .12, a, a + 3 / r, c); a += 5 / r
    return m.finish()


def crosswalk4():
    m = k.Module('RK3_Road_Urban4_Crosswalk_20m', 'Проспект 2+2 с переходом', [('Socket_Start', (0, 0, 0)), ('Socket_End', (0, 20, 0))])
    zc, zw = 10.0, 3.0
    lo, hi = zc - zw / 2 - .5, zc + zw / 2 + .5
    for g in ('Surface', 'Collision'): m.p(g, 'Asphalt').box(0, 10, 2 * HALF4, 20, -.22, 0)
    for s in (-1, 1):
        for g in ('Sidewalk', 'Collision'): m.p(g, 'Paving').box(s * (SIDE_IN + SIDE_OUT) / 2, 10, SIDE_OUT - SIDE_IN, 20, -.22, .15)
        # kerb lowered to 2 cm at the crossing
        curb_run(m, s * 7.6, 0, s * 7.6, lo); curb_run(m, s * 7.6, lo, s * 7.6, hi, top=.02); curb_run(m, s * 7.6, hi, s * 7.6, 20)
        for y0, y1 in ((0, lo), (hi, 20)):
            paint(m, s * .12, (y0 + y1) / 2, .12, y1 - y0)       # double solid axis, broken by the zebra
            paint(m, s * 7.2, (y0 + y1) / 2, .12, y1 - y0)       # edge line
        for y in (1.5, 6.5, 16.5): paint(m, s * 3.75, y, .12, 3)   # lane dividers as on the straight, none on the zebra
    v2.crosswalk_y(m, zc, -7.2, 7.2)
    return m.finish()


def main():
    k.material('Asphalt', (.115, .13, .145), texture='RK_Asphalt.png')
    k.material('Paving', (.51, .53, .51), texture='RK_Paving.png')
    k.material('Concrete', (.63, .65, .62)); k.material('White', (.87, .88, .83), .78)
    k.OUT = OUT
    k.REVIEW = ROOT / 'artifacts/visual-review/road-kit-v3'; (k.REVIEW / 'models').mkdir(parents=True, exist_ok=True)
    roots = [v2.cross4x2('RK3_Cross_4x2_Plain_24x32m', crosswalks=False), v2.cross4x2('RK3_Tee_4x2_24x32m', crosswalks=False, sides=(1,)),
             curve4(), crosswalk4()]
    for r in roots: k.export(r)
    k.lighting()
    k.visibility([roots[0]]); k.render('cross-4x2-plain', (-28, -34, 38), (0, 0, 0), 44)
    k.visibility([roots[1]]); k.render('tee-4x2', (-28, -34, 38), (0, 2, 0), 44)
    k.visibility([roots[2]]); k.render('curve4', (-20, -30, 50), (14, 14, 0), 50)
    k.visibility([roots[3]]); k.render('crosswalk4', (-18, -6, 12), (0, 10, 0))
    import bpy
    bpy.ops.file.pack_all(); bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE))
    report = {'revision': 'v3', 'createdUtc': datetime.now(timezone.utc).isoformat(), 'exporter': bpy.app.version_string,
              'source': str(SOURCE.relative_to(ROOT)), 'sourceSha256': hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
              'units': 'metres', 'blenderAxes': 'X right / Y forward / Z up', 'laneWidth': 3.5, 'curbHeight': .15,
              'markingOffset': .006, 'moduleCount': len(k.CATALOG), 'modules': k.CATALOG}
    (OUT / 'catalog-v3.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    (ROOT / 'artifacts/reports/road-kit-v3-export.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print('ROAD_KIT_V3_COMPLETE', len(k.CATALOG), flush=True)


main()
