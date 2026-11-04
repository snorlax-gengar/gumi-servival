using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 게임 시작 시 이 오브젝트의 자식(바닥, 건물, 선반)으로 NavMesh를 굽는다.
/// 별도 패키지나 Bake 버튼 없이 안드로이드가 길을 찾을 수 있게 해 준다.
/// CityGenerator(-2000)가 도시를 만든 뒤, 다른 스크립트보다는 먼저 실행된다.
///
/// LocalMode (실제 구미 지도처럼 넓은 맵): 맵 전체 대신 플레이어 주변 사각형만 굽고,
/// 플레이어가 멀리 움직이면 백그라운드에서 다시 굽는다.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class LevelNavMesh : MonoBehaviour
{
    NavMeshDataInstance instance;

    /// <summary>맵 전체 범위 (미니맵 전체 지도에서 사용)</summary>
    public static Bounds LevelBounds { get; private set; }

    public static bool LocalMode;
    public static float LocalHalfExtent = 170f;
    public static float RebuildDistance = 45f;

    static bool boundsGiven;
    public static void SetLevelBounds(Bounds b) { LevelBounds = b; boundsGiven = true; }

    // ---- 지역 모드 ----
    readonly List<NavMeshBuildSource> allSources = new List<NavMeshBuildSource>();
    readonly List<Bounds> allBounds = new List<Bounds>();
    readonly List<NavMeshBuildSource> picked = new List<NavMeshBuildSource>();
    NavMeshData data;
    NavMeshBuildSettings settings;
    AsyncOperation op;
    Vector3 center;
    Transform player;

    void Awake()
    {
        Physics.SyncTransforms();

        var sources = new List<NavMeshBuildSource>();
        NavMeshBuilder.CollectSources(transform, ~0, NavMeshCollectGeometry.PhysicsColliders, 0,
            new List<NavMeshBuildMarkup>(), sources);

        if (LocalMode) { InitLocal(sources); return; }

        // 자식 콜라이더 전체를 감싸는 범위로 굽는다 (맵 크기가 바뀌어도 자동)
        Bounds bounds = new Bounds(transform.position, Vector3.one);
        bool first = true;
        foreach (var c in GetComponentsInChildren<Collider>())
        {
            if (first) { bounds = c.bounds; first = false; }
            else bounds.Encapsulate(c.bounds);
        }
        if (!boundsGiven) LevelBounds = bounds;
        bounds.Expand(new Vector3(2f, 10f, 2f));

        NavMeshBuildSettings s = NavMesh.GetSettingsByID(0);
        NavMeshData d = NavMeshBuilder.BuildNavMeshData(s, sources, bounds, Vector3.zero, Quaternion.identity);
        instance = NavMesh.AddNavMeshData(d);
    }

    void InitLocal(List<NavMeshBuildSource> sources)
    {
        foreach (var s in sources)
        {
            var col = s.component as Collider;
            if (col == null) continue;
            allSources.Add(s);
            allBounds.Add(col.bounds);
        }

        settings = NavMesh.GetSettingsByID(0);
        settings.overrideVoxelSize = true;
        settings.voxelSize = 0.2f;

        var p = GameObject.FindGameObjectWithTag("Player");
        player = p != null ? p.transform : null;
        center = player != null ? player.position : transform.position;

        data = new NavMeshData(settings.agentTypeID);
        instance = NavMesh.AddNavMeshData(data);
        Bounds b = LocalBounds(center);
        float t0 = Time.realtimeSinceStartup;
        NavMeshBuilder.UpdateNavMeshData(data, settings, Pick(b), b);   // 처음 한 번은 기다린다
        Debug.Log($"[Gumi] 주변 NavMesh {LocalHalfExtent * 2:0}m 사각형 ({allSources.Count}개 중 {picked.Count}개, {Time.realtimeSinceStartup - t0:0.00}초)");
    }

    Bounds LocalBounds(Vector3 c) =>
        new Bounds(new Vector3(c.x, 8f, c.z), new Vector3(LocalHalfExtent * 2f, 30f, LocalHalfExtent * 2f));

    List<NavMeshBuildSource> Pick(Bounds b)
    {
        picked.Clear();
        for (int i = 0; i < allSources.Count; i++)
            if (allBounds[i].Intersects(b)) picked.Add(allSources[i]);
        return picked;
    }

    void Update()
    {
        if (!LocalMode || data == null || player == null) return;
        if (op != null && !op.isDone) return;
        Vector3 d = player.position - center; d.y = 0;
        if (d.magnitude < RebuildDistance) return;
        center = player.position;
        Bounds b = LocalBounds(center);
        op = NavMeshBuilder.UpdateNavMeshDataAsync(data, settings, new List<NavMeshBuildSource>(Pick(b)), b);
    }

    void OnDestroy()
    {
        if (instance.valid) NavMesh.RemoveNavMeshData(instance);
        // 도메인 리로드가 꺼져 있어서 다음 플레이에 남지 않게
        LocalMode = false;
        boundsGiven = false;
    }
}
