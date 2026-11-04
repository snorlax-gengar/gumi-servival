using UnityEngine;
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

    void Awake()
    {
        Instance = this;
        var p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) player = p.GetComponent<PlayerStats>();
    }

    void Start()
    {
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
            sun.intensity = Mathf.Clamp01(Mathf.Sin((HourOfDay - 6f) / 12f * Mathf.PI)) * 1.1f + 0.05f;
        }
    }

    public string ClockText()
    {
        int h = Mathf.FloorToInt(HourOfDay);
        int m = Mathf.FloorToInt((HourOfDay - h) * 60f);
        return $"{Day}일차  {h:00}:{m:00}";
    }
}
