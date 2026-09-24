"""Vehicle definitions. Original designs: proportions and dimensions follow real
vehicle classes, no real model, brand, badge or signature feature is copied.

Axes: X right, Y forward (+Y nose), Z up, metres; ground at Z = 0.
"""
import copy
import math


def tyre_radius(width_mm, aspect, rim_in):
    return (rim_in * 25.4 / 2 + width_mm * aspect / 100) / 1000


# --------------------------------------------------------------------------
# DS_Sedan_A - budget C-segment saloon, 2000s: upright glasshouse, one sharp
# shoulder crease, big wrap-round lamps, bumper ledges.
# 4.40 x 1.74 x 1.49 m, wheelbase 2.60 m, 185/65 R15.
# --------------------------------------------------------------------------
_r = tyre_radius(185, 65, 15)
SEDAN = dict(
    id='DS_Sedan_A', title='Седан (учебный, 2000-е)', paint='Paint_Atlantic', player=True,
    interior='classic',
    nose_y=2.20, tail_y=-2.20, half_w=0.87,
    rc_front=0.20, rc_rear=0.14, bow_front=0.05, bow_rear=0.025,
    taper_front=0.03, taper_start=1.2, taper_rear=0.015, taper_rear_start=-1.3,
    wheel_y=(1.30, -1.30), wheel_x=0.745, wheel_z=_r, tire_r=_r, tire_w=0.185,
    wheel=dict(style='alloy', rim_r=15 * 0.0254 / 2 + 0.01, spokes=7, spoke_w=0.04, bolts=4),
    arch_r=_r + 0.075, arch_corner=0.05, arch_lip=0.006,
    sill_z=0.20, nose_bottom=0.22, tail_bottom=0.26,
    top_keys=[(-2.20, 0.865), (-1.95, 0.9), (-1.5, 0.925), (-0.2, 0.915), (1.03, 0.895), (1.6, 0.865), (2.0, 0.82), (2.2, 0.775)],
    side_keys=[(0.18, -0.07), (0.22, -0.046), (0.27, -0.02), (0.31, -0.009), (0.33, -0.006), (0.50, -0.002),
               (0.69, 0.0), (0.705, -0.004), (0.72, -0.013), (0.80, -0.022), (0.94, -0.042)],
    front_keys=[(0.20, -0.075), (0.24, -0.035), (0.29, -0.01), (0.31, 0.0), (0.50, 0.0), (0.52, -0.016), (0.56, -0.02),
                (0.72, -0.032), (0.745, -0.05), (0.78, -0.085)],
    rear_keys=[(0.24, -0.07), (0.28, -0.025), (0.32, -0.004), (0.34, 0.0), (0.53, 0.0), (0.55, -0.014), (0.82, -0.02),
               (0.87, -0.04)],
    bumper_seams=[{'front': 0.515, 'rear': 0.545}],
    cowl_y=1.03, deck_y=-1.50,
    hood_crown=0.03, hood_creases=[(0.5, 0.006, 0.08)], deck_crown=0.022,
    splits=dict(nose=1.90, wing=0.88, b=-0.19, rear_door=-1.10, tail=-1.98),
    panels=[('Fender_Front', 'wing', 'nose', 0), ('Door_Front', 'b', 'wing', 0), ('Door_Rear', 'rear_door', 'b', 0),
            ('Fender_Rear', 'tail', 'rear_door', 0)],
    seams=['nose', 'wing', 'b', 'rear_door', 'tail'],
    window_flange=False,
    gh=dict(
        roof=dict(y_front=0.25, y_rear=-0.95, half_w=0.655, z=1.46, drop_f=0.035, drop_r=0.05, corner_r=0.12,
                  crown=0.03, bow_f=0.05, bow_r=0.02),
        stations=[(-0.19, -0.25)],
        bulge={'F': 0.022, 'S': 0.014, 'B': 0.02},
        windows=[dict(name='Windshield', seg='F', f0=0.04, f1=0.96, t0=0.03, t1=0.95, frame=True),
                 dict(name='Front', seg='S', y0=0.93, y1=-0.13, t0=0.07, t1=0.93),
                 dict(name='Rear', seg='S', y0=-0.25, y1=-1.03, t0=0.07, t1=0.93),
                 dict(name='Quarter', seg='S', y0=-1.07, y1=-1.36, t0=0.1, t1=0.55),
                 dict(name='Rear', seg='B', f0=0.06, f1=0.94, t0=0.05, t1=0.95)],
        black=[dict(seg='S', y0=-0.13, y1=-0.25, t0=0.0, t1=1.0), dict(seg='S', y0=-1.03, y1=-1.07, t0=0.0, t1=0.6)],
        frame=0.015,
    ),
    lamps=dict(
        head=dict(x_in=0.40, y_out=1.93, z_lo=lambda u: 0.6 + 0.02 * u, z_hi=lambda u: 0.735 - 0.02 * u * u, lens_frac=0.8),
        grilles=[dict(name='Grille', x=0.33, z_lo=0.58, z_hi=0.715, bars=4, bar_pitch=0.028, mat='Trim_Black'),
                 dict(name='Intake_Lower', x=0.46, z_lo=0.27, z_hi=0.37, mat='Plastic_Black')],
        fog=[dict(x=0.62, z=0.33, w=0.11, h=0.06)],
        tail=dict(x_in=0.34, y_out=-1.99, z_lo=lambda u: 0.7 + 0.01 * u, z_hi=lambda u: 0.83 - 0.01 * u),
        rear_trims=[dict(name='Rear_Lower', x=0.62, z_lo=0.27, z_hi=0.315)],
        reflectors=[dict(x=0.68, z=0.4, w=0.08)],
    ),
    plates=dict(front=0.44, rear=0.45),
    handles=dict(z=0.815, y=[-0.12, -1.06]),
    mirror=dict(f=0.025, reach=0.11, dz=0.085, w=0.2, h=0.12),
    mouldings=dict(z=0.52, h=0.035, spans=[(0.86, -0.17), (-0.21, -0.96)]),
    cabin=dict(floor_z=0.30, firewall_y=0.80, rear_wall_y=-1.40, tunnel_h=0.12, tunnel_w=0.28,
               driver_x=-0.37, h_y=-0.26, h_z=0.56, recline=0.36, eye_h=0.66,
               rear_h_y=-0.93, rear_h_z=0.53, rear_half_w=0.58, rear_back_h=0.27, rear_recline=0.24,
               parcel_shelf=(-1.24, -1.5, 0.905),
               dash_front=(1.02, 0.925), dash_face_y=0.44, dash_top_z=0.99, wheel_offset=(0, 0.46, 0.36),
               column_tilt=0.4, wheel_r=0.185, pedal_dy=0.86, console_h=0.22, card_top_z=0.885,
               door_cards=[('Front', 0.4, -0.16), ('Rear', -0.22, -0.88)], seat_style='fabric', com_y=-0.05, com_z=0.52),
)

# --------------------------------------------------------------------------
# DS_Crossover_A - compact crossover, 2020s: wedge belt line, floating roof
# (black B/C pillars), slim lamps with DRL strip, black arch cladding.
# 4.36 x 1.80 x 1.66 m, wheelbase 2.67 m, 215/60 R17, clearance ~0.2 m.
# --------------------------------------------------------------------------
_r2 = tyre_radius(215, 60, 17)
CROSSOVER = dict(
    id='DS_Crossover_A', title='Кроссовер (2020-е)', paint='Paint_Terracotta', player=True,
    interior='modern',
    nose_y=2.18, tail_y=-2.18, half_w=0.90,
    rc_front=0.3, rc_rear=0.2, bow_front=0.07, bow_rear=0.04,
    taper_front=0.04, taper_start=1.1, taper_rear=0.02, taper_rear_start=-1.2,
    wheel_y=(1.335, -1.335), wheel_x=0.785, wheel_z=_r2, tire_r=_r2, tire_w=0.215,
    wheel=dict(style='alloy', rim_r=17 * 0.0254 / 2 + 0.01, spokes=5, split=0.09, spoke_w=0.032, bolts=5, mat='Satin_Aluminium'),
    arch_r=_r2 + 0.085, arch_corner=0.05, arch_lip=0.0,
    sill_z=0.27, nose_bottom=0.31, tail_bottom=0.35,
    top_keys=[(-2.18, 1.04), (-2.0, 1.055), (-1.3, 1.05), (-0.2, 1.015), (1.0, 0.985), (1.5, 0.965), (1.9, 0.935), (2.18, 0.9)],
    side_keys=[(0.27, -0.06), (0.31, -0.03), (0.37, -0.012), (0.45, -0.006), (0.56, -0.018), (0.66, -0.006),
               (0.79, 0.0), (0.80, -0.004), (0.815, -0.013), (0.9, -0.022), (1.06, -0.05)],
    front_keys=[(0.3, -0.1), (0.35, -0.05), (0.41, -0.018), (0.46, 0.0), (0.64, 0.0), (0.68, -0.016), (0.8, -0.03),
                (0.86, -0.048), (0.9, -0.08)],
    rear_keys=[(0.34, -0.09), (0.4, -0.03), (0.47, -0.004), (0.5, 0.0), (0.66, 0.0), (0.7, -0.012), (0.9, -0.02),
               (1.04, -0.045)],
    bumper_seams=[{'front': 0.66, 'rear': 0.69}],
    cowl_y=1.02, deck_y=-2.25,
    hood_crown=0.03, hood_creases=[(0.62, 0.01, 0.06)],
    splits=dict(nose=1.86, wing=0.91, b=-0.2, rear_door=-1.12, tail=-1.95),
    panels=[('Fender_Front', 'wing', 'nose', 0), ('Door_Front', 'b', 'wing', 0), ('Door_Rear', 'rear_door', 'b', 0),
            ('Fender_Rear', 'tail', 'rear_door', 0)],
    seams=['nose', 'wing', 'b', 'rear_door', 'tail'],
    window_flange=False,
    gh=dict(
        roof=dict(y_front=0.1, y_rear=-1.97, half_w=0.66, z=1.64, drop_f=0.05, drop_r=0.07, corner_r=0.14,
                  crown=0.025, bow_f=0.06, bow_r=0.03),
        stations=[(-0.2, -0.27), (-1.12, -1.2)],
        bulge={'F': 0.02, 'S': 0.012, 'B': 0.015},
        windows=[dict(name='Windshield', seg='F', f0=0.04, f1=0.96, t0=0.03, t1=0.95),
                 dict(name='Front', seg='S', y0=0.95, y1=-0.15, t0=0.06, t1=0.9),
                 dict(name='Rear', seg='S', y0=-0.25, y1=-1.08, t0=0.06, t1=0.9),
                 dict(name='Quarter', seg='S', y0=-1.16, y1=-1.86, t0=0.08, t1=0.84),
                 dict(name='Tailgate', seg='B', f0=0.08, f1=0.92, t0=0.18, t1=0.93)],
        black=[dict(seg='S', y0=-0.15, y1=-0.25, t0=0.0, t1=1.0), dict(seg='S', y0=-1.08, y1=-1.16, t0=0.0, t1=1.0)],
        frame=0.012,
    ),
    lamps=dict(
        head=dict(x_in=0.5, y_out=1.9, z_lo=lambda u: 0.74 + 0.03 * u, z_hi=lambda u: 0.8 + 0.01 * u, lens_frac=0.84, drl=0.785),
        grilles=[dict(name='Grille', x=0.5, z_lo=0.44, z_hi=lambda u: 0.68 - 0.04 * u * u, bars=7, bar_pitch=0.03,
                      mat='Trim_Black', bar_mat='Satin_Aluminium', bar_r=0.004),
                 dict(name='LightBand', x=0.52, z_lo=0.748, z_hi=0.772, mat='Trim_Black', lift=0.003),
                 dict(name='Skid_Front', x=0.5, z_lo=0.31, z_hi=0.4, mat='Plastic_Grey')],
        fog=[dict(x=0.66, z=0.47, w=0.12, h=0.04)],
        tail=dict(x_in=0.28, y_out=-1.99, z_lo=lambda u: 0.93 - 0.02 * u, z_hi=lambda u: 1.0 - 0.005 * u),
        rear_trims=[dict(name='Skid_Rear', x=0.55, z_lo=0.35, z_hi=0.44, mat='Plastic_Grey')],
        reflectors=[dict(x=0.72, z=0.5, w=0.1)],
    ),
    plates=dict(front=0.56, rear=0.6),
    handles=dict(z=0.93, y=[-0.12, -1.07], mat='Paint_Terracotta'),
    mirror=dict(f=0.03, reach=0.12, dz=0.07, w=0.21, h=0.12, mat='Trim_Black', repeater=True),
    cladding=dict(width=0.07, lift=0.012, sill_h=0.12),
    roof_rails=dict(h=0.05, mat='Trim_Black'),
    cabin=dict(floor_z=0.4, firewall_y=0.8, rear_wall_y=-1.95, tunnel_h=0.1, tunnel_w=0.26,
               driver_x=-0.38, h_y=-0.24, h_z=0.7, recline=0.34, eye_h=0.67,
               rear_h_y=-1.0, rear_h_z=0.68, rear_half_w=0.6, cargo_floor=(-1.3, -2.05, 0.68),
               dash_front=(1.01, 1.02), dash_face_y=0.48, dash_top_z=1.1, wheel_offset=(0, 0.46, 0.35),
               column_tilt=0.44, wheel_r=0.18, pedal_dy=0.8, console_h=0.3, card_top_z=1.0,
               door_cards=[('Front', 0.44, -0.16), ('Rear', -0.23, -0.9)], seat_style='leather', com_y=-0.05, com_z=0.62),
)

# --------------------------------------------------------------------------
# DS_Police_A - the sedan in a police livery: white body, blue stripes, roof
# light bar, steel wheels. Simple interior (traffic vehicle).
# --------------------------------------------------------------------------
POLICE = copy.deepcopy(SEDAN)
POLICE.update(id='DS_Police_A', title='Полиция (на базе седана)', paint='Paint_White', player=False, interior='simple')
POLICE['wheel'] = dict(style='steel', rim_r=15 * 0.0254 / 2 + 0.01, bolts=4, mat='Plastic_Grey', cap_mat='Plastic_Grey')
POLICE['mouldings'] = None
POLICE['livery'] = dict(
    stripes=[dict(name='Stripe_Blue', mat='Paint_PoliceBlue', y0=1.88, y1=-1.96, z_lo=0.43, z_hi=0.6),
             dict(name='Stripe_Thin', mat='Paint_PoliceBlue', y0=1.88, y1=-1.96, z_lo=0.64, z_hi=0.665)],
    texts=[dict(name='Text_Police', body='ПОЛИЦИЯ', size=0.11, y=-0.32, z=0.515, mat='Paint_White')],
)
POLICE['beacons'] = [dict(name='LightBar', y=-0.05, at=(0.5, 1.0), w=1.12, d=0.26, h=0.09,
                          colors=['Lamp_Blue', 'Lamp_Red', 'Lamp_Blue', 'Lamp_Red'], segments=4)]
POLICE['plate'] = dict(mat='Paint_PoliceBlue', text_mat='Paint_White', text='А 0001 77')

# --------------------------------------------------------------------------
# DS_Ambulance_A - high-roof light commercial van (class B ambulance):
# 5.60 x 2.05 x 2.56 m, wheelbase 3.40 m, 185/75 R16C.
# --------------------------------------------------------------------------
_r3 = tyre_radius(185, 75, 16)
AMBULANCE = dict(
    id='DS_Ambulance_A', title='Скорая помощь (фургон)', paint='Paint_White', player=False, interior='simple',
    nose_y=2.6, tail_y=-3.0, half_w=1.025,
    rc_front=0.28, rc_rear=0.08, bow_front=0.1, bow_rear=0.01,
    taper_front=0.05, taper_start=1.8, taper_rear=0.0,
    wheel_y=(1.95, -1.45), wheel_x=0.88, wheel_z=_r3, tire_r=_r3, tire_w=0.185,
    wheel=dict(style='steel', rim_r=16 * 0.0254 / 2 + 0.01, bolts=6, mat='Plastic_Grey', cap_mat='Plastic_Grey'),
    arch_r=_r3 + 0.09, arch_corner=0.06, arch_lip=0.0,
    sill_z=0.34, nose_bottom=0.34, tail_bottom=0.42,
    top_keys=[(-3.0, 1.14), (-1.0, 1.14), (1.6, 1.12), (1.9, 1.09), (2.3, 1.02), (2.6, 0.95)],
    side_keys=[(0.34, -0.05), (0.4, -0.015), (0.46, 0.0), (0.95, 0.0), (1.14, -0.008)],
    front_keys=[(0.34, -0.09), (0.4, -0.04), (0.46, -0.01), (0.5, 0.0), (0.64, 0.0), (0.68, -0.02), (0.88, -0.04), (0.95, -0.08)],
    rear_keys=[(0.42, -0.02), (0.48, 0.0), (1.14, 0.0)],
    bumper_seams=[{'front': 0.66, 'rear': 0.6}],
    cowl_y=1.62, deck_y=-3.2,
    hood_crown=0.03, hood_creases=[],
    splits=dict(nose=2.35, wing=1.45, b=0.66, rear_door=-0.9, tail=-2.94),
    panels=[('Fender_Front', 'wing', 'nose', 0), ('Door_Front', 'b', 'wing', 0), ('Door_Slide', 'rear_door', 'b', 0),
            ('Body_Rear', 'tail', 'rear_door', 0)],
    seams=['nose', 'wing', 'b', 'rear_door'],
    window_flange=False,
    gh=dict(
        roof=dict(y_front=1.05, y_rear=-2.97, half_w=0.935, z=2.56, drop_f=0.015, drop_r=0.01, corner_r=0.08,
                  crown=0.03, bow_f=0.08, bow_r=0.0),
        stations=[(0.66, 0.62)],
        bulge={'F': 0.02, 'S': 0.012, 'B': 0.0},
        nt=16, nS=60,
        windows=[dict(name='Windshield', seg='F', f0=0.04, f1=0.96, t0=0.03, t1=0.6),
                 dict(name='Front', seg='S', y0=1.52, y1=0.7, t0=0.04, t1=0.6),
                 dict(name='Slide', seg='S', y0=0.5, y1=-0.35, t0=0.1, t1=0.42, frosted=True),
                 dict(name='RearDoors', seg='B', f0=0.1, f1=0.9, t0=0.14, t1=0.44, frosted=True)],
        black=[dict(seg='S', y0=0.7, y1=0.62, t0=0.0, t1=0.62), dict(seg='B', f0=0.49, f1=0.51, t0=0.0, t1=0.98)],
        frame=0.012,
    ),
    lamps=dict(
        head=dict(x_in=0.52, y_out=2.3, z_lo=lambda u: 0.78 + 0.02 * u, z_hi=lambda u: 0.9 - 0.01 * u, lens_frac=0.82),
        grilles=[dict(name='Grille', x=0.46, z_lo=0.66, z_hi=0.84, bars=5, bar_pitch=0.03, mat='Trim_Black', bar_mat='Chrome'),
                 dict(name='Bumper_Front_Lower', x=0.95, z_lo=0.35, z_hi=0.64, mat='Plastic_Black', lift=0.012)],
        tail=dict(x_in=0.86, y_out=-2.99, z_lo=lambda u: 0.5, z_hi=lambda u: 0.82),
        rear_trims=[dict(name='Bumper_Rear_Step', x=0.9, z_lo=0.43, z_hi=0.58, mat='Plastic_Black', lift=0.02)],
    ),
    plates=dict(front=0.5, rear=0.63),
    handles=dict(z=1.02, y=[0.78, -0.75], mat='Plastic_Black'),
    mirror=dict(f=0.02, reach=0.2, dz=0.18, w=0.2, h=0.32, mat='Plastic_Black'),
    livery=dict(
        stripes=[dict(name='Stripe_Red_Lower', mat='Paint_AmbulanceRed', y0=2.3, y1=-2.95, z_lo=0.72, z_hi=0.86)],
        gh_stripes=[dict(name='Stripe_Red_Box', mat='Paint_AmbulanceRed', y0=0.62, y1=-2.95, t0=0.46, t1=0.52)],
        texts=[dict(name='Text_Ambulance', body='СКОРАЯ МЕДИЦИНСКАЯ ПОМОЩЬ', size=0.1, gh=True, y=-1.3, t=0.32,
                    mat='Paint_AmbulanceRed'),
               dict(name='Text_03', body='03', size=0.3, gh=True, y=-2.45, t=0.66, mat='Paint_AmbulanceRed')],
    ),
    beacons=[dict(name='LightBar', y=0.85, at=(0.5, 1.0), w=1.2, d=0.24, h=0.09,
                  colors=['Lamp_Blue', 'Lamp_Blue', 'Lamp_Red', 'Lamp_Blue', 'Lamp_Blue'], segments=5),
             dict(name='Beacon_Rear', y=-2.85, at=(0.5, 1.0), w=0.9, d=0.14, h=0.07, colors=['Lamp_Blue'], segments=2)],
    cabin=dict(floor_z=0.52, firewall_y=1.35, rear_wall_y=0.3, tunnel_h=0.05, tunnel_w=0.2,
               driver_x=-0.5, h_y=0.72, h_z=0.94, recline=0.3, eye_h=0.68,
               rear_bench=False, dash_front=(1.56, 1.1), dash_face_y=1.18, dash_top_z=1.3, wheel_offset=(0, 0.42, 0.33),
               column_tilt=0.7, wheel_r=0.2, pedal_dy=0.7, console_h=0.12, card_top_z=1.12,
               door_cards=[('Front', 1.14, 0.72)], seat_style='fabric', com_y=0.1, com_z=0.85, cluster_dz=0.07, dash_inset=0.09,
               ambulance=True),
)
AMBULANCE['plate'] = dict(mat='Paint_White', text_mat='Ink_Dark', text='В 003 МО 77')

ALL = [SEDAN, CROSSOVER, POLICE, AMBULANCE]
