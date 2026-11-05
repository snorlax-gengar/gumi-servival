"""Add procedural filler buildings along streets where OSM has no buildings, and pick the safehouse."""
import json, math, random, sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from junctions import junctions, junction_grid, near_junction
m = json.load(open(sys.argv[1]))
rng = random.Random(1979)
CELL = 20.0
def pts(p): return list(zip(p[0::2], p[1::2]))
grid = {}
def key(x, z): return (int(math.floor(x / CELL)), int(math.floor(z / CELL)))
def add(kind, poly, ref):
    xs = [p[0] for p in poly]; zs = [p[1] for p in poly]
    for i in range(int(math.floor(min(xs) / CELL)), int(math.floor(max(xs) / CELL)) + 1):
        for j in range(int(math.floor(min(zs) / CELL)), int(math.floor(max(zs) / CELL)) + 1):
            grid.setdefault((i, j), []).append((kind, poly, ref))
def seg_dist(px, pz, a, b):
    ax, az = a; bx, bz = b; dx, dz = bx - ax, bz - az
    L = dx * dx + dz * dz
    t = 0 if L == 0 else max(0, min(1, ((px - ax) * dx + (pz - az) * dz) / L))
    return math.hypot(px - ax - t * dx, pz - az - t * dz)
def inside(pt, poly):
    x, z = pt; c = False
    for i in range(len(poly)):
        x1, z1 = poly[i]; x2, z2 = poly[i - 1]
        if (z1 > z) != (z2 > z) and x < (x2 - x1) * (z - z1) / (z2 - z1 + 1e-12) + x1: c = not c
    return c
for b in m['buildings']: add('b', pts(b['p']), b)
BLOCK_AREAS = ('water', 'wood', 'park', 'pitch', 'parking', 'schoolyard', 'farm', 'grass', 'plaza', 'railyard', 'platform')
for a in m['areas']:
    if a['k'] in BLOCK_AREAS: add('a', pts(a['p']), a)
segs = []
for r in m['roads']:
    p = pts(r['p'])
    for i in range(len(p) - 1): segs.append((p[i], p[i + 1], r['w'], r))
for l in m['lines']:
    p = pts(l['p'])
    for i in range(len(p) - 1): segs.append((p[i], p[i + 1], l['w'] + (6 if l['k'] == 'rail' else 2), l))
seggrid = {}
for s in segs:
    (ax, az), (bx, bz) = s[0], s[1]
    for i in range(int(math.floor(min(ax, bx) / CELL)) - 1, int(math.floor(max(ax, bx) / CELL)) + 2):
        for j in range(int(math.floor(min(az, bz) / CELL)) - 1, int(math.floor(max(az, bz) / CELL)) + 2):
            seggrid.setdefault((i, j), []).append(s)
def rect(cx, cz, ux, uz, w, d):
    vx, vz = -uz, ux   # perpendicular
    hw, hd = w / 2, d / 2
    return [(cx + ux * sx * hw + vx * sz * hd, cz + uz * sx * hw + vz * sz * hd) for sx, sz in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
def poly_overlap(A, B):
    # separating axis for convex polys
    for P in (A, B):
        for i in range(len(P)):
            x1, z1 = P[i]; x2, z2 = P[(i + 1) % len(P)]
            nx, nz = z2 - z1, x1 - x2
            pa = [nx * x + nz * z for x, z in A]; pb = [nx * x + nz * z for x, z in B]
            if max(pa) < min(pb) or max(pb) < min(pa): return False
    return True
JG = junction_grid(junctions(m))
def free(poly, margin=1.0):
    if near_junction(JG, list(poly)): return False
    cx = sum(p[0] for p in poly) / 4; cz = sum(p[1] for p in poly) / 4
    k = key(cx, cz)
    for i in range(k[0] - 2, k[0] + 3):
        for j in range(k[1] - 2, k[1] + 3):
            for kind, other, ref in grid.get((i, j), []):
                if kind == 'a':
                    if inside((cx, cz), other) or any(inside(p, other) for p in poly): return False
                else:
                    if poly_overlap(poly, other) or inside((cx, cz), other): return False
            for s in seggrid.get((i, j), []):
                for p in poly + [(cx, cz)]:
                    if seg_dist(p[0], p[1], s[0], s[1]) < s[2] / 2 + margin: return False
    return True
HX, HZ = 1120, 1100
FILL = {'residential': 1.0, 'living_street': 1.0, 'tertiary': 0.9, 'unclassified': 0.7, 'secondary': 0.8, 'service': 0.35}
added = 0
for r in m['roads']:
    pr = FILL.get(r['t'])
    if not pr: continue
    p = pts(r['p'])
    for i in range(len(p) - 1):
        (ax, az), (bx, bz) = p[i], p[i + 1]
        L = math.hypot(bx - ax, bz - az)
        if L < 8: continue
        ux, uz = (bx - ax) / L, (bz - az) / L
        for side in (-1, 1):
            t = rng.uniform(2, 6)
            while t < L - 4:
                w = rng.uniform(9, 15); d = rng.uniform(10, 16)
                setback = rng.uniform(1.5, 3.5)
                off = r['w'] / 2 + setback + d / 2
                cx = ax + ux * (t + w / 2) + (-uz) * side * off
                cz = az + uz * (t + w / 2) + ux * side * off
                if abs(cx) < HX and abs(cz) < HZ and rng.random() < pr:
                    poly = rect(cx, cz, ux, uz, w, d)
                    if free(poly):
                        # CCW
                        a2 = sum(poly[k][0] * poly[(k + 1) % 4][1] - poly[(k + 1) % 4][0] * poly[k][1] for k in range(4))
                        if a2 < 0: poly.reverse()
                        big = r['t'] in ('tertiary', 'secondary')
                        lv = rng.randint(3, 6) if big else rng.choice([2, 3, 3, 4, 4, 5])
                        b = {'k': 'shop' if big and rng.random() < 0.7 else 'villa', 'lv': lv, 'n': '', 'a': '', 'f': 1,
                             'p': [round(c, 2) for q in poly for c in q]}
                        m['buildings'].append(b); add('b', poly, b); added += 1
                t += w + rng.uniform(1.5, 4)
print('filler', added, 'total', len(m['buildings']))

# ---------- safehouse: rectangular-ish mid-size building near old town, facing a residential road ----------
TARGET = (-150.0, 280.0)
def obb(poly):
    best = None
    for i in range(len(poly)):
        x1, z1 = poly[i]; x2, z2 = poly[(i + 1) % len(poly)]
        L = math.hypot(x2 - x1, z2 - z1)
        if L < 1e-6: continue
        ux, uz = (x2 - x1) / L, (z2 - z1) / L
        us = [x * ux + z * uz for x, z in poly]; vs = [-x * uz + z * ux for x, z in poly]
        A = (max(us) - min(us)) * (max(vs) - min(vs))
        if best is None or A < best[0]:
            cu, cv = (max(us) + min(us)) / 2, (max(vs) + min(vs)) / 2
            best = (A, ux, uz, max(us) - min(us), max(vs) - min(vs), cu * ux - cv * uz, cu * uz + cv * ux)
    return best
def parea(poly): return abs(sum(poly[i][0] * poly[i - 1][1] - poly[i - 1][0] * poly[i][1] for i in range(len(poly)))) / 2
cands = []
for b in m['buildings']:
    if b.get('f') or b['k'] not in ('gen', 'house', 'villa'): continue
    poly = pts(b['p'])
    A = parea(poly)
    if not (70 < A < 220): continue
    o = obb(poly)
    if A / o[0] < 0.88 or min(o[3], o[4]) < 7: continue
    cx, cz = o[5], o[6]
    dist = math.hypot(cx - TARGET[0], cz - TARGET[1])
    if dist > 250: continue
    # nearest road (residential-ish) and direction to it
    bestr = None
    for s in segs:
        if s[3].get('t') not in ('residential', 'living_street', 'tertiary', 'unclassified', 'service'): continue
        dd = seg_dist(cx, cz, s[0], s[1])
        if bestr is None or dd < bestr[0]: bestr = (dd, s)
    if bestr is None or bestr[0] > max(o[3], o[4]) / 2 + 8: continue
    cands.append((dist, b, o, bestr))
cands.sort(key=lambda c: c[0])
dist, b, o, (rd, s) = cands[0]
A_, ux, uz, W, D, cx, cz = o
# road direction from center
(ax, az), (bx, bz) = s[0], s[1]
dx, dz = bx - ax, bz - az; L2 = dx * dx + dz * dz
t = max(0, min(1, ((cx - ax) * dx + (cz - az) * dz) / L2)); rx, rz = ax + t * dx, az + t * dz
fx, fz = rx - cx, rz - cz; fl = math.hypot(fx, fz); fx, fz = fx / fl, fz / fl
# choose OBB axis closest to the road direction as the front normal; w = along the facade
if abs(fx * ux + fz * uz) > abs(-fx * uz + fz * ux):
    nx, nz = (ux, uz) if fx * ux + fz * uz > 0 else (-ux, -uz); w, d = D, W
else:
    nx, nz = (-uz, ux) if -fx * uz + fz * ux > 0 else (uz, -ux); w, d = W, D
yaw = math.degrees(math.atan2(-nx, -nz))
b['safe'] = 1
m['safehouse'] = {'x': round(cx, 2), 'z': round(cz, 2), 'yaw': round(yaw, 1), 'w': round(w, 2), 'd': round(d, 2),
                  'rx': round(rx, 2), 'rz': round(rz, 2)}
print('safehouse', m['safehouse'], 'cands', len(cands), 'dist', round(dist))
json.dump(m, open(sys.argv[2], 'w'), ensure_ascii=False, separators=(',', ':'))
