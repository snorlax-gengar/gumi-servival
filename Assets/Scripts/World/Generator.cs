using UnityEngine;

/// <summary>
/// 거점 발전기 — 이 게임의 핵심 '전력 딜레마'.
/// 고장 → (연료통 1개 + 부품 3개) 수리 → 켜기/끄기. 연료통 1개로 게임 시간 5시간.
/// 켜면: 거점에 불이 들어오고, 근처(8m)에서 스태미나·체력이 빨리 회복되고 추위가 없다.
/// 대신 전력 신호가 퍼져서(2초마다 반경 28m 소음) 안드로이드가 몰려온다.
/// </summary>
public class Generator : MonoBehaviour, IInteractable
{
    public enum State { Broken, Off, On }

    public int partsToRepair = 3;
    public float hoursPerFuel = 5f;
    public float signalRadius = 28f;
    public float signalInterval = 2f;
    public float comfortRadius = 8f;

    public State Current { get; private set; } = State.Broken;
    public float FuelHours { get; private set; }
    public bool Inspected { get; private set; }

    public static Generator Instance { get; private set; }
    public static ItemData FuelItem;   // CityGenerator가 정해 준다

    Light lamp;
    Renderer bulb, statusLight;
    float signalTimer;

    void Awake() => Instance = this;

    public void Setup(Light light, Renderer bulbRenderer, Renderer status)
    {
        lamp = light; bulb = bulbRenderer; statusLight = status;
        Apply();
    }

    public bool IsComfortZone(Vector3 p) => Current == State.On && Vector3.Distance(p, transform.position) <= comfortRadius;

    public string Prompt
    {
        get
        {
            switch (Current)
            {
                case State.Broken: return $"[E] 고장난 발전기 수리 (연료통 1 + 부품 {partsToRepair})";
                case State.Off: return FuelHours > 0.05f ? $"[E] 발전기 켜기 (연료 {FuelHours:0.0}시간)" : "[E] 발전기에 연료 넣기";
                default: return $"[E] 발전기 끄기 (연료 {FuelHours:0.0}시간 남음)";
            }
        }
    }

    public void Interact(GameObject who)
    {
        Inspected = true;
        var inv = who.GetComponent<Inventory>();
        var wallet = who.GetComponent<Wallet>();

        if (Current == State.Broken)
        {
            bool hasFuel = inv != null && FuelItem != null && inv.Has(FuelItem);
            int parts = wallet != null ? wallet.Parts : 0;
            if (!hasFuel || parts < partsToRepair)
            {
                string need = "";
                if (!hasFuel) need += "연료통";
                if (parts < partsToRepair) need += (need.Length > 0 ? ", " : "") + $"부품 {partsToRepair - parts}개";
                HUD.ShowMessage($"수리하려면 {need}이(가) 더 필요하다", 3f);
                return;
            }
            inv.RemoveOne(FuelItem);
            wallet.Spend(partsToRepair);
            FuelHours += hoursPerFuel;
            Current = State.Off;
            NoiseSystem.Emit(transform.position, 10f);
            HUD.ShowMessage("발전기를 고쳤다! 다시 E를 누르면 켜진다", 3f);
            Apply();
            return;
        }

        // 연료가 바닥났으면 넣기
        if (FuelHours <= 0.05f)
        {
            if (inv != null && FuelItem != null && inv.Has(FuelItem))
            {
                inv.RemoveOne(FuelItem);
                FuelHours += hoursPerFuel;
                HUD.ShowMessage($"연료를 넣었다 ({FuelHours:0.0}시간)", 2.5f);
            }
            else HUD.ShowMessage("연료가 없다. 연료통을 구해 오자", 2.5f);
            return;
        }

        Current = Current == State.On ? State.Off : State.On;
        HUD.ShowMessage(Current == State.On
            ? "발전기를 켰다. 따뜻하고 밝다… 하지만 전력 신호가 퍼진다"
            : "발전기를 껐다. 어둠 속에 숨는다", 3f);
        Apply();
    }

    void Update()
    {
        if (Current != State.On) return;

        float hours = GameManager.Instance != null ? GameManager.Instance.GameHoursThisFrame : 0f;
        FuelHours -= hours;
        if (FuelHours <= 0f)
        {
            FuelHours = 0f;
            Current = State.Off;
            HUD.ShowMessage("발전기 연료가 떨어졌다…", 3f);
            Apply();
            return;
        }

        // 전력 신호: 안드로이드는 전기 신호에 끌린다
        signalTimer -= Time.deltaTime;
        if (signalTimer <= 0f)
        {
            signalTimer = signalInterval;
            NoiseSystem.Emit(transform.position, signalRadius);
        }

        if (lamp != null) lamp.intensity = 3.2f + Mathf.PerlinNoise(Time.time * 6f, 0f) * 0.6f; // 전구 떨림
    }

    AudioSource hum;

    void Apply()
    {
        bool on = Current == State.On;
        // 켜져 있으면 웅웅 도는 소리 (멀리서도 들려 위험하다는 느낌)
        if (on && hum == null) hum = Sfx.Loop("generator", transform, 0.6f, 30f);
        if (hum != null) { if (on && !hum.isPlaying) hum.Play(); else if (!on) hum.Stop(); }
        if (lamp != null) lamp.enabled = on;
        if (bulb != null) bulb.sharedMaterial = Kit.Mat(Kit.WarmLight, on ? 2.5f : 0f);
        if (statusLight != null)
            statusLight.sharedMaterial = Current == State.Broken ? Kit.Mat(Kit.Loot, 1.8f)      // 손댈 수 있음 = 주황
                                       : on ? Kit.Mat(Kit.Hex("5CD67A"), 2f) : Kit.Mat(Kit.Hex("3A3F45"));
    }

    /// <summary>필로티 아래에 발전기와 전구를 만든다</summary>
    public static Generator Build(Transform parent, Vector3 worldPos, float yaw)
    {
        var root = Kit.Group(parent, "발전기", Vector3.zero);
        root.position = worldPos;
        root.rotation = Quaternion.Euler(0, yaw, 0);

        Kit.Box(root, new Vector3(0, 0.45f, 0), new Vector3(1.3f, 0.9f, 0.8f), Kit.Hex("3E6B57"), true, name: "몸체");
        Kit.Box(root, new Vector3(0, 0.92f, 0), new Vector3(1.32f, 0.06f, 0.82f), Kit.Hazard, name: "경고띠");
        Kit.Box(root, new Vector3(-0.35f, 0.6f, -0.41f), new Vector3(0.4f, 0.3f, 0.02f), Kit.Hex("2E3135"), name: "계기판");
        Kit.Cyl(root, new Vector3(0.45f, 1.1f, 0.2f), new Vector3(0.12f, 0.4f, 0.12f), Kit.Hex("5A5E64"), name: "배기관");
        var status = Kit.Box(root, new Vector3(0.3f, 0.7f, -0.42f), new Vector3(0.12f, 0.12f, 0.03f), Kit.Loot, emission: 1.8f, name: "상태등");
        Kit.Box(root, new Vector3(0, 0.02f, 0), new Vector3(1.6f, 0.04f, 1.1f), Kit.Loot, name: "주황 표시");

        // 필로티 천장의 전구 + 실제 빛
        var bulb = Kit.Box(root, new Vector3(0, 2.75f, 0.6f), new Vector3(0.25f, 0.12f, 0.25f), Kit.WarmLight, name: "전구");
        var lgo = new GameObject("거점 불빛");
        lgo.transform.SetParent(root, false);
        lgo.transform.localPosition = new Vector3(0, 2.55f, 0.6f);
        var l = lgo.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = Kit.WarmLight;
        l.range = 11f;
        l.intensity = 3.2f;
        l.shadows = LightShadows.Soft;

        var gen = root.gameObject.AddComponent<Generator>();
        gen.Setup(l, bulb.GetComponent<Renderer>(), status.GetComponent<Renderer>());
        return gen;
    }
}
