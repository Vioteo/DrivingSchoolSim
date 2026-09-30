"""Overpass kit (T65, detailed in T69): a 20 m ramp of a 1+1 town street and a 20 m bridge span, same cross-section as
RK_Road_Urban_20m (tools/build_road_kit.py): carriageway 8 m (lanes 3.65 m, edge lines at ±3.65, dashed axis),
kerbs at ±4.1, sidewalks 2 m at ±5.2. Blender 5: -b --python tools/build_overpass.py.
Metres, X right / Y forward / Z up, road top at the start = 0; the ramp rises RISE over its 20 m (8 %).

T69 detail (the road, sockets and the clearance under the span stay as in T65):
  climb       — foot (grade 0 -> 8 %), ramp (8 %), crest (8 % -> 0): vertical curves, so the road bends smoothly at the
                bottom and at the top instead of kinking by 8 %. A climb is foot + 3 ramps + crest = 6.4 m over 100 m
                (RoadKitTemplates.SlopeHeight must give the same heights);
  bridge span — deck slab on five I-girders with end diaphragms, cornice edge beams with a drip, steel railings
                (posts, handrail, balusters), a pier of two round columns with a chamfered cap and bearings (mid-span,
                as before: the railway under the overpass keeps its distance), drain spouts;
  ramp        — retaining walls with a coping and precast-panel joints, the same railings on top.
Materials are named like the road kit (RK_Asphalt, RK_Paving, RK_Concrete, RK_White, RK_ConcreteDark, RK_Railing) and
remapped in Unity by RoadKitBuilder.BuildOverpass. Numbers must match RoadKitTemplates (RampRiseM, sockets).
"""
import bpy, bmesh, json, hashlib, math
from pathlib import Path
from datetime import datetime, timezone

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'Assets/DrivingSchool/Art/Overpass'
SOURCE = ROOT / 'ArtSource/DS_Overpass.blend'
REPORT = ROOT / 'artifacts/reports/overpass-kit.json'
OUT.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.unit_settings.system = 'METRIC'
bpy.context.scene.unit_settings.scale_length = 1

RISE = 1.6          # ramp rise over 20 m (8 %): four ramps lift the road 6.4 m
LENGTH = 20.0
DECK = 0.8          # structure depth under the asphalt top at the girders' bottom (clearance under the span as in T65)
WALL_DEPTH = 9.0    # ramp side walls and pillars reach this far below the road start (hidden in the ground)
EDGE_IN, EDGE_OUT = 6.2, 6.55   # edge beam / coping between the sidewalk and the outside
MATS = {}


def material(name, color):
    m = bpy.data.materials.new('RK_' + name)
    m.use_nodes = True; m.diffuse_color = (*color, 1)
    m.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value = (*color, 1)
    MATS[name] = m


for n, c in [('Asphalt', (.115, .13, .145)), ('Paving', (.51, .53, .51)), ('Concrete', (.55, .55, .53)), ('White', (.9, .9, .88)),
             ('ConcreteDark', (.3, .3, .29)), ('Railing', (.06, .09, .085))]:
    material(n, c)


def height(h, y):
    """Road height at y: h is the linear rise over LENGTH (number) or a profile function of y (vertical curves, T69)."""
    return h(y) if callable(h) else h * y / LENGTH


def stations(y0, y1, h):
    """Cross-sections along Y: the two ends for a straight profile, every metre in between for a curved one."""
    if not callable(h): return [y0, y1]
    ys = [y0] + [float(k) for k in range(int(math.floor(y0)) + 1, int(math.ceil(y1)))] + [y1]
    return sorted(set(ys))


class Part:
    def __init__(self): self.v = []; self.f = []

    def sweep(self, profile, y0, y1, h, fixed=()):
        """Closed profile [(x, z)] swept along Y from y0 to y1 on the road height (vertices in `fixed` keep their z)."""
        s = len(self.v); n = len(profile); ys = stations(y0, y1, h)
        for y in ys:
            for k, (x, z) in enumerate(profile):
                self.v.append((x, y, z if k in fixed else z + height(h, y)))
        self.f.append(tuple(s + i for i in range(n)))
        last = s + n * (len(ys) - 1)
        self.f.append(tuple(last + i for i in reversed(range(n))))
        for a in range(len(ys) - 1):
            o0, o1 = s + a * n, s + (a + 1) * n
            for i in range(n):
                j = (i + 1) % n
                self.f.append((o0 + i, o1 + i, o1 + j, o0 + j))

    def block(self, x0, x1, y0, y1, bottom, top, h):
        """Box x0..x1, y0..y1 whose bottom/top follow the road height (bottom as a 1-tuple = fixed depth)."""
        fixed = isinstance(bottom, tuple)
        b = bottom[0] if fixed else bottom
        self.sweep([(x0, b), (x1, b), (x1, top), (x0, top)], y0, y1, h, (0, 1) if fixed else ())

    def prism(self, profile, y0, y1, h):
        """Profile [(x, z)] (closed polygon) swept along Y from y0 to y1, following the road height."""
        self.sweep(profile, y0, y1, h)

    def cylinder(self, cx, cy, r, z0, z1, seg=16):
        s = len(self.v)
        for z in (z0, z1):
            for k in range(seg):
                a = 2 * math.pi * k / seg
                self.v.append((cx + r * math.cos(a), cy + r * math.sin(a), z))
        self.f.append(tuple(s + k for k in range(seg)))
        self.f.append(tuple(s + seg + k for k in reversed(range(seg))))
        for k in range(seg):
            j = (k + 1) % seg
            self.f.append((s + k, s + j, s + seg + j, s + seg + k))


class Module:
    def __init__(self, name): self.name = name; self.parts = {}
    def p(self, group, mat): return self.parts.setdefault((group, mat), Part())

    def finish(self, sockets):
        root = bpy.data.objects.new(self.name, None); bpy.context.collection.objects.link(root)
        for (group, mat), part in self.parts.items():
            me = bpy.data.meshes.new(self.name + '_' + group + '_' + mat)
            me.from_pydata(part.v, [], part.f); me.update()
            bm = bmesh.new(); bm.from_mesh(me)
            bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces)); bmesh.ops.triangulate(bm, faces=list(bm.faces))
            bm.to_mesh(me); bm.free()
            uv = me.uv_layers.new(name='UVMap')
            for loop in me.loops:
                co = me.vertices[loop.vertex_index].co
                uv.data[loop.index].uv = (co.x / 2, (co.y + co.z) / 2)
            ob = bpy.data.objects.new((('COL_' if group == 'Collision' else group + '_') + mat), me)
            bpy.context.collection.objects.link(ob); ob.parent = root; ob.data.materials.append(MATS[mat])
            if group == 'Collision': ob.hide_render = True; ob.display_type = 'WIRE'
        for name, loc in [('Axis_Forward', (0, 1, 0)), ('Axis_Up', (0, 0, 1))] + sockets:
            e = bpy.data.objects.new(name, None); bpy.context.collection.objects.link(e); e.parent = root; e.location = loc
        return root


def street(m, rise):
    """Carriageway, kerbs, sidewalks and markings of a 1+1 street, sloped by `rise`."""
    for g in ('Surface', 'Collision'): m.p(g, 'Asphalt').block(-4, 4, 0, LENGTH, -.22, 0, rise)
    for s in (-1, 1):
        x = (4.2, EDGE_IN) if s > 0 else (-EDGE_IN, -4.2)
        for g in ('Sidewalk', 'Collision'): m.p(g, 'Paving').block(x[0], x[1], 0, LENGTH, -.22, .15, rise)
        k = (4.0, 4.2) if s > 0 else (-4.2, -4.0)
        for g in ('Curb', 'Collision'): m.p(g, 'Concrete').block(k[0], k[1], 0, LENGTH, -.22, .15, rise)
        e = (3.59, 3.71) if s > 0 else (-3.71, -3.59)
        m.p('Markings', 'White').block(e[0], e[1], 0, LENGTH, .006, .009, rise)
    for y in (1.5, 6.5, 11.5, 16.5): m.p('Markings', 'White').block(-.06, .06, y - 1.5, y + 1.5, .006, .009, rise)


def mirror(profile, s):
    """Profile given for the right side (+X); s = -1 mirrors it to the left (winding fixed by the normal recalc)."""
    return [(s * x, z) for x, z in profile]


def railing(m, rise, s):
    """Steel railing on the edge beam: posts every 2.5 m, handrail, bottom rail, balusters every 0.15 m."""
    xc = s * (EDGE_IN + EDGE_OUT) / 2
    base = .25
    for i in range(9):
        y = 0.1 + i * 2.475
        y0, y1 = max(0.0, y - .04), min(LENGTH, y + .04)
        m.p('Railing', 'Railing').block(xc - .04, xc + .04, y0, y1, base, 1.2, rise)
    m.p('Railing', 'Railing').block(xc - .04, xc + .04, 0, LENGTH, 1.12, 1.2, rise)      # handrail
    m.p('Railing', 'Railing').block(xc - .025, xc + .025, 0, LENGTH, .36, .41, rise)     # bottom rail
    k = 0
    y = 0.2
    while y < LENGTH - 0.1:
        if all(abs(y - (0.1 + i * 2.475)) > .1 for i in range(9)):
            m.p('Railing', 'Railing').block(xc - .011, xc + .011, y - .011, y + .011, .41, 1.12, rise)
            k += 1
        y += 0.15
    for g in ('Collision',):
        m.p(g, 'Concrete').block(xc - .05, xc + .05, 0, LENGTH, base, 1.2, rise)


def bridge():
    m = Module('RK_Bridge_20m')
    street(m, 0)
    # deck slab under the road and the sidewalks
    for g in ('Deck', 'Collision'): m.p(g, 'Concrete').block(-EDGE_IN, EDGE_IN, 0, LENGTH, -.42, -.22, 0)
    # five I-girders and end diaphragms; the girders' bottom is the clearance line (-DECK)
    for x in (-4.8, -2.4, 0.0, 2.4, 4.8):
        m.p('Girders', 'Concrete').block(x - .09, x + .09, 0, LENGTH, -DECK + .1, -.42, 0)
        m.p('Girders', 'Concrete').block(x - .3, x + .3, 0, LENGTH, -DECK, -DECK + .1, 0)
        m.p('Girders', 'Concrete').prism([(x - .3, -DECK + .1), (x + .3, -DECK + .1), (x + .09, -DECK + .22), (x - .09, -DECK + .22)], 0, LENGTH, 0)
    for y0, y1 in ((0.0, 0.3), (LENGTH - 0.3, LENGTH), (LENGTH / 2 - .15, LENGTH / 2 + .15)):
        m.p('Girders', 'Concrete').block(-5.1, 5.1, y0, y1, -DECK + .1, -.42, 0)
    m.p('Collision', 'Concrete').block(-5.1, 5.1, 0, LENGTH, -DECK, -.42, 0)
    for s in (-1, 1):
        # cornice edge beam with an upstand, a drip groove and a chamfered bottom
        prof = [(EDGE_IN, .25), (EDGE_OUT - .05, .25), (EDGE_OUT, .2), (EDGE_OUT, -.55), (EDGE_OUT - .08, -.6),
                (EDGE_OUT - .12, -.68), (EDGE_OUT - .25, -.75), (EDGE_IN - .1, -.75), (EDGE_IN - .1, -.42), (EDGE_IN, -.42)]
        for g in ('Cornice',): m.p(g, 'Concrete').prism(mirror(prof, s), 0, LENGTH, 0)
        m.p('Collision', 'Concrete').block(min(s * EDGE_IN, s * EDGE_OUT), max(s * EDGE_IN, s * EDGE_OUT), 0, LENGTH, -.75, .25, 0)
        # dark band along the cornice face and expansion joint strips at both ends
        x0, x1 = sorted((s * EDGE_OUT, s * (EDGE_OUT + .01)))
        m.p('Band', 'ConcreteDark').block(x0, x1, 0, LENGTH, -.18, -.08, 0)
        for y0, y1 in ((0, .04), (LENGTH - .04, LENGTH)):
            m.p('Joint', 'ConcreteDark').block(min(s * 4.2, s * EDGE_OUT), max(s * 4.2, s * EDGE_OUT), y0, y1, .15, .152, 0)
        # drain spouts under the kerb line
        for y in (5.0, 15.0):
            m.p('Drain', 'Railing').block(min(s * 4.0, s * 4.12), max(s * 4.0, s * 4.12), y - .06, y + .06, -1.05, -.42, 0)
        railing(m, 0, s)
    m.p('Joint', 'ConcreteDark').block(-4.0, 4.0, 0, .04, 0, .002, 0)
    m.p('Joint', 'ConcreteDark').block(-4.0, 4.0, LENGTH - .04, LENGTH, 0, .002, 0)
    # pier at mid-span: two round columns, a chamfered cap and bearing pads under the girders
    yc = LENGTH / 2
    cap_top, cap_bot = -DECK - .06, -DECK - .9
    for x in (-2.8, 2.8):
        m.p('Pier', 'Concrete').cylinder(x, yc, .55, -WALL_DEPTH, cap_bot + .02, 20)
        m.p('Collision', 'Concrete').block(x - .55, x + .55, yc - .55, yc + .55, (-WALL_DEPTH,), cap_bot, 0)
    cap = [(-4.3, cap_top), (4.3, cap_top), (4.3, cap_top - .35), (3.4, cap_bot), (-3.4, cap_bot), (-4.3, cap_top - .35)]
    m.p('Pier', 'Concrete').prism(cap, yc - .75, yc + .75, 0)
    m.p('Collision', 'Concrete').block(-4.3, 4.3, yc - .75, yc + .75, cap_bot, cap_top, 0)
    for x in (-4.8, -2.4, 0.0, 2.4, 4.8):
        if abs(x) <= 4.3:
            m.p('Bearings', 'ConcreteDark').block(x - .25, x + .25, yc - .3, yc + .3, cap_top, -DECK, 0)
    return m.finish([('Socket_Start', (0, 0, 0)), ('Socket_End', (0, LENGTH, 0))])


GRADE = RISE / LENGTH   # 8 %


def foot(y): return GRADE * y * y / (2 * LENGTH)                  # grade 0 -> 8 % (vertical curve, T69)
def crest(y): return GRADE * y - GRADE * y * y / (2 * LENGTH)     # grade 8 % -> 0


def climb(name, h):
    """A climbing module: RK_Ramp_20m (h = RISE, straight 8 %), RK_RampFoot_20m / RK_RampCrest_20m (vertical curves)."""
    m = Module(name)
    street(m, h)
    for s in (-1, 1):
        # coping on top of the retaining wall (same edge beam line as on the bridge)
        prof = [(EDGE_IN, .25), (EDGE_OUT - .05, .25), (EDGE_OUT, .2), (EDGE_OUT, -.3), (EDGE_OUT - .12, -.38), (EDGE_IN, -.38)]
        m.p('Coping', 'Concrete').prism(mirror(prof, s), 0, LENGTH, h)
        m.p('Collision', 'Concrete').block(min(s * EDGE_IN, s * EDGE_OUT), max(s * EDGE_IN, s * EDGE_OUT), 0, LENGTH, -.38, .25, h)
        # retaining wall: face set back from the coping, precast panels 4 m wide with dark joints and a plinth
        w = sorted((s * EDGE_IN, s * (EDGE_OUT - .1)))
        for g in ('Wall', 'Collision'): m.p(g, 'Concrete').block(w[0], w[1], 0, LENGTH, (-WALL_DEPTH,), -.38, h)
        xf = s * (EDGE_OUT - .1)
        for i in range(1, 5):
            y = i * 4.0
            if y >= LENGTH: break
            jx = sorted((xf, xf + s * .012))
            m.p('Joints', 'ConcreteDark').block(jx[0], jx[1], y - .03, y + .03, (-WALL_DEPTH,), -.4, h)
        railing(m, h, s)
    m.p('Fill', 'Concrete').block(-EDGE_IN, EDGE_IN, 0, LENGTH, (-WALL_DEPTH,), -.22, h)
    return m.finish([('Socket_Start', (0, 0, 0)), ('Socket_End', (0, LENGTH, height(h, LENGTH)))])


roots = [climb('RK_RampFoot_20m', foot), climb('RK_Ramp_20m', RISE), climb('RK_RampCrest_20m', crest), bridge()]
report = {'revision': 'overpass-v3', 'utc': datetime.now(timezone.utc).isoformat(), 'blender': bpy.app.version_string,
          'riseM': RISE, 'lengthM': LENGTH, 'clearanceUnderSpanM': -DECK, 'modules': []}
for root in roots:
    bpy.ops.object.select_all(action='DESELECT')
    for ob in [root] + list(root.children_recursive): ob.select_set(True)
    bpy.context.view_layer.objects.active = root
    fbx = OUT / (root.name + '.fbx')
    bpy.ops.export_scene.fbx(filepath=str(fbx), use_selection=True, object_types={'MESH', 'EMPTY'}, axis_forward='-Z', axis_up='Y',
                             apply_unit_scale=True, add_leaf_bones=False, bake_anim=False, path_mode='RELATIVE')
    tris = sum(len(o.data.polygons) for o in root.children_recursive if o.type == 'MESH' and not o.name.startswith('COL_'))
    report['modules'].append({'name': root.name, 'fbx': str(fbx.relative_to(ROOT)), 'triangles': tris,
                              'sha256': hashlib.sha256(fbx.read_bytes()).hexdigest()})
    print('MODULE', root.name, 'tris', tris, flush=True)
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE))
report['sourceSha256'] = hashlib.sha256(SOURCE.read_bytes()).hexdigest()
REPORT.write_text(json.dumps(report, indent=2), encoding='utf8')
print('OVERPASS_KIT_PASS', len(roots), flush=True)
