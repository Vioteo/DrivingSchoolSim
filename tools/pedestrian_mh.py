"""Pedestrians from the CC0 MakeHuman base mesh and system assets (Blender 5, bpy).

What is reproduced from MakeHuman 1.x (shared/proxy.py, apps/human.py):
  * macro targets: gender / age / muscle / weight / height / proportions / ethnicity mixed by the same
    piecewise-linear weights, applied to the whole base mesh including helper geometry;
  * proxy fitting (.mhclo): each proxy vertex = weighted sum of three base vertices + offset scaled by
    the x/y/z_scale reference distances; body faces listed in delete_verts are removed under clothes;
  * skinning: the default 163-bone weights are summed onto our 20-bone game skeleton, proxies inherit
    them through their reference vertices.
MakeHuman space: decimetres, Y up, face +Z. Ours: metres, Z up, face -Y.
"""
import bpy, json, math, re
import numpy as np
from pathlib import Path
from mathutils import Vector, Quaternion, Matrix
import fetch_makehuman as fetch

MH_SCALE = .1
ARM_OUT = math.radians(10)          # rest pose: upper arm away from the body (clips rely on it)

def to_ours(co):
    return np.stack([co[:, 0], -co[:, 2], co[:, 1]], 1) * MH_SCALE

# ---------------------------------------------------------------- files
def load_obj(path):
    """Vertices, UVs and faces [(vertex ids, uv ids)] per group."""
    verts, uvs, faces = [], [], {}
    group = None
    for line in open(path, encoding='utf-8', errors='replace'):
        if line.startswith('v '):
            verts.append([float(x) for x in line.split()[1:4]])
        elif line.startswith('vt '):
            uvs.append([float(x) for x in line.split()[1:3]])
        elif line.startswith('g '):
            group = line.split()[1] if len(line.split()) > 1 else None
        elif line.startswith('f '):
            vi, ti = [], []
            for w in line.split()[1:]:
                p = w.split('/')
                vi.append(int(p[0]) - 1); ti.append(int(p[1]) - 1 if len(p) > 1 and p[1] else -1)
            faces.setdefault(group, []).append((vi, ti))
    return np.array(verts, float), np.array(uvs, float), faces

_targets = {}
def target(rel):
    if rel not in _targets:
        idx, d = [], []
        for line in open(fetch.data('targets/' + rel)):
            if line.startswith('#') or not line.strip(): continue
            w = line.split(); idx.append(int(w[0])); d.append([float(x) for x in w[1:4]])
        _targets[rel] = (np.array(idx, dtype=int), np.array(d, dtype=float).reshape(-1, 3))
    return _targets[rel]

def parse_material(path):
    m = {'dir': path.parent}
    for line in open(path, encoding='utf-8'):
        w = line.split()
        if len(w) >= 2 and not line.startswith('#'):
            m[w[0]] = ' '.join(w[1:])
    return m

class Proxy:
    """A fitted MakeHuman proxy (.mhclo): clothes, hair, eyebrows, eyelashes, eyes."""
    def __init__(self, path):
        self.path = Path(path); self.refs = []; self.weights = []; self.offsets = []
        self.scale = [None, None, None]; self.delete = set(); self.material = None; self.obj = None
        mode = None
        for line in open(self.path, encoding='utf-8'):
            w = line.split()
            if not w or line.startswith('#'): continue
            key = w[0]
            if key == 'verts': mode = 'verts'; continue
            if key == 'delete_verts': mode = 'delete'; continue
            if mode == 'verts' and re.match(r'^-?\d', key):
                if len(w) == 1:
                    self.refs.append((int(w[0]),)*3); self.weights.append((1, 0, 0)); self.offsets.append((0, 0, 0))
                else:
                    self.refs.append(tuple(map(int, w[:3]))); self.weights.append(tuple(map(float, w[3:6])))
                    self.offsets.append(tuple(map(float, w[6:9])) if len(w) > 6 else (0, 0, 0))
                continue
            if mode == 'delete' and re.match(r'^\d', key):
                prev = None; seq = False
                for t in w:
                    if t == '-': seq = True; continue
                    v = int(t)
                    if seq: self.delete.update(range(prev, v+1)); seq = False
                    else: self.delete.add(v)
                    prev = v
                continue
            if key == 'weights': mode = 'weights'   # per-bone weight lists are not used
            if key == 'obj_file': self.obj = self.path.parent / w[1]
            elif key == 'material': self.material = parse_material(self.path.parent / w[1])
            elif key in ('x_scale', 'y_scale', 'z_scale'):
                self.scale['xyz'.index(key[0])] = (int(w[1]), int(w[2]), float(w[3]))
        if self.material is None:        # some community assets rely on the only .mhmat next to them
            mats = sorted(self.path.parent.glob('*.mhmat'))
            if mats: self.material = parse_material(mats[0])
        self.refs = np.array(self.refs); self.weights = np.array(self.weights); self.offsets = np.array(self.offsets)

    def fit(self, co):
        m = np.identity(3)
        if all(self.scale):
            for n, (a, b, den) in enumerate(self.scale):
                m[n, n] = abs(co[a, n] - co[b, n]) / den
        return (co[self.refs[:, 0]] * self.weights[:, 0, None] + co[self.refs[:, 1]] * self.weights[:, 1, None] +
                co[self.refs[:, 2]] * self.weights[:, 2, None] + self.offsets @ m.T)

# ---------------------------------------------------------------- macro targets
def _three(v, lo, mid, hi):
    top = max(0., v*2 - 1); bottom = max(0., 1 - v*2)
    return {lo: bottom, mid: 1 - top - bottom, hi: top}

def macro_targets(p):
    """(target file, weight) for MakeHuman macro values p (0..1 each; age via years)."""
    years = p['age']
    age = (years - 1) / 48 if years < 25 else .5 + (years - 25) / 130
    if age < .5:
        young = max(0., (age - .1875) * 3.2); baby = max(0., 1 - age * 5.333)
        ages = {'baby': baby, 'child': max(0., min(1., 5.333*age) - young), 'young': young, 'old': 0.}
    else:
        old = max(0., age*2 - 1); ages = {'baby': 0., 'child': 0., 'young': 1 - old, 'old': old}
    genders = {'female': 1 - p['gender'], 'male': p['gender']}
    muscles = _three(p.get('muscle', .5), 'minmuscle', 'averagemuscle', 'maxmuscle')
    weights = _three(p.get('weight', .5), 'minweight', 'averageweight', 'maxweight')
    h = p.get('height', .5); heights = {'minheight': max(0., 1 - h*2), 'maxheight': max(0., h*2 - 1)}
    pr = p.get('proportions', .5)
    props = {'uncommonproportions': max(0., 1 - pr*2), 'idealproportions': max(0., pr*2 - 1)}
    races = p.get('race', {'caucasian': 1.})
    out = []
    for g, gw in genders.items():
        for a, aw in ages.items():
            if gw*aw <= 0: continue
            for r, rw in races.items():
                out.append((f'macrodetails/{r}-{g}-{a}.target', gw*aw*rw))
            for m, mw in muscles.items():
                for w, ww in weights.items():
                    base = gw*aw*mw*ww
                    if base <= 0: continue
                    out.append((f'macrodetails/universal-{g}-{a}-{m}-{w}.target', base))
                    for k, kw in heights.items():
                        out.append((f'macrodetails/height/{g}-{a}-{m}-{w}-{k}.target', base*kw))
                    for k, kw in props.items():
                        out.append((f'macrodetails/proportions/{g}-{a}-{m}-{w}-{k}.target', base*kw))
    return [(f, w) for f, w in out if w > 1e-4]

# ---------------------------------------------------------------- skeleton mapping
# Our bone <- MakeHuman bones whose weights it takes. Anything unlisted on the head goes to Head.
MAP = {'Hips': ['root', 'spine05', 'pelvis.L', 'pelvis.R'], 'Spine': ['spine04', 'spine03'],
       'Chest': ['spine02', 'spine01', 'breast.L', 'breast.R'], 'Neck': ['neck01', 'neck02', 'neck03']}
for s, S in (('L', 'L'), ('R', 'R')):
    MAP['UpperLeg_'+s] = [f'upperleg01.{S}', f'upperleg02.{S}']
    MAP['LowerLeg_'+s] = [f'lowerleg01.{S}', f'lowerleg02.{S}']
    MAP['Foot_'+s] = [f'foot.{S}'] + [f'toe{i}-{j}.{S}' for i in range(1, 6) for j in range(1, 4)]
    MAP['Shoulder_'+s] = [f'clavicle.{S}', f'shoulder01.{S}']
    MAP['UpperArm_'+s] = [f'upperarm01.{S}', f'upperarm02.{S}']
    MAP['Forearm_'+s] = [f'lowerarm01.{S}', f'lowerarm02.{S}']
    MAP['Hand_'+s] = [f'wrist.{S}'] + [f'metacarpal{i}.{S}' for i in range(1, 5)] + \
                     [f'finger{i}-{j}.{S}' for i in range(1, 6) for j in range(1, 4)]
BONES = [('Root',None),('Hips','Root'),('Spine','Hips'),('Chest','Spine'),('Neck','Chest'),('Head','Neck')] + \
    [(b+'_'+s, p+'_'+s if p not in ('Hips','Chest') else p) for s in 'LR' for b,p in
     (('UpperLeg','Hips'),('LowerLeg','UpperLeg'),('Foot','LowerLeg'),
      ('Shoulder','Chest'),('UpperArm','Shoulder'),('Forearm','UpperArm'),('Hand','Forearm'))]
NAMES = [b for b, _ in BONES]

def bone_weights(n_verts):
    """(n_verts, 20) weights of our bones on the base mesh."""
    mh = json.load(open(fetch.data('rigs/default_weights.mhw')))['weights']
    col = {b: NAMES.index(ours) for ours, bs in MAP.items() for b in bs}
    W = np.zeros((n_verts, len(NAMES)))
    for b, pairs in mh.items():
        j = col.get(b, NAMES.index('Head'))
        for v, w in pairs: W[v, j] += w
    W /= np.maximum(W.sum(1, keepdims=True), 1e-9)
    return W

def joints(co):
    """MakeHuman joint name -> position (our space) on the morphed mesh."""
    skel = json.load(open(fetch.data('rigs/default.mhskel')))
    ours = to_ours(co)
    pos = {k: Vector(ours[v].mean(0)) for k, v in skel['joints'].items()}
    head = lambda b: pos[skel['bones'][b]['head']]
    tail = lambda b: pos[skel['bones'][b]['tail']]
    return head, tail

def skeleton(co):
    head, tail = joints(co)
    J = {}
    J['Root'] = (Vector((0, 0, 0)), Vector((0, 0, .15)))
    root = head('root'); root.x = 0
    J['Hips'] = (root, head('spine04'))
    J['Spine'] = (head('spine04'), head('spine02'))
    J['Chest'] = (head('spine02'), head('neck01'))
    J['Neck'] = (head('neck01'), head('head'))
    J['Head'] = (head('head'), tail('head'))
    for s in 'LR':
        J['UpperLeg_'+s] = (head(f'upperleg01.{s}'), head(f'lowerleg01.{s}'))
        J['LowerLeg_'+s] = (head(f'lowerleg01.{s}'), head(f'foot.{s}'))
        J['Foot_'+s] = (head(f'foot.{s}'), tail(f'foot.{s}'))
        J['Shoulder_'+s] = (head(f'clavicle.{s}'), head(f'upperarm01.{s}'))
        J['UpperArm_'+s] = (head(f'upperarm01.{s}'), head(f'lowerarm01.{s}'))
        J['Forearm_'+s] = (head(f'lowerarm01.{s}'), head(f'wrist.{s}'))
        J['Hand_'+s] = (head(f'wrist.{s}'), head(f'finger3-1.{s}'))
    return J

# ---------------------------------------------------------------- blender helpers
def activate(o):
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True); bpy.context.view_layer.objects.active = o

def apply_all(o):
    activate(o)
    for m in list(o.modifiers): bpy.ops.object.modifier_apply(modifier=m.name)

class Kit:
    """Materials (with their texture files for Unity) and mesh parts of one character."""
    def __init__(self, textures):
        self.materials = {}; self.parts = []; self.textures = textures; self.unity = {}

    def texture(self, src, kind, size, tint=None):
        """Downscaled copy of a MakeHuman texture in the Unity texture folder; returns its file name."""
        src = Path(src)
        img = bpy.data.images.load(str(src), check_existing=True)
        alpha = kind == 'd' and img.channels == 4 and self._has_alpha(img)
        tag = '' if tint is None else '_' + '%02x%02x%02x' % tuple(int(c*255) for c in tint)
        name = f'MH_{src.stem}{tag}_{kind}.' + ('png' if alpha or kind == 'n' else 'jpg')
        out = self.textures / name
        if not out.exists():
            copy = img.copy(); copy.scale(min(size, img.size[0]), min(size, img.size[1]))
            if tint is not None:
                px = np.array(copy.pixels[:]).reshape(-1, 4)
                lum = px[:, :3] @ np.array([.3, .59, .11])
                px[:, :3] = np.clip(lum[:, None] * np.array(tint) * 3, 0, 1)
                copy.pixels[:] = px.ravel()
            copy.filepath_raw = str(out)
            copy.file_format = 'PNG' if out.suffix == '.png' else 'JPEG'
            copy.save()
            bpy.data.images.remove(copy)
        return name, alpha

    @staticmethod
    def _has_alpha(img):
        px = np.array(img.pixels[:]).reshape(-1, 4)
        return float(px[:, 3].min()) < .9

    def mh_material(self, name, mhmat, size=1024, tint=None, alpha_clip=None, double_sided=False):
        """Blender material from a MakeHuman .mhmat, plus the texture record for the Unity builder."""
        d = mhmat['dir']; rec = {'name': 'Ped_' + name, 'alphaClip': False, 'doubleSided': double_sided}
        m = bpy.data.materials.new('Ped_' + name); m.use_nodes = True
        nodes = m.node_tree.nodes; bsdf = nodes.get('Principled BSDF')
        bsdf.inputs['Roughness'].default_value = .75
        if 'diffuseTexture' in mhmat:
            file, alpha = self.texture(d / mhmat['diffuseTexture'], 'd', size, tint)
            rec['diffuse'] = file
            rec['alphaClip'] = alpha if alpha_clip is None else alpha_clip
            tex = nodes.new('ShaderNodeTexImage')
            tex.image = bpy.data.images.load(str(self.textures / file), check_existing=True)
            m.node_tree.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
            if rec['alphaClip']:
                m.node_tree.links.new(tex.outputs['Alpha'], bsdf.inputs['Alpha'])
        else:
            c = [float(x) for x in mhmat.get('diffuseColor', '0.5 0.5 0.5').split()]
            bsdf.inputs['Base Color'].default_value = (*c, 1); rec['color'] = c
        if 'normalmapTexture' in mhmat:
            rec['normal'], _ = self.texture(d / mhmat['normalmapTexture'], 'n', min(size, 512))
        self.materials[name] = m; self.unity[name] = rec
        return m

    def slot(self, o, name):
        m = self.materials[name]
        if m.name not in o.data.materials: o.data.materials.append(m)
        return list(o.data.materials).index(m)

    def flat(self, name, color, roughness=.7):
        m = bpy.data.materials.new('Ped_' + name); m.use_nodes = True
        b = m.node_tree.nodes.get('Principled BSDF')
        b.inputs['Base Color'].default_value = (*color, 1); b.inputs['Roughness'].default_value = roughness
        self.materials[name] = m
        self.unity[name] = {'name': 'Ped_' + name, 'color': list(color), 'alphaClip': False, 'doubleSided': False,
                            'smoothness': 1 - roughness}
        return m

def make_mesh(kit, name, verts, faces, uvs, material, W):
    """Mesh object with UVs, one material and our bone weights (top four per vertex)."""
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in verts], [], [f for f, _ in faces])
    me.update()
    if len(uvs):
        layer = me.uv_layers.new(name='UVMap')
        loop = 0
        for f, t in faces:
            for k in t:
                layer.data[loop].uv = uvs[k] if k >= 0 else (0, 0); loop += 1
    me.materials.append(kit.materials[material])
    for p in me.polygons: p.use_smooth = True
    o = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(o)
    W = W.copy()
    if W.shape[1] > 4: W[W < np.sort(W, 1)[:, -4:-3]] = 0
    W /= np.maximum(W.sum(1, keepdims=True), 1e-9)
    for j, n in enumerate(NAMES):
        g = o.vertex_groups.new(name=n)          # every mesh gets all groups in the same order
        idx = np.nonzero(W[:, j] > .01)[0]
        for i in idx: g.add([int(i)], float(W[i, j]), 'REPLACE')
    kit.parts.append(o)
    return o

# ---------------------------------------------------------------- assembly
def resolve(roots, rel):
    for r in roots:
        if (r / rel).exists(): return r / rel
    raise FileNotFoundError(rel)

def build(spec, kit, roots):
    """Morphed body, fitted proxies and our skeleton for one character spec; returns (J, parts by role)."""
    A = roots[0]
    base_v, base_uv, base_f = load_obj(fetch.data('3dobjs/base.obj'))
    co = base_v.copy()
    for f, w in macro_targets(spec['macro']):
        idx, d = target(f); co[idx] += w*d
    W = bone_weights(len(co))
    ours = to_ours(co)
    roles = {}
    # Proxies first: their delete_verts hide the body underneath.
    hidden = set()
    items = [(role, Proxy(resolve(roots, p))) for role, p in spec['proxies']]
    for role, px in items: hidden |= px.delete
    # Body: only faces whose vertices are all visible.
    skin = parse_material(A / spec['skin'])
    kit.mh_material('Skin', skin)
    faces = [(f, t) for f, t in base_f['body'] if not any(v in hidden for v in f)]
    used = sorted({v for f, _ in faces for v in f}); remap = {v: i for i, v in enumerate(used)}
    body = make_mesh(kit, 'Body', ours[used], [([remap[v] for v in f], t) for f, t in faces], base_uv,
                     'Skin', W[used])
    roles['body'] = body
    for role, px in items:
        pv, puv, pf = load_obj(px.obj)
        fitted = to_ours(px.fit(co))
        pw = (W[px.refs[:, 0]] * px.weights[:, 0, None] + W[px.refs[:, 1]] * px.weights[:, 1, None] +
              W[px.refs[:, 2]] * px.weights[:, 2, None])
        name = role.capitalize()
        opts = spec.get('materials', {}).get(role, {})
        kit.mh_material(name, px.material, size=opts.get('size', 1024), tint=opts.get('tint'),
                        double_sided=role in ('hair', 'eyebrows', 'eyelashes'))
        allf = [f for g in pf.values() for f in g]
        roles[role] = o = make_mesh(kit, name, fitted, allf, puv, name, pw)
        if 'tris' in opts:
            o.data.calc_loop_triangles(); n = len(o.data.loop_triangles)
            if n > opts['tris']:
                m = o.modifiers.new('Budget', 'DECIMATE'); m.ratio = opts['tris']/n; apply_all(o)
    # MakeHuman's smooth helpers (body-hugging suit, skirt): bases for coats, jackets and the police
    # vest; they follow the morphs and the skin weights and are removed after use.
    kit.flat('Helper', (.5, .5, .5))
    for role, group in (('tights', 'helper-tights'), ('skirt', 'helper-skirt')):
        hf = base_f[group]; hu = sorted({v for f, _ in hf for v in f}); hr = {v: i for i, v in enumerate(hu)}
        roles[role] = make_mesh(kit, role.capitalize(), ours[hu], [([hr[v] for v in f], t) for f, t in hf],
                                base_uv, 'Helper', W[hu])
    J = skeleton(co)
    # Soles on Z = 0, pelvis over the origin.
    low = min(min(v.co.z for v in o.data.vertices) for o in kit.parts)
    shift = Vector((0, -J['Hips'][0].y, -low))
    for o in kit.parts: o.data.transform(Matrix.Translation(shift))
    J = {k: (v[0] + shift, v[1] + shift) if k != 'Root' else v for k, v in J.items()}
    return J, roles, W, ours

def rig_create(J, name):
    data = bpy.data.armatures.new('PedestrianSkeleton')
    rig = bpy.data.objects.new(name, data); bpy.context.collection.objects.link(rig)
    activate(rig); bpy.ops.object.mode_set(mode='EDIT')
    for n, parent in BONES:
        b = data.edit_bones.new(n); b.head, b.tail = J[n]
        if parent: b.parent = data.edit_bones[parent]
    bpy.ops.object.mode_set(mode='OBJECT')
    return rig

def relax_arms(rig, parts):
    """MakeHuman is modelled in an A-pose; lower the arms to hang ARM_OUT from vertical and make it rest."""
    for o in parts:
        o.parent = rig; m = o.modifiers.new('Skin', 'ARMATURE'); m.object = rig
    activate(rig); bpy.ops.object.mode_set(mode='POSE')
    for pb in rig.pose.bones: pb.rotation_mode = 'QUATERNION'
    for s, sign in (('L', 1), ('R', -1)):
        up = rig.pose.bones['UpperArm_'+s]; fore = rig.pose.bones['Forearm_'+s]
        u = (up.bone.tail_local - up.bone.head_local).normalized()
        f = (fore.bone.tail_local - fore.bone.head_local).normalized()
        want_u = Vector((sign*math.sin(ARM_OUT), u.y*.3, -math.cos(ARM_OUT))).normalized()
        q_up = u.rotation_difference(want_u)
        want_f = Vector((sign*math.sin(ARM_OUT + math.radians(3)), -.10, -math.cos(ARM_OUT + math.radians(3)))).normalized()
        q_fore = f.rotation_difference(q_up.inverted() @ want_f)
        # Legs: from the A-pose stance to feet about hip width apart.
        thigh = rig.pose.bones['UpperLeg_'+s]; shin = rig.pose.bones['LowerLeg_'+s]
        t = (thigh.bone.tail_local - thigh.bone.head_local).normalized()
        k = (shin.bone.tail_local - shin.bone.head_local).normalized()
        q_thigh = t.rotation_difference(Vector((sign*math.sin(math.radians(1.5)), t.y, -1)).normalized())
        q_shin = k.rotation_difference(q_thigh.inverted() @ Vector((0, k.y, -1)).normalized())
        for pb, q in ((up, q_up), (fore, q_fore), (thigh, q_thigh), (shin, q_shin)):
            rest = pb.bone.matrix_local.to_3x3().to_quaternion()
            pb.rotation_quaternion = rest.inverted() @ q @ rest
    bpy.context.view_layer.update()
    bpy.ops.object.mode_set(mode='OBJECT')
    for o in parts:
        apply_all(o)
    activate(rig); bpy.ops.object.mode_set(mode='POSE')
    bpy.ops.pose.select_all(action='SELECT'); bpy.ops.pose.armature_apply(selected=False)
    for pb in rig.pose.bones: pb.rotation_mode = 'XYZ'; pb.rotation_quaternion = (1, 0, 0, 0)
    bpy.ops.object.mode_set(mode='OBJECT')
    # Back onto the floor after the legs moved.
    low = min(min(v.co.z for v in o.data.vertices) for o in parts)
    for o in parts: o.data.transform(Matrix.Translation((0, 0, -low)))
    activate(rig); bpy.ops.object.mode_set(mode='EDIT')
    for b in rig.data.edit_bones:
        if b.name != 'Root': b.head.z -= low; b.tail.z -= low
    bpy.ops.object.mode_set(mode='OBJECT')

# ---------------------------------------------------------------- extra parts
def finish(kit, o, mat, bone=None):
    activate(o)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    o.data.materials.clear(); kit.slot(o, mat)
    for f in o.data.polygons: f.material_index = 0; f.use_smooth = True
    if bone:
        o.vertex_groups.new(name=bone).add(list(range(len(o.data.vertices))), 1, 'REPLACE')
    kit.parts.append(o)
    return o

def ellipsoid(kit, name, loc, semi, mat, bone, rot=(0,0,0), segments=16, rings=10):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, location=loc, rotation=rot)
    o = bpy.context.object; o.name = name; o.scale = semi
    return finish(kit, o, mat, bone)

def box(kit, name, loc, size, mat, bone, bevel=.01):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    o = bpy.context.object; o.name = name; o.scale = size
    activate(o); bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        m = o.modifiers.new('Soft edges', 'BEVEL'); m.width = bevel; m.segments = 3
        apply_all(o)
    return finish(kit, o, mat, bone)

def cylinder(kit, name, a, b, radius, mat, bone, sides=12):
    a, b = Vector(a), Vector(b)
    bpy.ops.mesh.primitive_cylinder_add(vertices=sides, radius=radius, depth=(b-a).length, location=(a+b)/2)
    o = bpy.context.object; o.name = name
    o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = Vector((0,0,1)).rotation_difference(b-a)
    return finish(kit, o, mat, bone)

def ring(kit, name, rows, mat, bone, sides=32, closed=False):
    vs = [(x + rx*math.cos(i*math.tau/sides), y + ry*math.sin(i*math.tau/sides), z)
          for x,y,z,rx,ry in rows for i in range(sides)]
    fs = [(j*sides+i, j*sides+(i+1)%sides, (j+1)*sides+(i+1)%sides, (j+1)*sides+i)
          for j in range(len(rows)-1) for i in range(sides)]
    if closed:
        fs.append(tuple(reversed(range(sides)))); fs.append(tuple(range((len(rows)-1)*sides, len(rows)*sides)))
    me = bpy.data.meshes.new(name); me.from_pydata(vs, [], fs); me.update()
    o = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(o)
    return finish(kit, o, mat, bone)

def label(kit, name, text, loc, size, mat):
    bpy.ops.object.text_add(location=loc)
    o = bpy.context.object
    o.data.body = text; o.data.size = size; o.data.resolution_u = 3
    o.data.align_x = 'CENTER'; o.data.align_y = 'CENTER'
    o.rotation_euler = (math.pi/2, 0, math.pi)      # readable from behind (+Y)
    bpy.ops.object.convert(target='MESH')
    o = bpy.context.object; o.name = name
    return finish(kit, o, mat)

def copy_weights(o, source):
    o.vertex_groups.clear()
    m = o.modifiers.new('Weights', 'DATA_TRANSFER'); m.object = source
    m.use_vert_data = True; m.data_types_verts = {'VGROUP_WEIGHTS'}; m.vert_mapping = 'POLYINTERP_NEAREST'
    m.layers_vgroup_select_src = 'ALL'; m.layers_vgroup_select_dst = 'NAME'
    activate(o); bpy.ops.object.datalayout_transfer(modifier=m.name)
    bpy.ops.object.modifier_apply(modifier=m.name)

def wrap(o, target, offset):
    m = o.modifiers.new('Wrap', 'SHRINKWRAP'); m.target = target
    m.wrap_method = 'NEAREST_SURFACEPOINT'; m.wrap_mode = 'OUTSIDE_SURFACE'; m.offset = offset
    apply_all(o); copy_weights(o, target)

def bone_span(rig, name):
    b = rig.data.bones[name]
    return b.head_local.copy(), b.tail_local.copy()

def dominant(o, v):
    best = max(v.groups, key=lambda g: g.weight, default=None)
    return o.vertex_groups[best.group].name if best else ''

def extras(spec, kit, rig, roles):
    for g in spec.get('garments', []):
        kind = g['kind']
        if kind in ('puffer', 'windbreaker', 'coat'):
            roles['outer'] = shell_garment(kit, rig, roles, g)
        elif kind in ('knit_hat', 'fur_hat'):
            roles['hat'] = knit_hat(kit, rig, roles, g); roles['hat']['snug'] = True
        elif kind == 'scarf':
            scarf(kit, rig, roles, g)
    if 'hat' in roles and 'hair' in roles:
        trim_hair(roles['hair'], roles['hat'], roles['hat'].get('snug', False))
    if 'outer' in roles and 'hair' in roles:
        # A braid or ponytail would poke through the jacket's back: keep hair above the collar only.
        import bmesh
        collar = max(v.co.z for v in roles['outer'].data.vertices) - .01
        bm = bmesh.new(); bm.from_mesh(roles['hair'].data)
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.calc_center_median().z < collar], context='FACES')
        bm.to_mesh(roles['hair'].data); bm.free()
    if spec.get('backpack'):
        backpack(spec, kit, rig, outermost(roles))
    if spec.get('police'):
        police_kit(kit, rig, roles, cap='hat' not in roles)
    for role in ('tights', 'skirt'):
        o = roles.pop(role); kit.parts.remove(o); bpy.data.objects.remove(o)
    for o in kit.parts:
        if o.get('thickness'):
            m = o.modifiers.new('Thickness', 'SOLIDIFY'); m.thickness = o['thickness']; m.offset = -1; apply_all(o)

def outermost(roles):
    return roles.get('outer') or roles.get('suit') or roles.get('top')

def backpack(spec, kit, rig, suit):
    chest, neck = bone_span(rig, 'Chest')
    hips = bone_span(rig, 'Hips')[0]
    H = bone_span(rig, 'Head')[1].z
    top = neck.z - .03*H; bottom = hips.z + .1*H if spec['build'] == 'child' else chest.z - .08*H
    back = max(v.co.y for v in suit.data.vertices if bottom < v.co.z < top and abs(v.co.x) < .08)
    height = top - bottom; depth = .075*H; width = .17*H
    kit.flat('Pack', spec.get('pack', (.05, .18, .45) if spec['build'] == 'child' else (.04, .045, .05)), .6)
    kit.flat('PackTrim', (.02, .02, .025), .7)
    z = (top + bottom)/2
    box(kit, 'Backpack', (0, back + depth/2 - .004, z), (width, depth, height), 'Pack', 'Chest', bevel=.03)
    box(kit, 'Pack pocket', (0, back + depth + .012*H, z - .15*height), (width*.75, .03*H, height*.4),
        'Pack', 'Chest', bevel=.012)
    for s in (1, -1):
        # Shoulder strap: over the shoulder from the top of the pack down the chest, laid onto the clothes.
        x = s*.07*H
        pts = [Vector((x, back, top)), Vector((x, 0, neck.z + .01*H)), Vector((x, -.2, chest.z + .02*H)),
               Vector((x, -.2, bottom + .02*H))]
        n = 24; path = []
        for i in range(n+1):
            t = i/n*3; k = min(int(t), 2); u = t - k
            path.append(pts[k].lerp(pts[k+1], u))
        vs = []; fs = []
        for i, p in enumerate(path):
            vs += [p - Vector((.012*H, 0, 0)), p + Vector((.012*H, 0, 0))]
            if i: fs.append((2*i-2, 2*i-1, 2*i+1, 2*i))
        me = bpy.data.meshes.new('Strap'); me.from_pydata(vs, [], fs); me.update()
        o = bpy.data.objects.new('Strap', me); bpy.context.collection.objects.link(o)
        finish(kit, o, 'PackTrim')
        wrap(o, suit, .004)
        m = o.modifiers.new('Thickness', 'SOLIDIFY'); m.thickness = .005; m.offset = 1; apply_all(o)

def police_kit(kit, rig, roles, cap=True):
    suit = outermost(roles); body = roles['body']
    head0, head1 = bone_span(rig, 'Head'); H = head1.z
    spine = bone_span(rig, 'Spine')[0]; neck = bone_span(rig, 'Neck')[0]
    kit.flat('Vest', (.50, .75, .03), .6); kit.flat('Reflect', (.70, .72, .72), .35)
    kit.flat('CapBand', (.50, .02, .02), .7); kit.flat('CapTop', (.035, .045, .08), .6)
    kit.flat('Visor', (.01, .01, .012), .25); kit.flat('Badge', (.80, .58, .12), .35)
    kit.flat('Baton', (.012, .012, .014), .4)
    # Vest: MakeHuman's smooth tights helper over the torso, pushed out over the jacket, sleeveless.
    tights = roles['tights']
    import bmesh
    bm = bmesh.new(); bm.from_mesh(tights.data)
    deform = bm.verts.layers.deform.active
    names = {g.index: g.name for g in tights.vertex_groups}
    def torso(v):
        w = {names[k]: x for k, x in v[deform].items()}
        return sum(x for n, x in w.items() if n.split('_')[0] in ('UpperArm', 'Forearm', 'Hand')) < .1
    lo = spine.z - .03*H; hi = neck.z - .04*H
    for z in (lo, hi):   # straight hem and top edge
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0, 0, z), plane_no=(0, 0, 1))
    keep = {f for f in bm.faces if lo < f.calc_center_median().z < hi and all(torso(v) for v in f.verts)}
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f not in keep], context='FACES')
    bm.normal_update()
    for v in bm.verts: v.co += v.normal * .02
    me = bpy.data.meshes.new('Vest'); bm.to_mesh(me); bm.free()
    vest = bpy.data.objects.new('Vest', me); bpy.context.collection.objects.link(vest)
    m = vest.modifiers.new('Over jacket', 'SHRINKWRAP'); m.target = suit; m.wrap_method = 'NEAREST_SURFACEPOINT'
    m.wrap_mode = 'OUTSIDE'; m.offset = .01
    m = vest.modifiers.new('Relax', 'SMOOTH'); m.factor = .6; m.iterations = 6
    apply_all(vest)
    for p in vest.data.polygons: p.use_smooth = True
    bands = (lo + .2*(hi-lo), lo + .38*(hi-lo)); half = .011*H
    bm = bmesh.new(); bm.from_mesh(vest.data)
    for b in bands:
        for d in (-half, half):
            geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
            bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0, 0, b+d), plane_no=(0, 0, 1))
    bm.to_mesh(vest.data); bm.free()
    kit.slot(vest, 'Vest'); kit.slot(vest, 'Reflect')
    for p in vest.data.polygons:
        p.material_index = 1 if any(abs(p.center.z - b) < half for b in bands) else 0
    m = vest.modifiers.new('Thickness', 'SOLIDIFY'); m.thickness = .004; m.offset = -1
    apply_all(vest); copy_weights(vest, suit); kit.parts.append(vest)
    # A vest moves with the torso only; arm weights from the jacket would tear its armholes open.
    for g in [g for g in vest.vertex_groups if g.name.split('_')[0] in ('Shoulder', 'UpperArm', 'Forearm', 'Hand')]:
        vest.vertex_groups.remove(g)
    chest = vest.vertex_groups.get('Chest') or vest.vertex_groups.new(name='Chest')
    for v in vest.data.vertices:
        total = sum(e.weight for e in v.groups)
        if total < .999:
            own = next((e.weight for e in v.groups if e.group == chest.index), 0)
            chest.add([v.index], own + 1 - total, 'REPLACE')
    back = max(v.co.y for v in vest.data.vertices if abs(v.co.x) < .05 and abs(v.co.z - (hi - .1*(hi-lo))) < .03)
    text = label(kit, 'Vest lettering', 'ДПС', (0, back + .02, hi - .16*(hi-lo)), .05*H, 'Reflect')
    wrap(text, vest, .002)
    if cap: peaked_cap(kit, rig, body)
    baton(kit, rig)

def peaked_cap(kit, rig, body):
    """Peaked cap fitted to the head: band at forehead height, wide crown, visor, badge."""
    head0, head1 = bone_span(rig, 'Head')
    hv = [v.co for v in body.data.vertices if v.co.z > head0.z + .02 and
          (max(v.groups, key=lambda g: g.weight).group if v.groups else -1) == body.vertex_groups['Head'].index]
    top = max(c.z for c in hv); zb = top - .075
    ring_pts = [c for c in hv if abs(c.z - zb) < .01]
    rx = max(abs(c.x) for c in ring_pts) + .012
    y0 = min(c.y for c in ring_pts); y1 = max(c.y for c in ring_pts); cy = (y0 + y1)/2; ry = (y1 - y0)/2 + .014
    ring(kit, 'Cap band', [(0, cy, zb - .005, rx, ry), (0, cy, zb + .045, rx + .004, ry + .004)], 'CapBand', 'Head',
         closed=True)
    ellipsoid(kit, 'Cap crown', (0, cy + .012, zb + .064), (rx*1.28, ry*1.3, .032), 'CapTop', 'Head', segments=32, rings=12)
    ellipsoid(kit, 'Cap visor', (0, cy - ry - .012, zb + .004), (rx*.82, .045, .007), 'Visor', 'Head',
              rot=(.35, 0, 0), segments=24, rings=8)
    ellipsoid(kit, 'Cap badge', (0, cy - ry - .003, zb + .026), (.011, .004, .013), 'Badge', 'Head', segments=12, rings=6)

def baton(kit, rig):
    """Striped traffic baton gripped in the right hand, hanging forward-down."""
    wr, tip = bone_span(rig, 'Hand_R')
    a = wr + (tip - wr)*.7; b = a + Vector((-.03, -.4, -.92)).normalized()*.5
    for i in range(8):
        cylinder(kit, 'Baton', a.lerp(b, i/8), a.lerp(b, (i+1)/8), .014, 'Baton' if i % 2 == 0 else 'Reflect', 'Hand_R')

# ---------------------------------------------------------------- seasonal garments
def _periodic_noise(n, scale, seed):
    """Tileable noise: random field low-passed in the frequency domain, normalised to 0..1."""
    rng = np.random.default_rng(seed)
    f = np.fft.fft2(rng.standard_normal((n, n)))
    k = np.fft.fftfreq(n)[:, None]**2 + np.fft.fftfreq(n)[None, :]**2
    h = np.real(np.fft.ifft2(f * np.exp(-k * (n/scale)**2)))
    return (h - h.min()) / (np.ptp(h) + 1e-9)

def fabric_textures(kit, kind, n=512):
    """Grey detail texture and normal map of a fabric, shared by all characters (tinted by the material)."""
    d_name, n_name = f'Fabric_{kind}_d.png', f'Fabric_{kind}_n.png'
    if not (kit.textures / d_name).exists():
        y, x = np.mgrid[0:n, 0:n] / n
        if kind == 'nylon':      # soft creases of a synthetic shell
            h = .7*_periodic_noise(n, 6, 1) + .3*_periodic_noise(n, 40, 2); tone = .88 + .12*h
        elif kind == 'wool':     # felted coat cloth: fine twill and fibre noise
            twill = .5 + .5*np.sin(2*np.pi*(x + y)*96)
            h = .35*twill + .65*_periodic_noise(n, 160, 3); tone = .86 + .14*h
        elif kind == 'knit':     # knitted ribs with rows of stitches
            rib = np.abs(np.sin(np.pi*x*32))
            stitch = .6 + .4*np.abs(np.sin(np.pi*(y*64 + np.abs((x*32) % 1 - .5))))
            h = rib*stitch; tone = .72 + .28*h
        else:                    # 'fur'
            h = .6*_periodic_noise(n, 220, 4) + .4*_periodic_noise(n, 60, 5); tone = .7 + .3*h
        gy, gx = np.gradient(h)
        strength = {'nylon': 6, 'wool': 3, 'knit': 10, 'fur': 5}[kind]
        nm = np.dstack([-gx*strength*n/64, -gy*strength*n/64, np.ones_like(h)])
        nm /= np.linalg.norm(nm, axis=2, keepdims=True)
        for name, rgb in ((d_name, np.dstack([tone]*3)), (n_name, nm*.5 + .5)):
            img = bpy.data.images.new(name, n, n)
            img.pixels[:] = np.dstack([rgb, np.ones((n, n))])[::-1].ravel()
            img.filepath_raw = str(kit.textures / name); img.file_format = 'PNG'; img.save()
            bpy.data.images.remove(img)
    return d_name, n_name

def fabric_material(kit, name, kind, color, roughness):
    d, nrm = fabric_textures(kit, kind)
    m = bpy.data.materials.new('Ped_' + name); m.use_nodes = True
    nodes = m.node_tree.nodes; bsdf = nodes.get('Principled BSDF')
    tex = nodes.new('ShaderNodeTexImage'); tex.image = bpy.data.images.load(str(kit.textures / d), check_existing=True)
    mix = nodes.new('ShaderNodeMix'); mix.data_type = 'RGBA'; mix.blend_type = 'MULTIPLY'
    mix.inputs['Factor'].default_value = 1; mix.inputs[7].default_value = (*color, 1)
    m.node_tree.links.new(tex.outputs['Color'], mix.inputs[6])
    m.node_tree.links.new(mix.outputs[2], bsdf.inputs['Base Color'])
    bsdf.inputs['Roughness'].default_value = roughness
    kit.materials[name] = m
    kit.unity[name] = {'name': 'Ped_' + name, 'diffuse': d, 'normal': nrm, 'color': list(color),
                       'smoothness': 1 - roughness, 'alphaClip': False, 'doubleSided': False}

def _object_from_bmesh(bm, name, groups_from):
    me = bpy.data.meshes.new(name); bm.to_mesh(me)
    o = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(o)
    for g in groups_from.vertex_groups: o.vertex_groups.new(name=g.name)   # same order = same deform indices
    for p in me.polygons: p.use_smooth = True
    return o

def _union(parts):
    """Temporary single mesh of several parts (a shrinkwrap target)."""
    copies = []
    for o in parts:
        c = o.copy(); c.data = o.data.copy(); c.modifiers.clear(); bpy.context.collection.objects.link(c); copies.append(c)
    activate(copies[0])
    for c in copies: c.select_set(True)
    bpy.ops.object.join()
    return bpy.context.object

def _weights(bm, groups_from):
    deform = bm.verts.layers.deform.verify()
    names = {g.index: g.name for g in groups_from.vertex_groups}
    return lambda v: {names[k]: x for k, x in v[deform].items()}

def shell_garment(kit, rig, roles, g):
    """Puffer, windbreaker or coat: MakeHuman's tights helper (plus its skirt helper below the waist for long
    garments) grown over the clothes, with straight hem, collar and cuffs cut by planes."""
    import bmesh
    kind = g['kind']; long = g.get('long', kind == 'coat')
    H = bone_span(rig, 'Head')[1].z
    hips = bone_span(rig, 'Hips')[0]; neck = bone_span(rig, 'Neck')[0]
    knee = bone_span(rig, 'LowerLeg_L')[0]
    waist = hips.z - .01*H; collar = neck.z + .03*H
    hem = knee.z + .02*H if long else hips.z - g.get('drop', .07)*H
    tights, skirt = roles['tights'], roles['skirt']
    bm = bmesh.new(); bm.from_mesh(tights.data)
    top = waist + .06*H           # torso part ends and the skirt part starts at this ring
    cuts = [((0, 0, collar), (0, 0, 1)), ((0, 0, top), (0, 0, 1))]
    wrists = []
    for s in 'LR':
        el, wr = bone_span(rig, 'Forearm_' + s); d = (wr - el).normalized()
        wrists.append((wr - d*.015*H, d)); cuts.append((tuple(wr - d*.015*H), tuple(d)))
    for co, no in cuts:
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=co, plane_no=no)
    w = _weights(bm, tights)
    arm = lambda v: sum(x for n, x in w(v).items() if n.split('_')[0] in ('Shoulder', 'UpperArm', 'Forearm', 'Hand'))
    def keep(f):
        c = f.calc_center_median()
        if c.z > collar or any((c - p).dot(d) > 0 for p, d in wrists): return False
        # Sleeves hang below the ring and are kept down to the wrist.
        return c.z > top or all(arm(v) > .5 for v in f.verts)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if not keep(f)], context='FACES')
    # Below that ring: the skirt helper (a tube round both legs), scaled to the torso part's ring so that
    # the two parts meet edge to edge and read as one garment.
    band = [v.co for v in bm.verts if abs(v.co.z - top) < .004 and arm(v) < .1]   # torso only, not the hanging arms
    tx = max(abs(c.x) for c in band); ty0 = min(c.y for c in band); ty1 = max(c.y for c in band)
    sk = bmesh.new(); sk.from_mesh(skirt.data)
    for z in (top + .004, hem):
        geom = sk.verts[:] + sk.edges[:] + sk.faces[:]
        bmesh.ops.bisect_plane(sk, geom=geom, plane_co=(0, 0, z), plane_no=(0, 0, 1))
    bmesh.ops.delete(sk, geom=[f for f in sk.faces if not (hem < f.calc_center_median().z < top + .004)], context='FACES')
    ring = [v.co for v in sk.verts if abs(v.co.z - top - .004) < .003] or [v.co for v in sk.verts]
    sx = max(abs(c.x) for c in ring); sy0 = min(c.y for c in ring); sy1 = max(c.y for c in ring)
    for v in sk.verts:
        u = max(0., min(1., (top - v.co.z) / max(.01, top - hem)))
        flare = 1 + (.08 if long else .02)*u
        v.co.x *= tx/sx * flare
        v.co.y = (ty0 + ty1)/2 + (v.co.y - (sy0 + sy1)/2) * (ty1 - ty0)/(sy1 - sy0) * flare
    tmp = bpy.data.meshes.new('tmp'); sk.to_mesh(tmp); sk.free(); bm.from_mesh(tmp); bpy.data.meshes.remove(tmp)
    # Sew the two parts into one surface: bridge the torso's lower edge loop to the skirt's upper one.
    near = lambda e: all(abs(v.co.z - top) < .006 for v in e.verts) and e.is_boundary and \
                     all(arm(v) < .5 for v in e.verts)
    seam = [e for e in bm.edges if near(e)]
    try:
        bmesh.ops.bridge_loops(bm, edges=seam)
    except Exception as err:
        print('garment seam not bridged:', err)
    bm.normal_update()
    for v in bm.verts: v.co += v.normal * g.get('base', .02)
    uv = bm.loops.layers.uv.active
    if uv:
        for f in bm.faces:
            for l in f.loops: l[uv].uv *= 6
    o = _object_from_bmesh(bm, g['name'], tights); bm.free()
    budget(o, g.get('tris', 2600))
    target = _union([p for r, p in roles.items() if r in ('body', 'suit', 'top', 'pants')])
    # Smooth first, then push everything that is still inside the clothes back out.
    m = o.modifiers.new('Relax', 'SMOOTH'); m.factor = .5; m.iterations = 4
    m = o.modifiers.new('Over clothes', 'SHRINKWRAP'); m.target = target
    m.wrap_method = 'NEAREST_SURFACEPOINT'; m.wrap_mode = 'OUTSIDE'; m.offset = g.get('margin', .012)
    apply_all(o)
    bpy.data.objects.remove(target)
    if g.get('quilt'):
        # Horizontal quilting of a down jacket.
        bm = bmesh.new(); bm.from_mesh(o.data); bm.normal_update()
        period = g.get('period', .1)*H/1.75
        for v in bm.verts:
            v.co += v.normal * g['quilt'] * (.5 - .5*math.cos(2*math.pi*(v.co.z - hem)/period))
        bm.to_mesh(o.data); bm.free()
    fabric_material(kit, g['name'], {'coat': 'wool'}.get(kind, 'nylon'), g['color'], g.get('roughness', .55))
    o.data.materials.append(kit.materials[g['name']])
    o['thickness'] = .006          # applied last, after vest, scarf and backpack were laid onto it
    if long:
        # Buttons down the front of a coat.
        kit.flat(g['name'] + 'Button', (.02, .018, .016), .4)
        front = min(v.co.y for v in o.data.vertices if abs(v.co.x) < .02 and abs(v.co.z - hem) < .03)
        for i in range(4):
            z = hem + (collar - hem)*(.12 + .2*i)
            ys = [v.co.y for v in o.data.vertices if abs(v.co.x) < .02 and abs(v.co.z - z) < .02]
            if ys: ellipsoid(kit, 'Button', (0, min(ys) - .004, z), (.009, .004, .009), g['name'] + 'Button', 'Chest',
                             segments=10, rings=6)
    kit.parts.append(o)
    return o

def budget(o, tris):
    o.data.calc_loop_triangles(); n = len(o.data.loop_triangles)
    if n > tris:
        m = o.modifiers.new('Budget', 'DECIMATE'); m.ratio = tris/n; apply_all(o)

def knit_hat(kit, rig, roles, g):
    """Hat following the skull: knitted with a turned-up cuff (optional pompom) or a fur hat."""
    import bmesh
    fur = g['kind'] == 'fur_hat'
    body = roles['body']
    eyes = [v.co for v in roles['eyes'].data.vertices]
    eye_z = sum(c.z for c in eyes)/len(eyes); eye_y = min(c.y for c in eyes)
    lift = .05 if fur else .03
    def cut(y): return eye_z + lift - .3*max(0., y - eye_y)
    bm = bmesh.new(); bm.from_mesh(body.data)
    w = _weights(bm, body)
    def head(v): return w(v).get('Head', 0) > .5
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if not (all(head(v) for v in f.verts)
                     and f.calc_center_median().z > cut(f.calc_center_median().y))], context='FACES')
    for v in bm.verts:
        if v.is_boundary: v.co.z = cut(v.co.y)
    bm.normal_update()
    top = max(v.co.z for v in bm.verts)
    for v in bm.verts:
        u = (v.co.z - cut(v.co.y)) / max(.01, top - cut(v.co.y))
        if fur:
            off = .028 + .006*u
        else:
            off = .011 + g.get('slouch', .012)*u*u + (.006 if v.co.z - cut(v.co.y) < .045 else 0)   # cuff fold
        v.co += v.normal * off
    uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        for l in f.loops:
            c = l.vert.co
            l[uv].uv = (math.atan2(c.x, -c.y)/math.tau*3 + .5, (c.z - eye_z)*8)
    o = _object_from_bmesh(bm, g['name'], body); bm.free()
    budget(o, 900)
    m = o.modifiers.new('Relax', 'SMOOTH'); m.factor = .5; m.iterations = 3
    m = o.modifiers.new('Thickness', 'SOLIDIFY'); m.thickness = .005; m.offset = -1
    apply_all(o)
    fabric_material(kit, g['name'], 'fur' if fur else 'knit', g['color'], .9)
    o.data.materials.append(kit.materials[g['name']])
    kit.parts.append(o)
    if g.get('pompom'):
        kit.flat(g['name'] + 'Pompom', g.get('pompom_color', g['color']), .95)
        c = max(o.data.vertices, key=lambda v: v.co.z).co
        ellipsoid(kit, 'Pompom', (c.x, c.y, c.z + .03), (.04, .04, .035), g['name'] + 'Pompom', 'Head',
                  segments=16, rings=10)
    if fur and g.get('badge'):
        kit.flat('HatBadge', (.80, .58, .12), .35)
        front = min((v.co for v in o.data.vertices if abs(v.co.x) < .015), key=lambda c: c.y - c.z*.2)
        ellipsoid(kit, 'Hat badge', (0, front.y - .004, front.z), (.012, .004, .014), 'HatBadge', 'Head',
                  segments=12, rings=6)
    return o

def scarf(kit, rig, roles, g):
    """Knitted scarf: a loop round the neck and one end hanging down the front."""
    body = roles['body']
    neck = [v.co for v in body.data.vertices
            if v.groups and body.vertex_groups[max(v.groups, key=lambda e: e.weight).group].name == 'Neck']
    cx = sum(c.x for c in neck)/len(neck); cy = sum(c.y for c in neck)/len(neck)
    rx = max(abs(c.x - cx) for c in neck); ry = max(abs(c.y - cy) for c in neck)
    z0 = min(c.z for c in neck) + .005; z1 = z0 + .065
    fabric_material(kit, g['name'], 'knit', g['color'], .9)
    loop = ring(kit, 'Scarf', [(cx, cy, z0 - .01, rx + .03, ry + .035), (cx, cy, z0 + .02, rx + .042, ry + .045),
                               (cx, cy, z1 - .015, rx + .038, ry + .04), (cx, cy, z1, rx + .022, ry + .026)],
                g['name'], None, closed=True)
    uv = loop.data.uv_layers.new(name='UVMap')
    for p in loop.data.polygons:
        for li in p.loop_indices:
            c = loop.data.vertices[loop.data.loops[li].vertex_index].co
            uv.data[li].uv = (math.atan2(c.x - cx, -(c.y - cy))/math.tau*4, (c.z - z0)*10)
    copy_weights(loop, body)
    outer = outermost(roles)
    x = cx + .035
    pts = [Vector((x, cy - ry - .06, z0 - i*.03)) for i in range(9)]
    vs = []; fs = []
    for i, p in enumerate(pts):
        vs += [p - Vector((.04, 0, 0)), p + Vector((.04, 0, 0))]
        if i: fs.append((2*i-2, 2*i-1, 2*i+1, 2*i))
    me = bpy.data.meshes.new('Scarf end'); me.from_pydata(vs, [], fs); me.update()
    end = bpy.data.objects.new('Scarf end', me); bpy.context.collection.objects.link(end)
    uv = me.uv_layers.new(name='UVMap')
    for p in me.polygons:
        for li in p.loop_indices:
            c = me.vertices[me.loops[li].vertex_index].co; uv.data[li].uv = ((c.x - x)*12, c.z*10)
    finish(kit, end, g['name'])
    wrap(end, outer, .012)
    m = end.modifiers.new('Thickness', 'SOLIDIFY'); m.thickness = .008; m.offset = 1; apply_all(end)

def trim_hair(hair, hat, snug=True):
    """Removes hair cards that would poke through the hat; hair below its rim (nape, ponytail) stays."""
    import bmesh
    zs = [v.co.z for v in hat.data.vertices]
    rim = min(zs)
    xs = [v.co.x for v in hat.data.vertices]; ys = [v.co.y for v in hat.data.vertices]
    cx, cy = (min(xs) + max(xs))/2, (min(ys) + max(ys))/2
    rx, ry = (max(xs) - min(xs))/2 + .01, (max(ys) - min(ys))/2 + .01
    bm = bmesh.new(); bm.from_mesh(hair.data)
    def inside(c):
        e = ((c.x - cx)/rx)**2 + ((c.y - cy)/ry)**2
        if not snug:      # a ready-made cap sits over the hair: only the crown under it goes
            return c.z > rim + .02 and e < .9
        return (c.z > rim + .01 and e < 1.3) or (c.z > rim - .03 and e < 1.05)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if inside(f.calc_center_median())], context='FACES')
    bm.to_mesh(hair.data); bm.free()
