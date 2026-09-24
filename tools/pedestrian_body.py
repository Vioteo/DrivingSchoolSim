"""Procedural pedestrian bodies for tools/build_pedestrians.py (Blender 5, bpy). Metres, Z up, face -Y.

Proportions follow adult/child anthropometry (fractions of stature). The clothed body, head, hands,
shoes and hair volumes are metaball families: elements along the skeleton blend into one smooth
surface, and the same field gives the skinning weights and the material of every face.
Separate families (torso, each arm, each leg, head, ...) do not blend with each other, so limbs
never web together; each limb starts inside the torso and the seam is hidden under clothing.
"""
import bpy, math
import numpy as np
from mathutils import Vector, Quaternion, Matrix

TAU = math.tau
STIFF, THRESH = 2.0, .6
LONE = math.sqrt(1 - (THRESH/STIFF)**(1/3))     # surface radius of a lone element / element radius
TUBE_STEP = .5                                  # element spacing along a tube, in local radii
ARM_OUT = math.radians(10)                      # rest pose: upper arm away from the body

def _tube_radius():
    # Element radius (in local radii) at which a chain spaced TUBE_STEP apart has surface radius 1.
    def surface(r):
        lo, hi = 0., r
        for _ in range(40):
            x = (lo+hi)/2
            f = sum(STIFF*max(0., 1-(x*x+(TUBE_STEP*k)**2)/(r*r))**3 for k in range(-12, 13))
            lo, hi = (x, hi) if f > THRESH else (lo, x)
        return x
    lo, hi = .5, 3.
    for _ in range(40):
        r = (lo+hi)/2
        lo, hi = (lo, r) if surface(r) > 1 else (r, hi)
    return r
TUBE_R = _tube_radius()

# Landmarks as fractions of stature. Sources of the ratios: common anthropometric proportions
# (head ~1/7.5 of an adult's height, ~1/6 at 8 years); stylisation only, not a norm.
PROPORTIONS = {
    'male':   dict(ankle=.045, knee=.285, hip=.52, hipX=.05, spine=.60, chest=.70, neck=.815, eye=.936, headH=.13,
                   shoulderZ=.81, shoulderX=.101, upperArm=.186, forearm=.146, hand=.106, foot=.152, hem=.49, neckR=.030),
    'female': dict(ankle=.045, knee=.285, hip=.52, hipX=.053, spine=.605, chest=.70, neck=.818, eye=.935, headH=.13,
                   shoulderZ=.812, shoulderX=.094, upperArm=.183, forearm=.143, hand=.104, foot=.148, hem=.50, neckR=.027),
    'child':  dict(ankle=.048, knee=.27, hip=.495, hipX=.055, spine=.585, chest=.675, neck=.79, eye=.917, headH=.165,
                   shoulderZ=.785, shoulderX=.095, upperArm=.178, forearm=.138, hand=.10, foot=.152, hem=.48, neckR=.031),
}
# Torso sections over clothing: (height, half width, half depth, centre y), fractions of stature.
TORSO = {
    'male':   [(.46,.055,.05,.004),(.50,.086,.062,.006),(.535,.095,.066,.008),(.575,.09,.06,.003),(.61,.085,.057,-.001),
               (.65,.087,.058,-.004),(.69,.093,.062,-.006),(.73,.097,.064,-.006),(.765,.097,.06,-.002),(.795,.085,.052,.004)],
    'female': [(.46,.058,.052,.004),(.50,.094,.066,.008),(.535,.103,.07,.01),(.575,.091,.06,.004),(.61,.077,.052,0),
               (.65,.08,.053,-.002),(.69,.086,.057,-.004),(.73,.089,.058,-.004),(.765,.09,.056,-.001),(.795,.08,.05,.004)],
    'child':  [(.44,.06,.055,.004),(.48,.09,.068,.006),(.52,.095,.07,.006),(.56,.093,.07,0),(.60,.093,.071,-.006),
               (.64,.094,.068,-.006),(.68,.096,.066,-.004),(.72,.095,.062,-.001),(.76,.082,.054,.004)],
}

# ---------------------------------------------------------------- materials and simple meshes
class Kit:
    """Materials and the list of mesh parts of one character."""
    def __init__(self):
        self.materials = {}
        self.parts = []

    def material(self, name, color, roughness=.78):
        m = bpy.data.materials.new('Ped_' + name)
        m.diffuse_color = (*color, 1)
        m.use_nodes = True
        shader = m.node_tree.nodes.get('Principled BSDF')
        shader.inputs['Base Color'].default_value = (*color, 1)
        shader.inputs['Roughness'].default_value = roughness
        self.materials[name] = m
        return m

    def slot(self, o, name):
        m = self.materials[name]
        if m.name not in o.data.materials: o.data.materials.append(m)
        return list(o.data.materials).index(m)

def link(name, mesh):
    o = bpy.data.objects.new(name, mesh); bpy.context.collection.objects.link(o)
    return o

def activate(o):
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True); bpy.context.view_layer.objects.active = o

def apply_all(o):
    activate(o)
    for m in list(o.modifiers): bpy.ops.object.modifier_apply(modifier=m.name)

def finish(kit, o, mat, bone=None):
    """Applies transforms, sets one material and binds the whole part rigidly to one bone."""
    activate(o)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    o.data.materials.clear(); kit.slot(o, mat)
    for f in o.data.polygons: f.material_index = 0
    if bone:
        o.vertex_groups.new(name=bone).add(list(range(len(o.data.vertices))), 1, 'REPLACE')
    for f in o.data.polygons: f.use_smooth = True
    kit.parts.append(o)
    return o

def ellipsoid(kit, name, loc, semi, mat, bone, rot=(0,0,0), segments=16, rings=10):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, location=loc, rotation=rot)
    o = bpy.context.object; o.name = name; o.scale = semi
    return finish(kit, o, mat, bone)

def box(kit, name, loc, size, mat, bone, bevel=.01, rot=(0,0,0), cuts=0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=rot)
    o = bpy.context.object; o.name = name; o.scale = size
    activate(o); bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if cuts:
        bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.mesh.subdivide(number_cuts=cuts); bpy.ops.object.mode_set(mode='OBJECT')
    if bevel:
        m = o.modifiers.new('Soft edges', 'BEVEL'); m.width = bevel; m.segments = 2
        apply_all(o)
    return finish(kit, o, mat, bone)

def cylinder(kit, name, a, b, radius, mat, bone, sides=10):
    a, b = Vector(a), Vector(b)
    bpy.ops.mesh.primitive_cylinder_add(vertices=sides, radius=radius, depth=(b-a).length, location=(a+b)/2)
    o = bpy.context.object; o.name = name
    o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = Vector((0,0,1)).rotation_difference(b-a)
    return finish(kit, o, mat, bone)

def ring(kit, name, rows, mat, bone, sides=20, closed=False):
    """Loft of elliptical rows (x, y, z, half width, half depth); open ends unless closed."""
    vs = [(x + rx*math.cos(i*TAU/sides), y + ry*math.sin(i*TAU/sides), z)
          for x,y,z,rx,ry in rows for i in range(sides)]
    fs = []
    for j in range(len(rows)-1):
        for i in range(sides):
            a = j*sides+i; b = j*sides+(i+1)%sides
            fs.append((a, b, b+sides, a+sides))
    if closed:
        fs.append(tuple(reversed(range(sides))))
        fs.append(tuple(range((len(rows)-1)*sides, len(rows)*sides)))
    me = bpy.data.meshes.new(name); me.from_pydata(vs, [], fs); me.update()
    return finish(kit, link(name, me), mat, bone)

def ribbon(kit, name, points, width, thickness, mat, across=Vector((1,0,0))):
    """Thin strip through points (for details that are wrapped onto a surface afterwards)."""
    vs = []
    for p in points:
        vs += [p - across*width/2, p + across*width/2]
    fs = [(2*i, 2*i+1, 2*i+3, 2*i+2) for i in range(len(points)-1)]
    me = bpy.data.meshes.new(name); me.from_pydata(vs, [], fs); me.update()
    o = link(name, me); o['thickness'] = thickness
    return finish(kit, o, mat)

def label(kit, name, text, loc, size, mat):
    bpy.ops.object.text_add(location=loc)
    o = bpy.context.object
    o.data.body = text; o.data.size = size; o.data.resolution_u = 3
    o.data.align_x = 'CENTER'; o.data.align_y = 'CENTER'
    o.rotation_euler = (math.pi/2, 0, math.pi)      # readable from behind (+Y)
    bpy.ops.object.convert(target='MESH')
    o = bpy.context.object; o.name = name
    return finish(kit, o, mat)

def wrap(o, target, offset):
    """Lays a detail onto a surface and copies that surface's skin weights."""
    thickness = o.get('thickness', 0)
    m = o.modifiers.new('Wrap', 'SHRINKWRAP'); m.target = target
    m.wrap_method = 'NEAREST_SURFACEPOINT'; m.wrap_mode = 'OUTSIDE_SURFACE'; m.offset = offset + thickness/2
    if thickness:
        m = o.modifiers.new('Thickness', 'SOLIDIFY'); m.thickness = thickness; m.offset = 0
    apply_all(o)
    copy_weights(o, target)
    return o

def copy_weights(o, source):
    o.vertex_groups.clear()
    m = o.modifiers.new('Weights', 'DATA_TRANSFER'); m.object = source
    m.use_vert_data = True; m.data_types_verts = {'VGROUP_WEIGHTS'}; m.vert_mapping = 'POLYINTERP_NEAREST'
    m.layers_vgroup_select_src = 'ALL'; m.layers_vgroup_select_dst = 'NAME'
    activate(o); bpy.ops.object.datalayout_transfer(modifier=m.name)
    bpy.ops.object.modifier_apply(modifier=m.name)

# ---------------------------------------------------------------- skeleton
def skeleton(spec):
    H = spec['height']; P = PROPORTIONS[spec['build']]
    g = lambda k: P[k]*H
    hh = g('headH'); eye = g('eye')
    J = {'H': H, 'hh': hh, 'eye': eye, 'P': P}
    J['Root'] = (Vector((0,0,0)), Vector((0,0,.08*H)))
    J['Hips'] = (Vector((0,0,g('hip'))), Vector((0,0,g('spine'))))
    J['Spine'] = (Vector((0,0,g('spine'))), Vector((0,0,g('chest'))))
    J['Chest'] = (Vector((0,0,g('chest'))), Vector((0,.004*H,g('neck'))))
    pivot = Vector((0, .004*H, eye-.28*hh))
    J['Neck'] = (Vector((0,.004*H,g('neck'))), pivot)
    J['Head'] = (pivot, Vector((0,.004*H,H)))
    for side, s in (('L',1), ('R',-1)):
        hip = Vector((s*g('hipX'), 0, g('hip')))
        knee = Vector((s*g('hipX')*.97, -.004*H, g('knee')))
        ankle = Vector((s*g('hipX')*.95, 0, g('ankle')))
        ball = Vector((ankle.x, -g('foot')*.62, g('ankle')*.35))
        J['UpperLeg_'+side] = (hip, knee); J['LowerLeg_'+side] = (knee, ankle); J['Foot_'+side] = (ankle, ball)
        shoulder = Vector((s*g('shoulderX'), .002*H, g('shoulderZ')))
        clavicle = Vector((s*.012*H, -.004*H, g('shoulderZ')+.006*H))
        up = Vector((s*math.sin(ARM_OUT), .01, -math.cos(ARM_OUT))).normalized()
        elbow = shoulder + up*g('upperArm')
        fore = Vector((s*math.sin(ARM_OUT+math.radians(3)), -.10, -math.cos(ARM_OUT+math.radians(3)))).normalized()
        wrist = elbow + fore*g('forearm')
        J['Shoulder_'+side] = (clavicle, shoulder); J['UpperArm_'+side] = (shoulder, elbow)
        J['Forearm_'+side] = (elbow, wrist); J['Hand_'+side] = (wrist, wrist + fore*g('hand')*.55)
    return J

BONES = [('Root',None),('Hips','Root'),('Spine','Hips'),('Chest','Spine'),('Neck','Chest'),('Head','Neck')] + \
    [(b+'_'+s, p+'_'+s if p not in ('Hips','Chest') else p) for s in 'LR' for b,p in
     (('UpperLeg','Hips'),('LowerLeg','UpperLeg'),('Foot','LowerLeg'),
      ('Shoulder','Chest'),('UpperArm','Shoulder'),('Forearm','UpperArm'),('Hand','Forearm'))]

def rig_create(J, name):
    data = bpy.data.armatures.new('PedestrianSkeleton')
    rig = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(rig)
    activate(rig); bpy.ops.object.mode_set(mode='EDIT')
    for n, parent in BONES:
        b = data.edit_bones.new(n); b.head, b.tail = J[n]
        if parent: b.parent = data.edit_bones[parent]
    bpy.ops.object.mode_set(mode='OBJECT')
    rig.select_set(False)
    return rig

# ---------------------------------------------------------------- metaball sculpting
class Blob:
    __slots__ = ('fam','co','rot','size','radius','neg','bone','mat')

class Sculpt:
    def __init__(self):
        self.blobs = []

    def ell(self, fam, co, semi, bone, mat, rot=None, neg=False, radius=None):
        """Element whose surface, alone, is the ellipsoid with these semi-axes."""
        b = Blob(); b.fam = fam; b.co = Vector(co); b.rot = rot or Quaternion()
        b.size = Vector(semi); b.radius = radius or 1/LONE; b.neg = neg; b.bone = bone; b.mat = mat
        self.blobs.append(b)

    def ball(self, fam, co, r, bone, mat, neg=False):
        self.ell(fam, co, (r, r, r), bone, mat, neg=neg)

    def tube(self, fam, pts, bone, mat):
        """Smooth tube through (point, half width, half depth); bone/mat are names or f(point, t)."""
        pts = [(Vector(p), rx, ry) for p, rx, ry in pts]
        lengths = [0.]
        for a, b in zip(pts, pts[1:]): lengths.append(lengths[-1] + (b[0]-a[0]).length)
        total = lengths[-1]; s = 0.
        while s <= total + 1e-9:
            i = max(j for j in range(len(pts)-1) if lengths[j] <= s + 1e-9) if len(pts) > 1 else 0
            i = min(i, len(pts)-2)
            u = (s-lengths[i]) / max(1e-9, lengths[i+1]-lengths[i])
            (p0, x0, y0), (p1, x1, y1) = pts[i], pts[i+1]
            p = p0.lerp(p1, u); rx = x0 + (x1-x0)*u; ry = y0 + (y1-y0)*u; rz = (rx+ry)/2
            t = s/total
            self.ell(fam, p, (rx, ry, rz), bone(p, t) if callable(bone) else bone,
                     mat(p, t) if callable(mat) else mat, rot=frame(p1-p0), radius=TUBE_R)
            s += TUBE_STEP*rz

    def field(self, fam, pts):
        """(blob, field) for every element of the family at points (N, 3)."""
        out = []
        for b in self.blobs:
            if b.fam != fam: continue
            R = np.array(b.rot.to_matrix())
            local = (pts - np.array(b.co)) @ R / np.array(b.size)
            t = np.clip(1 - (local*local).sum(1)/(b.radius*b.radius), 0, None)
            out.append((b, STIFF*t**3))
        return out

    def mesh(self, kit, fam, resolution, tris, symmetric=False, material=None, cuts=(), fine=False):
        """Converts one family to a decimated mesh with skin weights and per-face materials.
        cuts: planes (point, normal) split first, so material borders there are straight lines.
        fine: also return the undecimated mesh data (for shells such as hair)."""
        mb = bpy.data.metaballs.new(fam); mb.resolution = mb.render_resolution = resolution; mb.threshold = THRESH
        for b in self.blobs:
            if b.fam != fam: continue
            e = mb.elements.new(type='ELLIPSOID'); e.co = b.co; e.rotation = b.rot; e.radius = b.radius
            e.size_x, e.size_y, e.size_z = b.size; e.stiffness = STIFF; e.use_negative = b.neg
        o = bpy.data.objects.new('MB' + fam, mb); bpy.context.collection.objects.link(o)
        activate(o); bpy.ops.object.convert(target='MESH')
        o = bpy.context.object; o.name = fam
        keep = o.data.copy() if fine else None
        o.data.calc_loop_triangles()
        count = len(o.data.loop_triangles)
        if count > tris:
            m = o.modifiers.new('Budget', 'DECIMATE'); m.ratio = tris/count
            m.use_symmetry = symmetric; m.symmetry_axis = 'X'
            apply_all(o)
        me = o.data
        if cuts:
            import bmesh
            bm = bmesh.new(); bm.from_mesh(me)
            for co, no in cuts:
                geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
                bmesh.ops.bisect_plane(bm, geom=geom, plane_co=co, plane_no=no)
            bm.to_mesh(me); bm.free()
        # Skin weights: each bone gets the summed field of its elements; three strongest kept.
        pts = np.array([v.co for v in me.vertices])
        per_bone = {}
        for b, f in self.field(fam, pts):
            if not b.neg: per_bone[b.bone] = per_bone.get(b.bone, 0) + f
        names = list(per_bone); w = np.stack([per_bone[n] for n in names], 1)
        if w.shape[1] > 3: w[w < np.sort(w, 1)[:, -3:-2]] = 0
        empty = w.sum(1) <= 0
        w[empty, int(np.argmax(w.sum(0)))] = 1          # vertex outside every field: the family's main bone
        w /= w.sum(1, keepdims=True)
        for j, n in enumerate(names):
            g = o.vertex_groups.new(name=n)
            for i in np.nonzero(w[:, j] > .02)[0]: g.add([int(i)], float(w[i, j]), 'REPLACE')
        # Material of each face: the material whose elements dominate at the face centre.
        centres = np.array([p.center for p in me.polygons])
        per_mat = {}
        for b, f in self.field(fam, centres):
            if not b.neg: per_mat[b.mat] = per_mat.get(b.mat, 0) + f
        mats = list(per_mat); best = np.argmax(np.stack([per_mat[n] for n in mats], 1), 1)
        for p, k in zip(me.polygons, best):
            name = mats[k] if material is None else material(Vector(p.center), mats[k])
            p.material_index = kit.slot(o, name)
            p.use_smooth = True
        kit.parts.append(o)
        return (o, keep) if fine else o

def frame(direction):
    """Rotation whose local Z follows direction, local X as close to world X as possible."""
    z = direction.normalized()
    x = Vector((1,0,0)) - z*z.x
    if x.length < .3: x = Vector((0,1,0)) - z*z.y
    x.normalize(); y = z.cross(x)
    return Matrix((x, y, z)).transposed().to_quaternion()

def delete_faces(o, predicate):
    import bmesh
    bm = bmesh.new(); bm.from_mesh(o.data)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if predicate(f.calc_center_median())], context='FACES')
    bm.to_mesh(o.data); bm.free()

# ---------------------------------------------------------------- the body
def torso_at(prof, z):
    for (z0,rx0,ry0,y0),(z1,rx1,ry1,y1) in zip(prof, prof[1:]):
        if z0 <= z <= z1:
            u = (z-z0)/(z1-z0)
            return y0+(y1-y0)*u, rx0+(rx1-rx0)*u, ry0+(ry1-ry0)*u
    z0,rx,ry,y = prof[-1] if z > prof[-1][0] else prof[0]
    return y, rx, ry

def build_body(spec, J, kit):
    H, hh, eye, P = J['H'], J['hh'], J['eye'], J['P']
    build = spec['build']; female = build == 'female'; child = build == 'child'
    girth = spec.get('girth', 1.)
    S = Sculpt()
    prof = [(z*H, rx*H*girth, ry*H*girth, y*H) for z,rx,ry,y in TORSO[build]]
    hem = P['hem']*H
    spine_z, chest_z = J['Spine'][0].z, J['Chest'][0].z

    def torso_bone(p, t=0):
        return 'Hips' if p.z < spine_z else 'Spine' if p.z < chest_z else 'Chest'

    # Torso with neck and shoulder slope.
    S.tube('Torso', [(Vector((0,y,z)),rx,ry) for z,rx,ry,y in prof], torso_bone, 'Outer')
    for s in (1,-1):
        S.ell('Torso', (s*.042*H, (.022 if not female else .028)*H, .53*H if not child else .5*H),
              (.042*H*girth, .032*H*girth, .05*H), 'Hips', 'Outer')
        if female:
            S.ell('Torso', (s*.042*H, -.048*H, .728*H), (.04*H, .036*H, .036*H), 'Chest', 'Outer')
    if child:
        S.ell('Torso', (0, -.02*H, .6*H), (.06*H, .03*H, .06*H), 'Spine', 'Outer')
    neck_base = J['Neck'][0]; nr = P['neckR']*H
    # Own family: the neck rises out of the jacket instead of blending into it.
    S.tube('Neck', [(neck_base - Vector((0,0,.03*H)), nr, nr), (neck_base, nr, nr*.98),
                    (J['Head'][0] + Vector((0,.01*hh,.05*hh)), nr*.95, nr*.95)],
           lambda p, t: 'Chest' if t < .2 else 'Neck' if t < .8 else 'Head', 'Skin')
    trap_top = neck_base + Vector((0, 0, -.004*H))
    for side, s in (('L',1), ('R',-1)):
        tip = J['UpperArm_'+side][0] + Vector((-s*.012*H, 0, .016*H))
        S.tube('Torso', [(trap_top + Vector((s*.03*H,0,0)), .03*H, .03*H), (tip, .03*H, .033*H)],
               lambda p, t, side=side: 'Chest' if t < .5 else 'Shoulder_'+side, 'Outer')

    torso = S.mesh(kit, 'Torso', .009*H/1.78, 3200, True, lambda c, m: 'Trouser' if c.z < hem else 'Outer',
                   cuts=[(Vector((0,0,hem)), Vector((0,0,1)))])
    S.mesh(kit, 'Neck', .005*H/1.78, 500)

    # Arms: sleeve from inside the shoulder to the wrist; cuff at the end.
    limb = 1.1 if child else 1.
    for side, s in (('L',1), ('R',-1)):
        sh, el = J['UpperArm_'+side]; wr = J['Forearm_'+side][1]
        d = (wr-el).normalized()
        pts = [(sh + Vector((-s*.02*H, 0, .012*H)), .028*H, .03*H), (sh, .031*H, .032*H),
               (sh.lerp(el, .45), .027*H, .028*H), (el, .023*H, .025*H),
               (el.lerp(wr, .35), .025*H, .025*H), (wr, .02*H, .02*H), (wr + d*.012*H, .02*H, .02*H)]
        pts = [(p, rx*limb, ry*limb) for p, rx, ry in pts]
        cuff = wr - d*.012*H
        S.tube('Arm'+side, pts, lambda p, t, e=el: 'UpperArm_'+side if (p-e).dot(d) < 0 else 'Forearm_'+side, 'Outer')
        S.mesh(kit, 'Arm'+side, .008*H/1.78, 700, material=lambda c, m: 'Accent' if (c-cuff).dot(d) > 0 else 'Outer',
               cuts=[(cuff, d)])

        # Hand: palm, four slightly curled fingers merged into a relaxed hand, thumb forward.
        hl = P['hand']*H
        n = Vector((-s, 0, 0)); n = (n - d*n.dot(d)).normalized()      # palm faces the thigh
        f = d.cross(n)                                                 # forward, thumb side
        if f.y > 0: f = -f
        w0 = wr + d*.01*hl
        S.ell('Hand'+side, w0 + d*.3*hl, (.2*hl, .075*hl, .28*hl), 'Hand_'+side, 'Skin',
              rot=frame_axes(f, n, d))
        for k, off in enumerate((-.14, -.045, .045, .13)):
            base = w0 + d*.55*hl + f*off*hl
            length = (.40, .46, .44, .36)[k]*hl
            mid = base + d*length*.55 + n*.03*hl
            tip = mid + (d*.8 + n*.6).normalized()*length*.45
            S.tube('Hand'+side, [(base, .045*hl, .042*hl), (mid, .042*hl, .04*hl), (tip, .035*hl, .033*hl)],
                   'Hand_'+side, 'Skin')
        tb = w0 + d*.18*hl + f*.13*hl + n*.04*hl
        S.tube('Hand'+side, [(tb, .06*hl, .055*hl), (tb + d*.2*hl + f*.12*hl + n*.05*hl, .05*hl, .045*hl),
                             (tb + d*.38*hl + f*.14*hl + n*.1*hl, .04*hl, .038*hl)], 'Hand_'+side, 'Skin')
        S.mesh(kit, 'Hand'+side, .0035*H/1.78, 500)

    # Legs: trousers from inside the pelvis down into the shoe.
    thigh = 1.08 if female else 1.
    for side, s in (('L',1), ('R',-1)):
        hp, kn = J['UpperLeg_'+side]; an = J['LowerLeg_'+side][1]
        down = (an-kn).normalized()
        pts = [(hp + Vector((-s*.006*H, .004*H, .012*H)), .042*H*thigh, .046*H), (hp, .05*H*thigh, .053*H),
               (hp.lerp(kn, .4), .047*H*thigh, .049*H), (kn, .034*H, .035*H),
               (kn.lerp(an, .3) + Vector((0, .004*H, 0)), .034*H, .036*H), (kn.lerp(an, .75), .029*H, .03*H),
               (an, .028*H, .029*H), (an - Vector((0,0,.02*H)), .027*H, .028*H)]
        pts = [(p, rx*girth*limb, ry*girth*limb) for p, rx, ry in pts]
        S.tube('Leg'+side, pts, lambda p, t, k=kn: 'UpperLeg_'+side if p.z > k.z else 'LowerLeg_'+side, 'Trouser')
        # Where the thigh top shows above the hem it belongs to the jacket.
        S.mesh(kit, 'Leg'+side, .009*H/1.78, 800, material=lambda c, m: 'Outer' if c.z > hem else 'Trouser',
               cuts=[(Vector((0,0,hem)), Vector((0,0,1)))])

        # Shoe: upper over a separate sole.
        L = P['foot']*H*1.07; a = an; sole = .018*L/.28
        y0 = a.y + .21*L
        S.tube('Shoe'+side, [(Vector((a.x, y0-.035*L, .17*L)), .13*L, .155*L),
                             (Vector((a.x, a.y+.02*L, .28*L)), .15*L, .19*L),
                             (Vector((a.x-s*.01*L, a.y-.33*L, .17*L)), .175*L, .13*L),
                             (Vector((a.x-s*.02*L, y0-.93*L, .12*L)), .15*L, .09*L)], 'Foot_'+side, 'Shoe')
        S.ball('Shoe'+side, Vector((a.x, a.y-.04*L, .3*L)), .095*L, 'Foot_'+side, 'Shoe')
        S.mesh(kit, 'Shoe'+side, .004*H/1.78, 450)
        box(kit, 'Sole'+side, (a.x-s*.01*L, y0-.5*L, sole/2), (.34*L, L, sole), 'Sole', 'Foot_'+side, bevel=.008)

    head = build_head(spec, J, kit, S)
    details(spec, J, kit, torso, prof)
    return torso, head

def frame_axes(x, y, z):
    return Matrix((x.normalized(), y.normalized(), z.normalized())).transposed().to_quaternion()

def build_head(spec, J, kit, S):
    hh, eye = J['hh'], J['eye']; child = spec['build'] == 'child'; female = spec['build'] == 'female'
    c = Vector((0, J['Head'][0].y, eye))
    def at(x, y, z): return c + Vector((x, y, z))*hh
    def sz(*v): return tuple(a*hh for a in v)
    k = .9 if child else 1.                                          # smaller face on a child
    S.ell('Head', at(0, .03, .13), sz(.35, .41, .37) if child else sz(.33, .39, .345), 'Head', 'Skin')
    S.ell('Head', at(0, -.12, -.12*k), sz(.27*k, .2, .29*k), 'Head', 'Skin')
    jaw = .2 if female or child else .23
    S.ell('Head', at(0, -.1, -.31*k), sz(jaw*k, .18*k, .15*k), 'Head', 'Skin')
    S.ball('Head', at(0, -.25*k, -.41*k), .07*k*hh, 'Head', 'Skin')
    for s in (1,-1):
        S.ball('Head', at(s*.17*k, -.19*k, -.08*k), .065*k*hh, 'Head', 'Skin')
        S.ball('Head', at(s*.125*k, -.35*k, .005), .05*k*hh, 'Head', 'Skin', neg=True)   # eye socket
    if not child:
        S.ell('Head', at(0, -.29, .075), sz(.2, .055, .045), 'Head', 'Skin')           # brow ridge
    nose = .8 if child else 1.
    S.tube('Head', [(at(0, -.34*k, .0), .03*nose*hh, .03*nose*hh), (at(0, -.39*k, -.17*k), .038*nose*hh, .04*nose*hh)], 'Head', 'Skin')
    S.ball('Head', at(0, -.385*k, -.19*k), .045*nose*hh, 'Head', 'Skin')
    for s in (1,-1):
        S.ball('Head', at(s*.05*nose, -.355*k, -.205*k), .032*nose*hh, 'Head', 'Skin')
    S.ell('Head', at(0, -.315*k, -.3*k), sz(.075*k, .02, .014), 'Head', 'Skin')
    S.ell('Head', at(0, -.31*k, -.335*k), sz(.066*k, .022, .017), 'Head', 'Skin')
    head, fine = S.mesh(kit, 'Head', .0045*hh/.23, 2200, True, fine=True)

    # Eyes sit in the sockets: white eyeball with a dark iris.
    for s in (1,-1):
        # Sunk so that only the lid opening shows.
        centre = at(s*.125*k, -.272*k, .003)
        ellipsoid(kit, 'Eye', centre, (.05*hh,)*3, 'EyeWhite', 'Head', segments=12, rings=8)
        ellipsoid(kit, 'Iris', centre + Vector((0, -.043*hh, 0)), (.024*hh, .01*hh, .024*hh), 'Iris', 'Head',
                  segments=12, rings=8)
        if s == 1:   # mouth: a thin line of lip colour laid onto the face
            mouth = ribbon(kit, 'Mouth', [at((i/4-.5)*.14*k, -.34*k, (-.318-.012*(1-((i/4-.5)*2)**2))*k) for i in range(5)],
                           .016*hh, .002*hh, 'Lips', Vector((0,0,1)))
            wrap(mouth, head, .0005)
        brow = ribbon(kit, 'Brow', [at(s*(.06+.1*i/4)*k, -.4, (.1+.03*math.sin(i/4*math.pi))*k) for i in range(5)],
                      .028*hh, .004*hh, 'Hair', Vector((0,0,1)))
        wrap(brow, head, .001)
        ear = ellipsoid(kit, 'Ear', at(s*.29*k, .03, -.06*k), (.02*hh, .055*hh, .11*hh*k), 'Skin', 'Head',
                        rot=(0, s*.12, -s*.18), segments=12, rings=8)
    hair(spec, J, kit, S, fine, at, k)
    return head

HAIRLINE = {   # height of the hairline above eye level (head heights) by azimuth from the front, 0..pi
    'short':    [(0, .31), (.3, .27), (.45, .05), (.55, -.02), (.62, .03), (.8, -.14), (1, -.42)],
    'fringe':   [(0, .2), (.3, .2), (.45, .05), (.55, -.02), (.62, .03), (.8, -.14), (1, -.42)],
    'cropped':  [(0, .33), (.3, .29), (.45, .08), (.55, .02), (.62, .05), (.8, -.1), (1, -.36)],
    'long':     [(0, .3), (.25, .24), (.4, .02), (.6, -.1), (1, -.45)],
}

def hair(spec, J, kit, S, scalp, at, k):
    style = spec['hair']; hh = J['hh']
    keys = HAIRLINE['long' if style in ('long', 'pigtails') else style]
    thick = {'short': (.03, .07), 'fringe': (.04, .08), 'cropped': (.012, .02), 'long': (.04, .09),
             'pigtails': (.035, .07)}[style]
    import bmesh
    bm = bmesh.new(); bm.from_mesh(scalp)
    centre = at(0, .03, .13); eye = J['eye']
    def line(az):
        u = abs(az)/math.pi
        for (u0, h0), (u1, h1) in zip(keys, keys[1:]):
            if u0 <= u <= u1: return (h0 + (h1-h0)*(u-u0)/(u1-u0))*hh
        return keys[-1][1]*hh
    def inside(p):
        az = math.atan2(p.x - centre.x, -(p.y - centre.y))
        return p.z - eye > line(az)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if not inside(f.calc_center_median())], context='FACES')
    bm.normal_update()
    # Slide the stair-stepped border onto the hairline curve.
    for v in bm.verts:
        if v.is_boundary:
            az = math.atan2(v.co.x - centre.x, -(v.co.y - centre.y))
            v.co.z = eye + line(az)
    for v in bm.verts:
        u = max(0, min(1, (v.co.z - eye + .1*hh)/(.6*hh)))
        v.co += v.normal * (thick[0] + (thick[1]-thick[0])*u)*hh
    me = bpy.data.meshes.new('Hair'); bm.to_mesh(me); bm.free()
    o = link('Hair', me)
    m = o.modifiers.new('Thickness', 'SOLIDIFY'); m.thickness = .012*hh; m.offset = -1
    m = o.modifiers.new('Budget', 'DECIMATE'); m.ratio = .25
    apply_all(o); finish(kit, o, 'Hair', 'Head')
    if style == 'long':
        S.tube('HairLong', [(at(0, .27, .0), .27*hh, .13*hh), (at(0, .31, -.35), .29*hh, .1*hh), (at(0, .29, -.72), .25*hh, .06*hh)],
               'Head', 'Hair')
        S.mesh(kit, 'HairLong', .006*hh/.23, 500, True)
    if style == 'pigtails':
        for s in (1,-1):
            S.tube('Pigtail', [(at(s*.29, .16, .1), .08*hh, .08*hh), (at(s*.33, .2, -.15), .07*hh, .07*hh),
                               (at(s*.31, .22, -.5), .05*hh, .05*hh)], 'Head', 'Hair')
            ellipsoid(kit, 'Hair tie', at(s*.3, .17, .06), (.07*hh, .07*hh, .04*hh), 'Accent', 'Head',
                      segments=12, rings=6)
        S.mesh(kit, 'Pigtail', .006*hh/.23, 500, True)

def details(spec, J, kit, torso, prof):
    """Zip, pockets, collar, backpack and police kit laid onto the clothed torso."""
    H = J['H']; P = J['P']; police = spec.get('police')
    neck = J['Neck'][0]; nr = P['neckR']*H
    # Stand collar at the height where the neck comes out of the jacket.
    top = max(v.co.z for v in torso.data.vertices if math.hypot(v.co.x, v.co.y-neck.y) < nr*1.25)
    ring(kit, 'Collar', [(0, neck.y, top-.016*H, nr*1.42, nr*1.38), (0, neck.y, top-.004*H, nr*1.24, nr*1.22),
                         (0, neck.y, top+.006*H, nr*1.14, nr*1.12), (0, neck.y, top+.009*H, nr*1.05, nr*1.04)],
         'Accent', None)
    copy_weights(kit.parts[-1], torso)
    front = lambda z: torso_at(prof, z)
    if not police:
        zs = [P['hem']*H + .01*H + i*(neck.z - .05*H - P['hem']*H)/12 for i in range(13)]
        zip_ = ribbon(kit, 'Zip', [Vector((0, front(z)[0]-front(z)[2], z)) for z in zs], .008*H, .002, 'Cream')
        wrap(zip_, torso, .001)
        for s in (1,-1):
            z = (P['hem']+.05)*H; y, rx, ry = front(z)
            pts = [Vector((s*(.02+.012*i)*H, y-ry*.9, z + i*.004*H)) for i in range(5)]
            welt = ribbon(kit, 'Pocket welt', pts, .012*H, .003, 'Accent', Vector((0,0,1)))
            wrap(welt, torso, .001)
    if spec.get('backpack'):
        z = (P['chest']+.02)*H; y, rx, ry = torso_at(prof, z)
        depth = .08*H; top = P['shoulderZ']*H - .02*H
        box(kit, 'Backpack', (0, y+ry+depth/2-.005, z), (.16*H, depth, .21*H), 'Accent', 'Chest', bevel=.035)
        box(kit, 'Pack pocket', (0, y+ry+depth+.012*H, z-.04*H), (.12*H, .03*H, .08*H), 'Pack', 'Chest', bevel=.015)
        for s in (1,-1):
            x = s*.055*H
            pts = [Vector((x, torso_at(prof, zz)[0]-torso_at(prof, zz)[2], zz)) for zz in
                   [P['hem']*H+.12*H + i*(top-P['hem']*H-.12*H)/8 for i in range(9)]]
            strap = ribbon(kit, 'Strap', pts, .022*H, .004, 'Accent', Vector((1,0,0)))
            wrap(strap, torso, .002)
    if police:
        police_kit(spec, J, kit, torso, prof)

def police_kit(spec, J, kit, torso, prof):
    H, hh, P = J['H'], J['hh'], J['P']
    # Vest: the torso shape grown by 12 mm between the chest and the waist, open at neck and hem.
    S = Sculpt()
    lo, hi = .585*H, .805*H
    S.tube('Vest', [(Vector((0,y,z)), rx+.012, ry+.012) for z,rx,ry,y in prof if lo-.06*H <= z <= hi+.02*H],
           'Chest', 'Vest')
    neck = J['Neck'][0]
    for side, s in (('L',1), ('R',-1)):
        tip = J['UpperArm_'+side][0] + Vector((-s*.03*H, 0, .014*H))
        S.tube('Vest', [(neck + Vector((s*.03*H, 0, -.006*H)), .036*H, .036*H), (tip, .034*H, .036*H)], 'Chest', 'Vest')
    # Full resolution first: edges and bands are cut with planes, then reduced keeping material borders.
    bands = (.625*H, .675*H); half = .012*H
    up = Vector((0,0,1))
    cuts = [(Vector((0,0,lo)), up)] + [(Vector((0,0,b+d)), up) for b in bands for d in (-half, half)]
    vest = S.mesh(kit, 'Vest', .007*H/1.78, 10**7, True, cuts=cuts,
                  material=lambda c, m: 'Reflect' if any(abs(c.z-b) < half for b in bands) else 'Vest')
    nr = P['neckR']*H
    delete_faces(vest, lambda c: c.z < lo or (c.z > neck.z - .03*H and math.hypot(c.x, c.y-neck.y) < nr + .03*H))
    m = vest.modifiers.new('Budget', 'DECIMATE'); m.decimate_type = 'DISSOLVE'
    m.angle_limit = math.radians(10); m.delimit = {'MATERIAL'}
    m = vest.modifiers.new('Thickness', 'SOLIDIFY'); m.thickness = .004; m.offset = -1
    apply_all(vest)
    activate(vest); bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.quads_convert_to_tris(); bpy.ops.object.mode_set(mode='OBJECT')
    copy_weights(vest, torso)
    y, rx, ry = torso_at(prof, .74*H)
    text = label(kit, 'Vest lettering', 'ДПС', (0, y+ry+.03, .74*H), .045*H, 'Reflect')
    wrap(text, vest, .0015)
    # Peaked cap: band, crown, visor and badge.
    eye = J['eye']; c = Vector((0, J['Head'][0].y, eye))
    at = lambda x, y, z: c + Vector((x, y, z))*hh
    ring(kit, 'Cap band', [(0, .03*hh+c.y, eye+.22*hh, .335*hh, .42*hh), (0, .04*hh+c.y, eye+.37*hh, .345*hh, .43*hh)],
         'CapBand', 'Head', 24, closed=True)
    ellipsoid(kit, 'Cap crown', at(0, .06, .42), (.44*hh, .52*hh, .1*hh), 'Outer', 'Head', segments=24, rings=12)
    ellipsoid(kit, 'Cap visor', at(0, -.36, .23), (.28*hh, .2*hh, .025*hh), 'Shoe', 'Head', rot=(.3, 0, 0),
              segments=20, rings=8)
    ellipsoid(kit, 'Cap badge', at(0, -.405, .31), (.05*hh, .02*hh, .06*hh), 'Badge', 'Head', segments=10, rings=6)
    # Striped traffic baton gripped in the right hand.
    wr, tip = J['Hand_R']; d = (tip-wr).normalized()
    a = wr + d*.06*H; b = a + Vector((-.03, -.4, -.92)).normalized()*.5
    for i in range(8):
        cylinder(kit, 'Baton', a.lerp(b, i/8), a.lerp(b, (i+1)/8), .015, 'Shoe' if i%2 == 0 else 'Reflect', 'Hand_R')
    for side, s in (('L',1), ('R',-1)):
        sh = J['UpperArm_'+side][0]
        pts = [neck.lerp(sh, .35+.1*i) + Vector((0, 0, .06*H)) for i in range(6)]
        ep = ribbon(kit, 'Epaulette', pts, .03*H, .004, 'Accent', Vector((0,1,0)))
        wrap(ep, torso, .003)
