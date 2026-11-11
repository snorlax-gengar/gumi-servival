using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 이야기 자막(주인공의 독백·무전). 화면 아래 가운데에 한 줄씩 차례로 나온다.
/// Story.Say("열쇠", "문장") — 같은 열쇠는 한 판에 한 번만 나온다 (열쇠가 null이면 매번).
/// 게임 안내(HUD.ShowMessage)와 따로 놀아서 서로 덮어쓰지 않는다. GameManager가 자동으로 만든다.
/// </summary>
public class Story : MonoBehaviour
{
    static Story inst;
    static readonly HashSet<string> said = new HashSet<string>();
    readonly Queue<(string text, float seconds, bool radio)> queue = new Queue<(string, float, bool)>();
    string current; float timer, total; bool currentRadio;
    GUIStyle style;

    public static void Ensure()
    {
        if (inst != null) return;
        said.Clear();   // 새 판 (도메인 리로드가 꺼져 있어도 초기화)
        inst = new GameObject("Story").AddComponent<Story>();
    }

    /// <summary>이미 나온 대사인가</summary>
    public static bool Said(string key) => said.Contains(key);

    public static void Say(string key, string text, float seconds = 4.5f, bool radio = false)
    {
        if (inst == null) return;
        if (key != null && !said.Add(key)) return;
        inst.queue.Enqueue((text, seconds, radio));
    }

    /// <summary>여러 줄을 차례로</summary>
    public static void Say(string key, params string[] lines)
    {
        if (inst == null) return;
        if (key != null && !said.Add(key)) return;
        foreach (var l in lines) inst.queue.Enqueue((l, Mathf.Clamp(2.2f + l.Length * 0.07f, 3f, 6.5f), false));
    }

    void Update()
    {
        if (timer > 0f) { timer -= Time.unscaledDeltaTime; if (timer <= 0f) current = null; }
        if (current == null && queue.Count > 0 && !PauseMenu.IsPaused)
        {
            var q = queue.Dequeue();
            current = q.text; timer = total = q.seconds; currentRadio = q.radio;
        }
    }

    void OnGUI()
    {
        if (current == null) return;
        UIScale.Apply();
        if (style == null)
            style = new GUIStyle(GUI.skin.label) { fontSize = 17, alignment = TextAnchor.MiddleCenter, wordWrap = true, fontStyle = FontStyle.Italic };
        float a = Mathf.Clamp01(Mathf.Min((total - timer) / 0.35f, timer / 0.5f));
        float w = Mathf.Min(760f, UIScale.W - 80f), h = style.CalcHeight(new GUIContent(current), w) + 14f;
        var r = new Rect((UIScale.W - w) / 2f, UIScale.H * 0.80f - h, w, h);
        GUI.color = new Color(0, 0, 0, 0.5f * a);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = currentRadio ? new Color(0.65f, 1f, 0.75f, a) : new Color(1f, 0.97f, 0.9f, a);
        GUI.Label(r, current, style);
        GUI.color = Color.white;
    }
}
