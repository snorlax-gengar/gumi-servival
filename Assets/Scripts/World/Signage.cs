using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 한국 거리 간판·가게 꾸미기 도구.
///  - 밤에 켜지는 간판 머티리얼 (낮엔 그냥 색, 밤엔 빛남)
///  - 세로 간판 탑(상가 건물 모서리에 층별 가게 이름을 세로로)
///  - 돌출 간판, 창문 글씨, 현수막, 층별 안내판
///  - 편의점 정면(띠 간판·24시·파라솔 테이블·아이스크림 냉동고)과 실내(진열대·냉장고 벽·계산대)
/// 상표는 쓰지 않는다: 편의점 띠 색은 지어낸 것(청록+산호), 가게 이름만 상가정보 데이터에서.
/// </summary>
public static class Signage
{
    // ---------------- 밤에 켜지는 머티리얼 ----------------
    static readonly Dictionary<(Color, float), Material> nightMats = new Dictionary<(Color, float), Material>();
    static readonly List<(Material m, Color c, float s)> lit = new List<(Material, Color, float)>();
    static Shader shader;

    /// <summary>밤에 빛나는 색. strength = 밤 최대 밝기 배수, 낮에도 조금(day) 빛난다</summary>
    public static Material Night(Color c, float strength = 1.6f)
    {
        var key = (c, strength);
        if (nightMats.TryGetValue(key, out var m) && m != null) return m;
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        m = new Material(shader) { name = $"Night_{ColorUtility.ToHtmlStringRGB(c)}_{strength}" };
        m.SetColor("_BaseColor", c);
        m.color = c;
        m.SetFloat("_Smoothness", 0.35f);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", c * 0.05f);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        nightMats[key] = m;
        lit.Add((m, c, strength));
        return m;
    }

    /// <summary>GameManager가 Facade.SetNightGlow와 함께 부른다 (0 = 낮, 1 = 한밤)</summary>
    public static void SetNight(float k)
    {
        for (int i = lit.Count - 1; i >= 0; i--)
        {
            var e = lit[i];
            if (e.m == null) { lit.RemoveAt(i); continue; }
            e.m.SetColor("_EmissionColor", e.c * Mathf.Lerp(0.05f, e.s, k));
        }
    }

    static GameObject Panel(Transform t, Vector3 center, Vector3 size, Quaternion rot, Material m, string name = "간판", bool collider = false)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        if (!collider) Object.DestroyImmediate(g.GetComponent<Collider>());
        g.transform.SetParent(t, false);
        g.transform.localPosition = center;
        g.transform.localRotation = rot;
        g.transform.localScale = size;
        g.GetComponent<Renderer>().sharedMaterial = m;
        return g;
    }

    static string Vertical(string s, int max)
    {
        s = s.Replace(" ", "");
        if (s.Length > max) s = s.Substring(0, max);
        return string.Join("\n", s.ToCharArray());
    }

    /// <summary>
    /// 세로 간판 탑: 벽에서 수직으로 튀어나온 긴 상자. 위에서부터 층별 가게 이름을 세로 글씨로 (양쪽 면 모두).
    /// at = 벽면 위 붙는 자리(로컬, 바닥 높이), n = 벽 바깥 법선, along = 벽 방향
    /// </summary>
    public static void Tower(Transform t, Vector3 at, Vector3 n, Vector3 along, float bottom, float top, IList<(string name, Color bg)> tenants)
    {
        if (tenants.Count == 0 || top - bottom < 2f) return;
        const float depth = 0.95f, thick = 0.26f;
        var rot = Quaternion.LookRotation(n, Vector3.up);
        Vector3 c0 = at + n * (depth * 0.5f + 0.08f);
        // 뼈대 (어두운 금속 테두리)
        Panel(t, c0 + Vector3.up * ((bottom + top) * 0.5f), new Vector3(thick + 0.04f, top - bottom + 0.12f, depth + 0.06f), rot, Kit.Mat(Kit.Hex("3A3F45")), "세로간판 틀");
        int k = Mathf.Min(tenants.Count, 6);
        float seg = (top - bottom) / k;
        for (int i = 0; i < k; i++)
        {
            var (name, bg) = tenants[i];
            float y0 = top - seg * (i + 1), yc = y0 + seg * 0.5f;
            Panel(t, c0 + Vector3.up * yc, new Vector3(thick + 0.06f, seg - 0.08f, depth - 0.06f), rot, Night(bg, 1.4f), "세로간판");
            int chars = Mathf.Max(1, Mathf.FloorToInt((seg - 0.2f) / 0.36f));
            string v = Vertical(name, Mathf.Min(chars, 7));
            int lines = v.Split('\n').Length;
            float ch = Mathf.Min(0.6f, (seg - 0.2f) / (lines * 0.9f + 0.1f), depth * 0.62f);
            Color fg = TextOn(bg);
            // 양쪽 면(길 따라 오가는 사람 모두 읽게)
            Kit.Label(t, v, c0 + Vector3.up * yc + along * (thick * 0.5f + 0.045f), Kit.Facing(along), ch, fg);
            Kit.Label(t, v, c0 + Vector3.up * yc - along * (thick * 0.5f + 0.045f), Kit.Facing(-along), ch, fg);
        }
        // 맨 위 작은 머리 장식
        Panel(t, c0 + Vector3.up * (top + 0.2f), new Vector3(thick + 0.1f, 0.28f, depth + 0.1f), rot, Night(Kit.Hex("E9ECEF"), 1.2f), "세로간판 머리");
        // 벽에 고정하는 팔
        Panel(t, at + n * 0.05f + Vector3.up * (bottom + 0.3f), new Vector3(0.08f, 0.08f, 0.2f), rot, Kit.Mat(Kit.Hex("3A3F45")), "고정쇠");
        Panel(t, at + n * 0.05f + Vector3.up * (top - 0.3f), new Vector3(0.08f, 0.08f, 0.2f), rot, Kit.Mat(Kit.Hex("3A3F45")), "고정쇠");
    }

    /// <summary>돌출 간판: 벽에서 튀어나온 작은 상자, 양쪽 면에 세로 글씨</summary>
    public static void Projecting(Transform t, Vector3 at, Vector3 n, Vector3 along, float y, string name, Color bg)
    {
        const float depth = 0.62f, thick = 0.18f, hgt = 1.5f;
        var rot = Quaternion.LookRotation(n, Vector3.up);
        Vector3 c = at + n * (depth * 0.5f + 0.1f) + Vector3.up * y;
        Panel(t, c, new Vector3(thick, hgt, depth), rot, Night(bg, 1.5f), "돌출간판");
        Panel(t, c + Vector3.up * (hgt * 0.5f + 0.03f), new Vector3(thick + 0.03f, 0.06f, depth + 0.03f), rot, Kit.Mat(Kit.Hex("3A3F45")), "돌출간판 테");
        string v = Vertical(name, 4);
        int lines = v.Split('\n').Length;
        float ch = Mathf.Min(0.36f, (hgt - 0.16f) / (lines * 0.9f));
        Color fg = TextOn(bg);
        Kit.Label(t, v, c + along * (thick * 0.5f + 0.02f), Kit.Facing(along), ch, fg);
        Kit.Label(t, v, c - along * (thick * 0.5f + 0.02f), Kit.Facing(-along), ch, fg);
    }

    /// <summary>창문에 붙인 시트지 글씨 (학원·병원·PC방 등 2층 이상 가게)</summary>
    public static void WindowText(Transform t, Vector3 at, Vector3 n, float y, string text, Color fg, float hgt)
    {
        Kit.Label(t, text, at + n * 0.05f + Vector3.up * y, Kit.Facing(n), hgt, fg);
    }

    /// <summary>현수막: 흰 천에 굵은 글씨, 양 끝을 줄로 맨 것처럼</summary>
    public static void Banner(Transform t, Vector3 at, Vector3 n, float y, float width, string text, Color fg)
    {
        var rot = Quaternion.LookRotation(n, Vector3.up);
        Panel(t, at + n * 0.08f + Vector3.up * y, new Vector3(width, 0.8f, 0.02f), rot, Kit.Mat(Kit.Hex("ECEAE4")), "현수막");
        float ch = Mathf.Min(0.5f, width / Mathf.Max(3, text.Length + 1) * 0.92f);
        Kit.Label(t, text, at + n * 0.1f + Vector3.up * y, Kit.Facing(n), ch, fg);
    }

    /// <summary>계단 입구 옆 층별 안내판</summary>
    public static void Directory(Transform t, Vector3 at, Vector3 n, IList<string> lines)
    {
        if (lines.Count == 0) return;
        var rot = Quaternion.LookRotation(n, Vector3.up);
        float h = 0.22f * lines.Count + 0.3f;
        Panel(t, at + n * 0.04f + Vector3.up * (1.55f), new Vector3(0.75f, h, 0.04f), rot, Kit.Mat(Kit.Hex("2E3A48")), "층별안내");
        Kit.Label(t, string.Join("\n", lines), at + n * 0.07f + Vector3.up * 1.55f, Kit.Facing(n), 0.13f, Kit.Hex("E9ECEF"));
        // 계단 입구: 어두운 문간
        Panel(t, at + n * 0.02f + Vector3.up * 1.15f + Vector3.Cross(Vector3.up, n) * 0.95f, new Vector3(1.1f, 2.3f, 0.03f), rot, Kit.Mat(Kit.Hex("24282D")), "계단 입구");
        Kit.Label(t, "계단", at + n * 0.05f + Vector3.up * 2.45f + Vector3.Cross(Vector3.up, n) * 0.95f, Kit.Facing(n), 0.16f, Kit.Hex("E9ECEF"));
    }

    public static Color TextOn(Color bg) => (bg.r * 0.3f + bg.g * 0.59f + bg.b * 0.11f) > 0.62f ? Kit.Hex("2E3135") : Color.white;

    // ================= 편의점 =================

    // 지어낸 편의점 색 (실제 상표와 겹치지 않게): 청록 + 산호
    public static readonly Color CvsMain = Kit.Hex("0F7C80"), CvsAccent = Kit.Hex("F06B4F"), CvsWhite = Kit.Hex("F4F5F2");

    /// <summary>
    /// 편의점 정면. mid = 이 가게 몫 정면의 가운데(로컬, 바닥), width = 가게 폭.
    /// 띠 간판(흰 바탕 + 청록·산호 줄), 왼쪽 '24' 상자, 이름, 문 위 영업시간, 파라솔 테이블, 아이스크림 냉동고, 상자 더미.
    /// </summary>
    public static void ConvenienceFront(Transform t, Vector3 mid, Vector3 n, Vector3 along, float width, string name, int hash)
    {
        var rot = Quaternion.LookRotation(n, Vector3.up);
        float w = Mathf.Max(2.4f, width - 0.2f);
        Vector3 band = mid + n * 0.2f + Vector3.up * 3.02f;
        Panel(t, band, new Vector3(w, 0.86f, 0.36f), rot, Night(CvsWhite, 1.3f), "편의점 띠간판");
        Panel(t, band + n * 0.185f + Vector3.down * 0.33f, new Vector3(w, 0.13f, 0.02f), rot, Night(CvsMain, 1.6f), "띠 청록");
        Panel(t, band + n * 0.185f + Vector3.down * 0.2f, new Vector3(w, 0.06f, 0.02f), rot, Night(CvsAccent, 1.6f), "띠 산호");
        // 왼쪽 로고 상자
        Vector3 logo = band + n * 0.2f - along * (w * 0.5f - 0.45f) + Vector3.up * 0.06f;
        Panel(t, logo, new Vector3(0.7f, 0.62f, 0.04f), rot, Night(CvsMain, 1.8f), "편의점 로고");
        Kit.Label(t, "24", logo + n * 0.03f, Kit.Facing(n), 0.42f, CvsWhite);
        // 이름
        string nm = name.Length > 12 ? name.Substring(0, 12) : name;
        float avail = w - 1.2f;
        float ch = Mathf.Min(0.42f, avail / Mathf.Max(3, nm.Length + 1) * 0.95f);
        Kit.Label(t, nm, band + n * 0.2f + along * 0.35f + Vector3.up * 0.08f, Kit.Facing(n), ch, CvsMain);
        // 문 위 작은 안내
        Kit.Label(t, "24시간 · 택배 · ATM", mid + n * 0.06f + Vector3.up * 2.42f, Kit.Facing(n), 0.13f, Kit.Hex("2E3135"));

        // ---- 가게 앞 (보도 위, 벽에서 1.6m 안) ----
        var rnd = new System.Random(hash ^ 0x5ca1);
        float side = rnd.Next(2) == 0 ? -1f : 1f;
        // 파라솔 테이블
        Vector3 tp = mid + n * 1.25f + along * side * Mathf.Min(w * 0.3f, 1.6f);
        Kit.Cyl(t, tp + Vector3.up * 0.37f, new Vector3(0.05f, 0.74f, 0.05f), Kit.Hex("D9DCD6"), name: "테이블 다리");
        var top = Kit.Cyl(t, tp + Vector3.up * 0.74f, new Vector3(0.8f, 0.04f, 0.8f), Kit.Hex("E9ECEF"), true, name: "플라스틱 테이블");
        Kit.Cyl(t, tp + Vector3.up * 1.3f, new Vector3(0.04f, 1.3f, 0.04f), Kit.Hex("B8BCB4"), name: "파라솔 대");
        Color para = (hash & 1) == 0 ? CvsMain : Kit.Hex("2E6EA8");
        Kit.Cyl(t, tp + Vector3.up * 2.05f, new Vector3(2.1f, 0.06f, 2.1f), para, name: "파라솔");
        Kit.Cyl(t, tp + Vector3.up * 2.12f, new Vector3(1.4f, 0.08f, 1.4f), Color.Lerp(para, Color.white, 0.12f), name: "파라솔 위");
        Kit.Cyl(t, tp + Vector3.up * 2.19f, new Vector3(0.7f, 0.06f, 0.7f), Color.Lerp(para, Color.white, 0.2f), name: "파라솔 꼭지");
        for (int i = 0; i < 2; i++)
        {
            Vector3 cp = tp + along * (i == 0 ? -0.62f : 0.62f);
            Color chair = i == 0 ? Kit.Hex("D9DCD6") : Kit.Hex("2E6EA8");
            Kit.Box(t, cp + Vector3.up * 0.22f, new Vector3(0.42f, 0.04f, 0.42f), chair, rot: rot, name: "의자");
            Kit.Box(t, cp + Vector3.up * 0.11f, new Vector3(0.36f, 0.22f, 0.36f), chair * 0.85f, rot: rot, name: "의자 다리");
            Kit.Box(t, cp + Vector3.up * 0.42f + (i == 0 ? -along : along) * 0.19f, new Vector3(0.04f, 0.4f, 0.42f), chair, rot: Quaternion.LookRotation(along), name: "등받이");
        }
        // 아이스크림 냉동고 (문 반대쪽)
        Vector3 fz = mid + n * 0.5f - along * side * Mathf.Min(w * 0.32f, 1.7f);
        Kit.Box(t, fz + Vector3.up * 0.42f, new Vector3(1.1f, 0.84f, 0.62f), CvsWhite, true, rot, name: "아이스크림 냉동고");
        Kit.Box(t, fz + Vector3.up * 0.86f, new Vector3(1.04f, 0.04f, 0.56f), Kit.Hex("9FC3C9"), rot: rot, emission: 0.25f, name: "냉동고 유리");
        Kit.Box(t, fz + Vector3.up * 0.6f + n * 0.315f, new Vector3(0.9f, 0.18f, 0.01f), CvsAccent, rot: rot, name: "냉동고 띠");
        // 음료 상자 더미
        Vector3 crate = fz + along * side * 0.85f;
        for (int i = 0; i < 3; i++)
            Kit.Box(t, crate + Vector3.up * (0.14f + i * 0.28f), new Vector3(0.5f, 0.26f, 0.36f), i % 2 == 0 ? Kit.Hex("C8402E") : Kit.Hex("2E6EA8"), rot: rot, name: "음료 상자");
    }

    /// <summary>
    /// 편의점 실내. inner = 안쪽 벽 다각형(월드 xz), c = 원점, front = 정면 변 번호, fn = 정면 바깥 법선.
    /// 가운데 진열대 줄(양면 3단, 상품이 빼곡), 안쪽 벽 냉장고(빛나는 유리문), 입구 옆 계산대와 담배 진열장, 천장 형광등.
    /// </summary>
    public static void ConvenienceInterior(Transform parent, string nm, Vector2 c, List<Vector2> inner, int front, Vector3 fn, int hash, float ceiling)
    {
        var rnd = new System.Random(hash ^ 0x24);
        var origin = new Vector3(c.x, 0, c.y);
        // 0 진열대 틀, 1 냉장고 몸, 2 계산대, 3~8 상품 색, 9 형광등(빛), 10 냉장고 유리(빛), 11 담배장
        var mb = new MeshBuilder(12);
        Vector2 fd = new Vector2(-fn.z, fn.x);
        Vector2 back = new Vector2(-fn.x, -fn.z);
        float fyaw = Mathf.Atan2(-fd.y, fd.x) * Mathf.Rad2Deg;
        Vector2 ctr = Poly.Centroid(inner);

        // 안쪽 벽(정면과 가장 마주 보는 변) = 냉장고 벽
        int backEdge = -1; float best = 0.3f;
        for (int i = 0; i < inner.Count; i++)
        {
            if (i == front) continue;
            Vector2 a = inner[i], e = inner[(i + 1) % inner.Count];
            float L = (e - a).magnitude;
            if (L < 2.5f) continue;
            Vector2 d = (e - a) / L, outward = new Vector2(d.y, -d.x);
            float s = Vector2.Dot(outward, back) * Mathf.Min(L, 8f);
            if (s > best) { best = s; backEdge = i; }
        }
        if (backEdge >= 0)
        {
            Vector2 a = inner[backEdge], e = inner[(backEdge + 1) % inner.Count];
            float L = (e - a).magnitude;
            Vector2 d = (e - a) / L, inward = new Vector2(-d.y, d.x);
            float yaw = Mathf.Atan2(-d.y, d.x) * Mathf.Rad2Deg;
            float len = L - 0.6f;
            Vector2 m = (a + e) * 0.5f + inward * 0.38f;
            Vector3 M = new Vector3(m.x - c.x, 0, m.y - c.y);
            mb.Box(1, M + Vector3.up * 1.05f, new Vector3(len, 2.1f, 0.7f), yaw);
            // 유리문: 0.7m 간격, 문틀 사이 빛나는 유리
            int doors = Mathf.Max(1, Mathf.FloorToInt(len / 0.72f));
            float dw = len / doors;
            Vector3 dir = new Vector3(d.x, 0, d.y), inn = new Vector3(inward.x, 0, inward.y);
            for (int i = 0; i < doors; i++)
            {
                Vector3 p = M + dir * (-len * 0.5f + dw * (i + 0.5f)) + inn * 0.36f + Vector3.up * 1.0f;
                mb.Box(10, p, new Vector3(dw - 0.08f, 1.7f, 0.02f), yaw);
                // 유리 너머 음료 줄
                for (int r = 0; r < 4; r++)
                    mb.Box(3 + (i + r) % 6, p - inn * 0.12f + Vector3.up * (-0.62f + r * 0.42f), new Vector3(dw - 0.18f, 0.22f, 0.1f), yaw);
            }
        }

        // 가운데 진열대 줄: 정면과 직각(안쪽으로 길게) 2~3줄
        float clear = 1.25f;
        for (int row = -2; row <= 2; row++)
        {
            Vector2 q = ctr + fd * (row * 1.9f) + back * 0.2f;
            float len = 4.4f;
            while (len >= 1.6f && !(InsideShrunk(inner, q + back * (len * 0.5f), clear) && InsideShrunk(inner, q - back * (len * 0.5f), clear))) len -= 0.4f;
            if (len < 1.6f) continue;
            Vector3 Q = new Vector3(q.x - c.x, 0, q.y - c.y);
            float ryaw = fyaw + 90f;
            mb.Box(0, Q + Vector3.up * 0.07f, new Vector3(len, 0.14f, 0.9f), ryaw);           // 받침
            mb.Box(0, Q + Vector3.up * 0.8f, new Vector3(len, 1.6f, 0.06f), ryaw);            // 가운데 등판
            mb.Box(0, Q + Vector3.up * 1.62f, new Vector3(len + 0.04f, 0.06f, 0.24f), ryaw);  // 위 테두리 (가격표 띠)
            Quaternion rq = Quaternion.Euler(0, ryaw, 0);
            Vector3 ax = rq * Vector3.right, az = rq * Vector3.forward;
            for (int s = -1; s <= 1; s += 2)
                for (int lvl = 0; lvl < 3; lvl++)
                {
                    float y = 0.3f + lvl * 0.48f;
                    mb.Box(0, Q + az * (s * 0.24f) + Vector3.up * y, new Vector3(len, 0.03f, 0.42f), ryaw);   // 선반
                    // 상품: 같은 색끼리 몇 개씩 붙여 '진열' 느낌
                    float x = -len * 0.5f + 0.06f;
                    while (x < len * 0.5f - 0.12f)
                    {
                        int col = 3 + rnd.Next(6);
                        int run = 2 + rnd.Next(4);
                        float pw = 0.1f + (float)rnd.NextDouble() * 0.1f, ph = 0.14f + (float)rnd.NextDouble() * 0.2f;
                        for (int r = 0; r < run && x < len * 0.5f - 0.12f; r++)
                        {
                            if (rnd.NextDouble() < 0.12) { x += pw + 0.02f; continue; }   // 빈자리 (털린 가게)
                            mb.Box(col, Q + ax * (x + pw * 0.5f) + az * (s * 0.3f) + Vector3.up * (y + 0.015f + ph * 0.5f), new Vector3(pw, ph, 0.26f), ryaw);
                            x += pw + 0.015f;
                        }
                        x += 0.03f;
                    }
                }
        }

        // 계산대: 입구 안쪽 한쪽 옆
        float sideSign = (hash & 2) == 0 ? 1f : -1f;
        Vector2 frontMid = (inner[front] + inner[(front + 1) % inner.Count]) * 0.5f;
        float fl = (inner[(front + 1) % inner.Count] - inner[front]).magnitude;
        Vector2 cq = frontMid + back * 1.3f + fd * sideSign * Mathf.Min(fl * 0.5f - 1.0f, 2.2f);
        if (InsideShrunk(inner, cq, 0.6f))
        {
            Vector3 C = new Vector3(cq.x - c.x, 0, cq.y - c.y);
            mb.Box(2, C + Vector3.up * 0.5f, new Vector3(1.8f, 1.0f, 0.6f), fyaw);
            mb.Box(0, C + Vector3.up * 1.02f, new Vector3(1.86f, 0.04f, 0.66f), fyaw);
            Vector3 bk = new Vector3(back.x, 0, back.y), fdv = new Vector3(fd.x, 0, fd.y);
            mb.Box(11, C + Vector3.up * 1.18f + fdv * 0.4f, new Vector3(0.36f, 0.28f, 0.3f), fyaw);   // 계산기
            // 계산대 뒤 담배 진열장 (알록달록 칸)
            Vector3 rack = C + bk * 0.95f;
            mb.Box(11, rack + Vector3.up * 1.45f, new Vector3(1.8f, 1.2f, 0.3f), fyaw);
            for (int gy = 0; gy < 5; gy++)
                for (int gx = 0; gx < 9; gx++)
                    mb.Box(3 + (gx * 3 + gy) % 6, rack - bk * 0.16f + fdv * (-0.8f + gx * 0.2f) + Vector3.up * (0.95f + gy * 0.22f), new Vector3(0.15f, 0.17f, 0.04f), fyaw);
        }

        // 천장 형광등 줄
        for (int row = -1; row <= 1; row++)
            for (int k = -2; k <= 2; k++)
            {
                Vector2 q = ctr + fd * (row * 2.2f) + back * (k * 1.6f);
                if (!InsideShrunk(inner, q, 0.5f)) continue;
                mb.Box(9, new Vector3(q.x - c.x, ceiling - 0.06f, q.y - c.y), new Vector3(0.18f, 0.04f, 1.2f), fyaw);
            }

        var mats = new[]
        {
            Kit.Mat(Kit.Hex("D9DCD6")), Kit.Mat(Kit.Hex("C9CCC6")), Kit.Mat(CvsMain),
            Kit.Mat(Kit.Hex("C8402E")), Kit.Mat(Kit.Hex("F2C230")), Kit.Mat(Kit.Hex("2E6EA8")),
            Kit.Mat(Kit.Hex("3A8A5C")), Kit.Mat(Kit.Hex("E8892B")), Kit.Mat(Kit.Hex("E9ECEF")),
            Kit.Mat(Kit.Hex("F4F6F2"), 2.2f), Kit.Mat(Kit.Hex("CFE6EE"), 0.9f), Kit.Mat(Kit.Hex("2E3135")),
        };
        mb.Build(nm + " 편의점 안", parent, origin, mats, true);
    }

    static bool InsideShrunk(List<Vector2> poly, Vector2 q, float r)
    {
        if (!Poly.Contains(poly, q)) return false;
        for (int k = 0; k < 8; k++)
        {
            float ang = k * Mathf.PI * 0.25f;
            if (!Poly.Contains(poly, q + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r)) return false;
        }
        return true;
    }
}
