# 실제 구미 지도 변환 (OpenStreetMap → 게임)

1. Overpass로 범위 데이터를 받는다 (`out geom` JSON) → `gumi_core.json`
2. 변환:
   ```
   python osm2map.py gumi_core.json map.json        # 좌표 변환·도로·건물·면 정리
   python fill.py map.json map_full.json            # 지도에 없는 골목 빌라 채우기 + 거점 선택
   python ground.py map_full.json out.json gumi_ground.png   # 바닥 텍스처 + 나무 위치
   ```
3. `out.json`을 `Assets/Resources/Map/gumi_core.json`으로, `gumi_ground.png`를 같은 폴더에 둔다.

범위: 위도 36.118~36.138, 경도 128.317~128.342 (구미역 일대 원도심). 원점 = 36.128, 128.3295.
지도 데이터 © OpenStreetMap contributors, ODbL 1.0 (https://www.openstreetmap.org/copyright)
