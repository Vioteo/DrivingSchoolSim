import numpy as np, cv2, json
from scipy.spatial import Delaunay
from scipy import ndimage as ndi
S=19.17
road=np.load('road_fixed.npy'); H,W=road.shape
def tom(p): return np.stack([p[:,0]/S-50, 50-p[:,1]/S],1)
def smooth_closed(c,sig=2.0):
    c=c.astype(float); n=len(c)
    return np.stack([ndi.gaussian_filter1d(c[:,i],sig,mode='wrap') for i in range(2)],1)
def resample_closed(c,step):
    d=np.r_[0,np.cumsum(np.hypot(*np.diff(np.vstack([c,c[:1]]),axis=0).T))]
    L=d[-1]; n=max(8,int(L/step)); t=np.linspace(0,L,n,endpoint=False)
    cc=np.vstack([c,c[:1]])
    return np.stack([np.interp(t,d,cc[:,0]),np.interp(t,d,cc[:,1])],1)
cs,hier=cv2.findContours(road.astype(np.uint8),cv2.RETR_CCOMP,cv2.CHAIN_APPROX_NONE)
loops=[]
for c in cs:
    c=c[:,0,:]
    if len(c)<20: continue
    c=smooth_closed(c,5.0); c=resample_closed(c,0.2*S)   # 20 cm spacing in px
    loops.append(c)
print('loops',len(loops),[len(l) for l in loops])
# Delaunay with interior fill points
pts=[np.vstack(loops)]
gy,gx=np.mgrid[0:H:int(1.5*S),0:W:int(1.5*S)]
g=np.stack([gx.ravel(),gy.ravel()],1)
dist=ndi.distance_transform_edt(road)
keep=dist[g[:,1],g[:,0]]>0.5*S
pts.append(g[keep].astype(float))
P=np.vstack(pts)
tri=Delaunay(P)
cen=P[tri.simplices].mean(1)
ci=np.clip(cen.round().astype(int),0,[W-1,H-1])
inside=road[ci[:,1],ci[:,0]]
T=tri.simplices[inside]
# also drop triangles with any edge longer than 3m (bridging concavities)
e=np.max(np.stack([np.hypot(*(P[T[:,a]]-P[T[:,b]]).T) for a,b in [(0,1),(1,2),(2,0)]]),0)
T=T[e<3*S]
V=tom(P)
json.dump({'loops':[tom(l).round(3).tolist() for l in loops]},open('loops.json','w'))
np.save('roadV.npy',V); np.save('roadT.npy',T)
# check raster
img=np.zeros((H,W),np.uint8)
for t in T: cv2.fillPoly(img,[P[t].round().astype(np.int32)],255)
diff=(img>0)!=road
print('tri',len(T),'mismatch px',diff.sum(),'= m2',diff.sum()/S/S)
vis=np.dstack([road*200,img,np.zeros_like(img)]).astype(np.uint8)
cv2.imwrite('tri_check.png',cv2.resize(vis,(1000,1000)))
