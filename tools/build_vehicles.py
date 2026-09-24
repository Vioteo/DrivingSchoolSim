"""Build all vehicles of the kit in Blender (background mode).

  "C:\\Program Files\\Blender Foundation\\Blender 5.0\\blender.exe" -b --factory-startup ^
      --python-exit-code 1 --python tools/build_vehicles.py [-- DS_Sedan_A DS_Crossover_A ...]

or double-click tools/build_vehicles.bat.

For every vehicle it writes
  ArtSource/Vehicles/<id>.blend, <id>_Traffic.blend           (sources)
  Assets/DrivingSchool/Art/Vehicles/<id>.fbx, <id>_Traffic.fbx (Unity)
The player sedan keeps its old path Assets/DrivingSchool/Art/DS_Sedan_A.fbx
(the existing prefab, scenes and material remaps point at that file) and its
source ArtSource/DS_Sedan_A.blend. The previous files are copied to
ArtSource_Backup/ first. Geometry comes from tools/vehicle_kit (pure Python).
Export settings match the previous sedan export (axis_forward='Y',
axis_up='Z'), so the existing Unity builders keep working.
"""
import bpy, bmesh, sys, math, json, shutil, hashlib, datetime
from pathlib import Path

TOOLS = Path(__file__).resolve().parent
ROOT = TOOLS.parent
sys.path.insert(0, str(TOOLS))
from vehicle_kit import specs, assemble, checks
from vehicle_kit.materials import PALETTE

ART = ROOT / 'Assets/DrivingSchool/Art'
SRC = ROOT / 'ArtSource'
BACKUP = ROOT / 'ArtSource_Backup'
REPORTS = ROOT / 'artifacts/reports'
WEB = ROOT / 'artifacts/visual-review/models'

CONTRACT_PLAYER = ('Wheel_FL', 'Wheel_FR', 'Wheel_RL', 'Wheel_RR', 'SteeringWheel_Pivot', 'Pedal_Clutch', 'Pedal_Brake',
                   'Pedal_Throttle', 'MirrorSurface_L', 'MirrorSurface_R', 'MirrorSurface_Centre', 'Socket_DriverEye',
                   'Socket_CentreOfMass', 'Seat_RearBench', 'Needle_RPM', 'Needle_Speed', 'Transmission_Manual',
                   'Transmission_Automatic', 'Wiper_Pivot_L', 'Wiper_Pivot_R')
CONTRACT_TRAFFIC = ('Wheel_FL', 'Wheel_FR', 'Wheel_RL', 'Wheel_RR', 'Socket_DriverEye', 'MirrorSurface_L', 'MirrorSurface_R')


def sha(p):
    p = Path(p)
    return hashlib.sha256(p.read_bytes()).hexdigest() if p.exists() else None


def paths(spec, lod):
    sfx = '' if lod == 'hi' else '_Traffic'
    if spec['id'] == 'DS_Sedan_A' and lod == 'hi':
        return SRC / 'DS_Sedan_A.blend', ART / 'DS_Sedan_A.fbx'
    return SRC / 'Vehicles' / f"{spec['id']}{sfx}.blend", ART / 'Vehicles' / f"{spec['id']}{sfx}.fbx"


def backup(files, stamp):
    BACKUP.mkdir(exist_ok=True)
    done = {}
    for f in files:
        if f.exists():
            dst = BACKUP / f'{f.stem}_before-vehicle-kit_{stamp}{f.suffix}'
            if not dst.exists():
                shutil.copy2(f, dst)
            done[str(f.relative_to(ROOT))] = str(dst.relative_to(ROOT))
    return done


def materials():
    M = {}
    for name, (col, metal, rough, emis, alpha) in PALETTE.items():
        m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
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


def font():
    for f in (Path('C:/Windows/Fonts/arialbd.ttf'), Path('C:/Windows/Fonts/arial.ttf')):
        if f.exists():
            try:
                return bpy.data.fonts.load(str(f), check_existing=True)
            except Exception:
                pass
    return None


def instantiate(parts, M, fnt):
    objs = {}
    coll = bpy.context.scene.collection
    for p in parts:
        if p.kind == 'empty':
            o = bpy.data.objects.new(p.name, None)
            o.empty_display_size = 0.05
        elif p.kind == 'text':
            body, size, extrude = p.text
            c = bpy.data.curves.new(p.name, 'FONT')
            c.body = body
            c.size = size
            c.extrude = extrude
            c.align_x = 'CENTER'
            c.align_y = 'CENTER'
            if fnt:
                c.font = fnt
            o = bpy.data.objects.new(p.name, c)
            o.data.materials.append(M[p.material])
        else:
            me = bpy.data.meshes.new(p.name)
            me.from_pydata(p.v, [], p.f)
            me.validate(clean_customdata=False)
            bm = bmesh.new()
            bm.from_mesh(me)
            bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
            bmesh.ops.dissolve_degenerate(bm, edges=bm.edges, dist=1e-6)
            bm.to_mesh(me)
            bm.free()
            for poly in me.polygons:
                poly.use_smooth = p.smooth
            if p.smooth:
                try:
                    me.set_sharp_from_angle(angle=math.radians(40))
                except Exception:
                    pass
            o = bpy.data.objects.new(p.name, me)
            o.data.materials.append(M[p.material])
            if p.solidify:
                s = o.modifiers.new('Thickness', 'SOLIDIFY')
                s.thickness = p.solidify
                s.offset = -1
                s.use_even_offset = True
        coll.objects.link(o)
        o.location = p.loc
        o.rotation_euler = p.rot
        if p.hidden:
            o.hide_render = True
        objs[p.name] = (o, p)
    for name, (o, p) in objs.items():
        if p.parent:
            o.parent = objs[p.parent][0]      # child transform stays in parent space
    return objs


def export(root, fbx, glb=None):
    bpy.ops.object.select_all(action='DESELECT')
    objs = [root] + list(root.children_recursive)
    copies = []
    deps = bpy.context.evaluated_depsgraph_get()
    for o in objs:
        if o.type == 'FONT':
            me = bpy.data.meshes.new_from_object(o.evaluated_get(deps))
            c = bpy.data.objects.new(o.name + '_Mesh', me)
            bpy.context.scene.collection.objects.link(c)
            c.parent = o.parent
            c.matrix_world = o.matrix_world
            copies.append(c)
    for o in objs + copies:
        o.select_set(o.type != 'FONT')
    bpy.context.view_layer.objects.active = root
    fbx.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=str(fbx), use_selection=True, object_types={'MESH', 'EMPTY'},
                             axis_forward='Y', axis_up='Z', apply_unit_scale=True, use_mesh_modifiers=True,
                             add_leaf_bones=False, bake_anim=False)
    if glb:
        glb.parent.mkdir(parents=True, exist_ok=True)
        try:
            bpy.ops.export_scene.gltf(filepath=str(glb), export_format='GLB', use_selection=True, export_apply=True)
        except Exception as e:
            print('GLB export skipped:', e)
    for c in copies:
        bpy.data.objects.remove(c, do_unlink=True)


def stats(root):
    deps = bpy.context.evaluated_depsgraph_get()
    tris, lo, hi = 0, [1e9] * 3, [-1e9] * 3
    for o in [root] + list(root.children_recursive):
        if o.type not in {'MESH', 'FONT'} or o.hide_render:
            continue
        ev = o.evaluated_get(deps)
        me = ev.to_mesh()
        me.calc_loop_triangles()
        tris += len(me.loop_triangles)
        for v in me.vertices:
            w = o.matrix_world @ v.co
            lo = [min(a, b) for a, b in zip(lo, w)]
            hi = [max(a, b) for a, b in zip(hi, w)]
        ev.to_mesh_clear()
    return tris, [round(v, 3) for v in lo], [round(v, 3) for v in hi]


def build_one(spec, lod, stamp, report):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    M = materials()
    fnt = font()
    parts, info = assemble.build(spec, lod)
    objs = instantiate(parts, M, fnt)
    root = objs[spec['id']][0]
    root['design'] = spec['title']
    root['generator'] = 'tools/vehicle_kit'
    names = {p.name for p in parts}
    need = CONTRACT_PLAYER if (spec['player'] and lod == 'hi') else CONTRACT_TRAFFIC
    missing = [n for n in need if n not in names]
    if missing:
        raise SystemExit(f"{spec['id']} ({lod}): contract objects missing: {missing}")
    blend, fbx = paths(spec, lod)
    report['backup'].update(backup([blend, fbx], stamp))
    blend.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(blend))
    export(root, fbx, WEB / f"{fbx.stem}.glb")
    tris, lo, hi = stats(root)
    entry = dict(id=spec['id'], lod=lod, title=spec['title'], blend=str(blend.relative_to(ROOT)),
                 fbx=str(fbx.relative_to(ROOT)), fbx_sha256=sha(fbx), triangles=tris,
                 bounds_min=lo, bounds_max=hi, size=[round(b - a, 3) for a, b in zip(lo, hi)],
                 interior=info['style'])
    if lod == 'hi':
        entry['checks'] = checks.run(parts, info)
    report['vehicles'].append(entry)
    print(f"VEHICLE_OK {spec['id']} {lod} tris={tris} size={entry['size']}", flush=True)


def main():
    args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    todo = [s for s in specs.ALL if not args or s['id'] in args]
    stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S')
    report = dict(script='tools/build_vehicles.py', utc=datetime.datetime.utcnow().isoformat() + 'Z',
                  blender=bpy.app.version_string, backup={}, vehicles=[])
    for spec in todo:
        for lod in ('hi', 'lo'):
            build_one(spec, lod, stamp, report)
    REPORTS.mkdir(parents=True, exist_ok=True)
    (REPORTS / 'vehicles-manifest.json').write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding='utf-8')
    fails = [(v['id'], c['id']) for v in report['vehicles'] for c in v.get('checks', []) if c['status'] != 'PASS']
    print('VEHICLES_COMPLETE', len(report['vehicles']), 'files; failed checks:', fails, flush=True)


main()
