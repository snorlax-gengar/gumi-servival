using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 좌클릭 근접 공격. 장착 무기가 없으면 맨손(약함).
/// 앞쪽으로 휘두르는 궤적이 보이고, 무거운 무기일수록 스태미나를 많이 쓴다.
/// 공격은 소음을 낸다 — 싸울수록 주변 안드로이드가 몰려온다.
/// </summary>
public class PlayerCombat : MonoBehaviour
{
    [Header("맨손")]
    public float fistDamage = 8f;
    public float fistRange = 1.5f;
    public float fistCooldown = 0.5f;
    public float fistNoise = 4f;

    [Header("스태미나 = 기본 + 무기 무게(kg) x 계수")]
    public float baseStaminaCost = 4f;
    public float staminaPerKg = 3f;

    public float hitAngle = 100f;  // 전방 판정 각도(도)

    [Header("기습 (눈치 못 챈 안드로이드의 등 뒤)")]
    public float ambushMultiplier = 2.5f;        // 서서 기습
    public float crouchAmbushMultiplier = 4f;    // 웅크려서 기습 (렌치 25 x 4 = 100, 한 방)
    public float ambushStun = 2f;                // 기습당한 안드로이드가 멈춰 있는 시간
    public float ambushNoiseMultiplier = 0.4f;   // 기습은 소리가 작다

    float cooldown;
    Inventory inv;
    PlayerStats stats;
    PlayerController controller;
    TargetIndicator target;

    /// <summary>지금 장착한 무기의 공격 거리 (맨손 포함)</summary>
    public float CurrentRange
    {
        get
        {
            ItemData w = inv != null ? inv.EquippedWeapon : null;
            return w ? w.attackRange : fistRange;
        }
    }
    readonly Collider[] hits = new Collider[16];

    // 휘두르기 연출
    Transform swing;
    float swingTimer;
    const float SwingDuration = 0.15f;
    float swingRange = 1.5f;

    void Awake()
    {
        inv = GetComponent<Inventory>();
        stats = GetComponent<PlayerStats>();
        controller = GetComponent<PlayerController>();
        target = GetComponent<TargetIndicator>();
        if (target == null) target = gameObject.AddComponent<TargetIndicator>();
        BuildSwingVisual();
    }

    void BuildSwingVisual()
    {
        var pivot = new GameObject("SwingPivot").transform;
        pivot.SetParent(transform, false);
        pivot.localPosition = new Vector3(0, 1.1f, 0);

        var blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(blade.GetComponent<Collider>());
        blade.name = "SwingBlade";
        blade.transform.SetParent(pivot, false);
        var r = blade.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.material.color = new Color(1f, 0.95f, 0.8f);
        r.material.SetColor("_BaseColor", new Color(1f, 0.95f, 0.8f));

        swing = pivot;
        swing.gameObject.SetActive(false);
    }

    void Update()
    {
        cooldown -= Time.deltaTime;
        AnimateSwing();
        if (stats != null && stats.IsDead) return;

        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame || cooldown > 0) return;
        // 인벤토리 버튼을 누른 클릭은 공격이 아니다
        Vector2 mp = mouse.position.ReadValue();
        if (HUD.InventoryScreenRect.Contains(new Vector2(mp.x, Screen.height - mp.y))) return;

        ItemData w = inv != null ? inv.EquippedWeapon : null;
        float dmg = w ? w.damage : fistDamage;
        float range = w ? w.attackRange : fistRange;
        float noise = w ? w.attackNoise : fistNoise;
        float cost = baseStaminaCost + (w ? w.weight : 0f) * staminaPerKg;

        if (stats != null && stats.stamina < cost) { HUD.ShowMessage("너무 지쳤다 (스태미나 부족)"); return; }

        cooldown = w ? w.attackCooldown : fistCooldown;
        // 휘두르는 동안은 제자리 (쿨다운의 약 60%)
        // 좀보이드식: 휘두르는 순간 마우스 커서 쪽으로 몸을 돌린다
        if (controller != null)
        {
            // 겨냥한 안드로이드가 있으면 그쪽, 없으면 커서 쪽
            if (target != null && target.Current != null) controller.FaceTowards(target.Current.transform.position);
            else if (controller.MouseGroundPoint(out Vector3 aim)) controller.FaceTowards(aim);
            controller.LockForAttack(cooldown * 0.6f);
        }
        if (stats != null) stats.stamina -= cost;
        StartSwing(range);
        bool crouching = controller != null && controller.IsCrouching;
        bool anyAmbush = false;

        Vector3 center = transform.position + Vector3.up;
        int n = Physics.OverlapSphereNonAlloc(center, range + 0.5f, hits);
        bool hitSomething = false;
        for (int i = 0; i < n; i++)
        {
            var d = hits[i].GetComponentInParent<Damageable>();
            if (d == null || d.IsDead) continue;
            Vector3 to = d.transform.position - transform.position; to.y = 0;
            if (to.magnitude > range + 0.6f) continue;
            if (Vector3.Angle(transform.forward, to) > hitAngle * 0.5f) continue;

            float dealt = dmg;
            var ai = d.GetComponent<AndroidAI>();
            if (ai != null && ai.CanBeAmbushedFrom(transform.position))
            {
                dealt *= crouching ? crouchAmbushMultiplier : ambushMultiplier;
                ai.Stun(ambushStun);   // 맞기 전에 멈춰 두어야 바로 돌아보지 않는다
                anyAmbush = true;
            }
            d.TakeDamage(dealt, transform.position);
            hitSomething = true;
        }

        // 소리는 휘두른 뒤에 퍼진다 (기습은 조용하다)
        NoiseSystem.Emit(transform.position, anyAmbush ? noise * ambushNoiseMultiplier : noise);

        if (anyAmbush) HUD.ShowMessage(crouching ? "기습! 급소를 노렸다" : "기습!", 1.5f);
        else if (!hitSomething) HUD.ShowMessage("빗나감", 0.4f);
        else if (w == null) HUD.ShowMessage("맨손은 약하다. 무기를 찾자", 1.2f);
    }

    void StartSwing(float range)
    {
        swingRange = range;
        swingTimer = SwingDuration;
        var blade = swing.GetChild(0);
        blade.localScale = new Vector3(0.08f, 0.08f, range);
        blade.localPosition = new Vector3(0, 0, range * 0.5f);
        swing.gameObject.SetActive(true);
        var visual = GetComponent<PlayerVisual>();
        if (visual != null) visual.TriggerSwing();
    }

    void AnimateSwing()
    {
        if (swingTimer <= 0) return;
        swingTimer -= Time.deltaTime;
        float t = 1f - Mathf.Clamp01(swingTimer / SwingDuration);
        float half = hitAngle * 0.5f;
        swing.localRotation = Quaternion.Euler(0, Mathf.Lerp(-half, half, t), 0);
        if (swingTimer <= 0) swing.gameObject.SetActive(false);
    }
}
