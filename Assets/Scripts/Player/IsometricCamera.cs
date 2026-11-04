using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// 3인칭 쿼터뷰 카메라.
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

    [Header("가림 처리")]
    public bool hideOccluders = true;
    public float occluderRadius = 0.6f;

    public float Yaw => currentYaw;      // 실제 화면 기준 각도 (나침반·미니맵에서 사용)
    public bool IsDragging { get; private set; }

    float currentYaw, currentPitch;
    readonly Dictionary<Renderer, ShadowCastingMode> hidden = new Dictionary<Renderer, ShadowCastingMode>();
    readonly HashSet<Renderer> hitNow = new HashSet<Renderer>();
    readonly List<Renderer> toRestore = new List<Renderer>();
    const int AndroidLayer = 31;

    void Awake()
    {
        currentYaw = yaw;
        currentPitch = pitch;
        if (GetComponent<MinimapHUD>() == null) gameObject.AddComponent<MinimapHUD>();
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
        HandleInput();

        currentYaw = Mathf.LerpAngle(currentYaw, yaw, rotateSmooth * Time.deltaTime);
        currentPitch = Mathf.Lerp(currentPitch, pitch, rotateSmooth * Time.deltaTime);

        Quaternion rot = Quaternion.Euler(currentPitch, currentYaw, 0f);
        Vector3 focus = target.position + Vector3.up * 1f;
        Vector3 desired = focus - rot * Vector3.forward * distance;
        transform.position = Vector3.Lerp(transform.position, desired, followSmooth * Time.deltaTime);
        transform.rotation = rot;

        if (hideOccluders) UpdateOccluders(focus);
    }

    void HandleInput()
    {
        var mouse = Mouse.current;
        var kb = Keyboard.current;

        if (mouse != null)
        {
            // 휠 값의 크기는 OS마다 달라서(1 또는 120) 방향만 사용
            float scroll = mouse.scroll.ReadValue().y;
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
        foreach (var kv in hidden) if (!hitNow.Contains(kv.Key)) toRestore.Add(kv.Key);
        foreach (var r in toRestore)
        {
            if (r != null) r.shadowCastingMode = hidden[r];
            hidden.Remove(r);
        }
    }

    void Hide(Renderer r)
    {
        if (r == null) return;
        hitNow.Add(r);
        if (!hidden.ContainsKey(r))
        {
            hidden[r] = r.shadowCastingMode;
            r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        }
    }
}
