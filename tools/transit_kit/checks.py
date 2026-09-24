"""Automated geometry checks of the transit kit (pure Python, no bpy).

Every check returns dict(id, status PASS/FAIL, value, target). They run on the
assembled parts in the Blender frame (front -Y), the same data that is
exported, and are written to artifacts/reports/transit-manifest.json.
"""
import math
from vehicle_kit.checks import world_parts

TOL = 0.002


def _verts(W, include=lambda n: True):
    return [v for n, ms in W.items() if include(n) for V, F in ms for v in V]


def _bounds(pts):
    return [min(p[i] for p in pts) for i in range(3)], [max(p[i] for p in pts) for i in range(3)]


def _world(parts, by, name):
    """World position of an empty (parents only translate in this kit)."""
    p = by[name]
    x, y, z = p.loc
    while p.parent:
        p = by[p.parent]
        x, y, z = x + p.loc[0], y + p.loc[1], z + p.loc[2]
    return x, y, z


def run(parts, meta):
    W, by = world_parts(parts)
    res = []

    def check(cid, ok, value, target):
        res.append(dict(id=cid, status='PASS' if ok else 'FAIL', value=value, target=target))

    visual = _verts(W, lambda n: not n.startswith('COL_'))
    lo, hi = _bounds(visual)
    root = parts[0]
    check('Корень в начале координат, без поворота', root.loc == (0, 0, 0) and root.rot == (0, 0, 0),
          [root.loc, root.rot], 'loc 0, rot 0')
    # Blender standard: front towards -Y, up +Z (Socket_Front / Socket_Up)
    f, u = _world(parts, by, 'Socket_Front'), _world(parts, by, 'Socket_Up')
    check('Лицевая сторона -Y (Socket_Front)', max(abs(a - b) for a, b in zip(f, (0, -1, 0))) < 1e-6, f, '(0,-1,0)')
    check('Верх +Z (Socket_Up)', max(abs(a - b) for a, b in zip(u, (0, 0, 1))) < 1e-6, u, '(0,0,1)')
    kind = meta['kind']
    if kind in ('bus', 'minibus', 'tram'):
        body = _verts(W, lambda n: n in ('Body',))
        blo, bhi = _bounds(body)
        check('Длина кузова, м', abs((bhi[1] - blo[1]) - meta['length']) < 0.01, round(bhi[1] - blo[1], 3), meta['length'])
        check('Ширина кузова, м', abs((bhi[0] - blo[0]) - meta['width']) < 0.01, round(bhi[0] - blo[0], 3), meta['width'])
        check('Габаритная высота, м', abs(hi[2] - meta['height']) < 0.06, round(hi[2], 3), f"{meta['height']} +-0.06")
        check('Ниже земли ничего нет (кроме реборд)', lo[2] >= -(meta.get('flange', 0) + TOL), round(lo[2], 4),
              '>= 0' if kind != 'tram' else f">= -{meta['flange']}")
        check('Корпус симметричен по X', abs(blo[0] + bhi[0]) < 0.005, round(blo[0] + bhi[0], 4), '0')
        axes = ('Wheel_FL', 'Wheel_RL') if kind != 'tram' else ('Bogie_F_Pivot', 'Bogie_R_Pivot')
        ya, yb = _world(parts, by, axes[0])[1], _world(parts, by, axes[1])[1]
        check('Начало координат посередине базы', abs(ya + yb) < 1e-6, round((ya + yb) / 2, 4), '0')
        check('Мотор/кабина спереди: нос по -Y', _verts(W, lambda n: n == 'Headlight_L')[0][1] < 0,
              round(_verts(W, lambda n: n == 'Headlight_L')[0][1], 3), '< 0')
        # driver on the left (right-hand traffic): Blender -Y front => left is +X
        eye = _world(parts, by, 'Socket_DriverEye')
        check('Водитель слева по ходу (Blender +X)', eye[0] > 0.1 or kind == 'tram', round(eye[0], 3),
              '> 0 (трамвай: у оси)')
        doors = [p.name for p in parts if p.name.startswith('Door_') and p.name.endswith('_Pivot')]
        side_ok = all(_world(parts, by, d)[0] < 0 for d in doors)
        check('Двери справа по ходу (Blender -X)', bool(doors) and side_ok, len(doors), 'все пивоты дверей при x < 0')
    if kind in ('bus', 'minibus'):
        for name, R in meta['wheels'].items():
            c = _world(parts, by, name)
            check(f'{name}: центр на высоте радиуса', abs(c[2] - R) < TOL, round(c[2], 4), R)
            tyre = _verts(W, lambda n, name=name: n.startswith(name + '_Tyre'))
            tl, th = _bounds(tyre)
            check(f'{name}: шина касается земли', abs(tl[2]) < 0.005, round(tl[2], 4), '0 +-0.005')
        for ax, out in meta['tyre_out'].items():
            check(f'Шина ({ax}) не выступает за кузов, м', out <= meta['half_w'] + 0.005, round(out - meta['half_w'], 3), '<= +0.005')
        gap = meta['arch_r'] - max(meta['wheels'].values())
        check('Зазор шина - арка, м', gap >= 0.06, round(gap, 3), '>= 0.06')
        if 'windscreen_rake' in meta:
            # route minibuses in Russia have a near-upright windscreen and a
            # short bonnet; a raked 'van wedge' front is a regression
            check('Наклон лобового стекла от вертикали, град', meta['windscreen_rake'] <= 30,
                  round(meta['windscreen_rake'], 1), '<= 30')
            check('Высота лобового стекла, м', meta['windscreen_h'] >= 1.0, round(meta['windscreen_h'], 2), '>= 1.0')
        for n in ('Wheel_FL', 'Wheel_FR', 'Wheel_RL', 'Wheel_RR', 'Socket_DriverEye', 'MirrorSurface_L', 'MirrorSurface_R',
                  'Socket_CentreOfMass', 'COL_Body'):
            check(f'Имя-контракт {n}', n in by, n in by, 'есть')
        # wheel naming vs side: Blender front -Y => vehicle left is +X
        fl = _world(parts, by, 'Wheel_FL')
        check('Wheel_FL: левое переднее (x>0, y<0 в Blender)', fl[0] > 0 and fl[1] < 0, [round(fl[0], 3), round(fl[1], 3)],
              'x > 0, y < 0')
    if kind == 'tram':
        sets = [p.name for p in parts if p.name.startswith('Wheelset_') and p.kind == 'empty']
        check('Четыре колёсные пары', len(sets) == 4, len(sets), 4)
        for s in sets:
            c = _world(parts, by, s)
            check(f'{s}: ось на высоте радиуса', abs(c[2] - meta['wheel_r']) < TOL, round(c[2], 4), meta['wheel_r'])
        wheels = _verts(W, lambda n: n.startswith('Wheelset_'))
        # tread contact: the widest radius point near the tread centre sits on the rail head centre
        tread = [v for v in wheels if abs(abs(v[0]) - meta['rail_c']) < 0.03]
        tz = min(v[2] for v in tread)
        check('Колесо стоит на головке рельса (z=0 у RAIL_C)', abs(tz) < 0.005, round(tz, 4), '0 +-0.005')
        # bogie swing in the R25 curve of the track kit: the turned bogie must
        # stay inside its opening in the skirts and under the well ceiling
        ang = math.asin(meta['bogie_y'] / 25.0)
        for b in ('F', 'R'):
            bx, by_, bz = _world(parts, by, f'Bogie_{b}_Pivot')
            pts = [(x - bx, y - by_, z) for n, ms in W.items() if n.startswith((f'Bogie_{b}_', 'Wheelset_' + b))
                   for V, F in ms for x, y, z in V]
            worst_y, worst_z = 0.0, 0.0
            for sgn in (-1, 1):
                c, s_ = math.cos(sgn * ang), math.sin(sgn * ang)
                for x, y, z in pts:
                    worst_y = max(worst_y, abs(x * s_ + y * c))
                    worst_z = max(worst_z, z)
            check(f'Тележка {b} при повороте {math.degrees(ang):.1f}° в проёме фартука, м',
                  worst_y <= meta['well_half'] - 0.03, round(meta['well_half'] - worst_y, 3), '>= 0.03 до края проёма')
            check(f'Тележка {b} ниже потолка ниши, м', worst_z <= meta['well_top'] - 0.02,
                  round(meta['well_top'] - worst_z, 3), '>= 0.02')
        # clearance between each door span and each bogie opening (negative = overlap)
        wells = [(sy * meta['bogie_y'] - meta['well_half'], sy * meta['bogie_y'] + meta['well_half']) for sy in (-1, 1)]
        gap = min(max(lo - d1, d0 - hi) for d0, d1 in meta['door_spans'] for lo, hi in wells)
        check('Двери не заходят на проёмы тележек, м', gap >= 0.01, round(gap, 3), '>= 0.01')
        for n in ('Bogie_F_Pivot', 'Bogie_R_Pivot', 'Pantograph_Pivot', 'Socket_DriverEye', 'MirrorSurface_L',
                  'MirrorSurface_R', 'COL_Body'):
            check(f'Имя-контракт {n}', n in by, n in by, 'есть')
    if kind in ('track', 'track_curve'):
        rails = _verts(W, lambda n: n.startswith('Rail'))
        rtop = max(v[2] for v in rails)
        check('Рельсы не выше дороги', abs(rtop) < 1e-6, round(rtop, 4), '0')
        s_end = _world(parts, by, 'Socket_End')
        if kind == 'track':
            check('Socket_End на -Y длины модуля', abs(s_end[1] + meta['length']) < 1e-6, s_end, (0, -meta['length'], 0))
            heads = sorted({round((v[0]), 4) for v in _verts(W, lambda n: n in ('Rail_Heads', 'Rails'))})
            # rail head centres from the geometry: pair up the head edges
            centres = meta['rails']
            for rc in centres:
                near = [x for x in heads if abs(x - (-rc)) < 0.05]
                ok = near and abs((min(near) + max(near)) / 2 - (-rc)) < 0.03
                check(f'Рельс у x={-rc:+.4f}', bool(ok), [min(near), max(near)] if near else None, 'головка вокруг центра')
            gauge = min(abs(a - b) for a in centres for b in centres if a < b)
            check('Ширина колеи по центрам головок, м', abs(gauge - 2 * 0.7975) < 1e-6, round(gauge, 4), '1.595 (1520 + 75)')
            if 'groove_depth' in meta:
                check('Желоб глубже реборды трамвая, м', meta['groove_depth'] > 0.025 + 0.01, meta['groove_depth'], '> 0.035')
    if kind == 'sign':
        check('Высота стойки, м', abs(hi[2] - meta['height']) < 0.01, round(hi[2], 3), meta['height'])
        face = _verts(W, lambda n: n == 'Face_Blue')
        check('Щит смотрит в -Y (к водителю)', max(v[1] for v in face) < 0, round(max(v[1] for v in face), 3), '< 0')
    if kind == 'platform':
        check('Высота площадки, м', abs(max(v[2] for v in _verts(W, lambda n: n == 'Platform_Top')) - meta['height']) < 1e-6,
              meta['height'], meta['height'])
        check('Длина площадки, м', abs((hi[1] - lo[1]) - meta['length']) < 0.01, round(hi[1] - lo[1], 3), meta['length'])
    if kind == 'marking':
        check('Разметка лежит на 6..9 мм', abs(lo[2] - 0.006) < 1e-6 and abs(hi[2] - 0.009) < 1e-6, [lo[2], hi[2]], '0.006..0.009')
    return res
