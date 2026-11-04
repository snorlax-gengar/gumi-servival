using UnityEngine;

/// <summary>
/// 플레이어 모델 (컨셉 아트 기준, 공단 야간조 노동자, 키 1.75m).
/// 남색 작업모·작업복, 은색 반사띠, 출입증, 올리브색 배낭과 손전등.
/// 걷기/달리기에 맞춰 팔다리를 흔들고, 웅크리면 낮아지고, 공격하면 오른팔을 휘두른다.
/// 장착한 무기(렌치/쇠파이프)를 오른손에 보여 준다.
/// 기존 캡슐(Body/Nose)은 숨긴다. PlayerController가 자동으로 붙인다.
/// </summary>
public class PlayerVisual : MonoBehaviour
{
    Transform model, hipL, hipR, armL, armR, torso, hand;
    GameObject wrenchModel, pipeModel;
    PlayerController ctrl;
    PlayerStats stats;
    Inventory inv;
    float phase, swingT = -1f, crouch;
    Vector3 lastPos;

    static readonly Color Navy = Kit.Hex("34456A");
    static readonly Color NavyDark = Kit.Hex("2A3958");
    static readonly Color Pants = Kit.Hex("3A3F45");
    static readonly Color PantsDark = Kit.Hex("2C3035");
    static readonly Color Boot = Kit.Hex("1F2124");
    static readonly Color Reflect = Kit.Hex("CFD5DB");
    static readonly Color Skin = Kit.Hex("D9A77F");
    static readonly Color SkinShade = Kit.Hex("C8946C");
    static readonly Color Hair = Kit.Hex("2A2420");
    static readonly Color Glove = Kit.Hex("5A4A3A");
    static readonly Color Pack = Kit.Hex("4A5040");
    static readonly Color PackDark = Kit.Hex("3E4436");

    void Awake()
    {
        ctrl = GetComponent<PlayerController>();
        stats = GetComponent<PlayerStats>();
        inv = GetComponent<Inventory>();

        // 기존 캡슐 모양(Body/Nose) 숨기기. 휘두르기 궤적 등 다른 자식은 그대로 둔다
        foreach (var r in GetComponentsInChildren<Renderer>())
            if (r.name == "Body" || r.name == "Nose") r.enabled = false;

        Build();
        lastPos = transform.position;
    }

    void Build()
    {
        model = Kit.Group(transform, "Model", Vector3.zero);

        hipL = Kit.Group(model, "HipL", new Vector3(-0.1f, 0.92f, 0));
        hipR = Kit.Group(model, "HipR", new Vector3(0.1f, 0.92f, 0));
        foreach (var e in new[] { (hipL, Pants), (hipR, PantsDark) })
        {
            Transform hip = e.Item1; Color c = e.Item2;
            Kit.Box(hip, new Vector3(0, -0.23f, 0), new Vector3(0.17f, 0.46f, 0.19f), c, name: "허벅지");
            Kit.Box(hip, new Vector3(0, -0.66f, 0), new Vector3(0.16f, 0.42f, 0.18f), c, name: "정강이");
            Kit.Box(hip, new Vector3(0, -0.46f, 0.095f), new Vector3(0.17f, 0.11f, 0.04f), Kit.Hex("4A5058"), name: "무릎보호대");
            Kit.Box(hip, new Vector3(0, -0.86f, 0.04f), new Vector3(0.18f, 0.13f, 0.3f), Boot, name: "작업화");
        }

        torso = Kit.Group(model, "Torso", new Vector3(0, 0.92f, 0));
        Kit.Box(torso, new Vector3(0, 0.04f, 0), new Vector3(0.42f, 0.09f, 0.24f), Kit.Hex("24272B"), name: "벨트");
        Kit.Box(torso, new Vector3(0, 0.04f, 0.121f), new Vector3(0.07f, 0.06f, 0.01f), Kit.Hex("8A8F96"), name: "버클");
        Kit.Box(torso, new Vector3(-0.11f, 0.33f, 0), new Vector3(0.22f, 0.52f, 0.26f), Navy, name: "작업복");
        Kit.Box(torso, new Vector3(0.11f, 0.33f, 0), new Vector3(0.22f, 0.52f, 0.26f), NavyDark, name: "작업복");
        Kit.Box(torso, new Vector3(0, 0.2f, 0), new Vector3(0.45f, 0.05f, 0.27f), Reflect, name: "반사띠");
        Kit.Box(torso, new Vector3(0, 0.33f, 0.131f), new Vector3(0.012f, 0.5f, 0.01f), Kit.Hex("1E2840"), name: "지퍼");
        Kit.Box(torso, new Vector3(-0.1f, 0.42f, 0.135f), new Vector3(0.08f, 0.1f, 0.01f), Kit.Hex("E9ECEF"), name: "출입증");
        Kit.Box(torso, new Vector3(-0.1f, 0.45f, 0.137f), new Vector3(0.08f, 0.025f, 0.01f), Kit.Hex("3D6E86"), name: "출입증띠");
        Kit.Box(torso, new Vector3(0.1f, 0.42f, 0.135f), new Vector3(0.09f, 0.08f, 0.01f), Kit.Hex("22304C"), name: "주머니");
        Kit.Box(torso, new Vector3(0, 0.6f, 0.02f), new Vector3(0.24f, 0.06f, 0.22f), Kit.Hex("26324D"), name: "깃");
        // 배낭 (등 = -z)
        Kit.Box(torso, new Vector3(0, 0.34f, -0.22f), new Vector3(0.34f, 0.42f, 0.18f), Pack, name: "배낭");
        Kit.Box(torso, new Vector3(0, 0.5f, -0.23f), new Vector3(0.35f, 0.12f, 0.19f), PackDark, name: "덮개");
        Kit.Cyl(torso, new Vector3(0, 0.62f, -0.22f), new Vector3(0.14f, 0.38f, 0.14f), Kit.Hex("6A5A44"),
            rot: Quaternion.Euler(0, 0, 90), name: "매트");
        Kit.Cyl(torso, new Vector3(0.2f, 0.32f, -0.22f), new Vector3(0.07f, 0.2f, 0.07f), Kit.Hex("C9CED4"), name: "손전등");
        Kit.Box(torso, new Vector3(0.2f, 0.43f, -0.22f), new Vector3(0.075f, 0.03f, 0.075f), Kit.Hazard, name: "손전등머리");
        foreach (float sx in new[] { -0.09f, 0.09f })
            Kit.Box(torso, new Vector3(sx, 0.36f, 0.13f), new Vector3(0.04f, 0.42f, 0.01f), Kit.Hex("1E2226"), name: "멜빵");

        // 팔
        armL = Kit.Group(torso, "ArmL", new Vector3(-0.27f, 0.54f, 0));
        armR = Kit.Group(torso, "ArmR", new Vector3(0.27f, 0.54f, 0));
        foreach (var e in new[] { (armL, Navy), (armR, NavyDark) })
        {
            Transform arm = e.Item1; Color c = e.Item2;
            Kit.Box(arm, new Vector3(0, -0.15f, 0), new Vector3(0.12f, 0.3f, 0.13f), c, name: "윗팔");
            Kit.Box(arm, new Vector3(0, -0.24f, 0), new Vector3(0.125f, 0.04f, 0.135f), Reflect, name: "팔반사띠");
            Kit.Box(arm, new Vector3(0, -0.43f, 0), new Vector3(0.11f, 0.28f, 0.12f), c, name: "아래팔");
            Kit.Box(arm, new Vector3(0, -0.62f, 0.01f), new Vector3(0.11f, 0.1f, 0.12f), Glove, name: "장갑");
        }
        hand = Kit.Group(armR, "Hand", new Vector3(0, -0.64f, 0.04f));
        wrenchModel = new GameObject("렌치");
        wrenchModel.transform.SetParent(hand, false);
        wrenchModel.transform.localRotation = Quaternion.Euler(80, 0, 0);
        Kit.Box(wrenchModel.transform, new Vector3(0, 0, 0.18f), new Vector3(0.04f, 0.025f, 0.38f), Kit.Hex("A9AFB6"), name: "자루");
        Kit.Box(wrenchModel.transform, new Vector3(0, 0, 0.4f), new Vector3(0.11f, 0.03f, 0.08f), Kit.Hex("9AA0A8"), name: "머리");
        Kit.Box(wrenchModel.transform, new Vector3(0, 0, 0.45f), new Vector3(0.04f, 0.03f, 0.05f), Kit.Hex("8A8F96"), name: "턱");
        pipeModel = new GameObject("쇠파이프");
        pipeModel.transform.SetParent(hand, false);
        pipeModel.transform.localRotation = Quaternion.Euler(80, 0, 0);
        Kit.Cyl(pipeModel.transform, new Vector3(0, 0, 0.4f), new Vector3(0.06f, 0.95f, 0.06f), Kit.Hex("7A8088"),
            rot: Quaternion.Euler(90, 0, 0), name: "파이프");
        Kit.Box(pipeModel.transform, new Vector3(0, 0, 0.05f), new Vector3(0.075f, 0.075f, 0.14f), Kit.Hex("2E3135"), name: "테이프");

        // 머리
        Kit.Box(torso, new Vector3(0, 0.64f, 0), new Vector3(0.1f, 0.08f, 0.1f), SkinShade, name: "목");
        var head = Kit.Group(torso, "Head", new Vector3(0, 0.66f, 0));
        Kit.Box(head, new Vector3(-0.055f, 0.13f, 0), new Vector3(0.11f, 0.24f, 0.22f), Skin, name: "얼굴");
        Kit.Box(head, new Vector3(0.055f, 0.13f, 0), new Vector3(0.11f, 0.24f, 0.22f), SkinShade, name: "얼굴");
        Kit.Box(head, new Vector3(0, 0.15f, -0.1f), new Vector3(0.23f, 0.16f, 0.05f), Hair, name: "뒷머리");
        Kit.Box(head, new Vector3(-0.05f, 0.15f, 0.111f), new Vector3(0.035f, 0.02f, 0.01f), Hair, name: "눈");
        Kit.Box(head, new Vector3(0.05f, 0.15f, 0.111f), new Vector3(0.035f, 0.02f, 0.01f), Hair, name: "눈");
        Kit.Box(head, new Vector3(0, 0.27f, 0), new Vector3(0.245f, 0.08f, 0.245f), Kit.Navy, name: "작업모");
        Kit.Box(head, new Vector3(0, 0.235f, 0.16f), new Vector3(0.22f, 0.02f, 0.12f), Kit.Hex("1E2840"), name: "챙");
        Kit.Box(head, new Vector3(0, 0.275f, 0.124f), new Vector3(0.06f, 0.04f, 0.01f), Kit.Hazard, name: "모자마크");
        Kit.Box(head, new Vector3(0, 0.315f, 0), new Vector3(0.08f, 0.01f, 0.08f), Kit.Hazard, name: "모자위마크");
    }

    /// <summary>PlayerCombat이 공격할 때 부른다</summary>
    public void TriggerSwing() => swingT = 0f;

    void LateUpdate()
    {
        bool dead = stats != null && stats.IsDead;

        // 무기 표시
        ItemData w = inv != null ? inv.EquippedWeapon : null;
        bool isPipe = w != null && w.displayName.Contains("파이프");
        wrenchModel.SetActive(w != null && !isPipe);
        pipeModel.SetActive(isPipe);

        if (dead)
        {
            model.localRotation = Quaternion.Slerp(model.localRotation, Quaternion.Euler(-85f, 0, 0), 4f * Time.deltaTime);
            model.localPosition = Vector3.Lerp(model.localPosition, new Vector3(0, 0.15f, 0), 4f * Time.deltaTime);
            return;
        }

        Vector3 delta = transform.position - lastPos; delta.y = 0;
        lastPos = transform.position;
        float speed = Time.deltaTime > 0 ? delta.magnitude / Time.deltaTime : 0f;
        float amount = Mathf.Clamp01(speed / 3.5f);

        phase += Time.deltaTime * (4f + speed * 1.8f);
        float legSwing = Mathf.Sin(phase) * (ctrl != null && ctrl.IsRunning ? 42f : 30f) * amount;

        // 웅크리기: 다리를 굽히고 몸을 낮춘다
        crouch = Mathf.MoveTowards(crouch, ctrl != null && ctrl.IsCrouching ? 1f : 0f, 5f * Time.deltaTime);
        float drop = 0.32f * crouch;
        hipL.localRotation = Quaternion.Euler(legSwing - 50f * crouch, 0, 0);
        hipR.localRotation = Quaternion.Euler(-legSwing - 50f * crouch, 0, 0);
        hipL.localPosition = new Vector3(-0.1f, 0.92f - drop, 0);
        hipR.localPosition = new Vector3(0.1f, 0.92f - drop, 0);

        float bob = Mathf.Abs(Mathf.Sin(phase)) * 0.035f * amount;
        torso.localPosition = new Vector3(0, 0.92f - drop + bob, 0);
        float lean = (ctrl != null && ctrl.IsRunning ? 10f : 0f) + 22f * crouch;
        torso.localRotation = Quaternion.Euler(lean, 0, 0);

        armL.localRotation = Quaternion.Euler(-legSwing * 0.8f, 0, 0);

        // 오른팔: 휘두르기 > 무기 들고 있기 > 걷기
        if (swingT >= 0f)
        {
            swingT += Time.deltaTime / 0.22f;
            float t = Mathf.Clamp01(swingT);
            float ang = t < 0.35f ? Mathf.Lerp(-20f, -140f, t / 0.35f) : Mathf.Lerp(-140f, 30f, (t - 0.35f) / 0.65f);
            armR.localRotation = Quaternion.Euler(ang, 0, 0);
            if (swingT >= 1f) swingT = -1f;
        }
        else
        {
            float hold = w != null ? -25f : 0f;
            armR.localRotation = Quaternion.Euler(hold + legSwing * 0.8f, 0, 0);
        }
    }
}
