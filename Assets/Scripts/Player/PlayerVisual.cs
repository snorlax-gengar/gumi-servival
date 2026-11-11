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
    [Tooltip("에셋 모델의 정면 보정 각도 (모델이 뒤를 보면 180)")]
    public float modelYawOffset = 0f;

    Transform model, hipL, hipR, armL, armR, torso, hand;
    CharacterAnimator anim;
    bool animated;
    /// <summary>애니메이션 있는 캐릭터 모델을 쓰는 중 (휘두르는 동작이 실제로 보임)</summary>
    public bool IsAnimated => animated;
    GameObject wrenchModel, pipeModel;

    /// <summary>1인칭: 몸은 그림자만 남기고 숨김 (손에 든 무기는 보인다)</summary>
    public void SetFirstPerson(bool fp)
    {
        if (model == null) return;
        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
        {
            bool weapon = (wrenchModel != null && r.transform.IsChildOf(wrenchModel.transform))
                          || (pipeModel != null && r.transform.IsChildOf(pipeModel.transform));
            if (weapon) continue;
            r.shadowCastingMode = fp ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : UnityEngine.Rendering.ShadowCastingMode.On;
        }
    }
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

        animated = TryBuildMixamo() || TryBuildAsset();
        if (!animated) Build();
        lastPos = transform.position;
    }

    // ================= Mixamo 사람 모델 (좀보이드·GTA식 실제 비율) =================

    bool mixamo;

    bool TryBuildMixamo()
    {
        if (!MixamoLib.HasModel("Player")) return false;
        var t = AssetLib.Spawn(MixamoLib.Root + "Player", transform, Vector3.zero, modelYawOffset, AssetLib.Fit.Height, new Vector3(0, 1.76f, 0),
            false, false, "Model");
        if (t == null) return false;
        var animator = t.GetComponentInChildren<Animator>();
        if (animator == null || !animator.isHuman) { Destroy(t.gameObject); Debug.LogWarning("[Gumi] Mixamo 플레이어가 휴머노이드가 아님"); return false; }

        anim = t.gameObject.AddComponent<CharacterAnimator>();
        anim.walkReference = 2.6f;   // 이 속도에서 완전히 걷기
        anim.runReference = 5.5f;
        anim.walkClipSpeed = 1.6f;   // Mixamo Walking 보폭
        anim.runClipSpeed = 4.4f;    // Mixamo Running 보폭
        anim.crouchClipSpeed = 1.1f;
        anim.alwaysAnimate = true;   // 1인칭에서 몸이 '안 보이는' 것으로 처리돼 굳어 버리던 문제
        anim.attackDuration = 0.7f;
        anim.attackFrom = 0.18f;   // 준비 동작 앞부분과 마무리 뒷부분을 잘라 빠르게 휘두른다
        anim.attackTo = 0.68f;
        bool ok = anim.Init(animator, MixamoLib.Clips(),
            new System.Collections.Generic.Dictionary<CharacterAnimator.Slot, string[]>
            {
                { CharacterAnimator.Slot.Idle, new[] { "idle" } },
                { CharacterAnimator.Slot.Walk, new[] { "walk" } },
                { CharacterAnimator.Slot.Run, new[] { "run" } },
                { CharacterAnimator.Slot.Crouch, new[] { "crouch" } },
                { CharacterAnimator.Slot.Attack, new[] { "attackmelee", "attackbat", "mutantpunch" } },
                { CharacterAnimator.Slot.Death, new[] { "dying" } },
                { CharacterAnimator.Slot.Jump, new[] { "jump" } },
                { CharacterAnimator.Slot.Fall, new[] { "fall" } },
                { CharacterAnimator.Slot.Vault, new[] { "vault" } },
                { CharacterAnimator.Slot.Climb, new[] { "climb" } },
                { CharacterAnimator.Slot.Hurdle, new[] { "hurdle" } },
                { CharacterAnimator.Slot.Drop, new[] { "drop" } },
            },
            null);
        if (!ok) { Destroy(t.gameObject); return false; }
        model = t;
        mixamo = true;
        if (ctrl != null) ctrl.OnTraverse += OnTraverse;
        // 발을 땅에 붙이기 (애니메이션 뒤 발 높이를 재서 모델 높이 보정)
        var g = animator.gameObject.AddComponent<FootGrounder>();
        g.animator = animator;
        g.character = transform;
        g.paused = () => (stats != null && stats.IsDead) || (ctrl != null && (ctrl.IsAirborne || ctrl.IsVaulting));
        // 고개 돌리기: 어깨 시점에서 둘러보는 쪽을 쳐다본다 (공격·넘기·쓰러짐 중에는 정면)
        var hl = animator.gameObject.AddComponent<HeadLook>();
        hl.animator = animator;
        hl.character = transform;
        hl.moving = () => ctrl != null ? ctrl.PlanarVelocity.magnitude / 2.3f : 0f;
        hl.lookDir = () =>
        {
            var cam = Camera.main;
            if (cam == null || IsometricCamera.FirstPerson || (stats != null && stats.IsDead)) return null;
            if (ctrl != null && (ctrl.IsVaulting || ctrl.IsAttacking)) return null;
            return cam.transform.forward;
        };
        leanBase = t.localRotation;

        // 사람 걸음에 맞춘 속도 (좀보이드처럼 걷기는 느긋하게, 뛰어야 따돌린다)
        if (ctrl != null) { ctrl.walkSpeed = 2.3f; ctrl.runSpeed = 5.5f; ctrl.crouchSpeed = 1.3f; }
        anim.walkReference = 2.3f;

        foreach (var r in t.GetComponentsInChildren<Renderer>())
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        ApplyRemyTextures(t);

        // 오른손 무기: 엄지 쪽으로 자루가 나오게 (T자세에서 계산)
        hand = AssetLib.FindBone(t, new[] { "righthand" }, new[] { "hand", "right" });
        if (hand != null)
        {
            var mid = AssetLib.FindBone(hand, new[] { "righthandmiddle1" }, new[] { "middle1" });
            var thumb = AssetLib.FindBone(hand, new[] { "righthandthumb1" }, new[] { "thumb1" });
            Vector3 finger = mid != null ? (mid.position - hand.position).normalized : transform.right;
            Vector3 axis = thumb != null ? Vector3.ProjectOnPlane(thumb.position - hand.position, finger).normalized : transform.forward;
            if (axis.sqrMagnitude < 0.5f) axis = transform.forward;
            // 엄지 쪽 그대로면 팔을 내렸을 때 공구가 사타구니 앞으로 수평으로 튀어나온다.
            // 손가락(아래) 쪽으로 60° 기울여 들고 다니는 자세로: 평소엔 아래-앞, 휘두르면 바깥으로.
            axis = (axis * Mathf.Cos(60f * Mathf.Deg2Rad) + finger * Mathf.Sin(60f * Mathf.Deg2Rad)).normalized;
            Vector3 upHint = Vector3.Cross(axis, Vector3.Cross(finger, axis)).normalized;
            if (upHint.sqrMagnitude < 0.5f) upHint = Vector3.up;
            var grip = new GameObject("무기손잡이").transform;
            grip.SetPositionAndRotation(hand.position + finger * 0.08f - axis * 0.04f, Quaternion.LookRotation(axis, upHint));
            grip.SetParent(hand, true);
            BuildWeapons(grip, false);
        }
        Debug.Log($"[Gumi] Mixamo 플레이어 준비 (손: {(hand ? hand.name : "없음")})");
        return true;
    }

    // 넘기·기어오르기·점프 동작 (Mixamo 파일이 있을 때)
    // 넘기·기어오르기·점프 동작 (Mixamo 파일이 있을 때)
    //  0 넘기: 달리면 허들(Hurdle), 서서는 손 짚고 넘기(Vault)   1 담장 기어오르기(Climb)
    //  2 점프   3 낮은 곳에 올라서기   4 담 넘기(손 짚고 넘어 반대편에 내려섬)
    float lastTraverse = -10f;
    bool Play(CharacterAnimator.Slot s, float d, float from, float to) => anim.PlayAction(s, d, from, to);
    void OnTraverse(int kind, float duration)
    {
        if (anim == null) return;
        lastTraverse = Time.time;
        switch (kind)
        {
            case 0: case 4:   // 손 짚고 넘기 / 담 넘기
                if (!Play(CharacterAnimator.Slot.Vault, duration, 0.05f, 0.9f)) Play(CharacterAnimator.Slot.Jump, duration, 0.15f, 0.7f);
                break;
            case 5:   // 달리며 넘기(허들): 앞으로 뛰어오르는 점프 동작
                Play(CharacterAnimator.Slot.Jump, duration, 0.2f, 0.75f);
                break;
            case 1: Play(CharacterAnimator.Slot.Climb, duration, 0.05f, 0.95f); break;
            case 2: Play(CharacterAnimator.Slot.Jump, 0.45f, 0.2f, 0.55f); break;
            case 3: Play(CharacterAnimator.Slot.Jump, duration, 0.15f, 0.6f); break;   // 무릎 올려 올라서기
        }
    }

    float airTime;
    float leanRoll, leanPitch;
    Quaternion leanBase = Quaternion.identity;

    /// <summary>
    /// Remy FBX 안에 묻힌 텍스처는 Unity가 못 찾아서 흰 마네킹처럼 보였다.
    /// Resources/Mixamo/Tex/에 뽑아 둔 텍스처를 재질 이름으로 찾아 직접 입힌다.
    /// </summary>
    static void ApplyRemyTextures(Transform t)
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        string[] keys = { "body", "bottom", "top", "shoes", "hair" };
        foreach (var r in t.GetComponentsInChildren<Renderer>())
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                string n = mats[i].name.ToLowerInvariant();
                if (n.Contains("eyelash")) { mats[i] = Kit.Mat(Kit.Hex("1A1512")); continue; }
                string key = null;
                foreach (var k in keys) if (n.Contains(k)) { key = k; break; }
                if (key == null) continue;
                string cap = char.ToUpperInvariant(key[0]) + key.Substring(1);
                var baseTex = Resources.Load<Texture2D>(MixamoLib.Root + "Tex/Remy_" + cap + (key == "hair" ? "_DiffuseA" : "_Diffuse"));
                if (baseTex == null) continue;
                var nrm = Resources.Load<Texture2D>(MixamoLib.Root + "Tex/Remy_" + cap + "_Normal");
                var m = lit != null ? new Material(lit) : new Material(mats[i]);
                m.name = "Remy_" + cap;
                m.SetTexture("_BaseMap", baseTex);
                m.mainTexture = baseTex;
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_Smoothness", key == "hair" ? 0.35f : 0.2f);
                if (nrm != null) { m.SetTexture("_BumpMap", nrm); m.EnableKeyword("_NORMALMAP"); }
                if (key == "hair")
                {
                    m.SetFloat("_AlphaClip", 1f);
                    m.SetFloat("_Cutoff", 0.45f);
                    m.EnableKeyword("_ALPHATEST_ON");
                    m.SetFloat("_Cull", 0f);
                    m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                }
                mats[i] = m;
            }
            r.sharedMaterials = mats;
        }
    }

    // ================= 에셋 모델 (Quaternius Worker_Male) =================

    bool TryBuildAsset()
    {
        const string path = "Quaternius/Worker_Male";
        var t = AssetLib.Spawn(path, transform, Vector3.zero, modelYawOffset, AssetLib.Fit.Height, new Vector3(0, 1.75f, 0),
            false, false, "Model");
        if (t == null) return false;

        var animator = t.GetComponentInChildren<Animator>();
        if (animator == null) animator = t.GetChild(0).gameObject.AddComponent<Animator>();
        anim = t.gameObject.AddComponent<CharacterAnimator>();
        anim.walkReference = 2.6f;
        anim.runReference = 6.5f;
        bool ok = anim.Init(animator, AssetLib.Clips(path),
            new System.Collections.Generic.Dictionary<CharacterAnimator.Slot, string[]>
            {
                { CharacterAnimator.Slot.Idle, new[] { "idle" } },
                { CharacterAnimator.Slot.Walk, new[] { "walk" } },
                { CharacterAnimator.Slot.Run, new[] { "run" } },
                { CharacterAnimator.Slot.Attack, new[] { "sword_slash", "slash", "punch", "attack" } },
                { CharacterAnimator.Slot.Death, new[] { "death", "die" } },
            },
            new[] { "jump", "sword_idle", "idle_sword", "gun", "shoot" });
        if (!ok)
        {
            Destroy(t.gameObject);
            return false;
        }
        model = t;

        // 색: 주황(루팅 전용 색)은 남색 작업복으로 바꾼다. 나머지는 원래 색 유지
        foreach (var r in t.GetComponentsInChildren<Renderer>())
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                Color c = AssetLib.BaseColor(mats[i]);
                Color.RGBToHSV(c, out float h, out float sat, out float v);
                Debug.Log($"[Gumi] 플레이어 재질 {mats[i].name}: #{ColorUtility.ToHtmlStringRGB(c)}");
                string mn = mats[i].name.ToLowerInvariant();
                if (mn.Contains("skin")) mats[i] = AssetLib.Recolor(mats[i], Kit.Hex("D9A77F"));          // 피부가 검게 들어온 것 보정
                else if (sat > 0.45f && h > 0.02f && h < 0.12f) mats[i] = AssetLib.Recolor(mats[i], Kit.Hex("34456A")); // 주황 → 남색
            }
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        // 오른손에 무기 붙이기
        hand = AssetLib.FindBone(t,
            new[] { "hand", ".r" }, new[] { "hand", "_r" }, new[] { "hand", "right" },
            new[] { "fist", ".r" }, new[] { "wrist", ".r" }, new[] { "lowerarm", ".r" }, new[] { "forearm", "r" });
        Debug.Log($"[Gumi] 플레이어 오른손 뼈: {(hand ? hand.name : "없음")}");
        if (hand != null) BuildWeapons(hand, true);
        return true;
    }

    void BuildWeapons(Transform parent, bool compensateScale)
    {
        float inv = compensateScale ? 1f / Mathf.Max(1e-4f, parent.lossyScale.x) : 1f;
        wrenchModel = new GameObject("렌치");
        wrenchModel.transform.SetParent(parent, false);
        wrenchModel.transform.localScale = Vector3.one * inv;
        Kit.Box(wrenchModel.transform, new Vector3(0, 0, 0.18f), new Vector3(0.04f, 0.025f, 0.38f), Kit.Hex("A9AFB6"), name: "자루");
        Kit.Box(wrenchModel.transform, new Vector3(0, 0, 0.4f), new Vector3(0.11f, 0.03f, 0.08f), Kit.Hex("9AA0A8"), name: "머리");
        pipeModel = new GameObject("쇠파이프");
        pipeModel.transform.SetParent(parent, false);
        pipeModel.transform.localScale = Vector3.one * inv;
        Kit.Cyl(pipeModel.transform, new Vector3(0, 0, 0.4f), new Vector3(0.06f, 0.95f, 0.06f), Kit.Hex("7A8088"),
            rot: Quaternion.Euler(90, 0, 0), name: "파이프");
        Kit.Box(pipeModel.transform, new Vector3(0, 0, 0.05f), new Vector3(0.075f, 0.075f, 0.14f), Kit.Hex("2E3135"), name: "테이프");
    }

    void AnimatedUpdate()
    {
        ItemData w = inv != null ? inv.EquippedWeapon : null;
        bool isPipe = w != null && w.displayName.Contains("파이프");
        if (wrenchModel != null) wrenchModel.SetActive(w != null && !isPipe);
        if (pipeModel != null) pipeModel.SetActive(isPipe);

        if (stats != null && stats.IsDead) { anim.PlayDeath(); return; }

        Vector3 delta = transform.position - lastPos; delta.y = 0;
        lastPos = transform.position;
        float speed = Time.deltaTime > 0 ? delta.magnitude / Time.deltaTime : 0f;

        // 웅크리기: 동작을 느리게 하고 몸을 낮춘다 (전용 동작이 없어서 자세로 표현)
        crouch = Mathf.MoveTowards(crouch, ctrl != null && ctrl.IsCrouching ? 1f : 0f, 5f * Time.deltaTime);
        // 0.25초 넘게 공중이면 낙하 동작
        bool air = ctrl != null && ctrl.IsAirborne;
        // 걷다가 높은 곳(1.2m 이상)에서 떨어지기 시작하면 뛰어내리는 동작
        if (air && airTime == 0f && Time.time - lastTraverse > 0.5f
            && !Physics.Raycast(transform.position + Vector3.up * 0.2f, Vector3.down, 1.4f, ~((1 << 30) | (1 << 31)), QueryTriggerInteraction.Ignore))
            Play(CharacterAnimator.Slot.Drop, 0.55f, 0.3f, 0.75f);
        airTime = air ? airTime + Time.deltaTime : 0f;
        anim.SetAirborne(airTime > 0.25f);

        // 몸 기울기: 돌 때는 안쪽으로, 출발할 때는 앞으로, 멈출 때는 뒤로 살짝 (오토바이 타듯 과하지 않게)
        if (mixamo && ctrl != null)
        {
            bool still = ctrl.IsVaulting || air || (stats != null && stats.IsDead) || ctrl.IsAttacking;
            float spd = ctrl.PlanarVelocity.magnitude;
            float roll = still ? 0f : Mathf.Clamp(-ctrl.TurnRate * spd * 0.0035f, -10f, 10f);
            float pitchL = still ? 0f : Mathf.Clamp(ctrl.Acceleration * 0.6f, -5f, 6f) + (ctrl.IsRunning ? 3f : 0f);
            float k = 1f - Mathf.Exp(-7f * Time.deltaTime);
            leanRoll = Mathf.Lerp(leanRoll, roll, k);
            leanPitch = Mathf.Lerp(leanPitch, pitchL, k);
            model.localRotation = leanBase * Quaternion.Euler(leanPitch, 0f, leanRoll);
        }

        if (anim.HasClip(CharacterAnimator.Slot.Crouch))
        {
            anim.SetCrouch(crouch);
            anim.SetMovement(speed);
            return;
        }
        anim.SetMovement(speed, Mathf.Lerp(1f, 0.75f, crouch));
        model.localPosition = new Vector3(0, -0.06f * crouch, 0);
        model.localRotation = Quaternion.Euler(18f * crouch, modelYawOffset, 0);
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
    public void TriggerSwing()
    {
        if (animated) anim.PlayAttack(mixamo ? 1f : 1.6f);
        else swingT = 0f;
    }

    void LateUpdate()
    {
        if (animated) { AnimatedUpdate(); return; }

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
