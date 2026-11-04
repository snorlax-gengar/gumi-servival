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
        if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
    }

    void Update()
    {
        if (stats != null && stats.IsDead) return;
        var kb = Keyboard.current;
        if (kb == null) return;

        // 입력
        Vector2 input = Vector2.zero;
        if (kb.wKey.isPressed) input.y += 1;
        if (kb.sKey.isPressed) input.y -= 1;
        if (kb.dKey.isPressed) input.x += 1;
        if (kb.aKey.isPressed) input.x -= 1;
        input = Vector2.ClampMagnitude(input, 1f);

        IsCrouching = kb.leftCtrlKey.isPressed;
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

        if (!attacking) FaceMoveDirection(move);
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
