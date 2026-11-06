using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 개발용 파쿠르 시험장 (에디터에서만). F10을 누르면 하늘 위(60m)에 장애물 코스를 만들고
/// 플레이어를 그 위에 옮긴 뒤, 자동으로 달려가며 넘기·허들·올라서기·담 넘기·기어오르기·뛰어내리기를 한 번씩 해 보고 녹화한다.
/// 끝나면 원래 자리로 돌려 놓는다.
/// </summary>
public class ParkourTest : MonoBehaviour
{
    const float Y = 60f;
    Transform course;
    bool running;

    void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var kb = Keyboard.current;
        if (kb != null && kb.f10Key.wasPressedThisFrame && !running) StartCoroutine(Run());
#endif
    }

    static GameObject Block(Transform parent, Vector3 center, Vector3 size, Color c)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.transform.SetParent(parent, false);
        g.transform.localPosition = center;
        g.transform.localScale = size;
        g.GetComponent<Renderer>().sharedMaterial = Kit.Mat(c);
        return g;
    }

    IEnumerator Run()
    {
        var ctrl = FindAnyObjectByType<PlayerController>();
        if (ctrl == null) yield break;
        running = true;
        Vector3 home = ctrl.transform.position;

        // 코스: +Z 방향으로 늘어선 장애물 (로컬 z = 거리)
        course = new GameObject("파쿠르 시험장").transform;
        course.position = new Vector3(home.x, home.y + Y, home.z);
        Color floor = Kit.Hex("8A8E92"), ob = Kit.Hex("C98A3A"), wall = Kit.Hex("9A6B5A");
        Block(course, new Vector3(0, -0.25f, 26), new Vector3(8, 0.5f, 64), floor);
        Block(course, new Vector3(0, 0.45f, 6), new Vector3(3, 0.9f, 0.4f), ob);        // ① 얇은 상자 0.9m: 서서 넘기
        Block(course, new Vector3(0, 0.45f, 14), new Vector3(3, 0.9f, 0.4f), ob);       // ② 얇은 상자 0.9m: 달리며 허들
        Block(course, new Vector3(0, 0.6f, 22.5f), new Vector3(3, 1.2f, 3f), ob);       // ③ 두꺼운 1.2m: 올라서기 → 걸어서 내려오기
        Block(course, new Vector3(0, 0.9f, 31), new Vector3(3, 1.8f, 0.3f), wall);      // ④ 얇은 담 1.8m: 담 넘기
        Block(course, new Vector3(0, 0.95f, 40f), new Vector3(3, 1.9f, 4f), wall);      // ⑤ 두꺼운 1.9m: 기어오르기 → 뛰어내리기
        // 떨어지지 않게 양옆 난간
        Block(course, new Vector3(-4.1f, 0.5f, 26), new Vector3(0.2f, 1f, 64), floor);
        Block(course, new Vector3(4.1f, 0.5f, 26), new Vector3(0.2f, 1f, 64), floor);
        Physics.SyncTransforms();

        ctrl.Teleport(course.position + new Vector3(0, 0.05f, 0));
        ctrl.transform.rotation = Quaternion.identity;
        var cam = FindAnyObjectByType<IsometricCamera>();
        if (cam != null) cam.SetLook(0f, 12f);   // 실제 플레이처럼 등 뒤에서 본다
        yield return new WaitForSeconds(0.8f);

        var rec = GameRecorder.Instance;
        bool recorded = rec != null && !rec.IsRecording;
        if (recorded) rec.StartRecording();
        HUD.ShowMessage("파쿠르 시험 중… (F10)", 2f);

        float[] obstacles = { 6f, 14f, 22.5f - 1.5f, 31f, 40f - 2f };
        int next = 0;
        float t0 = Time.time;
        while (Time.time - t0 < 30f)
        {
            float z = ctrl.transform.position.z - course.position.z;
            if (z > 50f) break;
            ctrl.DebugMove = Vector3.forward;
            ctrl.DebugRun = z > 8f && z < 15f;   // ② 허들 구간만 달린다
            if (next < obstacles.Length && !ctrl.IsTraversing)
            {
                float dist = obstacles[next] - z;
                if (dist < (ctrl.DebugRun ? 1.4f : 0.9f) && dist > 0f) { ctrl.DebugJump = true; next++; }
                else if (dist <= 0f) next++;
            }
            yield return null;
        }
        ctrl.DebugMove = null; ctrl.DebugRun = false;
        yield return new WaitForSeconds(1f);
        if (recorded && rec.IsRecording) rec.StopRecording();
        ctrl.Teleport(home);
        Destroy(course.gameObject);
        running = false;
    }
}
