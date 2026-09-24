"""Original traffic props. Blender 5: -b --python tools/build_traffic_assets.py.
Metres, Z up, front -Y. Only writes the dedicated Traffic asset directories.
"""
import bpy, math, json, hashlib
from pathlib import Path
from datetime import datetime, timezone
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'Assets/DrivingSchool/Art/Traffic'
REVIEW = ROOT / 'artifacts/visual-review/traffic'
for p in [OUT, REVIEW/'models', ROOT/'ArtSource', ROOT/'artifacts/reports']:
    p.mkdir(parents=True, exist_ok=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
M = {}
def mat(name, color, metal=0, rough=.4, emission=0):
    m=bpy.data.materials.new('Traffic_'+name); m.diffuse_color=(*color,1); m.use_nodes=True
    bs=m.node_tree.nodes.get('Principled BSDF')
    bs.inputs['Base Color'].default_value=(*color,1)
    bs.inputs['Metallic'].default_value=metal; bs.inputs['Roughness'].default_value=rough
    bs.inputs['Emission Color'].default_value=(*color,1); bs.inputs['Emission Strength'].default_value=emission
    M[name]=m
for args in [('White',(.92,.95,.92)),('Red',(.72,.014,.025)),('Blue',(.012,.12,.57)),
             ('Yellow',(1,.68,.016)),('Ink',(.008,.012,.018)),('Steel',(.39,.46,.5),.78,.32),
             ('Housing',(.025,.034,.044),.2,.32),('Gasket',(.006,.008,.01)),
             ('LensRed',(.11,.003,.004),.1,.22),('LensAmber',(.15,.057,.002),.1,.22),
             ('LensGreen',(.002,.07,.03),.1,.22),('RedOn',(1,.015,.009),0,.22,2),
             ('AmberOn',(1,.38,.006),0,.22,2),('GreenOn',(.015,.9,.3),0,.22,2)]: mat(*args)
current=None
TOWN = 'ЛЕСНОЙ'
town_font=bpy.data.fonts.load('C:/Windows/Fonts/arialbd.ttf')
def finish(o,name,material):
    o.name=name; o.parent=current
    if material: o.data.materials.append(M[material])
    return o
def box(name,loc,dim,material,bevel=0):
    bpy.ops.mesh.primitive_cube_add(size=1,location=loc); o=finish(bpy.context.object,name,material)
    o.scale=dim; bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    if bevel:
        b=o.modifiers.new('Manufactured edges','BEVEL'); b.width=bevel; b.segments=2
        bpy.context.view_layer.objects.active=o; bpy.ops.object.modifier_apply(modifier=b.name)
        n=o.modifiers.new('Weighted normals','WEIGHTED_NORMAL'); bpy.ops.object.modifier_apply(modifier=n.name)
    return o
def cyl(name,loc,r,depth,material,axis='Z',n=32):
    bpy.ops.mesh.primitive_cylinder_add(vertices=n,radius=r,depth=depth,location=loc)
    o=finish(bpy.context.object,name,material)
    if axis=='Y': o.rotation_euler.x=math.pi/2
    for p in o.data.polygons: p.use_smooth=len(p.vertices)==4
    return o
def polygon(name,coords,y,depth,material):
    n=len(coords); verts=[(x,y+dy,z) for dy in (0,depth) for x,z in coords]
    faces=[tuple(range(n)),tuple(reversed(range(n,2*n)))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    me=bpy.data.meshes.new(name); me.from_pydata(verts,[],faces); me.update()
    o=bpy.data.objects.new(name,me); bpy.context.collection.objects.link(o); finish(o,name,material)
    # Correct every closed plate's normals, including arbitrary input winding.
    import bmesh
    bm=bmesh.new(); bm.from_mesh(me); bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces)); bm.to_mesh(me); bm.free()
    return o
def shape(n,r,z,angle=0):
    return [(r*math.cos(angle+i*2*math.pi/n),z+r*math.sin(angle+i*2*math.pi/n)) for i in range(n)]
def lettering(body,z,size,material='White',x=0,y=-.097):
    bpy.ops.object.text_add(location=(x,y,z)); o=finish(bpy.context.object,'Legend_'+body,material)
    o.rotation_euler=(math.pi/2,0,0); o.data.body=body; o.data.align_x='CENTER'; o.data.align_y='CENTER'
    o.data.size=size; o.data.extrude=.0007; o.data.resolution_u=3
    if any(ord(c)>127 for c in body): o.data.font=town_font
    bpy.ops.object.convert(target='MESH'); return bpy.context.object
def stroke(name,points,width,material,y=-.102):
    for i,((x,z),(xx,zz)) in enumerate(zip(points,points[1:])):
        dx=xx-x; dz=zz-z; length=math.hypot(dx,dz); px=-dz/length*width/2; pz=dx/length*width/2
        polygon(name+str(i),[(x+px,z+pz),(x-px,z-pz),(xx-px,zz-pz),(xx+px,zz+pz)],y,.0015,material)
def person(cx,z,scale,material,y=-.105,walking=True):
    cyl('Pictogram_Head',(cx,y,z+.22*scale),.055*scale,.002,material,'Y',16)
    if walking:
        paths=[[(0,.15),(-.035,-.02),(-.14,-.2)], [(-.035,-.02),(.12,-.19)],
               [(-.015,.12),(.11,.02)], [(-.015,.12),(-.12,.04),(-.17,.08)]]
    else:
        paths=[[(0,.14),(0,-.03)], [(-.018,-.03),(-.05,-.22)],[(.018,-.03),(.05,-.22)],
               [(-.09,-.04),(-.09,.1),(.09,.1),(.09,-.04)]]
    for p in paths: stroke('Pictogram',[(cx+x*scale,z+zz*scale) for x,zz in p],.045*scale,material,y)
def arrow(cx,z,scale=1,angle=0,material='White',y=-.104):
    coords=[(-.045,-.23),(.045,-.23),(.045,.07),(.14,.07),(0,.24),(-.14,.07),(-.045,.07)]
    a=math.radians(angle)
    polygon('DirectionArrow',[(cx+scale*(x*math.cos(a)-zz*math.sin(a)),z+scale*(x*math.sin(a)+zz*math.cos(a))) for x,zz in coords],y,.002,material)
assets=[]
def root(name,label,kind):
    global current
    current=bpy.data.objects.new(name,None); bpy.context.collection.objects.link(current)
    current['catalogId']=name; current['label']=label; current['kind']=kind
    marker=bpy.data.objects.new('Socket_Front',None); bpy.context.collection.objects.link(marker); marker.parent=current; marker.location=(0,-1,0)
    up=bpy.data.objects.new('Socket_Up',None); bpy.context.collection.objects.link(up); up.parent=current; up.location=(0,0,1)
    assets.append((current,label,kind)); return current
def post(height,r=.038):
    cyl('Post',(0,.055,height/2),r,height,'Steel')
    box('BasePlate',(0,.055,.017),(.24,.24,.034),'Steel',.012)
    for x in [-.083,.083]:
        for y in [-.028,.138]: cyl('AnchorBolt',(x,y,.045),.012,.023,'Steel',n=6)
    cyl('PostCap',(0,.055,height+.008),r+.003,.016,'Steel')
def sign(name,label,kind):
    root('DS_Sign_'+name,label,'sign'); post(2.75)
    z=2.45
    for h in [z-.16,z+.16]:
        box('RearRail',(0,.008,h),(.4,.035,.04),'Steel',.005)
        box('PostClamp',(0,.06,h),(.105,.11,.032),'Steel',.005)
    if kind=='stop': outline=shape(8,.405,z,math.pi/8); border='White'; inner='Red'; ratio=.91
    elif kind=='yield': outline=shape(3,.46,z,-math.pi/2); border='Red'; inner='White'; ratio=.77
    elif kind=='priority': outline=shape(4,.435,z,0); border='White'; inner='Yellow'; ratio=.78
    elif kind in ('crossing','parking'): outline=[(-.36,z-.36),(.36,z-.36),(.36,z+.36),(-.36,z+.36)]; border='White'; inner='Blue'; ratio=.92
    else: outline=shape(64,.355,z); border='Red' if kind in ('speed','entry','no_stop','no_parking') else 'White'; inner='White' if kind=='speed' else ('Red' if kind=='entry' else 'Blue'); ratio=.81 if kind in ('speed','no_stop','no_parking') else .96
    polygon('Plate_Back',outline,-.065,.026,'Steel')
    polygon('Face_Border',[(x*.99,z+(zz-z)*.99) for x,zz in outline],-.081,.008,border)
    polygon('Face_Field',[(x*ratio,z+(zz-z)*ratio) for x,zz in outline],-.091,.004,inner)
    if kind=='stop': lettering('STOP',z,.23)
    if kind=='speed': lettering(name[-2:],z,.37,'Ink')
    if kind=='entry': box('NoEntryBar',(0,-.1,z),(.49,.004,.113),'White')
    if kind=='parking': lettering('P',z,.55)
    if kind in ('no_stop','no_parking'):
        stroke('ProhibitionSlash',[(-.218,z+.218),(.218,z-.218)],.055,'Red',-.106)
        if kind=='no_stop': stroke('ProhibitionCross',[(-.218,z-.218),(.218,z+.218)],.055,'Red',-.106)
    if kind=='crossing':
        polygon('CrossingTriangle',[(-.292,z-.26),(.292,z-.26),(0,z+.292)],-.098,.003,'White')
        for xx in [-.17,-.085,0,.085,.17]: box('Zebra',(xx,-.104,z-.189),(.053,.003,.044),'Ink')
        person(0,z-.023,.7,'Ink',-.11)
    if kind=='straight': arrow(0,z)
    if kind=='right': arrow(0,z,angle=-90)
    if kind=='roundabout':
        for a in [0,120,240]:
            # Three curved arrows following a counter-clockwise ring.
            start=math.radians(a); end=math.radians(a+76)
            arc=[(.18*math.cos(start+(end-start)*i/12),z+.18*math.sin(start+(end-start)*i/12)) for i in range(13)]
            stroke('RoundaboutArc',arc,.042,'White')
            cx=.18*math.cos(end); cz=z+.18*math.sin(end)
            tx=-math.sin(end); tz=math.cos(end); nx=math.cos(end); nz=math.sin(end)
            polygon('RoundaboutTip',[(cx+tx*.075,cz+tz*.075),(cx-tx*.012+nx*.075,cz-tz*.012+nz*.075),(cx-tx*.012-nx*.075,cz-tz*.012-nz*.075)],-.105,.002,'White')
    return current
for args in [('Stop','STOP','stop'),('Yield','Уступите дорогу','yield'),('Priority','Главная дорога','priority'),
             ('Speed30','Ограничение 30','speed'),('Speed40','Ограничение 40','speed'),('Speed60','Ограничение 60','speed'),
             ('NoEntry','Въезд запрещён','entry'),('Crossing','Пешеходный переход','crossing'),('Parking','Парковка','parking'),
             ('Straight','Движение прямо','straight'),('Right','Движение направо','right'),('Roundabout','Круговое движение','roundabout')]: sign(*args)

def visor(x,z):
    n=24; r=.128; verts=[]
    for y in [-.174,-.35]:
        verts.extend((x+r*math.cos(i*math.pi/n),y,z+r*math.sin(i*math.pi/n)) for i in range(n+1))
    me=bpy.data.meshes.new('Visor'); me.from_pydata(verts,[],[(i,i+1,i+n+2,i+n+1) for i in range(n)]); me.update()
    o=bpy.data.objects.new('SunVisor',me); bpy.context.collection.objects.link(o); finish(o,'SunVisor','Housing')
    mod=o.modifiers.new('Thickness','SOLIDIFY'); mod.thickness=.007
    bpy.context.view_layer.objects.active=o; bpy.ops.object.modifier_apply(modifier=mod.name)
def lamp(x,z,color,symbol=None):
    box('Module_'+color,(x,-.065,z),(.32,.24,.33),'Housing',.03)
    cyl('LensSeal',(x,-.191,z),.13,.023,'Gasket','Y',48)
    cyl('Lens_'+color,(x,-.207,z),.113,.018,'Lens'+color,'Y',48)
    visor(x,z)
    # Emissive discs/icons have dedicated renderer names for runtime switching.
    before=set(bpy.data.objects)
    if symbol=='person': person(x,z,.37,color+'On',-.222,color=='Green')
    elif symbol=='arrow': arrow(x,z,.37,-90,'GreenOn',-.223)
    else: cyl('Lit_'+color,(x,-.22,z),.104,.003,color+'On','Y',48)
    for o in set(bpy.data.objects)-before: o.name='Lamp_'+color+'_'+o.name
    # Fresnel-like fine lens ribs remain visible when switched off.
    for dx in [-.075,-.05,-.025,0,.025,.05,.075]:
        h=2*math.sqrt(.098**2-dx**2)
        box('LensRib',(x+dx,-.225,z),(.0015,.002,h),'Lens'+color)
def signal(name,label,pedestrian=False,extra=False):
    root('DS_Signal_'+name,label,'pedestrian' if pedestrian else 'vehicle'); post(3.35,.057)
    zs=[2.92,2.57] if pedestrian else [3.05,2.70,2.35]
    mid=(zs[0]+zs[-1])/2
    box('Backplate',(0,.071,mid),(.43,.035,len(zs)*.35+.12),'Housing',.045)
    for zz in [zs[0],zs[-1]]: box('MountBracket',(0,.035,zz),(.12,.23,.045),'Steel',.01)
    for zz,color in zip(zs,['Red','Green'] if pedestrian else ['Red','Amber','Green']): lamp(0,zz,color,'person' if pedestrian else None)
    box('ServiceDoor',(0,.094,mid),(.25,.018,.27),'Housing',.009)
    if extra:
        box('SideBracket',(.25,.04,2.35),(.45,.06,.065),'Steel',.008)
        before=set(bpy.data.objects)
        lamp(.37,2.35,'Green','arrow')
        for o in set(bpy.data.objects)-before:
            if o.name.startswith('Lamp_Green'): o.name=o.name.replace('Lamp_Green','Lamp_Arrow')
    return current
signal('Vehicle','Транспортный светофор')
signal('VehicleArrow','Светофор с дополнительной секцией',extra=True)
signal('Pedestrian','Пешеходный светофор',pedestrian=True)

# Additional signs and modular supplementary plates.
sign('NoStopping','Остановка запрещена','no_stop')
sign('NoParking','Стоянка запрещена','no_parking')

def town(exit=False):
    root('DS_Sign_Town'+('Exit' if exit else 'Entry'),('Конец' if exit else 'Начало')+' населённого пункта: '+TOWN,'town')
    for x in [-.63,.63]:
        before=set(bpy.data.objects); post(2.75)
        for o in set(bpy.data.objects)-before: o.location.x+=x
    z=2.45
    box('Plate_Back',(0,-.055,z),(1.9,.025,.58),'Steel',.04)
    box('TownBorder',(0,-.074,z),(1.88,.012,.56),'Ink',.033)
    box('TownFace',(0,-.085,z),(1.844,.008,.524),'White',.026)
    lettering(TOWN,z,.34,'Ink',y=-.094)
    for zz in [z-.16,z+.16]: box('RearRail',(0,.0,zz),(1.65,.04,.042),'Steel',.005)
    if exit: stroke('TownExitSlash',[(-.89,z-.24),(.89,z+.24)],.042,'Red',-.106)
town(); town(True)

def plaque_geometry(mode,z=.18):
    w,h=(.64,.36)
    box('Plate_Back',(0,-.055,z),(w,.025,h),'Steel',.018)
    box('PlaqueBorder',(0,-.074,z),(w-.008,.012,h-.008),'Ink',.014)
    box('PlaqueFace',(0,-.085,z),(w-.028,.008,h-.028),'White',.01)
    box('RearRail',(0,.002,z),(.4,.045,.045),'Steel',.005)
    box('PostClamp',(0,.053,z),(.1,.11,.031),'Steel',.005)
    if mode.startswith('distance'): lettering(mode[8:]+' м',z,.19,'Ink',y=-.098)
    if mode=='zone100':
        arrow(-.227,z,.43,material='Ink'); lettering('100 м',z,.15,'Ink',x=.052,y=-.099)
    if mode=='start': arrow(0,z,.51,material='Ink')
    if mode=='end': arrow(0,z,.51,180,'Ink')
    if mode=='continue':
        arrow(0,z+.047,.36,material='Ink'); arrow(0,z-.047,.36,180,'Ink')
    if mode in ('left','right'): arrow(0,z,.75,90 if mode=='left' else -90,'Ink')
    if mode=='both':
        arrow(-.075,z,.57,90,'Ink'); arrow(.075,z,.57,-90,'Ink')
    if mode in ('left100','right100'):
        lettering('100 м',z+.069,.15,'Ink',y=-.099)
        arrow(0,z-.082,.72,90 if mode=='left100' else -90,'Ink')
plaque_specs=[('Distance100','Расстояние до объекта 100 м','distance100'),('Distance300','Расстояние до объекта 300 м','distance300'),
    ('Zone100','Зона действия 100 м','zone100'),('ZoneStart','Начало зоны действия','start'),('ZoneEnd','Конец зоны действия','end'),
    ('ZoneContinue','Продолжение зоны действия','continue'),('DirectionLeft','Направление налево','left'),
    ('DirectionRight','Направление направо','right'),('DirectionBoth','Направления налево и направо','both'),
    ('ZoneLeft100','Зона действия налево 100 м','left100'),('ZoneRight100','Зона действия направо 100 м','right100')]
for name,label,mode in plaque_specs:
    root('DS_Plaque_'+name,label,'plaque'); plaque_geometry(mode)
for name,label,kind,mode in [('NoStopping_Zone100','Остановка запрещена — 100 м','no_stop','zone100'),
                              ('NoParking_ZoneEnd','Конец зоны запрета стоянки','no_parking','end')]:
    sign(name,label,kind); plaque_geometry(mode,1.83)

def select_tree(o):
    bpy.ops.object.select_all(action='DESELECT')
    for child in [o]+list(o.children_recursive): child.select_set(True)
    bpy.context.view_layer.objects.active=o
def measure(o):
    bpy.context.view_layer.update(); points=[]; tris=0; mats=set()
    for child in o.children_recursive:
        if child.type!='MESH': continue
        child.data.calc_loop_triangles(); tris+=len(child.data.loop_triangles)
        points.extend(child.matrix_world@v.co for v in child.data.vertices)
        mats.update(m.name for m in child.data.materials)
    lo=[min(p[i] for p in points) for i in range(3)]; hi=[max(p[i] for p in points) for i in range(3)]
    return dict(boundsMin=lo,boundsMax=hi,dimensionsM=[hi[i]-lo[i] for i in range(3)],triangles=tris,materials=sorted(mats))
manifest={'revision':'traffic-v1','utc':datetime.now(timezone.utc).isoformat(),'exporter':bpy.app.version_string,'units':'metres','sourceAxes':'X right, Z up, front -Y','lod':'Not authored','assets':[]}
for o,label,kind in assets:
    select_tree(o); stats=measure(o)
    fbx=OUT/(o.name+'.fbx'); glb=REVIEW/'models'/(o.name+'.glb')
    bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,add_leaf_bones=False,bake_anim=False)
    bpy.ops.export_scene.gltf(filepath=str(glb),export_format='GLB',use_selection=True,export_apply=True)
    manifest['assets'].append(dict(catalogId=o.name,label=label,kind=kind,**stats,fbx=str(fbx.relative_to(ROOT)),fbxSha256=hashlib.sha256(fbx.read_bytes()).hexdigest(),glb=str(glb.relative_to(ROOT)),glbSha256=hashlib.sha256(glb.read_bytes()).hexdigest()))

# Presentation arrangement is stored in the editable source; individual exports stay at origin.
for i,(o,_,kind) in enumerate(assets):
    o.location=((i%6-2.5)*2.25,(i//6)*2.2,0)
    for c in o.children_recursive:
        if c.name.startswith('Lamp_') and not c.name.startswith('Lamp_Red'): c.hide_render=True
current=None
mat('Ground',(.105,.135,.16),.1,.65)
box('DisplayGround',(0,1.5,-.08),(200,200,.12),'Ground')
scene=bpy.context.scene; scene.render.engine='CYCLES'; scene.cycles.samples=32
scene.world.color=(.3,.3,.3)
def area(name,loc,power,size):
    data=bpy.data.lights.new(name,'AREA'); data.energy=power; data.shape='DISK'; data.size=size
    o=bpy.data.objects.new(name,data); scene.collection.objects.link(o); o.location=loc
    o.rotation_euler=(Vector((0,1,1.5))-o.location).to_track_quat('-Z','Y').to_euler()
area('Key',(-3,-6,10),2300,8); area('Fill',(7,-1,6),1400,7); area('Rim',(0,7,9),2400,6)
data=bpy.data.cameras.new('ReviewCamera'); cam=bpy.data.objects.new('ReviewCamera',data); scene.collection.objects.link(cam); scene.camera=cam
data.type='ORTHO'; scene.render.image_settings.file_format='PNG'; scene.view_settings.view_transform='AgX'
def render(name,pos,target,scale,w,h):
    cam.location=pos; cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler(); data.ortho_scale=scale
    scene.render.resolution_x=w; scene.render.resolution_y=h; scene.render.resolution_percentage=100
    scene.render.filepath=str(REVIEW/(name+'.png')); bpy.ops.render.render(write_still=True)
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'ArtSource/DS_TrafficKit.blend'))
manifest['sourceSha256']=hashlib.sha256((ROOT/'ArtSource/DS_TrafficKit.blend').read_bytes()).hexdigest()
(ROOT/'artifacts/reports/traffic-manifest.json').write_text(json.dumps(manifest,indent=2,ensure_ascii=False),encoding='utf8')
def show(indices,aspect='Red'):
    for i,(o,_,_) in enumerate(assets):
        for c in o.children_recursive:
            c.hide_render=i not in indices or (c.name.startswith('Lamp_') and not (c.name.startswith('Lamp_'+aspect) or (aspect=='Green' and c.name.startswith('Lamp_Arrow'))))
for i,(o,_,_) in enumerate(assets[:15]):
    o.location=((i%6-2.5)*1.25,(i//6)*2.0,0) if i<12 else ((i-13)*1.65,4.2,0)
show(range(15))
render('overview',(8,-17,11),(0,1.7,1.55),11,1500,1000)
show(range(12))
for i,(o,_,_) in enumerate(assets[:12]):
    o.location=((i%6-2.5)*1.25,0,(i//6)*1.15)
    for c in o.children_recursive:
        if c.name.startswith(('Post','BasePlate','AnchorBolt','RearRail')): c.hide_render=True
render('signs-front',(0,-16,3),(0,0,3),8.3,1500,600)
show(range(12,15))
render('signals-red',(5,-9,6),(0,4.2,2.45),5.7,1100,900)
show(range(12,15),'Green')
render('signals-green',(0,-9,4.5),(0,4.2,2.4),5.5,1100,900)
show(range(12,15),'Amber')
render('signals-amber',(0,-9,4.5),(0,4.2,2.4),5.5,1100,900)
extended=[15,16,17,18,30,31]
for j,i in enumerate(extended): assets[i][0].location=((j%3-1)*2.5,(j//3)*2,0)
show(extended)
render('additional-signs',(7,-17,10),(0,.9,1.5),9.6,1500,1000)
render('additional-rear',(-7,15,8),(0,.9,1.6),9.5,1100,800)
show(range(19,30))
for j,i in enumerate(range(19,30)): assets[i][0].location=((j%4-1.5)*.95,0,(2-j//4)*.64+.1)
render('plaques-front',(0,-10,1),(0,0,1),4.15,1500,760)
print('TRAFFIC_ASSETS_PASS',len(assets),flush=True)
