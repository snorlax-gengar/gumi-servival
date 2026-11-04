"""OSM Overpass JSON (out geom) -> compact game map JSON for Unity (JsonUtility-friendly).
Coordinates: meters, x = east, z = north, origin = bbox center."""
import json, math, sys, random, hashlib
src, dst = sys.argv[1], sys.argv[2]
d = json.load(open(src))
el = d['elements']
LAT0, LON0 = 36.128, 128.3295
KX = math.cos(math.radians(LAT0)) * 111320.0
KZ = 110540.0
def xz(p): return (round((p['lon'] - LON0) * KX, 2), round((p['lat'] - LAT0) * KZ, 2))
HX, HZ = 1126.0, 1105.0
def clip_poly(pts):
    def clip(pts, inside, inter):
        out = []
        for i in range(len(pts)):
            a, b = pts[i - 1], pts[i]
            if inside(b):
                if not inside(a): out.append(inter(a, b))
                out.append(b)
            elif inside(a): out.append(inter(a, b))
        return out
    def ix(xc):
        return lambda a, b: (xc, a[1] + (b[1] - a[1]) * (xc - a[0]) / (b[0] - a[0]))
    def iz(zc):
        return lambda a, b: (a[0] + (b[0] - a[0]) * (zc - a[1]) / (b[1] - a[1]), zc)
    for f, g in ((lambda p: p[0] >= -HX, ix(-HX)), (lambda p: p[0] <= HX, ix(HX)),
                 (lambda p: p[1] >= -HZ, iz(-HZ)), (lambda p: p[1] <= HZ, iz(HZ))):
        if len(pts) < 3: return []
        pts = clip(pts, f, g)
    return [(round(x, 2), round(z, 2)) for x, z in pts]
def clip_line(pts, m=0):
    """split polyline into pieces inside the box (keep one point beyond the edge)"""
    ins = lambda p: abs(p[0]) <= HX + m and abs(p[1]) <= HZ + m
    pieces, cur = [], []
    for i, p in enumerate(pts):
        if ins(p):
            if not cur and i > 0: cur.append(pts[i - 1])
            cur.append(p)
        else:
            if cur: cur.append(p); pieces.append(cur); cur = []
    if cur: pieces.append(cur)
    return [c for c in pieces if len(c) >= 2]
def flat(pts): return [c for p in pts for c in p]
def area(pts):
    a = 0
    for i in range(len(pts)):
        x1, z1 = pts[i]; x2, z2 = pts[(i + 1) % len(pts)]
        a += x1 * z2 - x2 * z1
    return a / 2
def centroid(pts):
    return (sum(p[0] for p in pts) / len(pts), sum(p[1] for p in pts) / len(pts))
def inside(pt, poly):
    x, z = pt; c = False; n = len(poly)
    for i in range(n):
        x1, z1 = poly[i]; x2, z2 = poly[i - 1]
        if (z1 > z) != (z2 > z) and x < (x2 - x1) * (z - z1) / (z2 - z1 + 1e-12) + x1: c = not c
    return c
def closed(e):
    g = e.get('geometry') or []
    return len(g) >= 4 and g[0]['lat'] == g[-1]['lat'] and g[0]['lon'] == g[-1]['lon']
def ring(e):
    pts = [xz(p) for p in e['geometry'][:-1]]
    # dedupe consecutive
    out = []
    for p in pts:
        if not out or abs(out[-1][0] - p[0]) + abs(out[-1][1] - p[1]) > 0.05: out.append(p)
    if area(out) < 0: out.reverse()   # CCW (x east, z north)
    return out
def rnd(eid):
    return random.Random(int(hashlib.md5(str(eid).encode()).hexdigest()[:8], 16))

ROADW = {'motorway': 14, 'trunk': 12, 'primary': 12, 'secondary': 11, 'tertiary': 9, 'unclassified': 6,
         'residential': 6, 'living_street': 5, 'service': 4, 'pedestrian': 5, 'footway': 2, 'path': 1.8,
         'steps': 2, 'track': 3, 'cycleway': 2, 'motorway_link': 7, 'tertiary_link': 7, 'secondary_link': 7, 'primary_link': 8}
LANDK = {'farmland': 'farm', 'grass': 'grass', 'flowerbed': 'grass', 'greenhouse_horticulture': 'farm', 'meadow': 'grass',
         'forest': 'wood', 'cemetery': 'grass', 'recreation_ground': 'grass', 'village_green': 'grass'}
commercial = []
for e in el:
    t = e.get('tags', {})
    if e['type'] == 'way' and t.get('landuse') in ('commercial', 'retail') and closed(e): commercial.append(ring(e))

roads, blds, areas, lines, pois = [], [], [], [], []
for e in el:
    t = e.get('tags', {})
    if e['type'] == 'node':
        n = t.get('name')
        k = t.get('shop') or t.get('amenity')
        if k and abs(xz(e)[0]) <= HX and abs(xz(e)[1]) <= HZ: pois.append({'k': k, 'n': n or '', 'x': xz(e)[0], 'z': xz(e)[1]})
        continue
    if not e.get('geometry'): continue
    if 'highway' in t:
        hw = t['highway']
        if hw in ('platform', 'bus_stop', 'construction', 'proposed', 'elevator', 'corridor'): continue
        w = ROADW.get(hw, 5)
        lanes = t.get('lanes')
        if lanes and lanes.isdigit() and hw not in ('footway', 'path'): w = max(w, int(lanes) * 3.2)
        if t.get('area') == 'yes' and closed(e):
            cp = clip_poly(ring(e))
            if len(cp) >= 3: areas.append({'k': 'plaza', 'n': '', 'p': flat(cp)})
            continue
        for piece in clip_line([xz(p) for p in e['geometry']]):
            roads.append({'t': hw, 'w': w, 'n': t.get('name', ''), 'b': 1 if t.get('bridge') == 'yes' else 0,
                          'tu': 1 if t.get('tunnel') == 'yes' else 0, 'p': flat(piece)})
        continue
    if 'building' in t and closed(e):
        pts = ring(e)
        c0 = centroid(pts)
        if len(pts) < 3 or abs(c0[0]) > HX or abs(c0[1]) > HZ: continue
        A = area(pts)
        if A < 8: continue
        bt = t['building']
        r = rnd(e['id'])
        lv = t.get('building:levels')
        try: lv = int(float(lv))
        except: lv = None
        c = centroid(pts)
        com = any(inside(c, poly) for poly in commercial)
        if lv is None:
            if bt == 'apartments': lv = r.randint(12, 20) if A > 350 else r.randint(4, 6)
            elif bt == 'house': lv = r.choice([1, 1, 2, 2, 3])
            elif bt in ('school', 'kindergarten', 'public', 'hospital'): lv = r.randint(3, 5) if bt != 'kindergarten' else 2
            elif bt in ('roof', 'carport'): lv = 1
            elif bt in ('church',): lv = 3
            else:
                if A < 60: lv = r.choice([1, 1, 2])
                elif A < 200: lv = r.randint(2, 5) if com else r.randint(2, 4)
                elif A < 800: lv = r.randint(4, 8) if com else r.randint(2, 5)
                else: lv = r.randint(3, 7) if com else r.randint(1, 3)
        kind = {'apartments': 'apt', 'house': 'house', 'school': 'school', 'kindergarten': 'school', 'roof': 'roof',
                'church': 'church', 'hospital': 'public', 'public': 'public', 'commercial': 'shop', 'retail': 'shop',
                'residential': 'villa'}.get(bt, 'shop' if com else 'gen')
        if t.get('amenity') in ('hospital', 'townhall', 'police'): kind = 'public'
        blds.append({'k': kind, 'lv': max(1, min(lv, 35)), 'n': t.get('name', ''), 'a': t.get('amenity') or t.get('shop') or '',
                     'p': flat(pts)})
        continue
    k = None
    if t.get('natural') == 'water' or t.get('landuse') in ('reservoir', 'basin') or t.get('water'): k = 'water'
    elif t.get('natural') == 'wood': k = 'wood'
    elif t.get('natural') in ('grassland', 'scrub'): k = 'grass'
    elif t.get('leisure') in ('park', 'garden', 'playground'): k = 'park'
    elif t.get('leisure') in ('pitch', 'golf_course', 'track'): k = 'pitch'
    elif t.get('amenity') in ('parking',): k = 'parking'
    elif t.get('amenity') in ('school', 'kindergarten', 'university', 'college'): k = 'schoolyard'
    elif t.get('landuse') in LANDK: k = LANDK[t['landuse']]
    elif t.get('landuse') == 'railway': k = 'railyard'
    if k and closed(e):
        cp = clip_poly(ring(e))
        if len(cp) >= 3: areas.append({'k': k, 'n': t.get('name', ''), 'p': flat(cp)})
        continue
    if t.get('waterway') in ('river', 'stream', 'canal', 'drain', 'ditch'):
        w = {'river': 30, 'stream': 8, 'canal': 8}.get(t['waterway'], 3)
        for piece in clip_line([xz(p) for p in e['geometry']]):
            lines.append({'k': 'water', 'w': w, 'n': t.get('name', ''), 'p': flat(piece)})
        continue
    if t.get('railway') in ('rail', 'disused', 'light_rail'):
        for piece in clip_line([xz(p) for p in e['geometry']]):
            lines.append({'k': 'rail', 'w': 4, 'n': t.get('name', ''), 'p': flat(piece)})
        continue
    if t.get('railway') == 'platform' and closed(e):
        areas.append({'k': 'platform', 'n': '', 'p': flat(ring(e))}); continue
    # named amenity ways (no building) -> poi
    k2 = t.get('amenity') or t.get('shop')
    if k2:
        c = centroid([xz(p) for p in e['geometry']])
        pois.append({'k': k2, 'n': t.get('name', ''), 'x': round(c[0], 1), 'z': round(c[1], 1)})

allx = [v for b in blds for v in b['p'][0::2]] + [v for r in roads for v in r['p'][0::2]]
allz = [v for b in blds for v in b['p'][1::2]] + [v for r in roads for v in r['p'][1::2]]
pois = [p for p in pois if abs(p['x']) <= HX and abs(p['z']) <= HZ]
out = {'name': 'gumi_core', 'lat0': LAT0, 'lon0': LON0,
       'minX': math.floor(min(allx)), 'maxX': math.ceil(max(allx)), 'minZ': math.floor(min(allz)), 'maxZ': math.ceil(max(allz)),
       'attribution': '© OpenStreetMap contributors (ODbL)',
       'roads': roads, 'buildings': blds, 'areas': areas, 'lines': lines, 'pois': pois}
json.dump(out, open(dst, 'w'), ensure_ascii=False, separators=(',', ':'))
print(len(roads), 'roads', len(blds), 'buildings', len(areas), 'areas', len(lines), 'lines', len(pois), 'pois',
      out['minX'], out['maxX'], out['minZ'], out['maxZ'])
