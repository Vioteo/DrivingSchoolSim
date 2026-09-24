"""Targeted ray checks for previously missing body surfaces; not whole-model collision QA."""
import bpy, json
from pathlib import Path
from mathutils import Vector
root=Path(__file__).resolve().parents[1]
scene=bpy.context.scene
bpy.context.view_layer.update()
deps=bpy.context.evaluated_depsgraph_get()
checks=[]
def check(name,origin,direction,distance):
    hit,point,normal,index,obj,matrix=scene.ray_cast(deps,Vector(origin),Vector(direction),distance=distance)
    checks.append({'id':name,'pass':bool(hit),'origin':origin,'direction':direction,'maxDistanceM':distance,'hitObject':obj.name if obj else None,'hitPoint':list(point) if hit else None})
for x in (-.30,0,.30):
    for z in (.64,.69,.75):
        check(f'front-gap-x{x}-z{z}',(x,2.55,z),(0,-1,0),.50)
for x in (-.83,.83):
    for y in (-.65,0,.55):
        check(f'floor-side-x{x}-y{y}',(x,y,-.15),(0,0,1),.51)
for y in (-1.8,1.8):
    check(f'under-end-y{y}',(0,y,-.15),(0,0,1),.51)
report={'source':bpy.data.filepath,'pass':all(c['pass'] for c in checks),'checks':checks,'scope':'17 finite-length rays at former gaps. Does not prove watertightness, crash deformation or suspension clearance.'}
path=root/'artifacts/reports/shell-coverage-check.json'
path.write_text(json.dumps(report,indent=2),encoding='utf-8')
print('SHELL_COVERAGE',report['pass'],len(checks))
for c in checks:
    if not c['pass']:print('UNCOVERED',c['id'])
if not report['pass']:raise RuntimeError('Targeted shell coverage checks failed')
