using UnityEngine;

/// <summary>
/// 건물 하나의 렌더러 묶음. 카메라가 플레이어를 가리는 건물을 숨길 때
/// 벽만 사라지고 창문·간판이 떠 있지 않도록 통째로 숨긴다.
/// </summary>
public class OccluderGroup : MonoBehaviour
{
    Renderer[] renderers;
    public Renderer[] Renderers
    {
        get
        {
            if (renderers == null) renderers = GetComponentsInChildren<Renderer>();
            return renderers;
        }
    }
}
