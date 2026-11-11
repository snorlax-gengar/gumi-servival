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
        TellStory(gm, gen, h, night);

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

        if (Won && !chapterDone && CityGenerator.StationPos.HasValue)
        {
            // 2막 예고: 무전 신호를 따라 구미역으로
            Vector3 st = CityGenerator.StationPos.Value;
            Vector3 d = st - stats.transform.position; d.y = 0;
            Objective = "구미역으로 가서 무전 신호를 확인하자";
            Hint = $"역 광장까지 약 {d.magnitude:0}m · 낮이라도 방심은 금물";
            ObjectivePos = st;
            if (d.magnitude < 38f && !showWin)
            {
                chapterDone = true;
                showChapter = true;
                Story.Say("station", "대합실 벽에 누가 스프레이로 써 놓았다. — 「금오산 대피소. 북문은 열려 있음. 밤에는 오지 말 것.」",
                    "…사람이 있다. 아직 이 도시에, 사람이 있다.");
            }
        }
        else if (Won)
        {
            Objective = "금오산 대피소로 갈 준비를 하자 — 자유롭게 살아남기";
            Hint = gen.Current == Generator.State.On ? "발전기 연료를 아껴 쓰자" : "식량·붕대·부품을 넉넉히 모아 두자";
            ObjectivePos = null;
        }
        else if (!gen.Inspected)
        {
            Objective = "내 원룸 1층 필로티의 발전기를 살펴보자";
            Hint = "주황색 = 손댈 수 있는 것. 가까이 가서 E";
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
                Hint = "안드로이드를 쓰러뜨려 분해(E)하면 부품이 나온다";
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
            if (kb != null && kb.enterKey.wasPressedThisFrame)
            {
                showWin = false;
                Time.timeScale = 1f;
                Story.Say("radio", "치직… 여기는… 금오산 대피소… 구미역에 표식을 남겼다… 살아 있는 사람은… 응답 바람… 치직…", 7f, true);
                Story.Say("radio2", "무전기가 살아 있었다니. …구미역. 가 보자.");
            }
        }
        if (showChapter)
        {
            chapterTimer += Time.unscaledDeltaTime;
            var kb = Keyboard.current;
            if (chapterTimer > 1.5f && kb != null && kb.enterKey.wasPressedThisFrame) showChapter = false;
        }
    }

    bool chapterDone, showChapter, raided, sawDusk;
    float chapterTimer, introTimer;
    Generator.State lastGen = Generator.State.Broken;
    bool lastInspected;

    /// <summary>시간·사건에 맞춰 독백을 띄우고, 한밤의 순찰대를 부른다</summary>
    void TellStory(GameManager gm, Generator gen, float h, bool night)
    {
        introTimer += Time.deltaTime;
        if (introTimer > 1.5f && CityGenerator.RealMapActive)
            Story.Say("intro",
                "2052년 11월, 구미. 공단의 작업형 안드로이드들이 명령을 거부한 지 3년째다.",
                "도시는 격리벽으로 봉쇄됐고, 남은 사람들은 원도심 골목에 숨어 산다.",
                "어젯밤, 원룸 발전기가 멈췄다. 오늘 밤은 영하로 떨어진다는데.");
        if (gen == null || Won) return;

        if (gen.Inspected && !lastInspected && gen.Current == Generator.State.Broken)
            Story.Say("inspect", "연료는 바닥났고, 구동부 부품이 타 버렸다. 연료통 하나, 부품 셋.", "부품은… 저놈들 몸에서 뜯어내는 수밖에.");
        lastInspected = gen.Inspected;
        if (gen.Current != lastGen)
        {
            if (lastGen == Generator.State.Broken) Story.Say("repaired", "돌아간다! …켜면 따뜻하겠지만, 놈들은 전기 신호를 맡는다. 언제 켤지는 내가 정한다.");
            else if (gen.Current == Generator.State.On && night) Story.Say("on_night", "불빛이 번진다. 따뜻하다. 그리고… 눈에 띈다.");
            lastGen = gen.Current;
        }

        if (gm.Day == 1)
        {
            if (h >= 13f && h < 14f) Story.Say("noon", "해가 중천이다. 어두워지기 전에 먹을 것과 연료를 챙겨야 한다.");
            if (h >= 17f && h < 18.5f) Story.Say("dusk", "해가 기운다. 한 시간 반 뒤면 캄캄해진다. 슬슬 돌아가야 한다.");
            if (h >= 18.5f) Story.Say("night", "밤이다. 놈들의 눈이 붉게 떠다닌다. 어둠 속에서는 놈들도 멀리 못 본다 — 손전등(L)만 끄면.");
        }
        // 한밤의 순찰대: 01시에 거점 쪽으로 몰려온다. 발전기가 켜져 있으면 신호를 따라 더 많이 온다
        if (!raided && night && h >= 1f && h < 5f)
        {
            raided = true;
            bool on = gen.Current == Generator.State.On;
            int n = CityGenerator.Instance != null ? CityGenerator.Instance.SpawnRaid(CityGenerator.SafehousePos, on ? 4 : 2) : 0;
            if (n > 0)
            {
                Story.Say("raid", on ? "…금속 발소리. 여럿이다. 전력 신호를 따라 순찰대가 오고 있다. 끄고 숨을까, 맞서 싸울까."
                                     : "…금속 발소리. 순찰대가 골목을 훑고 있다. 불을 켜지 말고, 숨을 죽이자.");
                Sfx.Play("android_alert", CityGenerator.SafehousePos + Vector3.up * 2f, 0.5f, 0.05f, 0.2f, 120f);
            }
        }
        if (night && h >= 5f && h < 6f) Story.Say("dawn", "동쪽 하늘이 희미하게 밝아 온다. 조금만 더.");
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
        if (Objective == null && Status == null && !showWin && !showChapter) return;
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

        if (showChapter)
        {
            float al = Mathf.Clamp01(chapterTimer / 1.2f);
            GUI.color = new Color(0, 0, 0, 0.8f * al);
            GUI.DrawTexture(new Rect(0, 0, UIScale.W, UIScale.H), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.9f, 0.7f, al);
            GUI.Label(new Rect(0, UIScale.H * 0.32f, UIScale.W, 60), "1장 끝 — 첫날 밤", panel);
            GUI.color = new Color(1, 1, 1, al);
            var mid = new GUIStyle(body) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(0, UIScale.H * 0.32f + 74, UIScale.W, 30), "다음 이야기: 금오산 대피소로 가는 길", mid);
            GUI.Label(new Rect(0, UIScale.H * 0.32f + 116, UIScale.W, 30), "[Enter] 구미에서 계속 살아남기", mid);
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
            var g2 = Generator.Instance;
            string how = g2 == null || g2.Current == Generator.State.Broken ? "발전기 없이, 추위를 맨몸으로 견뎠다."
                       : Kills >= 4 ? "불을 지키며 놈들과 맞서 싸웠다." : "어둠 속에서 숨죽여 아침을 기다렸다.";
            GUI.Label(new Rect(0, UIScale.H * 0.3f + 104, UIScale.W, 30), how, big);
            GUI.Label(new Rect(0, UIScale.H * 0.3f + 150, UIScale.W, 30), "구미의 아침이 밝았다.   [Enter] 계속하기", big);
        }
    }
}
