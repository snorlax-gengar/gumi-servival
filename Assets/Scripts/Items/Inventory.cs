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

    public bool Has(ItemData item) => item != null && stacks.Exists(x => x.item == item && x.count > 0);

    public bool CanAdd(ItemData item) => CurrentWeight + item.weight <= maxWeight + 0.0001f;

    public bool Add(ItemData item)
    {
        if (item == null || !CanAdd(item)) return false;
        var s = stacks.Find(x => x.item == item);
        if (s != null) s.count++;
        else stacks.Add(new Stack(item, 1));

        // 맨손일 때 무기를 주우면 바로 장착
        if (item.type == ItemType.Weapon && EquippedWeapon == null)
        {
            EquippedWeapon = item;
            HUD.ShowMessage($"{item.displayName} 장착! (좌클릭으로 휘두르기)", 3f);
        }
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
                Sfx.Play("equip", transform.position, 0.7f, 0.06f, 0f);
                break;
            case ItemType.Food:
            case ItemType.Drink:
            case ItemType.Medical:
                stats.Eat(item.hungerRestore);
                stats.Drink(item.thirstRestore);
                stats.Heal(item.healthRestore);
                RemoveOne(item);
                Sfx.Play("eat", transform.position, 0.6f, 0.1f, 0f);
                HUD.ShowMessage($"{item.displayName} 사용", 1.2f);
                break;
        }
    }

    /// <summary>Tab으로 연 가방 (커서가 풀려 클릭으로 쓰기·장착)</summary>
    public static bool Open { get; private set; }

    void Update()
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null || PauseMenu.IsPaused) return;
        for (int i = 0; i < 9; i++)
        {
            if (kb[UnityEngine.InputSystem.Key.Digit1 + i].wasPressedThisFrame) Use(i);
        }
        if (kb.tabKey.wasPressedThisFrame) Open = !Open;

        // H: 체력 회복 아이템 바로 쓰기
        if (kb.hKey.wasPressedThisFrame)
        {
            int idx = stacks.FindIndex(s => s.item.type == ItemType.Medical);
            if (idx >= 0) Use(idx); else HUD.ShowMessage("붕대가 없다", 1.2f);
        }
        // 마우스 휠: 무기 바꾸기 (맨손 포함 순환)
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse != null && IsometricCamera.MouseLook && !MinimapHUD.PointerOverMap && !MinimapHUD.BigMapOpen)
        {
            float sc = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(sc) > 0.01f) CycleWeapon(sc > 0 ? -1 : 1);
        }
    }

    void CycleWeapon(int dir)
    {
        var weapons = stacks.FindAll(s => s.item.type == ItemType.Weapon);
        if (weapons.Count == 0) return;
        int cur = weapons.FindIndex(s => s.item == EquippedWeapon);   // -1 = 맨손
        int n = weapons.Count + 1;
        int next = ((cur + 1 + dir) % n + n) % n - 1;
        EquippedWeapon = next < 0 ? null : weapons[next].item;
        OnChanged?.Invoke();
        Sfx.Play("equip", transform.position, 0.6f, 0.06f, 0f);
        HUD.ShowMessage(EquippedWeapon != null ? $"{EquippedWeapon.displayName} 들기" : "맨손", 1f);
    }
}
