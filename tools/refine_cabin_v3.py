"""Refine existing sedan cabin. Run Blender with closed_shell.blend loaded.
Use -- --before for diagnosis/before renders; otherwise repair, save and render.
"""
import bpy, json, math, sys
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'tools'))
import repair_shell as shell
import build_art as art
art.M={m.name:m for m in bpy.data.materials}
OUT=ROOT/'artifacts/visual-review/cabin-v3';OUT.mkdir(parents=True,exist_ok=True)
CAR=bpy.data.objects['DS_Sedan_A']
CAMERAS={
 'driver-eye': ((-.38,-.19,1.25),(-.38,1.8,1.13),22),
 'frontseat-cutaway': ((-3.15,.85,1.9),(0,.05,.83),48),
 'rearseat-cutaway': ((-2.65,-1.6,1.78),(0,-.60,.83),48),
 'exterior3quarter': ((-4.7,6.1,2.45),(0,.15,.75),52),
 'pedals': ((-.43,.38,.68),(-.43,.78,.46),22),
 'rear-exterior': ((4.7,-6.1,2.45),(0,-.15,.75),52),
}
def measurements():
 bpy.context.view_layer.update(); result={}
 for o in CAR.children_recursive:
  if o.name.startswith(('Seat_','SteeringWheel','Socket_DriverEye')) or (o.type=='MESH' and (o.parent.name.startswith('Seat_') or o.name.startswith(('RearCentre','SteeringColumn','PedalPad','Gear','Roof','Glass_','A_Pillar','C_Pillar')))):
   result[o.name]=dict(parent=o.parent.name,location=list(o.location),rotation=list(o.rotation_euler),dimensions=list(o.dimensions),bounds=shell.bounds(o) if o.type=='MESH' else None)
 return result
def render(stage):
 shell.OUT=OUT;shell.CAMERAS=CAMERAS;shell.setup_inspection()
 # A modest roof fill makes the interior inspection readable, without changing materials.
 d=bpy.data.lights.new('CabinInspectionFill','AREA');d.energy=10;d.size=.6
 o=bpy.data.objects.new(d.name,d);bpy.context.collection.objects.link(o);o.location=(0,-.15,1.35)
 for name in CAMERAS:
  selected=next((v.split('=',1)[1] for v in sys.argv if v.startswith('--view=')),None)
  if selected and name!=selected:continue
  hidden=[]
  if 'cutaway' in name:
   for o in CAR.children_recursive:
    if o.name in ('Roof','Headliner') or o.name.endswith('_L') or o.name.startswith(('Glass_','Seal_','A_Pillar','B_Pillar','C_Pillar','Door_Trim','Door_Armrest','SeatBelt','RoofLamp','VisorMount','SunVisor','Cabin_RoofRail','Cabin_WindshieldHeader','Cabin_RearHeader')):
     if not o.hide_render: hidden.append(o);o.hide_render=True
  shell.render_set(stage,[name])
  for o in hidden:o.hide_render=False
def repair():
 # Rebuild only the unfinished upholstery. Keep the repaired shell and sockets.
 for o in list(CAR.children):
  if o.name.startswith(('Seat_Front','Seat_Rear','RearCentre','SeatBelt','BeltBuckle','BeltRelease')):
   for c in list(o.children_recursive):bpy.data.objects.remove(c,do_unlink=True)
   bpy.data.objects.remove(o,do_unlink=True)
 art.mat('Seat_Fabric',(.105,.145,.15),rough=.92)
 art.mat('Seat_Insert',(.23,.30,.30),rough=.94)
 def box(n,p,d,m='Interior_Graphite',b=.01,parent=CAR):return art.box(n,p,d,m,b,parent)
 def tube(n,p,r=.002,m='Stitch',parent=CAR):return art.tube(n,p,r,m,parent)
 for x in (-.40,.40):
  seat=art.empty('Seat_Front'+str(x),(x,.02,.43),CAR)
  for sx in (-.17,.17):
   box('Seat_Rail',(sx,-.015,-.082),(.035,.48,.045),'Satin_Aluminium',.006,seat)
   for y in (-.18,.18):box('Seat_Mount',(sx,y,-.036),(.05,.065,.072),'Rubber',.008,seat)
  box('Cushion',(0,0,.10),(.50,.51,.14),'Leather',.055,seat)
  box('Cushion_Insert',(0,.025,.174),(.30,.37,.025),'Seat_Fabric',.012,seat)
  for sx in (-.209,.209):box('Cushion_Bolster',(sx,.015,.17),(.075,.42,.09),'Leather',.032,seat)
  back=art.empty('Seat_BackFrame',(0,-.205,.35),seat);back.rotation_euler.x=.12
  box('Backrest',(0,0,0),(.48,.12,.52),'Leather',.052,back)
  box('Backrest_Insert',(0,.063,.015),(.30,.025,.405),'Seat_Fabric',.012,back)
  for sx in (-.20,.20):box('Backrest_Bolster',(sx,.052,-.015),(.075,.10,.46),'Leather',.030,back)
  for sx in (-.085,.085):art.cyl('HeadrestRod',(sx,0,.31),.008,.15,'Satin_Aluminium',parent=back)
  box('Headrest',(0,0,.41),(.29,.12,.16),'Leather',.045,back)
  for sx in (-.153,.153):
   tube('SeatStitch',[(sx,.079,-.16),(sx,.079,.19)],parent=back)
   tube('SeatStitch',[(sx,-.14,.187),(sx,.18,.187)],parent=seat)
  for z in (-.10,-.035,.03,.095):box('Seat_FabricRib',(0,.078,z),(.27,.004,.006),'Seat_Insert',.002,back)
  # Belt remains parked against the B pillar, outside the occupant's seat.
  s=1 if x>0 else -1
  tube('SeatBelt_Parked',[(s*.708,-.205,1.28),(s*.717,-.25,.75),(s*.67,-.23,.39)],.009,'Rubber')
  box('BeltBuckle',(s*.12,-.17,.60),(.035,.05,.07),'Rubber',.007)
  box('BeltRelease',(s*.12,-.196,.622),(.027,.008,.025),'Lamp_Red',.003)
 # A continuous rear bench, set behind the front backrests with a legwell.
 rear=art.empty('Seat_RearBench',(0,-.87,.43),CAR)
 box('RearBench_Base',(0,-.07,.015),(1.23,.43,.13),'Leather',.045,rear)
 box('RearBench_Cushion',(0,-.02,.11),(1.23,.44,.14),'Leather',.045,rear)
 back=art.empty('RearBench_Back',(0,-.16,.395),rear);back.rotation_euler.x=.14
 box('RearBench_Backrest',(0,0,0),(1.23,.11,.48),'Leather',.04,back)
 for x in (-.40,0,.40):
  box('RearBench_Insert',(x,.057,0),(.32,.022,.38),'Seat_Fabric',.01,back)
  box('RearBench_CushionInsert',(x,.005,.184),(.32,.33,.018),'Seat_Fabric',.008,rear)
  for dx in (-.165,.165):tube('RearSeatStitch',[(x+dx,.071,-.17),(x+dx,.071,.17)],parent=back)
  z=1.125 if x else 1.10
  for dx in (-.07,.07):art.cyl('RearHeadrestRod',(x+dx,-.98,1.057),.008,.14,'Satin_Aluminium',parent=CAR)
  box('RearHeadrest',(x,-.98,z),(.27,.10,.14),'Leather',.033)
 for x in (-.21,.21):
  box('RearBeltBuckle',(x,-.69,.625),(.035,.05,.05),'Rubber',.006)
  box('RearBeltRelease',(x,-.663,.634),(.026,.006,.018),'Lamp_Red',.002)
 shelf=bpy.data.objects['RearParcelShelf'];shelf.location=(0,-1.205,.865);shelf.dimensions=(1.44,.25,.04)
 for x in (-.48,.48):
  box('RearSpeaker',(x,-1.23,.888),(.19,.12,.012),'Rubber',.024)
  for i in range(7):box('RearSpeakerSlot',(x-.065+i*.022,-1.23,.895),(.005,.082,.002),'Interior_Graphite',.001)
 for x in (-.40,.40):box('RearFloorMat',(x,-.48,.335),(.49,.24,.01),'Rubber',.014)
 # Lower the wheel and align its column, so the gauge faces are visible above the spokes.
 steering=bpy.data.objects['SteeringWheel_Pivot'];steering.location.z=.90
 bpy.data.objects['SteeringColumn'].location.z=.90
 for o in CAR.children:
  if o.name.startswith('Stalk_'):o.location.z-=.12
  if o.name.startswith(('Instrument_Hood','Gauge','Needle_','Cluster_Display','Gear_Display','Odometer')):o.location.z+=.04
 # Complete the passenger side and rear door touch points.
 for s in (-1,1):
  box('RearDoorPull',(s*.704,-.67,.81),(.028,.16,.028),'Satin_Aluminium',.01)
  box('RearWindowSwitch',(s*.69,-.65,.724),(.035,.055,.009),'Rubber',.004)
  box('DoorSpeaker',(s*.712,.62,.56),(.016,.18,.13),'Rubber',.025)
  box('SillTrim',(s*.755,-.07,.345),(.065,1.62,.025),'Rubber',.008)
  box('VisorMount',(s*.36,.535,1.397),(.06,.065,.055),'Interior_Graphite',.01)
 box('GloveboxHandle',(.43,.503,.82),(.12,.018,.018),'Satin_Aluminium',.006)
 box('RoofLampHousing',(0,-.12,1.382),(.16,.18,.024),'Interior_Stone',.016)
 box('RoofLampLens',(0,-.12,1.367),(.115,.11,.009),'Paint_White',.009)
 # Solid back wall prevents an open view into the boot below the parcel shelf.
 box('Cabin_RearBulkhead',(0,-1.165,.59),(1.23,.025,.51),'Interior_Graphite',.008)
 # Curves remain editable in Blender; export converts copies to mesh in both formats.
 CAR['revision']='cabin_v3';CAR['cabin_notes']='Separated seat rows, mounted seats, rear bench, aligned upholstery and steering column.'
if __name__=='__main__':
 if '--render-only' in sys.argv:render('after')
 elif '--before' in sys.argv:
  (OUT/'before-measurements.json').write_text(json.dumps(measurements(),indent=2),encoding='utf-8');render('before')
 else:
  repair();bpy.context.view_layer.update()
  (OUT/'after-measurements.json').write_text(json.dumps(measurements(),indent=2),encoding='utf-8')
  bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'ArtSource/DS_Sedan_A_cabin_v3.blend'))
  render('after')
