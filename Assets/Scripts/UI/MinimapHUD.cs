using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 오른쪽 위 미니맵 + 화면 위쪽 나침반 + 조작 안내.
/// - 미니맵은 카메라가 바라보는 방향이 위쪽 (이동 방향과 일치)
/// - M: 전체 지도 (북쪽이 위)
/// - 안드로이드는 평소에 보이지 않고, 경계/추적 중이거나 아주 가까이 있을 때만 점으로 표시
/// IsometricCamera가 자동으로 붙인다.
/// </summary>
[RequireComponent(typeof(IsometricCamera))]
public class MinimapHUD : MonoBehaviour
{
    public int mapSize = 220;            // 화면 픽셀
    public float miniOrthoSize = 35f;    // 미니맵에 보이는 반경(m)
    public float senseRadius = 12f;      // 이 거리 안의 안드로이드는 회색 점으로 감지
    public bool showHelp = true;

    IsometricCamera iso;
    Camera mapCam;
    RenderTexture miniRT, bigRT;
    Texture2D arrow, dot;
    bool bigMap;
    GUIStyle label, small, compassStyle;

    readonly List<AndroidAI> androids = new List<AndroidAI>();
    float refreshTimer;

    static readonly (float deg, string name)[] Dirs =
    {
        (0, "북"), (45, "북동"), (90, "동"), (135, "남동"), (180, "남"), (225, "남서"), (270, "서"), (315, "북서")
    };

    void Awake()
    {
        iso = GetComponent<IsometricCamera>();

        miniRT = new RenderTexture(256, 256, 16) { name = "MinimapRT" };
        bigRT = new RenderTexture(768, 768, 16) { name = "BigMapRT" };

        var go = new GameObject("MinimapCamera");
        mapCam = go.AddComponent<Camera>();
        mapCam.orthographic = true;
        mapCam.orthographicSize = miniOrthoSize;
        mapCam.clearFlags = CameraClearFlags.SolidColor;
        mapCam.backgroundColor = new Color(0.12f, 0.12f, 0.14f);
        mapCam.cullingMask = ~(1 << AndroidAI.AndroidLayer);
        mapCam.nearClipPlane = 1f;
        mapCam.farClipPlane = 400f;
        mapCam.targetTexture = miniRT;
        mapCam.depth = -10;

        arrow = MakeArrow(32);
        dot = MakeDot(16);
    }

    void OnDestroy()
    {
        if (miniRT) miniRT.Release();
        if (bigRT) bigRT.Release();
        if (mapCam) Destroy(mapCam.gameObject);
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.mKey.wasPressedThisFrame) bigMap = !bigMap;

        refreshTimer -= Time.deltaTime;
        if (refreshTimer <= 0)
        {
            refreshTimer = 1f;
            androids.Clear();
            androids.AddRange(FindObjectsByType<AndroidAI>(FindObjectsSortMode.None));
        }
    }

    void LateUpdate()
    {
        if (iso.target == null) return;
        Vector3 p = iso.target.position;

        if (bigMap)
        {
            Bounds b = LevelNavMesh.LevelBounds;
            if (b.size.sqrMagnitude < 1f) b = new Bounds(p, new Vector3(200, 10, 200));
            mapCam.targetTexture = bigRT;
            mapCam.orthographicSize = Mathf.Max(b.extents.x, b.extents.z) + 4f;
            mapCam.transform.SetPositionAndRotation(new Vector3(b.center.x, 150f, b.center.z), Quaternion.Euler(90f, 0f, 0f));
        }
        else
        {
            mapCam.targetTexture = miniRT;
            mapCam.orthographicSize = miniOrthoSize;
            mapCam.transform.SetPositionAndRotation(new Vector3(p.x, 150f, p.z), Quaternion.Euler(90f, iso.Yaw, 0f));
        }
    }

    // ================= 그리기 =================

    void InitStyles()
    {
        if (label != null) return;
        Font kr = null;
        try { kr = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "NanumGothic", "Apple SD Gothic Neo" }, 15); }
        catch { kr = null; }
        if (kr != null) GUI.skin.font = kr;
        label = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        label.normal.textColor = Color.white;
        small = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.LowerRight };
        small.normal.textColor = new Color(1, 1, 1, 0.75f);
        compassStyle = new GUIStyle(label) { fontSize = 14 };
    }

    void OnGUI()
    {
        if (iso.target == null) return;
        InitStyles();
        UIScale.Apply();
        DrawCompass();

        Rect r = bigMap
            ? CenterSquare(Mathf.Min(UIScale.W, UIScale.H) * 0.82f)
            : new Rect(UIScale.W - mapSize - 20, 48, mapSize, mapSize);
        DrawMap(r, bigMap ? bigRT : miniRT);

        if (showHelp)
            GUI.Label(new Rect(UIScale.W - 820, UIScale.H - 30, 800, 24),
                "WASD 이동 · Shift 달리기 · Ctrl 웅크리기 · F 뒤지기 · 좌클릭 공격(바라보는 방향) · Q/E·우클릭 드래그 카메라 · 휠 줌 · M 지도 · F9 녹화", small);
    }

    void DrawMap(Rect r, RenderTexture rt)
    {
        // 테두리
        GUI.color = new Color(0, 0, 0, 0.7f);
        GUI.DrawTexture(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.DrawTexture(r, rt, ScaleMode.StretchToFill, false);

        // 안드로이드 (경계 중이거나 가까이 있을 때만)
        Vector3 pp = iso.target.position;
        foreach (var a in androids)
        {
            if (a == null || a.CurrentState == AndroidAI.State.Disabled) continue;
            bool alert = a.CurrentState == AndroidAI.State.Chase || a.CurrentState == AndroidAI.State.Investigate;
            bool near = Vector3.Distance(a.transform.position, pp) < senseRadius;
            if (!alert && !near && !bigMap) continue;
            if (bigMap && !alert && !near) continue;

            if (!ToMap(r, a.transform.position, out Vector2 m)) continue;
            GUI.color = a.CurrentState == AndroidAI.State.Chase ? new Color(1f, 0.2f, 0.15f)
                      : alert ? new Color(1f, 0.75f, 0.2f) : new Color(0.8f, 0.8f, 0.8f, 0.8f);
            GUI.DrawTexture(new Rect(m.x - 5, m.y - 5, 10, 10), dot);
        }

        // 플레이어 화살표
        if (ToMap(r, pp, out Vector2 pm))
        {
            float mapUpYaw = bigMap ? 0f : iso.Yaw;
            float angle = iso.target.eulerAngles.y - mapUpYaw;
            Matrix4x4 saved = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, pm * UIScale.S); // 피벗은 실제 화면 좌표 기준
            GUI.color = new Color(0.35f, 0.75f, 1f);
            GUI.DrawTexture(new Rect(pm.x - 9, pm.y - 9, 18, 18), arrow);
            GUI.matrix = saved;
        }
        GUI.color = Color.white;

        if (bigMap)
            GUI.Label(new Rect(r.x, r.yMax + 4, r.width, 24), "전체 지도 (북쪽이 위) — M으로 닫기", label);
        else
        {
            // 미니맵 위쪽에 '북' 방향 표시
            float northScreenAngle = -iso.Yaw * Mathf.Deg2Rad;
            Vector2 c = r.center;
            Vector2 n = c + new Vector2(Mathf.Sin(northScreenAngle), -Mathf.Cos(northScreenAngle)) * (r.width * 0.5f - 10);
            GUI.color = new Color(1f, 0.35f, 0.3f);
            GUI.Label(new Rect(n.x - 12, n.y - 12, 24, 24), "N", label);
            GUI.color = Color.white;
        }
    }

    bool ToMap(Rect r, Vector3 world, out Vector2 m)
    {
        Vector3 v = mapCam.WorldToViewportPoint(world);
        m = new Vector2(r.x + v.x * r.width, r.y + (1f - v.y) * r.height);
        return v.x >= 0 && v.x <= 1 && v.y >= 0 && v.y <= 1;
    }

    void DrawCompass()
    {
        const float w = 420, h = 28;
        var r = new Rect((UIScale.W - w) / 2, 10, w, h);
        GUI.color = new Color(0, 0, 0, 0.5f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;

        float pxPerDeg = w / 180f;   // 화면에 ±90도 표시
        foreach (var d in Dirs)
        {
            float delta = Mathf.DeltaAngle(iso.Yaw, d.deg);
            if (Mathf.Abs(delta) > 88f) continue;
            float x = r.center.x + delta * pxPerDeg;
            GUI.color = d.deg == 0 ? new Color(1f, 0.4f, 0.35f) : d.deg % 90 == 0 ? Color.white : new Color(1, 1, 1, 0.6f);
            string text = d.deg == 90 ? "동(공단)" : d.name;
            GUI.Label(new Rect(x - 40, r.y, 80, h), text, compassStyle);
        }
        // 가운데 눈금
        GUI.color = new Color(1f, 0.85f, 0.3f);
        GUI.DrawTexture(new Rect(r.center.x - 1, r.yMax - 6, 2, 8), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    static Rect CenterSquare(float size) => new Rect((UIScale.W - size) / 2, (UIScale.H - size) / 2, size, size);

    // ================= 텍스처 =================

    static Texture2D MakeArrow(int s)
    {
        var t = new Texture2D(s, s, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                // 위쪽을 가리키는 삼각형 (텍스처 좌표는 아래가 0)
                float fy = 1f - y / (float)(s - 1);         // 0 = 위
                float half = fy * 0.45f;                     // 위에서 아래로 갈수록 넓어짐
                float fx = Mathf.Abs(x / (float)(s - 1) - 0.5f);
                bool inside = fx <= half && fy <= 0.95f && !(fy > 0.7f && fx < (fy - 0.7f) * 1.2f);
                t.SetPixel(x, y, inside ? Color.white : Color.clear);
            }
        t.Apply();
        return t;
    }

    static Texture2D MakeDot(int s)
    {
        var t = new Texture2D(s, s, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        float c = (s - 1) / 2f;
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01((1f - d) * 3f)));
            }
        t.Apply();
        return t;
    }
}
