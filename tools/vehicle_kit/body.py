"""Lower body: skin panels, lids, bumpers, closed wheelhouses, undertray."""
import math
from .geom import (Part, lin, smooth, clamp, lerp, grid, orient_to, fan, tube, mirror_x, merge)


class Skin:
    """Right-hand half of the lower body, described by a plan outline (arc
    length a from the rear centre to the front centre) and a section law
    offset(z). Left side is the mirror image."""

    def __init__(self, S, detail='hi'):
        self.S = S
        self.hi = detail == 'hi'
        self.step = 0.028 if self.hi else 0.07
        self._outline()
        self.zc = 0.5 * (S['sill_z'] + max(z for _, z in S['top_keys']))

    # ---------------------------------------------------------- outline
    def _outline(self):
        S = self.S
        W = S['half_w']
        Wf = W - S.get('taper_front', 0.0)
        Wr = W - S.get('taper_rear', 0.0)
        rcf, rcr = S['rc_front'], S['rc_rear']
        bf, br = S.get('bow_front', 0.04), S.get('bow_rear', 0.03)
        yn, yt = S['nose_y'], S['tail_y']
        poly = []
        # rear face, centre -> corner
        xr = Wr - rcr
        for i in range(40):
            x = xr * i / 40
            poly.append((x, yt + br * (x / xr) ** 2))
        cy = yt + br + rcr
        for i in range(31):
            ang = -math.pi / 2 + (math.pi / 2) * i / 30
            poly.append((xr + rcr * math.cos(ang), cy + rcr * math.sin(ang)))
        # flank (optional taper towards both ends)
        y0, y1 = cy, yn - bf - rcf
        ts, te = S.get('taper_start', (y0 + y1) / 2), S.get('taper_rear_start', (y0 + y1) / 2)
        for i in range(1, 120):
            y = y0 + (y1 - y0) * i / 120
            x = W - (W - Wf) * smooth((y - ts) / max(1e-3, y1 - ts)) if y > ts else \
                W - (W - Wr) * smooth((te - y) / max(1e-3, te - y0)) if y < te else W
            poly.append((x, y))
        xf = Wf - rcf
        cyf = yn - bf - rcf
        for i in range(31):
            ang = (math.pi / 2) * i / 30
            poly.append((xf + rcf * math.cos(ang), cyf + rcf * math.sin(ang)))
        for i in range(1, 41):
            x = xf * (1 - i / 40)
            poly.append((x, yn - bf * (x / xf) ** 2))
        # resample by arc length, denser across the wheel openings
        d = [0.0]
        for (x0, y0_), (x1, y1_) in zip(poly, poly[1:]):
            d.append(d[-1] + math.hypot(x1 - x0, y1_ - y0_))
        total = d[-1]
        pts, a_list = [], []
        a, j = 0.0, 0
        while True:
            while j < len(d) - 2 and d[j + 1] < a:
                j += 1
            f = (a - d[j]) / ((d[j + 1] - d[j]) or 1)
            x = poly[j][0] + (poly[j + 1][0] - poly[j][0]) * f
            y = poly[j][1] + (poly[j + 1][1] - poly[j][1]) * f
            pts.append((x, y))
            a_list.append(a)
            if a >= total:
                break
            near_arch = x > W - 0.05 and any(abs(y - wy) < S['arch_r'] + 0.1 for wy in S['wheel_y'])
            a = min(total, a + (self.step * (0.45 if near_arch else 1.0)))
        out = []
        for i, (x, y) in enumerate(pts):
            p = pts[max(i - 1, 0)]
            q = pts[min(i + 1, len(pts) - 1)]
            tx, ty = q[0] - p[0], q[1] - p[1]
            l = math.hypot(tx, ty) or 1
            nx, ny = ty / l, -tx / l
            if i == 0:
                nx, ny = 0.0, -1.0
            if i == len(pts) - 1:
                nx, ny = 0.0, 1.0
            out.append((x, y, nx, ny))
        self.pts = out
        self.a = a_list
        self.A = total

    # ---------------------------------------------------------- laws
    def z_top(self, y):
        return lin(self.S['top_keys'], y)

    def arch_z(self, y):
        """Height of the skin's lower edge produced by the wheel openings."""
        S = self.S
        R = S['arch_r']
        best = None
        for wy in S['wheel_y']:
            d = abs(y - wy)
            if d < R:
                z = S['wheel_z'] + math.sqrt(R * R - d * d)
            elif d < R + S.get('arch_corner', 0.05):
                k = 1 - (d - R) / S.get('arch_corner', 0.05)
                z = self.base_bottom(y) + (S['wheel_z'] - self.base_bottom(y)) * (1 - math.sqrt(max(0.0, 1 - k * k)))
            else:
                continue
            best = z if best is None else max(best, z)
        return best

    def base_bottom(self, y):
        S = self.S
        if y > 0:
            return lerp(S['sill_z'], S['nose_bottom'], smooth((y - S['wheel_y'][0] - S['arch_r'] - 0.1) / 0.3))
        return lerp(S['sill_z'], S['tail_bottom'], smooth((S['wheel_y'][1] - S['arch_r'] - 0.1 - y) / 0.3))

    def z_bottom(self, x, y):
        b = self.base_bottom(y)
        if x > self.S['half_w'] - 0.12:
            az = self.arch_z(y)
            if az is not None:
                b = max(b, az)
        return b

    def end_weight(self, ny):
        return abs(ny) ** 1.6

    def offset(self, z, x, y, ny):
        S = self.S
        o = lin(S['side_keys'], z)
        e = self.end_weight(ny)
        if e > 0:
            keys = S['front_keys'] if y > 0 else S['rear_keys']
            o = lerp(o, lin(keys, z), e)
        # a bumper band standing proud (90s/2000s style), wraps round corners
        band = S.get('bumper_band')
        if band:
            lo, hi, out, start = band['front'] if y > 0 else band['rear']
            q = smooth(((y if y > 0 else -y) - start) / 0.25)
            if q > 0:
                o += q * out * smooth((z - lo) / 0.02) * smooth((hi - z) / 0.015)
        # wheel-arch treatment
        if x > S['half_w'] - 0.12 and S.get('arch_lip', 0) > 0:
            for wy in S['wheel_y']:
                r = math.hypot(y - wy, z - S['wheel_z'])
                if z > S['wheel_z'] - 0.04 and r < S['arch_r'] + 0.12:
                    o += S['arch_lip'] * math.exp(-((r - S['arch_r'] - 0.012) / S.get('arch_lip_w', 0.04)) ** 2)
        return o

    def point(self, p, z, lift=0.0):
        x, y, nx, ny = p
        o = self.offset(z, x, y, ny) + lift
        return (x + nx * o, y + ny * o, z)

    def at(self, a):
        """Outline point at arc length a (interpolated)."""
        a = clamp(a, 0, self.A)
        lo, hi = 0, len(self.a) - 1
        while hi - lo > 1:
            m = (lo + hi) // 2
            if self.a[m] <= a:
                lo = m
            else:
                hi = m
        f = (a - self.a[lo]) / ((self.a[hi] - self.a[lo]) or 1)
        p, q = self.pts[lo], self.pts[hi]
        return tuple(p[k] + (q[k] - p[k]) * f for k in range(4))

    def a_side(self, y):
        """Arc length of the flank point at longitudinal position y."""
        best = min(range(len(self.pts)), key=lambda i: abs(self.pts[i][1] - y) + (0 if self.pts[i][0] > self.S['half_w'] - 0.2 else 9))
        return self.a[best]

    def a_front(self, x):
        """Arc length of the front-face point at lateral position x."""
        best = min(range(len(self.pts)), key=lambda i: abs(self.pts[i][0] - x) + (0 if self.pts[i][1] > 0.5 else 9))
        return self.a[best]

    def a_rear(self, x):
        best = min(range(len(self.pts)), key=lambda i: abs(self.pts[i][0] - x) + (0 if self.pts[i][1] < -0.5 else 9))
        return self.a[best]

    def surf(self, a, z, lift=0.0, side=1):
        p = self.at(a)
        x, y, zz = self.point(p, z, lift)
        return (side * x, y, zz)

    def rows_z(self):
        """Row heights: uniform fill plus every key height so creases stay sharp."""
        S = self.S
        lo = min(S['sill_z'], S['nose_bottom'], S['tail_bottom'])
        hi = max(z for _, z in S['top_keys'])
        n = 20 if self.hi else 8
        zs = {round(lo + (hi - lo) * i / n, 4) for i in range(n + 1)}
        if self.hi:
            for keys in (S['side_keys'], S['front_keys'], S['rear_keys']):
                zs.update(round(z, 4) for z, _ in keys if lo < z < hi)
        return sorted(zs)

    def column(self, p):
        x, y = p[0], p[1]
        zb, zt = self.z_bottom(x, y), self.z_top(y)
        col = [zb]
        for z in self.rows_z():
            if zb + 0.004 < z < zt - 0.004:
                col.append(z)
        col.append(zt)
        return col

    def ref(self, c):
        """Outward reference direction for orienting open skin surfaces."""
        S = self.S
        wy = S['wheel_y']
        return (c[0], c[1] - clamp(c[1], wy[1] + 0.2, wy[0] - 0.2), c[2] - self.zc)


def _panel_rows(sk, pts, flange=0.0):
    """Grid rows for a panel: every column is resampled to the same number of
    rows (columns over the arches are shorter)."""
    cols = [sk.column(p) for p in pts]
    n = max(len(c) for c in cols)
    rows_pts = []
    for p, c in zip(pts, cols):
        # resample column c to n entries, keeping its key heights
        if len(c) < n:
            extra = n - len(c)
            c = [c[0]] * extra + c   # degenerate rows collapse at the bottom edge
        colp = [sk.point(p, z) for z in c]
        if flange:
            x, y, z = colp[-1]
            colp.append((x - p[2] * flange, y - p[3] * flange, z - 0.004))
        rows_pts.append(colp)
    return [list(r) for r in zip(*rows_pts)]


def _index(sk, y, side_only=True):
    return min(range(len(sk.pts)), key=lambda i: abs(sk.pts[i][1] - y) + (0 if sk.pts[i][0] > sk.S['half_w'] - 0.2 else 9))


def build_body(S, parts, paint, detail='hi'):
    sk = Skin(S, detail)
    P = sk.pts
    seam_cols = []

    def add_panel(name, i0, i1, flange=0.0, side=1):
        rows = _panel_rows(sk, P[i0:i1 + 1], flange)
        vf = (lambda v: (v[0], v[1]))(grid(rows))
        if side < 0:
            vf = mirror_x(vf)
        vf = orient_to(vf, sk.ref)
        pt = Part(name, material=paint)
        pt.solidify = 0.012
        pt.add(vf)
        parts.append(pt)

    spl = S['splits']            # ordered front -> rear along the flank
    idx = {k: _index(sk, v) for k, v in spl.items()}
    iN = idx['nose']
    iT = idx['tail']
    panels = S['panels']         # list of (name, from_key, to_key, flange)
    for side, sfx in ((1, '_R'), (-1, '_L')):
        for name, k_rear, k_front, fl in panels:
            add_panel(name + sfx, idx[k_rear], idx[k_front], fl if S.get('window_flange', True) else 0.0, side)

    # bumper caps (both halves in one object each)
    for name, sl in (('Bumper_Front', slice(iN, None)), ('Bumper_Rear', slice(0, iT + 1))):
        seg = P[sl]
        rows_r = _panel_rows(sk, seg)
        rows_l = [[(-x, y, z) for x, y, z in r] for r in rows_r]
        if name == 'Bumper_Front':   # segment runs flank -> centre
            rows = [l + list(reversed(r))[1:] for l, r in zip(rows_l, rows_r)]
        else:                         # segment runs centre -> flank
            rows = [list(reversed(l)) + r[1:] for l, r in zip(rows_l, rows_r)]
        vf = orient_to(grid(rows), sk.ref)
        pt = Part(name, material=paint)
        pt.solidify = 0.012
        pt.add(vf)
        parts.append(pt)

    # lids (hood, boot) following the skin top edge
    def lid(name, pts, crown, creases=()):
        rows = []
        ncol = 25 if sk.hi else 9
        for p in pts:
            x, y, z = sk.point(p, sk.z_top(p[1]))
            row = []
            for j in range(ncol):
                u = -1 + 2 * j / (ncol - 1)
                k = min(1.0, x / (S['half_w'] - 0.05)) ** 1.3
                h = crown * k * (1 - u * u)
                for cu, ch, cw in creases:
                    h += ch * k * (math.exp(-((u - cu) / cw) ** 2) + math.exp(-((u + cu) / cw) ** 2))
                row.append((u * x, y, z + h))
            rows.append(row)
        vf = orient_to(grid(rows), lambda c: (0, 0, 1))
        pt = Part(name, material=paint)
        pt.solidify = 0.012
        pt.add(vf)
        parts.append(pt)
        return rows

    hood_pts = [p for p in P if p[1] >= S['cowl_y']]
    hood_rows = lid('Hood', hood_pts, S.get('hood_crown', 0.035), S.get('hood_creases', ()))
    deck_rows = None
    if S['deck_y'] > S['tail_y'] + 0.05:
        deck_pts = [p for p in P if p[1] <= S['deck_y']][::-1]
        deck_rows = lid('Trunk', deck_pts, S.get('deck_crown', 0.03))

    # panel gaps (fine dark tubes), one per shut line
    gaps = []
    r_gap = 0.0022
    for k in S.get('seams', []):
        i = idx[k]
        col = [sk.point(P[i], z) for z in sk.column(P[i])]
        gaps.append(tube(col, r_gap, 4, caps=False))
        gaps.append(tube([(-x, y, z) for x, y, z in col], r_gap, 4, caps=False))
    for rows in (hood_rows, deck_rows):
        if rows:
            edge_r = [r[-1] for r in rows]
            line = [(x, y, z + 0.002) for x, y, z in reversed([(-e[0], e[1], e[2]) for e in edge_r])] + \
                   [(x, y, z + 0.002) for x, y, z in edge_r[1:]]
            gaps.append(tube(line, 0.0025, 4, caps=False))
            first = rows[0]
            gaps.append(tube([(x, y, z + 0.002) for x, y, z in first], 0.0025, 4, caps=False))
    for zb in S.get('bumper_seams', []):
        # horizontal split between bumper and body across the ends
        for ends, key in ((range(iN, len(P)), 'front'), (range(0, iT + 1), 'rear')):
            z = zb[key]
            line = [sk.point(P[i], z, 0.001) for i in ends]
            full = [(-x, y, z_) for x, y, z_ in reversed(line)] + line[1:] if key == 'front' else \
                   [(x, y, z_) for x, y, z_ in reversed(line)] + [(-x, y, z_) for x, y, z_ in line[1:]]
            gaps.append(tube(full, r_gap, 4, caps=False))
    if gaps:
        g = Part('PanelGaps', material='Trim_Black')
        g.smooth = False
        g.add(merge(*gaps))
        parts.append(g)

    build_wheelhouses(sk, parts)
    build_undertray(sk, parts)
    return sk, hood_rows, deck_rows


def build_wheelhouses(sk, parts):
    """Closed tubs: skin opening edge -> liner -> inner wall. Nothing behind the
    wheel is open to the cabin or to the sky."""
    S = sk.S
    x_in = S['wheel_x'] - S['tire_w'] / 2 - 0.05
    for wy, label in ((S['wheel_y'][0], 'Front'), (S['wheel_y'][1], 'Rear')):
        R = S['arch_r'] + S.get('arch_corner', 0.05)
        edge = []
        for p in sk.pts:
            if p[0] > S['half_w'] - 0.12 and abs(p[1] - wy) <= R + 0.001:
                z = sk.z_bottom(p[0], p[1])
                edge.append(sk.point(p, z))
        if len(edge) < 3:
            continue
        # make the edge start and end at sill height
        zs = min(sk.base_bottom(edge[0][1]), sk.base_bottom(edge[-1][1]))
        e0 = (edge[0][0], edge[0][1], zs)
        e1 = (edge[-1][0], edge[-1][1], zs)
        edge = [e0] + edge + [e1]
        inner = [(x_in, y, z) for x, y, z in edge]
        liner = grid([edge, inner])
        wall = fan(inner, (x_in, wy, zs))
        for side, sfx in ((1, '_R'), (-1, '_L')):
            vf = merge(liner, wall)
            if side < 0:
                vf = mirror_x(vf)
            centre = (side * (S['wheel_x']), wy, S['wheel_z'])
            vf = orient_to(vf, lambda c, centre=centre: (centre[0] - c[0], centre[1] - c[1], centre[2] - c[2]))
            pt = Part('Wheelhouse_' + label + sfx, material='Plastic_Black')
            pt.add(vf)
            parts.append(pt)


def build_undertray(sk, parts):
    S = sk.S
    x_in = S['wheel_x'] - S['tire_w'] / 2 - 0.05
    z = min(S['sill_z'], S['nose_bottom'], S['tail_bottom']) + 0.02
    ys = [p[1] for p in sk.pts]
    xs = [p[0] for p in sk.pts]
    y0, y1 = ys[0] + 0.12, ys[-1] - 0.12
    n = 90 if sk.hi else 30
    rows = []
    for i in range(n + 1):
        y = y0 + (y1 - y0) * i / n
        k = next(j for j in range(1, len(ys)) if ys[j] >= y)
        f = (y - ys[k - 1]) / ((ys[k] - ys[k - 1]) or 1)
        w = xs[k - 1] + (xs[k] - xs[k - 1]) * f - 0.10
        for wy in S['wheel_y']:
            if abs(y - wy) < S['arch_r'] + S.get('arch_corner', 0.05) + 0.02:
                w = min(w, x_in)
        rows.append(((-w, y, z), (w, y, z)))
    v, f = grid([[a for a, _ in rows], [b for _, b in rows]])
    vf = orient_to((v, f), lambda c: (0, 0, -1))
    pt = Part('Undertray', material='Plastic_Black')
    pt.add(vf)
    parts.append(pt)
