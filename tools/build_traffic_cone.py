"""Build the original 75 cm traffic cone. Run with Blender --background --python."""
import bpy
import bmesh
import math
import json
import hashlib
from pathlib import Path
from datetime import datetime, timezone
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / 'Assets/DrivingSchool/Art/Props'
REVIEW = ROOT / 'artifacts/visual-review/traffic-cone'
for folder in (ART, REVIEW, ROOT / 'ArtSource', ROOT / 'artifacts/reports'):
    folder.mkdir(parents=True, exist_ok=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1

def material(name, color, roughness):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1)
    mat.use_nodes = True
    shader = mat.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value = (*color, 1)
    shader.inputs['Roughness'].default_value = roughness
    return mat

orange = material('Cone_Orange', (1, .115, .006), .32)
white = material('Cone_White', (.88, .9, .85), .28)
rubber = material('Cone_Rubber', (.019, .023, .027), .82)

# Continuous turned shell: stripes are face materials, never overlapping decals.
# Closed cross-section includes the top lip and hollow interior.
profile = [(.187, .057), (.196, .064), (.196, .077), (.188, .087),
           (.185, .098), (.153, .23), (.1288, .33), (.0998, .45),
           (.0756, .55), (.032, .73), (.0305, .744), (.026, .75),
           (.019, .75), (.016, .744), (.0175, .73), (.171, .098),
           (.174, .077), (.174, .057)]
segments = 64
vertices = [(r*math.cos(2*math.pi*i/segments), r*math.sin(2*math.pi*i/segments), z)
            for r,z in profile for i in range(segments)]
faces = []
for j in range(len(profile)):
    for i in range(segments):
        k = (i+1) % segments
        jj = (j+1) % len(profile)
        faces.append((j*segments+i,j*segments+k,jj*segments+k,jj*segments+i))
mesh = bpy.data.meshes.new('ConeShellMesh')
mesh.from_pydata(vertices, [], faces)
mesh.materials.append(orange)
mesh.materials.append(white)
for poly in mesh.polygons:
    ring = poly.index // segments
    poly.material_index = 1 if ring in (5, 7) else 0
    poly.use_smooth = ring not in (11,17)
shell = bpy.data.objects.new('Cone_Shell', mesh)
bpy.context.collection.objects.link(shell)

# Low, rounded square weighted foot, 46 cm wide. Bottom sits exactly on ground.
bpy.ops.mesh.primitive_cube_add(size=1, location=(0,0,.031))
base = bpy.context.object
base.name = 'Cone_WeightedBase'
base.scale = (.46,.46,.062)
bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
base.data.materials.append(rubber)
bevel = base.modifiers.new('Moulded rounded edges', 'BEVEL')
bevel.width = .016
bevel.segments = 4
bpy.ops.object.modifier_apply(modifier=bevel.name)
normal = base.modifiers.new('Weighted face normals','WEIGHTED_NORMAL')
bpy.ops.object.modifier_apply(modifier=normal.name)

# One mesh, three material slots, origin at the centre of its footprint.
bpy.ops.object.select_all(action='DESELECT')
shell.select_set(True)
base.select_set(True)
bpy.context.view_layer.objects.active = shell
bpy.ops.object.join()
cone = bpy.context.object
cone.name = 'DS_TrafficCone'
scene.cursor.location = (0,0,0)
bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
bm = bmesh.new()
bm.from_mesh(cone.data)
bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
assert all(edge.is_manifold for edge in bm.edges), 'Non-manifold geometry'
bm.to_mesh(cone.data)
bm.free()
# UVs for future paint/texturing; current portable materials need no textures.
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(island_margin=.025)
bpy.ops.object.mode_set(mode='OBJECT')
cone.data.calc_loop_triangles()
triangles = len(cone.data.loop_triangles)
assert triangles < 4000
assert abs(cone.dimensions.z-.75) < .0001
assert min((cone.matrix_world @ v.co).z for v in cone.data.vertices) >= -.00001

bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'ArtSource/DS_TrafficCone.blend'))
bpy.ops.export_scene.fbx(filepath=str(ART/'DS_TrafficCone.fbx'), use_selection=True,
    object_types={'MESH'}, add_leaf_bones=False, bake_anim=False,
    axis_forward='-Z', axis_up='Y', apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS')
bpy.ops.export_scene.gltf(filepath=str(REVIEW/'DS_TrafficCone.glb'), export_format='GLB',
    use_selection=True, export_yup=True)

# Dedicated studio evidence. Stage, lights and camera are excluded from exports.
floor_mat = material('Preview_Floor', (.105,.13,.15), .86)
bpy.ops.mesh.primitive_plane_add(size=200)
floor = bpy.context.object
floor.location.z = -.001
floor.data.materials.append(floor_mat)
scene.world.color = (.25,.25,.25)
def light(name, position, power, size):
    data = bpy.data.lights.new(name,'AREA')
    data.energy = power
    data.shape = 'DISK'
    data.size = size
    obj = bpy.data.objects.new(name, data)
    scene.collection.objects.link(obj)
    obj.location = position
    obj.rotation_euler = (Vector((0,0,.36))-obj.location).to_track_quat('-Z','Y').to_euler()
light('Large soft key',(1,-2,3),230,2.1)
light('Soft fill',(-2,-1,1.4),95,2)
light('Rim',(1,1,2.3),210,1.6)
camera_data = bpy.data.cameras.new('ReviewCamera')
camera = bpy.data.objects.new('ReviewCamera',camera_data)
scene.collection.objects.link(camera)
scene.camera = camera
camera_data.type = 'ORTHO'
camera_data.ortho_scale = 1.12
scene.render.engine = 'CYCLES'
scene.cycles.samples = 32
scene.cycles.use_denoising = True
scene.render.resolution_x = 800
scene.render.resolution_y = 800
scene.render.resolution_percentage = 100
scene.view_settings.view_transform = 'AgX'
for name, position, target in [('hero',(1.1,-1.7,1.08),(0,0,.365)),
                                ('top',(.8,-1.1,1.8),(0,0,.35))]:
    camera.location = position
    camera.rotation_euler = (Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath = str(REVIEW/(name+'.png'))
    bpy.ops.render.render(write_still=True)
files = [ROOT/'ArtSource/DS_TrafficCone.blend', ART/'DS_TrafficCone.fbx', REVIEW/'DS_TrafficCone.glb']
report = {'asset':'DS_TrafficCone','revision':1,'utc':datetime.now(timezone.utc).isoformat(),
    'generator':'Blender '+bpy.app.version_string, 'units':'metres','source_up':'Z','export_up':'Y',
    'dimensions_m':list(cone.dimensions),'triangles':triangles,'material_slots':len(cone.data.materials),
    'textures':0,'uv_layers':len(cone.data.uv_layers),'manifold':True,'pivot':'ground centre',
    'lods':'not prepared','files':{str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in files}}
(ROOT/'artifacts/reports/traffic-cone-manifest.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('CONE_MODEL_PASS',json.dumps(report))
