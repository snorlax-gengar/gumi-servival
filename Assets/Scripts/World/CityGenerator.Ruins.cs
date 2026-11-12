using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 무너진 구미: 그을린 벽, 건물 밑 잔해, 낙서, 연기 기둥·불, 버려지거나 불탄 차,
/// 모래주머니 검문소, 쓰레기봉투 더미, 현수막.
/// 메시는 120m 칸마다 하나로 묶어 그린다 (그릴 물체 수를 줄이려고).
/// </summary>
public partial class CityGenerator
{
    Transform ruinRoot, fxRoot;
    /// <summary>개발용 F7 둘러보기 지점</summary>
    public static readonly List<Showcase> RuinSpots = new List<Showcase>();
    void RuinSpot(Vector3 pos, Vector2 dir, string label)
    {
        if (RuinSpots.Count >= 40) return;
        Vector3 n = new Vector3(dir.x, 0, dir.y);
        if (n.sqrMagnitude < 0.01f) n = Vector3.forward;
        RuinSpots.Add(new Showcase { pos = pos, n = n.normalized, label = label });
    }
    readonly Dictionary<long, MeshBuilder> ruinCells = new Dictionary<long, MeshBuilder>();
    int smokeCount, fireCount;
    const int MaxSmoke = 16, MaxFire = 10;

    // 칸 메시 재질 번호
    const int RU_CONC = 0, RU_BRICK = 1, RU_SAND = 2, RU_JERSEY = 3, RU_BAGW = 4, RU_BAGB = 5, RU_BAGK = 6, RU_BOX = 7, RU_REBAR = 8, RU_CLOTH = 9;

    static readonly string[] Graffiti =
    {
        "생존자 있음 3명", "물 없음", "→ 구미역", "여기 위험", "엄마 원평동 교회로 감", "로봇 조심",
        "밤에 나오지 마", "식량 X", "2층 안전", "민수야 살아있으면 역으로", "SOS", "안드로이드 순찰 02시",
    };
    static readonly string[] BannerTexts =
    {
        "구미시 재난 대피 안내 · 구미역 집결", "재난지역 출입 통제", "실종자를 찾습니다 · 연락 054-4**-****",
        "안드로이드 접근 금지 구역", "구미 시민 여러분 힘내십시오", "급수 배급 · 매일 10시 원평동 주민센터",
        "야간 통행 금지 22:00 ~ 06:00", "생존자 등록소 → 구미역 광장",
    };

    MeshBuilder RuinCell(Vector2 p)
    {
        long k = Key(Mathf.FloorToInt(p.x / 120f), Mathf.FloorToInt(p.y / 120f));
        if (!ruinCells.TryGetValue(k, out var mb)) ruinCells[k] = mb = new MeshBuilder(10);
        return mb;
    }

    void EnsureRuinRoots()
    {
        if (ruinRoot != null) return;
        ruinRoot = new GameObject("구미_폐허").transform; ruinRoot.SetParent(root, false);
        fxRoot = new GameObject("구미_연기·불").transform; fxRoot.SetParent(root, false);
    }

    // ================= 건물 하나 =================

    /// <summary>BuildRealBuildings가 건물마다 부른다. 3채 중 1채꼴로 상처를 입힌다.</summary>
    void RuinBuilding(Transform bgo, List<Vector2> pts, Vector2 c, float h, int hash, int front, bool enter)
    {
        EnsureRuinRoots();
        int dmg = (hash >> 4) % 100;
        if (dmg >= 36) return;                       // 64%는 멀쩡
        bool heavy = dmg < 10;                       // 10%는 크게 탐
        var rnd = new System.Random(hash ^ 0x7a11);
        var soot = new MeshBuilder(1);

        int plumes = heavy ? 4 + rnd.Next(5) : 1 + rnd.Next(3);
        int floors = Mathf.Max(1, Mathf.FloorToInt(h / Facade.Floor));
        for (int k = 0; k < plumes; k++)
        {
            int i = rnd.Next(pts.Count);
            Vector2 a = pts[i], e = pts[(i + 1) % pts.Count];
            float L = (e - a).magnitude;
            if (L < 3f) continue;
            Vector2 d = (e - a) / L;
            Vector3 n = new Vector3(d.y, 0, -d.x);
            float t = 1f + (float)rnd.NextDouble() * (L - 2f);
            int fl = rnd.Next(floors);
            float y0 = fl * Facade.Floor + 1.4f;
            float pw = 1.6f + (float)rnd.NextDouble() * 1.4f, ph = Mathf.Min(h - y0 + 0.3f, 2.4f + (float)rnd.NextDouble() * 3f);
            if (ph < 1f) continue;
            Vector2 m = a + d * t;
            Vector3 M = new Vector3(m.x - c.x, y0, m.y - c.y) + n * 0.04f;
            Vector3 D = new Vector3(d.x, 0, d.y);
            soot.Quad(0, M - D * pw * 0.5f, M + D * pw * 0.5f, M + D * pw * 0.5f + Vector3.up * ph, M - D * pw * 0.5f + Vector3.up * ph, n,
                new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1));
        }
        if (!soot.Empty) soot.Build("그을음", bgo, bgo.position, new[] { SootMat() }, false, false);

        // 건물 밑 잔해 더미 (정면은 문 앞을 비우고 모서리 쪽만)
        int piles = heavy ? 3 + rnd.Next(3) : rnd.Next(2);
        for (int k = 0; k < piles; k++)
        {
            int i = rnd.Next(pts.Count);
            Vector2 a = pts[i], e = pts[(i + 1) % pts.Count];
            float L = (e - a).magnitude;
            if (L < 2.5f) continue;
            Vector2 d = (e - a) / L, outw = new Vector2(d.y, -d.x);
            float t = i == front ? (rnd.NextDouble() < 0.5 ? 0.8f : L - 0.8f) : 0.8f + (float)rnd.NextDouble() * (L - 1.6f);
            Vector2 q = a + d * t + outw * 0.9f;
            if (RoadEdgeDist(q) < 0.6f) continue;
            RubblePile(RuinCell(q), q, 0.9f + (heavy ? 0.6f : 0f), rnd, Chance(0.4f));
        }

        // 벽 낙서 (빨강·검정 스프레이)
        if (rnd.NextDouble() < (heavy ? 0.7 : 0.35))
        {
            int i = front >= 0 && rnd.NextDouble() < 0.6 ? front : rnd.Next(pts.Count);
            Vector2 a = pts[i], e = pts[(i + 1) % pts.Count];
            float L = (e - a).magnitude;
            if (L >= 3f)
            {
                Vector2 d = (e - a) / L;
                Vector3 n = new Vector3(d.y, 0, -d.x);
                float t = enter && i == front ? (rnd.NextDouble() < 0.5 ? 0.9f : L - 0.9f) : L * (0.25f + 0.5f * (float)rnd.NextDouble());
                string txt = Graffiti[rnd.Next(Graffiti.Length)];
                Vector2 m = a + d * t;
                float hgt = Mathf.Clamp(0.32f * 6f / Mathf.Max(4, txt.Length), 0.2f, 0.4f);
                var lbl = Kit.Label(bgo, txt, new Vector3(m.x - c.x, 1.5f + (float)rnd.NextDouble() * 0.6f, m.y - c.y) + n * 0.05f,
                    Kit.Facing(n) * Quaternion.Euler(0, 0, (float)rnd.NextDouble() * 8f - 4f), hgt,
                    rnd.NextDouble() < 0.65 ? Kit.Hex("B0201A") : Kit.Hex("1A1A1A"));
                if (lbl != null) lbl.gameObject.name = "낙서";
            }
        }

        // 크게 탄 건물: 옥상에서 연기, 일부는 불길
        if (heavy && smokeCount < MaxSmoke && Mathf.Abs(c.x) < PlayHalfX - 20 && Mathf.Abs(c.y) < PlayHalfZ - 20 && rnd.NextDouble() < 0.5)
        {
            Vector3 top = new Vector3(c.x, h + 0.3f, c.y);
            Smoke(top, 1.0f + (float)rnd.NextDouble() * 0.6f);
            if (front >= 0)
            {
                Vector2 fa = pts[front], fe = pts[(front + 1) % pts.Count];
                Vector2 fdir = (fe - fa).normalized;
                RuinSpot(new Vector3((fa.x + fe.x) * 0.5f, 0, (fa.y + fe.y) * 0.5f), new Vector2(fdir.y, -fdir.x), "연기 나는 건물");
            }
            if (fireCount < MaxFire && rnd.NextDouble() < 0.6) Fire(top, 1.3f);
        }
    }

    void RubblePile(MeshBuilder mb, Vector2 q, float r, System.Random rnd, bool brick)
    {
        int n = 7 + rnd.Next(8);
        for (int i = 0; i < n; i++)
        {
            float ang = (float)rnd.NextDouble() * Mathf.PI * 2f, rr = (float)rnd.NextDouble() * r;
            Vector2 p = q + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rr;
            float s = 0.2f + (float)rnd.NextDouble() * 0.5f * (1f - rr / (r + 0.01f) * 0.5f);
            float y = s * 0.3f + (1f - rr / (r + 0.01f)) * 0.25f;
            int sub = brick && rnd.NextDouble() < 0.6 ? RU_BRICK : RU_CONC;
            mb.Box(sub, new Vector3(p.x, y, p.y), new Vector3(s * 1.4f, s * 0.6f, s), (float)rnd.NextDouble() * 180f);
        }
        if (rnd.NextDouble() < 0.5)   // 삐져나온 철근
            for (int i = 0; i < 2; i++)
                mb.Box(RU_REBAR, new Vector3(q.x + (float)rnd.NextDouble() * 0.6f - 0.3f, 0.5f, q.y), new Vector3(0.03f, 1.0f, 0.03f), (float)rnd.NextDouble() * 180f);
    }

    // ================= 거리 =================

    /// <summary>BuildRealStreetFurniture 뒤에 부른다: 버려진 차 행렬, 검문소, 쓰레기 더미</summary>
    void BuildRuinsStreet()
    {
        EnsureRuinRoots();
        var rnd = new System.Random(UsedSeed ^ 0x5eed);
        int jams = 0, checkpoints = 0, trash = 0;
        foreach (var r in real.roads)
        {
            if (r.tu == 1 || r.b == 1 || !Walkable(r.t)) continue;
            var p = Pts(r.p);
            bool major = Major(r.t);
            bool alley = r.t == "residential" || r.t == "living_street" || r.t == "unclassified" || r.t == "service";
            for (int i = 0; i + 1 < p.Count; i++)
            {
                Vector2 a = p[i], b = p[i + 1];
                float L = (b - a).magnitude;
                if (L < 14f) continue;
                Vector2 d = (b - a) / L, n = new Vector2(-d.y, d.x);
                float hw = RoadHalfW(r, i, i + 1);
                float yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
                if (Mathf.Abs(a.x) > PlayHalfX - 10 || Mathf.Abs(a.y) > PlayHalfZ - 10) continue;

                // 큰길: 피란 행렬 그대로 버려진 차들 (일부는 불탐)
                if (major && jams < 90 && rnd.NextDouble() < 0.22)
                {
                    int k = 1 + rnd.Next(3);
                    float t = L * (0.2f + 0.6f * (float)rnd.NextDouble());
                    for (int j = 0; j < k; j++)
                    {
                        float lane = ((float)rnd.NextDouble() * 2f - 1f) * Mathf.Max(0.5f, hw - 1.6f);
                        Vector2 q = a + d * (t + j * 5.5f) + n * lane;
                        if (InsideBuilding(q, 1.5f)) continue;
                        var car = BuildingKit.Car(ruinRoot, new Vector3(q.x, 0, q.y), yaw + (float)rnd.NextDouble() * 50f - 25f, rng);
                        if (rnd.NextDouble() < 0.4)
                        {
                            BuildingKit.Burn(car, rnd);
                            if (smokeCount < MaxSmoke && rnd.NextDouble() < 0.25) Smoke(new Vector3(q.x, 1.2f, q.y), 0.6f);
                        }
                        else if (rnd.NextDouble() < 0.5)
                        {
                            AddLoot(car.GetChild(0).gameObject, "버려진 차", (water, 0.5f, 1), (coffee, 0.4f, 1), (bandage, 0.3f, 1), (Fuel, 0.35f, 1), (kimbap, 0.25f, 1));
                            Kit.Box(car, new Vector3(0, 0.92f, 1.9f), new Vector3(1.4f, 0.06f, 0.5f), Kit.Loot, name: "트렁크 표시");
                        }
                        jams++;
                        if (j == 0 && rnd.NextDouble() < 0.3) RuinSpot(new Vector3(q.x, 0, q.y), d, "버려진 차");
                    }
                }

                // 큰길: 모래주머니 검문소
                if (major && checkpoints < 14 && hw > 3f && rnd.NextDouble() < 0.04)
                {
                    Checkpoint(a + d * (L * 0.5f), d, n, hw, rnd);
                    Vector2 cpq = a + d * (L * 0.5f);
                    RuinSpot(new Vector3(cpq.x, 0, cpq.y), d, "검문소");
                    checkpoints++;
                }

                // 골목: 쓰레기봉투 더미 (수거가 끊긴 지 오래)
                if (alley && trash < 420)
                    for (float t = 4f; t < L - 3f; t += 9f)
                    {
                        if (rnd.NextDouble() > 0.16) continue;
                        int side = rnd.NextDouble() < 0.5 ? 1 : -1;
                        Vector2 q = a + d * t + n * side * Mathf.Max(0.6f, hw - 0.5f);
                        if (InsideBuilding(q, 0.7f)) continue;
                        TrashPile(RuinCell(q), q, yaw, rnd);
                        trash++;
                    }
            }
        }
        FlushRuinCells();
        Debug.Log($"[Gumi] 폐허: 버려진 차 {jams}, 검문소 {checkpoints}, 쓰레기 더미 {trash}, 연기 {smokeCount}, 불 {fireCount}");
    }

    void Checkpoint(Vector2 q, Vector2 d, Vector2 n, float hw, System.Random rnd)
    {
        var mb = RuinCell(q);
        float yaw = Mathf.Atan2(n.x, n.y) * Mathf.Rad2Deg;   // 상자 x축이 길 건너는 방향
        // 모래주머니 벽: 길 반쪽을 막고 가운데로 지나가게
        for (int side = -1; side <= 1; side += 2)
        {
            Vector2 c0 = q + n * side * (hw * 0.55f) + d * (side * 1.2f);
            float len = hw * 0.75f;
            for (int row = 0; row < 3; row++)
                for (float x = -len * 0.5f; x < len * 0.5f; x += 0.62f)
                {
                    float off = (row % 2) * 0.31f;
                    Vector2 p = c0 + n * (x + off);
                    mb.Box(RU_SAND, new Vector3(p.x, 0.16f + row * 0.28f, p.y), new Vector3(0.6f, 0.28f, 0.42f), yaw + (float)rnd.NextDouble() * 6f - 3f);
                }
            // 뒤쪽 콘크리트 방호벽
            Vector2 j = c0 - d * side * 1.6f;
            mb.Box(RU_JERSEY, new Vector3(j.x, 0.4f, j.y), new Vector3(Mathf.Min(len, 3.2f), 0.8f, 0.5f), yaw + side * 8f);
            mb.Box(RU_JERSEY, new Vector3(j.x, 0.85f, j.y), new Vector3(Mathf.Min(len, 3.2f), 0.12f, 0.25f), yaw + side * 8f);
        }
        // 안내판
        Vector2 sp = q + n * (hw * 0.9f) + d * 2.5f;
        var t = Kit.Group(ruinRoot, "검문소 안내판", new Vector3(sp.x, 0, sp.y), Quaternion.identity);
        Vector3 face = new Vector3(d.x, 0, d.y);
        Kit.Box(t, new Vector3(0, 0.9f, 0), new Vector3(0.08f, 1.8f, 0.08f), Kit.Hex("3A3F45"), name: "기둥");
        SignPanel(t, "검문소 · 통제구역", new Vector3(0, 1.65f, 0) - face * 0.06f, -face, new Vector3(1.6f, 0.5f, 0.04f), Kit.Hex("F2C230"), Kit.Hex("1A1A1A"));
        SignPanel(t, "검문소 · 통제구역", new Vector3(0, 1.65f, 0) + face * 0.06f, face, new Vector3(1.6f, 0.5f, 0.04f), Kit.Hex("F2C230"), Kit.Hex("1A1A1A"));
    }

    void TrashPile(MeshBuilder mb, Vector2 q, float yaw, System.Random rnd)
    {
        int n = 3 + rnd.Next(6);
        for (int i = 0; i < n; i++)
        {
            Vector2 p = q + new Vector2((float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f) * 1.2f;
            double r = rnd.NextDouble();
            int sub = r < 0.55 ? RU_BAGW : r < 0.75 ? RU_BAGB : r < 0.9 ? RU_BAGK : RU_BOX;
            float s = sub == RU_BOX ? 0.45f : 0.42f + (float)rnd.NextDouble() * 0.2f;
            float y = s * 0.4f + (i > 3 ? s * 0.6f : 0f);
            mb.Box(sub, new Vector3(p.x, y, p.y), new Vector3(s, s * (sub == RU_BOX ? 0.7f : 0.8f), s * 0.85f), yaw + (float)rnd.NextDouble() * 90f);
            if (sub != RU_BOX) mb.Box(sub, new Vector3(p.x, y + s * 0.45f, p.y), new Vector3(s * 0.25f, s * 0.2f, s * 0.25f), yaw);   // 묶은 매듭
        }
    }

    /// <summary>전봇대 사이 현수막 (BuildRealStreetFurniture에서 부른다)</summary>
    void Banner(Vector3 pole, Vector3 toward, Vector2 roadN)
    {
        EnsureRuinRoots();
        Vector3 dir = toward - pole; dir.y = 0;
        if (dir.magnitude < 6f) return;
        dir.Normalize();
        Vector3 mid = pole + dir * 2.9f + Vector3.up * 3.1f;
        var t = Kit.Group(ruinRoot, "현수막", mid, Quaternion.LookRotation(new Vector3(roadN.x, 0, roadN.y)));
        string txt = BannerTexts[Mathf.Abs((int)(pole.x * 13f + pole.z * 7f)) % BannerTexts.Length];
        bool yellow = (Mathf.Abs((int)pole.x) & 1) == 0;
        Color bg = yellow ? Kit.Hex("F2E6B0") : Kit.Hex("F4F2EC");
        Color fg = yellow ? Kit.Hex("1E3A8A") : Kit.Hex("B0201A");
        // 길 방향으로 길쭉한 천: t 기준 x = 길 방향
        float yawToDir = Vector3.SignedAngle(t.right, dir, Vector3.up);
        var cloth = Kit.Group(t, "천", Vector3.zero, Quaternion.Euler(0, yawToDir, 0));
        Kit.Box(cloth, Vector3.zero, new Vector3(4.6f, 0.85f, 0.02f), bg, name: "천");
        float ch = Mathf.Min(0.42f, 4.2f / (txt.Length + 1) * 1.0f);
        Kit.Label(cloth, txt, new Vector3(0, 0, -0.02f), Quaternion.identity, ch, fg);
        Kit.Label(cloth, txt, new Vector3(0, 0, 0.02f), Quaternion.Euler(0, 180, 0), ch, fg);
        // 끈
        Kit.Box(cloth, new Vector3(-2.45f, 0.35f, 0), new Vector3(0.3f, 0.01f, 0.01f), Kit.Hex("2A2A2A"), name: "끈");
        Kit.Box(cloth, new Vector3(2.45f, 0.35f, 0), new Vector3(0.3f, 0.01f, 0.01f), Kit.Hex("2A2A2A"), name: "끈");
    }

    void FlushRuinCells()
    {
        var mats = new[]
        {
            Kit.Mat(Kit.Hex("8E8A84")), Kit.Mat(Kit.Hex("8E5A44")), Kit.Mat(Kit.Hex("A8916A")), Kit.Mat(Kit.Hex("B9B6AE")),
            Kit.Mat(Kit.Hex("E6E2D6")), Kit.Mat(Kit.Hex("9DB8D0")), Kit.Mat(Kit.Hex("2A2A2C")), Kit.Mat(Kit.Hex("B08A5A")),
            Kit.Mat(Kit.Hex("5A3A2A")), Kit.Mat(Kit.Hex("F4F2EC")),
        };
        int idx = 0;
        foreach (var kv in ruinCells)
            if (!kv.Value.Empty) kv.Value.Build("폐허 " + (idx++), ruinRoot, Vector3.zero, mats, true);
        ruinCells.Clear();
    }

    // ================= 연기 · 불 =================

    static Material sootMat, smokeMat, fireMat;
    static Texture2D softTex, sootTex;

    static Material TransparentMat(string shader, Texture2D tex, Color c, bool additive)
    {
        var sh = Shader.Find(shader) ?? Shader.Find("Universal Render Pipeline/Unlit");
        var m = new Material(sh);
        m.SetTexture("_BaseMap", tex); m.mainTexture = tex;
        m.SetColor("_BaseColor", c); m.color = c;
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", additive ? 2f : 0f);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", additive ? (float)UnityEngine.Rendering.BlendMode.One : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_Cull", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        if (additive) m.EnableKeyword("_BLENDMODE_ADD");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return m;
    }

    static Texture2D SoftTex()
    {
        if (softTex != null) return softTex;
        const int N = 64;
        softTex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "부드러운 점" };
        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - r); a = a * a * (3f - 2f * a);
                a *= 0.75f + 0.25f * Mathf.PerlinNoise(x * 0.15f, y * 0.15f);
                px[y * N + x] = new Color(1, 1, 1, a);
            }
        softTex.SetPixels(px); softTex.Apply();
        return softTex;
    }

    /// <summary>그을음: 아래(창문 위)는 진하고 위로 갈수록 퍼지며 옅어진다</summary>
    static Material SootMat()
    {
        if (sootMat != null) return sootMat;
        const int W = 64, H = 128;
        sootTex = new Texture2D(W, H, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "그을음" };
        var px = new Color[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float v = y / (float)H, u = x / (float)W * 2f - 1f;
                float width = 0.35f + v * 0.65f;
                float edge = Mathf.Clamp01(1f - Mathf.Abs(u) / width);
                float n = Mathf.PerlinNoise(x * 0.09f, y * 0.05f);
                float a = edge * edge * (1f - v * 0.85f) * (0.55f + 0.6f * n);
                a *= Mathf.Clamp01(v * 8f + 0.3f);
                px[y * W + x] = new Color(0.06f, 0.05f, 0.045f, Mathf.Clamp01(a * 1.1f));
            }
        sootTex.SetPixels(px); sootTex.Apply();
        sootMat = TransparentMat("Universal Render Pipeline/Unlit", sootTex, Color.white, false);
        sootMat.name = "그을음";
        return sootMat;
    }

    void Smoke(Vector3 pos, float scale)
    {
        if (smokeMat == null) smokeMat = TransparentMat("Universal Render Pipeline/Particles/Unlit", SoftTex(), new Color(0.22f, 0.21f, 0.2f, 1f), false);
        var go = new GameObject("연기 기둥");
        go.layer = 1;   // TransparentFX: 미니맵에서 빼려고
        go.transform.SetParent(fxRoot, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(-90f, 0, 0);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 10f; main.loop = true; main.prewarm = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(10f * scale, 14f * scale);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 2.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(2.5f * scale, 4.5f * scale);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.1f, 0.095f, 0.09f, 0.85f), new Color(0.24f, 0.23f, 0.22f, 0.75f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 200;
        main.gravityModifier = -0.02f;
        var em = ps.emission; em.rateOverTime = 11f * scale;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 10f; sh.radius = 0.8f * scale;
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(0.7f, 1.3f); vel.y = new ParticleSystem.MinMaxCurve(0f, 0f); vel.z = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 3.2f));
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.85f, 0.85f, 0.85f), 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
        var pr = go.GetComponent<ParticleSystemRenderer>();
        pr.sharedMaterial = smokeMat;
        pr.sortingFudge = 10f;
        pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        pr.receiveShadows = false;
        ps.Play();
        smokeCount++;
    }

    void Fire(Vector3 pos, float scale)
    {
        if (fireMat == null) fireMat = TransparentMat("Universal Render Pipeline/Particles/Unlit", SoftTex(), new Color(1f, 0.55f, 0.2f, 1f), true);
        var go = new GameObject("불");
        go.layer = 1;   // TransparentFX: 미니맵에서 빼려고
        go.transform.SetParent(fxRoot, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(-90f, 0, 0);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 2f; main.loop = true; main.prewarm = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.7f * scale, 1.5f * scale);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.3f, 0.9f), new Color(1f, 0.4f, 0.12f, 0.8f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 80;
        var em = ps.emission; em.rateOverTime = 40f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 15f; sh.radius = 0.9f * scale;
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1f, 1, 0.2f));
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.85f, 0.5f), 0f), new GradientColorKey(new Color(0.9f, 0.25f, 0.05f), 1f) },
                  new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var pr = go.GetComponent<ParticleSystemRenderer>();
        pr.sharedMaterial = fireMat;
        pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        pr.receiveShadows = false;
        ps.Play();
        var lg = new GameObject("불빛");
        lg.layer = 1;
        lg.transform.SetParent(go.transform, false);
        lg.transform.localPosition = new Vector3(0, 0, 0.8f);
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point; l.color = new Color(1f, 0.55f, 0.25f); l.range = 12f; l.intensity = 2.5f; l.shadows = LightShadows.None;
        lg.AddComponent<FireFlicker>();
        fireCount++;
    }
}

/// <summary>불빛 깜빡임</summary>
public class FireFlicker : MonoBehaviour
{
    Light l; float baseI, seed;
    void Awake() { l = GetComponent<Light>(); baseI = l != null ? l.intensity : 1f; seed = Random.value * 100f; }
    void Update()
    {
        if (l == null) return;
        float t = Time.time * 7f + seed;
        l.intensity = baseI * (0.75f + 0.35f * Mathf.PerlinNoise(t, seed));
    }
}
