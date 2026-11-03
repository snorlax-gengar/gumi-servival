using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 좌클릭 근접 공격. 장착 무기가 없으면 맨손(약함).
/// 공격은 스태미나를 쓰고 소음을 낸다 — 싸울수록 주변 안드로이드가 몰려온다.
/// </summary>
public class PlayerCombat : MonoBehaviour
{
    [Header("맨손")]
    public float fistDamage = 5f;
    public float fistRange = 1.4f;
    public float fistCooldown = 0.6f;
    public float fistNoise = 4f;

    public float staminaCost = 12f;
    public float hitAngle = 70f;  // 전방 판정 각도(도)

    float cooldown;
    Inventory inv;
    PlayerStats stats;
    readonly Collider[] hits = new Collider[16];

    void Awake()
    {
        inv = GetComponent<Inventory>();
        stats = GetComponent<PlayerStats>();
    }

    void Update()
    {
        cooldown -= Time.deltaTime;
        if (stats != null && stats.IsDead) return;

        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame || cooldown > 0) return;
        if (stats != null && stats.stamina < staminaCost) { HUD.ShowMessage("너무 지쳤다"); return; }

        ItemData w = inv != null ? inv.EquippedWeapon : null;
        float dmg = w ? w.damage : fistDamage;
        float range = w ? w.attackRange : fistRange;
        float noise = w ? w.attackNoise : fistNoise;
        cooldown = w ? w.attackCooldown : fistCooldown;

        if (stats != null) stats.stamina -= staminaCost;
        NoiseSystem.Emit(transform.position, noise);

        Vector3 center = transform.position + Vector3.up;
        int n = Physics.OverlapSphereNonAlloc(center, range, hits);
        bool hitSomething = false;
        for (int i = 0; i < n; i++)
        {
            var d = hits[i].GetComponentInParent<Damageable>();
            if (d == null || d.IsDead) continue;
            Vector3 to = d.transform.position - transform.position; to.y = 0;
            if (Vector3.Angle(transform.forward, to) > hitAngle * 0.5f) continue;
            d.TakeDamage(dmg, transform.position);
            hitSomething = true;
        }
        if (!hitSomething) HUD.ShowMessage("빗나감", 0.5f);
    }
}
