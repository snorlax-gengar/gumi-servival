using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 게임 시작 시 이 오브젝트의 자식(바닥, 건물, 선반)으로 NavMesh를 굽는다.
/// 별도 패키지나 Bake 버튼 없이 안드로이드가 길을 찾을 수 있게 해 준다.
/// 다른 스크립트보다 먼저 실행되어야 하므로 실행 순서를 앞당겼다.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class LevelNavMesh : MonoBehaviour
{
    public Vector3 boundsSize = new Vector3(200f, 30f, 200f);

    NavMeshDataInstance instance;

    void Awake()
    {
        var sources = new List<NavMeshBuildSource>();
        NavMeshBuilder.CollectSources(transform, ~0, NavMeshCollectGeometry.PhysicsColliders, 0,
            new List<NavMeshBuildMarkup>(), sources);

        NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);
        var bounds = new Bounds(Vector3.zero, boundsSize);
        NavMeshData data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
        instance = NavMesh.AddNavMeshData(data);
    }

    void OnDestroy()
    {
        if (instance.valid) NavMesh.RemoveNavMeshData(instance);
    }
}
