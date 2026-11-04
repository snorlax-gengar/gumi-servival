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
    public float attackRange = 1.6f;
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

    /// <summary>이 위치에서 공격하면 기습이 되는가 (나를 못 알아챘고, 등 뒤에서)</summary>
    public bool CanBeAmbushedFrom(Vector3 attackerPos)
    {
        if (CurrentState == State.Chase || CurrentState == State.Disabled || IsStunned) return false;
        Vector3 to = attackerPos - transform.position; to.y = 0;
        return Vector3.Angle(transform.forward, to) > ambushAngle;
    }

    /// <summary>기습당해 잠시 멈춤. 끝나면 추적으로 전환.</summary>
    public void Stun(float seconds)
    {
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
        if (GetComponent<AndroidVisual>() == null) gameObject.AddComponent<AndroidVisual>();
        if (GetComponent<AndroidFeedback>() == null) gameObject.AddComponent<AndroidFeedback>();

        // 전용 레이어: 미니맵에 그대로 보이지 않게 하고, 카메라 가림 처리에서도 제외
        foreach (var t in GetComponentsInChildren<Transform>(true)) t.gameObject.layer = AndroidLayer;

        var p = GameObject.FindGameObjectWithTag("Player");
        if (p != null)
        {
            player = p.transform;
            playerStats = p.GetComponent<PlayerStats>();
            playerController = p.GetComponent<PlayerController>();
        }
    }

    void Start()
    {
        // NavMesh는 LevelNavMesh가 Awake에서 굽기 때문에, 에이전트는 그 뒤(Start)에 켠다
        agent.enabled = true;
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

        if (stunTimer > 0)
        {
            stunTimer -= Time.deltaTime;
            if (stunTimer <= 0)
            {
                // 정신을 차리면 마지막으로 맞은 방향으로 추적
                CurrentState = State.Chase;
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

        bool sees = CanSeePlayer();
        if (sees)
        {
            if (CurrentState != State.Chase && alertTimer <= 0)
            {
                // 발견! 경보를 울려 주변 동료를 부른다
                alertTimer = alertCooldown;
                NoiseSystem.Emit(transform.position, alertRadius);
                HUD.ShowMessage("삐— 안드로이드가 경보를 울렸다!", 2f);
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
            // 도착하면 제자리에서 두리번거림
            transform.Rotate(0, 90f * Time.deltaTime, 0);
        }
        if (investigateTimer <= 0) GoPatrol();
    }

    void TickChase(bool sees)
    {
        if (player == null || (playerStats != null && playerStats.IsDead)) { GoPatrol(); return; }

        agent.speed = chaseSpeed;
        float dist = Vector3.Distance(transform.position, player.position);

        if (dist <= attackRange && sees)
        {
            if (agent.hasPath) agent.ResetPath();
            Vector3 look = player.position - transform.position; look.y = 0;
            if (look.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(look);

            if (attackTimer <= 0)
            {
                attackTimer = attackCooldown;
                playerStats?.TakeDamage(attackDamage);
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

    bool CanSeePlayer()
    {
        if (player == null || (playerStats != null && playerStats.IsDead)) return false;

        Vector3 eye = transform.position + Vector3.up * eyeHeight;
        Vector3 target = player.position + Vector3.up * 1.0f;
        Vector3 dir = target - eye;
        float dist = dir.magnitude;

        float range = viewDistance;
        if (playerController != null && playerController.IsCrouching) range *= crouchViewMultiplier;
        if (dist > range) return false;

        // 바로 옆은 각도와 관계없이 기척을 느낀다. 단, 웅크리고 다가오면 느끼지 못함
        bool crouching = playerController != null && playerController.IsCrouching;
        float closeSense = crouching ? 0f : closeSenseWalk;
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
        CurrentState = State.Chase;
    }

    void OnDeath()
    {
        CurrentState = State.Disabled;
        if (agent.enabled && agent.isOnNavMesh) agent.isStopped = true;
        agent.enabled = false; // 에이전트가 위치를 다시 끌어올리지 않도록
        // 쓰러짐: 옆으로 눕히고 바닥에 붙인다
        transform.rotation = Quaternion.Euler(90f, transform.eulerAngles.y, 0);
        transform.position = new Vector3(transform.position.x, 0.55f, transform.position.z);
        HUD.ShowMessage("안드로이드 정지");
    }

    // ---------- 전환 ----------

    void StartInvestigate(Vector3 pos)
    {
        CurrentState = State.Investigate;
        investigateTimer = investigateTime;
        SetDest(pos);
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
