"""Shared primitives, wheels and the author-frame -> Blender-frame turn."""
import math
from vehicle_kit.geom import Part, lathe, cylinder, make_outward, merge, grid, orient_to


def box(center, size):
    """Axis-aligned box, outward normals."""
    cx, cy, cz = center
    hx, hy, hz = size[0] / 2, size[1] / 2, size[2] / 2
    v = [(cx + sx * hx, cy + sy * hy, cz + sz * hz) for sz in (-1, 1) for sy in (-1, 1) for sx in (-1, 1)]
    f = [(0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)]
    return make_outward((v, f))


def box_between(z0, z1, x0, x1, y0, y1):
    return box(((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), (abs(x1 - x0), abs(y1 - y0), abs(z1 - z0)))


def prism(poly_xy, z0, z1):
    """Vertical prism from a simple polygon (any winding, may be concave:
    the Blender side triangulates n-gons with bmesh polyfill)."""
    n = len(poly_xy)
    v = [(x, y, z0) for x, y in poly_xy] + [(x, y, z1) for x, y in poly_xy]
    f = [tuple(reversed(range(n))), tuple(range(n, 2 * n))]
    f += [(i, (i + 1) % n, n + (i + 1) % n, n + i) for i in range(n)]
    area = sum(poly_xy[i][0] * poly_xy[(i + 1) % n][1] - poly_xy[(i + 1) % n][0] * poly_xy[i][1] for i in range(n))
    if area < 0:
        f = [tuple(reversed(q)) for q in f]
    return v, f


def panel_x(x, y0, y1, z0, z1, t, outward):
    """Thin plate on a side face (normal along +-X), thickness t outwards."""
    xa, xb = (x, x + outward * t)
    return box_between(z0, z1, min(xa, xb), max(xa, xb), y0, y1)


def panel_y(y, x0, x1, z0, z1, t, outward):
    """Thin plate on an end face (normal along +-Y)."""
    ya, yb = (y, y + outward * t)
    return box_between(z0, z1, x0, x1, min(ya, yb), max(ya, yb))


def mesh(name, material, *vfs, parent=None, loc=(0, 0, 0), smooth=False):
    p = Part(name, material=material, parent=parent, loc=loc)
    for vf in vfs:
        p.add(vf)
    p.smooth = smooth
    return p


def empty(name, loc, parent=None):
    return Part(name, kind='empty', parent=parent, loc=loc)


def text(name, body, size, loc, facing, material, parent=None, extrude=0.002):
    """Text lying on a face. facing: '+Y', '-Y', '+X', '-X' in the author frame."""
    rot = {'+Y': (math.pi / 2, 0, math.pi), '-Y': (math.pi / 2, 0, 0),
           '+X': (math.pi / 2, 0, math.pi / 2), '-X': (math.pi / 2, 0, -math.pi / 2)}[facing]
    p = Part(name, kind='text', material=material, parent=parent, loc=loc, rot=rot)
    p.text = (body, size, extrude)
    return p


# ------------------------------------------------------------------ wheels
def tyre(R, w, r_rim, seg=28):
    h = w / 2
    prof = [(r_rim + 0.01, -h + 0.012), (r_rim + 0.05, -h), (R - 0.05, -h - 0.004), (R - 0.015, -h + 0.012),
            (R, -h + 0.035), (R, h - 0.035), (R - 0.015, h - 0.012), (R - 0.05, h + 0.004), (r_rim + 0.05, h),
            (r_rim + 0.01, h - 0.012)]
    return prof


def road_wheel(parts, name, pos, R, w, r_rim, outward, dual=0.0, rim_mat='PT_Steel_Wheel', seg=28):
    """Wheel_XX empty at the hub centre (contract), tyre + disc as children.
    dual > 0 adds an inner tyre (twin rear wheels), `dual` metres inboard;
    the empty then sits midway between the twins."""
    root = empty(name, pos)
    parts.append(root)
    offsets = [0.0] if dual <= 0 else [dual / 2, -dual / 2]
    for k, off in enumerate(offsets):
        o = (outward * off, 0.0, 0.0)
        ax = (outward, 0.0, 0.0)
        t = mesh(f'{name}_Tyre' + ('' if k == 0 else '_Inner'), 'PT_Rubber', lathe(tyre(R, w, r_rim), o, ax, seg),
                 parent=name, smooth=True)
        parts.append(t)
        face = w / 2 - 0.02
        disc = lathe([(0.0, face - 0.03), (0.09, face - 0.03), (r_rim * 0.62, face - 0.05), (r_rim * 0.8, face - 0.02),
                      (r_rim, face), (r_rim + 0.012, face + 0.006), (r_rim + 0.012, -w / 2 + 0.02), (r_rim, -w / 2 + 0.01)],
                     o, ax, seg)
        parts.append(mesh(f'{name}_Rim' + ('' if k == 0 else '_Inner'), rim_mat, disc, parent=name, smooth=True))
    hub_x = outward * (offsets[0] + w / 2 - 0.05)
    hub = merge(cylinder((hub_x - outward * 0.02, 0, 0), (hub_x + outward * 0.03, 0, 0), 0.11, 16),
                cylinder((hub_x, 0, 0), (hub_x + outward * 0.06, 0, 0), 0.06, 12))
    parts.append(mesh(f'{name}_Hub', 'PT_Steel_Dark', hub, parent=name))
    return root


def flanged_wheel(R, w, flange_h, inner_sign, centre, seg=24):
    """Rail wheel: tread radius R, flange of height flange_h on the inner side
    (towards the track centre, inner_sign = -1 for the right wheel)."""
    h = w / 2
    # axial coordinate a runs outward; flange at a = -h
    prof = [(0.12, -h - 0.01), (R + flange_h - 0.01, -h - 0.01), (R + flange_h, -h + 0.005), (R + 0.004, -h + 0.03),
            (R, -h + 0.045), (R - 0.004, h), (R - 0.06, h), (0.14, h - 0.02)]
    ax = (-inner_sign, 0.0, 0.0)
    return lathe(prof, centre, ax, seg, cap_start=True, cap_end=True)


# ------------------------------------------------------------------ frames
def to_blender(parts, root_name):
    """Turn the finished asset 180 deg around Z (author +Y forward -> Blender
    front -Y). Rotation, not mirror: winding and handedness are preserved.
    Parts with rot == 0 get their vertices turned (transforms stay applied)
    and their children are turned the same way; parts with a rotation (text,
    pivots) get their frame turned instead."""
    by_parent = {}
    for p in parts:
        by_parent.setdefault(p.parent, []).append(p)

    def turn(p):
        x, y, z = p.loc
        p.loc = (-x, -y, z)
        if any(abs(a) > 1e-9 for a in p.rot):
            # Blender XYZ Euler is Rz*Ry*Rx: a pre-turn around Z adds to rz;
            # children stay in this part's (turned) frame.
            p.rot = (p.rot[0], p.rot[1], p.rot[2] + math.pi)
            return
        p.v = [(-a, -b, c) for a, b, c in p.v]
        for ch in by_parent.get(p.name, []):
            turn(ch)

    for p in by_parent.get(root_name, []):
        turn(p)
    return parts
