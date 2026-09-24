"""Focused geometric checks of the authored cabin, using evaluated world vertices."""
import bpy, json, math, hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
car=bpy.data.objects['DS_Sedan_A'];checks=[]
def verts(o):
 bpy.context.view_layer.update();ev=o.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=ev.to_mesh()
 result=[ev.matrix_world@v.co for v in mesh.vertices];ev.to_mesh_clear();return result
def extent(objects,axis,op):return op(v[axis] for o in objects if o.type in {'MESH','CURVE','FONT'} for v in verts(o))
def check(name,passed,**values):checks.append(dict(name=name,passed=bool(passed),**values))
front=[o for o in car.children if o.name.startswith('Seat_Front')]
rear=bpy.data.objects['Seat_RearBench']
gap=min(extent(s.children_recursive,1,min) for s in front)-extent(rear.children_recursive,1,max)
check('Front/rear upholstery longitudinal separation',gap>.15,gapM=gap)
for s in front:
 bottom=extent([o for o in s.children_recursive if o.name.startswith('Seat_Rail')],2,min)
 top=extent(s.children_recursive,2,max)
 check(s.name+' floor mounting / roof clearance',.325<bottom<.335 and top<1.37,railBottomM=bottom,seatTopM=top)
window=verts(bpy.data.objects['Glass_Rear']);normal=(window[1]-window[0]).cross(window[2]-window[0]).normalized()
if normal.y<0:normal=-normal
objects=[o for o in car.children_recursive if o.name.startswith(('RearHeadrest','RearBench_Backrest'))]
clearance=min((v-window[0]).dot(normal) for o in objects for v in verts(o))
check('Rear headrests/backrest ahead of rear glazing',clearance>.012,minPlaneClearanceM=clearance)
bench_half=max(abs(v.x) for o in rear.children_recursive if o.type=='MESH' for v in verts(o))
check('Bench inside wheelhouse side walls',bench_half<.633,maxHalfWidthM=bench_half,wheelhouseInnerM=.633)
for p in [o for o in car.children if o.name.startswith('Pedal_') and o.type=='EMPTY']:
 old=p.rotation_euler.copy();minimum=999
 for i in range(11):
  p.rotation_euler.x=old.x+math.radians(20)*i/10
  minimum=min(minimum,extent(p.children_recursive,2,min)-.34)
 p.rotation_euler=old
 check(p.name+' 11-step floor clearance',minimum>.02,minFloorClearanceM=minimum,steps=11)
source=Path(bpy.data.filepath)
report=dict(source=str(source),sha256=hashlib.sha256(source.read_bytes()).hexdigest(),checks=checks,
 scope='Cabin fit and pedal-to-floor clearance only. Does not certify wheel suspension sweeps, all mesh intersections, Unity collision, mirrors or VR.',
 passed=all(c['passed'] for c in checks))
(ROOT/'artifacts/reports/cabin-v3-fit.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps(report,indent=2),flush=True)
if not report['passed']:raise RuntimeError('Cabin checks failed')
