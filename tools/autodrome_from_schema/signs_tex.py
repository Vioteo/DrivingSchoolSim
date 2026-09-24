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
