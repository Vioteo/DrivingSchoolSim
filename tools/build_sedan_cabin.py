"""Cabin fixes of the sedan (driver feedback 26.09):
- longer wiper blades (driver 0.60 m, passenger 0.52 m; the passenger pivot moves to the centre so the blade fits
  the glass), a blade spine so the blade reads as a blade, not a stick;
- side air vents made narrower and moved outboard: the left one no longer covers the tachometer;
- the static "DRIVE SCHOOL / READY TO LEARN" text is removed from the centre screen (Unity draws a live
  trip-computer page there, InfotainmentView).

Replaces Wiper_Pivot_* (with children), Vent*, VentSlat*, Screen_Title, Screen_Subtitle. Names used by code stay:
Wiper_Pivot_<x> (prefix), Infotainment_Glass.

Run from the project root (Blender 5, background):
  "C:\\Program Files\\Blender Foundation\\Blender 5.0\\blender.exe" -b ArtSource/DS_Sedan_A.blend --python-exit-code 1 --python tools/build_sedan_cabin.py

Coordinates as in build_art.py: X right, Y forward, Z up, metres. Idempotent; copies the .blend and FBX to
ArtSource_Backup/ first.
"""
import bpy, sys, json, shutil, hashlib, datetime
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
import build_art as a

ROOT = a.ROOT
BLEND = ROOT / 'ArtSource/DS_Sedan_A.blend'
FBX = ROOT / 'Assets/DrivingSchool/Art/DS_Sedan_A.fbx'
BACKUP = ROOT / 'ArtSource_Backup'
REPORT = ROOT / 'artifacts/reports/sedan-cabin.json'
REPLACED = ('Wiper_Pivot_', 'WiperArm', 'WiperBlade', 'WiperSpine', 'Vent', 'Screen_Title', 'Screen_Subtitle')

# Wipers: pivot x, blade length. Both park pointing to the passenger side (+X) along the cowl, like a tandem system.
WIPERS = [(-0.44, 0.60, -0.045, 0.060),   # driver: blade offset up the glass (−Y, +Z) — parks above the passenger blade
          (0.07, 0.52, -0.022, 0.030)]
PIVOT_Y, PIVOT_Z = 0.975, 0.927
# Vents: x centre, width. Side vents sit outboard of the instrument hood (x −0.64…−0.12).
VENTS = [('Vent_L', -0.695, 0.095), ('Vent_C', 0.065, 0.21), ('Vent_R', 0.695, 0.095)]


def sha(p):
    return hashlib.sha256(Path(p).read_bytes()).hexdigest() if Path(p).exists() else None


def backup():
    BACKUP.mkdir(exist_ok=True)
    stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S')
    out = {}
    for src in (BLEND, FBX):
        if src.exists():
            dst = BACKUP / f'{src.stem}_before-cabin_{stamp}{src.suffix}'
            shutil.copy2(src, dst)
            out[src.name] = str(dst.relative_to(ROOT))
    return out


def wipers(root):
    made = []
    for x, length, dy, dz in WIPERS:
        w = a.empty('Wiper_Pivot_' + str(x), (x, PIVOT_Y, PIVOT_Z), root)
        x0, x1 = 0.05, 0.05 + length
        xc = 0.5 * (x0 + x1)
        a.tube('WiperArm', [(0, 0, 0), (0.12, dy * 0.4, dz * 0.4), (xc, dy, dz)], .0055, 'Rubber', w)
        a.tube('WiperBlade', [(x0, dy, dz), (x1, dy, dz)], .0045, 'Rubber', w)
        a.tube('WiperSpine', [(x0 + 0.02, dy + 0.004, dz + 0.009), (xc, dy + 0.003, dz + 0.014), (x1 - 0.02, dy + 0.004, dz + 0.009)], .003, 'Interior_Graphite', w)
        made.append(w.name)
    return made


def vents(root):
    made = []
    for name, x, width in VENTS:
        made.append(a.box(name, (x, .514, .945), (width, .025, .075), 'Rubber', .014, root).name)
        for j in range(4):
            made.append(a.box('VentSlat', (x, .492, .925 + j * .013), (width - .025, .01, .003), 'Satin_Aluminium', .001, root).name)
    return made


def main():
    if bpy.data.filepath and Path(bpy.data.filepath).resolve() != BLEND.resolve():
        raise SystemExit('Open ArtSource/DS_Sedan_A.blend, not ' + bpy.data.filepath)
    a.M = {m.name: m for m in bpy.data.materials}
    root = bpy.data.objects['DS_Sedan_A']
    report = {'script': 'tools/build_sedan_cabin.py', 'utc': datetime.datetime.utcnow().isoformat() + 'Z',
              'before': {'blend_sha256': sha(BLEND), 'fbx_sha256': sha(FBX)}, 'backup': backup()}
    removed = []
    for name in [o.name for o in root.children_recursive]:
        o = bpy.data.objects.get(name)
        if o is None or not name.startswith(REPLACED):
            continue
        for c in [c.name for c in o.children_recursive]:
            removed.append(c); bpy.data.objects.remove(bpy.data.objects[c], do_unlink=True)
        removed.append(name); bpy.data.objects.remove(o, do_unlink=True)
    report['removed'] = sorted(removed)
    report['created'] = sorted(wipers(root) + vents(root))
    for n in ('Infotainment_Glass', 'Cluster_Display', 'GaugeFace_RPM'):
        if n not in bpy.data.objects:
            raise SystemExit('Contract object missing: ' + n)
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND))
    a.export('DS_Sedan_A', root)
    report['after'] = {'blend_sha256': sha(BLEND), 'fbx_sha256': sha(FBX)}
    REPORT.parent.mkdir(parents=True, exist_ok=True)
    REPORT.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding='utf-8')
    print('SEDAN_CABIN_COMPLETE', json.dumps(report['created']), flush=True)


main()
