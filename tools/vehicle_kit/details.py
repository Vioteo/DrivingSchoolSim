"""Surface details: lamps, grilles, intakes, handles, mirrors, wipers, plates,
cladding, liveries and beacons. Lamps follow the skin surface."""
import math
from .geom import (Part, grid, orient_to, tube, superellipsoid, cylinder, slab, quad, merge,
                   mirror_x, add, sub, mul, norm, cross, vlerp, clamp, lin, rbox)


def patch(sk, a0, a1, zlo, zhi, lift, nu=12, nv=4, side=1):
    """Strip lying on the skin between arc lengths a0..a1; zlo/zhi may be
    callables of u in [0,1] so lamp outlines can sweep and taper."""
    rows = []
    for j in range(nv + 1):
        v = j / nv
        row = []
        for i in range(nu + 1):
            u = i / nu
            lo = zlo(u) if callable(zlo) else zlo
            hi = zhi(u) if callable(zhi) else zhi
            z = lo + (hi - lo) * v
            a = a0 + (a1 - a0) * u
            q = sk.at(a)
            # never float over a wheel opening or above the belt
            z = min(max(z, sk.z_bottom(q[0], q[1]) + 0.004), sk.z_top(q[1]) - 0.004)
            row.append(sk.surf(a, z, lift, side))
        rows.append(row)
    return orient_to(grid(rows), sk.ref)


def both(sk, parts, name, material, a0, a1, zlo, zhi, lift, nu=12, nv=4, emissive_name=None):
    """Symmetric pair (right + mirrored left) as one object."""
    r = patch(sk, a0, a1, zlo, zhi, lift, nu, nv, 1)
    l = patch(sk, a0, a1, zlo, zhi, lift, nu, nv, -1)
    for vf, sfx in ((r, '_R'), (l, '_L')):
        pt = Part(name + sfx, material=material)
        pt.add(vf)
        parts.append(pt)


def centre(sk, parts, name, material, front, x0, x1, zlo, zhi, lift, nu=12, nv=3):
    """Patch across the car centre line on the front or rear face."""
    af = sk.a_front if front else sk.a_rear
    a_c = sk.A if front else 0.0
    a_r = af(x1)
    r = patch(sk, a_r, a_c, zlo, zhi, lift, nu, nv, 1)
    l = patch(sk, a_r, a_c, zlo, zhi, lift, nu, nv, -1)
    pt = Part(name, material=material)
    pt.add(merge(r, l))
    parts.append(pt)


def build_lamps(S, sk, parts, detail='hi'):
    D = S['lamps']
    hi = detail == 'hi'
    nu = 14 if hi else 5
    # ---- front: headlamp housing (dark), lens, DRL strip, indicator
    h = D['head']
    a0, a1 = sk.a_front(h['x_in']), sk.a_side(h['y_out'])
    both(sk, parts, 'Headlight_Housing', 'Trim_Black', a0, a1, h['z_lo'], h['z_hi'], 0.003, nu, 4)
    lens_lo = (lambda u, f=h['z_lo']: (f(u) if callable(f) else f) + 0.008)
    lens_hi = (lambda u, f=h['z_hi']: (f(u) if callable(f) else f) - 0.008)
    span = a1 - a0
    both(sk, parts, 'Headlight', 'Lamp_White', a0 + 0.01, a0 + span * h.get('lens_frac', 0.78),
         lens_lo, lens_hi, 0.0055, nu, 3)
    both(sk, parts, 'TurnSignal_Front', 'Lamp_Amber', a0 + span * (h.get('lens_frac', 0.78) + 0.02), a1 - 0.01,
         lens_lo, lens_hi, 0.0055, 4, 2)
    if h.get('drl'):
        zd = h['drl']
        both(sk, parts, 'DRL', 'Lamp_White', a0 + 0.02, a0 + span * 0.7, zd, zd + 0.008, 0.0068, nu, 1)
    # ---- grille and intakes on the front face
    for g in D.get('grilles', []):
        centre(sk, parts, g['name'], g.get('mat', 'Trim_Black'), True, 0, g['x'], g['z_lo'], g['z_hi'], g.get('lift', 0.002), 10 if hi else 4, 3)
        if g.get('bars') and hi:
            bars = []
            for k in range(g['bars']):
                z = (g['z_lo'] if not callable(g['z_lo']) else g['z_lo'](0)) + 0.012 + k * g.get('bar_pitch', 0.022)
                line = [sk.surf(sk.a_front(abs(x)), z, 0.006, 1 if x >= 0 else -1) for x in [g['x'] * (-1 + 2 * i / 16) * 0.97 for i in range(17)]]
                bars.append(tube(line, g.get('bar_r', 0.004), 5, caps=True))
            pt = Part(g['name'] + '_Bars', material=g.get('bar_mat', 'Chrome'))
            pt.add(merge(*bars))
            parts.append(pt)
    for fl in D.get('fog', []):
        a = sk.a_front(fl['x'])
        both(sk, parts, 'FogLamp', 'Lamp_White', a - fl['w'] / 2, a + fl['w'] / 2, fl['z'] - fl['h'] / 2, fl['z'] + fl['h'] / 2, 0.004, 6, 2)
    # ---- rear: tail lamps, brake, reverse, reflectors
    t = D['tail']
    b0, b1 = sk.a_rear(t['x_in']), sk.a_side(t['y_out'])
    # arc length on the right half increases from rear centre -> flank
    both(sk, parts, 'Taillight_Housing', 'Trim_Black', b0, b1, t['z_lo'], t['z_hi'], 0.003, nu, 4)
    tl = (lambda u, f=t['z_lo']: (f(u) if callable(f) else f) + 0.007)
    th = (lambda u, f=t['z_hi']: (f(u) if callable(f) else f) - 0.007)
    sp = b1 - b0
    both(sk, parts, 'Taillight', 'Lamp_Red', b0 + sp * 0.28, b1 - 0.008, tl, th, 0.0055, nu, 3)
    both(sk, parts, 'ReverseLight', 'Lamp_White', b0 + 0.008, b0 + sp * 0.14, tl, th, 0.0055, 3, 2)
    both(sk, parts, 'TurnSignal_Rear', 'Lamp_Amber', b0 + sp * 0.15, b0 + sp * 0.27, tl, th, 0.0055, 3, 2)
    if t.get('bar'):
        zb = t['bar']
        centre(sk, parts, 'TailLightBar', 'Lamp_Red', False, 0, t['x_in'], zb[0], zb[1], 0.004, 8 if hi else 3, 1)
    for g in D.get('rear_trims', []):
        centre(sk, parts, g['name'], g.get('mat', 'Plastic_Black'), False, 0, g['x'], g['z_lo'], g['z_hi'], g.get('lift', 0.002), 10 if hi else 4, 2)
    for r in D.get('reflectors', []):
        a = sk.a_rear(r['x'])
        both(sk, parts, 'Reflector', 'Lamp_Red', a - r['w'] / 2, a + r['w'] / 2, r['z'] - 0.012, r['z'] + 0.012, 0.004, 3, 1)


def build_plates(S, sk, parts, plate_mat='Paint_White', text_mat='Ink_Dark', text='А 001 АА 77'):
    """Flat GOST-size plates (520 x 112 mm); text object faces outwards."""
    for key, front in (('front', True), ('rear', False)):
        z = S['plates'][key]
        a = sk.A if front else 0.0
        x, y, zz = sk.surf(a, z, 0.0)
        y += 0.012 if front else -0.012
        n = (0, 1, 0) if front else (0, -1, 0)
        right = (1, 0, 0) if front else (-1, 0, 0)
        pt = Part('NumberPlate_' + ('Front' if front else 'Rear'), material=plate_mat)
        pt.add(slab((0, y, z), right, (0, 0, 1), 0.52, 0.112, 0.008))
        parts.append(pt)
        tx = Part('NumberPlateText_' + ('Front' if front else 'Rear'), kind='text', material=text_mat,
                  loc=(0, y + (0.0045 if front else -0.0045), z),
                  rot=(math.pi / 2, 0, math.pi if front else 0))
        tx.text = (text, 0.075, 0.0006)
        parts.append(tx)


def build_handles(S, sk, parts, paint):
    z = S['handles']['z']
    for y in S['handles']['y']:
        a = sk.a_side(y)
        for side, sfx in ((1, '_R'), (-1, '_L')):
            rec = patch(sk, a - 0.012, a + 0.16, z - 0.02, z + 0.02, 0.0012, 4, 2, side)
            grip = patch(sk, a, a + 0.145, z - 0.011, z + 0.012, 0.012, 4, 2, side)
            p1 = Part('Handle_Recess_' + str(round(y, 2)) + sfx, material='Trim_Black')
            p1.add(rec)
            p2 = Part('Handle_' + str(round(y, 2)) + sfx, material=S['handles'].get('mat', paint))
            p2.add(grip)
            parts += [p1, p2]


def build_mirrors(S, gh, parts, paint):
    M = S['mirror']
    base = gh.point('S', M['f'], 0.02)
    for side, sfx in ((1, 'R'), (-1, 'L')):
        bx, by, bz = base
        bx *= side
        c = (side * (abs(bx) + M['reach']), by - 0.02, bz + M['dz'])
        arm = cylinder((bx - side * 0.01, by, bz + 0.015), (c[0] - side * 0.07, c[1], c[2] - 0.02), 0.022, 10)
        body = superellipsoid(c, (M['w'] / 2, 0.075, M['h'] / 2), exp=0.45, seg_u=18, seg_v=8)
        p = Part('MirrorHousing_' + sfx, material=M.get('mat', paint))
        p.add(merge(arm, body))
        parts.append(p)
        # reflective surface: object origin at its centre, facing rearwards (-Y)
        surf = Part('MirrorSurface_' + sfx, material='Mirror', loc=(c[0], c[1] - 0.078, c[2]))
        surf.add(slab((0, 0, 0), (-1, 0, 0), (0, 0, 1), M['w'] - 0.035, M['h'] - 0.03, 0.004))
        surf.smooth = False
        parts.append(surf)
        if M.get('repeater'):
            rp = Part('TurnSignal_Mirror_' + sfx, material='Lamp_Amber')
            rp.add(slab((c[0] + side * 0.02, c[1] + 0.06, c[2] - M['h'] / 2 + 0.012), (1, 0, 0), (0, 1, 0), 0.07, 0.02, 0.006))
            parts.append(rp)


def build_wipers(S, gh, parts):
    for name, f in (('Wiper_Pivot_L', 0.2), ('Wiper_Pivot_R', 0.62)):
        p = gh.point('F', f, 0.02)
        pv = Part(name, kind='empty', loc=(p[0], p[1] + 0.02, p[2] + 0.01))
        parts.append(pv)
        tip = gh.point('F', min(0.97, f + 0.34), 0.06)
        d = sub(tip, p)
        arm = tube([(0, 0, 0), (d[0] * 0.95, d[1] * 0.95 - 0.02, d[2] * 0.95)], 0.006, 5)
        blade = tube([(d[0] * 0.25, d[1] * 0.25 - 0.02, d[2] * 0.25 + 0.012), (d[0] * 0.98, d[1] * 0.98 - 0.02, d[2] * 0.98 + 0.012)], 0.007, 5)
        m = Part(name + '_Mesh', material='Trim_Black', parent=name)
        m.add(merge(arm, blade))
        parts.append(m)


def build_cladding(S, sk, parts):
    """Unpainted plastic arch mouldings and sill (crossover)."""
    C = S.get('cladding')
    if not C:
        return
    w = C['width']
    for wy, label in ((S['wheel_y'][0], 'F'), (S['wheel_y'][1], 'R')):
        for side, sfx in ((1, '_R'), (-1, '_L')):
            rows = []
            for k in range(3):
                d = w * k / 2
                row = []
                for i in range(33):
                    ang = math.pi * i / 32
                    r = S['arch_r'] + 0.004 + d
                    y = wy + r * math.cos(ang)
                    z = S['wheel_z'] + r * math.sin(ang)
                    a = sk.a_side(y)
                    p = sk.at(a)
                    x, yy, zz = sk.point(p, max(z, sk.base_bottom(y)), C['lift'] * (1 - 0.4 * k / 2))
                    row.append((side * x, yy, zz))
                rows.append(row)
            vf = orient_to(grid(rows), sk.ref)
            pt = Part('ArchCladding_' + label + sfx, material='Plastic_Black')
            pt.add(vf)
            parts.append(pt)
    y0 = S['wheel_y'][1] + S['arch_r'] + 0.02
    y1 = S['wheel_y'][0] - S['arch_r'] - 0.02
    for side, sfx in ((1, '_R'), (-1, '_L')):
        vf = patch(sk, sk.a_side(y0), sk.a_side(y1), S['sill_z'] + 0.002, S['sill_z'] + C['sill_h'], C['lift'], 16, 2, side)
        pt = Part('SillCladding' + sfx, material='Plastic_Black')
        pt.add(vf)
        parts.append(pt)


def build_mouldings(S, sk, parts):
    Mo = S.get('mouldings')
    if not Mo:
        return
    for y0, y1 in Mo['spans']:
        both(sk, parts, 'SideMoulding_' + str(round(y0, 2)), Mo.get('mat', 'Trim_Black'), sk.a_side(y0), sk.a_side(y1),
             Mo['z'] - Mo['h'] / 2, Mo['z'] + Mo['h'] / 2, 0.006, 10, 2)


def tilt_about_y(rot, a):
    """Euler XYZ of (rotation about world Y by a) applied after rot."""
    from .geom import rot_euler
    cols = [rot_euler(e, rot) for e in ((1, 0, 0), (0, 1, 0), (0, 0, 1))]
    ca, sa = math.cos(a), math.sin(a)
    cols = [(c[0] * ca + c[2] * sa, c[1], -c[0] * sa + c[2] * ca) for c in cols]
    R = [[cols[j][i] for j in range(3)] for i in range(3)]      # R[row][col]
    y = math.asin(max(-1.0, min(1.0, -R[2][0])))
    x = math.atan2(R[2][1], R[2][2])
    z = math.atan2(R[1][0], R[0][0])
    return (x, y, z)


def build_livery(S, sk, gh, parts):
    L = S.get('livery')
    if not L:
        return
    for st in L.get('stripes', []):
        both(sk, parts, st['name'], st['mat'], sk.a_side(st['y0']), sk.a_side(st['y1']),
             st['z_lo'], st['z_hi'], 0.0016, 24, 2)
    for st in L.get('gh_stripes', []):
        # stripes on the (van) box above the belt line
        st.setdefault('f0', gh.frac_at_y(st['y0']))
        st.setdefault('f1', gh.frac_at_y(st['y1']))
        for side, sfx in ((1, '_R'), (-1, '_L')):
            rows = []
            for t in (st['t0'], st['t1']):
                row = []
                for i in range(25):
                    f = st['f0'] + (st['f1'] - st['f0']) * i / 24
                    x, y, z = gh.point('S', f, t)
                    row.append((side * (x + 0.004), y, z))
                rows.append(row)
            vf = orient_to(grid(rows), gh.ref)
            pt = Part(st['name'] + sfx, material=st['mat'])
            pt.add(vf)
            parts.append(pt)
    for tx in L.get('texts', []):
        for side, sfx in ((1, '_R'), (-1, '_L')):
            if tx.get('gh'):
                x, y, z = gh.point('S', gh.frac_at_y(tx['y']), tx['t'])
            else:
                x, y, z = sk.surf(sk.a_side(tx['y']), tx['z'], 0.0)
            # text lies in its XY plane: rotate to stand on the flank, reading front->rear on
            # the right side and rear->front on the left side (as painted on real cars)
            rot = (math.pi / 2, 0, math.pi / 2) if side > 0 else (math.pi / 2, 0, -math.pi / 2)
            if tx.get('gh'):
                # lean the lettering with the (tumblehome) side of the box
                xa = gh.side_x(y, z - 0.1)
                xb = gh.side_x(y, z + 0.1)
                if xa and xb:
                    lean = math.atan2(xa - xb, 0.2)
                    rot = tilt_about_y(rot, -side * lean)
            t = Part(tx['name'] + sfx, kind='text', material=tx['mat'], loc=(side * (x + 0.006), y, z), rot=rot)
            t.text = (tx['body'], tx['size'], 0.0008)
            parts.append(t)
    for tx in L.get('texts_front', []):
        x, y, z = sk.surf(sk.A, tx['z'], 0.0) if not tx.get('hood') else (0, tx['y'], tx['z'])
        t = Part(tx['name'], kind='text', material=tx['mat'], loc=(0, y + 0.006, z), rot=tx.get('rot', (math.pi / 2, 0, math.pi)))
        t.text = (tx['body'], tx['size'], 0.0008)
        parts.append(t)


def build_beacons(S, gh, parts):
    B = S.get('beacons')
    if not B:
        return
    for bar in B:
        f, t = bar['at']
        y = bar['y']
        z = gh.roof_z(y) + gh.G['roof'].get('crown', 0.03) + bar['h'] / 2 + 0.02
        w = bar['w']
        body = rbox((0, y, z), (w, bar['d'], bar['h']), 0.02)
        feet = merge(*[rbox((sx * w * 0.38, y, z - bar['h'] / 2 - 0.012), (0.05, 0.12, 0.03), 0.01) for sx in (-1, 1)])
        p = Part(bar['name'], material='Trim_Black')
        p.add(merge(body, feet))
        parts.append(p)
        n = bar.get('segments', 4)
        for i in range(n):
            x0 = -w / 2 + 0.03 + (w - 0.06) * i / n
            x1 = -w / 2 + 0.03 + (w - 0.06) * (i + 1) / n - 0.012
            mat = bar['colors'][i % len(bar['colors'])]
            for face, sgn in (('Front', 1), ('Rear', -1)):
                lens = slab(((x0 + x1) / 2, y + sgn * (bar['d'] / 2 + 0.002), z + 0.004), (sgn, 0, 0), (0, 0, 1),
                            x1 - x0, bar['h'] * 0.62, 0.006)
                lp = Part(bar['name'] + '_Lens_' + face + str(i), material=mat)
                lp.add(lens)
                parts.append(lp)


def build_roof_rails(S, gh, parts):
    RR = S.get('roof_rails')
    if not RR:
        return
    R = gh.G['roof']
    for side, sfx in ((1, '_R'), (-1, '_L')):
        pts = []
        for i in range(17):
            y = R['y_front'] - 0.12 - (R['y_front'] - R['y_rear'] - 0.24) * i / 16
            x = side * (R['half_w'] - 0.09)
            z = gh.roof_z(y) + R.get('crown', 0.03) * (1 - ((R['half_w'] - 0.09) / R['half_w']) ** 2) + RR['h']
            pts.append((x, y, z))
        rail = tube(pts, 0.014, 8)
        posts = merge(*[rbox((p[0], p[1], p[2] - RR['h'] / 2), (0.03, 0.08, RR['h']), 0.01) for p in (pts[0], pts[-1])])
        pt = Part('RoofRail' + sfx, material=RR.get('mat', 'Satin_Aluminium'))
        pt.add(merge(rail, posts))
        parts.append(pt)
