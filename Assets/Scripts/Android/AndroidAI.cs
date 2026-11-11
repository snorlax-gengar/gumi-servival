using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 작업형 안드로이드. 상태 머신: 순찰 → (소리) 조사 → (시야) 추적 → 공격 → 놓치면 조사 후 복귀.
/// 느리지만 강하다. 웅크리면 시야 탐지 거리가 줄어든다.
/// NavMeshAgent + Damageable 필요. 씬에 NavMesh를 굽고(Bake) 사용.
/// </summary>
[RequireComponent(typeof(NavMeshAgent), typeof(Damageable))]
public class AndroidAI : MonoBehaviour
{
    public enum State { Patrol, Investigate, Chase, Disabled }

    [Header("순찰")]
    public Transform[] waypoints;
    public float patrolSpeed = 1.5f;
    public float waitAtPoint = 2f;

    [Header("감지")]
    public float viewDistance = 12f;
    public float viewAngle = 100f;
    public float crouchViewMultiplier = 0.5f;
    public float hearingMultiplier = 1f;   // 1보다 크면 귀가 밝음
    public float eyeHeight = 0.7f;         // 피벗(캡슐 중심) 기준 눈 높이
    public LayerMask obstacleMask = ~0;

    [Header("추적/공격")]
    public float chaseSpeed = 3.2f;   // 플레이어 걷기(3.5)보다 약간 느림, 달리면 따돌릴 수 있음
    public float attackRange = 1.15f;    // 수평 거리. 이 안에 들어와야 주먹을 휘두른다
    public float hitReach = 1.45f;       // 주먹이 닿는 순간 이 거리 안에 있어야 맞는다 (뒤로 빠지면 피함)
    public float hitDelay = 0.45f;       // 휘두르기 시작 → 맞는 순간
    public float attackDamage = 18f;
    public float attackCooldown = 1.5f;
    public float loseSightTime = 4f;
    public float investigateTime = 6f;

    [Header("기습")]
    public float ambushAngle = 110f;        // 등 뒤 판정: 정면에서 이 각도 이상 벗어나면 '뒤'
    public float closeSenseWalk = 1.5f;     // 걷거나 뛰어서 다가오면 이 거리에서 기척을 느낌 (웅크리면 없음)
    public Vector2 reactDelay = new Vector2(0.6f, 1.1f); // 소리를 듣고 돌아보기까지 멈칫하는 시간

    [Header("경보")]
    public float alertRadius = 16f;     // 발견 시 주변 안드로이드를 부르는 범위
    public float alertCooldown = 10f;

    public State CurrentState { get; private set; } = State.Patrol;
    public const int AndroidLayer = 31;

    NavMeshAgent agent;
    Damageable body;
    Transform player;
    PlayerStats playerStats;
    PlayerController playerController;

    int wpIndex;
    float waitTimer, attackTimer, lostTimer, investigateTimer, alertTimer;
    float lookYaw;
    float stunTimer, reactTimer;
    Vector3 pendingNoisePos;
    bool hasPendingNoise;

    public bool IsStunned => stunTimer > 0;
    /// <summary>플레이어를 얼마나 알아챘나 (0~1). 1이 되면 추적.</summary>
    public float Awareness { get; private set; }
    /// <summary>뭔가 본 것 같아 멈춰서 살피는 중</summary>
    public bool Noticing { get; private set; }
    Vector3 searchCenter; int searchLeft; float searchWait;
    Vector3? pendingAlert; float pendingAlertTime;
    Flashlight playerLight;
    float pendingHit;
    AndroidVisual visual;

    /// <summary>이 위치에서 공격하면 기습이 되는가 (나를 못 알아챘고, 등 뒤에서)</summary>
    public bool CanBeAmbushedFrom(Vector3 attackerPos)
    {
        if (CurrentState == State.Chase || CurrentState == State.Disabled || IsStunned) return false;
        Vector3 to = attackerPos - transform.position; to.y = 0;
        return Vector3.Angle(transform.forward, to) > ambushAngle;
    }

    /// <summary>기습당해 잠시 멈춤. 끝나면 추적으로 전환.</summary>
    AudioSource hum;

    /// <summary>밀쳐져 비틀거림: 밀린 방향으로 1.5m 정도 밀려나고 잠시 멈춤</summary>
    public void Stagger(Vector3 dir, float seconds)
    {
        if (CurrentState == State.Disabled) return;
        Stun(seconds);
        StartCoroutine(PushBack(dir));
        if (player != null) { lastKnownPos = player.position; }
    }

    System.Collections.IEnumerator PushBack(Vector3 dir)
    {
        float t = 0f;
        while (t < 0.22f && agent.enabled && agent.isOnNavMesh)
        {
            agent.Move(dir * 7f * Time.deltaTime);
            t += Time.deltaTime;
            yield return null;
        }
    }

    public void Stun(float seconds)
    {
        if (seconds >= 1f) Sfx.Play("android_stun", transform.position + Vector3.up, 0.5f, 0.1f, 1f, 20f);
        stunTimer = Mathf.Max(stunTimer, seconds);
        hasPendingNoise = false;
        if (agent.enabled && agent.isOnNavMesh && agent.hasPath) agent.ResetPath();
    }
    Vector3 lastKnownPos, homePos;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        body = GetComponent<Damageable>();
        homePos = transform.position;

        // 개체마다 성능을 조금씩 다르게 → 매번 같은 패턴이 되지 않도록
        patrolSpeed *= Random.Range(0.85f, 1.2f);
        chaseSpeed *= Random.Range(0.92f, 1.1f);
        viewDistance *= Random.Range(0.85f, 1.15f);
        hearingMultiplier *= Random.Range(0.8f, 1.25f);
        waitTimer = Random.Range(0f, waitAtPoint * 2f);
        wpIndex = waypoints != null && waypoints.Length > 0 ? Random.Range(0, waypoints.Length) : 0;
        // 모델을 먼저 만들어야 피격 연출(AndroidFeedback)이 모델의 렌더러를 잡는다
        visual = GetComponent<AndroidVisual>();
        if (visual == null) visual = gameObject.AddComponent<AndroidVisual>();
        if (GetComponent<AndroidFeedback>() == null) gameObject.AddComponent<AndroidFeedback>();

        // 전용 레이어: 미니맵에 그대로 보이지 않게 하고, 카메라 가림 처리에서도 제외
        foreach (var t in GetComponentsInChildren<Transform>(true)) t.gameObject.layer = AndroidLayer;

        var p = GameObject.FindGameObjectWithTag("Player");
        if (p != null)
        {
            player = p.transform;
            playerStats = p.GetComponent<PlayerStats>();
            playerController = p.GetComponent<PlayerController>();
            playerLight = p.GetComponentInChildren<Flashlight>();
        }
    }

    void Start()
    {
        // NavMesh는 LevelNavMesh가 Awake에서 굽기 때문에, 에이전트는 그 뒤(Start)에 켠다
        agent.enabled = true;
        hum = Sfx.Loop("android_hum", transform, 0.18f, 14f);   // 가까이 가면 들리는 낮은 기계음
        if (pendingAlert.HasValue && agent.isOnNavMesh) { var pa = pendingAlert.Value; pendingAlert = null; Alert(pa, pendingAlertTime); }
        if (!agent.isOnNavMesh)
            Debug.LogWarning($"{name}: NavMesh 위에 있지 않습니다. Level 오브젝트에 LevelNavMesh가 있는지 확인하세요.");
    }

    void SetDest(Vector3 pos)
    {
        if (agent.enabled && agent.isOnNavMesh) agent.SetDestination(pos);
    }

    void OnEnable()
    {
        NoiseSystem.OnNoise += HearNoise;
        body.OnHit += OnHit;
        body.OnDeath += OnDeath;
    }

    void OnDisable()
    {
        NoiseSystem.OnNoise -= HearNoise;
        body.OnHit -= OnHit;
        body.OnDeath -= OnDeath;
    }

    void Update()
    {
        if (CurrentState == State.Disabled || !agent.enabled || !agent.isOnNavMesh) return;
        attackTimer -= Time.deltaTime;
        alertTimer -= Time.deltaTime;

        // 휘두른 주먹이 닿는 순간: 그때도 가까이 있어야 맞는다
        if (pendingHit > 0f)
        {
            pendingHit -= Time.deltaTime;
            if (pendingHit <= 0f && stunTimer <= 0 && player != null)
            {
                Vector3 f = player.position - transform.position; f.y = 0;
                if (playerController != null && playerController.IsDodging) HUD.ShowMessage("피했다!", 0.8f);
                else if (f.magnitude <= hitReach) playerStats?.TakeDamage(attackDamage);
            }
        }

        if (stunTimer > 0)
        {
            stunTimer -= Time.deltaTime;
            if (stunTimer <= 0)
            {
                // 정신을 차리면 마지막으로 맞은 방향으로 추적
                CurrentState = State.Chase;
                Awareness = 1f;
                lostTimer = loseSightTime;
                alertTimer = 0;
            }
            return;
        }

        // 소리를 들었으면 잠깐 멈칫한 뒤에 돌아본다 (이 사이에 기습 가능)
        if (hasPendingNoise)
        {
            reactTimer -= Time.deltaTime;
            if (reactTimer <= 0)
            {
                hasPendingNoise = false;
                StartInvestigate(pendingNoisePos);
            }
        }

        bool sees = CanSeePlayer(out float seeDist, out float seeRange);
        // 알아채는 데 시간이 걸린다: 멀수록·웅크릴수록 느리고, 코앞이면 즉시. 그 사이에 시야 밖으로 빠지면 들키지 않는다
        if (CurrentState == State.Chase) Awareness = 1f;
        else if (sees)
        {
            float t = Mathf.Clamp01(seeDist / Mathf.Max(0.1f, seeRange));
            float fill = Mathf.Lerp(0.12f, 1.4f, t);
            if (playerController != null && playerController.IsCrouching) fill *= 1.5f;
            if (playerController != null && playerController.IsRunning) fill *= 0.6f;
            if (CurrentState == State.Investigate) fill *= 0.5f;   // 이미 경계 중이면 빨리 알아챈다
            Awareness = Mathf.Min(1f, Awareness + Time.deltaTime / fill);
            Story.Say("first_seen", "작업형이다. 정면으로 붙으면 진다… 웅크려서(C) 등 뒤로 돌아가거나, 피해 가자.");
        }
        else Awareness = Mathf.Max(0f, Awareness - Time.deltaTime * 0.45f);

        Noticing = sees && Awareness < 1f;
        agent.isStopped = Noticing;
        if (Noticing)
        {
            // "뭐지?" — 멈춰 서서 그쪽으로 천천히 고개를 돌린다
            Vector3 look = player.position - transform.position; look.y = 0;
            if (look.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(look), 100f * Time.deltaTime);
            return;
        }

        sees = sees && Awareness >= 1f;
        if (sees)
        {
            if (CurrentState != State.Chase && alertTimer <= 0)
            {
                // 발견! 경보를 울려 주변 동료를 부른다
                alertTimer = alertCooldown;
                NoiseSystem.Emit(transform.position, alertRadius);
                HUD.ShowMessage("삐— 안드로이드가 경보를 울렸다!", 2f);
                Sfx.Play("android_alert", transform.position + Vector3.up * 1.6f, 1f, 0.05f, 1f, 45f);
            }
            lastKnownPos = player.position;
            lostTimer = loseSightTime;
            CurrentState = State.Chase;
        }

        switch (CurrentState)
        {
            case State.Patrol: TickPatrol(); break;
            case State.Investigate: TickInvestigate(); break;
            case State.Chase: TickChase(sees); break;
        }
    }

    // ---------- 상태 ----------

    void TickPatrol()
    {
        agent.speed = patrolSpeed;
        if (waypoints == null || waypoints.Length == 0)
        {
            if (Vector3.Distance(transform.position, homePos) > 1f) SetDest(homePos);
            return;
        }
        if (agent.pathPending || agent.remainingDistance > 0.5f) return;

        waitTimer -= Time.deltaTime;
        if (waitTimer > 0)
        {
            // 서 있는 동안 주변을 두리번거린다
            Quaternion look = Quaternion.Euler(0, lookYaw, 0);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, 70f * Time.deltaTime);
            if (Quaternion.Angle(transform.rotation, look) < 2f) lookYaw = transform.eulerAngles.y + Random.Range(-120f, 120f);
            return;
        }

        // 다음 목적지는 무작위 (같은 곳 연속 방지), 대기 시간도 들쭉날쭉
        waitTimer = Random.Range(waitAtPoint * 0.4f, waitAtPoint * 2.5f);
        lookYaw = transform.eulerAngles.y + Random.Range(-120f, 120f);
        if (waypoints.Length > 1)
        {
            int next = Random.Range(0, waypoints.Length - 1);
            wpIndex = next >= wpIndex ? next + 1 : next;
        }
        SetDest(waypoints[wpIndex].position);
    }

    void TickInvestigate()
    {
        agent.speed = (patrolSpeed + chaseSpeed) * 0.5f;
        investigateTimer -= Time.deltaTime;
        bool arrived = !agent.pathPending && agent.remainingDistance < 1f;

        if (arrived)
        {
            // 도착하면 좌우를 살피고, 근처 몇 군데를 더 뒤져 본 뒤에야 돌아간다
            searchWait -= Time.deltaTime;
            Quaternion look = Quaternion.Euler(0, lookYaw, 0);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, 110f * Time.deltaTime);
            if (Quaternion.Angle(transform.rotation, look) < 3f) lookYaw = transform.eulerAngles.y + Random.Range(60f, 150f) * (Random.value < 0.5f ? -1f : 1f);
            if (searchWait <= 0f && searchLeft > 0)
            {
                searchLeft--;
                searchWait = Random.Range(1.8f, 3f);
                Vector2 r = Random.insideUnitCircle.normalized * Random.Range(4f, 9f);
                if (NavMesh.SamplePosition(searchCenter + new Vector3(r.x, 0, r.y), out NavMeshHit nh, 3f, NavMesh.AllAreas))
                    SetDest(nh.position);
            }
        }
        if (investigateTimer <= 0) GoPatrol();
    }

    void TickChase(bool sees)
    {
        if (player == null || (playerStats != null && playerStats.IsDead)) { GoPatrol(); return; }

        agent.speed = chaseSpeed;
        // 수평 거리 (안드로이드 중심은 지면 위 1.1m라 3D 거리로 재면 실제보다 멀게 나온다)
        Vector3 flat = player.position - transform.position; flat.y = 0;
        float dist = flat.magnitude;

        // 사람 몸에 파고들지 않게 팔 길이쯤에서 멈춘다
        if (dist < 1.0f && agent.enabled && agent.isOnNavMesh && agent.hasPath) agent.ResetPath();

        if (dist <= attackRange && sees)
        {
            if (agent.hasPath) agent.ResetPath();
            Vector3 look = player.position - transform.position; look.y = 0;
            if (look.sqrMagnitude > 0.01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(look), 420f * Time.deltaTime);

            if (attackTimer <= 0 && Vector3.Angle(transform.forward, look) < 35f)
            {
                attackTimer = attackCooldown;
                pendingHit = hitDelay;
                if (visual != null) visual.PlayAttack();
                NoiseSystem.Emit(transform.position, 6f);
            }
            return;
        }

        SetDest(lastKnownPos);

        if (!sees)
        {
            lostTimer -= Time.deltaTime;
            if (lostTimer <= 0) StartInvestigate(lastKnownPos);
        }
    }

    // ---------- 감지 ----------

    bool CanSeePlayer(out float dist, out float range)
    {
        dist = 0f; range = viewDistance;
        if (player == null || (playerStats != null && playerStats.IsDead)) return false;

        Vector3 eye = transform.position + Vector3.up * eyeHeight;
        Vector3 target = player.position + Vector3.up * 1.0f;
        Vector3 dir = target - eye;
        dist = dir.magnitude;

        range = viewDistance;
        if (playerController != null && playerController.IsCrouching) range *= crouchViewMultiplier;
        // 밤에는 멀리 못 본다. 단, 손전등을 켜고 있으면 오히려 멀리서도 보인다
        var gm = GameManager.Instance;
        if (gm != null && (gm.HourOfDay >= 18.5f || gm.HourOfDay < 6f))
            range *= playerLight != null && playerLight.IsOn ? 1.25f : 0.6f;
        if (dist > range) return false;

        // 바로 옆은 각도와 관계없이 기척을 느낀다. 단, 웅크리고 다가오면 느끼지 못함
        bool crouching = playerController != null && playerController.IsCrouching;
        float closeSense = crouching ? 0f : closeSenseWalk;
        if (dist <= closeSense) Awareness = 1f;   // 코앞의 기척은 즉시
        if (dist > closeSense && Vector3.Angle(transform.forward, new Vector3(dir.x, 0, dir.z)) > viewAngle * 0.5f)
            return false;

        if (Physics.Raycast(eye, dir.normalized, out RaycastHit hit, dist, obstacleMask, QueryTriggerInteraction.Ignore))
            return hit.transform == player || hit.transform.IsChildOf(player);

        return true;
    }

    void HearNoise(Vector3 pos, float radius)
    {
        if (CurrentState == State.Disabled || CurrentState == State.Chase || IsStunned) return;
        if (Vector3.Distance(transform.position, pos) > radius * hearingMultiplier) return;

        if (CurrentState == State.Investigate) { StartInvestigate(pos); return; } // 이미 경계 중이면 즉시
        pendingNoisePos = pos;
        if (!hasPendingNoise) reactTimer = Random.Range(reactDelay.x, reactDelay.y);
        hasPendingNoise = true;
    }

    void OnHit(Vector3 attackerPos)
    {
        if (CurrentState == State.Disabled) return;
        lastKnownPos = attackerPos;
        if (IsStunned) return; // 기습으로 멈춘 동안은 추적 시작을 미룬다
        lostTimer = loseSightTime;
        Awareness = 1f;
        CurrentState = State.Chase;
    }

    void OnDeath()
    {
        CurrentState = State.Disabled;
        Sfx.Play("android_die", transform.position + Vector3.up, 0.9f, 0.1f, 1f, 30f);
        if (hum != null) hum.Stop();
        ScenarioManager.Kills++;
        Story.Say("first_kill", "…멈췄다. 몸통을 열면(E) 쓸 만한 부품이 나온다.");
        if (agent.enabled && agent.isOnNavMesh) agent.isStopped = true;
        agent.enabled = false; // 에이전트가 위치를 다시 끌어올리지 않도록
        if (visual != null && visual.Animated) visual.PlayDeath();   // 쓰러지는 동작
        else
        {
            // 동작이 없는 모델: 옆으로 눕히고 바닥에 붙인다
            transform.rotation = Quaternion.Euler(90f, transform.eulerAngles.y, 0);
            transform.position = new Vector3(transform.position.x, 0.55f, transform.position.z);
        }
        // 분해해서 부품을 얻을 수 있게 된다. 기습(기절 중)으로 쓰러뜨리면 부품이 더 많다
        int parts = Random.Range(4, 9) + (IsStunned ? 3 : 0);
        gameObject.AddComponent<AndroidSalvage>().Init(parts);
        HUD.ShowMessage(IsStunned ? "안드로이드 정지 — 깔끔하게 쓰러뜨렸다 [E] 분해" : "안드로이드 정지 — [E]로 분해해 부품을 얻자", 3f);
    }

    // ---------- 전환 ----------

    void StartInvestigate(Vector3 pos)
    {
        CurrentState = State.Investigate;
        investigateTimer = investigateTime + 8f;
        searchCenter = pos;
        searchLeft = 2;
        searchWait = Random.Range(1.5f, 2.5f);
        lookYaw = transform.eulerAngles.y + Random.Range(-90f, 90f);
        SetDest(pos);
    }

    /// <summary>먼 곳으로 불려 간다 (야간 순찰대 등). 도착해서 주변을 오래 뒤진다.</summary>
    public void Alert(Vector3 pos, float searchSeconds)
    {
        if (CurrentState == State.Disabled || CurrentState == State.Chase) return;
        if (!agent.enabled) { pendingAlert = pos; pendingAlertTime = searchSeconds; return; }   // 아직 켜지기 전: Start에서 처리
        StartInvestigate(pos);
        investigateTimer = searchSeconds;
        searchLeft = 5;
    }

    void GoPatrol()
    {
        CurrentState = State.Patrol;
        waitTimer = 0;
        if (waypoints != null && waypoints.Length > 0) SetDest(waypoints[wpIndex].position);
        else SetDest(homePos);
    }

    // 에디터에서 시야 범위 표시
    void OnDrawGizmosSelected()
    {
        Gizmos.color = CurrentState == State.Chase ? Color.red : CurrentState == State.Investigate ? Color.yellow : Color.cyan;
        Vector3 l = Quaternion.Euler(0, -viewAngle / 2, 0) * transform.forward;
        Vector3 r = Quaternion.Euler(0, viewAngle / 2, 0) * transform.forward;
        Gizmos.DrawRay(transform.position + Vector3.up, l * viewDistance);
        Gizmos.DrawRay(transform.position + Vector3.up, r * viewDistance);
    }
}
