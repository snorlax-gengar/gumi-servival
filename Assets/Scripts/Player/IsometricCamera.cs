using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// 카메라: 1인칭(기본) ↔ 3인칭 어깨 시점, V로 전환. (예전 쿼터뷰 코드도 남아 있음)
/// 1인칭: 마우스로 둘러보기(커서 잠김, Alt를 누르면 커서), 몸은 보는 방향을 향함, 걸을 때 머리 흔들림.
/// 3인칭 쿼터뷰:
/// - 마우스 우클릭 드래그: 자유 회전(좌우) + 기울기(상하)
/// - Q / E: 90도씩 부드럽게 회전
/// - 마우스 휠: 줌
/// - 플레이어를 가리는 건물은 그림자만 남기고 숨긴다
/// 미니맵/나침반(MinimapHUD)이 없으면 자동으로 붙인다.
/// </summary>
public class IsometricCamera : MonoBehaviour
{
    public Transform target;
    public float distance = 18f;
    public float minDistance = 6f;
    public float maxDistance = 35f;
    public float pitch = 50f;   // 내려다보는 각도
    public float yaw = 45f;     // 좌우 회전
    public float followSmooth = 10f;
    public float zoomStep = 1.5f;   // 휠 한 칸당 이동 거리(m)

    [Header("회전")]
    public float dragYawSpeed = 0.25f;     // 픽셀당 도
    public float dragPitchSpeed = 0.15f;
    public float minPitch = 20f, maxPitch = 85f;
    public float snapStep = 90f;           // Q/E 한 번에 도는 각도
    public float rotateSmooth = 12f;

    [Header("좀보이드식 시점")]
    [Tooltip("좁은 화각으로 멀리서 내려다봐서 원근 왜곡이 적은 아이소메트릭 느낌")]
    public bool zomboidView = true;
    public float zomboidFov = 24f;

    [Header("가림 처리")]
    public bool hideOccluders = true;
    public float occluderRadius = 0.6f;

    public float Yaw => currentYaw;

    [Header("1인칭")]
    public bool startFirstPerson = true;
    public float fpFov = 72f;
    public float mouseSensitivity = 0.11f;
    public float eyeHeight = 1.62f, crouchEyeHeight = 1.05f;
    public static bool FirstPerson { get; private set; }

    [Header("3인칭 어깨 시점")]
    public float shoulderDistance = 2.8f;
    public float shoulderRight = 0.6f, shoulderHeight = 1.55f;
    public float shoulderFov = 62f;
    /// <summary>어깨 시점 (V로 1인칭과 전환)</summary>
    public static bool Shoulder { get; private set; }
    /// <summary>마우스로 둘러보는 시점 (1인칭 또는 어깨). 몸이 보는 방향을 향한다.</summary>
    public static bool MouseLook => FirstPerson || Shoulder;
    float shoulderCur;
    float fpYaw, fpPitch, eye, bobT;
    float isoFov = 24f, isoNear = 0.3f;
    PlayerVisual pvis;
    PlayerController pctrl;      // 실제 화면 기준 각도 (나침반·미니맵에서 사용)
    public bool IsDragging { get; private set; }

    float currentYaw, currentPitch;
    readonly HashSet<Renderer> hitNow = new HashSet<Renderer>();
    readonly List<Renderer> toRestore = new List<Renderer>();
    const int AndroidLayer = 31;

    void Awake()
    {
        // 미니맵 전용 아이콘(출입구 등)은 본 화면에서 안 보이게
        var mainCam = GetComponent<Camera>();
        if (mainCam != null) mainCam.cullingMask &= ~(1 << CityGenerator.MapOnlyLayer);

        if (zomboidView)
        {
            var cam = GetComponent<Camera>();
            if (cam != null)
            {
                // 화각을 좁힌 만큼 멀리 둔다 (화면에 보이는 범위는 비슷하게)
                float k = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(zomboidFov * 0.5f * Mathf.Deg2Rad);
                cam.fieldOfView = zomboidFov;
                cam.farClipPlane = Mathf.Max(cam.farClipPlane, 600f);
                distance *= k; minDistance *= k; maxDistance *= k; zoomStep *= k;
                pitch = 42f;
                minPitch = 28f; maxPitch = 70f;
            }
        }
        currentYaw = yaw;
        currentPitch = pitch;
        if (GetComponent<MinimapHUD>() == null) gameObject.AddComponent<MinimapHUD>();
        var c0 = GetComponent<Camera>();
        if (c0 != null) { isoFov = c0.fieldOfView; isoNear = c0.nearClipPlane; }
        FirstPerson = false;
        Shoulder = false;
    }

    void Start()
    {
        if (target != null) { pvis = target.GetComponent<PlayerVisual>(); pctrl = target.GetComponent<PlayerController>(); }
        SetFirstPerson(startFirstPerson);
    }

    void OnDisable() { FirstPerson = false; Shoulder = false; Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }

    /// <summary>어깨 시점으로 (몸이 보이는 3인칭, 마우스로 둘러봄)</summary>
    public void SetShoulder()
    {
        bool wasMouseLook = MouseLook;
        FirstPerson = false;
        Shoulder = true;
        RestoreAllGhosts();
        if (!wasMouseLook) { fpYaw = target != null ? target.eulerAngles.y : currentYaw; fpPitch = 10f; }
        shoulderCur = shoulderDistance;
        var cam = GetComponent<Camera>();
        if (cam != null) { cam.fieldOfView = shoulderFov; cam.nearClipPlane = 0.1f; }
        if (pvis == null && target != null) pvis = target.GetComponent<PlayerVisual>();
        if (pvis != null) pvis.SetFirstPerson(false);
        UpdateCursor();
    }

    public void SetFirstPerson(bool fp)
    {
        bool wasMouseLook = MouseLook;
        FirstPerson = fp;
        Shoulder = false;
        var cam = GetComponent<Camera>();
        if (fp)
        {
            RestoreAllGhosts();
            if (!wasMouseLook) { fpYaw = target != null ? target.eulerAngles.y : currentYaw; fpPitch = 8f; }
            eye = eyeHeight;
            if (cam != null) { cam.fieldOfView = fpFov; cam.nearClipPlane = 0.05f; }
        }
        else
        {
            yaw = fpYaw; currentYaw = fpYaw;
            if (cam != null) { cam.fieldOfView = isoFov; cam.nearClipPlane = isoNear; }
            Snap();
        }
        if (pvis == null && target != null) pvis = target.GetComponent<PlayerVisual>();
        if (pvis != null) pvis.SetFirstPerson(fp);
        UpdateCursor();
    }

    void UpdateCursor()
    {
        var kb = Keyboard.current;
        bool free = !MouseLook || MinimapHUD.BigMapOpen || (kb != null && kb.leftAltKey.isPressed);
        Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = free;
    }

    void RestoreAllGhosts()
    {
        foreach (var kv in ghosted) if (kv.Key != null) Restore(kv.Key, kv.Value);
        ghosted.Clear();
    }

    void FirstPersonUpdate()
    {
        UpdateCursor();
        var mouse = Mouse.current;
        if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
        {
            Vector2 d = mouse.delta.ReadValue() * mouseSensitivity;
            fpYaw += d.x;
            fpPitch = Mathf.Clamp(fpPitch - d.y, -80f, 80f);
        }
        // 몸은 보는 방향
        target.rotation = Quaternion.Euler(0f, fpYaw, 0f);
        currentYaw = fpYaw;

        bool crouch = pctrl != null && pctrl.IsCrouching;
        eye = Mathf.Lerp(eye, crouch ? crouchEyeHeight : eyeHeight, 10f * Time.deltaTime);
        // 걸을 때 머리 흔들림 (달리면 더 크게)
        float bob = 0f, sway = 0f;
        if (pctrl != null && pctrl.IsMoving)
        {
            float f = pctrl.IsRunning ? 13f : crouch ? 6f : 9f;
            float a = pctrl.IsRunning ? 0.05f : 0.025f;
            bobT += Time.deltaTime * f;
            bob = Mathf.Sin(bobT) * a;
            sway = Mathf.Cos(bobT * 0.5f) * a * 0.6f;
        }
        Quaternion rot = Quaternion.Euler(fpPitch, fpYaw, 0f);
        Vector3 pos = target.position + Vector3.up * (eye + bob) + rot * new Vector3(sway, 0, 0.12f);
        transform.SetPositionAndRotation(pos, rot);

        RenderSettings.fogStartDistance = 12f;
        RenderSettings.fogEndDistance = 12f + GameManager.FogDepth * 0.8f;
        var urp = GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
        if (urp != null && Mathf.Abs(urp.shadowDistance - 70f) > 5f) urp.shadowDistance = 70f;
    }

    /// <summary>어깨 시점: 오른쪽 어깨 너머. 벽에 막히면 카메라를 당긴다.</summary>
    void ShoulderUpdate()
    {
        UpdateCursor();
        var mouse = Mouse.current;
        if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
        {
            Vector2 d = mouse.delta.ReadValue() * mouseSensitivity;
            fpYaw += d.x;
            fpPitch = Mathf.Clamp(fpPitch - d.y, -60f, 70f);
        }
        target.rotation = Quaternion.Euler(0f, fpYaw, 0f);
        currentYaw = fpYaw;

        bool crouch = pctrl != null && pctrl.IsCrouching;
        Quaternion rot = Quaternion.Euler(fpPitch, fpYaw, 0f);
        Vector3 pivot = target.position + Vector3.up * (crouch ? shoulderHeight - 0.45f : shoulderHeight) + (rot * Vector3.right) * shoulderRight;
        Vector3 back = rot * Vector3.back;
        float want = shoulderDistance;
        int mask = ~(1 << AndroidLayer);
        if (Physics.SphereCast(target.position + Vector3.up * 1.4f, 0.2f, (pivot + back * want - (target.position + Vector3.up * 1.4f)).normalized,
                out RaycastHit hit, Vector3.Distance(target.position + Vector3.up * 1.4f, pivot + back * want), mask, QueryTriggerInteraction.Ignore)
            && !hit.transform.IsChildOf(target))
            want = Mathf.Max(0.4f, hit.distance - 0.6f);
        shoulderCur = Mathf.Lerp(shoulderCur, want, (want < shoulderCur ? 20f : 5f) * Time.deltaTime);
        transform.SetPositionAndRotation(pivot + back * shoulderCur, rot);

        RenderSettings.fogStartDistance = 14f;
        RenderSettings.fogEndDistance = 14f + GameManager.FogDepth * 0.85f;
        var urp = GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
        if (urp != null && Mathf.Abs(urp.shadowDistance - 70f) > 5f) urp.shadowDistance = 70f;
    }

    void OnGUI()
    {
        if (!MouseLook || MinimapHUD.BigMapOpen) return;
        // 화면 가운데 조준점
        float s = 4f;
        GUI.color = new Color(1f, 1f, 1f, 0.75f);
        GUI.DrawTexture(new Rect(Screen.width * 0.5f - s * 0.5f, Screen.height * 0.5f - s * 0.5f, s, s), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    /// <summary>보간 없이 즉시 플레이어 위치로 이동 (스폰 직후 사용)</summary>
    public void Snap()
    {
        if (target == null) return;
        currentYaw = yaw; currentPitch = pitch;
        Quaternion rot = Quaternion.Euler(currentPitch, currentYaw, 0f);
        transform.SetPositionAndRotation(target.position - rot * Vector3.forward * distance, rot);
    }

    void LateUpdate()
    {
        if (target == null) return;
        var kbv = Keyboard.current;
        // V: 1인칭 ↔ 어깨 시점
        if (kbv != null && kbv.vKey.wasPressedThisFrame)
        {
            if (FirstPerson) SetShoulder(); else SetFirstPerson(true);
        }
        if (FirstPerson) { FirstPersonUpdate(); return; }
        if (Shoulder) { ShoulderUpdate(); return; }
        UpdateCursor();
        HandleInput();

        currentYaw = Mathf.LerpAngle(currentYaw, yaw, rotateSmooth * Time.deltaTime);
        currentPitch = Mathf.Lerp(currentPitch, pitch, rotateSmooth * Time.deltaTime);

        Quaternion rot = Quaternion.Euler(currentPitch, currentYaw, 0f);
        Vector3 focus = target.position + Vector3.up * 1f;
        Vector3 desired = focus - rot * Vector3.forward * distance;
        transform.position = Vector3.Lerp(transform.position, desired, followSmooth * Time.deltaTime);
        transform.rotation = rot;

        if (hideOccluders) UpdateOccluders(focus);

        // 안개·그림자 거리는 카메라가 멀어진 만큼 밀어 준다 (플레이어 주변은 선명하게)
        float camDist = Vector3.Distance(transform.position, focus);
        RenderSettings.fogStartDistance = camDist + 12f;
        RenderSettings.fogEndDistance = camDist + GameManager.FogDepth;
        var urp = GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
        if (urp != null)
        {
            float sd = camDist + 45f;
            if (Mathf.Abs(urp.shadowDistance - sd) > 5f) urp.shadowDistance = sd;
        }
    }

    void HandleInput()
    {
        var mouse = Mouse.current;
        var kb = Keyboard.current;

        if (mouse != null)
        {
            // 휠 값의 크기는 OS마다 달라서(1 또는 120) 방향만 사용
            float scroll = mouse.scroll.ReadValue().y;
            if (MinimapHUD.BigMapOpen || MinimapHUD.PointerOverMap) scroll = 0f;   // 지도 위에서는 지도 줌
            if (Mathf.Abs(scroll) > 0.01f)
                distance = Mathf.Clamp(distance - Mathf.Sign(scroll) * zoomStep, minDistance, maxDistance);

            IsDragging = mouse.rightButton.isPressed;
            if (IsDragging)
            {
                Vector2 d = mouse.delta.ReadValue();
                yaw += d.x * dragYawSpeed;
                pitch = Mathf.Clamp(pitch - d.y * dragPitchSpeed, minPitch, maxPitch);
            }
        }

        if (kb != null)
        {
            if (kb.qKey.wasPressedThisFrame) yaw -= snapStep;
            if (kb.eKey.wasPressedThisFrame) yaw += snapStep;
        }
    }

    void UpdateOccluders(Vector3 focus)
    {
        hitNow.Clear();
        Vector3 from = transform.position;
        Vector3 dir = focus - from;
        float len = dir.magnitude;
        int mask = ~(1 << AndroidLayer);
        var hits = Physics.SphereCastAll(from, occluderRadius, dir / len, len - 1f, mask, QueryTriggerInteraction.Ignore);
        foreach (var h in hits)
        {
            if (h.transform == target || h.transform.IsChildOf(target)) continue;

            // 건물이면 창문·간판까지 통째로 숨긴다
            var group = h.collider.GetComponentInParent<OccluderGroup>();
            if (group != null)
            {
                foreach (var gr in group.Renderers) Hide(gr);
                continue;
            }
            var r = h.collider.GetComponent<Renderer>();
            if (r == null || r.bounds.max.y < 2f) continue;   // 바닥·낮은 물건은 그대로
            Hide(r);
        }

        toRestore.Clear();
        foreach (var kv in ghosted) if (!hitNow.Contains(kv.Key)) toRestore.Add(kv.Key);
        foreach (var r in toRestore)
        {
            if (r != null) Restore(r, ghosted[r]);
            ghosted.Remove(r);
        }
    }

    // ---------------- 반투명 처리 ----------------
    [Header("가리는 건물 반투명")]
    [Range(0.05f, 0.9f)] public float ghostAlpha = 0.28f;

    struct Saved { public Material[] mats; public ShadowCastingMode shadow; }
    readonly Dictionary<Renderer, Saved> ghosted = new Dictionary<Renderer, Saved>();
    static readonly Dictionary<Material, Material> ghostCache = new Dictionary<Material, Material>();

    /// <summary>가리는 건물을 반투명하게 바꿔 실루엣은 남긴다 (URP Lit가 아니면 그림자만 남기고 숨김)</summary>
    void Hide(Renderer r)
    {
        if (r == null) return;
        hitNow.Add(r);
        if (ghosted.ContainsKey(r)) return;

        var saved = new Saved { mats = r.sharedMaterials, shadow = r.shadowCastingMode };
        bool allLit = saved.mats.Length > 0;
        foreach (var m in saved.mats)
            if (m == null || m.shader == null || !m.shader.name.Contains("Universal Render Pipeline/Lit")) { allLit = false; break; }

        if (allLit)
        {
            var g = new Material[saved.mats.Length];
            for (int i = 0; i < g.Length; i++) g[i] = Ghost(saved.mats[i]);
            r.sharedMaterials = g;   // 그림자는 그대로 둬서 바닥 명암이 튀지 않게
        }
        else r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        ghosted[r] = saved;
    }

    static void Restore(Renderer r, Saved s)
    {
        r.sharedMaterials = s.mats;
        r.shadowCastingMode = s.shadow;
    }

    Material Ghost(Material src)
    {
        if (ghostCache.TryGetValue(src, out var m) && m != null) return m;
        m = new Material(src) { name = src.name + "_ghost" };
        m.SetFloat("_Surface", 1f);            // Transparent
        m.SetFloat("_Blend", 0f);              // Alpha
        m.SetFloat("_AlphaClip", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_ALPHATEST_ON");
        m.renderQueue = (int)RenderQueue.Transparent;
        if (m.HasProperty("_BaseColor"))
        {
            Color c = m.GetColor("_BaseColor"); c.a = ghostAlpha;
            m.SetColor("_BaseColor", c);
        }
        ghostCache[src] = m;
        return m;
    }
}
