"""Original metric building kit. Blender 5: -b --python tools/build_houses.py.
New assets only; existing district and vehicle sources are never opened or saved.
"""
import bpy, math, json, hashlib, sys
from pathlib import Path
from mathutils import Vector
from datetime import datetime, timezone

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'Assets/DrivingSchool/Art/Houses'
SOURCE = ROOT / 'ArtSource/Houses'
REVIEW = ROOT / 'artifacts/visual-review/houses'
REPORT = ROOT / 'artifacts/reports/houses'
for p in (OUT, SOURCE, REVIEW, REPORT): p.mkdir(parents=True, exist_ok=True)
PALETTE = {
    'Ivory': (.76,.70,.56), 'Sage': (.32,.43,.36), 'Brick': (.44,.20,.12),
    'Stone': (.51,.52,.48), 'Cream': (.88,.83,.70), 'Graphite': (.075,.105,.12),
    'Roof': (.22,.095,.065), 'Glass': (.075,.19,.24), 'GlassLight': (.25,.38,.40),
    'Timber': (.24,.12,.055), 'Metal': (.16,.20,.21), 'Mortar': (.54,.34,.23)
}
SPECS = [
    dict(id='DS_House_Cottage', title='Коттедж', w=9., d=8., floors=2, style='cottage'),
    dict(id='DS_House_Townhouse', title='Таунхаус', w=16., d=9., floors=2, style='townhouse'),
    dict(id='DS_House_Brick5', title='Кирпичный дом · 5 этажей', w=21., d=12., floors=5, style='brick'),
    dict(id='DS_House_Modern8', title='Современный дом · 8 этажей', w=19., d=13., floors=8, style='modern'),
]

class MeshBuilder:
    def __init__(self): self.v=[]; self.f=[]; self.m=[]
    def poly(self, verts, faces, mat):
        n=len(self.v); self.v.extend(verts); self.f.extend([tuple(n+i for i in f) for f in faces]); self.m.extend([mat]*len(faces))
    def box(self, loc, dim, mat, turn=0):
        x,y,z=[a/2 for a in dim]; c=math.cos(turn); s=math.sin(turn)
        vv=[(-x,-y,-z),(x,-y,-z),(x,y,-z),(-x,y,-z),(-x,-y,z),(x,-y,z),(x,y,z),(-x,y,z)]
        self.poly([(loc[0]+a*c-b*s,loc[1]+a*s+b*c,loc[2]+h) for a,b,h in vv],
                  [(3,2,1,0),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],mat)
    def object(self, name, mats):
        mesh=bpy.data.meshes.new(name); mesh.from_pydata(self.v,[],self.f); mesh.update()
        keys=list(dict.fromkeys(self.m))
        for k in keys: mesh.materials.append(mats[k])
        for p,k in zip(mesh.polygons,self.m): p.material_index=keys.index(k)
        obj=bpy.data.objects.new(name,mesh); bpy.context.collection.objects.link(obj)
        # Metric planar UVs for later texture work; current materials need no textures.
        uv=mesh.uv_layers.new(name='UVMap')
        for face in mesh.polygons:
            n=face.normal; axes=(0,1) if abs(n.z)>.7 else ((0,2) if abs(n.y)>.7 else (1,2))
            for li in face.loop_indices:
                co=mesh.vertices[mesh.loops[li].vertex_index].co
                uv.data[li].uv=(co[axes[0]],co[axes[1]])
        return obj

def materials():
    result={}
    for name,color in PALETTE.items():
        m=bpy.data.materials.new('House_'+name); m.diffuse_color=(*color,1); m.use_nodes=True
        p=m.node_tree.nodes.get('Principled BSDF'); p.inputs['Base Color'].default_value=(*color,1)
        p.inputs['Roughness'].default_value=.25 if 'Glass' in name else .78
        p.inputs['Metallic'].default_value=.25 if name in ('Glass','GlassLight','Metal') else 0
        result[name]=m
    return result

def build(spec, lod, mats):
    b=MeshBuilder(); w=spec['w']; d=spec['d']; floors=spec['floors']; h=floors*3.; style=spec['style']
    wall={'cottage':'Ivory','townhouse':'Sage','brick':'Brick','modern':'Cream'}[style]
    balcony_u=w*(.25 if style=='brick' else .4)
    b.box((0,0,h/2),(w,d,h),wall)
    b.box((0,0,.25),(w+.12,d+.12,.5),'Stone')
    # Facade-local coordinates: u horizontal, outward positive v. Front is Blender -Y / Unity +Z.
    def face_box(side,u,v,z,ww,dd,hh,mat):
        angle=side*math.pi/2; x=u*math.cos(angle)+(d/2+v)*math.sin(angle); y=u*math.sin(angle)-(d/2+v)*math.cos(angle)
        # Side faces use width as their outward extent.
        if side in (1,3):
            x=u*math.cos(angle)+(w/2+v)*math.sin(angle); y=u*math.sin(angle)-(w/2+v)*math.cos(angle)
        b.box((x,y,z),(ww,dd,hh),mat,angle)
    def window(side,u,z,width=1.45,height=1.5,index=0):
        glass='GlassLight' if index%4==0 else 'Glass'
        face_box(side,u,.035,z,width+.18,.10,height+.18,'Cream' if style!='modern' else 'Graphite')
        face_box(side,u,.094,z,width,.025,height,glass)
        if lod<2:
            face_box(side,u,.12,z,.055,.04,height,'Cream')
            face_box(side,u,.12,z+.22,width,.04,.045,'Cream')
            face_box(side,u,.15,z-height/2-.10,width+.30,.32,.10,'Stone')
        if lod==0 and style=='cottage':
            for s in (-1,1):
                face_box(side,u+s*(width/2+.28),.10,z,.30,.12,height,'Sage')
                for dz in (-.45,-.15,.15,.45): face_box(side,u+s*(width/2+.28),.17,z+dz,.29,.035,.035,'Ivory')
    for side in range(4):
        span=w if side%2==0 else d
        count=max(2,int(span/3.2)); spacing=span/count
        for level in range(floors):
            for col in range(count):
                u=-span/2+spacing*(col+.5)
                if side==0 and level==0 and ((style in ('brick','modern') and abs(u)<3) or (style=='cottage' and abs(u)<1) or (style=='townhouse' and col in (1,3))): continue
                balcony=style in ('brick','modern') and side in (0,2) and level>0 and abs(abs(u)-balcony_u)<.01
                window(side,u,(1.54 if balcony else 1.85)+3*level,1.05 if balcony else (1.55 if style=='modern' else 1.35),2.15 if balcony else 1.5,index=col+level+side)
        if lod<2:
            face_box(side,0,.07,h-.18,span+.16,.20,.22,'Cream')
            for level in range(1,floors): face_box(side,0,.055,level*3,span+.1,.14,.10 if style=='brick' else .18,'Stone')
        # Rainwater pipes, including rear facades.
        if lod==0:
            for u in (-span/2+.25,span/2-.25): face_box(side,u,.15,h/2,.085,.10,h,'Metal')
    doors=[-3.2,3.2] if style=='townhouse' else [0]
    for u in doors:
        face_box(0,u,.06,1.25,1.5,.14,2.5,'Cream')
        face_box(0,u,.15,1.22,1.27,.09,2.35,'Timber' if style=='cottage' else 'Graphite')
        if lod<2:
            face_box(0,u,.21,1.57,.88,.025,1.15,'Glass')
            face_box(0,u+.43,.26,1.1,.04,.10,.32,'Cream')
        face_box(0,u,.75,2.65,2.3,1.75,.14,'Graphite')
        face_box(0,u,.8,.10,2.4,1.8,.20,'Stone')
        if lod==0:
            face_box(0,u-.95,.19,2.0,.16,.15,.32,'Cream')
            face_box(0,u+.98,.19,1.65,.28,.12,.35,'Metal')
    if style in ('cottage','townhouse'):
        # Closed gable volume plus two thick sloped roof slabs.
        rise=2.3; rw=w/2+.5; rd=d/2+.55
        b.poly([(-w/2,-d/2,h),(w/2,-d/2,h),(0,-d/2,h+rise),(-w/2,d/2,h),(w/2,d/2,h),(0,d/2,h+rise)],
               [(0,1,2),(5,4,3),(0,3,4,1),(1,4,5,2),(2,5,3,0)],wall)
        for s in (-1,1):
            verts=[(0,-rd,h+rise+.13),(s*rw,-rd,h-.1),(s*rw,rd,h-.1),(0,rd,h+rise+.13)]
            # Consistent outward normals on both slope solids.
            if s<0: verts.reverse()
            b.poly(verts+[(x,y,z-.18) for x,y,z in verts],[(0,1,2,3),(7,6,5,4),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)],'Roof' if style=='cottage' else 'Graphite')
            if lod==0:
                for j in range(1,9):
                    x=s*rw*j/9; z=h+rise+.14-(rise+.23)*j/9
                    b.box((x,0,z),(.055,rd*2,.055),'Roof' if style=='cottage' else 'Metal')
        if lod<2:
            b.box((w*.27,d*.22,h+1.2),(.65,.7,2.5),'Brick')
            b.box((w*.27,d*.22,h+2.48),(.86,.90,.16),'Stone')
        # Attic vent on both gables.
        for side in (0,2): window(side,0,h+.8,.6,.65)
        if style=='townhouse':
            for u in (-w/2+.16,0,w/2-.16): b.box((u,0,h/2),(.24,d+.08,h),'Cream')
    else:
        b.box((0,0,h+.06),(w+.4,d+.4,.2),'Stone')
        b.box((0,0,h+.20),(w-.2,d-.2,.15),'Graphite')
        for side in range(4):
            face_box(side,0,-.05,h+.48,(w if side%2==0 else d)+.2,.22,.65,'Cream')
        b.box((0,1.4,h+.85),(3.2,3.6,1.3),'Stone')
        b.box((0,1.4,h+1.55),(3.5,3.9,.15),'Graphite')
        # Stacked balconies. Slab, opaque lower apron and visibly open railing above.
        for side in (0,2):
            for u in (-balcony_u,balcony_u):
                for level in range(1,floors):
                    z=level*3+.35
                    face_box(side,u,.63,z,2.55,1.50,.18,'Stone')
                    face_box(side,u,1.30,z+.40,2.55,.14,.72,'Brick' if style=='brick' else 'Sage')
                    for s in (-1,1): face_box(side,u+s*1.2,.67,z+.48,.13,1.35,.85,'Stone')
                    face_box(side,u,1.30,z+1.05,2.55,.075,.065,'Metal')
                    if lod==0:
                        for j in range(7): face_box(side,u-1.15+j*2.3/6,1.30,z+.87,.045,.045,.36,'Metal')
        if style=='modern':
            for u in (-balcony_u,balcony_u):
                face_box(0,u,-.005,h/2,3.2,.04,h-.4,'Graphite')
            # Above panels sit behind glazing, preserving facade legibility.
        if lod==0 and style=='brick':
            # Shallow masonry courses accent the plinth and blind side-wall margins.
            for side in range(4):
                span=w if side%2==0 else d
                for j in range(2,60):
                    z=j*.25
                    for s in (-1,1): face_box(side,s*(span/2-.52),.016,z,.5,.035,.022,'Mortar')
    return b.object(spec['id']+'_LOD'+str(lod),mats)

def bounds(obj):
    vs=[obj.matrix_world @ Vector(v) for v in obj.bound_box]
    return {'min':[min(v[i] for v in vs) for i in range(3)],'max':[max(v[i] for v in vs) for i in range(3)]}

def studio(spec, model):
    scene=bpy.context.scene; scene.render.engine='CYCLES'; scene.cycles.samples=24
    scene.cycles.use_denoising=True; scene.render.resolution_x=800; scene.render.resolution_y=760; scene.render.resolution_percentage=100
    scene.world=bpy.data.worlds.new('Studio_World'); scene.world.color=(.38,.38,.38); scene.view_settings.view_transform='AgX'
    floor=MeshBuilder(); floor.box((0,0,-.15),(200,200,.28),'Stone'); floor.object('Studio_Ground',materials_cache)
    bpy.ops.object.light_add(type='AREA',location=(-14,-18,32)); bpy.context.object.data.energy=8500; bpy.context.object.data.shape='DISK'; bpy.context.object.data.size=15
    bpy.context.object.rotation_euler=(Vector((0,0,5))-bpy.context.object.location).to_track_quat('-Z','Y').to_euler()
    bpy.ops.object.light_add(type='SUN',location=(0,0,30)); bpy.context.object.rotation_euler=(.5,-.5,-.4); bpy.context.object.data.energy=2.
    bpy.ops.object.camera_add(); cam=bpy.context.object; scene.camera=cam; cam.data.type='ORTHO'; cam.data.lens=48
    height=spec['floors']*3+2.5; target=Vector((0,0,height*.43)); scale=max(spec['w']*1.35,height*1.4,spec['d']*1.7)
    cam.data.ortho_scale=scale
    for view,direction in [('front',Vector((1.15,-1.6,.9))),('rear',Vector((-1.15,1.6,.9)))]:
        cam.location=target+direction*26; cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
        scene.render.filepath=str(REVIEW/(spec['id']+'-'+view+'.png')); bpy.ops.render.render(write_still=True)

manifest={'revision':'houses-v1','utc':datetime.now(timezone.utc).isoformat(),'exporter':bpy.app.version_string,'units':'metres','sourceAxes':'X right, -Y front, Z up','assets':[]}
selected=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
if selected and (REPORT/'manifest.json').exists():
    manifest['assets']=[a for a in json.loads((REPORT/'manifest.json').read_text(encoding='utf8'))['assets'] if a['style'] not in selected]
for spec in SPECS:
    if selected and spec['style'] not in selected: continue
    bpy.ops.wm.read_factory_settings(use_empty=True); bpy.context.scene.unit_settings.system='METRIC'
    materials_cache=materials(); root=bpy.data.objects.new(spec['id'],None); bpy.context.collection.objects.link(root)
    models=[build(spec,lod,materials_cache) for lod in range(3)]
    for o in models: o.parent=root
    bpy.context.view_layer.update()
    stats=[]
    for o in models:
        o.data.calc_loop_triangles(); stats.append({'name':o.name,'triangles':len(o.data.loop_triangles),'materials':len(o.data.materials),'bounds':bounds(o)})
    # All LODs exported, enabled; prefab builder controls visibility through LODGroup.
    bpy.ops.object.select_all(action='DESELECT'); root.select_set(True)
    for o in models:o.select_set(True)
    bpy.context.view_layer.objects.active=root
    fbx=OUT/(spec['id']+'.fbx')
    bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'EMPTY','MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_anim=False,add_leaf_bones=False)
    for o in models[1:]:o.select_set(False)
    glb=REVIEW/(spec['id']+'.glb')
    bpy.ops.export_scene.gltf(filepath=str(glb),export_format='GLB',use_selection=True)
    for o in models[1:]:o.hide_render=True; o.hide_set(True)
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/(spec['id']+'_v1.blend')),compress=True)
    manifest['assets'].append({**spec,'catalogId':'house.'+spec['style']+'.v1','lods':stats,'sha256':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in [fbx,glb,SOURCE/(spec['id']+'_v1.blend')]},'collision':{'type':'BoxCollider','size':[spec['w'],spec['floors']*3,spec['d']],'center':[0,spec['floors']*1.5,0]},'pivot':'ground centre','interior':False})
    (REPORT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf8')
    studio(spec,models[0])
print('HOUSES_EXPORT_PASS')
