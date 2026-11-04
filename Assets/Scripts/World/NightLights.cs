using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 가로등 불빛을 밤(18:30~06:00)에만 켠다. 낮에는 실제 라이트를 꺼서 성능을 아낀다.
/// 램프 자체의 발광은 항상 보이고, 바닥을 비추는 빛만 켜고 끈다.
/// </summary>
public class NightLights : MonoBehaviour
{
    static readonly List<Light> lights = new List<Light>();
    static NightLights instance;
    bool? lastNight;
    float timer;
    Transform player;

    /// <summary>0보다 크면 플레이어 주변 이 반경 안의 불빛만 켠다 (넓은 지도에서 성능)</summary>
    public static float ActiveRadius = 0f;

    public static void Register(Light l)
    {
        lights.Add(l);
        if (instance == null) instance = new GameObject("NightLights").AddComponent<NightLights>();
        instance.lastNight = null; // 다음 갱신 때 상태를 다시 적용
    }

    void OnDestroy()
    {
        if (instance == this) { instance = null; lights.Clear(); }
    }

    void Update()
    {
        timer -= Time.deltaTime;
        if (timer > 0) return;
        timer = 1f;

        float hour = GameManager.Instance != null ? GameManager.Instance.HourOfDay : 12f;
        bool night = hour >= 18.5f || hour < 6f;

        if (ActiveRadius > 0f)
        {
            timer = 0.5f;
            if (player == null)
            {
                var p = GameObject.FindGameObjectWithTag("Player");
                if (p != null) player = p.transform;
            }
            lights.RemoveAll(l => l == null);
            Vector3 c = player != null ? player.position : Vector3.zero;
            float r2 = ActiveRadius * ActiveRadius;
            foreach (var l in lights)
            {
                bool on = night && (l.transform.position - c).sqrMagnitude < r2;
                if (l.enabled != on) l.enabled = on;
            }
            lastNight = null;
            return;
        }

        if (lastNight == night) return;
        lastNight = night;

        lights.RemoveAll(l => l == null);
        foreach (var l in lights) l.enabled = night;
    }
}
