using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 무게 기반 인벤토리. 같은 아이템은 묶어서 개수로 관리.
/// 숫자키 1~9 또는 HUD 클릭으로 사용/장착.
/// </summary>
public class Inventory : MonoBehaviour
{
    [Serializable]
    public class Stack
    {
        public ItemData item;
        public int count;
        public Stack(ItemData item, int count) { this.item = item; this.count = count; }
    }

    public float maxWeight = 12f;   // kg. 나중에 가방으로 늘림
    public List<Stack> stacks = new List<Stack>();

    public ItemData EquippedWeapon { get; private set; }
    public event Action OnChanged;

    PlayerStats stats;

    void Awake() => stats = GetComponent<PlayerStats>();

    public float CurrentWeight
    {
        get
        {
            float w = 0;
            foreach (var s in stacks) w += s.item.weight * s.count;
            return w;
        }
    }

    public bool CanAdd(ItemData item) => CurrentWeight + item.weight <= maxWeight + 0.0001f;

    public bool Add(ItemData item)
    {
        if (item == null || !CanAdd(item)) return false;
        var s = stacks.Find(x => x.item == item);
        if (s != null) s.count++;
        else stacks.Add(new Stack(item, 1));
        OnChanged?.Invoke();
        return true;
    }

    public void RemoveOne(ItemData item)
    {
        var s = stacks.Find(x => x.item == item);
        if (s == null) return;
        s.count--;
        if (s.count <= 0)
        {
            stacks.Remove(s);
            if (EquippedWeapon == item) EquippedWeapon = null;
        }
        OnChanged?.Invoke();
    }

    /// <summary>소비 아이템은 먹고, 무기는 장착/해제.</summary>
    public void Use(int index)
    {
        if (index < 0 || index >= stacks.Count || stats == null || stats.IsDead) return;
        ItemData item = stacks[index].item;

        switch (item.type)
        {
            case ItemType.Weapon:
                EquippedWeapon = EquippedWeapon == item ? null : item;
                OnChanged?.Invoke();
                break;
            case ItemType.Food:
            case ItemType.Drink:
            case ItemType.Medical:
                stats.Eat(item.hungerRestore);
                stats.Drink(item.thirstRestore);
                stats.Heal(item.healthRestore);
                RemoveOne(item);
                break;
        }
    }

    void Update()
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return;
        for (int i = 0; i < 9; i++)
        {
            if (kb[UnityEngine.InputSystem.Key.Digit1 + i].wasPressedThisFrame) Use(i);
        }
    }
}
