"""Per-asset checks written to the manifest (PASS / FAIL)."""
import math
import re

BUDGET = {'tree': (12000, 1800), 'bush': (6000, 900), 'prop': (8000, None), 'road': (24000, None),
          'barrier': (8000, None), 'sign': (3000, None), 'yard': (12000, None), 'parking': (16000, None)}


def r(id_, ok, value=None, target=None):
    return dict(id=id_, status='PASS' if ok else 'FAIL', value=value, target=target)


def run(a, src):
    out = []
    root = a.root
    out.append(r('Корень в начале координат без поворота и масштаба',
                 root.location.length < 1e-6 and max(abs(x) for x in root.rotation_euler) < 1e-6
                 and all(abs(s - 1) < 1e-6 for s in root.scale)))
    min_z = a.meta.get('min_z', -0.1)
    out.append(r('Ничего не уходит под землю глубже допуска', src['min'][2] >= min_z - 1e-4,
                 src['min'][2], f'>= {min_z}'))
    lod0, lod_last = BUDGET.get(a.kind, (12000, None))
    lod0 = a.meta.get('budget', lod0)
    out.append(r('Бюджет треугольников LOD0', src['triangles'] <= lod0, src['triangles'], f'<= {lod0}'))
    lt = src['lod_triangles']
    if lt:
        seq = [lt[k] for k in sorted(lt)]
        out.append(r('LOD: каждый следующий уровень легче (≤ 60 %)',
                     all(seq[i + 1] <= 0.6 * seq[i] for i in range(len(seq) - 1)), seq))
        if lod_last and len(seq) >= 3:
            out.append(r('Бюджет последнего LOD', seq[-1] <= lod_last, seq[-1], f'<= {lod_last}'))
    for name, (lo, hi) in a.meta.get('expect_size', {}).items():
        ax = 'xyz'.index(name)
        v = src['size'][ax]
        out.append(r(f'Габарит по {name}', lo - 1e-3 <= v <= hi + 1e-3, v, f'{lo}..{hi}'))
    names = {re.sub(r'\.\d{3}$', '', o.name): o for o in root.children_recursive}
    for s, pos in a.meta.get('sockets', {}).items():
        o = names.get(s)
        ok = o is not None and all(abs(x - y) < 2e-3 for x, y in zip(o.matrix_world.translation, pos))
        out.append(r(f'Сокет {s} на месте', ok, list(o.matrix_world.translation) if o else None, pos))
    for need in a.meta.get('required', []):
        out.append(r(f'Есть объект {need}', need in names))
    if a.meta.get('collider', True):
        out.append(r('Есть коллизия COL_*', any(n.startswith('COL_') for n in names)))
    return out
