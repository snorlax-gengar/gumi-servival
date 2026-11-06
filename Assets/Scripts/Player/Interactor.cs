using UnityEngine;

public interface IInteractable
{
    string Prompt { get; }
    void Interact(GameObject who);
}

/// <summary>
/// 주변의 상호작용 가능한 오브젝트 중 가장 가까운 것을 찾아 F키로 상호작용.
/// 컨테이너에는 Collider가 있어야 한다.
/// </summary>
public class Interactor : MonoBehaviour
{
    public float radius = 2f;
    public IInteractable Current { get; private set; }

    readonly Collider[] hits = new Collider[16];
    PlayerStats stats;

    void Awake() => stats = GetComponent<PlayerStats>();

    void Update()
    {
        Current = FindNearest();
        if (stats != null && stats.IsDead) return;

        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (PauseMenu.IsPaused) return;
        if (Current != null && kb != null && (kb.eKey.wasPressedThisFrame || kb.fKey.wasPressedThisFrame))
        {
            Current.Interact(gameObject);
            var mb = Current as MonoBehaviour;
            Sfx.Play(Current is LootContainer ? "loot" : "door", mb != null ? mb.transform.position : transform.position, 0.8f, 0.08f, 0.4f);
        }
    }

    IInteractable FindNearest()
    {
        int n = Physics.OverlapSphereNonAlloc(transform.position, radius, hits, ~0, QueryTriggerInteraction.Collide);
        IInteractable best = null;
        float bestDist = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            var it = hits[i].GetComponentInParent<IInteractable>();
            if (it == null) continue;
            float d = (hits[i].transform.position - transform.position).sqrMagnitude;
            if (d < bestDist) { bestDist = d; best = it; }
        }
        return best;
    }
}
