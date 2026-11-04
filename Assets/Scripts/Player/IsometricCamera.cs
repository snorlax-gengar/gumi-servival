using UnityEngine;

/// <summary>
/// 좀보이드 스타일 쿼터뷰 카메라. 플레이어를 부드럽게 따라가며
/// 마우스 휠로 줌. 카메라 오브젝트에 붙이고 target에 플레이어를 넣는다.
/// </summary>
public class IsometricCamera : MonoBehaviour
{
    public Transform target;
    public float distance = 18f;
    public float minDistance = 8f;
    public float maxDistance = 30f;
    public float pitch = 50f;   // 내려다보는 각도
    public float yaw = 45f;     // 대각선 시점
    public float followSmooth = 8f;
    public float zoomStep = 1.5f;   // 휠 한 칸당 이동 거리(m)

    /// <summary>보간 없이 즉시 플레이어 위치로 이동 (스폰 직후 사용)</summary>
    public void Snap()
    {
        if (target == null) return;
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        transform.SetPositionAndRotation(target.position - rot * Vector3.forward * distance, rot);
    }

    void LateUpdate()
    {
        if (target == null) return;

        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse != null)
        {
            // 휠 값의 크기는 OS마다 달라서(1 또는 120) 방향만 사용
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
                distance = Mathf.Clamp(distance - Mathf.Sign(scroll) * zoomStep, minDistance, maxDistance);
        }

        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 desired = target.position - rot * Vector3.forward * distance;
        transform.position = Vector3.Lerp(transform.position, desired, followSmooth * Time.deltaTime);
        transform.rotation = rot;
    }
}
