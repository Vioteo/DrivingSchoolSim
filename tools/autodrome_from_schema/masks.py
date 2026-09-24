import numpy as np, cv2
from scipy import ndimage as ndi
S=19.17; X0=78; Y0=81; N=1917
im=cv2.cvtColor(cv2.imread('schema_hi.png'),cv2.COLOR_BGR2RGB).astype(int)
sub=im[Y0:Y0+N, X0:X0+N]
r,g,b=sub[...,0],sub[...,1],sub[...,2]
sat=np.max(sub,2)-np.min(sub,2)
road=(np.abs(r-150)<=8)&(np.abs(g-149)<=8)&(np.abs(b-147)<=8)&(sat<8)
zone=(np.abs(r-169)<=5)&(sat<5)
white=(np.min(sub,2)>215)&(sat<30)
def m2px(x,z): return (x+50)*S,(50-z)*S
remove=[(-15.6,15.3,0.4,42.4),(19.4,22.8,42.4,30.2),(19.4,-25.6,42.4,-11.6),(-42.4,-10.6,-20.8,3.7)]
rm=np.zeros(road.shape,bool)
for x0,z0,x1,z1 in remove:
    a=m2px(x0,z1); c=m2px(x1,z0); rm[int(a[1]):int(c[1])+1,int(a[0]):int(c[0])+1]=True
lab=np.full(road.shape,-1,np.int8); lab[road]=1; lab[zone]=0
# nearest-label fill for unknown pixels
d,(iy,ix)=ndi.distance_transform_edt(lab<0,return_indices=True)
full=lab[iy,ix]
roadm=(full==1)&~rm
# cleanup
k=cv2.getStructuringElement(cv2.MORPH_ELLIPSE,(15,15))
roadm=cv2.morphologyEx(roadm.astype(np.uint8),cv2.MORPH_OPEN,k)
roadm=cv2.morphologyEx(roadm,cv2.MORPH_CLOSE,k).astype(bool)
# fill small holes (<25 m2) and drop small islands
hl,n=ndi.label(~roadm); sz=ndi.sum(np.ones_like(hl),hl,range(1,n+1))
for i,s in enumerate(sz,1):
    if s<25*S*S: roadm[hl==i]=True
rl,n=ndi.label(roadm); sz=ndi.sum(np.ones_like(rl),rl,range(1,n+1))
for i,s in enumerate(sz,1):
    if s<25*S*S: roadm[rl==i]=False
np.save('road.npy',roadm); np.save('white.npy',white&~rm); np.save('rm.npy',rm)
np.save('sat.npy',sat)
out=np.full(road.shape+(3,),220,np.uint8); out[roadm]=90; out[white&~rm]=[255,60,60]
cv2.imwrite('road_check.png',cv2.resize(out[...,::-1],(1000,1000),interpolation=cv2.INTER_AREA))
print('done', roadm.mean())
