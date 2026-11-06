# 실제 구미 지도 변환 (OpenStreetMap → 게임)

1. Overpass로 범위 데이터를 받는다 (`out geom` JSON) → `gumi_core.json`
2. 변환:
   ```
   python osm2map.py gumi_core.json map.json        # 좌표 변환·도로·건물·면 정리
   python shops.py map.json gumi_shops.csv map_shops.json   # 소상공인 상가정보: 실제 상호·층수 (없는 건물은 새로 세움)
   python roadfit.py map_shops.json map_fit.json    # 건물에 닿는 도로 구간 폭 줄이기
   python fill.py map_fit.json map_full.json        # 지도에 없는 골목 빌라 채우기 + 거점 선택
   python ground.py map_full.json out.json gumi_ground.png   # 바닥 텍스처 + 나무 위치
   ```
3. `out.json`을 `Assets/Resources/Map/gumi_core.json`으로, `gumi_ground.png`를 같은 폴더에 둔다.

받은 데이터 범위: 위도 36.118~36.138, 경도 128.317~128.342.
게임 지도: 구미역 동쪽 상권 1km × 1km (중심 36.128, 128.33311 — 상가가 가장 빽빽한 곳). 바꾸려면
`python osm2map.py gumi_core.json map.json <위도> <경도> <반경m>`
지도 데이터 © OpenStreetMap contributors, ODbL 1.0 (https://www.openstreetmap.org/copyright)

상가 정보: 소상공인시장진흥공단_상가(상권)정보 (공공데이터포털). `gumi_shops.csv`는 경북 CSV에서 구미시·지도 범위만 뽑은 것.
