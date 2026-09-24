import numpy as np, json, os
from scipy.spatial import Delaunay
from matplotlib.path import Path
S=19.17
road=np.load('road_fixed.npy'); H,W=road.shape
V=np.load('roadV.npy'); T=np.load('roadT.npy')
M=json.load(open('marks.json'))
def inroad(x,z):
    i=int((50-z)*S); j=int((x+50)*S)
    return 0<=i<H and 0<=j<W and road[i,j]
pv=[]; pt=[]
def add_tri(a,b,c):
    n=len(pv)//2; pv.extend([*a,*b,*c]); 
    # ensure upward (clockwise when viewed from +Y in Unity left-handed: x right, z forward)
    cross=(b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0])
    pt.extend([n,n+1,n+2] if cross<0 else [n,n+2,n+1])
def strip(pts,w,shift=0.0):
    p=np.array(pts,float)
    if len(p)<2: return
    d=np.gradient(p,axis=0); d/=np.linalg.norm(d,axis=1,keepdims=True)+1e-9
    nrm=np.stack([-d[:,1],d[:,0]],1)
    c=p+nrm*shift
    L=c+nrm*w/2; R=c-nrm*w/2
    for i in range(len(p)-1):
        add_tri(L[i],R[i],R[i+1]); add_tri(L[i],R[i+1],L[i+1])
def poly(pts):
    p=np.array(pts,float)
    if len(p)<3: return
    # densify boundary for delaunay
    q=[]
    for a,b in zip(p,np.roll(p,-1,0)):
        n=max(1,int(np.hypot(*(b-a))/0.05)); q+= [a+(b-a)*t for t in np.arange(n)/n]
    q=np.array(q); tri=Delaunay(q); path=Path(p)
    for s in tri.simplices:
        if path.contains_point(q[s].mean(0)): add_tri(*q[s])
# edge lines: offset toward road interior by 0.09 m
for e in M['edges']:
    p=np.array(e,float)
    if len(p)<2: continue
    d=np.gradient(p,axis=0); d/=np.linalg.norm(d,axis=1,keepdims=True)+1e-9
    nrm=np.stack([-d[:,1],d[:,0]],1)
    k=len(p)//2; test=p[k]+nrm[k]*0.35
    sgn=1 if inroad(*test) else -1
    strip(p,0.12,shift=sgn*0.09)
AC=np.array([a[:2] for a in [(43.9,39.4),(43.9,30.9),(43.9,5.5),(43.9,-8.2),(-43.9,-12.0),(-15.8,13.8),(2.8,13.9),(-32.4,38.2),(20.8,34.3),(21.9,-8.7),(-4.0,7.4),(-9.4,-30.4)]])
for l in M['lines']:
    p=np.array(l); L_=np.sum(np.hypot(*np.diff(p,axis=0).T)) if len(p)>1 else 0
    if L_<5.5 and np.min(np.hypot(*(AC[:,None,:]-p[None]).transpose(2,0,1)),axis=0).max()<3.2: continue
    strip(l,0.12)
for r in M['rects']: add_tri(r[0],r[1],r[2]); add_tri(r[0],r[2],r[3])
# lane arrows (road marking 1.18) re-drawn from templates at the scheme's positions
ARROWS=[(43.9,39.4,180,'SR'),(43.9,30.9,180,'S'),(43.9,5.5,180,'SR'),(43.9,-8.2,180,'S'),(-43.9,-12.0,0,'S'),
        (-15.8,13.8,270,'SR'),(2.8,13.9,270,'S'),(-32.4,38.2,0,'R'),(20.8,34.3,270,'R'),(21.9,-8.7,270,'R'),
        (-4.0,7.4,90,'L'),(-9.4,-30.4,270,'SR')]
def arrow(cx,cz,h,kind):
    a=np.radians(h); f=np.array([np.sin(a),np.cos(a)]); r=np.array([np.cos(a),-np.sin(a)])
    o=np.array([cx,cz])-f*2.5
    L=lambda u,v: o+r*u+f*v
    def shaft(p0,p1,w=0.16):
        p0,p1=np.array(p0),np.array(p1); d=(p1-p0)/np.linalg.norm(p1-p0); n=np.array([-d[1],d[0]])*w/2
        add_tri(L(*(p0+n)),L(*(p0-n)),L(*(p1-n))); add_tri(L(*(p0+n)),L(*(p1-n)),L(*(p1+n)))
    def head(tip,d,w=0.62,l=1.1):
        tip=np.array(tip); d=np.array(d,float); d/=np.linalg.norm(d); n=np.array([-d[1],d[0]])
        b=tip-d*l; add_tri(L(*tip),L(*(b+n*w/2)),L(*(b-n*w/2)))
    if 'S' in kind: shaft((0,0),(0,3.95)); head((0,5.0),(0,1))
    if kind=='R': shaft((0,0),(0,2.2)); shaft((0,2.2),(0.9,3.4)); head((1.9,3.9),(1,0.45))
    if kind=='L': shaft((0,0),(0,2.2)); shaft((0,2.2),(-0.9,3.4)); head((-1.9,3.9),(-1,0.45))
    if kind=='SR': shaft((0,1.1),(0.95,2.5)); head((2.0,3.0),(1,0.45))
for ar in ARROWS: arrow(*ar)
print('paint tris',len(pt)//3)
# ---------- road mesh with upward winding
RT=[]
for a,b,c in T:
    A,B,C=V[a],V[b],V[c]
    cr=(B[0]-A[0])*(C[1]-A[1])-(B[1]-A[1])*(C[0]-A[0])
    RT += [int(a),int(b),int(c)] if cr<0 else [int(a),int(c),int(b)]
# ---------- signs: (x, z, heading of governed traffic, [plates top->bottom])
signs=[
 (-35.08,49.27,270,['tex:Speed20']),
 (-29.2,49.27,270,['tex:NoLeftTurn']),
 (-39.57,41.7,90,['DS_Sign_Straight']),
 (-29.6,37.1,0,['DS_Sign_Yield','DS_Sign_Right']),
 (-49.3,36.1,180,['DS_Sign_Stop']),   # 1.3.1 is carried by the railway signal mast
 (-41.7,23.3,0,['DS_Sign_Stop']),
 (-49.3,18.1,180,['DS_Sign_Priority']),
 (-39.4,15.8,270,['DS_Sign_Yield']),
 (-19.04,18.23,0,['tex:Curves']),
 (4.49,49.48,270,['tex:MinSpeed20']),
 (10.2,41.7,90,['DS_Sign_Yield','tex:Plate813']),
 (12.57,38.6,180,['tex:NoLeftTurn']),
 (12.05,21.1,180,['DS_Sign_Priority']),
 (-13.5,15.7,270,['tex:StraightOrRight']),
 (22.3,49.35,270,['DS_Sign_Priority','tex:Plate813']),
 (19.96,39.4,0,['DS_Sign_Priority','tex:Plate813']),
 (21.9,37.25,270,['DS_Sign_Yield','DS_Sign_Right']),
 (41.5,39.4,180,['tex:StraightOrRight']),
 (49.2,32.3,0,['tex:NoLeftTurn']),
 (20.35,18.2,0,['DS_Sign_Straight']),
 (25.4,16.25,270,['DS_Sign_Yield']),
 (41.7,18.0,180,['DS_Sign_Priority']),
 (-41.8,6.0,0,['DS_Sign_Priority']),
 (-28.7,5.25,90,['tex:Ascent']),
 (-21.2,5.4,90,['DS_Sign_Stop']),
 (-16.0,5.24,90,['tex:Descent']),
 (-49.3,1.85,180,['DS_Sign_Straight']),
 (-42.0,-14.2,0,['DS_Sign_Straight']),
 (7.15,7.8,90,['DS_Sign_Yield']),
 (12.6,-3.6,180,['tex:StraightOrRight']),
 (39.4,8.24,90,['DS_Sign_Yield']),
 (41.5,6.2,180,['tex:StraightOrRight']),
 (36.65,3.3,270,['tex:Curves']),
 (49.2,5.9,0,['DS_Sign_Priority']),
 (49.2,-0.9,0,['tex:NoLeftTurn']),
 (20.5,2.9,0,['DS_Sign_Priority']),
 (22.95,-5.8,270,['DS_Sign_Yield','DS_Sign_Right']),
 (-20.9,-25.1,180,['DS_Sign_Yield','DS_Sign_Right']),
 (-23.1,-35.9,90,['DS_Sign_Straight']),
 (-7.0,-28.6,270,['tex:StraightOrRight']),
 (2.24,-28.2,270,['DS_Sign_Crossing']),
 (-2.0,-35.9,90,['DS_Sign_Crossing']),
 (10.2,-35.8,90,['DS_Sign_Priority']),
 (12.55,-25.8,180,['DS_Sign_Yield']),
 (11.1,-17.1,90,['DS_Sign_Yield','DS_Sign_Right']),
 (20.35,-27.1,0,['DS_Sign_Straight']),
 (22.3,-28.2,270,['DS_Sign_Priority']),
 (20.2,-37.6,0,['DS_Sign_Yield']),
]
# ---------- signal poles at the crossroad: (x,z, vehicle heading served, pedestrian faces)
signals=[(12.05,21.1,180,[90,180]),(25.4,16.25,270,[270,180]),(7.15,7.8,90,[0,90]),(20.5,2.9,0,[270,0])]
data=dict(
  source='schema.jpg (100 x 100 m), one exercise of each type; generated by tools/autodrome_from_schema',
  road=dict(v=np.round(V,3).ravel().tolist(),t=RT),
  paint=dict(v=np.round(pv,3).tolist(),t=pt),
  cones=np.round(np.array(M['posts']).ravel(),3).tolist(),
  signs=[dict(x=x,z=z,heading=h,plates=p) for x,z,h,p in signs],
  signals=[dict(x=x,z=z,heading=h,pedestrian=ped) for x,z,h,ped in signals])
os.makedirs('out',exist_ok=True)
json.dump(data,open('out/autodrome-schema.json','w'),separators=(',',':'))
print('json KB',os.path.getsize('out/autodrome-schema.json')//1024)
