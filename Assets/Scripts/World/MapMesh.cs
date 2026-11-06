using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 코드로 메시를 쌓는 도구. 서브메시(머티리얼)별 삼각형 목록을 가진다.
/// 삼각형 앞면은 원하는 법선 방향을 주면 알아서 감는 방향을 맞춘다.
/// </summary>
public class MeshBuilder
{
    public readonly List<Vector3> verts = new List<Vector3>();
    public readonly List<Vector3> normals = new List<Vector3>();
    public readonly List<Vector2> uvs = new List<Vector2>();
    public readonly List<int>[] subs;

    public MeshBuilder(int submeshes)
    {
        subs = new List<int>[submeshes];
        for (int i = 0; i < submeshes; i++) subs[i] = new List<int>();
    }

    public int Count => verts.Count;
    public bool Empty => verts.Count == 0;

    public int V(Vector3 p, Vector3 n, Vector2 uv)
    {
        verts.Add(p); normals.Add(n); uvs.Add(uv);
        return verts.Count - 1;
    }

    public void Tri(int sub, int a, int b, int c, Vector3 want)
    {
        Vector3 n = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
        var s = subs[sub];
        if (Vector3.Dot(n, want) >= 0) { s.Add(a); s.Add(b); s.Add(c); }
        else { s.Add(a); s.Add(c); s.Add(b); }
    }

    /// <summary>a-b-c-d 순서로 도는 사각형</summary>
    public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n,
        Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
    {
        int ia = V(a, n, ua), ib = V(b, n, ub), ic = V(c, n, uc), id = V(d, n, ud);
        Tri(sub, ia, ib, ic, n);
        Tri(sub, ia, ic, id, n);
    }

    /// <summary>비어 있지 않은 서브메시만 담은 메시와, 각 서브메시가 원래 몇 번이었는지</summary>
    public Mesh ToMesh(string name, out List<int> usedSubs)
    {
        usedSubs = new List<int>();
        for (int i = 0; i < subs.Length; i++) if (subs[i].Count > 0) usedSubs.Add(i);
        var m = new Mesh { name = name };
        if (verts.Count > 65000) m.indexFormat = IndexFormat.UInt32;
        m.SetVertices(verts);
        m.SetNormals(normals);
        m.SetUVs(0, uvs);
        m.subMeshCount = usedSubs.Count;
        for (int i = 0; i < usedSubs.Count; i++) m.SetTriangles(subs[usedSubs[i]], i, false);
        m.RecalculateBounds();
        return m;
    }

    /// <summary>메시를 가진 오브젝트를 만든다 (materials는 서브메시 번호 순서의 전체 목록)</summary>
    public GameObject Build(string name, Transform parent, Vector3 pos, Material[] materials, bool collider, bool shadows = true)
    {
        if (Empty) return null;
        var mesh = ToMesh(name, out var used);
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        var mats = new Material[used.Count];
        for (int i = 0; i < used.Count; i++) mats[i] = materials[used[i]];
        mr.sharedMaterials = mats;
        if (!shadows) mr.shadowCastingMode = ShadowCastingMode.Off;
        if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
        return go;
    }

    // ---------------- 평면 도형 ----------------

    /// <summary>바닥에 깔리는 다각형 (위를 보는 면)</summary>
    public void Polygon(int sub, IList<Vector2> pts, float y, Vector3 origin, float uvScale = 0.1f, bool facingDown = false)
    {
        var tris = Poly.Triangulate(pts);
        int start = verts.Count;
        Vector3 nrm = facingDown ? Vector3.down : Vector3.up;
        for (int i = 0; i < pts.Count; i++)
            V(new Vector3(pts[i].x - origin.x, y, pts[i].y - origin.z), nrm, pts[i] * uvScale);
        for (int i = 0; i + 2 < tris.Count; i += 3)
            Tri(sub, start + tris[i], start + tris[i + 1], start + tris[i + 2], nrm);
    }

    /// <summary>
    /// 선을 따라가는 띠 (도로, 하천, 차선). offset은 진행 방향 왼쪽으로 미는 거리.
    /// </summary>
    public void Strip(int sub, IList<Vector2> p, float halfW, float y, Vector3 origin, float offset = 0f,
        IList<float> halfWs = null, IList<float> offsets = null)
    {
        // 너무 가까운 점은 건너뛴다 (꼭짓점별 폭/오프셋도 같이)
        var pts = new List<Vector2>(p.Count);
        var hws = new List<float>(p.Count);
        var offs = new List<float>(p.Count);
        for (int i = 0; i < p.Count; i++)
            if (pts.Count == 0 || (p[i] - pts[pts.Count - 1]).sqrMagnitude > 0.01f)
            {
                pts.Add(p[i]);
                hws.Add(halfWs != null && i < halfWs.Count ? halfWs[i] : halfW);
                offs.Add(offsets != null && i < offsets.Count ? offsets[i] : offset);
            }
        if (pts.Count < 2) return;

        float dist = 0f;
        int prevL = -1, prevR = -1;
        for (int i = 0; i < pts.Count; i++)
        {
            Vector2 dPrev = i > 0 ? (pts[i] - pts[i - 1]).normalized : (pts[1] - pts[0]).normalized;
            Vector2 dNext = i < pts.Count - 1 ? (pts[i + 1] - pts[i]).normalized : dPrev;
            Vector2 nPrev = new Vector2(-dPrev.y, dPrev.x), nNext = new Vector2(-dNext.y, dNext.x);
            Vector2 miter = (nPrev + nNext);
            if (miter.sqrMagnitude < 1e-4f) miter = nNext; else miter.Normalize();
            float scale = 1f / Mathf.Max(Vector2.Dot(miter, nNext), 0.4f);
            if (i > 0) dist += (pts[i] - pts[i - 1]).magnitude;

            Vector2 l = pts[i] + miter * (offs[i] + hws[i]) * scale;
            Vector2 r = pts[i] + miter * (offs[i] - hws[i]) * scale;
            float v = dist / Mathf.Max(0.5f, hws[i] * 2f);
            int il = V(new Vector3(l.x - origin.x, y, l.y - origin.z), Vector3.up, new Vector2(0, v));
            int ir = V(new Vector3(r.x - origin.x, y, r.y - origin.z), Vector3.up, new Vector2(1, v));
            if (prevL >= 0)
            {
                Tri(sub, prevL, il, ir, Vector3.up);
                Tri(sub, prevL, ir, prevR, Vector3.up);
            }
            prevL = il; prevR = ir;
        }
    }

    /// <summary>점선 (차선): dash 길이, gap 간격</summary>
    public void Dashes(int sub, IList<Vector2> p, float halfW, float y, Vector3 origin, float dash, float gap, float offset = 0f)
    {
        float rem = dash;
        bool on = true;
        for (int i = 0; i + 1 < p.Count; i++)
        {
            Vector2 a = p[i], b = p[i + 1];
            float L = (b - a).magnitude;
            if (L < 0.01f) continue;
            Vector2 d = (b - a) / L, n = new Vector2(-d.y, d.x);
            float t = 0f;
            while (t < L - 1e-4f)
            {
                float step = Mathf.Min(rem, L - t);
                if (on)
                {
                    Vector2 s = a + d * t + n * offset, e = a + d * (t + step) + n * offset;
                    Vector3 o = origin;
                    Quad(sub,
                        new Vector3(s.x + n.x * halfW - o.x, y, s.y + n.y * halfW - o.z),
                        new Vector3(e.x + n.x * halfW - o.x, y, e.y + n.y * halfW - o.z),
                        new Vector3(e.x - n.x * halfW - o.x, y, e.y - n.y * halfW - o.z),
                        new Vector3(s.x - n.x * halfW - o.x, y, s.y - n.y * halfW - o.z),
                        Vector3.up, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                }
                t += step; rem -= step;
                if (rem <= 1e-4f) { on = !on; rem = on ? dash : gap; }
            }
        }
    }

    public void Disc(int sub, Vector2 c, float r, float y, Vector3 origin, int segs = 10)
    {
        int center = V(new Vector3(c.x - origin.x, y, c.y - origin.z), Vector3.up, new Vector2(0.5f, 0.5f));
        int first = -1, prev = -1;
        for (int i = 0; i < segs; i++)
        {
            float a = i / (float)segs * Mathf.PI * 2f;
            int k = V(new Vector3(c.x + Mathf.Cos(a) * r - origin.x, y, c.y + Mathf.Sin(a) * r - origin.z), Vector3.up,
                new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f));
            if (prev >= 0) Tri(sub, center, prev, k, Vector3.up);
            else first = k;
            prev = k;
        }
        Tri(sub, center, prev, first, Vector3.up);
    }
}

public static class MeshBuilderExt
{
    /// <summary>상자 (center = 바닥 중앙이 아니라 상자 중앙, yaw = 도)</summary>
    public static void Box(this MeshBuilder mb, int sub, Vector3 center, Vector3 size, float yaw)
    {
        Quaternion q = Quaternion.Euler(0, yaw, 0);
        Vector3 hx = q * new Vector3(size.x * 0.5f, 0, 0), hy = new Vector3(0, size.y * 0.5f, 0), hz = q * new Vector3(0, 0, size.z * 0.5f);
        Vector3[] n = { hx.normalized, -hx.normalized, Vector3.up, Vector3.down, hz.normalized, -hz.normalized };
        Vector3[,] faces =
        {
            { center + hx - hy - hz, center + hx + hy - hz, center + hx + hy + hz, center + hx - hy + hz },
            { center - hx - hy + hz, center - hx + hy + hz, center - hx + hy - hz, center - hx - hy - hz },
            { center - hx + hy - hz, center - hx + hy + hz, center + hx + hy + hz, center + hx + hy - hz },
            { center - hx - hy + hz, center - hx - hy - hz, center + hx - hy - hz, center + hx - hy + hz },
            { center + hx - hy + hz, center + hx + hy + hz, center - hx + hy + hz, center - hx - hy + hz },
            { center - hx - hy - hz, center - hx + hy - hz, center + hx + hy - hz, center + hx - hy - hz },
        };
        for (int f = 0; f < 6; f++)
            mb.Quad(sub, faces[f, 0], faces[f, 1], faces[f, 2], faces[f, 3], n[f],
                new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));
    }

    /// <summary>임의 회전 상자 (태양광 패널처럼 기울어진 것)</summary>
    public static void BoxRot(this MeshBuilder mb, int sub, Vector3 center, Vector3 size, Quaternion q)
    {
        Vector3 hx = q * new Vector3(size.x * 0.5f, 0, 0), hy = q * new Vector3(0, size.y * 0.5f, 0), hz = q * new Vector3(0, 0, size.z * 0.5f);
        Vector3[] n = { hx.normalized, -hx.normalized, hy.normalized, -hy.normalized, hz.normalized, -hz.normalized };
        Vector3[,] f =
        {
            { center + hx - hy - hz, center + hx + hy - hz, center + hx + hy + hz, center + hx - hy + hz },
            { center - hx - hy + hz, center - hx + hy + hz, center - hx + hy - hz, center - hx - hy - hz },
            { center - hx + hy - hz, center - hx + hy + hz, center + hx + hy + hz, center + hx + hy - hz },
            { center - hx - hy + hz, center - hx - hy - hz, center + hx - hy - hz, center + hx - hy + hz },
            { center + hx - hy + hz, center + hx + hy + hz, center - hx + hy + hz, center - hx - hy + hz },
            { center - hx - hy - hz, center - hx + hy - hz, center + hx + hy - hz, center + hx - hy - hz },
        };
        for (int k = 0; k < 6; k++)
            mb.Quad(sub, f[k, 0], f[k, 1], f[k, 2], f[k, 3], n[k], new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));
    }

    /// <summary>풀 포기: 엇갈린 두 장의 판 (양면 재질)</summary>
    public static void Tuft(this MeshBuilder mb, int sub, Vector3 basePos, float w, float h, float yaw)
    {
        for (int k = 0; k < 2; k++)
        {
            Quaternion q = Quaternion.Euler(0, yaw + k * 90f, 0);
            Vector3 r = q * new Vector3(w * 0.5f, 0, 0);
            Vector3 n = q * Vector3.forward;
            Vector3 up = Vector3.up * h;
            mb.Quad(sub, basePos - r, basePos - r + up, basePos + r + up, basePos + r, n,
                new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));
        }
    }
}

/// <summary>2D 다각형 도구 (x, y = 월드 x, z)</summary>
public static class Poly
{
    public static float SignedArea(IList<Vector2> p)
    {
        float a = 0;
        for (int i = 0; i < p.Count; i++)
        {
            Vector2 u = p[i], v = p[(i + 1) % p.Count];
            a += u.x * v.y - v.x * u.y;
        }
        return a * 0.5f;
    }

    public static Vector2 Centroid(IList<Vector2> p)
    {
        Vector2 s = Vector2.zero;
        for (int i = 0; i < p.Count; i++) s += p[i];
        return s / Mathf.Max(1, p.Count);
    }

    public static bool Contains(IList<Vector2> poly, Vector2 pt)
    {
        bool c = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            Vector2 a = poly[i], b = poly[j];
            if ((a.y > pt.y) != (b.y > pt.y) && pt.x < (b.x - a.x) * (pt.y - a.y) / (b.y - a.y + 1e-9f) + a.x) c = !c;
        }
        return c;
    }

    static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    /// <summary>반시계 다각형을 안쪽으로 d만큼 줄인다 (꼭짓점 이등분선, 뾰족한 곳은 제한)</summary>
    public static List<Vector2> Inset(IList<Vector2> p, float d)
    {
        var r = new List<Vector2>(p.Count);
        int n = p.Count;
        for (int i = 0; i < n; i++)
        {
            Vector2 a = p[(i + n - 1) % n], b = p[i], c = p[(i + 1) % n];
            Vector2 e1 = (b - a).normalized, e2 = (c - b).normalized;
            Vector2 n1 = new Vector2(-e1.y, e1.x), n2 = new Vector2(-e2.y, e2.x);   // 반시계 → 왼쪽이 안쪽
            Vector2 m = (n1 + n2);
            if (m.sqrMagnitude < 1e-6f) m = n2; else m.Normalize();
            float s = 1f / Mathf.Max(0.35f, Vector2.Dot(m, n2));
            r.Add(b + m * d * s);
        }
        return r;
    }

    static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Cross(b - a, p - a), d2 = Cross(c - b, p - b), d3 = Cross(a - c, p - c);
        return d1 > 1e-6f && d2 > 1e-6f && d3 > 1e-6f;
    }

    /// <summary>귀 자르기 삼각분할. 실패하면 남은 부분은 부채꼴로 채운다.</summary>
    public static List<int> Triangulate(IList<Vector2> pts)
    {
        var res = new List<int>();
        int n = pts.Count;
        if (n < 3) return res;
        var idx = new List<int>(n);
        bool ccw = SignedArea(pts) > 0;
        for (int i = 0; i < n; i++) idx.Add(ccw ? i : n - 1 - i);

        int iCur = 0, fails = 0;
        while (idx.Count > 3)
        {
            int cnt = idx.Count;
            iCur %= cnt;
            int a = idx[(iCur + cnt - 1) % cnt], b = idx[iCur], c = idx[(iCur + 1) % cnt];
            Vector2 A = pts[a], B = pts[b], C = pts[c];
            float cr = Cross(B - A, C - B);
            bool ear = cr > 1e-6f;
            if (ear)
            {
                for (int j = 0; j < cnt; j++)
                {
                    int q = idx[j];
                    if (q == a || q == b || q == c) continue;
                    if (InTri(pts[q], A, B, C)) { ear = false; break; }
                }
            }
            if (ear)
            {
                res.Add(a); res.Add(b); res.Add(c);
                idx.RemoveAt(iCur);
                fails = 0;
                continue;
            }
            if (Mathf.Abs(cr) <= 1e-6f && fails > cnt) { idx.RemoveAt(iCur); fails = 0; continue; } // 일직선 점 제거
            iCur++;
            if (++fails > cnt * 2)
            {
                for (int i = 1; i + 1 < idx.Count; i++) { res.Add(idx[0]); res.Add(idx[i]); res.Add(idx[i + 1]); }
                return res;
            }
        }
        res.Add(idx[0]); res.Add(idx[1]); res.Add(idx[2]);
        return res;
    }
}
