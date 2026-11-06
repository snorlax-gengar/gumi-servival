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
    public enum Slot { Idle = 0, Walk = 1, Run = 2, Attack = 3, Death = 4, Crouch = 5 }
    const int SlotCount = 6;

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
        animator.cullingMode = alwaysAnimate ? AnimatorCullingMode.AlwaysAnimate : AnimatorCullingMode.CullUpdateTransforms;

        graph = PlayableGraph.Create(name + "_Anim");
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        var output = AnimationPlayableOutput.Create(graph, "Anim", animator);
        mixer = AnimationMixerPlayable.Create(graph, SlotCount);
        for (int i = 0; i < SlotCount; i++)
        {
            if (clips[i] == null) continue;
            plays[i] = AnimationClipPlayable.Create(graph, clips[i]);
            plays[i].SetApplyFootIK(false);
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

            if (walkClipSpeed > 0f) SetSpeed(Slot.Walk, Mathf.Clamp(speed / walkClipSpeed, 0.6f, 1.8f) * speedScale);
            else SetSpeed(Slot.Walk, Mathf.Clamp(speed / walkReference, 0.6f, 1.6f) * speedScale);
            if (runClipSpeed > 0f) SetSpeed(Slot.Run, Mathf.Clamp(speed / runClipSpeed, 0.7f, 1.6f) * speedScale);
            else SetSpeed(Slot.Run, Mathf.Clamp(speed / runReference, 0.7f, 1.4f) * speedScale);

            // 웅크리기: 서 있는 동작 대신 웅크려 걷기 (멈추면 그 자세로 정지)
            if (clips[(int)Slot.Crouch] != null && crouchAmount > 0f)
            {
                float c = crouchAmount;
                weights[(int)Slot.Idle] *= 1f - c;
                weights[(int)Slot.Walk] *= 1f - c;
                weights[(int)Slot.Run] *= 1f - c;
                weights[(int)Slot.Crouch] = c;
                SetSpeed(Slot.Crouch, Mathf.Clamp(speed / Mathf.Max(0.1f, crouchClipSpeed), 0f, 1.8f));
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

        for (int i = 0; i < SlotCount; i++)
        {
            if (clips[i] == null) continue;
            mixer.SetInputWeight(i, weights[i]);
            // 이동 클립은 직접 반복
            if (i <= (int)Slot.Run || i == (int)Slot.Crouch)
            {
                double len = clips[i].length;
                double tm = plays[i].GetTime();
                if (len > 0.01 && tm > len) plays[i].SetTime(tm % len);
            }
        }
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
