using System;
using UnityEngine;

/// <summary>
/// 배고픔·갈증·체력·스태미나. 0~100 범위.
/// 배고픔/갈증이 바닥나면 체력이 서서히 깎이고, 체력 0이면 사망.
/// 수치는 게임 내 1시간(GameManager 기준)당 감소량으로 설정한다.
/// </summary>
public class PlayerStats : MonoBehaviour
{
    [Header("현재 수치")]
    [Range(0, 100)] public float health = 100f;
    [Range(0, 100)] public float hunger = 100f;   // 100 = 배부름
    [Range(0, 100)] public float thirst = 100f;   // 100 = 갈증 없음
    [Range(0, 100)] public float stamina = 100f;

    [Header("게임 내 1시간당 감소량")]
    public float hungerPerHour = 4f;    // 약 25시간이면 0
    public float thirstPerHour = 6f;    // 약 16시간이면 0
    public float starvingDamagePerHour = 8f;

    [Header("스태미나 (실시간 초당)")]
    public float staminaDrainRun = 15f;
    public float staminaRegen = 10f;

    [Header("환경 보정 (ScenarioManager가 정함)")]
    public float drainMultiplier = 1f;         // 추위: 배고픔·갈증이 빨리 준다
    public float staminaRegenMultiplier = 1f;
    public float healthRegenPerHour = 0f;      // 발전기 곁: 체력 회복

    public bool IsDead { get; private set; }
    public bool CanRun => stamina > 5f;
    public float SpeedMultiplier => (hunger < 15f || thirst < 15f) ? 0.8f : 1f;

    public event Action OnDeath;

    PlayerController controller;

    void Awake() => controller = GetComponent<PlayerController>();

    void Update()
    {
        if (IsDead) return;

        float hours = GameManager.Instance != null
            ? GameManager.Instance.GameHoursThisFrame
            : Time.deltaTime / 60f; // 매니저 없을 때: 실제 1분 = 게임 1시간

        hunger = Mathf.Max(0, hunger - hungerPerHour * hours * drainMultiplier);
        thirst = Mathf.Max(0, thirst - thirstPerHour * hours * drainMultiplier);
        if (healthRegenPerHour > 0 && hunger > 20 && thirst > 20) health = Mathf.Min(100, health + healthRegenPerHour * hours);

        if (hunger <= 0) TakeDamage(starvingDamagePerHour * hours);
        if (thirst <= 0) TakeDamage(starvingDamagePerHour * 1.5f * hours);

        // 스태미나
        if (controller != null && controller.IsRunning)
            stamina = Mathf.Max(0, stamina - staminaDrainRun * Time.deltaTime);
        else
            stamina = Mathf.Min(100, stamina + staminaRegen * staminaRegenMultiplier * Time.deltaTime);
    }

    public void TakeDamage(float amount)
    {
        if (IsDead) return;
        health = Mathf.Max(0, health - amount);
        if (health <= 0) Die();
    }

    public void Eat(float amount) => hunger = Mathf.Clamp(hunger + amount, 0, 100);
    public void Drink(float amount) => thirst = Mathf.Clamp(thirst + amount, 0, 100);
    public void Heal(float amount) => health = Mathf.Clamp(health + amount, 0, 100);

    void Die()
    {
        IsDead = true;
        OnDeath?.Invoke();
    }
}
