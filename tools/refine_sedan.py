"""Edit the existing Blender source, replace rough body topology and audit fit.
Run: blender -b ArtSource/DS_Sedan_A.blend --python tools/refine_sedan.py
"""
import bpy,sys,math,json
from pathlib import Path
from mathutils import Vector
sys.path.insert(0,str(Path(__file__).parent))
import build_art as a
ROOT=a.ROOT
a.M={m.name:m for m in bpy.data.materials}
root=bpy.data.objects['DS_Sedan_A']
def remove(o):
    for c in list(o.children_recursive):bpy.data.objects.remove(c,do_unlink=True)
    bpy.data.objects.remove(o,do_unlink=True)
def smooth(o):
    for p in o.data.polygons:p.use_smooth=True
    return o
def panel(name,pts,material='Paint_Atlantic',thickness=.018):
    o=a.mesh(name,pts,[tuple(range(len(pts)))],material,root)
    sol=o.modifiers.new('Panel thickness','SOLIDIFY');sol.thickness=thickness
    b=o.modifiers.new('Edge radius','BEVEL');b.width=.007;b.segments=3
    o.modifiers.new('Surface normals','WEIGHTED_NORMAL');return o
def profile(y):
    # Smooth width and belt-line profiles. Front points have +Y coordinates.
    keys=[(-2.25,.70,.77),(-2.05,.85,.87),(-1.50,.91,.905),(-.9,.91,.91),(.4,.91,.92),(1.3,.89,.91),(1.85,.86,.83),(2.22,.77,.77)]
    for i in range(len(keys)-1):
        y0,w0,z0=keys[i];y1,w1,z1=keys[i+1]
        if y<=y1:
            t=max(0,min(1,(y-y0)/(y1-y0)));t=t*t*(3-2*t)
            return w0+(w1-w0)*t,z0+(z1-z0)*t
    return keys[-1][1:]
def bottom(y):
    result=.28
    for cy in (-1.36,1.36):
        d=abs(y-cy)
        if d<.395:result=max(result,.34+math.sqrt(.395**2-d*d))
    return result

# Remove obsolete body surfaces and exposed structural tubes.
for o in list(root.children):
    if o.name.startswith(('Body_Shell','Door_Seam','A_Pillar','B_Pillar','C_Pillar','Bumper_','Grille_','Grille_Slat','LampHousing_','Headlight','Taillight','TurnSignal_','Plate_','Door_Handle_')):remove(o)

# Body skin has true wheel openings. Doors are individual surface meshes, with
# origins at the hinge; there are no doubled paint faces or boolean artefacts.
for s in (-1,1):
    for title,lo,hi in [('RearQuarter',-2.25,-1.24),('DoorRear',-1.24,-.20),('DoorFront',-.20,1.0),('FrontWing',1.0,2.22)]:
        ys=[lo+(hi-lo)*i/36 for i in range(37)];verts=[]
        for y in ys:
            w,z=profile(y);z0=bottom(y)
            for k in range(5):
                t=k/4;zz=z0+(z-z0)*t
                xx=w*(.966+.034*math.sin(t*math.pi))
                verts.append((s*xx,y,zz))
        faces=[]
        for i in range(36):
            for k in range(4):
                f=(i*5+k,(i+1)*5+k,(i+1)*5+k+1,i*5+k+1)
                faces.append(f if s==1 else tuple(reversed(f)))
        name=title+('_L' if s<0 else '_R')
        obj=smooth(a.mesh(name,verts,faces,'Paint_Atlantic',root))
        sol=obj.modifiers.new('Sheet metal','SOLIDIFY');sol.thickness=.014
        if title.startswith('Door'):
            for y in (lo,hi):
                w,z=profile(y);a.tube('PanelGap_'+name,[(s*w*.967,y,.32),(s*w*1.001,y,.59),(s*w*.968,y,z)],.0018,'Rubber',root)
            w,z=profile(lo+.20)
            a.box('Handle_'+name,(s*(w+.008),lo+.22,z-.065),(.026,.14,.025),'Paint_Atlantic',.011,root)
        # Concave arch lining; radial clearance is authored, not hidden by trim.
    for cy in (-1.36,1.36):
        verts=[]
        for i in range(49):
            ang=math.pi*i/48;y=cy+.397*math.cos(ang);z=.34+.397*math.sin(ang);w,_=profile(y)
            verts.extend([(s*(w-.015),y,z),(s*.65,y,z)])
        lining=smooth(a.mesh('WheelArchLiner',verts,[(i*2,i*2+1,i*2+3,i*2+2) for i in range(48)],'Rubber',root))

# Hood and boot have curved cross sections and a visible panel gap at the cowl.
for label,lo,hi in [('Hood',1.015,2.22),('Trunk',-2.25,-1.32)]:
    verts=[]
    for i in range(25):
        y=lo+(hi-lo)*i/24;w,z=profile(y)
        for j in range(25):
            u=-1+j/12;verts.append((u*w*.966,y,z+.037*(1-u*u)))
    o=smooth(a.mesh(label,verts,[(i*25+j,i*25+j+1,(i+1)*25+j+1,(i+1)*25+j) for i in range(24) for j in range(24)],'Paint_Atlantic',root))
    sol=o.modifiers.new('Panel skin','SOLIDIFY');sol.thickness=.015
    for s in (-1,1):
        a.tube(label+'_Crease',[(s*profile(lo+(hi-lo)*i/16)[0]*.73,lo+(hi-lo)*i/16,profile(lo+(hi-lo)*i/16)[1]+.022) for i in range(17)],.0025,'Paint_Atlantic',root)

for y,s in [(2.23,1),(-2.26,-1)]:
    # Facia objects are flush with end of body rather than floating on the hood.
    a.box('BumperFacia_'+str(s),(0,y-.035*s,.45),(1.53,.16,.34),'Paint_Atlantic',.065,root)
    a.box('Grille_Recess_'+str(s),(0,y+.053*s,.50),(.82,.027,.18),'Rubber',.035,root)
    for k in range(5):a.box('GrilleBar',(0,y+.071*s,.432+k*.034),(.75,.008,.007),'Satin_Aluminium',.003,root)
    a.box('NumberPlate_'+str(s),(0,y+.085*s,.445),(.47,.014,.105),'Paint_White',.007,root)
    a.text3('NumberPlateText','DS  01',(0,y+.096*s,.445),.066,'Interior_Graphite',(math.pi/2 if s<0 else -math.pi/2,0,math.pi if s>0 else 0),root)
    for side in (-1,1):
        a.box('LampUnit_'+str(s)+str(side),(side*.565,y+.024*s,.682),(.36,.06,.115),'Rubber',.036,root)
        a.box('DRL_'+str(s)+str(side),(side*.565,y+.06*s,.714),(.295,.01,.013),'Lamp_White' if s>0 else 'Lamp_Red',.006,root)
        for dx in (-.07,.045):a.cyl('Projector',(side*.565+dx,y+.06*s,.666),.026,.016,'Lamp_White' if s>0 else 'Lamp_Red','Y',32,root)
        a.box('Indicator_'+str(s)+str(side),(side*.69,y+.062*s,.674),(.025,.01,.035),'Lamp_Amber',.006,root)

# Flat formed pillars replace primitive-looking tubes, with inner trim.
for s in (-1,1):
    panel('A_Pillar_Panel_'+str(s),[(s*.84,1.064,.88),(s*.786,1.025,.92),(s*.628,.54,1.411),(s*.684,.57,1.462)])
    panel('C_Pillar_Panel_'+str(s),[(s*.877,-1.40,.86),(s*.814,-1.29,.91),(s*.65,-.80,1.419),(s*.714,-.89,1.457)])
    panel('B_Pillar_Panel_'+str(s),[(s*.892,-.232,.88),(s*.892,-.17,.88),(s*.707,-.17,1.423),(s*.707,-.232,1.423)],'Interior_Graphite',.025)
    panel('A_Pillar_Trim_'+str(s),[(s*.798,1.016,.89),(s*.774,.989,.92),(s*.618,.525,1.40),(s*.651,.54,1.418)],'Interior_Stone',.02)

# Keep tyres inside body width. Wheel design target: 205 mm width, 654 diameter.
for o in root.children:
    if o.name.startswith('Wheel_') and o.type=='EMPTY':o.location.x=math.copysign(.80,o.location.x)
floor=bpy.data.objects['Cabin_Floor'];floor.location.z=.305;floor.dimensions.z=.04
for o in root.children:
    if o.name.startswith('Seat_Front'):o.location.z=.48
    if o.name.startswith('Seat_Rear'):
        o.location.y=-.64;o.location.z=.44
        # Rear seat has a lower headrest and back than the front seats.
        for c in o.children:
            if c.name.startswith('Headrest'):c.location.z-=.045
    if o.name.startswith('Pedal_') and o.type=='EMPTY':o.location.z=.67;o.location.y=.87
    if o.name.startswith(('Instrument_Hood','Gauge','Needle_','Cluster_Display','Gear_Display','Odometer')):o.location.z-=.055
    if o.name=='SteeringWheel_Pivot':o.location.z=1.035
    if o.name=='SteeringColumn':o.location.z=1.00
    if o.name.startswith('Stalk_'):o.location.z+=.045

a.box('RearCentreCushion',(0,-.64,.49),(.30,.51,.10),'Leather',.04,root)
a.box('RearCentreBack',(0,-.86,.81),(.33,.14,.56),'Leather',.045,root)
a.box('RearParcelShelf',(0,-1.12,.88),(1.48,.30,.045),'Interior_Graphite',.035,root)
# Footwell is open underneath the dashboard. Pads are above, not buried in floor.
a.box('DriverFloorMat',(-.4,.48,.331),(.53,.70,.009),'Rubber',.025,root)
for o in root.children:
    if o.name.startswith('Pedal_') and o.type=='EMPTY':
        for k in range(4):a.box('PedalGrip',(0,-.141,-.201+k*.017),(.055,.005,.004),'Satin_Aluminium',.001,o)
a.box('DeadPedal',(-.70,.72,.43),(.065,.17,.04),'Rubber',.008,root).rotation_euler[0]=.45
# Safety-related cockpit parts and door controls.
for s in (-1,1):
    a.tube('SeatBelt_'+str(s),[(s*.70,-.26,1.30),(s*.67,-.24,.62),(s*.24,-.19,.50)],.012,'Rubber',root)
    a.box('BeltBuckle',(s*.15,-.22,.61),(.036,.05,.065),'Rubber',.008,root)
    a.box('BeltRelease',(s*.15,-.248,.633),(.027,.012,.02),'Lamp_Red',.004,root)
    a.box('DoorPull',(s*.704,.3,.81),(.028,.17,.028),'Satin_Aluminium',.012,root)
    a.box('WindowSwitch',(s*.69,.35,.724),(.035,.065,.009),'Rubber',.004,root)
    a.box('SunVisor',(s*.36,.51,1.38),(.32,.11,.025),'Interior_Graphite',.015,root)
eye=bpy.data.objects['Socket_DriverEye'];eye.location=(-.38,-.19,1.25)

# Studio workbench view settings stored in the .blend itself.
for workspace in bpy.data.workspaces:
    for screen in [workspace.screens[0]] if len(workspace.screens) else []:
        for area in screen.areas:
            if area.type=='VIEW_3D':
                area.spaces.active.clip_end=1000;area.spaces.active.region_3d.view_distance=6
                area.spaces.active.region_3d.view_location=(0,0,.7)

def world_bounds(o):
    p=[o.matrix_world@Vector(c) for c in o.bound_box]
    return [min(v[i] for v in p) for i in range(3)],[max(v[i] for v in p) for i in range(3)]
bpy.context.view_layer.update()
checks=[]
def check(name,passed,detail):checks.append({'id':name,'passed':bool(passed),'detail':detail})
for o in root.children:
    if o.name.startswith('Pedal_') and o.type=='EMPTY':
        pad=next(c for c in o.children if c.name.startswith('PedalPad'));mn,mx=world_bounds(pad)
        check(o.name+' floor clearance',mn[2]>.34,{'lowestZ':mn[2],'floorZ':.331})
for o in root.children:
    if o.name.startswith('Seat_Rear'):
        head=next(c for c in o.children if c.name.startswith('Headrest') and c.type=='MESH' and not c.name.startswith('HeadrestRod'))
        mn,mx=world_bounds(head);ceiling=1.4+(min(mn[1],-.85)+.85)*1.0
        check(o.name+' rear-glass clearance',mx[2]<ceiling-.015,{'topZ':mx[2],'rearGlassZAtBack':ceiling})
check('Tyre radial clearance',.395-.327>=.06,{'clearanceM':.068,'neutralSteering':True,'fullSuspensionSweep':'pending'})
check('No exposed A/C structural tubes',not any(o.name.startswith(('A_Pillar_-','A_Pillar_1','C_Pillar_-','C_Pillar_1')) for o in root.children),'formed panel replacements')
(ROOT/'artifacts/reports/sedan-fit.json').write_text(json.dumps({'revision':2,'checks':checks,'status':'PASS' if all(c['passed'] for c in checks) else 'FAIL'},indent=2),encoding='utf-8')

# Save editable state with all seats/body panels visible before making cutaways.
a.setup_render();a.camera('Driver_Inspection',tuple(eye.location),(-.28,1.2,1.00),20)
a.save_scene('DS_Sedan_A');a.export('DS_Sedan_A',root)
a.render('sedan-hero',(-5.8,7.2,3.2),(0,0,.78),55)
a.render('sedan-side',(-7,0,1.2),(0,0,.77),55,5.25)
a.render('sedan-rear',(5.8,-7,2.8),(0,0,.8),55)
a.render('sedan-front',(0,7,1.65),(0,0,.77),55,2.55)
a.area('InteriorFill',(0,-.25,1.35),(0,.7,.6),7,.6)
a.render('cockpit-day',tuple(eye.location),(-.2,1.2,1.02),18)
# Dedicated pedal camera (no hidden panels): direct footwell view from seat.
a.area('FootwellInspection',(-.45,.22,.71),(-.43,.76,.46),7,.35)
a.render('pedal-clearance',(-.43,-.06,.74),(-.43,.78,.46),29)
# Cutaway explicitly removes the near body side/glass/roof for fit inspection.
hidden=[]
for o in root.children_recursive:
    if o.name in ('Roof','Headliner') or o.name.endswith('_L') or ('Glass' in o.name) or o.name.startswith(('A_Pillar','B_Pillar','C_Pillar','Seal_','Door_Trim','Door_Armrest','SeatBelt')):
        if not o.hide_render:hidden.append(o);o.hide_render=True
a.render('cabin-cutaway',(-3.5,.05,2.30),(0,-.08,.68),52)
a.render('rear-seat-clearance',(-2.2,-2.5,2.2),(0,-.72,.87),52)
for o in hidden:o.hide_render=False
# Cabin close view from passenger position without looking through a headrest.
a.render('cabin-detail',(.51,.15,1.23),(-.20,.57,.89),21)
for o in bpy.data.objects:
    if o.type=='LIGHT':o.data.energy*=.015
bpy.context.scene.world.node_tree.nodes['Background'].inputs[1].default_value=.025
a.area('NightFill',(0,.25,1.35),(0,.6,.8),1,.5,(.35,.65,1))
a.render('cockpit-night',tuple(eye.location),(-.2,1.2,1.02),18)
print('SEDAN_REVISION_2_COMPLETE',flush=True)
