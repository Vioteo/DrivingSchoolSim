"""Fetches the CC0 MakeHuman sources used by tools/build_pedestrians.py into a git-ignored cache.

Pinned: the MakeHuman repository commit (base mesh, targets, rig, weights) and the SHA256 of the
system asset pack (skins, clothes, hair, eyebrows, eyelashes, eyes, body proxies).
Both are CC0 1.0 (see ArtSource/Pedestrians/MakeHuman/LICENSE.md). Plain Python, no bpy needed:
    python tools/fetch_makehuman.py
"""
import hashlib, shutil, sys, urllib.request, zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CACHE = ROOT / 'ArtSource/Pedestrians/MakeHuman/cache'
COMMIT = 'a8bc2d54ff0ac92e78ff71431b1023eda42bf482'
RAW = f'https://raw.githubusercontent.com/makehumancommunity/makehuman/{COMMIT}/makehuman/data/'
PACK = 'https://files.makehumancommunity.org/asset_packs/makehuman_system_assets/makehuman_system_assets_cc0.zip'
PACK_SHA256 = 'b542127a8e25547c7c29c19f2d1d2adb9a664c80396ecd694095dbc8028a0107'

def _download(url, dest):
    dest.parent.mkdir(parents=True, exist_ok=True)
    tmp = dest.with_suffix(dest.suffix + '.part')
    with urllib.request.urlopen(url, timeout=120) as r, open(tmp, 'wb') as f:
        shutil.copyfileobj(r, f)
    tmp.replace(dest)

def data(rel):
    """Path of a file from the MakeHuman repository's data folder, downloaded on first use."""
    p = CACHE / 'data' / rel
    if not p.exists():
        _download(RAW + rel, p)
    return p

def assets():
    """Folder with the unpacked system asset pack (clothes/, hair/, skins/, ...)."""
    out = CACHE / 'system_assets'
    if (out / '.complete').exists():
        return out
    z = CACHE / 'makehuman_system_assets_cc0.zip'
    if not z.exists():
        _download(PACK, z)
    digest = hashlib.sha256(z.read_bytes()).hexdigest()
    if digest != PACK_SHA256:
        raise RuntimeError(f'{z}: SHA256 {digest}, expected {PACK_SHA256}')
    with zipfile.ZipFile(z) as f:
        f.extractall(out)
    (out / '.complete').write_text(digest)
    return out

if __name__ == '__main__':
    for rel in ('3dobjs/base.obj', 'rigs/default.mhskel', 'rigs/default_weights.mhw'):
        print(data(rel))
    print(assets())
    sys.exit(0)
