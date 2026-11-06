using UnityEngine;

/// <summary>
/// 쓰러진 안드로이드. E로 분해하면 부품(재화)을 얻는다.
/// 분해는 금속 소리를 내서 주변 안드로이드가 올 수 있다.
/// 기습으로 쓰러뜨리면 부품이 덜 상해서 더 많이 나온다.
/// 오래 방치하면(실제 5분) 사라진다.
/// </summary>
public class AndroidSalvage : MonoBehaviour, IInteractable
{
    public int parts = 5;
    public float noise = 7f;
    public float despawnSeconds = 300f;

    bool done;
    GameObject marker;
    Vector3 basePos;

    void Update()
    {
        // 쓰러진 자리 위에 주황 마름모가 떠서 돈다
        if (marker == null) return;
        marker.transform.position = basePos + Vector3.up * Mathf.Sin(Time.time * 2.5f) * 0.12f;
        marker.transform.rotation = Quaternion.Euler(45f, Time.time * 90f, 45f);
    }

    public void Init(int amount)
    {
        parts = amount;
        // 뒤질 수 있는 것 = 주황. 쓰러진 자리 위에 주황 표시를 띄운다
        marker = Kit.Box(transform, Vector3.zero, Vector3.one * 0.3f, Kit.Loot, emission: 1.8f, name: "분해 표시").gameObject;
        marker.transform.localScale = Vector3.one * (0.3f / Mathf.Max(0.01f, transform.lossyScale.x));
        basePos = new Vector3(transform.position.x, 1.2f, transform.position.z);
        Destroy(gameObject, despawnSeconds);
    }

    public string Prompt => done ? "" : $"[E] 안드로이드 분해 (부품 {parts}개)";

    public void Interact(GameObject who)
    {
        if (done) return;
        var wallet = who.GetComponent<Wallet>();
        if (wallet == null) return;

        done = true;
        wallet.Add(parts);
        NoiseSystem.Emit(transform.position, noise);
        HUD.ShowMessage($"부품 +{parts}  (보유 {wallet.Parts}개)", 2.5f);

        if (marker != null) Destroy(marker);
        // 분해되고 남은 껍데기는 잠시 뒤 사라진다
        foreach (var c in GetComponents<Collider>()) c.enabled = false;
        Destroy(gameObject, 1.5f);
    }
}
