import numpy as np, cv2
S=19.17
road=np.load('road.npy').astype(np.uint8)
def px(x,z): return [int(round((x+50)*S)),int(round((50-z)*S))]
def poly(pts,val):
    cv2.fillPoly(road,[np.array([px(*p) for p in pts],np.int32)],val)
def rect(x0,z0,x1,z1,val): poly([(x0,z0),(x1,z0),(x1,z1),(x0,z1)],val)
# У3 strip: clear area then redraw clean shape
rect(-40,3.8,1,9.2,0)
poly([(-38.6,9.25),(-35.5,6.0),(-3.25,6.0),(-0.1,9.25)],1)
rect(38,-50,50,-38,0)          # shelter drawn dark
rect(-50,26,-48.3,34,0); rect(-42.4,26,-40,34,0)   # railway symbol spill
np.save('road_fixed.npy',road.astype(bool))
im=cv2.imread('schema_hi.png')[81:81+1917,78:78+1917]
ov=im.copy(); ov[road>0]=(ov[road>0]*0.35+np.array([255,120,0])*0.65).astype(np.uint8)
for n,(x0,y0) in {'nw':(0,0),'ne':(958,0),'sw':(0,958),'se':(958,958)}.items():
    cv2.imwrite(f'fx_{n}.png',ov[y0:y0+959,x0:x0+959])
# --- generic notch repair near signs / arrows
from scipy import ndimage as ndi
im2=cv2.imread('schema_hi.png')[81:81+1917,78:78+1917]
g=cv2.cvtColor(im2,cv2.COLOR_BGR2GRAY); satm=np.load('sat.npy')
glyph=ndi.binary_dilation((satm>40)|(g<90),iterations=3)
r=np.load('road_fixed.npy').astype(np.uint8)
k=cv2.getStructuringElement(cv2.MORPH_ELLIPSE,(35,35))
sm=cv2.morphologyEx(cv2.morphologyEx(r,cv2.MORPH_CLOSE,k),cv2.MORPH_OPEN,k)
d=(sm!=r); l,n=ndi.label(d); cnt=0
for i,sl in enumerate(ndi.find_objects(l),1):
    c=(l[sl]==i); a=c.sum()
    if a<3*S*S and (glyph[sl][c]).mean()>0.5:
        r[sl][c]=sm[sl][c]; cnt+=1
road=r
rect(-17,13.2,-12,15.05,1); rect(2,13.2,6.5,15.05,1)
# straight carriageways drawn with a ruler on the scheme: enforce their edges
rect(-38,42.45,38,48.5,1); rect(-38,48.5,38,50,0)
rect(-30,-35.05,36,-29.0,1); rect(-30,-38.5,12.6,-35.05,0); rect(20.2,-38.5,36,-35.05,0)
rect(-48.4,-18,-42.45,38,1); rect(-50,-18,-48.4,38,0); rect(-42.44,16,-40,38,0)
rect(42.5,-20,48.5,38,1); rect(48.5,-20,50,38,0)
rect(13.35,-39.8,19.4,42.5,1)
rect(-42.4,9.05,42.5,15.0,1)
np.save('road_fixed.npy',road.astype(bool)); print('patched',cnt)
