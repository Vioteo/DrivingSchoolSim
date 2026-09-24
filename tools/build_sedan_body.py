"""Replace the exterior skin of the sedan with the shaped body from
tools/sedan_body_shape.py (plan-view rounding, tumblehome, tuck-under,
flared arches, integrated bumpers, lamps that follow the surface).

Cabin, greenhouse (roof, glass, pillars), wheels, mirrors, sockets and every
name used by code are left untouched.

Run from the project root (Blender 5, background), or double-click tools/build_sedan_body.bat:
  "C:\\Program Files\\Blender Foundation\\Blender 5.0\\blender.exe" -b ArtSource/DS_Sedan_A.blend --python-exit-code 1 --python tools/build_sedan_body.py

Before changing anything it copies the current .blend and FBX to
ArtSource_Backup/. The script is idempotent: running it again rebuilds the
same body.
"""
import bpy, bmesh, sys, math, json, shutil, hashlib, datetime
from pathlib import Path
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).parent))
import build_art as a
import sedan_body_shape as S

ROOT = a.ROOT
BLEND = ROOT / 'ArtSource/DS_Sedan_A.blend'
FBX = ROOT / 'Assets/DrivingSchool/Art/DS_Sedan_A.fbx'
BACKUP = ROOT / 'ArtSource_Backup'
REPORT = ROOT / 'artifacts/reports/sedan-body.json'

# Old exterior parts (and anything this script created on a previous run).
REPLACED = ('FrontWing', 'DoorFront', 'DoorRear', 'RearQuarter', 'Hood', 'Trunk',
            'BumperFacia', 'Grille_', 'GrilleBar', 'NumberPlate', 'LampUnit', 'DRL',
            'Projector', 'Indicator', 'Shell_FrontUpperFacia', 'Shell_RearUpperFacia',
            'Shell_Undertray', 'WheelArchLiner', 'PanelGap', 'Handle_Door',
            'Headlight', 'Taillight', 'TurnSignal', 'ReverseLight', 'Diffuser', 'Intake_Lower')
# Names that code looks up; the script must never delete these.
CONTRACT = ('Wheel_', 'Pedal_', 'SteeringWheel_Pivot', 'MirrorSurface_', 'Socket_',
            'Needle_', 'Wiper_Pivot', 'Seat_RearBench', 'Door_Front', 'Door_Rear')


def sha(p):
    return hashlib.sha256(Path(p).read_bytes()).hexdigest() if Path(p).exists() else None


def backup():
    BACKUP.mkdir(exist_ok=True)
    stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S')
    out = {}
    for src in (BLEND, FBX):
        if src.exists():
            dst = BACKUP / f'{src.stem}_before-body_{stamp}{src.suffix}'
            shutil.copy2(src, dst)
            out[src.name] = str(dst.relative_to(ROOT))
    return out


def remove_tree(o):
    for c in list(o.children_recursive):
        bpy.data.objects.remove(c, do_unlink=True)
    bpy.data.objects.remove(o, do_unlink=True)


def build_mesh(m, root):
    obj = a.mesh(m.name, m.v, m.f, m.material, root)
    # Rows that meet at the nose/tail centre collapse to a point: weld them.
    bm = bmesh.new(); bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.dissolve_degenerate(bm, edges=bm.edges, dist=1e-6)
    bm.to_mesh(obj.data); bm.free()
    for p in obj.data.polygons:
        p.use_smooth = True
    if m.solidify:
        sol = obj.modifiers.new('Sheet metal', 'SOLIDIFY')
        sol.thickness = m.solidify
        sol.offset = -1            # thickness grows inwards, outer surface stays put
        sol.use_even_offset = True
    obj.modifiers.new('Surface normals', 'WEIGHTED_NORMAL').keep_sharp = True
    return obj


def plates(root):
    made = []
    for key, (y, z, w, h) in S.plate_quads().items():
        s = 1 if key == 'front' else -1
        o = a.box('NumberPlate_' + str(s), (0, y, z), (w, 0.012, h), 'Paint_White', .006, root)
        # Text lies in XY; +90 deg about X makes it face -Y (rear, readable).
        # The front plate is additionally turned 180 deg about Z so it faces +Y
        # and still reads left-to-right (the old script mirrored it).
        rot = (math.pi / 2, 0, math.pi if s > 0 else 0)
        t = a.text3('NumberPlateText', 'DS  01', (0, y + s * 0.0075, z), .066, 'Interior_Graphite', rot, root)
        made += [o.name, t.name]
    return made


def main():
    if bpy.data.filepath and Path(bpy.data.filepath).resolve() != BLEND.resolve():
        raise SystemExit('Open ArtSource/DS_Sedan_A.blend, not ' + bpy.data.filepath)
    a.M = {m.name: m for m in bpy.data.materials}
    root = bpy.data.objects['DS_Sedan_A']
    report = {'script': 'tools/build_sedan_body.py', 'utc': datetime.datetime.utcnow().isoformat() + 'Z',
              'before': {'blend_sha256': sha(BLEND), 'fbx_sha256': sha(FBX)}}
    report['backup'] = backup()

    removed = []
    for o in list(root.children):
        if o.name.startswith(REPLACED) and not o.name.startswith(CONTRACT):
            removed.append(o.name)
            remove_tree(o)
    report['removed'] = sorted(removed)

    made = []
    for m in S.build():
        made.append(build_mesh(m, root).name)
    made += plates(root)
    report['created'] = sorted(made)

    missing = [n for n in ('Wheel_FL', 'Wheel_FR', 'Wheel_RL', 'Wheel_RR', 'SteeringWheel_Pivot',
                           'Pedal_Clutch', 'Pedal_Brake', 'Pedal_Throttle', 'MirrorSurface_L',
                           'MirrorSurface_R', 'MirrorSurface_Centre', 'Socket_DriverEye',
                           'Seat_RearBench') if not any(o.name.startswith(n) for o in root.children_recursive)]
    if missing:
        raise SystemExit('Contract objects missing: ' + ', '.join(missing))

    bpy.context.view_layer.update()
    lo = Vector((1e9,) * 3); hi = Vector((-1e9,) * 3)
    deps = bpy.context.evaluated_depsgraph_get()
    tris = 0
    for o in [root] + list(root.children_recursive):
        if o.type != 'MESH' or o.hide_render:
            continue
        ev = o.evaluated_get(deps); me = ev.to_mesh(); me.calc_loop_triangles()
        tris += len(me.loop_triangles)
        for v in me.vertices:
            p = o.matrix_world @ v.co
            lo = Vector(map(min, lo, p)); hi = Vector(map(max, hi, p))
        ev.to_mesh_clear()
    report['bounds_m'] = {'min': list(lo), 'max': list(hi), 'size': list(hi - lo)}
    report['triangles_visible'] = tris

    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND))
    a.export('DS_Sedan_A', root)          # same FBX settings as before (axis_forward='Y', axis_up='Z')
    report['after'] = {'blend_sha256': sha(BLEND), 'fbx_sha256': sha(FBX)}
    REPORT.parent.mkdir(parents=True, exist_ok=True)
    REPORT.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding='utf-8')
    print('SEDAN_BODY_COMPLETE', json.dumps({k: report[k] for k in ('bounds_m', 'triangles_visible')}), flush=True)


main()
