using UnityEngine;

/// <summary>
/// 컨셉 아트의 건물을 코드로 조립한다. 모든 건물은 로컬 -z 방향이 '정면'(도로 쪽).
/// 층고 3m. 콜라이더는 외곽(벽·기둥)에만 두고 창문·간판 같은 장식에는 두지 않는다.
/// </summary>
public static class BuildingKit
{
    const float H = 3f; // 층고

    static readonly Color[] CarColors =
    {
        Kit.Hex("DADDE0"), Kit.Hex("3A3F45"), Kit.Hex("8A8F96"), Kit.Hex("2F3E5C"), Kit.Hex("5A3A36"), Kit.Hex("E9ECEF")
    };

    static readonly (string name, Color band, Color text)[] Shops =
    {
        ("24시 편의점", Kit.Hex("3A8A5C"), Color.white),
        ("바삭치킨", Kit.Hex("F2C230"), Kit.Hex("2E3135")),
        ("구미약국", Kit.Hex("2E7D6B"), Color.white),
        ("공단부동산", Kit.Hex("34558A"), Color.white),
        ("미래학원", Kit.Hex("E9ECEF"), Kit.Hex("2E3A48")),
        ("동네세탁소", Kit.Hex("6A5A8A"), Color.white),
        ("백반 · 국밥", Kit.Hex("7A5A3A"), Color.white),
        ("PC방", Kit.Hex("2E3A48"), Kit.Hex("6CB4E4")),
    };

    static Transform Root(Transform parent, string name, Vector3 pos, float yaw)
    {
        var root = Kit.Group(parent, name, pos, Quaternion.Euler(0, yaw, 0));
        root.gameObject.AddComponent<OccluderGroup>();
        return root;
    }

    static float R(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);
    static bool Chance(System.Random r, float p) => r.NextDouble() < p;

    // =============================================================
    // 필로티 원룸 (1층 주차장 + 위층 원룸)
    // =============================================================
    public static Transform Oneroom(Transform parent, Vector3 pos, float yaw, float w, float d, int floors, System.Random rng, bool emptyPilotis = false)
    {
        var root = Root(parent, "원룸", pos, yaw);
        Color wall = Chance(rng, 0.6f) ? Kit.Tile : Kit.Hex(Chance(rng, 0.5f) ? "D9CFC0" : "CFC6B6");
        float top = H + floors * H;

        // --- 1층 필로티: 기둥 + 뒤쪽 계단실 ---
        int cols = Mathf.Max(2, Mathf.RoundToInt(w / 4f) + 1);
        for (int i = 0; i < cols; i++)
        {
            float x = Mathf.Lerp(-w / 2 + 0.25f, w / 2 - 0.25f, i / (float)(cols - 1));
            Kit.Box(root, new Vector3(x, H / 2, -d / 2 + 0.25f), new Vector3(0.45f, H, 0.45f), Kit.Brick, true, name: "기둥");
        }
        Kit.Box(root, new Vector3(0, H / 2, d / 4), new Vector3(w, H, d / 2), Kit.TileShade, true, name: "계단실");
        Kit.Box(root, new Vector3(-w / 4, 1.1f, -0.02f), new Vector3(1.0f, 2.2f, 0.1f), Kit.Hex("4A4F55"), name: "현관문");
        Kit.Box(root, new Vector3(w / 4, 1.3f, -0.03f), new Vector3(1.2f, 0.6f, 0.12f), Kit.Hex("8A8F96"), name: "우편함");
        Kit.Box(root, new Vector3(0, H - 0.05f, -d / 4), new Vector3(w, 0.1f, d / 2), Kit.Hex("2A2F36"), name: "필로티천장");
        Kit.Box(root, new Vector3(-w / 2 + 0.25f, 1.4f, -d / 2 + 0.5f), new Vector3(0.18f, 0.28f, 0.06f), Kit.Tech, emission: 1.6f, name: "충전소켓");
        if (!emptyPilotis && Chance(rng, 0.65f) && w > 6.5f) Car(root, new Vector3(R(rng, -w / 4 + 1f, w / 4 - 1f), 0, -d / 4), Chance(rng, 0.5f) ? 90 : 270, rng);

        // --- 위층 외피 ---
        Kit.Box(root, new Vector3(0, H + floors * H / 2, 0), new Vector3(w, floors * H, d), wall, true, name: "외벽");
        UpperFloors(root, w, d, H, floors, rng, true);
        Roof(root, w, d, top, rng);

        // --- 세로 간판 ---
        if (Chance(rng, 0.5f))
        {
            var sign = Kit.Box(root, new Vector3(-w / 2 + 0.1f, H + 1.9f, -d / 2 - 0.45f), new Vector3(0.18f, 3.2f, 0.8f), Kit.Brick, name: "세로간판");
            string txt = Chance(rng, 0.5f) ? "원\n룸\n임\n대" : "투\n룸\n전\n세";
            Kit.Label(root, txt, new Vector3(-w / 2 + 0.0f, H + 1.9f, -d / 2 - 0.45f), Kit.Facing(Vector3.left), 0.62f, Color.white);
            Kit.Label(root, txt, new Vector3(-w / 2 + 0.2f, H + 1.9f, -d / 2 - 0.45f), Kit.Facing(Vector3.right), 0.62f, Color.white);
        }
        return root;
    }

    // =============================================================
    // 1층 상가 + 위층
    // =============================================================
    public static Transform Shop(Transform parent, Vector3 pos, float yaw, float w, float d, int upperFloors, System.Random rng, int shopIndex = -1)
    {
        var root = Root(parent, "상가", pos, yaw);
        var s = Shops[shopIndex >= 0 ? shopIndex % Shops.Length : rng.Next(Shops.Length)];
        float top = H + upperFloors * H;

        Kit.Box(root, new Vector3(0, top / 2, 0), new Vector3(w, top, d), Kit.Hex("CFC6B6"), true, name: "외벽");
        // 유리 쇼윈도 + 문
        Kit.Box(root, new Vector3(0, 1.15f, -d / 2 - 0.03f), new Vector3(w - 0.6f, 2.1f, 0.06f), Kit.GlassLight, name: "쇼윈도");
        Kit.Box(root, new Vector3(0, 1.05f, -d / 2 - 0.06f), new Vector3(1.2f, 2.1f, 0.06f), Kit.Hex("6F9AA1"), name: "문");
        for (int i = 1; i < Mathf.Max(2, (int)(w / 3f)); i++)
            Kit.Box(root, new Vector3(-w / 2 + 0.3f + (w - 0.6f) * i / Mathf.Max(2, (int)(w / 3f)), 1.15f, -d / 2 - 0.07f), new Vector3(0.08f, 2.1f, 0.04f), Kit.Frame, name: "창틀");
        // 간판 띠 + 차양
        Kit.Box(root, new Vector3(0, 2.62f, -d / 2 - 0.15f), new Vector3(w, 0.75f, 0.25f), s.band, name: "간판");
        Kit.Label(root, s.name, new Vector3(0, 2.62f, -d / 2 - 0.29f), Kit.Facing(Vector3.back), 0.5f, s.text);
        Kit.Box(root, new Vector3(0, 2.15f, -d / 2 - 0.6f), new Vector3(w, 0.08f, 1.0f), s.band * 0.8f,
            rot: Quaternion.Euler(-12, 0, 0), name: "차양");

        UpperFloors(root, w, d, H, upperFloors, rng, false);
        Roof(root, w, d, top, rng);
        return root;
    }

    // =============================================================
    // 아파트 (발코니 띠)
    // =============================================================
    public static Transform Apartment(Transform parent, Vector3 pos, float yaw, float w, float d, int floors, int number, System.Random rng)
    {
        var root = Root(parent, "아파트", pos, yaw);
        float h = floors * H;
        Kit.Box(root, new Vector3(0, h / 2, 0), new Vector3(w, h, d), Kit.Hex("E3E6E8"), true, name: "외벽");
        Kit.Box(root, new Vector3(0, 0.6f, 0), new Vector3(w + 0.1f, 1.2f, d + 0.1f), Kit.Hex("8A8F96"), name: "기단");

        int units = Mathf.Max(2, Mathf.RoundToInt(w / 6f));
        for (int f = 1; f < floors; f++)
        {
            float y = f * H;
            // 앞면 발코니: 슬래브 + 유리 난간 (세대별로 끊어서)
            for (int u = 0; u < units; u++)
            {
                float cx = -w / 2 + (u + 0.5f) * w / units;
                float bw = w / units - 0.6f;
                Kit.Box(root, new Vector3(cx, y, -d / 2 - 0.6f), new Vector3(bw, 0.15f, 1.2f), Kit.Hex("D9DDE1"), name: "발코니");
                Kit.Box(root, new Vector3(cx, y + 0.55f, -d / 2 - 1.18f), new Vector3(bw, 0.95f, 0.05f), Kit.Hex("A9C4CC"), name: "난간");
                bool lit = Chance(rng, 0.12f);
                Kit.Box(root, new Vector3(cx, y + 1.4f, -d / 2 - 0.02f), new Vector3(bw - 0.4f, 2.0f, 0.05f), lit ? Kit.WarmLight : Kit.Glass,
                    emission: lit ? 0.8f : 0f, name: "창");
            }
            // 뒷면 창
            for (int u = 0; u < units * 2; u++)
            {
                float cx = -w / 2 + (u + 0.5f) * w / (units * 2);
                Kit.Box(root, new Vector3(cx, y + 1.5f, d / 2 + 0.02f), new Vector3(1.0f, 1.0f, 0.05f), Kit.Glass, name: "창");
            }
        }
        // 동 번호
        Kit.Label(root, $"{number}동", new Vector3(w / 2 + 0.03f, h - 3.5f, 0), Kit.Facing(Vector3.right), 2.4f, Kit.Hex("5E6E80"));
        Kit.Label(root, $"{number}동", new Vector3(-w / 2 - 0.03f, h - 3.5f, 0), Kit.Facing(Vector3.left), 2.4f, Kit.Hex("5E6E80"));
        // 옥상
        Kit.Box(root, new Vector3(w / 4, h + 1.4f, 0), new Vector3(3.5f, 2.8f, 3.5f), Kit.Hex("D3D8DE"), name: "기계실");
        Parapet(root, w, d, h, Kit.Hex("D9DDE1"));
        return root;
    }

    // =============================================================
    // 공장 장식 (벽은 CityGenerator가 세운 hollow 구조 위에 덧붙임)
    // =============================================================
    public static void FactoryDetails(Transform factory, float cx, float cz, float w, float d, float h, float door, int number, System.Random rng)
    {
        // 톱니 지붕: x 방향으로 경사판 + 수직 채광창
        int seg = Mathf.Max(3, Mathf.RoundToInt(w / 4.5f));
        float sw = w / seg;
        for (int i = 0; i < seg; i++)
        {
            float x0 = cx - w / 2 + i * sw;
            float rise = 1.6f;
            float ang = Mathf.Atan2(rise, sw) * Mathf.Rad2Deg;
            float len = Mathf.Sqrt(sw * sw + rise * rise);
            Kit.Box(factory, new Vector3(x0 + sw / 2, h + rise / 2, cz), new Vector3(len, 0.15f, d), Kit.SteelDark, true,
                Quaternion.Euler(0, 0, ang), name: "지붕");
            Kit.Box(factory, new Vector3(x0 + sw - 0.05f, h + rise / 2, cz), new Vector3(0.08f, rise, d - 0.4f), Kit.GlassLight, name: "채광창");
        }
        // 정면 간판 띠
        float front = cz - d / 2 - 0.2f;
        Kit.Box(factory, new Vector3(cx, h - 0.8f, front), new Vector3(w * 0.7f, 1.0f, 0.12f), Kit.Hex("E9ECEF"), name: "간판");
        Kit.Label(factory, $"제{number}공장 · 안드로이드 조립동", new Vector3(cx, h - 0.8f, front - 0.08f), Kit.Facing(Vector3.back), 0.6f, Kit.Hex("2E3A48"));

        // 셔터: 문 위쪽은 내려온 셔터, 문 양옆은 노란 경고 기둥
        foreach (float z in new[] { cz - d / 2, cz + d / 2 })
        {
            float sgn = z < cz ? -1 : 1;
            Kit.Box(factory, new Vector3(cx, (h + 2.9f) / 2, z), new Vector3(door, h - 2.9f, 0.25f), Kit.Hex("8A8F96"), true, name: "셔터");
            for (int k = 0; k < 4; k++)
                Kit.Box(factory, new Vector3(cx, 3.1f + k * 0.35f, z + sgn * 0.14f), new Vector3(door, 0.04f, 0.04f), Kit.Hex("6F757D"), name: "셔터줄");
            Kit.Box(factory, new Vector3(cx - door / 2 - 0.15f, 1.5f, z + sgn * 0.1f), new Vector3(0.25f, 3f, 0.25f), Kit.Hazard, name: "경고기둥");
            Kit.Box(factory, new Vector3(cx + door / 2 + 0.15f, 1.5f, z + sgn * 0.1f), new Vector3(0.25f, 3f, 0.25f), Kit.Hazard, name: "경고기둥");
        }

        // 벽 골판 줄무늬 (앞면)
        for (float x = cx - w / 2 + 1f; x < cx + w / 2 - 1f; x += 1.2f)
        {
            if (Mathf.Abs(x - cx) < door / 2 + 0.6f) continue;
            Kit.Box(factory, new Vector3(x, h / 2, cz - d / 2 - 0.17f), new Vector3(0.06f, h - 1.6f, 0.05f), Kit.Hex("52606F"), name: "골판");
        }

        // 굴뚝
        if (Chance(rng, 0.55f))
        {
            var p = new Vector3(cx + w / 2 - 2f, 0, cz + d / 2 + 2.2f);
            Kit.Cyl(factory, p + Vector3.up * 9f, new Vector3(1.6f, 18f, 1.6f), Kit.Hex("8A8F96"), true, name: "굴뚝");
            Kit.Cyl(factory, p + Vector3.up * 15.5f, new Vector3(1.66f, 0.7f, 1.66f), Kit.Hex("E9ECEF"), name: "굴뚝띠");
            Kit.Cyl(factory, p + Vector3.up * 13.5f, new Vector3(1.66f, 0.7f, 1.66f), Kit.Hex("E9ECEF"), name: "굴뚝띠");
        }
        // 지게차
        if (Chance(rng, 0.5f)) Forklift(factory, new Vector3(cx + R(rng, -w / 3, w / 3), 0, cz + R(rng, -d / 4, d / 4)), R(rng, 0, 360));
    }

    public static Transform Container(Transform parent, Vector3 pos, float yaw, System.Random rng, bool collider = true)
    {
        Color[] cols = { Kit.Hex("3D5A7A"), Kit.Hex("8A4E36"), Kit.Hex("7A8088"), Kit.Hex("3E6B57") };
        Color c = cols[rng.Next(cols.Length)];
        var t = Kit.Group(parent, "컨테이너", pos, Quaternion.Euler(0, yaw, 0));
        Kit.Box(t, new Vector3(0, 1.3f, 0), new Vector3(2.4f, 2.6f, 6f), c, collider, name: "몸체");
        for (float z = -2.7f; z <= 2.7f; z += 0.45f)
        {
            Kit.Box(t, new Vector3(1.22f, 1.3f, z), new Vector3(0.05f, 2.4f, 0.12f), c * 0.82f, name: "골");
            Kit.Box(t, new Vector3(-1.22f, 1.3f, z), new Vector3(0.05f, 2.4f, 0.12f), c * 0.82f, name: "골");
        }
        Kit.Box(t, new Vector3(0, 1.3f, 3.02f), new Vector3(2.3f, 2.5f, 0.05f), c * 0.75f, name: "문");
        Kit.Box(t, new Vector3(-0.4f, 1.3f, 3.06f), new Vector3(0.06f, 2.3f, 0.05f), Kit.Hex("A9AFB6"), name: "잠금대");
        Kit.Box(t, new Vector3(0.4f, 1.3f, 3.06f), new Vector3(0.06f, 2.3f, 0.05f), Kit.Hex("A9AFB6"), name: "잠금대");
        return t;
    }

    // =============================================================
    // 공통 부품
    // =============================================================

    /// <summary>위층 창문/실외기/층 띠 (네 면)</summary>
    static void UpperFloors(Transform root, float w, float d, float baseY, int floors, System.Random rng, bool sideWindows)
    {
        for (int f = 0; f < floors; f++)
        {
            float y0 = baseY + f * H;
            Kit.Box(root, new Vector3(0, y0, 0), new Vector3(w + 0.08f, 0.14f, d + 0.08f), Kit.Hex("BFB29A"), name: "층띠");
            FaceWindows(root, Vector3.back, w, d, y0, rng);
            FaceWindows(root, Vector3.forward, w, d, y0, rng);
            if (sideWindows)
            {
                FaceWindows(root, Vector3.left, w, d, y0, rng);
                FaceWindows(root, Vector3.right, w, d, y0, rng);
            }
        }
    }

    static void FaceWindows(Transform root, Vector3 n, float w, float d, float y0, System.Random rng)
    {
        bool front = Mathf.Abs(n.z) > 0.5f;
        float faceW = front ? w : d;
        float half = front ? d / 2 : w / 2;
        int count = Mathf.Max(1, Mathf.FloorToInt(faceW / 3.2f));
        Vector3 along = front ? Vector3.right : Vector3.forward;
        Quaternion rot = Quaternion.LookRotation(n);

        for (int i = 0; i < count; i++)
        {
            float u = -faceW / 2 + faceW * (i + 0.5f) / count;
            Vector3 c = along * u + n * half + Vector3.up * (y0 + 1.6f);
            Kit.Box(root, c + n * 0.05f, new Vector3(1.4f, 1.3f, 0.1f), Kit.Frame, rot: rot, name: "창틀");
            bool lit = Chance(rng, 0.15f);
            Kit.Box(root, c + n * 0.1f, new Vector3(1.2f, 1.1f, 0.05f), lit ? Kit.WarmLight : Kit.Glass, rot: rot,
                emission: lit ? 0.9f : 0f, name: "유리");
            if (Chance(rng, 0.3f))
                for (int b = -1; b <= 1; b++)
                    Kit.Box(root, c + n * 0.14f + along * (b * 0.35f), new Vector3(0.04f, 1.1f, 0.04f), Kit.Hex("8A8F96"), rot: rot, name: "방범창");
            if (Chance(rng, 0.4f))
            {
                Vector3 ac = along * (u + 1.15f) + n * (half + 0.22f) + Vector3.up * (y0 + 0.55f);
                Kit.Box(root, ac, new Vector3(0.8f, 0.55f, 0.4f), Kit.Hex("DADDE0"), rot: rot, name: "실외기");
                Kit.Cyl(root, ac + n * 0.21f, new Vector3(0.38f, 0.02f, 0.38f), Kit.Hex("8A8F96"),
                    rot: rot * Quaternion.Euler(90, 0, 0), name: "팬");
            }
        }
    }

    static void Parapet(Transform root, float w, float d, float top, Color c)
    {
        Kit.Box(root, new Vector3(0, top + 0.4f, -d / 2 + 0.1f), new Vector3(w, 0.8f, 0.2f), c, name: "난간벽");
        Kit.Box(root, new Vector3(0, top + 0.4f, d / 2 - 0.1f), new Vector3(w, 0.8f, 0.2f), c, name: "난간벽");
        Kit.Box(root, new Vector3(-w / 2 + 0.1f, top + 0.4f, 0), new Vector3(0.2f, 0.8f, d), c, name: "난간벽");
        Kit.Box(root, new Vector3(w / 2 - 0.1f, top + 0.4f, 0), new Vector3(0.2f, 0.8f, d), c, name: "난간벽");
    }

    static void Roof(Transform root, float w, float d, float top, System.Random rng)
    {
        Parapet(root, w, d, top, Kit.Concrete);
        Kit.Box(root, new Vector3(0, top + 0.03f, 0), new Vector3(w - 0.4f, 0.06f, d - 0.4f), Kit.Hex("8A8F96"), name: "옥상바닥");
        Kit.Box(root, new Vector3(w / 2 - 1.6f, top + 1.2f, d / 2 - 1.6f), new Vector3(2.4f, 2.4f, 2.4f), Kit.Concrete, name: "계단탑");
        Kit.Cyl(root, new Vector3(-w / 2 + 1.3f, top + 0.8f, d / 2 - 1.3f), new Vector3(1.4f, 1.6f, 1.4f), Kit.Hex("B9C4CC"), name: "물탱크");
        if (Chance(rng, 0.5f))
        {
            int n = Mathf.Max(1, (int)(w / 2.6f) - 1);
            for (int i = 0; i < n; i++)
            {
                float x = -w / 2 + 1.6f + i * 2.2f;
                if (x > w / 2 - 3.2f) break;
                Kit.Box(root, new Vector3(x, top + 0.7f, -d / 4), new Vector3(1.9f, 0.08f, 1.2f), Kit.Hex("2B4A6B"),
                    rot: Quaternion.Euler(-22, 0, 0), name: "태양광");
                Kit.Box(root, new Vector3(x, top + 0.7f, -d / 4 - 0.62f), new Vector3(1.9f, 0.04f, 0.04f), Kit.Tech, emission: 0.6f,
                    rot: Quaternion.Euler(-22, 0, 0), name: "태양광테");
            }
        }
    }

    /// <summary>주차된 차. 첫 번째 자식이 차체(콜라이더 있음).</summary>
    public static Transform Car(Transform parent, Vector3 localPos, float yaw, System.Random rng)
    {
        Color c = CarColors[rng.Next(CarColors.Length)];
        var t = Kit.Group(parent, "주차된 차", localPos, Quaternion.Euler(0, yaw, 0));
        Kit.Box(t, new Vector3(0, 0.55f, 0), new Vector3(1.8f, 0.7f, 4.2f), c, true, name: "차체");
        Kit.Box(t, new Vector3(0, 1.1f, -0.2f), new Vector3(1.6f, 0.5f, 2.2f), c * 0.92f, true, name: "지붕");   // 지붕도 부딪힘 (넘을 때 몸이 지붕을 뚫지 않게)
        Kit.Box(t, new Vector3(0, 1.08f, -1.32f), new Vector3(1.5f, 0.42f, 0.05f), Kit.Glass, rot: Quaternion.Euler(-30, 0, 0), name: "앞유리");
        Kit.Box(t, new Vector3(0, 1.08f, 0.92f), new Vector3(1.5f, 0.4f, 0.05f), Kit.Glass, rot: Quaternion.Euler(30, 0, 0), name: "뒷유리");
        foreach (var wpos in new[] { new Vector2(-0.85f, -1.35f), new Vector2(0.85f, -1.35f), new Vector2(-0.85f, 1.35f), new Vector2(0.85f, 1.35f) })
            Kit.Cyl(t, new Vector3(wpos.x, 0.32f, wpos.y), new Vector3(0.64f, 0.22f, 0.64f), Kit.Dark, rot: Quaternion.Euler(0, 0, 90), name: "바퀴");
        Kit.Box(t, new Vector3(-0.6f, 0.65f, -2.11f), new Vector3(0.3f, 0.12f, 0.03f), Kit.Hex("F4F1EC"), name: "전조등");
        Kit.Box(t, new Vector3(0.6f, 0.65f, -2.11f), new Vector3(0.3f, 0.12f, 0.03f), Kit.Hex("F4F1EC"), name: "전조등");
        return t;
    }

    /// <summary>불탄 차로 바꾼다: 그을린 몸체, 녹, 유리 없음, 바퀴 빠져 주저앉음</summary>
    public static void Burn(Transform car, System.Random rnd)
    {
        var charred = Kit.Mat(Kit.Hex("26221F"));
        var rust = Kit.Mat(Kit.Hex("5A3A2A"));
        var ash = Kit.Mat(Kit.Hex("4A4642"));
        foreach (var r in car.GetComponentsInChildren<MeshRenderer>())
        {
            string n = r.gameObject.name;
            if (n.Contains("유리") || n.Contains("전조등")) { r.enabled = false; continue; }
            if (n.Contains("바퀴")) { r.transform.localScale = new Vector3(r.transform.localScale.x * 0.7f, r.transform.localScale.y, r.transform.localScale.z * 0.7f); continue; }
            double k = rnd.NextDouble();
            r.sharedMaterial = k < 0.5 ? charred : k < 0.8 ? rust : ash;
        }
        car.localPosition += Vector3.down * 0.12f;
        car.localRotation *= Quaternion.Euler((float)rnd.NextDouble() * 4f - 2f, 0, (float)rnd.NextDouble() * 4f - 2f);
        car.name = "불탄 차";
    }

    static void Forklift(Transform parent, Vector3 pos, float yaw)
    {
        var t = Kit.Group(parent, "지게차", pos, Quaternion.Euler(0, yaw, 0));
        Kit.Box(t, new Vector3(0, 0.7f, 0), new Vector3(1.2f, 0.9f, 2.0f), Kit.Hazard, true, name: "몸체");
        Kit.Box(t, new Vector3(0, 1.6f, 0.3f), new Vector3(1.1f, 0.08f, 1.1f), Kit.Hex("3A3F45"), name: "지붕");
        foreach (float x in new[] { -0.5f, 0.5f })
        {
            Kit.Box(t, new Vector3(x, 1.15f, 0.8f), new Vector3(0.06f, 0.95f, 0.06f), Kit.Hex("3A3F45"), name: "기둥");
            Kit.Box(t, new Vector3(x, 1.15f, -0.2f), new Vector3(0.06f, 0.95f, 0.06f), Kit.Hex("3A3F45"), name: "기둥");
            Kit.Box(t, new Vector3(x * 0.6f, 0.08f, -1.6f), new Vector3(0.12f, 0.06f, 1.1f), Kit.Hex("4A4F55"), name: "포크");
        }
        Kit.Box(t, new Vector3(0, 1.3f, -1.05f), new Vector3(1.0f, 2.4f, 0.1f), Kit.Hex("4A4F55"), name: "마스트");
    }

    // =============================================================
    // 거리 요소
    // =============================================================

    /// <summary>yaw: 완목(가로대)이 향하는 방향. 도로와 직각이 되도록 준다.</summary>
    public static Transform UtilityPole(Transform parent, Vector3 pos, float yaw, bool transformer)
    {
        var t = Kit.Group(parent, "전봇대", pos, Quaternion.Euler(0, yaw, 0));
        Kit.Cyl(t, new Vector3(0, 4.5f, 0), new Vector3(0.3f, 9f, 0.3f), Kit.Pole, true, name: "기둥");
        Kit.Box(t, new Vector3(0, 8.4f, 0), new Vector3(0.12f, 0.12f, 2.0f), Kit.Hex("6A6258"), name: "완목");
        Kit.Box(t, new Vector3(0, 7.4f, 0), new Vector3(0.1f, 0.1f, 1.4f), Kit.Hex("6A6258"), name: "완목");
        if (transformer) Kit.Cyl(t, new Vector3(0.35f, 6.4f, 0), new Vector3(0.5f, 0.9f, 0.5f), Kit.Hex("8A8F96"), name: "변압기");
        Kit.Box(t, new Vector3(0, 2.0f, 0.17f), new Vector3(0.25f, 0.4f, 0.03f), Kit.Tile, name: "전단지");
        return t;
    }

    /// <summary>두 전봇대 사이 전깃줄 (처진 곡선)</summary>
    public static void Wires(Transform parent, Vector3 a, Vector3 b)
    {
        float[] offs = { -0.9f, 0.9f, 0f };
        float[] heights = { 8.45f, 8.45f, 7.45f };
        Vector3 dir = b - a; dir.y = 0;
        Vector3 side = Vector3.Cross(dir.normalized, Vector3.up); // 줄과 직각 = 완목 방향
        if (side.sqrMagnitude < 0.01f) side = Vector3.right;
        side.Normalize();
        for (int k = 0; k < offs.Length; k++)
        {
            var go = new GameObject("전깃줄");
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = Kit.Mat(Kit.Wire);
            lr.widthMultiplier = 0.04f;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            const int N = 9;
            lr.positionCount = N;
            Vector3 pa = a + Vector3.up * heights[k] + side * offs[k];
            Vector3 pb = b + Vector3.up * heights[k] + side * offs[k];
            float sag = 0.6f + Vector3.Distance(a, b) * 0.02f;
            for (int i = 0; i < N; i++)
            {
                float t = i / (float)(N - 1);
                Vector3 p = Vector3.Lerp(pa, pb, t);
                p.y -= sag * 4f * t * (1 - t);
                lr.SetPosition(i, p);
            }
        }
    }

    public static Transform StreetLight(Transform parent, Vector3 pos, float yaw)
    {
        var t = Kit.Group(parent, "가로등", pos, Quaternion.Euler(0, yaw, 0));
        Kit.Cyl(t, new Vector3(0, 3.2f, 0), new Vector3(0.18f, 6.4f, 0.18f), Kit.Hex("5A5E64"), true, name: "기둥");
        Kit.Box(t, new Vector3(0, 6.35f, 0.8f), new Vector3(0.1f, 0.1f, 1.7f), Kit.Hex("5A5E64"), name: "팔");
        Kit.Box(t, new Vector3(0, 6.25f, 1.55f), new Vector3(0.45f, 0.14f, 0.7f), Kit.Hex("5A5E64"), name: "갓");
        Kit.Box(t, new Vector3(0, 6.15f, 1.55f), new Vector3(0.36f, 0.04f, 0.56f), Kit.WarmLight, emission: 2.2f, name: "램프");

        var lightGo = new GameObject("가로등 불빛");
        lightGo.transform.SetParent(t, false);
        lightGo.transform.localPosition = new Vector3(0, 5.8f, 1.55f);
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = Kit.WarmLight;
        l.range = 13f;
        l.intensity = 3f;
        l.shadows = LightShadows.None;
        l.enabled = false;
        NightLights.Register(l);
        return t;
    }
}
