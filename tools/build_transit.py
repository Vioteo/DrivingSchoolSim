"""Public transport kit (prefix PT_): bus, route minibus, tram, tram tracks, stops.

    "C:\\Program Files\\Blender Foundation\\Blender 5.0\\blender.exe" -b --factory-startup ^
        --python-exit-code 1 --python tools/build_transit.py [-- --no-render]

or double-click tools/build_transit.bat. Geometry comes from tools/transit_kit
(pure Python; offline check: python tools/transit_kit/selftest.py).

Writes
  ArtSource/DS_TransitKit.blend                      source (all assets, laid out)
  Assets/DrivingSchool/Art/Transit/PT_*.fbx          one FBX per asset (Unity)
  Assets/DrivingSchool/Art/Transit/PT_Materials.json palette for TransitKitBuilder
  artifacts/reports/transit-manifest.json            SHA256, bounds, triangles, checks
  artifacts/visual-review/transit/*.png, models/*.glb review renders and GLB

Export follows docs/art-pipeline.md section 4: axis_forward='-Z', axis_up='Y',
FBX_SCALE_UNITS; models face -Y in Blender, Socket_Front / Socket_Up empties
let the Unity builder verify the axes after import.
"""
import bpy, bmesh, sys, math, json, hashlib, datetime
from pathlib import Path
from mathutils import Vector

TOOLS = Path(__file__).resolve().parent
ROOT = TOOLS.parent
sys.path.insert(0, str(TOOLS))
from transit_kit import vehicles, infra, checks  # noqa: E402
from transit_kit.materials import PALETTE, GROUND  # noqa: E402

ART = ROOT / 'Assets/DrivingSchool/Art/Transit'
SOURCE = ROOT / 'ArtSource/DS_TransitKit.blend'
REPORTS = ROOT / 'artifacts/reports'
REVIEW = ROOT / 'artifacts/visual-review/transit'


def sha(p):
    return hashlib.sha256(Path(p).read_bytes()).hexdigest()


def materials():
    M = {}
    for name, (col, metal, rough, emis, alpha) in PALETTE.items():
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        m.diffuse_color = (*col, alpha)
        p = m.node_tree.nodes.get('Principled BSDF')
        p.inputs['Base Color'].default_value = (*col, 1)
        p.inputs['Metallic'].default_value = metal
        p.inputs['Roughness'].default_value = rough
        p.inputs['Alpha'].default_value = alpha
        if emis:
            p.inputs['Emission Color'].default_value = (*col, 1)
            p.inputs['Emission Strength'].default_value = emis
        if alpha < 1:
            try:
                m.surface_render_method = 'BLENDED'
            except Exception:
                pass
        M[name] = m
    return M


def planar_uv(me, repeat):
    """Box projection in metres: the dominant normal axis picks the plane."""
    uv = me.uv_layers.new(name='UVMap')
    for poly in me.polygons:
        n = poly.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        for li in poly.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            a, b = [(co.y, co.z), (co.x, co.z), (co.x, co.y)][ax]
            uv.data[li].uv = (a / repeat, b / repeat)


def instantiate(parts, M):
    objs = {}
    coll = bpy.context.scene.collection
    deps = None
    for p in parts:
        if p.kind == 'empty':
            o = bpy.data.objects.new(p.name, None)
            o.empty_display_size = 0.1
        elif p.kind == 'text':
            body, size, extrude = p.text
            c = bpy.data.curves.new(p.name + '_Curve', 'FONT')
            c.body, c.size, c.extrude = body, size, extrude
            c.align_x, c.align_y = 'CENTER', 'CENTER'
            tmp = bpy.data.objects.new(p.name + '_Tmp', c)
            coll.objects.link(tmp)
            deps = bpy.context.evaluated_depsgraph_get()
            me = bpy.data.meshes.new_from_object(tmp.evaluated_get(deps))
            bpy.data.objects.remove(tmp, do_unlink=True)
            bpy.data.curves.remove(c)
            me.name = p.name
            o = bpy.data.objects.new(p.name, me)
            o.data.materials.append(M[p.material])
        else:
            me = bpy.data.meshes.new(p.name)
            me.from_pydata(p.v, [], p.f)
            me.validate(clean_customdata=False)
            bm = bmesh.new()
            bm.from_mesh(me)
            bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
            bmesh.ops.dissolve_degenerate(bm, edges=bm.edges, dist=1e-6)
            # explicit triangulation: concave n-gons (arches, sector caps) stay correct
            bmesh.ops.triangulate(bm, faces=list(bm.faces))
            bm.to_mesh(me)
            bm.free()
            for poly in me.polygons:
                poly.use_smooth = p.smooth
            if p.smooth:
                me.set_sharp_from_angle(angle=math.radians(35))
            planar_uv(me, 2.0 if p.material in GROUND else 1.0)
            o = bpy.data.objects.new(p.name, me)
            o.data.materials.append(M[p.material])
            if p.name.startswith('COL_'):
                o.hide_render = True
                o.display_type = 'WIRE'
        coll.objects.link(o)
        o.location = p.loc
        o.rotation_euler = p.rot
        objs[p.name] = (o, p)
    for name, (o, p) in objs.items():
        if p.parent:
            o.parent = objs[p.parent][0]      # child transform stays in parent space
    return objs


def measure(objs, evaluated=True):
    deps = bpy.context.evaluated_depsgraph_get() if evaluated else None
    tris, lo, hi, mats = 0, [1e9] * 3, [-1e9] * 3, set()
    for o in objs:
        if o.type != 'MESH' or o.name.startswith('COL_'):
            continue
        ev = o.evaluated_get(deps) if evaluated else o
        me = ev.to_mesh()
        me.calc_loop_triangles()
        tris += len(me.loop_triangles)
        for v in me.vertices:
            w = o.matrix_world @ v.co
            lo = [min(a, b) for a, b in zip(lo, w)]
            hi = [max(a, b) for a, b in zip(hi, w)]
        mats.update(m.name for m in o.data.materials if m)
        ev.to_mesh_clear()
    return dict(triangles=tris, min=[round(v, 4) for v in lo], max=[round(v, 4) for v in hi],
                size=[round(b - a, 4) for a, b in zip(lo, hi)], materials=sorted(mats))


def export(root):
    objs = [root] + list(root.children_recursive)
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = root
    fbx = ART / f'{root.name}.fbx'
    bpy.ops.export_scene.fbx(filepath=str(fbx), use_selection=True, object_types={'MESH', 'EMPTY'},
                             axis_forward='-Z', axis_up='Y', apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_UNITS', use_mesh_modifiers=True,
                             mesh_smooth_type='FACE', add_leaf_bones=False, bake_anim=False)
    for o in objs:
        if o.name.startswith('COL_'):
            o.select_set(False)
    glb = REVIEW / 'models' / f'{root.name}.glb'
    bpy.ops.export_scene.gltf(filepath=str(glb), export_format='GLB', use_selection=True, export_apply=True)
    # re-import the FBX into an empty scene and compare the measured bounds
    chk = bpy.data.scenes.new('ExportCheck')
    before = set(bpy.data.objects)
    with bpy.context.temp_override(scene=chk, view_layer=chk.view_layers[0]):
        bpy.ops.import_scene.fbx(filepath=str(fbx))
    new = [o for o in bpy.data.objects if o not in before]
    chk.view_layers[0].update()
    got = measure(new, evaluated=False)
    for o in new:
        bpy.data.objects.remove(o, do_unlink=True)
    bpy.data.scenes.remove(chk)
    return fbx, glb, got


def build_assets():
    out = []
    for fn in vehicles.ALL + infra.ALL:
        parts, meta = fn()
        out.append((parts, meta))
    return out


def lighting():
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'CPU'
    sc.cycles.samples = 24
    sc.cycles.use_denoising = True
    try:
        sc.cycles.denoiser = 'OPENIMAGEDENOISE'
    except Exception:
        pass
    sc.render.resolution_x, sc.render.resolution_y = 1280, 720
    sc.world = bpy.data.worlds.new('Transit_World')
    sc.world.use_nodes = True
    bg = sc.world.node_tree.nodes['Background']
    bg.inputs[0].default_value = (.55, .66, .78, 1)
    bg.inputs[1].default_value = .55
    sun = bpy.data.lights.new('Sun', 'SUN')
    sun.energy, sun.angle = 3.0, .12
    o = bpy.data.objects.new('Sun', sun)
    sc.collection.objects.link(o)
    o.rotation_euler = (.75, -.35, -.6)
    sc.view_settings.view_transform = 'AgX'
    gm = bpy.data.meshes.new('Review_Ground')
    s = 200
    gm.from_pydata([(-s, -s, -.23), (s, -s, -.23), (s, s, -.23), (-s, s, -.23)], [], [(0, 1, 2, 3)])
    g = bpy.data.objects.new('Review_Ground', gm)
    gm.materials.append(bpy.data.materials['PT_Paving'])
    sc.collection.objects.link(g)
    return g


def render(name, pos, target, lens=40, ortho=None):
    cam = bpy.data.cameras.new(name)
    ob = bpy.data.objects.new(name, cam)
    bpy.context.scene.collection.objects.link(ob)
    ob.location = pos
    ob.rotation_euler = (Vector(target) - Vector(pos)).to_track_quat('-Z', 'Y').to_euler()
    cam.lens = lens
    cam.clip_end = 500
    if ortho:
        cam.type, cam.ortho_scale = 'ORTHO', ortho
    bpy.context.scene.camera = ob
    bpy.context.scene.render.filepath = str(REVIEW / f'{name}.png')
    bpy.ops.render.render(write_still=True)
    print('RENDER', name, flush=True)


def main():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else sys.argv[1:]
    no_render = '--no-render' in argv
    for d in (ART, REVIEW / 'models', REPORTS, SOURCE.parent):
        d.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.unit_settings.system, sc.unit_settings.scale_length = 'METRIC', 1.0
    M = materials()
    report = dict(kit='transit', prefix='PT_', script='tools/build_transit.py', generator='tools/transit_kit',
                  utc=datetime.datetime.now(datetime.timezone.utc).isoformat(), blender=bpy.app.version_string,
                  axes='Blender: X right, Y back (front -Y), Z up; FBX -Z forward / Y up', assets=[])
    roots = []
    fails = []
    for parts, meta in build_assets():
        res = checks.run(parts, meta)
        objs = instantiate(parts, M)
        root = objs[meta['id']][0]
        root['catalogId'] = meta['id']
        root['label'] = meta['title']
        root['generator'] = 'tools/transit_kit'
        bpy.context.view_layer.update()
        src = measure([root] + list(root.children_recursive))
        fbx, glb, got = export(root)
        reimport_ok = max(abs(a - b) for a, b in zip(src['size'], got['size'])) < 0.002
        res.append(dict(id='FBX: повторный импорт совпадает по габаритам', status='PASS' if reimport_ok else 'FAIL',
                        value=got['size'], target=src['size']))
        budget = 35000 if meta['kind'] in ('bus', 'minibus', 'tram') else 20000
        res.append(dict(id='Бюджет треугольников', status='PASS' if src['triangles'] <= budget else 'FAIL',
                        value=src['triangles'], target=f'<= {budget}'))
        fails += [(meta['id'], r['id']) for r in res if r['status'] != 'PASS']
        entry = dict(id=meta['id'], title=meta['title'], kind=meta['kind'], fbx=str(fbx.relative_to(ROOT)),
                     fbx_sha256=sha(fbx), glb=str(glb.relative_to(ROOT)), triangles=src['triangles'],
                     bounds_min=src['min'], bounds_max=src['max'], size=src['size'], reimport_size=got['size'],
                     materials=src['materials'],
                     sockets={o.name: [round(c, 4) for c in o.matrix_world.translation]
                              for o in root.children_recursive if o.type == 'EMPTY' and o.name.startswith('Socket_')},
                     meta={k: v for k, v in meta.items() if k not in ('id', 'title')}, checks=res)
        report['assets'].append(entry)
        roots.append(root)
        print(f"ASSET_OK {meta['id']} tris={src['triangles']} size={src['size']} fails={len([r for r in res if r['status'] != 'PASS'])}",
              flush=True)
    (ART / 'PT_Materials.json').write_text(json.dumps(
        {'materials': [dict(name=n, color=list(c) + [a], metallic=m, roughness=r, emission=e)
                       for n, (c, m, r, e, a) in PALETTE.items()]}, indent=2), encoding='utf-8')
    # lay the kit out for the source file and the review renders
    layout = {'PT_Bus_City12': (0, 0, 0), 'PT_Minibus_Route': (-5.5, 12, 0), 'PT_Tram_City': (6.5, 0, 0),
              'PT_Road_TramUrban_20m': (30, 10, 0), 'PT_TramTrack_Grass_20m': (45, 10, 0),
              'PT_TramTrack_Grass_Curve90_R25': (80, 10, 0), 'PT_Sign_5_16_BusStop': (-12, -3, 0),
              'PT_Sign_5_17_TramStop': (-12, 3, 0), 'PT_BusStop_Shelter': (-18, 0, 0),
              'PT_TramStop_Platform_30m': (16, 0, 0), 'PT_Marking_1_17_Zigzag_20m': (-24, 10, 0)}
    for r in roots:
        r.location = layout.get(r.name, (0, 0, 0))
    if not no_render:
        lighting()
        render('overview', (-30, -62, 42), (28, -2, 0), 30)
        render('vehicles-front', (-12, -22, 5), (1.5, 0, 1.6), 40)
        render('vehicles-rear', (16, 24, 6), (0.5, 0, 1.6), 42)
        render('bus-side-right', (-14, 0, 2.2), (0, 0, 1.5), 30)
        render('minibus-side-right', (-14, 12, 1.8), (-5.5, 12, 1.2), 35)
        render('tram-bogie', (2.2, 7.5, 1.1), (6.5, 3.75, 0.5), 30)
        render('stops', (-22, -12, 4), (-15, 0, 1.2), 35)
        render('tram-platform-tracks', (6, -20, 9), (30, 5, 0), 30)
        render('embedded-rails', (28, 6, 2.2), (31, -3, 0), 30)
        render('curve-top', (66, -4, 60), (66, -4, 0), 50, ortho=36)
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE))
    report['source'] = str(SOURCE.relative_to(ROOT))
    report['source_sha256'] = sha(SOURCE)
    report['failed_checks'] = fails
    (REPORTS / 'transit-manifest.json').write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding='utf-8')
    print('TRANSIT_COMPLETE', len(report['assets']), 'assets; failed checks:', fails, flush=True)
    if fails:
        raise SystemExit(1)


main()
