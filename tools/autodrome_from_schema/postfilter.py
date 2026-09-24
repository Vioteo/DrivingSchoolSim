import numpy as np, json, cv2
from scipy import ndimage as ndi
S=19.17
P=np.array(json.load(open('posts.json'))); sat=np.load('sat.npy')
loops=[np.array(l) for l in json.load(open('loops.json'))['loops']]
allp=np.vstack(loops)
def dmin(p): return np.min(np.hypot(*(allp-p).T))
keep=[]
for x,z in P:
    px,py=int((x+50)*S),int((50-z)*S)
    s=sat[max(0,py-12):py+13,max(0,px-12):px+13]
    if (s>40).mean()>0.04: continue
    u10=(-43<=x<=12) and (abs(z-48.5)<.4 or abs(z-45.6)<.4)
    bar=(-26<=x<=-22 and -26<z<-24)
    if dmin((x,z))<=0.45 or u10 or bar: keep.append((x,z))
# manual corrections: drop three sign glyph false positives, add the posts of the box-exercise stop bar
bad=[(20.72,49.0),(20.36,40.86),(19.14,25.71)]
keep=[k for k in keep if min(np.hypot(k[0]-b[0],k[1]-b[1]) for b in bad)>1.0]
keep+=[(-16.8,-23.14),(-13.1,-23.14)]
rm=np.load('rm.npy'); keep=[k for k in keep if not rm[min(1916,int((50-k[1])*S)),min(1916,int((k[0]+50)*S))]]
print(len(P),'->',len(keep))
json.dump(np.round(keep,3).tolist(),open('posts_f.json','w'))
im=cv2.imread('schema_hi.png')[81:81+1917,78:78+1917]; vis=(im*.6).astype(np.uint8)
for x,z in keep: cv2.circle(vis,(int((x+50)*S),int((50-z)*S)),6,(0,0,255),2)
cv2.imwrite('posts_f.png',cv2.resize(vis,(1400,1400)))
