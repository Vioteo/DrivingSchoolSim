"""Instrument cluster and steering column of the player sedan DS_Sedan_A (T72).

Driver feedback 01.10: "on one dashboard the speedometer and tachometer are very big, on the other small and without
numbers". The sedan's dials were Ø 196 mm each (47 cm together, wider than the steering wheel) under a 52 cm hood.
This script replaces them with the binnacle shared with the vehicle kit (vehicle_kit.interior.cluster_pod): Ø 105 mm
dials 189 mm apart, the centre display between them, a hood with a visor. The steering wheel leans 20° (it stood
vertical) and its column goes down into the knee panel instead of running horizontally into the cluster.
Geometry: tools/sedan_cluster_shape.py (pure Python, previewable without Blender).

Also: the number plates and the roof-lamp lens get their own materials (Plate_White, Interior_Light) instead of
Paint_White, which the profile colour repainted.

Kept (names used by code): Needle_RPM / Needle_Speed (moved), SteeringWheel_Pivot (tilted), everything under it.
New contract objects: GaugeFace_RPM, GaugeFace_Speed, Cluster_Display, Instrument_Hood (DashboardView, VehicleVisuals).

Run after tools/build_sedan_interior.py (Unity closed):
  "C:\\Program Files\\Blender Foundation\\Blender 5.0\\blender.exe" -b ArtSource/DS_Sedan_A.blend --python-exit-code 1 --python tools/build_sedan_cluster.py
or double-click tools/build_sedan_cluster.bat. Re-running is safe: the objects it made are replaced again.
"""
import bpy, bmesh, sys, math, json, shutil, hashlib, datetime
from pathlib import Path
from mathutils import Vector, Matrix

sys.path.insert(0, str(Path(__file__).parent))
import build_art as a
import sedan_cluster_shape as sc
from vehicle_kit.materials import PALETTE

ROOT = a.ROOT
BLEND = ROOT / 'ArtSource/DS_Sedan_A.blend'
FBX = ROOT / 'Assets/DrivingSchool/Art/DS_Sedan_A.fbx'
BACKUP = ROOT / 'ArtSource_Backup'
REPORT = ROOT / 'artifacts/reports/sedan-cluster.json'


def sha(p):
    return hashlib.sha256(Path(p).read_bytes()).hexdigest() if Path(p).exists() else None


def backup():
    BACKUP.mkdir(exist_ok=True)
    stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S')
    out = {}
    for src in (BLEND, FBX):
        if src.exists():
            dst = BACKUP / f'{src.stem}_before-cluster_{stamp}{src.suffix}'
            shutil.copy2(src, dst)
            out[src.name] = str(dst.relative_to(ROOT))
    return out


def material(name):
    """Existing material of the .blend, or a new one from the vehicle-kit palette (same names as in Unity)."""
    m = bpy.data.materials.get(name)
    if m is not None:
        return m
    col, metal, rough, emis, alpha = PALETTE[name]
    return a.mat(name, col, metal, rough, emis, alpha)


def mesh_object(part, verts, parent):
    """Mesh from a vehicle_kit Part; verts are world coordinates, the object keeps its place under parent."""
    me = bpy.data.meshes.new(part.name)
    me.from_pydata(verts, [], part.f)
    me.validate(clean_customdata=False)
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    for poly in me.polygons:
        poly.use_smooth = part.smooth
    if part.smooth:
        try:
            me.set_sharp_from_angle(angle=math.radians(40))
        except Exception:
            pass
    me.materials.append(material(part.material))
    o = bpy.data.objects.new(part.name, me)
    bpy.context.scene.collection.objects.link(o)
    o.parent = parent
    o.matrix_parent_inverse = parent.matrix_world.inverted()
    return o


def main():
    if bpy.data.filepath and Path(bpy.data.filepath).resolve() != BLEND.resolve():
        raise SystemExit('Open ArtSource/DS_Sedan_A.blend, not ' + bpy.data.filepath)
    car = bpy.data.objects['DS_Sedan_A']
    pivot = bpy.data.objects['SteeringWheel_Pivot']
    report = {'script': 'tools/build_sedan_cluster.py', 'utc': datetime.datetime.utcnow().isoformat() + 'Z',
              'backup': backup(), 'cluster': sc.CLUSTER, 'wheel_tilt_deg': sc.WHEEL_TILT_DEG}
    # 1. remove the old cluster, column and stalks (meshes and text curves; the Needle_* empties stay)
    removed = []
    for o in list(car.children_recursive):
        if o.name not in bpy.data.objects or o.type not in {'MESH', 'CURVE', 'FONT'}:
            continue
        if o.name.startswith(sc.REPLACED):
            removed.append(o.name)
            bpy.data.objects.remove(o, do_unlink=True)
    report['removed'] = sorted(removed)
    # 1b. plates and the roof-lamp lens off the Paint_* materials (the profile colour must not reach them)
    fixed = []
    for o in car.children_recursive:
        if o.type != 'MESH':
            continue
        for prefix, mat_name in sc.MATERIAL_FIXES.items():
            if o.name.startswith(prefix):
                for slot in o.material_slots:
                    if slot.material is not None and slot.material.name.startswith('Paint'):
                        slot.material = material(mat_name)
                        fixed.append(o.name)
    report['material_fixes'] = sorted(set(fixed))
    # 2. tilt the steering wheel about its pivot: the rim top leans away from the driver (column axis = local +Y)
    pivot.rotation_mode = 'XYZ'
    pivot.rotation_euler = (-math.radians(sc.WHEEL_TILT_DEG), 0.0, 0.0)
    bpy.context.view_layer.update()
    # 3. new cluster and column
    parts = sc.build_parts()
    empties = {p.name: p for p in parts if p.kind == 'empty'}
    for name, p in empties.items():
        e = bpy.data.objects.get(name)
        if e is None:
            e = bpy.data.objects.new(name, None)
            e.empty_display_size = 0.02
            bpy.context.scene.collection.objects.link(e)
            e.parent = car
            e.matrix_parent_inverse = car.matrix_world.inverted()
        e.matrix_world = Matrix.Translation(Vector(p.loc)) @ e.matrix_world.to_quaternion().to_matrix().to_4x4()
    bpy.context.view_layer.update()
    made = []
    for p in parts:
        if p.kind != 'mesh' or not p.v:
            continue
        if p.parent in empties:              # needle blade and hub: Part-local around the empty (no rotation)
            e = bpy.data.objects[p.parent]
            verts = [tuple(Vector(p.loc) + Vector(empties[p.parent].loc) + Vector(v)) for v in p.v]
            made.append(mesh_object(p, verts, e))
        else:
            made.append(mesh_object(p, [tuple(Vector(p.loc) + Vector(v)) for v in p.v], car))
    report['created'] = sorted(o.name for o in made)
    report['needles'] = {n: [round(c, 4) for c in bpy.data.objects[n].matrix_world.translation] for n in empties}
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND))
    a.export('DS_Sedan_A', car)
    report['after'] = {'blend_sha256': sha(BLEND), 'fbx_sha256': sha(FBX)}
    REPORT.parent.mkdir(parents=True, exist_ok=True)
    REPORT.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding='utf-8')
    print('SEDAN_CLUSTER_COMPLETE', len(made), 'objects;', len(removed), 'removed', flush=True)


if __name__ == '__main__':
    main()
