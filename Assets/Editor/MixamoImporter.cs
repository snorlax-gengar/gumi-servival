using UnityEditor;

/// <summary>
/// Assets/Resources/Mixamo/ 의 FBX를 휴머노이드로 가져온다.
/// - 아바타는 각 파일에서 만든다 (Mixamo 뼈 이름은 자동 매핑됨)
/// - 클립 이름 = 파일 이름 (Mixamo는 전부 "mixamo.com"이라 구분이 안 됨)
/// - 서기/걷기/달리기/웅크려걷기는 반복. 회전·높이는 자세에 굽고, 앞뒤 이동은 루트로 빼서
///   (루트 모션을 쓰지 않으니) 제자리에서 걷는다 → In Place로 안 받았어도 미끄러지지 않는다.
/// </summary>
public class MixamoImporter : AssetPostprocessor
{
    const string Folder = "/Resources/Mixamo/";
    static readonly string[] Loops = { "Idle", "Walk", "Run", "CrouchWalk", "Fall", "CrouchIdle" };

    // 점프·넘기·기어오르기: 몸이 위로 솟는 높이는 버린다 (높이는 PlayerController가 직접 움직임 → 두 배로 뜨지 않게)
    static readonly string[] Traverse = { "Jump", "Climb", "Hurdle", "Drop", "Vault", "JumpA", "JumpB" };

    static bool IsMixamo(string p) => p.Replace('\\', '/').Contains(Folder);

    public override uint GetVersion() => 9;

    // 캐릭터에서 뽑아낸 텍스처: 노멀맵은 노멀맵으로
    void OnPreprocessTexture()
    {
        if (!IsMixamo(assetPath)) return;
        var ti = (TextureImporter)assetImporter;
        if (assetPath.Contains("_Normal")) ti.textureType = TextureImporterType.NormalMap;
        if (assetPath.Contains("DiffuseA")) ti.alphaIsTransparency = true;
        ti.maxTextureSize = 2048;
    }

    void OnPreprocessModel()
    {
        if (!IsMixamo(assetPath)) return;
        var mi = (ModelImporter)assetImporter;
        mi.animationType = ModelImporterAnimationType.Human;
        mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        mi.importAnimation = true;
    }

    void OnPreprocessAnimation()
    {
        if (!IsMixamo(assetPath)) return;
        var mi = (ModelImporter)assetImporter;
        string name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
        bool loop = System.Array.IndexOf(Loops, name) >= 0;
        var clips = mi.defaultClipAnimations;
        for (int i = 0; i < clips.Length; i++)
        {
            clips[i].name = clips.Length == 1 ? name : name + "_" + i;
            clips[i].loopTime = loop;
            clips[i].lockRootRotation = true;
            clips[i].keepOriginalOrientation = true;
            clips[i].lockRootHeightY = System.Array.IndexOf(Traverse, name) < 0;
            // 높이는 '발' 기준: 다른 체형(Y Bot 동작 → Remy)에 입혀도 발이 땅에 붙는다 (원본 기준이면 공중에 뜸)
            clips[i].keepOriginalPositionY = false;
            clips[i].heightFromFeet = true;
            clips[i].lockRootPositionXZ = false;
        }
        mi.clipAnimations = clips;
    }
}
