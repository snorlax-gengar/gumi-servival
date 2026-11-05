using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 작업형 안드로이드 모델 (컨셉 아트 기준, 키 2.2m).
/// 넓은 어깨 + 노란 경고 줄무늬, 짧은 다리, 긴 팔, 작은 머리와 빨간 바이저.
/// 원래 캡슐 메시는 숨기고(콜라이더는 그대로) 관절 모델을 붙인다.
/// 이동 속도에 맞춰 팔다리를 흔들고, 기절하면 고개를 떨군다.
/// </summary>
public class AndroidVisual : MonoBehaviour
{
    public float modelYawOffset = 0f;

    Transform model, hipL, hipR, armL, armR, head, torso;
    CharacterAnimator anim;

    /// <summary>에셋 모델(애니메이션 있음)을 쓰는 중인가</summary>
    public bool Animated { get; private set; }
    NavMeshAgent agent;
    AndroidAI ai;
    Damageable body;
    float phase;
    Vector3 lastPos;

    static readonly Color White = Kit.Hex("E9ECEF");
    static readonly Color WhiteShade = Kit.Hex("D3D8DE");
    static readonly Color Joint = Kit.Hex("5E6E80");
    static readonly Color JointDark = Kit.Hex("4E5C6C");
    static readonly Color Limb = Kit.Hex("C9CED4");
    static readonly Color LimbDark = Kit.Hex("AEB5BD");

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        ai = GetComponent<AndroidAI>();
        body = GetComponent<Damageable>();

        // 기존 캡슐/바이저 렌더러 숨기기
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;

        Animated = TryBuildMixamo() || TryBuildAsset();
        if (!Animated) Build();
        lastPos = transform.position;
    }

    // ================= Mixamo X Bot → 니어식 사람형 안드로이드 =================

    readonly System.Collections.Generic.List<Renderer> glow = new System.Collections.Generic.List<Renderer>();
    bool glowOn = true;

    bool TryBuildMixamo()
    {
        if (!MixamoLib.HasModel("Android")) return false;
        float s = Mathf.Max(0.01f, transform.lossyScale.y);
        var space = Kit.Group(transform, "ModelSpace", new Vector3(0, -1.1f / s, 0));
        space.localScale = Vector3.one / s;

        var t = AssetLib.Spawn(MixamoLib.Root + "Android", space, Vector3.zero, modelYawOffset, AssetLib.Fit.Height, new Vector3(0, 1.92f, 0),
            false, false, "Model");
        if (t == null) { Destroy(space.gameObject); return false; }
        var animator = t.GetComponentInChildren<Animator>();
        if (animator == null || !animator.isHuman) { Destroy(space.gameObject); return false; }

        // 색: 도자기 같은 흰 외장 + 흑연색 관절
        foreach (var r in t.GetComponentsInChildren<Renderer>())
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                Color c = AssetLib.BaseColor(mats[i]);
                Color.RGBToHSV(c, out float h, out float sat, out float v);
                string n = mats[i].name.ToLowerInvariant();
                bool joint = n.Contains("joint") || v < 0.35f;
                mats[i] = AssetLib.Recolor(mats[i], joint ? Kit.Hex("23262B") : Kit.Hex("E6E8EA"));
                mats[i].SetFloat("_Smoothness", joint ? 0.35f : 0.7f);
            }
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        // 빛나는 눈띠·라인 (T자세에서 붙인다)
        Vector3 fwd = transform.forward, up = Vector3.up;
        Quaternion face = Quaternion.LookRotation(fwd, up);
        Color red = Kit.Visor;
        var headB = AssetLib.FindBone(t, new[] { "head" });
        var chest = AssetLib.FindBone(t, new[] { "spine2" }, new[] { "spine1" });
        var spine = AssetLib.FindBone(t, new[] { "spine1" }, new[] { "spine" });
        if (headB != null)
        {
            glow.Add(MixamoLib.Attach(headB, "눈띠", headB.position + fwd * 0.105f + up * 0.075f, face, new Vector3(0.15f, 0.022f, 0.012f), red, 4f).GetComponent<Renderer>());
            MixamoLib.Attach(headB, "눈띠테", headB.position + fwd * 0.098f + up * 0.075f, face, new Vector3(0.17f, 0.05f, 0.01f), Kit.Hex("15171A"), 0f);
        }
        if (chest != null)
        {
            glow.Add(MixamoLib.Attach(chest, "가슴코어", chest.position + fwd * 0.14f + up * 0.03f, face, new Vector3(0.05f, 0.05f, 0.012f), red, 3f).GetComponent<Renderer>());
            glow.Add(MixamoLib.Attach(chest, "등라인", chest.position - fwd * 0.12f, face, new Vector3(0.012f, 0.26f, 0.01f), red, 2.5f).GetComponent<Renderer>());
        }
        if (spine != null)
            glow.Add(MixamoLib.Attach(spine, "허리라인", spine.position + fwd * 0.12f, face, new Vector3(0.2f, 0.01f, 0.01f), red, 2f).GetComponent<Renderer>());
        foreach (var side in new[] { "left", "right" })
        {
            var fore = AssetLib.FindBone(t, new[] { side + "forearm" });
            var hnd = AssetLib.FindBone(t, new[] { side + "hand" });
            if (fore != null && hnd != null) LimbLine(fore, hnd, up * 0.045f, red);
            var thigh = AssetLib.FindBone(t, new[] { side + "upleg" });
            var knee = AssetLib.FindBone(t, new[] { side + "leg" });
            if (thigh != null && knee != null && knee != thigh) LimbLine(thigh, knee, fwd * 0.075f, red);
        }

        anim = t.gameObject.AddComponent<CharacterAnimator>();
        anim.walkReference = 1.5f;
        anim.runReference = 3.2f;
        anim.walkClipSpeed = 1.6f;
        anim.runClipSpeed = 4.4f;
        anim.attackDuration = 1.0f;
        bool ok = anim.Init(animator, MixamoLib.Clips(),
            new System.Collections.Generic.Dictionary<CharacterAnimator.Slot, string[]>
            {
                { CharacterAnimator.Slot.Idle, new[] { "idle" } },
                { CharacterAnimator.Slot.Walk, new[] { "walk" } },
                { CharacterAnimator.Slot.Run, new[] { "run" } },
                { CharacterAnimator.Slot.Attack, new[] { "mutantpunch", "attackmelee" } },
                { CharacterAnimator.Slot.Death, new[] { "dying" } },
            },
            new[] { "crouch" });
        if (!ok) { Destroy(space.gameObject); glow.Clear(); return false; }
        model = space;
        return true;
    }

    void LimbLine(Transform a, Transform b, Vector3 offset, Color c)
    {
        Vector3 d = b.position - a.position;
        if (d.sqrMagnitude < 1e-4f) return;
        var r = MixamoLib.Attach(a, "라인", (a.position + b.position) * 0.5f + offset, Quaternion.LookRotation(d.normalized, Vector3.up),
            new Vector3(0.012f, 0.012f, d.magnitude * 0.7f), c, 2.5f).GetComponent<Renderer>();
        glow.Add(r);
    }

    void SetGlow(bool on, float flicker = 1f)
    {
        bool vis = on && flicker > 0.5f;
        if (glowOn == vis) return;
        glowOn = vis;
        foreach (var r in glow) if (r != null) r.enabled = vis;
    }

    // ================= 에셋 모델 (Quaternius Animated Robot) =================

    bool TryBuildAsset()
    {
        const string path = "Quaternius/Robot";
        if (!AssetLib.Has(path)) return false;

        // 부모(캡슐)는 중심이 지면 위 1.1m, 크기 1.1배 → 발바닥 기준·실제 미터 단위 공간을 만든다
        float s = Mathf.Max(0.01f, transform.lossyScale.y);
        var space = Kit.Group(transform, "ModelSpace", new Vector3(0, -1.1f / s, 0));
        space.localScale = Vector3.one / s;

        var t = AssetLib.Spawn(path, space, Vector3.zero, modelYawOffset, AssetLib.Fit.Height, new Vector3(0, 2.2f, 0),
            false, false, "Model");
        if (t == null) { Destroy(space.gameObject); return false; }

        var animator = t.GetComponentInChildren<Animator>();
        if (animator == null) animator = t.GetChild(0).gameObject.AddComponent<Animator>();
        anim = t.gameObject.AddComponent<CharacterAnimator>();
        anim.walkReference = 1.5f;
        anim.runReference = 3.6f;
        bool ok = anim.Init(animator, AssetLib.Clips(path),
            new System.Collections.Generic.Dictionary<CharacterAnimator.Slot, string[]>
            {
                { CharacterAnimator.Slot.Idle, new[] { "idle", "standing" } },
                { CharacterAnimator.Slot.Walk, new[] { "walking", "walk" } },
                { CharacterAnimator.Slot.Run, new[] { "running", "run" } },
                { CharacterAnimator.Slot.Attack, new[] { "punch", "attack" } },
                { CharacterAnimator.Slot.Death, new[] { "death", "die" } },
            },
            new[] { "jump", "dance", "wave", "thumb", "sitting" });
        if (!ok) { Destroy(space.gameObject); return false; }
        model = space;

        // 색: 컨셉 아트 기준 흰 외장 + 회색 관절 + 빨간 눈.
        // 채도 높은 몸체 색 → 흰색, 밝은 하늘/파랑/초록(눈·조명) → 빨간 발광, 어두운 색은 그대로
        foreach (var r in t.GetComponentsInChildren<Renderer>())
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                Color c = AssetLib.BaseColor(mats[i]);
                Color.RGBToHSV(c, out float h, out float sat, out float v);
                string n = mats[i].name.ToLowerInvariant();
                Debug.Log($"[Gumi] 로봇 재질 {mats[i].name}: #{ColorUtility.ToHtmlStringRGB(c)}");
                bool eye = n.Contains("eye") || n.Contains("light") || n.Contains("emiss") || (sat > 0.4f && v > 0.6f && h > 0.42f && h < 0.72f);
                if (eye) mats[i] = AssetLib.Recolor(mats[i], Kit.Visor, 3f);
                else if (sat > 0.35f) mats[i] = AssetLib.Recolor(mats[i], Kit.Hex("E9ECEF"));
                else if (v > 0.35f && v < 0.8f) mats[i] = AssetLib.Recolor(mats[i], Kit.Hex("5E6E80"));
            }
            r.sharedMaterials = mats;
        }
        return true;
    }

    public void PlayAttack() { if (Animated) anim.PlayAttack(1.3f); }
    public void PlayDeath() { if (Animated) anim.PlayDeath(); }

    void Build()
    {
        // 부모(캡슐)는 중심이 지면 위 1.1m이고 크기 1.1배 → 모델은 발바닥 기준, 실제 미터 단위로 만든다
        float s = Mathf.Max(0.01f, transform.lossyScale.y);
        model = Kit.Group(transform, "Model", new Vector3(0, -1.0f, 0));
        model.localScale = Vector3.one / s;
        model.localPosition = new Vector3(0, -1.1f / s, 0);

        // 다리 (엉덩이 관절에서 흔든다)
        hipL = Kit.Group(model, "HipL", new Vector3(-0.24f, 1.05f, 0));
        hipR = Kit.Group(model, "HipR", new Vector3(0.24f, 1.05f, 0));
        foreach (var e in new[] { (hipL, Limb, Joint), (hipR, LimbDark, JointDark) })
        {
            Transform hip = e.Item1; Color c1 = e.Item2, c2 = e.Item3;
            Kit.Box(hip, new Vector3(0, -0.25f, 0), new Vector3(0.3f, 0.5f, 0.32f), c1, name: "허벅지");
            Kit.Box(hip, new Vector3(0, -0.52f, 0.02f), new Vector3(0.34f, 0.14f, 0.36f), c2, name: "무릎");
            Kit.Box(hip, new Vector3(0, -0.78f, 0), new Vector3(0.36f, 0.42f, 0.38f), c1, name: "정강이");
            Kit.Box(hip, new Vector3(0, -0.98f, 0.08f), new Vector3(0.4f, 0.14f, 0.56f), c2, name: "발");
        }

        torso = Kit.Group(model, "Torso", new Vector3(0, 1.2f, 0));
        Kit.Box(torso, new Vector3(0, 0, 0), new Vector3(0.62f, 0.26f, 0.42f), Joint, name: "골반");
        Kit.Box(torso, new Vector3(0, 0.5f, 0), new Vector3(0.98f, 0.72f, 0.58f), White, name: "몸통");
        Kit.Box(torso, new Vector3(0.25f, 0.5f, 0.005f), new Vector3(0.48f, 0.72f, 0.58f), WhiteShade, name: "몸통음영");
        Kit.Box(torso, new Vector3(0, 0.55f, 0.3f), new Vector3(0.62f, 0.42f, 0.04f), Kit.Hex("DDE1E5"), name: "가슴판");
        Kit.Box(torso, new Vector3(0, 0.25f, 0.3f), new Vector3(0.44f, 0.09f, 0.03f), Kit.Hex("2E3135"), name: "통풍구");
        Kit.Box(torso, new Vector3(-0.14f, 0.25f, 0.32f), new Vector3(0.08f, 0.05f, 0.02f), Kit.Visor, emission: 2f, name: "상태등");
        Kit.Box(torso, new Vector3(0, 0.55f, -0.31f), new Vector3(0.22f, 0.3f, 0.04f), Kit.Tech, emission: 1.2f, name: "충전구");
        // 어깨 + 노란 경고 줄무늬
        foreach (float sx in new[] { -1f, 1f })
        {
            Kit.Box(torso, new Vector3(sx * 0.64f, 0.82f, 0), new Vector3(0.42f, 0.26f, 0.56f), sx < 0 ? WhiteShade : Kit.Hex("C2C8CF"), name: "어깨");
            Kit.Box(torso, new Vector3(sx * 0.64f, 0.96f, 0), new Vector3(0.43f, 0.03f, 0.57f), Kit.Hazard, name: "경고띠");
            for (int k = -1; k <= 1; k++)
                Kit.Box(torso, new Vector3(sx * 0.64f + k * 0.13f, 0.975f, 0), new Vector3(0.05f, 0.02f, 0.58f), Kit.Hex("2E3135"), name: "줄무늬");
        }
        // 팔 (어깨에서 흔든다, 길고 아래팔이 굵다)
        armL = Kit.Group(torso, "ArmL", new Vector3(-0.66f, 0.72f, 0));
        armR = Kit.Group(torso, "ArmR", new Vector3(0.66f, 0.72f, 0));
        foreach (var e in new[] { (armL, Limb, Joint), (armR, LimbDark, JointDark) })
        {
            Transform arm = e.Item1; Color c1 = e.Item2, c2 = e.Item3;
            Kit.Box(arm, new Vector3(0, -0.28f, 0), new Vector3(0.22f, 0.52f, 0.24f), c1, name: "윗팔");
            Kit.Box(arm, new Vector3(0, -0.56f, 0), new Vector3(0.26f, 0.12f, 0.28f), c2, name: "팔꿈치");
            Kit.Box(arm, new Vector3(0, -0.86f, 0.02f), new Vector3(0.32f, 0.52f, 0.34f), c1, name: "아래팔");
            Kit.Box(arm, new Vector3(0, -1.18f, 0.05f), new Vector3(0.3f, 0.16f, 0.3f), c2, name: "손");
            Kit.Box(arm, new Vector3(-0.08f, -1.3f, 0.12f), new Vector3(0.07f, 0.12f, 0.07f), c2, name: "집게");
            Kit.Box(arm, new Vector3(0.08f, -1.3f, 0.12f), new Vector3(0.07f, 0.12f, 0.07f), c2, name: "집게");
        }
        // 머리 (작고 각진 헬멧 + 빨간 바이저 + 안테나)
        Kit.Box(torso, new Vector3(0, 0.95f, 0), new Vector3(0.2f, 0.12f, 0.2f), Joint, name: "목");
        head = Kit.Group(torso, "Head", new Vector3(0, 1.0f, 0.02f));
        Kit.Box(head, new Vector3(0, 0.17f, 0), new Vector3(0.46f, 0.32f, 0.42f), White, name: "머리");
        Kit.Box(head, new Vector3(0.12f, 0.17f, 0.002f), new Vector3(0.22f, 0.32f, 0.42f), WhiteShade, name: "머리음영");
        Kit.Box(head, new Vector3(0, 0.19f, 0.215f), new Vector3(0.4f, 0.09f, 0.03f), Kit.Visor, emission: 3f, name: "바이저");
        Kit.Box(head, new Vector3(0.14f, 0.35f, -0.1f), new Vector3(0.03f, 0.2f, 0.03f), Kit.Hex("8A8F96"), name: "안테나");
        Kit.Box(head, new Vector3(0.14f, 0.46f, -0.1f), new Vector3(0.07f, 0.04f, 0.07f), Joint, name: "안테나끝");

        // 그림자는 모델이 드리운다
        foreach (var r in model.GetComponentsInChildren<Renderer>())
            if (r.sharedMaterial != null && !r.sharedMaterial.IsKeywordEnabled("_EMISSION"))
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
    }

    void LateUpdate()
    {
        if (body != null && body.IsDead) { SetGlow(false); return; }
        if (glow.Count > 0)
        {
            // 기절하면 눈·라인이 깜빡인다
            bool stunnedNow = ai != null && ai.IsStunned;
            SetGlow(true, stunnedNow ? Mathf.PerlinNoise(Time.time * 12f, 0.3f) * 1.6f : 1f);
        }

        Vector3 delta = transform.position - lastPos; delta.y = 0;
        lastPos = transform.position;
        float speed = Time.deltaTime > 0 ? delta.magnitude / Time.deltaTime : 0f;

        if (Animated)
        {
            bool dazed = ai != null && ai.IsStunned;
            anim.SetMovement(dazed ? 0f : speed, dazed ? 0.2f : 1f);
            // 기절하면 휘청거린다
            model.localRotation = Quaternion.Slerp(model.localRotation,
                dazed ? Quaternion.Euler(12f, 0, Mathf.Sin(Time.time * 7f) * 6f) : Quaternion.identity, 8f * Time.deltaTime);
            return;
        }
        float amount = Mathf.Clamp01(speed / 1.6f);

        phase += Time.deltaTime * (3f + speed * 1.6f);
        float swing = Mathf.Sin(phase) * 32f * amount;

        hipL.localRotation = Quaternion.Euler(swing, 0, 0);
        hipR.localRotation = Quaternion.Euler(-swing, 0, 0);
        armL.localRotation = Quaternion.Euler(-swing * 0.8f, 0, 0);
        armR.localRotation = Quaternion.Euler(swing * 0.8f, 0, 0);

        float bob = Mathf.Abs(Mathf.Sin(phase)) * 0.05f * amount;
        torso.localPosition = new Vector3(0, 1.2f + bob, 0);

        // 추적 중엔 앞으로 숙이고, 기절하면 고개를 떨군다
        bool chasing = ai != null && ai.CurrentState == AndroidAI.State.Chase;
        bool stunned = ai != null && ai.IsStunned;
        float lean = chasing ? 12f : 0f;
        torso.localRotation = Quaternion.Slerp(torso.localRotation, Quaternion.Euler(lean, 0, 0), 6f * Time.deltaTime);
        Quaternion headTarget = stunned ? Quaternion.Euler(35f, Mathf.Sin(Time.time * 9f) * 8f, 0) : Quaternion.identity;
        head.localRotation = Quaternion.Slerp(head.localRotation, headTarget, 8f * Time.deltaTime);
    }
}
