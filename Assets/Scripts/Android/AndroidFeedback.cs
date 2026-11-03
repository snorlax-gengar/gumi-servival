using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 안드로이드 피격 연출: 빨갛게 번쩍임, 뒤로 밀림, 머리 위 체력 바.
/// 쓰러지면 어둡게 변한다. AndroidAI가 자동으로 붙인다.
/// </summary>
[RequireComponent(typeof(Damageable))]
public class AndroidFeedback : MonoBehaviour
{
    public float flashTime = 0.15f;
    public float knockback = 0.8f;
    public float barShowTime = 4f;

    Damageable body;
    NavMeshAgent agent;
    Renderer[] renderers;
    MaterialPropertyBlock mpb;
    Color[] baseColors;
    float flashTimer, barTimer;
    Vector3 pushDir;
    float pushTimer;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    void Awake()
    {
        body = GetComponent<Damageable>();
        agent = GetComponent<NavMeshAgent>();
        renderers = GetComponentsInChildren<Renderer>();
        mpb = new MaterialPropertyBlock();
        baseColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            var m = renderers[i].sharedMaterial;
            baseColors[i] = m != null && m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId) : Color.white;
        }
    }

    void OnEnable()
    {
        body.OnHit += OnHit;
        body.OnDeath += OnDeath;
    }

    void OnDisable()
    {
        body.OnHit -= OnHit;
        body.OnDeath -= OnDeath;
    }

    void OnHit(Vector3 attackerPos)
    {
        flashTimer = flashTime;
        barTimer = barShowTime;
        pushDir = transform.position - attackerPos;
        pushDir.y = 0;
        pushDir = pushDir.sqrMagnitude > 0.001f ? pushDir.normalized : -transform.forward;
        pushTimer = 0.12f;
    }

    void OnDeath()
    {
        flashTimer = 0;
        barTimer = 0;
        SetColor(i => baseColors[i] * 0.35f);
    }

    void Update()
    {
        if (pushTimer > 0 && agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.Move(pushDir * (knockback / 0.12f) * Time.deltaTime);
            pushTimer -= Time.deltaTime;
        }

        if (body.IsDead) return;
        if (barTimer > 0) barTimer -= Time.deltaTime;

        if (flashTimer > 0)
        {
            flashTimer -= Time.deltaTime;
            if (flashTimer <= 0) SetColor(i => baseColors[i]);
            else SetColor(i => Color.Lerp(baseColors[i], new Color(1f, 0.2f, 0.15f), 0.8f));
        }
    }

    void SetColor(System.Func<int, Color> pick)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, pick(i));
            renderers[i].SetPropertyBlock(mpb);
        }
    }

    void OnGUI()
    {
        if (body.IsDead || barTimer <= 0) return;
        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 sp = cam.WorldToScreenPoint(transform.position + Vector3.up * 1.6f);
        if (sp.z < 0) return;
        const float w = 60, h = 7;
        var r = new Rect(sp.x - w / 2, Screen.height - sp.y - h, w, h);
        GUI.color = new Color(0, 0, 0, 0.6f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = new Color(0.9f, 0.2f, 0.2f);
        GUI.DrawTexture(new Rect(r.x, r.y, w * Mathf.Clamp01(body.Health / body.maxHealth), h), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
}
