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
public partial class CityGenerator : MonoBehaviour
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

    [Header("주기적 생성 (게임 시간 기준)")]
    public float spawnIntervalHours = 2f;   // 낮 기준 출고 간격 (밤에는 절반)
    public int maxAliveBase = 14;           // 1일차 최대 동시 개체 수
    public int maxAlivePerDay = 4;          // 하루 지날 때마다 늘어나는 최대 수
    public float minSpawnDistance = 45f;    // 플레이어 근처에는 생기지 않게

    [Header("머티리얼")]
    public Material groundMat, roadMat, buildingMat, apartmentMat, storeMat, factoryMat,
                    shelfMat, containerMat, treeMat, androidMat, visorMat;

    [Header("아이템")]
    public ItemData kimbap, ramen, water, coffee, bandage, wrench, pipe;

    public int UsedSeed { get; private set; }

    System.Random rng;
    Transform root, androidRoot;
    BlockType[,] map;
    bool safehouseBuilt;

    /// <summary>거점(발전기) 위치와 확실한 연료통 위치 — 시나리오 목표 표시에 쓴다</summary>
    public static Vector3 SafehousePos { get; private set; }
    public static Vector3 FuelHintPos { get; private set; }

    ItemData fuel;
    /// <summary>연료통 (런타임에 만드는 아이템)</summary>
    public ItemData Fuel
    {
        get
        {
            if (fuel == null)
            {
                fuel = ScriptableObject.CreateInstance<ItemData>();
                fuel.name = "Item_Fuel";
                fuel.displayName = "연료통";
                fuel.type = ItemType.Misc;
                fuel.weight = 2f;
            }
            return fuel;
        }
    }
    int apartmentCount, factoryCount;
    readonly List<Vector3> factoryCenters = new List<Vector3>();
    float spawnTimer;
    int spawnSerial;

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
        FuelHintText = "동쪽 도로의 쓰러진 배달 오토바이를 뒤져 보자";

        // 실제 구미 지도 (OpenStreetMap)가 있으면 그걸로 만든다
        if (TryBuildRealMap())
        {
            PlacePlayerReal();
            SpawnAndroidsReal();
            return;
        }

        BuildGround();
        PlanBlocks();
        for (int x = 0; x < blocksX; x++)
            for (int z = 0; z < blocksZ; z++)
                BuildBlock(x, z, map[x, z]);
        BuildStreets();

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
                bool homeLot = !safehouseBuilt && Mathf.Approximately(x0, Origin(0)) && Mathf.Approximately(z0, Origin(0)) && j == 0 && i == (nx - 1) / 2;
                if (!homeLot && Chance(0.12f)) { MaybeContainer(p, x0 + i * (lotX + alley) + lotX / 2, z0 + j * (lotZ + alley) + lotZ / 2, "빈 공터"); continue; }
                float w = lotX * R(0.75f, 0.92f), d = lotZ * R(0.75f, 0.9f);
                float cx = x0 + i * (lotX + alley) + lotX / 2, cz = z0 + j * (lotZ + alley) + lotZ / 2;
                bool south = j < (nz + 1) / 2;          // 남쪽 절반은 남쪽 도로, 북쪽 절반은 북쪽 도로를 본다
                float yaw = south ? 0f : 180f;
                var pos = new Vector3(cx, 0.1f, cz);

                // 시작 블록의 남쪽 가운데 필지 = 플레이어의 원룸(거점). 1층 필로티에 발전기
                if (homeLot)
                {
                    safehouseBuilt = true;
                    BuildingKit.Oneroom(p, pos, 0f, w, d, 3, rng, true);
                    Vector3 genPos = pos + new Vector3(w / 4f, 0f, -d / 4f);
                    Generator.FuelItem = Fuel;
                    Generator.Build(p, genPos, 180f);
                    SafehousePos = genPos;
                    continue;
                }

                if (Chance(0.72f)) Kenney(p, Pick(Commercial), pos, yaw, w, d, () => BuildingKit.Oneroom(p, pos, yaw, w, d, RI(2, 4), rng));
                else Kenney(p, Pick(Suburban), pos, yaw, w, d, () => BuildingKit.Shop(p, pos, yaw, w, d, RI(1, 3), rng));

                // 건물 앞에 쓰레기통이나 택배상자
                float fz = south ? cz - d / 2 - 0.8f : cz + d / 2 + 0.8f;
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
        var store = Hollow(p, "편의점", sx, sz, sw, sd, 3.5f, storeMat, 2.6f);
        DecorateStore(store, sx, sz, sw, sd, 2.6f);

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
            float d = R(8f, 11f);
            var sp = new Vector3(x0 + 1 + w * i + w / 2, 0.1f, z0 + S - d / 2 - 1);
            float sw2 = w - 2, sd2 = d;
            Kenney(p, Pick(Commercial), sp, 180f, sw2, sd2, () => BuildingKit.Shop(p, sp, 180f, sw2, sd2, RI(1, 4), rng));
        }
    }

    void BuildApartment(Transform p, float x0, float z0, float S)
    {
        // 길쭉한 고층동 2개 + 주차된 차
        for (int k = 0; k < 2; k++)
        {
            float w = R(22f, 28f), d = R(8f, 10f);
            int floors = RI(6, 11);
            float cz = z0 + (k == 0 ? S * 0.28f : S * 0.75f);
            var ap = new Vector3(x0 + S / 2, 0.1f, cz);
            int num = 101 + (apartmentCount++);
            Kenney(p, Pick(Skyscrapers), ap, 0f, w, d, () => BuildingKit.Apartment(p, ap, 0f, w, d, floors, num, rng));
            Container(p, "택배 보관함", new Vector3(x0 + S / 2 + R(-6, 6), 0.6f, cz - d / 2 - 0.8f), new Vector3(1.6f, 1.2f, 0.6f),
                (ramen, 0.6f, 2), (water, 0.5f, 2), (bandage, 0.4f, 1));
        }
        int cars = RI(2, 5);
        for (int i = 0; i < cars; i++)
        {
            Vector3 pos = new Vector3(x0 + R(4, S - 4), 0.1f, z0 + S * 0.52f + R(-1, 1));
            var car = BuildingKit.Car(p, pos, Chance(0.5f) ? 90f : 270f, rng);
            AddLoot(car.GetChild(0).gameObject, "주차된 차 트렁크",
                (water, 0.4f, 1), (pipe, 0.15f, 1), (coffee, 0.4f, 1), (bandage, 0.3f, 1), (Fuel, 0.3f, 1));
            Kit.Box(car, new Vector3(0, 0.92f, 1.9f), new Vector3(1.4f, 0.06f, 0.5f), Kit.Loot, name: "트렁크 표시");
        }
    }

    void BuildPark(Transform p, float x0, float z0, float S)
    {
        int trees = RI(8, 15);
        for (int i = 0; i < trees; i++)
        {
            float x = x0 + R(2, S - 2), z = z0 + R(2, S - 2);
            bool big = Chance(0.7f);   // tree-small은 화분 덤불이라 작게
            var kt = AssetLib.Spawn(big ? "Kenney/Suburban/tree-large" : "Kenney/Suburban/tree-small", p, new Vector3(x, 0.1f, z),
                R(0, 360), AssetLib.Fit.Height, new Vector3(0, big ? R(5.5f, 8f) : R(1.4f, 2.0f), 0), false, false, "나무");
            if (kt != null)
            {
                var trunkCol = Kit.Box(kt, new Vector3(0, 1.2f, 0), new Vector3(0.4f, 2.4f, 0.4f), Color.gray, true, name: "줄기충돌");
                trunkCol.GetComponent<Renderer>().enabled = false;
                continue;
            }
            var tree = Kit.Group(p, "나무", new Vector3(x, 0.1f, z), Quaternion.Euler(0, R(0, 360), 0));
            Kit.Box(tree, new Vector3(0, 1.4f, 0), new Vector3(0.4f, 2.8f, 0.4f), Kit.Hex("5A4636"), true, name: "줄기");
            float cs = R(2.4f, 3.6f);
            Color leaf = Chance(0.5f) ? Kit.Hex("3F5E3A") : Kit.Hex("4E7048");
            Kit.Box(tree, new Vector3(0, 3.2f, 0), new Vector3(cs, cs * 0.6f, cs), leaf, rot: Quaternion.Euler(0, 45, 0), name: "잎");
            Kit.Box(tree, new Vector3(0, 3.2f + cs * 0.45f, 0), new Vector3(cs * 0.7f, cs * 0.5f, cs * 0.7f), leaf * 1.1f, name: "잎");
        }
        Container(p, "자판기", new Vector3(x0 + S / 2, 1f, z0 + 1.2f), new Vector3(1.2f, 2f, 0.8f), shelfMat,
            (water, 0.7f, 2), (coffee, 0.7f, 2));
    }

    void BuildFactory(Transform p, float x0, float z0, float S)
    {
        // 큰 공장동 (문 두 개) + 내부 자재
        float w = S - R(4f, 8f), d = S - R(8f, 12f), h = R(7f, 10f);
        float cx = x0 + S / 2, cz = z0 + d / 2 + 2f;
        bool kenneyFactory = AssetLib.Spawn(Pick(Industrial), p, new Vector3(cx, 0.1f, cz), AssetLib.KenneyFrontYaw,
            AssetLib.Fit.Footprint, new Vector3(w, 0, d), true, true, "공장") != null;
        if (kenneyFactory)
        {
            factoryCount++;
            if (Chance(0.6f)) AssetLib.Spawn(Pick(Chimneys), p, new Vector3(cx + w / 2 - 2.5f, 0.1f, cz + d / 2 + 2.5f), 0,
                AssetLib.Fit.Height, new Vector3(0, R(12f, 18f), 0), true, true, "굴뚝");
            if (Chance(0.5f)) AssetLib.Spawn(Chance(0.5f) ? "Kenney/Industrial/detail-tank-large" : "Kenney/Industrial/water-tower", p,
                new Vector3(cx + R(-w / 3, 0), 0.1f, cz + d / 2 + 3f), 0, AssetLib.Fit.Height, new Vector3(0, R(5f, 9f), 0), true, true, "탱크");
        }
        else
        {
            var factory = Hollow(p, "공장", cx, cz, w, d, h, factoryMat, 4f, true);
            BuildingKit.FactoryDetails(factory, cx, cz, w, d, h, 4f, 1 + (factoryCount++), rng);
        }
        int boxes = RI(1, 4);
        for (int i = 0; i < boxes; i++)
        {
            var cpos = new Vector3(cx - w / 2 + 4f + i * 7f, 0.1f, cz + d / 2 + 3.6f);
            var kc = AssetLib.Spawn(Pick(Containers), p, cpos, 90f, AssetLib.Fit.Footprint, new Vector3(2.6f, 0, 6.2f), true, false, "컨테이너");
            if (kc == null)
            {
                var c = BuildingKit.Container(p, cpos, 90f, rng);
                if (Chance(0.4f)) BuildingKit.Container(c, new Vector3(0, 2.6f, 0), 0f, rng, true);
            }
        }

        float lootZ = kenneyFactory ? cz - d / 2 - 1.4f : cz + d / 2 - 1.2f;
        Container(p, "공구함", new Vector3(cx - w / 3, 0.5f, lootZ), new Vector3(1.4f, 1f, 0.7f),
            (wrench, 0.5f, 1), (pipe, 0.4f, 1), (bandage, 0.3f, 1), (Fuel, 0.4f, 1));
        Container(p, "고철더미", new Vector3(cx + R(0, w / 3), 0.5f, kenneyFactory ? lootZ : cz + R(-d / 4, d / 4)), new Vector3(2f, 1f, 1.5f),
            (pipe, 0.6f, 1));
        if (!kenneyFactory)
        {
            int crates = RI(3, 7);
            for (int i = 0; i < crates; i++)
                Box("자재", p, new Vector3(cx + R(-w / 2 + 2, w / 2 - 2), 0.75f, cz + R(-d / 2 + 2, d / 2 - 2)), new Vector3(R(1.5f, 3f), 1.5f, R(1.5f, 3f)), shelfMat);
        }

        // 뒤편 야적장
        Container(p, "부품 상자", new Vector3(x0 + R(3, S - 3), 0.5f, z0 + S - 1.5f), new Vector3(1.5f, 1f, 1f),
            (bandage, 0.4f, 2), (wrench, 0.2f, 1), (Fuel, 0.3f, 1));
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
        MakeStarterToolbox(start + new Vector3(3f, 0.2f, 1.8f));

        // 확실한 연료통: 같은 도로를 따라 동쪽, 다음 블록 앞에 쓰러진 배달 오토바이
        MakeFuelBike(new Vector3(Origin(1) + blockSize * 0.5f, 0.1f, RoadCenter(0) + roadWidth / 2f - 1.2f), 75f);

        var cam = FindFirstObjectByType<IsometricCamera>();
        if (cam != null) cam.Snap();
    }

    void MakeStarterToolbox(Vector3 pos)
    {
        Container(root, "공구함", pos + new Vector3(0, 0.1f, 0), new Vector3(1.2f, 0.8f, 0.6f), containerMat,
            new List<ItemData> { wrench }, (water, 0.6f, 1), (kimbap, 0.5f, 1));
    }

    void MakeFuelBike(Vector3 bike, float yaw)
    {
        var b = Kit.Group(root, "버려진 오토바이", bike, Quaternion.Euler(0, yaw, 0));
        var body = Kit.Box(b, new Vector3(0, 0.45f, 0), new Vector3(0.5f, 0.5f, 1.6f), Kit.Hex("E9ECEF"), true, name: "차체");
        Kit.Box(b, new Vector3(0, 0.78f, -0.25f), new Vector3(0.38f, 0.12f, 0.7f), Kit.Hex("2E3135"), name: "안장");
        Kit.Box(b, new Vector3(0, 0.95f, -0.6f), new Vector3(0.55f, 0.45f, 0.45f), Kit.Loot, name: "배달통");
        Kit.Cyl(b, new Vector3(0, 0.25f, 0.65f), new Vector3(0.5f, 0.12f, 0.5f), Kit.Dark, rot: Quaternion.Euler(0, 0, 90), name: "바퀴");
        Kit.Cyl(b, new Vector3(0, 0.25f, -0.65f), new Vector3(0.5f, 0.12f, 0.5f), Kit.Dark, rot: Quaternion.Euler(0, 0, 90), name: "바퀴");
        var lc = body.AddComponent<LootContainer>();
        lc.containerName = "버려진 배달 오토바이";
        lc.fixedItems = new List<ItemData> { Fuel };
        lc.lootTable.Add(new LootContainer.LootEntry { item = kimbap, chance = 0.6f, maxCount = 1 });
        FuelHintPos = bike;
    }

    void SpawnAndroids()
    {
        var player = GameObject.FindGameObjectWithTag("Player");
        Vector3 pp = player ? player.transform.position : Vector3.zero;

        int factories = 0;
        factoryCenters.Clear();
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
            MakeAndroid($"작업형_안드로이드_{++spawnSerial}", pos, wps);
            made++;
        }
    }

    // ================= 주기적 생성 =================

    void Update()
    {
        var gm = GameManager.Instance;
        if (gm == null) return;
        if (RealMapActive) UpdatePopulationReal();
        float h = gm.HourOfDay;
        bool night = h >= 18.5f || h < 6f;
        spawnTimer += gm.GameHoursThisFrame;
        if (spawnTimer < (night ? spawnIntervalHours * 0.5f : spawnIntervalHours)) return;
        spawnTimer = 0f;
        SpawnWave(gm.Day);
    }

    int AliveAndroids()
    {
        int n = 0;
        foreach (var ai in androidRoot.GetComponentsInChildren<AndroidAI>())
            if (ai.CurrentState != AndroidAI.State.Disabled) n++;
        return n;
    }

    /// <summary>공장에서 새 안드로이드가 출고된다. 날이 갈수록 많이, 최대 수까지.</summary>
    void SpawnWave(int day)
    {
        var player = GameObject.FindGameObjectWithTag("Player");
        Vector3 pp = player ? player.transform.position : Vector3.zero;

        int cap = maxAliveBase + (day - 1) * maxAlivePerDay;
        int want = Mathf.Min(1 + day / 2, cap - AliveAndroids());
        int made = 0;
        for (int k = 0; k < want; k++)
        {
            Vector3 pos = Vector3.zero;
            bool ok = false;
            for (int t = 0; t < 20 && !ok; t++)
            {
                pos = factoryCenters.Count > 0 && Chance(0.8f)
                    ? RoadPointNear(factoryCenters[RI(0, factoryCenters.Count)], 25f)
                    : RandomRoadPoint();
                ok = Vector3.Distance(pos, pp) >= minSpawnDistance;
            }
            if (!ok) continue;

            // 공장 주변을 돌다가 시내 쪽으로도 한 번씩 내려온다
            var wps = new Vector3[RI(3, 6)];
            for (int i = 0; i < wps.Length; i++)
                wps[i] = i == wps.Length - 1 ? RandomRoadPoint() : RoadPointNear(pos, R(18f, 40f));
            MakeAndroid($"작업형_안드로이드_{++spawnSerial}", pos, wps);
            made++;
        }

        if (made > 0)
            HUD.ShowMessage(factoryCenters.Count > 0
                ? "멀리 공단에서 사이렌이 울린다… 새 안드로이드가 출고됐다"
                : "어딘가에서 안드로이드가 깨어났다", 3.5f);
    }

    Vector3 RandomRoadPoint()
    {
        if (RealMapActive) return RealRoadPoint(PlayerPos(), 70f, 150f);
        if (Chance(0.5f))
            return new Vector3(RoadCenter(RI(0, blocksX + 1)) + R(-2, 2), 0, R(4, TotalZ - 4));
        return new Vector3(R(4, TotalX - 4), 0, RoadCenter(RI(0, blocksZ + 1)) + R(-2, 2));
    }

    Vector3 RoadPointNear(Vector3 c, float radius)
    {
        if (RealMapActive) return RealRoadPoint(c, 0f, radius);
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
    Transform Hollow(Transform p, string name, float cx, float cz, float w, float d, float h, Material m, float door, bool bothSides = false)
    {
        var g = new GameObject(name).transform;
        g.SetParent(p);
        g.gameObject.AddComponent<OccluderGroup>();
        const float t = 0.3f;
        Box("벽_서", g, new Vector3(cx - w / 2, h / 2, cz), new Vector3(t, h, d), m);
        Box("벽_동", g, new Vector3(cx + w / 2, h / 2, cz), new Vector3(t, h, d), m);
        WallWithDoor(g, cx, cz - d / 2, w, h, m, door);
        if (bothSides) WallWithDoor(g, cx, cz + d / 2, w, h, m, door);
        else Box("벽_북", g, new Vector3(cx, h / 2, cz + d / 2), new Vector3(w, h, t), m);
        return g;
    }

    void DecorateStore(Transform store, float sx, float sz, float sw, float sd, float door)
    {
        float front = sz - sd / 2;
        float seg = (sw - door) / 2;
        foreach (float sgn in new[] { -1f, 1f })
        {
            float x = sx + sgn * (door / 2 + seg / 2);
            Kit.Box(store, new Vector3(x, 1.25f, front - 0.17f), new Vector3(seg - 0.4f, 2.0f, 0.05f), Kit.GlassLight, name: "쇼윈도");
            for (int i = 0; i < 2; i++)
                Kit.Box(store, new Vector3(x, 0.75f + i * 0.7f, front - 0.2f), new Vector3(seg - 0.6f, 0.06f, 0.04f), Kit.Frame, name: "진열대");
        }
        Kit.Box(store, new Vector3(sx, 3.05f, front - 0.25f), new Vector3(sw + 0.2f, 0.8f, 0.3f), Kit.Hex("3A8A5C"), name: "간판");
        Kit.Label(store, "24시 편의점", new Vector3(sx, 3.05f, front - 0.42f), Kit.Facing(Vector3.back), 0.5f, Color.white);
        Kit.Box(store, new Vector3(sx, 2.55f, front - 0.75f), new Vector3(sw + 0.2f, 0.08f, 1.1f), Kit.Hex("2E6E4A"),
            rot: Quaternion.Euler(-12, 0, 0), name: "차양");
    }

    void AddLoot(GameObject g, string label, params (ItemData item, float chance, int max)[] table)
    {
        var lc = g.AddComponent<LootContainer>();
        lc.containerName = label;
        foreach (var e in table)
            if (e.item != null) lc.lootTable.Add(new LootContainer.LootEntry { item = e.item, chance = e.chance, maxCount = e.max });
    }

    // ================= 거리 =================

    bool InHorizontalRoad(float z) { for (int j = 0; j <= blocksZ; j++) if (Mathf.Abs(z - RoadCenter(j)) < roadWidth / 2 + 0.5f) return true; return false; }
    bool InVerticalRoad(float x) { for (int i = 0; i <= blocksX; i++) if (Mathf.Abs(x - RoadCenter(i)) < roadWidth / 2 + 0.5f) return true; return false; }

    // ================= Kenney 에셋 =================

    static readonly string[] Commercial = Names("Kenney/Commercial/building-", "abcdefghijklmn");
    static readonly string[] Skyscrapers = Names("Kenney/Commercial/building-skyscraper-", "abcde");
    static readonly string[] Suburban = Names("Kenney/Suburban/building-type-", "abcdefghijklmnopqrstu");
    static readonly string[] Industrial = Names("Kenney/Industrial/building-", "abcdefghijklmnopqrst");
    static readonly string[] Chimneys = { "Kenney/Industrial/chimney-large", "Kenney/Industrial/chimney-medium", "Kenney/Industrial/chimney-basic" };
    static readonly string[] Containers = { "Kenney/Industrial/shipping-container-a", "Kenney/Industrial/shipping-container-b", "Kenney/Industrial/shipping-container-c" };

    static string[] Names(string prefix, string letters)
    {
        var r = new string[letters.Length];
        for (int i = 0; i < letters.Length; i++) r[i] = prefix + letters[i];
        return r;
    }

    string Pick(string[] list) { return list[RI(0, list.Length)]; }

    /// <summary>Kenney 건물을 필지에 맞춰 놓는다. 에셋이 없으면 fallback(코드 건물)을 쓴다.</summary>
    void Kenney(Transform p, string path, Vector3 pos, float yaw, float w, float d, System.Action fallback)
    {
        var t = AssetLib.Spawn(path, p, pos, yaw + AssetLib.KenneyFrontYaw, AssetLib.Fit.Footprint, new Vector3(w, 0, d), true, true);
        if (t == null) fallback();
    }

    void AddStreetLamp(Transform parent, Vector3 worldPos)
    {
        var go = new GameObject("가로등 불빛");
        go.transform.SetParent(parent, true);
        go.transform.position = worldPos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = Kit.WarmLight;
        l.range = 13f;
        l.intensity = 3f;
        l.shadows = LightShadows.None;
        l.enabled = false;
        NightLights.Register(l);
    }

    void BuildProps(Transform streets)
    {
        // 신호등 + 공사 구간
        for (int i = 0; i <= blocksX; i++)
            for (int j = 0; j <= blocksZ; j++)
            {
                float cx = RoadCenter(i), cz = RoadCenter(j);
                if (Chance(0.25f))
                    AssetLib.Spawn("Kenney/Roads/traffic-light", streets, new Vector3(cx - roadWidth / 2 + 0.4f, 0, cz - roadWidth / 2 + 0.4f),
                        AssetLib.KenneyFrontYaw, AssetLib.Fit.HeightSlim, new Vector3(0.5f, 5f, 0), true, false, "신호등");
                if (Chance(0.12f))
                {
                    Vector3 c = new Vector3(cx + 1.5f, 0, cz + roadWidth / 2 + R(5f, 15f));
                    if (c.z < TotalZ - 8f)
                        for (int k = 0; k < 4; k++)
                            AssetLib.Spawn(k % 2 == 0 ? "Kenney/Roads/construction-cone" : "Kenney/Roads/construction-barrier", streets,
                                c + new Vector3(0, 0, k * 1.6f), 90f, AssetLib.Fit.Height, new Vector3(0, k % 2 == 0 ? 0.8f : 1.1f, 0), true, false, "공사");
                }
            }

        // 블록 앞 인도의 쓰레기 수거함 (뒤질 수 있음)
        for (int x = 0; x < blocksX; x++)
            for (int z = 0; z < blocksZ; z++)
            {
                if (map[x, z] == BlockType.Park || !Chance(0.6f)) continue;
                Vector3 pos = new Vector3(Origin(x) + R(2f, blockSize - 2f), 0.1f, Origin(z) + 0.9f);
                var dm = AssetLib.Spawn("Kenney/Roads/dumpster", streets, pos, AssetLib.KenneyFrontYaw, AssetLib.Fit.Footprint,
                    new Vector3(2.0f, 0, 1.2f), true, false, "쓰레기 수거함");
                if (dm == null) continue;
                AddLoot(dm.gameObject, "쓰레기 수거함", (water, 0.3f, 1), (kimbap, 0.25f, 1), (pipe, 0.1f, 1), (bandage, 0.2f, 1), (Fuel, 0.06f, 1));
                Bounds b = AssetLib.LocalBounds(dm);
                Kit.Box(dm, new Vector3(b.center.x, b.max.y + 0.02f, b.center.z), new Vector3(b.size.x * 0.9f, 0.05f, 0.25f), Kit.Loot, name: "주황 띠");
            }
    }

    void BuildStreets()
    {
        var streets = new GameObject("거리").transform;
        streets.SetParent(root);
        Color lane = Kit.Hex("E9ECEF");

        // 중앙 차선 (점선)
        for (int i = 0; i <= blocksX; i++)
            for (float z = 3f; z < TotalZ - 3f; z += 6f)
                if (!InHorizontalRoad(z)) Kit.Box(streets, new Vector3(RoadCenter(i), 0.01f, z), new Vector3(0.15f, 0.02f, 3f), lane, name: "차선");
        for (int j = 0; j <= blocksZ; j++)
            for (float x = 3f; x < TotalX - 3f; x += 6f)
                if (!InVerticalRoad(x)) Kit.Box(streets, new Vector3(x, 0.01f, RoadCenter(j)), new Vector3(3f, 0.02f, 0.15f), lane, name: "차선");

        // 횡단보도 (교차로 일부)
        for (int i = 0; i <= blocksX; i++)
            for (int j = 0; j <= blocksZ; j++)
            {
                if (!Chance(0.4f)) continue;
                float cx = RoadCenter(i), cz = RoadCenter(j);
                float zc = cz - roadWidth / 2 - 1.8f;
                if (zc > 2f)
                    for (float x = cx - roadWidth / 2 + 0.8f; x < cx + roadWidth / 2 - 0.4f; x += 1.1f)
                        Kit.Box(streets, new Vector3(x, 0.012f, zc), new Vector3(0.55f, 0.02f, 2.8f), lane, name: "횡단보도");
                float xc = cx + roadWidth / 2 + 1.8f;
                if (xc < TotalX - 2f)
                    for (float z = cz - roadWidth / 2 + 0.8f; z < cz + roadWidth / 2 - 0.4f; z += 1.1f)
                        Kit.Box(streets, new Vector3(xc, 0.012f, z), new Vector3(2.8f, 0.02f, 0.55f), lane, name: "횡단보도");
            }

        // 전봇대 + 전깃줄 (세로 도로 동쪽 가장자리)
        for (int i = 0; i <= blocksX; i++)
        {
            float x = RoadCenter(i) + roadWidth / 2 - 0.45f;
            if (x > TotalX - 1f) continue;
            Vector3? prev = null;
            for (float z = 8f; z < TotalZ - 4f; z += 20f + R(-3f, 3f))
            {
                if (InHorizontalRoad(z)) { prev = null; continue; }
                var pos = new Vector3(x, 0, z);
                if (AssetLib.Spawn("Kenney/Roads/electricity-pole", streets, pos, 90f, AssetLib.Fit.HeightSlim, new Vector3(0.3f, 9f, 0), true, false, "전봇대") == null)
                    BuildingKit.UtilityPole(streets, pos, 90f, Chance(0.25f));
                if (prev.HasValue) BuildingKit.Wires(streets, prev.Value, pos);
                prev = pos;
            }
        }

        // 나트륨 가로등 (가로 도로 북쪽 가장자리)
        for (int j = 0; j <= blocksZ; j++)
        {
            float z = RoadCenter(j) + roadWidth / 2 - 0.4f;
            if (z > TotalZ - 1f) continue;
            for (float x = 14f; x < TotalX - 6f; x += 28f)
            {
                if (InVerticalRoad(x)) x += roadWidth;
                var lp = new Vector3(x, 0, z);
                var lt = AssetLib.Spawn("Kenney/Roads/light-curved", streets, lp, 180f + AssetLib.KenneyFrontYaw, AssetLib.Fit.HeightSlim, new Vector3(0.6f, 6.5f, 0), true, false, "가로등");
                if (lt != null) AddStreetLamp(lt, new Vector3(lp.x, 5.9f, lp.z - 1.4f));
                else BuildingKit.StreetLight(streets, lp, 180f);
            }
        }

        BuildProps(streets);
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
