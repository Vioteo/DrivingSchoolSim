import numpy as np, cv2, json
from scipy import ndimage as ndi
S=19.17
white=np.load('white.npy'); rm=np.load('rm.npy')
H,W=white.shape
im=cv2.imread('schema_hi.png')[81:81+H,78:78+W]
dt=ndi.distance_transform_edt(white)
core=dt>=2.6
l,n=ndi.label(core); objs=ndi.find_objects(l)
posts=[]
for i,sl in enumerate(objs,1):
    c=(l[sl]==i); a=c.sum(); h=sl[0].stop-sl[0].start; w=sl[1].stop-sl[1].start
    if 2<=a<=40 and max(h,w)<=7 and dt[sl][c].max()>=3.0:
        cy,cx=ndi.center_of_mass(c); cx+=sl[1].start; cy+=sl[0].start
        if rm[int(cy),int(cx)]: continue
        posts.append((cx,cy))
P=np.array(posts); M=np.stack([P[:,0]/S-50,50-P[:,1]/S],1)
json.dump(M.round(3).tolist(),open('posts.json','w'))
vis=(im*0.6).astype(np.uint8)
for x,y in posts: cv2.circle(vis,(int(x),int(y)),6,(0,0,255),2)
cv2.imwrite('posts_check.png',cv2.resize(vis,(1400,1400)))
print(len(posts))
