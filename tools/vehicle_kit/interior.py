"""Cabins. 'classic' (2000s: binnacle with round gauges, upright dash),
'modern' (2020s: layered dash, screen cluster, floating display) and 'simple'
(police/ambulance/traffic: shapes only). Everything is placed relative to the
driver's hip point (H-point), so ergonomics are consistent."""
import math
from .geom import (Part, superellipsoid, rbox, prism_x, cylinder, torus, tube, slab, quad, grid, merge,
                   orient_to, transform, mirror_x, add, sub, mul, norm, lathe, clamp, fan)


def seat(center_h, style, recline=0.36, width=0.52, hi=True, headrest=True):
    """Front seat around hip point h=(x,y,z). Returns dict name->(vf, material)."""
    x, y, z = center_h
    su, sv = (20, 10) if hi else (10, 6)
    out = {}
    cush_top = z - 0.02
    cush = superellipsoid((x, y + 0.2, cush_top - 0.06), (width / 2 - 0.05, 0.26, 0.065), 0.35, su, sv)
    out['Cushion'] = (cush, 'Seat_Fabric' if style != 'leather' else 'Leather')
    # side bolsters
    bol = merge(*[superellipsoid((x + s * (width / 2 - 0.045), y + 0.2, cush_top - 0.035), (0.05, 0.25, 0.07), 0.45, su // 2, sv)
                  for s in (-1, 1)])
    out['Cushion_Bolster'] = (bol, 'Leather' if style != 'fabric' else 'Seat_Fabric')
    # backrest: modelled upright around a pivot behind the hip, then reclined
    piv = (x, y - 0.13, z - 0.02)
    back_local = merge(
        superellipsoid((0, -0.02, 0.33), (width / 2 - 0.05, 0.075, 0.32), 0.35, su, sv),
        *[superellipsoid((s * (width / 2 - 0.045), 0.02, 0.30), (0.05, 0.07, 0.29), 0.45, su // 2, sv) for s in (-1, 1)])
    out['Backrest'] = (transform(back_local, (recline, 0, 0), piv), 'Seat_Fabric' if style != 'leather' else 'Leather')
    ins = superellipsoid((0, 0.045, 0.32), (width / 2 - 0.12, 0.012, 0.24), 0.3, su // 2, sv // 2)
    out['Backrest_Insert'] = (transform(ins, (recline, 0, 0), piv), 'Seat_Insert')
    if headrest:
        hr = superellipsoid((0, -0.02, 0.76), (0.13, 0.055, 0.085), 0.5, su // 2, sv)
        rods = merge(*[cylinder((s * 0.07, -0.02, 0.62), (s * 0.07, -0.02, 0.70), 0.007, 8) for s in (-1, 1)])
        out['Headrest'] = (transform(hr, (recline * 0.7, 0, 0), piv), 'Leather')
        out['HeadrestRod'] = (transform(rods, (recline * 0.7, 0, 0), piv), 'Chrome')
    rails = merge(*[rbox((x + s * 0.18, y + 0.12, z - 0.19), (0.03, 0.55, 0.03), 0.008) for s in (-1, 1)])
    base = rbox((x, y + 0.15, z - 0.15), (width - 0.14, 0.42, 0.08), 0.02)
    out['Seat_Frame'] = (merge(rails, base), 'Interior_Graphite')
    return out


def headrest_front(h, recline=0.36):
    """Y of the front face of the headrest (for eye clearance checks)."""
    piv_y = h[1] - 0.13
    return piv_y - 0.02 + 0.055 - math.sin(recline * 0.7) * 0.76


class Cabin:
    def __init__(self, S, sk, gh, detail='hi'):
        self.S, self.sk, self.gh = S, sk, gh
        self.C = S['cabin']
        self.hi = detail == 'hi'
        C = self.C
        self.H = (C['driver_x'], C['h_y'], C['h_z'])
        self.recline = C.get('recline', 0.36)
        self.eye = add(self.H, (0, -0.10 - 0.1 * math.sin(self.recline - 0.3), C.get('eye_h', 0.66)))

    def inner_x(self, y, z, wall=0.055):
        a = self.sk.a_side(y)
        x, _, _ = self.sk.point(self.sk.at(a), z)
        return x - wall

    # --------------------------------------------------------------- build
    def build(self, parts, style):
        start = len(parts)
        self._build(parts, style)
        # anything of the cabin that rises above the belt stays inside the glass
        for p in parts[start:]:
            if p.kind != 'mesh' or p.parent is not None or not p.v or any(p.loc) or any(p.rot):
                continue
            nv = []
            for x, y, z in p.v:
                if z > self.sk.z_top(y) + 0.01:
                    sx = self.gh.side_x(y, z)
                    if sx is not None and abs(x) > sx - 0.02:
                        x = math.copysign(sx - 0.02, x)
                nv.append((x, y, z))
            p.v = nv

    def _build(self, parts, style):
        C, S = self.C, self.S
        hi = self.hi
        H = self.H
        pk = lambda name, vf, mat, parent=None, loc=(0, 0, 0), rot=(0, 0, 0): parts.append(Part(name, material=mat, parent=parent, loc=loc, rot=rot).add(vf))
        fz = C['floor_z']
        y_fw = C['firewall_y']
        y_rear = C['rear_wall_y']
        xi = self.inner_x(0.0, fz - 0.04, 0.02)
        # floor with footwells and tunnel
        floor = rbox((0, (y_fw + y_rear) / 2, fz - 0.02), (2 * xi, y_fw - y_rear, 0.04), 0.01)
        tun = prism_x([(y_fw, fz), (y_fw, fz + C['tunnel_h']), (y_fw - 0.3, fz + C['tunnel_h'] * 0.95),
                       (y_rear + 0.4, fz + C['tunnel_h'] * 0.8), (y_rear + 0.35, fz)], -C['tunnel_w'] / 2, C['tunnel_w'] / 2)
        toe = slab((0, y_fw + 0.08, fz + 0.14), (1, 0, 0), norm((0, 0.55, 0.83)), 2 * xi - 0.1, 0.34, 0.03)
        pk('Cabin_Floor', merge(floor, tun, toe), 'Carpet')
        for x, name in ((H[0], 'DriverFloorMat'), (-H[0], 'PassengerFloorMat')):
            pk(name, rbox((x, H[1] + 0.62, fz + 0.006), (0.46, 0.55, 0.012), 0.01), 'Rubber')
        # seats ---------------------------------------------------------------
        seat_style = C.get('seat_style', 'fabric')
        for side, sx in (('L', 1), ('R', -1)):
            name = 'Seat_Front_' + side
            hx = H[0] * sx
            parts.append(Part(name, kind='empty', loc=(hx, H[1], H[2])))
            for k, (vf, mat) in seat((0, 0, 0), seat_style, self.recline, hi=hi).items():
                pk(k + '_' + side, vf, mat, parent=name)
        self.build_rear_bench(parts, seat_style)
        # dashboard -----------------------------------------------------------
        if style == 'classic':
            self.dash_classic(parts)
        elif style == 'modern':
            self.dash_modern(parts)
        else:
            self.dash_simple(parts)
        self.steering(parts, style)
        if style != 'simple':
            self.pedals(parts, style)
            self.console(parts, style)
            self.door_cards(parts, style)
            self.visors(parts)
        else:
            fz = C['floor_z']
            parts.append(Part('CentreConsole', material='Interior_Graphite').add(
                rbox((0, H[1] + 0.25, fz + C['console_h'] / 2 + 0.05), (0.2, 0.7, C['console_h']), 0.02)))
        self.mirror_centre(parts)
        parts.append(Part('Socket_DriverEye', kind='empty', loc=self.eye))
        parts.append(Part('Socket_CentreOfMass', kind='empty', loc=(0, C.get('com_y', -0.1), C.get('com_z', 0.52))))

    def build_rear_bench(self, parts, seat_style):
        C = self.C
        if not C.get('rear_bench', True):
            return
        yh, zh = C['rear_h_y'], C['rear_h_z']
        w = C.get('rear_half_w', 0.62)
        parts.append(Part('Seat_RearBench', kind='empty', loc=(0, yh, zh)))
        su, sv = (24, 10) if self.hi else (10, 6)
        mat = 'Seat_Fabric' if seat_style != 'leather' else 'Leather'
        bh = C.get('rear_back_h', 0.3)
        rr = C.get('rear_recline', 0.3)
        cush = superellipsoid((0, 0.2, -0.08), (w, 0.25, 0.07), 0.3, su, sv)
        back = transform(superellipsoid((0, 0, bh), (w, 0.07, bh), 0.3, su, sv), (rr, 0, 0), (0, -0.12, -0.02))
        parts.append(Part('RearBench_Cushion', material=mat, parent='Seat_RearBench').add(cush))
        parts.append(Part('RearBench_Backrest', material=mat, parent='Seat_RearBench').add(back))
        hrs = []
        for x in (-w + 0.2, 0.0, w - 0.2):
            hrs.append(transform(superellipsoid((x, 0, 2 * bh + 0.07), (0.12, 0.05, 0.06), 0.5, 12, 6), (rr, 0, 0), (0, -0.12, -0.02)))
        parts.append(Part('RearHeadrests', material='Leather', parent='Seat_RearBench').add(merge(*hrs)))
        # parcel shelf / cargo cover
        if C.get('parcel_shelf'):
            y0, y1, z = C['parcel_shelf']
            xw = self.inner_x((y0 + y1) / 2, z, 0.03)
            parts.append(Part('RearParcelShelf', material='Interior_Graphite').add(rbox((0, (y0 + y1) / 2, z), (2 * xw, y0 - y1, 0.02), 0.008)))
        if C.get('cargo_floor'):
            y0, y1, z = C['cargo_floor']
            xw = min(self.inner_x((y0 + y1) / 2, z + 0.1, 0.03), S_arch_limit(self.S))
            parts.append(Part('CargoFloor', material='Carpet').add(rbox((0, (y0 + y1) / 2, z), (2 * xw, y0 - y1, 0.03), 0.008)))

    # ------------------------------------------------------------------ dash
    def _dash_extent(self):
        C = self.C
        y_base, z_base = C['dash_front']                  # meets the windshield base
        xw = self.inner_x(C['dash_face_y'], z_base - 0.05, C.get('dash_inset', 0.03))
        return y_base, z_base, xw

    def under_glass(self, prof, xw, gap=0.025):
        """Clamp a (y, z) dash profile below the windshield at the dash's outer edge."""
        return [(y, min(z, self.gh.glass_z(xw, y) - gap)) for y, z in prof]

    def dash_classic(self, parts):
        C, H = self.C, self.H
        y_base, z_base, xw = self._dash_extent()
        yf = C['dash_face_y']
        top = C['dash_top_z']
        prof = [(y_base + 0.02, C['floor_z'] + 0.25), (y_base + 0.02, z_base - 0.02), (y_base - 0.06, top),
                (yf + 0.05, top + 0.005), (yf, top - 0.03), (yf - 0.01, top - 0.2), (yf + 0.07, top - 0.36),
                (yf + 0.22, top - 0.42)]
        dash = prism_x(self.under_glass(prof, xw), -xw, xw)
        parts.append(Part('Dashboard', material='Interior_Graphite').add(dash))
        # binnacle hood over two round gauges
        cx, cz = H[0], top - 0.04
        hood = superellipsoid((cx, yf + 0.02, cz + 0.03), (0.2, 0.09, 0.06), 0.4, 20, 8)
        parts.append(Part('Instrument_Hood', material='Interior_Graphite').add(hood))
        face_y = yf - 0.045
        self.gauges(parts, [(cx - 0.085, 'RPM', 0.07), (cx + 0.085, 'Speed', 0.07)], face_y, cz - 0.005, ring=True)
        self.cluster_y = face_y
        self.cluster_z = cz - 0.005
        # centre stack: two vents, radio, three knobs
        sx = 0.0
        stack = rbox((sx, yf + 0.03, top - 0.2), (0.3, 0.1, 0.34), 0.02)
        parts.append(Part('CentreStack', material='Interior_Graphite').add(stack))
        for vx in (-0.075, 0.075):
            self.vent(parts, (vx, yf - 0.021, top - 0.08), 0.1, 0.06, 'Vent_Centre')
        for vx in (-xw + 0.12, xw - 0.12):
            self.vent(parts, (vx, yf - 0.012, top - 0.07), 0.11, 0.07, 'Vent_Side')
        parts.append(Part('Radio', material='Display').add(slab((sx, yf - 0.022, top - 0.2), (1, 0, 0), (0, 0, 1), 0.18, 0.05, 0.006)))
        knobs = merge(*[cylinder((x, yf - 0.02, top - 0.29), (x, yf - 0.045, top - 0.29), 0.022, 16) for x in (-0.08, 0, 0.08)])
        parts.append(Part('ClimateDial', material='Satin_Aluminium').add(knobs))
        glove = slab((-H[0] * 1.0 + 0.0, yf - 0.012, top - 0.2), (1, 0, 0), (0, 0, 1), 0.36, 0.14, 0.006)
        parts.append(Part('Glovebox', material='Interior_Graphite_Light').add(glove))

    def dash_modern(self, parts):
        C, H = self.C, self.H
        y_base, z_base, xw = self._dash_extent()
        yf = C['dash_face_y']
        top = C['dash_top_z']
        # upper soft layer
        up = [(y_base + 0.02, z_base - 0.1), (y_base + 0.02, z_base - 0.02), (y_base - 0.05, top), (yf + 0.03, top),
              (yf - 0.02, top - 0.04), (yf, top - 0.1), (yf + 0.12, top - 0.12)]
        parts.append(Part('Dashboard', material='Interior_Graphite').add(prism_x(self.under_glass(up, xw), -xw, xw)))
        # lower layer, set back, with a trim strip between the two
        lo = [(y_base + 0.02, C['floor_z'] + 0.25), (y_base + 0.02, top - 0.12), (yf + 0.06, top - 0.12),
              (yf + 0.04, top - 0.3), (yf + 0.14, top - 0.4), (yf + 0.3, top - 0.44)]
        parts.append(Part('Dashboard_Lower', material='Interior_Stone').add(prism_x(self.under_glass(lo, xw), -xw + 0.01, xw - 0.01)))
        strip = rbox((0, yf + 0.0, top - 0.11), (2 * xw - 0.06, 0.03, 0.022), 0.006)
        parts.append(Part('Dash_Trim', material='Satin_Aluminium').add(strip))
        # screen cluster under a slim hood
        cx, cz = H[0], top + 0.005
        hood = superellipsoid((cx, yf + 0.06, cz + 0.045), (0.17, 0.075, 0.03), 0.3, 16, 6)
        parts.append(Part('Instrument_Hood', material='Interior_Graphite').add(hood))
        scr = slab((cx, yf + 0.02, cz), (1, 0, 0), norm((0, -0.25, 1)), 0.3, 0.11, 0.012)
        parts.append(Part('Cluster_Display', material='Display').add(scr))
        self.gauges(parts, [(cx - 0.085, 'RPM', 0.045), (cx + 0.085, 'Speed', 0.045)], yf + 0.012, cz, ring=False)
        self.cluster_y = yf + 0.012
        self.cluster_z = cz
        # floating touchscreen
        tab = slab((0.02, yf + 0.03, top + 0.07), (1, 0, 0), norm((0, -0.18, 1)), 0.26, 0.16, 0.018)
        parts.append(Part('Infotainment', material='Interior_Graphite').add(tab))
        parts.append(Part('Infotainment_Glass', material='Display').add(slab((0.02, yf + 0.02, top + 0.072), (1, 0, 0), norm((0, -0.18, 1)), 0.24, 0.14, 0.004)))
        for vx in (-0.1, 0.12, -xw + 0.1, xw - 0.1):
            self.vent(parts, (vx, yf - 0.012, top - 0.06), 0.14 if abs(vx) < 0.3 else 0.1, 0.035, 'Vent')
        knobs = merge(*[rbox((x, yf + 0.05, top - 0.2), (0.05, 0.02, 0.018), 0.006) for x in (-0.09, -0.03, 0.03, 0.09)])
        parts.append(Part('ClimateButtons', material='Satin_Aluminium').add(knobs))

    def dash_simple(self, parts):
        C = self.C
        y_base, z_base, xw = self._dash_extent()
        yf = C['dash_face_y']
        top = C['dash_top_z']
        prof = [(y_base + 0.02, C['floor_z'] + 0.25), (y_base + 0.02, z_base - 0.02), (y_base - 0.08, top), (yf, top),
                (yf - 0.01, top - 0.3), (yf + 0.2, top - 0.42)]
        parts.append(Part('Dashboard', material='Interior_Graphite').add(prism_x(self.under_glass(prof, xw), -xw, xw)))
        dz = C.get('cluster_dz', -0.035)
        self.cluster_y = yf - 0.01
        self.cluster_z = top + dz
        if dz > 0:   # instrument pod standing on the dash top (van)
            parts.append(Part('Instrument_Hood', material='Interior_Graphite').add(rbox((self.H[0], yf + 0.03, top + dz / 2 + 0.01), (0.34, 0.1, dz + 0.06), 0.02)))
        parts.append(Part('Cluster_Display', material='Display').add(slab((self.H[0], yf - 0.022, top + dz), (1, 0, 0), (0, 0, 1), 0.3, 0.07, 0.004)))

    def gauges(self, parts, dials, y, z, ring=True):
        for x, title, r in dials:
            if ring:
                parts.append(Part('GaugeFace_' + title, material='Rubber').add(cylinder((x, y + 0.012, z), (x, y, z), r, 32)))
                parts.append(Part('GaugeBezel_' + title, material='Satin_Aluminium').add(torus((x, y - 0.002, z), (0, 1, 0), r, 0.004, 32, 6)))
            ticks = []
            for i in range(11):
                a = math.radians(-130 + i * 26)
                p1 = (x + math.sin(a) * r * 0.78, y - 0.003, z + math.cos(a) * r * 0.78)
                p2 = (x + math.sin(a) * r * 0.92, y - 0.003, z + math.cos(a) * r * 0.92)
                ticks.append(tube([p1, p2], 0.0018, 4))
            parts.append(Part('GaugeTicks_' + title, material='Ink').add(merge(*ticks)))
            piv = 'Needle_' + title
            parts.append(Part(piv, kind='empty', loc=(x, y - 0.006, z)))
            needle = tube([(0, 0, 0), (-r * 0.6, 0, -r * 0.6)], 0.0022, 4)
            parts.append(Part('NeedleBlade_' + title, material='Lamp_Red', parent=piv).add(needle))
            parts.append(Part('NeedleHub_' + title, material='Satin_Aluminium', parent=piv).add(cylinder((0, 0.001, 0), (0, -0.004, 0), 0.008, 12)))

    def vent(self, parts, c, w, h, name):
        frame = slab(c, (1, 0, 0), (0, 0, 1), w, h, 0.012)
        slats = merge(*[slab((c[0], c[1] - 0.007, c[2] - h / 2 + h * (k + 0.5) / 4), (1, 0, 0), (0, 0.3, 1), w - 0.012, 0.006, 0.003) for k in range(4)])
        parts.append(Part(name, material='Rubber').add(frame))
        parts.append(Part(name + '_Slats', material='Satin_Aluminium').add(slats))

    # ------------------------------------------------------------------ controls
    def steering(self, parts, style):
        C, H = self.C, self.H
        wc = add(H, C['wheel_offset'])          # wheel centre
        tilt = C.get('column_tilt', 0.42)       # rim plane leans back from vertical (rad)
        self.wheel_centre = wc
        self.wheel_tilt = tilt
        R = C.get('wheel_r', 0.185)
        piv = Part('SteeringWheel_Pivot', kind='empty', loc=wc, rot=(-tilt, 0, 0))
        parts.append(piv)
        # in pivot space the rim lies in the XZ plane, axis along local Y (towards -Y = driver)
        rim = torus((0, 0, 0), (0, 1, 0), R, 0.016 if style == 'modern' else 0.014, 48 if self.hi else 20, 10 if self.hi else 6)
        parts.append(Part('Steering_Rim', material='Leather', parent='SteeringWheel_Pivot').add(rim))
        hub = superellipsoid((0, -0.03, -0.01), (0.075, 0.035, 0.06), 0.45, 16, 8)
        spokes = []
        if style == 'classic':
            for a in (-60, 60, -120, 120):
                t = math.radians(a)
                spokes.append(tube([(0, -0.02, 0), (R * math.sin(t) * 0.95, -0.005, R * math.cos(t) * 0.95)], 0.012, 6))
        else:
            for a in (-90, 90, 180):
                t = math.radians(a)
                spokes.append(tube([(0, -0.025, -0.01), (R * math.sin(t) * 0.95, -0.01, R * math.cos(t) * 0.95)], 0.015, 6))
        parts.append(Part('Steering_Hub', material='Interior_Graphite', parent='SteeringWheel_Pivot').add(merge(hub, *spokes)))
        # column + shroud + stalks (static)
        axis = (0, math.cos(tilt), -math.sin(tilt))            # from wheel towards the dash
        axis = norm((0, math.sin(tilt + 0.0) * 0 + 1.0, -math.tan(tilt) * 0.0))
        dirv = norm((0, math.cos(tilt), math.sin(tilt) * 0 - math.sin(tilt)))
        p0 = add(wc, mul(dirv, 0.06))
        p1 = add(wc, mul(dirv, 0.34))
        parts.append(Part('SteeringColumn', material='Interior_Graphite').add(merge(cylinder(p0, p1, 0.055, 16), cylinder(p1, add(p1, mul(dirv, 0.1)), 0.035, 12))))
        st = []
        for s in (-1, 1):
            b = add(wc, mul(dirv, 0.1))
            st.append(tube([add(b, (s * 0.04, 0, 0.02)), add(b, (s * 0.2, -0.03, 0.03))], 0.008, 6))
        parts.append(Part('Stalks', material='Interior_Graphite').add(merge(*st)))

    def pedals(self, parts, style):
        C, H = self.C, self.H
        py, pz = H[1] + C.get('pedal_dy', 0.86), C['floor_z'] + 0.36
        xs = [(H[0] - 0.17, 'Clutch'), (H[0] + 0.0, 'Brake'), (H[0] + 0.16, 'Throttle')]
        for x, label in xs:
            name = 'Pedal_' + label
            parts.append(Part(name, kind='empty', loc=(x, py + 0.1, pz)))
            arm = tube([(0, 0, 0), (0, -0.1, -0.19)], 0.008, 6)
            w = 0.075 if label != 'Brake' else 0.09
            pad = transform(rbox((0, 0, 0), (w, 0.02, 0.1 if label != 'Throttle' else 0.2), 0.008), (-0.3, 0, 0), (0, -0.11, -0.22 if label != 'Throttle' else -0.18))
            parts.append(Part('PedalArm_' + label, material='Satin_Aluminium', parent=name).add(arm))
            parts.append(Part('PedalPad_' + label, material='Rubber', parent=name).add(pad))
        parts.append(Part('DeadPedal', material='Rubber').add(transform(rbox((0, 0, 0), (0.07, 0.2, 0.03), 0.008), (0.5, 0, 0), (H[0] - 0.31, py - 0.02, C['floor_z'] + 0.1))))
        self.pedal_pos = (H[0], py - 0.01, pz - 0.2)

    def console(self, parts, style):
        C, H = self.C, self.H
        fz = C['floor_z']
        y0, y1 = C['dash_face_y'] + 0.12, H[1] - 0.25
        hgt = C['console_h']
        prof = [(y0, fz + C['tunnel_h']), (y0, fz + hgt + 0.1), (y0 - 0.15, fz + hgt), (y1, fz + hgt), (y1, fz + C['tunnel_h'])]
        parts.append(Part('CentreConsole', material='Interior_Graphite').add(prism_x(prof, -0.1, 0.1)))
        parts.append(Part('Armrest', material='Leather').add(rbox((0, H[1] - 0.12, fz + hgt + 0.1), (0.18, 0.3, 0.07), 0.02)))
        # gearboxes: manual visible, automatic hidden (toggled by code)
        gy = H[1] + 0.34
        gz = fz + hgt + 0.02
        man = Part('Transmission_Manual', kind='empty', loc=(0, gy, gz))
        parts.append(man)
        parts.append(Part('ShiftBoot', material='Leather', parent='Transmission_Manual').add(superellipsoid((0, 0, 0.015), (0.06, 0.07, 0.035), 0.6, 12, 6)))
        parts.append(Part('GearLever_Pivot', kind='empty', parent='Transmission_Manual', loc=(0, 0, 0.02)))
        knob = superellipsoid((0, 0, 0.16), (0.028, 0.03, 0.028), 0.9 if style == 'classic' else 0.5, 12, 8)
        parts.append(Part('GearStick', material='Satin_Aluminium', parent='GearLever_Pivot').add(cylinder((0, 0, 0), (0, 0, 0.14), 0.009, 10)))
        parts.append(Part('GearKnob', material='Leather', parent='GearLever_Pivot').add(knob))
        auto = Part('Transmission_Automatic', kind='empty', loc=(0, gy, gz))
        auto.hidden = True
        parts.append(auto)
        a1 = Part('AutoSelector', material='Leather', parent='Transmission_Automatic')
        a1.add(merge(rbox((0, 0, 0.0), (0.12, 0.2, 0.03), 0.01), superellipsoid((0, 0, 0.08), (0.03, 0.05, 0.06), 0.5, 10, 6)))
        a1.hidden = True
        parts.append(a1)
        hb = Part('Handbrake_Pivot', kind='empty', loc=(0.0, H[1] - 0.02, fz + hgt + 0.03))
        parts.append(hb)
        parts.append(Part('HandbrakeLever', material='Leather', parent='Handbrake_Pivot').add(transform(rbox((0, 0.1, 0), (0.035, 0.22, 0.04), 0.012), (0.18, 0, 0))))

    def door_cards(self, parts, style):
        C = self.C
        z0 = C['floor_z'] + 0.12
        for key, y0, y1 in C['door_cards']:
            for side, sfx in ((1, '_R'), (-1, '_L')):
                rows = []
                for zz in [z0 + (C['card_top_z'] - z0) * i / 6 for i in range(7)]:
                    row = []
                    for i in range(9):
                        y = y1 + (y0 - y1) * i / 8
                        row.append((side * self.inner_x(y, zz, 0.04), y, zz))
                    rows.append(row)
                vf = orient_to(grid(rows), lambda c, s=side: (-s, 0, 0))
                parts.append(Part('DoorCard_' + key + sfx, material='Interior_Graphite' if style != 'modern' else 'Interior_Stone').add(vf))
                ym = (y0 + y1) / 2
                ax = side * (self.inner_x(ym, C['card_top_z'] - 0.22, 0.09))
                parts.append(Part('DoorArmrest_' + key + sfx, material='Leather').add(rbox((ax, ym - 0.05, C['card_top_z'] - 0.22), (0.07, (y0 - y1) * 0.55, 0.05), 0.015)))
                parts.append(Part('DoorPull_' + key + sfx, material='Satin_Aluminium').add(rbox((side * self.inner_x(ym + 0.1, C['card_top_z'] - 0.1, 0.055), ym + 0.1, C['card_top_z'] - 0.1), (0.02, 0.12, 0.025), 0.008)))
                if self.hi:
                    sp = cylinder((side * self.inner_x(ym, z0 + 0.12, 0.035), ym + (y0 - y1) * 0.25, z0 + 0.12),
                                  (side * self.inner_x(ym, z0 + 0.12, 0.05), ym + (y0 - y1) * 0.25, z0 + 0.12), 0.07, 20)
                    parts.append(Part('DoorSpeaker_' + key + sfx, material='Rubber').add(sp))

    def mirror_centre(self, parts):
        gh = self.gh
        top = gh.point('F', 0.5, 0.94)
        c = (0.0, top[1] - 0.09, top[2] - 0.075)
        parts.append(Part('CentreMirrorHousing', material='Interior_Graphite').add(merge(
            superellipsoid(c, (0.12, 0.022, 0.036), 0.4, 16, 6), tube([(0, top[1] - 0.03, top[2] - 0.03), (0, c[1], c[2] + 0.02)], 0.007, 6))))
        surf = Part('MirrorSurface_Centre', material='Mirror', loc=(c[0], c[1] - 0.023, c[2]))
        surf.add(slab((0, 0, 0), (-1, 0, 0), (0, 0, 1), 0.21, 0.058, 0.003))
        surf.smooth = False
        parts.append(surf)

    def visors(self, parts):
        gh = self.gh
        for s in (-1, 1):
            top = gh.point('F', 0.5 + s * 0.25, 0.97)
            parts.append(Part('SunVisor' + ('_R' if s > 0 else '_L'), material='Interior_Stone').add(
                rbox((top[0], top[1] - 0.09, top[2] - 0.03), (0.32, 0.13, 0.014), 0.005)))


def S_arch_limit(S):
    return S['wheel_x'] - S['tire_w'] / 2 - 0.08
