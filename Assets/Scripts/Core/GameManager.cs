using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// 게임 내 시간 관리. 기본값: 실제 60초 = 게임 1시간 → 하루 24분.
/// 생존 일수를 세고, 낮밤에 맞춰 Directional Light를 돌린다.
/// 씬에 빈 오브젝트 "GameManager"를 만들고 붙인다.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("시간")]
    public float realSecondsPerGameHour = 60f;
    public float startHour = 8f;          // 오전 8시 시작
    public Light sun;                      // Directional Light (선택)

    public float TotalGameHours { get; private set; }
    public float GameHoursThisFrame { get; private set; }
    public int Day => Mathf.FloorToInt((startHour + TotalGameHours) / 24f) + 1;
    public float HourOfDay => (startHour + TotalGameHours) % 24f;
    public float DaysSurvived => TotalGameHours / 24f;

    PlayerStats player;

    /// <summary>안개를 쓰는지. 미니맵 카메라는 안개를 끄고 그린다.</summary>
    public static bool FogOn { get; private set; }
    /// <summary>카메라에서 안개가 끝까지 짙어지는 깊이 (카메라가 거리를 더해 쓴다)</summary>
    public static float FogDepth { get; private set; } = 200f;
    static readonly Color DayFog = new Color(0.66f, 0.69f, 0.72f), DuskFog = new Color(0.55f, 0.45f, 0.40f), NightFog = new Color(0.07f, 0.09f, 0.15f);

    void Awake()
    {
        Instance = this;
        if (GetComponent<ScenarioManager>() == null) gameObject.AddComponent<ScenarioManager>();
        if (GetComponent<PostFX>() == null) gameObject.AddComponent<PostFX>();
        if (GetComponent<PauseMenu>() == null) gameObject.AddComponent<PauseMenu>();
        Sfx.Ensure();
        Story.Ensure();
        var p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) player = p.GetComponent<PlayerStats>();
    }

    void Start()
    {
        // 먼 곳은 뿌옇게: 넓은 지도에서 원경을 정리하고 분위기를 낸다
        FogOn = true;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 45f;
        RenderSettings.fogEndDistance = 240f;
        if (CityGenerator.RealMapActive)
            HUD.ShowMessage("2052년 구미, 구미역 앞 원도심. 배가 고프다...\n바로 옆 주황색 공구함(E)부터 뒤져 보자", 8f);
        else
            HUD.ShowMessage("배가 고프다... 바로 옆 주황색 공구함(E)부터 뒤져 보자\n동쪽 공단은 안드로이드 소굴이다", 8f);
    }

    void Update()
    {
        if (player != null && player.IsDead)
        {
            GameHoursThisFrame = 0;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.rKey.wasPressedThisFrame)
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            return;
        }

        GameHoursThisFrame = Time.deltaTime / realSecondsPerGameHour;
        TotalGameHours += GameHoursThisFrame;

        if (sun != null)
        {
            // 6시 일출, 18시 일몰
            float angle = (HourOfDay - 6f) / 24f * 360f;
            sun.transform.rotation = Quaternion.Euler(angle, 170f, 0);
            float daylight = Mathf.Clamp01(Mathf.Sin((HourOfDay - 6f) / 12f * Mathf.PI));
            sun.intensity = daylight * 1.15f + 0.04f;
            // 해 높이에 따라 빛 색: 낮은 해는 주황, 한낮은 살짝 누런 흰빛
            sun.color = Color.Lerp(Kit.Hex("FF9A55"), Kit.Hex("FFF1DC"), Mathf.Clamp01(daylight * 2.2f));
            // 밤: 같은 조명을 달빛으로 쓴다 (푸르스름하고 약한 빛이 위에서 비춰 윤곽과 그림자가 보인다)
            bool nightNow = HourOfDay >= 18f || HourOfDay < 6f;
            if (nightNow)
            {
                float into = HourOfDay >= 18f ? HourOfDay - 18f : Mathf.Min(HourOfDay + 6f, 6f - HourOfDay);   // 해 진 뒤·뜨기 전 시간
                float moon = Mathf.Clamp01(into / 0.6f);
                sun.transform.rotation = Quaternion.Euler(52f, 305f, 0);
                sun.color = Kit.Hex("9DB4E8");
                sun.intensity = 0.04f + moon * 0.42f;
            }
            sun.shadowStrength = 0.85f;
            // 주변광: 하늘·지평선·땅 세 가지 색 (평평한 회색보다 입체감이 난다)
            RenderSettings.ambientMode = AmbientMode.Trilight;
            float amb = Mathf.Lerp(0.42f, 1f, Mathf.Clamp01(daylight * 2.5f));   // 밤에도 사물 윤곽은 보이게
            RenderSettings.ambientSkyColor = Color.Lerp(Kit.Hex("2C3C66"), Kit.Hex("9DB0C8"), amb) * amb;
            RenderSettings.ambientEquatorColor = Color.Lerp(Kit.Hex("232C44"), Kit.Hex("A39C8E"), amb) * amb;
            RenderSettings.ambientGroundColor = Color.Lerp(Kit.Hex("14182A"), Kit.Hex("5E584E"), amb) * amb;
            // 밤에는 주변광도 어둡게: 가로등과 발전기 불빛만 의지하게
            RenderSettings.ambientIntensity = Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(daylight * 2.5f));
            RenderSettings.reflectionIntensity = Mathf.Lerp(0.2f, 1f, daylight);
            float dusk = Mathf.Clamp01(1f - Mathf.Abs(daylight - 0.15f) * 6f);
            Color fog = Color.Lerp(NightFog, DayFog, Mathf.Clamp01(daylight * 2f));
            RenderSettings.fogColor = Color.Lerp(fog, DuskFog, dusk * 0.5f);
            FogDepth = Mathf.Lerp(90f, 200f, Mathf.Clamp01(daylight * 2f));
            // 밤에는 생존자들이 사는 창 몇 개에 불이 켜진다
            Facade.SetNightGlow(1f - Mathf.Clamp01(daylight * 4f));
            Signage.SetNight(1f - Mathf.Clamp01(daylight * 3f));
        }
    }

    /// <summary>시간을 건너뛴다 (개발용 F12)</summary>
    public void AdvanceHours(float hours) { TotalGameHours += hours; }

    public string ClockText()
    {
        int h = Mathf.FloorToInt(HourOfDay);
        int m = Mathf.FloorToInt((HourOfDay - h) * 60f);
        return $"{Day}일차  {h:00}:{m:00}";
    }
}
