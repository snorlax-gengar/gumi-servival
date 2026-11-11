using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 업종별 가게 안 꾸미기: 옷가게, 미용실, 식당, 카페, 부동산, 약국.
/// 좌표: 정면 벽 방향 fd(가로), 안쪽 방향 back(세로). 상자만 써서 메시 하나로 묶는다.
/// </summary>
public static class ShopInteriors
{
    public enum Kind { None, Clothes, Salon, Restaurant, Cafe, Realty, Pharmacy }

    public static Kind Classify(string c, string m)
    {
        c = c ?? ""; m = m ?? "";
        if (c.Contains("편의점")) return Kind.None;
        if (c.Contains("약국")) return Kind.Pharmacy;
        if (c.Contains("미용") || c.Contains("헤어") || c.Contains("네일") || c.Contains("피부")) return Kind.Salon;
        if (c.Contains("부동산") || c.Contains("중개")) return Kind.Realty;
        if (c.Contains("카페") || c.Contains("커피") || c.Contains("빵") || c.Contains("제과") || c.Contains("디저트") || c.Contains("아이스크림")) return Kind.Cafe;
        if (c.Contains("의류") || c.Contains("옷") || c.Contains("신발") || c.Contains("가방") || c.Contains("잡화") || c.Contains("패션")) return Kind.Clothes;
        if (m.Contains("음식")) return Kind.Restaurant;
        return Kind.None;
    }

    // 재질 칸
    const int FRAME = 0, WOOD = 1, DARK = 2, LIGHT = 3, WHITE = 4, GLASS = 5, C1 = 6, C2 = 7, C3 = 8, C4 = 9, C5 = 10, LEATHER = 11, STEEL = 12;

    class Ctx
    {
        public MeshBuilder mb = new MeshBuilder(13);
        public List<Vector2> inner; public Vector2 c, ctr, fd, back; public float fyaw, ceil; public int front;
        public System.Random rnd;
        public Vector3 P(Vector2 q, float y) => new Vector3(q.x - c.x, y, q.y - c.y);
        public Vector2 Q(float x, float z) => ctr + fd * x + back * z;
        public void Box(int sub, float x, float z, float y, Vector3 size, float yawAdd = 0f) => mb.Box(sub, P(Q(x, z), y), size, fyaw + yawAdd);
        public void BoxAt(int sub, Vector2 q, float y, Vector3 size, float yawAdd = 0f) => mb.Box(sub, P(q, y), size, fyaw + yawAdd);
        public bool Ok(float x, float z, float r) => InsideShrunk(inner, Q(x, z), r);
        public bool OkQ(Vector2 q, float r) => InsideShrunk(inner, q, r);
        public int Col() => C1 + rnd.Next(5);
    }

    /// <summary>정면에서 가장 먼 안쪽 벽: 벽 위 한 점(q), 벽 방향(d), 방 안쪽(inw), 길이</summary>
    static bool BackWall(Ctx k, out Vector2 mid, out Vector2 d, out Vector2 inw, out float len)
    {
        mid = d = inw = Vector2.zero; len = 0;
        int best = -1; float score = 0.3f;
        for (int i = 0; i < k.inner.Count; i++)
        {
            if (i == k.front) continue;
            Vector2 a = k.inner[i], e = k.inner[(i + 1) % k.inner.Count];
            float L = (e - a).magnitude;
            if (L < 2.2f) continue;
            Vector2 dd = (e - a) / L, outward = new Vector2(dd.y, -dd.x);
            float s = Vector2.Dot(outward, k.back) * Mathf.Min(L, 8f);
            if (s > score) { score = s; best = i; }
        }
        if (best < 0) return false;
        Vector2 A = k.inner[best], E = k.inner[(best + 1) % k.inner.Count];
        len = (E - A).magnitude; d = (E - A) / len; inw = new Vector2(-d.y, d.x); mid = (A + E) * 0.5f;
        return true;
    }

    static float WallYaw(Vector2 d) => Mathf.Atan2(-d.y, d.x) * Mathf.Rad2Deg;

    public static void Build(Kind kind, Transform parent, Transform labelRoot, string nm, Vector2 c, List<Vector2> inner, int front, Vector3 fn, int hash, float ceiling)
    {
        if (kind == Kind.None) return;
        var k = new Ctx
        {
            inner = inner, c = c, ctr = Poly.Centroid(inner), front = front, ceil = ceiling,
            fd = new Vector2(-fn.z, fn.x), back = new Vector2(-fn.x, -fn.z), rnd = new System.Random(hash ^ 0x5a1)
        };
        k.fyaw = Mathf.Atan2(-k.fd.y, k.fd.x) * Mathf.Rad2Deg;

        string[] pal;
        switch (kind)
        {
            case Kind.Clothes: Clothes(k); pal = new[] { "2E3135", "D8D2C4", "F2F0EA", "000000" }; break;
            case Kind.Salon: Salon(k, labelRoot); pal = new[] { "E9ECEF", "C9B49A", "1E2226", "000000" }; break;
            case Kind.Restaurant: Restaurant(k, labelRoot, hash); pal = new[] { "6A6E74", "A8794E", "3A2E26", "000000" }; break;
            case Kind.Cafe: Cafe(k, labelRoot); pal = new[] { "3A3F45", "B98A5E", "2A2622", "000000" }; break;
            case Kind.Realty: Realty(k, labelRoot); pal = new[] { "8A9098", "C9B08A", "2E3135", "000000" }; break;
            default: Pharmacy(k, labelRoot); pal = new[] { "D9DCD6", "E9E6DE", "2E3135", "000000" }; break;
        }
        // 천장 등
        for (int x = -2; x <= 2; x++)
            for (int z = -2; z <= 2; z++)
                if (k.Ok(x * 2.4f, z * 2.4f, 0.6f))
                    k.Box(LIGHT, x * 2.4f, z * 2.4f, ceiling - 0.05f, kind == Kind.Cafe ? new Vector3(0.3f, 0.3f, 0.3f) : new Vector3(0.6f, 0.04f, 0.6f));

        var mats = new[]
        {
            Kit.Mat(Kit.Hex(pal[0])), Kit.Mat(Kit.Hex(pal[1])), Kit.Mat(Kit.Hex(pal[2])),
            Kit.Mat(kind == Kind.Cafe ? Kit.Hex("FFD9A0") : Kit.Hex("F4F6F2"), 2.0f), Kit.Mat(Kit.Hex("F2F0EA")),
            Kit.Mat(Kit.Hex("CFE6EE"), 0.6f),
            Kit.Mat(Kit.Hex("C8402E")), Kit.Mat(Kit.Hex("2E6EA8")), Kit.Mat(Kit.Hex("E3C26A")), Kit.Mat(Kit.Hex("3A8A5C")), Kit.Mat(Kit.Hex("D9A0A8")),
            Kit.Mat(Kit.Hex("1E2226")), Kit.Mat(Kit.Hex("B8BCC0")),
        };
        if (!k.mb.Empty) k.mb.Build(nm + " 안 (" + kind + ")", parent, new Vector3(c.x, 0, c.y), mats, true);
    }

    // ---------------- 옷가게 ----------------
    static void Clothes(Ctx k)
    {
        // 행거 줄: 앞뒤로 두세 줄, 옷이 색색으로 걸림
        for (int row = -1; row <= 1; row++)
        {
            float z = row * 1.8f;
            float len = 3.2f;
            while (len >= 1.2f && !(k.Ok(len * 0.5f, z, 0.6f) && k.Ok(-len * 0.5f, z, 0.6f))) len -= 0.4f;
            if (len < 1.2f) continue;
            for (int s = -1; s <= 1; s += 2) k.Box(STEEL, s * len * 0.5f, z, 0.7f, new Vector3(0.04f, 1.4f, 0.04f));
            k.Box(STEEL, 0, z, 1.4f, new Vector3(len, 0.03f, 0.03f));
            k.Box(STEEL, 0, z, 0.02f, new Vector3(len, 0.04f, 0.5f));
            float x = -len * 0.5f + 0.1f;
            int col = k.Col();
            while (x < len * 0.5f - 0.1f)
            {
                if (k.rnd.NextDouble() < 0.25) col = k.Col();
                float hgt = 0.7f + (float)k.rnd.NextDouble() * 0.35f;
                if (k.rnd.NextDouble() > 0.15) k.Box(col, x, z, 1.38f - hgt * 0.5f, new Vector3(0.05f, hgt, 0.45f));
                x += 0.09f;
            }
        }
        // 벽쪽 접어둔 옷 선반
        if (BackWall(k, out var m, out var d, out var inw, out var L))
        {
            float yaw = WallYaw(d) - k.fyaw;
            float len = Mathf.Min(L - 1.0f, 5f);
            Vector2 q = m + inw * 0.25f;
            k.BoxAt(WOOD, q, 1.1f, new Vector3(len, 2.2f, 0.06f), yaw);
            for (int lv = 0; lv < 4; lv++)
            {
                k.BoxAt(WOOD, q + inw * 0.18f, 0.35f + lv * 0.5f, new Vector3(len, 0.03f, 0.36f), yaw);
                for (float x = -len * 0.5f + 0.25f; x < len * 0.5f - 0.2f; x += 0.42f)
                    if (k.rnd.NextDouble() > 0.2)
                        k.BoxAt(k.Col(), q + inw * 0.2f + d * x, 0.37f + lv * 0.5f + 0.07f, new Vector3(0.32f, 0.14f, 0.28f), yaw);
            }
            // 전신 거울
            Vector2 mq = m + inw * 0.05f + d * (len * 0.5f + 0.5f);
            if (k.OkQ(mq + inw * 0.4f, 0.3f)) k.BoxAt(GLASS, mq, 1.0f, new Vector3(0.6f, 1.7f, 0.03f), yaw);
        }
        // 계산대 + 앞쪽 테이블에 개어 둔 옷
        Table(k, 0, 0.9f, 1.4f, 0.8f, WOOD, true);
        Counter(k);
    }

    // ---------------- 미용실 ----------------
    static void Salon(Ctx k, Transform labels)
    {
        if (BackWall(k, out var m, out var d, out var inw, out var L))
        {
            float yaw = WallYaw(d) - k.fyaw;
            int n = Mathf.Clamp(Mathf.FloorToInt((L - 0.8f) / 1.25f), 1, 5);
            for (int i = 0; i < n; i++)
            {
                float x = (i - (n - 1) * 0.5f) * 1.25f;
                Vector2 q = m + d * x;
                k.BoxAt(GLASS, q + inw * 0.04f, 1.35f, new Vector3(0.9f, 1.1f, 0.03f), yaw);            // 거울
                k.BoxAt(DARK, q + inw * 0.04f, 1.35f, new Vector3(0.98f, 1.18f, 0.02f), yaw);
                k.BoxAt(WHITE, q + inw * 0.2f, 0.82f, new Vector3(1.0f, 0.05f, 0.32f), yaw);            // 화장대 판
                k.BoxAt(k.Col(), q + inw * 0.2f + d * 0.3f, 0.92f, new Vector3(0.07f, 0.16f, 0.07f), yaw);   // 스프레이 병
                k.BoxAt(STEEL, q + inw * 0.2f - d * 0.25f, 0.88f, new Vector3(0.2f, 0.06f, 0.1f), yaw);      // 드라이기
                Vector2 ch = q + inw * 0.95f;                                                              // 의자
                k.BoxAt(STEEL, ch, 0.22f, new Vector3(0.12f, 0.44f, 0.12f), yaw);
                k.BoxAt(STEEL, ch, 0.03f, new Vector3(0.5f, 0.05f, 0.5f), yaw);
                k.BoxAt(LEATHER, ch, 0.52f, new Vector3(0.55f, 0.12f, 0.5f), yaw);
                k.BoxAt(LEATHER, ch + inw * 0.24f, 0.85f, new Vector3(0.55f, 0.62f, 0.08f), yaw);
            }
        }
        // 샴푸대 (옆에 하나)
        if (k.Ok(-1.6f, 0.6f, 0.6f))
        {
            k.Box(WHITE, -1.6f, 0.6f, 0.45f, new Vector3(0.6f, 0.9f, 0.6f));
            k.Box(LEATHER, -1.6f, 1.25f, 0.45f, new Vector3(0.6f, 0.2f, 1.2f));
        }
        // 대기 소파 + 잡지 탁자 (입구 쪽)
        float sx = 1.4f;
        if (k.Ok(sx, -1.4f, 0.6f))
        {
            k.Box(LEATHER, sx, -1.4f, 0.22f, new Vector3(1.6f, 0.44f, 0.6f));
            k.Box(LEATHER, sx, -1.15f, 0.6f, new Vector3(1.6f, 0.5f, 0.14f));
            k.Box(WOOD, sx, -2.0f, 0.22f, new Vector3(0.8f, 0.44f, 0.45f));
            k.Box(C5, sx + 0.1f, -2.0f, 0.46f, new Vector3(0.3f, 0.03f, 0.22f), 12f);
        }
        Counter(k);
    }

    // ---------------- 식당 (한식·백반·분식) ----------------
    static readonly string[][] Menus =
    {
        new[] { "김치찌개 9,000", "된장찌개 9,000", "제육볶음 10,000", "공기밥 1,000" },
        new[] { "백반 8,000", "순두부 9,000", "비빔밥 9,000", "계란말이 7,000" },
        new[] { "라면 5,000", "김밥 3,500", "떡볶이 5,000", "돈까스 9,000" },
        new[] { "삼겹살 15,000", "목살 15,000", "된장찌개 3,000", "냉면 7,000" },
    };

    static void Restaurant(Ctx k, Transform labels, int hash)
    {
        bool grill = (hash & 3) == 3;
        for (int gx = -2; gx <= 2; gx++)
            for (int gz = -2; gz <= 1; gz++)
            {
                float x = gx * 1.9f, z = gz * 1.9f + 0.3f;
                if (!k.Ok(x, z, 1.1f)) continue;
                k.Box(WOOD, x, z, 0.72f, new Vector3(1.2f, 0.06f, 0.75f));            // 상판
                k.Box(STEEL, x, z, 0.36f, new Vector3(0.1f, 0.7f, 0.1f));
                k.Box(STEEL, x, z, 0.02f, new Vector3(0.5f, 0.04f, 0.5f));
                if (grill) { k.Box(DARK, x, z, 0.76f, new Vector3(0.36f, 0.04f, 0.36f)); k.Box(STEEL, x, z, 1.9f, new Vector3(0.22f, 1.6f, 0.22f)); }   // 불판 + 연통
                else
                {
                    k.Box(STEEL, x - 0.4f, z, 0.77f, new Vector3(0.12f, 0.04f, 0.3f));   // 수저통·휴지
                    k.Box(WHITE, x - 0.4f, z + 0.22f, 0.8f, new Vector3(0.12f, 0.1f, 0.12f));
                }
                for (int s = -1; s <= 1; s += 2)                                       // 둥근 의자 대신 네모 의자 2개씩
                    for (int t = -1; t <= 1; t += 2)
                    {
                        k.Box(C1, x + t * 0.32f, z + s * 0.6f, 0.42f, new Vector3(0.32f, 0.05f, 0.32f));
                        k.Box(STEEL, x + t * 0.32f, z + s * 0.6f, 0.21f, new Vector3(0.05f, 0.42f, 0.05f));
                    }
            }
        // 안쪽: 주방 배식대 + 메뉴판
        if (BackWall(k, out var m, out var d, out var inw, out var L))
        {
            float yaw = WallYaw(d) - k.fyaw;
            float len = Mathf.Min(L - 0.8f, 4.5f);
            k.BoxAt(STEEL, m + inw * 0.35f, 0.5f, new Vector3(len, 1.0f, 0.6f), yaw);
            k.BoxAt(STEEL, m + inw * 0.35f, 1.02f, new Vector3(len, 0.04f, 0.66f), yaw);
            k.BoxAt(STEEL, m + inw * 0.3f + d * (len * 0.3f), 1.15f, new Vector3(0.5f, 0.2f, 0.4f), yaw);   // 밥솥
            Vector2 bq = m + inw * 0.04f;
            k.BoxAt(DARK, bq, 2.25f, new Vector3(Mathf.Min(len, 2.6f), 0.9f, 0.03f), yaw);
            if (labels != null)
            {
                var menu = Menus[(hash >> 3 & 0x7fff) % Menus.Length];
                if (grill) menu = Menus[3];
                Vector3 nrm = new Vector3(inw.x, 0, inw.y);
                for (int i = 0; i < menu.Length; i++)
                    Kit.Label(labels, menu[i], new Vector3(bq.x, 2.55f - i * 0.2f, bq.y) + nrm * 0.03f, Kit.Facing(nrm), 0.13f, Kit.Hex("F2F0EA"));
            }
        }
        // 정수기
        if (k.Ok(2.6f, 1.6f, 0.4f)) { k.Box(WHITE, 2.6f, 1.6f, 0.6f, new Vector3(0.35f, 1.2f, 0.35f)); k.Box(C2, 2.6f, 1.6f, 1.33f, new Vector3(0.28f, 0.3f, 0.28f)); }
    }

    // ---------------- 카페 ----------------
    static void Cafe(Ctx k, Transform labels)
    {
        if (BackWall(k, out var m, out var d, out var inw, out var L))
        {
            float yaw = WallYaw(d) - k.fyaw;
            float len = Mathf.Min(L - 0.8f, 5f);
            Vector2 cq = m + inw * 1.3f;
            k.BoxAt(WOOD, cq, 0.5f, new Vector3(len, 1.0f, 0.6f), yaw);                         // 바 카운터
            k.BoxAt(DARK, cq, 1.03f, new Vector3(len + 0.05f, 0.05f, 0.66f), yaw);
            k.BoxAt(STEEL, cq + d * (-len * 0.3f), 1.25f, new Vector3(0.7f, 0.42f, 0.5f), yaw);   // 에스프레소 머신
            k.BoxAt(DARK, cq + d * (-len * 0.3f + 0.6f), 1.2f, new Vector3(0.2f, 0.34f, 0.2f), yaw); // 그라인더
            k.BoxAt(WHITE, cq + d * (len * 0.1f), 1.12f, new Vector3(0.3f, 0.14f, 0.3f), yaw);   // 포스기
            Vector2 vq = cq + d * (len * 0.33f) - inw * 0.05f;                                   // 빵 진열 쇼케이스
            k.BoxAt(GLASS, vq, 1.3f, new Vector3(1.1f, 0.5f, 0.55f), yaw);
            for (int i = 0; i < 4; i++) k.BoxAt(C3, vq + d * (-0.4f + i * 0.27f), 1.12f, new Vector3(0.18f, 0.08f, 0.18f), yaw);
            // 뒷벽 선반 (컵·원두)
            for (int lv = 0; lv < 2; lv++)
            {
                k.BoxAt(WOOD, m + inw * 0.15f, 1.6f + lv * 0.45f, new Vector3(len * 0.8f, 0.04f, 0.28f), yaw);
                for (float x = -len * 0.38f; x < len * 0.38f; x += 0.22f)
                    k.BoxAt(lv == 0 ? WHITE : C3, m + inw * 0.15f + d * x, 1.68f + lv * 0.45f, new Vector3(0.1f, 0.13f, 0.1f), yaw);
            }
            k.BoxAt(DARK, m + inw * 0.03f, 2.45f, new Vector3(Mathf.Min(len, 2.4f), 0.6f, 0.03f), yaw);
            if (labels != null)
            {
                Vector3 nrm = new Vector3(inw.x, 0, inw.y);
                string[] menu = { "아메리카노 3,000", "카페라떼 3,800", "바닐라라떼 4,300" };
                for (int i = 0; i < menu.Length; i++)
                    Kit.Label(labels, menu[i], new Vector3(m.x, 2.62f - i * 0.17f, m.y) + nrm * 0.05f, Kit.Facing(nrm), 0.11f, Kit.Hex("F2F0EA"));
            }
        }
        // 2인 테이블 + 의자
        for (int gx = -2; gx <= 2; gx++)
            for (int gz = -2; gz <= 0; gz++)
            {
                float x = gx * 1.6f, z = gz * 1.6f - 0.2f;
                if (!k.Ok(x, z, 0.9f)) continue;
                k.Box(WHITE, x, z, 0.72f, new Vector3(0.6f, 0.04f, 0.6f));
                k.Box(DARK, x, z, 0.36f, new Vector3(0.06f, 0.72f, 0.06f));
                for (int s = -1; s <= 1; s += 2)
                {
                    k.Box(DARK, x + s * 0.5f, z, 0.45f, new Vector3(0.38f, 0.04f, 0.38f));
                    k.Box(DARK, x + s * 0.67f, z, 0.68f, new Vector3(0.04f, 0.45f, 0.36f));
                    k.Box(DARK, x + s * 0.5f, z, 0.22f, new Vector3(0.04f, 0.44f, 0.04f));
                }
                if (k.rnd.NextDouble() < 0.5) k.Box(WHITE, x + 0.12f, z, 0.79f, new Vector3(0.08f, 0.1f, 0.08f));   // 남겨진 컵
                k.Box(LIGHT, x, z, k.ceil - 0.6f, new Vector3(0.22f, 0.18f, 0.22f));   // 펜던트 등
                k.Box(DARK, x, z, k.ceil - 0.25f, new Vector3(0.01f, 0.5f, 0.01f));
            }
    }

    // ---------------- 부동산 ----------------
    static void Realty(Ctx k, Transform labels)
    {
        for (int i = -1; i <= 1; i += 2)
        {
            float x = i * 1.3f, z = 0.9f;
            if (!k.Ok(x, z, 0.8f)) continue;
            k.Box(WOOD, x, z, 0.74f, new Vector3(1.4f, 0.05f, 0.7f));
            k.Box(DARK, x, z + 0.3f, 0.37f, new Vector3(1.3f, 0.72f, 0.05f));
            k.Box(DARK, x + 0.2f, z + 0.15f, 1.0f, new Vector3(0.5f, 0.32f, 0.03f));      // 모니터
            k.Box(DARK, x + 0.2f, z + 0.18f, 0.8f, new Vector3(0.05f, 0.12f, 0.05f));
            k.Box(WHITE, x - 0.35f, z, 0.79f, new Vector3(0.3f, 0.04f, 0.22f), 8f);          // 서류
            k.Box(LEATHER, x, z - 0.6f, 0.47f, new Vector3(0.48f, 0.08f, 0.48f));            // 의자
            k.Box(LEATHER, x, z - 0.38f, 0.8f, new Vector3(0.46f, 0.6f, 0.06f));
            k.Box(STEEL, x, z - 0.6f, 0.22f, new Vector3(0.06f, 0.44f, 0.06f));
        }
        // 손님 소파 + 낮은 탁자
        if (k.Ok(0, -1.2f, 0.7f))
        {
            k.Box(LEATHER, 0, -1.6f, 0.22f, new Vector3(1.8f, 0.44f, 0.6f));
            k.Box(LEATHER, 0, -1.9f, 0.6f, new Vector3(1.8f, 0.5f, 0.14f));
            k.Box(GLASS, 0, -0.9f, 0.4f, new Vector3(1.0f, 0.04f, 0.5f));
            k.Box(STEEL, 0, -0.9f, 0.2f, new Vector3(0.9f, 0.4f, 0.04f));
        }
        // 안쪽 벽: 매물 게시판 (종이 칸칸) + 지도
        if (BackWall(k, out var m, out var d, out var inw, out var L))
        {
            float yaw = WallYaw(d) - k.fyaw;
            float len = Mathf.Min(L - 1f, 3.6f);
            k.BoxAt(WOOD, m + inw * 0.03f, 1.6f, new Vector3(len, 1.2f, 0.03f), yaw);
            for (int gy = 0; gy < 3; gy++)
                for (float x = -len * 0.5f + 0.25f; x < len * 0.5f - 0.2f; x += 0.38f)
                    k.BoxAt(WHITE, m + inw * 0.05f + d * x, 1.2f + gy * 0.38f, new Vector3(0.3f, 0.32f, 0.01f), yaw);
            if (labels != null)
            {
                Vector3 nrm = new Vector3(inw.x, 0, inw.y);
                Kit.Label(labels, "급매 · 전세 · 월세", new Vector3(m.x, 2.4f, m.y) + nrm * 0.05f, Kit.Facing(nrm), 0.15f, Kit.Hex("C8402E"));
            }
        }
        // 정수기 + 화분
        if (k.Ok(2.4f, -1.6f, 0.4f)) { k.Box(C4, 2.4f, -1.6f, 0.5f, new Vector3(0.4f, 1.0f, 0.4f)); k.Box(DARK, 2.4f, -1.6f, 0.15f, new Vector3(0.4f, 0.3f, 0.4f)); }
    }

    // ---------------- 약국 ----------------
    static void Pharmacy(Ctx k, Transform labels)
    {
        if (BackWall(k, out var m, out var d, out var inw, out var L))
        {
            float yaw = WallYaw(d) - k.fyaw;
            float len = Mathf.Min(L - 0.6f, 6f);
            Vector2 sq = m + inw * 0.22f;
            k.BoxAt(WHITE, sq, 1.1f, new Vector3(len, 2.2f, 0.4f), yaw);
            for (int lv = 0; lv < 6; lv++)                                                          // 약 상자 칸칸
                for (float x = -len * 0.5f + 0.1f; x < len * 0.5f - 0.1f; x += 0.14f)
                    if (k.rnd.NextDouble() > 0.15)
                        k.BoxAt(k.Col(), sq + inw * 0.2f + d * x, 0.42f + lv * 0.3f, new Vector3(0.11f, 0.15f + (float)k.rnd.NextDouble() * 0.06f, 0.08f), yaw);
            // 조제 카운터 (유리 상판)
            Vector2 cq = m + inw * 1.5f;
            k.BoxAt(WHITE, cq, 0.5f, new Vector3(Mathf.Min(len, 3.6f), 1.0f, 0.55f), yaw);
            k.BoxAt(GLASS, cq, 1.02f, new Vector3(Mathf.Min(len, 3.6f), 0.04f, 0.6f), yaw);
            k.BoxAt(DARK, cq + d * 0.8f, 1.15f, new Vector3(0.36f, 0.22f, 0.3f), yaw);
            if (labels != null)
            {
                Vector3 nrm = new Vector3(inw.x, 0, inw.y);
                Kit.Label(labels, "처방 조제", new Vector3(m.x, 2.45f, m.y) + nrm * 0.45f, Kit.Facing(nrm), 0.18f, Kit.Hex("3A8A5C"));
            }
        }
        // 가운데 낮은 매대 (영양제·파스)
        for (int i = -1; i <= 1; i += 2)
        {
            float x = i * 1.2f;
            if (!k.Ok(x, -0.6f, 0.7f)) continue;
            k.Box(WHITE, x, -0.6f, 0.45f, new Vector3(1.3f, 0.9f, 0.5f));
            for (float t = -0.55f; t < 0.56f; t += 0.16f) k.Box(k.Col(), x + t, -0.6f, 0.97f, new Vector3(0.12f, 0.14f, 0.12f));
        }
    }

    // ---------------- 공통 ----------------
    static void Table(Ctx k, float x, float z, float w, float dpt, int mat, bool folded)
    {
        if (!k.Ok(x, z, Mathf.Max(w, dpt) * 0.6f)) return;
        k.Box(mat, x, z, 0.4f, new Vector3(w, 0.8f, dpt));
        if (!folded) return;
        for (float t = -w * 0.5f + 0.2f; t < w * 0.5f - 0.1f; t += 0.32f)
            for (int s = 0; s < 1 + k.rnd.Next(3); s++)
                k.Box(k.Col(), x + t, z, 0.84f + s * 0.06f, new Vector3(0.28f, 0.05f, 0.3f));
    }

    static void Counter(Ctx k)
    {
        Vector2 fm = (k.inner[k.front] + k.inner[(k.front + 1) % k.inner.Count]) * 0.5f;
        float fl = (k.inner[(k.front + 1) % k.inner.Count] - k.inner[k.front]).magnitude;
        Vector2 q = fm + k.back * 1.4f + k.fd * Mathf.Min(fl * 0.5f - 1.0f, 2.2f);
        if (!k.OkQ(q, 0.6f)) return;
        k.BoxAt(WOOD, q, 0.5f, new Vector3(1.4f, 1.0f, 0.55f), 0f);
        k.BoxAt(WHITE, q, 1.02f, new Vector3(1.46f, 0.04f, 0.6f), 0f);
        k.BoxAt(DARK, q + k.fd * 0.35f, 1.15f, new Vector3(0.3f, 0.22f, 0.26f), 0f);
    }

    static bool InsideShrunk(List<Vector2> poly, Vector2 q, float r)
    {
        if (!Poly.Contains(poly, q)) return false;
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI * 0.25f;
            if (!Poly.Contains(poly, q + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r)) return false;
        }
        return true;
    }
}
