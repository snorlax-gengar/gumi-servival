using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 건물 외벽 텍스처를 코드로 그린다 (이미지 파일 없음).
/// 텍스처 한 장 = 가로 2칸(6.4m) × 한 층(3m). 1층 상가는 3.5m 높이 한 장.
/// 같은 스타일·색이면 머티리얼을 공유해서 배치가 가볍다.
/// </summary>
public static class Facade
{
    public enum Style { Villa, Apt, Shop, House, School, Public, Office }
    public const float BayWidth = 6.4f;   // 텍스처 한 장 가로 (m)
    public const float Floor = 3f;
    public const float ShopFloor = 3.5f;

    static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
    static Shader lit;

    static readonly Color[] VillaWalls = { Kit.Hex("E3D8C4"), Kit.Hex("D9CFC0"), Kit.Hex("CFC6B6"), Kit.Hex("C9B9A0"), Kit.Hex("B8A48C"), Kit.Hex("D8D4CC"), Kit.Hex("A9715A") };
    static readonly Color[] AptWalls = { Kit.Hex("ECE9E2"), Kit.Hex("E2E0DA"), Kit.Hex("DCD7CC"), Kit.Hex("E8E1D2") };
    static readonly Color[] ShopWalls = { Kit.Hex("B9B6AE"), Kit.Hex("9E9A92"), Kit.Hex("C8C1B3"), Kit.Hex("8E9399"), Kit.Hex("D0CBC0") };
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

    public static Material Textured(string key, Texture2D tex, Color tint, float smooth)
    {
        if (cache.TryGetValue(key, out var m) && m != null) return m;
        return Make(key, tex, tint, smooth);
    }

    public static Material Wall(Style s, int variant)
    {
        string key = $"Wall_{s}_{variant}";
        if (cache.TryGetValue(key, out var m) && m != null) return m;
        return Make(key, DrawWall(s, variant), Color.white, 0.12f);
    }

    public static Material Storefront(int variant)
    {
        string key = $"Store_{variant}";
        if (cache.TryGetValue(key, out var m) && m != null) return m;
        return Make(key, DrawStore(variant), Color.white, 0.3f);
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
        return Make(key, null, c, 0.05f);
    }

    // ================= 그리기 =================

    const int W = 256, H = 128;   // 2칸 × 1층

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

    static Texture2D NewTex(string name)
    {
        var t = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
        return t;
    }

    static void Rect(Color[] px, int x0, int y0, int x1, int y1, Color c)
    {
        for (int y = Mathf.Max(0, y0); y < Mathf.Min(H, y1); y++)
            for (int x = Mathf.Max(0, x0); x < Mathf.Min(W, x1); x++)
                px[y * W + x] = c;
    }

    /// <summary>창문: 테두리 + 유리(위가 밝은 반사) + 가운데 창살</summary>
    static void Window(Color[] px, int x0, int y0, int x1, int y1, Color frame, Color glass, bool mullion = true, int border = 3)
    {
        Rect(px, x0, y0, x1, y1, frame);
        for (int y = y0 + border; y < y1 - border; y++)
        {
            float t = (y - y0) / (float)Mathf.Max(1, y1 - y0);
            Color g = Color.Lerp(glass * 0.8f, glass * 1.35f, t);
            g.a = 1;
            for (int x = x0 + border; x < x1 - border; x++)
            {
                // 비스듬한 반사광 한 줄
                float diag = ((x - x0) + (y - y0) * 0.8f) % 60f;
                px[y * W + x] = diag < 6f ? Color.Lerp(g, Color.white, 0.18f) : g;
            }
        }
        if (mullion) Rect(px, (x0 + x1) / 2 - 1, y0, (x0 + x1) / 2 + 2, y1, frame);
        // 창턱 그림자
        Rect(px, x0 - 2, y0 - 3, x1 + 2, y0, frame * 0.7f);
    }

    static void Noise(Color[] px, Color baseC, float amt, int seed)
    {
        var r = new System.Random(seed);
        for (int i = 0; i < px.Length; i++)
        {
            float n = ((float)r.NextDouble() - 0.5f) * amt;
            Color c = baseC * (1f + n);
            // 아래쪽 빗물 얼룩
            int y = i / W;
            if (y < 10) c *= 0.93f + y * 0.007f;
            c.a = 1;
            px[i] = c;
        }
    }

    static Texture2D DrawWall(Style s, int v)
    {
        var tex = NewTex($"Wall_{s}_{v}");
        var px = new Color[W * H];
        Color wall = WallColor(s, v);
        Noise(px, wall, 0.06f, v * 31 + (int)s);
        Color glass = Kit.Hex("34495A"), frame = Kit.Hex("CFD5DB"), dark = Kit.Hex("4A4F55");

        switch (s)
        {
            case Style.Apt:
                // 층 슬래브 띠 + 넓은 발코니 창 + 난간
                Rect(px, 0, 0, W, 6, wall * 0.88f);
                for (int b = 0; b < 2; b++)
                {
                    int x0 = b * 128;
                    Window(px, x0 + 8, 34, x0 + 120, 112, frame, glass);
                    Rect(px, x0 + 6, 20, x0 + 122, 40, wall * 0.94f);   // 난간 벽
                    Rect(px, x0 + 6, 40, x0 + 122, 43, frame * 0.8f);
                }
                Rect(px, 126, 6, 130, H, wall * 0.9f); // 세대 경계
                break;
            case Style.Shop:
            case Style.Office:
                Rect(px, 0, 0, W, 5, wall * 0.85f);
                for (int b = 0; b < 2; b++)
                {
                    int x0 = b * 128;
                    Window(px, x0 + 10, 26, x0 + 118, 108, dark, glass * 1.1f, true, 4);
                }
                break;
            case Style.House:
                Window(px, 32, 40, 96, 96, frame * 0.95f, glass);
                Rect(px, 0, 0, W, 4, wall * 0.9f);
                // 두 번째 칸은 벽 + 작은 환기창
                Window(px, 176, 74, 208, 98, frame * 0.9f, glass, false, 2);
                break;
            case Style.School:
                Rect(px, 0, 0, W, 6, wall * 0.86f);
                for (int b = 0; b < 4; b++)
                {
                    int x0 = b * 64;
                    Window(px, x0 + 6, 30, x0 + 58, 104, Kit.Hex("E9ECEF"), glass, true, 3);
                }
                break;
            case Style.Public:
                Rect(px, 0, 0, W, 6, wall * 0.85f);
                for (int b = 0; b < 2; b++)
                {
                    int x0 = b * 128;
                    Window(px, x0 + 14, 28, x0 + 114, 104, Kit.Hex("8A8F96"), glass * 1.15f, true, 4);
                }
                break;
            default: // 빌라 / 원룸
                Rect(px, 0, 0, W, 5, wall * 0.9f);
                Window(px, 20, 34, 108, 100, frame, glass);
                Window(px, 162, 62, 222, 100, frame, glass, false, 3);  // 욕실 작은 창
                // 실외기 받침
                Rect(px, 150, 22, 230, 30, Kit.Hex("8A8F96"));
                break;
        }
        tex.SetPixels(px);
        tex.Apply(true);
        return tex;
    }

    /// <summary>1층 상가: 쇼윈도 + 출입문 + 간판 띠</summary>
    static Texture2D DrawStore(int v)
    {
        var tex = NewTex($"Store_{v}");
        var px = new Color[W * H];
        Color wall = Kit.Hex("8E8A84");
        Noise(px, wall, 0.05f, 900 + v);
        Color band = SignBands[v % SignBands.Length];
        Color glass = Kit.Hex("3E5566"), frame = Kit.Hex("3A3F45");
        // 유리 (3.5m 중 아래 0.2 ~ 2.6m 정도)
        Window(px, 4, 8, 252, 82, frame, glass * 1.15f, false, 4);
        for (int x = 64; x < W; x += 64) Rect(px, x - 2, 8, x + 2, 82, frame);   // 창틀
        Rect(px, 150, 8, 196, 82, frame);                                            // 출입문
        Window(px, 154, 10, 192, 78, frame, glass * 0.9f, true, 2);
        // 간판 띠 (위쪽)
        Rect(px, 0, 90, W, 122, band);
        Rect(px, 0, 88, W, 91, band * 0.6f);
        Rect(px, 0, 122, W, 128, wall * 0.9f);
        // 셔터 박스
        Rect(px, 0, 82, W, 88, Kit.Hex("6A6E74"));
        tex.SetPixels(px);
        tex.Apply(true);
        return tex;
    }
}
