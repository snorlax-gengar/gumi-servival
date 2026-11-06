using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 효과음 + 타격감 (Kenney CC0 사운드, Resources/Audio).
/// Sfx.Play("hit_metal", 위치) 처럼 묶음 이름으로 부르면 그 묶음에서 하나를 골라 음높이를 살짝 바꿔 튼다.
/// Sfx.HitStop(초) = 맞는 순간 아주 짧게 멈춤, IsometricCamera.Shake(세기) = 화면 흔들림.
/// GameManager가 자동으로 만든다.
/// </summary>
public class Sfx : MonoBehaviour
{
    static Sfx inst;
    static readonly Dictionary<string, List<AudioClip>> groups = new Dictionary<string, List<AudioClip>>();
    static readonly Dictionary<string, string[]> map = new Dictionary<string, string[]>
    {
        { "step", new[] { "imp_footstep_concrete_" } },
        { "step_grass", new[] { "imp_footstep_grass_" } },
        { "swing", new[] { "rpg_cloth" } },
        { "hit_metal", new[] { "imp_impactMetal_heavy_", "imp_impactMetal_medium_", "sf_impactMetal_" } },
        { "hit_punch", new[] { "imp_impactPunch_heavy_", "imp_impactPunch_medium_" } },
        { "hurt", new[] { "imp_impactSoft_heavy_" } },
        { "shove", new[] { "imp_impactSoft_medium_", "imp_impactPunch_medium_" } },
        { "loot", new[] { "rpg_handleSmallLeather", "rpg_metalLatch", "rpg_dropLeather" } },
        { "equip", new[] { "rpg_drawKnife" } },
        { "eat", new[] { "rpg_cloth", "rpg_metalClick" } },
        { "door", new[] { "rpg_doorOpen_", "rpg_creak" } },
        { "jump", new[] { "rpg_cloth" } },
        { "land", new[] { "imp_footstep_concrete_" } },
        { "android_alert", new[] { "sf_computerNoise_" } },
        { "android_stun", new[] { "sf_forceField_" } },
        { "android_die", new[] { "sf_explosionCrunch_" } },
        { "android_hum", new[] { "sf_spaceEngineLow_" } },
        { "generator", new[] { "sf_engineCircular_" } },
        { "glass", new[] { "imp_impactGlass_light_" } },
    };

    public static float Volume = 1f;
    readonly List<AudioSource> pool = new List<AudioSource>();
    int next;

    public static void Ensure()
    {
        if (inst != null) return;
        var go = new GameObject("Sfx");
        inst = go.AddComponent<Sfx>();
    }

    void Awake()
    {
        inst = this;
        if (groups.Count == 0)
        {
            var all = Resources.LoadAll<AudioClip>("Audio");
            foreach (var kv in map)
            {
                var list = new List<AudioClip>();
                foreach (var c in all)
                    foreach (var pre in kv.Value)
                        if (c.name.StartsWith(pre)) { list.Add(c); break; }
                groups[kv.Key] = list;
            }
        }
        for (int i = 0; i < 24; i++)
        {
            var g = new GameObject("snd");
            g.transform.SetParent(transform, false);
            var a = g.AddComponent<AudioSource>();
            a.playOnAwake = false;
            a.rolloffMode = AudioRolloffMode.Linear;
            a.minDistance = 2f; a.maxDistance = 35f;
            a.dopplerLevel = 0f;
            pool.Add(a);
        }
    }

    public static AudioClip Clip(string group)
    {
        if (!groups.TryGetValue(group, out var l) || l.Count == 0) return null;
        return l[Random.Range(0, l.Count)];
    }

    /// <summary>spatial: 0 = 귀 바로 옆(내 소리), 1 = 그 위치에서 들림</summary>
    public static void Play(string group, Vector3 pos, float volume = 1f, float pitchVar = 0.08f, float spatial = 1f, float maxDist = 35f)
    {
        Ensure();
        var clip = Clip(group);
        if (clip == null || inst == null) return;
        var a = inst.pool[inst.next];
        inst.next = (inst.next + 1) % inst.pool.Count;
        a.transform.position = pos;
        a.clip = clip;
        a.volume = volume * Volume;
        a.pitch = 1f + Random.Range(-pitchVar, pitchVar);
        a.spatialBlend = spatial;
        a.maxDistance = maxDist;
        a.Play();
    }

    /// <summary>반복 소리 (발전기·안드로이드 기계음). 대상에 붙는다.</summary>
    public static AudioSource Loop(string group, Transform on, float volume, float maxDist)
    {
        Ensure();
        var clip = Clip(group);
        if (clip == null) return null;
        var a = on.gameObject.AddComponent<AudioSource>();
        a.clip = clip; a.loop = true; a.volume = volume * Volume;
        a.spatialBlend = 1f; a.rolloffMode = AudioRolloffMode.Linear; a.minDistance = 1.5f; a.maxDistance = maxDist;
        a.dopplerLevel = 0f;
        a.time = Random.Range(0f, clip.length);
        a.Play();
        return a;
    }

    // ---------------- 맞았을 때 붉은 화면 ----------------
    float flash;
    public static void DamageFlash(float k) { Ensure(); if (inst != null) inst.flash = Mathf.Max(inst.flash, 0.35f + k * 0.5f); }

    void OnGUI()
    {
        if (flash <= 0f) return;
        flash = Mathf.Max(0f, flash - Time.unscaledDeltaTime * 1.8f);
        GUI.color = new Color(0.6f, 0f, 0f, flash * 0.45f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    // ---------------- 타격감 ----------------

    public static bool HitStopEnabled = true;
    float stopUntil;
    bool stopping;

    /// <summary>맞는 순간 세상이 아주 잠깐 멈춘 듯 (0.04~0.1초)</summary>
    public static void HitStop(float seconds)
    {
        Ensure();
        if (!HitStopEnabled || inst == null || Time.timeScale < 0.5f && !inst.stopping) return;
        inst.stopUntil = Mathf.Max(inst.stopUntil, Time.unscaledTime + seconds);
        if (!inst.stopping) inst.StartCoroutine(inst.DoStop());
    }

    IEnumerator DoStop()
    {
        stopping = true;
        Time.timeScale = 0.05f;
        while (Time.unscaledTime < stopUntil) yield return null;
        if (!PauseMenu.IsPaused) Time.timeScale = 1f;
        stopping = false;
    }
}
