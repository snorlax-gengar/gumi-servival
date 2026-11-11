using UnityEngine;

/// <summary>
/// 고개 돌리기: 애니메이션이 끝난 뒤 가슴·목·머리를 카메라가 보는 쪽으로 조금씩 나눠 돌린다.
/// 어깨 시점에서 마우스로 둘러보면 캐릭터도 그쪽을 쳐다본다 (뒤쪽을 보면 정면으로 돌아온다).
/// 휴머노이드 모델의 Animator와 같은 오브젝트에 붙인다.
/// </summary>
[DefaultExecutionOrder(210)]
public class HeadLook : MonoBehaviour
{
    public Animator animator;
    public Transform character;
    public System.Func<Vector3?> lookDir;     // null이면 정면
    public float maxYaw = 70f, maxPitch = 35f;
    public float movingYaw = 25f;          // 걸을 때는 가는 방향을 주로 본다 (어깨 너머로 계속 돌아보면 어색)
    public System.Func<float> moving;      // 0 = 서 있음, 1 = 걷는 중 이상

    Transform chest, neck, head;
    float yaw, pitch;

    void Start()
    {
        if (animator == null || !animator.isHuman) { enabled = false; return; }
        chest = animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest);
        neck = animator.GetBoneTransform(HumanBodyBones.Neck);
        head = animator.GetBoneTransform(HumanBodyBones.Head);
    }

    void LateUpdate()
    {
        if (head == null || character == null) return;
        float ty = 0f, tp = 0f;
        Vector3? d = lookDir != null ? lookDir() : null;
        if (d.HasValue && d.Value.sqrMagnitude > 0.01f)
        {
            Vector3 l = character.InverseTransformDirection(d.Value.normalized);
            float y = Mathf.Atan2(l.x, l.z) * Mathf.Rad2Deg;
            float p = -Mathf.Asin(Mathf.Clamp(l.y, -1f, 1f)) * Mathf.Rad2Deg;
            float lim = Mathf.Lerp(maxYaw, movingYaw, moving != null ? Mathf.Clamp01(moving()) : 0f);
            if (Mathf.Abs(y) < 115f) { ty = Mathf.Clamp(y, -lim, lim); tp = Mathf.Clamp(p, -maxPitch, maxPitch); }
        }
        float k = 1f - Mathf.Exp(-6f * Time.deltaTime);
        yaw = Mathf.Lerp(yaw, ty, k);
        pitch = Mathf.Lerp(pitch, tp, k);
        if (Mathf.Abs(yaw) < 0.1f && Mathf.Abs(pitch) < 0.1f) return;

        Vector3 up = character.up, right = character.right;
        Turn(chest, 0.2f, up, right);
        Turn(neck, 0.3f, up, right);
        Turn(head, 0.5f, up, right);
    }

    void Turn(Transform b, float share, Vector3 up, Vector3 right)
    {
        if (b == null) return;
        b.rotation = Quaternion.AngleAxis(yaw * share, up) * Quaternion.AngleAxis(pitch * share, right) * b.rotation;
    }
}
