"""Upper body: one continuous loft from the belt line to the roof, so pillars
flow out of the body instead of being separate plates. Windows are regions of
that loft (straight edges along loft columns/rows), glass is inset."""
import math
from .geom import Part, grid, orient_to, clamp, lerp, smooth, add, sub, mul, norm, vlerp


def _resample(pts, n):
    d = [0.0]
    for p, q in zip(pts, pts[1:]):
        d.append(d[-1] + math.sqrt(sum((q[k] - p[k]) ** 2 for k in range(3))))
    L = d[-1] or 1.0
    out, j = [], 0
    for i in range(n):
        t = L * i / (n - 1)
        while j < len(d) - 2 and d[j + 1] < t:
            j += 1
        f = (t - d[j]) / ((d[j + 1] - d[j]) or 1)
        out.append(vlerp(pts[j], pts[j + 1], f))
    return out


def _at_y(poly, y):
    """Point on a polyline that is monotonic (decreasing) in y."""
    for p, q in zip(poly, poly[1:]):
        if (p[1] - y) * (q[1] - y) <= 0 and p[1] != q[1]:
            f = (y - p[1]) / (q[1] - p[1])
            return vlerp(p, q, f)
    return poly[0] if abs(poly[0][1] - y) < abs(poly[-1][1] - y) else poly[-1]


class Greenhouse:
    def __init__(self, S, sk, hood_rows, deck_rows, paint, detail='hi'):
        self.S, self.sk = S, sk
        G = S['gh']
        self.G = G
        hi = detail == 'hi'
        self.n = {'F': G.get('nF', 16) if hi else 6, 'S': G.get('nS', 44) if hi else 14,
                  'B': G.get('nB', 16) if hi else 6}
        self.nt = G.get('nt', 14) if hi else 5
        self.paint = paint
        self._belt(hood_rows, deck_rows)
        self._roof()
        self._loft()
        self.resolve_windows()

    # ------------------------------------------------------------ loops
    def _belt(self, hood_rows, deck_rows):
        S, sk = self.S, self.sk
        cowl, deck = S['cowl_y'], S['deck_y']
        F = hood_rows[0]                                  # left -> right across the cowl
        hatch = deck_rows is None
        x_face = S['half_w'] - S.get('taper_rear', 0.0) - S['rc_rear'] + 0.002   # rear face / corner boundary
        side = [sk.point(p, sk.z_top(p[1])) for p in sk.pts
                if deck <= p[1] <= cowl and (p[0] > x_face if hatch else p[0] > S['half_w'] - 0.35)]
        side = sorted(side, key=lambda q: -q[1])          # front -> rear
        if deck_rows is not None:
            B = list(reversed(deck_rows[0]))              # right -> left
        else:
            # hatch/van: the belt runs round the tail
            rear = [sk.point(p, sk.z_top(p[1])) for p in sk.pts if p[1] < 0 and p[0] <= x_face]
            rear = sorted(rear, key=lambda q: -q[0])      # right -> centre
            B = rear + [(-x, y, z) for x, y, z in reversed(rear[:-1])]
        side = [F[-1]] + side + [B[0]]
        self.belt = {'F': _resample(F, self.n['F']), 'B': _resample(B, self.n['B'])}
        self._belt_side = side

    def roof_z(self, y):
        R = self.G['roof']
        ym = 0.5 * (R['y_front'] + R['y_rear'])
        h = 0.5 * (R['y_front'] - R['y_rear'])
        u = (y - ym) / h
        drop = R['drop_f'] if u > 0 else R['drop_r']
        return R['z'] - drop * u * u

    def _roof(self):
        R = self.G['roof']
        yf, yr, w, rc = R['y_front'], R['y_rear'], R['half_w'], R['corner_r']
        bowf = R.get('bow_f', 0.04)
        # front edge (left -> right), slightly bowed forward
        F = [(x, yf - bowf * (x / (w - rc)) ** 2, 0) for x in [-(w - rc) + 2 * (w - rc) * i / 30 for i in range(31)]]
        side = [(w - rc + rc * math.cos(math.pi / 2 - (math.pi / 2) * i / 20),
                 (yf - bowf) - rc + rc * math.sin(math.pi / 2 - (math.pi / 2) * i / 20), 0) for i in range(21)]
        for i in range(1, 40):
            side.append((w, (yf - bowf - rc) + ((yr + rc) - (yf - bowf - rc)) * i / 40, 0))
        for i in range(21):                              # rear-right corner
            a = -(math.pi / 2) * i / 20
            side.append((w - rc + rc * math.cos(a), yr + rc + rc * math.sin(a), 0))
        B = [(x, yr + R.get('bow_r', 0.02) * (x / (w - rc)) ** 2, 0) for x in [(w - rc) - 2 * (w - rc) * i / 30 for i in range(31)]]
        F = [(x, y, self.roof_z(y)) for x, y, _ in F]
        side = [(x, y, self.roof_z(y)) for x, y, _ in side]
        B = [(x, y, self.roof_z(y)) for x, y, _ in B]
        self.roofloop = {'F': _resample(F, self.n['F']), 'B': _resample(B, self.n['B'])}
        self._roof_side = side
        self._pair_sides()

    def _pair_sides(self):
        """Pair belt and roof side points through stations (belt y -> roof y) so
        pillars get the intended lean (e.g. a near-vertical B pillar)."""
        bs, rs = self._belt_side, self._roof_side
        st = [(bs[0][1], rs[0][1])] + list(self.G.get('stations', [])) + [(bs[-1][1], rs[-1][1])]

        def roof_y(by):
            for (b0, r0), (b1, r1) in zip(st, st[1:]):
                if (b0 - by) * (b1 - by) <= 0 and b0 != b1:
                    return r0 + (r1 - r0) * (by - b0) / (b1 - b0)
            return st[0][1] if abs(by - st[0][0]) < abs(by - st[-1][0]) else st[-1][1]

        # every window / pillar edge becomes a loft column, so pillars survive
        # even at low detail and window edges are exactly straight
        cuts = {round(b, 4) for b, _ in st}
        for w in self.G['windows'] + self.G.get('black', []):
            if w.get('seg') == 'S' and 'y0' in w:
                for yv in (w['y0'], w['y1']):
                    if bs[-1][1] < yv < bs[0][1]:
                        cuts.add(round(yv, 4))
        ys = sorted(cuts, reverse=True)
        st = [(b, roof_y(b)) for b in ys]
        total = abs(st[0][0] - st[-1][0])
        n = self.n['S']
        belt, roof = [], []
        for (b0, r0), (b1, r1) in zip(st, st[1:]):
            k = max(1, int(round(n * abs(b0 - b1) / total)))
            for i in range(k):
                u = i / k
                belt.append(_at_y(bs, b0 + (b1 - b0) * u))
                roof.append(_at_y(rs, r0 + (r1 - r0) * u))
        belt.append(bs[-1])
        roof.append(rs[-1])
        self.belt['S'] = belt
        self.roofloop['S'] = roof
        self.n['S'] = len(belt)

    def loop(self, which):
        L = self.belt if which == 'belt' else self.roofloop
        R_side = L['S']
        L_side = [(-x, y, z) for x, y, z in reversed(R_side)]
        # F (left->right), S right (front->rear), B (right->left), S left (rear->front)
        pts, tags = [], []
        for seg, arr in (('F', L['F'][:-1]), ('S', R_side[:-1]), ('B', L['B'][:-1]), ('L', L_side[:-1])):
            n = len(arr)
            for i, p in enumerate(arr):
                pts.append(p)
                tags.append((seg, i / max(1, n)))
        return pts, tags

    # ------------------------------------------------------------ loft
    def _loft(self):
        G = self.G
        belt, tags = self.loop('belt')
        roof, _ = self.loop('roof')
        bul = G.get('bulge', {'F': 0.02, 'S': 0.012, 'B': 0.015})
        rows = []
        for k in range(self.nt + 1):
            t = k / self.nt
            row = []
            for (seg, f), b, r in zip(tags, belt, roof):
                p = vlerp(b, r, t)
                if seg == 'F':
                    n = (0, 1, 0)
                elif seg == 'B':
                    n = (0, -1, 0)
                else:
                    n = (1 if seg == 'S' else -1, 0, 0)
                amt = bul['F' if seg == 'F' else 'B' if seg == 'B' else 'S'] * math.sin(math.pi * t)
                row.append(add(p, mul(n, amt)))
            rows.append(row)
        self.rows = rows
        self.tags = tags
        # roof cap: rings shrinking to the roof centre, crowned
        R = G['roof']
        cy = 0.5 * (R['y_front'] + R['y_rear'])
        K = 6 if self.nt > 5 else 2
        cap = [rows[-1]]
        for k in range(1, K + 1):
            s = 1 - k / K
            ring = []
            for x, y, z in rows[-1]:
                yy = cy + (y - cy) * s
                ring.append((x * s, yy, self.roof_z(yy) + R.get('crown', 0.03) * (1 - s * s)))
            cap.append(ring)
        self.cap = cap

    # ------------------------------------------------------------ query
    def frac_at_y(self, y):
        """Fraction along the right-hand side segment whose belt point is at y."""
        side = self.belt['S']
        n = len(side) - 1
        best = min(range(len(side)), key=lambda i: abs(side[i][1] - y))
        return best / n

    def resolve_windows(self):
        """Windows/black areas may be given by longitudinal position (y0 front,
        y1 rear) on the flank; convert those to fractions of the side loft."""
        for w in self.G['windows'] + self.G.get('black', []):
            if 'y0' in w:
                w['f0'] = self.frac_at_y(w['y0'])
                w['f1'] = self.frac_at_y(w['y1'])

    def point(self, seg, f, t):
        """Point on the loft for segment ('F','S','B'), fraction along it, height t."""
        idx = [i for i, (s, ff) in enumerate(self.tags) if s == seg]
        fr = [self.tags[i][1] for i in idx]
        j = min(range(len(idx)), key=lambda q: abs(fr[q] - f))
        i = idx[j]
        k = t * self.nt
        k0 = int(min(self.nt - 1, math.floor(k)))
        return vlerp(self.rows[k0][i], self.rows[k0 + 1][i], k - k0)

    def glass_z(self, x, y):
        """Height of the windshield / front loft surface at (x, y); below the
        cowl it returns the cowl height, above the roof front the roof height."""
        cols = [i for i, (sg, _) in enumerate(self.tags) if sg == 'F']
        i = min(cols, key=lambda c: abs(abs(self.rows[0][c][0]) - abs(x)))
        pts = [r[i] for r in self.rows]
        if y >= pts[0][1]:
            return pts[0][2]
        for a, b in zip(pts, pts[1:]):
            if b[1] <= y <= a[1]:
                f = (y - a[1]) / ((b[1] - a[1]) or 1)
                return a[2] + (b[2] - a[2]) * f
        return pts[-1][2]

    def side_x(self, y, z):
        """Half-width of the side loft (glass/pillar) at longitudinal y, height z."""
        cols = [i for i, (sg, _) in enumerate(self.tags) if sg == 'S']
        best = None
        for k in range(len(self.rows) - 1):
            for i in cols:
                a, b = self.rows[k][i], self.rows[k + 1][i]
                if a[2] <= z <= b[2]:
                    f = (z - a[2]) / ((b[2] - a[2]) or 1)
                    p = (a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f)
                    if best is None or abs(p[1] - y) < abs(best[1] - y):
                        best = p
        return None if best is None else best[0]

    def classify(self, seg, f, t):
        seg = 'S' if seg == 'L' else seg
        for w in self.G['windows']:
            if w['seg'] == seg and w['f0'] <= f <= w['f1'] and w['t0'] <= t <= w['t1']:
                return 'glass', w['name']
        m = self.G.get('frame', 0.02)
        for b in self.G.get('black', []):
            if b['seg'] == seg and b['f0'] <= f <= b['f1'] and b['t0'] <= t <= b['t1']:
                return 'black', None
        for w in self.G['windows']:
            if w.get('frame', True) and w['seg'] == seg and w['f0'] - m <= f <= w['f1'] + m and w['t0'] - m <= t <= w['t1'] + m:
                return 'black', None
        return 'paint', None

    def ref(self, c):
        R = self.G['roof']
        return (c[0], c[1] - clamp(c[1], R['y_rear'] + 0.1, R['y_front'] - 0.1), c[2] - (self.S['top_keys'][0][1] - 0.2))

    def build(self, parts, glass_mat='Glass'):
        rows, tags = self.rows, self.tags
        m = len(rows[0])
        V = [p for r in rows for p in r]
        groups = {}
        for k in range(self.nt):
            t = (k + 0.5) / self.nt
            for i in range(m):
                i2 = (i + 1) % m
                seg, f = tags[i]
                seg2, f2 = tags[i2]
                fc = f + (1.0 / max(1, self.n['S' if seg in 'SL' else seg] - 1)) * 0.5
                if seg == 'L':
                    fc = 1 - fc   # left side mirrors the right-hand window layout
                cls, name = self.classify(seg, fc, t)
                q = (k * m + i, k * m + i2, (k + 1) * m + i2, (k + 1) * m + i)
                if cls == 'glass':
                    side = '' if seg in 'FB' else ('_R' if seg == 'S' else '_L')
                    key = ('glass', name + side)
                else:
                    key = (cls, None)
                groups.setdefault(key, []).append(q)
        # roof cap goes with the paint
        cap_v, cap_f = grid(self.cap, closed_u=True)
        base = len(V)
        V2 = V + cap_v
        groups.setdefault(('paint', None), []).extend(tuple(i + base for i in q) for q in cap_f)

        def subset(faces):
            used = sorted({i for q in faces for i in q})
            remap = {o: n for n, o in enumerate(used)}
            return [V2[i] for i in used], [tuple(remap[i] for i in q) for q in faces]

        out = {}
        for (cls, name), faces in groups.items():
            vf = orient_to(subset(faces), self.ref)
            if cls == 'glass':
                # inset the glass 7 mm behind the frame
                v, f = vf
                v = [self._inset(p, 0.007) for p in v]
                pt = Part('Glass_' + name, material=glass_mat)
                pt.solidify = 0.004
                pt.add((v, f))
            elif cls == 'black':
                pt = Part('Greenhouse_Trim', material='Trim_Black')
                pt.solidify = 0.008
                pt.add(vf)
            else:
                pt = Part('Roof', material=self.paint)
                pt.solidify = 0.010
                pt.add(vf)
            parts.append(pt)
            out[(cls, name)] = pt
        # interior trim: pillars and headliner, facing the cabin
        trim_faces = [q for (cls, _), fs in groups.items() if cls == 'paint' for q in fs]
        v, f = subset(trim_faces)
        v = [self._inset(p, 0.028) for p in v]
        v = [(x, y, z - (0.008 if z > self.G['roof']['z'] - 0.06 else 0.0)) for x, y, z in v]
        vf = orient_to((v, f), lambda c: tuple(-e for e in self.ref(c)))
        pt = Part('Headliner', material='Interior_Stone')
        pt.add(vf)
        parts.append(pt)
        return out

    def _inset(self, p, d):
        R = self.G['roof']
        cy = clamp(p[1], R['y_rear'] + 0.2, R['y_front'] - 0.2)
        c = (0.0, cy, self.S['top_keys'][0][1] - 0.1)
        n = norm(sub(p, c))
        return sub(p, mul(n, d))
