"""Box-type bodies (bus, minibus, tram): a loft of rounded-rectangle sections.

Author frame (the whole kit): X right, Y forward (+Y = nose), Z up, metres,
ground at Z = 0. tools/build_transit.py turns the finished asset by 180 deg
around Z, so the exported model faces -Y as docs/art-pipeline.md requires.
"""
import math
from vehicle_kit.geom import grid, make_outward, orient_to, lin, merge


class Station:
    """One section of the body: half width, bottom, top, corner radii."""

    def __init__(self, y, hw, zb, zt, rb, rt):
        self.y, self.hw, self.zb, self.zt = y, hw, zb, zt
        h = max(1e-3, zt - zb)
        self.rb = max(0.0, min(rb, hw * 0.98, h * 0.45))
        self.rt = max(0.0, min(rt, hw * 0.98, h * 0.45))


def ring(st, nc=5):
    """Section outline, 4 * (nc + 1) points, same count for every station."""
    pts = []
    corners = ((st.hw - st.rb, st.zb + st.rb, st.rb, -90, 0),
               (st.hw - st.rt, st.zt - st.rt, st.rt, 0, 90),
               (-st.hw + st.rt, st.zt - st.rt, st.rt, 90, 180),
               (-st.hw + st.rb, st.zb + st.rb, st.rb, 180, 270))
    for cx, cz, r, a0, a1 in corners:
        for i in range(nc + 1):
            a = math.radians(a0 + (a1 - a0) * i / nc)
            pts.append((cx + r * math.cos(a), st.y, cz + r * math.sin(a)))
    return pts


def loft(stations, nc=5):
    """Closed shell through the stations (sorted by y), capped at both ends."""
    stations = sorted(stations, key=lambda s: s.y)
    rows = [ring(s, nc) for s in stations]
    v, f = grid(rows, closed_u=True)
    m = len(rows[0])
    for idx in (0, len(rows) - 1):
        r = rows[idx]
        c = (0.0, r[0][1], sum(p[2] for p in r) / m)
        v.append(c)
        ci = len(v) - 1
        base = idx * m
        f += [(ci, base + (k + 1) % m, base + k) for k in range(m)]
    return make_outward((v, f))


class Profile:
    """Body description along Y.

    hw_keys / zt_keys / zb_keys: piecewise-linear (y, value) keys for the
    straight part; nose/tail get plan corner radius rc and a roof drop re.
    arches: [(wheel_y, wheel_z, arch_r)] cut into the bottom edge.
    """

    def __init__(self, tail, nose, hw, zb, zt, rb, rt, rc_front, rc_rear, re_front=0.0, re_rear=0.0,
                 hw_keys=None, zt_keys=None, rt_keys=None, arches=(), cutouts=(), step=0.12, n_end=7):
        self.tail, self.nose = tail, nose
        self.hw, self.zb, self.zt, self.rb, self.rt = hw, zb, zt, rb, rt
        self.rc_front, self.rc_rear, self.re_front, self.re_rear = rc_front, rc_rear, re_front, re_rear
        self.hw_keys, self.zt_keys, self.rt_keys = hw_keys, zt_keys, rt_keys
        self.arches = list(arches)
        self.cutouts = list(cutouts)          # [(y0, y1, z_top)]: rectangular openings (tram bogies)
        self.step, self.n_end = step, n_end

    # ---------------------------------------------------------- laws
    def hw_at(self, y):
        return lin(self.hw_keys, y) if self.hw_keys else self.hw

    def zt_at(self, y):
        return lin(self.zt_keys, y) if self.zt_keys else self.zt

    def rt_at(self, y):
        return lin(self.rt_keys, y) if self.rt_keys else self.rt

    def zb_at(self, y):
        z = self.zb
        for wy, wz, ra in self.arches:
            d = abs(y - wy)
            if d <= ra:
                z = max(z, wz + math.sqrt(max(0.0, ra * ra - d * d)))
        for y0, y1, zc in self.cutouts:
            if y0 <= y <= y1:
                z = max(z, zc)
        return z

    def end_theta(self, y):
        """0 on the straight part, rising to pi/2 at the nose/tail face."""
        if y > self.nose - self.rc_front:
            return math.asin(max(-1.0, min(1.0, (y - (self.nose - self.rc_front)) / self.rc_front))), 'front'
        if y < self.tail + self.rc_rear:
            return math.asin(max(-1.0, min(1.0, ((self.tail + self.rc_rear) - y) / self.rc_rear))), 'rear'
        return 0.0, None

    def station(self, y):
        th, end = self.end_theta(y)
        hw, zt = self.hw_at(y), self.zt_at(y)
        if end:
            rc = self.rc_front if end == 'front' else self.rc_rear
            re = self.re_front if end == 'front' else self.re_rear
            hw -= rc * (1 - math.cos(th))
            zt -= re * (1 - math.cos(th))
        return Station(y, hw, self.zb_at(y), zt, self.rb, self.rt_at(y))

    # ---------------------------------------------------------- sampling
    def ys(self):
        ys = set()
        n = max(2, int((self.nose - self.tail) / self.step))
        for i in range(n + 1):
            ys.add(round(self.tail + (self.nose - self.tail) * i / n, 5))
        for i in range(self.n_end + 1):
            th = math.pi / 2 * i / self.n_end
            ys.add(round(self.nose - self.rc_front + self.rc_front * math.sin(th), 5))
            ys.add(round(self.tail + self.rc_rear - self.rc_rear * math.sin(th), 5))
        for wy, wz, ra in self.arches:
            for k in range(-8, 9):
                ys.add(round(wy + ra * math.sin(math.pi / 2 * k / 8), 5))
            ys.add(round(wy - ra - 0.002, 5))
            ys.add(round(wy + ra + 0.002, 5))
        for y0, y1, _ in self.cutouts:
            ys.update((round(y0 - 0.002, 5), round(y0, 5), round(y1, 5), round(y1 + 0.002, 5)))
        for keys in (self.hw_keys, self.zt_keys, self.rt_keys):
            for y, _ in keys or ():
                ys.add(round(y, 5))
        return sorted(y for y in ys if self.tail <= y <= self.nose)

    def shell(self, nc=5):
        return loft([self.station(y) for y in self.ys()], nc)

    # ---------------------------------------------------------- overlays
    def outline(self, z0, z1, y_min=None, y_max=None):
        """Plan outline (right side, x >= 0) of the body at heights z0..z1,
        from the tail centre to the nose centre, as runs [[(x, y, nx, ny)]].
        A new run starts wherever an arch or cutout interrupts the skin, so
        bands never bridge an opening."""
        runs, cur = [], []
        ys = [y for y in self.ys() if (y_min is None or y >= y_min) and (y_max is None or y <= y_max)]
        for y in ys:
            st = self.station(y)
            if st.zb + st.rb > z0 + 1e-4 or st.zt - st.rt < z1 - 1e-4:
                if cur:
                    runs.append(cur)
                cur = []
                continue
            cur.append((st.hw, y))
        if cur:
            runs.append(cur)
        out = []
        for pts in runs:
            if len(pts) < 2:
                continue
            run = []
            for i, (x, y) in enumerate(pts):
                p = pts[max(i - 1, 0)]
                q = pts[min(i + 1, len(pts) - 1)]
                tx, ty = q[0] - p[0], q[1] - p[1]
                l = math.hypot(tx, ty) or 1.0
                run.append((x, y, ty / l, -tx / l))
            out.append(run)
        return out

    def band(self, z0, z1, offset, y_min=None, y_max=None, sides=(1, -1), wrap_front=False, wrap_rear=False):
        """A strip lying `offset` outside the body skin between z0 and z1.

        Without wrapping it covers the side faces only; wrap_front/rear
        continue it around the nose/tail (windscreens, livery bands)."""
        pieces = []
        for run in self.outline(z0, z1, y_min, y_max):
            for s in sides:
                pts = [(s * (x + nx * offset), y + ny * offset) for x, y, nx, ny in run]
                pieces.append(_strip(pts, z0, z1, self._outward))
        if wrap_front or wrap_rear:
            for y_end, flag, sign in ((self.nose, wrap_front, 1), (self.tail, wrap_rear, -1)):
                if not flag:
                    continue
                st = self.station(y_end)
                pts = [(-st.hw, y_end + sign * offset), (st.hw, y_end + sign * offset)]
                if sign < 0:
                    pts.reverse()
                pieces.append(_strip(pts, z0, z1, self._outward))
        return merge(*pieces)

    def _outward(self, c):
        """Direction from the body axis to a point near the skin."""
        y = min(max(c[1], self.tail + self.rc_rear), self.nose - self.rc_front)
        return (c[0], c[1] - y, 0.0)

    def side_x(self, y):
        """Skin x (right side) on the straight vertical part of the section."""
        return self.station(y).hw


def _strip(pts, z0, z1, outward):
    rows = [[(x, y, z0) for x, y in pts], [(x, y, z1) for x, y in pts]]
    return orient_to(grid(rows), outward)
