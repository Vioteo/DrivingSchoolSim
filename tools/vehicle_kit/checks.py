"""Automated fit and ergonomics checks on an assembled vehicle (no bpy)."""
import math
from .geom import rot_euler, add, sub, dot, cross, norm


def world_parts(parts):
    by = {p.name: p for p in parts}

    def xf(p, v):
        while p is not None:
            v = add(rot_euler(v, p.rot), p.loc)
            p = by.get(p.parent) if p.parent else None
        return v

    out = {}
    for p in parts:
        if p.kind == 'mesh' and p.v and not p.hidden:
            out.setdefault(p.name, []).append(([xf(p, v) for v in p.v], p.f))
    return out, by


def _tris(W, prefixes):
    for name, meshes in W.items():
        if any(name.startswith(pr) for pr in prefixes):
            for V, F in meshes:
                for q in F:
                    for k in range(1, len(q) - 1):
                        yield V[q[0]], V[q[k]], V[q[k + 1]]


def ray_tri(o, d, a, b, c):
    e1, e2 = sub(b, a), sub(c, a)
    p = cross(d, e2)
    det = dot(e1, p)
    if abs(det) < 1e-12:
        return None
    inv = 1.0 / det
    s = sub(o, a)
    u = dot(s, p) * inv
    if u < 0 or u > 1:
        return None
    q = cross(s, e1)
    v = dot(d, q) * inv
    if v < 0 or u + v > 1:
        return None
    t = dot(e2, q) * inv
    return t if t > 1e-6 else None


def first_hit(o, d, tris):
    best = None
    for a, b, c in tris:
        t = ray_tri(o, d, a, b, c)
        if t is not None and (best is None or t < best):
            best = t
    return best


def run(parts, info):
    S = info['spec']
    W, by = world_parts(parts)
    eye = by['Socket_DriverEye'].loc
    res = []

    def check(cid, ok, value, target):
        res.append(dict(id=cid, status='PASS' if ok else 'FAIL', value=value, target=target))

    solid_upper = list(_tris(W, ('Headliner', 'Roof', 'Greenhouse_Trim')))
    t = first_hit(eye, (0, 0, 1), solid_upper)
    head = t if t is not None else 9
    check('Потолок над глазами, м', head >= 0.16, round(head, 3), '>= 0.16 (голова ~0.12 над глазами + зазор)')
    back = list(_tris(W, ('Headrest', 'Backrest')))
    t = first_hit(eye, (0, -1, 0), back)
    check('Глаз перед подголовником, м', t is not None and 0.06 <= t <= 0.2, None if t is None else round(t, 3), '0.06..0.20')
    t = first_hit(eye, (0, 1, 0), back)
    check('Глаз не внутри сиденья', t is None or t > 0.5, None if t is None else round(t, 3), 'нет препятствия впереди')
    # upward vision through the windshield (traffic lights at the stop line)
    opaque = list(_tris(W, ('Roof', 'Headliner', 'Greenhouse_Trim', 'SunVisor', 'CentreMirror', 'MirrorSurface_Centre')))
    up = 0
    for deg in range(0, 45):
        a = math.radians(deg)
        d = (0, math.cos(a), math.sin(a))
        if first_hit(eye, d, opaque) is not None:
            break
        up = deg
    check('Обзор вверх через стекло, град', up >= 14, up, '>= 14 (светофор у стоп-линии)')
    # downward: the closest visible ground point ahead of the bumper
    blockers = [tr for name in W for tr in _tris({name: W[name]}, ('',))
                if not name.startswith(('Glass_', 'Wiper', 'Steering', 'Socket', 'Needle', 'Gauge', 'Cluster'))]
    ground = None
    for i in range(60, -1, -1):              # steepest clear ray = nearest visible ground
        a = math.radians(-2 - i * 0.5)
        d = (0, math.cos(a), math.sin(a))
        hit = first_hit(eye, d, blockers)
        tg = -eye[2] / d[2]
        if hit is None or hit >= tg:
            ground = eye[1] + d[1] * tg - S['nose_y']
            break
    check('Мёртвая зона перед бампером, м', ground is not None and ground <= 6.5, None if ground is None else round(ground, 2), '<= 6.5')
    # gauges visible through the wheel
    cab = info['cabin']
    if hasattr(cab, 'cluster_y'):
        tgt = (eye[0], cab.cluster_y, cab.cluster_z)
        d = norm(sub(tgt, eye))
        dist = math.sqrt(sum((tgt[k] - eye[k]) ** 2 for k in range(3)))
        wheel = list(_tris(W, ('Steering_Rim', 'Steering_Hub')))
        h = first_hit(eye, d, wheel)
        check('Приборы видны сквозь руль', h is None or h > dist - 0.01, 'видны' if (h is None or h > dist - 0.01) else 'закрыты', 'видны')
    # steering wheel to chest, pedals
    if 'SteeringWheel_Pivot' in by:
        wc = by['SteeringWheel_Pivot'].loc
        H = cab.H
        d_chest = wc[1] - (H[1] - 0.13 + math.sin(cab.recline) * 0.45) - 0.12
        check('Руль - грудь, м', 0.25 <= d_chest <= 0.45, round(d_chest, 3), '0.25..0.45')
        rim_bottom = wc[2] - 0.185 * math.cos(cab.wheel_tilt)
        thigh = H[2] + 0.13
        check('Зазор руль - бедро, м', rim_bottom - thigh >= 0.05, round(rim_bottom - thigh, 3), '>= 0.05')
    if hasattr(cab, 'pedal_pos'):
        p = cab.pedal_pos
        H = cab.H
        reach = math.sqrt((p[1] - H[1]) ** 2 + (p[2] - H[2]) ** 2)
        check('Таз - педаль тормоза, м', 0.78 <= reach <= 1.0, round(reach, 3), '0.78..1.00')
    # nothing of the interior outside the lower body (below the belt)
    sk = info['skin']
    worst, where = 0.0, None
    interior = ('Seat', 'Cushion', 'Backrest', 'Head', 'Dash', 'Instrument', 'DoorCard', 'DoorArmrest', 'DoorPull',
                'DoorSpeaker', 'Cabin_Floor', 'CentreConsole', 'Armrest', 'RearBench', 'RearHeadrests', 'Pedal', 'Glovebox',
                'Vent', 'CentreStack', 'Radio', 'RearParcelShelf', 'CargoFloor')
    for name, meshes in W.items():
        if not name.startswith(interior):
            continue
        for V, F in meshes:
            for x, y, z in V[::3]:
                if z >= sk.z_top(y) - 0.02 or abs(y) > S['nose_y'] - 0.3:
                    continue
                a = sk.a_side(y)
                p = sk.at(a)
                if z < sk.z_bottom(p[0], p[1]):
                    continue
                xs = sk.point(p, z)[0] - 0.012
                if abs(x) - xs > worst:
                    worst, where = abs(x) - xs, (name, round(x, 3), round(y, 3), round(z, 3))
    check('Салон не выходит за кузов, м', worst <= 0.003, round(worst, 3), '<= 0.003' + ('' if where is None else f' (худшее: {where})'))
    # nothing of the interior pokes through the glass or the roof
    shell = list(_tris(W, ('Glass_', 'Roof', 'Greenhouse_Trim')))
    worst2, where2 = 0.0, None
    for name, meshes in W.items():
        if not name.startswith(interior + ('Rear', 'Headrest', 'Steering', 'CentreMirror', 'SunVisor')):
            continue
        for V, F in meshes:
            for x, y, z in (V if len(V) < 800 else V[::5]):
                if z < sk.z_top(y) + 0.02:
                    continue
                t = first_hit((x, y, 5.0), (0, 0, -1), shell)
                if t is None:
                    continue
                zs = 5.0 - t
                if z - (zs - 0.01) > worst2:
                    worst2, where2 = z - (zs - 0.01), (name, round(x, 3), round(y, 3), round(z, 3))
    check('Салон не протыкает стёкла и крышу, м', worst2 <= 0.0, round(worst2, 3), '<= 0' + ('' if where2 is None else f' (худшее: {where2})'))
    # tyres inside the arches
    tyre_out = S['wheel_x'] + S['tire_w'] / 2
    check('Шина не торчит из кузова, м', tyre_out <= S['half_w'] + 0.01, round(tyre_out - S['half_w'], 3), '<= +0.01')
    gap = S['arch_r'] - S['tire_r']
    check('Зазор шина - арка, м', gap >= 0.06, round(gap, 3), '>= 0.06')
    return res
