"""Review scenes for the EN kit: collection instances of the exported assets, rendered with Cycles.

Each area is its own Blender scene. Every shot can be rendered in three lightings
(docs/art-pipeline.md, acceptance item 5): day (sun + sky), dusk (sun at the horizon, lamps
and headlights on) and night (moonlight, street lamps, car headlights). Context models
(project cars, a house) are imported only for the renders and are not part of the kit.
Positions are in the author frame (X right, Y forward, Z up) and converted.
"""
import bpy, math, random, re
from pathlib import Path
from mathutils import Vector, Matrix
from mathutils.kdtree import KDTree

from .core import to_blender, MB, mesh_object
from . import road

AREAS = {'trees': (0, 0), 'forest': (0, 1000), 'road': (1000, 0), 'barrier': (1000, 1000),
         'yard': (-1000, 0), 'parking': (-1000, 1000), 'props': (0, -1000)}
ROOT = Path(__file__).resolve().parents[2]


class Scene:
    def __init__(self, assets, M):
        self.assets, self.M = assets, M
        self.scenes = {}
        self.lights = {}          # area -> collection of artificial lights (on at dusk and night)
        self.n = 0

    def area(self, area):
        if area not in self.scenes:
            sc = bpy.data.scenes.new('Review_' + area)
            sc.unit_settings.system, sc.unit_settings.scale_length = 'METRIC', 1.0
            lc = bpy.data.collections.new('Lights_' + area)
            sc.collection.children.link(lc)
            self.scenes[area], self.lights[area] = sc, lc
        return self.scenes[area]

    def has(self, *ids):
        return all(i in self.assets for i in ids)

    def W(self, area, p):
        ox, oy = AREAS[area]
        return Vector(to_blender((p[0] + ox, p[1] + oy, p[2] if len(p) > 2 else 0.0)))

    def put_coll(self, coll, area, loc, yaw=0.0, s=1.0, name='I'):
        e = bpy.data.objects.new(f'{name}_{self.n}', None)
        self.n += 1
        e.instance_type = 'COLLECTION'
        e.instance_collection = coll
        e.location = self.W(area, loc)
        e.rotation_euler = (0, 0, yaw)
        e.scale = (s, s, s)
        self.area(area).collection.objects.link(e)
        return e

    def put(self, aid, area, loc, yaw=0.0, s=1.0):
        if aid not in self.assets:
            return None
        return self.put_coll(self.assets[aid].coll, area, loc, yaw, s, 'I_' + aid)

    def ground(self, area, size=1200, mat='EN_Grass', z=-0.03, hole=None, window=None, cell=1.0):
        """Flat ground. With hole(x, y) -> True (area coordinates) and a window (x0, x1, y0, y1), the
        window is a grid of cells and cells lying wholly inside the hole are left out: road modules carry
        their own shoulders, ditches and verges, a ground plane over them would hide the shoulders."""
        ox, oy = AREAS[area]
        mb = MB()
        if hole is None:
            mb.ngon([(ox - size, oy - size, z), (ox + size, oy - size, z), (ox + size, oy + size, z),
                     (ox - size, oy + size, z)], mat)
        else:
            x0, x1, y0, y1 = window
            nx, ny = int(math.ceil((x1 - x0) / cell)), int(math.ceil((y1 - y0) / cell))
            xs = [x0 + i * cell for i in range(nx + 1)]
            ys = [y0 + j * cell for j in range(ny + 1)]
            inside = [[hole(x, y) for y in ys] for x in xs]
            verts = [(ox + x, oy + y, z) for x in xs for y in ys]
            faces = []
            for i in range(nx):
                for j in range(ny):
                    if inside[i][j] and inside[i + 1][j] and inside[i][j + 1] and inside[i + 1][j + 1]:
                        continue
                    a_, b_ = i * (ny + 1) + j, (i + 1) * (ny + 1) + j
                    faces.append((a_, b_, b_ + 1, a_ + 1))
            mb.add(verts, faces, mat)
            X0, X1, Y0, Y1 = x0 + ox, xs[-1] + ox, y0 + oy, ys[-1] + oy
            L, R_, B, T = ox - size, ox + size, oy - size, oy + size
            for q in (((L, B), (R_, B), (R_, Y0), (L, Y0)), ((L, Y1), (R_, Y1), (R_, T), (L, T)),
                      ((L, Y0), (X0, Y0), (X0, Y1), (L, Y1)), ((X1, Y0), (R_, Y0), (R_, Y1), (X1, Y1))):
                mb.ngon([(x, y, z) for x, y in q], mat)
        mesh_object(f'Ground_{area}', mb, self.M, coll=self.area(area).collection)

    def strip(self, area, pts, w, mat='EN_Asphalt'):
        ox, oy = AREAS[area]
        mb = MB()
        fr = []
        for i, p in enumerate(pts):
            q = (Vector(pts[min(i + 1, len(pts) - 1)]) - Vector(pts[max(i - 1, 0)])).normalized()
            fr.append((Vector((p[0] + ox, p[1] + oy, -0.004)), Vector((q.y, -q.x, 0)), Vector((0, 0, 1))))
        mb.sweep(fr, [(-w / 2, 0), (w / 2, 0)], mat)
        mesh_object(f'Strip_{area}_{self.n}', mb, self.M, coll=self.area(area).collection)
        self.n += 1

    # ---- artificial lights (only rendered at dusk and night)
    def spot(self, area, pos, direction, energy, size_deg=60, color=(1, 0.95, 0.85), blend=0.35, radius=0.05):
        L = bpy.data.lights.new(f'Spot_{self.n}', 'SPOT')
        L.energy, L.spot_size, L.spot_blend, L.color = energy, math.radians(size_deg), blend, color
        L.shadow_soft_size = radius
        o = bpy.data.objects.new(f'Spot_{self.n}', L)
        self.n += 1
        o.location = self.W(area, pos)
        d = Vector(to_blender(direction)).normalized()
        o.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
        self.area(area)
        self.lights[area].objects.link(o)
        return o

    def point(self, area, pos, energy, color=(1, 0.2, 0.1), radius=0.03):
        L = bpy.data.lights.new(f'Pt_{self.n}', 'POINT')
        L.energy, L.color, L.shadow_soft_size = energy, color, radius
        o = bpy.data.objects.new(f'Pt_{self.n}', L)
        self.n += 1
        o.location = self.W(area, pos)
        self.area(area)
        self.lights[area].objects.link(o)

    def street_lamp(self, area, base, yaw):
        """Light of EN_Yard_Lamp placed at base with yaw: LED head 1.25 m in front, 6.1 m up."""
        h = Vector((-math.sin(yaw), math.cos(yaw), 0)) * 1.25
        p = (base[0] + h.x, base[1] + h.y, 6.05)
        self.spot(area, p, (0, 0, -1), 1400, 130, (1.0, 0.82, 0.6), 0.6, 0.15)

    def headlights(self, area, pos, heading, low=True):
        """Dipped headlights of a car at pos (front axle area) heading along the 2D vector."""
        f = Vector((heading[0], heading[1], 0)).normalized()
        r = Vector((f.y, -f.x, 0))
        pos = Vector((pos[0], pos[1], pos[2] if len(pos) > 2 else 0.0))
        for sgn in (-1, 1):
            p = pos + f * 2.0 + r * sgn * 0.62 + Vector((0, 0, 0.68))
            d = f + Vector((0, 0, -0.06 if low else -0.02)) + r * sgn * 0.05
            self.spot(area, tuple(p), tuple(d), 900, 48, (1.0, 0.95, 0.88), 0.45, 0.06)
            t = pos - f * 2.2 + r * sgn * 0.65 + Vector((0, 0, 0.8))
            self.point(area, tuple(t), 1.5)


def scatter(S, area, region, density, seed, mix, min_d=3.0, avoid=None, height=None, scale=(0.85, 1.15)):
    """Random instances; avoid(x, y) -> True to skip; height(x, y) -> ground z."""
    R = random.Random(seed)
    x0, x1, y0, y1 = region
    n = int((x1 - x0) * (y1 - y0) * density)
    pool = [a for a, w in mix for _ in range(w) if a in S.assets]
    if not pool:
        return
    grid = {}
    placed = tries = 0
    cell = max(min_d, 0.5)
    while placed < n and tries < n * 6:
        tries += 1
        x, y = R.uniform(x0, x1), R.uniform(y0, y1)
        gx, gy = int(x // cell), int(y // cell)
        near = [p for i in (-1, 0, 1) for j in (-1, 0, 1) for p in grid.get((gx + i, gy + j), ())]
        if any((x - px) ** 2 + (y - py) ** 2 < min_d ** 2 for px, py in near):
            continue
        if avoid and avoid(x, y):
            continue
        grid.setdefault((gx, gy), []).append((x, y))
        placed += 1
        z = height(x, y) if height else 0.0
        S.put(R.choice(pool), area, (x, y, z), R.uniform(0, 6.28), R.uniform(*scale))


FOREST_MIX = [('EN_Tree_Spruce_A', 4), ('EN_Tree_Spruce_B', 3), ('EN_Tree_Birch_A', 2), ('EN_Tree_Birch_B', 2),
              ('EN_Tree_Pine_A', 2), ('EN_Tree_Pine_B', 2), ('EN_Tree_Spruce_Young', 2), ('EN_Bush_A', 1),
              ('EN_Bush_B', 1)]
UNDER_MIX = [('EN_Grass_Clump_A', 16), ('EN_Grass_Clump_B', 16), ('EN_Rock_B', 1), ('EN_Stump', 1),
             ('EN_Log_Fallen', 1), ('EN_Tree_Spruce_Young', 1)]


# ---------------------------------------------------------------- context models
_IMPORTED = {}


def import_fbx(path, name, fix_front=False, auto_rotate=True, smooth=True):
    """Imports a project FBX (cars, a house) for context, LOD0 only."""
    if name in _IMPORTED:
        return _IMPORTED[name]
    if not Path(path).exists():
        print('CONTEXT_MISSING', path)
        return None
    tmp = bpy.data.scenes.new('tmp_import')
    before = set(bpy.data.objects)
    with bpy.context.temp_override(scene=tmp, view_layer=tmp.view_layers[0]):
        bpy.ops.import_scene.fbx(filepath=str(path), use_custom_normals=False)
    new = [o for o in bpy.data.objects if o not in before]
    # keep LOD0 only: LOD1/LOD2 overlap it and z-fight (that was the 'broken lighting' on the house)
    for o in [o for o in new if re.search(r'_LOD[1-9]$', o.name)]:
        new.remove(o)
        bpy.data.objects.remove(o, do_unlink=True)
    coll = bpy.data.collections.new('CTX_' + name)
    for o in new:
        for c in list(o.users_collection):
            c.objects.unlink(o)
        coll.objects.link(o)
        if o.type == 'MESH':
            for p in o.data.polygons:
                p.use_smooth = smooth
            if smooth:
                o.data.set_sharp_from_angle(angle=math.radians(35))
    bpy.data.scenes.remove(tmp)
    root = bpy.data.objects.new('CTXROOT_' + name, None)
    coll.objects.link(root)
    for o in new:
        if o.parent is None:
            o.parent = root
    bpy.context.view_layer.update()

    def bounds():
        lo, hi = [1e9] * 3, [-1e9] * 3
        for o in new:
            if o.type != 'MESH':
                continue
            for c in o.bound_box:
                w = o.matrix_world @ Vector(c)
                lo = [min(a, b) for a, b in zip(lo, w)]
                hi = [max(a, b) for a, b in zip(hi, w)]
        return lo, hi
    lo, hi = bounds()
    yaw = math.pi / 2 if auto_rotate and (hi[0] - lo[0]) > (hi[1] - lo[1]) else 0.0
    # mesh origins of the car parts sit at the car origin, so use the lamp geometry, not the origin
    heads = [sum((o.matrix_world @ Vector(c) for c in o.bound_box), Vector()) / 8
             for o in new if o.type == 'MESH' and o.name.startswith('Headlight_') and 'Housing' not in o.name]
    if fix_front and heads:
        h = sum(heads, Vector()) / len(heads)
        c = Vector(((lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, 0))
        if (Matrix.Rotation(yaw, 3, 'Z') @ (h - c)).y > 0:        # front must face Blender -Y
            yaw += math.pi
    root.rotation_euler = (0, 0, yaw)
    bpy.context.view_layer.update()
    lo, hi = bounds()
    root.location = (-(lo[0] + hi[0]) / 2, -(lo[1] + hi[1]) / 2, -lo[2])
    print('CONTEXT', name, [round(b - a, 2) for a, b in zip(lo, hi)])

    class Ctx:
        pass
    c = Ctx()
    c.coll = coll
    _IMPORTED[name] = c
    return c


def cars():
    out = []
    for n in ('DS_Sedan_A_Traffic', 'DS_Crossover_A_Traffic', 'DS_Police_A_Traffic'):
        c = import_fbx(ROOT / f'Assets/DrivingSchool/Art/Vehicles/{n}.fbx', n, fix_front=True)
        if c:
            out.append(c)
    return out


def house(name='DS_House_Brick5'):
    return import_fbx(ROOT / f'Assets/DrivingSchool/Art/Houses/{name}.fbx', name, auto_rotate=False)


def put_car(S, area, cs, i, pos, heading, lights=False):
    """Car (front = author +Y) at pos, heading along a 2D vector."""
    if not cs:
        return
    yaw = math.atan2(-heading[0], heading[1])
    S.put_coll(cs[i % len(cs)].coll, area, pos, yaw, 1.0, 'CAR')
    if lights:
        S.headlights(area, pos, heading)


# ---------------------------------------------------------------- lighting presets
def gpu():
    dev = 'CPU'
    try:
        prefs = bpy.context.preferences.addons['cycles'].preferences
        for t in ('OPTIX', 'CUDA', 'HIP', 'ONEAPI'):
            try:
                prefs.compute_device_type = t
            except Exception:
                continue
            prefs.get_devices()
            if [d for d in prefs.devices if d.type == t]:
                for d in prefs.devices:
                    d.use = d.type == t
                dev = t
                break
    except Exception as ex:
        print('GPU setup failed:', ex)
    print('RENDER_DEVICE', dev, flush=True)
    return dev


PRESETS = {
    #           sun elev, sun energy, sun colour,        sky str, exposure, lights
    'day': (38.0, 4.2, (1.0, 0.96, 0.9), 0.22, 0.0, False),
    'dusk': (1.2, 1.2, (1.0, 0.55, 0.3), 0.12, 1.2, True),
    'night': (35.0, 0.06, (0.62, 0.72, 1.0), 0.0, 2.6, True),
}


def world(preset):
    w = bpy.data.worlds.get('EN_World') or bpy.data.worlds.new('EN_World')
    w.use_nodes = True
    nt = w.node_tree
    bg = nt.nodes['Background']
    sky = nt.nodes.get('Sky Texture')
    if sky is None:
        try:
            sky = nt.nodes.new('ShaderNodeTexSky')
            for t in ('MULTIPLE_SCATTERING', 'NISHITA', 'SINGLE_SCATTERING'):
                try:
                    sky.sky_type = t
                    break
                except Exception:
                    continue
            sky.sun_disc = False
            nt.links.new(sky.outputs[0], bg.inputs[0])
        except Exception as ex:
            print('sky failed', ex)
            sky = None
    elev, _, _, strength, _, _ = PRESETS[preset]
    if preset == 'night':
        for l in list(bg.inputs[0].links):
            nt.links.remove(l)
        bg.inputs[0].default_value = (0.004, 0.006, 0.014, 1)
        bg.inputs[1].default_value = 1.0
    else:
        if sky is not None:
            if not bg.inputs[0].links:
                nt.links.new(sky.outputs[0], bg.inputs[0])
            sky.sun_elevation = math.radians(elev)
            sky.sun_rotation = math.radians(150)
        bg.inputs[1].default_value = strength
    return w


def setup(sc, fast, dev):
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'GPU' if dev != 'CPU' else 'CPU'
    sc.cycles.samples = 32 if fast else 128
    sc.cycles.use_denoising = True
    sc.cycles.max_bounces = 8
    sc.cycles.transparent_max_bounces = 32       # stacked foliage cards
    sc.render.resolution_x, sc.render.resolution_y = (960, 540) if fast else (1600, 900)
    sc.view_settings.view_transform = 'AgX'
    try:
        sc.view_settings.look = 'AgX - Medium High Contrast'
    except Exception:
        pass
    sun = bpy.data.lights.new('Sun_' + sc.name, 'SUN')
    o = bpy.data.objects.new('Sun_' + sc.name, sun)
    sc.collection.objects.link(o)
    sc['sun'] = o.name


def apply_preset(S, area, preset):
    sc = S.area(area)
    elev, energy, color, _, exposure, lights = PRESETS[preset]
    sc.world = world(preset)
    sun = bpy.data.objects[sc['sun']]
    sun.data.energy, sun.data.color = energy, color
    sun.data.angle = math.radians(0.6 if preset == 'night' else 1.2)
    sun.rotation_euler = (math.radians(90 - elev), 0, math.radians(150 if preset != 'night' else 220))
    sc.view_settings.exposure = exposure
    S.lights[area].hide_render = not lights


def shot(S, out, name, area, pos, target, lens=35, preset='day'):
    sc = S.area(area)
    apply_preset(S, area, preset)
    p = S.W(area, pos)
    t = S.W(area, target)
    cam = bpy.data.cameras.new(name)
    ob = bpy.data.objects.new('CAM_' + name, cam)
    sc.collection.objects.link(ob)
    ob.location = p
    ob.rotation_euler = (t - p).to_track_quat('-Z', 'Y').to_euler()
    cam.lens = lens
    cam.clip_start, cam.clip_end = 0.05, 1500
    sc.camera = ob
    sc.render.filepath = str(out / f'{name}.png')
    bpy.ops.render.render(write_still=True, scene=sc.name)
    print('RENDER', name, preset, flush=True)


# ---------------------------------------------------------------- scenes
def scene_trees(S):
    A = 'trees'
    S.ground(A)
    ids = ['EN_Tree_Birch_A', 'EN_Tree_Birch_B', 'EN_Tree_Linden_A', 'EN_Tree_Pine_A', 'EN_Tree_Pine_B',
           'EN_Tree_Spruce_A', 'EN_Tree_Spruce_B', 'EN_Tree_Spruce_Young', 'EN_Bush_A', 'EN_Bush_B']
    xs = [-27, -18, -8, 2, 10, 19, 27, 33, 37, 41]
    for aid, x in zip(ids, xs):
        S.put(aid, A, (x, 0))
    for aid, x in (('EN_Stump', 34), ('EN_Log_Fallen', 38), ('EN_Rock_A', 44), ('EN_Rock_B', 46)):
        S.put(aid, A, (x, -5))
    scatter(S, A, (-35, 50, -14, 6), 0.35, 3, [('EN_Grass_Clump_A', 1), ('EN_Grass_Clump_B', 1)], min_d=0.6)
    return [('trees-lineup', A, (7, -64, 7), (7, 0, 8), 30, 'day'),
            ('trees-lineup-dusk', A, (7, -64, 7), (7, 0, 8), 30, 'dusk'),
            ('trees-birch', A, (-20, -13, 2.0), (-23, 0, 7.5), 30, 'day'),
            ('trees-spruce-pine', A, (17, -18, 2.5), (13, 0, 8.0), 30, 'day'),
            ('trees-linden-closeup', A, (-6, -9, 1.8), (-8, 0, 5.5), 30, 'day')]


def scene_forest(S):
    A = 'forest'
    S.ground(A)
    scatter(S, A, (-70, 70, 6, 100), 0.05, 1, FOREST_MIX)
    scatter(S, A, (-70, 70, -10, 100), 0.4, 2, UNDER_MIX, min_d=0.7)
    for i in range(10):
        S.put('EN_Bush_A' if i % 2 else 'EN_Bush_B', A, (-45 + i * 10, 5 + (i % 3)), i)
    return [('forest-edge', A, (6, -38, 3.0), (0, 40, 8), 28, 'day'),
            ('forest-edge-night', A, (6, -38, 3.0), (0, 40, 8), 28, 'night')]


class Chain:
    """Road modules end to end; world(s, lat, h) gives a surface point along the whole chain."""

    def __init__(self, S, area):
        self.S, self.area = S, area
        self.p, self.h = Vector((0, 0, 0)), 0.0
        self.mods = []
        self.s = 0.0

    def add(self, aid, path, furniture=None):
        self.S.put(aid, self.area, tuple(self.p), self.h)
        if furniture:
            self.S.put(furniture, self.area, tuple(self.p), self.h)
        self.mods.append((self.s, self.p.copy(), self.h, path))
        end, _, _, _ = path.at(path.L)
        self.p = self.p + Matrix.Rotation(self.h, 3, 'Z') @ end
        self.h += path.end_yaw()
        self.s += path.L
        return self.s - path.L

    def _mod(self, s):
        for s0, p0, h0, path in reversed(self.mods):
            if s >= s0 or s0 == 0:
                return s0, p0, h0, path
        return self.mods[0]

    def world(self, s, lat=0.0, h=0.0):
        s0, p0, h0, path = self._mod(s)
        return p0 + Matrix.Rotation(h0, 3, 'Z') @ path.point(s - s0, lat, h)

    def frame(self, s):
        """Forward and right unit vectors (author frame), by finite difference along the chain."""
        a, b = self.world(max(0.0, s - 0.25)), self.world(min(self.s, s + 0.25))
        t = Vector((b.x - a.x, b.y - a.y, 0)).normalized()
        return t, Vector((t.y, -t.x, 0))

    def samples(self, step=2.0):
        out = []
        s = 0.0
        while s <= self.s:
            out.append(self.world(s))
            s += step
        return out


def yaw_of(v):
    return math.atan2(-v[0], v[1])


def scene_road(S):
    if not S.has('EN_Road_Country2_Curve90_R50'):
        return []
    A = 'road'
    ch = Chain(S, A)
    ch.p = Vector((0, -96, 0))
    for aid, path in (('EN_Road_Country2_24m', road.P_STRAIGHT), ('EN_Road_Country2_24m', road.P_STRAIGHT),
                      ('EN_Road_Country2_24m_Approach', road.P_STRAIGHT),
                      ('EN_Road_Country2_24m_Approach', road.P_STRAIGHT)):
        ch.add(aid, path)
    c0 = ch.add('EN_Road_Country2_Curve90_R50', road.P_CURVE90, 'EN_Road_Country2_Curve90_R50_Furniture')
    c1 = c0 + road.P_CURVE90.L
    for aid, path in (('EN_Road_Country2_24m_Solid', road.P_STRAIGHT), ('EN_Road_Country2_24m', road.P_STRAIGHT),
                      ('EN_Road_Country2_Curve30_R160', road.P_CURVE30), ('EN_Road_Country2_24m', road.P_STRAIGHT),
                      ('EN_Road_Country2_24m', road.P_STRAIGHT)):
        ch.add(aid, path)
    # signs before the curve (right side), km post at the start
    for s, aid in ((c0 - 85, 'EN_Sign_1_11_1_CurveRight'), (c0 - 55, 'EN_Sign_3_20_NoOvertaking'),
                   (10, 'EN_Sign_6_13_Km17')):
        p = ch.world(s, 6.9, road.ground_h(6.9))
        t, r = ch.frame(s)
        S.put(aid, A, tuple(p), yaw_of(-t))
    # straight-section delineators (the curve has its own in the furniture asset)
    s = 0.0
    while s < ch.s:
        if not (c0 - 40 < s < c1 + 40):
            t, r = ch.frame(s)
            for side in (1, -1):
                p = ch.world(s, side * 6.0, road.ground_h(6.0))
                S.put('EN_Road_Delineator', A, tuple(p), yaw_of(-t if side > 0 else t))
        s += 24.0
    pts = ch.samples(1.0)
    kd = KDTree(len(pts))
    for i, p in enumerate(pts):
        kd.insert((p.x, p.y, 0.0), i)
    kd.balance()

    def dist(x, y):
        return kd.find((x, y, 0.0))[2]
    # the road modules are 25 m wide (+0.7 m widening inside the curve): no ground plane over them
    def on_road(x, y):          # beside the chain, not beyond its open ends
        _, i, d = kd.find((x, y, 0.0))
        return d < 12.45 and 0 < i < len(pts) - 1
    xs, ys = [p.x for p in pts], [p.y for p in pts]
    S.ground(A, 1500, hole=on_road, window=(min(xs) - 40, max(xs) + 40, min(ys) - 40, max(ys) + 40))
    scatter(S, A, (-150, 260, -120, 230), 0.012, 7, FOREST_MIX, avoid=lambda x, y: dist(x, y) < 15.5)
    scatter(S, A, (-150, 260, -120, 230), 0.006, 9, [('EN_Bush_A', 3), ('EN_Bush_B', 3),
                                                      ('EN_Tree_Spruce_Young', 2), ('EN_Tree_Birch_B', 1)],
            avoid=lambda x, y: not (13.0 < dist(x, y) < 16.0))
    # grass clumps on the verges and the ditch, near the camera paths
    scatter(S, A, (-40, 110, -90, 110), 0.5, 11, [('EN_Grass_Clump_A', 1), ('EN_Grass_Clump_B', 2)], min_d=0.55,
            avoid=lambda x, y: not (6.5 < dist(x, y) < 22.0))
    # cars: ours (headlights from behind the camera) and an oncoming one in the curve
    cs = cars()
    t, r = ch.frame(c0 - 30)
    S.headlights(A, tuple(ch.world(c0 - 34, 1.75)), tuple(t))
    tc, rc = ch.frame(c0 + 40)
    put_car(S, A, cs, 1, tuple(ch.world(c0 + 40, -1.85, 0.0)), tuple(-tc), lights=True)
    eye = ch.world(c0 - 32, 1.6, 1.3)
    look = ch.world(c0 + 18, 0.5, 0.8)
    aer_t = ch.world(c0 + 50, 0.0)
    aer_p = ch.world(c0 + 10, 70.0, 48.0)
    bp = ch.world(c0 + 28, 1.6, 1.3)            # driver's eye in our (right) lane
    tb, _ = ch.frame(c0 + 26)
    S.headlights(A, tuple(ch.world(c0 + 26, 1.75)), tuple(tb))
    bt = ch.world(c0 + 55, -7.0, 0.6)
    shots = []
    for preset in ('day', 'dusk', 'night'):
        shots.append((f'road-curve-driver-{preset}', A, tuple(eye), tuple(look), 24, preset))
    shots += [('road-curve-aerial', A, tuple(aer_p), tuple(aer_t), 30, 'day'),
              ('road-curve-barrier', A, tuple(bp), tuple(bt), 28, 'day'),
              ('road-curve-barrier-night', A, tuple(bp), tuple(bt), 28, 'night')]
    return shots


def scene_barrier(S):
    if not S.has('EN_Barrier_WBeam_4m'):
        return []
    A = 'barrier'
    S.ground(A, 1200, 'EN_Gravel')
    S.put('EN_Barrier_WBeam_EndRamp_Start', A, (0, 0))
    for i in range(4):
        S.put('EN_Barrier_WBeam_4m', A, (0, i * 4))
    S.put('EN_Barrier_WBeam_EndCap', A, (0, 16))
    for i in range(4):
        S.put('EN_Barrier_Concrete_NJ_4m', A, (-5, i * 4.02))
    S.put('EN_Road_Delineator', A, (-2.5, 3), math.pi)
    return [('barrier-overview', A, (-9, -6, 2.2), (-1, 8, 0.4), 30, 'day'),
            ('barrier-detail', A, (-1.6, 1.0, 0.95), (-0.1, 3.6, 0.55), 30, 'day'),
            ('barrier-end-ramp', A, (-3.5, -6, 1.4), (0.3, -1, 0.2), 32, 'day')]


def scene_signs(S):
    ids = ['EN_Sign_1_11_1_CurveRight', 'EN_Sign_1_11_2_CurveLeft', 'EN_Sign_1_34_1_Right', 'EN_Sign_1_34_2_Left_x2',
           'EN_Sign_3_20_NoOvertaking', 'EN_Sign_6_13_Km17', 'EN_Road_Delineator']
    if not S.has(ids[0]):
        return []
    A = 'props'
    S.ground(A, 1200)
    xs = [-5.4, -3.6, -1.8, 0.4, 2.6, 4.3, 5.6]
    for x, aid in zip(xs, ids):
        S.put(aid, A, (x, 0), 0.0)
    scatter(S, A, (-12, 12, -3, 3), 0.8, 21, [('EN_Grass_Clump_A', 1), ('EN_Grass_Clump_B', 2)], min_d=0.45)
    # a car 14 m in front of the signs, facing them, headlights on (night shot)
    S.headlights(A, (0.8, 16.5, 0), (0, -1))
    return [('signs-front', A, (0, 11, 1.6), (0, 0, 1.8), 26, 'day'),
            ('signs-front-night', A, (0, 11, 1.25), (0, 0, 1.7), 26, 'night'),
            ('signs-back', A, (2.5, -6.5, 1.6), (0.5, 0, 1.6), 30, 'day')]


def scene_yard(S):
    if not S.has('EN_Yard_Swing'):
        return []
    A = 'yard'
    S.ground(A, 1200)
    R = random.Random(4)
    S.put('EN_Yard_PlaygroundPad', A, (0, 0))
    S.put('EN_Yard_Swing', A, (-4.0, 2.5))
    S.put('EN_Yard_Slide', A, (3.5, 1.5), math.pi)
    S.put('EN_Yard_Sandbox', A, (-3.5, -2.5))
    S.put('EN_Yard_Carousel', A, (1.0, -2.8))
    S.put('EN_Yard_SportsFrame', A, (5.0, -3.0), 0.3)
    for x in (-5.0, -1.5, 2.0):
        S.put('EN_Yard_Bench', A, (x, -6.0), 0.0)
    S.put('EN_Yard_Bin', A, (0.2, -6.0))
    S.put('EN_Yard_Bin', A, (-3.3, -6.0))
    for i in range(12):
        S.put('EN_Yard_LawnFence_2m', A, (-12 + i * 2.0, -7.2))
    for x in (-10.0, -8.6, 9.0):
        S.put('EN_Yard_TireFlowerbed', A, (x, -8.3))
    for p, aid in (((-11, 6), 'EN_Tree_Linden_A'), ((11, 7), 'EN_Tree_Birch_A'), ((-12, -3), 'EN_Tree_Birch_B'),
                   ((12.5, -2), 'EN_Tree_Linden_A'), ((20, 10), 'EN_Tree_Linden_A'), ((-20, 12), 'EN_Tree_Birch_A')):
        S.put(aid, A, p, R.uniform(0, 6.28))
    for x in (-16, -14.5, 15):
        S.put('EN_Bush_A', A, (x, -8.5), R.uniform(0, 6))
    for b, yaw in (((-8, -7.8), math.pi), ((10, -7.8), math.pi), ((-24, -10.0), 0.0), ((24, -10.0), 0.0)):
        S.put('EN_Yard_Lamp', A, b, yaw)
        S.street_lamp(A, b, yaw)
    S.strip(A, [(-40, -12.5), (40, -12.5)], 5.5)
    for x in (-22.5, -10.0):
        S.put('EN_Park_Bay_Perp_5x', A, (x, -15.25), 0.0)
    cs = cars()
    for i, x in enumerate((-27.5, -25.0, -20.0, -12.5, -7.5)):
        put_car(S, A, cs, i, (x, -18.0), (0, 1) if i % 2 else (0, -1))
    put_car(S, A, cs, 1, (20, -11.4), (-1, 0), lights=True)
    S.put('EN_Yard_WasteSite', A, (8.0, -17.0), 0.0)
    for x in (16.0, 19.2, 22.4):
        S.put('EN_Yard_Garage_Teal' if x != 19.2 else 'EN_Yard_Garage_Beige', A, (x, -19.5), 0.0)
    S.put('EN_Yard_BarrierGate', A, (31.0, -9.6), 0.0)
    for x in (-2.0, 0.0, 2.0):
        S.put('EN_Yard_Bollard_Sphere', A, (x + 27, -9.6))
    scatter(S, A, (-30, 30, -11, 20), 0.25, 5, [('EN_Grass_Clump_B', 1)], min_d=0.8,
            avoid=lambda x, y: (abs(x) < 7.3 and abs(y) < 5.3) or (y < -7.5 and abs(x) < 31))
    h = house()
    if h:
        S.put_coll(h.coll, A, (0, 22), math.pi, 1.0, 'HOUSE')
    return [('yard-overview', A, (-26, -40, 16), (2, -4, 0), 28, 'day'),
            ('yard-overview-dusk', A, (-26, -40, 16), (2, -4, 0), 28, 'dusk'),
            ('yard-overview-night', A, (-26, -40, 16), (2, -4, 0), 28, 'night'),
            ('yard-playground', A, (-9, -12, 3.2), (0.5, 0.5, 1.0), 30, 'day'),
            ('yard-drive-night', A, (36, -11.5, 1.6), (6, -16, 1.2), 30, 'night')]


def scene_parking(S):
    if not S.has('EN_Park_Lot_20'):
        return []
    A = 'parking'
    S.ground(A, 1200)
    R = random.Random(8)
    S.put('EN_Park_Lot_20', A, (0, 0))
    cs = cars()
    occupied = [i for i in range(20) if R.random() < 0.55]
    for i in occupied:
        row = i // 10
        x = -11.25 + (i % 10) * 2.5
        put_car(S, A, cs, i, (x, 5.4 if row == 0 else -5.4), (0, 1) if row == 0 else (0, -1))
    for i in range(10):
        if i not in occupied:
            S.put('EN_Park_WheelStop', A, (-11.25 + i * 2.5, 7.4))
    for p in ((-13.5, 5.5), (13.5, -5.5)):
        S.put('EN_Tree_Linden_A', A, p, R.uniform(0, 6))
    for p in ((-13.5, -5.5), (13.5, 5.5)):
        S.put('EN_Bush_B', A, p, R.uniform(0, 6))
    S.put('EN_Park_PayStation', A, (-15.2, 3.6), -math.pi / 2)
    for y in (-2.0, 0.0, 2.0):
        S.put('EN_Park_Bollard', A, (17.5, y))
    for b, yaw in (((-6, 8.6), math.pi), ((6, 8.6), math.pi), ((-6, -8.6), 0.0), ((6, -8.6), 0.0)):
        S.put('EN_Yard_Lamp', A, b, yaw)
        S.street_lamp(A, b, yaw)
    S.strip(A, [(-40, -16), (40, -16)], 7.0)
    S.put('EN_Park_Bay_Angle45_4x', A, (-18, -19.5), 0.0)
    S.put('EN_Park_Bay_Perp_5x', A, (2, -19.5), 0.0)
    S.put('EN_Park_Pocket_Parallel_3x', A, (12, -19.5), -math.pi / 2)
    put_car(S, A, cs, 1, (-19.6, -22.0), (math.sin(math.radians(-45)), math.cos(math.radians(45))))
    put_car(S, A, cs, 0, (4.5, -22.2), (0, 1))
    put_car(S, A, cs, 2, (22, -20.7), (1, 0))
    put_car(S, A, cs, 0, (-20, -14.3), (1, 0), lights=True)
    return [('parking-overview', A, (-24, -36, 18), (0, -3, 0), 30, 'day'),
            ('parking-overview-night', A, (-24, -36, 18), (0, -3, 0), 30, 'night'),
            ('parking-lot-eye', A, (-17, 0.5, 1.6), (6, -1, 0.8), 30, 'day')]


def scene_props(S):
    ids = ['EN_Yard_Bench', 'EN_Yard_Bin', 'EN_Yard_Bollard_Sphere', 'EN_Yard_TireFlowerbed', 'EN_Park_WheelStop',
           'EN_Park_PayStation', 'EN_Park_Bollard', 'EN_Yard_Lamp']
    if not S.has(ids[0]):
        return []
    A = 'props'
    S.area(A)
    for x, aid in zip([-14, -12, -10.7, -9.5, -8, -6, -4.7, -3], ids):
        S.put(aid, A, (x, -10), 0.0)
    return [('yard-props', A, (-8.5, -4.5, 1.6), (-8.5, -10, 0.5), 36, 'day')]


SCENES = [scene_trees, scene_forest, scene_road, scene_barrier, scene_signs, scene_yard, scene_parking, scene_props]


def build(assets, out, render=True, fast=False, shots=None):
    M = {m.name: m for m in bpy.data.materials}
    S = Scene(assets, M)
    todo = []
    for fn in SCENES:
        try:
            todo += fn(S) or []
        except KeyError as ex:
            print('SCENE_SKIPPED', fn.__name__, ex)
    if not render:
        return
    dev = gpu()
    for sc in S.scenes.values():
        setup(sc, fast, dev)
    for name, area, pos, target, lens, preset in todo:
        if shots and not any(name.startswith(sh) for sh in shots):
            continue
        shot(S, out, name, area, pos, target, lens, preset)
