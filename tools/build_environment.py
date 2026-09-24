"""Fast data-API construction of environment studies; never touches sedan sources."""
import sys, math
from pathlib import Path
sys.path.insert(0,str(Path(__file__).parent))
import bpy, bmesh
import build_art as art

def box(name,loc,dim,material,bevel=0,parent=None):
    x,y,z=[v*.5 for v in dim]
    verts=[(-x,-y,-z),(x,-y,-z),(x,y,-z),(-x,y,-z),(-x,-y,z),(x,-y,z),(x,y,z),(-x,y,z)]
    faces=[(3,2,1,0),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)]
    obj=art.mesh(name,verts,faces,material,parent);obj.location=loc
    if bevel and min(dim)>.15:
        m=obj.modifiers.new('Edge bevel','BEVEL');m.width=min(bevel,min(dim)*.2);m.segments=2
    return obj

def cyl(name,loc,radius,depth,material,axis='Z',vertices=32,parent=None):
    verts=[(radius*math.cos(i*2*math.pi/vertices),radius*math.sin(i*2*math.pi/vertices),z) for z in (-depth/2,depth/2) for i in range(vertices)]
    faces=[tuple(reversed(range(vertices))),tuple(range(vertices,vertices*2))]
    faces += [(i,(i+1)%vertices,(i+1)%vertices+vertices,i+vertices) for i in range(vertices)]
    obj=art.mesh(name,verts,faces,material,parent);obj.location=loc
    if axis=='X':obj.rotation_euler[1]=math.pi/2
    if axis=='Y':obj.rotation_euler[0]=math.pi/2
    for face in obj.data.polygons[2:]:face.use_smooth=True
    return obj

canopy=None
def tree(x,y,parent,scale=1):
    global canopy
    cyl('Tree_Trunk',(x,y,1.9*scale),.16*scale,3.8*scale,'Trunk',vertices=8,parent=parent)
    if canopy is None:
        canopy=bpy.data.meshes.new('CanopyShared');bm=bmesh.new();bmesh.ops.create_icosphere(bm,subdivisions=2,radius=1);bm.to_mesh(canopy);bm.free();canopy.materials.append(art.M['Leaf'])
    for dx,dy,z,r in [(0,0,4,1.7),(-.5,.3,4.7,1.3),(.65,-.3,4.2,1.3)]:
        obj=bpy.data.objects.new('Tree_Canopy',canopy);bpy.context.collection.objects.link(obj);obj.parent=parent
        obj.location=(x+dx*scale,y+dy*scale,z*scale);obj.scale=(r*scale,)*3

def cone(x,y,parent):
    box('ConeBase',(x,y,.035),(.36,.36,.07),'Rubber',.025,parent)
    n=16;verts=[(x+r*math.cos(i*2*math.pi/n),y+r*math.sin(i*2*math.pi/n),z) for r,z in ((.13,.07),(.028,.55)) for i in range(n)]
    art.mesh('Cone',verts,[tuple(reversed(range(n))),tuple(range(n,n*2))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)],'Lamp_Amber',parent)
    cyl('ConeBand',(x,y,.30),.085,.08,'Line_White',vertices=16,parent=parent)

art.box=box;art.cyl=cyl;art.tree=tree;art.cone=cone
if __name__=='__main__':
    task=sys.argv[sys.argv.index('--')+1] if '--' in sys.argv else 'district'
    if task not in ('district','autodrome'):raise ValueError('Environment only')
    art.run()
