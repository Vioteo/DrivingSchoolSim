"""Environment kit (EN_) core: mesh builder in the author frame, objects, LOD, materials.

Author frame = Unity axes in right-handed form: X right, Y forward, Z up.
Blender frame (art-pipeline.md section 3): front faces -Y, so every vertex is
turned 180 deg around Z on output: (x, y, z) -> (-x, -y, z). This is a rotation,
winding and handedness are preserved. After FBX export (-Z forward, Y up) the
author +Y becomes Unity +Z and author +X becomes Unity +X.
"""
import bpy, bmesh, math, random
from mathutils import Vector, Matrix, noise

from .materials import PALETTE, GROUND


SUFFIX_RE = __import__('re').compile(r'\.\d{3}$')


def to_blender(p):
    return (-p[0], -p[1], p[2])


# ---------------------------------------------------------------- materials
def make_materials():
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


# ---------------------------------------------------------------- mesh builder
class MB:
    """Collects polygons with a material per face, in the author frame."""

    def __init__(self):
        self.v, self.f, self.fm, self.fs = [], [], [], []
        self.mats = []

    def _mi(self, mat):
        if mat not in PALETTE:
            raise KeyError(f'material {mat} is not in the EN palette')
        if mat not in self.mats:
            self.mats.append(mat)
        return self.mats.index(mat)

    def add(self, verts, faces, mat, smooth=False, face_mats=None):
        o = len(self.v)
        self.v += [tuple(p) for p in verts]
        for i, fc in enumerate(faces):
            self.f.append(tuple(j + o for j in fc))
            self.fm.append(self._mi(face_mats[i] if face_mats else mat))
            self.fs.append(smooth)
        return o

    def extend(self, other, xf=None):
        """Append another builder, optionally transformed by a 4x4 matrix."""
        o = len(self.v)
        for p in other.v:
            self.v.append(tuple(xf @ Vector(p)) if xf else p)
        for fc, mi, s in zip(other.f, other.fm, other.fs):
            self.f.append(tuple(j + o for j in fc))
            self.fm.append(self._mi(other.mats[mi]))
            self.fs.append(s)

    @property
    def empty(self):
        return not self.f

    # ---- primitives
    def box(self, c, s, mat, smooth=False):
        cx, cy, cz = c
        hx, hy, hz = s[0] / 2, s[1] / 2, s[2] / 2
        v = [(cx + sx * hx, cy + sy * hy, cz + sz * hz) for sz in (-1, 1) for sy in (-1, 1) for sx in (-1, 1)]
        f = [(0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)]
        return self.add(v, f, mat, smooth)

    def box_between(self, x0, x1, y0, y1, z0, z1, mat):
        return self.box(((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2),
                        (abs(x1 - x0), abs(y1 - y0), abs(z1 - z0)), mat)

    def obox(self, c, size, yaw, mat, pitch=0.0, roll=0.0):
        """Oriented box: rotated by roll (Y), pitch (X), then yaw (Z) around its centre."""
        tmp = MB()
        tmp.box((0, 0, 0), size, mat)
        xf = Matrix.Translation(Vector(c)) @ Matrix.Rotation(yaw, 4, 'Z') @ Matrix.Rotation(pitch, 4, 'X') \
            @ Matrix.Rotation(roll, 4, 'Y')
        self.extend(tmp, xf)

    def beam(self, a, b, w, h, mat, up=(0, 0, 1)):
        """Box beam from point a to point b with cross-section w x h."""
        a, b = Vector(a), Vector(b)
        t = (b - a)
        L = t.length
        t.normalize()
        u = Vector(up)
        if abs(t.dot(u)) > 0.98:
            u = Vector((1, 0, 0))
        side = t.cross(u).normalized()
        upv = side.cross(t).normalized()
        hw, hh = w / 2, h / 2
        ring0 = [a + side * sx * hw + upv * sz * hh for sx, sz in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
        ring1 = [p + t * L for p in ring0]
        v = ring0 + ring1
        f = [(3, 2, 1, 0), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
        self.add(v, f, mat)

    def cyl(self, p0, p1, r0, r1=None, seg=12, mat='EN_Steel', cap0=True, cap1=True, smooth=True):
        r1 = r0 if r1 is None else r1
        return self.tube([p0, p1], [r0, r1], seg, mat, cap0=cap0, cap1=cap1, smooth=smooth)

    def tube(self, pts, radii, seg, mat, cap0=False, cap1=True, smooth=True, face_mat=None, phase=0.0):
        """Swept circle along a polyline with parallel-transport frames.
        face_mat(i, k) -> material name for the quad between ring i and i+1, sector k."""
        P = [Vector(p) for p in pts]
        n = len(P)
        T = []
        for i in range(n):
            if i == 0:
                t = P[1] - P[0]
            elif i == n - 1:
                t = P[-1] - P[-2]
            else:
                t = (P[i + 1] - P[i]).normalized() + (P[i] - P[i - 1]).normalized()
            T.append(t.normalized())
        a = Vector((1, 0, 0)) if abs(T[0].x) < 0.9 else Vector((0, 1, 0))
        N = T[0].cross(a).normalized()
        verts = []
        for i in range(n):
            if i > 0:
                b = T[i - 1].cross(T[i])
                if b.length > 1e-9:
                    ang = T[i - 1].angle(T[i])
                    N = (Matrix.Rotation(ang, 3, b.normalized()) @ N).normalized()
            B = T[i].cross(N)
            for k in range(seg):
                a_ = 2 * math.pi * k / seg + phase
                verts.append(P[i] + radii[i] * (math.cos(a_) * N + math.sin(a_) * B))
        faces, fms = [], []
        for i in range(n - 1):
            for k in range(seg):
                k1 = (k + 1) % seg
                faces.append((i * seg + k, i * seg + k1, (i + 1) * seg + k1, (i + 1) * seg + k))
                fms.append(face_mat(i, k) if face_mat else mat)
        if cap0:
            faces.append(tuple(reversed(range(seg))))
            fms.append(mat)
        if cap1:
            faces.append(tuple((n - 1) * seg + k for k in range(seg)))
            fms.append(mat)
        return self.add(verts, faces, mat, smooth, face_mats=fms)

    def ico(self, c, r, subdiv, mat, scale=(1, 1, 1), jitter=0.0, seed=0, smooth=True, nscale=1.0, flat_bottom=None):
        bm = bmesh.new()
        bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
        off = Vector((seed * 7.31, seed * 3.17, seed * 5.53))
        verts = []
        for v in bm.verts:
            d = v.co.normalized()
            k = 1.0 + jitter * noise.noise(d * 1.7 * nscale + off) + 0.5 * jitter * noise.noise(d * 4.1 * nscale - off)
            p = Vector((d.x * scale[0], d.y * scale[1], d.z * scale[2])) * r * k
            if flat_bottom is not None and p.z < -flat_bottom * r * scale[2]:
                p.z = -flat_bottom * r * scale[2] + (p.z + flat_bottom * r * scale[2]) * 0.25
            verts.append(Vector(c) + p)
        faces = [tuple(v.index for v in f.verts) for f in bm.faces]
        bm.free()
        return self.add(verts, faces, mat, smooth)

    def sweep(self, frames, profile, mats, closed=False, smooth=False):
        """frames: [(pos, right, up)], profile: [(lat, h)], mats: one name or a list, mats[j] between profile points j, j+1.
        Ordered left -> right, the faces of an open profile face 'up' (right x forward)."""
        np_ = len(profile)
        verts = []
        for pos, right, up in frames:
            pos, right, up = Vector(pos), Vector(right), Vector(up)
            for lat, h in profile:
                verts.append(pos + right * lat + up * h)
        faces, fms = [], []
        segs = np_ if closed else np_ - 1
        for i in range(len(frames) - 1):
            for j in range(segs):
                j1 = (j + 1) % np_
                faces.append((i * np_ + j, i * np_ + j1, (i + 1) * np_ + j1, (i + 1) * np_ + j))
                fms.append(mats[j] if isinstance(mats, (list, tuple)) else mats)
        return self.add(verts, faces, fms[0], smooth, face_mats=fms)

    def ngon(self, pts, mat, smooth=False):
        return self.add(pts, [tuple(range(len(pts)))], mat, smooth)

    def plate(self, poly, origin, u, v, t, mat, side_mat=None, back_mat=None):
        """Flat plate: 2D polygon (CCW seen from the front) in plane origin + a*u + b*v,
        front normal = u x v... the plate extends t backwards (against the normal)."""
        O, U, V = Vector(origin), Vector(u), Vector(v)
        Nn = U.cross(V).normalized()
        n = len(poly)
        front = [O + U * a + V * b for a, b in poly]
        back = [p - Nn * t for p in front]
        verts = front + back
        faces = [tuple(range(n)), tuple(reversed(range(n, 2 * n)))]
        fms = [mat, back_mat or side_mat or mat]
        for i in range(n):
            j = (i + 1) % n
            faces.append((j, i, n + i, n + j))
            fms.append(side_mat or mat)
        return self.add(verts, faces, mat, False, face_mats=fms)

    def lathe(self, prof, base, seg, mat, smooth=True, cap_top=True):
        """Profile [(r, z)] revolved around the vertical axis through base."""
        bx, by, bz = base
        verts = []
        for r, z in prof:
            for k in range(seg):
                a = 2 * math.pi * k / seg
                verts.append((bx + r * math.cos(a), by + r * math.sin(a), bz + z))
        faces = []
        for i in range(len(prof) - 1):
            for k in range(seg):
                k1 = (k + 1) % seg
                faces.append((i * seg + k, i * seg + k1, (i + 1) * seg + k1, (i + 1) * seg + k))
        if cap_top and prof[-1][0] > 1e-4:
            i = len(prof) - 1
            faces.append(tuple(i * seg + k for k in range(seg)))
        return self.add(verts, faces, mat, smooth)

    def prism(self, poly, z0, z1, mat, smooth=False):
        """Vertical prism from a simple 2D polygon (any winding)."""
        poly = ccw(poly)
        n = len(poly)
        v = [(x, y, z0) for x, y in poly] + [(x, y, z1) for x, y in poly]
        f = [tuple(reversed(range(n))), tuple(range(n, 2 * n))]
        f += [(i, (i + 1) % n, n + (i + 1) % n, n + i) for i in range(n)]
        return self.add(v, f, mat, smooth)

    def transform(self, xf, start=0):
        for i in range(start, len(self.v)):
            self.v[i] = tuple(xf @ Vector(self.v[i]))


def area2(poly):
    return sum(poly[i][0] * poly[(i + 1) % len(poly)][1] - poly[(i + 1) % len(poly)][0] * poly[i][1]
               for i in range(len(poly))) / 2


def ccw(poly):
    return list(poly) if area2(poly) > 0 else list(reversed(poly))


def cw(poly):
    return list(reversed(ccw(poly)))


def stroke(points, w, cap_round=0):
    """Outline polygon of a polyline of width w (mitred joints), CCW. 2D."""
    P = [Vector((p[0], p[1])) for p in points]
    n = len(P)
    left, right = [], []
    for i in range(n):
        if i == 0:
            d = (P[1] - P[0]).normalized()
            nrm = Vector((-d.y, d.x))
            k = 1.0
        elif i == n - 1:
            d = (P[-1] - P[-2]).normalized()
            nrm = Vector((-d.y, d.x))
            k = 1.0
        else:
            d0 = (P[i] - P[i - 1]).normalized()
            d1 = (P[i + 1] - P[i]).normalized()
            n0, n1 = Vector((-d0.y, d0.x)), Vector((-d1.y, d1.x))
            nrm = (n0 + n1).normalized()
            k = 1.0 / max(0.3, nrm.dot(n0))
        left.append(P[i] + nrm * w / 2 * k)
        right.append(P[i] - nrm * w / 2 * k)
    return ccw([tuple(p) for p in right + list(reversed(left))])


def arc_pts(cx, cy, r, a0, a1, n):
    return [(cx + r * math.cos(a0 + (a1 - a0) * i / n), cy + r * math.sin(a0 + (a1 - a0) * i / n)) for i in range(n + 1)]


def text_mb(body, size, mat, extrude=0.0015):
    """Text as mesh in the XZ plane facing +Y (the model front, art-pipeline.md), centred at origin."""
    c = bpy.data.curves.new('tmp_text', 'FONT')
    c.body, c.size, c.extrude = body, size, extrude
    c.align_x, c.align_y = 'CENTER', 'CENTER'
    o = bpy.data.objects.new('tmp_text', c)
    bpy.context.scene.collection.objects.link(o)
    deps = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(o.evaluated_get(deps))
    bpy.data.objects.remove(o, do_unlink=True)
    bpy.data.curves.remove(c)
    rx = Matrix.Rotation(math.pi, 4, 'Z') @ Matrix.Rotation(math.pi / 2, 4, 'X')   # normal +Z -> +Y, up -> +Z
    mb = MB()
    mb.add([tuple(rx @ v.co) for v in me.vertices], [tuple(p.vertices) for p in me.polygons], mat)
    bpy.data.meshes.remove(me)
    return mb


# ---------------------------------------------------------------- objects
def planar_uv(me, repeat_for):
    uv = me.uv_layers.new(name='UVMap')
    mats = [m.name if m else '' for m in me.materials]
    for poly in me.polygons:
        n = poly.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        rep = repeat_for(mats[poly.material_index] if poly.material_index < len(mats) else '')
        for li in poly.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            a, b = [(co.y, co.z), (co.x, co.z), (co.x, co.y)][ax]
            uv.data[li].uv = (a / rep, b / rep)


def mesh_object(name, mb, M, parent=None, sharp_deg=40, coll=None, weld=True):
    me = bpy.data.meshes.new(name)
    me.from_pydata([to_blender(p) for p in mb.v], [], mb.f)
    for m in mb.mats:
        me.materials.append(M[m])
    for poly, mi, s in zip(me.polygons, mb.fm, mb.fs):
        poly.material_index = mi
        poly.use_smooth = s
    me.validate(clean_customdata=False)
    bm = bmesh.new()
    bm.from_mesh(me)
    if weld:
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.dissolve_degenerate(bm, edges=bm.edges, dist=1e-7)
    ngons = [f for f in bm.faces if len(f.verts) > 4]
    if ngons:
        bmesh.ops.triangulate(bm, faces=ngons)
    bm.to_mesh(me)
    bm.free()
    if any(mb.fs):
        me.set_sharp_from_angle(angle=math.radians(sharp_deg))
    planar_uv(me, lambda m: 2.0 if m in GROUND else 1.0)
    o = bpy.data.objects.new(name, me)
    (coll or bpy.context.scene.collection).objects.link(o)
    if parent:
        o.parent = parent
    if name.startswith('COL_'):
        o.hide_render = True
        o.display_type = 'WIRE'
        for i in range(len(me.materials)):
            me.materials[i] = M['EN_Collision']
    return o


def empty_object(name, loc=(0, 0, 0), parent=None, coll=None, yaw=0.0, size=0.15):
    """Empty at an author-frame location; yaw is the author-frame heading change (same sign in Blender)."""
    o = bpy.data.objects.new(name, None)
    o.empty_display_size = size
    (coll or bpy.context.scene.collection).objects.link(o)
    o.location = to_blender(loc)
    o.rotation_euler = (0, 0, yaw)
    if parent:
        o.parent = parent
    return o


class Asset:
    """One exported asset: root empty (the asset id) + children, in its own collection."""

    def __init__(self, aid, title, kind, M, **meta):
        self.id, self.title, self.kind, self.M = aid, title, kind, M
        self.meta = dict(meta)
        self.coll = bpy.data.collections.new(aid)
        bpy.context.scene.collection.children.link(self.coll)
        self.root = empty_object(aid, coll=self.coll, size=0.5)
        self.root['catalogId'] = aid
        self.root['label'] = title
        self.root['generator'] = 'tools/env_kit'
        # axis markers 1 m ahead and above the origin: the Unity builder checks forward +Z / up +Y after import
        empty_object('Socket_Front', (0, 1, 0), parent=self.root, coll=self.coll, size=0.1)
        empty_object('Socket_Up', (0, 0, 1), parent=self.root, coll=self.coll, size=0.1)

    def mesh(self, name, mb, parent=None, **kw):
        if mb.empty:
            return None
        return mesh_object(name, mb, self.M, parent=parent or self.root, coll=self.coll, **kw)

    def socket(self, name, loc, yaw=0.0):
        return empty_object(name, loc, parent=self.root, coll=self.coll, yaw=yaw, size=0.25)

    def lods(self, builders, **kw):
        """builders: [MB for LOD0, LOD1, LOD2]; LOD1+ are hidden in renders."""
        out = []
        for i, mb in enumerate(builders):
            o = self.mesh(f'{self.id}_LOD{i}', mb, **kw)
            if i > 0:
                o.hide_render = True
            out.append(o)
        self.meta['lods'] = len(builders)
        return out

    def objects(self):
        return [self.root] + list(self.root.children_recursive)


def rng(seed):
    return random.Random(seed)


# ---------------------------------------------------------------- organic masses (foliage)
def organic(mb, voxel, target_tris, displace=0.0, noise_size=0.6, mats=None, seed=0, up_bias=0.3, smooth=True):
    """Merges overlapping blobs into one organic surface: voxel remesh -> cloud displacement -> decimate.
    mats = (shade, light): faces looking up (normal.z > up_bias + noise) get the light material.
    Works in the author frame and returns a new MB."""
    me = bpy.data.meshes.new('tmp_organic')
    me.from_pydata(mb.v, [], mb.f)
    me.validate()
    o = bpy.data.objects.new('tmp_organic', me)
    bpy.context.scene.collection.objects.link(o)
    rm = o.modifiers.new('remesh', 'REMESH')
    rm.mode, rm.voxel_size, rm.adaptivity = 'VOXEL', voxel, 0.0
    rm.use_smooth_shade = True
    tex = None
    if displace:
        tex = bpy.data.textures.new('tmp_clouds', 'CLOUDS')
        tex.noise_scale, tex.noise_depth = noise_size, 2
        tex.noise_basis = 'ORIGINAL_PERLIN'
        dm = o.modifiers.new('displace', 'DISPLACE')
        dm.texture, dm.strength, dm.mid_level = tex, displace, 0.5
        dm.texture_coords = 'GLOBAL'
    dec = o.modifiers.new('decimate', 'DECIMATE')
    dec.ratio = 1.0
    deps = bpy.context.evaluated_depsgraph_get()
    ev = o.evaluated_get(deps)
    m2 = ev.to_mesh()
    m2.calc_loop_triangles()
    n = len(m2.loop_triangles)
    ev.to_mesh_clear()
    dec.ratio = min(1.0, target_tris / max(1, n))
    deps = bpy.context.evaluated_depsgraph_get()
    ev = o.evaluated_get(deps)
    m3 = bpy.data.meshes.new_from_object(ev)
    out = MB()
    shade, light = mats if mats else ('EN_Collision', 'EN_Collision')
    off = Vector((seed * 1.37, seed * 0.71, 0))
    verts = [tuple(v.co) for v in m3.vertices]
    faces, fms = [], []
    for p in m3.polygons:
        faces.append(tuple(p.vertices))
        c = p.center
        k = up_bias + 0.35 * noise.noise(c * 0.45 + off)
        fms.append(light if p.normal.z > k else shade)
    out.add(verts, faces, shade, smooth, face_mats=fms)
    bpy.data.objects.remove(o, do_unlink=True)
    bpy.data.meshes.remove(me)
    bpy.data.meshes.remove(m3)
    if tex:
        bpy.data.textures.remove(tex)
    return out
