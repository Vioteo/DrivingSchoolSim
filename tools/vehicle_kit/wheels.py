"""Wheel assemblies. Wheel_XX is an empty at the hub centre (contract name);
everything that spins or steers is its child, in local coordinates."""
import math
from .geom import Part, lathe, merge, rbox, cylinder, transform, prism_x, make_outward


def tyre_profile(R, w, r_rim, hi):
    """(radius, axial) around the tyre section, inner bead -> outer bead."""
    h = w / 2
    pts = [(r_rim + 0.012, -h + 0.018), (r_rim + 0.05, -h + 0.004), (R - 0.045, -h - 0.004), (R - 0.018, -h + 0.006),
           (R - 0.004, -h + 0.02)]
    if hi:
        for g in (-0.55, -0.18, 0.18, 0.55):         # circumferential grooves
            c = g * h
            pts += [(R, c - 0.012), (R - 0.009, c - 0.006), (R - 0.009, c + 0.006), (R, c + 0.012)]
    pts += [(R - 0.004, h - 0.02), (R - 0.018, h - 0.006), (R - 0.045, h + 0.004), (r_rim + 0.05, h - 0.004),
            (r_rim + 0.012, h - 0.018)]
    return pts


def build_wheel(S, parts, name, pos, outward, detail='hi'):
    hi = detail == 'hi'
    W = S['wheel']
    R, w, rr = S['tire_r'], S['tire_w'], W['rim_r']
    seg = 48 if hi else 20
    o = (0, 0, 0)
    ax = (outward, 0, 0)            # axial direction pointing away from the car
    root = Part(name, kind='empty', loc=pos)
    parts.append(root)
    tyre = Part(name + '_Tyre', material='Rubber', parent=name)
    tyre.add(lathe(tyre_profile(R, w, rr, hi), o, ax, seg))
    parts.append(tyre)
    # rim barrel, dish and centre
    face = w / 2 - 0.028
    barrel = lathe([(rr + 0.006, -w / 2 + 0.01), (rr + 0.012, -w / 2 + 0.018), (rr, -w / 2 + 0.03), (rr - 0.004, face - 0.02),
                    (rr + 0.01, face - 0.004), (rr + 0.014, face + 0.006), (rr + 0.004, face + 0.012)], o, ax, seg)
    rim = Part(name + '_Rim', material=W.get('mat', 'Satin_Aluminium'), parent=name)
    rim.add(barrel)
    style = W['style']
    if style == 'alloy':
        n = W.get('spokes', 5)
        hub = lathe([(0.0, face + 0.008), (0.05, face + 0.008), (0.075, face - 0.004), (0.08, face - 0.03)], o, ax, 24)
        rim.add(hub)
        for i in range(n):
            a = 2 * math.pi * i / n
            for da in (-W.get('split', 0.0), W.get('split', 0.0)) if W.get('split') else (0.0,):
                sp = spoke(rr, face, W.get('spoke_w', 0.045), outward)
                rim.add(transform(sp, rot=(a + da, 0, 0)))
    else:  # steel wheel with plastic trim
        disc = lathe([(0.0, face - 0.004), (rr * 0.55, face - 0.006), (rr * 0.8, face + 0.004), (rr * 0.97, face + 0.002),
                      (rr + 0.01, face - 0.01)], o, ax, seg)
        rim.add(disc)
        cap = Part(name + '_Cap', material=W.get('cap_mat', 'Plastic_Grey'), parent=name)
        cap.add(lathe([(0.0, face + 0.02), (0.07, face + 0.018), (rr * 0.72, face + 0.008), (rr * 0.95, face + 0.012),
                       (rr + 0.02, face + 0.0)], o, ax, seg))
        parts.append(cap)
    parts.append(rim)
    nut = Part(name + '_Hub', material='Chrome', parent=name)
    for i in range(W.get('bolts', 4)):
        a = 2 * math.pi * i / W.get('bolts', 4)
        c = (outward * (face + 0.004), math.cos(a) * 0.05, math.sin(a) * 0.05)
        nut.add(cylinder(c, (c[0] + outward * 0.012, c[1], c[2]), 0.009, 6))
    parts.append(nut)
    # brake disc + caliper (caliper does not spin in reality; kept under the
    # wheel pivot here so it steers with the wheel)
    disc = Part(name + '_BrakeDisc', material='Satin_Aluminium', parent=name)
    disc.add(lathe([(0.06, -0.012), (rr - 0.04, -0.012), (rr - 0.04, 0.012), (0.06, 0.012)], (outward * (face - 0.07), 0, 0), ax, 32, False, False))
    parts.append(disc)
    cal = Part(name + '_Caliper', material=W.get('caliper', 'Interior_Graphite'), parent=name)
    cal.add(rbox((outward * (face - 0.07), -0.1 if pos[1] > 0 else 0.1, 0.06), (0.06, 0.07, 0.13), 0.015))
    parts.append(cal)


def spoke(rr, face, width, outward):
    """A tapered spoke in the Y=0 / +Z direction (rotated around X later)."""
    t = 0.022
    prof = [(-width / 2, 0.07), (width / 2, 0.07), (width / 3, rr - 0.01), (-width / 3, rr - 0.01)]
    x0, x1 = outward * (face - t), outward * (face + 0.004)
    v = []
    for x in (x0, x1):
        for y, z in prof:
            v.append((x, y, z))
    f = [(0, 1, 2, 3), (7, 6, 5, 4), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)]
    return make_outward((v, f))


def build_wheels(S, parts, detail='hi'):
    for label, y in (('F', S['wheel_y'][0]), ('R', S['wheel_y'][1])):
        for side, s in (('L', -1), ('R', 1)):
            build_wheel(S, parts, 'Wheel_' + label + side, (s * S['wheel_x'], y, S['wheel_z']), s, detail)
