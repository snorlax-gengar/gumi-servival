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
        if (kb != null && kb.f11Key.wasPressedThisFrame && !running) StartCoroutine(Walk());
        // F12: 시간 1시간 건너뛰기 (밤·순찰대·아침 장면 확인용)
        if (kb != null && kb.f12Key.wasPressedThisFrame && GameManager.Instance != null) { GameManager.Instance.AdvanceHours(1f); HUD.ShowMessage("(개발) 1시간 건너뜀 → " + GameManager.Instance.ClockText(), 1.5f); }
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

    /// <summary>F11: 걷기 시험. 넓은 바닥에서 출발·정지·90도 꺾기·뒤돌기·걷다 뛰기·멈추기를 자동으로 하고 녹화한다.</summary>
    IEnumerator Walk()
    {
        var ctrl = FindAnyObjectByType<PlayerController>();
        if (ctrl == null) yield break;
        running = true;
        Vector3 home = ctrl.transform.position;
        course = new GameObject("걷기 시험장").transform;
        course.position = new Vector3(home.x, home.y + Y, home.z);
        Block(course, new Vector3(0, -0.25f, 0), new Vector3(40, 0.5f, 40), Kit.Hex("8A8E92"));
        for (int i = -4; i <= 4; i++)   // 바닥 줄무늬 (미끄러짐 확인용)
            Block(course, new Vector3(i * 4f, 0.005f, 0), new Vector3(0.1f, 0.01f, 40), Kit.Hex("5A5E62"));
        Physics.SyncTransforms();
        ctrl.Teleport(course.position + new Vector3(0, 0.05f, -8f));
        ctrl.transform.rotation = Quaternion.identity;
        var cam = FindAnyObjectByType<IsometricCamera>();
        if (cam != null) cam.SetLook(-60f, 14f);   // 비스듬히 뒤에서
        yield return new WaitForSeconds(0.8f);
        var rec = GameRecorder.Instance;
        bool recorded = rec != null && !rec.IsRecording;
        if (recorded) rec.StartRecording();

        // (방향, 달리기, 시간)
        var steps = new (Vector3 dir, bool run, float t)[]
        {
            (Vector3.zero, false, 0.6f),
            (Vector3.forward, false, 2.0f),   // 걷기 출발
            (Vector3.zero, false, 1.0f),      // 정지
            (Vector3.forward, false, 1.2f),
            (Vector3.right, false, 1.5f),     // 90도 꺾기
            (Vector3.left, false, 1.5f),      // 뒤돌기
            (Vector3.forward, false, 0.8f),
            (Vector3.forward, true, 2.0f),    // 걷다가 뛰기
            (Vector3.right, true, 1.2f),      // 뛰다가 꺾기
            (Vector3.zero, false, 1.5f),      // 뛰다 멈추기
            (Vector3.back, false, 0.05f),     // 뒤로 피하기 (Q)
            (Vector3.zero, false, 1.2f),
            (Vector3.forward, false, 0.6f),
            (Vector3.left, false, 0.05f),      // 걷다가 옆으로 피하기
            (Vector3.zero, false, 1.2f),
        };
        foreach (var st in steps)
        {
            float t0 = Time.time;
            while (Time.time - t0 < st.t)
            {
                ctrl.DebugMove = st.dir == Vector3.zero ? Vector3.zero : st.dir;
                if (st.t < 0.1f) ctrl.DebugDodge = true;
                ctrl.DebugRun = st.run;
                yield return null;
            }
        }
        ctrl.DebugMove = null; ctrl.DebugRun = false;
        yield return new WaitForSeconds(0.5f);
        if (recorded && rec.IsRecording) rec.StopRecording();
        ctrl.Teleport(home);
        Destroy(course.gameObject);
        running = false;
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
        // ⑥ 옆으로 주차된 차: 차 넘기
        BuildingKit.Car(course, new Vector3(0, 0, 48f), 90f, new System.Random(3));
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

        float[] obstacles = { 6f, 14f, 22.5f - 1.5f, 31f, 40f - 2f, 48f - 0.9f };
        int next = 0;
        float t0 = Time.time;
        while (Time.time - t0 < 30f)
        {
            float z = ctrl.transform.position.z - course.position.z;
            if (z > 54f) break;
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
