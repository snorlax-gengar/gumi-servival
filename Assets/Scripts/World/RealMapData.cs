using System;
using UnityEngine;

/// <summary>
/// 실제 구미 지도 데이터 (OpenStreetMap에서 변환). Resources/Map/gumi_core.json.
/// 좌표는 미터, x = 동쪽, z = 북쪽, 원점 = 지도 중심 (구미역 북서쪽 원도심).
/// 변환 스크립트: Tools/osm2map/ (Python).  지도 데이터 © OpenStreetMap contributors (ODbL)
/// </summary>
[Serializable]
public class RealMapData
{
    public string name;
    public string attribution;
    public float lat0, lon0;
    public float minX, maxX, minZ, maxZ;
    public MapRoad[] roads;
    public MapBuilding[] buildings;
    public MapArea[] areas;
    public MapLine[] lines;
    public MapPoi[] pois;
    public MapSafehouse safehouse;
    /// <summary>나무: x, z, 종류(0 숲, 1 작은 숲, 2 공원, 3 가로수) 반복</summary>
    public float[] trees;

    public static RealMapData Load(string resourcePath)
    {
        var ta = Resources.Load<TextAsset>(resourcePath);
        if (ta == null) return null;
        try { return JsonUtility.FromJson<RealMapData>(ta.text); }
        catch (Exception e) { Debug.LogError("[Gumi] 지도 데이터 읽기 실패: " + e.Message); return null; }
    }
}

[Serializable]
public class MapRoad
{
    public string t;    // highway 종류 (residential, tertiary ...)
    public float w;     // 폭 (m)
    public string n;    // 도로명
    public int b, tu;   // 다리, 터널
    public float[] p;   // x,z,x,z...
    public float[] wv;  // 꼭짓점별 폭 (건물에 닿는 구간은 좁힘). 없으면 w
}

[Serializable]
public class MapBuilding
{
    public string k;    // apt, villa, house, shop, gen, school, public, roof, church
    public int lv;      // 층수
    public string n;    // 이름
    public string a;    // 용도 (amenity/shop)
    public int f;       // 1 = 지도에 없어서 채워 넣은 건물
    public int safe;    // 1 = 플레이어 거점 자리
    public float[] p;   // 외곽선 (반시계), x,z...
    public MapShop[] shops;   // 소상공인 상가정보: 이 건물에 든 실제 가게들
}

/// <summary>소상공인시장진흥공단 상가(상권)정보 한 줄 (공공데이터)</summary>
[Serializable]
public class MapShop
{
    public string n;    // 상호명
    public string c;    // 업종 소분류 (편의점, 카페, 약국 ...)
    public string m;    // 업종 대분류 (소매, 음식, 교육 ...)
    public int f;       // 층 (0 = 모름 → 1층으로 본다, -1 = 지하)
}

[Serializable]
public class MapArea
{
    public string k;    // water, wood, park, grass, pitch, parking, schoolyard, farm, plaza, railyard, platform
    public string n;
    public float[] p;
}

[Serializable]
public class MapLine
{
    public string k;    // water, rail
    public float w;
    public string n;
    public float[] p;
}

[Serializable]
public class MapPoi
{
    public string k;    // restaurant, cafe, convenience, fuel ...
    public string n;
    public float x, z;
}

[Serializable]
public class MapSafehouse
{
    public float x, z, yaw, w, d, rx, rz;
}
