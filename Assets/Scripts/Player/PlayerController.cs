using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// WASD 이동(카메라 기준), Shift 달리기, Ctrl 웅크리기. 마우스 방향을 바라봄.
/// 움직일 때 상태에 따라 다른 크기의 소음을 낸다 (웅크리기 &lt; 걷기 &lt; 달리기).
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("이동")]
    public float walkSpeed = 3.5f;
    public float runSpeed = 6.5f;
    public float crouchSpeed = 1.8f;
    public float gravity = -20f;

    [Header("소음 반경 (m)")]
    public float walkNoise = 6f;
    public float runNoise = 14f;
    public float sneakNoise = 1.2f;   // 웅크려 걷기 (안드로이드 등 뒤로 다가갈 수 있을 만큼 조용)
    public float noiseInterval = 0.4f;

    [Header("참조")]
    public Transform cameraTransform;

    public bool IsRunning { get; private set; }
    public bool IsCrouching { get; private set; }
    public bool IsMoving { get; private set; }

    CharacterController cc;
    PlayerStats stats;
    float verticalVel;
    float noiseTimer;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        stats = GetComponent<PlayerStats>();
        if (GetComponent<PlayerVisual>() == null) gameObject.AddComponent<PlayerVisual>();
        if (GetComponent<Wallet>() == null) gameObject.AddComponent<Wallet>();
        if (GetComponent<Flashlight>() == null) gameObject.AddComponent<Flashlight>();
        if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
    }

    [Header("점프 · 넘기")]
    public float jumpSpeed = 4.6f;          // 약 1.1m
    public float jumpStamina = 8f, vaultStamina = 6f;
    public float vaultMaxHeight = 1.35f;    // 이보다 낮은 장애물은 넘어간다 (차, 방호벽, 화분, 낮은 담)
    public float vaultTime = 0.6f;
    bool vaulting;
    bool crouchToggle, wasGrounded = true;
    float stepTimer;
    float vaultT;
    Vector3 vaultFrom, vaultTo;
    float vaultPeak;
    /// <summary>넘는 중 (애니메이션·소리용)</summary>
    public bool IsVaulting => vaulting;
    public bool IsAirborne => !cc.isGrounded && !vaulting;

    /// <summary>
    /// Space: 앞에 허리 높이 장애물이 있으면 넘어가고, 없으면 점프.
    /// 넘기 판정: 무릎 높이에서 앞이 막혔고, 머리 높이는 뚫려 있고, 장애물 너머에 설 자리가 있으면.
    /// </summary>
    [Tooltip("이 높이까지는 손을 짚고 기어오른다 (차 지붕, 담, 컨테이너, 낮은 지붕)")]
    public float climbMaxHeight = 2.0f;     // 차 지붕·담장까지. 건물 지붕·차양은 높이와 상관없이 못 오른다
    public float climbStamina = 14f;
    /// <summary>넘기·오르기 시작 (종류 0 넘기, 1 기어오르기, 2 점프, 길이 초). 애니메이션이 듣는다.</summary>
    public event System.Action<int, float> OnTraverse;
    int vaultKind;
    float vaultDuration;

    /// <summary>
    /// Space 판정:
    ///  · 허리 높이(0.35~1.35m)이고 너머에 설 자리가 있으면 → 훌쩍 넘기
    ///  · 1.35~2.6m 이고 위에 설 자리가 있으면 → 손을 짚고 기어올라 그 위에 선다
    ///  · 아니면 점프
    /// </summary>
    bool TryVault()
    {
        Vector3 fwd = transform.forward;
        int mask = ~((1 << 31) | (1 << 30));
        Vector3 o = transform.position;
        if (!Physics.Raycast(o + Vector3.up * 0.45f, fwd, out RaycastHit low, 1.2f, mask, QueryTriggerInteraction.Ignore)
            && !Physics.Raycast(o + Vector3.up * 1.2f, fwd, out low, 1.0f, mask, QueryTriggerInteraction.Ignore)) return false;
        if (Physics.Raycast(o + Vector3.up * (climbMaxHeight + 0.4f), fwd, low.distance + 0.9f, mask, QueryTriggerInteraction.Ignore)) return false;  // 너무 높음
        // 장애물 윗면 높이
        Vector3 over = o + fwd * (low.distance + 0.3f) + Vector3.up * (climbMaxHeight + 0.35f);
        if (!Physics.Raycast(over, Vector3.down, out RaycastHit top, climbMaxHeight + 0.5f, mask, QueryTriggerInteraction.Ignore)) return false;
        float topY = top.point.y - o.y;
        if (topY < 0.35f || topY > climbMaxHeight) return false;
        float r = cc.radius * 0.9f;

        if (topY <= vaultMaxHeight)
        {
            // 너머 착지점: 장애물 두께를 몰라서 0.7~2.6m 앞을 차례로 본다
            for (float ahead = low.distance + 0.7f; ahead <= low.distance + 2.6f; ahead += 0.3f)
            {
                Vector3 land = o + fwd * ahead;
                if (!Physics.Raycast(land + Vector3.up * (topY + 0.3f), Vector3.down, out RaycastHit g, topY + 1.2f, mask, QueryTriggerInteraction.Ignore)) continue;
                if (g.point.y - o.y > 0.25f) continue;   // 아직 장애물 위
                Vector3 p = g.point;
                if (Physics.CheckCapsule(p + Vector3.up * (cc.radius + 0.05f), p + Vector3.up * (1.7f - cc.radius), r, mask, QueryTriggerInteraction.Ignore)) continue;
                // 달리면서 넘으면 허들처럼 낮고 빠르게, 서서 넘으면 손 짚고 넘기
                if (IsRunning) StartTraverse(0, o, p, topY + 0.2f, vaultTime * 0.75f, vaultStamina);
                else StartTraverse(0, o, p, topY + 0.35f, vaultTime, vaultStamina);
                return true;
            }
        }
        else
        {
            // 담 넘기: 높은 담(1.35~2.0m)이 얇으면 손 짚고 몸을 넘겨 반대편에 내려선다
            for (float ahead = low.distance + 0.5f; ahead <= low.distance + 1.4f; ahead += 0.15f)
            {
                Vector3 land = o + fwd * ahead;
                if (!Physics.Raycast(land + Vector3.up * (topY + 0.3f), Vector3.down, out RaycastHit g, topY + 1.5f, mask, QueryTriggerInteraction.Ignore)) continue;
                if (Mathf.Abs(g.point.y - o.y) > 0.6f) continue;   // 아직 담 위이거나 반대편이 너무 낮음
                Vector3 p = g.point;
                if (Physics.CheckCapsule(p + Vector3.up * (cc.radius + 0.05f), p + Vector3.up * (1.7f - cc.radius), r, mask, QueryTriggerInteraction.Ignore)) continue;
                if (stats != null && stats.stamina < climbStamina) { HUD.ShowMessage("담을 넘기엔 너무 지쳤다", 1.2f); return true; }
                StartTraverse(4, o, p, topY + 0.25f, 0.85f + topY * 0.2f, climbStamina);
                return true;
            }
        }
        // 건물(지붕·차양·진열대) 위로는 오르지 않는다 → 옥상으로 새는 버그 방지
        if (CityGenerator.BuildingRoot != null && top.collider != null && top.collider.transform.IsChildOf(CityGenerator.BuildingRoot)) return false;
        // 위에 올라설 자리 (넘을 수 없는 두꺼운 것, 또는 높은 것)
        Vector3 stand = top.point + fwd * 0.35f;
        if (Physics.Raycast(stand + Vector3.up * 0.6f, Vector3.down, out RaycastHit st, 1.0f, mask, QueryTriggerInteraction.Ignore)
            && Mathf.Abs(st.point.y - top.point.y) < 0.3f
            && !Physics.CheckCapsule(st.point + Vector3.up * (cc.radius + 0.08f), st.point + Vector3.up * (1.7f - cc.radius), r, mask, QueryTriggerInteraction.Ignore))
        {
            if (stats != null && stats.stamina < climbStamina) { HUD.ShowMessage("기어오르기엔 너무 지쳤다", 1.2f); return true; }
            // 낮은 것(차 보닛·평상)은 한 발 딛고 올라서기, 높은 것(담장)은 손으로 매달려 끌어올리기
            bool step = topY <= vaultMaxHeight;
            float dur = step ? 0.45f + topY * 0.2f : 0.7f + topY * 0.3f;
            StartTraverse(step ? 3 : 1, o, st.point, 0f, dur, step ? vaultStamina : climbStamina);
            return true;
        }
        return false;
    }

    void StartTraverse(int kind, Vector3 from, Vector3 to, float peak, float duration, float stamina)
    {
        vaulting = true;
        vaultKind = kind;
        vaultT = 0f;
        vaultFrom = from;
        vaultTo = to;
        vaultPeak = peak;
        vaultDuration = duration;
        cc.enabled = false;
        if (stats != null) stats.stamina -= stamina;
        NoiseSystem.Emit(from, walkNoise);
        OnTraverse?.Invoke(kind, duration);
    }

    void UpdateVault()
    {
        vaultT += Time.deltaTime / Mathf.Max(0.05f, vaultDuration);
        float t = Mathf.Clamp01(vaultT);
        Vector3 p;
        if (vaultKind == 1 || vaultKind == 3)
        {
            // 기어오르기: 먼저 손을 짚고 몸을 끌어올리고(65%), 그다음 무릎을 올려 앞으로(35%)
            float up = Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / 0.65f));
            float fw = Mathf.SmoothStep(0, 1, Mathf.Clamp01((t - 0.45f) / 0.55f));
            p = new Vector3(Mathf.Lerp(vaultFrom.x, vaultTo.x, fw), Mathf.Lerp(vaultFrom.y, vaultTo.y + 0.05f, up), Mathf.Lerp(vaultFrom.z, vaultTo.z, fw));
        }
        else if (vaultKind == 4)
        {
            // 담 넘기: 손 짚고 몸을 끌어올림(0~40%) → 담 위로 넘김(40~60%) → 반대편에 내려섬(60~100%)
            float fw = Mathf.SmoothStep(0, 1, Mathf.Clamp01((t - 0.15f) / 0.7f));
            float topAbs = vaultFrom.y + vaultPeak;
            float y = t < 0.4f ? Mathf.Lerp(vaultFrom.y, topAbs, Mathf.SmoothStep(0, 1, t / 0.4f))
                    : t < 0.6f ? topAbs
                    : Mathf.Lerp(topAbs, vaultTo.y, Mathf.Pow((t - 0.6f) / 0.4f, 1.6f));
            p = new Vector3(Mathf.Lerp(vaultFrom.x, vaultTo.x, fw), y, Mathf.Lerp(vaultFrom.z, vaultTo.z, fw));
        }
        else
        {
            p = Vector3.Lerp(vaultFrom, vaultTo, Mathf.SmoothStep(0, 1, t));
            p.y = Mathf.Lerp(vaultFrom.y, vaultTo.y, t) + Mathf.Sin(t * Mathf.PI) * vaultPeak;
        }
        transform.position = p;
        if (t >= 1f)
        {
            vaulting = false;
            cc.enabled = true;
            verticalVel = -2f;
        }
    }

    void Update()
    {
        if (stats != null && stats.IsDead) return;
        var kb = Keyboard.current;
        if (kb == null) return;
        if (vaulting) { UpdateVault(); return; }
        if (PauseMenu.IsPaused) return;

        // 점프 / 넘기 (전체 지도가 열려 있으면 Space는 지도용)
        if (kb.spaceKey.wasPressedThisFrame && !MinimapHUD.BigMapOpen && cc.isGrounded && attackLock <= 0f
            && (ScenarioManager.Instance == null || !ScenarioManager.Instance.Won || Time.timeScale > 0f))
        {
            bool tired = stats != null && stats.stamina < jumpStamina;
            if (!TryVault() && !tired)
            {
                verticalVel = jumpSpeed;
                if (stats != null) stats.stamina -= jumpStamina;
                OnTraverse?.Invoke(2, 0.5f);
                Sfx.Play("jump", transform.position, 0.5f, 0.1f, 0f);
            }
            else if (vaulting) Sfx.Play("jump", transform.position, 0.7f, 0.1f, 0f);
            if (vaulting) return;
        }

        // 입력
        Vector2 input = Vector2.zero;
        if (kb.wKey.isPressed) input.y += 1;
        if (kb.sKey.isPressed) input.y -= 1;
        if (kb.dKey.isPressed) input.x += 1;
        if (kb.aKey.isPressed) input.x -= 1;
        input = Vector2.ClampMagnitude(input, 1f);

        if (kb.cKey.wasPressedThisFrame) crouchToggle = !crouchToggle;
        if (kb.leftShiftKey.isPressed && input.sqrMagnitude > 0.01f) crouchToggle = false;   // 달리면 일어선다
        IsCrouching = kb.leftCtrlKey.isPressed || crouchToggle;
        bool canRun = stats == null || stats.CanRun;
        IsRunning = kb.leftShiftKey.isPressed && !IsCrouching && canRun && input.sqrMagnitude > 0.01f;
        IsMoving = input.sqrMagnitude > 0.01f;

        // 카메라 기준 방향
        Vector3 fwd = Vector3.forward, right = Vector3.right;
        if (cameraTransform != null)
        {
            fwd = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
            right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
        }
        Vector3 move = fwd * input.y + right * input.x;

        float speed = IsCrouching ? crouchSpeed : IsRunning ? runSpeed : walkSpeed;
        if (stats != null) speed *= stats.SpeedMultiplier;

        // 공격 중: 발을 멈추고, 휘두르는 순간에만 앞으로 살짝 내딛는다
        bool attacking = attackLock > 0f;
        if (attacking)
        {
            attackLock -= Time.deltaTime;
            lungeTimer -= Time.deltaTime;
            move = lungeTimer > 0f ? transform.forward : Vector3.zero;
            speed = lungeTimer > 0f ? lungeSpeed : 0f;
            IsRunning = false;
            IsMoving = false;
        }

        // 중력
        if (cc.isGrounded && verticalVel < 0) verticalVel = -2f;
        verticalVel += gravity * Time.deltaTime;

        cc.Move((move * speed + Vector3.up * verticalVel) * Time.deltaTime);

        // 웅크리기 시 키 낮추기
        cc.height = Mathf.Lerp(cc.height, IsCrouching ? 1.1f : 1.8f, 10f * Time.deltaTime);
        cc.center = new Vector3(0, cc.height / 2f, 0);

        if (!attacking && !IsometricCamera.FirstPerson) FaceMoveDirection(move);   // 1인칭만 카메라가 몸 방향을 정함

        // 발소리 · 착지 소리
        if (cc.isGrounded && !wasGrounded) Sfx.Play("land", transform.position, 0.8f, 0.05f, 0f);
        wasGrounded = cc.isGrounded;
        if (IsMoving && cc.isGrounded)
        {
            stepTimer -= Time.deltaTime;
            if (stepTimer <= 0f)
            {
                stepTimer = IsRunning ? 0.32f : IsCrouching ? 0.7f : 0.48f;
                Sfx.Play("step", transform.position, IsCrouching ? 0.15f : IsRunning ? 0.55f : 0.35f, 0.12f, 0f);
            }
        }   // 1인칭에서는 카메라가 몸 방향을 정한다
        EmitFootstepNoise();
    }

    [Header("공격 중 이동")]
    public float lungeSpeed = 3.5f;     // 휘두를 때 내딛는 속도
    public float lungeTime = 0.12f;     // 내딛는 시간 (0.12초 x 3.5 = 약 0.4m)
    float attackLock, lungeTimer;

    /// <summary>공격하는 동안 이동을 멈춘다. PlayerCombat이 부른다.</summary>
    public void LockForAttack(float seconds)
    {
        attackLock = seconds;
        lungeTimer = lungeTime;
    }

    public bool IsAttacking => attackLock > 0f;

    /// <summary>해당 지점을 즉시 바라본다 (마우스 조준 공격용)</summary>
    public void FaceTowards(Vector3 worldPoint)
    {
        if (IsometricCamera.FirstPerson) return;
        Vector3 dir = worldPoint - transform.position; dir.y = 0;
        if (dir.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
    }

    /// <summary>마우스 커서가 가리키는 땅 위 지점 (플레이어 발 높이 평면)</summary>
    public bool MouseGroundPoint(out Vector3 point)
    {
        point = transform.position;
        var mouse = Mouse.current;
        Camera cam = Camera.main;
        if (mouse == null || cam == null) return false;
        // 커서가 잠긴 시점(1인칭·어깨)에서는 화면 가운데 조준점이 겨냥점
        Vector2 sp = Cursor.lockState == CursorLockMode.Locked ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f) : mouse.position.ReadValue();
        Ray ray = cam.ScreenPointToRay(sp);
        var ground = new Plane(Vector3.up, transform.position);
        if (!ground.Raycast(ray, out float enter)) return false;
        point = ray.GetPoint(enter);
        return true;
    }

    [Header("회전")]
    public float turnSpeed = 720f;   // 초당 회전 각도

    /// <summary>
    /// 키보드 이동 방향을 바라본다 (카메라 기준). 멈추면 마지막 방향을 유지.
    /// 카메라는 우클릭 드래그 / Q·E로 따로 돌린다.
    /// </summary>
    void FaceMoveDirection(Vector3 move)
    {
        move.y = 0;
        if (move.sqrMagnitude < 0.01f) return;
        Quaternion target = Quaternion.LookRotation(move.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.deltaTime);
    }

    void EmitFootstepNoise()
    {
        if (!IsMoving) return;
        noiseTimer -= Time.deltaTime;
        if (noiseTimer > 0) return;
        noiseTimer = noiseInterval;

        float radius = IsCrouching ? sneakNoise : IsRunning ? runNoise : walkNoise;
        NoiseSystem.Emit(transform.position, radius);
    }
}
