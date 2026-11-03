using System;
using UnityEngine;

/// <summary>안드로이드 등 피해를 받을 수 있는 대상.</summary>
public class Damageable : MonoBehaviour
{
    public float maxHealth = 100f;
    public float Health { get; private set; }
    public bool IsDead => Health <= 0;

    public event Action<Vector3> OnHit;   // 공격자 위치
    public event Action OnDeath;

    void Awake() => Health = maxHealth;

    public void TakeDamage(float amount, Vector3 attackerPos)
    {
        if (IsDead) return;
        Health -= amount;
        OnHit?.Invoke(attackerPos);
        if (Health <= 0) OnDeath?.Invoke();
    }
}
