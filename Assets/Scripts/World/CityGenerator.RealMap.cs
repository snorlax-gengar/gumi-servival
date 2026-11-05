using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 실제 구미 지도 모드 (OpenStreetMap 데이터). 구미역 일대 원도심 약 2.2km × 2.2km.
/// - 도로·건물·하천·철길·공원·숲은 실제 위치 그대로. 지도에 건물이 비어 있는 골목에는 빌라를 채워 넣었다.
/// - 건물은 외곽선을 그대로 세워 만든 메시 + 코드로 그린 외벽 텍스처.
/// - 안드로이드는 플레이어 주변에만 살아 있다 (멀어지면 사라지고, 가는 곳에서 새로 나타난다).
/// - NavMesh는 LevelNavMesh가 플레이어 주변만 굽는다.
/// 지도 데이터 © OpenStreetMap contributors (ODbL)
/// </summary>
public partial class CityGenerator
{
    [Header("실제 구미 지도")]
    public bool useRealMap = true;
    public string realMapResource = "Map/gumi_core";
    public string groundTextureResource = "Map/gumi_ground";
    public int nearbyAndroidsBase = 7;      // 플레이어 주변에 유지할 수 (1일차)
    public int nearbyAndroidsPerDay = 2;
    public float despawnDistance = 175f;    // 이보다 멀어진 안드로이드는 사라진다 (NavMesh 범위 밖)

    public static bool RealMapActive { get; private set; }
    public static string MapAttribution { get; private set; }
    public static string FuelHintText = "동쪽 도로의 쓰러진 배달 오토바이를 뒤져 보자";

    RealMapData real;
    const float GroundHalf = 1152f;          // 바닥 텍스처가 덮는 범위
    const float PlayHalfX = 1118f, PlayHalfZ = 1098f;
    const float Chunk = 256f;

    // 도로 위 점 (안드로이드 생성·순찰), 도로 선분 (건물 정면 찾기)
    readonly List<Vector3> roadPts = new List<Vector3>();
    readonly Dictionary<long, List<int>> roadCells = new Dictionary<long, List<int>>();
    const float RoadCell = 40f;
    struct Seg { public Vector2 a, b; public float hw; }
    readonly Dictionary<long, List<Seg>> segCells = new Dictionary<long, List<Seg>>();
    const float SegCell = 25f;
    readonly Dictionary<long, List<List<Vector2>>> bldCells = new Dictionary<long, List<List<Vector2>>>();
    const float BldCell = 25f;
    readonly List<MapPoi> poiList = new List<MapPoi>();
    float populationTimer;

    static long Key(int i, int j) => ((long)i << 32) ^ (uint)j;
    static int Cell(float v, float size) => Mathf.FloorToInt(v / size);

    // ================= 진입 =================

    bool TryBuildRealMap()
    {
        RealMapActive = false;
        LevelNavMesh.LocalMode = false;
        NightLights.ActiveRadius = 0f;
        if (!useRealMap) return false;
        real = RealMapData.Load(realMapResource);
        if (real == null || real.buildings == null || real.buildings.Length == 0)
        {
            Debug.LogWarning("[Gumi] 실제 지도 데이터가 없어 가상 도시를 만듭니다 (Resources/" + realMapResource + ")");
            return false;
        }
        RealMapActive = true;
        MapAttribution = string.IsNullOrEmpty(real.attribution) ? "© OpenStreetMap contributors" : real.attribution;
        LevelNavMesh.LocalMode = true;
        LevelNavMesh.SetLevelBounds(new Bounds(Vector3.zero, new Vector3(PlayHalfX * 2, 30f, PlayHalfZ * 2)));
        NightLights.ActiveRadius = 75f;

        float t0 = Time.realtimeSinceStartup;
        IndexReal();
        BuildRealGround();
        BuildRealFlat();
        BuildRealBuildings();
        BuildRealTrees();
        BuildRealTufts();
        BuildDoorMarkers();
        BuildRealStreetFurniture();
        Debug.Log($"[Gumi] 실제 구미 지도 생성: 건물 {real.buildings.Length}, 도로 {real.roads.Length}, 나무 {(real.trees != null ? real.trees.Length / 3 : 0)} ({Time.realtimeSinceStartup - t0:0.0}초)");
        return true;
    }

    static List<Vector2> Pts(float[] p)
    {
        var l = new List<Vector2>(p.Length / 2);
        for (int i = 0; i + 1 < p.Length; i += 2) l.Add(new Vector2(p[i], p[i + 1]));
        return l;
    }

    /// <summary>도로 i~j 꼭짓점 구간의 반폭 (건물에 맞춰 좁힌 값 중 작은 쪽)</summary>
    static float RoadHalfW(MapRoad r, int i, int j)
    {
        if (r.wv == null || r.wv.Length == 0) return r.w * 0.5f;
        float a = i < r.wv.Length ? r.wv[i] : r.w, b = j < r.wv.Length ? r.wv[j] : r.w;
        return Mathf.Min(a, b) * 0.5f;
    }

    static bool Walkable(string t) => t != "motorway" && t != "motorway_link" && t != "path" && t != "footway" && t != "steps" && t != "track";
    static bool Major(string t) => t == "tertiary" || t == "secondary" || t == "primary" || t == "trunk" || t == "motorway" || t == "tertiary_link" || t == "secondary_link" || t == "motorway_link";

    // ================= 색인 =================

    void IndexReal()
    {
        foreach (var r in real.roads)
        {
            var p = Pts(r.p);
            bool walk = Walkable(r.t) && r.tu == 0;
            for (int i = 0; i + 1 < p.Count; i++)
            {
                Vector2 a = p[i], b = p[i + 1];
                float L = (b - a).magnitude;
                if (L < 0.01f) continue;
                if (r.w >= 3.5f)
                {
                    var s = new Seg { a = a, b = b, hw = RoadHalfW(r, i, i + 1) };
                    int i0 = Cell(Mathf.Min(a.x, b.x), SegCell), i1 = Cell(Mathf.Max(a.x, b.x), SegCell);
                    int j0 = Cell(Mathf.Min(a.y, b.y), SegCell), j1 = Cell(Mathf.Max(a.y, b.y), SegCell);
                    for (int ci = i0; ci <= i1; ci++)
                        for (int cj = j0; cj <= j1; cj++)
                        {
                            long k = Key(ci, cj);
                            if (!segCells.TryGetValue(k, out var list)) segCells[k] = list = new List<Seg>();
                            list.Add(s);
                        }
                }
                if (!walk) continue;
                for (float t = 0; t < L; t += 8f)
                {
                    Vector2 q = a + (b - a) * (t / L);
                    if (Mathf.Abs(q.x) > PlayHalfX - 4 || Mathf.Abs(q.y) > PlayHalfZ - 4) continue;
                    roadPts.Add(new Vector3(q.x, 0f, q.y));
                    long k = Key(Cell(q.x, RoadCell), Cell(q.y, RoadCell));
                    if (!roadCells.TryGetValue(k, out var list)) roadCells[k] = list = new List<int>();
                    list.Add(roadPts.Count - 1);
                }
            }
        }

        foreach (var b in real.buildings)
        {
            var p = Pts(b.p);
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var q in p) { minX = Mathf.Min(minX, q.x); maxX = Mathf.Max(maxX, q.x); minZ = Mathf.Min(minZ, q.y); maxZ = Mathf.Max(maxZ, q.y); }
            for (int ci = Cell(minX, BldCell); ci <= Cell(maxX, BldCell); ci++)
                for (int cj = Cell(minZ, BldCell); cj <= Cell(maxZ, BldCell); cj++)
                {
                    long k = Key(ci, cj);
                    if (!bldCells.TryGetValue(k, out var list)) bldCells[k] = list = new List<List<Vector2>>();
                    list.Add(p);
                }
        }
        if (real.pois != null) poiList.AddRange(real.pois);
    }

    bool InsideBuilding(Vector2 q, float margin = 0f)
    {
        if (!bldCells.TryGetValue(Key(Cell(q.x, BldCell), Cell(q.y, BldCell)), out var list)) return false;
        foreach (var poly in list)
        {
            if (Poly.Contains(poly, q)) return true;
            if (margin > 0)
                for (int i = 0; i < poly.Count; i++)
                    if (SegDist(q, poly[i], poly[(i + 1) % poly.Count]) < margin) return true;
        }
        return false;
    }

    static float SegDist(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 d = b - a;
        float L = d.sqrMagnitude;
        float t = L < 1e-6f ? 0 : Mathf.Clamp01(Vector2.Dot(p - a, d) / L);
        return (p - (a + d * t)).magnitude;
    }

    /// <summary>가장 가까운 도로 가장자리까지 거리 (없으면 큰 값)</summary>
    float RoadEdgeDist(Vector2 q)
    {
        float best = 999f;
        if (!segCells.TryGetValue(Key(Cell(q.x, SegCell), Cell(q.y, SegCell)), out var list)) return best;
        foreach (var s in list) best = Mathf.Min(best, SegDist(q, s.a, s.b) - s.hw);
        return best;
    }

    // ================= 바닥 =================

    readonly Dictionary<long, MeshBuilder> flatChunks = new Dictionary<long, MeshBuilder>();
    Material[] flatMats;
    const int F_ASPH = 0, F_ASPH2 = 1, F_PAVER = 2, F_YELLOW = 3, F_WHITE = 4, F_WATER = 5, F_PARK = 6, F_GRASS = 7, F_FARM = 8,
              F_PITCH = 9, F_PARKING = 10, F_DIRT = 11, F_PLAZA = 12, F_GRAVEL = 13, F_STEEL = 14, F_WOOD = 15, F_PLATFORM = 16,
              F_SIDEWALK = 17, F_CURB = 18, F_COUNT = 19;

    MeshBuilder FlatAt(Vector2 p, out Vector3 origin)
    {
        int i = Cell(p.x, Chunk), j = Cell(p.y, Chunk);
        origin = new Vector3(i * Chunk, 0, j * Chunk);
        long k = Key(i, j);
        if (!flatChunks.TryGetValue(k, out var mb)) flatChunks[k] = mb = new MeshBuilder(F_COUNT);
        return mb;
    }

    static Material Shiny(Color c, float smooth)
    {
        var m = new Material(Kit.Mat(c)) { name = "Shiny_" + ColorUtility.ToHtmlStringRGB(c) };
        m.SetFloat("_Smoothness", smooth);
        return m;
    }

    void BuildRealGround()
    {
        var tex = Resources.Load<Texture2D>(groundTextureResource);
        if (tex != null) tex.wrapMode = TextureWrapMode.Clamp;
        Material gm = tex != null ? Facade.Textured("Ground_Gumi", tex, Color.white, 0.05f) : Kit.Mat(Kit.Hex("A8A398"));

        var mb = new MeshBuilder(1);
        float H = GroundHalf;
        mb.Quad(0, new Vector3(-H, 0, -H), new Vector3(-H, 0, H), new Vector3(H, 0, H), new Vector3(H, 0, -H), Vector3.up,
            new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));
        var g = mb.Build("Ground", root, Vector3.zero, new[] { gm }, false);
        var bc = g.AddComponent<BoxCollider>();
        bc.center = new Vector3(0, -0.5f, 0);
        bc.size = new Vector3(H * 2, 1f, H * 2);

        // 지도 끝 보이지 않는 벽
        float h = 6f;
        foreach (var w in new[]
        {
            new Bounds(new Vector3(0, h / 2, -PlayHalfZ - 0.5f), new Vector3(PlayHalfX * 2, h, 1)),
            new Bounds(new Vector3(0, h / 2, PlayHalfZ + 0.5f), new Vector3(PlayHalfX * 2, h, 1)),
            new Bounds(new Vector3(-PlayHalfX - 0.5f, h / 2, 0), new Vector3(1, h, PlayHalfZ * 2)),
            new Bounds(new Vector3(PlayHalfX + 0.5f, h / 2, 0), new Vector3(1, h, PlayHalfZ * 2)),
        })
        {
            var wall = new GameObject("지도 끝");
            wall.transform.SetParent(root, false);
            wall.transform.position = w.center;
            wall.AddComponent<BoxCollider>().size = w.size;
        }
    }

    void BuildRealFlat()
    {
        flatMats = new Material[F_COUNT];
        flatMats[F_ASPH] = SurfaceTextures.Asphalt(Kit.Hex("3E4146"), "Asphalt_Alley");
        flatMats[F_ASPH2] = SurfaceTextures.Asphalt(Kit.Hex("33363B"), "Asphalt_Main");
        flatMats[F_PAVER] = SurfaceTextures.Pavers(Kit.Hex("A39A8C"), "Pavers_Path");
        flatMats[F_SIDEWALK] = SurfaceTextures.Pavers(Kit.Hex("B3A99A"), "Pavers_Sidewalk");
        flatMats[F_CURB] = Kit.Mat(Kit.Hex("C9C4BA"));
        flatMats[F_YELLOW] = Kit.Mat(Kit.Hex("E8B830"));
        flatMats[F_WHITE] = Kit.Mat(Kit.Hex("E6E6E0"));
        flatMats[F_WATER] = Shiny(Kit.Hex("4C7894"), 0.88f);
        flatMats[F_PARK] = SurfaceTextures.Grass(Kit.Hex("5E7E42"), Kit.Hex("7A7A4E"), "Grass_Park");
        flatMats[F_GRASS] = SurfaceTextures.Grass(Kit.Hex("6E8A4E"), Kit.Hex("8A8456"), "Grass_Wild");
        flatMats[F_FARM] = SurfaceTextures.Grass(Kit.Hex("8A8456"), Kit.Hex("6E5E40"), "Farm");
        flatMats[F_PITCH] = SurfaceTextures.Grass(Kit.Hex("557E44"), Kit.Hex("62864C"), "Pitch");
        flatMats[F_PARKING] = SurfaceTextures.Asphalt(Kit.Hex("4E5156"), "Asphalt_Parking");
        flatMats[F_DIRT] = SurfaceTextures.Grass(Kit.Hex("B09872"), Kit.Hex("9C8662"), "Dirt");
        flatMats[F_PLAZA] = SurfaceTextures.Pavers(Kit.Hex("B5AEA2"), "Pavers_Plaza");
        flatMats[F_GRAVEL] = Kit.Mat(Kit.Hex("7A7168"));
        flatMats[F_STEEL] = Shiny(Kit.Hex("8A8F96"), 0.7f);
        flatMats[F_WOOD] = SurfaceTextures.Grass(Kit.Hex("3E5030"), Kit.Hex("5A5438"), "Forest_Floor");
        flatMats[F_PLATFORM] = Kit.Mat(Kit.Hex("C9C2B6"));

        // --- 면 ---
        if (real.areas != null)
            foreach (var a in real.areas)
            {
                var p = Pts(a.p);
                if (p.Count < 3) continue;
                int m; float y;
                switch (a.k)
                {
                    case "water": m = F_WATER; y = 0.016f; break;
                    case "wood": m = F_WOOD; y = 0.008f; break;
                    case "park": m = F_PARK; y = 0.010f; break;
                    case "grass": m = F_GRASS; y = 0.009f; break;
                    case "farm": m = F_FARM; y = 0.008f; break;
                    case "pitch": m = F_PITCH; y = 0.013f; break;
                    case "parking": m = F_PARKING; y = 0.014f; break;
                    case "schoolyard": m = F_DIRT; y = 0.011f; break;
                    case "plaza": m = F_PLAZA; y = 0.012f; break;
                    case "railyard": m = F_GRAVEL; y = 0.012f; break;
                    case "platform": m = F_PLATFORM; y = 0.04f; break;
                    default: continue;
                }
                var mb = FlatAt(Poly.Centroid(p), out var o);
                mb.Polygon(m, p, y, o, m == F_PLAZA || m == F_PLATFORM ? 0.3f : m == F_PARKING ? 0.12f : 0.1f);
            }

        // --- 하천·철길 ---
        if (real.lines != null)
            foreach (var l in real.lines)
            {
                var p = Pts(l.p);
                if (p.Count < 2) continue;
                var mb = FlatAt(p[0], out var o);
                if (l.k == "water")
                {
                    mb.Strip(F_WATER, p, l.w * 0.5f, 0.017f, o);
                    mb.Strip(F_GRAVEL, p, 0.6f, 0.012f, o, l.w * 0.5f + 0.5f);
                    mb.Strip(F_GRAVEL, p, 0.6f, 0.012f, o, -l.w * 0.5f - 0.5f);
                }
                else
                {
                    mb.Strip(F_GRAVEL, p, 2.0f, 0.018f, o);
                    mb.Dashes(F_PAVER, p, 1.3f, 0.024f, o, 0.25f, 0.45f);   // 침목
                    mb.Strip(F_STEEL, p, 0.05f, 0.05f, o, 0.72f);
                    mb.Strip(F_STEEL, p, 0.05f, 0.05f, o, -0.72f);
                }
            }

        // --- 도로 ---
        foreach (var r in real.roads)
        {
            if (r.tu == 1) continue;
            var p = Pts(r.p);
            if (p.Count < 2) continue;
            float hw = r.w * 0.5f, y;
            int m;
            switch (r.t)
            {
                case "footway": case "path": case "steps": case "pedestrian": case "track": m = F_PAVER; y = 0.020f; break;
                case "service": m = F_ASPH; y = 0.025f; break;
                case "motorway": case "motorway_link": m = F_ASPH2; y = 0.045f; break;
                default: m = Major(r.t) ? F_ASPH2 : F_ASPH; y = Major(r.t) ? 0.035f : 0.03f; break;
            }
            if (r.t == "path") m = F_DIRT;
            var mb = FlatAt(p[0], out var o);

            // 꼭짓점별 반폭 (건물에 닿는 구간은 좁다)
            var hws = new List<float>(p.Count);
            float minHw = hw;
            for (int i = 0; i < p.Count; i++)
            {
                float v = r.wv != null && i < r.wv.Length ? r.wv[i] * 0.5f : hw;
                hws.Add(v);
                minHw = Mathf.Min(minHw, v);
            }
            mb.Strip(m, p, hw, y, o, 0f, hws);
            mb.Disc(m, p[0], hws[0], y, o);
            mb.Disc(m, p[p.Count - 1], hws[hws.Count - 1], y, o);

            // 인도 + 연석: 도로보다 살짝 아래 높이로 깔아서 교차로에서는 도로가 덮는다
            bool walkSide = (Major(r.t) && r.t != "motorway" && r.t != "motorway_link") || ((r.t == "residential" || r.t == "unclassified") && r.w >= 6.5f);
            if (walkSide && r.b == 0)
            {
                float sw = Major(r.t) ? 3.2f : 1.8f;
                var offL = new List<float>(p.Count); var offR = new List<float>(p.Count);
                var curbL = new List<float>(p.Count); var curbR = new List<float>(p.Count);
                foreach (var v in hws)
                {
                    offL.Add(v + sw * 0.5f); offR.Add(-v - sw * 0.5f);
                    curbL.Add(v + 0.1f); curbR.Add(-v - 0.1f);
                }
                mb.Strip(F_SIDEWALK, p, sw * 0.5f, 0.021f, o, 0f, null, offL);
                mb.Strip(F_SIDEWALK, p, sw * 0.5f, 0.021f, o, 0f, null, offR);
                mb.Strip(F_CURB, p, 0.11f, 0.023f, o, 0f, null, curbL);
                mb.Strip(F_CURB, p, 0.11f, 0.023f, o, 0f, null, curbR);
            }

            float ly = y + 0.004f;
            var edgeL = new List<float>(p.Count);
            var edgeR = new List<float>(p.Count);
            foreach (var v in hws) { edgeL.Add(v - 0.4f); edgeR.Add(-v + 0.4f); }
            if (r.t == "motorway")
            {
                mb.Strip(F_WHITE, p, 0.08f, ly, o, 0f, null, edgeL);
                mb.Strip(F_WHITE, p, 0.08f, ly, o, 0f, null, edgeR);
                mb.Dashes(F_WHITE, p, 0.07f, ly, o, 4f, 6f, minHw * 0.5f);
                mb.Dashes(F_WHITE, p, 0.07f, ly, o, 4f, 6f, -minHw * 0.5f);
            }
            else if (Major(r.t) && r.w >= 7f)
            {
                mb.Strip(F_YELLOW, p, 0.07f, ly, o, 0.16f);   // 중앙선 (황색 실선 두 줄)
                mb.Strip(F_YELLOW, p, 0.07f, ly, o, -0.16f);
                mb.Strip(F_WHITE, p, 0.08f, ly, o, 0f, null, edgeL);
                mb.Strip(F_WHITE, p, 0.08f, ly, o, 0f, null, edgeR);
                if (minHw * 2f >= 11f)
                {
                    mb.Dashes(F_WHITE, p, 0.06f, ly, o, 3f, 5f, minHw * 0.5f);
                    mb.Dashes(F_WHITE, p, 0.06f, ly, o, 3f, 5f, -minHw * 0.5f);
                }
            }
            else if ((r.t == "residential" || r.t == "unclassified") && minHw * 2f >= 5.5f)
                mb.Dashes(F_WHITE, p, 0.06f, ly, o, 3f, 4f);
        }

        var parent = new GameObject("구미_바닥").transform;
        parent.SetParent(root, false);
        foreach (var kv in flatChunks)
        {
            int ci = (int)(kv.Key >> 32), cj = (int)(uint)kv.Key;
            var go = kv.Value.Build($"바닥_{ci}_{cj}", parent, new Vector3(ci * Chunk, 0, cj * Chunk), flatMats, false, false);
            if (go != null) go.GetComponent<MeshRenderer>().receiveShadows = true;
        }
        flatChunks.Clear();
    }

    // ================= 건물 =================

    static Facade.Style StyleOf(MapBuilding b)
    {
        switch (b.k)
        {
            case "apt": return Facade.Style.Apt;
            case "house": return Facade.Style.House;
            case "school": return Facade.Style.School;
            case "public": case "church": return Facade.Style.Public;
            case "shop": return Facade.Style.Shop;
            case "gen": return b.lv >= 6 ? Facade.Style.Office : Facade.Style.Villa;
            default: return Facade.Style.Villa;
        }
    }

    readonly Dictionary<long, Transform> bldParents = new Dictionary<long, Transform>();
    Transform bldRoot, lootRoot;

    Transform BuildingParent(Vector2 c)
    {
        int i = Cell(c.x, Chunk), j = Cell(c.y, Chunk);
        long k = Key(i, j);
        if (!bldParents.TryGetValue(k, out var t))
        {
            t = new GameObject($"건물_{i}_{j}").transform;
            t.SetParent(bldRoot, false);
            bldParents[k] = t;
        }
        return t;
    }

    void BuildRealBuildings()
    {
        bldRoot = new GameObject("구미_건물").transform;
        bldRoot.SetParent(root, false);
        lootRoot = new GameObject("뒤질 곳").transform;
        lootRoot.SetParent(root, false);

        // 이름 있는 장소는 그 장소가 들어 있는 건물을 빨리 찾도록 격자에 넣어 둔다
        var poiCells = new Dictionary<long, List<MapPoi>>();
        foreach (var p in poiList)
        {
            long k = Key(Cell(p.x, BldCell), Cell(p.z, BldCell));
            if (!poiCells.TryGetValue(k, out var l)) poiCells[k] = l = new List<MapPoi>();
            l.Add(p);
        }

        int idx = 0;
        foreach (var b in real.buildings)
        {
            idx++;
            if (b.safe == 1) continue;
            var pts = Pts(b.p);
            if (pts.Count < 3) continue;
            if (Poly.SignedArea(pts) < 0) pts.Reverse();
            if (b.k == "station") { BuildStation(pts, b); continue; }
            Vector2 c = Poly.Centroid(pts);
            int hash = Mathf.Abs((int)(pts[0].x * 73.13f) ^ (int)(pts[0].y * 191.7f) ^ idx * 7919);

            var style = StyleOf(b);
            int lv = Mathf.Max(1, b.lv);
            bool roofOnly = b.k == "roof";
            var ground = new List<MapShop>();
            var upper = new List<MapShop>();
            if (b.shops != null)
                foreach (var sp in b.shops)
                {
                    if (sp.f <= 1 && sp.f >= 0) ground.Add(sp);
                    else if (sp.f >= 2 && sp.f <= lv) upper.Add(sp);
                }
            bool store = ground.Count > 0
                         || (b.shops == null || b.shops.Length == 0) && (style == Facade.Style.Shop
                             || (style == Facade.Style.Villa && b.k == "gen" && lv >= 2 && lv <= 5 && hash % 10 < 2)
                             || (style == Facade.Style.Office && hash % 10 < 3));
            if (ground.Count > 0 && style == Facade.Style.Villa) style = Facade.Style.Shop;
            float h = roofOnly ? 4.6f : store ? Facade.ShopFloor + (lv - 1) * Facade.Floor : lv * Facade.Floor;
            int wv = hash % Facade.Variants(style), sv = (hash / 7) % Facade.SignVariants, rv = (hash / 13) % 4;

            var origin = new Vector3(c.x, 0, c.y);
            var mb = new MeshBuilder(7);   // 0 1층 상가 외벽, 1 외벽, 2 지붕, 3 옥상 난간, 4 옥상 물건, 5 실내 벽·천장, 6 실내 바닥

            // 도로를 보는 벽 = 정면 (간판·현관을 붙인다)
            int front = -1;
            float frontScore = float.MaxValue;
            for (int i = 0; i < pts.Count; i++)
            {
                Vector2 a = pts[i], e = pts[(i + 1) % pts.Count];
                float L = (e - a).magnitude;
                if (L <= 3f) continue;
                Vector2 nn = new Vector2(e.y - a.y, -(e.x - a.x)) / L;
                float score = RoadEdgeDist((a + e) * 0.5f + nn * 3f) - L * 0.05f;
                if (score < frontScore) { frontScore = score; front = i; }
            }

            // 들어갈 수 있는 가게: 1층이 비어 있고 정면에 문이 난다 (좀보이드처럼 안을 뒤질 수 있다)
            float area = Poly.SignedArea(pts);
            List<Vector2> inner = null;
            bool enter = store && !roofOnly && front >= 0 && pts.Count <= 14 && area >= 35f && area <= 700f
                         && (pts[(front + 1) % pts.Count] - pts[front]).magnitude >= 3.4f;
            if (enter)
            {
                inner = Poly.Inset(pts, 0.25f);
                if (Poly.SignedArea(inner) < area * 0.55f) enter = false;
            }
            const float DoorW = 1.8f, DoorH = 2.4f;
            float gh = Facade.ShopFloor;

            float acc = 0f;
            for (int i = 0; i < pts.Count; i++)
            {
                Vector2 a = pts[i], e = pts[(i + 1) % pts.Count];
                float L = (e - a).magnitude;
                if (L < 0.05f) continue;
                Vector3 n = new Vector3(e.y - a.y, 0, -(e.x - a.x)) / L;
                Vector3 A = new Vector3(a.x - c.x, 0, a.y - c.y), E = new Vector3(e.x - c.x, 0, e.y - c.y);
                float u0 = acc / Facade.BayWidth, u1 = (acc + L) / Facade.BayWidth;
                float acc0 = acc;
                acc += L;
                if (roofOnly) continue;

                if (enter)
                {
                    Vector2 ai = inner[i], ei = inner[(i + 1) % pts.Count];
                    Vector3 Ai = new Vector3(ai.x - c.x, 0, ai.y - c.y), Ei = new Vector3(ei.x - c.x, 0, ei.y - c.y);
                    Vector3 up = Vector3.up * gh;
                    if (i == front)
                    {
                        float t0 = (L - DoorW) * 0.5f, t1 = t0 + DoorW;
                        Vector3 D0 = Vector3.Lerp(A, E, t0 / L), D1 = Vector3.Lerp(A, E, t1 / L);
                        Vector3 I0 = Vector3.Lerp(Ai, Ei, t0 / L), I1 = Vector3.Lerp(Ai, Ei, t1 / L);
                        float ud0 = (acc0 + t0) / Facade.BayWidth, ud1 = (acc0 + t1) / Facade.BayWidth, vd = DoorH / gh;
                        Vector3 dh = Vector3.up * DoorH;
                        doorMarks.Add(new DoorMark { a = origin + D0, b = origin + D1, n = n });
                        mb.Quad(0, A, A + up, D0 + up, D0, n, new Vector2(u0, 0), new Vector2(u0, 1), new Vector2(ud0, 1), new Vector2(ud0, 0));
                        mb.Quad(0, D1, D1 + up, E + up, E, n, new Vector2(ud1, 0), new Vector2(ud1, 1), new Vector2(u1, 1), new Vector2(u1, 0));
                        mb.Quad(0, D0 + dh, D0 + up, D1 + up, D1 + dh, n, new Vector2(ud0, vd), new Vector2(ud0, 1), new Vector2(ud1, 1), new Vector2(ud1, vd));
                        mb.Quad(5, I0, I0 + up, Ai + up, Ai, -n, Vector2.zero, Vector2.up, Vector2.one, Vector2.right);
                        mb.Quad(5, Ei, Ei + up, I1 + up, I1, -n, Vector2.zero, Vector2.up, Vector2.one, Vector2.right);
                        mb.Quad(5, I1 + dh, I1 + up, I0 + up, I0 + dh, -n, Vector2.zero, Vector2.up, Vector2.one, Vector2.right);
                        Vector3 dir = (E - A) / L;
                        mb.Quad(5, D0, D0 + dh, I0 + dh, I0, dir, Vector2.zero, Vector2.up, Vector2.one, Vector2.right);     // 문틀
                        mb.Quad(5, I1, I1 + dh, D1 + dh, D1, -dir, Vector2.zero, Vector2.up, Vector2.one, Vector2.right);
                        mb.Quad(5, D0 + dh, I0 + dh, I1 + dh, D1 + dh, Vector3.down, Vector2.zero, Vector2.up, Vector2.one, Vector2.right);
                    }
                    else
                    {
                        mb.Quad(0, A, A + up, E + up, E, n, new Vector2(u0, 0), new Vector2(u0, 1), new Vector2(u1, 1), new Vector2(u1, 0));
                        mb.Quad(5, Ei, Ei + up, Ai + up, Ai, -n, Vector2.zero, Vector2.up, Vector2.one, Vector2.right);
                    }
                    if (h > gh + 0.01f)
                    {
                        float v1 = (h - gh) / Facade.Floor;
                        mb.Quad(1, A + up, A + Vector3.up * h, E + Vector3.up * h, E + up, n,
                            new Vector2(u0, 0), new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, 0));
                    }
                }
                else if (store)
                {
                    float s = Facade.ShopFloor;
                    mb.Quad(0, A, A + Vector3.up * s, E + Vector3.up * s, E, n,
                        new Vector2(u0, 0), new Vector2(u0, 1), new Vector2(u1, 1), new Vector2(u1, 0));
                    if (h > s + 0.01f)
                    {
                        float v1 = (h - s) / Facade.Floor;
                        mb.Quad(1, A + Vector3.up * s, A + Vector3.up * h, E + Vector3.up * h, E + Vector3.up * s, n,
                            new Vector2(u0, 0), new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, 0));
                    }
                }
                else
                {
                    float v1 = h / Facade.Floor;
                    mb.Quad(1, A, A + Vector3.up * h, E + Vector3.up * h, E, n,
                        new Vector2(u0, 0), new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, 0));
                }
            }
            if (enter)
            {
                mb.Polygon(5, inner, gh - 0.02f, origin, 0.25f, true);   // 천장
                mb.Polygon(6, inner, 0.025f, origin, 0.5f);              // 실내 바닥
            }
            mb.Polygon(2, pts, h, origin);
            if (!roofOnly && style != Facade.Style.House && pts.Count <= 48) RoofDetails(mb, pts, c, h, style, lv, hash);
            if (!roofOnly) TuftsAround(pts, hash);
            if (roofOnly)
            {
                // 주유소·주차장 지붕: 아래쪽 면도 만든다
                int start = mb.Count;
                for (int i = 0; i < pts.Count; i++) mb.V(new Vector3(pts[i].x - c.x, h - 0.3f, pts[i].y - c.y), Vector3.down, Vector2.zero);
                var tris = Poly.Triangulate(pts);
                for (int i = 0; i + 2 < tris.Count; i += 3) mb.Tri(2, start + tris[i], start + tris[i + 1], start + tris[i + 2], Vector3.down);
            }

            var mats = new[] { Facade.Storefront(sv), Facade.Wall(style, wv), Facade.Roof(style, rv),
                               Kit.Mat(Facade.WallTint(style, wv) * 0.93f), Kit.Mat(Kit.Hex("BFC4C9")),
                               Kit.Mat(Kit.Hex("D6D0C4")), SurfaceTextures.Pavers(Kit.Hex("C8C2B6"), "Floor_Tile") };
            string nm = !string.IsNullOrEmpty(b.n) ? b.n : (b.f == 1 ? "빌라" : b.k);
            var go = mb.Build(nm, BuildingParent(c), origin, mats, !roofOnly);
            if (go == null) continue;
            go.AddComponent<OccluderGroup>();
            if (front < 0 || roofOnly) continue;

            // ---------- 정면 장식 / 간판 / 뒤질 곳 ----------
            Vector2 fa = pts[front], fe = pts[(front + 1) % pts.Count];
            float fl = (fe - fa).magnitude;
            Vector2 fd = (fe - fa) / fl;
            Vector3 fn = new Vector3(fd.y, 0, -fd.x);
            Vector3 fmid = new Vector3((fa.x + fe.x) * 0.5f, 0, (fa.y + fe.y) * 0.5f);
            Vector3 localMid = fmid - origin;
            var t = go.transform;

            MapPoi poi = null;
            if (poiList.Count > 0)
            {
                long k = Key(Cell(c.x, BldCell), Cell(c.y, BldCell));
                if (poiCells.TryGetValue(k, out var nearList))
                    foreach (var p in nearList)
                        if (!string.IsNullOrEmpty(p.n) && Poly.Contains(pts, new Vector2(p.x, p.z))) { poi = p; break; }
            }
            string realName = poi != null ? poi.n : (!string.IsNullOrEmpty(b.n) && b.k != "apt" ? b.n : null);

            if (style == Facade.Style.Apt && lv >= 6)
            {
                string num = null;
                if (!string.IsNullOrEmpty(b.n))
                {
                    var mm = Regex.Match(b.n, @"(\d+)\s*동");
                    if (mm.Success) num = mm.Groups[1].Value + "동";
                }
                if (num == null) num = (101 + hash % 12) + "동";
                Kit.Label(t, num, localMid + fn * 0.06f + Vector3.up * (h - 2.4f), Kit.Facing(fn), 2.2f, Kit.Hex("5E6E80"));
            }
            else if (ground.Count > 0 || upper.Count > 0)
            {
                ShopSigns(t, ground, upper, localMid, fn, fd, fl, h, hash);
            }
            else if (store && fl > 4f)
            {
                // 상가 정보에 없는 빈 점포
                if (hash % 10 < 3)
                    SignPanel(t, "임대 문의", localMid + Vector3.up * 2.95f, fn, new Vector3(Mathf.Min(fl - 1f, 4f), 0.7f, 0.08f), Kit.Hex("E9ECEF"), Kit.Hex("C8402E"));
            }
            else if (realName != null && fl > 4f)
            {
                if (realName.Length > 12) realName = realName.Substring(0, 12);
                Kit.Label(t, realName, localMid + fn * 0.06f + Vector3.up * Mathf.Min(h - 0.8f, 5f), Kit.Facing(fn), 0.8f, Kit.Hex("2E3A48"));
            }

            if (enter) Interior(nm, c, inner, front, fn, ground, hash);
            else if (ground.Count > 0) PlaceShopLoot(ground, fmid, fn, fd, fl, hash);
            else PlaceBuildingLoot(b, style, store, poi, fmid, fn, fd, fl, hash);
        }
    }

    // ================= 구미역 (선상역사) =================

    /// <summary>
    /// 구미역: 철길 위에 떠 있는 선상역사. 기둥 위 유리 대합실 + 코레일 파란 띠 + 큰 '구미역' 글씨 (밤에 빛남).
    /// 아래로는 철길과 승강장이 지나가고 사람도 기둥 사이로 다닐 수 있다.
    /// </summary>
    void BuildStation(List<Vector2> pts, MapBuilding b)
    {
        Vector2 c = Poly.Centroid(pts);
        var origin = new Vector3(c.x, 0, c.y);
        const float yb = 7.5f, yt = 17f;
        var mb = new MeshBuilder(5);   // 0 유리벽, 1 지붕, 2 아랫면, 3 기둥, 4 파란 띠
        for (int i = 0; i < pts.Count; i++)
        {
            Vector2 a = pts[i], e = pts[(i + 1) % pts.Count];
            float L = (e - a).magnitude;
            if (L < 0.05f) continue;
            Vector3 n = new Vector3(e.y - a.y, 0, -(e.x - a.x)) / L;
            Vector3 A = new Vector3(a.x - c.x, 0, a.y - c.y), E = new Vector3(e.x - c.x, 0, e.y - c.y);
            float u1 = L / Facade.BayWidth, v1 = (yt - yb - 1.6f) / Facade.Floor;
            mb.Quad(0, A + Vector3.up * yb, A + Vector3.up * (yt - 1.6f), E + Vector3.up * (yt - 1.6f), E + Vector3.up * yb, n,
                new Vector2(0, 0), new Vector2(0, v1), new Vector2(u1, v1), new Vector2(u1, 0));
            mb.Quad(4, A + Vector3.up * (yt - 1.6f), A + Vector3.up * yt, E + Vector3.up * yt, E + Vector3.up * (yt - 1.6f), n,
                Vector2.zero, Vector2.up, Vector2.one, Vector2.right);
        }
        mb.Polygon(1, pts, yt, origin);
        mb.Polygon(2, pts, yb, origin, 0.1f, true);
        // 기둥: 14m 간격
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var q in pts) { minX = Mathf.Min(minX, q.x); maxX = Mathf.Max(maxX, q.x); minZ = Mathf.Min(minZ, q.y); maxZ = Mathf.Max(maxZ, q.y); }
        for (float x = minX + 6f; x < maxX; x += 14f)
            for (float z = minZ + 6f; z < maxZ; z += 14f)
            {
                var q = new Vector2(x, z);
                if (!InsideShrunk(pts, q, 2f)) continue;
                mb.Box(3, new Vector3(q.x - c.x, yb * 0.5f, q.y - c.y), new Vector3(1.3f, yb, 1.3f), 0f);
            }
        var mats = new[] { Facade.Wall(Facade.Style.Office, 3), Kit.Mat(Kit.Hex("8A8F96")), Kit.Mat(Kit.Hex("C9CED4")),
                           Kit.Mat(Kit.Hex("D8DCE0")), Kit.Mat(Kit.Hex("0B5BAA")) };
        var go = mb.Build("구미역", BuildingParent(c), origin, mats, true);
        if (go == null) return;
        go.AddComponent<OccluderGroup>();

        // 큰 글씨: 가장 긴 두 벽 (역 앞 광장 쪽과 반대쪽)
        var order = new List<int>();
        for (int i = 0; i < pts.Count; i++) order.Add(i);
        order.Sort((i, j) => (pts[(j + 1) % pts.Count] - pts[j]).sqrMagnitude.CompareTo((pts[(i + 1) % pts.Count] - pts[i]).sqrMagnitude));
        for (int k = 0; k < Mathf.Min(2, order.Count); k++)
        {
            int i = order[k];
            Vector2 a = pts[i], e = pts[(i + 1) % pts.Count];
            float L = (e - a).magnitude;
            if (L < 20f) continue;
            Vector3 n = new Vector3(e.y - a.y, 0, -(e.x - a.x)) / L;
            Vector3 mid = new Vector3((a.x + e.x) * 0.5f - c.x, 0, (a.y + e.y) * 0.5f - c.y);
            Kit.Label(go.transform, "구미역", mid + n * 0.12f + Vector3.up * (yt - 4.3f), Kit.Facing(n), 3.6f, Color.white);
            Kit.Label(go.transform, "GUMI STATION · 2052", mid + n * 0.12f + Vector3.up * (yt - 6.7f), Kit.Facing(n), 1.0f, Kit.Hex("CFE3F5"));
            Kit.Box(go.transform, mid + n * 0.06f + Vector3.up * (yt - 4.3f), new Vector3(Mathf.Min(L * 0.5f, 22f), 4.4f, 0.08f),
                Kit.Hex("0B3E73"), rot: Quaternion.LookRotation(n), emission: 0.6f, name: "역 간판");
        }
        // 밤에 역 앞이 밝게
        var lg = new GameObject("역 조명");
        lg.transform.SetParent(go.transform, false);
        lg.transform.localPosition = new Vector3(0, yb - 0.5f, 0);
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point; l.range = 45f; l.intensity = 4f; l.color = Kit.Hex("DDE8FF"); l.shadows = LightShadows.None; l.enabled = false;
        NightLights.Register(l);
        Debug.Log($"[Gumi] 구미역 선상역사 ({b.n})");
    }

    // ================= 가게 안 =================

    Transform interiorRoot;

    struct DoorMark { public Vector3 a, b, n; }
    readonly List<DoorMark> doorMarks = new List<DoorMark>();
    /// <summary>미니맵 카메라만 보는 레이어 (출입구 아이콘)</summary>
    public const int MapOnlyLayer = 30;

    /// <summary>
    /// 들어갈 수 있는 건물 표시: 문 둘레에 민트색으로 빛나는 문틀, 문 앞 발판,
    /// 그리고 미니맵·전체 지도에만 보이는 출입구 아이콘. (주황 = 뒤질 것, 빨강 = 안드로이드, 민트 = 들어갈 수 있는 곳)
    /// </summary>
    void BuildDoorMarkers()
    {
        if (doorMarks.Count == 0) return;
        var glowChunks = new Dictionary<long, MeshBuilder>();
        var mapChunks = new Dictionary<long, MeshBuilder>();
        foreach (var d in doorMarks)
        {
            Vector3 mid = (d.a + d.b) * 0.5f;
            int ci = Cell(mid.x, Chunk), cj = Cell(mid.z, Chunk);
            long k = Key(ci, cj);
            Vector3 o = new Vector3(ci * Chunk, 0, cj * Chunk);
            if (!glowChunks.TryGetValue(k, out var g)) glowChunks[k] = g = new MeshBuilder(2);
            if (!mapChunks.TryGetValue(k, out var mp)) mapChunks[k] = mp = new MeshBuilder(1);

            Vector3 dir = (d.b - d.a); float w = dir.magnitude; dir /= Mathf.Max(0.01f, w);
            float yaw = Mathf.Atan2(-dir.z, dir.x) * Mathf.Rad2Deg;
            Vector3 out1 = d.n * 0.05f;
            const float H = 2.4f, t = 0.09f;
            g.Box(0, d.a - o + out1 + Vector3.up * H * 0.5f, new Vector3(t, H, t), yaw);          // 왼쪽 문틀
            g.Box(0, d.b - o + out1 + Vector3.up * H * 0.5f, new Vector3(t, H, t), yaw);          // 오른쪽
            g.Box(0, mid - o + out1 + Vector3.up * (H + t * 0.5f), new Vector3(w + t * 2f, t, t), yaw); // 위
            g.Box(1, mid - o + d.n * 0.55f + Vector3.up * 0.04f, new Vector3(w, 0.03f, 0.9f), yaw);   // 발판

            // 미니맵 아이콘: 문 앞 공중에 민트색 문 모양 (화살표 없이 굵은 ㄷ자)
            Vector3 c = mid - o + d.n * 2.2f + Vector3.up * 45f;
            mp.Box(0, c, new Vector3(3.2f, 0.2f, 3.2f), yaw);
        }
        var parent = new GameObject("구미_출입구").transform;
        parent.SetParent(root, false);
        var glowMats = new[] { Kit.Mat(Kit.Hex("6FE0B8"), 2.2f), Kit.Mat(Kit.Hex("2F5A4A")) };
        var mapMats = new[] { Kit.Mat(Kit.Hex("6FE0B8"), 1.5f) };
        foreach (var kv in glowChunks)
        {
            int ci = (int)(kv.Key >> 32), cj = (int)(uint)kv.Key;
            kv.Value.Build($"출입구_{ci}_{cj}", parent, new Vector3(ci * Chunk, 0, cj * Chunk), glowMats, false, false);
        }
        foreach (var kv in mapChunks)
        {
            int ci = (int)(kv.Key >> 32), cj = (int)(uint)kv.Key;
            var go = kv.Value.Build($"출입구지도_{ci}_{cj}", parent, new Vector3(ci * Chunk, 0, cj * Chunk), mapMats, false, false);
            if (go != null) go.layer = MapOnlyLayer;
        }
        Debug.Log($"[Gumi] 들어갈 수 있는 가게 {doorMarks.Count}곳");
    }

    /// <summary>
    /// 들어갈 수 있는 가게 안: 벽을 따라 선반, 가운데는 업종에 따라 진열대 줄 또는 식탁, 안쪽에 뒤질 곳.
    /// 가구는 건물(반투명 처리 대상)과 따로 두어 건물이 투명해져도 또렷하게 보인다.
    /// </summary>
    void Interior(string nm, Vector2 c, List<Vector2> inner, int front, Vector3 fn, List<MapShop> ground, int hash)
    {
        if (interiorRoot == null)
        {
            interiorRoot = new GameObject("구미_가게안").transform;
            interiorRoot.SetParent(root, false);
        }
        var rnd = new System.Random(hash ^ 0x1e7);
        var origin = new Vector3(c.x, 0, c.y);
        var mb = new MeshBuilder(2);   // 0 금속 선반, 1 나무 탁자·계산대
        string c0 = ground.Count > 0 ? (ground[0].c ?? "") : "", m0 = ground.Count > 0 ? (ground[0].m ?? "") : "";
        bool food = m0.Contains("음식") || c0.Contains("카페") || c0.Contains("빵");

        // 벽 따라 선반 (정면 벽 제외)
        for (int i = 0; i < inner.Count; i++)
        {
            if (i == front) continue;
            Vector2 a = inner[i], e = inner[(i + 1) % inner.Count];
            float L = (e - a).magnitude;
            if (L < 2.4f) continue;
            Vector2 d = (e - a) / L, inward = new Vector2(-d.y, d.x);
            float len = L - 1.4f;
            Vector2 mid = (a + e) * 0.5f + inward * 0.32f;
            float yaw = Mathf.Atan2(-d.y, d.x) * Mathf.Rad2Deg;
            float hgt = food ? 1.0f : 1.8f;
            mb.Box(food ? 1 : 0, new Vector3(mid.x - c.x, hgt * 0.5f, mid.y - c.y), new Vector3(len, hgt, 0.5f), yaw);
        }

        // 가운데: 정면과 나란히
        Vector2 fd = new Vector2(-fn.z, fn.x);   // 정면 벽 방향
        float fyaw = Mathf.Atan2(-fd.y, fd.x) * Mathf.Rad2Deg;
        Vector2 ctr = Poly.Centroid(inner);
        Vector2 back = new Vector2(-fn.x, -fn.z);
        if (food)
        {
            for (int gx = -2; gx <= 2; gx++)
                for (int gz = -2; gz <= 2; gz++)
                {
                    Vector2 q = ctr + fd * (gx * 2.1f) + back * (gz * 2.1f);
                    if (!InsideShrunk(inner, q, 1.1f)) continue;
                    mb.Box(1, new Vector3(q.x - c.x, 0.37f, q.y - c.y), new Vector3(0.9f, 0.74f, 0.9f), fyaw);
                }
        }
        else
        {
            for (int row = -1; row <= 1; row++)
            {
                Vector2 q = ctr + back * (row * 2.4f);
                float len = 4f;
                while (len >= 1.6f && !(InsideShrunk(inner, q + fd * (len * 0.5f), 1.0f) && InsideShrunk(inner, q - fd * (len * 0.5f), 1.0f))) len -= 0.8f;
                if (len < 1.6f) continue;
                mb.Box(0, new Vector3(q.x - c.x, 0.7f, q.y - c.y), new Vector3(len, 1.4f, 0.8f), fyaw);
            }
        }
        var go = mb.Build(nm + " 안", interiorRoot, origin, new[] { Kit.Mat(Kit.Hex("8A9098")), Kit.Mat(Kit.Hex("7A5C40")) }, true);

        // 뒤질 곳: 안쪽 깊숙이 (가게마다 하나, 최대 2곳)
        int n = Mathf.Min(2, ground.Count);
        for (int i = 0; i < n; i++)
        {
            Vector2 q = ctr + back * 1.2f + fd * ((i - (n - 1) * 0.5f) * 2.2f);
            if (!InsideShrunk(inner, q, 0.7f)) q = ctr;
            var sp = ground[i];
            string c1 = sp.c ?? "", m1 = sp.m ?? "";
            string label = sp.n.Length > 10 ? sp.n.Substring(0, 10) : sp.n;
            Vector3 pos = new Vector3(q.x, 0.45f, q.y);
            var size = new Vector3(1.1f, 0.9f, 0.6f);
            if (c1.Contains("편의점") || c1.Contains("슈퍼"))
                Container(lootRoot, label + " 창고 상자", pos, size, (kimbap, 0.7f, 2), (ramen, 0.8f, 3), (water, 0.8f, 3), (coffee, 0.6f, 2), (bandage, 0.3f, 1));
            else if (c1.Contains("약국") || m1.Contains("보건"))
                Container(lootRoot, label + " 약장", pos, size, (bandage, 0.95f, 3), (water, 0.3f, 1));
            else if (c1.Contains("카페") || c1.Contains("빵"))
                Container(lootRoot, label + " 냉장고", pos, size, (coffee, 0.85f, 3), (water, 0.6f, 2), (kimbap, 0.25f, 1));
            else if (m1.Contains("음식"))
                Container(lootRoot, label + " 주방 식자재", pos, size, (ramen, 0.7f, 3), (kimbap, 0.5f, 2), (water, 0.6f, 2));
            else if (c1.Contains("철물") || c1.Contains("정비") || c1.Contains("공구") || c1.Contains("자전거"))
                Container(lootRoot, label + " 공구함", pos, size, (wrench, 0.6f, 1), (pipe, 0.7f, 1), (Fuel, c1.Contains("정비") ? 0.5f : 0.15f, 1));
            else
                Container(lootRoot, label + " 계산대 서랍", pos, size, (water, 0.5f, 1), (bandage, 0.3f, 1), (coffee, 0.3f, 1), (pipe, 0.1f, 1));
        }
    }

    /// <summary>점 q가 다각형 안에 있고, 사방 r만큼 떨어진 점들도 안에 있는가</summary>
    static bool InsideShrunk(List<Vector2> poly, Vector2 q, float r)
    {
        if (!Poly.Contains(poly, q)) return false;
        for (int k = 0; k < 8; k++)
        {
            float ang = k * Mathf.PI * 0.25f;
            if (!Poly.Contains(poly, q + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r)) return false;
        }
        return true;
    }

    // ================= 옥상 · 풀 =================

    /// <summary>옥상 난간(벽을 0.85m 더 올리고 안쪽 면·윗면) + 물탱크·실외기·계단실</summary>
    void RoofDetails(MeshBuilder mb, List<Vector2> pts, Vector2 c, float h, Facade.Style style, int lv, int hash)
    {
        const float ph = 0.85f, t = 0.22f;
        var inner = Poly.Inset(pts, t);
        float top = h + ph;
        for (int i = 0; i < pts.Count; i++)
        {
            int j = (i + 1) % pts.Count;
            Vector2 a = pts[i], e = pts[j], ai = inner[i], ei = inner[j];
            float L = (e - a).magnitude;
            if (L < 0.05f) continue;
            Vector3 n = new Vector3(e.y - a.y, 0, -(e.x - a.x)) / L;
            Vector3 A = new Vector3(a.x - c.x, h, a.y - c.y), E = new Vector3(e.x - c.x, h, e.y - c.y);
            Vector3 Ai = new Vector3(ai.x - c.x, h, ai.y - c.y), Ei = new Vector3(ei.x - c.x, h, ei.y - c.y);
            Vector3 up = Vector3.up * ph;
            mb.Quad(3, A, A + up, E + up, E, n, Vector2.zero, Vector2.up, Vector2.one, Vector2.right);          // 바깥
            mb.Quad(3, Ei, Ei + up, Ai + up, Ai, -n, Vector2.zero, Vector2.up, Vector2.one, Vector2.right);     // 안쪽
            mb.Quad(3, A + up, Ai + up, Ei + up, E + up, Vector3.up, Vector2.zero, Vector2.up, Vector2.one, Vector2.right); // 윗면
        }

        // 옥상 물건: 안쪽 다각형 안에 들어가는 자리만
        var rnd = new System.Random(hash);
        Vector2 ctr = Poly.Centroid(inner);
        float yaw = Mathf.Atan2(pts[1].x - pts[0].x, pts[1].y - pts[0].y) * Mathf.Rad2Deg;
        int items = style == Facade.Style.Apt ? 3 : style == Facade.Style.Office || style == Facade.Style.Shop ? 3 : 2;
        for (int k = 0; k < items; k++)
        {
            Vector2 q = ctr + new Vector2((float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f) * 6f;
            Vector3 size;
            if (k == 0 && style == Facade.Style.Apt && lv >= 6) size = new Vector3(4f, 3f, 4f);           // 계단·승강기실
            else if (k == 0 && rnd.NextDouble() < 0.6) size = new Vector3(1.4f, 1.5f, 1.4f);            // 물탱크
            else size = new Vector3(0.9f, 0.65f, 0.5f);                                               // 실외기
            float rad = Mathf.Max(size.x, size.z) * 0.75f;
            if (!Poly.Contains(inner, q) || !Poly.Contains(inner, q + new Vector2(rad, 0)) || !Poly.Contains(inner, q - new Vector2(rad, 0))
                || !Poly.Contains(inner, q + new Vector2(0, rad)) || !Poly.Contains(inner, q - new Vector2(0, rad))) continue;
            mb.Box(4, new Vector3(q.x - c.x, h + size.y * 0.5f, q.y - c.y), size, yaw);
        }
    }

    readonly Dictionary<long, MeshBuilder> tuftChunks = new Dictionary<long, MeshBuilder>();
    int tuftCount;
    const int MaxTufts = 45000;

    void AddTuft(Vector2 p, System.Random rnd, float scale = 1f)
    {
        if (tuftCount >= MaxTufts) return;
        int i = Cell(p.x, Chunk), j = Cell(p.y, Chunk);
        long k = Key(i, j);
        if (!tuftChunks.TryGetValue(k, out var mb)) tuftChunks[k] = mb = new MeshBuilder(1);
        float s = (0.7f + (float)rnd.NextDouble() * 0.7f) * scale;
        mb.Tuft(0, new Vector3(p.x - i * Chunk, 0f, p.y - j * Chunk), 0.75f * s, 0.5f * s, (float)rnd.NextDouble() * 180f);
        tuftCount++;
    }

    /// <summary>건물 벽 밑 틈새로 자란 풀 (사람이 떠난 지 오래된 거리)</summary>
    void TuftsAround(List<Vector2> pts, int hash)
    {
        var rnd = new System.Random(hash ^ 0x7a11);
        for (int i = 0; i < pts.Count; i++)
        {
            Vector2 a = pts[i], e = pts[(i + 1) % pts.Count];
            float L = (e - a).magnitude;
            if (L < 1f) continue;
            Vector2 d = (e - a) / L, n = new Vector2(d.y, -d.x);
            for (float t = rnd.Next(0, 2); t < L; t += 1.8f)
                if (rnd.NextDouble() < 0.32) AddTuft(a + d * t + n * (0.15f + (float)rnd.NextDouble() * 0.35f), rnd);
        }
    }

    /// <summary>풀: 골목 가장자리, 공원·풀밭 (청크별로 한 메시)</summary>
    void BuildRealTufts()
    {
        var rnd = new System.Random(UsedSeed ^ 0x6a55);
        foreach (var r in real.roads)
        {
            if (r.tu == 1 || r.t == "motorway" || r.t == "motorway_link") continue;
            var p = Pts(r.p);
            bool alley = r.t == "residential" || r.t == "living_street" || r.t == "service" || r.t == "unclassified" || r.t == "path" || r.t == "track";
            float prob = alley ? 0.28f : 0.08f;
            for (int i = 0; i + 1 < p.Count; i++)
            {
                Vector2 a = p[i], b = p[i + 1];
                float L = (b - a).magnitude;
                if (L < 0.5f) continue;
                Vector2 d = (b - a) / L, n = new Vector2(-d.y, d.x);
                float hw = RoadHalfW(r, i, i + 1);
                for (float t = 0; t < L; t += 2.2f)
                    for (int side = -1; side <= 1; side += 2)
                        if (rnd.NextDouble() < prob)
                        {
                            Vector2 q = a + d * t + n * side * (hw + (Major(r.t) ? 3.3f : 0.2f) + (float)rnd.NextDouble() * 0.4f);
                            if (!InsideBuilding(q)) AddTuft(q, rnd);
                        }
            }
        }
        if (real.areas != null)
            foreach (var ar in real.areas)
            {
                if (ar.k != "park" && ar.k != "grass" && ar.k != "wood" && ar.k != "farm") continue;
                var p = Pts(ar.p);
                if (p.Count < 3) continue;
                float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
                foreach (var q in p) { minX = Mathf.Min(minX, q.x); maxX = Mathf.Max(maxX, q.x); minZ = Mathf.Min(minZ, q.y); maxZ = Mathf.Max(maxZ, q.y); }
                float step = ar.k == "wood" ? 5f : 3.2f;
                for (float x = minX; x < maxX; x += step)
                    for (float z = minZ; z < maxZ; z += step)
                    {
                        var q = new Vector2(x + (float)rnd.NextDouble() * step, z + (float)rnd.NextDouble() * step);
                        if (rnd.NextDouble() < 0.6 && Poly.Contains(p, q)) AddTuft(q, rnd, 1.25f);
                    }
            }

        var parent = new GameObject("구미_풀").transform;
        parent.SetParent(root, false);
        var mats = new[] { SurfaceTextures.Tuft() };
        foreach (var kv in tuftChunks)
        {
            int ci = (int)(kv.Key >> 32), cj = (int)(uint)kv.Key;
            var go = kv.Value.Build($"풀_{ci}_{cj}", parent, new Vector3(ci * Chunk, 0, cj * Chunk), mats, false, false);
        }
        tuftChunks.Clear();
        Debug.Log($"[Gumi] 풀 포기 {tuftCount}개");
    }

    // ================= 실제 가게 간판 =================

    static Color SignColor(MapShop s, int salt)
    {
        string c = s.c ?? "", m = s.m ?? "";
        Color[] pal;
        if (c.Contains("편의점")) pal = new[] { Kit.Hex("3A8A5C"), Kit.Hex("5A3A8A"), Kit.Hex("2E6EA8") };
        else if (c.Contains("약국")) pal = new[] { Kit.Hex("2E7D6B"), Kit.Hex("3A8A5C") };
        else if (c.Contains("카페") || c.Contains("빵")) pal = new[] { Kit.Hex("4A3428"), Kit.Hex("2E3135"), Kit.Hex("E9ECEF") };
        else if (c.Contains("치킨")) pal = new[] { Kit.Hex("F2C230"), Kit.Hex("C8402E") };
        else if (m.Contains("음식")) pal = new[] { Kit.Hex("C8402E"), Kit.Hex("7A5A3A"), Kit.Hex("E8892B"), Kit.Hex("A8322A") };
        else if (m.Contains("교육")) pal = new[] { Kit.Hex("34558A"), Kit.Hex("2E3A48"), Kit.Hex("E9ECEF") };
        else if (m.Contains("보건")) pal = new[] { Kit.Hex("2E7D6B"), Kit.Hex("34558A"), Kit.Hex("E9ECEF") };
        else if (m.Contains("숙박")) pal = new[] { Kit.Hex("8A2E6A"), Kit.Hex("5A3A8A") };
        else if (m.Contains("예술") || m.Contains("스포츠")) pal = new[] { Kit.Hex("5A3A8A"), Kit.Hex("2E3A48") };
        else if (m.Contains("부동산")) pal = new[] { Kit.Hex("34558A"), Kit.Hex("E9ECEF") };
        else if (m.Contains("소매")) pal = new[] { Kit.Hex("2E6EA8"), Kit.Hex("3A8A5C"), Kit.Hex("E9ECEF"), Kit.Hex("C8402E") };
        else pal = new[] { Kit.Hex("6A6E74"), Kit.Hex("2E3A48"), Kit.Hex("E9ECEF") };
        return pal[Mathf.Abs((s.n ?? "").GetHashCode() + salt) % pal.Length];
    }

    static Color TextOn(Color bg) => (bg.r * 0.3f + bg.g * 0.59f + bg.b * 0.11f) > 0.62f ? Kit.Hex("2E3135") : Color.white;

    /// <summary>벽에 붙은 간판 판 + 글씨. center = 판 중앙(로컬, 벽면 위), n = 바깥 법선</summary>
    static void SignPanel(Transform t, string text, Vector3 center, Vector3 n, Vector3 size, Color bg, Color fg)
    {
        var rot = Quaternion.LookRotation(n, Vector3.up);
        Kit.Box(t, center + n * (size.z * 0.5f + 0.02f), size, bg, rot: rot, name: "간판");
        // 한글 한 글자 폭 ≈ 글자 높이 → 판 안에 들어가게 (양옆 여백 반 글자씩)
        float hgt = Mathf.Min(size.y * 0.72f, size.x / Mathf.Max(2, text.Length + 1) * 0.92f);
        Kit.Label(t, text, center + n * (size.z + 0.04f), Kit.Facing(n), hgt, fg);
    }

    void ShopSigns(Transform t, List<MapShop> ground, List<MapShop> upper, Vector3 mid, Vector3 n, Vector2 d, float frontLen, float h, int hash)
    {
        Vector3 along = new Vector3(d.x, 0, d.y);
        // 1층: 정면을 가게 수만큼 나눠 가게마다 간판
        int k = Mathf.Clamp(ground.Count, 0, Mathf.Max(1, Mathf.FloorToInt(frontLen / 3.2f)));
        float seg = (frontLen - 0.4f) / Mathf.Max(1, k);
        for (int i = 0; i < k; i++)
        {
            var sp = ground[i];
            float off = -frontLen * 0.5f + 0.2f + seg * (i + 0.5f);
            Color bg = SignColor(sp, i);
            string name = sp.n.Length > 12 ? sp.n.Substring(0, 12) : sp.n;
            SignPanel(t, name, mid + along * off + Vector3.up * 2.95f, n, new Vector3(seg - 0.25f, 0.75f, 0.1f), bg, TextOn(bg));
        }
        // 2층 이상: 층마다 창문 위에 판 간판 (한 층에 최대 2개)
        var perFloor = new Dictionary<int, int>();
        int made = 0;
        foreach (var sp in upper)
        {
            if (made >= 8) break;
            perFloor.TryGetValue(sp.f, out int c);
            if (c >= 2) continue;
            perFloor[sp.f] = c + 1;
            float y = Facade.ShopFloor + (sp.f - 2) * Facade.Floor + 2.55f;
            if (y > h - 0.3f) continue;
            float w = Mathf.Min(frontLen * 0.45f, 1.2f + sp.n.Length * 0.42f);
            float off = (c == 0 ? -1f : 1f) * (frontLen * 0.25f);
            Color bg = SignColor(sp, sp.f);
            string name = sp.n.Length > 10 ? sp.n.Substring(0, 10) : sp.n;
            SignPanel(t, name, mid + along * off + Vector3.up * y, n, new Vector3(w, 0.62f, 0.08f), bg, TextOn(bg));
            made++;
        }
    }

    void PlaceShopLoot(List<MapShop> ground, Vector3 mid, Vector3 n, Vector2 d, float frontLen, int hash)
    {
        // 가게 2곳 중 1곳 꼴로 문 앞 매대가 남아 있다
        Vector3 along = new Vector3(d.x, 0, d.y);
        int k = Mathf.Clamp(ground.Count, 0, Mathf.Max(1, Mathf.FloorToInt(frontLen / 3.2f)));
        float seg = (frontLen - 0.4f) / Mathf.Max(1, k);
        for (int i = 0; i < k; i++)
        {
            var sp = ground[i];
            if (((hash >> i) + i * 7) % 10 >= 5) continue;
            string c = sp.c ?? "", m = sp.m ?? "";
            Vector3 pos = mid + along * (-frontLen * 0.5f + 0.2f + seg * (i + 0.5f)) + n * 0.9f + Vector3.up * 0.8f;
            string label = (sp.n.Length > 10 ? sp.n.Substring(0, 10) : sp.n);
            var size = new Vector3(Mathf.Min(1.4f, seg - 0.4f), 1.6f, 0.5f);
            if (c.Contains("편의점") || c.Contains("슈퍼"))
                Container(lootRoot, label + " 진열대", pos, size, shelfMat, (kimbap, 0.7f, 2), (ramen, 0.7f, 3), (water, 0.8f, 3), (coffee, 0.6f, 2), (bandage, 0.25f, 1));
            else if (c.Contains("약국") || m.Contains("보건"))
                Container(lootRoot, label + " 약장", pos, size, shelfMat, (bandage, 0.9f, 3), (water, 0.3f, 1));
            else if (c.Contains("카페") || c.Contains("빵"))
                Container(lootRoot, label + " 쇼케이스", pos, size, shelfMat, (coffee, 0.8f, 3), (water, 0.5f, 1), (kimbap, 0.2f, 1));
            else if (m.Contains("음식"))
                Container(lootRoot, label + " 주방 식자재", pos + Vector3.down * 0.4f, new Vector3(size.x, 0.8f, 0.7f), (ramen, 0.6f, 2), (kimbap, 0.5f, 2), (water, 0.5f, 1));
            else if (c.Contains("철물") || c.Contains("정비") || c.Contains("공구") || c.Contains("자전거"))
                Container(lootRoot, label + " 공구 선반", pos, size, shelfMat, (wrench, 0.5f, 1), (pipe, 0.7f, 1), (Fuel, c.Contains("정비") ? 0.45f : 0.1f, 1));
            else if (m.Contains("소매"))
                Container(lootRoot, label + " 매대", pos, size, shelfMat, (water, 0.4f, 1), (bandage, 0.2f, 1), (pipe, 0.1f, 1));
        }
    }

    void PlaceBuildingLoot(MapBuilding b, Facade.Style style, bool store, MapPoi poi, Vector3 mid, Vector3 n, Vector2 d, float frontLen, int hash)
    {
        float roll = (hash % 1000) / 1000f;
        string kind = poi != null ? poi.k : b.a;
        Vector3 side = new Vector3(d.x, 0, d.y) * Mathf.Clamp(((hash / 3) % 100 / 100f - 0.5f) * frontLen * 0.6f, -4f, 4f);

        if (kind == "fuel")
        {
            Container(lootRoot, "주유소 창고", mid + n * 1.0f + side + Vector3.up * 0.5f, new Vector3(1.2f, 1f, 0.8f), containerMat,
                new List<ItemData> { Fuel }, (Fuel, 0.5f, 1), (water, 0.4f, 1));
            return;
        }
        if (store || kind == "convenience" || kind == "supermarket")
        {
            if (roll > 0.5f) return;
            bool med = kind == "pharmacy" || kind == "doctors" || kind == "dentist" || kind == "hospital" || kind == "clinic";
            bool hw = kind == "hardware" || kind == "doityourself" || kind == "car_repair";
            if (med)
                Container(lootRoot, "약국 진열대", mid + n * 0.9f + side + Vector3.up * 0.8f, new Vector3(1.2f, 1.6f, 0.5f), shelfMat,
                    (bandage, 0.9f, 3), (water, 0.3f, 1));
            else if (hw)
                Container(lootRoot, "공구 진열대", mid + n * 0.9f + side + Vector3.up * 0.8f, new Vector3(1.2f, 1.6f, 0.5f), shelfMat,
                    (wrench, 0.5f, 1), (pipe, 0.6f, 1), (Fuel, 0.15f, 1));
            else
                Container(lootRoot, "가게 매대", mid + n * 0.9f + side + Vector3.up * 0.8f, new Vector3(1.2f, 1.6f, 0.5f), shelfMat,
                    (kimbap, 0.6f, 2), (ramen, 0.6f, 2), (water, 0.6f, 2), (coffee, 0.5f, 2), (bandage, 0.15f, 1));
            return;
        }
        switch (style)
        {
            case Facade.Style.School:
                if (roll < 0.5f)
                    Container(lootRoot, "급식실 식자재", mid + n * 1.0f + side + Vector3.up * 0.4f, new Vector3(1.4f, 0.8f, 0.8f),
                        (ramen, 0.7f, 3), (water, 0.6f, 2), (kimbap, 0.3f, 1));
                break;
            case Facade.Style.Public:
                if (roll < 0.5f)
                    Container(lootRoot, "구급함", mid + n * 0.9f + side + Vector3.up * 0.9f, new Vector3(0.6f, 0.5f, 0.3f),
                        (bandage, 0.9f, 2), (water, 0.4f, 1));
                break;
            case Facade.Style.Apt:
                if (roll < 0.35f)
                    Container(lootRoot, "택배 보관함", mid + n * 0.9f + side + Vector3.up * 0.6f, new Vector3(1.6f, 1.2f, 0.6f),
                        (ramen, 0.6f, 2), (water, 0.5f, 2), (bandage, 0.4f, 1));
                break;
            default:
                if (roll < 0.2f)
                    Container(lootRoot, roll < 0.1f ? "현관 앞 택배 상자" : "분리수거함", mid + n * 0.8f + side + Vector3.up * 0.3f, new Vector3(0.7f, 0.6f, 0.5f),
                        (kimbap, 0.35f, 1), (ramen, 0.3f, 1), (water, 0.35f, 1), (bandage, 0.15f, 1), (pipe, 0.05f, 1));
                break;
        }
    }

    // ================= 나무 =================

    class TreePart { public Mesh mesh; public Material[] mats; public Matrix4x4 local; }

    void BuildRealTrees()
    {
        if (real.trees == null || real.trees.Length < 3) return;
        var prefab = AssetLib.Prefab("Kenney/Suburban/tree-large");
        if (prefab == null) { Debug.LogWarning("[Gumi] 나무 에셋이 없어 나무를 생략합니다"); return; }

        var inst = Instantiate(prefab);
        inst.transform.position = Vector3.zero;
        inst.transform.rotation = Quaternion.identity;
        Bounds lb = AssetLib.LocalBounds(inst.transform);
        var parts = new List<TreePart>();
        Matrix4x4 toRoot = inst.transform.worldToLocalMatrix;
        foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
        {
            var mr = mf.GetComponent<MeshRenderer>();
            if (mf.sharedMesh == null || mr == null) continue;
            parts.Add(new TreePart { mesh = mf.sharedMesh, mats = mr.sharedMaterials, local = toRoot * mf.transform.localToWorldMatrix });
        }
        DestroyImmediate(inst);
        if (parts.Count == 0 || lb.size.y < 1e-4f) return;

        var chunks = new Dictionary<long, Dictionary<Material, List<CombineInstance>>>();
        var colliders = new Dictionary<long, GameObject>();
        var treeRoot = new GameObject("구미_나무").transform;
        treeRoot.SetParent(root, false);
        var tr = new System.Random(UsedSeed ^ 0x5eed);

        for (int i = 0; i + 2 < real.trees.Length; i += 3)
        {
            float x = real.trees[i], z = real.trees[i + 1];
            int kind = (int)real.trees[i + 2];
            float height;
            switch (kind)
            {
                case 0: height = 6.5f + (float)tr.NextDouble() * 3.5f; break;
                case 1: height = 4.5f + (float)tr.NextDouble() * 2f; break;
                case 2: height = 5f + (float)tr.NextDouble() * 2.5f; break;
                default: height = 5f + (float)tr.NextDouble() * 1.5f; break;
            }
            float s = height / lb.size.y;
            int ci = Cell(x, Chunk), cj = Cell(z, Chunk);
            long k = Key(ci, cj);
            Vector3 o = new Vector3(ci * Chunk, 0, cj * Chunk);
            Vector3 pos = new Vector3(x, -lb.min.y * s, z) - o;
            var trs = Matrix4x4.TRS(pos, Quaternion.Euler(0, (float)tr.NextDouble() * 360f, 0), Vector3.one * s);

            if (!chunks.TryGetValue(k, out var byMat)) chunks[k] = byMat = new Dictionary<Material, List<CombineInstance>>();
            foreach (var part in parts)
                for (int sm = 0; sm < part.mesh.subMeshCount && sm < part.mats.Length; sm++)
                {
                    if (!byMat.TryGetValue(part.mats[sm], out var list)) byMat[part.mats[sm]] = list = new List<CombineInstance>();
                    list.Add(new CombineInstance { mesh = part.mesh, subMeshIndex = sm, transform = trs * part.local });
                }

            // 가로수·공원 나무는 줄기에 부딪힌다 (숲은 그냥 지나간다)
            if (kind >= 2)
            {
                if (!colliders.TryGetValue(k, out var holder))
                {
                    holder = new GameObject($"나무줄기_{ci}_{cj}");
                    holder.transform.SetParent(treeRoot, false);
                    holder.transform.position = o;
                    colliders[k] = holder;
                }
                var bc = holder.AddComponent<BoxCollider>();
                bc.center = new Vector3(pos.x, 1.2f, pos.z);
                bc.size = new Vector3(0.45f, 2.4f, 0.45f);
            }
        }

        foreach (var kv in chunks)
        {
            int ci = (int)(kv.Key >> 32), cj = (int)(uint)kv.Key;
            foreach (var mk in kv.Value)
            {
                var mesh = new Mesh { name = $"Trees_{ci}_{cj}", indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(mk.Value.ToArray(), true, true);
                var go = new GameObject($"나무_{ci}_{cj}");
                go.transform.SetParent(treeRoot, false);
                go.transform.position = new Vector3(ci * Chunk, 0, cj * Chunk);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = mk.Key;
            }
        }
    }

    // ================= 가로등 · 전봇대 · 주차된 차 =================

    void BuildRealStreetFurniture()
    {
        var street = new GameObject("구미_거리").transform;
        street.SetParent(root, false);
        int lamps = 0, poles = 0, cars = 0;
        const int MaxLamps = 700, MaxPoles = 750, MaxCars = 420;

        foreach (var r in real.roads)
        {
            if (r.tu == 1 || r.b == 1) continue;
            var p = Pts(r.p);
            float hw = r.w * 0.5f;
            bool major = Major(r.t) && r.t != "motorway" && r.t != "motorway_link";
            bool alley = r.t == "residential" || r.t == "living_street" || r.t == "unclassified";
            bool parkable = alley || r.t == "service";
            if (!major && !alley && !parkable) continue;

            float lampNext = R(5f, 20f), poleNext = R(3f, 15f), carNext = R(4f, 12f);
            float dist = 0f;
            int lampSide = 1;
            Vector3 lastPole = Vector3.zero;
            bool havePole = false;
            int poleSide = Chance(0.5f) ? 1 : -1;

            for (int i = 0; i + 1 < p.Count; i++)
            {
                Vector2 a = p[i], b = p[i + 1];
                float L = (b - a).magnitude;
                if (L < 0.01f) continue;
                Vector2 d = (b - a) / L, n = new Vector2(-d.y, d.x);
                hw = RoadHalfW(r, i, i + 1);
                while (true)
                {
                    float next = Mathf.Min(major ? lampNext : float.MaxValue, alley ? poleNext : float.MaxValue, parkable ? carNext : float.MaxValue);
                    if (next > dist + L) break;
                    Vector2 q = a + d * (next - dist);
                    bool inMap = Mathf.Abs(q.x) < PlayHalfX - 3 && Mathf.Abs(q.y) < PlayHalfZ - 3;

                    if (major && Mathf.Approximately(next, lampNext))
                    {
                        lampNext += 34f;
                        lampSide = -lampSide;
                        Vector2 lp = q + n * lampSide * (hw + 0.8f);
                        if (inMap && lamps < MaxLamps && !InsideBuilding(lp, 0.8f))
                        {
                            Vector2 toRoad = -n * lampSide;
                            BuildingKit.StreetLight(street, new Vector3(lp.x, 0, lp.y), Mathf.Atan2(toRoad.x, toRoad.y) * Mathf.Rad2Deg);
                            lamps++;
                        }
                    }
                    else if (alley && Mathf.Approximately(next, poleNext))
                    {
                        poleNext += R(27f, 33f);
                        Vector2 pp = q + n * poleSide * (hw + 0.4f);
                        if (inMap && poles < MaxPoles && !InsideBuilding(pp, 0.5f))
                        {
                            var pos = new Vector3(pp.x, 0, pp.y);
                            BuildingKit.UtilityPole(street, pos, Mathf.Atan2(n.x, n.y) * Mathf.Rad2Deg, Chance(0.15f));
                            if (havePole && (pos - lastPole).magnitude < 45f) BuildingKit.Wires(street, lastPole, pos);
                            lastPole = pos; havePole = true;
                            poles++;
                        }
                        else havePole = false;
                    }
                    else
                    {
                        carNext += R(9f, 16f);
                        if (inMap && cars < MaxCars && Chance(r.t == "service" ? 0.12f : 0.09f))
                        {
                            int side = Chance(0.5f) ? 1 : -1;
                            Vector2 cp = q + n * side * Mathf.Max(0f, hw - 1.15f);
                            if (!InsideBuilding(cp, 1.2f))
                            {
                                float yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg + (Chance(0.5f) ? 0f : 180f);
                                var car = BuildingKit.Car(street, new Vector3(cp.x, 0f, cp.y), yaw + R(-4f, 4f), rng);
                                if (Chance(0.4f))
                                {
                                    AddLoot(car.GetChild(0).gameObject, "주차된 차 트렁크",
                                        (water, 0.4f, 1), (pipe, 0.15f, 1), (coffee, 0.4f, 1), (bandage, 0.3f, 1), (Fuel, 0.3f, 1));
                                    Kit.Box(car, new Vector3(0, 0.92f, 1.9f), new Vector3(1.4f, 0.06f, 0.5f), Kit.Loot, name: "트렁크 표시");
                                }
                                cars++;
                            }
                        }
                    }
                }
                dist += L;
            }
        }
        Debug.Log($"[Gumi] 거리: 가로등 {lamps}, 전봇대 {poles}, 주차된 차 {cars}");
    }

    // ================= 플레이어 / 안드로이드 =================

    Vector3 PlayerPos()
    {
        var p = GameObject.FindGameObjectWithTag("Player");
        return p ? p.transform.position : Vector3.zero;
    }

    /// <summary>c에서 rmin~rmax 거리에 있는 도로 위 아무 점</summary>
    Vector3 RealRoadPoint(Vector3 c, float rmin, float rmax)
    {
        int r = Mathf.CeilToInt(rmax / RoadCell);
        int ci = Cell(c.x, RoadCell), cj = Cell(c.z, RoadCell);
        int seen = 0, pick = -1;
        float best = float.MaxValue; int nearest = -1;
        for (int i = ci - r; i <= ci + r; i++)
            for (int j = cj - r; j <= cj + r; j++)
            {
                if (!roadCells.TryGetValue(Key(i, j), out var list)) continue;
                foreach (int k in list)
                {
                    float dd = Vector2.Distance(new Vector2(roadPts[k].x, roadPts[k].z), new Vector2(c.x, c.z));
                    if (dd < best) { best = dd; nearest = k; }
                    if (dd < rmin || dd > rmax) continue;
                    seen++;
                    if (rng.Next(seen) == 0) pick = k;   // 고르게 하나 뽑기
                }
            }
        if (pick >= 0) return roadPts[pick] + new Vector3(R(-1.5f, 1.5f), 0, R(-1.5f, 1.5f));
        if (nearest >= 0) return roadPts[nearest];
        return new Vector3(c.x, 0, c.z);
    }

    static string Compass(Vector3 d)
    {
        float a = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        if (a < 0) a += 360f;
        string[] n = { "북", "북동", "동", "남동", "남", "남서", "서", "북서" };
        return n[Mathf.RoundToInt(a / 45f) % 8];
    }

    void PlacePlayerReal()
    {
        var player = GameObject.FindGameObjectWithTag("Player");
        var sh = real.safehouse;
        Vector3 home = new Vector3(sh.x, 0f, sh.z);
        Quaternion rot = Quaternion.Euler(0, sh.yaw, 0);
        float w = Mathf.Max(7f, sh.w * 0.96f), d = Mathf.Max(8f, sh.d * 0.96f);

        // 내 원룸: 1층 필로티에 고장난 발전기
        var house = BuildingKit.Oneroom(root, home, sh.yaw, w, d, 3, rng, true);
        house.name = "내 원룸 (거점)";
        Vector3 genPos = home + rot * new Vector3(w / 4f, 0f, -d / 4f);
        Generator.FuelItem = Fuel;
        Generator.Build(root, genPos, sh.yaw + 180f);
        SafehousePos = genPos;

        Vector3 start = home + rot * new Vector3(-w / 4f, 0.1f, -d / 2f - 1.6f);
        if (player != null)
        {
            var cc = player.GetComponent<CharacterController>();
            if (cc) cc.enabled = false;
            player.transform.position = start;
            player.transform.rotation = Quaternion.Euler(0, sh.yaw + 180f, 0);
            if (cc) cc.enabled = true;
        }
        MakeStarterToolbox(start + rot * new Vector3(-2.2f, 0.2f, 0.6f));

        // 확실한 연료통: 60~100m 떨어진 골목에 쓰러진 배달 오토바이
        Vector3 bike = RealRoadPoint(start, 60f, 100f);
        MakeFuelBike(bike, R(0, 360));
        Vector3 dir = bike - start;
        FuelHintText = $"{Compass(dir)}쪽 {dir.magnitude:0}m쯤, 길가에 쓰러진 배달 오토바이를 뒤져 보자";

        var cam = FindFirstObjectByType<IsometricCamera>();
        if (cam != null) cam.Snap();
    }

    void SpawnAndroidsReal()
    {
        Vector3 pp = PlayerPos();
        int count = nearbyAndroidsBase + 3;
        for (int n = 0; n < count; n++)
        {
            Vector3 pos = RealRoadPoint(pp, 55f, 145f);
            if (Vector3.Distance(pos, pp) < 45f) continue;
            MakeAndroid($"작업형_안드로이드_{++spawnSerial}", pos, Patrol(pos));
        }
    }

    Vector3[] Patrol(Vector3 pos)
    {
        var wps = new Vector3[RI(3, 6)];
        for (int i = 0; i < wps.Length; i++) wps[i] = RealRoadPoint(pos, 10f, R(25f, 50f));
        return wps;
    }

    /// <summary>멀어진 안드로이드는 정리하고, 주변이 비면 시야 밖에서 새로 나타나게 한다.</summary>
    void UpdatePopulationReal()
    {
        populationTimer -= Time.deltaTime;
        if (populationTimer > 0) return;
        populationTimer = 4f;
        var gm = GameManager.Instance;
        Vector3 pp = PlayerPos();
        int near = 0;
        foreach (var ai in androidRoot.GetComponentsInChildren<AndroidAI>())
        {
            float dd = Vector3.Distance(ai.transform.position, pp);
            if (dd > despawnDistance && ai.CurrentState != AndroidAI.State.Chase)
            {
                if (ai.waypoints != null && ai.waypoints.Length > 0 && ai.waypoints[0] != null)
                    Destroy(ai.waypoints[0].parent.gameObject);
                Destroy(ai.gameObject);
                continue;
            }
            if (ai.CurrentState != AndroidAI.State.Disabled) near++;
        }
        int day = gm != null ? gm.Day : 1;
        bool night = gm != null && (gm.HourOfDay >= 18.5f || gm.HourOfDay < 6f);
        int want = nearbyAndroidsBase + (day - 1) * nearbyAndroidsPerDay + (night ? 3 : 0);
        if (near < want)
        {
            Vector3 pos = RealRoadPoint(pp, 100f, 150f);
            if (Vector3.Distance(pos, pp) >= 80f)
                MakeAndroid($"작업형_안드로이드_{++spawnSerial}", pos, Patrol(pos));
        }
    }
}
