using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 플레이할 때마다 새로운 구미 시가지를 만든다.
/// 서쪽(왼쪽 열)은 원룸촌·상가, 가운데는 아파트·공원, 동쪽은 공단.
/// 블록 사이는 모두 도로라서 어디서든 길이 이어진다.
/// LevelNavMesh보다 먼저 실행되어야 하므로 실행 순서를 더 앞당겼다.
/// </summary>
[DefaultExecutionOrder(-2000)]
public class CityGenerator : MonoBehaviour
{
    public enum BlockType { Oneroom, Shop, Apartment, Park, Factory }

    [Header("크기")]
    public int blocksX = 5;
    public int blocksZ = 5;
    public float blockSize = 36f;
    public float roadWidth = 8f;

    [Header("무작위")]
    public int seed = 0;               // 0이면 매번 다른 도시
    public int baseAndroids = 8;
    public int androidsPerFactory = 3;

    [Header("머티리얼")]
    public Material groundMat, roadMat, buildingMat, apartmentMat, storeMat, factoryMat,
                    shelfMat, containerMat, treeMat, androidMat, visorMat;

    [Header("아이템")]
    public ItemData kimbap, ramen, water, coffee, bandage, wrench, pipe;

    public int UsedSeed { get; private set; }

    System.Random rng;
    Transform root, androidRoot;
    BlockType[,] map;

    float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
    int RI(int a, int bExclusive) => rng.Next(a, bExclusive);
    bool Chance(float p) => rng.NextDouble() < p;

    float Origin(int i) => i * (blockSize + roadWidth) + roadWidth;   // 블록 i의 시작 좌표
    float RoadCenter(int i) => i * (blockSize + roadWidth) + roadWidth * 0.5f; // 도로 i의 중앙
    float TotalX => blocksX * (blockSize + roadWidth) + roadWidth;
    float TotalZ => blocksZ * (blockSize + roadWidth) + roadWidth;

    void Awake()
    {
        UsedSeed = seed != 0 ? seed : System.Environment.TickCount;
        rng = new System.Random(UsedSeed);
        root = transform;
        androidRoot = new GameObject("Androids").transform;

        BuildGround();
        PlanBlocks();
        for (int x = 0; x < blocksX; x++)
            for (int z = 0; z < blocksZ; z++)
                BuildBlock(x, z, map[x, z]);

        PlacePlayer();
        SpawnAndroids();
        Debug.Log($"[Gumi] 도시 생성 완료 (seed {UsedSeed}, {blocksX}x{blocksZ} 블록)");
    }

    // ================= 계획 =================

    void PlanBlocks()
    {
        map = new BlockType[blocksX, blocksZ];
        for (int x = 0; x < blocksX; x++)
        {
            float east = blocksX > 1 ? x / (float)(blocksX - 1) : 0f; // 0 = 서쪽, 1 = 동쪽
            for (int z = 0; z < blocksZ; z++)
            {
                BlockType t;
                double r = rng.NextDouble();
                if (east < 0.3f) t = r < 0.65 ? BlockType.Oneroom : BlockType.Shop;
                else if (east < 0.7f) t = r < 0.45 ? BlockType.Apartment : r < 0.65 ? BlockType.Park : r < 0.85 ? BlockType.Shop : BlockType.Oneroom;
                else t = r < 0.75 ? BlockType.Factory : BlockType.Park;
                map[x, z] = t;
            }
        }
        // 시작 블록(남서쪽)은 원룸촌, 그 옆에는 편의점이 하나는 있게
        map[0, 0] = BlockType.Oneroom;
        if (blocksZ > 1) map[0, 1] = BlockType.Shop;
    }

    // ================= 바닥 =================

    void BuildGround()
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = "Ground";
        g.transform.SetParent(root);
        g.transform.position = new Vector3(TotalX / 2, -0.5f, TotalZ / 2);
        g.transform.localScale = new Vector3(TotalX, 1, TotalZ);
        Paint(g, roadMat);

        // 블록 바닥(인도/부지)은 살짝 높고 색이 다르게
        for (int x = 0; x < blocksX; x++)
            for (int z = 0; z < blocksZ; z++)
            {
                var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
                p.name = $"부지_{x}_{z}";
                p.transform.SetParent(root);
                p.transform.position = new Vector3(Origin(x) + blockSize / 2, 0.05f, Origin(z) + blockSize / 2);
                p.transform.localScale = new Vector3(blockSize, 0.1f, blockSize);
                Paint(p, groundMat);
            }

        // 도시 경계 벽 (밖으로 떨어지지 않게)
        float h = 4f;
        Box("경계_남", root, new Vector3(TotalX / 2, h / 2, -0.5f), new Vector3(TotalX, h, 1), buildingMat);
        Box("경계_북", root, new Vector3(TotalX / 2, h / 2, TotalZ + 0.5f), new Vector3(TotalX, h, 1), buildingMat);
        Box("경계_서", root, new Vector3(-0.5f, h / 2, TotalZ / 2), new Vector3(1, h, TotalZ), buildingMat);
        Box("경계_동", root, new Vector3(TotalX + 0.5f, h / 2, TotalZ / 2), new Vector3(1, h, TotalZ), buildingMat);
    }

    // ================= 블록 =================

    void BuildBlock(int bx, int bz, BlockType type)
    {
        var block = new GameObject($"블록_{bx}_{bz}_{type}").transform;
        block.SetParent(root);
        float x0 = Origin(bx), z0 = Origin(bz), S = blockSize;

        switch (type)
        {
            case BlockType.Oneroom: BuildOneroom(block, x0, z0, S); break;
            case BlockType.Shop: BuildShop(block, x0, z0, S); break;
            case BlockType.Apartment: BuildApartment(block, x0, z0, S); break;
            case BlockType.Park: BuildPark(block, x0, z0, S); break;
            case BlockType.Factory: BuildFactory(block, x0, z0, S); break;
        }
    }

    void BuildOneroom(Transform p, float x0, float z0, float S)
    {
        // 2~3 x 2~3 필지로 나누고 사이에 골목
        int nx = RI(2, 4), nz = RI(2, 4);
        float alley = 3f;
        float lotX = (S - alley * (nx - 1)) / nx;
        float lotZ = (S - alley * (nz - 1)) / nz;
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
            {
                if (Chance(0.12f)) { MaybeContainer(p, x0 + i * (lotX + alley) + lotX / 2, z0 + j * (lotZ + alley) + lotZ / 2, "빈 공터"); continue; }
                float w = lotX * R(0.7f, 0.95f), d = lotZ * R(0.7f, 0.95f), h = R(6f, 12f);
                float cx = x0 + i * (lotX + alley) + lotX / 2, cz = z0 + j * (lotZ + alley) + lotZ / 2;
                Box("원룸", p, new Vector3(cx, h / 2, cz), new Vector3(w, h, d), buildingMat);

                // 건물 앞에 쓰레기통이나 택배상자
                float fz = cz - d / 2 - 0.8f;
                if (Chance(0.45f)) Container(p, "쓰레기통", new Vector3(cx + R(-w / 3, w / 3), 0.5f, fz), new Vector3(1, 1, 1),
                    (water, 0.25f, 1), (kimbap, 0.2f, 1), (coffee, 0.2f, 1));
                else if (Chance(0.35f)) Container(p, "택배상자", new Vector3(cx + R(-w / 3, w / 3), 0.35f, fz), new Vector3(0.9f, 0.7f, 0.9f),
                    (ramen, 0.5f, 2), (water, 0.4f, 1), (bandage, 0.3f, 1));
            }
    }

    void BuildShop(Transform p, float x0, float z0, float S)
    {
        // 편의점 (남쪽 도로를 향해 문)
        float sw = R(11f, 14f), sd = R(9f, 11f);
        float sx = x0 + R(sw / 2 + 1, S - sw / 2 - 1), sz = z0 + sd / 2 + 1.5f;
        Hollow(p, "편의점", sx, sz, sw, sd, 3.5f, storeMat, 2.6f);

        float inner = sd / 2 - 1.2f;
        Container(p, "편의점 선반", new Vector3(sx - sw / 4, 0.75f, sz + inner), new Vector3(sw / 3, 1.5f, 0.6f), shelfMat,
            (kimbap, 0.7f, 2), (ramen, 0.5f, 2));
        Container(p, "편의점 선반", new Vector3(sx + sw / 4, 0.75f, sz + inner), new Vector3(sw / 3, 1.5f, 0.6f), shelfMat,
            (ramen, 0.5f, 1), (bandage, 0.5f, 2), (kimbap, 0.4f, 1));
        Container(p, "음료 냉장고", new Vector3(sx + sw / 2 - 0.8f, 1f, sz), new Vector3(0.8f, 2f, 2.2f), shelfMat,
            (water, 0.8f, 3), (coffee, 0.6f, 2));
        Container(p, "계산대", new Vector3(sx - sw / 2 + 1.5f, 0.5f, sz - 1f), new Vector3(1.2f, 1f, 2.5f), shelfMat,
            (bandage, 0.4f, 1), (kimbap, 0.3f, 1));

        // 뒤쪽엔 작은 상가 건물들
        int n = RI(2, 4);
        float w = (S - 2) / n;
        for (int i = 0; i < n; i++)
        {
            float h = R(5f, 9f), d = R(8f, 12f);
            Box("상가", p, new Vector3(x0 + 1 + w * i + w / 2, h / 2, z0 + S - d / 2 - 1), new Vector3(w - 2, h, d), buildingMat);
        }
    }

    void BuildApartment(Transform p, float x0, float z0, float S)
    {
        // 길쭉한 고층동 2개 + 주차된 차
        for (int k = 0; k < 2; k++)
        {
            float w = R(22f, 28f), d = R(8f, 10f), h = R(18f, 30f);
            float cz = z0 + (k == 0 ? S * 0.28f : S * 0.75f);
            Box("아파트", p, new Vector3(x0 + S / 2, h / 2, cz), new Vector3(w, h, d), apartmentMat);
            Container(p, "택배 보관함", new Vector3(x0 + S / 2 + R(-6, 6), 0.6f, cz - d / 2 - 0.8f), new Vector3(1.6f, 1.2f, 0.6f),
                (ramen, 0.6f, 2), (water, 0.5f, 2), (bandage, 0.4f, 1));
        }
        int cars = RI(2, 5);
        for (int i = 0; i < cars; i++)
        {
            Vector3 pos = new Vector3(x0 + R(3, S - 3), 0.75f, z0 + S * 0.52f + R(-2, 2));
            Container(p, "주차된 차 트렁크", pos, new Vector3(4.2f, 1.5f, 1.9f), shelfMat,
                (water, 0.4f, 1), (pipe, 0.15f, 1), (coffee, 0.4f, 1), (bandage, 0.3f, 1));
        }
    }

    void BuildPark(Transform p, float x0, float z0, float S)
    {
        int trees = RI(8, 15);
        for (int i = 0; i < trees; i++)
        {
            float x = x0 + R(2, S - 2), z = z0 + R(2, S - 2);
            var trunk = Box("나무", p, new Vector3(x, 1.5f, z), new Vector3(0.5f, 3f, 0.5f), treeMat);
            var crown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            DestroyImmediate(crown.GetComponent<Collider>());
            crown.transform.SetParent(trunk.transform.parent);
            crown.transform.position = new Vector3(x, 3.6f, z);
            crown.transform.localScale = Vector3.one * R(2.5f, 3.8f);
            Paint(crown, treeMat);
        }
        Container(p, "자판기", new Vector3(x0 + S / 2, 1f, z0 + 1.2f), new Vector3(1.2f, 2f, 0.8f), shelfMat,
            (water, 0.7f, 2), (coffee, 0.7f, 2));
    }

    void BuildFactory(Transform p, float x0, float z0, float S)
    {
        // 큰 공장동 (문 두 개) + 내부 자재
        float w = S - R(4f, 8f), d = S - R(8f, 12f), h = R(7f, 10f);
        float cx = x0 + S / 2, cz = z0 + d / 2 + 2f;
        Hollow(p, "공장", cx, cz, w, d, h, factoryMat, 4f, true);

        Container(p, "공구함", new Vector3(cx - w / 2 + 1.5f, 0.5f, cz + d / 2 - 1.2f), new Vector3(1.4f, 1f, 0.7f),
            (wrench, 0.5f, 1), (pipe, 0.4f, 1), (bandage, 0.3f, 1));
        Container(p, "고철더미", new Vector3(cx + R(-w / 4, w / 4), 0.5f, cz + R(-d / 4, d / 4)), new Vector3(2f, 1f, 1.5f),
            (pipe, 0.6f, 1));
        int crates = RI(3, 7);
        for (int i = 0; i < crates; i++)
            Box("자재", p, new Vector3(cx + R(-w / 2 + 2, w / 2 - 2), 0.75f, cz + R(-d / 2 + 2, d / 2 - 2)), new Vector3(R(1.5f, 3f), 1.5f, R(1.5f, 3f)), shelfMat);

        // 뒤편 야적장
        Container(p, "부품 상자", new Vector3(x0 + R(3, S - 3), 0.5f, z0 + S - 1.5f), new Vector3(1.5f, 1f, 1f),
            (bandage, 0.4f, 2), (wrench, 0.2f, 1));
    }

    void MaybeContainer(Transform p, float x, float z, string label)
    {
        if (Chance(0.5f)) Container(p, "버려진 가방", new Vector3(x, 0.3f, z), new Vector3(0.7f, 0.6f, 0.5f),
            (kimbap, 0.4f, 1), (water, 0.4f, 1), (bandage, 0.3f, 1));
    }

    // ================= 플레이어 / 안드로이드 =================

    void PlacePlayer()
    {
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player == null) return;

        // 남서쪽 원룸촌 앞 도로
        Vector3 start = new Vector3(Origin(0) + blockSize * 0.5f, 0.1f, RoadCenter(0));
        var cc = player.GetComponent<CharacterController>();
        if (cc) cc.enabled = false;
        player.transform.position = start;
        if (cc) cc.enabled = true;

        // 첫 무기는 확실히 근처에
        Container(root, "공구함", start + new Vector3(3f, 0.3f, 1.8f), new Vector3(1.2f, 0.8f, 0.6f), containerMat,
            new List<ItemData> { wrench }, (water, 0.6f, 1), (kimbap, 0.5f, 1));

        var cam = FindFirstObjectByType<IsometricCamera>();
        if (cam != null) cam.Snap();
    }

    void SpawnAndroids()
    {
        var player = GameObject.FindGameObjectWithTag("Player");
        Vector3 pp = player ? player.transform.position : Vector3.zero;

        int factories = 0;
        var factoryCenters = new List<Vector3>();
        for (int x = 0; x < blocksX; x++)
            for (int z = 0; z < blocksZ; z++)
                if (map[x, z] == BlockType.Factory)
                {
                    factories++;
                    factoryCenters.Add(new Vector3(Origin(x) + blockSize / 2, 0, Origin(z) + blockSize / 2));
                }

        int count = baseAndroids + factories * androidsPerFactory;
        int made = 0, guard = 0;
        while (made < count && guard++ < count * 20)
        {
            // 절반은 공단 근처, 나머지는 시내 도로 아무 곳
            Vector3 pos = factoryCenters.Count > 0 && Chance(0.5f)
                ? RoadPointNear(factoryCenters[RI(0, factoryCenters.Count)], 30f)
                : RandomRoadPoint();
            if (Vector3.Distance(pos, pp) < 35f) continue; // 시작하자마자 붙잡히지 않게

            var wps = new Vector3[RI(3, 6)];
            for (int i = 0; i < wps.Length; i++) wps[i] = RoadPointNear(pos, R(18f, 40f));
            MakeAndroid($"작업형_안드로이드_{made + 1}", pos, wps);
            made++;
        }
    }

    Vector3 RandomRoadPoint()
    {
        if (Chance(0.5f))
            return new Vector3(RoadCenter(RI(0, blocksX + 1)) + R(-2, 2), 0, R(4, TotalZ - 4));
        return new Vector3(R(4, TotalX - 4), 0, RoadCenter(RI(0, blocksZ + 1)) + R(-2, 2));
    }

    Vector3 RoadPointNear(Vector3 c, float radius)
    {
        for (int t = 0; t < 12; t++)
        {
            Vector3 p = RandomRoadPoint();
            if (Vector3.Distance(p, c) <= radius) return p;
        }
        // 가장 가까운 세로 도로 위로 끌어온다
        int i = Mathf.Clamp(Mathf.RoundToInt((c.x - roadWidth * 0.5f) / (blockSize + roadWidth)), 0, blocksX);
        return new Vector3(RoadCenter(i), 0, Mathf.Clamp(c.z + R(-radius, radius), 4, TotalZ - 4));
    }

    void MakeAndroid(string name, Vector3 pos, Vector3[] wps)
    {
        var a = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        a.SetActive(false); // 컴포넌트를 다 붙인 뒤 켠다
        a.name = name;
        a.transform.SetParent(androidRoot);
        a.transform.position = pos + Vector3.up * 1.1f;
        a.transform.rotation = Quaternion.Euler(0, R(0, 360), 0);
        a.transform.localScale = Vector3.one * 1.1f;
        Paint(a, androidMat);

        var visor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        DestroyImmediate(visor.GetComponent<Collider>());
        visor.transform.SetParent(a.transform, false);
        visor.transform.localPosition = new Vector3(0, 0.55f, 0.4f);
        visor.transform.localScale = new Vector3(0.6f, 0.12f, 0.2f);
        Paint(visor, visorMat);

        var agent = a.AddComponent<NavMeshAgent>();
        agent.baseOffset = 1.1f;
        agent.height = 2.2f;
        agent.radius = 0.5f;
        agent.angularSpeed = 240f;
        agent.enabled = false;
        a.AddComponent<Damageable>().maxHealth = 100f;
        var ai = a.AddComponent<AndroidAI>();

        var wpRoot = new GameObject(name + "_순찰경로").transform;
        wpRoot.SetParent(androidRoot);
        var list = new Transform[wps.Length];
        for (int i = 0; i < wps.Length; i++)
        {
            var wp = new GameObject($"WP{i + 1}").transform;
            wp.SetParent(wpRoot);
            wp.position = wps[i];
            list[i] = wp;
        }
        ai.waypoints = list;
        a.SetActive(true);
    }

    // ================= 헬퍼 =================

    /// <summary>안이 비어 있는 건물. 남쪽 벽에 문(틈)을 낸다. bothSides면 북쪽에도.</summary>
    void Hollow(Transform p, string name, float cx, float cz, float w, float d, float h, Material m, float door, bool bothSides = false)
    {
        var g = new GameObject(name).transform;
        g.SetParent(p);
        const float t = 0.3f;
        Box("벽_서", g, new Vector3(cx - w / 2, h / 2, cz), new Vector3(t, h, d), m);
        Box("벽_동", g, new Vector3(cx + w / 2, h / 2, cz), new Vector3(t, h, d), m);
        WallWithDoor(g, cx, cz - d / 2, w, h, m, door);
        if (bothSides) WallWithDoor(g, cx, cz + d / 2, w, h, m, door);
        else Box("벽_북", g, new Vector3(cx, h / 2, cz + d / 2), new Vector3(w, h, t), m);
    }

    void WallWithDoor(Transform g, float cx, float z, float w, float h, Material m, float door)
    {
        const float t = 0.3f;
        float seg = (w - door) / 2;
        Box("벽", g, new Vector3(cx - door / 2 - seg / 2, h / 2, z), new Vector3(seg, h, t), m);
        Box("벽", g, new Vector3(cx + door / 2 + seg / 2, h / 2, z), new Vector3(seg, h, t), m);
    }

    GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material m)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        g.transform.SetParent(parent);
        g.transform.position = pos;
        g.transform.localScale = size;
        Paint(g, m);
        return g;
    }

    void Container(Transform p, string label, Vector3 pos, Vector3 size, params (ItemData item, float chance, int max)[] table)
        => Container(p, label, pos, size, containerMat, null, table);

    void Container(Transform p, string label, Vector3 pos, Vector3 size, Material m, params (ItemData item, float chance, int max)[] table)
        => Container(p, label, pos, size, m, null, table);

    void Container(Transform p, string label, Vector3 pos, Vector3 size, Material m, List<ItemData> fixedItems,
        params (ItemData item, float chance, int max)[] table)
    {
        var g = Box(label, p, pos, size, m == shelfMat ? shelfMat : containerMat);
        // 뒤질 수 있는 물건은 항상 주황색 띠를 둘러 눈에 띄게
        if (m == shelfMat)
        {
            var band = GameObject.CreatePrimitive(PrimitiveType.Cube);
            DestroyImmediate(band.GetComponent<Collider>());
            band.transform.SetParent(g.transform, false);
            band.transform.localPosition = new Vector3(0, 0.5f, 0);
            band.transform.localScale = new Vector3(1.02f, 0.12f, 1.02f);
            Paint(band, containerMat);
        }

        var lc = g.AddComponent<LootContainer>();
        lc.containerName = label;
        if (fixedItems != null) lc.fixedItems = new List<ItemData>(fixedItems);
        foreach (var e in table)
            if (e.item != null) lc.lootTable.Add(new LootContainer.LootEntry { item = e.item, chance = e.chance, maxCount = e.max });
    }

    static void Paint(GameObject g, Material m)
    {
        var r = g.GetComponent<Renderer>();
        if (r != null && m != null) r.sharedMaterial = m;
    }
}
