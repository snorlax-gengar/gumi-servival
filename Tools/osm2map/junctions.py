"""교차로 목록: 3갈래 이상 만나는 지점과 그 지점의 최대 도로 반폭. fill/shops가 교차로 한가운데에 건물을 세우지 않게 쓴다."""
import collections
def junctions(m, min_degree=3):
    deg = collections.Counter(); hw = collections.defaultdict(float)
    for r in m['roads']:
        if r['t'] in ('footway', 'path', 'steps', 'track') or r.get('tu'): continue
        P = list(zip(r['p'][0::2], r['p'][1::2]))
        for i, q in enumerate(P):
            k = (round(q[0], 1), round(q[1], 1))
            deg[k] += 1 if i in (0, len(P) - 1) else 2
            hw[k] = max(hw[k], r['w'] / 2)
    return [(k[0], k[1], hw[k], deg[k]) for k, d in deg.items() if d >= min_degree]
def junction_grid(js, cell=20.0):
    import math
    g = collections.defaultdict(list)
    for j in js:
        for i in range(int(math.floor((j[0] - 30) / cell)), int(math.floor((j[0] + 30) / cell)) + 1):
            for k in range(int(math.floor((j[1] - 30) / cell)), int(math.floor((j[1] + 30) / cell)) + 1):
                g[(i, k)].append(j)
    return g
def near_junction(g, poly, cell=20.0, extra=4.0):
    """건물 꼭짓점·중심이 교차로 반경(최대 반폭 + 여유) 안에 들어오면 True. 5갈래 이상은 더 넓게."""
    import math
    cx = sum(q[0] for q in poly) / len(poly); cz = sum(q[1] for q in poly) / len(poly)
    for j in g.get((int(math.floor(cx / cell)), int(math.floor(cz / cell))), []):
        rad = j[2] + extra + (6.0 if j[3] >= 5 else 2.0 if j[3] >= 4 else 0.0)
        for q in poly + [(cx, cz)]:
            if math.hypot(q[0] - j[0], q[1] - j[1]) < rad: return True
    return False
