"""소상공인 상가(상권)정보 → 건물에 실제 상호·층 정보 붙이기. 매칭되는 건물이 없으면 도로를 보는 건물을 새로 세운다."""
import csv, json, math, sys, random, collections
m = json.load(open(sys.argv[1]))
LAT0, LON0 = m['lat0'], m['lon0']
KX = math.cos(math.radians(LAT0)) * 111320.0; KZ = 110540.0
def xz(lon, lat): return ((lon - LON0) * KX, (lat - LAT0) * KZ)
def pts(p): return list(zip(p[0::2], p[1::2]))
def inside(pt, poly):
    x, z = pt; c = False
    for i in range(len(poly)):
        x1, z1 = poly[i]; x2, z2 = poly[i - 1]
        if (z1 > z) != (z2 > z) and x < (x2 - x1) * (z - z1) / (z2 - z1 + 1e-12) + x1: c = not c
    return c
def seg_dist(px, pz, a, b):
    ax, az = a; bx, bz = b; dx, dz = bx - ax, bz - az; L = dx * dx + dz * dz
    t = 0 if L == 0 else max(0, min(1, ((px - ax) * dx + (pz - az) * dz) / L))
    return math.hypot(px - ax - t * dx, pz - az - t * dz)
def poly_overlap(A, B):
    for P in (A, B):
        for i in range(len(P)):
            x1, z1 = P[i]; x2, z2 = P[(i + 1) % len(P)]
            nx, nz = z2 - z1, x1 - x2
            pa = [nx * x + nz * z for x, z in A]; pb = [nx * x + nz * z for x, z in B]
            if max(pa) < min(pb) or max(pb) < min(pa): return False
    return True
CELL = 20.0
def ck(x, z): return (int(math.floor(x / CELL)), int(math.floor(z / CELL)))
bgrid = collections.defaultdict(list)
def addb(b):
    P = pts(b['p']); xs = [q[0] for q in P]; zs = [q[1] for q in P]
    for i in range(int(math.floor(min(xs) / CELL)), int(math.floor(max(xs) / CELL)) + 1):
        for j in range(int(math.floor(min(zs) / CELL)), int(math.floor(max(zs) / CELL)) + 1):
            bgrid[(i, j)].append((b, P))
for b in m['buildings']: addb(b)
sgrid = collections.defaultdict(list)
for r in m['roads']:
    if r['t'] in ('motorway', 'motorway_link', 'footway', 'path', 'steps', 'track') or r.get('tu'): continue
    P = pts(r['p'])
    for i in range(len(P) - 1):
        a, b = P[i], P[i + 1]
        for ci in range(int(math.floor(min(a[0], b[0]) / CELL)) - 1, int(math.floor(max(a[0], b[0]) / CELL)) + 2):
            for cj in range(int(math.floor(min(a[1], b[1]) / CELL)) - 1, int(math.floor(max(a[1], b[1]) / CELL)) + 2):
                sgrid[(ci, cj)].append((a, b, r['w'] / 2))
allsegs = []
for r in m['roads']:
    P = pts(r['p'])
    for i in range(len(P) - 1): allsegs.append((P[i], P[i + 1], r['w'] / 2))
lines = [(q[0], q[1], l['w'] / 2 + 2) for l in m['lines'] for q in [(pts(l['p'])[i], pts(l['p'])[i + 1]) for i in range(len(pts(l['p'])) - 1)]]

# ---------- 상가 읽기 ----------
groups = collections.OrderedDict()
with open(sys.argv[2], encoding='utf-8') as fh:
    for r in csv.DictReader(fh):
        x, z = xz(float(r['경도']), float(r['위도']))
        if abs(x) > 1115 or abs(z) > 1095: continue
        key = r['건물관리번호'] or f"{round(x)}_{round(z)}"
        g = groups.setdefault(key, {'x': [], 'z': [], 'shops': [], 'bn': r['건물명']})
        g['x'].append(x); g['z'].append(z)
        fl = r['층정보'].strip()
        try: f = int(fl)
        except: f = 0
        if f > 30: f = 0
        if fl.startswith('B') or fl.startswith('지하'): f = -1
        name = r['상호명'].strip()
        if r['지점명'].strip() and len(name) + len(r['지점명']) < 14: name += ' ' + r['지점명'].strip()
        g['shops'].append({'n': name[:16], 'c': r['상권업종소분류명'].strip(), 'm': r['상권업종대분류명'].strip(), 'f': f})
print('shop groups', len(groups), 'shops', sum(len(g['shops']) for g in groups.values()))

rng = random.Random(2052)
matched = created = attached = skipped = 0
def near_building(x, z, maxd):
    best = None
    for i in range(ck(x, z)[0] - 1, ck(x, z)[0] + 2):
        for j in range(ck(x, z)[1] - 1, ck(x, z)[1] + 2):
            for b, P in bgrid.get((i, j), []):
                if inside((x, z), P): return b, 0
                d = min(seg_dist(x, z, P[k], P[(k + 1) % len(P)]) for k in range(len(P)))
                if d < maxd and (best is None or d < best[1]): best = (b, d)
    return best if best else (None, None)
def nearest_road(x, z):
    best = None
    k = ck(x, z)
    for i in range(k[0] - 2, k[0] + 3):
        for j in range(k[1] - 2, k[1] + 3):
            for a, b, hw in sgrid.get((i, j), []):
                d = seg_dist(x, z, a, b) - hw
                if best is None or d < best[0]: best = (d, a, b, hw)
    return best
def free(poly):
    cx = sum(q[0] for q in poly) / 4; cz = sum(q[1] for q in poly) / 4
    k = ck(cx, cz)
    for i in range(k[0] - 2, k[0] + 3):
        for j in range(k[1] - 2, k[1] + 3):
            for b, P in bgrid.get((i, j), []):
                if poly_overlap(poly, P): return False
            for a, bb, hw in sgrid.get((i, j), []):
                for q in poly:
                    if seg_dist(q[0], q[1], a, bb) < hw + 0.8: return False
    for a, bb, hw in lines:
        if seg_dist(cx, cz, a, bb) < hw + 8: 
            for q in poly:
                if seg_dist(q[0], q[1], a, bb) < hw: return False
    return True

def attach(b, g):
    shops = sorted(g['shops'], key=lambda s: (s['f'] if s['f'] > 0 else 1))
    b.setdefault('shops', []).extend(shops)
    maxf = max([s['f'] for s in shops] + [1])
    if b['k'] in ('gen', 'villa', 'house', 'roof', 'shop'):
        if any(s['f'] in (0, 1) for s in shops) and b['k'] != 'roof': b['k'] = 'shop'
        lv = b['lv']
        if lv < maxf: lv = maxf
        elif lv > maxf + 3 and not b.get('lvTag'): lv = maxf + rng.randint(0, 2)
        b['lv'] = lv
    else:
        b['lv'] = max(b['lv'], maxf)
    if g['bn'] and not b.get('n'): b['n'] = g['bn'][:16]

for key, g in groups.items():
    x = sum(g['x']) / len(g['x']); z = sum(g['z']) / len(g['z'])
    b, d = near_building(x, z, 6.0)
    if b is not None:
        attach(b, g); matched += 1; continue
    nr = nearest_road(x, z)
    ok = False
    if nr and nr[0] < 40:
        d0, a, bb, hw = nr
        dx, dz = bb[0] - a[0], bb[1] - a[1]; L = math.hypot(dx, dz) or 1; ux, uz = dx / L, dz / L
        # 도로에서 건물 쪽 방향
        t = max(0, min(1, ((x - a[0]) * dx + (z - a[1]) * dz) / (L * L)))
        px, pz = a[0] + dx * t, a[1] + dz * t
        nx, nz = x - px, z - pz; nl = math.hypot(nx, nz)
        if nl < 0.1: nx, nz = -uz, ux
        else: nx, nz = nx / nl, nz / nl
        nground = sum(1 for s in g['shops'] if s['f'] in (0, 1))
        for scale in (1.0, 0.8, 0.62):
            w = min(22, max(9, 7 + 3 * nground)) * scale; dd = rng.uniform(11, 15) * scale
            off = hw + rng.uniform(1.2, 2.5) + dd / 2
            for slide in (0, 3, -3, 6, -6):
                cx, cz = px + nx * off + ux * slide, pz + nz * off + uz * slide
                vx, vz = nx, nz
                poly = [(cx + ux * sx * w / 2 + vx * sz * dd / 2, cz + uz * sx * w / 2 + vz * sz * dd / 2) for sx, sz in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
                a2 = sum(poly[k][0] * poly[(k + 1) % 4][1] - poly[(k + 1) % 4][0] * poly[k][1] for k in range(4))
                if a2 < 0: poly.reverse()
                if free(poly):
                    nb = {'k': 'shop', 'lv': 2, 'n': '', 'a': '', 'f': 2, 'p': [round(c, 2) for q in poly for c in q]}
                    nb['lv'] = rng.randint(2, 4)
                    attach(nb, g)
                    m['buildings'].append(nb); addb(nb); created += 1; ok = True
                    break
            if ok: break
    if not ok:
        b, d = near_building(x, z, 18.0)
        if b is not None: attach(b, g); attached += 1
        else: skipped += 1
print('matched', matched, 'created', created, 'attached-near', attached, 'skipped', skipped)
withshops = [b for b in m['buildings'] if b.get('shops')]
print('buildings with shops', len(withshops), 'max floors', max(b['lv'] for b in withshops))
m['attribution'] = '© OpenStreetMap contributors · 상가정보: 소상공인시장진흥공단'
json.dump(m, open(sys.argv[3], 'w'), ensure_ascii=False, separators=(',', ':'))
