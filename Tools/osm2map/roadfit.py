"""도로 폭을 건물에 맞춘다: 건물을 침범하는 구간은 꼭짓점별 폭(wv)을 줄인다."""
import json, math, sys, collections
m = json.load(open(sys.argv[1]))
def pts(p): return list(zip(p[0::2], p[1::2]))
def seg_dist(px, pz, a, b):
    ax, az = a; bx, bz = b; dx, dz = bx - ax, bz - az; L = dx * dx + dz * dz
    t = 0 if L == 0 else max(0, min(1, ((px - ax) * dx + (pz - az) * dz) / L))
    return math.hypot(px - ax - t * dx, pz - az - t * dz)
def cross(o, a, b): return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
def seg_inter(a, b, c, d):
    d1, d2, d3, d4 = cross(c, d, a), cross(c, d, b), cross(a, b, c), cross(a, b, d)
    return (d1 > 0) != (d2 > 0) and (d3 > 0) != (d4 > 0)
def inside(pt, poly):
    x, z = pt; c = False
    for i in range(len(poly)):
        x1, z1 = poly[i]; x2, z2 = poly[i - 1]
        if (z1 > z) != (z2 > z) and x < (x2 - x1) * (z - z1) / (z2 - z1 + 1e-12) + x1: c = not c
    return c
CELL = 20.0
grid = collections.defaultdict(list)
for b in m['buildings']:
    P = pts(b['p']); xs = [q[0] for q in P]; zs = [q[1] for q in P]
    for i in range(int(math.floor(min(xs) / CELL)), int(math.floor(max(xs) / CELL)) + 1):
        for j in range(int(math.floor(min(zs) / CELL)), int(math.floor(max(zs) / CELL)) + 1):
            grid[(i, j)].append(P)
def clearance(a, b, reach):
    best = reach
    seen = set()
    for i in range(int(math.floor((min(a[0], b[0]) - reach) / CELL)), int(math.floor((max(a[0], b[0]) + reach) / CELL)) + 1):
        for j in range(int(math.floor((min(a[1], b[1]) - reach) / CELL)), int(math.floor((max(a[1], b[1]) + reach) / CELL)) + 1):
            for P in grid.get((i, j), []):
                if id(P) in seen: continue
                seen.add(id(P))
                for k in range(len(P)):
                    c, d = P[k], P[(k + 1) % len(P)]
                    if seg_inter(a, b, c, d): return 0.0
                    best = min(best, seg_dist(c[0], c[1], a, b), seg_dist(a[0], a[1], c, d), seg_dist(b[0], b[1], c, d))
                if inside(a, P) or inside(b, P): return 0.0
    return best
changed = 0
for r in m['roads']:
    if r['t'] in ('footway', 'path', 'steps', 'track') or r.get('tu'): continue
    P = pts(r['p']); hw = r['w'] / 2
    wv = [r['w']] * len(P)
    minhw = 1.6 if r['t'] in ('service', 'living_street', 'pedestrian') else 2.4
    for i in range(len(P) - 1):
        c = clearance(P[i], P[i + 1], hw + 0.5)
        if c < hw + 0.3:
            nh = max(minhw, c - 0.3)
            if nh < hw:
                wv[i] = min(wv[i], nh * 2); wv[i + 1] = min(wv[i + 1], nh * 2)
    if min(wv) < r['w'] - 0.01:
        r['wv'] = [round(v, 2) for v in wv]; changed += 1
print('roads narrowed', changed, 'of', len(m['roads']))
json.dump(m, open(sys.argv[2], 'w'), ensure_ascii=False, separators=(',', ':'))
