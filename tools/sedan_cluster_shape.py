"""Instrument cluster, steering column and the steering-wheel tilt of the player sedan DS_Sedan_A (T72), pure Python.

Used by tools/build_sedan_cluster.py (Blender) and by offline previews. Coordinates: Blender, metres, X right, Y forward,
Z up (the sedan's FBX keeps them: axis_forward='Y', axis_up='Z').

Why (driver feedback 01.10): the dials were Ø 196 mm each, 47 cm across together — wider than the steering wheel —
while the crossover's were half hidden by the dash. Real clusters: Ø 95…110 mm dials ≈ 180 mm apart, seen through the
upper half of the wheel. The sedan's wheel also stood vertical on a horizontal column that ran into the middle of the
cluster; now the wheel leans 20° (top forward, as in a real car) and the column goes down into the knee panel.
"""
import math
from vehicle_kit.geom import Part, superellipsoid, cylinder, tube, merge, transform
from vehicle_kit.interior import cluster_pod

# measured on the current model (artifacts: Socket_DriverEye, SteeringWheel_Pivot, dash profile of build_sedan_interior)
EYE = (-0.38, -0.19, 1.25)
WHEEL_PIVOT = (-0.38, 0.28, 0.90)
WHEEL_TILT_DEG = 20.0          # rim plane leans back from vertical, top away from the driver (kit cars: 23-25 deg)
CLUSTER = dict(cx=-0.38, y_face=0.50, cz=0.925, top=0.995, r=0.0525, gap=0.068, style='classic')

# old objects replaced by this script (any type: meshes, text curves); Needle_* empties are kept and moved
REPLACED = ('GaugeFace', 'GaugeBezel', 'GaugeTick', 'GaugeNumber', 'GaugeUnit', 'NeedleBlade', 'NeedleHub',
            'Instrument_Hood', 'Cluster_Display', 'Cluster_Panel', 'Gear_Display', 'Odometer', 'SteeringColumn',
            'SteeringShroud', 'Stalk')


def column_parts():
    """Column, shroud and the two stalks along the tilted column axis (local +Y of the steering pivot)."""
    t = math.radians(WHEEL_TILT_DEG)
    rot = (-t, 0, 0)
    out = []
    shroud = superellipsoid((0, 0.175, -0.012), (0.058, 0.115, 0.048), 0.38, 20, 8)
    out.append(Part('SteeringShroud', material='Interior_Graphite').add(transform(shroud, rot, WHEEL_PIVOT)))
    col = cylinder((0, 0.05, 0), (0, 0.42, 0), 0.032, 16)
    out.append(Part('SteeringColumn', material='Rubber').add(transform(col, rot, WHEEL_PIVOT)))
    stalks = []
    for s in (-1, 1):
        stalks.append(tube([(s * 0.05, 0.095, 0.004), (s * 0.12, 0.088, 0.012), (s * 0.168, 0.084, 0.018)], 0.0065, 8))
        stalks.append(superellipsoid((s * 0.175, 0.084, 0.019), (0.018, 0.011, 0.011), 0.5, 10, 6))
    out.append(Part('Stalks', material='Interior_Graphite').add(transform(merge(*stalks), rot, WHEEL_PIVOT)))
    return out


def build_parts():
    parts = []
    c = CLUSTER
    cluster_pod(parts, c['cx'], c['y_face'], c['cz'], c['top'], c['r'], c['gap'], c['style'])
    parts += column_parts()
    return parts
