"""Assemble a complete vehicle (list of Parts) from a spec."""
import copy
from .geom import Part, rbox, slab, superellipsoid, merge, cylinder, prism_x
from .body import build_body
from .greenhouse import Greenhouse
from .details import (build_lamps, build_plates, build_handles, build_mirrors, build_wipers, build_cladding,
                      build_mouldings, build_livery, build_beacons, build_roof_rails)
from .wheels import build_wheels
from .interior import Cabin


def build(spec, detail='hi', interior=None):
    """detail: 'hi' (LOD0) or 'lo' (traffic). interior: 'classic' | 'modern' |
    'simple' | None (spec default; traffic LOD always uses 'simple')."""
    S = copy.deepcopy(spec)
    paint = S['paint']
    style = interior or (S['interior'] if detail == 'hi' else 'simple')
    parts = []
    body_parts = []
    sk, hood_rows, deck_rows = build_body(S, body_parts, paint, detail)
    gh = Greenhouse(S, sk, hood_rows, deck_rows, paint, detail)
    out = gh.build(body_parts)
    for w in S['gh']['windows']:
        if w.get('frosted'):
            for (cls, name), pt in out.items():
                if cls == 'glass' and name.startswith(w['name']):
                    pt.material = 'Glass_Frosted'
    build_lamps(S, sk, body_parts, detail)
    pl = S.get('plate', {})
    build_plates(S, sk, body_parts, pl.get('mat', 'Paint_White'), pl.get('text_mat', 'Ink_Dark'), pl.get('text', 'А 001 АА 77'))
    build_handles(S, sk, body_parts, paint)
    build_mirrors(S, gh, body_parts, paint)
    build_wipers(S, gh, body_parts)
    build_cladding(S, sk, body_parts)
    build_mouldings(S, sk, body_parts)
    build_livery(S, sk, gh, body_parts)
    build_beacons(S, gh, body_parts)
    build_roof_rails(S, gh, body_parts)
    build_wheels(S, body_parts, detail)
    cab = Cabin(S, sk, gh, detail)
    cab.build(body_parts, style)
    if S['cabin'].get('ambulance'):
        ambulance_box(S, sk, gh, body_parts)
    root = Part(S['id'], kind='empty')
    root.design = S['title']
    parts.append(root)
    seen = {}
    for p in body_parts:
        if p.parent is None:
            p.parent = S['id']
        if p.name in seen:            # unique object names (parents are unique by construction)
            seen[p.name] += 1
            p.name = f'{p.name}.{seen[p.name]:03d}'
        else:
            seen[p.name] = 0
        parts.append(p)
    return parts, dict(skin=sk, gh=gh, cabin=cab, spec=S, style=style)


def ambulance_box(S, sk, gh, parts):
    """Patient compartment: partition, floor, stretcher, cabinets, seat."""
    C = S['cabin']
    fz = C['floor_z']
    y0, y1 = C['rear_wall_y'], S['tail_y'] + 0.1
    xi = S['half_w'] - 0.09
    top = gh.roof_z(0) - 0.08
    parts.append(Part('Box_Floor', material='Plastic_Grey').add(rbox((0, (y0 + y1) / 2, fz), (2 * xi, y0 - y1, 0.03), 0.005)))
    parts.append(Part('Partition', material='Interior_Light').add(slab((0, y0, (fz + top) / 2), (1, 0, 0), (0, 0, 1), 2 * xi, top - fz, 0.03)))
    parts.append(Part('Stretcher', material='Stretcher_Orange').add(merge(
        rbox((0.28, (y0 + y1) / 2 - 0.2, fz + 0.55), (0.56, 1.9, 0.1), 0.04),
        rbox((0.28, (y0 + y1) / 2 - 0.2, fz + 0.3), (0.5, 1.7, 0.06), 0.01))))
    parts.append(Part('Cabinets', material='Interior_Light').add(merge(
        rbox((-xi + 0.2, (y0 + y1) / 2 + 0.3, top - 0.35), (0.38, 1.6, 0.55), 0.02),
        rbox((-xi + 0.25, (y0 + y1) / 2 - 0.2, fz + 0.3), (0.48, 1.0, 0.6), 0.02))))
    parts.append(Part('Attendant_Seat', material='Seat_Fabric').add(merge(
        rbox((-0.3, y0 - 0.35, fz + 0.45), (0.45, 0.45, 0.1), 0.03),
        rbox((-0.3, y0 - 0.12, fz + 0.8), (0.45, 0.08, 0.6), 0.03))))
    parts.append(Part('Ceiling_Light', material='Lamp_White').add(rbox((0, (y0 + y1) / 2, top - 0.01), (0.3, 1.2, 0.02), 0.01)))
