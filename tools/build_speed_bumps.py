"""Original speed bump kit. Blender --background --python tools/build_speed_bumps.py."""
import bpy
import bmesh
import math
import json
import hashlib
from pathlib import Path
from datetime import datetime, timezone
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / 'Assets/DrivingSchool/Art/SpeedBumps'
REVIEW = ROOT / 'artifacts/visual-review/speed-bumps'
REPORT = ROOT / 'artifacts/reports/speed-bumps'
for path in (ART, REVIEW, REPORT, ROOT / 'ArtSource'):
    path.mkdir(parents=True, exist_ok=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1


def material(name, color, roughness, metallic=0):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value = (*color, 1)
    bsdf.inputs['Roughness'].default_value = roughness
    bsdf.inputs['Metallic'].default_value = metallic
    return mat


rubber = material('SB_Rubber', (.018, .023, .027), .87)
yellow = material('SB_Yellow', (.95, .58, .012), .58)
steel = material('SB_Steel', (.14, .17, .19), .38, .75)
asphalt = material('SB_Asphalt', (.052, .059, .064), .96)
white = material('SB_White', (.82, .84, .80), .83)


def solid_grid(name, xs, ys, height, mats, face_material=None):
    """Closed solid, separately shaded top and side faces, planar bottom."""
    nx, ny = len(xs), len(ys)
    vertices = [(x, y, height(x, y)) for x in xs for y in ys]
    faces, indices = [], []
    for i in range(nx-1):
        for j in range(ny-1):
            faces.append((i*ny+j, (i+1)*ny+j, (i+1)*ny+j+1, i*ny+j+1))
            indices.append(face_material((xs[i]+xs[i+1])/2, (ys[j]+ys[j+1])/2) if face_material else 0)
    boundary = ([i*ny for i in range(nx)] + [(nx-1)*ny+j for j in range(1,ny)]
                + [i*ny+ny-1 for i in range(nx-2,-1,-1)] + [j for j in range(ny-2,0,-1)])
    bottom = []
    for top in boundary:
        bottom.append(len(vertices))
        vertices.append((vertices[top][0], vertices[top][1], 0))
    for i in range(len(boundary)):
        k = (i+1) % len(boundary)
        faces.append((boundary[k], boundary[i], bottom[i], bottom[k]))
        indices.append(0)
    faces.append(tuple(reversed(bottom)))
    indices.append(0)
    mesh = bpy.data.meshes.new(name + '_Mesh')
    mesh.from_pydata(vertices, [], faces)
    for mat in mats:
        mesh.materials.append(mat)
    for poly, index in zip(mesh.polygons, indices):
        poly.material_index = index
    obj = bpy.data.objects.new(name, mesh)
    scene.collection.objects.link(obj)
    return obj


def join(objects, name):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    obj = bpy.context.object
    obj.name = name
    scene.cursor.location = (0, 0, 0)
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    assert all(e.is_manifold for e in bm.edges), name + ': open edges'
    bm.to_mesh(obj.data)
    bm.free()
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(island_margin=.015)
    bpy.ops.object.mode_set(mode='OBJECT')
    # Triangulate explicitly: Blender and Unity use the exact same curved surface.
    mod = obj.modifiers.new('Export triangles', 'TRIANGULATE')
    bpy.ops.object.modifier_apply(modifier=mod.name)
    return obj


def rubber_height(y):
    return .003 + .062 * math.cos(math.pi*y/.5)


def build_rubber(name, count):
    pieces = []
    half = count*.5/2
    ys = [-.25+i*.5/24 for i in range(25)]
    for i in range(count):
        center = -half + .25 + i*.5
        # Moulded black border surrounds each yellow insert; seams are 4 mm.
        xs = [center+t for t in (-.248,-.17,.17,.248)]
        pieces.append(solid_grid('Moulded_module', xs, ys, lambda x,y: rubber_height(y),
            [rubber, yellow], lambda x,y: int(abs(x-center)<.17 and abs(y)<.19)))
        for x in (center-.211, center+.211):
            for y in (-.125,.125):
                # Flush hex fastener and dark socket, seated on the curved body.
                bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=.013, depth=.002,
                    location=(x,y,rubber_height(y)))
                bolt = bpy.context.object
                bolt.data.materials.append(steel)
                pieces.append(bolt)
                bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=.005, depth=.0008,
                    location=(x,y,rubber_height(y)+.0012))
                socket = bpy.context.object
                socket.data.materials.append(rubber)
                pieces.append(socket)
    for side in (-1,1):
        xs = sorted([side*(half+.002+i*.248/12) for i in range(13)])
        pieces.append(solid_grid('Tapered_endcap', xs, ys,
            lambda x,y: .003+(rubber_height(y)-.003)*math.cos((abs(x)-half-.002)/.248*math.pi/2), [rubber]))
    return join(pieces, name)


def build_asphalt():
    xs = [-1.75,-1.70,-1.60,-1.50,1.50,1.60,1.70,1.75]
    ys = [-1.5+i*3/60 for i in range(61)]
    def height(x,y):
        taper = min(1, max(0, (1.75-abs(x))/.25))
        return .001+.089*(.5+.5*math.cos(math.pi*y/1.5))*taper
    body = solid_grid('Asphalt_hump',xs,ys,height,[asphalt])
    parts = [body]
    # White approach triangles follow the profile. Thin closed paint solids.
    for center in (-1.05,-.35,.35,1.05):
        for side in (-1,1):
            levels = 24
            verts = []
            for layer in (0,.0006):
                for j in range(levels+1):
                    t = j/levels
                    y = side*(1.36-1.08*t)
                    w = .24*(1-t)+.001
                    for x in (center-w,center+w):
                        verts.append((x,y,height(x,y)+.0002+layer))
            n = (levels+1)*2
            faces = []
            for j in range(levels):
                a=j*2
                faces.extend([(a,a+1,a+3,a+2),(n+a+2,n+a+3,n+a+1,n+a),
                              (a,a+2,n+a+2,n+a),(a+3,a+1,n+a+1,n+a+3)])
            faces.extend([(0,n,n+1,1),(n-2,n-1,2*n-1,2*n-2)])
            mesh=bpy.data.meshes.new('ApproachTriangle')
            mesh.from_pydata(verts,[],faces)
            mesh.materials.append(white)
            paint=bpy.data.objects.new('White_approach_mark',mesh)
            scene.collection.objects.link(paint)
            parts.append(paint)
    return join(parts,'DS_SpeedBump_Asphalt_3p5m')


models = [build_rubber('DS_SpeedBump_Rubber_3p5m',6),
          build_rubber('DS_SpeedBump_Rubber_7m',13), build_asphalt()]
entries=[]
for obj in models:
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active=obj
    obj.data.calc_loop_triangles()
    triangles=len(obj.data.loop_triangles)
    assert triangles<15000
    assert abs(min(v.co.z for v in obj.data.vertices))<1e-6
    paths=[ART/(obj.name+'.fbx'), REVIEW/(obj.name+'.glb')]
    bpy.ops.export_scene.fbx(filepath=str(paths[0]),use_selection=True,object_types={'MESH'},
        add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',
        apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS')
    bpy.ops.export_scene.gltf(filepath=str(paths[1]),export_format='GLB',use_selection=True,export_yup=True)
    entries.append({'catalogId':obj.name,'dimensionsBlenderXYZ':list(obj.dimensions),
        'triangles':triangles,'materialSlots':len(obj.data.materials),'uvLayers':len(obj.data.uv_layers),
        'closedMeshComponents':True,'textures':0,'lods':'not prepared',
        'sha256':{str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in paths}})

# Save editable source with separated assemblies. Every exported model has ground-centre pivot.
for obj, y in zip(models,(0,1.7,4.6)):
    obj.location.y=y
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'ArtSource/DS_SpeedBumps.blend'))
floor_mat=material('Preview_Road',(.09,.105,.12),.93)
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.002))
bpy.context.object.data.materials.append(floor_mat)
scene.world.color=(.30,.30,.30)
for name,pos,power,size in [('Key',(-3,-4,8),1900,7),('Fill',(5,3,6),1400,6)]:
    data=bpy.data.lights.new(name,'AREA')
    data.energy=power
    data.shape='DISK'
    data.size=size
    light=bpy.data.objects.new(name,data)
    scene.collection.objects.link(light)
    light.location=pos
    light.rotation_euler=(Vector((0,2,0))-light.location).to_track_quat('-Z','Y').to_euler()
data=bpy.data.cameras.new('ReviewCamera')
camera=bpy.data.objects.new('ReviewCamera',data)
scene.collection.objects.link(camera)
scene.camera=camera
data.type='ORTHO'
scene.render.engine='CYCLES'
scene.cycles.samples=24
scene.cycles.use_denoising=True
scene.render.resolution_x=1000
scene.render.resolution_y=700
scene.render.resolution_percentage=100
scene.view_settings.view_transform='AgX'
for name,pos,target,scale in [('overview',(7,-9,11),(0,2.3,0),10),
                              ('rubber-detail',(1.6,-1.6,1.2),(0,0,.02),2.3),
                              ('asphalt',(4,1,3),(0,4.6,0),5.3)]:
    for obj in models:
        obj.hide_render=(name=='rubber-detail' and obj!=models[0]) or (name=='asphalt' and obj!=models[2])
    data.ortho_scale=scale
    camera.location=pos
    camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=str(REVIEW/(name+'.png'))
    bpy.ops.render.render(write_still=True)
report={'revision':1,'utc':datetime.now(timezone.utc).isoformat(),'exporter':'Blender '+bpy.app.version_string,
    'units':'metres','sourceUp':'Z','exportUp':'Y','unityForward':'Z: drive over the hump; X: across road',
    'pivot':'ground centre','models':entries,'sourceSha256':hashlib.sha256((ROOT/'ArtSource/DS_SpeedBumps.blend').read_bytes()).hexdigest()}
(REPORT/'manifest.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('SPEED_BUMPS_MODEL_PASS',json.dumps(report))
