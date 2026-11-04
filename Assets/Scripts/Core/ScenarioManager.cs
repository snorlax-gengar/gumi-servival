using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// '첫날 밤' 시나리오.
/// 1) 내 원룸 필로티의 발전기 살펴보기 → 2) 연료통 + 부품 3개 모아 수리 →
/// 3) 해 지기 전(18:30) 거점으로 → 4) 아침 6시까지 버티기 (발전기를 켤지 말지는 선택) → 생존 화면
/// 밤에는 춥다: 발전기 근처가 아니면 배고픔·갈증이 빨리 줄고 스태미나 회복이 느리다.
/// 발전기를 켜면 따뜻하고 회복이 빠르지만, 전력 신호가 안드로이드를 부른다.
/// GameManager가 자동으로 붙인다. 발전기가 없는 씬(골목 프로토타입)에서는 아무것도 하지 않는다.
/// </summary>
public class ScenarioManager : MonoBehaviour
{
    public static ScenarioManager Instance { get; private set; }

    public string Objective { get; private set; }
    public string Hint { get; private set; }
    public Vector3? ObjectivePos { get; private set; }
    public string Status { get; private set; }
    public bool Won { get; private set; }

    public static int Kills;   // AndroidAI가 올린다

    PlayerStats stats;
    Inventory inv;
    Wallet wallet;
    bool showWin;
    GUIStyle title, body, panel;

    void Awake()
    {
        Instance = this;
        Kills = 0;   // 플레이 모드 재시작 시 초기화 (도메인 리로드가 꺼져 있어도)
    }

    void Start()
    {
        var p = GameObject.FindGameObjectWithTag("Player");
        if (p != null)
        {
            stats = p.GetComponent<PlayerStats>();
            inv = p.GetComponent<Inventory>();
            wallet = p.GetComponent<Wallet>();
        }
    }

    static bool IsNight(float h) => h >= 18.5f || h < 6f;

    void Update()
    {
        var gm = GameManager.Instance;
        var gen = Generator.Instance;
        if (gm == null || stats == null) return;

        float h = gm.HourOfDay;
        bool night = IsNight(h);

        // ---------- 추위 / 따뜻함 ----------
        bool warm = gen != null && gen.IsComfortZone(stats.transform.position);
        if (warm)
        {
            stats.drainMultiplier = 1f; stats.staminaRegenMultiplier = 2f; stats.healthRegenPerHour = 4f;
            Status = "따뜻하다 — 발전기 곁에서 빨리 회복된다";
        }
        else if (night)
        {
            stats.drainMultiplier = 1.35f; stats.staminaRegenMultiplier = 0.5f; stats.healthRegenPerHour = 0f;
            Status = "춥다 — 배고픔·갈증이 빨리 줄고 숨이 금방 찬다";
        }
        else
        {
            stats.drainMultiplier = 1f; stats.staminaRegenMultiplier = 1f; stats.healthRegenPerHour = 0f;
            Status = null;
        }

        if (gen == null) { Objective = null; Hint = null; ObjectivePos = null; return; }

        // ---------- 목표 ----------
        bool hasFuel = Generator.FuelItem != null && inv != null && inv.Has(Generator.FuelItem);
        int parts = wallet != null ? wallet.Parts : 0;

        if (Won)
        {
            Objective = "첫날 밤을 버텼다 — 자유롭게 살아남자";
            Hint = gen.Current == Generator.State.On ? "발전기 연료를 아껴 쓰자" : null;
            ObjectivePos = null;
        }
        else if (!gen.Inspected)
        {
            Objective = "내 원룸 1층 필로티의 발전기를 살펴보자";
            Hint = "주황색 = 손댈 수 있는 것. 가까이 가서 F";
            ObjectivePos = CityGenerator.SafehousePos;
        }
        else if (gen.Current == Generator.State.Broken)
        {
            Objective = $"발전기 수리: 연료통 {(hasFuel ? 1 : 0)}/1 · 부품 {Mathf.Min(parts, gen.partsToRepair)}/{gen.partsToRepair}";
            if (!hasFuel)
            {
                Hint = CityGenerator.FuelHintText;
                ObjectivePos = CityGenerator.FuelHintPos;
            }
            else if (parts < gen.partsToRepair)
            {
                Hint = "안드로이드를 쓰러뜨려 분해(F)하면 부품이 나온다";
                ObjectivePos = null;
            }
            else
            {
                Hint = "거점으로 돌아가 발전기를 고치자";
                ObjectivePos = CityGenerator.SafehousePos;
            }
        }
        else if (!night && gm.Day == 1)
        {
            Objective = "해가 지기 전(18:30)에 거점으로 돌아오자";
            Hint = $"남은 시간 약 {Mathf.Max(0f, 18.5f - h):0.0}시간 · 그 전에 식량과 연료를 더 모아 두면 좋다";
            ObjectivePos = CityGenerator.SafehousePos;
        }
        else
        {
            float left = h >= 18.5f ? (24f - h) + 6f : 6f - h;
            Objective = $"아침 6시까지 버티자 (약 {left:0.0}시간)";
            Hint = gen.Current == Generator.State.On
                ? $"발전기 켜짐 — 따뜻하지만 신호가 퍼진다 (연료 {gen.FuelHours:0.0}시간)"
                : "발전기 꺼짐 — 숨기 좋지만 춥다";
            ObjectivePos = CityGenerator.SafehousePos;
        }

        // 발전기를 못 고쳤어도, 살아서 아침을 맞으면 성공
        if (!Won && gm.Day >= 2 && h >= 6f && h < 18.5f && !stats.IsDead)
        {
            Won = true;
            showWin = true;
            Time.timeScale = 0f;
        }

        if (showWin)
        {
            var kb = Keyboard.current;
            if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
            {
                showWin = false;
                Time.timeScale = 1f;
            }
        }
    }

    void OnDisable() { if (showWin) Time.timeScale = 1f; }

    void InitStyles()
    {
        if (title != null) return;
        title = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, wordWrap = true };
        title.normal.textColor = Color.white;
        body = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
        body.normal.textColor = new Color(1f, 1f, 1f, 0.8f);
        panel = new GUIStyle(GUI.skin.label) { fontSize = 44, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        panel.normal.textColor = Color.white;
    }

    void OnGUI()
    {
        if (Objective == null && Status == null && !showWin) return;
        InitStyles();
        UIScale.Apply();

        float x = 20, y = 150, w = 330;
        if (Objective != null)
        {
            float hgt = Hint != null ? 64 : 40;
            GUI.color = new Color(0, 0, 0, 0.55f);
            GUI.DrawTexture(new Rect(x - 6, y - 4, w + 12, hgt), Texture2D.whiteTexture);
            GUI.color = Kit.Hazard;
            GUI.DrawTexture(new Rect(x - 6, y - 4, 4, hgt), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(x + 4, y, w, 22), "목표  " + Objective, title);
            if (Hint != null) GUI.Label(new Rect(x + 4, y + 24, w, 36), Hint, body);
            y += hgt + 6;
        }
        if (Status != null)
        {
            GUI.color = Status.StartsWith("따뜻") ? new Color(1f, 0.85f, 0.5f) : new Color(0.7f, 0.85f, 1f);
            GUI.Label(new Rect(x, y, w, 22), Status, body);
            GUI.color = Color.white;
        }

        if (showWin)
        {
            GUI.color = new Color(0, 0, 0, 0.72f);
            GUI.DrawTexture(new Rect(0, 0, UIScale.W, UIScale.H), Texture2D.whiteTexture);
            GUI.color = Kit.WarmLight;
            GUI.Label(new Rect(0, UIScale.H * 0.3f, UIScale.W, 60), "첫날 밤을 버텼다", panel);
            GUI.color = Color.white;
            int earned = wallet != null ? wallet.TotalEarned : 0;
            var big = new GUIStyle(body) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(0, UIScale.H * 0.3f + 70, UIScale.W, 30),
                $"쓰러뜨린 안드로이드 {Kills}기 · 모은 부품 {earned}개 · 체력 {stats.health:0}", big);
            GUI.Label(new Rect(0, UIScale.H * 0.3f + 110, UIScale.W, 30), "구미의 아침이 밝았다.   [Enter] 계속하기", big);
        }
    }
}
