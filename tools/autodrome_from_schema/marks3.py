import numpy as np, cv2, json
from scipy import ndimage as ndi
from skimage.morphology import skeletonize
import networkx as nx
S=19.17
road=np.load('road_fixed.npy'); white=np.load('white.npy'); sat=np.load('sat.npy'); rm=np.load('rm.npy')
H,W=road.shape
im=cv2.imread('schema_hi.png')[81:81+H,78:78+W]
gray=cv2.cvtColor(im,cv2.COLOR_BGR2GRAY)
def px(x,z): return (x+50)*S,(50-z)*S
def tom(p): p=np.asarray(p,float); return np.stack([p[...,0]/S-50,50-p[...,1]/S],-1)
loops=json.load(open('loops.json'))['loops']
edge=np.zeros((H,W),np.uint8)
for l in loops: cv2.polylines(edge,[np.array([px(x,z) for x,z in l]).round().astype(np.int32)],True,255,1)
dedge=ndi.distance_transform_edt(edge==0)
excl=np.zeros((H,W),bool)
def box(x0,z0,x1,z1):
    a=px(x0,z1); b=px(x1,z0); excl[max(0,int(a[1])):int(b[1])+1,max(0,int(a[0])):int(b[0])+1]=True
box(-51,-51,-49.3,51); box(-51,-51,51,-49.4); box(49.3,-51,51,51); box(-51,49.4,51,51)
box(38,-50,50,-38); box(38.6,32.6,39.7,36.0); box(-50,28,-40.5,31.6); box(0.3,12.4,5.8,15.2); box(-2.2,-35.2,2.4,-29.0)
box(-29.2,6.1,-27.8,9.1); box(-24.3,6.1,-22.9,9.1); box(-8.4,6.1,-7.0,9.1)
for x,z in [(-16.6,47.4),(-22,30.3),(-45.3,26.4),(-16.1,7.6),(16.4,11.6),(31.9,37.7),(37.7,1.4),(-14.6,-11.1),(0.1,-32.3),(-20,7.6)]:
    box(x-1.05,z-0.9,x+1.05,z+0.9)
# circle markers ("5" on hill)
colored=sat>40; dark=gray<70
nearcol=ndi.binary_dilation(colored|dark,iterations=int(0.3*S))
posts=json.load(open('posts_f.json'))
pm=np.zeros((H,W),np.uint8)
for x,z in posts: cv2.circle(pm,tuple(int(v) for v in px(x,z)),7,1,-1)
base=white&~excl&~nearcol&~pm.astype(bool)&~ndi.binary_dilation(rm,iterations=4)
# ---- edge lines: loop points with white nearby (or seams of removed areas)
wd=ndi.distance_transform_edt(~(white&~excl))
rmb=ndi.binary_dilation(rm,iterations=4)&~ndi.binary_erosion(rm,iterations=4)
edges=[]
for l in loops:
    L=np.array(l); P=np.array([px(x,z) for x,z in L])
    xi=np.clip(P[:,0].round().astype(int),0,W-1); yi=np.clip(P[:,1].round().astype(int),0,H-1)
    painted=(wd[yi,xi]<=3.5)|rmb[yi,xi]|pm[yi,xi].astype(bool)
    Lx,Lz=L[:,0],L[:,1]
    rail=(Lx<-41)&(Lz>25)&(Lz<35)
    painted|=rail&~((Lz>29.1)&(Lz<31.1))
    painted&=~(rail&(Lz>29.1)&(Lz<31.1))
    painted=ndi.binary_closing(painted,iterations=6)
    painted&=~(rail&(Lz>29.1)&(Lz<31.1))  # bridge gaps under posts/signs (~1.2 m)
    painted=ndi.binary_opening(painted,iterations=3)
    n=len(L)
    if painted.all(): edges.append(np.vstack([L,L[:1]]).round(3).tolist()); continue
    # rotate so we start at an unpainted point
    s=int(np.argmin(painted)); idx=np.r_[s:n,0:s]; pp=painted[idx]; LL=L[idx]
    run=[]
    for k in range(n):
        if pp[k]: run.append(LL[k])
        elif run:
            if len(run)>=5: edges.append(np.array(run).round(3).tolist())
            run=[]
    if len(run)>=5: edges.append(np.array(run).round(3).tolist())
# ---- interior markings
inner=base&(dedge>0.28*S)
dt=ndi.distance_transform_edt(inner)
thick=ndi.binary_dilation(dt>=2.6,iterations=3)&inner
thin=inner&~ndi.binary_dilation(thick,iterations=2)
rects=[];polys=[]
lab,n=ndi.label(thick)
for i,sl in enumerate(ndi.find_objects(lab),1):
    comp=(lab[sl]==i); ys,xs=np.nonzero(comp)
    if len(xs)<25: continue
    pts=np.stack([xs+sl[1].start,ys+sl[0].start],1).astype(np.float32)
    rect=cv2.minAreaRect(pts); w,h=rect[1]
    if len(xs)/max((w+1)*(h+1),1)>0.72:
        if max(w,h)<1.6*S: continue
        rects.append(tom(cv2.boxPoints(((rect[0][0],rect[0][1]),(w+1,h+1),rect[2]))).round(3).tolist())
    else:
        full=np.zeros((H,W),np.uint8); full[sl][comp]=1
        cs,_=cv2.findContours(full,cv2.RETR_EXTERNAL,cv2.CHAIN_APPROX_NONE)
        c=max(cs,key=len)
        c=cv2.approxPolyDP(c,0.8,True)[:,0,:]
        if len(xs)<0.6*S*S: continue
        polys.append(tom(c).round(3).tolist())
sk=skeletonize(thin); ys,xs=np.nonzero(sk); pts=set(zip(ys.tolist(),xs.tolist()))
G=nx.Graph()
for y,x in pts:
    for dy,dx in ((0,1),(1,0),(1,1),(1,-1)):
        if (y+dy,x+dx) in pts: G.add_edge((y,x),(y+dy,x+dx),w=np.hypot(dy,dx))
lines=[]
for cc in nx.connected_components(G):
    T=nx.minimum_spanning_tree(G.subgraph(cc),weight='w')
    if T.size(weight='w')<0.3*S: continue
    deg=dict(T.degree()); starts=[v for v in T if deg[v]!=2] or [next(iter(T))]
    seen=set()
    for s in starts:
        for nb in T[s]:
            if frozenset((s,nb)) in seen: continue
            path=[s,nb]; seen.add(frozenset((s,nb)))
            while deg[path[-1]]==2:
                nxt=[u for u in T[path[-1]] if u!=path[-2]][0]; seen.add(frozenset((path[-1],nxt))); path.append(nxt)
            if len(path)<0.25*S: continue
            p=np.array([(x,y) for y,x in path],np.float32).reshape(-1,1,2)
            p=cv2.approxPolyDP(p,0.8,False)[:,0,:]
            lines.append(tom(p).round(3).tolist())
for k in range(6): zc=-29.5-k; rects.append([[-1.88,zc-0.2],[2.08,zc-0.2],[2.08,zc+0.2],[-1.88,zc+0.2]])
json.dump(dict(edges=edges,rects=rects,polys=polys,lines=lines,posts=posts),open('marks.json','w'))
print('edges',len(edges),'rects',len(rects),'polys',len(polys),'lines',len(lines),'posts',len(posts))
vis=(im*0.45).astype(np.uint8)
for e in edges: cv2.polylines(vis,[np.array([px(*q) for q in e]).round().astype(np.int32)],False,(255,255,0),2)
for r in rects: cv2.fillPoly(vis,[np.array([px(*q) for q in r]).round().astype(np.int32)],(0,255,255))
for p in polys: cv2.fillPoly(vis,[np.array([px(*q) for q in p]).round().astype(np.int32)],(0,220,0))
for l in lines: cv2.polylines(vis,[np.array([px(*q) for q in l]).round().astype(np.int32)],False,(255,0,255),2)
for x,z in posts: cv2.circle(vis,tuple(int(v) for v in px(x,z)),5,(0,0,255),-1)
vis[rm]=(vis[rm]*0.3).astype(np.uint8)
for n,(x0,y0) in {'nw':(0,0),'ne':(958,0),'sw':(0,958),'se':(958,958)}.items():
    cv2.imwrite(f'mk_{n}.png',vis[y0:y0+959,x0:x0+959])
