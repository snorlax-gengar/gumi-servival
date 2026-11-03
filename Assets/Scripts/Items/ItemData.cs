using UnityEngine;

public enum ItemType { Food, Drink, Medical, Weapon, Misc }

/// <summary>
/// 아이템 정의. Project 창 우클릭 → Create → Gumi → Item 으로 만든다.
/// 예) 삼각김밥(Food, 0.2kg, hunger 25), 생수(Drink, 0.5kg, thirst 40), 렌치(Weapon, 1.2kg, 데미지 25)
/// </summary>
[CreateAssetMenu(menuName = "Gumi/Item", fileName = "NewItem")]
public class ItemData : ScriptableObject
{
    public string displayName = "아이템";
    public ItemType type = ItemType.Misc;
    [Min(0)] public float weight = 0.5f;   // kg

    [Header("소비 아이템 효과")]
    public float hungerRestore;
    public float thirstRestore;
    public float healthRestore;

    [Header("무기")]
    public float damage = 10f;
    public float attackRange = 1.8f;
    public float attackCooldown = 0.8f;
    public float attackNoise = 8f;
}
