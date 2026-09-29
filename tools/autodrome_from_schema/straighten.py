"""Straighten traced polylines of the autodrome scheme (T68).

The scheme is a raster, so traced road edges and lines wobble by a few centimetres, and posts or sign glyphs
leave bumps. Straight stretches are found with Douglas-Peucker (tolerance ``eps``); a stretch at least
``min_len`` long is replaced by its least-squares line (snapped to the X or Z axis when it is within
``snap_deg``), and the curves between stretches are smoothed and blended into the lines. All in metres.
"""
import numpy as np


def _dp(p, eps):
    """Douglas-Peucker on an open polyline; returns kept indices."""
    keep = np.zeros(len(p), bool); keep[0] = keep[-1] = True
    stack = [(0, len(p) - 1)]
    while stack:
        a, b = stack.pop()
        if b <= a + 1: continue
        d = p[b] - p[a]; n = np.hypot(*d)
        seg = p[a + 1:b] - p[a]
        dist = np.abs(seg[:, 0] * d[1] - seg[:, 1] * d[0]) / n if n > 1e-9 else np.hypot(*seg.T)
        k = int(np.argmax(dist))
        if dist[k] > eps:
            m = a + 1 + k; keep[m] = True; stack += [(a, m), (m, b)]
    return np.nonzero(keep)[0]


def _fit_line(q, snap_deg):
    c = q.mean(0); u, s, vt = np.linalg.svd(q - c); d = vt[0]
    ang = np.degrees(np.arctan2(d[1], d[0])) % 180
    for axis in (0, 90, 180):
        if abs(ang - axis) <= snap_deg:
            d = np.array([1.0, 0.0]) if axis != 90 else np.array([0.0, 1.0])
            c = np.array([c[0], np.median(q[:, 1])]) if axis != 90 else np.array([np.median(q[:, 0]), c[1]])
            break
    return c, d


def _smooth(q, passes):
    q = q.copy()
    for _ in range(passes):
        if len(q) > 2: q[1:-1] = 0.25 * q[:-2] + 0.5 * q[1:-1] + 0.25 * q[2:]
    return q


def straighten(p, closed, eps=0.3, min_len=4.0, min_len_axis=1.5, snap_deg=4.0, blend=1.0, curve_passes=6, max_bump=5.0, max_bump_depth=0.8):
    p = np.asarray(p, float)
    if len(p) < 4: return p
    if closed:
        # start at the point farthest from the centroid: a corner, not the middle of a straight
        s = int(np.argmax(np.hypot(*(p - p.mean(0)).T))); p = np.roll(p, -s, 0); p = np.vstack([p, p[:1]])
    idx = _dp(p, eps)
    segs = []
    out = p.copy(); fixed = np.zeros(len(p), bool)
    for a, b in zip(idx[:-1], idx[1:]):
        d = p[b] - p[a]; length = np.hypot(*d)
        ang = np.degrees(np.arctan2(d[1], d[0])) % 90
        axis = min(ang, 90 - ang) <= snap_deg
        if length < (min_len_axis if axis else min_len): continue
        c, d = _fit_line(p[a:b + 1], snap_deg)
        q = p[a:b + 1] - c
        off = q[:, 0] * d[1] - q[:, 1] * d[0]
        rms = float(np.sqrt(np.mean(off ** 2)))
        # a curve cut into chords has a systematic offset: only well-fitting stretches become lines
        if not (rms < 0.05 or (axis and length >= min_len and rms < 0.12)): continue
        t = q @ d
        out[a:b + 1] = c + np.outer(t, d); fixed[a:b + 1] = True
        segs.append((a, b, c, d))
    # a short bump between two stretches of the same ruled line (a post or a sign glyph on the scheme) is flattened
    for (a0, b0, c0, d0), (a1, b1, c1, d1) in zip(segs[:-1], segs[1:]):
        gap = np.hypot(*(p[a1] - p[b0]))
        if a1 <= b0 or gap > max_bump: continue
        if abs(abs(d0 @ d1) - 1) > 1e-6: continue
        if abs((c1 - c0) @ np.array([-d0[1], d0[0]])) > 0.15: continue
        q = p[b0:a1 + 1] - c0
        if np.abs(q[:, 0] * d0[1] - q[:, 1] * d0[0]).max() > max_bump_depth: continue   # an opening, not a bump
        out[b0:a1 + 1] = c0 + np.outer(q @ d0, d0); fixed[b0:a1 + 1] = True
    # curves: smoothed free points, shifted so that each curve starts and ends exactly on its neighbouring lines
    sm = _smooth(p, curve_passes)
    n = len(p); i = 0
    while i < n:
        if fixed[i]: i += 1; continue
        j = i
        while j < n and not fixed[j]: j += 1
        a, b = i - 1, j            # neighbouring fixed points (may be outside for an open line)
        e0 = out[a] - sm[a] if a >= 0 else np.zeros(2)
        e1 = out[b] - sm[b] if b < n else e0
        if a < 0: e0 = e1
        m = j - i
        for k in range(m):
            t = (k + 1) / (m + 1)
            out[i + k] = sm[i + k] + e0 * (1 - t) + e1 * t
        i = j
    if closed: out = out[:-1]
    return out
