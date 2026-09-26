"""Rebuild the mirrors of the sedan: rounded door-mirror housings on a sail mount with a
recessed rounded glass and a turn-signal repeater, and a rounded interior mirror.

Replaces MirrorHousing_*, MirrorArm_*, MirrorSurface_*, CentreMirror* (and anything this
script made on an earlier run: MirrorBase_*, MirrorRepeater_*). Names used by code stay:
MirrorSurface_L / _R / _Centre (thin glass slab, origin at the glass centre, facing the driver side).

Run from the project root (Blender 5, background):
  "C:\\Program Files\\Blender Foundation\\Blender 5.0\\blender.exe" -b ArtSource/DS_Sedan_A.blend --python-exit-code 1 --python tools/build_sedan_mirrors.py

Coordinates as in build_art.py: X right, Y forward, Z up, metres. The script is idempotent and
copies the .blend and FBX to ArtSource_Backup/ first.
"""
import bpy, bmesh, sys, math, json, shutil, hashlib, datetime
from pathlib import Path
from mathutils import Vector, Matrix

sys.path.insert(0, str(Path(__file__).parent))
import build_art as a

ROOT = a.ROOT
BLEND = ROOT / 'ArtSource/DS_Sedan_A.blend'
FBX = ROOT / 'Assets/DrivingSchool/Art/DS_Sedan_A.fbx'
BACKUP = ROOT / 'ArtSource_Backup'
REPORT = ROOT / 'artifacts/reports/sedan-mirrors.json'
REPLACED = ('MirrorHousing_', 'MirrorArm_', 'MirrorSurface_', 'MirrorBase_', 'MirrorRepeater_', 'MirrorRecess_',
            'CentreMirror')


def sha(p):
    return hashlib.sha256(Path(p).read_bytes()).hexdigest() if Path(p).exists() else None


def backup():
    BACKUP.mkdir(exist_ok=True)
    stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S')
    out = {}
    for src in (BLEND, FBX):
        if src.exists():
            dst = BACKUP / f'{src.stem}_before-mirrors_{stamp}{src.suffix}'
            shutil.copy2(src, dst)
            out[src.name] = str(dst.relative_to(ROOT))
    return out


# ---------------------------------------------------------------- 2D outline

def rounded_quad(corners, radii, seg=7):
    """Convex polygon with rounded corners, counter-clockwise, as a list of (u, v)."""
    pts = []
    n = len(corners)
    for i in range(n):
        p0, p1, p2 = Vector(corners[i - 1]), Vector(corners[i]), Vector(corners[(i + 1) % n])
        d0, d1 = (p0 - p1).normalized(), (p2 - p1).normalized()
        half = math.acos(max(-1.0, min(1.0, d0.dot(d1)))) / 2
        r = radii[i]
        t = r / math.tan(half)
        c = p1 + (d0 + d1).normalized() * (r / math.sin(half))
        a0 = p1 + d0 * t - c; a1 = p1 + d1 * t - c
        ang0 = math.atan2(a0.y, a0.x); ang1 = math.atan2(a1.y, a1.x)
        da = ang1 - ang0
        while da > math.pi: da -= 2 * math.pi
        while da < -math.pi: da += 2 * math.pi
        for k in range(seg + 1):
            ang = ang0 + da * k / seg
            pts.append((c.x + r * math.cos(ang), c.y + r * math.sin(ang)))
    return pts


def centroid(pts):
    return Vector((sum(p[0] for p in pts) / len(pts), sum(p[1] for p in pts) / len(pts)))


def scaled(pts, s, c=None):
    c = c or centroid(pts)
    return [(c.x + (p[0] - c.x) * s, c.y + (p[1] - c.y) * s) for p in pts]


# ---------------------------------------------------------------- mesh helpers

def new_object(name, bm, material_names, parent, origin_world, smooth=True):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me); bm.free()
    for mn in material_names:
        me.materials.append(a.M[mn])
    if smooth:
        for p in me.polygons: p.use_smooth = True
    o = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(o)
    # Geometry was built in world space: move the origin to origin_world.
    me.transform(Matrix.Translation(-Vector(origin_world)))
    o.location = origin_world
    o.parent = parent
    o.matrix_parent_inverse = parent.matrix_world.inverted()
    if smooth:
        o.modifiers.new('Corner normals', 'WEIGHTED_NORMAL')
    return o


def ring(bm, pts2, depth, frame, mat=0):
    return [bm.verts.new(frame(u, depth, v)) for (u, v) in pts2]


def bridge(bm, r0, r1, flip=False, mat=0):
    n = len(r0); out = []
    for i in range(n):
        out.append(bm.faces.new((r0[i], r0[(i + 1) % n], r1[(i + 1) % n], r1[i])))
    return out


def cap(bm, r, centre_co, flip=False, mat=0):
    c = bm.verts.new(centre_co)
    n = len(r); out = []
    for i in range(n):
        out.append(bm.faces.new((r[i], r[(i + 1) % n], c)))
    return out


def orient(faces, ref, outward=True):
    """Winding does not survive the left/right mirroring, so every face group is turned explicitly:
    away from ref (outward) or towards it."""
    ref = Vector(ref)
    for f in faces:
        f.normal_update()
        d = (f.calc_center_median() - ref).dot(f.normal)
        if (d < 0) == outward:
            f.normal_flip()


def make_frame(anchor, side, toe_deg):
    """(u outward, depth forward, v up) -> world. side = -1 left, +1 right, 0 centre (u along +X)."""
    sx = side if side != 0 else 1
    rot = Matrix.Rotation(math.radians(toe_deg), 4, 'Z')
    A = Vector(anchor)
    def f(u, depth, v):
        return A + (rot @ Vector((sx * u, depth, v)).to_4d()).to_3d()
    return f


# ---------------------------------------------------------------- housings

def mirror_unit(root, side, name_glass, outline, depth, anchor, toe_deg, shell_mat, rim_mat, dome=1.0, repeater=False):
    """Housing shell (open at the back), rim, recess and glass. The back face (glass side) is at depth 0,
    the housing bulges forward (+depth). Returns the created objects."""
    f = make_frame(anchor, side, toe_deg)
    flip = side < 0   # mirrored geometry: keep outward normals
    c2 = centroid(outline)
    made = []

    # --- shell: rim ring at depth 0 → dome towards the front
    bm = bmesh.new()
    K = 7
    rings = []
    for k in range(K + 1):
        t = k / K
        s = max(0.18, (1 - t ** 2.2) ** 0.5)
        pts = scaled(outline, s, c2 + Vector((-0.012 * t * dome, 0.004 * t)))   # front leans towards the body a little
        rings.append(ring(bm, pts, depth * t, f))
    shell = []
    for k in range(K):
        shell += bridge(bm, rings[k], rings[k + 1])
    shell += cap(bm, rings[-1], f(c2.x - 0.012 * dome, depth * 1.02, c2.y + 0.004))
    orient(shell, f(c2.x, depth * 0.3, c2.y))
    far = f(c2.x, 5.0, c2.y)            # a point far in front: faces pointing away from it face the back
    # rim: from the outer edge inwards to the lip, then the recess wall back into the housing
    lip = ring(bm, scaled(outline, 0.935, c2), -0.003, f)
    orient(bridge(bm, rings[0], lip), far)
    wall = ring(bm, scaled(outline, 0.935, c2), 0.022, f)
    orient(bridge(bm, lip, wall), f(c2.x, 0.01, c2.y), outward=False)
    orient(cap(bm, wall, f(c2.x, 0.022, c2.y)), far)
    made.append(new_object(('MirrorHousing_' + ('1' if side > 0 else '-1')) if side != 0 else 'CentreMirrorHousing',
                           bm, [shell_mat], root, f(c2.x, depth * 0.4, c2.y)))
    # rim colour differs from the shell on door mirrors: separate thin ring object
    if rim_mat != shell_mat:
        bm = bmesh.new()
        r0 = ring(bm, scaled(outline, 1.004, c2), -0.0005, f)
        r1 = ring(bm, scaled(outline, 0.93, c2), -0.0035, f)
        orient(bridge(bm, r0, r1), f(c2.x, 5.0, c2.y))
        made.append(new_object('MirrorRecess_' + ('R' if side > 0 else 'L'), bm, [rim_mat], root, f(c2.x, 0, c2.y)))

    # --- glass: thin slab, origin at its centre, facing back (−depth)
    bm = bmesh.new()
    g = scaled(outline, 0.915, c2)
    back = ring(bm, g, 0.012, f)
    front = ring(bm, g, 0.016, f)
    gc = f(c2.x, 0.014, c2.y)
    orient(bridge(bm, back, front) + cap(bm, back, f(c2.x, 0.012, c2.y)) + cap(bm, front, f(c2.x, 0.016, c2.y)), gc)
    made.append(new_object(name_glass, bm, ['Mirror'], root, f(c2.x, 0.014, c2.y), smooth=False))

    if repeater:
        # Amber strip on the lower front edge of the shell, towards the outer end.
        bm = bmesh.new()
        us = [c2.x + 0.03, c2.x + 0.055, c2.x + 0.08]
        bottom = min(p[1] for p in outline)
        vb = c2.y + (bottom - c2.y) * 0.93         # just proud of the shell between 30 % and 50 % of the depth
        quad = [(us[0], vb - 0.003), (us[2], vb - 0.005), (us[2], vb + 0.006), (us[0], vb + 0.008)]
        ra = ring(bm, quad, depth * 0.3, f)
        rb = ring(bm, quad, depth * 0.5, f)
        rc = f(us[1], depth * 0.4, vb + 0.002)
        orient(bridge(bm, ra, rb) + cap(bm, ra, f(us[1], depth * 0.3, vb + 0.002)) + cap(bm, rb, f(us[1], depth * 0.5, vb + 0.002)), rc)
        made.append(new_object('MirrorRepeater_' + ('R' if side > 0 else 'L'), bm, ['Lamp_Amber'], root, rc))
    return made


def door_mirror(root, side):
    # Housing: inner end 4 cm off the door skin, 21 cm long, taller at the outer end (teardrop in front view).
    outline = rounded_quad([(0.0, -0.046), (0.205, -0.068), (0.212, 0.060), (0.0, 0.052)],
                           [0.022, 0.05, 0.04, 0.022])
    anchor = (side * 0.94, 0.705, 0.995)    # inner end, glass plane, housing mid height
    made = mirror_unit(root, side, 'MirrorSurface_' + ('R' if side > 0 else 'L'), outline, 0.115, anchor,
                       toe_deg=-9.0 * side, shell_mat='Paint_Atlantic', rim_mat='Rubber', repeater=True)
    # Sail: a wedge from the door top corner out to the inner end of the housing (the housing overlaps it).
    f = make_frame((side * 0.905, 0.0, 0.0), side, 0)
    bm = bmesh.new()
    #          (u out, y forward, z up)
    bottom = [(0.000, 0.72, 0.900), (0.000, 0.99, 0.900), (0.060, 0.93, 0.945), (0.060, 0.735, 0.945)]
    top = [(0.004, 0.735, 0.975), (0.004, 0.90, 0.955), (0.060, 0.84, 1.000), (0.060, 0.745, 1.010)]
    rb = [bm.verts.new(f(u, y, z)) for (u, y, z) in bottom]
    rt = [bm.verts.new(f(u, y, z)) for (u, y, z) in top]
    faces = bridge(bm, rb, rt)
    faces.append(bm.faces.new(rt))
    orient(faces, f(0.03, 0.82, 0.945))
    made.append(new_object('MirrorBase_' + ('1' if side > 0 else '-1'), bm, ['Paint_Atlantic'], root, f(0.03, 0.82, 0.945)))
    return made


def centre_mirror(root):
    outline = rounded_quad([(-0.125, -0.036), (0.125, -0.036), (0.125, 0.036), (-0.125, 0.036)],
                           [0.03, 0.03, 0.03, 0.03])
    # Turned ~12° towards the driver (LHD) like a set mirror; the glass faces −Y.
    made = mirror_unit(root, 0, 'MirrorSurface_Centre', outline, 0.035, (0.0, 0.378, 1.31), toe_deg=-12.0,
                       shell_mat='Interior_Graphite', rim_mat='Interior_Graphite', dome=0.0)
    bm = bmesh.new()
    sec = [(-0.009, -0.009), (0.009, -0.009), (0.009, 0.009), (-0.009, 0.009)]
    p0 = Vector((0.0, 0.52, 1.395)); p1 = Vector((0.0, 0.41, 1.325))
    d = (p1 - p0).normalized(); r = Vector((1, 0, 0)); up = r.cross(d).normalized()
    r0 = [bm.verts.new(p0 + r * u + up * v) for (u, v) in sec]
    r1 = [bm.verts.new(p1 + r * u + up * v) for (u, v) in sec]
    orient(bridge(bm, r0, r1), tuple((p0 + p1) / 2))
    made.append(new_object('CentreMirrorArm', bm, ['Interior_Graphite'], root, tuple((p0 + p1) / 2)))
    return made


def main():
    if bpy.data.filepath and Path(bpy.data.filepath).resolve() != BLEND.resolve():
        raise SystemExit('Open ArtSource/DS_Sedan_A.blend, not ' + bpy.data.filepath)
    a.M = {m.name: m for m in bpy.data.materials}
    root = bpy.data.objects['DS_Sedan_A']
    report = {'script': 'tools/build_sedan_mirrors.py', 'utc': datetime.datetime.utcnow().isoformat() + 'Z',
              'before': {'blend_sha256': sha(BLEND), 'fbx_sha256': sha(FBX)}, 'backup': backup()}
    removed = []
    for o in list(root.children_recursive):
        if o.name.startswith(REPLACED) and o.name in bpy.data.objects:
            removed.append(o.name)
            bpy.data.objects.remove(o, do_unlink=True)
    report['removed'] = sorted(removed)
    made = []
    for side in (-1, 1):
        made += [o.name for o in door_mirror(root, side)]
    made += [o.name for o in centre_mirror(root)]
    report['created'] = sorted(made)
    for n in ('MirrorSurface_L', 'MirrorSurface_R', 'MirrorSurface_Centre'):
        if n not in bpy.data.objects:
            raise SystemExit('Contract object missing: ' + n)
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND))
    a.export('DS_Sedan_A', root)
    report['after'] = {'blend_sha256': sha(BLEND), 'fbx_sha256': sha(FBX)}
    REPORT.parent.mkdir(parents=True, exist_ok=True)
    REPORT.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding='utf-8')
    print('SEDAN_MIRRORS_COMPLETE', json.dumps(report['created']), flush=True)


main()
