#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// 메뉴 [Gumi → 프로토타입 씬 만들기] 로 원룸촌 한 블록 그레이박스 씬을 자동 생성.
/// 처음 프로젝트를 열 때 씬이 없으면 한 번 자동 실행된다.
/// </summary>
[InitializeOnLoad]
public static class GumiSceneBuilder
{
    const string Root = "Assets/Gumi";
    const string ScenePath = "Assets/Scenes/Gumi_Prototype.unity";
    const string AutoRunKey = "Gumi_AutoBuilt_v1";

    static GumiSceneBuilder()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorPrefs.GetBool(AutoRunKey, false)) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) { EditorPrefs.SetBool(AutoRunKey, true); return; }
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            EditorPrefs.SetBool(AutoRunKey, true);
            Build();
        };
    }

    [MenuItem("Gumi/프로토타입 씬 만들기")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        EnsureFolder("Assets", "Gumi");
        EnsureFolder(Root, "Items");
        EnsureFolder(Root, "Materials");
        EnsureFolder("Assets", "Scenes");

        // ---------- 머티리얼 ----------
        Material mGround = Mat("Ground", new Color(0.32f, 0.33f, 0.34f));   // 아스팔트
        Material mBuilding = Mat("Building", new Color(0.78f, 0.74f, 0.68f)); // 오래된 원룸 외벽
        Material mStore = Mat("Store", new Color(0.35f, 0.62f, 0.45f));     // 편의점 초록
        Material mShelf = Mat("Shelf", new Color(0.85f, 0.85f, 0.88f));
        Material mBox = Mat("Container", new Color(0.85f, 0.55f, 0.15f));   // 뒤질 수 있는 것 = 주황
        Material mPlayer = Mat("Player", new Color(0.2f, 0.45f, 0.85f));
        Material mAndroid = Mat("Android", new Color(0.88f, 0.9f, 0.92f));
        Material mVisor = Mat("Visor", new Color(0.9f, 0.15f, 0.1f));

        // ---------- 아이템 ----------
        ItemData kimbap = Item("Item_Kimbap", "삼각김밥", ItemType.Food, 0.2f, h: 25);
        ItemData ramen = Item("Item_Ramen", "컵라면", ItemType.Food, 0.3f, h: 35, t: -5);
        ItemData water = Item("Item_Water", "생수", ItemType.Drink, 0.5f, t: 40);
        ItemData coffee = Item("Item_Coffee", "캔커피", ItemType.Drink, 0.25f, t: 20);
        ItemData bandage = Item("Item_Bandage", "붕대", ItemType.Medical, 0.1f, hp: 20);
        ItemData wrench = Weapon("Item_Wrench", "렌치", 1.2f, 25, 1.8f, 0.8f, 8);
        ItemData pipe = Weapon("Item_Pipe", "쇠파이프", 2.0f, 35, 2.2f, 1.1f, 10);

        // ---------- 씬 ----------
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var level = new GameObject("Level");
        level.AddComponent<LevelNavMesh>();

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.SetParent(level.transform);
        ground.transform.localScale = new Vector3(6, 1, 6); // 60m x 60m
        Paint(ground, mGround);

        // 원룸 건물들 (위치, 크기)
        var buildings = new (Vector3 pos, Vector3 size)[]
        {
            (new Vector3(-15, 3, -16), new Vector3(8, 6, 10)),
            (new Vector3(-15, 3, 0),   new Vector3(8, 6, 10)),
            (new Vector3(-15, 4, 17),  new Vector3(8, 8, 9)),
            (new Vector3(16, 3, -16),  new Vector3(10, 6, 8)),
            (new Vector3(16, 3, 20),   new Vector3(10, 6, 8)),
            (new Vector3(0, 2.5f, 23), new Vector3(9, 5, 6)),
            (new Vector3(5, 2, 9),     new Vector3(5, 4, 5)),
        };
        int bi = 1;
        foreach (var b in buildings) Box($"원룸_{bi++}", level.transform, b.pos, b.size, mBuilding);

        // 편의점 (10 x 8, 앞쪽에 2.5m 입구)
        var store = new GameObject("편의점");
        store.transform.SetParent(level.transform);
        float wh = 3.5f, wt = 0.3f;
        Box("벽_뒤", store.transform, new Vector3(14, wh / 2, 8), new Vector3(10, wh, wt), mStore);
        Box("벽_왼", store.transform, new Vector3(9, wh / 2, 4), new Vector3(wt, wh, 8), mStore);
        Box("벽_오", store.transform, new Vector3(19, wh / 2, 4), new Vector3(wt, wh, 8), mStore);
        Box("벽_앞1", store.transform, new Vector3(10.875f, wh / 2, 0), new Vector3(3.75f, wh, wt), mStore);
        Box("벽_앞2", store.transform, new Vector3(17.125f, wh / 2, 0), new Vector3(3.75f, wh, wt), mStore);

        var shelf1 = Box("선반_1", store.transform, new Vector3(11.5f, 0.75f, 6.5f), new Vector3(3, 1.5f, 0.6f), mShelf);
        Loot(shelf1, "편의점 선반", null, (kimbap, 0.7f, 2), (ramen, 0.5f, 2));
        var shelf2 = Box("선반_2", store.transform, new Vector3(16.5f, 0.75f, 6.5f), new Vector3(3, 1.5f, 0.6f), mShelf);
        Loot(shelf2, "편의점 선반", null, (ramen, 0.5f, 1), (bandage, 0.5f, 2));
        var fridge = Box("냉장고", store.transform, new Vector3(18.3f, 1f, 3f), new Vector3(0.8f, 2f, 2f), mShelf);
        Loot(fridge, "음료 냉장고", null, (water, 0.8f, 3), (coffee, 0.6f, 2));
        var counter = Box("계산대", store.transform, new Vector3(11f, 0.5f, 2.5f), new Vector3(1.2f, 1f, 2.5f), mShelf);
        Loot(counter, "계산대", null, (bandage, 0.4f, 1), (kimbap, 0.3f, 1));

        // 거리의 컨테이너들
        var toolbox = Box("공구함", level.transform, new Vector3(3, 0.4f, -17), new Vector3(1.2f, 0.8f, 0.6f), mBox);
        Loot(toolbox, "공구함", new List<ItemData> { wrench });
        var trash1 = Box("쓰레기통_1", level.transform, new Vector3(-10.3f, 0.5f, -8), new Vector3(1, 1, 1), mBox);
        Loot(trash1, "쓰레기통", null, (water, 0.3f, 1), (kimbap, 0.2f, 1));
        var trash2 = Box("쓰레기통_2", level.transform, new Vector3(-10.3f, 0.5f, 9), new Vector3(1, 1, 1), mBox);
        Loot(trash2, "쓰레기통", null, (coffee, 0.4f, 1));
        var scrap = Box("고철더미", level.transform, new Vector3(21, 0.5f, -9), new Vector3(2, 1, 1.5f), mBox);
        Loot(scrap, "고철더미", null, (pipe, 0.8f, 1));
        var box2 = Box("택배상자", level.transform, new Vector3(4, 0.35f, 18.5f), new Vector3(0.9f, 0.7f, 0.9f), mBox);
        Loot(box2, "택배상자", null, (ramen, 0.6f, 2), (water, 0.5f, 1), (bandage, 0.3f, 1));

        // ---------- 플레이어 ----------
        var player = new GameObject("Player");
        player.tag = "Player";
        player.transform.position = new Vector3(0, 0.1f, -20);
        var cc = player.AddComponent<CharacterController>();
        cc.height = 1.8f; cc.center = new Vector3(0, 0.9f, 0); cc.radius = 0.35f;
        player.AddComponent<PlayerStats>();
        player.AddComponent<PlayerController>();
        player.AddComponent<Inventory>();
        player.AddComponent<Interactor>();
        player.AddComponent<PlayerCombat>();
        player.AddComponent<HUD>();
        Visual(PrimitiveType.Capsule, "Body", player.transform, new Vector3(0, 0.9f, 0), new Vector3(0.7f, 0.9f, 0.7f), mPlayer);
        Visual(PrimitiveType.Cube, "Nose", player.transform, new Vector3(0, 1.5f, 0.35f), new Vector3(0.3f, 0.15f, 0.3f), mPlayer);

        // ---------- 카메라 / 빛 / 매니저 ----------
        Camera cam = Camera.main;
        var iso = cam.gameObject.AddComponent<IsometricCamera>();
        iso.target = player.transform;
        Quaternion camRot = Quaternion.Euler(iso.pitch, iso.yaw, 0);
        cam.transform.SetPositionAndRotation(player.transform.position - camRot * Vector3.forward * iso.distance, camRot);
        player.GetComponent<PlayerController>().cameraTransform = cam.transform;

        Light sun = Object.FindFirstObjectByType<Light>();
        var gm = new GameObject("GameManager").AddComponent<GameManager>();
        gm.sun = sun;

        // ---------- 안드로이드 ----------
        var androids = new GameObject("Androids");
        MakeAndroid("작업형_안드로이드_1", androids.transform, mAndroid, mVisor, new Vector3(0, 0, -2),
            new[] { new Vector3(0, 0, -6), new Vector3(0, 0, 14), new Vector3(-8, 0, 14), new Vector3(-8, 0, -6) });
        MakeAndroid("작업형_안드로이드_2", androids.transform, mAndroid, mVisor, new Vector3(14, 0, -4),
            new[] { new Vector3(10, 0, -4), new Vector3(22, 0, -4), new Vector3(22, 0, 12), new Vector3(14, 0, 4) });
        MakeAndroid("작업형_안드로이드_3", androids.transform, mAndroid, mVisor, new Vector3(-8, 0, 22),
            new[] { new Vector3(-8, 0, 25), new Vector3(10, 0, 26), new Vector3(10, 0, 15) });

        // ---------- 저장 ----------
        EditorSceneManager.SaveScene(scene, ScenePath);
        AddToBuildSettings(ScenePath);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = player;
        Debug.Log("<b>[Gumi]</b> 프로토타입 씬 생성 완료! ▶ 버튼을 눌러 플레이하세요. (주황색 = 뒤질 수 있는 물건)");
    }

    // ================= 헬퍼 =================

    static void MakeAndroid(string name, Transform parent, Material body, Material visor, Vector3 pos, Vector3[] wps)
    {
        var a = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        a.name = name;
        a.transform.SetParent(parent);
        a.transform.position = pos + Vector3.up * 1.1f;
        a.transform.localScale = new Vector3(1.1f, 1.1f, 1.1f); // 키 약 2.2m
        Paint(a, body);
        Visual(PrimitiveType.Cube, "Visor", a.transform, new Vector3(0, 0.55f, 0.4f), new Vector3(0.6f, 0.12f, 0.2f), visor);

        var ai = a.AddComponent<AndroidAI>(); // NavMeshAgent, Damageable 자동 추가
        var agent = a.GetComponent<NavMeshAgent>();
        agent.baseOffset = 1.1f;
        agent.height = 2.2f;
        agent.radius = 0.5f;
        agent.angularSpeed = 240f;
        agent.enabled = false; // NavMesh가 런타임에 구워진 뒤 AndroidAI.Start에서 켬
        a.GetComponent<Damageable>().maxHealth = 100f;

        var wpRoot = new GameObject(name + "_순찰경로");
        wpRoot.transform.SetParent(parent);
        var list = new Transform[wps.Length];
        for (int i = 0; i < wps.Length; i++)
        {
            var wp = new GameObject($"WP{i + 1}");
            wp.transform.SetParent(wpRoot.transform);
            wp.transform.position = wps[i];
            list[i] = wp.transform;
        }
        ai.waypoints = list;
    }

    static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material m)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        g.transform.SetParent(parent);
        g.transform.position = pos;
        g.transform.localScale = size;
        g.isStatic = true;
        Paint(g, m);
        return g;
    }

    static void Visual(PrimitiveType t, string name, Transform parent, Vector3 localPos, Vector3 scale, Material m)
    {
        var g = GameObject.CreatePrimitive(t);
        g.name = name;
        Object.DestroyImmediate(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        g.transform.localScale = scale;
        Paint(g, m);
    }

    static void Loot(GameObject g, string label, List<ItemData> fixedItems, params (ItemData item, float chance, int max)[] table)
    {
        var lc = g.AddComponent<LootContainer>();
        lc.containerName = label;
        if (fixedItems != null) lc.fixedItems = fixedItems;
        foreach (var e in table)
            lc.lootTable.Add(new LootContainer.LootEntry { item = e.item, chance = e.chance, maxCount = e.max });
    }

    static void Paint(GameObject g, Material m)
    {
        var r = g.GetComponent<Renderer>();
        if (r != null && m != null) r.sharedMaterial = m;
    }

    static Material Mat(string name, Color c)
    {
        string path = $"{Root}/Materials/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;

        Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        m = new Material(sh);
        m.SetColor("_BaseColor", c);
        m.color = c;
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static ItemData Item(string file, string display, ItemType type, float weight, float h = 0, float t = 0, float hp = 0)
    {
        var it = LoadOrCreate(file);
        it.displayName = display; it.type = type; it.weight = weight;
        it.hungerRestore = h; it.thirstRestore = t; it.healthRestore = hp;
        EditorUtility.SetDirty(it);
        return it;
    }

    static ItemData Weapon(string file, string display, float weight, float dmg, float range, float cd, float noise)
    {
        var it = LoadOrCreate(file);
        it.displayName = display; it.type = ItemType.Weapon; it.weight = weight;
        it.damage = dmg; it.attackRange = range; it.attackCooldown = cd; it.attackNoise = noise;
        EditorUtility.SetDirty(it);
        return it;
    }

    static ItemData LoadOrCreate(string file)
    {
        string path = $"{Root}/Items/{file}.asset";
        var it = AssetDatabase.LoadAssetAtPath<ItemData>(path);
        if (it != null) return it;
        it = ScriptableObject.CreateInstance<ItemData>();
        AssetDatabase.CreateAsset(it, path);
        return it;
    }

    static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder($"{parent}/{name}")) AssetDatabase.CreateFolder(parent, name);
    }

    static void AddToBuildSettings(string path)
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        scenes.RemoveAll(s => s.path == path);
        scenes.Insert(0, new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
#endif
