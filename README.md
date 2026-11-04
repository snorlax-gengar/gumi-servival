# 구미 생존게임 — 프로토타입 0.1

목표: **원룸촌 한 블록에서 혼자 살아남기.** 15분 플레이가 긴장감 있는지 확인하는 것이 이 단계의 전부입니다.

## 들어 있는 것

| 파일 | 역할 |
| --- | --- |
| `Player/PlayerController.cs` | WASD 이동, Shift 달리기, Ctrl 웅크리기, 마우스 방향 바라보기, 발소리 소음 |
| `Player/IsometricCamera.cs` | 쿼터뷰 추적 카메라, 마우스 휠 줌 |
| `Player/PlayerStats.cs` | 체력·배고픔·갈증·스태미나 |
| `Player/Interactor.cs` | 가까운 물체와 F키 상호작용 |
| `Items/ItemData.cs` | 아이템 정의 (ScriptableObject) |
| `Items/Inventory.cs` | 무게 기반 인벤토리, 숫자키로 먹기/장착 |
| `Items/LootContainer.cs` | 뒤질 수 있는 선반·냉장고·공구함, 랜덤 루팅 |
| `Combat/PlayerCombat.cs` | 좌클릭 근접 공격 (무기 또는 맨손) |
| `Combat/Damageable.cs` | 안드로이드 체력 |
| `Android/AndroidAI.cs` | 작업형 안드로이드: 순찰 → 소리 조사 → 시야 추적 → 공격 |
| `Core/NoiseSystem.cs` | 모든 소음을 방송하는 허브 |
| `Core/GameManager.cs` | 게임 내 시간(실제 1분 = 1시간), 낮밤, 재시작 |
| `UI/HUD.cs` | 상태 바, 시계, 인벤토리, 사망 화면 (임시 UI) |
| `UI/MinimapHUD.cs` | 미니맵, 전체 지도(M), 나침반, 조작 안내 |
| `World/CityGenerator.cs` | 플레이마다 새로 생성되는 구미 시가지 |

## 조작

| 키 | 동작 |
| --- | --- |
| WASD | 이동 |
| Shift | 달리기 (스태미나 소모, 소음 큼) |
| Ctrl | 웅크리기 (느림, 소음·발각 거리 감소) |
| 마우스 | 바라보는 방향 |
| 좌클릭 | 공격 |
| F | 뒤지기 |
| Q / E | 카메라 90° 회전 |
| 우클릭 드래그 | 카메라 자유 회전·기울기 |
| M | 전체 지도 |
| F9 | 플레이 녹화 시작/정지 (Recordings 폴더) |

**기습**: 눈치채지 못한 안드로이드의 등 뒤에서 공격하면 피해 2.5배 + 2초 기절, **웅크린 채로** 공격하면 4배(렌치 한 방).
머리 위에 `◆ 기습 가능` / `◆ 급소 노출`이 뜨면 기회다. 서서 걸으면 1.5m 안에서 기척을 들킨다.
| 1~9 | 아이템 사용/무기 장착 |
| 휠 | 줌 |
| R | 사망 후 재시작 |

---

## 설치 (약 30분)

1. **Unity Hub** 설치 → unity.com에서 다운로드
2. Hub에서 **Unity 6 LTS** 버전 설치 (모듈은 기본값이면 충분)
3. Hub → New project → **Universal 3D** 템플릿 → 이름 `GumiSurvival`
4. 이 저장소를 클론한 폴더를 Unity Hub → **Add**로 열기 (씬과 아이템은 이미 들어 있음: `Assets/Scenes/Gumi_Prototype`)
5. **Window → Package Manager → Unity Registry**에서 두 패키지 확인/설치
   - **Input System** (보통 이미 설치됨. 재시작을 묻으면 Yes)
   - **AI Navigation** (NavMesh 굽기에 필요)
6. Console 창(Window → General → Console)에 빨간 에러가 없으면 준비 완료

> Unity 6 이전 버전을 쓴다면 Edit → Project Settings → Player → Active Input Handling을 **Input System Package (New)** 또는 **Both**로 설정하세요.

---

## 씬 만들기 (약 1시간)

처음에는 전부 기본 도형(큐브, 캡슐)으로 만드는 **그레이박스**입니다. 모양보다 재미가 먼저입니다.

### 1. 바닥과 건물

1. Hierarchy 우클릭 → 3D Object → **Plane** → 이름 `Ground`, Scale `(6, 1, 6)` → 60m × 60m
2. **Cube**로 건물 5~6채 배치. 예: Scale `(8, 6, 10)`. 사이사이에 골목을 남겨 두기
3. 건물 하나는 **편의점**: 큐브 4개로 벽을 세우고 한쪽에 입구(틈)를 둠
4. 건물들을 선택해 Inspector 우측 상단 **Static** 체크

### 2. 플레이어

1. **Create Empty** → 이름 `Player`, Position `(0, 0.1, 0)` (피벗이 발밑에 오도록 빈 오브젝트를 부모로 씀)
2. Inspector 상단 **Tag → Player** 지정 (중요: 안드로이드와 매니저가 이 태그로 찾음)
3. Player의 자식으로 3D Object → **Capsule** → 이름 `Body`, Position `(0, 0.9, 0)`, Scale `(1, 0.9, 1)` → Body의 **Capsule Collider 제거** (CharacterController가 대신함)
4. `Player`(부모)에 Add Component로 아래를 붙임
   - `Character Controller` (Height 1.8, Center Y 0.9)
   - `PlayerController`, `PlayerStats`, `Inventory`, `Interactor`, `PlayerCombat`, `HUD`
5. 앞쪽 표시용으로 작은 Cube를 Player 자식으로 넣고 Position `(0, 1.5, 0.5)`, Scale `(0.3, 0.2, 0.4)` → 이 Cube의 Collider도 제거

### 3. 카메라와 시간

1. **Main Camera**에 `IsometricCamera` 추가 → Target에 `Player` 드래그
2. 빈 오브젝트 생성(Create Empty) → 이름 `GameManager` → `GameManager` 추가 → Sun에 **Directional Light** 드래그

### 4. 아이템 만들기

Project 창에서 `Assets/Items` 폴더 생성 → 우클릭 → **Create → Gumi → Item**

| 파일 이름 | Display Name | Type | Weight | 효과 |
| --- | --- | --- | --- | --- |
| Item_Kimbap | 삼각김밥 | Food | 0.2 | Hunger Restore 25 |
| Item_Ramen | 컵라면 | Food | 0.3 | Hunger 35, Thirst -5 |
| Item_Water | 생수 | Drink | 0.5 | Thirst Restore 40 |
| Item_Bandage | 붕대 | Medical | 0.1 | Health Restore 20 |
| Item_Wrench | 렌치 | Weapon | 1.2 | Damage 25, Range 1.8, Cooldown 0.8, Noise 8 |
| Item_Pipe | 쇠파이프 | Weapon | 2.0 | Damage 35, Range 2.2, Cooldown 1.1, Noise 10 |

### 5. 뒤질 수 있는 컨테이너

1. 편의점 안에 Cube로 선반 2~3개 → 각각 `LootContainer` 추가
2. Container Name `편의점 선반`, Loot Table에 항목 추가 (예: 삼각김밥 Chance 0.6 Max 2, 생수 0.5 Max 2)
3. 원룸 건물 앞에 공구함 하나 → Fixed Items에 `렌치` (첫 무기를 확실히 주기 위해)
4. 나머지 건물에도 냉장고, 쓰레기통 등을 흩뿌리기 (확률 낮게)

### 6. 길찾기 굽기 (NavMesh)

1. `Ground` 선택 → Add Component → **NavMesh Surface**
2. **Bake** 클릭 → 바닥에 파란 영역이 생기면 성공. 건물 자리는 비어 있어야 함

### 7. 안드로이드

1. Capsule 생성 → 이름 `Android_Worker`, Scale `(1.2, 1.1, 1.2)`, 회색 머티리얼
2. `AndroidAI` 추가 → NavMeshAgent와 Damageable이 자동으로 붙음
3. Damageable Max Health `100` (렌치 4대)
4. 순찰 지점: 빈 오브젝트 3~4개(`WP1`…)를 골목에 배치 → AndroidAI의 Waypoints에 드래그
5. 마음에 들면 Project 창으로 드래그해 **프리팹**으로 만든 뒤 2~3기 배치

### 8. 플레이

▶ 버튼. 체크할 것:

- [ ] 걷기/달리기/웅크리기 소음 차이가 느껴지는가 (안드로이드 선택 시 Scene 뷰에 시야 표시)
- [ ] 달리면 따돌릴 수 있지만 스태미나가 바닥나면 위험한가
- [ ] 편의점까지 가는 길이 긴장되는가
- [ ] 싸우면 소리 때문에 다른 안드로이드가 몰려오는가
- [ ] 배고픔·갈증 때문에 '나가야만 하는' 압박이 생기는가

---

## 튜닝 포인트

| 느낌 | 조정할 값 |
| --- | --- |
| 너무 쉽다 | AndroidAI `chaseSpeed` 3.2 → 3.6, `viewDistance` 12 → 15 |
| 너무 어렵다 | PlayerController `walkNoise` 6 → 4, AndroidAI `attackDamage` 18 → 12 |
| 배고픔 압박이 약하다 | PlayerStats `hungerPerHour` 4 → 6 |
| 하루가 너무 길다 | GameManager `realSecondsPerGameHour` 60 → 30 |

## 자주 막히는 곳

- **한글이 □□로 보임** → 한글 폰트(.ttf, 예: 나눔고딕)를 Assets에 넣고 HUD의 Korean Font에 연결
- **안드로이드가 안 움직임** → NavMesh를 구웠는지, 안드로이드가 파란 영역 위에 있는지 확인
- **`InvalidOperationException: You are trying to read Input`** → Active Input Handling 설정 확인 (설치 5번 아래 참고)
- **F키가 안 먹음** → 컨테이너에 Collider가 있는지, 2m 안에 있는지 확인
- **플레이어가 바닥으로 떨어짐** → Player Position Y를 1 이상으로

## 다음 단계 (프로토타입 0.2)

1. 전력 신호 탐지 (발전기 오브젝트 + `PowerSignalSystem`, NoiseSystem과 같은 구조)
2. 낮밤에 따른 안드로이드 충전 귀환
3. 저장/불러오기
4. 실제 원룸촌 느낌의 로우폴리 에셋으로 교체
