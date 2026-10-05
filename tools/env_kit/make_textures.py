"""Procedural textures of the EN kit (T73): bark, foliage cards, ground, road signs.

    python tools/env_kit/make_textures.py [--only name,name] [--preview]

Needs Python 3.10+, numpy, scipy, Pillow. Writes tools/env_kit/textures/T_*.png
(the build script copies them to Assets/DrivingSchool/Art/EnvKit/Textures).
Everything is drawn here from noise and simple shapes, so there is no third-party
licence. Colours are sRGB. Tileable textures wrap in both directions.
"""
import math
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont
from scipy import ndimage

OUT = Path(__file__).resolve().parent / 'textures'
FONT_CANDIDATES = [
    '/usr/share/fonts/truetype/dejavu/DejaVuSansCondensed-Bold.ttf',
    'C:/Windows/Fonts/arialbd.ttf',
    '/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf',
]


# ---------------------------------------------------------------- noise
def rng(seed):
    return np.random.default_rng(seed)


def value_noise(shape, cells, seed, wrap=True):
    """Smooth tileable noise in [0, 1]: random grid of cells (cy, cx) upscaled with cubic splines."""
    R = rng(seed)
    cy, cx = cells if isinstance(cells, tuple) else (cells, cells)
    g = R.random((cy, cx))
    zy, zx = shape[0] / cy, shape[1] / cx
    out = ndimage.zoom(np.tile(g, (3, 3)), (zy, zx), order=3, mode='grid-wrap' if wrap else 'reflect')
    h, w = shape
    out = out[h:2 * h, w:2 * w]
    return (out - out.min()) / max(1e-9, out.max() - out.min())


def fbm(shape, base_cells, octaves, seed, gain=0.5, aspect=(1, 1)):
    acc = np.zeros(shape)
    amp, tot = 1.0, 0.0
    for o in range(octaves):
        c = (max(1, int(base_cells * aspect[0] * 2 ** o)), max(1, int(base_cells * aspect[1] * 2 ** o)))
        c = (min(c[0], shape[0]), min(c[1], shape[1]))
        acc += amp * value_noise(shape, c, seed + o * 101)
        tot += amp
        amp *= gain
    return acc / tot


def voronoi(shape, n, seed, stretch=(1.0, 1.0)):
    """Tileable Voronoi: distance to nearest and second nearest point (in pixels) and cell id."""
    from scipy.spatial import cKDTree
    R = rng(seed)
    h, w = shape
    pts = R.random((n, 2)) * [h, w]
    tiled = np.concatenate([pts + [dy * h, dx * w] for dy in (-1, 0, 1) for dx in (-1, 0, 1)])
    ids = np.tile(np.arange(n), 9)
    sy, sx = stretch
    tree = cKDTree(tiled * [sy, sx])
    yy, xx = np.mgrid[0:h, 0:w]
    q = np.stack([yy.ravel() * sy, xx.ravel() * sx], 1)
    d, i = tree.query(q, k=2)
    return d[:, 0].reshape(shape), d[:, 1].reshape(shape), ids[i[:, 0]].reshape(shape)


def colorize(t, stops):
    """t in [0,1] -> RGB by linear interpolation between (pos, (r,g,b)) stops."""
    t = np.clip(t, 0, 1)
    out = np.zeros(t.shape + (3,))
    pos = [p for p, _ in stops]
    cols = np.array([c for _, c in stops], float)
    for ch in range(3):
        out[..., ch] = np.interp(t, pos, cols[:, ch])
    return out


def save(name, arr, alpha=None):
    OUT.mkdir(parents=True, exist_ok=True)
    rgb = np.clip(arr, 0, 255).astype(np.uint8)
    if alpha is not None:
        a = np.clip(alpha, 0, 255).astype(np.uint8)
        img = Image.fromarray(np.dstack([rgb, a]), 'RGBA')
    else:
        img = Image.fromarray(rgb, 'RGB')
    img.save(OUT / f'{name}.png', optimize=True)
    print('TEX', name, img.size, img.mode, flush=True)
    return img


def save_img(name, img):
    OUT.mkdir(parents=True, exist_ok=True)
    img.save(OUT / f'{name}.png', optimize=True)
    print('TEX', name, img.size, img.mode, flush=True)


def tile_paste(canvas, sprite, x, y):
    """Alpha-composites a sprite at (x, y) wrapping around the canvas edges (tileable)."""
    W, H = canvas.size
    for dx in (-W, 0, W):
        for dy in (-H, 0, H):
            px, py = int(x + dx), int(y + dy)
            if px + sprite.width < 0 or py + sprite.height < 0 or px > W or py > H:
                continue
            canvas.alpha_composite(sprite, (px, py)) if px >= 0 and py >= 0 else _paste_clip(canvas, sprite, px, py)


def _paste_clip(canvas, sprite, px, py):
    sx, sy = max(0, -px), max(0, -py)
    crop = sprite.crop((sx, sy, sprite.width, sprite.height))
    canvas.alpha_composite(crop, (max(0, px), max(0, py)))


# ---------------------------------------------------------------- ground
def asphalt():
    """Worn asphalt concrete, 4 m tile. Deliberately without distinct features (no cracks, stains or
    patches): anything recognisable repeats every 4 m and reads as squares and stripes on the road."""
    S = 1024                     # 4 m tile -> 3.9 mm/px
    base = fbm((S, S), 6, 4, 11)
    fine = fbm((S, S), 96, 3, 12)
    t = 0.45 + 0.08 * (base - 0.5) + 0.3 * (fine - 0.5)
    col = colorize(t, [(0, (44, 45, 46)), (0.5, (64, 64, 65)), (1, (86, 85, 84))])
    R = rng(13)
    # aggregate: light and dark stones 1-3 px
    n = 70000
    ys, xs = R.integers(0, S, n), R.integers(0, S, n)
    light = R.random(n) < 0.55
    vals = np.where(light, R.uniform(78, 115, n), R.uniform(20, 34, n))
    for k in range(n):
        r = 1 if R.random() < 0.8 else 2
        y0, x0 = ys[k], xs[k]
        yy = np.arange(y0 - r + 1, y0 + r) % S
        xx = np.arange(x0 - r + 1, x0 + r) % S
        col[np.ix_(yy, xx)] = col[np.ix_(yy, xx)] * 0.45 + vals[k] * 0.55
    save('T_Asphalt', col)


def gravel():
    S = 1024                     # 2 m tile
    soil = fbm((S, S), 4, 5, 21)
    col = colorize(soil, [(0, (70, 60, 48)), (1, (118, 104, 86))])
    canvas = Image.fromarray(col.astype(np.uint8)).convert('RGBA')
    R = rng(22)
    for k in range(5200):
        r = R.uniform(3, 11)
        e = R.uniform(0.6, 1.0)
        g = R.uniform(0.75, 1.15)
        tone = R.choice([(150, 146, 138), (128, 120, 110), (165, 155, 140), (110, 104, 98), (140, 125, 105)])
        c = tuple(int(min(255, v * g)) for v in tone)
        w, h = int(r * 2 + 4), int(r * 2 * e + 4)
        sp = Image.new('RGBA', (w, h), (0, 0, 0, 0))
        dd = ImageDraw.Draw(sp)
        dd.ellipse([2, 2, w - 2, h - 2], fill=c + (255,))
        dd.ellipse([2 + r * 0.3, 2, w - 2, h - 2 - r * 0.4], fill=tuple(min(255, int(v * 1.12)) for v in c) + (255,))
        sp = sp.rotate(R.uniform(0, 180), expand=True)
        shadow = Image.new('RGBA', sp.size, (30, 25, 20, 0))
        shadow.putalpha(sp.getchannel('A').point(lambda a: int(a * 0.5)))
        x, y = R.uniform(0, S), R.uniform(0, S)
        tile_paste(canvas, shadow, x + 1.5, y + 2)
        tile_paste(canvas, sp, x, y)
    save_img('T_Gravel', canvas.convert('RGB'))


def grass_ground(name='T_Grass', seed=31, dry=0.15):
    S = 1024                     # 2 m tile
    base = fbm((S, S), 4, 5, seed)
    col = colorize(base, [(0, (34, 52, 18)), (0.5, (52, 76, 26)), (1, (78, 98, 38))])
    canvas = Image.fromarray(col.astype(np.uint8)).convert('RGBA')
    d = ImageDraw.Draw(canvas)
    R = rng(seed + 1)
    patch = fbm((S, S), 3, 3, seed + 2)
    for k in range(26000):
        x, y = R.uniform(0, S), R.uniform(0, S)
        L = R.uniform(10, 28)
        a = -math.pi / 2 + R.normal(0, 0.5)
        is_dry = R.random() < dry * (0.5 + patch[int(y) % S, int(x) % S])
        if is_dry:
            c = (int(R.uniform(120, 160)), int(R.uniform(115, 140)), int(R.uniform(55, 75)))
        else:
            g = R.uniform(0.7, 1.35)
            c = (int(48 * g), int(88 * g), int(28 * g))
        x2, y2 = x + math.cos(a) * L, y + math.sin(a) * L
        for dx in (-S, 0, S):
            for dy in (-S, 0, S):
                if -30 < x + dx < S + 30 and -30 < y + dy < S + 30:
                    d.line([(x + dx, y + dy), (x2 + dx, y2 + dy)], fill=c + (255,), width=2)
    img = canvas.convert('RGB').filter(ImageFilter.GaussianBlur(0.4))
    save_img(name, img)


def soil():
    S = 512
    n = fbm((S, S), 6, 5, 41)
    col = colorize(n, [(0, (48, 34, 22)), (1, (92, 70, 48))])
    save('T_Soil', col)


def concrete():
    S = 512                      # 2 m tile
    n = fbm((S, S), 8, 5, 51)
    f = fbm((S, S), 128, 2, 52)
    col = colorize(0.5 + 0.35 * (n - 0.5) + 0.3 * (f - 0.5), [(0, (120, 118, 112)), (1, (178, 176, 168))])
    R = rng(53)
    for k in range(4000):   # pores
        y, x = R.integers(0, S, 2)
        col[y, x] *= 0.6
    save('T_Concrete', col)


def marking():
    S = 512
    n = fbm((S, S), 6, 4, 61)
    col = colorize(n, [(0, (196, 196, 188)), (1, (238, 238, 232))])
    R = rng(62)
    for k in range(3000):   # glass beads and grime specks
        y, x = R.integers(0, S, 2)
        col[y, x] = (150, 150, 145) if R.random() < 0.6 else (255, 255, 255)
    save('T_Marking', col)


def rubber_crumb(name='T_RubberCrumb', tones=((150, 40, 32), (120, 28, 22), (175, 60, 45)), seed=71):
    S = 512
    col = np.zeros((S, S, 3))
    R = rng(seed)
    idx = R.integers(0, len(tones), (S // 2, S // 2))
    t = np.array(tones, float)[idx]
    col = np.kron(t, np.ones((2, 2, 1)))
    col *= (0.85 + 0.3 * fbm((S, S), 8, 3, seed + 1))[..., None]
    save(name, col)


def wood_planks():
    S = 512                       # 1 m tile, grain along U
    g = fbm((S, S), 4, 5, 81, aspect=(6, 0.4))
    col = colorize(g, [(0, (88, 52, 26)), (0.5, (126, 80, 42)), (1, (150, 100, 58))])
    save('T_Wood', col)


def galvanized():
    """Hot-dip zinc: fine spangle crystals (a few mm), low contrast, light streaks."""
    S = 512
    n = fbm((S, S), 8, 4, 91)
    sp, _, ids = voronoi((S, S), 5000, 92)
    cell = rng(93).random(5000)[ids]
    streak = fbm((S, S), 4, 3, 94, aspect=(8, 0.5))
    col = colorize(0.3 * n + 0.45 * cell + 0.25 * streak, [(0, (150, 154, 158)), (1, (186, 190, 194))])
    save('T_Galvanized', col)


# ---------------------------------------------------------------- bark
def bark_birch():
    W, H = 512, 1024              # tile: 0.6 m around x 1.2 m up
    n = fbm((H, W), 6, 4, 101, aspect=(0.6, 1.4))
    col = colorize(n, [(0, (196, 192, 180)), (1, (236, 233, 224))])
    img = Image.fromarray(col.astype(np.uint8)).convert('RGBA')
    d = ImageDraw.Draw(img)
    R = rng(102)
    # grey-pink papery streaks around the trunk
    for k in range(140):
        y = R.uniform(0, H)
        x = R.uniform(0, W)
        L = R.uniform(40, 220)
        d.line([(x, y), (x + L, y + R.normal(0, 2))], fill=(170, 160, 150, 90), width=int(R.uniform(1, 3)))
    # lenticels: black horizontal dashes, often in rows
    for k in range(420):
        y = R.uniform(0, H)
        x = R.uniform(0, W)
        L = R.uniform(8, 60) * (2.5 if R.random() < 0.08 else 1)
        h = R.uniform(2, 6)
        c = (int(R.uniform(15, 45)),) * 3 + (255,)
        sp = Image.new('RGBA', (int(L + 6), int(h + 6)), (0, 0, 0, 0))
        ImageDraw.Draw(sp).ellipse([3, 3, L + 3, h + 3], fill=c)
        tile_paste(img, sp, x, y)
    # a few large dark patches (old branch scars)
    for k in range(4):
        x, y = R.uniform(0, W), R.uniform(0, H)
        w, h = R.uniform(60, 110), R.uniform(14, 26)
        sp = Image.new('RGBA', (int(w + 40), int(h + 40)), (0, 0, 0, 0))
        dd = ImageDraw.Draw(sp)
        dd.ellipse([20, 20, 20 + w, 20 + h], fill=(28, 25, 22, 255))
        dd.polygon([(20 + w * 0.35, 20 + h * 0.5), (20 + w * 0.5, 20 + h + 16), (20 + w * 0.65, 20 + h * 0.5)], fill=(28, 25, 22, 255))
        sp = sp.filter(ImageFilter.GaussianBlur(1.5))
        tile_paste(img, sp, x, y)
    save_img('T_Bark_Birch', img.convert('RGB'))


def bark_birch_base():
    """Non-tiling vertically: bottom 3 m of a birch: black cracked bark fading into white at the top."""
    W, H = 512, 1024              # 0.6 m around x 3 m up (v = 0 at the ground)
    n = fbm((H, W), 6, 5, 111, aspect=(2.5, 0.6))
    d1, d2, _ = voronoi((H, W), 120, 112, stretch=(0.25, 1.0))
    crack = np.clip((d2 - d1) / 6.0, 0, 1)
    dark = colorize(n, [(0, (22, 20, 18)), (1, (70, 64, 58))]) * (0.55 + 0.45 * crack[..., None])
    white = colorize(n, [(0, (196, 192, 180)), (1, (236, 233, 224))])
    yy = np.linspace(1, 0, H)[:, None]      # image top = 3 m
    island = fbm((H, W), 5, 4, 113, aspect=(0.5, 1.5))
    m = np.clip((yy - 0.18) * 3.2 + (island - 0.5) * 1.4, 0, 1)      # 0 = dark bark, 1 = white
    col = dark * (1 - m[..., None]) + white * m[..., None]
    save('T_Bark_BirchBase', col)


def bark_pine():
    W, H = 512, 1024              # 0.8 m around x 1.6 m up
    d1, d2, ids = voronoi((H, W), 70, 121, stretch=(0.35, 1.0))
    plate = rng(122).random(70)[ids]
    n = fbm((H, W), 8, 4, 123)
    jitter = fbm((H, W), 24, 3, 126)
    crack = np.clip((d2 - d1 - 4 + 10 * (jitter - 0.5)) / 10.0, 0, 1) ** 0.7
    col = colorize(0.55 * plate + 0.45 * n, [(0, (70, 54, 44)), (0.6, (108, 86, 68)), (1, (136, 112, 92))])
    col *= (0.3 + 0.7 * crack)[..., None]
    save('T_Bark_PineLow', col)
    # upper trunk: orange, thin flakes peeling horizontally
    n = fbm((H, W), 4, 5, 124, aspect=(3, 0.5))
    f = fbm((H, W), 32, 3, 125, aspect=(2.0, 0.4))
    col = colorize(0.6 * n + 0.4 * f, [(0, (150, 70, 36)), (0.5, (196, 104, 56)), (1, (222, 146, 92))])
    flake = np.clip((f - 0.62) * 5, 0, 1)
    col = col * (1 - 0.25 * flake[..., None]) + np.array([235, 200, 160]) * 0.25 * flake[..., None]
    save('T_Bark_PineUp', col)


def bark_spruce():
    W, H = 512, 512
    d1, d2, ids = voronoi((H, W), 400, 131, stretch=(0.8, 1.0))
    sc = rng(132).random(400)[ids]
    edge = np.clip((d2 - d1) / 5.0, 0, 1)
    n = fbm((H, W), 6, 4, 133)
    col = colorize(0.4 * sc + 0.6 * n, [(0, (66, 54, 46)), (1, (122, 104, 90))]) * (0.7 + 0.3 * edge[..., None])
    save('T_Bark_Spruce', col)


def bark_dark():
    """Furrowed grey-brown bark (linden, branches): ridges along the trunk."""
    W, H = 512, 1024
    d1, d2, ids = voronoi((H, W), 60, 141, stretch=(0.18, 1.0))
    jitter = fbm((H, W), 20, 3, 142)
    crack = np.clip((d2 - d1 - 3 + 12 * (jitter - 0.5)) / 12.0, 0, 1) ** 0.8
    n = fbm((H, W), 6, 4, 143)
    col = colorize(n, [(0, (58, 52, 46)), (1, (110, 100, 90))]) * (0.35 + 0.65 * crack[..., None])
    save('T_Bark_Dark', col)


# ---------------------------------------------------------------- foliage cards
def leaf_shape(kind, L):
    """Leaf polygon (pointing +x from the petiole at 0,0), length L px."""
    pts_top = []
    n = 14
    for i in range(n + 1):
        t = i / n
        if kind == 'birch':        # triangular-ovate, toothed
            w = L * 0.36 * (math.sin(math.pi * min(1, t * 1.25)) ** 0.8) * (1 - t * 0.35)
            if i % 2 == 1:
                w *= 0.88
        elif kind == 'linden':     # heart-shaped
            w = L * 0.48 * math.sin(math.pi * t) ** 0.7 * (1.12 - t * 0.5)
            if i % 2 == 1:
                w *= 0.92
        else:                      # bush: elliptic
            w = L * 0.3 * math.sin(math.pi * t) ** 0.9
        pts_top.append((t * L, -w))
    bottom = [(x, -y) for x, y in reversed(pts_top[1:-1])]
    return pts_top + bottom


def draw_leaf(d, x, y, ang, L, col, kind):
    poly = leaf_shape(kind, L)
    ca, sa = math.cos(ang), math.sin(ang)
    P = [(x + px * ca - py * sa, y + px * sa + py * ca) for px, py in poly]
    d.polygon(P, fill=col)
    # midrib slightly lighter, edge rim slightly darker
    rib = tuple(min(255, int(c * 1.18)) for c in col[:3]) + (255,)
    d.line([(x, y), (x + L * 0.9 * ca, y + L * 0.9 * sa)], fill=rib, width=max(1, int(L / 28)))


def foliage_card(kind, size, seed, n_leaves, palette, droop=True):
    """One leafy sprig on a transparent card, stem entering from the top centre (hanging) or
    from the bottom (droop=False). Supersampled x2 for clean edges."""
    SS = 2
    S = size * SS
    img = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    R = rng(seed)
    # twig skeleton: main twig + side twigs
    if droop:
        start = (S * 0.5, S * 0.02)
        main_dir = math.pi / 2 + R.normal(0, 0.15)
    else:
        start = (S * 0.5, S * 0.98)
        main_dir = -math.pi / 2 + R.normal(0, 0.2)
    twigs = []
    p = list(start)
    a = main_dir
    main = [tuple(p)]
    for k in range(12):
        a += R.normal(0, 0.12)
        p = [p[0] + math.cos(a) * S * 0.075, p[1] + math.sin(a) * S * 0.075]
        main.append(tuple(p))
    twigs.append(main)
    for k in range(13):
        i = R.integers(1, len(main) - 2)
        b = main[i]
        side = 1 if k % 2 else -1
        aa = a + side * R.uniform(0.6, 1.2)
        q = list(b)
        tw = [tuple(q)]
        for s in range(int(R.integers(3, 6))):
            aa += R.normal(0, 0.15) + (0.08 if droop else -0.05)
            q = [q[0] + math.cos(aa) * S * 0.06, q[1] + math.sin(aa) * S * 0.06]
            tw.append(tuple(q))
        twigs.append(tw)
    twig_col = (70, 52, 40, 255) if kind != 'birch' else (60, 40, 32, 255)
    for tw in twigs:
        d.line(tw, fill=twig_col, width=max(2, int(S * 0.006)))
    # leaves along twigs, back leaves darker first
    pts = [pt for tw in twigs for pt in tw[1:]]
    leaves = []
    for k in range(n_leaves):
        bx, by = pts[R.integers(0, len(pts))]
        bx += R.normal(0, S * 0.02)
        by += R.normal(0, S * 0.02)
        L = S * R.uniform(0.06, 0.095) * (1.3 if kind == 'linden' else 1.0)
        ang = (math.pi / 2 if droop else -math.pi / 2) + R.normal(0, 0.9)
        shade = R.uniform(0, 1)
        leaves.append((shade, bx, by, ang, L))
    leaves.sort()
    for shade, bx, by, ang, L in leaves:
        base = palette[int(R.integers(0, len(palette)))]
        g = 0.62 + 0.5 * shade
        col = tuple(int(min(255, c * g)) for c in base) + (255,)
        draw_leaf(d, bx, by, ang, L, col, kind)
    img = img.resize((size, size), Image.LANCZOS)
    return img


def foliage_atlas(name, kind, palette, droop, n_leaves, seed):
    size = 1024
    atlas = Image.new('RGBA', (2 * size, 2 * size), (0, 0, 0, 0))
    for k in range(4):
        card = foliage_card(kind, size, seed + k, n_leaves, palette, droop)
        atlas.paste(card, ((k % 2) * size, (k // 2) * size))
    atlas = dilate_rgb(atlas)
    save_img(name, atlas)


def dilate_rgb(img, it=6):
    """Bleeds colour into transparent pixels so mip-mapping does not produce dark fringes."""
    a = np.asarray(img).astype(float)
    rgb, al = a[..., :3], a[..., 3]
    mask = al > 8
    for _ in range(it):
        grown = ndimage.binary_dilation(mask)
        new = grown & ~mask
        if not new.any():
            break
        acc = np.zeros_like(rgb)
        cnt = np.zeros(al.shape)
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                sh = np.roll(np.roll(mask, dy, 0), dx, 1)
                acc += np.roll(np.roll(rgb, dy, 0), dx, 1) * sh[..., None]
                cnt += sh
        rgb[new] = acc[new] / np.maximum(cnt[new], 1)[..., None]
        mask = grown
    return Image.fromarray(np.dstack([rgb, al]).astype(np.uint8), 'RGBA')


def spruce_frond(size_w, size_h, seed):
    """Spruce spray seen from above: main shoot left -> right, side shoots angled forward and
    getting shorter toward the tip (an elongated arrow), short needles all round every shoot.
    Aspect 2:1, everything stays inside the cell (no clipped edges)."""
    SS = 2
    W, H = size_w * SS, size_h * SS
    img = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    R = rng(seed)
    y0 = H * 0.5
    x_a, x_b = W * 0.01, W * 0.97
    main = [(x_a + (x_b - x_a) * i / 24, y0 + math.sin(i * 0.35 + seed) * H * 0.015) for i in range(25)]
    shoots = [(main, 1.0, 0)]
    n_side = 16
    for k in range(n_side):
        u = (k + 0.5 + R.uniform(-0.25, 0.25)) / n_side * 0.92
        i = min(23, int(u * 24))
        b = main[i]
        side = 1 if k % 2 else -1
        ang = R.uniform(0.75, 1.0)              # from the main axis, forward
        room = (H * 0.47) / math.sin(ang)        # stays inside the cell
        L = room * (1.0 - 0.72 * u) * R.uniform(0.85, 1.0)
        a = side * ang
        tw, q = [b], b
        for s_ in range(6):
            a -= side * 0.05                     # curves slightly toward the tip
            q = (q[0] + math.cos(a) * L / 6, q[1] + math.sin(a) * L / 6)
            tw.append(q)
        shoots.append((tw, 0.75, 1))
        if L > room * 0.45:                      # second order on long side shoots
            for m in range(2):
                j = 2 + m * 2
                c = tw[j]
                a2 = a + side * R.uniform(0.5, 0.8) * (1 if m % 2 else -1)
                L2 = L * R.uniform(0.25, 0.35)
                e = (c[0] + math.cos(a2) * L2, min(H * 0.985, max(H * 0.015, c[1] + math.sin(a2) * L2)))
                shoots.append(([c, e], 0.55, 2))
    dark = [(16, 40, 24), (22, 50, 30), (28, 60, 34), (19, 46, 34)]
    tip = (64, 104, 52)
    for tw, wt, lvl in shoots:
        d.line(tw, fill=(78, 56, 38, 255), width=max(2, int(5 * wt * SS)))
    for tw, wt, lvl in shoots:
        tot = sum(math.dist(tw[j], tw[j + 1]) for j in range(len(tw) - 1))
        acc = 0.0
        for j in range(len(tw) - 1):
            (xa, ya), (xb, yb) = tw[j], tw[j + 1]
            seg = math.dist(tw[j], tw[j + 1])
            sa = math.atan2(yb - ya, xb - xa)
            n = int(seg / (1.6 * SS)) + 1
            for m in range(n):
                f = (m + R.random()) / n
                px, py = xa + (xb - xa) * f, ya + (yb - ya) * f
                along = (acc + seg * f) / max(tot, 1)
                for side in (-1, 1):
                    na = sa + side * R.uniform(0.5, 1.15)
                    Ln = H * R.uniform(0.045, 0.07) * (0.85 + 0.3 * wt) * (1 - 0.35 * along)
                    is_tip = along > 0.8 and R.random() < 0.7
                    c = tip if is_tip else dark[int(R.integers(0, 4))]
                    g = R.uniform(0.82, 1.2)
                    c = tuple(int(min(255, v * g)) for v in c) + (255,)
                    ex = px + math.cos(na) * Ln
                    ey = min(H - 1, max(0, py + math.sin(na) * Ln))
                    d.line([(px, py), (ex, ey)], fill=c, width=int(2.6 * SS))
            acc += seg
    return img.resize((size_w, size_h), Image.LANCZOS)


def spruce_atlas():
    """2 x 4 cells of 1024 x 512 (cell k: column k % 2, row k // 2)."""
    w, h = 1024, 512
    atlas = Image.new('RGBA', (2 * w, 4 * h), (0, 0, 0, 0))
    for k in range(8):
        atlas.paste(spruce_frond(w, h, 200 + k), ((k % 2) * w, (k // 2) * h))
    save_img('T_Needles_Spruce', dilate_rgb(atlas))


def pine_clump(size, seed):
    """End of a pine branch: a dense brush of 5-7 needle tufts (needles 5-7 cm, in pairs),
    stem entering from the bottom centre."""
    SS = 2
    S = size * SS
    img = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    R = rng(seed)
    cols = [(40, 74, 40), (52, 88, 46), (32, 62, 36), (64, 98, 54), (46, 82, 50)]
    base = (S * 0.5, S * 0.99)
    n = int(R.integers(5, 8))
    tips = []
    for k in range(n):
        a = -math.pi / 2 + (k - (n - 1) / 2) * min(0.3, 1.15 / (n - 1)) + R.normal(0, 0.06)
        L = S * R.uniform(0.42, 0.56)
        mid = (base[0] + math.cos(a) * L * 0.45, base[1] + math.sin(a) * L * 0.5)
        end = (base[0] + math.cos(a) * L, base[1] + math.sin(a) * L)
        d.line([base, mid, end], fill=(112, 72, 42, 255), width=int(S * 0.012))
        tips.append((end, a, mid))
    tufts = []
    for end, a, mid in tips:
        tufts.append((end, a, 1.0))
        tufts.append(((end[0] * 0.55 + mid[0] * 0.45, end[1] * 0.55 + mid[1] * 0.45), a, 0.75))
    for (cx, cy), a, k in tufts:
        for m in range(int(240 * k)):
            na = a + R.normal(0, 0.95)               # needles mostly forward, a few backward
            Ln = S * R.uniform(0.09, 0.15) * k
            bend = R.normal(0, 0.15)
            x0, y0 = cx + R.normal(0, S * 0.012), cy + R.normal(0, S * 0.012)
            mid = (x0 + math.cos(na) * Ln * 0.5, y0 + math.sin(na) * Ln * 0.5)
            e = (x0 + math.cos(na + bend) * Ln, y0 + math.sin(na + bend) * Ln)
            c = cols[int(R.integers(0, len(cols)))]
            g = R.uniform(0.78, 1.18)
            c = tuple(int(min(255, v * g)) for v in c) + (255,)
            d.line([(x0, y0), mid, e], fill=c, width=int(2.4 * SS))
    al = np.asarray(img)[..., 3].copy()
    m = int(S * 0.006)
    al[:, :m] = 0
    al[:, -m:] = 0
    al[:m, :] = 0
    img.putalpha(Image.fromarray(al))
    return img.resize((size, size), Image.LANCZOS)


def pine_atlas():
    size = 1024
    atlas = Image.new('RGBA', (2 * size, 2 * size), (0, 0, 0, 0))
    for k in range(4):
        atlas.paste(pine_clump(size, 300 + k), ((k % 2) * size, (k // 2) * size))
    save_img('T_Needles_Pine', dilate_rgb(atlas))


def grass_blades():
    """Grass tuft card: blades rise from a narrow base and fan out, tallest in the middle, so the
    card reads as a rounded tuft with no straight edges. Alpha."""
    W, H = 1024, 512
    SS = 2
    img = Image.new('RGBA', (W * SS, H * SS), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    R = rng(401)
    blades = []
    for k in range(520):
        u = float(np.clip(R.normal(0, 0.16), -0.42, 0.42))      # base offset from the centre
        env = 1.0 - (abs(u) / 0.45) ** 1.6
        L = H * SS * 0.94 * env * R.uniform(0.45, 1.0)
        lean = u * 1.4 + R.normal(0, 0.18)                      # fans outward
        dry = R.random() < 0.14
        c = (int(R.uniform(150, 182)), int(R.uniform(138, 158)), int(R.uniform(80, 98))) if dry else \
            tuple(int(v * R.uniform(0.75, 1.3)) for v in (72, 112, 40))
        blades.append((R.random(), u, L, lean, c))
    blades.sort()
    for _, u, L, lean, c in blades:
        x0 = (0.5 + u * 0.6) * W * SS
        pts = [(x0 + lean * L * (i / 7) ** 1.6, H * SS - L * (i / 7)) for i in range(8)]
        base_w = SS * R.uniform(3.5, 6.0)
        for i in range(7):
            shade = 0.62 + 0.38 * (i / 6)                           # darker at the base
            cc = tuple(int(min(255, v * shade)) for v in c) + (255,)
            d.line([pts[i], pts[i + 1]], fill=cc, width=max(1, int(base_w * (1 - i / 7))))
    img = img.resize((W, H), Image.LANCZOS)
    save_img('T_GrassBlades', dilate_rgb(img))


# ---------------------------------------------------------------- road signs (ГОСТ Р 52290, типоразмер II)
SIGN_RED = (196, 24, 32)
SIGN_WHITE = (246, 246, 242)
SIGN_BLACK = (22, 22, 24)
PX_PER_M = 1100            # sign textures: 1.1 px per mm


def font(size):
    for f in FONT_CANDIDATES:
        if Path(f).exists():
            return ImageFont.truetype(f, size)
    return ImageFont.load_default()


class SignCanvas:
    """Draws in millimetres with y up and origin at the sign centre; image covers the plate bbox."""

    def __init__(self, w_mm, h_mm, ss=3):
        self.w, self.h, self.ss = w_mm, h_mm, ss
        self.k = PX_PER_M / 1000 * ss
        self.img = Image.new('RGBA', (int(w_mm * self.k), int(h_mm * self.k)), (0, 0, 0, 0))
        self.d = ImageDraw.Draw(self.img)

    def p(self, x, y):
        return ((x + self.w / 2) * self.k, (self.h / 2 - y) * self.k)

    def poly(self, pts, fill):
        self.d.polygon([self.p(x, y) for x, y in pts], fill=fill)

    def circle(self, r, fill, cx=0, cy=0):
        x0, y0 = self.p(cx - r, cy + r)
        x1, y1 = self.p(cx + r, cy - r)
        self.d.ellipse([x0, y0, x1, y1], fill=fill)

    def rrect(self, x0, y0, x1, y1, r, fill):
        a, b = self.p(x0, y1), self.p(x1, y0)
        self.d.rounded_rectangle([a, b], radius=r * self.k, fill=fill)

    def line(self, pts, w, fill):
        self.d.line([self.p(x, y) for x, y in pts], fill=fill, width=max(1, int(w * self.k)), joint='curve')

    def text(self, s, size_mm, fill, cy=0):
        f = font(int(size_mm * self.k))
        bb = self.d.textbbox((0, 0), s, font=f)
        tw, th = bb[2] - bb[0], bb[3] - bb[1]
        cx_, cy_ = self.p(0, cy)
        self.d.text((cx_ - tw / 2 - bb[0], cy_ - th / 2 - bb[1]), s, font=f, fill=fill)

    def finish(self, name, grime=0.05):
        out = self.img.resize((int(self.w * PX_PER_M / 1000), int(self.h * PX_PER_M / 1000)), Image.LANCZOS)
        a = np.asarray(out).astype(float)
        H, W = a.shape[:2]
        n = fbm((H, W), 6, 4, hash(name) % 1000)
        a[..., :3] *= (1 - grime * n)[..., None]
        out = Image.fromarray(a.astype(np.uint8), 'RGBA')
        save_img(name, dilate_rgb(out, 4))


def rounded_triangle(A, r, inset=0.0, n=10):
    """Equilateral triangle, side A (mm), apex up, centroid at the origin, corners rounded with radius r,
    offset inwards by `inset` (same corner centres while r > inset). Returns the outline polygon."""
    rho = A / (2 * math.sqrt(3)) - inset          # inradius of the offset shape
    rr = max(r - inset, 6.0)
    d_c = 2 * (rho - rr)                          # corner arc centre: distance rr from both adjacent sides
    pts = []
    for k in range(3):
        a = math.radians(90 + 120 * k)
        cx, cy = math.cos(a) * d_c, math.sin(a) * d_c
        for i in range(n + 1):
            t = a - math.radians(60) + math.radians(120) * i / n
            pts.append((cx + rr * math.cos(t), cy + rr * math.sin(t)))
    return pts


def sign_1_11(right=True):
    A, r = 900, 45 * 1.3
    c = SignCanvas(A, A * math.sqrt(3) / 2 + 2)
    # the triangle's centroid is lower than the bbox centre: shift drawings by dy
    dy = -(A * math.sqrt(3) / 2) / 2 + A / (2 * math.sqrt(3))
    T = lambda pts: [(x, y + dy) for x, y in pts]
    c.poly(T(rounded_triangle(A, r)), SIGN_WHITE + (255,))          # white edge (кант) 10 mm
    c.poly(T(rounded_triangle(A, r, 10)), SIGN_RED + (255,))
    c.poly(T(rounded_triangle(A, r, 10 + 70)), SIGN_WHITE + (255,))  # red border 70 mm
    s = 1 if right else -1
    w = 64                                   # stroke width
    # pictogram: vertical stem, a 75 deg bend to the side and an arrow head
    cy0 = -140
    x_st = -60 * s
    pts = [(x_st, cy0), (x_st, cy0 + 170)]
    R = 105
    for i in range(1, 13):
        t = math.radians(180 - 75 * i / 12) if right else math.radians(75 * i / 12)
        cx_ = x_st + R * s
        pts.append((cx_ + R * math.cos(t), cy0 + 150 + R * math.sin(t)))
    c.line(T(pts), w, SIGN_BLACK + (255,))
    # round the start of the stem with a square end (as on the sign) and draw the head
    ex, ey = pts[-1]
    px, py = pts[-2]
    ang = math.atan2(ey - py, ex - px)
    L, hw = 125, 80
    tip = (ex + math.cos(ang) * L * 0.85, ey + math.sin(ang) * L * 0.85)
    back = (ex - math.cos(ang) * L * 0.15, ey - math.sin(ang) * L * 0.15)
    nx, ny = -math.sin(ang), math.cos(ang)
    c.poly(T([tip, (back[0] + nx * hw, back[1] + ny * hw), (back[0] - nx * hw, back[1] - ny * hw)]), SIGN_BLACK + (255,))
    c.finish('T_Sign_1_11_1' if right else 'T_Sign_1_11_2')


def sign_1_34(right=True, n=1):
    H = 500
    W = {1: 615, 2: 1160}[n]
    c = SignCanvas(W, H)
    c.rrect(-W / 2, -H / 2, W / 2, H / 2, 30, SIGN_WHITE + (255,))
    c.rrect(-W / 2 + 10, -H / 2 + 10, W / 2 - 10, H / 2 - 10, 22, SIGN_RED + (255,))
    s = 1 if right else -1
    pitch = (W - 20) / n
    for i in range(n):
        cx = -W / 2 + 10 + pitch * (i + 0.5)
        # chevron arrow: thick > shape
        t, h, hw = 135, 195, 150
        pts = [(cx - hw, h), (cx - hw + t, h), (cx + hw, 0), (cx - hw + t, -h), (cx - hw, -h), (cx + hw - t, 0)]
        c.poly([(cx + (x - cx) * s, y) for x, y in pts], SIGN_WHITE + (255,))
    c.finish(f"T_Sign_1_34_{'1' if right else '2'}_x{n}")


def car_rear(c, cx, col, k=1.0):
    """Car seen from behind (pictogram of 3.20), width ~180 mm x k."""
    c0 = c
    class _S:
        def poly(self, pts, fill): c0.poly([(cx + (x - cx) * k, y * k) for x, y in pts], fill)
        def rrect(self, x0, y0, x1, y1, r, fill): c0.rrect(cx + (x0 - cx) * k, y0 * k, cx + (x1 - cx) * k, y1 * k, r * k, fill)
    c = _S()
    body = [(-92, -40), (92, -40), (92, 10), (78, 22), (55, 30), (40, 78), (-40, 78), (-55, 30), (-78, 22), (-92, 10)]
    c.poly([(cx + x, y) for x, y in body], col)
    for wx in (-62, 62):
        c.rrect(cx + wx - 20, -82, cx + wx + 20, -36, 8, col)
    c.poly([(cx - 34, 36), (cx + 34, 36), (cx + 28, 68), (cx - 28, 68)], SIGN_WHITE + (255,))   # rear window
    for lx in (-74, 74):
        c.rrect(cx + lx - 12, -6, cx + lx + 12, 10, 3, SIGN_WHITE + (255,))                      # tail lights
    c.rrect(cx - 30, -30, cx + 30, -12, 3, SIGN_WHITE + (255,))                                   # number plate


def sign_3_20():
    D = 700
    c = SignCanvas(D, D)
    c.circle(D / 2, SIGN_WHITE + (255,))
    c.circle(D / 2 - 10, SIGN_RED + (255,))
    c.circle(D / 2 - 10 - 70, SIGN_WHITE + (255,))
    car_rear(c, -112, SIGN_RED + (255,), 1.15)
    car_rear(c, 112, SIGN_BLACK + (255,), 1.15)
    c.finish('T_Sign_3_20')


def sign_6_13(front='17', back='23'):
    W, H = 300, 200          # типоразмер II
    for name, num in ((f'T_Sign_6_13_{front}', front), (f'T_Sign_6_13_{back}', back)):
        c = SignCanvas(W, H)
        c.rrect(-W / 2, -H / 2, W / 2, H / 2, 12, SIGN_WHITE + (255,))
        c.rrect(-W / 2 + 12, -H / 2 + 12, W / 2 - 12, H / 2 - 12, 8, SIGN_BLACK + (255,))
        c.rrect(-W / 2 + 22, -H / 2 + 22, W / 2 - 22, H / 2 - 22, 4, SIGN_WHITE + (255,))
        c.text(num, 130, SIGN_BLACK + (255,), cy=0)
        c.finish(name)


ALL = {
    'asphalt': asphalt, 'gravel': gravel, 'grass': grass_ground,
    'grass_dry': lambda: grass_ground('T_GrassDry', 35, dry=0.55), 'soil': soil, 'concrete': concrete,
    'marking': marking, 'rubber': rubber_crumb,
    'rubber_green': lambda: rubber_crumb('T_RubberCrumbGreen', ((40, 110, 50), (30, 90, 40), (60, 130, 60)), 72),
    'wood': wood_planks, 'galvanized': galvanized,
    'bark_birch': bark_birch, 'bark_birch_base': bark_birch_base, 'bark_pine': bark_pine,
    'bark_spruce': bark_spruce, 'bark_dark': bark_dark,
    'leaves_birch': lambda: foliage_atlas('T_Leaves_Birch', 'birch',
                                          [(70, 120, 40), (88, 136, 46), (60, 104, 36), (104, 146, 54)], True, 340, 500),
    'leaves_linden': lambda: foliage_atlas('T_Leaves_Linden', 'linden',
                                           [(46, 92, 30), (58, 106, 36), (40, 80, 26), (70, 116, 40)], False, 200, 600),
    'leaves_bush': lambda: foliage_atlas('T_Leaves_Bush', 'bush',
                                         [(44, 88, 30), (56, 100, 34), (36, 74, 24)], False, 300, 700),
    'needles_spruce': spruce_atlas, 'needles_pine': pine_atlas, 'grass_blades': grass_blades,
    'sign_1_11_1': lambda: sign_1_11(True), 'sign_1_11_2': lambda: sign_1_11(False),
    'sign_1_34_1': lambda: (sign_1_34(True, 1), sign_1_34(True, 2)),
    'sign_1_34_2': lambda: (sign_1_34(False, 1), sign_1_34(False, 2)),
    'sign_3_20': sign_3_20, 'sign_6_13': sign_6_13,
}


def preview(names):
    """Contact sheet of the generated textures (for review only)."""
    files = [OUT / f'{n}.png' for n in names if (OUT / f'{n}.png').exists()]
    thumbs = []
    for f in files:
        im = Image.open(f).convert('RGBA')
        im.thumbnail((360, 360))
        bg = Image.new('RGBA', (370, 390), (120, 160, 200, 255))
        bg.alpha_composite(im, ((370 - im.width) // 2, 5))
        ImageDraw.Draw(bg).text((5, 372), f.stem, fill=(0, 0, 0, 255))
        thumbs.append(bg)
    cols = 6
    rows = (len(thumbs) + cols - 1) // cols
    sheet = Image.new('RGBA', (cols * 370, rows * 390), (255, 255, 255, 255))
    for i, t in enumerate(thumbs):
        sheet.alpha_composite(t, ((i % cols) * 370, (i // cols) * 390))
    sheet.convert('RGB').save(OUT.parent / 'textures_preview.png')


if __name__ == '__main__':
    args = sys.argv[1:]
    only = args[args.index('--only') + 1].split(',') if '--only' in args else list(ALL)
    for k in only:
        ALL[k]()
    if '--preview' in args:
        preview(sorted(p.stem for p in OUT.glob('T_*.png')))
