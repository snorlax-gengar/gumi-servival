using System.Collections;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// F9로 플레이 녹화 시작/정지. 프로젝트 폴더의 Recordings/날짜_시간/ 에 프레임(JPG)을 저장한다.
/// 녹화 중에는 게임 시간을 30fps로 고정(Time.captureFramerate)해서 PC가 느려도 영상은 매끄럽다.
/// 프레임을 mp4로 묶는 건 ffmpeg로 따로 한다.
/// HUD가 자동으로 붙인다.
/// </summary>
public class GameRecorder : MonoBehaviour
{
    public int fps = 30;
    [Range(50, 100)] public int jpgQuality = 85;
    public int maxWidth = 1920;   // 화면이 더 크면 이 너비로 줄여서 저장

    public bool IsRecording { get; private set; }
    public static string LastFolder { get; private set; }

    string folder;
    int frame;
    float startTime;
    Coroutine loop;

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.f9Key.wasPressedThisFrame)
        {
            if (IsRecording) StopRecording(); else StartRecording();
        }
    }

    void StartRecording()
    {
        string root = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Recordings");
        folder = Path.Combine(root, System.DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(folder);
        frame = 0;
        startTime = Time.unscaledTime;
        Time.captureFramerate = fps;
        IsRecording = true;
        LastFolder = folder;
        loop = StartCoroutine(CaptureLoop());
        Debug.Log($"[Gumi] 녹화 시작: {folder}");
    }

    void StopRecording()
    {
        IsRecording = false;
        Time.captureFramerate = 0;
        if (loop != null) StopCoroutine(loop);
        File.WriteAllText(Path.Combine(folder, "info.txt"), $"fps={fps}\nframes={frame}\n");
        Debug.Log($"[Gumi] 녹화 종료: {frame}프레임 ({frame / (float)fps:0.0}초) → {folder}");
        HUD.ShowMessage($"녹화 저장 완료 ({frame / (float)fps:0}초)", 3f);
    }

    void OnDisable()
    {
        if (IsRecording) StopRecording();
    }

    IEnumerator CaptureLoop()
    {
        var wait = new WaitForEndOfFrame();
        while (IsRecording)
        {
            yield return wait;
            Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
            Texture2D save = shot;
            if (shot.width > maxWidth)
            {
                int h = Mathf.RoundToInt(shot.height * (maxWidth / (float)shot.width)) & ~1;
                save = Resize(shot, maxWidth, h);
                Destroy(shot);
            }
            byte[] jpg = save.EncodeToJPG(jpgQuality);
            Destroy(save);

            string path = Path.Combine(folder, $"frame_{frame:D6}.jpg");
            frame++;
            Task.Run(() => File.WriteAllBytes(path, jpg));
        }
    }

    static Texture2D Resize(Texture2D src, int w, int h)
    {
        var rt = RenderTexture.GetTemporary(w, h, 0);
        Graphics.Blit(src, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var dst = new Texture2D(w, h, TextureFormat.RGB24, false);
        dst.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        dst.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        return dst;
    }

    void OnGUI()
    {
        if (!IsRecording) return;
        UIScale.Apply();
        // 녹화 표시 (작게, 영상에도 들어감)
        float t = Time.unscaledTime - startTime;
        bool blink = Mathf.FloorToInt(t * 2) % 2 == 0;
        GUI.color = blink ? new Color(1f, 0.2f, 0.2f) : new Color(1f, 0.2f, 0.2f, 0.4f);
        GUI.Label(new Rect(20, 150, 200, 24), $"● REC {frame / (float)fps:0}s");
        GUI.color = Color.white;
    }
}
