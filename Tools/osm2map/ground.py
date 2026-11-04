"""Ground texture (urban concrete vs wild grass) + tree positions."""
import json, math, random, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
m = json.load(open(sys.argv[1]))
H = 1152.0; N = 1024; S = N / (2 * H)        # px per meter
def px(x, z): return ((x + H) * S, (H - z) * S)   # image y down = north up
def pts(p): return list(zip(p[0::2], p[1::2]))
urban = Image.new('L', (N, N), 0); du = ImageDraw.Draw(urban)
bmask = Image.new('L', (N, N), 0); db = ImageDraw.Draw(bmask)
block = Image.new('L', (N, N), 0); dk = ImageDraw.Draw(block)   # no wild trees here (water, fields, pitches...)
for b in m['buildings']:
    P = [px(*q) for q in pts(b['p'])]
    db.polygon(P, fill=255)
    c = (sum(q[0] for q in P) / len(P), sum(q[1] for q in P) / len(P)); r = 16 * S
    du.ellipse([c[0] - r, c[1] - r, c[0] + r, c[1] + r], fill=255)
    du.polygon(P, fill=255)
WILD_ROADS = ('path', 'footway', 'track', 'steps', 'motorway', 'motorway_link')
for r in m['roads']:
    P = [px(*q) for q in pts(r['p'])]
    if r['t'] in WILD_ROADS: dk.line(P, fill=255, width=max(1, int(r['w'] * S + 2)))
    else: du.line(P, fill=255, width=max(1, int((r['w'] + 14) * S)))
    db.line(P, fill=255, width=max(1, int((r['w'] + 2) * S)))
for a in m['areas']:
    P = [px(*q) for q in pts(a['p'])]
    if a['k'] in ('water', 'pitch', 'schoolyard', 'parking', 'farm', 'plaza', 'railyard', 'platform'): dk.polygon(P, fill=255)
    if a['k'] in ('parking', 'plaza', 'railyard', 'platform', 'schoolyard'): du.polygon(P, fill=255)
for l in m['lines']:
    P = [px(*q) for q in pts(l['p'])]
    dk.line(P, fill=255, width=max(1, int((l['w'] + 4) * S)))
    if l['k'] == 'rail': du.line(P, fill=255, width=max(1, int((l['w'] + 10) * S)))
U = np.asarray(urban.filter(ImageFilter.GaussianBlur(3)), dtype=np.float32) / 255.0
rng = np.random.default_rng(7)
def noise(scale, amp):
    small = rng.random((N // scale + 2, N // scale + 2)).astype(np.float32)
    im = Image.fromarray((small * 255).astype(np.uint8)).resize((N + scale * 2, N + scale * 2), Image.BICUBIC)
    return (np.asarray(im, dtype=np.float32)[:N, :N] / 255.0 - 0.5) * amp
n1 = noise(64, 1.0) + noise(16, 0.6) + noise(4, 0.35)
concrete = np.array([168, 163, 152], np.float32); concrete2 = np.array([150, 146, 138], np.float32)
grass = np.array([92, 108, 66], np.float32); dirt = np.array([120, 112, 82], np.float32)
t = np.clip(n1[..., None] + 0.5, 0, 1)
wild = grass * (1 - t) + dirt * t
urb = concrete * (1 - np.clip(n1[..., None] * 0.8 + 0.5, 0, 1)) + concrete2 * np.clip(n1[..., None] * 0.8 + 0.5, 0, 1)
k = np.clip((U[..., None] - 0.35) * 3.0, 0, 1)
img = wild * (1 - k) + urb * k
img += (rng.random((N, N, 1)) - 0.5) * 8
Image.fromarray(np.clip(img, 0, 255).astype(np.uint8)).save(sys.argv[3])

# ---------------- trees ----------------
B = np.asarray(bmask) > 0; K = np.asarray(block) > 0; Uh = U > 0.4
def cell(x, z):
    i = int((H - z) * S); j = int((x + H) * S)
    return (i, j) if 0 <= i < N and 0 <= j < N else None
rr = random.Random(11)
trees = []
def add(x, z, s):
    c = cell(x, z)
    if c is None or B[c] or K[c] or abs(x) > 1120 or abs(z) > 1100: return False
    trees.append((round(x, 1), round(z, 1), s)); return True
def inside(pt, poly):
    x, z = pt; c = False
    for i in range(len(poly)):
        x1, z1 = poly[i]; x2, z2 = poly[i - 1]
        if (z1 > z) != (z2 > z) and x < (x2 - x1) * (z - z1) / (z2 - z1 + 1e-12) + x1: c = not c
    return c
# wild land (not urban): mountain slopes etc.
g = 13.0
x = -1120.0
while x < 1120:
    z = -1100.0
    while z < 1100:
        px_, pz_ = x + rr.uniform(-5, 5), z + rr.uniform(-5, 5)
        c = cell(px_, pz_)
        if c and not Uh[c] and rr.random() < 0.8: add(px_, pz_, rr.choice([0, 0, 1]))
        z += g
    x += g
nw = len(trees)
for a in m['areas']:
    if a['k'] not in ('wood', 'park', 'grass'): continue
    poly = pts(a['p']); xs = [q[0] for q in poly]; zs = [q[1] for q in poly]
    sp = 10.0 if a['k'] == 'wood' else 11.0
    pr = 0.9 if a['k'] == 'wood' else 0.45
    x = min(xs)
    while x < max(xs):
        z = min(zs)
        while z < max(zs):
            q = (x + rr.uniform(-4, 4), z + rr.uniform(-4, 4))
            if rr.random() < pr and inside(q, poly):
                c = cell(*q)
                if c and Uh[c] and a['k'] == 'wood': pass
                add(q[0], q[1], 0 if a['k'] == 'wood' else 2)
            z += sp
        x += sp
na = len(trees) - nw
# street trees (가로수): 2 = street tree (gets a trunk collider)
st = 0
for r in m['roads']:
    if r['t'] not in ('tertiary', 'secondary', 'primary'): continue
    P = pts(r['p'])
    for i in range(len(P) - 1):
        (ax, az), (bx, bz) = P[i], P[i + 1]
        L = math.hypot(bx - ax, bz - az)
        if L < 1: continue
        ux, uz = (bx - ax) / L, (bz - az) / L
        t0 = rr.uniform(3, 12)
        while t0 < L:
            for side in (-1, 1):
                off = r['w'] / 2 + 1.4
                x, z = ax + ux * t0 - uz * side * off, az + uz * t0 + ux * side * off
                c = cell(x, z)
                if c and not B[c]:
                    trees.append((round(x, 1), round(z, 1), 3)); st += 1
            t0 += 17
rr.shuffle(trees)
print('trees wild', nw, 'areas', na, 'street', st, 'total', len(trees))
m['trees'] = [v for t in trees for v in t]
json.dump(m, open(sys.argv[2], 'w'), ensure_ascii=False, separators=(',', ':'))
