using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Esc 메뉴 + 설정 + 조작법 카드 + 처음 하는 사람 힌트.
/// - Esc: 일시정지 (계속하기 · 시점 · 마우스 감도 · 소리 · 화면 흔들림 · 타격 멈춤 · 조작법 · 처음부터 · 끝내기)
/// - F1: 조작법 카드. 처음 실행하면 자동으로 한 번 보여 준다.
/// - 설정은 PlayerPrefs에 저장돼 다음에도 유지.
/// GameManager가 자동으로 붙인다.
/// </summary>
public class PauseMenu : MonoBehaviour
{
    public static bool IsPaused { get; private set; }
    bool showControls;
    float controlsAutoClose;
    GUIStyle title, body, key, btn;
    IsometricCamera cam;

    const string PrefSens = "gumi_sens", PrefVol = "gumi_vol", PrefShake = "gumi_shake", PrefStop = "gumi_hitstop", PrefIntro = "gumi_intro_v2", PrefView = "gumi_view";

    void Start()
    {
        cam = FindFirstObjectByType<IsometricCamera>();
        if (cam != null) cam.mouseSensitivity = PlayerPrefs.GetFloat(PrefSens, cam.mouseSensitivity);
        Sfx.Volume = PlayerPrefs.GetFloat(PrefVol, 1f);
        AudioListener.volume = Sfx.Volume;
        IsometricCamera.ShakeEnabled = PlayerPrefs.GetInt(PrefShake, 1) == 1;
        Sfx.HitStopEnabled = PlayerPrefs.GetInt(PrefStop, 1) == 1;
        if (cam != null && PlayerPrefs.GetInt(PrefView, 0) == 1) cam.SetShoulder();
        if (PlayerPrefs.GetInt(PrefIntro, 0) == 0)
        {
            showControls = true;
            controlsAutoClose = Time.unscaledTime + 25f;
            PlayerPrefs.SetInt(PrefIntro, 1);
        }
        IsPaused = false;
    }

    void OnDestroy() { IsPaused = false; Time.timeScale = 1f; }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.escapeKey.wasPressedThisFrame)
        {
            if (showControls && !IsPaused) showControls = false;
            else if (Inventory.Open) { /* 가방은 Tab으로 닫는다 */ }
            else SetPaused(!IsPaused);
        }
        if (kb.f1Key.wasPressedThisFrame) { showControls = !showControls; controlsAutoClose = 0f; }
        if (showControls && !IsPaused && controlsAutoClose > 0f && Time.unscaledTime > controlsAutoClose) showControls = false;
        // 조작법 카드는 아무 이동 키나 누르면 닫힘
        if (showControls && !IsPaused && controlsAutoClose > 0f && (kb.wKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame))
            showControls = false;
        UpdateHints();
    }

    void SetPaused(bool p)
    {
        IsPaused = p;
        Time.timeScale = p ? 0f : 1f;
        if (!p) showControls = false;
    }

    // ---------------- 처음 하는 사람 힌트 (한 번씩) ----------------

    bool hAndroid, hLoot, hStamina, hHealth, hChase, hHunger, hInside;
    float hintTimer;

    void UpdateHints()
    {
        hintTimer -= Time.unscaledDeltaTime;
        if (hintTimer > 0f || IsPaused) return;
        hintTimer = 0.5f;
        var p = GameObject.FindGameObjectWithTag("Player");
        if (p == null) return;
        var st = p.GetComponent<PlayerStats>();
        var inter = p.GetComponent<Interactor>();
        Vector3 pp = p.transform.position;

        if (!hLoot && inter != null && inter.Current != null)
        {
            hLoot = true;
            HUD.ShowMessage("주황색 = 손댈 수 있는 것.  [E] 뒤지기", 4f);
            return;
        }
        if (!hAndroid || !hChase)
        {
            foreach (var ai in FindObjectsByType<AndroidAI>(FindObjectsSortMode.None))
            {
                if (ai.CurrentState == AndroidAI.State.Disabled) continue;
                float d = Vector3.Distance(ai.transform.position, pp);
                if (!hChase && ai.CurrentState == AndroidAI.State.Chase && d < 20f)
                {
                    hChase = true;
                    HUD.ShowMessage("쫓긴다!  [Shift] 달려서 따돌리거나  [우클릭] 밀쳐서 틈을 만들자.  차·바리케이드는 [Space]로 넘는다", 6f);
                    return;
                }
                if (!hAndroid && d < 18f)
                {
                    hAndroid = true;
                    HUD.ShowMessage("안드로이드다.  [C] 웅크리고 등 뒤로 다가가 치면 기습 — 피해 4배", 6f);
                    return;
                }
            }
        }
        if (st == null) return;
        if (!hStamina && st.stamina < 25f) { hStamina = true; HUD.ShowMessage("숨이 차다. 달리기를 멈추면 스태미나가 돌아온다", 4f); return; }
        if (!hHealth && st.health < 65f) { hHealth = true; HUD.ShowMessage("다쳤다.  [H] 붕대 바로 쓰기  ·  [Tab] 가방", 5f); return; }
        if (!hHunger && (st.hunger < 45f || st.thirst < 45f)) { hHunger = true; HUD.ShowMessage("배고프고 목마르다. 숫자키나 [Tab] 가방에서 먹고 마시자", 5f); return; }
    }

    // ---------------- 그리기 ----------------

    void Styles()
    {
        if (title != null) return;
        title = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        title.normal.textColor = Color.white;
        body = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
        body.normal.textColor = new Color(1, 1, 1, 0.9f);
        key = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
        key.normal.textColor = Kit.Hazard;
        btn = new GUIStyle(GUI.skin.button) { fontSize = 16, fixedHeight = 36 };
    }

    static readonly string[,] Controls =
    {
        { "WASD", "이동" }, { "마우스", "둘러보기" }, { "Shift", "달리기" }, { "C / Ctrl", "웅크리기 (C는 켜고 끄기)" },
        { "Space", "점프 · 낮은 장애물 넘기" }, { "좌클릭", "공격 (등 뒤에서 치면 기습)" }, { "우클릭", "밀치기 — 안드로이드를 비틀거리게" },
        { "E", "뒤지기 · 문 · 발전기 · 분해" }, { "휠", "무기 바꾸기" }, { "1~9", "아이템 쓰기 · 장착" }, { "H", "붕대 바로 쓰기" },
        { "Tab", "가방 (클릭으로 사용)" }, { "L", "손전등 (불빛은 눈에 띈다)" }, { "V", "1인칭 ↔ 어깨 시점" }, { "M", "전체 지도" },
        { "Alt", "커서 잠깐 보기" }, { "F9", "녹화" }, { "Esc", "메뉴 · 설정" }, { "F1", "이 조작법" },
    };

    void OnGUI()
    {
        if (!IsPaused && !showControls && !Inventory.Open) return;
        Styles();
        UIScale.Apply();
        float W = UIScale.W, H = UIScale.H;

        if (Inventory.Open && !IsPaused)
        {
            GUI.color = new Color(0, 0, 0, 0.55f);
            GUI.DrawTexture(new Rect(10, H * 0.5f - 40, 330, 30), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(20, H * 0.5f - 38, 320, 26), "가방 — 아래 목록을 클릭해 쓰기·장착 · Tab 닫기", body);
            if (!showControls) return;
        }

        GUI.color = new Color(0, 0, 0, IsPaused ? 0.72f : 0.6f);
        GUI.DrawTexture(new Rect(0, 0, W, H), Texture2D.whiteTexture);
        GUI.color = Color.white;

        if (showControls)
        {
            float cw = 620, ch = 44 + Controls.GetLength(0) * 24 + 40;
            var r = new Rect((W - cw) / 2, (H - ch) / 2, cw, ch);
            GUI.Label(new Rect(r.x, r.y, cw, 40), "조작법", title);
            for (int i = 0; i < Controls.GetLength(0); i++)
            {
                float y = r.y + 46 + i * 24;
                GUI.Label(new Rect(r.x, y, 200, 24), Controls[i, 0], key);
                GUI.Label(new Rect(r.x + 220, y, 400, 24), Controls[i, 1], body);
            }
            string close = IsPaused ? "" : "  ·  이동하면 닫힘";
            GUI.Label(new Rect(r.x, r.yMax - 30, cw, 24), "F1로 다시 보기 · Esc 메뉴" + close, new GUIStyle(body) { alignment = TextAnchor.MiddleCenter });
            if (IsPaused && GUI.Button(new Rect((W - 200) / 2, r.yMax + 8, 200, 36), "돌아가기", btn)) showControls = false;
            return;
        }

        // ---- 일시정지 메뉴 ----
        float mw = 360, x = (W - mw) / 2, y0 = H * 0.18f;
        GUI.Label(new Rect(x, y0, mw, 40), "일시정지", title);
        float y1 = y0 + 56;
        if (GUI.Button(new Rect(x, y1, mw, 36), "계속하기", btn)) SetPaused(false);
        y1 += 44;
        string view = IsometricCamera.FirstPerson ? "시점: 1인칭  (누르면 어깨 시점)" : "시점: 어깨  (누르면 1인칭)";
        if (cam != null && GUI.Button(new Rect(x, y1, mw, 36), view, btn))
        {
            if (IsometricCamera.FirstPerson) cam.SetShoulder(); else cam.SetFirstPerson(true);
            PlayerPrefs.SetInt(PrefView, IsometricCamera.Shoulder ? 1 : 0);
        }
        y1 += 50;

        if (cam != null)
        {
            GUI.Label(new Rect(x, y1, mw, 22), $"마우스 감도  {cam.mouseSensitivity * 10f:0.0}", body);
            float s = GUI.HorizontalSlider(new Rect(x, y1 + 24, mw, 20), cam.mouseSensitivity, 0.02f, 0.4f);
            if (!Mathf.Approximately(s, cam.mouseSensitivity)) { cam.mouseSensitivity = s; PlayerPrefs.SetFloat(PrefSens, s); }
            y1 += 52;
        }
        GUI.Label(new Rect(x, y1, mw, 22), $"소리  {Sfx.Volume * 100f:0}%", body);
        float v = GUI.HorizontalSlider(new Rect(x, y1 + 24, mw, 20), Sfx.Volume, 0f, 1f);
        if (!Mathf.Approximately(v, Sfx.Volume)) { Sfx.Volume = v; AudioListener.volume = v; PlayerPrefs.SetFloat(PrefVol, v); }
        y1 += 52;

        bool sh = GUI.Toggle(new Rect(x, y1, mw, 26), IsometricCamera.ShakeEnabled, "  화면 흔들림");
        if (sh != IsometricCamera.ShakeEnabled) { IsometricCamera.ShakeEnabled = sh; PlayerPrefs.SetInt(PrefShake, sh ? 1 : 0); }
        y1 += 30;
        bool hs = GUI.Toggle(new Rect(x, y1, mw, 26), Sfx.HitStopEnabled, "  때릴 때 잠깐 멈춤 (타격감)");
        if (hs != Sfx.HitStopEnabled) { Sfx.HitStopEnabled = hs; PlayerPrefs.SetInt(PrefStop, hs ? 1 : 0); }
        y1 += 40;

        if (GUI.Button(new Rect(x, y1, mw, 36), "조작법 보기 (F1)", btn)) showControls = true;
        y1 += 44;
        if (GUI.Button(new Rect(x, y1, mw, 36), "처음부터 다시", btn))
        {
            SetPaused(false);
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
        y1 += 44;
        if (GUI.Button(new Rect(x, y1, mw, 36), "게임 끝내기", btn))
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
