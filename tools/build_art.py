"""Original geometry for Driving School Sim. Run with Blender 5 --background --python.
Coordinates in source: X right, Y forward, Z up; metres. No external art inputs.
"""
import bpy, math, json, random, sys, os
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / 'ArtSource'
OUT = ROOT / 'Assets/DrivingSchool/Art'
WEB = ROOT / 'artifacts/visual-review'
for p in (ART, OUT, WEB/'renders', WEB/'models', ROOT/'artifacts/reports'):
    p.mkdir(parents=True, exist_ok=True)
random.seed(12)
M = {}

def mat(name, color, metal=0, rough=.45, emission=0, alpha=1):
    m=bpy.data.materials.new(name); m.use_nodes=True
    m.diffuse_color=(*color,alpha)
    p=m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value=(*color,alpha)
    p.inputs['Metallic'].default_value=metal; p.inputs['Roughness'].default_value=rough
    p.inputs['Alpha'].default_value=alpha
    if alpha<1:
        m.surface_render_method='DITHERED'
    if emission:
        p.inputs['Emission Color'].default_value=(*color,1)
        p.inputs['Emission Strength'].default_value=emission
    M[name]=m; return m

def materials():
    mat('Paint_Atlantic',(.085,.23,.29),.7,.25)
    mat('Paint_White',(.8,.83,.8),.15,.28)
    mat('Rubber',(.018,.024,.026),0,.82)
    mat('Interior_Graphite',(.038,.048,.052),0,.7)
    mat('Interior_Stone',(.32,.35,.32),0,.83)
    mat('Leather',(.07,.085,.08),0,.68)
    mat('Stitch',(.55,.58,.48),0,.8)
    mat('Satin_Aluminium',(.4,.46,.47),.85,.28)
    mat('Chrome',(.7,.76,.79),.95,.13)
    mat('Glass',(.24,.44,.48),.05,.12,alpha=.16)
    mat('Mirror',(.53,.65,.7),1,.035)
    mat('Lamp_White',(.83,.94,1),.1,.18,3)
    mat('Lamp_Red',(.8,.025,.018),.1,.23,2)
    mat('Lamp_Amber',(1,.3,.025),.1,.25,2)
    mat('Display',(.018,.06,.08),.1,.3)
    mat('Ink',(.72,.89,.91),0,.6,1.2)
    mat('Asphalt',(.11,.13,.145),0,.94)
    mat('Line_White',(.87,.87,.79),0,.9)
    mat('Line_Yellow',(.95,.62,.08),0,.8)
    mat('Concrete',(.43,.46,.44),0,.9)
    mat('Paving',(.58,.58,.52),0,.94)
    mat('Brick',(.44,.26,.17),0,.9)
    mat('Plaster',(.68,.67,.56),0,.9)
    mat('Facade_Dark',(.21,.28,.29),0,.85)
    mat('Window',(.065,.16,.2),.45,.18)
    mat('Grass',(.19,.27,.14),0,1)
    mat('Leaf',(.13,.24,.12),0,1)
    mat('Trunk',(.15,.105,.07),0,1)
    mat('Sign_Blue',(.025,.22,.53),0,.55)
    mat('Sign_Red',(.65,.045,.035),0,.55)
    mat('Stage',(.16,.19,.2),.05,.7)

def finish(o,name,material=None,parent=None):
    o.name=name
    if material:o.data.materials.append(M[material])
    if parent:o.parent=parent
    return o

def empty(name,loc=(0,0,0),parent=None):
    o=bpy.data.objects.new(name,None); bpy.context.collection.objects.link(o)
    o.location=loc
    if parent:o.parent=parent
    return o

def box(name,loc,dim,material,bevel=0,parent=None):
    bpy.ops.mesh.primitive_cube_add(size=1,location=loc); o=bpy.context.object
    o.dimensions=dim; bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    if bevel:
        mod=o.modifiers.new('Manufactured edge','BEVEL');mod.width=bevel;mod.segments=3
        o.modifiers.new('Corner normals','WEIGHTED_NORMAL')
    return finish(o,name,material,parent)

def cyl(name,loc,radius,depth,material,axis='Z',vertices=32,parent=None):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices,radius=radius,depth=depth,location=loc)
    o=bpy.context.object
    if axis=='X':o.rotation_euler[1]=math.pi/2
    if axis=='Y':o.rotation_euler[0]=math.pi/2
    for f in o.data.polygons:f.use_smooth=True
    return finish(o,name,material,parent)

def torus(name,loc,major,minor,material,axis='Z',parent=None):
    bpy.ops.mesh.primitive_torus_add(major_radius=major,minor_radius=minor,major_segments=48,minor_segments=12,location=loc)
    o=bpy.context.object
    if axis=='X':o.rotation_euler[1]=math.pi/2
    if axis=='Y':o.rotation_euler[0]=math.pi/2
    for f in o.data.polygons:f.use_smooth=True
    return finish(o,name,material,parent)

def mesh(name,verts,faces,material,parent=None):
    me=bpy.data.meshes.new(name);me.from_pydata(verts,[],faces);me.update()
    o=bpy.data.objects.new(name,me);bpy.context.collection.objects.link(o)
    return finish(o,name,material,parent)

def tube(name,pts,radius,material,parent=None,closed=False):
    c=bpy.data.curves.new(name,'CURVE');c.dimensions='3D';c.bevel_depth=radius;c.bevel_resolution=2
    s=c.splines.new('POLY');s.points.add(len(pts)-1)
    for p,co in zip(s.points,pts):p.co=(*co,1)
    s.use_cyclic_u=closed
    o=bpy.data.objects.new(name,c);bpy.context.collection.objects.link(o)
    return finish(o,name,material,parent)

def text3(name,body,loc,size,material,rotation=(math.pi/2,0,0),parent=None):
    c=bpy.data.curves.new(name,'FONT');c.body=body;c.size=size;c.align_x='CENTER';c.align_y='CENTER';c.extrude=.0003
    o=bpy.data.objects.new(name,c);bpy.context.collection.objects.link(o);o.location=loc;o.rotation_euler=rotation
    return finish(o,name,material,parent)

def clear():
    bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)

def camera(name,loc,target,lens=50,ortho=None):
    bpy.ops.object.camera_add(location=loc);o=bpy.context.object;o.name=name
    o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler()
    o.data.lens=lens;o.data.clip_end=5000
    if ortho:o.data.type='ORTHO';o.data.ortho_scale=ortho
    bpy.context.scene.camera=o;return o

def area(name,loc,target,power,size,color=(1,1,1)):
    bpy.ops.object.light_add(type='AREA',location=loc);o=bpy.context.object;o.name=name;o.data.energy=power;o.data.shape='DISK';o.data.size=size;o.data.color=color
    o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler();return o

def setup_render():
    s=bpy.context.scene;s.render.engine='CYCLES';s.cycles.samples=24
    s.cycles.use_denoising=True
    s.render.resolution_x=1600;s.render.resolution_y=1000;s.render.resolution_percentage=100
    s.world.color=(.22,.22,.22)
    s.world.use_nodes=True;s.world.node_tree.nodes['Background'].inputs[0].default_value=(.32,.39,.44,1)
    s.world.node_tree.nodes['Background'].inputs[1].default_value=.45
    s.view_settings.view_transform='AgX'
    try:
        prefs=bpy.context.preferences.addons['cycles'].preferences;prefs.compute_device_type='OPTIX';prefs.get_devices()
        for d in prefs.devices:d.use=d.type!='CPU'
        if any(d.use for d in prefs.devices):s.cycles.device='GPU'
    except Exception:pass

def render(name,loc,target,lens=50,ortho=None):
    camera('Camera_'+name,loc,target,lens,ortho)
    bpy.context.scene.render.filepath=str(WEB/'renders'/f'{name}.png')
    bpy.ops.render.render(write_still=True)

def export(name,root):
    # Keep pivots and text geometry in interchange files. Applying transforms to
    # animated mesh children would erase their authored local pivot, so don't.
    bpy.ops.object.select_all(action='DESELECT')
    objs=[root]+list(root.children_recursive)
    for o in objs:o.select_set(True)
    bpy.context.view_layer.objects.active=root
    bpy.ops.export_scene.gltf(filepath=str(WEB/'models'/f'{name}.glb'),export_format='GLB',use_selection=True,export_apply=True)
    # FBX exporter skips FONT/CURVE: convert temporary duplicates, not source.
    originals=list(objs);copies=[]
    for o in originals:
        if o.type in {'FONT','CURVE'}:
            deps=bpy.context.evaluated_depsgraph_get()
            me=bpy.data.meshes.new_from_object(o.evaluated_get(deps))
            copy=bpy.data.objects.new(o.name+'_Mesh',me);bpy.context.collection.objects.link(copy)
            copy.parent=o.parent;copy.matrix_world=o.matrix_world
            o.select_set(False);copy.select_set(True);copies.append(copy)
    bpy.ops.export_scene.fbx(filepath=str(OUT/f'{name}.fbx'),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='Y',axis_up='Z',apply_unit_scale=True,add_leaf_bones=False,bake_anim=False)
    for o in copies:bpy.data.objects.remove(o,do_unlink=True)
    report=[]
    for o in originals:
        if o.type=='MESH':report.append({'name':o.name,'vertices':len(o.data.vertices),'triangles':sum(len(p.vertices)-2 for p in o.data.polygons),'materials':[m.name for m in o.data.materials if m]})
    (ROOT/'artifacts/reports'/f'{name}-geometry.json').write_text(json.dumps({'source':'Original procedural authored geometry','objects':report,'trianglesBase':sum(o['triangles'] for o in report)},indent=2),encoding='utf-8')

def car():
    root=empty('DS_Sedan_A');root['design']='Original DS-01 / 4.50 x 1.80 x 1.48 metres'
    # Rounded longitudinal body sections, with an actual empty cabin and arches.
    sections=[(-2.25,.70,.75),(-2.08,.82,.83),(-1.55,.88,.88),(-.9,.9,.87),(.6,.9,.9),(1.45,.86,.87),(2.06,.82,.79),(2.25,.73,.74)]
    verts=[]
    for y,w,h in sections:
        verts.extend([(-w*.89,y,.28),(-w,y,.46),(-w*.98,y,h-.07),(-w*.85,y,h),(w*.85,y,h),(w*.98,y,h-.07),(w,y,.46),(w*.89,y,.28)])
    faces=[]
    for i in range(len(sections)-1):
        for j in range(8):faces.append((i*8+j,i*8+(j+1)%8,(i+1)*8+(j+1)%8,(i+1)*8+j))
    faces.extend([tuple(reversed(range(8))),tuple((len(sections)-1)*8+j for j in range(8))])
    body=mesh('Body_Shell',verts,faces,'Paint_Atlantic',root)
    cutters=[]
    cutters.append(box('CabinCut',(0,-.12,1.27),(1.55,2.22,1.2),'Rubber',.07))
    for y in (-1.36,1.36):cutters.append(cyl('ArchCut',(0,y,.34),.37,2.4,'Rubber','X',48))
    for cutter in cutters:
        mod=body.modifiers.new('Functional opening','BOOLEAN');mod.operation='DIFFERENCE';mod.object=cutter
        bpy.context.view_layer.objects.active=body
        bpy.ops.object.modifier_apply(modifier=mod.name);bpy.data.objects.remove(cutter,do_unlink=True)
    mod=body.modifiers.new('Body edge','BEVEL');mod.width=.025;mod.segments=3
    body.modifiers.new('Body normal','WEIGHTED_NORMAL')
    box('Cabin_Floor',(0,-.08,.39),(1.52,2.43,.08),'Interior_Graphite',.03,root)
    # roof skin and pillars; glazing is separate and never a solid cube.
    roofverts=[]
    for y in (-.86,-.65,-.2,.30,.56):
        for x in (-.7,-.56,0,.56,.7):roofverts.append((x,y,1.43+.065*(1-(x/.7)**2)-.025*((y+.15)/.71)**2))
    roof=mesh('Roof',roofverts,[(i*5+j,i*5+j+1,(i+1)*5+j+1,(i+1)*5+j) for i in range(4) for j in range(4)],'Paint_Atlantic',root)
    sub=roof.modifiers.new('Roof curvature','SUBSURF');sub.levels=2
    solid=roof.modifiers.new('Roof thickness','SOLIDIFY');solid.thickness=.035
    for f in roof.data.polygons:f.use_smooth=True
    box('Headliner',(0,-.15,1.405),(1.28,1.23,.02),'Interior_Graphite',.045,root)
    for s in (-1,1):
        tube('A_Pillar_'+str(s),[(s*.81,1.05,.87),(s*.65,.54,1.45)],.045,'Paint_Atlantic',root)
        tube('B_Pillar_'+str(s),[(s*.865,-.18,.7),(s*.704,-.18,1.44)],.035,'Interior_Graphite',root)
        tube('C_Pillar_'+str(s),[(s*.86,-1.39,.84),(s*.68,-.84,1.435)],.08,'Paint_Atlantic',root)
        window_front=[(s*.817,1.0,.91),(s*.665,.52,1.395),(s*.706,-.145,1.405),(s*.864,-.145,.91)]
        window_back=[(s*.864,-.215,.91),(s*.706,-.215,1.405),(s*.684,-.81,1.405),(s*.834,-1.32,.91)]
        for label,points in [('Front',window_front),('Rear',window_back)]:
            mesh('Glass_'+label+('_L' if s<0 else '_R'),points,[(0,1,2,3)],'Glass',root)
            tube('Seal_'+label+str(s),points,.015,'Rubber',root,True)
        # Independent lower door panels and seams.
        for label,ya,yb in [('Front',-.17,1.00),('Rear',-1.26,-.21)]:
            points=[(s*.901,ya,.46),(s*.901,yb,.46),(s*.876,yb,.81),(s*.891,ya,.81)]
            # Door paint is part of the shell; explicit door anchor avoids
            # coplanar duplicate panels and preserves a future hinge contract.
            empty('Door_'+label+('_L' if s<0 else '_R'),(s*.89,yb,.70),root)
            tube('Door_Seam_'+label+str(s),points,.002,'Rubber',root,True)
            box('Door_Handle_'+label+str(s),(s*.886,ya+.18,.835),(.025,.16,.027),'Satin_Aluminium',.012,root)
            box('Door_Trim_'+label+str(s),(s*.76,(ya+yb)/2,.7),(.08,yb-ya-.06,.30),'Interior_Graphite',.05,root)
            box('Door_Armrest_'+label+str(s),(s*.70,(ya+yb)/2,.68),(.12,.43,.065),'Leather',.025,root)
    mesh('Glass_Windshield',[(-.78,1.025,.935),(.78,1.025,.935),(.636,.542,1.4),(-.636,.542,1.4)],[(0,1,2,3)],'Glass',root)
    mesh('Glass_Rear',[(-.805,-1.34,.91),(-.646,-.85,1.4),(.646,-.85,1.4),(.805,-1.34,.91)],[(0,1,2,3)],'Glass',root)
    for y,s in [(2.255,1),(-2.255,-1)]:
        box('Bumper_'+str(s),(0,y,.43),(1.5,.12,.18),'Paint_Atlantic',.075,root)
        box('Grille_'+str(s),(0,y+s*.058,.54),(.83,.023,.18),'Rubber',.035,root)
        for j in range(4):box('Grille_Slat',(0,y+s*.073,.48+j*.035),(.77,.014,.007),'Satin_Aluminium',.002,root)
        box('Plate_'+str(s),(0,y+s*.076,.38),(.49,.018,.105),'Paint_White',.008,root)
        text3('Plate_Letters','DS  01',(0,y+s*.087,.38),.066,'Interior_Graphite',(math.pi/2 if s==-1 else -math.pi/2,0,math.pi if s==1 else 0),root)
        for side in (-1,1):
            box('LampHousing_'+str(s)+str(side),(side*.58,y,.69),(.39,.12,.13),'Interior_Graphite',.045,root)
            box(('Headlight' if s==1 else 'Taillight')+str(side),(side*.58,y+s*.065,.705),(.32,.012,.038),'Lamp_White' if s==1 else 'Lamp_Red',.014,root)
            box('TurnSignal_'+str(s)+str(side),(side*.67,y+s*.069,.661),(.13,.013,.014),'Lamp_Amber',.004,root)
    # Visible wheels with spoke meshes, rotors, calipers, bolts and tread bands.
    for s in (-1,1):
        for y,label in [(1.36,'F'),(-1.36,'R')]:
            wp=empty('Wheel_'+label+('L' if s<0 else 'R'),(s*.855,y,.34),root)
            torus('Tire',(0,0,0),.249,.078,'Rubber','X',wp)
            torus('RimLip',(s*.082,0,0),.215,.012,'Chrome','X',wp)
            cyl('BrakeRotor',(s*.035,0,0),.17,.018,'Satin_Aluminium','X',48,wp)
            box('BrakeCaliper',(s*.05,.11,0),(.065,.055,.145),'Interior_Graphite',.018,wp)
            cyl('WheelHub',(s*.09,0,0),.059,.04,'Satin_Aluminium','X',24,wp)
            for i in range(10):
                a=i*math.tau/10
                sp=box('AlloySpoke',(s*.09,math.sin(a)*.13,math.cos(a)*.13),(.027,.027,.18),'Satin_Aluminium',.009,wp);sp.rotation_euler[0]=-a
            for i in range(5):
                a=i*math.tau/5;cyl('WheelBolt',(s*.116,math.sin(a)*.038,math.cos(a)*.038),.006,.008,'Chrome','X',8,wp)
            for i in (-1,1):torus('TreadChannel',(i*.026,0,0),.32,.002,'Interior_Graphite','X',wp)
    # Seats: bolsters, headrest supports, stitched panels.
    for x in (-.4,.4):
        for y,row in [(-.13,'Front'),(-.87,'Rear')]:
            seat=empty('Seat_'+row+str(x),(x,y,.52),root)
            box('Cushion',(0,0,.06),(.5,.54,.15),'Leather',.085,seat)
            box('Cushion_Insert',(0,.02,.14),(.30,.40,.02),'Interior_Stone',.045,seat)
            back=box('Backrest',(0,-.22,.38),(.49,.14,.60),'Leather',.09,seat);back.rotation_euler[0]=-.10
            box('Backrest_Insert',(0,-.14,.4),(.30,.04,.43),'Interior_Stone',.045,seat)
            for sx in (-.095,.095):cyl('HeadrestRod',(sx,-.23,.71),.01,.15,'Chrome',parent=seat)
            box('Headrest',(0,-.23,.79),(.31,.125,.17),'Leather',.055,seat)
            for sx in (-.16,.16):tube('SeatStitch',[(sx,-.135,.2),(sx,-.135,.60)],.0015,'Stitch',seat)
    box('Dashboard',(0,.765,.89),(1.49,.45,.21),'Interior_Graphite',.075,root)
    box('Dash_Trim',(0,.524,.877),(1.44,.014,.03),'Satin_Aluminium',.006,root)
    box('Glovebox',(.43,.525,.79),(.52,.03,.14),'Interior_Graphite',.02,root)
    box('Centre_Tunnel',(0,-.015,.57),(.25,1.08,.24),'Interior_Graphite',.04,root)
    box('Armrest',(0,-.37,.735),(.23,.31,.075),'Leather',.04,root)
    for x in (-.59,.59,.065):
        box('Vent',(x,.514,.945),(.21,.025,.075),'Rubber',.014,root)
        for j in range(4):box('VentSlat',(x,.492,.925+j*.013),(.185,.01,.003),'Satin_Aluminium',.001,root)
    # Instrument binnacle forward of wheel, face towards driver (-Y).
    box('Instrument_Hood',(-.38,.62,1.036),(.52,.22,.20),'Interior_Graphite',.055,root)
    for x,title,maxv in [(-.515,'RPM',8),(-.245,'km/h',200)]:
        cyl('GaugeFace_'+title,(x,.497,1.045),.098,.014,'Rubber','Y',64,root)
        torus('GaugeBezel',(x,.486,1.045),.098,.004,'Satin_Aluminium','Y',root)
        for i in range(11):
            a=math.radians(-130+i*26)
            p1=(x+math.sin(a)*.078,.482,1.045+math.cos(a)*.078)
            p2=(x+math.sin(a)*.089,.482,1.045+math.cos(a)*.089)
            tube('GaugeTick',[p1,p2],.0016,'Ink',root)
            if i%2==0:text3('GaugeNumber',str(round(maxv*i/10)),(x+math.sin(a)*.064,.478,1.045+math.cos(a)*.064),.014,'Ink',parent=root)
        text3('GaugeUnit',title,(x,.477,1.014),.014,'Ink',parent=root)
        pivot=empty('Needle_'+('RPM' if title=='RPM' else 'Speed'),(x,.474,1.045),root)
        tube('NeedleBlade',[(0,0,-.012),(-.048,0,-.051)],.002,'Lamp_Red',pivot)
        cyl('NeedleHub',(0,-.001,0),.008,.009,'Satin_Aluminium','Y',16,pivot)
    box('Cluster_Display',(-.38,.485,1.037),(.07,.008,.10),'Display',.005,root)
    text3('Gear_Display','N',(-.38,.475,1.06),.028,'Ink',parent=root)
    text3('Odometer','00128',(-.38,.475,1.017),.012,'Ink',parent=root)
    # Wheel pivot is the steering column direction; locally children face -Y.
    steering=empty('SteeringWheel_Pivot',(-.38,.28,.938),root)
    torus('Steering_Rim',(0,0,0),.164,.016,'Leather','Y',steering)
    for pts in [[(-.145,0,0),(-.045,0,-.02)],[(.145,0,0),(.045,0,-.02)],[(0,0,-.145),(0,0,-.04)]]:
        tube('Steering_Spoke',pts,.015,'Satin_Aluminium',steering)
    box('Airbag',(0,-.005,-.018),(.12,.05,.09),'Interior_Graphite',.03,steering)
    text3('Wheel_Badge','DS',(0,-.033,-.018),.022,'Satin_Aluminium',parent=steering)
    cyl('SteeringColumn',(-.38,.4,.94),.035,.27,'Rubber','Y',parent=root)
    for s in (-1,1):tube('Stalk_'+str(s),[(-.38+s*.03,.43,.98),(-.38+s*.20,.4,.97)],.01,'Interior_Graphite',root)
    box('Infotainment',(0.12,.514,1.075),(.28,.035,.155),'Interior_Graphite',.015,root)
    box('Infotainment_Glass',(.12,.491,1.075),(.25,.005,.126),'Display',.005,root)
    text3('Screen_Title','DRIVE SCHOOL',(.12,.484,1.10),.019,'Ink',parent=root)
    text3('Screen_Subtitle','READY TO LEARN',(.12,.484,1.058),.012,'Ink',parent=root)
    for x in (-.025,.125,.275):
        cyl('ClimateDial',(x,.509,.826),.026,.025,'Satin_Aluminium','Y',32,root)
        cyl('ClimateFace',(x,.49,.826),.021,.012,'Rubber','Y',32,root)
    manual=empty('Transmission_Manual',(0,.05,.7),root)
    box('ShiftBoot',(0,0,0),(.13,.16,.045),'Leather',.03,manual)
    stick=empty('GearLever_Pivot',(0,0,.025),manual)
    cyl('GearStick',(0,0,.07),.012,.14,'Satin_Aluminium',parent=stick)
    box('GearKnob',(0,0,.15),(.064,.071,.055),'Leather',.025,stick)
    text3('ShiftPattern','1 3 5\n2 4 6',(0,0,.181),.012,'Ink',(0,0,0),stick)
    auto=empty('Transmission_Automatic',(0,.05,.7),root);auto.hide_render=True
    box('AutoGate',(0,0,0),(.14,.23,.035),'Satin_Aluminium',.02,auto)
    box('AutoSelector',(0,0,.075),(.06,.10,.12),'Leather',.025,auto)
    text3('AutoLabels','P R N D',(.0,.095,.024),.014,'Ink',(0,0,0),auto)
    for o in auto.children_recursive:o.hide_render=True
    hand=empty('Handbrake_Pivot',(.13,-.23,.66),root)
    box('HandbrakeLever',(0,.08,.02),(.035,.20,.045),'Leather',.016,hand)
    for x,label in [(-.57,'Clutch'),(-.40,'Brake'),(-.23,'Throttle')]:
        pedal=empty('Pedal_'+label,(x,.88,.52),root)
        tube('PedalArm',[(0,0,0),(0,-.12,-.18)],.008,'Satin_Aluminium',pedal)
        p=box('PedalPad',(0,-.12,-.17),(.07,.025,.1),'Rubber',.008,pedal);p.rotation_euler[0]=-.22
    for s in (-1,1):
        tube('MirrorArm_'+str(s),[(s*.79,.77,.95),(s*.97,.74,.96)],.023,'Interior_Graphite',root)
        housing=box('MirrorHousing_'+str(s),(s*1.01,.77,1.0),(.23,.14,.13),'Paint_Atlantic',.05,root)
        mirror=box('MirrorSurface_'+('L' if s<0 else 'R'),(s*1.01,.69,1.0),(.185,.008,.092),'Mirror',.02,root)
    tube('CentreMirrorArm',[(0,.55,1.385),(0,.40,1.31)],.008,'Interior_Graphite',root)
    box('CentreMirrorHousing',(0,.40,1.31),(.24,.04,.074),'Interior_Graphite',.012,root)
    box('MirrorSurface_Centre',(0,.375,1.31),(.218,.006,.055),'Mirror',.008,root)
    for x in (-.44,.31):
        w=empty('Wiper_Pivot_'+str(x),(x,.975,.927),root)
        tube('WiperArm',[(0,0,0),(.22,.013,.035)],.005,'Rubber',w)
        tube('WiperBlade',[(.07,-.035,.05),(.48,-.035,.05)],.007,'Rubber',w)
    empty('Socket_DriverEye',(-.38,-.19,1.28),root)
    empty('Socket_CentreOfMass',(0,-.1,.51),root)
    return root

def lighting(studio=True):
    if studio:
        box('StudioFloor',(0,0,-.075),(200,200,.1),'Stage')
        area('Key',(-4,3,7),(0,0,0),1500,5)
        area('Rim',(4,-3,5),(0,0,1),1800,4,(.68,.84,1))
        area('Front',(1,6,3),(0,0,1),650,3)
    else:
        bpy.ops.object.light_add(type='SUN',location=(100,100,150));sun=bpy.context.object;sun.name='Sun';sun.rotation_euler=(.35,-.45,-.3);sun.data.energy=3;sun.data.angle=.12

def tree(x,y,parent,scale=1):
    cyl('Tree_Trunk',(x,y,1.9*scale),.16*scale,3.8*scale,'Trunk',vertices=8,parent=parent)
    for dx,dy,z,r in [(0,0,4,1.7),(-.5,.3,4.7,1.3),(.65,-.3,4.2,1.3)]:
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2,radius=r*scale,location=(x+dx*scale,y+dy*scale,z*scale));finish(bpy.context.object,'Tree_Canopy','Leaf',parent)

def road(parent,a,b,width=7,sidewalk=True):
    x1,y1=a;x2,y2=b;length=math.hypot(x2-x1,y2-y1);angle=-math.atan2(x2-x1,y2-y1)
    r=empty('RoadSegment',((x1+x2)/2,(y1+y2)/2,0),parent);r.rotation_euler[2]=angle
    box('RoadSurface',(0,0,.012),(width,length,.08),'Asphalt',parent=r)
    if sidewalk:
        for s in (-1,1):
            box('Sidewalk',(s*(width/2+1.45),0,.14),(2.9,length,.24),'Paving',parent=r)
            box('Curb',(s*(width/2+.075),0,.15),(.15,length,.28),'Concrete',parent=r)
    for i in range(int(length/9)):
        box('Centre_Dash',(0,-length/2+3+i*9,.059),(.12,3,.006),'Line_White',parent=r)
    return r

def lamp(x,y,parent):
    cyl('LightPole',(x,y,3.5),.075,7,'Facade_Dark',vertices=12,parent=parent)
    tube('LightArm',[(x,y,6.8),(x+.8,y,7.05),(x+1.5,y,7.05)],.055,'Facade_Dark',parent)
    box('Luminaire',(x+1.5,y,7.03),(1.0,.35,.12),'Facade_Dark',.05,parent)
    box('LampLens',(x+1.5,y,6.957),(.84,.27,.008),'Lamp_White',parent=parent)

def sign(x,y,parent):
    cyl('SignPost',(x,y,1.3),.035,2.6,'Satin_Aluminium',vertices=12,parent=parent)
    box('CrosswalkSign',(x,y,2.55),(.65,.035,.65),'Sign_Blue',.02,parent)
    mesh('SignTriangle',[(x-.27,y-.024,2.31),(x+.27,y-.024,2.31),(x,y-.024,2.82)],[(0,1,2)],'Line_White',parent)
    tube('WalkerLegs',[(x-.08,y-.035,2.37),(x,y-.035,2.48),(x+.1,y-.035,2.36)],.018,'Interior_Graphite',parent)
    tube('WalkerTorso',[(x,y-.035,2.48),(x+.02,y-.035,2.61)],.024,'Interior_Graphite',parent)
    cyl('WalkerHead',(x+.025,y-.04,2.66),.037,.015,'Interior_Graphite','Y',16,parent)

def signal(x,y,parent):
    cyl('TrafficPole',(x,y,1.65),.065,3.3,'Facade_Dark',vertices=12,parent=parent)
    box('SignalHousing',(x,y,3),(.28,.22,.88),'Rubber',.055,parent)
    for z,m in [(3.27,'Lamp_Red'),(3.0,'Interior_Graphite'),(2.73,'Interior_Graphite')]:cyl('SignalLens',(x,y-.12,z),.09,.02,m,'Y',24,parent)

def building(x,y,w,d,floors,parent,style=0):
    h=floors*3.15;r=empty('Building_'+str(style),(x,y,0),parent)
    box('BuildingMass',(0,0,h/2+.2),(w,d,h),'Plaster' if style==0 else 'Brick',.08,r)
    box('Plinth',(0,0,.45),(w+.1,d+.1,.9),'Facade_Dark',.03,r)
    box('FlatRoof',(0,0,h+.27),(w+.6,d+.6,.2),'Concrete',.04,r)
    for level in range(floors):
        z=2+level*3.15
        for i in range(max(1,int(w/3))):
            wx=-w/2+1.5+i*3
            for sy in (-1,1):
                box('WindowFrame',(wx,sy*(d/2+.04),z),(1.5,.13,1.7),'Paint_White',.02,r)
                box('WindowGlass',(wx,sy*(d/2+.12),z),(1.32,.02,1.52),'Window',.01,r)
                box('WindowMullion',(wx,sy*(d/2+.14),z),(.04,.035,1.53),'Satin_Aluminium',parent=r)
    box('Entrance',(0,-d/2-.09,1.3),(1.65,.2,2.3),'Window',.025,r)
    box('Canopy',(0,-d/2-.8,2.6),(2.5,1.7,.10),'Facade_Dark',.025,r)
    return r

def cone(x,y,parent):
    box('ConeBase',(x,y,.035),(.36,.36,.07),'Rubber',.025,parent)
    bpy.ops.mesh.primitive_cone_add(vertices=16,radius1=.13,radius2=.028,depth=.48,location=(x,y,.31));finish(bpy.context.object,'Cone','Lamp_Amber',parent)
    cyl('ConeBand',(x,y,.30),.085,.08,'Line_White',vertices=16,parent=parent)

def district():
    r=empty('DS_District_500m')
    box('Terrain',(0,0,-.12),(500,500,.18),'Grass',parent=r)
    # Segments stop at junction boundaries; central junction owns one surface.
    for y1,y2 in [(-250,-7),(7,250)]:road(r,(0,y1),(0,y2),14)
    for x1,x2 in [(-250,-7),(7,250)]:road(r,(x1,0),(x2,0),14)
    box('IntersectionSurface',(0,0,.012),(14,14,.08),'Asphalt',parent=r)
    for signum in (-1,1):
        for i in range(9):
            box('Crosswalk',(signum*14,-6+i*1.5,.06),(3,.65,.008),'Line_White',parent=r)
            box('Crosswalk',(-6+i*1.5,signum*14,.06),(.65,3,.008),'Line_White',parent=r)
        sign(signum*17,-9,r);signal(signum*10,-9,r)
    for x,y,w,d,f,s in [(-38,37,28,20,5,0),(40,40,30,20,4,1),(-42,-40,32,20,5,0),(40,-35,25,18,2,1),(-100,30,40,18,6,0),(100,35,30,22,4,0),(-90,-60,50,25,2,1),(90,-65,40,25,3,1),(-150,70,35,24,6,0),(150,70,40,25,5,0),(-155,-60,45,25,3,1),(150,-70,55,30,2,1),(-50,115,30,20,5,0),(45,110,35,25,4,0),(-50,-135,35,24,5,1),(45,-135,35,25,4,0)]:building(x,y,w,d,f,r,s)
    road(r,(32,12),(32,23),5,False)
    box('Parking',(50,20,.02),(30,12,.08),'Asphalt',parent=r)
    for i in range(10):box('ParkingLine',(36+i*3,20,.067),(.1,5,.006),'Line_White',parent=r)
    for x in (-210,-170,-130,-90,-50,45,85,125,165,205):
        for y in (-11,11):lamp(x,y,r)
    for y in (-200,-150,-100,-60,60,100,150,200):
        lamp(-11,y,r)
        for x in (-17,17):tree(x,y,r,1.2)
    for i in range(35):
        x=random.choice([-1,1])*random.uniform(65,230);y=random.choice([-1,1])*random.uniform(100,230);tree(x,y,r,random.uniform(.9,1.7))
    # bus shelter, bench, bin and barriers.
    for x in (59,63):cyl('ShelterPost',(x,-10,1.35),.045,2.7,'Facade_Dark',vertices=12,parent=r)
    box('ShelterRoof',(61,-10,2.8),(5,2.1,.15),'Facade_Dark',.08,r)
    box('ShelterGlass',(61,-10.85,1.55),(4,.035,2.2),'Glass',parent=r)
    box('Bench',(61,-10.6,.55),(3,.43,.14),'Brick',.04,r)
    cyl('Bin',(64,-10,.45),.28,.9,'Facade_Dark',vertices=16,parent=r)
    for i in range(6):box('ParkingStop',(36+i*3,22,.14),(1.8,.2,.18),'Concrete',.03,r)
    return r

EXERCISES=[('01 START / STOP',(-72,45),(22,38)),('02 SLALOM',(-35,40),(16,48)),('03 BOX PARK',(5,44),(28,36)),('04 PARALLEL',(47,45),(27,30)),('05 HILL START',(57,-12),(16,44)),('06 U TURN',(12,-33),(28,28)),('07 REVERSE',(-32,-30),(18,38)),('08 SHIFT / BRAKE',(-70,-34),(16,50))]

def autodrome():
    r=empty('DS_Autodrome');box('Ground',(0,0,-.12),(220,180,.2),'Grass',parent=r)
    box('Asphalt',(0,0,.0),(190,150,.10),'Asphalt',.5,r)
    for x in (-92,92):box('EdgeLine',(x,0,.056),(.15,146,.006),'Line_White',parent=r)
    for y in (-72,72):box('EdgeLine',(0,y,.056),(184,.15,.006),'Line_White',parent=r)
    for title,(x,y),(w,d) in EXERCISES:
        tube('ExerciseBoundary',[(x-w/2,y-d/2,.06),(x-w/2,y+d/2,.06),(x+w/2,y+d/2,.06),(x+w/2,y-d/2,.06)],.065,'Line_Yellow',r,True)
        text3('ExerciseLabel',title,(x,y-d/2+2,.066),.85,'Line_White',(0,0,0),r)
        empty('Spawn_'+title[:2],(x,y-d/2+5,.1),r)
    for y in (17.5,28.75,40.0,51.25,62.5):cone(-35,y,r)
    for x in (-8,0,8):
        tube('ParkingBox',[(x,55,.065),(x,46,.065),(x+3,46,.065),(x+3,55,.065)],.065,'Line_White',r)
    tube('ParallelBay',[(42,41,.065),(42,48,.065),(45,48,.065),(45,41,.065)],.065,'Line_White',r)
    # Ramp 10%, 12 m ascent / 8 m top / 12 m descent; explicit solid mesh.
    profile=[(-28,.06),(-16,1.26),(-8,1.26),(4,.06)]
    v=[(x,y,z) for x in (53,61) for y,z in profile]+[(53,-28,0),(53,4,0),(61,-28,0),(61,4,0)]
    mesh('HillRamp',v,[(0,4,5,1),(1,5,6,2),(2,6,7,3),(0,1,2,3,9,8),(4,10,11,7,6,5),(0,8,10,4),(3,7,11,9)],'Concrete',r)
    for x in (53.3,60.7):tube('RampEdge',[(x,y,z+.02) for y,z in profile],.065,'Line_Yellow',r)
    box('HillStop',(57,-20,0.86),(7,.25,.012),'Line_White',parent=r)

    # reference turning path for sedan: 5.6 m centre radius, 1.8 m body width.
    pts=[(12+5.6*math.cos(a),-32+5.6*math.sin(a),.067) for a in [i*math.pi/40 for i in range(41)]]
    tube('TurningGuide',pts,.055,'Line_White',r)
    for x in range(-80,90,25):lamp(x,76,r)
    for x in (-96,96):
        for y in range(-65,75,20):tree(x,y,r,1.4)
    return r

def save_scene(name):
    bpy.ops.wm.save_as_mainfile(filepath=str(ART/f'{name}.blend'))

def run():
    args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else ['all']
    task=args[0] if args else 'all'
    clear();materials();setup_render()
    if task in ('car','all'):
        root=car();export('DS_Sedan_A',root);lighting()
        save_scene('DS_Sedan_A')
        render('sedan-hero',(-5.8,7.2,3.4),(0,0,.8),55)
        render('sedan-rear',(5.8,-7.0,2.7),(0,0,.8),55)
        render('sedan-side',(-7,0,1.4),(0,0,.75),55,5.4)
        render('sedan-front',(0,7,1.7),(0,0,.77),55,2.6)
        area('CabinSoft',(0,-.35,1.30),(0,.65,.9),8,.6)
        render('cockpit-day',(-.38,-.31,1.28),(-.20,.9,1.08),18)
        render('cabin-detail',(.68,-.68,1.35),(-.16,.55,.88),25)
        # night uses the actual emissive instrument geometry.
        for o in bpy.data.objects:
            if o.type=='LIGHT':o.data.energy*=.018
        bpy.context.scene.world.node_tree.nodes['Background'].inputs[1].default_value=.025
        area('CabinAmbient',(-.2,.25,1.36),(0,.5,.8),1,.6,(.35,.65,1))
        render('cockpit-night',(-.38,-.31,1.28),(-.20,.9,1.08),18)
    if task in ('district','all'):
        clear();setup_render();root=district();export('DS_District',root);lighting(False);save_scene('DS_District')
        render('district-overview',(-245,-295,245),(0,0,0),48)
        render('district-plan',(0,-.01,550),(0,0,0),50,530)
        render('district-street',(-3,-48,1.25),(0,20,6),28)
        bpy.context.scene.world.node_tree.nodes['Background'].inputs[1].default_value=.035
        bpy.data.objects['Sun'].data.energy=.04
        for x in (-90,-50,45,85):
            for y in (-11,11):area('StreetLight',(x+1.5,y,6.8),(x+1.5,y,0),350,1.0,(1,.73,.42))
        render('district-night',(-3,-48,1.25),(0,20,6),28)
    if task in ('autodrome','all'):
        clear();setup_render();root=autodrome();export('DS_Autodrome',root);lighting(False);save_scene('DS_Autodrome')
        render('autodrome-overview',(-185,-215,185),(0,0,0),50)
        render('autodrome-plan',(0,-.01,230),(0,0,0),50,235)
        for i,(title,(x,y),(w,d)) in enumerate(EXERCISES):render('exercise-'+str(i+1),(x-w,y-d,24),(x,y,0),48)
    palette={n:{'color':list(m.diffuse_color),'metallic':m.node_tree.nodes.get('Principled BSDF').inputs['Metallic'].default_value,'roughness':m.node_tree.nodes.get('Principled BSDF').inputs['Roughness'].default_value} for n,m in M.items()}
    (OUT/'palette.json').write_text(json.dumps(palette,indent=2),encoding='utf-8')
    print('ART_BUILD_COMPLETE',task,flush=True)

if __name__=='__main__':run()
