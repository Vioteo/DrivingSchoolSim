"""Offline self-test of the transit kit geometry (no Blender needed).

    python tools/transit_kit/selftest.py

Builds every asset, runs checks.run and exits 1 on any FAIL. Two deliberately
broken fixtures must FAIL, otherwise the checks prove nothing.
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from transit_kit import vehicles, infra, checks  # noqa: E402


def build_all():
    for fn in vehicles.ALL + infra.ALL:
        yield fn()


def main():
    fails = 0
    total = 0
    for parts, meta in build_all():
        res = checks.run(parts, meta)
        bad = [r for r in res if r['status'] != 'PASS']
        total += len(res)
        fails += len(bad)
        print(f"{'PASS' if not bad else 'FAIL'} {meta['id']}: {len(res) - len(bad)}/{len(res)}")
        for r in bad:
            print('   FAIL', r['id'], r['value'], 'target', r['target'])
    # cross-module: street and lawn tracks share the rail line (AI trams follow sockets)
    def socks(parts):
        return {p.name: p.loc for p in parts if p.name.startswith('Socket_Tram')}
    road = socks(infra.road_tram_urban()[0])
    grass = socks(infra.track_grass()[0])
    seam = all(abs(road[k][0] - grass[k][0]) < 1e-9 for k in road)
    total += 1
    fails += 0 if seam else 1
    print(('PASS' if seam else 'FAIL'), 'street and lawn tracks share track centres')
    # negative fixtures
    parts, meta = vehicles.bus()
    wheel = next(p for p in parts if p.name == 'Wheel_FL')
    wheel.loc = (wheel.loc[0], wheel.loc[1], wheel.loc[2] + 0.05)
    neg1 = any(r['status'] == 'FAIL' and r['id'].startswith('Wheel_FL') for r in checks.run(parts, meta))
    parts, meta = vehicles.tram()
    for p in parts:                       # undo the Blender turn: front would face +Y
        if p.parent == meta['id']:
            p.loc = (-p.loc[0], -p.loc[1], p.loc[2])
            p.v = [(-a, -b, c) for a, b, c in p.v]
    neg2 = any(r['status'] == 'FAIL' and 'Socket_Front' in r['id'] for r in checks.run(parts, meta))
    print(('PASS' if neg1 else 'FAIL'), 'negative: lifted Wheel_FL is detected')
    print(('PASS' if neg2 else 'FAIL'), 'negative: model facing +Y is detected')
    ok = fails == 0 and neg1 and neg2
    print(f"TRANSIT_SELFTEST {'PASS' if ok else 'FAIL'}: {total - fails}/{total} checks, negatives {int(neg1) + int(neg2)}/2")
    return 0 if ok else 1


if __name__ == '__main__':
    sys.exit(main())
