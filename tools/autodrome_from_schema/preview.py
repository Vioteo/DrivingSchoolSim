import json, numpy as np, cv2, math, sys
d=json.load(open('out/autodrome-schema.json'))
K=16; N=100*K
img=np.full((N,N,3),(118,120,120),np.uint8)
def P(x,z): return [int((x+50)*K),int((50-z)*K)]
V=np.array(d['road']['v']).reshape(-1,2); T=np.array(d['road']['t']).reshape(-1,3)
for t in T: cv2.fillPoly(img,[np.array([P(*V[i]) for i in t],np.int32)],(64,66,68))
V=np.array(d['paint']['v']).reshape(-1,2); T=np.array(d['paint']['t']).reshape(-1,3)
for t in T: cv2.fillPoly(img,[np.array([P(*V[i]) for i in t],np.int32)],(245,245,245),lineType=cv2.LINE_AA)
cv2.rectangle(img,tuple(P(-26.8,9.3)),tuple(P(-13.9,5.9)),(140,160,170),-1)
for x,z in np.array(d['cones']).reshape(-1,2): cv2.circle(img,tuple(P(x,z)),4,(0,120,255),-1)
for s in d['signs']:
    p=P(s['x'],s['z']); a=math.radians(s['heading']); dx,dz=math.sin(a),math.cos(a)
    cv2.circle(img,tuple(p),7,(40,40,220),-1); cv2.arrowedLine(img,tuple(p),tuple(P(s['x']-dx*2,s['z']-dz*2)),(40,40,220),2,tipLength=.4)
for s in d['signals']: cv2.circle(img,tuple(P(s['x'],s['z'])),9,(20,200,20),-1)
cv2.imwrite('plan_preview.png',cv2.resize(img,(1400,1400),interpolation=cv2.INTER_AREA))
if len(sys.argv)>1:
    x0,z0,x1,z1=map(float,sys.argv[1:5]); a=P(x0,z1); b=P(x1,z0); cv2.imwrite('plan_zoom.png',img[a[1]:b[1],a[0]:b[0]])
