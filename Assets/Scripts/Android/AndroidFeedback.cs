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

    public float ambushHintRange = 3f;

    Damageable body;
    NavMeshAgent agent;
    AndroidAI ai;
    Transform player;
    PlayerController playerCtrl;
    GUIStyle hintStyle;
    Renderer[] renderers;
    MaterialPropertyBlock mpb;
    Color[][] baseColors;   // [렌더러][재질 번호] — 재질이 여러 개인 모델도 각자 색을 지킨다
    float flashTimer, barTimer;
    Vector3 pushDir;
    float pushTimer;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    void Awake()
    {
        body = GetComponent<Damageable>();
        agent = GetComponent<NavMeshAgent>();
        ai = GetComponent<AndroidAI>();
        var p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) { player = p.transform; playerCtrl = p.GetComponent<PlayerController>(); }
        renderers = GetComponentsInChildren<Renderer>();
        mpb = new MaterialPropertyBlock();
        baseColors = new Color[renderers.Length][];
        for (int i = 0; i < renderers.Length; i++)
        {
            var mats = renderers[i].sharedMaterials;
            baseColors[i] = new Color[mats.Length];
            for (int k = 0; k < mats.Length; k++)
                baseColors[i][k] = mats[k] != null && mats[k].HasProperty(BaseColorId) ? mats[k].GetColor(BaseColorId) : Color.white;
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
        SetColor(c => c * 0.35f);
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
            if (flashTimer <= 0) ClearColor();
            else SetColor(c => Color.Lerp(c, new Color(1f, 0.2f, 0.15f), 0.8f));
        }
    }

    /// <summary>원래 색에서 바꾼 색을 재질마다 따로 적용</summary>
    void SetColor(System.Func<Color, Color> change)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            for (int k = 0; k < baseColors[i].Length; k++)
            {
                mpb.Clear();
                mpb.SetColor(BaseColorId, change(baseColors[i][k]));
                renderers[i].SetPropertyBlock(mpb, k);
            }
        }
    }

    void ClearColor()
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            for (int k = 0; k < baseColors[i].Length; k++) renderers[i].SetPropertyBlock(null, k);
        }
    }

    float chaseMark; AndroidAI.State lastState;

    void OnGUI()
    {
        if (body.IsDead) return;
        if (Event.current.type == EventType.Repaint && ai != null)
        {
            if (ai.CurrentState == AndroidAI.State.Chase && lastState != AndroidAI.State.Chase) chaseMark = 1.2f;
            lastState = ai.CurrentState;
            chaseMark -= Time.unscaledDeltaTime;
        }
        Camera cam = Camera.main;
        if (cam == null) return;

        // 머리 위 상태 표시: 기절 / 기습 가능
        string hint = null;
        Color hintColor = Color.white;
        if (ai != null && ai.IsStunned) { hint = "기절"; hintColor = new Color(1f, 0.85f, 0.3f); }
        else if (ai != null && ai.CurrentState != AndroidAI.State.Chase && ai.Awareness > 0.05f)
        {
            // 알아채는 중: 물음표가 차오르다가 느낌표가 되면 들킨 것
            int dots = 1 + Mathf.Min(2, (int)(ai.Awareness * 3f));
            hint = new string('?', dots);
            hintColor = Color.Lerp(new Color(1f, 0.95f, 0.5f), new Color(1f, 0.45f, 0.2f), ai.Awareness);
        }
        else if (ai != null && ai.CurrentState == AndroidAI.State.Chase && chaseMark > 0f) { hint = "!"; hintColor = new Color(1f, 0.25f, 0.2f); }
        else if (ai != null && player != null
                 && Vector3.Distance(player.position, transform.position) < ambushHintRange
                 && ai.CanBeAmbushedFrom(player.position))
        {
            bool crouch = playerCtrl != null && playerCtrl.IsCrouching;
            hint = crouch ? "◆ 급소 노출" : "◆ 기습 가능";
            hintColor = crouch ? new Color(0.4f, 1f, 0.5f) : new Color(0.75f, 1f, 0.75f);
        }
        if (hint == null && barTimer <= 0) return;

        Vector3 sp = cam.WorldToScreenPoint(transform.position + Vector3.up * 1.6f);
        if (sp.z < 0) return;
        UIScale.Apply();
        Vector2 gp = UIScale.FromScreen(sp);

        if (hint != null)
        {
            if (hintStyle == null)
            {
                hintStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 14 };
            }
            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.Label(new Rect(gp.x - 59, gp.y - 33, 120, 22), hint, hintStyle);
            GUI.color = hintColor;
            GUI.Label(new Rect(gp.x - 60, gp.y - 34, 120, 22), hint, hintStyle);
            GUI.color = Color.white;
        }
        if (barTimer <= 0) return;

        const float w = 60, h = 7;
        var r = new Rect(gp.x - w / 2, gp.y - h, w, h);
        GUI.color = new Color(0, 0, 0, 0.6f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = new Color(0.9f, 0.2f, 0.2f);
        GUI.DrawTexture(new Rect(r.x, r.y, w * Mathf.Clamp01(body.Health / body.maxHealth), h), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
}
