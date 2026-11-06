using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mixamo(어도비) 캐릭터·동작. Assets/Resources/Mixamo/ 에 둔다 (Editor/MixamoImporter가 휴머노이드로 가져옴).
///   Player.fbx  = 플레이어 (사람, With Skin)
///   Android.fbx = 안드로이드 (X Bot, With Skin)
///   Idle / Walk / Run / CrouchWalk / AttackMelee / AttackBat / Dying / MutantPunch / HitReaction .fbx = 동작 (Without Skin)
/// 휴머노이드라서 어떤 동작이든 두 캐릭터에 모두 맞는다.
/// Mixamo 약관상 원본 파일을 공개 저장소에 올리지 않는다 (.gitignore). 다른 PC에서는 Tools/mixamo/README.md 대로 다시 받는다.
/// 파일이 없으면 기존 Quaternius 모델을 쓴다.
/// </summary>
public static class MixamoLib
{
    public const string Root = "Mixamo/";
    static readonly string[] AnimFiles = { "Idle", "Walk", "Run", "CrouchWalk", "AttackMelee", "AttackBat", "Dying", "MutantPunch", "HitReaction",
                                           "Jump", "Fall", "Vault", "Climb" };
    static AnimationClip[] cache;

    public static bool HasModel(string name) => AssetLib.Has(Root + name);

    public static AnimationClip[] Clips()
    {
        if (cache != null && cache.Length > 0) return cache;
        var list = new List<AnimationClip>();
        foreach (var f in AnimFiles)
            foreach (var c in AssetLib.Clips(Root + f))
                if (c != null && !c.name.StartsWith("__preview__")) list.Add(c);
        cache = list.ToArray();
        return cache;
    }

    /// <summary>T자세(처음 만든 직후) 기준으로 뼈에 장식을 붙인다. 월드 좌표로 놓은 뒤 뼈의 자식으로 옮긴다.</summary>
    public static GameObject Attach(Transform bone, string name, Vector3 worldPos, Quaternion worldRot, Vector3 size, Color c, float emission)
    {
        var holder = new GameObject(name).transform;
        holder.SetPositionAndRotation(worldPos, worldRot);
        var box = Kit.Box(holder, Vector3.zero, size, c, emission: emission, name: name);
        box.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        holder.SetParent(bone, true);
        return box;
    }
}
