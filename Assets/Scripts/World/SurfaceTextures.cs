using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 바닥 질감을 코드로 그린다: 아스팔트(얼룩·균열), 보도블록, 잔디, 풀 포기(알파).
/// 단색 바닥보다 훨씬 덜 밋밋하다. 머티리얼은 공유한다.
/// </summary>
public static class SurfaceTextures
{
    static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
    static Shader lit;

    static Material Make(string key, Texture2D tex, float smooth, bool cutout = false)
    {
        if (cache.TryGetValue(key, out var m) && m != null) return m;
        if (lit == null) lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        m = new Material(lit) { name = key };
        m.SetTexture("_BaseMap", tex);
        m.mainTexture = tex;
        m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_Smoothness", smooth);
        if (cutout)
        {
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cull", 0f);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        }
        cache[key] = m;
        return m;
    }

    static Texture2D New(string name, int size, bool alpha = false)
    {
        return new Texture2D(size, size, alpha ? TextureFormat.RGBA32 : TextureFormat.RGB24, true)
        { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
    }

    static float Fbm(float x, float y, int oct)
    {
        float v = 0, a = 0.5f, f = 1f;
        for (int i = 0; i < oct; i++) { v += Mathf.PerlinNoise(x * f + i * 17.3f, y * f + i * 31.7f) * a; a *= 0.5f; f *= 2f; }
        return v;
    }

    /// <summary>아스팔트: 큰 얼룩 + 자잘한 알갱이 + 가는 균열 (가로가 도로 폭 방향)</summary>
    public static Material Asphalt(Color baseC, string key)
    {
        if (cache.TryGetValue(key, out var m) && m != null) return m;
        const int N = 256;
        var tex = New(key, N);
        var px = new Color[N * N];
        var rng = new System.Random(key.GetHashCode());
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = x / (float)N, v = y / (float)N;
                // 바퀴 자국: 도로 양쪽 1/4 지점이 살짝 밝고 매끈
                float track = Mathf.Exp(-Mathf.Pow((Mathf.Abs(u - 0.5f) - 0.22f) / 0.07f, 2)) * 0.06f;
                float blot = (Fbm(u * 4, v * 4, 3) - 0.5f) * 0.22f;
                float grain = ((float)rng.NextDouble() - 0.5f) * 0.12f;
                px[y * N + x] = baseC * (1f + blot + grain + track);
            }
        // 균열
        for (int c = 0; c < 6; c++)
        {
            float x = (float)rng.NextDouble() * N, y = (float)rng.NextDouble() * N, ang = (float)rng.NextDouble() * 6.28f;
            int len = 30 + rng.Next(60);
            for (int i = 0; i < len; i++)
            {
                ang += ((float)rng.NextDouble() - 0.5f) * 0.6f;
                x += Mathf.Cos(ang); y += Mathf.Sin(ang);
                int ix = ((int)x % N + N) % N, iy = ((int)y % N + N) % N;
                px[iy * N + ix] = baseC * 0.55f;
            }
        }
        // 기름 얼룩 몇 개
        for (int c = 0; c < 4; c++)
        {
            int cx = rng.Next(N), cy = rng.Next(N), r = 6 + rng.Next(12);
            for (int y = -r; y <= r; y++)
                for (int x = -r; x <= r; x++)
                {
                    float d = Mathf.Sqrt(x * x + y * y) / r;
                    if (d > 1) continue;
                    int ix = ((cx + x) % N + N) % N, iy = ((cy + y) % N + N) % N;
                    px[iy * N + ix] *= Mathf.Lerp(0.82f, 1f, d);
                }
        }
        for (int i = 0; i < px.Length; i++) px[i].a = 1;
        tex.SetPixels(px); tex.Apply(true);
        return Make(key, tex, 0.12f);
    }

    /// <summary>보도블록: 4×4 장, 줄눈 + 장마다 다른 밝기 + 이끼 낀 줄눈</summary>
    public static Material Pavers(Color baseC, string key)
    {
        if (cache.TryGetValue(key, out var m) && m != null) return m;
        const int N = 256, T = 64;
        var tex = New(key, N);
        var px = new Color[N * N];
        var rng = new System.Random(key.GetHashCode());
        var tile = new float[16];
        for (int i = 0; i < 16; i++) tile[i] = 0.9f + (float)rng.NextDouble() * 0.18f;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int tx = x / T, ty = y / T, lx = x % T, ly = y % T;
                bool joint = lx < 3 || ly < 3;
                float n = ((float)rng.NextDouble() - 0.5f) * 0.08f + (Fbm(x / 40f, y / 40f, 2) - 0.5f) * 0.12f;
                Color c = baseC * (tile[(ty * 4 + tx) % 16] + n);
                if (joint) c = Color.Lerp(baseC * 0.6f, Kit.Hex("4E5A3A"), Fbm(x / 23f, y / 23f, 2) > 0.55f ? 0.6f : 0.1f);
                c.a = 1; px[y * N + x] = c;
            }
        tex.SetPixels(px); tex.Apply(true);
        return Make(key, tex, 0.1f);
    }

    /// <summary>잔디·흙 얼룩 (면 단위로 넓게 깔리는 곳)</summary>
    public static Material Grass(Color a, Color b, string key)
    {
        if (cache.TryGetValue(key, out var m) && m != null) return m;
        const int N = 256;
        var tex = New(key, N);
        var px = new Color[N * N];
        var rng = new System.Random(key.GetHashCode());
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float t = Fbm(x / 60f, y / 60f, 4);
                Color c = Color.Lerp(a, b, Mathf.SmoothStep(0.35f, 0.65f, t));
                c *= 1f + ((float)rng.NextDouble() - 0.5f) * 0.18f;
                c.a = 1; px[y * N + x] = c;
            }
        tex.SetPixels(px); tex.Apply(true);
        return Make(key, tex, 0.05f);
    }

    /// <summary>철망 (알파 잘라내기, 양면)</summary>
    public static Material Fence()
    {
        const string key = "Fence";
        if (cache.TryGetValue(key, out var m) && m != null) return m;
        const int N = 64;
        var tex = New(key, N, true);
        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                bool wire = ((x + y) % 16) < 2 || ((x - y + 64) % 16) < 2;
                px[y * N + x] = wire ? new Color(0.62f, 0.64f, 0.66f, 1f) : new Color(0, 0, 0, 0);
            }
        tex.SetPixels(px); tex.Apply(true);
        return Make(key, tex, 0.5f, true);
    }

    /// <summary>옥상 방수 페인트: 벗겨진 자국·물 고였던 얼룩 (10m에 한 장)</summary>
    public static Texture2D RoofTex(Color baseC, string key)
    {
        const int N = 256;
        var tex = New(key + "_tex", N);
        var px = new Color[N * N];
        var rng = new System.Random(key.GetHashCode());
        Color bare = Kit.Hex("8E8C86");
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float peel = Fbm(x / 50f, y / 50f, 4);
                Color c = peel > 0.62f ? bare : baseC;
                float puddle = Fbm(x / 90f + 5, y / 90f + 9, 3);
                if (puddle > 0.6f) c *= 0.85f;
                c *= 1f + ((float)rng.NextDouble() - 0.5f) * 0.08f;
                c.a = 1; px[y * N + x] = c;
            }
        tex.SetPixels(px); tex.Apply(true);
        return tex;
    }

    /// <summary>풀 포기: 가는 잎 여러 개 (알파 잘라내기, 양면)</summary>
    public static Material Tuft()
    {
        const string key = "Tuft";
        if (cache.TryGetValue(key, out var m) && m != null) return m;
        const int N = 128;
        var tex = New(key, N, true);
        var px = new Color[N * N];
        for (int i = 0; i < px.Length; i++) px[i] = new Color(0, 0, 0, 0);
        var rng = new System.Random(7);
        for (int b = 0; b < 26; b++)
        {
            float x0 = 10 + (float)rng.NextDouble() * (N - 20);
            float lean = ((float)rng.NextDouble() - 0.5f) * 1.2f;
            float hgt = N * (0.45f + (float)rng.NextDouble() * 0.5f);
            Color c = Color.Lerp(Kit.Hex("4F6B35"), Kit.Hex("8A9450"), (float)rng.NextDouble());
            if (rng.NextDouble() < 0.2) c = Kit.Hex("9A8A5A");   // 마른 잎
            for (int y = 0; y < hgt; y++)
            {
                float t = y / hgt;
                float w = Mathf.Lerp(2.6f, 0.4f, t);
                float x = x0 + lean * y * 0.35f * t;
                for (int dx = Mathf.FloorToInt(-w); dx <= Mathf.CeilToInt(w); dx++)
                {
                    int ix = Mathf.RoundToInt(x) + dx;
                    if (ix < 0 || ix >= N) continue;
                    Color cc = c * Mathf.Lerp(0.6f, 1.1f, t);
                    cc.a = 1;
                    px[y * N + ix] = cc;
                }
            }
        }
        tex.SetPixels(px); tex.Apply(true);
        return Make(key, tex, 0.1f, true);
    }
}
