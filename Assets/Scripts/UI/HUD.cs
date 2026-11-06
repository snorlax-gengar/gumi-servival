using UnityEngine;

/// <summary>
/// 프로토타입용 HUD (IMGUI). UI 캔버스 세팅 없이 바로 보인다.
/// 상태 바, 시계, 인벤토리, 상호작용 안내, 메시지, 사망 화면.
/// 플레이어 오브젝트에 붙인다. 정식 UI는 버티컬 슬라이스 단계에서 교체.
/// </summary>
public class HUD : MonoBehaviour
{
    static string message;
    static float messageTimer;

    /// <summary>인벤토리 버튼이 차지하는 화면 영역 (위가 0인 픽셀 좌표)</summary>
    public static Rect InventoryScreenRect;

    public static void ShowMessage(string msg, float seconds = 2.5f)
    {
        message = msg;
        messageTimer = seconds;
    }

    [Tooltip("한글이 □□로 깨지면 한글 폰트(.ttf, 예: 나눔고딕)를 넣는다")]
    public Font koreanFont;

    PlayerStats stats;
    Inventory inv;
    Interactor interactor;
    Wallet wallet;
    GUIStyle label, big, box, clockStyle;
    Texture2D white;

    void Awake()
    {
        stats = GetComponent<PlayerStats>();
        inv = GetComponent<Inventory>();
        interactor = GetComponent<Interactor>();
        wallet = GetComponent<Wallet>();
        if (GetComponent<GameRecorder>() == null) gameObject.AddComponent<GameRecorder>();
        white = Texture2D.whiteTexture;
    }

    void Update()
    {
        if (messageTimer > 0) messageTimer -= Time.deltaTime;
    }

    void InitStyles()
    {
        if (label != null) return;
        if (koreanFont == null)
        {
            // 윈도우 기본 한글 폰트(맑은 고딕)를 우선 사용
            try { koreanFont = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "NanumGothic", "Apple SD Gothic Neo" }, 16); }
            catch { koreanFont = null; }
        }
        if (koreanFont != null) { GUI.skin.font = koreanFont; }
        label = new GUIStyle(GUI.skin.label) { fontSize = 16 };
        label.normal.textColor = Color.white;
        clockStyle = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        big = new GUIStyle(label) { fontSize = 40, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        box = new GUIStyle(GUI.skin.box);
    }

    void OnGUI()
    {
        InitStyles();
        UIScale.Apply();
        if (stats == null) return;

        // --- 상태 바 ---
        float x = 20, y = 20;
        Bar(ref y, x, "체력", stats.health, new Color(0.85f, 0.25f, 0.25f));
        Bar(ref y, x, "배고픔", stats.hunger, new Color(0.9f, 0.6f, 0.2f));
        Bar(ref y, x, "갈증", stats.thirst, new Color(0.3f, 0.6f, 0.95f));
        Bar(ref y, x, "스태미나", stats.stamina, new Color(0.4f, 0.85f, 0.4f));

        // --- 재화 ---
        if (wallet != null)
        {
            GUI.color = Kit.Loot;
            GUI.DrawTexture(new Rect(x + 2, y + 3, 12, 12), white);
            GUI.color = Color.white;
            GUI.Label(new Rect(x + 22, y - 2, 200, 22), $"부품 {wallet.Parts}개", label);
            y += 24;
        }

        // --- 자세 표시: 웅크리면 화면 아래 가운데에 (C로 켜 둔 건지 알 수 있게) ---
        var pc = stats.GetComponent<PlayerController>();
        if (pc != null && pc.IsCrouching)
        {
            GUI.color = new Color(0, 0, 0, 0.5f);
            GUI.DrawTexture(new Rect(UIScale.W / 2 - 90, UIScale.H - 70, 180, 26), white);
            GUI.color = Kit.Hazard;
            GUI.Label(new Rect(UIScale.W / 2 - 90, UIScale.H - 70, 180, 26), "웅크림 — C로 일어서기", new GUIStyle(label) { alignment = TextAnchor.MiddleCenter });
            GUI.color = Color.white;
        }

        // --- 시계 ---
        if (GameManager.Instance != null)
            GUI.Label(new Rect(UIScale.W - 240, 16, 220, 30), GameManager.Instance.ClockText(), clockStyle);

        // --- 인벤토리 ---
        if (inv != null)
        {
            float iy = UIScale.H - 40 - inv.stacks.Count * 26;
            // 인벤토리 버튼 영역(실제 화면 좌표) — 여기를 클릭하면 공격하지 않는다
            float sc = UIScale.S;
            InventoryScreenRect = new Rect(20 * sc, iy * sc, 300 * sc, Mathf.Max(0, inv.stacks.Count * 26) * sc);
            GUI.Label(new Rect(20, iy - 28, 400, 26),
                $"가방 {inv.CurrentWeight:0.0} / {inv.maxWeight:0.0} kg   (숫자키로 사용/장착)", label);
            for (int i = 0; i < inv.stacks.Count; i++)
            {
                var s = inv.stacks[i];
                string eq = inv.EquippedWeapon == s.item ? "  [장착]" : "";
                if (GUI.Button(new Rect(20, iy + i * 26, 300, 24), $"{i + 1}. {s.item.displayName} x{s.count}{eq}"))
                    inv.Use(i);
            }
        }

        // --- 상호작용 안내 ---
        if (interactor != null && interactor.Current != null && !stats.IsDead)
        {
            var c = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(0, UIScale.H * 0.62f, UIScale.W, 30), interactor.Current.Prompt, c);
        }

        // --- 메시지 ---
        if (messageTimer > 0 && !string.IsNullOrEmpty(message))
        {
            var c = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = 18 };
            GUI.Label(new Rect(0, UIScale.H * 0.25f, UIScale.W, 60), message, c);
        }

        // --- 사망 화면 ---
        if (stats.IsDead)
        {
            GUI.color = new Color(0, 0, 0, 0.75f);
            GUI.DrawTexture(new Rect(0, 0, UIScale.W, UIScale.H), white);
            GUI.color = Color.white;
            float days = GameManager.Instance != null ? GameManager.Instance.DaysSurvived : 0;
            int d = Mathf.FloorToInt(days);
            int h = Mathf.FloorToInt((days - d) * 24f);
            GUI.Label(new Rect(0, UIScale.H * 0.4f, UIScale.W, 60), "당신은 사망했습니다", big);
            GUI.Label(new Rect(0, UIScale.H * 0.4f + 60, UIScale.W, 40),
                $"구미에서 {d}일 {h}시간 동안 생존했습니다.   [R] 다시 시작",
                new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = 20 });
        }
    }

    void Bar(ref float y, float x, string name, float value, Color color)
    {
        const float w = 200, h = 16;
        GUI.Label(new Rect(x, y - 2, 80, 22), name, label);
        GUI.color = new Color(0, 0, 0, 0.5f);
        GUI.DrawTexture(new Rect(x + 80, y, w, h), white);
        GUI.color = color;
        GUI.DrawTexture(new Rect(x + 80, y, w * Mathf.Clamp01(value / 100f), h), white);
        GUI.color = Color.white;
        y += 24;
    }
}
