"""Repair the existing reviewed sedan shell; no regeneration or export.

blender -b ArtSource/DS_Sedan_A_reviewed.blend --python tools/repair_shell.py -- --diagnose
blender -b ArtSource/DS_Sedan_A_reviewed.blend --python tools/repair_shell.py
"""
import bpy, bmesh, json, math, sys
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'artifacts/visual-review/shell-repair'
OUT.mkdir(parents=True, exist_ok=True)
CAR = bpy.data.objects['DS_Sedan_A']
CAMERAS = {
    'front-low': ((0, 5.3, .39), (0, 1.45, .55), 52),
    'underside': ((-3.1, 4.5, -3.2), (0, 0, .35), 48),
    'front3quarter': ((-4.7, 6.1, 2.45), (0, .15, .75), 52),
    'cockpit-floor': ((-.4, -.12, .83), (-.4, .78, .38), 25),
}

def bounds(o):
    pts = [o.matrix_world @ Vector(v) for v in o.bound_box]
    return [[round(min(v[i] for v in pts), 5) for i in range(3)],
            [round(max(v[i] for v in pts), 5) for i in range(3)]]

def diagnose():
    bpy.context.view_layer.update()
    result = []
    for o in CAR.children_recursive:
        if o.type != 'MESH': continue
        bm = bmesh.new(); bm.from_mesh(o.data)
        result.append(dict(name=o.name, bounds=bounds(o),
            vertices=len(bm.verts), faces=len(bm.faces),
            boundary_edges=sum(e.is_boundary for e in bm.edges),
            non_manifold_edges=sum(not e.is_manifold for e in bm.edges),
            mean_normal=[round(sum(f.normal[i] for f in bm.faces)/max(1,len(bm.faces)),4) for i in range(3)],
            modifiers=[m.type for m in o.modifiers], hidden_render=o.hide_render))
        bm.free()
    return result

def setup_inspection():
    # Studio ground is excluded from *both* underside renders; no car part hidden.
    for o in bpy.data.objects:
        if o.type=='LIGHT': o.hide_render=True
    if 'StudioFloor' in bpy.data.objects: bpy.data.objects['StudioFloor'].hide_render=True
    s=bpy.context.scene; s.render.engine='CYCLES'; s.cycles.samples=16
    s.cycles.use_denoising=True; s.cycles.device='CPU'
    s.render.resolution_x=640; s.render.resolution_y=480; s.render.resolution_percentage=100
    s.render.image_settings.file_format='PNG'
    s.world.use_nodes=True
    bg=s.world.node_tree.nodes.get('Background'); bg.inputs[0].default_value=(.32,.39,.44,1); bg.inputs[1].default_value=.55
    s.view_settings.view_transform='AgX'
    for name,loc,target,energy,size in [
        ('Shell_Key',(-3,4,5),(0,0,.5),900,5),
        ('Shell_Fill',(4,2,2),(0,0,.6),650,4),
        ('Shell_Under',(-1,1,-4),(0,0,.4),600,4),
        ('Shell_Footwell',(-.4,.1,.75),(-.4,.85,.4),6,.4),
    ]:
        data=bpy.data.lights.new(name,'AREA'); data.energy=energy;data.shape='DISK';data.size=size
        o=bpy.data.objects.new(name,data);bpy.context.collection.objects.link(o);o.location=loc
        o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler()

def render_set(stage, names):
    for name in names:
        loc,target,lens=CAMERAS[name]
        data=bpy.data.cameras.new('Shell_'+name);data.lens=lens;data.clip_start=.01
        o=bpy.data.objects.new('Shell_'+name,data);bpy.context.collection.objects.link(o);o.location=loc
        o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler()
        bpy.context.scene.camera=o
        bpy.context.scene.render.filepath=str(OUT/f'{stage}-{name}.png')
        bpy.ops.render.render(write_still=True)
        bpy.data.objects.remove(o,do_unlink=True)

def manifold_mesh(name, verts, faces, material):
    me=bpy.data.meshes.new(name);me.from_pydata(verts,[],faces);me.update()
    bm=bmesh.new();bm.from_mesh(me);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(me);bm.free()
    o=bpy.data.objects.new(name,me);bpy.context.collection.objects.link(o)
    o.parent=CAR; me.materials.append(bpy.data.materials[material])
    o['shell_repair']='closed physical panel, metres; 2026-09-19'
    return o

def prism(name, outline, low, high, material):
    n=len(outline);verts=[(x,y,z) for z in (low,high) for x,y in outline]
    faces=[tuple(reversed(range(n))),tuple(range(n,2*n))]
    faces += [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    return manifold_mesh(name,verts,faces,material)

def sample(points,y):
    for (ya,xa),(yb,xb) in zip(points,points[1:]):
        if ya<=y<=yb:
            return xa+(xb-xa)*(y-ya)/max(1e-9,yb-ya)
    return points[0][1] if y<points[0][0] else points[-1][1]

def repair():
    bpy.context.view_layer.update()
    side={}
    for suffix in ('L','R'):
        pts=[]
        for label in ('RearQuarter','DoorRear','DoorFront','FrontWing'):
            o=bpy.data.objects[label+'_'+suffix]
            assert len(o.data.vertices)==185, 'Unexpected side topology: inspect before repairing'
            pts += [(p.y,p.x) for p in [o.matrix_world@o.data.vertices[i].co for i in range(0,185,5)]]
        side[suffix]=sorted(pts)

    # The notch is wider than the neutral tyre envelope in both X and Y.
    # The original floor otherwise projects into the inner tyre region at its ends.
    inner=.635; radius=.407; wheel_y=(-1.36,1.36)
    def outline(lo,hi,cabin=False):
        stations={lo,hi}
        for pts in side.values(): stations.update(y for y,x in pts if lo<y<hi)
        for cy in wheel_y:
            for y in (cy-radius,cy+radius):
                if lo<y<hi:stations.update((y-0.0001,y+0.0001))
        result=[]
        for suffix in ('R','L'):
            seq=sorted(stations,reverse=suffix=='L')
            for y in seq:
                x=sample(side[suffix],y)
                if cabin: x=math.copysign(min(abs(x),.76),x)
                if any(abs(y-cy)<radius for cy in wheel_y):x=math.copysign(inner,x)
                result.append((x,y))
        return result
    added=[]
    added.append(prism('Shell_Undertray',outline(-2.25,2.22),.250,.285,'Interior_Graphite'))
    floor=bpy.data.objects['Cabin_Floor']
    replacement=prism('Shell_TemporaryFloor',outline(-1.295,1.135,True),.285,.325,'Interior_Graphite')
    # Keep the existing floor object, hierarchy and world transform.
    inv=floor.matrix_world.inverted()
    for v in replacement.data.vertices:v.co=inv@v.co
    floor.data=replacement.data;floor['shell_repair']='corner relief for tyre clearance; unchanged cabin floor height'
    bpy.data.objects.remove(replacement,do_unlink=True)
    for m in list(floor.modifiers):
        if m.type=='BEVEL':m.width=.001

    # Close the top of each bumper to the actual current bonnet/boot edge.
    for label,front in [('Hood',True),('Trunk',False)]:
        o=bpy.data.objects[label]; pts=[o.matrix_world@v.co for v in o.data.vertices]
        ey=max(p.y for p in pts) if front else min(p.y for p in pts)
        edge=sorted([p for p in pts if abs(p.y-ey)<.0005],key=lambda p:p.x)
        assert len(edge)>=20, 'Unexpected end-panel topology'
        sign=1 if front else -1
        verts=[]
        for p in edge:
            verts.extend([(p.x,ey-sign*.05,.575),(p.x,ey+sign*.017,.575),
                          (p.x,ey+sign*.017,p.z-.005),(p.x,ey-sign*.05,p.z-.005)])
        faces=[(3,2,1,0)]
        for i in range(len(edge)-1):
            for k in range(4):faces.append((4*i+k,4*(i+1)+k,4*(i+1)+(k+1)%4,4*i+(k+1)%4))
        faces.append(tuple(4*(len(edge)-1)+k for k in range(4)))
        added.append(manifold_mesh('Shell_'+('Front' if front else 'Rear')+'UpperFacia',verts,faces,'Paint_Atlantic'))

    # Inboard wheelhouse walls meet the existing curved arch liners. No outer
    # disk or panel is placed across the wheel opening.
    for suffix,s in [('L',-1),('R',1)]:
        for cy in wheel_y:
            r=.397
            path=[(cy+r,.265),(cy-r,.265)]
            path += [(cy+r*math.cos(math.pi-math.pi*i/48),.34+r*math.sin(math.pi-math.pi*i/48)) for i in range(49)]
            verts=[(s*x,y,z) for x in (.633,.650) for y,z in path]
            n=len(path);faces=[tuple(reversed(range(n))),tuple(range(n,2*n))]
            faces += [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
            added.append(manifold_mesh('Shell_Wheelhouse_'+suffix+('_Front' if cy>0 else '_Rear'),verts,faces,'Rubber'))
    for o in CAR.children_recursive:
        if o.name.startswith('WheelArchLiner') and o.type=='MESH':
            bm=bmesh.new();bm.from_mesh(o.data)
            # Normal points towards the tyre cavity before solidifying outwards.
            if sum(f.normal.z for f in bm.faces)>0:bmesh.ops.reverse_faces(bm,faces=list(bm.faces))
            bm.to_mesh(o.data);bm.free()
            if not any(m.type=='SOLIDIFY' for m in o.modifiers):
                mod=o.modifiers.new('Wheelhouse sheet thickness','SOLIDIFY');mod.thickness=.008;mod.offset=-1

    # A shaped bulkhead behind the engine bay, forward of the pedal pivots.
    # Raised lower corners follow the wheelhouse instead of cutting across it.
    cross=[(-.635,.28),(.635,.28),(.635,.59),(.832,.59),(.832,.911),(-.832,.911),(-.832,.59),(-.635,.59)]
    n=len(cross);verts=[(x,y,z) for y in (1.025,1.045) for x,z in cross]
    faces=[tuple(reversed(range(n))),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    added.append(manifold_mesh('Shell_Firewall',verts,faces,'Interior_Graphite'))
    return [o.name for o in added]

if __name__=='__main__':
    before=diagnose()
    if '--diagnose' in sys.argv:
        (OUT/'diagnosis-before.json').write_text(json.dumps(before,indent=2),encoding='utf-8')
        setup_inspection();render_set('before',['front-low','underside','front3quarter'])
        print('SHELL_DIAGNOSIS_COMPLETE',flush=True)
    else:
        added=repair()
        bpy.context.view_layer.update()
        after=diagnose()
        report=dict(source='DS_Sedan_A_reviewed.blend',output='DS_Sedan_A_closed_shell.blend',
                    added=added,modified=['Cabin_Floor','WheelArchLiner*'],cameras=CAMERAS,
                    geometry=after)
        (OUT/'repair-geometry.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
        bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'ArtSource/DS_Sedan_A_closed_shell.blend'))
        setup_inspection();render_set('after',CAMERAS)
        print('SHELL_REPAIR_COMPLETE',flush=True)
