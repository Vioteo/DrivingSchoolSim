"""Overpass kit (T65): a 20 m ramp of a 1+1 town street and a 20 m bridge span, same cross-section as
RK_Road_Urban_20m (tools/build_road_kit.py): carriageway 8 m (lanes 3.65 m, edge lines at ±3.65, dashed axis),
kerbs at ±4.1, sidewalks 2 m at ±5.2, plus parapets. Blender 5: -b --python tools/build_overpass.py.
Metres, X right / Y forward / Z up, road top at the start = 0; the ramp rises RISE over its 20 m (8 %).
Materials are named like the road kit (RK_Asphalt, RK_Paving, RK_Concrete, RK_White) and remapped in Unity by
RoadKitBuilder.BuildOverpass. Numbers must match RoadKitTemplates (RampRiseM, sockets).
"""
import bpy, bmesh, json, hashlib
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
DECK = 0.8          # bridge deck thickness under the asphalt top
WALL_DEPTH = 9.0    # ramp side walls and pillars reach this far below the road start (hidden in the ground)
MATS = {}

def material(name, color):
    m = bpy.data.materials.new('RK_' + name)
    m.use_nodes = True; m.diffuse_color = (*color, 1)
    m.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value = (*color, 1)
    MATS[name] = m

for n, c in [('Asphalt', (.115, .13, .145)), ('Paving', (.51, .53, .51)), ('Concrete', (.55, .55, .53)), ('White', (.9, .9, .88))]:
    material(n, c)

class Part:
    def __init__(self): self.v = []; self.f = []
    def block(self, x0, x1, y0, y1, bottom, top, rise):
        """Box x0..x1, y0..y1 whose bottom/top follow the ramp: z = off + rise * y / LENGTH (bottom None = fixed depth)."""
        s = len(self.v)
        def z(off, y, fixed=False): return off if fixed else off + rise * y / LENGTH
        fixed = isinstance(bottom, tuple)
        b = bottom[0] if fixed else bottom
        for y in (y0, y1):
            for x in (x0, x1):
                self.v.append((x, y, z(b, y, fixed)))
        for y in (y0, y1):
            for x in (x0, x1):
                self.v.append((x, y, z(top, y)))
        # 0:(x0,y0,b) 1:(x1,y0,b) 2:(x0,y1,b) 3:(x1,y1,b) 4..7 same on top
        for f in [(0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)]:
            self.f.append(tuple(s + i for i in f))

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
    """Carriageway, kerbs, sidewalks, markings and parapets of a 1+1 street, sloped by `rise`."""
    for g in ('Surface', 'Collision'): m.p(g, 'Asphalt').block(-4, 4, 0, LENGTH, -.22, 0, rise)
    for s in (-1, 1):
        x = (4.2, 6.2) if s > 0 else (-6.2, -4.2)
        for g in ('Sidewalk', 'Collision'): m.p(g, 'Paving').block(x[0], x[1], 0, LENGTH, -.22, .15, rise)
        k = (4.0, 4.2) if s > 0 else (-4.2, -4.0)
        for g in ('Curb', 'Collision'): m.p(g, 'Concrete').block(k[0], k[1], 0, LENGTH, -.22, .15, rise)
        w = (6.2, 6.45) if s > 0 else (-6.45, -6.2)
        for g in ('Parapet', 'Collision'): m.p(g, 'Concrete').block(w[0], w[1], 0, LENGTH, -.22, 1.1, rise)
        e = (3.59, 3.71) if s > 0 else (-3.71, -3.59)
        m.p('Markings', 'White').block(e[0], e[1], 0, LENGTH, .006, .009, rise)
    for y in (1.5, 6.5, 11.5, 16.5): m.p('Markings', 'White').block(-.06, .06, y - 1.5, y + 1.5, .006, .009, rise)

def ramp():
    m = Module('RK_Ramp_20m')
    street(m, RISE)
    # Retaining walls down into the ground: the ramp is an embankment, nothing drives under it.
    for s in (-1, 1):
        w = (6.2, 6.45) if s > 0 else (-6.45, -6.2)
        m.p('Wall', 'Concrete').block(w[0], w[1], 0, LENGTH, (-WALL_DEPTH,), -.22, RISE)
    m.p('Fill', 'Concrete').block(-6.2, 6.2, 0, LENGTH, (-WALL_DEPTH,), -.22, RISE)
    return m.finish([('Socket_Start', (0, 0, 0)), ('Socket_End', (0, LENGTH, RISE))])

def bridge():
    m = Module('RK_Bridge_20m')
    street(m, 0)
    for g in ('Deck', 'Collision'): m.p(g, 'Concrete').block(-6.45, 6.45, 0, LENGTH, -DECK, -.22, 0)
    # Two columns under the middle of the span with a cross beam.
    for x in (-2.8, 2.8):
        for g in ('Pillar', 'Collision'): m.p(g, 'Concrete').block(x - .6, x + .6, LENGTH / 2 - .6, LENGTH / 2 + .6, (-WALL_DEPTH,), -DECK, 0)
    for g in ('Pillar', 'Collision'): m.p(g, 'Concrete').block(-4.2, 4.2, LENGTH / 2 - .7, LENGTH / 2 + .7, -DECK - .7, -DECK, 0)
    return m.finish([('Socket_Start', (0, 0, 0)), ('Socket_End', (0, LENGTH, 0))])

roots = [ramp(), bridge()]
report = {'revision': 'overpass-v1', 'utc': datetime.now(timezone.utc).isoformat(), 'blender': bpy.app.version_string,
          'riseM': RISE, 'lengthM': LENGTH, 'modules': []}
for root in roots:
    bpy.ops.object.select_all(action='DESELECT')
    for ob in [root] + list(root.children_recursive): ob.select_set(True)
    bpy.context.view_layer.objects.active = root
    fbx = OUT / (root.name + '.fbx')
    bpy.ops.export_scene.fbx(filepath=str(fbx), use_selection=True, object_types={'MESH', 'EMPTY'}, axis_forward='-Z', axis_up='Y',
                             apply_unit_scale=True, add_leaf_bones=False, bake_anim=False, path_mode='RELATIVE')
    report['modules'].append({'name': root.name, 'fbx': str(fbx.relative_to(ROOT)), 'sha256': hashlib.sha256(fbx.read_bytes()).hexdigest()})
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE))
report['sourceSha256'] = hashlib.sha256(SOURCE.read_bytes()).hexdigest()
REPORT.write_text(json.dumps(report, indent=2), encoding='utf8')
print('OVERPASS_KIT_PASS', len(roots), flush=True)
