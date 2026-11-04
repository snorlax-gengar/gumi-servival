using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 게임 시작 시 이 오브젝트의 자식(바닥, 건물, 선반)으로 NavMesh를 굽는다.
/// 별도 패키지나 Bake 버튼 없이 안드로이드가 길을 찾을 수 있게 해 준다.
/// CityGenerator(-2000)가 도시를 만든 뒤, 다른 스크립트보다는 먼저 실행된다.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class LevelNavMesh : MonoBehaviour
{
    NavMeshDataInstance instance;

    /// <summary>맵 전체 범위 (미니맵 전체 지도에서 사용)</summary>
    public static Bounds LevelBounds { get; private set; }

    void Awake()
    {
        Physics.SyncTransforms();

        var sources = new List<NavMeshBuildSource>();
        NavMeshBuilder.CollectSources(transform, ~0, NavMeshCollectGeometry.PhysicsColliders, 0,
            new List<NavMeshBuildMarkup>(), sources);

        // 자식 콜라이더 전체를 감싸는 범위로 굽는다 (맵 크기가 바뀌어도 자동)
        Bounds bounds = new Bounds(transform.position, Vector3.one);
        bool first = true;
        foreach (var c in GetComponentsInChildren<Collider>())
        {
            if (first) { bounds = c.bounds; first = false; }
            else bounds.Encapsulate(c.bounds);
        }
        LevelBounds = bounds;
        bounds.Expand(new Vector3(2f, 10f, 2f));

        NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);
        NavMeshData data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
        instance = NavMesh.AddNavMeshData(data);
    }

    void OnDestroy()
    {
        if (instance.valid) NavMesh.RemoveNavMeshData(instance);
    }
}
