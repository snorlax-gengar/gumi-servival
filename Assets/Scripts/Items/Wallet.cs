using System;
using UnityEngine;

/// <summary>
/// 재화: 안드로이드 부품. 쓰러뜨린 안드로이드를 분해해서 얻는다.
/// 나중에 제작·거래·발전기 수리에 쓴다. PlayerController가 자동으로 붙인다.
/// </summary>
public class Wallet : MonoBehaviour
{
    public int Parts { get; private set; }
    public int TotalEarned { get; private set; }

    public event Action<int> OnChanged;

    public void Add(int amount)
    {
        if (amount <= 0) return;
        Parts += amount;
        TotalEarned += amount;
        OnChanged?.Invoke(Parts);
    }

    public bool Spend(int amount)
    {
        if (amount < 0 || Parts < amount) return false;
        Parts -= amount;
        OnChanged?.Invoke(Parts);
        return true;
    }
}
