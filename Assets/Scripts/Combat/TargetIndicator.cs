using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 좀보이드식 공격 대상 표시. 마우스 커서 근처의 안드로이드 발밑에 고리를 띄운다.
/// - 흰색(흐림): 겨냥했지만 아직 닿지 않음
/// - 노란색: 지금 휘두르면 맞는 거리
/// - 초록색: 등 뒤라서 기습이 들어감
/// PlayerCombat은 대상이 있으면 그쪽으로 몸을 돌려 휘두른다. PlayerCombat이 자동으로 붙인다.
/// </summary>
public class TargetIndicator : MonoBehaviour
{
    public float cursorRadius = 2.5f;     // 커서에서 이 거리 안의 안드로이드를 겨냥
    public float maxDistance = 14f;       // 플레이어에서 이 거리 넘으면 표시 안 함
    public float ringRadius = 0.85f;

    public AndroidAI Current { get; private set; }
    public bool InRange { get; private set; }
    public bool Ambush { get; private set; }

    PlayerController ctrl;
    PlayerCombat combat;
    PlayerStats stats;
    LineRenderer ring, cursor;
    Material matFar, matNear, matAmbush, matCursor;
    readonly List<AndroidAI> androids = new List<AndroidAI>();
    float refresh;
    const int Segments = 40;

    void Awake()
    {
        ctrl = GetComponent<PlayerController>();
        combat = GetComponent<PlayerCombat>();
        stats = GetComponent<PlayerStats>();
        matFar = Kit.Mat(new Color(0.85f, 0.88f, 0.9f), 0.8f);
        matNear = Kit.Mat(Kit.Hazard, 2.2f);
        matAmbush = Kit.Mat(Kit.Hex("5CD67A"), 2.2f);
        matCursor = Kit.Mat(new Color(0.85f, 0.88f, 0.9f), 0.6f);
        ring = MakeRing("공격 대상 표시", 0.07f);
        cursor = MakeRing("조준점", 0.03f);
    }

    LineRenderer MakeRing(string n, float width)
    {
        var go = new GameObject(n);
        var lr = go.AddComponent<LineRenderer>();
        lr.loop = true;
        lr.positionCount = Segments;
        lr.useWorldSpace = true;
        lr.widthMultiplier = width;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.enabled = false;
        return lr;
    }

    void OnDestroy()
    {
        if (ring) Destroy(ring.gameObject);
        if (cursor) Destroy(cursor.gameObject);
    }

    void Update()
    {
        refresh -= Time.deltaTime;
        if (refresh <= 0f)
        {
            refresh = 0.5f;
            androids.Clear();
            androids.AddRange(FindObjectsByType<AndroidAI>(FindObjectsSortMode.None));
        }

        Current = null; InRange = false; Ambush = false;
        bool dead = stats != null && stats.IsDead;
        if (dead || ctrl == null || !ctrl.MouseGroundPoint(out Vector3 aim))
        {
            ring.enabled = false; cursor.enabled = false;
            return;
        }

        // 커서 가까운 안드로이드 찾기
        float best = cursorRadius;
        foreach (var a in androids)
        {
            if (a == null || a.CurrentState == AndroidAI.State.Disabled) continue;
            Vector3 p = a.transform.position; p.y = aim.y;
            if (Vector3.Distance(a.transform.position, transform.position) > maxDistance) continue;
            float d = Vector3.Distance(p, aim);
            if (d < best) { best = d; Current = a; }
        }

        // 조준점 (작은 고리)
        Draw(cursor, aim + Vector3.up * 0.04f, 0.25f);
        cursor.sharedMaterial = matCursor;
        cursor.enabled = Current == null;

        if (Current == null) { ring.enabled = false; return; }

        Vector3 to = Current.transform.position - transform.position; to.y = 0;
        float range = combat != null ? combat.CurrentRange : 1.5f;
        InRange = to.magnitude <= range + 0.6f;
        Ambush = InRange && Current.CanBeAmbushedFrom(transform.position);

        ring.sharedMaterial = Ambush ? matAmbush : InRange ? matNear : matFar;
        float pulse = 1f + Mathf.Sin(Time.time * (InRange ? 10f : 4f)) * 0.06f;
        Vector3 ground = new Vector3(Current.transform.position.x, transform.position.y + 0.05f, Current.transform.position.z);
        Draw(ring, ground, ringRadius * pulse);
        ring.widthMultiplier = InRange ? 0.09f : 0.05f;
        ring.enabled = true;
    }

    static void Draw(LineRenderer lr, Vector3 center, float r)
    {
        for (int i = 0; i < Segments; i++)
        {
            float a = i / (float)Segments * Mathf.PI * 2f;
            lr.SetPosition(i, center + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r));
        }
    }
}
