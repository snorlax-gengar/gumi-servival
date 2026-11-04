using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Resources 폴더의 무료 에셋(Kenney, Quaternius)을 불러와 배치한다.
/// - 원하는 크기(바닥 면적 또는 키)에 맞춰 크기를 자동으로 맞춘다
/// - 바닥이 지면에 닿게, 중심이 지정 위치에 오게 한다
/// - 건물에는 박스 콜라이더와 가림 처리 그룹을 붙인다
/// 에셋이 없으면 null을 돌려주므로, 호출하는 쪽은 코드 모델로 대신한다.
/// </summary>
public static class AssetLib
{
    static readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
    static readonly Dictionary<string, AnimationClip[]> clipCache = new Dictionary<string, AnimationClip[]>();

    /// <summary>Kenney 모델의 정면 보정. 모델 정면이 로컬 -z(도로 쪽)를 보도록 돌리는 각도.</summary>
    public static float KenneyFrontYaw = 180f;

    public static GameObject Prefab(string path)
    {
        if (prefabs.TryGetValue(path, out var p)) return p;
        p = Resources.Load<GameObject>(path);
        prefabs[path] = p;
        if (p == null) Debug.LogWarning($"[Gumi] 에셋 없음: Resources/{path}");
        return p;
    }

    public static bool Has(string path) => Prefab(path) != null;

    public static AnimationClip[] Clips(string path)
    {
        if (clipCache.TryGetValue(path, out var c)) return c;
        c = Resources.LoadAll<AnimationClip>(path);
        clipCache[path] = c;
        return c;
    }

    public enum Fit { None, Footprint, Height, FootprintStretch }

    /// <summary>
    /// 에셋을 만들어 배치한다.
    /// fit = Footprint: size.x × size.z 안에 들어가도록 균일 축소/확대
    /// fit = Height: 높이가 size.y가 되도록 균일 축소/확대
    /// fit = FootprintStretch: 바닥은 size.x × size.z에 꽉 맞추고 높이는 비율 유지(평균 배율)
    /// </summary>
    public static Transform Spawn(string path, Transform parent, Vector3 pos, float yaw, Fit fit, Vector3 size,
        bool collider, bool occluder, string name = null, float extraYaw = 0f)
    {
        var prefab = Prefab(path);
        if (prefab == null) return null;

        // 회전 없이 먼저 만들어 원래 크기를 잰다
        var holder = new GameObject(name ?? prefab.name).transform;
        holder.SetParent(parent, false);
        var inst = Object.Instantiate(prefab, holder, false);
        inst.transform.localPosition = Vector3.zero;
        inst.transform.localRotation = Quaternion.Euler(0, extraYaw, 0);
        inst.transform.localScale = Vector3.one;

        Bounds b = LocalBounds(holder);
        Vector3 s = Vector3.one;
        if (b.size.sqrMagnitude > 1e-6f)
        {
            switch (fit)
            {
                case Fit.Footprint:
                    s = Vector3.one * Mathf.Min(size.x / Mathf.Max(0.01f, b.size.x), size.z / Mathf.Max(0.01f, b.size.z));
                    break;
                case Fit.Height:
                    s = Vector3.one * (size.y / Mathf.Max(0.01f, b.size.y));
                    break;
                case Fit.FootprintStretch:
                    float sx = size.x / Mathf.Max(0.01f, b.size.x), sz = size.z / Mathf.Max(0.01f, b.size.z);
                    s = new Vector3(sx, (sx + sz) * 0.5f, sz);
                    break;
            }
        }
        inst.transform.localScale = s;
        // 바닥 중심을 원점으로
        Vector3 c = Vector3.Scale(b.center, s);
        float bottom = (b.center.y - b.extents.y) * s.y;
        inst.transform.localPosition = new Vector3(-c.x, -bottom, -c.z);

        holder.localPosition = pos;
        holder.localRotation = Quaternion.Euler(0, yaw, 0);

        if (collider)
        {
            Bounds nb = LocalBounds(holder);
            var bc = holder.gameObject.AddComponent<BoxCollider>();
            bc.center = nb.center;
            bc.size = nb.size;
        }
        if (occluder) holder.gameObject.AddComponent<OccluderGroup>();
        return holder;
    }

    /// <summary>root 기준 로컬 좌표로 모든 렌더러를 감싸는 범위</summary>
    public static Bounds LocalBounds(Transform root)
    {
        var rs = root.GetComponentsInChildren<Renderer>();
        bool any = false;
        Bounds result = new Bounds();
        Matrix4x4 toLocal = root.worldToLocalMatrix;
        foreach (var r in rs)
        {
            Bounds wb = r.bounds;
            Vector3 mn = wb.min, mx = wb.max;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3((i & 1) == 0 ? mn.x : mx.x, (i & 2) == 0 ? mn.y : mx.y, (i & 4) == 0 ? mn.z : mx.z);
                Vector3 lp = toLocal.MultiplyPoint3x4(corner);
                if (!any) { result = new Bounds(lp, Vector3.zero); any = true; }
                else result.Encapsulate(lp);
            }
        }
        return result;
    }

    // ---------------- 색 바꾸기 ----------------
    static readonly Dictionary<(Material, Color, float), Material> recolored = new Dictionary<(Material, Color, float), Material>();

    /// <summary>원본 재질을 복사해 색(과 발광)을 바꾼다. 같은 조합은 재사용.</summary>
    public static Material Recolor(Material src, Color c, float emission = 0f)
    {
        var key = (src, c, emission);
        if (recolored.TryGetValue(key, out var m) && m != null) return m;
        m = new Material(src) { name = src.name + "_gumi" };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
        if (emission > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * emission);
        }
        recolored[key] = m;
        return m;
    }

    public static Color BaseColor(Material m)
    {
        if (m == null) return Color.white;
        if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
        if (m.HasProperty("_Color")) return m.GetColor("_Color");
        return Color.white;
    }

    /// <summary>뼈 이름으로 찾기 (모든 키워드를 포함하는 것 중 가장 짧은 이름)</summary>
    public static Transform FindBone(Transform root, params string[][] candidates)
    {
        var all = root.GetComponentsInChildren<Transform>(true);
        foreach (var keys in candidates)
        {
            Transform best = null;
            foreach (var t in all)
            {
                string n = t.name.ToLowerInvariant();
                bool ok = true;
                foreach (var k in keys) if (!n.Contains(k)) { ok = false; break; }
                if (ok && (best == null || t.name.Length < best.name.Length)) best = t;
            }
            if (best != null) return best;
        }
        return null;
    }
}
