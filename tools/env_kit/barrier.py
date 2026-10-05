"""Road safety barriers (отбойники).

Metal barrier: single-sided W-beam, group 11ДО style (ГОСТ Р 52607-2006 /
ГОСТ 33128-2014): beam height 0.31 m, top of beam 0.75 m above the shoulder,
C-channel posts every 2 m with spacers (консоль-амортизатор), splice every
4 m, reflectors on the spacers. Concrete: New Jersey profile 0.81 m.

Frame: the barrier runs along author +Y (Unity +Z) from Socket_Start (0,0,0)
to Socket_End (0,0,4) on the post line; the traffic face looks to -X (the road
is on the left). Put it on the right edge of a road running +Y; for the left
edge turn it 180 deg. Reflectors: red facing traffic moving +Y (front -Y face),
white on the back.
"""
import math
from mathutils import Vector, Matrix

from .core import MB, Asset, cw, to_blender

BEAM_C = 0.6            # beam centre height
SPACER = 0.17           # post line -> back of the beam
THK = 0.004


def w_profile():
    """Closed W-beam section in (lat, h), lat to the road (-X) = negative, h around the beam centre."""
    front = [(0.0, 0.155), (0.012, 0.15), (0.083, 0.1), (0.083, 0.05), (0.038, 0.012), (0.038, -0.012),
             (0.083, -0.05), (0.083, -0.1), (0.012, -0.15), (0.0, -0.155)]
    back = [(d - THK, h) for d, h in reversed(front)]
    loop = [(-(SPACER + d), BEAM_C + h) for d, h in front + back]
    return cw(loop)


def beam(mb, frames, mat='EN_Steel_Galv'):
    mb.sweep(frames, w_profile(), mat, closed=True, smooth=True)
    # end caps
    prof = w_profile()
    for fr, rev in ((frames[0], True), (frames[-1], False)):
        pos, right, up = (Vector(x) for x in fr)
        pts = [pos + right * l + up * h for l, h in prof]
        mb.ngon(list(reversed(pts)) if rev else pts, mat)


def straight_frames(y0, y1, n=2, dx=0.0):
    return [(Vector((dx, y0 + (y1 - y0) * i / (n - 1), 0)), Vector((1, 0, 0)), Vector((0, 0, 1))) for i in range(n)]


def c_post(mb, y, h=0.72, z0=-0.1):
    """C-channel 100 x 50, web facing the road."""
    poly = [(-0.05, -0.05), (0.0, -0.05), (0.0, -0.044), (-0.044, -0.044), (-0.044, 0.044), (0.0, 0.044),
            (0.0, 0.05), (-0.05, 0.05)]
    mb.prism([(x + 0.025, yy + y) for x, yy in poly], z0, h, 'EN_Steel_Galv')
    mb.box((0, y, h + 0.003), (0.055, 0.105, 0.006), 'EN_Steel_Galv')


def spacer(mb, y, reflector=True):
    # консоль: bent plate between post and beam
    mb.box((-0.025 - (SPACER - 0.025) / 2, y, BEAM_C), (SPACER - 0.025, 0.07, 0.26), 'EN_Steel_Galv')
    for dz in (-0.04, 0.04):     # bolts through the beam valley
        mb.cyl((-(SPACER + 0.035), y, BEAM_C + dz), (-(SPACER + 0.05), y, BEAM_C + dz), 0.014, seg=8,
               mat='EN_Steel_Dark')
    if reflector:
        # КД-type reflector on a bracket above the beam
        # КД-type reflector on a small bracket on top of the beam
        mb.box((-(SPACER + 0.02), y, BEAM_C + 0.175), (0.01, 0.03, 0.05), 'EN_Steel_Galv')
        mb.box((-(SPACER + 0.02), y - 0.006, BEAM_C + 0.215), (0.05, 0.003, 0.045), 'EN_Reflector_Red')
        mb.box((-(SPACER + 0.02), y + 0.006, BEAM_C + 0.215), (0.05, 0.003, 0.045), 'EN_Reflector_White')
        mb.box((-(SPACER + 0.02), y, BEAM_C + 0.215), (0.054, 0.009, 0.05), 'EN_Plastic_Grey')


def splice(mb, y):
    """Overlap of two beams: a short outer layer with 8 bolts."""
    fr = [(Vector((-THK - 0.0015, y - 0.16, 0)), Vector((1, 0, 0)), Vector((0, 0, 1))),
          (Vector((-THK - 0.0015, y + 0.16, 0)), Vector((1, 0, 0)), Vector((0, 0, 1)))]
    beam(mb, fr)
    for dy in (-0.11, -0.04, 0.04, 0.11):
        for dz in (-0.075, 0.075):
            mb.cyl((-(SPACER + 0.083 + THK), y + dy, BEAM_C + dz), (-(SPACER + 0.095), y + dy, BEAM_C + dz),
                   0.012, seg=8, mat='EN_Steel_Dark')


def wbeam_path(R, concave):
    """Beam line (post line) of one 4 m section: straight, or an arc of radius R whose centre is on the road
    side (concave = the road is on the inside of the bend, i.e. the barrier is on the outer edge of a curve)."""
    def f(s):
        if R is None:
            return Vector((0, s, 0)), Vector((0, 1, 0)), 0.0
        t = s / R
        if concave:
            return Vector((-R + R * math.cos(t), R * math.sin(t), 0)), Vector((-math.sin(t), math.cos(t), 0)), t
        return Vector((R - R * math.cos(t), R * math.sin(t), 0)), Vector((math.sin(t), math.cos(t), 0)), -t
    return f


def at(mb, builder, pos, yaw):
    """Builds a detail at the origin of a temporary builder and places it on the beam line."""
    tmp = MB()
    builder(tmp)
    mb.extend(tmp, Matrix.Translation(pos) @ Matrix.Rotation(yaw, 4, 'Z'))


def wbeam_4m(M, R=None, concave=True):
    if R is None:
        aid, title = 'EN_Barrier_WBeam_4m', 'Барьерное ограждение (W-балка), секция 4 м'
    else:
        aid = f'EN_Barrier_WBeam_4m_R{int(R)}' + ('' if concave else '_Convex')
        title = f'Барьерное ограждение, секция 4 м по дуге R{int(R)} ' + \
                ('(дорога внутри поворота — внешняя кромка)' if concave else '(дорога снаружи — внутренняя кромка)')
    a = Asset(aid, title, 'barrier', M, min_z=-0.1, length=4.0, post_spacing=2.0, radius=R,
              required=['Socket_Start', 'Socket_End'])
    f = wbeam_path(R, concave)
    mb = MB()
    n = 2 if R is None else 9
    fr = []
    for i in range(n):
        p, t, _ = f(4.0 * i / (n - 1))
        fr.append((p, Vector((t.y, -t.x, 0)), Vector((0, 0, 1))))
    beam(mb, fr)
    for s_ in (0.0, 2.0):
        p, t, yaw = f(s_)
        at(mb, lambda m: c_post(m, 0.0), p, yaw)
        at(mb, lambda m, r=(s_ == 0.0): spacer(m, 0.0, reflector=r), p, yaw)
    p, t, yaw = f(0.0)
    at(mb, lambda m: splice(m, 0.0), p, yaw)
    a.mesh(f'{a.id}_Mesh', mb)
    col = MB()
    for s_ in (1.0, 3.0):
        p, t, yaw = f(s_)
        tmp = MB()
        tmp.box((-(SPACER + 0.04), 0, BEAM_C - 0.05), (0.13, 2.05, 0.45), 'EN_Collision')
        tmp.box((0.0, 0, 0.2), (0.06, 2.05, 0.4), 'EN_Collision')
        col.extend(tmp, Matrix.Translation(p) @ Matrix.Rotation(yaw, 4, 'Z'))
    a.mesh('COL_Barrier', col)
    a.socket('Socket_Start', (0, 0, 0))
    pe, te, yawe = f(4.0)
    a.socket('Socket_End', tuple(pe), yaw=yawe)
    a.meta['sockets'] = {'Socket_Start': (0, 0, 0), 'Socket_End': to_blender(tuple(pe))}
    a.meta['end_heading_deg'] = round(math.degrees(yawe), 3)
    if R is None:
        a.meta['expect_size'] = {'y': (4.0, 4.4), 'z': (0.85, 0.95)}
    return a


def ramp_mb(start=False):
    """Отгон in the barrier frame: the beam goes down into the ground over 4 m and away from the road by 0.5 m.
    End version continues along +Y from the joint at the origin; start version rises from y = -4 to the origin."""
    mb = MB()
    fr = []
    n = 12
    for i in range(n + 1):
        t = i / n
        u = 1 - t if start else t           # 0 at the joint, 1 in the ground
        e = u * u * (3 - 2 * u)
        y = -4.0 + 4.0 * t if start else 4.0 * t
        fr.append((Vector((0.5 * e, y, -(BEAM_C + 0.05) * e)), Vector((1, 0, 0)), Vector((0, 0, 1))))
    beam(mb, fr)
    splice(mb, 0.0)
    c_post(mb, 0.0)
    spacer(mb, 0.0, reflector=True)
    ye = -4.0 if start else 4.0
    mb.box((0.33, ye, -0.15), (0.5, 0.5, 0.3), 'EN_Concrete')      # anchor where the beam enters the ground
    return mb


def run_mb(frame, length, ramps=True):
    """Continuous barrier along a path. frame(t) -> (pos, x_axis, y_axis) for t in [0, length] metres along
    the post line; x_axis points away from the road (beam on -x), y_axis is the running direction.
    Posts every 2 m, splices and reflectors every 4 m, ramps (отгон) at both ends."""
    mb = MB()
    n = max(2, int(length / 0.5) + 1)
    fr = []
    for i in range(n):
        p, x, y = frame(length * i / (n - 1))
        fr.append((p, x, Vector((0, 0, 1))))
    beam(mb, fr)

    def place(t, fn):
        p, x, y = frame(t)
        m = Matrix((
            (x.x, y.x, 0, p.x),
            (x.y, y.y, 0, p.y),
            (x.z, y.z, 1, p.z),
            (0, 0, 0, 1)))
        tmp = MB()
        fn(tmp)
        mb.extend(tmp, m)
    k = 0
    t = 1.0
    while t < length - 0.5:
        place(t, lambda m: c_post(m, 0.0))
        place(t, lambda m, r=(k % 2 == 0): spacer(m, 0.0, reflector=r))
        if k % 2 == 0:
            place(t, lambda m: splice(m, 0.0))
        k += 1
        t += 2.0
    if ramps:
        place(0.0, lambda m: m.extend(ramp_mb(start=True)))
        place(length, lambda m: m.extend(ramp_mb(start=False)))
    return mb


def wbeam_end_ramp(M, start=False):
    """End version: Socket_Start (0,0,0) joins a section's Socket_End, the ramp continues along +Y.
    Start version: the ramp rises from the ground at y = -4 to Socket_End (0,0,0), which joins the
    Socket_Start of the first section (a mirror of the end version, the traffic face stays at -X)."""
    aid = 'EN_Barrier_WBeam_EndRamp_Start' if start else 'EN_Barrier_WBeam_EndRamp'
    a = Asset(aid, 'Барьерное ограждение: ' + ('начальный' if start else 'концевой') + ' участок с отгоном (4 м)',
              'barrier', M, min_z=-0.6)
    mb = ramp_mb(start)
    ye = -4.0 if start else 4.0
    a.mesh(f'{aid}_Mesh', mb)
    col = MB()
    col.box((0.05, ye / 2, 0.25), (0.55, 4.0, 0.5), 'EN_Collision')
    a.mesh('COL_Barrier', col)
    sock = 'Socket_End' if start else 'Socket_Start'
    a.socket(sock, (0, 0, 0))
    a.meta['sockets'] = {sock: (0, 0, 0)}
    a.meta['required'] = [sock]
    return a


def wbeam_end_cap(M):
    """Торцевой элемент: the beam bends back round to the post line (where a ramp does not fit)."""
    a = Asset('EN_Barrier_WBeam_EndCap', 'Барьерное ограждение: торцевой закруглённый элемент', 'barrier', M,
              min_z=-0.1, required=['Socket_Start'])
    mb = MB()
    r = 0.2
    cx = -(SPACER) + r       # centre of the bend, behind the beam
    fr = []
    n = 10
    for i in range(n + 1):
        t = math.pi * 0.5 * i / n
        pos = Vector((cx - r * math.cos(t) + SPACER, 0.3 + r * math.sin(t), 0))
        tang = Vector((math.sin(t), math.cos(t), 0))
        right = Vector((tang.y, -tang.x, 0))
        fr.append((pos, right, Vector((0, 0, 1))))
    straight = straight_frames(0.0, 0.3)
    beam(mb, straight)
    beam(mb, fr)
    c_post(mb, 0.0)
    spacer(mb, 0.0)
    a.mesh(f'{a.id}_Mesh', mb)
    col = MB()
    col.box((-0.1, 0.35, BEAM_C - 0.05), (0.35, 0.7, 0.45), 'EN_Collision')
    a.mesh('COL_Barrier', col)
    a.socket('Socket_Start', (0, 0, 0))
    a.meta['sockets'] = {'Socket_Start': (0, 0, 0)}
    return a


def nj_profile():
    half = [(0.305, 0.0), (0.305, 0.075), (0.18, 0.33), (0.075, 0.81)]
    loop = half + [(-x, h) for x, h in reversed(half)]
    return cw(loop)


def concrete_nj(M):
    a = Asset('EN_Barrier_Concrete_NJ_4m', 'Бетонное ограждение «Нью-Джерси», блок 4 м', 'barrier', M, min_z=-0.01,
              required=['Socket_Start', 'Socket_End'])
    mb = MB()
    prof = nj_profile()
    L = 3.98
    fr = straight_frames(0.01, 0.01 + L)
    mb.sweep(fr, prof, 'EN_Concrete', closed=True)
    for fr_, rev in ((fr[0], True), (fr[-1], False)):
        pos, right, up = (Vector(x) for x in fr_)
        pts = [pos + right * l + up * h for l, h in prof]
        mb.ngon(list(reversed(pts)) if rev else pts, 'EN_Concrete')
    # drainage / forklift slots and red-white reflective plates on both faces
    for y in (1.0, 3.0):
        mb.box((0, y, 0.04), (0.62, 0.3, 0.06), 'EN_Steel_Dark')
    for sx in (-1, 1):
        for i, y in enumerate((0.6, 1.4, 2.2, 3.0)):
            m = 'EN_Paint_Red' if i % 2 == 0 else 'EN_Paint_White'
            mb.obox((sx * 0.135, y + 0.2, 0.55), (0.004, 0.36, 0.14), 0.0, m, roll=-sx * math.atan2(0.105, 0.48))
    mb.box((0, L / 2, 0.815), (0.14, 0.12, 0.012), 'EN_Reflector_Amber')
    a.mesh(f'{a.id}_Mesh', mb)
    col = MB()
    col.sweep(fr, prof, 'EN_Collision', closed=True)
    a.mesh('COL_Barrier', col)
    a.socket('Socket_Start', (0, 0, 0))
    a.socket('Socket_End', (0, 4.0, 0))
    a.meta['sockets'] = {'Socket_Start': (0, 0, 0), 'Socket_End': to_blender((0, 4.0, 0))}
    a.meta['expect_size'] = {'x': (0.6, 0.62), 'z': (0.8, 0.83)}
    return a


ALL = [wbeam_4m, lambda M: wbeam_4m(M, 56, True), lambda M: wbeam_4m(M, 44, False), wbeam_end_ramp, lambda M: wbeam_end_ramp(M, start=True), wbeam_end_cap, concrete_nj]
