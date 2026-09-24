"""Additive training-ground kit. Blender -b --python tools/build_training_kit.py.
Source: metres, X right/Y forward/Z up; named axis sockets validate Unity import.
Never edits the existing vehicle or environment sources.
"""
import bpy, math, json, hashlib
from pathlib import Path
from datetime import datetime, timezone

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT/'Assets/DrivingSchool/Art/TrainingKit'
OUT.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.unit_settings.system = 'METRIC'
M = {}
for name, rgb in {'Concrete':(.48,.51,.49), 'Asphalt':(.12,.15,.17),
                  'White':(.9,.92,.85), 'Yellow':(.96,.65,.08),
                  'Steel':(.1,.19,.22), 'Teal':(.05,.38,.39),
                  'Glass':(.15,.31,.36), 'Rubber':(.035,.045,.05)}.items():
    mat = bpy.data.materials.new('TK_'+name); mat.diffuse_color=(*rgb,1); mat.use_nodes=True
    mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(*rgb,1)
    mat.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.72
    M[name]=mat

roots=[]
def root(name):
    ob=bpy.data.objects.new(name,None); bpy.context.collection.objects.link(ob); roots.append(ob)
    for n,p in [('Axis_Forward',(0,1,0)),('Axis_Up',(0,0,1))]:
        s=bpy.data.objects.new(n,None); bpy.context.collection.objects.link(s); s.parent=ob; s.location=p
    return ob
def box(r,n,p,d,m):
    bpy.ops.mesh.primitive_cube_add(size=1,location=p); o=bpy.context.object; o.name=n; o.dimensions=d
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    o.data.materials.append(M[m]); o.parent=r; return o
def mesh(r,n,verts,faces,m):
    data=bpy.data.meshes.new(n); data.from_pydata(verts,[],faces); data.update()
    o=bpy.data.objects.new(n,data); bpy.context.collection.objects.link(o); o.parent=r; data.materials.append(M[m]); return o

# 6 m wide, 32 m long: 12 m rise at 10%, 8 m crest, 12 m descent.
r=root('TK_Hill_6x32')
ys=[-16,-4,4,16]; hs=[0,1.2,1.2,0]
verts=[(x,y,z) for y,h in zip(ys,hs) for x,z in [(-3,-.18),(3,-.18),(-3,h),(3,h)]]
faces=[]
for j in range(3):
    a=j*4; b=a+4
    faces.extend([(a+2,a+3,b+3,b+2),(a,b,b+1,a+1),(a,a+2,b+2,b),(a+1,b+1,b+3,a+3)])
faces.extend([(0,1,3,2),(12,14,15,13)])
mesh(r,'DriveSurface_Hill',verts,faces,'Asphalt')
for x in [-3.12,3.12]:
    for y,h in zip(ys,hs): box(r,'RailPost',(x,y,h+.65),(.09,.09,1.3),'Steel')
    for j in range(3):
        dy=ys[j+1]-ys[j]; dh=hs[j+1]-hs[j]
        ob=box(r,'GuardRail',(x,(ys[j]+ys[j+1])/2,(hs[j]+hs[j+1])/2+.8),(.1,math.hypot(dy,dh),.12),'White')
        ob.rotation_euler.x=math.atan2(dh,dy)
for x in [-2.8,2.8]:
    for j in range(3):
        dy=ys[j+1]-ys[j]; dh=hs[j+1]-hs[j]
        ob=box(r,'EdgePaint',(x,(ys[j]+ys[j+1])/2,(hs[j]+hs[j+1])/2+.012),(.12,math.hypot(dy,dh),.009),'Yellow')
        ob.rotation_euler.x=math.atan2(dh,dy)

r=root('TK_GarageFrame_4x7')
for x in [-2,2]:
    for y in [-3.5,3.5]: box(r,'FramePost',(x,y,1.4),(.12,.12,2.8),'Teal')
    box(r,'TopRail',(x,0,2.8),(.12,7.12,.12),'Teal')
    box(r,'RearSideRail',(x,2.3,.6),(.1,2.4,.1),'White')
box(r,'Lintel',(0,-3.5,2.8),(4.12,.12,.22),'Teal')
box(r,'BackRail',(0,3.5,.65),(4.12,.12,.12),'White')

r=root('TK_WheelStop_1p8')
box(r,'WheelStop',(0,0,.10),(1.8,.24,.2),'Rubber')
for x in [-.65,0,.65]: box(r,'Reflector',(x,-.124,.11),(.24,.012,.08),'Yellow')

r=root('TK_Fence_5m')
for x in [-2.5,2.5]: box(r,'FencePost',(x,0,1),(.09,.09,2),'Steel')
for z in [.22,1.75]: box(r,'FenceRail',(0,0,z),(5,.055,.055),'Steel')
for i in range(19): box(r,'Picket',(-2.25+i*.25,0,1),(.025,.025,1.55),'Steel')

r=root('TK_Gate_8m')
for x in [-4.4,4.4]: box(r,'GatePillar',(x,0,2.2),(.36,.36,4.4),'Teal')
box(r,'GateHeader',(0,0,4.25),(9.2,.4,.65),'Teal')
box(r,'HeaderInset',(0,-.211,4.25),(7,.018,.4),'White')

r=root('TK_Lamp_7m')
box(r,'Foot',(0,0,.12),(.4,.4,.24),'Concrete')
box(r,'Mast',(0,0,3.5),(.13,.13,7),'Steel')
box(r,'Arm',(0,.65,6.95),(.12,1.4,.12),'Steel')
box(r,'LightHousing',(0,1.25,6.9),(.5,.85,.14),'White')

r=root('TK_InstructorShelter')
box(r,'Plinth',(0,0,.1),(5,3,.2),'Concrete')
for x in [-2.3,2.3]:
    for y in [-1.3,1.3]: box(r,'ShelterPost',(x,y,1.5),(.1,.1,3),'Teal')
box(r,'Canopy',(0,0,3),(5.3,3.3,.18),'Teal')
box(r,'BackPanel',(0,1.3,1.6),(4.6,.08,2.5),'Glass')
box(r,'Bench',(0,.75,.55),(3.8,.45,.12),'White')
for x in [-1.5,1.5]: box(r,'BenchLeg',(x,.75,.3),(.08,.3,.5),'Steel')

r=root('TK_ZoneBoard')
for x in [-.65,.65]: box(r,'BoardPost',(x,0,1.25),(.07,.07,2.5),'Steel')
box(r,'BoardFace',(0,0,2.15),(1.8,.12,.9),'Teal')

catalog=[]
for r in roots:
    bpy.ops.object.select_all(action='DESELECT'); r.select_set(True)
    for c in r.children_recursive:c.select_set(True)
    path=OUT/(r.name+'.fbx')
    bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,object_types={'MESH','EMPTY'},
        add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',
        apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS')
    tris=0
    for o in r.children_recursive:
        if o.type=='MESH': o.data.calc_loop_triangles(); tris+=len(o.data.loop_triangles)
    catalog.append({'name':r.name,'triangles':tris,'sha256':hashlib.sha256(path.read_bytes()).hexdigest()})
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'ArtSource/DS_TrainingKit.blend'))
report={'revision':1,'utc':datetime.now(timezone.utc).isoformat(),'units':'metres',
        'blender':bpy.app.version_string,'modules':catalog,'hill':{'riseM':1.2,'slope':.1,'widthM':6,'lengthM':32}}
(ROOT/'artifacts/reports/training-kit.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('TRAINING_KIT_EXPORTED',len(catalog))
