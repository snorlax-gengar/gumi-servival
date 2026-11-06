using System.Collections;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// F9로 플레이 녹화 시작/정지. 프로젝트 폴더의 Recordings/날짜_시간/ 에 프레임(JPG)을 저장한다.
/// 게임을 멈추지 않는다: 화면을 GPU에서 비동기로 읽어 오고(AsyncGPUReadback), JPG 압축·저장은 다른 스레드에서 한다.
/// 실제 시간 기준 30fps로 찍고, PC가 느려 프레임을 놓치면 직전 장면을 한 장 더 저장해 영상 길이를 맞춘다.
/// 프레임을 mp4로 묶는 건 ffmpeg로 따로 한다.
/// HUD가 자동으로 붙인다.
/// </summary>
public class GameRecorder : MonoBehaviour
{
    public int fps = 30;
    [Range(50, 100)] public int jpgQuality = 85;
    public int maxWidth = 1600;   // 화면이 더 크면 이 너비로 줄여서 저장

    public bool IsRecording { get; private set; }
    public static string LastFolder { get; private set; }

    string folder;
    int frame;          // 다음에 쓸 파일 번호
    int pending;        // GPU에서 읽어오는 중인 요청 수
    float startTime, nextShot;
    Coroutine loop;
    RenderTexture full, small;

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
        if (!SystemInfo.supportsAsyncGPUReadback) { HUD.ShowMessage("이 PC에서는 녹화를 지원하지 않는다", 3f); return; }
        string root = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Recordings");
        folder = Path.Combine(root, System.DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(folder);
        frame = 0;
        startTime = nextShot = Time.unscaledTime;
        IsRecording = true;
        LastFolder = folder;
        loop = StartCoroutine(CaptureLoop());
        Debug.Log($"[Gumi] 녹화 시작: {folder}");
    }

    void StopRecording()
    {
        IsRecording = false;
        if (loop != null) StopCoroutine(loop);
        File.WriteAllText(Path.Combine(folder, "info.txt"), $"fps={fps}\nframes={frame}\n");
        Debug.Log($"[Gumi] 녹화 종료: {frame}프레임 ({frame / (float)fps:0.0}초) → {folder}");
        HUD.ShowMessage($"녹화 저장 완료 ({frame / (float)fps:0}초)", 3f);
    }

    void OnDisable()
    {
        if (IsRecording) StopRecording();
        if (full != null) { full.Release(); full = null; }
        if (small != null) { small.Release(); small = null; }
    }

    void EnsureTargets()
    {
        int sw = Screen.width, sh = Screen.height;
        if (full == null || full.width != sw || full.height != sh)
        {
            if (full != null) full.Release();
            full = new RenderTexture(sw, sh, 0, RenderTextureFormat.ARGB32);
        }
        int w = Mathf.Min(sw, maxWidth) & ~1;
        int h = Mathf.RoundToInt(sh * (w / (float)sw)) & ~1;
        if (small == null || small.width != w || small.height != h)
        {
            if (small != null) small.Release();
            small = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
        }
    }

    IEnumerator CaptureLoop()
    {
        var wait = new WaitForEndOfFrame();
        float step = 1f / fps;
        while (IsRecording)
        {
            yield return wait;
            float now = Time.unscaledTime;
            if (now < nextShot) continue;
            // 놓친 칸 수만큼 같은 장면을 반복 저장 (영상이 실제 시간과 같은 길이가 되게)
            int copies = 1 + Mathf.Min(10, Mathf.FloorToInt((now - nextShot) / step));
            nextShot += copies * step;
            if (pending > 6) continue;   // GPU·디스크가 못 따라오면 이번 장은 건너뜀

            EnsureTargets();
            ScreenCapture.CaptureScreenshotIntoRenderTexture(full);
            // D3D 등에서는 위아래가 뒤집혀 나온다
            if (SystemInfo.graphicsUVStartsAtTop) Graphics.Blit(full, small, new Vector2(1, -1), new Vector2(0, 1));
            else Graphics.Blit(full, small);

            int first = frame;
            frame += copies;
            int w = small.width, h = small.height, q = jpgQuality;
            string dir = folder;
            pending++;
            AsyncGPUReadback.Request(small, 0, TextureFormat.RGBA32, req =>
            {
                pending--;
                if (req.hasError) return;
                byte[] raw = req.GetData<byte>().ToArray();
                Task.Run(() =>
                {
                    byte[] jpg = ImageConversion.EncodeArrayToJPG(raw, GraphicsFormat.R8G8B8A8_UNorm, (uint)w, (uint)h, 0, q);
                    for (int i = 0; i < copies; i++)
                        File.WriteAllBytes(Path.Combine(dir, $"frame_{first + i:D6}.jpg"), jpg);
                });
            });
        }
    }

    void OnGUI()
    {
        if (!IsRecording) return;
        UIScale.Apply();
        // 녹화 표시 (작게, 영상에도 들어감)
        float t = Time.unscaledTime - startTime;
        bool blink = Mathf.FloorToInt(t * 2) % 2 == 0;
        GUI.color = blink ? new Color(1f, 0.2f, 0.2f) : new Color(1f, 0.2f, 0.2f, 0.4f);
        GUI.Label(new Rect((UIScale.W - 120) / 2f, 44, 160, 24), $"● REC {t:0}s");
        GUI.color = Color.white;
    }
}
