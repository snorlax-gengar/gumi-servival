using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 코드로 로우폴리 모델을 조립하는 도구 모음.
/// - 색마다 머티리얼을 한 번만 만들어 재사용 (SRP Batcher가 묶어서 그린다)
/// - 상자/원기둥 부품을 부모 기준 위치·크기로 붙인다 (장식은 콜라이더 없음)
/// - 한글 간판 글씨 (윈도우 '맑은 고딕'을 쓰는 TextMesh)
/// 색은 컨셉 아트의 팔레트를 그대로 쓴다.
/// </summary>
public static class Kit
{
    // ---------------- 팔레트 (컨셉 아트 기준) ----------------
    public static readonly Color Asphalt = Hex("2E3135");
    public static readonly Color Concrete = Hex("C9C2B6");
    public static readonly Color Tile = Hex("E3D8C4");
    public static readonly Color TileShade = Hex("D5C9B3");
    public static readonly Color Brick = Hex("9C4F36");
    public static readonly Color Steel = Hex("5E6E80");
    public static readonly Color SteelDark = Hex("4E5C6C");
    public static readonly Color Frame = Hex("CFD5DB");
    public static readonly Color Glass = Hex("34495A");
    public static readonly Color GlassLight = Hex("9FC3C9");
    public static readonly Color WarmLight = Hex("F2B544");
    public static readonly Color Tech = Hex("6CB4E4");      // 2050 기술 (하늘색)
    public static readonly Color Visor = Hex("E0362A");     // 안드로이드 눈 (빨강)
    public static readonly Color Loot = Hex("E8892B");      // 뒤질 수 있는 것 (주황)
    public static readonly Color Hazard = Hex("F2C230");    // 산업 경고 (노랑)
    public static readonly Color Dark = Hex("1F2124");
    public static readonly Color Wire = Hex("14171A");
    public static readonly Color Pole = Hex("7A7266");
    public static readonly Color Navy = Hex("2F3E5C");

    public static Color Hex(string h)
    {
        ColorUtility.TryParseHtmlString("#" + h, out Color c);
        return c;
    }

    // ---------------- 머티리얼 ----------------
    static readonly Dictionary<(Color, float), Material> cache = new Dictionary<(Color, float), Material>();
    static Shader lit;

    public static Material Mat(Color c, float emission = 0f)
    {
        var key = (c, emission);
        if (cache.TryGetValue(key, out var m) && m != null) return m;

        if (lit == null) lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        m = new Material(lit) { name = $"Kit_{ColorUtility.ToHtmlStringRGB(c)}_{emission}" };
        m.SetColor("_BaseColor", c);
        m.color = c;
        m.SetFloat("_Smoothness", 0.15f);
        if (emission > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        cache[key] = m;
        return m;
    }

    // ---------------- 부품 ----------------

    public static GameObject Box(Transform parent, Vector3 localPos, Vector3 size, Color c,
        bool collider = false, Quaternion? rot = null, float emission = 0f, string name = "part")
        => Prim(PrimitiveType.Cube, parent, localPos, size, c, collider, rot, emission, name);

    /// <summary>원기둥. size = (지름 x, 높이, 지름 z)</summary>
    public static GameObject Cyl(Transform parent, Vector3 localPos, Vector3 size, Color c,
        bool collider = false, Quaternion? rot = null, float emission = 0f, string name = "cyl")
        => Prim(PrimitiveType.Cylinder, parent, localPos, new Vector3(size.x, size.y * 0.5f, size.z), c, collider, rot, emission, name);

    static GameObject Prim(PrimitiveType t, Transform parent, Vector3 localPos, Vector3 scale, Color c,
        bool collider, Quaternion? rot, float emission, string name)
    {
        var g = GameObject.CreatePrimitive(t);
        g.name = name;
        if (!collider) Object.DestroyImmediate(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        g.transform.localRotation = rot ?? Quaternion.identity;
        g.transform.localScale = scale;
        var r = g.GetComponent<Renderer>();
        r.sharedMaterial = Mat(c, emission);
        if (emission > 0f) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return g;
    }

    public static Transform Group(Transform parent, string name, Vector3 localPos, Quaternion? rot = null)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = localPos;
        t.localRotation = rot ?? Quaternion.identity;
        return t;
    }

    // ---------------- 한글 글씨 ----------------
    static Font krFont;
    static bool fontTried;

    public static Font KoreanFont
    {
        get
        {
            if (!fontTried)
            {
                fontTried = true;
                try { krFont = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "NanumGothic", "Apple SD Gothic Neo" }, 64); }
                catch { krFont = null; }
            }
            return krFont;
        }
    }

    /// <summary>
    /// 간판 글씨. 글씨는 rot 기준 -z 쪽에서 읽힌다 (즉 rot의 forward 반대쪽이 읽는 사람 쪽).
    /// height = 한 줄 높이(m).
    /// </summary>
    public static TextMesh Label(Transform parent, string text, Vector3 localPos, Quaternion rot, float height, Color color)
    {
        var font = KoreanFont;
        if (font == null) return null;
        var go = new GameObject("글씨");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = rot;
        var tm = go.AddComponent<TextMesh>();
        tm.font = font;
        tm.fontSize = 64;
        tm.characterSize = height / 6.4f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.lineSpacing = 0.9f;
        tm.color = color;
        tm.text = text;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = TextMaterial(font);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return tm;
    }

    static Material textMat;
    static Font textMatFont;

    /// <summary>깊이 검사를 하는 글씨 머티리얼 (벽 너머 글씨가 비치지 않게). 셰이더가 없으면 기본 글꼴 머티리얼.</summary>
    public static Material TextMaterial(Font font)
    {
        if (textMat != null && textMatFont == font) return textMat;
        var sh = Shader.Find("Gumi/TextDepth");
        if (sh == null) return font.material;
        textMat = new Material(sh) { name = "GumiText" };
        textMat.mainTexture = font.material.mainTexture;
        if (textMatFont == null) Font.textureRebuilt += OnFontRebuilt;
        textMatFont = font;
        return textMat;
    }

    static void OnFontRebuilt(Font f)
    {
        if (textMat != null && f == textMatFont) textMat.mainTexture = f.material.mainTexture;
    }

    /// <summary>바깥쪽 법선 n을 향한 면에 글씨를 붙일 때의 회전</summary>
    public static Quaternion Facing(Vector3 outwardNormal) => Quaternion.LookRotation(-outwardNormal, Vector3.up);
}
