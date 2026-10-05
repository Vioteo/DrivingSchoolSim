"""Review scenes for the EN kit: collection instances of the exported assets, rendered with Cycles.

Every scene lives in its own area of the source file (areas are 1 km apart),
positions are in the author frame (X right, Y forward, Z up) and converted.
"""
import bpy, math, random
from mathutils import Vector

from .core import to_blender, MB, mesh_object

AREAS = {'trees': (0, 0), 'forest': (0, 1000), 'road': (1000, 0), 'barrier': (1000, 1000),
         'yard': (-1000, 0), 'parking': (-1000, 1000), 'props': (0, -1000)}


class Scene:
    """One Blender scene per review area, so a render only syncs its own instances."""

    def __init__(self, assets, M):
        self.assets, self.M = assets, M
        self.scenes = {}
        self.n = 0

    def area(self, area):
        if area not in self.scenes:
            sc = bpy.data.scenes.new('Review_' + area)
            sc.unit_settings.system, sc.unit_settings.scale_length = 'METRIC', 1.0
            self.scenes[area] = sc
        return self.scenes[area]

    def has(self, *ids):
        return all(i in self.assets for i in ids)

    def put(self, aid, area, loc, yaw=0.0, s=1.0):
        if aid not in self.assets:
            return None
        ox, oy = AREAS[area]
        e = bpy.data.objects.new(f'I_{aid}_{self.n}', None)
        self.n += 1
        e.instance_type = 'COLLECTION'
        e.instance_collection = self.assets[aid].coll
        e.location = to_blender((loc[0] + ox, loc[1] + oy, loc[2] if len(loc) > 2 else 0.0))
        e.rotation_euler = (0, 0, yaw)
        e.scale = (s, s, s)
        self.area(area).collection.objects.link(e)
        return e

    def ground(self, area, size=1200, mat='EN_Grass', z=-0.03):
        ox, oy = AREAS[area]
        mb = MB()
        mb.ngon([(ox - size, oy - size, z), (ox + size, oy - size, z), (ox + size, oy + size, z),
                 (ox - size, oy + size, z)], mat)
        mesh_object(f'Ground_{area}', mb, self.M, coll=self.area(area).collection)


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
            gpus = [d for d in prefs.devices if d.type == t]
            if gpus:
                for d in prefs.devices:
                    d.use = d.type == t
                dev = t
                break
    except Exception as ex:
        print('GPU setup failed:', ex)
    print('RENDER_DEVICE', dev, flush=True)
    return dev


def world():
    w = bpy.data.worlds.get('EN_World')
    if w:
        return w
    w = bpy.data.worlds.new('EN_World')
    w.use_nodes = True
    nt = w.node_tree
    bg = nt.nodes['Background']
    try:
        sky = nt.nodes.new('ShaderNodeTexSky')
        for t in ('MULTIPLE_SCATTERING', 'NISHITA', 'SINGLE_SCATTERING'):
            try:
                sky.sky_type = t
                break
            except Exception:
                continue
        sky.sun_disc = False
        sky.sun_elevation = math.radians(38)
        sky.sun_rotation = math.radians(150)
        nt.links.new(sky.outputs[0], bg.inputs[0])
        bg.inputs[1].default_value = 0.22
    except Exception as ex:
        print('sky failed', ex)
        bg.inputs[0].default_value = (.55, .68, .85, 1)
        bg.inputs[1].default_value = .8
    return w


def setup(sc, fast, dev):
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'GPU' if dev != 'CPU' else 'CPU'
    sc.cycles.samples = 24 if fast else 96
    sc.cycles.use_denoising = True
    sc.cycles.max_bounces = 6
    sc.render.resolution_x, sc.render.resolution_y = (960, 540) if fast else (1600, 900)
    sc.view_settings.view_transform = 'AgX'
    try:
        sc.view_settings.look = 'AgX - Medium High Contrast'
    except Exception:
        pass
    sc.world = world()
    sun = bpy.data.lights.get('Sun') or bpy.data.lights.new('Sun', 'SUN')
    sun.energy, sun.angle = 4.2, math.radians(1.2)
    sun.color = (1.0, 0.96, 0.9)
    o = bpy.data.objects.new('Sun_' + sc.name, sun)
    sc.collection.objects.link(o)
    o.rotation_euler = (math.radians(52), 0, math.radians(150))


def shot(S, out, name, area, pos, target, lens=35):
    sc = S.area(area)
    ox, oy = AREAS[area]
    p = Vector(to_blender((pos[0] + ox, pos[1] + oy, pos[2])))
    t = Vector(to_blender((target[0] + ox, target[1] + oy, target[2])))
    cam = bpy.data.cameras.new(name)
    ob = bpy.data.objects.new('CAM_' + name, cam)
    sc.collection.objects.link(ob)
    ob.location = p
    ob.rotation_euler = (t - p).to_track_quat('-Z', 'Y').to_euler()
    cam.lens = lens
    cam.clip_start, cam.clip_end = 0.05, 900
    sc.camera = ob
    sc.render.filepath = str(out / f'{name}.png')
    bpy.ops.render.render(write_still=True, scene=sc.name)
    print('RENDER', name, flush=True)


# ---------------------------------------------------------------- scenes
def scene_trees(S):
    ids = ['EN_Tree_Birch_A', 'EN_Tree_Birch_B', 'EN_Tree_Linden_A', 'EN_Tree_Pine_A', 'EN_Tree_Pine_B',
           'EN_Tree_Spruce_A', 'EN_Tree_Spruce_B', 'EN_Tree_Spruce_Young', 'EN_Bush_A', 'EN_Bush_B']
    xs = [-26, -18, -9, 1, 9, 17, 25, 31, 36, 40]
    for aid, x in zip(ids, xs):
        S.put(aid, 'trees', (x, 0))
    for aid, x in (('EN_Stump', 33), ('EN_Log_Fallen', 37), ('EN_Rock_A', 43), ('EN_Rock_B', 45)):
        S.put(aid, 'trees', (x, -5))
    S.ground('trees')
    # LOD comparison row
    for i, aid in enumerate(('EN_Tree_Birch_A', 'EN_Tree_Spruce_A', 'EN_Tree_Pine_A', 'EN_Tree_Linden_A')):
        if aid in S.assets:
            for l in range(3):
                pass
    return [('trees-lineup', 'trees', (7, -62, 7), (7, 0, 7.5), 30),
            ('trees-closeup-birch', 'trees', (-20, -16, 2.5), (-22, 0, 6), 32),
            ('trees-closeup-spruce', 'trees', (22, -20, 3), (21, 0, 6), 30)]


def scatter_forest(S, area, region, density, seed, avoid=None, mix=None):
    """region: (x0, x1, y0, y1); avoid(x, y) -> True to skip."""
    R = random.Random(seed)
    x0, x1, y0, y1 = region
    n = int((x1 - x0) * (y1 - y0) * density)
    mix = mix or [('EN_Tree_Spruce_A', 3), ('EN_Tree_Spruce_B', 3), ('EN_Tree_Birch_A', 2), ('EN_Tree_Birch_B', 2),
                  ('EN_Tree_Pine_A', 2), ('EN_Tree_Pine_B', 2), ('EN_Tree_Spruce_Young', 2), ('EN_Bush_A', 1),
                  ('EN_Bush_B', 1)]
    pool = [a for a, w in mix for _ in range(w)]
    placed = 0
    grid = {}
    tries = 0
    while placed < n and tries < n * 6:
        tries += 1
        x, y = R.uniform(x0, x1), R.uniform(y0, y1)
        gx, gy = int(x // 3), int(y // 3)
        near = [p for i in (-1, 0, 1) for j in (-1, 0, 1) for p in grid.get((gx + i, gy + j), ())]
        if any((x - px) ** 2 + (y - py) ** 2 < 9 for px, py in near):
            continue
        if avoid and avoid(x, y):
            continue
        grid.setdefault((gx, gy), []).append((x, y))
        placed += 1
        aid = R.choice(pool)
        S.put(aid, area, (x, y), R.uniform(0, 6.28), R.uniform(0.85, 1.15))
    for i in range(int(n * 0.12)):
        x, y = R.uniform(x0, x1), R.uniform(y0, y1)
        if avoid and avoid(x, y):
            continue
        S.put(R.choice(('EN_Rock_B', 'EN_Stump', 'EN_Rock_A', 'EN_Log_Fallen')), area, (x, y), R.uniform(0, 6.28))


def scene_forest(S):
    if not S.has('EN_Tree_Spruce_A'):
        return []
    S.ground('forest')
    scatter_forest(S, 'forest', (-60, 60, 8, 90), 0.045, 1)
    for i in range(8):
        S.put('EN_Bush_A' if i % 2 else 'EN_Bush_B', 'forest', (-40 + i * 11, 6 + (i % 3)), i)
    return [('forest-edge', 'forest', (6, -40, 5), (0, 40, 7), 28)]


SCENES = [scene_trees, scene_forest]


def build(assets, out, render=True, fast=False, shots=None):
    M = {m.name: m for m in bpy.data.materials}
    S = Scene(assets, M)
    todo = []
    for fn in SCENES:
        todo += fn(S) or []
    try:
        from . import review_more
        for fn in review_more.SCENES:
            todo += fn(S) or []
    except ImportError:
        pass
    if not render:
        return
    dev = gpu()
    for sc in S.scenes.values():
        setup(sc, fast, dev)
    for name, area, pos, target, lens in todo:
        if shots and name not in shots:
            continue
        shot(S, out, name, area, pos, target, lens)
