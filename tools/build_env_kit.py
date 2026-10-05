"""Environment kit (prefix EN_): forest, country road, guardrails, yards, parking (T73).

    "C:\\Program Files\\Blender Foundation\\Blender 5.0\\blender.exe" -b --factory-startup ^
        --python-exit-code 1 --python tools/build_env_kit.py [-- --no-render] [--only forest,road] [--fast]

or double-click tools/build_env_kit.bat. Geometry: tools/env_kit (bpy).

Writes
  ArtSource/DS_EnvKit.blend                         source: every asset in its own collection (excluded
                                                    from the view layer) + review scenes built from instances
  Assets/DrivingSchool/Art/EnvKit/EN_*.fbx          one FBX per asset (Unity)
  Assets/DrivingSchool/Art/EnvKit/EN_Materials.json palette for the Unity builder
  artifacts/reports/envkit-manifest.json            SHA256, bounds, triangles per LOD, sockets, checks
  artifacts/visual-review/envkit/*.png              review renders

Export follows docs/art-pipeline.md section 4 (axis_forward='-Z', axis_up='Y', FBX_SCALE_UNITS);
models face -Y in Blender, see tools/env_kit/core.py for the author frame.
"""
import bpy, sys, math, json, hashlib, datetime, re, time
from pathlib import Path

TOOLS = Path(__file__).resolve().parent
ROOT = TOOLS.parent
sys.path.insert(0, str(TOOLS))
from env_kit import core, forest, road, barrier, yard, parking, review, checks  # noqa: E402
from env_kit.materials import PALETTE  # noqa: E402

ART = ROOT / 'Assets/DrivingSchool/Art/EnvKit'
SOURCE = ROOT / 'ArtSource/DS_EnvKit.blend'
REPORTS = ROOT / 'artifacts/reports'
REVIEW = ROOT / 'artifacts/visual-review/envkit'
# axis conversion stays on the FBX root node (as in the transit kit): baking it breaks nested pivots (gate boom)
BAKE_SPACE = False
GROUPS = {'forest': forest, 'road': road, 'barrier': barrier, 'yard': yard, 'parking': parking}


def sha(p):
    return hashlib.sha256(Path(p).read_bytes()).hexdigest()


def measure(objs, evaluated=True, skip_lod=True):
    deps = bpy.context.evaluated_depsgraph_get() if evaluated else None
    tris, lo, hi, mats = 0, [1e9] * 3, [-1e9] * 3, set()
    lod_tris = {}
    for o in objs:
        if o.type != 'MESH' or o.name.split('.')[0].startswith('COL_'):
            continue
        ev = o.evaluated_get(deps) if evaluated else o
        me = ev.to_mesh()
        me.calc_loop_triangles()
        n = len(me.loop_triangles)
        m = re.search(r'_LOD(\d)', o.name)
        if m:
            lod_tris[int(m.group(1))] = lod_tris.get(int(m.group(1)), 0) + n
        if not (m and m.group(1) != '0'):
            tris += n
            for v in me.vertices:
                w = o.matrix_world @ v.co
                lo = [min(a, b) for a, b in zip(lo, w)]
                hi = [max(a, b) for a, b in zip(hi, w)]
        mats.update(mm.name for mm in o.data.materials if mm)
        ev.to_mesh_clear()
    return dict(triangles=tris, lod_triangles=lod_tris, min=[round(v, 4) for v in lo], max=[round(v, 4) for v in hi],
                size=[round(b - a, 4) for a, b in zip(lo, hi)], materials=sorted(mats))


SUFFIX = re.compile(r'\.\d{3}$')


def export(root):
    """Blender keeps object names unique per file, so COL_* / Socket_* of later assets get '.001'.
    Those names are a contract with Unity, so for the export they take their plain names back."""
    objs = [root] + list(root.children_recursive)
    wanted = {o: SUFFIX.sub('', o.name) for o in objs}
    if len(set(wanted.values())) != len(wanted):
        raise RuntimeError(f'{root.name}: duplicate part names inside one asset')
    plain = set(wanted.values())
    parked = [(o, o.name) for o in bpy.data.objects if o not in wanted and o.name in plain]
    for o, n in parked:
        o.name = n + '__parked'
    renamed = [(o, o.name) for o in objs if o.name != wanted[o]]
    for o, _ in renamed:
        o.name = wanted[o]
    try:
        bpy.ops.object.select_all(action='DESELECT')
        for o in objs:
            o.select_set(True)
        bpy.context.view_layer.objects.active = root
        fbx = ART / f'{root.name}.fbx'
        bpy.ops.export_scene.fbx(filepath=str(fbx), use_selection=True, object_types={'MESH', 'EMPTY'},
                                 axis_forward='-Z', axis_up='Y', apply_unit_scale=True,
                                 apply_scale_options='FBX_SCALE_UNITS', use_mesh_modifiers=True,
                                 mesh_smooth_type='FACE', add_leaf_bones=False, bake_anim=False,
                                 bake_space_transform=BAKE_SPACE)
    finally:
        for o, n in renamed:
            o.name = n
        for o, n in parked:
            o.name = n
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
    return fbx, got


def main():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    no_render = '--no-render' in argv
    fast = '--fast' in argv
    only = None
    shots = None
    if '--only' in argv:
        only = argv[argv.index('--only') + 1].split(',')
    if '--shots' in argv:
        shots = argv[argv.index('--shots') + 1].split(',')
    no_export = '--no-export' in argv
    for d in (ART, REVIEW, REPORTS, SOURCE.parent):
        d.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.unit_settings.system, sc.unit_settings.scale_length = 'METRIC', 1.0
    M = core.make_materials()
    report = dict(kit='envkit', prefix='EN_', script='tools/build_env_kit.py', generator='tools/env_kit',
                  utc=datetime.datetime.now(datetime.timezone.utc).isoformat(), blender=bpy.app.version_string,
                  axes='Blender: X right, Y back (front -Y), Z up; FBX -Z forward / Y up', assets=[])
    assets = {}
    fails = []
    t0 = time.time()
    for gname, mod in GROUPS.items():
        if only and gname not in only:
            continue
        for fn in mod.ALL:
            a = fn(M)
            a.meta['group'] = gname
            assets[a.id] = a
            bpy.context.view_layer.update()
            src = measure(a.objects())
            res = checks.run(a, src)
            entry = dict(id=a.id, title=a.title, kind=a.kind, group=gname, triangles=src['triangles'],
                         lod_triangles=src['lod_triangles'], bounds_min=src['min'], bounds_max=src['max'],
                         size=src['size'], materials=src['materials'],
                         sockets={core.SUFFIX_RE.sub('', o.name): [round(c, 4) for c in o.matrix_world.translation]
                                  for o in a.root.children_recursive if o.type == 'EMPTY'},
                         meta=a.meta)
            if not no_export:
                fbx, got = export(a.root)
                ok = max(abs(x - y) for x, y in zip(src['size'], got['size'])) < 0.003
                res.append(dict(id='FBX: повторный импорт совпадает по габаритам', status='PASS' if ok else 'FAIL',
                                value=got['size'], target=src['size']))
                entry.update(fbx=str(fbx.relative_to(ROOT)).replace('\\', '/'), fbx_sha256=sha(fbx))
            entry['checks'] = res
            fails += [(a.id, r['id'], r.get('value')) for r in res if r['status'] != 'PASS']
            report['assets'].append(entry)
            print(f"ASSET_OK {a.id} tris={src['triangles']} lods={src['lod_triangles']} size={src['size']} "
                  f"fails={[r['id'] for r in res if r['status'] != 'PASS']}", flush=True)
    print(f'BUILD_TIME {time.time() - t0:.1f}s', flush=True)
    if not no_export:
        from env_kit.materials import TEX, FOLIAGE
        import shutil
        tex_out = ART / 'Textures'
        tex_out.mkdir(parents=True, exist_ok=True)
        used = sorted({t['file'] for t in TEX.values()})
        for f in used:
            shutil.copyfile(TOOLS / 'env_kit' / 'textures' / f'{f}.png', tex_out / f'{f}.png')
        mats = []
        for n, (c, m, r, e, a) in PALETTE.items():
            d = dict(name=n, color=list(c) + [a], metallic=m, roughness=r, emission=e)
            t = TEX.get(n)
            if t:
                d.update(texture=f"Textures/{t['file']}.png", tint=list(t.get('tint', (1, 1, 1))),
                         alphaClip=bool(t.get('alpha')), doubleSided=n in FOLIAGE)
            mats.append(d)
        (ART / 'EN_Materials.json').write_text(json.dumps({'materials': mats}, indent=2), encoding='utf-8')
        report['textures'] = {f: sha(tex_out / f'{f}.png') for f in used}
    # hide the kit collections: review scenes instance them
    for lc in bpy.context.view_layer.layer_collection.children:
        if lc.name in assets:
            lc.exclude = True
    review.build(assets, REVIEW, render=not no_render, fast=fast, shots=shots)
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE))
    report['source'] = str(SOURCE.relative_to(ROOT)).replace('\\', '/')
    report['source_sha256'] = sha(SOURCE)
    report['failed_checks'] = fails
    if not only:
        (REPORTS / 'envkit-manifest.json').write_text(json.dumps(report, indent=2, ensure_ascii=False),
                                                      encoding='utf-8')
    print('ENVKIT_COMPLETE', len(report['assets']), 'assets; failed checks:', fails, flush=True)
    if fails:
        raise SystemExit(1)


main()
