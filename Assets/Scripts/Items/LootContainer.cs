using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 편의점 선반, 냉장고, 공구함 등 뒤질 수 있는 컨테이너.
/// 고정 아이템(fixedItems) + 랜덤 루팅 테이블(lootTable)에서 시작 시 내용물을 정한다.
/// 뒤질 때 작은 소음이 난다.
/// </summary>
public class LootContainer : MonoBehaviour, IInteractable
{
    [System.Serializable]
    public class LootEntry
    {
        public ItemData item;
        [Range(0, 1)] public float chance = 0.5f;
        public int maxCount = 1;
    }

    public string containerName = "선반";
    public List<ItemData> fixedItems = new List<ItemData>();
    public List<LootEntry> lootTable = new List<LootEntry>();
    public float searchNoise = 3f;

    readonly List<ItemData> contents = new List<ItemData>();
    bool searched;

    void Start()
    {
        contents.AddRange(fixedItems);
        foreach (var e in lootTable)
        {
            if (e.item == null) continue;
            for (int i = 0; i < Mathf.Max(1, e.maxCount); i++)
                if (Random.value < e.chance) contents.Add(e.item);
        }
    }

    public string Prompt => searched && contents.Count == 0
        ? $"{containerName} (비어 있음)"
        : $"[E] {containerName} 뒤지기";

    public void Interact(GameObject who)
    {
        var inv = who.GetComponent<Inventory>();
        if (inv == null) return;

        searched = true;
        NoiseSystem.Emit(transform.position, searchNoise);

        var taken = new List<string>();
        for (int i = contents.Count - 1; i >= 0; i--)
        {
            if (inv.Add(contents[i]))
            {
                taken.Add(contents[i].displayName);
                contents.RemoveAt(i);
            }
        }

        string msg = taken.Count > 0 ? $"획득: {string.Join(", ", taken)}" : "아무것도 없다";
        if (contents.Count > 0) msg += " (너무 무거워서 일부 남김)";
        HUD.ShowMessage(msg);
    }
}
