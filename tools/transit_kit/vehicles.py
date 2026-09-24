"""City bus, route minibus and tram (original designs, stylised by vehicle class).

Everything is authored in the kit frame (X right, Y forward, Z up, metres,
ground Z = 0, origin between the axles / bogie centres) and turned to the
Blender standard by common.to_blender at the end of build().

The glazing is modelled as dark tinted glass on the outside of a closed body
(no passenger saloon): these are traffic vehicles, not player cars.
"""
import math
from vehicle_kit.geom import Part, lathe, cylinder, tube, rbox, quad, merge, grid, orient_to, slab
from .body import Profile
from .common import (box, box_between, panel_x, panel_y, mesh, empty, text, road_wheel, flanged_wheel, to_blender)

GLASS_OFF = 0.004     # glass lies this far outside the skin
LIVERY_OFF = 0.003    # paint bands
TRIM_OFF = 0.009      # black seals and pillars, over the glass
LEAF_T = 0.035        # door leaf thickness


# ------------------------------------------------------------------ helpers
def side_poly(poly_yz, x, t, outward):
    """Thin plate lying on a side face (x = const), outline given in (y, z)."""
    from .common import prism
    xa, xb = (x, x + outward * t)
    v, f = prism(poly_yz, min(xa, xb), max(xa, xb))
    # (y, z, x) -> (x, y, z) is a cyclic permutation: a rotation, winding kept
    return [(c, a, b) for a, b, c in v], f


def door_leaves(parts, idx, side, x_skin, yc, width, z0, z1, paint, glass_z=(0.95, None)):
    """Double-leaf door on a side face. Each leaf hangs on its own pivot
    empty (Door_<n>_A_Pivot / _B_Pivot, vertical axis at the outer edge) so
    code can swing or slide it; the leaf mesh is a child in pivot space."""
    s = side
    seal = panel_x(x_skin, yc - width / 2 - 0.03, yc + width / 2 + 0.03, z0 - 0.02, z1 + 0.03, TRIM_OFF, s)
    parts.append(mesh(f'Door_{idx}_Seal', 'PT_Trim_Black', seal))
    xl = x_skin + s * (TRIM_OFF + 0.003)
    gz0 = glass_z[0]
    gz1 = glass_z[1] if glass_z[1] is not None else z1 - 0.12
    for leaf, (y_hinge, y_meet) in (('A', (yc + width / 2, yc)), ('B', (yc - width / 2, yc))):
        piv = f'Door_{idx}_{leaf}_Pivot'
        px, py = xl + s * LEAF_T / 2, y_hinge
        parts.append(empty(piv, (px, py, 0.0)))
        ya, yb = sorted((y_hinge, y_meet))
        ya, yb = ya + 0.004, yb - 0.004
        frame = box_between(z0, z1, xl, xl + s * LEAF_T, ya, yb)
        frame = ([(a - px, b - py, c) for a, b, c in frame[0]], frame[1])
        parts.append(mesh(f'Door_{idx}_{leaf}', paint, frame, parent=piv))
        g = box_between(gz0, gz1, xl + s * LEAF_T, xl + s * (LEAF_T + 0.004), ya + 0.06, yb - 0.06)
        g = ([(a - px, b - py, c) for a, b, c in g[0]], g[1])
        parts.append(mesh(f'Door_{idx}_{leaf}_Glass', 'PT_Glass_Tinted', g, parent=piv))


def lamp_box(parts, name, material, center, size):
    parts.append(mesh(name, material, box(center, size)))


def mirror(parts, sfx, arm_pts, head_c, head_size, mat_arm='PT_Plastic_Black'):
    parts.append(mesh('MirrorArm_' + sfx, mat_arm, tube(arm_pts, 0.018, 8), smooth=True))
    parts.append(mesh('MirrorHousing_' + sfx, 'PT_Plastic_Black', rbox(head_c, head_size, 0.02), smooth=True))
    c = (head_c[0], head_c[1] - head_size[1] / 2 - 0.004, head_c[2])
    # quad normal = right x up = (1,0,0) x (0,0,1) = (0,-1,0): faces rearwards
    surf = Part('MirrorSurface_' + sfx, material='PT_Mirror')
    surf.add(quad(c, (1, 0, 0), (0, 0, 1), head_size[0] - 0.03, head_size[2] - 0.03))
    surf.smooth = False
    parts.append(surf)


def plate(parts, name, center, facing, mat='PT_Plate_White'):
    w, h = 0.52, 0.113
    s = 1 if facing == '+Y' else -1
    parts.append(mesh(name, mat, panel_y(center[1], center[0] - w / 2, center[0] + w / 2, center[2] - h / 2,
                                         center[2] + h / 2, 0.01, s)))


def pillars(parts, P, ys, z0, z1, sides=(1, -1), width=0.07, mat='PT_Trim_Black', name='Pillar'):
    for s in sides:
        vfs = []
        for y in ys:
            vfs.append(panel_x(s * P.side_x(y), y - width / 2, y + width / 2, z0, z1, TRIM_OFF, s))
        parts.append(mesh(f'{name}_{"R" if s > 0 else "L"}', mat, merge(*vfs)))


def wheelhouse(parts, name, wy, ra, x_in, z0, z_top):
    """Inner wall of a wheel arch: you do not see through the arch."""
    vfs = []
    for s in (1, -1):
        vfs.append(box_between(z0, z_top, s * x_in, s * (x_in + 0.02), wy - ra, wy + ra))
    parts.append(mesh(name, 'PT_Plastic_Black', merge(*vfs)))


def skin_point(P, x_target, end):
    """Point on the plan outline near an end with |x| ~ x_target (right side)."""
    best = None
    for x, y, nx, ny in P.outline(P.zb + P.rb + 0.01, P.zb + P.rb + 0.02):
        if (end == 'front' and y > 0) or (end == 'rear' and y < 0):
            d = abs(x - x_target)
            if best is None or d < best[0]:
                best = (d, x, y)
    return best[1], best[2]


def finish(vid, title, parts, meta):
    root = Part(vid, kind='empty')
    for p in parts:
        if p.parent is None:
            p.parent = vid
    seen = set()
    for p in parts:
        if p.name in seen:
            raise ValueError(f'{vid}: duplicate object name {p.name}')
        seen.add(p.name)
    parts = [root] + parts
    parts.append(empty('Socket_Front', (0, 1, 0)))          # author +Y = front; turned to -Y below
    parts.append(empty('Socket_Up', (0, 0, 1)))
    parts[-1].parent = parts[-2].parent = vid
    meta.update(id=vid, title=title)
    return to_blender(parts, vid), meta


# ------------------------------------------------------------------ bus
def bus():
    """PT_Bus_City12: 12 m low-floor city bus, three double doors on the right."""
    L_F, L_R = 2.975, -2.975                  # axles, wheelbase 5.95 m
    NOSE, TAIL = 5.675, -6.325                # length 12.0 m
    HW, H = 1.275, 2.95                       # width 2.55 m, body height
    R, TW, RIM = 0.478, 0.275, 0.286          # 275/70 R22.5
    RA = 0.55
    paint = 'PT_Paint_BusWhite'
    P = Profile(TAIL, NOSE, HW, 0.30, H, 0.06, 0.22, rc_front=0.18, rc_rear=0.25, re_front=0.12, re_rear=0.15,
                arches=[(L_F, R, RA), (L_R, R, RA)])
    parts = [mesh('Body', paint, P.shell(), smooth=True)]
    wheelhouse(parts, 'Wheelhouse_F', L_F, RA, 0.72, 0.30, R + RA)
    wheelhouse(parts, 'Wheelhouse_R', L_R, RA, 0.58, 0.30, R + RA)
    # glazing: side ribbon, wrapped windscreen, rear window
    y_ws = NOSE - 0.18 - 0.35
    y_rw = TAIL + 0.25 + 0.3
    parts.append(mesh('Glass_Side', 'PT_Glass_Tinted', P.band(1.18, 2.52, GLASS_OFF, y_min=y_rw, y_max=y_ws)))
    parts.append(mesh('Glass_Windscreen', 'PT_Glass_Tinted', P.band(0.98, 2.42, GLASS_OFF, y_min=y_ws, wrap_front=True)))
    parts.append(mesh('Glass_Rear', 'PT_Glass_Tinted', P.band(1.62, 2.45, GLASS_OFF, y_max=y_rw, wrap_rear=True)))
    doors = [(1, 4.35), (2, 0.0), (3, -4.35)]
    door_w = 1.2
    posts = [y_ws, y_rw, 5.05, 3.05, 1.6, -1.6, -2.9, -3.55, -5.2]
    posts += [yc + s * (door_w / 2 + 0.06) for _, yc in doors for s in (-1, 1)]
    pillars(parts, P, [y for y in posts], 1.18, 2.52, sides=(1,))
    pillars(parts, P, [y_ws, y_rw, 4.3, 2.9, 1.45, 0.0, -1.45, -2.9, -4.3], 1.18, 2.52, sides=(-1,))
    # livery
    parts.append(mesh('Livery_Stripe', 'PT_Paint_BusBlue', P.band(1.10, 1.16, LIVERY_OFF, y_max=y_ws, wrap_rear=True)))
    parts.append(mesh('Livery_Face', 'PT_Paint_BusBlue', P.band(0.55, 0.95, LIVERY_OFF, y_min=y_ws, wrap_front=True)))
    parts.append(mesh('Livery_Roofline', 'PT_Paint_BusBlue', P.band(2.56, 2.62, LIVERY_OFF, y_min=y_rw, y_max=y_ws)))
    for idx, yc in doors:
        door_leaves(parts, idx, 1, HW, yc, door_w, 0.34, 2.50, paint, glass_z=(0.62, 2.38))
    # front end
    parts.append(mesh('Display_Front', 'PT_Display', panel_y(NOSE, -0.85, 0.85, 2.47, 2.76, 0.02, 1)))
    parts.append(text('Display_Front_Text', '42', 0.22, (0.0, NOSE + 0.022, 2.615), '+Y', 'PT_Display_Amber'))
    parts.append(mesh('Bumper_Front', 'PT_Plastic_Grey', box_between(0.30, 0.52, -1.20, 1.20, NOSE - 0.06, NOSE + 0.08)))
    parts.append(mesh('Bumper_Rear', 'PT_Plastic_Grey', box_between(0.30, 0.50, -1.22, 1.22, TAIL - 0.08, TAIL + 0.06)))
    for s, sfx in ((-1, 'L'), (1, 'R')):
        lamp_box(parts, 'Headlight_' + sfx, 'PT_Lamp_White', (s * 0.86, NOSE + 0.005, 0.70), (0.30, 0.03, 0.14))
        lamp_box(parts, 'Indicator_F' + sfx, 'PT_Lamp_Amber', (s * 0.86, NOSE + 0.005, 0.83), (0.30, 0.03, 0.05))
        lamp_box(parts, 'Taillight_' + sfx, 'PT_Lamp_Red', (s * 0.96, TAIL - 0.005, 0.95), (0.14, 0.03, 0.40))
        lamp_box(parts, 'Brake_' + sfx, 'PT_Lamp_Red', (s * 0.96, TAIL - 0.005, 1.28), (0.14, 0.03, 0.14))
        lamp_box(parts, 'Indicator_R' + sfx, 'PT_Lamp_Amber', (s * 0.96, TAIL - 0.005, 0.67), (0.14, 0.03, 0.10))
        # side marker lamps along the skirt
        lamp_box(parts, 'SideMarker_' + sfx, 'PT_Lamp_Amber', (s * (HW + 0.005), 1.6, 0.55), (0.02, 0.1, 0.04))
    plate(parts, 'NumberPlate_Front', (0.0, NOSE + 0.08, 0.41), '+Y')
    plate(parts, 'NumberPlate_Rear', (0.0, TAIL - 0.08, 0.62), '-Y')
    parts.append(mesh('EngineGrille', 'PT_Trim_Black', panel_y(TAIL, -0.70, 0.70, 0.55, 1.05, 0.01, -1)))
    parts.append(mesh('Display_Rear', 'PT_Display', panel_y(TAIL, -0.45, 0.45, 2.50, 2.72, 0.02, -1)))
    parts.append(text('Display_Rear_Text', '42', 0.17, (0.0, TAIL - 0.022, 2.61), '-Y', 'PT_Display_Amber'))
    parts.append(mesh('Display_Side', 'PT_Display', panel_x(HW, 3.1, 3.75, 2.22, 2.44, 0.012, 1)))
    parts.append(text('Display_Side_Text', '42', 0.15, (HW + 0.014, 3.425, 2.33), '+X', 'PT_Display_Amber'))
    # roof: air-conditioning pod and hatches
    parts.append(mesh('Roof_AC', 'PT_Plastic_Grey', rbox((0, 0.6, H + 0.11), (1.9, 2.6, 0.26), 0.05), smooth=True))
    parts.append(mesh('Roof_Hatches', 'PT_Plastic_Grey', merge(box((0, 3.6, H + 0.02), (0.8, 0.8, 0.06)),
                                                               box((0, -3.4, H + 0.02), (0.8, 0.8, 0.06)))))
    # "ram's horn" mirrors hanging in front of the windscreen corners
    for s, sfx in ((-1, 'L'), (1, 'R')):
        pts = [(s * 1.0, NOSE - 0.02, 2.80), (s * 1.26, NOSE + 0.30, 2.72), (s * 1.36, NOSE + 0.40, 2.35)]
        mirror(parts, sfx, pts, (s * 1.36, NOSE + 0.40, 2.14), (0.16, 0.07, 0.36))
    # wheels
    for s, sfx in ((-1, 'L'), (1, 'R')):
        road_wheel(parts, 'Wheel_F' + sfx, (s * 1.05, L_F, R), R, TW, RIM, s)
        road_wheel(parts, 'Wheel_R' + sfx, (s * 0.96, L_R, R), R, TW, RIM, s, dual=0.32)
    parts.append(empty('Socket_DriverEye', (-0.62, NOSE - 1.05, 2.02)))
    parts.append(empty('Socket_CentreOfMass', (0.0, -0.2, 1.05)))
    parts.append(mesh('COL_Body', 'PT_Collision', box_between(0.30, H, -HW, HW, TAIL, NOSE)))
    meta = dict(kind='bus', length=NOSE - TAIL, width=2 * HW, height=H + 0.24, wheelbase=L_F - L_R,
                wheels={'Wheel_FL': R, 'Wheel_FR': R, 'Wheel_RL': R, 'Wheel_RR': R}, arch_r=RA, half_w=HW,
                tyre_out={'F': 1.05 + TW / 2, 'R': 0.96 + 0.16 + TW / 2}, doors=[d for d, _ in doors])
    return finish('PT_Bus_City12', 'Городской автобус 12 м, низкопольный', parts, meta)


# ------------------------------------------------------------------ minibus
def minibus():
    """PT_Minibus_Route: route minibus (marshrutka) on a light-commercial
    chassis: short bonnet, raised roof, sliding door on the right."""
    L_F, L_R = 1.8725, -1.8725                # wheelbase 3.745 m
    NOSE, TAIL = 2.87, -3.83                  # length 6.70 m
    HW, H = 1.035, 2.62                       # width 2.07 m
    R, TW, RIM = 0.364, 0.215, 0.2032         # 215/75 R16C
    RA = 0.44
    paint = 'PT_Paint_MinibusYellow'
    zt_keys = [(TAIL, H), (0.75, H), (1.75, 1.18), (2.25, 1.03), (NOSE, 0.98)]
    rt_keys = [(TAIL, 0.2), (0.75, 0.2), (1.75, 0.1), (NOSE, 0.1)]
    hw_keys = [(TAIL, HW), (1.4, HW), (NOSE, 0.96)]
    P = Profile(TAIL, NOSE, HW, 0.40, H, 0.08, 0.2, rc_front=0.28, rc_rear=0.12, re_front=0.1, re_rear=0.1,
                hw_keys=hw_keys, zt_keys=zt_keys, rt_keys=rt_keys, arches=[(L_F, R, RA), (L_R, R, RA)], step=0.08)
    parts = [mesh('Body', paint, P.shell(), smooth=True)]
    wheelhouse(parts, 'Wheelhouse_F', L_F, RA, 0.64, 0.40, R + RA)
    wheelhouse(parts, 'Wheelhouse_R', L_R, RA, 0.64, 0.40, R + RA)
    # windscreen: a patch of the sloped top surface, lifted along its normal
    y0, y1 = 0.80, 1.70
    rows = []
    ys = [y0 + (y1 - y0) * i / 8 for i in range(9)]
    for y in ys:
        st = P.station(y)
        slope = (P.zt_at(y + 0.01) - P.zt_at(y - 0.01)) / 0.02
        n = (0.0, -slope, 1.0)
        l = math.sqrt(n[1] ** 2 + 1)
        xs = st.hw - st.rt - 0.03
        rows.append([(xs * (2 * k / 8 - 1), y + n[1] / l * GLASS_OFF, st.zt + GLASS_OFF / l) for k in range(9)])
    ws = orient_to(grid(rows), lambda c: (0.0, 1.44, 1.0))
    parts.append(mesh('Glass_Windscreen', 'PT_Glass_Tinted', ws))
    # side glazing
    for s, sfx in ((-1, 'L'), (1, 'R')):
        x = s * HW
        cab = [(0.80, 1.30), (1.45, 1.30), (1.45, P.zt_at(1.45) - 0.18), (0.80, P.zt_at(0.80) - 0.22)]
        parts.append(mesh('Glass_Cab_' + sfx, 'PT_Glass_Tinted', side_poly(cab, x, GLASS_OFF, s)))
        panes = [(-3.55, -2.45), (-2.35, -1.25), (-1.15, -0.62), (0.68, 0.72)] if s > 0 else \
                [(-3.55, -2.45), (-2.35, -1.25), (-1.15, -0.05), (0.05, 0.72)]
        vfs = [panel_x(x, a, b, 1.35, 2.25, GLASS_OFF, s) for a, b in panes if b - a > 0.1]
        parts.append(mesh('Glass_Side_' + sfx, 'PT_Glass_Tinted', merge(*vfs)))
        # cab door seam
        parts.append(mesh('DoorSeam_Cab_' + sfx, 'PT_Trim_Black', panel_x(x, 0.72, 0.745, 0.45, 2.30, 0.006, s)))
    # sliding passenger door (translates along -Y from Door_Slide_Pivot)
    piv = 'Door_Slide_Pivot'
    yd0, yd1 = -0.58, 0.66
    px = HW + 0.012
    parts.append(mesh('Door_Slide_Seal', 'PT_Trim_Black', panel_x(HW, yd0 - 0.02, yd1 + 0.02, 0.42, 2.32, 0.008, 1)))
    parts.append(empty(piv, (px, yd1, 0.0)))
    leaf = box_between(0.44, 2.30, px, px + 0.03, yd0, yd1)
    parts.append(mesh('Door_Slide', paint, ([(a - px, b - yd1, c) for a, b, c in leaf[0]], leaf[1]), parent=piv))
    g = box_between(1.35, 2.20, px + 0.03, px + 0.034, yd0 + 0.08, yd1 - 0.08)
    parts.append(mesh('Door_Slide_Glass', 'PT_Glass_Tinted', ([(a - px, b - yd1, c) for a, b, c in g[0]], g[1]), parent=piv))
    parts.append(mesh('Door_Slide_Rail', 'PT_Trim_Black', panel_x(HW, -3.2, yd1, 1.28, 1.31, 0.012, 1)))
    # rear end
    parts.append(mesh('Glass_Rear', 'PT_Glass_Tinted', panel_y(TAIL, -0.78, 0.78, 1.50, 2.25, GLASS_OFF, -1)))
    parts.append(mesh('RearDoorSeam', 'PT_Trim_Black', panel_y(TAIL, -0.012, 0.012, 0.45, 2.40, 0.006, -1)))
    parts.append(mesh('Bumper_Rear', 'PT_Plastic_Black', box_between(0.38, 0.56, -0.98, 0.98, TAIL - 0.07, TAIL + 0.06)))
    for s, sfx in ((-1, 'L'), (1, 'R')):
        lamp_box(parts, 'Taillight_' + sfx, 'PT_Lamp_Red', (s * 0.84, TAIL - 0.005, 0.98), (0.10, 0.03, 0.26))
        lamp_box(parts, 'Brake_' + sfx, 'PT_Lamp_Red', (s * 0.84, TAIL - 0.005, 1.16), (0.10, 0.03, 0.08))
        lamp_box(parts, 'Indicator_R' + sfx, 'PT_Lamp_Amber', (s * 0.84, TAIL - 0.005, 0.80), (0.10, 0.03, 0.08))
    plate(parts, 'NumberPlate_Rear', (0.0, TAIL - 0.075, 0.70), '-Y')
    # front end
    parts.append(mesh('Bumper_Front', 'PT_Plastic_Grey', box_between(0.38, 0.56, -0.95, 0.95, NOSE - 0.14, NOSE + 0.07)))
    parts.append(mesh('Grille', 'PT_Trim_Black', panel_y(NOSE, -0.28, 0.28, 0.57, 0.66, 0.012, 1)))
    for s, sfx in ((-1, 'L'), (1, 'R')):
        lamp_box(parts, 'Headlight_' + sfx, 'PT_Lamp_White', (s * 0.50, NOSE + 0.004, 0.72), (0.26, 0.03, 0.10))
        lamp_box(parts, 'Indicator_F' + sfx, 'PT_Lamp_Amber', (s * 0.50, NOSE + 0.004, 0.64), (0.26, 0.03, 0.04))
        mirror(parts, sfx, [(s * (HW - 0.02), 1.62, 1.32), (s * (HW + 0.14), 1.66, 1.40), (s * (HW + 0.2), 1.70, 1.46)],
               (s * (HW + 0.2), 1.70, 1.46), (0.12, 0.07, 0.26))
    plate(parts, 'NumberPlate_Front', (0.0, NOSE + 0.07, 0.47), '+Y')
    # route boards: behind the windscreen (right corner) and in a side window
    st = P.station(1.62)
    parts.append(mesh('RouteBoard_Front', 'PT_Plate_White', box((0.52, 1.62, st.zt + 0.02), (0.44, 0.03, 0.02))))
    parts.append(text('RouteBoard_Front_Text', '107', 0.14, (0.52, 1.66, st.zt + 0.06), '+Y', 'PT_Ink'))
    parts.append(mesh('RouteBoard_Side', 'PT_Plate_White', panel_x(HW, -1.95, -1.45, 1.95, 2.18, 0.008, 1)))
    parts.append(text('RouteBoard_Side_Text', '107', 0.15, (HW + 0.01, -1.70, 2.065), '+X', 'PT_Ink'))
    # wheels
    for s, sfx in ((-1, 'L'), (1, 'R')):
        road_wheel(parts, 'Wheel_F' + sfx, (s * 0.875, L_F, R), R, TW, RIM, s)
        road_wheel(parts, 'Wheel_R' + sfx, (s * 0.875, L_R, R), R, TW, RIM, s)
    parts.append(empty('Socket_DriverEye', (-0.45, 1.00, 1.86)))
    parts.append(empty('Socket_CentreOfMass', (0.0, -0.1, 0.85)))
    parts.append(mesh('COL_Body', 'PT_Collision', box_between(0.40, H, -HW, HW, TAIL, NOSE)))
    meta = dict(kind='minibus', length=NOSE - TAIL, width=2 * HW, height=H, wheelbase=L_F - L_R,
                wheels={'Wheel_FL': R, 'Wheel_FR': R, 'Wheel_RL': R, 'Wheel_RR': R}, arch_r=RA, half_w=P.hw_at(L_F),
                tyre_out={'F': 0.875 + TW / 2, 'R': 0.875 + TW / 2}, doors=['Slide'])
    return finish('PT_Minibus_Route', 'Маршрутное такси (микроавтобус)', parts, meta)


# ------------------------------------------------------------------ tram
RAIL_C = 0.7975          # rail head centre: 1520 mm gauge + 75 mm head (as the railway kit)
TRAM_WHEEL_R = 0.355
TRAM_FLANGE = 0.025


def tram():
    """PT_Tram_City: single-section high-floor city tram on two bogies,
    one-ended (driver's cab in front), three double doors on the right."""
    NOSE, TAIL = 7.65, -7.65                  # length 15.3 m
    HW, H = 1.25, 3.25                        # width 2.5 m
    BOGIE = 3.75                              # bogie centres, base 7.5 m
    AXLE = 0.97                               # half bogie wheelbase
    R = TRAM_WHEEL_R
    paint = 'PT_Paint_TramCream'
    P = Profile(TAIL, NOSE, HW, 0.78, H, 0.08, 0.30, rc_front=0.9, rc_rear=0.9, re_front=0.25, re_rear=0.25, step=0.15,
                n_end=10)
    parts = [mesh('Body', paint, P.shell(), smooth=True)]
    y_ws, y_rw = 6.3, -6.4
    parts.append(mesh('Glass_Side', 'PT_Glass_Tinted', P.band(1.32, 2.50, GLASS_OFF, y_min=y_rw, y_max=y_ws)))
    parts.append(mesh('Glass_Windscreen', 'PT_Glass_Tinted', P.band(1.22, 2.35, GLASS_OFF, y_min=y_ws, wrap_front=True)))
    parts.append(mesh('Glass_Rear', 'PT_Glass_Tinted', P.band(1.40, 2.45, GLASS_OFF, y_max=y_rw, wrap_rear=True)))
    doors = [(1, 5.5), (2, 0.0), (3, -5.5)]
    door_w = 1.3
    posts = [y_ws, y_rw, 3.9, 2.4, 1.1, -1.1, -2.4, -3.9]
    posts += [yc + s * (door_w / 2 + 0.06) for _, yc in doors for s in (-1, 1)]
    pillars(parts, P, posts, 1.32, 2.50, sides=(1,))
    pillars(parts, P, [y_ws, y_rw, 5.0, 3.7, 2.4, 1.1, -0.2, -1.5, -2.8, -4.1, -5.4], 1.32, 2.50, sides=(-1,))
    parts.append(mesh('Livery_Skirt', 'PT_Paint_TramRed', P.band(0.87, 1.25, LIVERY_OFF, wrap_front=True, wrap_rear=True)))
    parts.append(mesh('Livery_Roofline', 'PT_Paint_TramRed', P.band(2.58, 2.72, LIVERY_OFF, y_min=y_rw, y_max=y_ws)))
    parts.append(mesh('Bumper_Front', 'PT_Plastic_Black', P.band(0.87, 0.95, 0.012, y_min=6.0, wrap_front=True)))
    for idx, yc in doors:
        door_leaves(parts, idx, 1, HW, yc, door_w, 0.80, 2.62, paint, glass_z=(1.0, 2.48))
        parts.append(mesh(f'Door_{idx}_Step', 'PT_Steel_Dark', box_between(0.40, 0.78, HW - 0.28, HW - 0.02,
                                                                             yc - door_w / 2, yc + door_w / 2)))
    # front: route display, lamps
    parts.append(mesh('Display_Front', 'PT_Display', panel_y(NOSE, -0.33, 0.33, 2.40, 2.64, 0.02, 1)))
    parts.append(text('Display_Front_Text', '7', 0.2, (0.0, NOSE + 0.022, 2.52), '+Y', 'PT_Display_Amber'))
    for s, sfx in ((-1, 'L'), (1, 'R')):
        x, y = skin_point(P, 0.72, 'front')
        lamp_box(parts, 'Headlight_' + sfx, 'PT_Lamp_White', (s * x, y, 1.02), (0.22, 0.10, 0.12))
        x, y = skin_point(P, 0.95, 'front')
        lamp_box(parts, 'Indicator_F' + sfx, 'PT_Lamp_Amber', (s * x, y, 1.02), (0.10, 0.12, 0.10))
        x, y = skin_point(P, 0.72, 'rear')
        lamp_box(parts, 'Taillight_' + sfx, 'PT_Lamp_Red', (s * x, y, 1.05), (0.20, 0.10, 0.12))
        lamp_box(parts, 'Brake_' + sfx, 'PT_Lamp_Red', (s * x, y, 1.20), (0.20, 0.10, 0.08))
        x, y = skin_point(P, 0.95, 'rear')
        lamp_box(parts, 'Indicator_R' + sfx, 'PT_Lamp_Amber', (s * x, y, 1.05), (0.10, 0.12, 0.10))
        mirror(parts, sfx, [(s * (HW - 0.02), 6.2, 2.25), (s * (HW + 0.22), 6.35, 2.25), (s * (HW + 0.26), 6.45, 2.1)],
               (s * (HW + 0.26), 6.45, 1.95), (0.14, 0.07, 0.32))
    # towing coupler under the rear overhang: bracket from the underframe
    parts.append(mesh('Coupler_Rear', 'PT_Steel_Dark', merge(box((0, TAIL + 0.55, 0.74), (0.5, 0.3, 0.08)),
                                                            box((0, TAIL + 0.05, 0.66), (0.16, 1.1, 0.12)),
                                                            box((0, TAIL - 0.52, 0.66), (0.26, 0.06, 0.22)))))
    parts.append(mesh('Display_Side', 'PT_Display', panel_x(HW, 4.2, 4.9, 2.30, 2.50, 0.012, 1)))
    parts.append(text('Display_Side_Text', '7', 0.14, (HW + 0.014, 4.55, 2.40), '+X', 'PT_Display_Amber'))
    # roof equipment
    top = H
    parts.append(mesh('Roof_Equipment', 'PT_Plastic_Grey', merge(
        rbox((0, 4.4, top + 0.1), (1.5, 2.2, 0.24), 0.04), rbox((0, -3.6, top + 0.1), (1.5, 3.0, 0.24), 0.04)), smooth=True))
    # pantograph (lowered): Pantograph_Pivot, the frame rotates about local X
    base_y, base_z = 1.1, top + 0.08
    parts.append(mesh('Pantograph_Base', 'PT_Steel_Dark', merge(
        box((0, base_y, top + 0.06), (1.1, 1.2, 0.06)),
        *[cylinder((sx * 0.45, base_y + sy * 0.5, top), (sx * 0.45, base_y + sy * 0.5, top + 0.05), 0.04, 10)
          for sx in (-1, 1) for sy in (-1, 1)])))
    piv = 'Pantograph_Pivot'
    parts.append(empty(piv, (0, base_y - 0.45, base_z)))
    knee = (0.0, 1.35, 0.22)
    head = (0.0, -0.35, 0.36)
    arms = merge(tube([(-0.28, 0.0, 0.0), (-0.05, knee[1], knee[2])], 0.025, 8),
                 tube([(0.28, 0.0, 0.0), (0.05, knee[1], knee[2])], 0.025, 8),
                 tube([knee, head], 0.022, 8))
    parts.append(mesh('Pantograph_Arms', 'PT_Steel_Dark', arms, parent=piv, smooth=True))
    parts.append(mesh('Pantograph_Head', 'PT_Steel_Bright', merge(
        box((head[0], head[1], head[2] + 0.03), (1.3, 0.06, 0.04)),
        tube([(-0.65, head[1], head[2] + 0.03), (-0.85, head[1], head[2] - 0.08)], 0.015, 6),
        tube([(0.65, head[1], head[2] + 0.03), (0.85, head[1], head[2] - 0.08)], 0.015, 6)), parent=piv))
    # bogies: Bogie_F/R_Pivot (vertical axis at the bogie centre, on the rail
    # top), each carrying two wheelsets Wheelset_F1.. (axle = local X)
    for bname, by in (('F', BOGIE), ('R', -BOGIE)):
        bp = f'Bogie_{bname}_Pivot'
        parts.append(empty(bp, (0, by, 0.0)))
        frame = merge(box((-0.98, 0, 0.42), (0.12, 2.7, 0.2)), box((0.98, 0, 0.42), (0.12, 2.7, 0.2)),
                      box((0, 0, 0.48), (1.96, 0.36, 0.16)), box((0, 0, 0.64), (1.6, 0.5, 0.12)))
        parts.append(mesh(f'Bogie_{bname}_Frame', 'PT_Steel_Dark', frame, parent=bp))
        parts.append(mesh(f'Bogie_{bname}_Drive', 'PT_Plastic_Black',
                          merge(box((-0.35, 0, 0.36), (0.45, 0.8, 0.3)), box((0.35, 0, 0.36), (0.45, 0.8, 0.3))), parent=bp))
        springs = merge(*[cylinder((sx * 0.98, sy * 0.97, 0.52), (sx * 0.98, sy * 0.97, 0.7), 0.07, 10)
                          for sx in (-1, 1) for sy in (-1, 1)])
        parts.append(mesh(f'Bogie_{bname}_Springs', 'PT_Rubber', springs, parent=bp))
        for k, ay in ((1, AXLE), (2, -AXLE)):
            ws = f'Wheelset_{bname}{k}'
            parts.append(empty(ws, (0, ay, R), parent=bp))
            wheels = merge(flanged_wheel(R, 0.13, TRAM_FLANGE, -1, (RAIL_C, 0, 0)),
                           flanged_wheel(R, 0.13, TRAM_FLANGE, 1, (-RAIL_C, 0, 0)),
                           cylinder((-0.72, 0, 0), (0.72, 0, 0), 0.075, 12))
            parts.append(mesh(ws + '_Wheels', 'PT_Steel_Wheel', wheels, parent=ws, smooth=True))
    parts.append(empty('Socket_DriverEye', (-0.25, 6.75, 2.25)))
    parts.append(empty('Socket_CentreOfMass', (0.0, 0.0, 1.2)))
    parts.append(mesh('COL_Body', 'PT_Collision', box_between(0.78, H, -HW, HW, TAIL, NOSE)))
    meta = dict(kind='tram', length=NOSE - TAIL, width=2 * HW, height=H + 0.5, bogie_base=2 * BOGIE,
                wheel_r=R, rail_c=RAIL_C, flange=TRAM_FLANGE, half_w=HW, doors=[d for d, _ in doors])
    return finish('PT_Tram_City', 'Трамвай односекционный', parts, meta)


ALL = (bus, minibus, tram)
