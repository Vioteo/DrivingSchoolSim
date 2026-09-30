from PIL import Image, ImageDraw, ImageFont
import math, os
os.makedirs('out/SignFaces',exist_ok=True)
N=512; RED=(200,16,30,255); BLUE=(0,82,165,255); WHITE=(255,255,255,255); BLACK=(20,20,20,255)
F=ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf',230)
def canvas(): return Image.new('RGBA',(N,N),(0,0,0,0))
def circle(d,fill,r=250): d.ellipse([N/2-r,N/2-r,N/2+r,N/2+r],fill=fill)
def arrow(d,pts,w,head,col):
    d.line(pts,fill=col,width=w,joint='curve')
    (x0,y0),(x1,y1)=pts[-2],pts[-1]; a=math.atan2(y1-y0,x1-x0)
    tip=(x1+head*0.9*math.cos(a),y1+head*0.9*math.sin(a))
    d.polygon([tip,(x1+head*math.cos(a+2.2),y1+head*math.sin(a+2.2)),(x1+head*math.cos(a-2.2),y1+head*math.sin(a-2.2))],fill=col)
def save(im,n): im.save(f'out/SignFaces/{n}.png')
# 3.24 speed 20
im=canvas(); d=ImageDraw.Draw(im); circle(d,RED); circle(d,WHITE,195); d.text((N/2,N/2+8),'20',font=F,fill=BLACK,anchor='mm'); save(im,'Speed20')
# 4.6 minimum speed 20
im=canvas(); d=ImageDraw.Draw(im); circle(d,WHITE); circle(d,BLUE,238); d.text((N/2,N/2+8),'20',font=F,fill=WHITE,anchor='mm'); save(im,'MinSpeed20')
# 3.18.2 no left turn
im=canvas(); d=ImageDraw.Draw(im); circle(d,RED); circle(d,WHITE,195)
arrow(d,[(300,410),(300,230),(190,230)],46,80,BLACK)
d.line([(N/2-138,N/2-138),(N/2+138,N/2+138)],fill=RED,width=52); save(im,'NoLeftTurn')
# 4.1.4 straight or right
im=canvas(); d=ImageDraw.Draw(im); circle(d,WHITE); circle(d,BLUE,238)
arrow(d,[(225,420),(225,150)],46,80,WHITE); arrow(d,[(225,330),(225,280),(330,280)],46,80,WHITE); save(im,'StraightOrRight')
def tri(d,up=True):
    h=440; top=40
    pts=[(N/2,top),(N/2+254,top+h),(N/2-254,top+h)]
    d.polygon(pts,fill=RED); 
    k=0.72; c=(N/2,top+h*2/3)
    d.polygon([(c[0]+(x-c[0])*k,c[1]+(y-c[1])*k) for x,y in pts],fill=WHITE)
# 1.12.1 dangerous curves (first right)
im=canvas(); d=ImageDraw.Draw(im); tri(d)
arrow(d,[(236,420),(236,380),(290,330),(290,290),(236,240),(236,215)],30,55,BLACK); save(im,'Curves')
# 1.13 steep ascent / 1.14 steep descent
Fs=ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf',70)
for n,up in (('Ascent',True),('Descent',False)):
    im=canvas(); d=ImageDraw.Draw(im); tri(d)
    if up: d.polygon([(130,420),(382,420),(382,280)],fill=BLACK)
    else: d.polygon([(130,420),(382,420),(130,280)],fill=BLACK)
    d.text(((205 if up else 307),300),'12%',font=ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf',52),fill=BLACK,anchor='mm')
    save(im,n)
# 8.13 main road direction plate (rectangular, 3:2)
im=Image.new('RGBA',(N,340),(0,0,0,0)); d=ImageDraw.Draw(im)
d.rounded_rectangle([4,4,N-4,336],24,fill=WHITE,outline=BLACK,width=10)
d.line([(N/2,300),(N/2,170),(N-90,170)],fill=BLACK,width=46); d.line([(90,170),(N/2,170)],fill=BLACK,width=16); d.line([(N/2,170),(N/2,60)],fill=BLACK,width=16)
im.save('out/SignFaces/Plate813.png')
print(os.listdir('out/SignFaces'))
# T68: exercise numbers for the zone boards (У1…У9 in the order of the exam route), white on a transparent field;
# the builder puts one quad on each face of the teal board, so the number is never seen mirrored through it.
FB=ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf',200)
for n in range(1,10):
    im=Image.new('RGBA',(512,256),(0,0,0,0)); d=ImageDraw.Draw(im)
    d.text((256,136),'У'+str(n),font=FB,fill=WHITE,anchor='mm'); save(im,f'Board_{n}')
# T69: 6.16 "Stop line" — a horizontal blue plate 3:1 (type size II 1050 x 350 mm, ГОСТ Р 52290-2004 via the maker's
# size table), white rim, the word СТОП over a white bar the full width of the field. Stands at the stop line
# (the sign and the marking in one cross-section) on the signal pole.
W,H=1536,512
im=Image.new('RGBA',(W,H),(0,0,0,0)); d=ImageDraw.Draw(im)
d.rounded_rectangle([4,4,W-4,H-4],26,fill=WHITE); d.rounded_rectangle([26,26,W-26,H-26],16,fill=BLUE)
FC=ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSansCondensed-Bold.ttf',236)
d.text((W/2,196),'СТОП',font=FC,fill=WHITE,anchor='mm')
d.rectangle([62,344,W-62,432],fill=WHITE)
save(im,'StopLine')
# T69: backs of the textured signs — the plate's silhouette in grey steel, no mirrored face showing through
# (the builder puts <face>_Back.png on the rear quad).
for n in sorted(os.listdir('out/SignFaces')):
    if not n.endswith('.png') or n.endswith('_Back.png') or n.startswith('Board_'): continue
    f=Image.open('out/SignFaces/'+n).convert('RGBA')
    back=Image.new('RGBA',f.size,(150,156,162,255)); back.putalpha(f.getchannel('A'))
    back.save('out/SignFaces/'+n[:-4]+'_Back.png')
