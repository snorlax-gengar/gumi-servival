using System;
using UnityEngine;

/// <summary>
/// 소음 이벤트 허브. 발소리, 공격, 루팅 등 모든 소리는 여기로 방송되고
/// 안드로이드는 반경 안의 소리를 듣고 반응한다.
/// 나중에 '전력 신호' 탐지도 같은 방식(별도 이벤트)으로 확장하면 된다.
/// </summary>
public static class NoiseSystem
{
    public static event Action<Vector3, float> OnNoise;

    public static void Emit(Vector3 position, float radius)
    {
        OnNoise?.Invoke(position, radius);
    }
}
