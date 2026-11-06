using UnityEngine;

/// <summary>
/// 발 붙이기: 애니메이션이 끝난 뒤(LateUpdate) 양발·발끝 중 가장 낮은 곳을 재서
/// 바로 아래 땅에 닿도록 모델 높이를 부드럽게 맞춘다.
/// 다른 체형에서 받은 동작(Y Bot → Remy), 웅크리기, 계단·턱에서 캐릭터가 떠 보이거나 묻히는 걸 막는다.
/// 점프·넘기·쓰러짐 중에는 쉰다. 휴머노이드 모델에만 붙인다.
/// </summary>
[DefaultExecutionOrder(200)]
public class FootGrounder : MonoBehaviour
{
    public Animator animator;
    public Transform character;           // 땅을 잴 기준 (플레이어/안드로이드 루트)
    public float sole = 0.03f;            // 발목·발끝 뼈에서 신발 바닥까지
    public float maxUp = 0.35f, maxDown = 0.6f;
    public System.Func<bool> paused;      // true면 쉼 (점프·사망 등)

    Transform[] feet;
    Vector3 baseLocal;
    float offset;
    const int Mask = ~((1 << 31) | (1 << 30) | (1 << 2));   // 안드로이드·지도 전용·무시 레이어 제외

    void Start()
    {
        if (animator == null || !animator.isHuman) { enabled = false; return; }
        feet = new[]
        {
            animator.GetBoneTransform(HumanBodyBones.LeftFoot), animator.GetBoneTransform(HumanBodyBones.RightFoot),
            animator.GetBoneTransform(HumanBodyBones.LeftToes), animator.GetBoneTransform(HumanBodyBones.RightToes),
        };
        baseLocal = transform.localPosition;
    }

    void LateUpdate()
    {
        if (feet == null || character == null) return;
        bool rest = paused != null && paused();
        float target = 0f;
        if (!rest)
        {
            float minY = float.MaxValue;
            foreach (var f in feet) if (f != null) minY = Mathf.Min(minY, f.position.y);
            if (minY < float.MaxValue)
            {
                // 기준점(플레이어는 발밑, 안드로이드는 몸 가운데)에서 아래로 땅을 찾는다
                Vector3 o = character.position + Vector3.up * 0.6f;
                if (Physics.Raycast(o, Vector3.down, out RaycastHit hit, 3.5f, Mask, QueryTriggerInteraction.Ignore)
                    && !hit.transform.IsChildOf(character))
                {
                    // 지금 오프셋을 뺀 원래 발 높이 기준으로 필요한 오프셋
                    float footNoOffset = minY - offset;
                    target = Mathf.Clamp(hit.point.y + sole - footNoOffset, -maxDown, maxUp);
                }
            }
        }
        offset = Mathf.Lerp(offset, target, 1f - Mathf.Exp(-14f * Time.deltaTime));
        transform.localPosition = baseLocal + transform.parent.InverseTransformVector(Vector3.up * offset);
    }
}
