using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 건물 외벽 텍스처를 코드로 그린다 (이미지 파일 없음).
/// 텍스처 한 장 = 가로 2칸(6.4m) × 한 층(3m). 1층 상가는 3.5m 높이 한 장.
/// 한 장마다 네 가지 맵을 같이 그린다:
///   색 (창마다 커튼·블라인드·깨진 유리·판자 등 다르게, 빗물 얼룩)
///   노멀 (창틀은 튀어나오고 유리는 들어가 보이게)
///   매끈함 (유리는 반짝, 벽은 거칠게)
///   발광 (밤에 몇몇 창에 불이 켜짐 — 발전기를 돌리는 생존자들)
/// 같은 스타일·색이면 머티리얼을 공유해서 배치가 가볍다.
/// </summary>
public static class Facade
{
    public enum Style { Villa, Apt, Shop, House, School, Public, Office }
    public const float BayWidth = 6.4f;   // 텍스처 한 장 가로 (m)
    public const float Floor = 3f;
    public const float ShopFloor = 3.5f;

    static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
    static readonly List<Material> glowing = new List<Material>();
    static Shader lit;

    static readonly Color[] VillaWalls = { Kit.Hex("E3D8C4"), Kit.Hex("D9CFC0"), Kit.Hex("CFC6B6"), Kit.Hex("C9B9A0"), Kit.Hex("B8A48C"), Kit.Hex("ECECE8"), Kit.Hex("A9715A"), Kit.Hex("8E5A44") };
    static readonly Color[] AptWalls = { Kit.Hex("ECE9E2"), Kit.Hex("E2E0DA"), Kit.Hex("DCD7CC"), Kit.Hex("E8E1D2") };
    static readonly Color[] ShopWalls = { Kit.Hex("B9B6AE"), Kit.Hex("9E9A92"), Kit.Hex("C8C1B3"), Kit.Hex("8E9399"), Kit.Hex("D0CBC0"), Kit.Hex("6E747C") };
    static readonly Color[] HouseWalls = { Kit.Hex("D8CDB8"), Kit.Hex("C4B49A"), Kit.Hex("E6E0D4"), Kit.Hex("9C6A52") };
    static readonly Color[] SchoolWalls = { Kit.Hex("E8DCC0"), Kit.Hex("DCC9A8") };
    static readonly Color[] PublicWalls = { Kit.Hex("C9CCCF"), Kit.Hex("B5BBC0") };
    static readonly Color[] SignBands = { Kit.Hex("3A8A5C"), Kit.Hex("F2C230"), Kit.Hex("2E7D6B"), Kit.Hex("34558A"), Kit.Hex("C8402E"), Kit.Hex("6A5A8A"), Kit.Hex("2E3A48"), Kit.Hex("E8892B") };

    public static int Variants(Style s)
    {
        switch (s)
        {
            case Style.Apt: return AptWalls.Length;
            case Style.Shop: case Style.Office: return ShopWalls.Length;
            case Style.House: return HouseWalls.Length;
            case Style.School: return SchoolWalls.Length;
            case Style.Public: return PublicWalls.Length;
            default: return VillaWalls.Length;
        }
    }
    public static int SignVariants => SignBands.Length;

    /// <summary>밤에 창문 불빛 세기 (0 = 낮). GameManager가 매 프레임 준다.</summary>
    public static void SetNightGlow(float k)
    {
        Color c = new Color(1f, 0.78f, 0.5f) * (k * 2.2f);
        for (int i = glowing.Count - 1; i >= 0; i--)
        {
            if (glowing[i] == null) { glowing.RemoveAt(i); continue; }
            glowing[i].SetColor("_EmissionColor", c);
        }
    }

    static Material Make(string key, Texture2D tex, Color tint, float smooth)
    {
        if (lit == null) lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m = new Material(lit) { name = key };
        if (tex != null) { m.SetTexture("_BaseMap", tex); m.mainTexture = tex; }
        m.SetColor("_BaseColor", tint);
        m.color = tint;
        m.SetFloat("_Smoothness", smooth);
        cache[key] = m;
        return m;
    }

    static Material MakeFull(string key, Canvas cv)
    {
        var m = Make(key, cv.BaseTex(key), Color.white, 1f);
        m.SetTexture("_BumpMap", cv.NormalTex(key));
        m.SetFloat("_BumpScale", 1f);
        m.EnableKeyword("_NORMALMAP");
        m.SetTexture("_MetallicGlossMap", cv.GlossTex(key));
        m.EnableKeyword("_METALLICSPECGLOSSMAP");
        m.SetFloat("_Smoothness", 1f);
        m.SetFloat("_GlossMapScale", 1f);
        if (cv.HasGlow)
        {
            m.SetTexture("_EmissionMap", cv.GlowTex(key));
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.black);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            glowing.Add(m);
        }
        return m;
    }

    public static Material Textured(string key, Texture2D tex, Color tint, float smooth)
    {
        if (cache.TryGetValue(key, out var m) && m != null) return m;
        return Make(key, tex, tint, smooth);
    }

    public static Material Wall(Style s, int variant)
    {
        string key = $"Wall2_{s}_{variant}";
        if (cache.TryGetValue(key, out var m) && m != null) return m;
        return MakeFull(key, DrawWall(s, variant));
    }

    public static Material Storefront(int variant)
    {
        string key = $"Store2_{variant}";
        if (cache.TryGetValue(key, out var m) && m != null) return m;
        return MakeFull(key, DrawStore(variant));
    }

    public static Material Roof(Style s, int variant)
    {
        Color c;
        switch (s)
        {
            case Style.House: c = new[] { Kit.Hex("7A4A3A"), Kit.Hex("4E5A6A"), Kit.Hex("6A6A66"), Kit.Hex("5E7A5E") }[variant % 4]; break;
            case Style.Villa: c = new[] { Kit.Hex("5E8A6A"), Kit.Hex("6C8F74"), Kit.Hex("8A8C88"), Kit.Hex("4F7A5E") }[variant % 4]; break; // 옥상 방수 페인트
            case Style.Apt: c = Kit.Hex("8A8C88"); break;
            default: c = new[] { Kit.Hex("7E8079"), Kit.Hex("6F726E"), Kit.Hex("5E8A6A") }[variant % 3]; break;
        }
        string key = $"Roof_{ColorUtility.ToHtmlStringRGB(c)}";
        if (cache.TryGetValue(key, out var m) && m != null) return m;
        // 옥상: 얼룩진 방수 페인트 질감
        return Make(key, SurfaceTextures.RoofTex(c, key), Color.white, 0.18f);
    }

    /// <summary>이 스타일·변형의 바탕 벽색 (옥상 난간 등에 씀)</summary>
    public static Color WallTint(Style s, int v) => WallColor(s, v);

    static Color WallColor(Style s, int v)
    {
        switch (s)
        {
            case Style.Apt: return AptWalls[v % AptWalls.Length];
            case Style.Shop: case Style.Office: return ShopWalls[v % ShopWalls.Length];
            case Style.House: return HouseWalls[v % HouseWalls.Length];
            case Style.School: return SchoolWalls[v % SchoolWalls.Length];
            case Style.Public: return PublicWalls[v % PublicWalls.Length];
            default: return VillaWalls[v % VillaWalls.Length];
        }
    }

    // ================= 그리기 도구 =================

    const int W = 512, H = 256;   // 2칸 × 1층 (1px ≈ 1.25cm)

    /// <summary>색·높이·매끈함·발광을 함께 칠하는 화판. y=0이 아래.</summary>
    class Canvas
    {
        public readonly Color[] col = new Color[W * H];
        public readonly float[] hgt = new float[W * H];
        public readonly float[] gloss = new float[W * H];
        public readonly float[] glow = new float[W * H];
        public readonly Vector2[] pn = new Vector2[W * H];   // 실사 노멀 (사진에서 옴)
        public bool HasGlow;
        public readonly System.Random rng;
        public Canvas(int seed) { rng = new System.Random(seed); }
        public float R() => (float)rng.NextDouble();

        public void Rect(int x0, int y0, int x1, int y1, Color c, float h, float g)
        {
            for (int y = Mathf.Max(0, y0); y < Mathf.Min(H, y1); y++)
                for (int x = Mathf.Max(0, x0); x < Mathf.Min(W, x1); x++)
                {
                    int i = y * W + x;
                    col[i] = c; hgt[i] = h; gloss[i] = g; pn[i] = Vector2.zero;
                }
        }

        public void Tint(int x0, int y0, int x1, int y1, float k)
        {
            for (int y = Mathf.Max(0, y0); y < Mathf.Min(H, y1); y++)
                for (int x = Mathf.Max(0, x0); x < Mathf.Min(W, x1); x++) col[y * W + x] *= k;
        }

        public void Glow(int x0, int y0, int x1, int y1, float k)
        {
            HasGlow = true;
            for (int y = Mathf.Max(0, y0); y < Mathf.Min(H, y1); y++)
                for (int x = Mathf.Max(0, x0); x < Mathf.Min(W, x1); x++) glow[y * W + x] = k;
        }

        /// <summary>벽 바탕: 미세한 얼룩 + 아래쪽 빗물 자국 + 위에서 흘러내린 때</summary>
        public void Wall(Color c, float roughness, CC0Lib.Photo p = null)
        {
            if (p != null) { PhotoWall(c, p); return; }
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float n = Mathf.PerlinNoise(x * 0.03f + 3.1f, y * 0.03f + 7.7f) - 0.5f;
                    float fine = (R() - 0.5f) * roughness;
                    float streak = Mathf.PerlinNoise(x * 0.18f, 0.5f) > 0.72f ? (y / (float)H) * 0.06f : 0f;   // 위에서 흘러내린 때
                    float bottom = y < 18 ? (18 - y) / 18f * 0.12f : 0f;
                    int i = y * W + x;
                    col[i] = c * (1f + n * 0.08f + fine - streak - bottom);
                    hgt[i] = 0.5f + fine * 0.6f + n * 0.04f;
                    gloss[i] = 0.08f;
                }
        }

        /// <summary>실사 벽 (미장·도장·콘크리트): 사진의 밝기 결만 가져오고 색은 c. 빗물 자국은 그대로.</summary>
        void PhotoWall(Color c, CC0Lib.Photo p)
        {
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float n = Mathf.PerlinNoise(x * 0.03f + 3.1f, y * 0.03f + 7.7f) - 0.5f;
                    float streak = Mathf.PerlinNoise(x * 0.18f, 0.5f) > 0.72f ? (y / (float)H) * 0.06f : 0f;
                    float bottom = y < 18 ? (18 - y) / 18f * 0.12f : 0f;
                    float d = Mathf.Lerp(1f, p.Lum(x, y) / p.meanLum, 0.85f);
                    int i = y * W + x;
                    col[i] = c * (d * (1f + n * 0.06f) - streak - bottom);
                    hgt[i] = 0.5f;
                    pn[i] = p.Nrm(x, y) * 0.9f;
                    gloss[i] = 0.08f;
                }
        }

        /// <summary>사진을 그대로 깔되 평균색만 c로 맞춘다 (벽돌·타일). gloss는 밝은 곳(타일 면)일수록 높게.</summary>
        void PhotoTinted(Color c, CC0Lib.Photo p, float g0, float g1)
        {
            Color k = new Color(c.r / Mathf.Max(0.02f, p.mean.r), c.g / Mathf.Max(0.02f, p.mean.g), c.b / Mathf.Max(0.02f, p.mean.b));
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    Color a = p.A(x, y);
                    float bottom = y < 18 ? (18 - y) / 18f * 0.1f : 0f;
                    col[i] = new Color(a.r * k.r, a.g * k.g, a.b * k.b) * (1f - bottom);
                    hgt[i] = 0.5f;
                    pn[i] = p.Nrm(x, y);
                    gloss[i] = Mathf.Lerp(g0, g1, Mathf.Clamp01((p.Lum(x, y) / p.meanLum - 0.85f) * 5f));
                }
        }

        /// <summary>붉은 벽돌 (한국 빌라 단골)</summary>
        public void Bricks(Color c)
        {
            var ph = CC0Lib.Get("red_brick_128");
            if (ph != null) { PhotoTinted(c, ph, 0.04f, 0.1f); return; }
            const int bw = 28, bh = 11;
            for (int y = 0; y < H; y++)
            {
                int row = y / bh;
                int off = (row % 2) * (bw / 2);
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    bool mortar = (y % bh) < 2 || ((x + off) % bw) < 2;
                    int brick = row * 97 + (x + off) / bw;
                    float bk = 0.86f + (((brick * 7919) ^ (brick >> 3)) & 1023) / 1023f * 0.24f;
                    if (mortar) { col[i] = Kit.Hex("B9B2A6"); hgt[i] = 0.42f; }
                    else { col[i] = c * bk * (0.95f + R() * 0.08f); hgt[i] = 0.55f + R() * 0.03f; }
                    gloss[i] = 0.06f;
                }
            }
        }

        /// <summary>흰 타일 외장 (90년대 빌라)</summary>
        public void Tiles(Color c)
        {
            var ph = CC0Lib.Get("long_white_tiles_128");
            if (ph != null) { PhotoTinted(c, ph, 0.05f, 0.5f); return; }
            const int t = 24;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    bool grout = (x % t) < 2 || (y % (t / 2)) < 2;
                    col[i] = grout ? c * 0.78f : c * (0.97f + R() * 0.05f);
                    hgt[i] = grout ? 0.45f : 0.55f;
                    gloss[i] = grout ? 0.05f : 0.45f;
                }
        }

        /// <summary>
        /// 창문 하나: 바깥 창틀(튀어나옴) → 유리(들어감, 반짝) → 안쪽 상태 (커튼/블라인드/어둠/깨짐/판자) + 창턱.
        /// </summary>
        public void Window(int x0, int y0, int x1, int y1, Color frame, int kind, bool mullion, float glowChance)
        {
            int b = 5;
            Rect(x0 - 3, y0 - 6, x1 + 3, y0, frame * 0.8f, 0.85f, 0.2f);          // 창턱
            Rect(x0, y0, x1, y1, frame, 0.8f, 0.35f);                              // 창틀
            Color glass = Kit.Hex("2C3A46");
            for (int y = y0 + b; y < y1 - b; y++)
                for (int x = x0 + b; x < x1 - b; x++)
                {
                    float t = (y - y0) / (float)(y1 - y0);
                    int i = y * W + x;
                    Color g = Color.Lerp(glass * 0.75f, glass * 1.45f, t);
                    float diag = ((x - x0) + (y - y0) * 0.8f) % 120f;
                    if (diag < 10f) g = Color.Lerp(g, Color.white, 0.16f);
                    col[i] = g; hgt[i] = 0.22f; gloss[i] = 0.92f;
                }
            int ix0 = x0 + b, ix1 = x1 - b, iy0 = y0 + b, iy1 = y1 - b;
            switch (kind)
            {
                case 1: // 커튼 (양옆으로 젖힘)
                    {
                        Color cur = new[] { Kit.Hex("B8A07A"), Kit.Hex("8A5A4A"), Kit.Hex("D8D0C0"), Kit.Hex("5A6A7A") }[rng.Next(4)];
                        int w = (ix1 - ix0) / 4;
                        for (int y = iy0; y < iy1; y++)
                            for (int x = ix0; x < ix1; x++)
                                if (x < ix0 + w || x > ix1 - w)
                                {
                                    int i = y * W + x;
                                    col[i] = cur * (0.8f + 0.2f * Mathf.Sin(x * 0.7f)) * 0.75f;
                                    gloss[i] = 0.5f;
                                }
                        break;
                    }
                case 2: // 블라인드 반쯤
                    {
                        int stop = iy1 - (iy1 - iy0) * (2 + rng.Next(5)) / 10;
                        for (int y = stop; y < iy1; y++)
                            for (int x = ix0; x < ix1; x++)
                            {
                                int i = y * W + x;
                                col[i] = Kit.Hex("C9C2B2") * ((y % 6) < 2 ? 0.8f : 1f) * 0.8f;
                                gloss[i] = 0.4f;
                            }
                        break;
                    }
                case 3: // 깨진 유리: 구멍은 까맣고 금이 감
                    {
                        int cx = ix0 + rng.Next(Mathf.Max(1, ix1 - ix0)), cy = iy0 + rng.Next(Mathf.Max(1, iy1 - iy0));
                        for (int y = iy0; y < iy1; y++)
                            for (int x = ix0; x < ix1; x++)
                            {
                                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                                int i = y * W + x;
                                if (d < 16 + rng.Next(6)) { col[i] = Kit.Hex("0E1114"); gloss[i] = 0f; hgt[i] = 0.1f; }
                                else if (((int)(Mathf.Atan2(y - cy, x - cx) * 9f) % 7 == 0) && d < 60) col[i] *= 1.6f;
                            }
                        break;
                    }
                case 4: // 판자로 막음 (폐허 느낌)
                    {
                        Color wood = Kit.Hex("7A5C40");
                        for (int k = 0; k < 3; k++)
                        {
                            int yy = iy0 + (iy1 - iy0) * (k + 1) / 4;
                            for (int y = yy - 7; y < yy + 7; y++)
                                for (int x = ix0 - 4; x < ix1 + 4; x++)
                                {
                                    if (y < 0 || y >= H || x < 0 || x >= W) continue;
                                    int i = y * W + x;
                                    col[i] = wood * (0.85f + 0.15f * Mathf.PerlinNoise(x * 0.2f, k * 3f)); hgt[i] = 0.9f; gloss[i] = 0.05f;
                                }
                        }
                        break;
                    }
            }
            if (mullion) Rect((x0 + x1) / 2 - 3, y0, (x0 + x1) / 2 + 3, y1, frame, 0.8f, 0.35f);
            // 밤 불빛: 커튼 있는 창이나 블라인드 창 일부
            if ((kind == 1 || kind == 2 || kind == 0) && R() < glowChance)
                Glow(ix0, iy0, ix1, iy1, 0.6f + R() * 0.4f);
        }

        public int RandomKind(float broken)
        {
            float r = R();
            if (r < broken * 0.5f) return 4;
            if (r < broken) return 3;
            if (r < broken + 0.25f) return 1;
            if (r < broken + 0.45f) return 2;
            return 0;
        }

        /// <summary>실외기 (벽에 매단 에어컨 실외기, 철제 받침)</summary>
        public void AirCon(int x, int y)
        {
            Rect(x - 4, y - 6, x + 64, y - 2, Kit.Hex("5A5E64"), 0.75f, 0.3f);
            Rect(x, y, x + 60, y + 40, Kit.Hex("D4D6D2"), 0.95f, 0.3f);
            for (int k = 0; k < 6; k++) Rect(x + 30, y + 6 + k * 5, x + 56, y + 8 + k * 5, Kit.Hex("8A8E92"), 0.85f, 0.2f);
            Rect(x + 6, y + 8, x + 26, y + 32, Kit.Hex("9A9EA2"), 0.88f, 0.2f);
        }

        public void Pipe(int x, Color c)
        {
            for (int y = 0; y < H; y++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int i = y * W + x + dx;
                    if (x + dx < 0 || x + dx >= W) continue;
                    float sh = 1f - Mathf.Abs(dx) / 6f;
                    col[i] = c * (0.6f + 0.5f * sh); hgt[i] = 0.7f + 0.3f * sh; gloss[i] = 0.45f;
                }
        }

        // ---------- 텍스처로 굽기 ----------

        Texture2D NewTex(string name, bool linear)
        {
            return new Texture2D(W, H, TextureFormat.RGBA32, true, linear)
            { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
        }

        public Texture2D BaseTex(string key)
        {
            var t = NewTex(key, false);
            var px = new Color32[W * H];
            for (int i = 0; i < px.Length; i++) { var c = col[i]; c.a = 1; px[i] = c; }
            t.SetPixels32(px); t.Apply(true);
            return t;
        }

        public Texture2D NormalTex(string key)
        {
            var t = NewTex(key + "_N", true);
            var px = new Color32[W * H];
            const float k = 6f;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float hl = hgt[y * W + (x + W - 1) % W], hr = hgt[y * W + (x + 1) % W];
                    float hd = hgt[((y + H - 1) % H) * W + x], hu = hgt[((y + 1) % H) * W + x];
                    Vector2 q = pn[y * W + x];
                    Vector3 n = new Vector3((hl - hr) * k + q.x, (hd - hu) * k + q.y, 1f).normalized;
                    px[y * W + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }
            t.SetPixels32(px); t.Apply(true);
            return t;
        }

        public Texture2D GlossTex(string key)
        {
            var t = NewTex(key + "_G", true);
            var px = new Color32[W * H];
            for (int i = 0; i < px.Length; i++) px[i] = new Color(0f, 0f, 0f, gloss[i]);
            t.SetPixels32(px); t.Apply(true);
            return t;
        }

        public Texture2D GlowTex(string key)
        {
            var t = NewTex(key + "_E", false);
            var px = new Color32[W * H];
            for (int i = 0; i < px.Length; i++) { float g = glow[i]; px[i] = new Color(g, g, g, 1f); }
            t.SetPixels32(px); t.Apply(true);
            return t;
        }
    }

    // ================= 외벽 =================

    static Canvas DrawWall(Style s, int v)
    {
        var cv = new Canvas(v * 131 + (int)s * 17 + 5);
        Color wall = WallColor(s, v);
        Color frame = Kit.Hex("D5DADF"), darkFrame = Kit.Hex("3E4349");
        float broken = 0.12f, glowCh = 0.12f;

        switch (s)
        {
            case Style.Apt:
                cv.Wall(wall, 0.05f, CC0Lib.Get("painted_plaster_wall_256"));
                cv.Rect(0, 0, W, 12, wall * 0.86f, 0.62f, 0.1f);              // 층 슬래브
                for (int b = 0; b < 2; b++)
                {
                    int x0 = b * 256;
                    cv.Window(x0 + 16, 70, x0 + 240, 226, frame, cv.RandomKind(broken * 0.6f), true, glowCh);
                    cv.Rect(x0 + 12, 40, x0 + 244, 80, wall * 0.95f, 0.75f, 0.12f);   // 발코니 난간 벽
                    cv.Rect(x0 + 12, 80, x0 + 244, 86, frame * 0.8f, 0.85f, 0.4f);
                }
                cv.Rect(252, 12, 260, H, wall * 0.88f, 0.4f, 0.1f);           // 세대 경계 줄눈
                if (v % 2 == 0) cv.AirCon(150, 92);
                break;

            case Style.Shop:
            case Style.Office:
                cv.Wall(wall, 0.04f, CC0Lib.Get(v % 2 == 0 ? "rectangular_facade_tiles_256" : "concrete_wall_004_256"));
                cv.Rect(0, 0, W, 10, wall * 0.82f, 0.62f, 0.12f);
                for (int b = 0; b < 2; b++)
                {
                    int x0 = b * 256;
                    cv.Window(x0 + 20, 52, x0 + 236, 216, darkFrame, cv.RandomKind(broken), true, glowCh * 0.6f);
                }
                if (s == Style.Office) cv.Rect(0, 222, W, 232, wall * 0.75f, 0.7f, 0.2f);
                break;

            case Style.House:
                cv.Wall(wall, 0.07f, CC0Lib.Get("white_plaster_02_256"));
                cv.Rect(0, 0, W, 8, wall * 0.88f, 0.6f, 0.1f);
                cv.Window(64, 80, 192, 192, frame * 0.95f, cv.RandomKind(broken), true, glowCh);
                cv.Window(352, 148, 416, 196, frame * 0.9f, 0, false, 0f);   // 작은 환기창
                break;

            case Style.School:
                cv.Wall(wall, 0.05f, CC0Lib.Get("painted_plaster_wall_256"));
                cv.Rect(0, 0, W, 12, wall * 0.86f, 0.62f, 0.1f);
                for (int b = 0; b < 4; b++)
                {
                    int x0 = b * 128;
                    cv.Window(x0 + 12, 60, x0 + 116, 208, Kit.Hex("E9ECEF"), cv.RandomKind(broken), true, 0.03f);
                }
                break;

            case Style.Public:
                cv.Wall(wall, 0.04f, CC0Lib.Get("concrete_wall_004_256"));
                cv.Rect(0, 0, W, 12, wall * 0.85f, 0.62f, 0.1f);
                for (int b = 0; b < 2; b++)
                {
                    int x0 = b * 256;
                    cv.Window(x0 + 28, 56, x0 + 228, 208, Kit.Hex("8A8F96"), cv.RandomKind(broken * 0.5f), true, 0.05f);
                }
                break;

            default: // 빌라 / 원룸: 벽돌·타일·도장 세 가지 외장
                Color c = wall;
                bool brick = c.r > c.g * 1.25f;
                bool tile = !brick && c.r > 0.9f && c.g > 0.9f;
                if (brick) cv.Bricks(c); else if (tile) cv.Tiles(c); else cv.Wall(c, 0.06f, CC0Lib.Get(v % 2 == 0 ? "white_plaster_02_256" : "painted_plaster_wall_256"));
                cv.Rect(0, 0, W, 10, (brick ? Kit.Hex("B9B2A6") : c) * 0.88f, 0.62f, 0.1f);
                cv.Window(40, 68, 216, 200, frame, cv.RandomKind(broken), true, glowCh);
                cv.Window(324, 124, 444, 200, frame, cv.RandomKind(broken * 0.5f), false, glowCh * 0.5f);   // 욕실 작은 창
                cv.AirCon(330, 40);
                if (v % 3 == 0) cv.Pipe(500, Kit.Hex("E3B53A"));    // 노란 가스관
                break;
        }
        return cv;
    }

    /// <summary>1층 상가: 쇼윈도(안쪽 진열장 그림자) + 출입문 + 셔터 박스 + 간판 띠</summary>
    static Canvas DrawStore(int v)
    {
        var cv = new Canvas(900 + v);
        Color wall = Kit.Hex("8E8A84");
        cv.Wall(wall, 0.04f, CC0Lib.Get("rectangular_facade_tiles_256"));
        Color band = SignBands[v % SignBands.Length];
        Color frame = Kit.Hex("3A3F45");
        // 쇼윈도 (아래 0.2 ~ 2.6m)
        int gy0 = 14, gy1 = 168;
        cv.Rect(6, gy0, 506, gy1, frame, 0.8f, 0.4f);
        Color glass = Kit.Hex("34495A");
        for (int y = gy0 + 8; y < gy1 - 8; y++)
            for (int x = 14; x < 498; x++)
            {
                float t = (y - gy0) / (float)(gy1 - gy0);
                int i = y * W + x;
                Color g = Color.Lerp(glass * 0.6f, glass * 1.3f, t);
                // 안쪽 진열대 실루엣 (어둑한 선반)
                if ((y - gy0) % 40 < 4 && y < gy1 - 30) g *= 0.6f;
                if (((x + y * 0.8f) % 140f) < 12f) g = Color.Lerp(g, Color.white, 0.18f);
                cv.col[i] = g; cv.hgt[i] = 0.25f; cv.gloss[i] = 0.9f;
            }
        for (int x = 128; x < W; x += 128) cv.Rect(x - 4, gy0, x + 4, gy1, frame, 0.8f, 0.4f);  // 창틀
        cv.Rect(300, gy0, 392, gy1, frame, 0.8f, 0.4f);                                      // 출입문
        if (cv.R() < 0.5f) cv.Rect(304, gy0 + 6, 388, gy1 - 6, Kit.Hex("0E1114"), 0.15f, 0f); // 열린 문 (어둠)
        // 셔터 박스 + 반쯤 내려온 셔터
        cv.Rect(0, 168, W, 182, Kit.Hex("6A6E74"), 0.85f, 0.3f);
        if (v % 3 == 1)
            for (int y = 120; y < 168; y++)
                for (int x = 6; x < 296; x++)
                {
                    int i = y * W + x;
                    cv.col[i] = Kit.Hex("8C9096") * ((y % 8) < 2 ? 0.75f : 1f); cv.hgt[i] = (y % 8) < 2 ? 0.6f : 0.7f; cv.gloss[i] = 0.35f;
                }
        // 간판 띠
        cv.Rect(0, 186, W, 244, band, 0.75f, 0.35f);
        cv.Rect(0, 182, W, 188, band * 0.6f, 0.7f, 0.3f);
        cv.Rect(0, 244, W, H, wall * 0.9f, 0.6f, 0.1f);
        return cv;
    }
}
