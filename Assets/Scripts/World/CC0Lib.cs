using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 실사 질감 (Poly Haven, CC0 퍼블릭 도메인). Resources/CC0/*.bytes 에 PNG로 들어 있다.
/// 파일이 없으면 null을 돌려주고, 부르는 쪽은 예전 코드 그리기로 돌아간다.
/// </summary>
public static class CC0Lib
{
    /// <summary>화판(Facade)에 섞어 쓰는 작은 사진: 픽셀 배열 + 노멀 + 평균색</summary>
    public class Photo
    {
        public int N;
        public Color[] a;
        public Vector2[] n;
        public Color mean;
        public float meanLum;
        public Color A(int x, int y) => a[(((y % N) + N) % N) * N + (((x % N) + N) % N)];
        public Vector2 Nrm(int x, int y) => n[(((y % N) + N) % N) * N + (((x % N) + N) % N)];
        public float Lum(int x, int y) { var c = A(x, y); return c.r * 0.3f + c.g * 0.59f + c.b * 0.11f; }
    }

    static readonly Dictionary<string, Photo> photos = new Dictionary<string, Photo>();
    static readonly Dictionary<string, Texture2D> texs = new Dictionary<string, Texture2D>();
    static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
    static Shader lit;

    static Texture2D LoadTex(string path, bool linear)
    {
        if (texs.TryGetValue(path, out var t)) return t;
        var ta = Resources.Load<TextAsset>("CC0/" + path);
        if (ta == null) { texs[path] = null; return null; }
        t = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear)
        { name = path, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
        if (!t.LoadImage(ta.bytes, false)) { texs[path] = null; return null; }
        texs[path] = t;
        return t;
    }

    public static Photo Get(string name)
    {
        if (photos.TryGetValue(name, out var p)) return p;
        var ta = LoadTex(name + "_albedo", false);
        var tn = LoadTex(name + "_normal", true);
        if (ta == null) { photos[name] = null; return null; }
        p = new Photo { N = ta.width, a = ta.GetPixels() };
        p.n = new Vector2[p.a.Length];
        if (tn != null && tn.width == ta.width)
        {
            var np = tn.GetPixels();
            for (int i = 0; i < np.Length; i++) p.n[i] = new Vector2(np[i].r * 2f - 1f, np[i].g * 2f - 1f);
        }
        Color sum = Color.black;
        for (int i = 0; i < p.a.Length; i++) sum += p.a[i];
        p.mean = sum / p.a.Length;
        p.meanLum = Mathf.Max(0.02f, p.mean.r * 0.3f + p.mean.g * 0.59f + p.mean.b * 0.11f);
        photos[name] = p;
        return p;
    }

    /// <summary>
    /// 바닥 재질: 사진 질감 + 노멀맵. tint는 원하는 평균색 (사진 평균색을 이 색으로 맞춘다).
    /// tiling은 UV 1칸에 사진을 몇 번 반복할지.
    /// </summary>
    public static Material Mat(string name, string key, Color tint, float tiling, float smooth, float bump = 1f)
    {
        if (mats.TryGetValue(key, out var m) && m != null) return m;
        var ta = LoadTex(name + "_albedo", false);
        if (ta == null) return null;
        var tn = LoadTex(name + "_normal", true);
        var p = Get(name);
        if (lit == null) lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        m = new Material(lit) { name = key };
        m.SetTexture("_BaseMap", ta);
        m.mainTexture = ta;
        m.SetTextureScale("_BaseMap", Vector2.one * tiling);
        Color k = Color.white;
        if (p != null)
            k = new Color(tint.r / Mathf.Max(0.02f, p.mean.r), tint.g / Mathf.Max(0.02f, p.mean.g), tint.b / Mathf.Max(0.02f, p.mean.b), 1f);
        m.SetColor("_BaseColor", k);
        m.color = k;
        m.SetFloat("_Smoothness", smooth);
        if (tn != null)
        {
            m.SetTexture("_BumpMap", tn);
            m.SetTextureScale("_BumpMap", Vector2.one * tiling);
            m.SetFloat("_BumpScale", bump);
            m.EnableKeyword("_NORMALMAP");
        }
        mats[key] = m;
        return m;
    }
}
