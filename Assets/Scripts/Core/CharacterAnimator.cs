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
    public enum Slot { Idle = 0, Walk = 1, Run = 2, Attack = 3, Death = 4 }
    const int SlotCount = 5;

    public float walkReference = 1.6f;   // 걷기 클립이 원래 속도로 보이는 이동 속도(m/s)
    public float runReference = 4.5f;

    public bool IsReady { get; private set; }
    public bool HasClip(Slot s) => clips[(int)s] != null;

    PlayableGraph graph;
    AnimationMixerPlayable mixer;
    readonly AnimationClipPlayable[] plays = new AnimationClipPlayable[SlotCount];
    readonly AnimationClip[] clips = new AnimationClip[SlotCount];
    readonly float[] weights = new float[SlotCount];

    float speed, speedScale = 1f;
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
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

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
        speed = metersPerSecond;
        speedScale = playbackScale;
    }

    public void PlayAttack(float playbackSpeed = 1f)
    {
        if (!IsReady || dead || clips[(int)Slot.Attack] == null) return;
        attackSpeed = playbackSpeed;
        attackLen = clips[(int)Slot.Attack].length / Mathf.Max(0.1f, playbackSpeed);
        attackT = 0f;
        plays[(int)Slot.Attack].SetTime(0);
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

            SetSpeed(Slot.Walk, Mathf.Clamp(speed / walkReference, 0.6f, 1.6f) * speedScale);
            SetSpeed(Slot.Run, Mathf.Clamp(speed / runReference, 0.7f, 1.4f) * speedScale);

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
            if (i <= (int)Slot.Run)
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
