using UnityEngine;

/// <summary>
/// IMGUI 화면 배율. 세로 900px 화면을 기준(1배)으로 UI 전체를 키우거나 줄인다.
/// OnGUI 맨 앞에서 Apply()를 부르고, 위치는 Screen.width/height 대신 W/H로 계산한다.
/// 아주 작은 창(에디터 Game 창 등)에서도 글씨가 읽히도록 최소 배율을 둔다.
/// </summary>
public static class UIScale
{
    public const float ReferenceHeight = 900f;
    public const float MinScale = 0.55f;
    public const float MaxScale = 2.5f;

    public static float S => Mathf.Clamp(Screen.height / ReferenceHeight, MinScale, MaxScale);
    public static float W => Screen.width / S;    // 가상 화면 너비
    public static float H => Screen.height / S;   // 가상 화면 높이

    public static void Apply()
    {
        float s = S;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
    }

    /// <summary>화면 좌표(픽셀, 아래가 0) → 가상 GUI 좌표(위가 0)</summary>
    public static Vector2 FromScreen(Vector3 screen)
    {
        float s = S;
        return new Vector2(screen.x / s, (Screen.height - screen.y) / s);
    }
}
