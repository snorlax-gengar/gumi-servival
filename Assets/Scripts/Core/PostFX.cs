using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 화면 후처리 (좀보이드 같은 눅눅하고 빛바랜 분위기).
/// ACES 톤매핑 + 채도 낮추기 + 대비, 비네트, 약한 블룸, 필름 그레인.
/// 시간에 따라 색이 변한다: 아침·저녁은 따뜻하게, 밤은 푸르고 어둡게.
/// GameManager가 자동으로 붙인다. 카메라의 후처리도 켠다.
/// </summary>
public class PostFX : MonoBehaviour
{
    Volume volume;
    ColorAdjustments color;
    WhiteBalance white;
    Vignette vignette;
    Bloom bloom;
    LiftGammaGain lgg;

    void Start()
    {
        var go = new GameObject("PostFX Volume");
        go.transform.SetParent(transform, false);
        volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 10;
        var p = ScriptableObject.CreateInstance<VolumeProfile>();
        p.name = "GumiPostFX";
        volume.sharedProfile = p;

        var tm = p.Add<Tonemapping>(true);
        tm.mode.Override(TonemappingMode.ACES);

        color = p.Add<ColorAdjustments>(true);
        color.postExposure.Override(0.25f);
        color.contrast.Override(20f);
        color.saturation.Override(-30f);

        white = p.Add<WhiteBalance>(true);
        white.temperature.Override(6f);
        white.tint.Override(2f);

        lgg = p.Add<LiftGammaGain>(true);
        lgg.lift.Override(new Vector4(0.97f, 1.0f, 1.04f, 0.0f));   // 그늘은 살짝 푸르게
        lgg.gain.Override(new Vector4(1.03f, 1.0f, 0.96f, 0.0f));   // 밝은 곳은 살짝 누렇게

        vignette = p.Add<Vignette>(true);
        vignette.intensity.Override(0.32f);
        vignette.smoothness.Override(0.45f);
        vignette.color.Override(Color.black);

        bloom = p.Add<Bloom>(true);
        bloom.intensity.Override(0.55f);
        bloom.threshold.Override(1.05f);
        bloom.scatter.Override(0.65f);

        var grain = p.Add<FilmGrain>(true);
        grain.type.Override(FilmGrainLookup.Thin1);
        grain.intensity.Override(0.22f);
        grain.response.Override(0.85f);

        foreach (var cam in Camera.allCameras)
        {
            var data = cam.GetComponent<UniversalAdditionalCameraData>();
            if (data != null && cam.targetTexture == null) data.renderPostProcessing = true;
        }
    }

    void Update()
    {
        if (color == null) return;
        var gm = GameManager.Instance;
        float h = gm != null ? gm.HourOfDay : 12f;
        float daylight = Mathf.Clamp01(Mathf.Sin((h - 6f) / 12f * Mathf.PI));
        float dusk = Mathf.Clamp01(1f - Mathf.Abs(daylight - 0.18f) * 5f);   // 해 뜰 때·질 때
        float night = 1f - Mathf.Clamp01(daylight * 3f);

        color.saturation.value = Mathf.Lerp(-30f, -45f, night);   // 전쟁 뒤 잿빛 도시
        color.postExposure.value = Mathf.Lerp(0.25f, 0.8f, night);   // 밤에도 윤곽은 보이게
        white.temperature.value = Mathf.Lerp(6f, -28f, night) + dusk * 22f;
        white.tint.value = Mathf.Lerp(2f, 6f, night);
        vignette.intensity.value = Mathf.Lerp(0.32f, 0.48f, night);
        bloom.intensity.value = Mathf.Lerp(0.55f, 1.1f, night);       // 밤엔 불빛이 번진다
    }
}
