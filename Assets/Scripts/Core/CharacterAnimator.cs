using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// 에셋에 들어 있는 애니메이션 클립을 Animator Controller 없이 코드로 재생한다 (Playables).
/// - 이동 속도에 따라 대기 → 걷기 → 달리기를 부드럽게 섞는다
/// - 공격은 잠깐 덮어쓰고 다시 이동 동작으로 돌아온다
/// - 쓰러짐은 끝 프레임에서 멈춘다
/// 클립 이름은 키워드로 찾는다 (예: "walk", "run"). FBX의 루프 설정과 상관없이 직접 반복한다.
/// </summary>
public class CharacterAnimator : MonoBehaviour
{
    public enum Slot { Idle = 0, Walk = 1, Run = 2, Attack = 3, Death = 4, Crouch = 5, Jump = 6, Fall = 7, Vault = 8, Climb = 9, Hurdle = 10, Drop = 11, CrouchIdle = 12, JumpA = 13, JumpB = 14 }
    const int SlotCount = 15;

    public float walkReference = 1.6f;   // 이 속도(m/s)에서 걷기 동작이 완전히 섞인다
    public float runReference = 4.5f;    // 이 속도에서 달리기 동작이 완전히 섞인다
    [Tooltip("클립 원래 보폭 속도(m/s). 0이면 walkReference/runReference를 쓴다 (Mixamo처럼 실제 보폭이 있는 클립용)")]
    public float walkClipSpeed = 0f, runClipSpeed = 0f, crouchClipSpeed = 1.2f;
    [Tooltip("0보다 크면 공격 동작을 이 시간(초)에 맞춰 재생한다")]
    public float attackDuration = 0f;
    [Tooltip("공격 클립 중 쓸 구간 (0~1). 긴 준비·마무리 동작을 잘라 빠르게 휘두르게 한다")]
    [Range(0, 1)] public float attackFrom = 0f, attackTo = 1f;
    [Tooltip("이동 속도 변화를 부드럽게 (클수록 빠르게 따라감)")]
    public float speedSmoothing = 10f;
    [Tooltip("화면에 안 보여도 계속 움직인다 (1인칭에서 몸을 그림자만 남겨도 굳지 않게)")]
    public bool alwaysAnimate = false;

    public bool IsReady { get; private set; }
    public bool HasClip(Slot s) => clips[(int)s] != null;

    PlayableGraph graph;
    AnimationMixerPlayable mixer;
    readonly AnimationClipPlayable[] plays = new AnimationClipPlayable[SlotCount];
    readonly AnimationClip[] clips = new AnimationClip[SlotCount];
    readonly float[] weights = new float[SlotCount];

    float speed, rawSpeed, speedScale = 1f, crouchAmount;
    float attackT = -1f, attackLen = 0.6f, attackSpeed = 1f;
    bool dead;

    /// <summary>
    /// keywords[slot] = 클립 이름에 들어 있어야 하는 단어들 (앞에 있을수록 우선).
    /// exclude = 이 단어가 들어간 클립은 고르지 않음.
    /// </summary>
    public bool Init(Animator animator, AnimationClip[] all, Dictionary<Slot, string[]> keywords, string[] exclude)
    {
        if (animator == null || all == null || all.Length == 0) return false;

        foreach (var kv in keywords)
            clips[(int)kv.Key] = Find(all, kv.Value, exclude);
        if (clips[(int)Slot.Idle] == null && clips[(int)Slot.Walk] == null) return false;

        animator.applyRootMotion = false;
        // 걷기·달리기 발 박자 맞추기: 각 클립에서 왼발이 가장 앞에 나온 순간을 찾아 둔다
        walkOffset = FootPhase(animator, clips[(int)Slot.Walk]);
        runOffset = FootPhase(animator, clips[(int)Slot.Run]);
        crouchOffset = FootPhase(animator, clips[(int)Slot.Crouch]);
        if (!profiled && clips[(int)Slot.Jump] != null) { profiled = true; foreach (var ps in new[] { Slot.JumpA, Slot.JumpB }) if (clips[(int)ps] != null) ProfileClip(animator, clips[(int)ps]); }
        CaptureArms(animator);
        animator.cullingMode = alwaysAnimate ? AnimatorCullingMode.AlwaysAnimate : AnimatorCullingMode.CullUpdateTransforms;

        graph = PlayableGraph.Create(name + "_Anim");
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        var output = AnimationPlayableOutput.Create(graph, "Anim", animator);
        mixer = AnimationMixerPlayable.Create(graph, SlotCount);
        for (int i = 0; i < SlotCount; i++)
        {
            if (clips[i] == null) continue;
            plays[i] = AnimationClipPlayable.Create(graph, clips[i]);
            plays[i].SetApplyFootIK(animator.isHuman);   // 휴머노이드: 발 IK로 동작 원래 발 위치를 지킴 (미끄러짐·뜸 줄임)
            graph.Connect(plays[i], 0, mixer, i);
            mixer.SetInputWeight(i, 0f);
        }
        output.SetSourcePlayable(mixer);
        graph.Play();
        IsReady = true;

        var names = new List<string>();
        for (int i = 0; i < SlotCount; i++) names.Add($"{(Slot)i}={(clips[i] ? clips[i].name : "-")}");
        Debug.Log($"[Gumi] {name} 애니메이션: {string.Join(", ", names)}");
        return true;
    }

    float walkOffset, runOffset, locoPhase, crouchOffset, crouchPhase;

    /// <summary>하던 공격 동작을 끊는다 (맞아서 움찔할 때)</summary>
    public void CancelAttack() { if (attackT >= 0f) attackT = Mathf.Max(attackT, attackLen * 0.86f); }

    /// <summary>클립에서 왼발이 오른발보다 가장 앞에 있는 순간 (0~1). 못 재면 0.</summary>
    static float FootPhase(Animator animator, AnimationClip clip)
    {
        if (clip == null || !animator.isHuman) return 0f;
        var lf = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        var rf = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        if (lf == null || rf == null) return 0f;
        float best = float.MinValue, bestT = 0f;
        try
        {
            const int N = 32;
            for (int k = 0; k < N; k++)
            {
                float t = k / (float)N;
                clip.SampleAnimation(animator.gameObject, t * clip.length);
                float d = Vector3.Dot(lf.position - rf.position, animator.transform.forward);
                if (d > best) { best = d; bestT = t; }
            }
        }
        catch (System.Exception e) { Debug.LogWarning($"[Gumi] 발 박자 측정 실패 ({clip.name}): {e.Message}"); return 0f; }
        Debug.Log($"[Gumi] 발 박자 {clip.name}: 왼발 앞 = {bestT:0.00}");
        return bestT;
    }

    /// <summary>개발용: 클립 자세를 20등분해 골반·발 높이를 로그로 남긴다 (점프 구간 맞추기용)</summary>
    static bool profiled;
    static void ProfileClip(Animator animator, AnimationClip clip)
    {
        if (!animator.isHuman) return;
        var hip = animator.GetBoneTransform(HumanBodyBones.Hips);
        var lf = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        var rf = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        if (hip == null || lf == null || rf == null) return;
        var sb = new System.Text.StringBuilder();
        sb.Append($"[Gumi] 클립 자세 {clip.name} ({clip.length:0.00}s): t hip lf rf sep\n");
        var tr = animator.transform;
        try
        {
            for (int k = 0; k <= 20; k++)
            {
                float t = k / 20f;
                clip.SampleAnimation(animator.gameObject, t * clip.length);
                Vector3 h = tr.InverseTransformPoint(hip.position), l = tr.InverseTransformPoint(lf.position), r = tr.InverseTransformPoint(rf.position);
                sb.Append($"{t:0.00} {h.y:0.00} {l.y:0.00} {r.y:0.00} {l.z - r.z:0.00}\n");
            }
        }
        catch (System.Exception e) { sb.Append(e.Message); }
        Debug.Log(sb.ToString());
    }

    static AnimationClip Find(AnimationClip[] all, string[] keys, string[] exclude)
    {
        foreach (var k in keys)
        {
            AnimationClip best = null;
            foreach (var c in all)
            {
                if (c == null || c.name.StartsWith("__preview__")) continue;
                string n = c.name.ToLowerInvariant();
                if (!n.Contains(k)) continue;
                bool bad = false;
                if (exclude != null) foreach (var e in exclude) if (n.Contains(e)) { bad = true; break; }
                if (bad) continue;
                if (best == null || c.name.Length < best.name.Length) best = c; // 가장 짧은 이름 = 가장 기본 동작
            }
            if (best != null) return best;
        }
        return null;
    }

    /// <summary>현재 이동 속도(m/s)와 재생 배율</summary>
    public void SetMovement(float metersPerSecond, float playbackScale = 1f)
    {
        rawSpeed = metersPerSecond;
        speedScale = playbackScale;
    }

    /// <summary>0 = 서 있음, 1 = 웅크림 (웅크려 걷기 클립이 있을 때만 의미 있음)</summary>
    public void SetCrouch(float amount) { crouchAmount = Mathf.Clamp01(amount); }

    public void PlayAttack(float playbackSpeed = 1f)
    {
        if (!IsReady || dead || clips[(int)Slot.Attack] == null) return;
        float len = clips[(int)Slot.Attack].length;
        float span = Mathf.Max(0.05f, attackTo - attackFrom) * len;
        if (attackDuration > 0f) playbackSpeed = span / attackDuration;
        attackSpeed = playbackSpeed;
        attackLen = span / Mathf.Max(0.1f, playbackSpeed);
        attackT = 0f;
        plays[(int)Slot.Attack].SetTime(attackFrom * len);
    }

    // ---------------- 점프·낙하·넘기·기어오르기 ----------------
    Slot actionSlot;
    float actionT = -1f, actionLen = 1f, actionSpeed = 1f, actionFadeOut = 0.12f;
    bool airborne;

    /// <summary>한 번 재생하는 동작 (넘기·기어오르기·점프 시작). duration 초에 맞춰 from~to 구간을 재생.</summary>
    public bool PlayAction(Slot slot, float duration, float from = 0f, float to = 1f, float fadeOut = 0.12f)
    {
        actionFadeOut = Mathf.Clamp(fadeOut, 0.05f, 0.6f);
        if (!IsReady || dead || clips[(int)slot] == null) return false;
        float len = clips[(int)slot].length;
        float span = Mathf.Max(0.05f, to - from) * len;
        actionSlot = slot;
        actionSpeed = span / Mathf.Max(0.05f, duration);
        actionLen = duration;
        actionT = 0f;
        plays[(int)slot].SetTime(from * len);
        return true;
    }

    /// <summary>공중에 떠 있는 동안 낙하 동작 (Fall 클립)</summary>
    public void SetAirborne(bool a) { airborne = a; }

    public void PlayDeath()
    {
        if (!IsReady || dead) return;
        dead = true;
        if (clips[(int)Slot.Death] != null) plays[(int)Slot.Death].SetTime(0);
    }

    void Update()
    {
        if (!IsReady) return;
        float dt = Time.deltaTime;
        // 매 프레임 위치 차이로 잰 속도는 들쭉날쭉해서, 걷기↔서기가 튀지 않게 부드럽게 따라간다
        speed = Mathf.Lerp(speed, rawSpeed, 1f - Mathf.Exp(-speedSmoothing * dt));
        if (rawSpeed < 0.05f && speed < 0.15f) speed = 0f;

        for (int i = 0; i < SlotCount; i++) weights[i] = 0f;

        if (dead && clips[(int)Slot.Death] != null)
        {
            weights[(int)Slot.Death] = 1f;
            var p = plays[(int)Slot.Death];
            double end = clips[(int)Slot.Death].length - 0.02;
            if (p.GetTime() >= end) { p.SetTime(end); p.SetSpeed(0); }
        }
        else
        {
            // 이동: 대기 ↔ 걷기 ↔ 달리기
            float walk = Mathf.Clamp01(speed / walkReference);
            float run = clips[(int)Slot.Run] != null ? Mathf.Clamp01((speed - walkReference) / Mathf.Max(0.1f, runReference - walkReference)) : 0f;
            weights[(int)Slot.Idle] = 1f - walk;
            weights[(int)Slot.Walk] = walk * (1f - run);
            weights[(int)Slot.Run] = walk * run;
            if (clips[(int)Slot.Idle] == null) { weights[(int)Slot.Walk] += weights[(int)Slot.Idle]; weights[(int)Slot.Idle] = 0f; }

            float wPlay = (walkClipSpeed > 0f ? Mathf.Clamp(speed / walkClipSpeed, 0.6f, 1.8f) : Mathf.Clamp(speed / walkReference, 0.6f, 1.6f)) * speedScale;
            float rPlay = (runClipSpeed > 0f ? Mathf.Clamp(speed / runClipSpeed, 0.7f, 1.6f) : Mathf.Clamp(speed / runReference, 0.7f, 1.4f)) * speedScale;
            var wc = clips[(int)Slot.Walk]; var rc = clips[(int)Slot.Run];
            if (wc != null && rc != null)
            {
                // 걷기와 달리기를 같은 발 박자로 돌린다: 섞일 때 다리가 엇갈려 꼬이지 않고, 걷다 뛰기가 매끄럽다
                float rate = Mathf.Lerp(wPlay / wc.length, rPlay / rc.length, run);
                locoPhase = Mathf.Repeat(locoPhase + rate * dt, 1f);
                plays[(int)Slot.Walk].SetSpeed(0); plays[(int)Slot.Run].SetSpeed(0);
                plays[(int)Slot.Walk].SetTime(Mathf.Repeat(locoPhase + walkOffset, 1f) * wc.length);
                plays[(int)Slot.Run].SetTime(Mathf.Repeat(locoPhase + runOffset, 1f) * rc.length);
            }
            else { SetSpeed(Slot.Walk, wPlay); SetSpeed(Slot.Run, rPlay); }

            // 웅크리기: 서 있는 동작 대신 웅크려 걷기 (멈추면 그 자세로 정지)
            if (clips[(int)Slot.Crouch] != null && crouchAmount > 0f)
            {
                float c = crouchAmount;
                weights[(int)Slot.Idle] *= 1f - c;
                weights[(int)Slot.Walk] *= 1f - c;
                weights[(int)Slot.Run] *= 1f - c;
                var cc = clips[(int)Slot.Crouch];
                // 멈추면 걷던 동작 중간(한 발 든 채)에 얼어붙지 않게, 두 발이 다 땅에 닿은 자세까지 마저 디디고 선다
                float step = Mathf.Clamp(speed / Mathf.Max(0.1f, crouchClipSpeed), 0f, 1.8f) / cc.length * dt;
                if (speed < 0.12f)
                {
                    float dA = Mathf.DeltaAngle(crouchPhase * 360f, (crouchOffset + crouchStopShift) * 360f) / 360f;          // 왼발 앞 자세까지
                    float dB = Mathf.DeltaAngle(crouchPhase * 360f, (crouchOffset + 0.5f + crouchStopShift) * 360f) / 360f;   // 오른발 앞 자세까지
                    float d = Mathf.Abs(dA) < Mathf.Abs(dB) ? dA : dB;
                    step = Mathf.Sign(d) * Mathf.Min(Mathf.Abs(d), 0.9f / cc.length * dt);
                }
                crouchPhase = Mathf.Repeat(crouchPhase + step, 1f);
                plays[(int)Slot.Crouch].SetSpeed(0);
                plays[(int)Slot.Crouch].SetTime(crouchPhase * cc.length);
                // 웅크려 가만히 있는 전용 동작이 있으면 멈췄을 때 그걸로
                float idleShare = clips[(int)Slot.CrouchIdle] != null ? 1f - Mathf.Clamp01(speed / 0.5f) : 0f;
                weights[(int)Slot.Crouch] = c * (1f - idleShare);
                weights[(int)Slot.CrouchIdle] = c * idleShare;
                SetSpeed(Slot.CrouchIdle, 1f);
            }

            // 공중: 낙하 동작
            if (airborne && clips[(int)Slot.Fall] != null && actionT < 0f)
            {
                for (int i = 0; i < SlotCount; i++) weights[i] = 0f;
                weights[(int)Slot.Fall] = 1f;
            }

            // 한 번 재생 동작 (넘기·기어오르기·점프)
            if (actionT >= 0f)
            {
                actionT += dt;
                float t = actionT / actionLen;
                float a = Mathf.Clamp01(Mathf.Min(t / 0.08f, (1f - t) / actionFadeOut));
                for (int i = 0; i < SlotCount; i++) weights[i] *= (1f - a);
                weights[(int)actionSlot] = a;
                SetSpeed(actionSlot, actionSpeed);
                if (t >= 1f) actionT = -1f;
            }

            // 공격: 들어올 때와 끝날 때 살짝 섞는다
            if (attackT >= 0f)
            {
                attackT += dt;
                float t = attackT / attackLen;
                float a = Mathf.Clamp01(Mathf.Min(t / 0.1f, (1f - t) / 0.15f));
                for (int i = 0; i < SlotCount; i++) weights[i] *= (1f - a);
                weights[(int)Slot.Attack] = a;
                SetSpeed(Slot.Attack, attackSpeed);
                if (t >= 1f) attackT = -1f;
            }
        }

        // 동작이 바뀔 때 자세가 뚝 튀지 않게, 모든 섞임 비율을 약 0.12초에 걸쳐 따라가게 한다 (착지·일어서기·넘기 끝 등)
        float sum = 0f;
        for (int i = 0; i < SlotCount; i++)
        {
            if (clips[i] == null) { smooth[i] = 0f; continue; }
            smooth[i] = dead ? weights[i] : Mathf.MoveTowards(smooth[i], weights[i], 8f * dt);
            sum += smooth[i];
        }
        if (sum < 1e-4f) sum = 1f;
        armRelax = smooth[(int)Slot.Crouch] / sum;
        for (int i = 0; i < SlotCount; i++)
        {
            if (clips[i] == null) continue;
            mixer.SetInputWeight(i, smooth[i] / sum);
            // 이동 클립은 직접 반복
            if (i <= (int)Slot.Run || i == (int)Slot.Fall || i == (int)Slot.CrouchIdle)
            {
                double len = clips[i].length;
                double tm = plays[i].GetTime();
                if (len > 0.01 && tm > len) plays[i].SetTime(tm % len);
            }
        }
    }

    readonly float[] smooth = new float[SlotCount];
    float armRelax;
    Transform[] armBones; Quaternion[] armRest;
    static readonly HumanBodyBones[] ArmIds =
    {
        HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm,
        HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm,
    };

    /// <summary>서 있는 동작의 팔 자세를 기억해 둔다 (웅크릴 때 팔을 내리는 데 쓴다)</summary>
    void CaptureArms(Animator animator)
    {
        var idle = clips[(int)Slot.Idle];
        if (idle == null || !animator.isHuman) return;
        try
        {
            idle.SampleAnimation(animator.gameObject, 0f);
            armBones = new Transform[ArmIds.Length]; armRest = new Quaternion[ArmIds.Length];
            for (int i = 0; i < ArmIds.Length; i++)
            {
                armBones[i] = animator.GetBoneTransform(ArmIds[i]);
                if (armBones[i] != null) armRest[i] = armBones[i].localRotation;
            }
        }
        catch { armBones = null; }
    }

    /// <summary>웅크려 걷기 동작은 두 팔을 얼굴 앞에 든 자세라 권투하는 것처럼 보인다 → 팔만 편하게 내린다</summary>
    public float crouchArmRelax = 0.8f;
    /// <summary>멈출 때 서는 자세: 0 = 두 발을 한껏 벌린 순간, 0.12 = 발이 조금 모인 순간 (덜 벌어져 자연스럽다)</summary>
    public float crouchStopShift = 0.12f;
    void LateUpdate()
    {
        if (!IsReady || armBones == null || armRelax < 0.01f || crouchArmRelax <= 0f) return;
        float k = armRelax * crouchArmRelax;
        for (int i = 0; i < armBones.Length; i++)
            if (armBones[i] != null) armBones[i].localRotation = Quaternion.Slerp(armBones[i].localRotation, armRest[i], k);
    }

    void SetSpeed(Slot s, float v)
    {
        if (clips[(int)s] != null) plays[(int)s].SetSpeed(v);
    }

    void OnDestroy()
    {
        if (graph.IsValid()) graph.Destroy();
    }
}
