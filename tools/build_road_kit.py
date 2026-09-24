"""Original modular road assets. Blender 5: -b --python tools/build_road_kit.py.

All authored geometry uses metres, X right / Y forward / Z up. Existing art is
never loaded for editing. FBX, GLB, textured .blend and measured catalog share
the same evaluated meshes. Road top = 0; overlay paint = +0.006 m.
"""
import bpy, bmesh, math, json, hashlib, random
import numpy as np
from pathlib import Path
from datetime import datetime, timezone
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'Assets/DrivingSchool/Art/RoadKit'
REVIEW = ROOT / 'artifacts/visual-review/road-kit'
SOURCE = ROOT / 'ArtSource/DS_RoadKit_v1.blend'
for p in (OUT / 'Textures', REVIEW / 'models', ROOT / 'artifacts/reports'):
    p.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.unit_settings.system = 'METRIC'
bpy.context.scene.unit_settings.scale_length = 1
random.seed(81)
MATS = {}
CATALOG = []
ROOTS = []


def material(name, color, roughness=.9, texture=None):
    m = bpy.data.materials.new('RK_' + name)
    m.use_nodes = True
    m.diffuse_color = (*color, 1)
    p = m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value = (*color, 1)
    p.inputs['Roughness'].default_value = roughness
    if texture:
        tex = m.node_tree.nodes.new('ShaderNodeTexImage')
        tex.image = bpy.data.images.load(str(OUT / 'Textures' / texture))
        m.node_tree.links.new(tex.outputs['Color'], p.inputs['Base Color'])
        bump = m.node_tree.nodes.new('ShaderNodeBump')
        bump.inputs['Strength'].default_value = .22
        bump.inputs['Distance'].default_value = .014
        m.node_tree.links.new(tex.outputs['Color'], bump.inputs['Height'])
        m.node_tree.links.new(bump.outputs['Normal'], p.inputs['Normal'])
    MATS[name] = m


def textures():
    rng = np.random.default_rng(81)
    n = 512
    yy, xx = np.mgrid[0:n, 0:n]
    for name, rgb, amount in [('Asphalt', (.115,.13,.145), .035),
                              ('Gravel', (.43,.39,.30), .17),
                              ('Paving', (.51,.53,.51), .022)]:
        noise = rng.normal(0, amount, (n,n))
        if name == 'Gravel':
            # Periodic Voronoi aggregate, so neighbouring UV tiles meet.
            distance = np.full((n,n), np.inf)
            stone = np.zeros((n,n))
            for i in range(220):
                x,y = rng.uniform(0,n,2)
                dx = np.minimum(abs(xx-x), n-abs(xx-x))
                dy = np.minimum(abs(yy-y), n-abs(yy-y))
                d = dx*dx+dy*dy
                pick = d < distance
                distance[pick] = d[pick]
                stone[pick] = rng.uniform(-.13,.13)
            noise = stone + rng.normal(0,.025,(n,n)) - np.minimum(distance/13000,.06)
        pixels = np.ones((n,n,4), dtype=np.float32)
        pixels[:,:,:3] = np.clip(np.array(rgb)[None,None,:]+noise[:,:,None], .015,.95)
        if name == 'Paving':
            # 2 m repeat: 400 x 200 mm staggered pavers, 5 mm mortar joints.
            row = yy // (n/10)
            joint = (yy % (n/10) < 1.4) | ((xx+((row%2)*n/10)) % (n/5) < 1.4)
            pixels[joint,:3] *= .50
        img = bpy.data.images.new('RK_' + name, width=n, height=n)
        img.pixels.foreach_set(pixels.ravel())
        img.filepath_raw = str(OUT/'Textures'/f'RK_{name}.png')
        img.file_format = 'PNG'
        img.save()


class Part:
    def __init__(self):
        self.v = []; self.f = []

    def prism(self, polygon, bottom, top):
        start = len(self.v); count = len(polygon)
        self.v += [(x,y,bottom) for x,y in polygon] + [(x,y,top) for x,y in polygon]
        self.f += [tuple(start+i for i in reversed(range(count))), tuple(start+count+i for i in range(count))]
        self.f += [(start+i,start+(i+1)%count,start+(i+1)%count+count,start+i+count) for i in range(count)]

    def box(self, x, y, w, length, bottom, top):
        self.prism([(x-w/2,y-length/2),(x+w/2,y-length/2),(x+w/2,y+length/2),(x-w/2,y+length/2)],bottom,top)

    def wedge(self, corners, bottom=-.22):
        start=len(self.v)
        self.v += [(x,y,bottom) for x,y,z in corners]+list(corners)
        self.f += [tuple(start+i for i in face) for face in [(3,2,1,0),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)]]

    def arc(self, r0, r1, a0, a1, bottom, top, center=(14,0), steps=None):
        n = steps or max(1,math.ceil((a1-a0)*32))
        def point(r,a): return (center[0]-r*math.cos(a), center[1]+r*math.sin(a))
        poly = [point(r0,a0+(a1-a0)*i/n) for i in range(n+1)]
        poly += [point(r1,a0+(a1-a0)*i/n) for i in reversed(range(n+1))]
        self.prism(poly,bottom,top)


class Module:
    def __init__(self, name, label, sockets=()):
        self.name = name; self.parts = {}; self.label = label; self.sockets = sockets

    def p(self, group, mat):
        return self.parts.setdefault((group,mat),Part())

    def finish(self):
        root = bpy.data.objects.new(self.name,None)
        bpy.context.collection.objects.link(root)
        for (group,mat),part in self.parts.items():
            mesh = bpy.data.meshes.new(self.name+'_'+group)
            mesh.from_pydata(part.v,[],part.f); mesh.update()
            bm = bmesh.new(); bm.from_mesh(mesh)
            bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
            # Explicit triangulation keeps concave road/arrow polygons consistent.
            bmesh.ops.triangulate(bm,faces=list(bm.faces))
            bm.to_mesh(mesh); bm.free()
            uv = mesh.uv_layers.new(name='UVMap')
            for loop in mesh.loops:
                v = mesh.vertices[loop.vertex_index].co
                repeat = .5 if mat == 'Gravel' else 2
                uv.data[loop.index].uv = (v.x/repeat,v.y/repeat)
            ob = bpy.data.objects.new(group+'_'+mat,mesh)
            bpy.context.collection.objects.link(ob); ob.parent = root
            ob.data.materials.append(MATS[mat])
            if group == 'Collision':
                ob.name = 'COL_'+mat; ob.hide_render = True; ob.display_type = 'WIRE'
        for name, loc in [('Axis_Forward',(0,1,0)),('Axis_Up',(0,0,1))] + list(self.sockets):
            ob = bpy.data.objects.new(name,None); bpy.context.collection.objects.link(ob)
            ob.parent = root; ob.location = loc; ob.empty_display_size = .2
        ROOTS.append(root)
        return root


def straight(name, rural=False):
    m = Module(name,'Загородная дорога' if rural else 'Городская дорога', [('Socket_Start',(0,0,0)),('Socket_End',(0,20,0))])
    for group in ('Surface','Collision'):
        m.p(group,'Asphalt').box(0,10,8,20,-.22,0)
    for s in (-1,1):
        m.p('Markings','White').box(s*3.65,10,.12,20,.006,.009)
        if rural:
            # One sloped mesh, matching road height at the inner edge.
            p=m.p('Shoulder','Gravel'); x0=s*4; x1=s*5.5
            verts=[(x0,0,0),(x1,0,-.06),(x1,20,-.06),(x0,20,0)]
            p.v += verts + [(x,y,-.22) for x,y,z in verts]
            p.f += [(0,1,2,3),(7,6,5,4),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)]
            # Separate collision uses identical slope, without decorative fragments.
        else:
            for group in ('Sidewalk','Collision'):
                m.p(group,'Paving').box(s*5.2,10,2,20,-.22,.15)
            m.p('Collision','Concrete').box(s*4.1,10,.2,20,-.22,.15)
            for j in range(20):
                m.p('Curb','Concrete').box(s*4.1,j+.5,.2,.993,-.22,.15)
    if rural:
        src=m.parts[('Shoulder','Gravel')]
        # Two strips appended above need independent face indices.
        src.f[6:]=[tuple(i+8 for i in f) for f in src.f[6:]]
        m.parts[('Collision','Gravel')]=src
    for y in (1.5,6.5,11.5,16.5): m.p('Markings','White').box(0,y,.12,3,.006,.009)
    return m.finish()


def curve():
    m=Module('RK_Road_Curve90_R14','Поворот 90° / R14', [('Socket_Start',(0,0,0)),('Socket_End',(14,14,0))])
    for group in ('Surface','Collision'): m.p(group,'Asphalt').arc(10,18,0,math.pi/2,-.22,0)
    for r in (7.8,18.2):
        for group in ('Sidewalk','Collision'): m.p(group,'Paving').arc(r,r+2,0,math.pi/2,-.22,.15)
    for r in (9.8,18):
        m.p('Collision','Concrete').arc(r,r+.2,0,math.pi/2,-.22,.15)
        count=math.ceil((r+.1)*math.pi/2)
        for i in range(count):m.p('Curb','Concrete').arc(r,r+.2,i*math.pi/2/count+.0002,(i+1)*math.pi/2/count-.0002,-.22,.15)
    for r in (10.35,17.65):m.p('Markings','White').arc(r-.06,r+.06,0,math.pi/2,.006,.009)
    for start in (0,5,10,15,20):
        m.p('Markings','White').arc(13.94,14.06,start/14,min((start+3)/14,math.pi/2),.006,.009)
    return m.finish()


def junction():
    m=Module('RK_Road_Cross_24m','Перекрёсток с переходами',[(f'Socket_{n}',p) for n,p in [('South',(0,-12,0)),('North',(0,12,0)),('East',(12,0,0)),('West',(-12,0,0))]])
    # Union polygon: no coplanar overlapping rectangles in the centre.
    cross=[(-4,-12),(4,-12),(4,-4),(12,-4),(12,4),(4,4),(4,12),(-4,12),(-4,4),(-12,4),(-12,-4),(-4,-4)]
    for group in ('Surface','Collision'):m.p(group,'Asphalt').prism(cross,-.22,0)
    for sx in (-1,1):
        for sy in (-1,1):
            for group in ('Sidewalk','Collision'):
                p=m.p(group,'Paving')
                p.box(sx*8.5,sy*8.5,7,7,-.22,.15)
                p.box(sx*4.6,sy*4.6,.8,.8,-.22,.15)
                for t,length in [(5.5,1),(10.5,3)]:
                    p.box(sx*4.6,sy*t,.8,length,-.22,.15)
                    p.box(sx*t,sy*4.6,length,.8,-.22,.15)
                p.wedge([(sx*4.2,sy*6,.02),(sx*5,sy*6,.15),(sx*5,sy*9,.15),(sx*4.2,sy*9,.02)])
                p.wedge([(sx*6,sy*4.2,.02),(sx*9,sy*4.2,.02),(sx*9,sy*5,.15),(sx*6,sy*5,.15)])
            # Lowered crossing curbs (2 cm) and matching pavement approach wedges.
            for i in range(8):
                t=4.5+i
                top=.02 if 6<=t<=9 else .15
                m.p('Curb','Concrete').box(sx*4.1,sy*t,.2,.993,-.22,top)
                m.p('Curb','Concrete').box(sx*t,sy*4.1,.993,.2,-.22,top)
            # Collision curbs preserve crossing cutouts.
            for t,l,h in [(5,2,.15),(7.5,3,.02),(10.5,3,.15)]:
                m.p('Collision','Concrete').box(sx*4.1,sy*t,.2,l,-.22,h)
                m.p('Collision','Concrete').box(sx*t,sy*4.1,l,.2,-.22,h)
    # Four crosswalks, right-hand incoming lane stop bars and short centre lines.
    for sign in (-1,1):
        for i in range(8):
            t=-3.5+i
            m.p('Markings','White').box(t,sign*7.5,.5,3,.006,.009)
            m.p('Markings','White').box(sign*7.5,t,3,.5,.006,.009)
        m.p('Markings','White').box(-sign*1.9,sign*10,3.5,.4,.006,.009)
        m.p('Markings','White').box(sign*10,sign*1.9,.4,3.5,.006,.009)
        m.p('Markings','White').box(0,sign*10.75,.12,2.5,.006,.009)
        m.p('Markings','White').box(sign*10.75,0,2.5,.12,.006,.009)
    return m.finish()


def accessories():
    m=Module('RK_Sidewalk_2x5m','Тротуар 2 × 5 м')
    for g in ('Sidewalk','Collision'):m.p(g,'Paving').box(0,2.5,2,5,-.22,.15)
    m.finish()
    m=Module('RK_Curb_1m','Бордюр 1 м')
    for g in ('Curb','Collision'):m.p(g,'Concrete').box(0,.5,.2,1,-.22,.15)
    m.finish()
    m=Module('RK_Shoulder_1p5x5m','Гравийная обочина 1,5 × 5 м')
    for g in ('Shoulder','Collision'):m.p(g,'Gravel').box(0,2.5,1.5,5,-.22,0)
    m.finish()
    for kind in ('Solid','Dashed','DoubleSolid','Yellow'):
        m=Module('RK_Marking_'+kind+'_20m',kind)
        p=m.p('Markings','Yellow' if kind=='Yellow' else 'White')
        for x in ((-.12,.12) if kind=='DoubleSolid' else (0,)):
            for y,length in ([(1.5+5*i,3) for i in range(4)] if kind=='Dashed' else [(10,20)]):p.box(x,y,.12,length,.006,.009)
        m.finish()
    m=Module('RK_Marking_Zebra_8x3m','Пешеходный переход')
    for i in range(8):m.p('Markings','White').box(-3.5+i,1.5,.5,3,.006,.009)
    m.finish()
    m=Module('RK_Marking_Stop_3p5m','Стоп-линия')
    m.p('Markings','White').box(0,.2,3.5,.4,.006,.009); m.finish()
    m=Module('RK_Marking_Arrow_Straight','Стрелка прямо')
    m.p('Markings','White').prism([(-.13,0),(.13,0),(.13,2),(.65,1.7),(0,3.5),(-.65,1.7),(-.13,2)],.006,.009);m.finish()
    m=Module('RK_Marking_Arrow_Left','Стрелка налево')
    m.p('Markings','White').prism([(-.13,0),(.13,0),(.13,2.6),(-.75,2.6),(-.65,3.1),(-1.8,2.45),(-.65,1.8),(-.75,2.3),(-.13,2.3)],.006,.009);m.finish()
    m=Module('RK_Marking_Parking_2p5x5m','Парковочное место')
    p=m.p('Markings','White');p.box(-1.2,2.5,.1,5,.006,.009);p.box(1.2,2.5,.1,5,.006,.009);p.box(0,4.95,2.3,.1,.006,.009);m.finish()


def measure(objects):
    pts=[]; tris=0
    for ob in objects:
        if ob.type!='MESH' or ob.name.startswith('COL_'):continue
        pts.extend(ob.matrix_world@v.co for v in ob.data.vertices)
        ob.data.calc_loop_triangles();tris+=len(ob.data.loop_triangles)
    lo=[min(p[i] for p in pts) for i in range(3)];hi=[max(p[i] for p in pts) for i in range(3)]
    return {'min':lo,'max':hi,'size':[hi[i]-lo[i] for i in range(3)],'triangles':tris}


def export(root):
    bpy.context.view_layer.update()
    objs=[root]+list(root.children_recursive)
    base=measure(objs)
    bpy.ops.object.select_all(action='DESELECT')
    for ob in objs:ob.select_set(True)
    bpy.context.view_layer.objects.active=root
    fbx=OUT/(root.name+'.fbx')
    bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,add_leaf_bones=False,bake_anim=False,path_mode='RELATIVE')
    # Interchange preview excludes collision geometry; FBX contains it for Unity.
    for ob in objs:
        if ob.name.startswith('COL_'):ob.select_set(False)
    glb=REVIEW/'models'/(root.name+'.glb')
    bpy.ops.export_scene.gltf(filepath=str(glb),export_format='GLB',use_selection=True,export_apply=True)
    # Isolated temporary scene checks real FBX reimport, not source-only counts.
    source_scene=bpy.context.scene
    check=bpy.data.scenes.new('ExportCheck');bpy.context.window.scene=check
    bpy.ops.import_scene.fbx(filepath=str(fbx))
    actual=measure(list(check.objects))
    errors=[abs(a-b) for a,b in zip(sorted(base['size']),sorted(actual['size']))]
    assert max(errors)<.001,(root.name,base,actual)
    for ob in list(check.objects):bpy.data.objects.remove(ob,do_unlink=True)
    bpy.context.window.scene=source_scene;bpy.data.scenes.remove(check)
    CATALOG.append({'catalogId':root.name,'sourceBounds':base,'fbxReimportBounds':actual,'reimportPass':True,
                    'fbxSha256':hashlib.sha256(fbx.read_bytes()).hexdigest(),'glbSha256':hashlib.sha256(glb.read_bytes()).hexdigest(),
                    'collision':'static MeshCollider from COL_*; markings have none', 'lod':'LOD0 only; no LODGroup authored',
                    'sockets':{o.name:list(o.location) for o in root.children if o.name.startswith('Socket_')},
                    'materials':sorted({mat.name for o in objs if o.type=='MESH' for mat in o.data.materials})})


def lighting():
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=24;scene.cycles.use_denoising=True
    scene.render.resolution_x=960;scene.render.resolution_y=720;scene.render.resolution_percentage=100
    scene.world=bpy.data.worlds.new('RoadKit_World');scene.world.use_nodes=True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.53,.65,.78,1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value=.45
    light=bpy.data.lights.new('Sun','SUN');light.energy=2.5;light.angle=.15
    ob=bpy.data.objects.new('Sun',light);scene.collection.objects.link(ob);ob.rotation_euler=(.45,-.55,-.45)
    scene.view_settings.view_transform='AgX'
    try:
        prefs=bpy.context.preferences.addons['cycles'].preferences;prefs.compute_device_type='OPTIX';prefs.get_devices()
        for d in prefs.devices:d.use=d.type!='CPU'
        if any(d.use for d in prefs.devices):scene.cycles.device='GPU'
    except Exception:pass


def render(name,pos,target,ortho=None):
    cam=bpy.data.cameras.new(name);ob=bpy.data.objects.new(name,cam);bpy.context.collection.objects.link(ob)
    ob.location=pos;ob.rotation_euler=(Vector(target)-ob.location).to_track_quat('-Z','Y').to_euler()
    cam.lens=45
    if ortho:cam.type='ORTHO';cam.ortho_scale=ortho
    bpy.context.scene.camera=ob;bpy.context.scene.render.filepath=str(REVIEW/(name+'.png'))
    bpy.ops.render.render(write_still=True)


def visibility(roots):
    for root in ROOTS:
        for ob in root.children_recursive:
            if ob.type=='MESH':ob.hide_render=root not in roots or ob.name.startswith('COL_')


def main():
    textures()
    material('Asphalt',(.115,.13,.145),texture='RK_Asphalt.png')
    material('Gravel',(.43,.39,.30),texture='RK_Gravel.png')
    material('Paving',(.51,.53,.51),texture='RK_Paving.png')
    material('Concrete',(.63,.65,.62));material('White',(.87,.88,.83),.78);material('Yellow',(.95,.61,.035),.8)
    urban=straight('RK_Road_Urban_20m');rural=straight('RK_Road_Rural_20m',True);bend=curve();cross=junction();accessories()
    for root in ROOTS:export(root)
    lighting()
    visibility([urban]);render('urban-detail',(-9,-2,5),(0,8,0))
    visibility([rural]);render('rural-detail',(-9,-2,5),(0,8,0))
    visibility([bend]);render('curve',(-15,-16,28),(6,9,0),38)
    visibility([cross]);render('intersection',(-25,-29,30),(0,0,0),37)
    # A compact contact sheet made from the actual mesh modules in the .blend.
    layout=[(-18,8,0),(0,8,0),(18,8,0),(-10,-9,0)]
    for root,loc in zip(ROOTS[:4],layout):root.location=loc
    extras=[(15,-16,0),(19,-16,0),(22,-16,0),(28,-18,0),(30,-18,0),(32,-18,0),(34,-18,0),(-20,-35,0),(-10,-32,0),(0,-35,0),(5,-35,0),(12,-35,0)]
    for root,loc in zip(ROOTS[4:],extras):root.location=loc
    visibility(ROOTS);render('kit-overview',(-50,-61,67),(6,-3,0),88)
    # Packed textures make the editable source portable.
    bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE))
    report={'revision':'v1','createdUtc':datetime.now(timezone.utc).isoformat(),'exporter':bpy.app.version_string,
            'source':str(SOURCE.relative_to(ROOT)),'sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
            'units':'metres','blenderAxes':'X right / Y forward / Z up','roadWidth':8,'sidewalkWidth':2,'curbHeight':.15,
            'markingOffset':.006,'textureResolution':512,'moduleCount':len(CATALOG),'modules':CATALOG}
    (OUT/'catalog.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    (ROOT/'artifacts/reports/road-kit-export.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    print('ROAD_KIT_COMPLETE',len(CATALOG),flush=True)


if __name__=='__main__':main()
