using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 손전등 (L 키). 밤에는 이것 없이는 거의 안 보인다 — 대신 불빛은 안드로이드 눈에 띈다.
/// 켜 두면 3초마다 작은 소리(빛 신호)를 내서 근처 안드로이드가 알아챌 수 있다.
/// PlayerController가 자동으로 붙인다.
/// </summary>
public class Flashlight : MonoBehaviour
{
    public float range = 22f;
    public float angle = 58f;
    public float intensity = 9f;
    public float signalRadius = 9f;

    public bool IsOn { get; private set; }
    Light spot, glowFill;
    float signalTimer;
    bool hinted;

    void Start()
    {
        var go = new GameObject("손전등");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0.15f, 1.35f, 0.25f);
        go.transform.localRotation = Quaternion.Euler(14f, 0, 0);
        spot = go.AddComponent<Light>();
        spot.type = LightType.Spot;
        spot.range = range;
        spot.spotAngle = angle;
        spot.innerSpotAngle = angle * 0.45f;
        spot.intensity = intensity;
        spot.color = Kit.Hex("FFF2D6");
        spot.shadows = LightShadows.Soft;
        spot.shadowStrength = 0.8f;
        spot.enabled = false;

        // 발밑이 완전히 까맣지 않게 아주 약한 주변 빛
        var g2 = new GameObject("손전등 반사광");
        g2.transform.SetParent(transform, false);
        g2.transform.localPosition = new Vector3(0, 1.2f, 0.8f);
        glowFill = g2.AddComponent<Light>();
        glowFill.type = LightType.Point;
        glowFill.range = 4.5f;
        glowFill.intensity = 0.6f;
        glowFill.color = Kit.Hex("FFF2D6");
        glowFill.shadows = LightShadows.None;
        glowFill.enabled = false;
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.lKey.wasPressedThisFrame) Set(!IsOn);

        var gm = GameManager.Instance;
        bool night = gm != null && (gm.HourOfDay >= 18.8f || gm.HourOfDay < 5.5f);
        if (night && !IsOn && !hinted)
        {
            hinted = true;
            HUD.ShowMessage("어두워졌다. [L] 손전등 — 단, 불빛은 안드로이드 눈에 띈다", 5f);
        }

        if (IsOn)
        {
            signalTimer -= Time.deltaTime;
            if (signalTimer <= 0f)
            {
                signalTimer = 3f;
                NoiseSystem.Emit(transform.position, signalRadius);
            }
        }
    }

    public void Set(bool on)
    {
        IsOn = on;
        if (spot != null) spot.enabled = on;
        if (glowFill != null) glowFill.enabled = on;
    }
}
