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
# CC0 community packs (seasonal clothing), https://static.makehumancommunity.org/assets/assetpacks.html
COMMUNITY = {
    'shirts01': 'a5a723b0e84a109bb190fcfeac7f1de4138d875da3e30fe5b3340eac9f38bcd3',
    'pants01': 'e4e0ec60db34f279be291a83cfd7b342a7c5cf09bb7676682a5f39f4f6ac4ad9',
    'shoes01': 'ded3f70428505eabbf1f6d7b5f61196a7366ef20757103d276ad0ed336c35ada',
    'gloves01': 'ecdaee1d02749d17352791d415cb622a883350cc8a4b90eda3725aef35d9afb2',
    'hats01': '97b70d7bd90e74ee87a49faeb7c4a1b2762b311902974db575851daaae05b50e',
}

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

def _unpack(url, z, sha, out):
    if (out / ('.complete-' + z.stem)).exists():
        return
    if not z.exists():
        _download(url, z)
    digest = hashlib.sha256(z.read_bytes()).hexdigest()
    if digest != sha:
        raise RuntimeError(f'{z}: SHA256 {digest}, expected {sha}')
    with zipfile.ZipFile(z) as f:
        f.extractall(out)
    (out / ('.complete-' + z.stem)).write_text(digest)

def assets():
    """Folder with the unpacked system asset pack (clothes/, hair/, skins/, ...)."""
    out = CACHE / 'system_assets'
    _unpack(PACK, CACHE / 'makehuman_system_assets_cc0.zip', PACK_SHA256, out)
    return out

def community():
    """Folder with the unpacked CC0 community packs (clothes/<asset>/...)."""
    out = CACHE / 'community'
    for name, sha in COMMUNITY.items():
        _unpack(f'https://files.makehumancommunity.org/asset_packs/{name}/{name}_cc0.zip',
                CACHE / 'packs' / f'{name}_cc0.zip', sha, out)
    return out

if __name__ == '__main__':
    for rel in ('3dobjs/base.obj', 'rigs/default.mhskel', 'rigs/default_weights.mhw'):
        print(data(rel))
    print(assets())
    print(community())
    sys.exit(0)
