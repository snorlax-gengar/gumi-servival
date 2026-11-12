using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// 오른쪽 위 미니맵 + 화면 위쪽 나침반 + 조작 안내.
/// - 미니맵은 카메라가 바라보는 방향이 위쪽 (이동 방향과 일치)
/// - M: 전체 지도 (북쪽이 위). 휠 = 확대/축소(마우스 위치 기준), 왼쪽 드래그 = 이동, Space = 내 위치로
/// - 미니맵 위에서 휠, 또는 - / = 키: 미니맵 확대/축소
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

    /// <summary>전체 지도가 열려 있거나 마우스가 지도 위에 있으면 true (카메라 줌·공격 클릭을 막는다)</summary>
    public static bool BigMapOpen { get; private set; }
    public static bool PointerOverMap { get; private set; }

    public float miniMin = 15f, miniMax = 250f;
    float bigSize = 250f;
    Vector2 bigCenter;
    Rect lastRect;
    bool dragging;
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
        mapCam.cullingMask = ~((1 << AndroidAI.AndroidLayer) | (1 << 1));   // 1 = TransparentFX: 연기·불은 지도에 안 그림
        mapCam.nearClipPlane = 1f;
        mapCam.farClipPlane = 400f;
        mapCam.targetTexture = miniRT;
        mapCam.depth = -10;

        RenderPipelineManager.beginCameraRendering += BeforeCamera;
        arrow = MakeArrow(32);
        dot = MakeDot(16);
    }

    // 미니맵은 위에서 150m 떨어져 찍으니 안개를 끄고 그린다
    float savedFogStart = -1f, savedFogEnd;

    void BeforeCamera(ScriptableRenderContext ctx, Camera cam)
    {
        // URP는 안개 켜고 끄기를 카메라마다 바꾸지 못해서, 지도 카메라 때만 안개 거리를 아주 멀리 민다
        if (cam == mapCam)
        {
            if (savedFogStart < 0f) { savedFogStart = RenderSettings.fogStartDistance; savedFogEnd = RenderSettings.fogEndDistance; }
            RenderSettings.fogStartDistance = 5000f;
            RenderSettings.fogEndDistance = 6000f;
        }
        else if (savedFogStart >= 0f)
        {
            RenderSettings.fogStartDistance = savedFogStart;
            RenderSettings.fogEndDistance = savedFogEnd;
            savedFogStart = -1f;
        }
        RenderSettings.fog = GameManager.FogOn;
    }

    void OnDestroy()
    {
        RenderPipelineManager.beginCameraRendering -= BeforeCamera;
        if (miniRT) miniRT.Release();
        if (bigRT) bigRT.Release();
        if (mapCam) Destroy(mapCam.gameObject);
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.mKey.wasPressedThisFrame)
        {
            bigMap = !bigMap;
            if (bigMap && iso.target != null)
            {
                // 열 때는 내 주변 500m 정도
                bigCenter = new Vector2(iso.target.position.x, iso.target.position.z);
                bigSize = Mathf.Min(250f, FullSize());
            }
        }
        BigMapOpen = bigMap;
        HandleMapInput(kb);

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
            mapCam.targetTexture = bigRT;
            mapCam.orthographicSize = bigSize;
            mapCam.transform.SetPositionAndRotation(new Vector3(bigCenter.x, 150f, bigCenter.y), Quaternion.Euler(90f, 0f, 0f));
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
        lastRect = r;
        DrawMap(r, bigMap ? bigRT : miniRT);

        if (CityGenerator.RealMapActive && !string.IsNullOrEmpty(CityGenerator.MapAttribution))
        {
            var att = new GUIStyle(small) { fontSize = 11, alignment = TextAnchor.UpperRight };
            Rect ar = bigMap ? new Rect(r.xMax - 400, r.yMax + 4, 400, 18) : new Rect(r.xMax - 400, r.yMax + 2, 400, 18);
            GUI.Label(ar, "지도 " + CityGenerator.MapAttribution, att);
            var legend = new GUIStyle(att) { fontSize = 12 };
            legend.normal.textColor = new Color(0.44f, 0.88f, 0.72f);
            GUI.Label(new Rect(ar.x, ar.y + 16, ar.width, 18), "■ 민트 = 들어갈 수 있는 가게", legend);
        }

        if (showHelp)
            GUI.Label(new Rect(UIScale.W - 820, UIScale.H - 30, 800, 24),
                "F1 조작법 · Esc 메뉴·설정 · E 뒤지기 · 우클릭 밀치기 · Space 점프/넘기 · V 시점 · M 지도", small);
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

        // 목표 (노란 마름모). 지도 밖이면 가장자리에 붙인다
        var sm = ScenarioManager.Instance;
        if (sm != null && sm.ObjectivePos.HasValue)
        {
            Vector3 v = mapCam.WorldToViewportPoint(sm.ObjectivePos.Value);
            float vx = Mathf.Clamp(v.x, 0.04f, 0.96f), vy = Mathf.Clamp(v.y, 0.04f, 0.96f);
            Vector2 om = new Vector2(r.x + vx * r.width, r.y + (1f - vy) * r.height);
            Matrix4x4 sv = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, om * UIScale.S);
            GUI.color = Kit.Hazard;
            float sz = 9f + Mathf.Sin(Time.unscaledTime * 5f) * 1.5f;
            GUI.DrawTexture(new Rect(om.x - sz / 2, om.y - sz / 2, sz, sz), Texture2D.whiteTexture);
            GUI.matrix = sv;
            GUI.color = Color.white;
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
            GUI.Label(new Rect(r.x, r.yMax + 4, r.width, 24), "전체 지도 (북쪽이 위) · 휠 확대/축소 · 드래그 이동 · Space 내 위치 · M 닫기", label);
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

    // ================= 지도 확대/축소·이동 =================

    float FullSize()
    {
        Bounds b = LevelNavMesh.LevelBounds;
        if (b.size.sqrMagnitude < 1f) return 150f;
        return Mathf.Max(b.extents.x, b.extents.z) + 4f;
    }

    void ClampBig()
    {
        float full = FullSize();
        bigSize = Mathf.Clamp(bigSize, 30f, full);
        Bounds b = LevelNavMesh.LevelBounds;
        if (b.size.sqrMagnitude < 1f) return;
        float mx = Mathf.Max(0f, b.extents.x - bigSize * 0.5f), mz = Mathf.Max(0f, b.extents.z - bigSize * 0.5f);
        bigCenter.x = Mathf.Clamp(bigCenter.x, b.center.x - mx, b.center.x + mx);
        bigCenter.y = Mathf.Clamp(bigCenter.y, b.center.z - mz, b.center.z + mz);
    }

    void HandleMapInput(Keyboard kb)
    {
        var mouse = Mouse.current;
        Vector2 mp = Vector2.zero;
        if (mouse != null)
        {
            Vector2 sp = mouse.position.ReadValue();
            mp = new Vector2(sp.x / UIScale.S, (Screen.height - sp.y) / UIScale.S);   // OnGUI 좌표
        }
        PointerOverMap = lastRect.width > 0 && lastRect.Contains(mp);
        float scroll = mouse != null ? mouse.scroll.ReadValue().y : 0f;

        if (!bigMap)
        {
            float step = 0f;
            if (PointerOverMap && Mathf.Abs(scroll) > 0.01f) step = -Mathf.Sign(scroll);
            if (kb != null && (kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame)) step = 1f;
            if (kb != null && (kb.equalsKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame)) step = -1f;
            if (step != 0f) miniOrthoSize = Mathf.Clamp(miniOrthoSize * (step > 0 ? 1.35f : 1f / 1.35f), miniMin, miniMax);
            dragging = false;
            return;
        }

        // 전체 지도: 휠 = 마우스가 가리키는 곳을 기준으로 확대/축소
        float zoom = 0f;
        if (Mathf.Abs(scroll) > 0.01f) zoom = -Mathf.Sign(scroll);
        if (kb != null && (kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame)) zoom = 1f;
        if (kb != null && (kb.equalsKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame)) zoom = -1f;
        if (zoom != 0f)
        {
            Vector2 uv = PointerOverMap
                ? new Vector2((mp.x - lastRect.x) / lastRect.width, 1f - (mp.y - lastRect.y) / lastRect.height)
                : new Vector2(0.5f, 0.5f);
            Vector2 world = bigCenter + (uv - new Vector2(0.5f, 0.5f)) * 2f * bigSize;
            bigSize *= zoom > 0 ? 1.3f : 1f / 1.3f;
            bigSize = Mathf.Clamp(bigSize, 30f, FullSize());
            bigCenter = world - (uv - new Vector2(0.5f, 0.5f)) * 2f * bigSize;
        }

        // 왼쪽 드래그 = 이동
        if (mouse != null)
        {
            if (mouse.leftButton.wasPressedThisFrame && PointerOverMap) dragging = true;
            if (!mouse.leftButton.isPressed) dragging = false;
            if (dragging && lastRect.width > 0)
            {
                Vector2 d = mouse.delta.ReadValue() / UIScale.S;
                bigCenter.x -= d.x / lastRect.width * 2f * bigSize;
                bigCenter.y -= d.y / lastRect.height * 2f * bigSize;
            }
        }
        if (kb != null && kb.spaceKey.wasPressedThisFrame && iso.target != null)
            bigCenter = new Vector2(iso.target.position.x, iso.target.position.z);
        ClampBig();
    }

    void OnDisable() { BigMapOpen = false; PointerOverMap = false; }

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
        // 목표 방향 (노란 점)
        var sm = ScenarioManager.Instance;
        if (sm != null && sm.ObjectivePos.HasValue && iso.target != null)
        {
            Vector3 d = sm.ObjectivePos.Value - iso.target.position;
            float yawTo = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float delta = Mathf.Clamp(Mathf.DeltaAngle(iso.Yaw, yawTo), -88f, 88f);
            float ox = r.center.x + delta * pxPerDeg;
            GUI.color = Kit.Hazard;
            GUI.DrawTexture(new Rect(ox - 4, r.yMax - 10, 8, 8), Texture2D.whiteTexture);
            GUI.color = Color.white;
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
