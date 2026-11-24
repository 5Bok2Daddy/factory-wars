# 저품질·임시 그래픽 전수 감사 (AGY Fix F, 2026-11-24)

> **감사 대상:** `VisualCatalog.asset`(총 419 슬롯), `Assets/QuantumUser/View/FactoryWars/**/*.cs`(프로시저럴 기본 도형 `CreatePrimitive`), `Game/Docs/status_images/T39·T41·T46` 최신 게임 시점 캡처.
> **원칙:** 보유 Synty 팩 원본 및 `temp-assets`는 수정 금지. 프로젝트 측 변형/래퍼 프리팹만 사용. 매치되는 보유 모델이 없는 핵심 게임플레이 오브젝트는 유지하고 아래에 구매/후속(T44) 후보로 표기.

---

## 1. 감사 요약
- **VisualCatalog 전체 슬롯:** 419개 중 **362개 채워짐 / 57개 빈 `Prefabs`**
  - 이 중 실제 Map4P 플레이에서 쓰이는 빈 슬롯(`building/ff_shipyard`, `building/cf_shipyard`, `building/ff_mlrs_battery`, `building/cf_mlrs_battery` 등)은 `SyntyAssign`이 별도 복합 프리팹/레거시 경로로 처리하거나 폴백되는 항목이므로 별도 표기.
  - `dress/*` 25개와 `unit/ff_sandbags..artillery` 14개는 옛 320m 맵·구형 건설 슬롯 잔재(현재 Map4P v4는 `dress4p/*` 및 `building/*` 사용).
- **3D 월드 프로시저럴 기본 도형 (`CreatePrimitive`):**
  - `WaterwayView.cs:50`의 **해상 수문 신호등(`GateLamp`, `PrimitiveType.Cylinder`)** 1건만 3D 월드 소품. (`FogOverlay.cs`의 전장 안개 `Quad`, `LogisticsView.cs`의 바닥/진행 바 `Quad`는 UI·오버레이 레이어이므로 유지).
- **외부 저폴리/단색 팔레트 임포트 (`ThirdParty/`·`Kenney/`):**
  - 총 8개 슬롯(전함 1, 드론 3, 헬기 2, PMC 트럭 1, 컨베이어 1). 모두 보유 Synty 팩에 동일 역할 대체 모델이 없는 핵심 유닛/설비이며, 전함·전투기·전차 계열은 **T44(사용자 GLB 시범·일괄 변환)**에서 고품질 교체 예정.

---

## 2. 항목별 감사 표

| 슬롯 / 오브젝트 | 현재 소스 | 확인 캡처 | 제안 보유 대체 (Synty 팩 / 경로) | 판정 · 조치 |
| --- | --- | --- | --- | --- |
| `WaterwayView` 수문 신호등 (`GateLamp*`) | `PrimitiveType.Cylinder` + Unlit 단색 (`WaterwayView.cs:50`) | `T46/t46b_barrier_tower.png` | `PolygonConstruction/Prefabs/Props/SM_Prop_WarningLight_01.prefab` 또는 `SM_Prop_Floodlights_01.prefab` 하우징 + 신호등 | **교체 대상 (유지 시 Synty 등주 결합 권장)** |
| `building/sea_barrier` (외곽 수로 방벽) | `FactoryWarsArt/Waterway/sea_barrier_kit.prefab` (`SM_Env_Port_Wall_01` + `Dock_Poles_01`) | `T46/t46b_barrier_tower.png` | 보유 Synty 조립체(실제 팩 모델). 단, 회색 판 느낌 완화 위해 진영 색 띠/방벽 보강 가능 | **보유 Synty 사용 중 (유지)** |
| `unit/logistics_truck` (물류 트럭) | `FactoryWarsArt/Logistics/logistics_truck_kit.prefab` (`SM_Veh_Truck_01_Canopy`) | `T41/t41_truck_loading.png` | AGY Fix A(`b1e7793`)에서 로켓 트럭 → Synty 캐노피 트럭 교체 완료 | **완료 (`b1e7793`)** |
| `Map4P` 중앙 빈 `X` 부지 (모래 바닥) | `GridPaint` 모래 폴리곤 + `GridDress` 5배 조기 컬링 | `T46/t46b_west_sector.png` | AGY Fix B2에서 63~65% `H` 시가지(PolygonCity + `SM_Bld_Shop_*_Destroyed`) + 35~37% `X` 폐허(`SM_Bld_Destroyed_01/02`) + `XC` 공사 부지(`PolygonConstruction`)로 교체 | **완료 (B2)** |
| `camp/display_tank`, `camp/display_mrap`, `MK` 북쪽 컨테이너·감시탑 지붕·위장망 | `PolygonMilitary` / `PolygonBattleRoyale` (초록 위장·빨간 튐) | `T39/t39_fixc_before_after.png`, `T39/t39_fixc_camp_wide.png` | AGY Fix C(`98abd94`)에서 `PMCM_*` 저대비 건메탈/차콜 변형으로 통일 완료 | **완료 (`98abd94`)** |
| `building/ff_shipyard`, `building/cf_shipyard` | `VisualCatalog` `Prefabs` 비어 있음 (코드 폴백) | `T46/t46b_canal_shipyard_tooltip.png` | `PolygonBattleRoyale/Prefabs/Environment/SM_Env_Dock_01.prefab` + `SM_Bld_Crane_01.prefab` 래퍼 | **후속 슬롯 채우기 대상** |
| `building/ff_mlrs_battery`, `building/cf_mlrs_battery`, `building/MLRS_Battery` | `VisualCatalog` `Prefabs` 비어 있음 (전용 `SyntyAssign` 조립) | `MLRS/completed_battery.png` | `PolygonMilitary/Prefabs/Vehicles/SM_Veh_Rocket_Truck_01.prefab` + 방벽 래퍼 | **기존 전용 조립 동작 확인됨 (슬롯 정리 권장)** |
| `unit/ff_sortie_fighter/striker/transport`, `unit/cf_sortie_*` (6종) | `VisualCatalog` `Prefabs` 비어 있음 | — | **no owned match** (Synty 팩에 현대 제트 전투기/수송기 없음) → **T44** `f-16cm`, `fa-18e`, `f-117`, `boeing_c-17b` CC BY GLB 적용 예정 | **게임플레이 핵심 · T44 교체 대기** |
| `unit/cf_artillery_ship` | `ThirdParty/CfIronwallBattleship/cf_ironwall_battleship.prefab` (CC BY decimated) | `T46/t46b_barrier_tower.png` | **no owned match** (Synty에 현대 구축함 없음) → **T44** `south_korean_destroyer_chung_mu` CC BY GLB 검토 예정 | **게임플레이 핵심 · T44 교체 대기** |
| `unit/ff_nest_drone`, `unit/shared_scout_drone`, `unit/pmc_attack_drone` | `ThirdParty/SharedScoutDrone`, `pmc_loiter_drone` (CC BY decimated) | `T39/t39_fixc_close_apc.png` | **no owned match** (Synty에 현대 정찰/자폭 드론 없음) | **게임플레이 핵심 · 유지 (구매/T44 후보)** |
| `unit/pmc_attack_heli`, `unit/pmc_scout_heli`, `unit/merc_cargo_truck` | `ThirdParty` CC BY GLB (`apache_1`, `mh-6_little_bird`, `military_truck`) | `T39/t39_v6_close_heli.png` | **no owned match** (실척 아파치/리틀버드/PMC 트럭, 시각 품질 양호) | **유지 (T39 검수 통과)** |
| `fx/extractor_conveyor` | `ExternalAssets/Kenney/factory-kit/conveyor-long-stripe-sides.fbx` | `current_factory_complex.png` | **no owned match** (Synty에 산업 컨베이어 벨트 없음) | **유지 (구매 후보)** |
| `dress/*` (25개), `unit/ff_sandbags..artillery` (14개) | `VisualCatalog` `Prefabs` 비어 있음 (구 320m 맵 레거시 슬롯) | — | Map4P v4에서는 미사용(`dress4p/*` 84종이 대체) | **미사용 레거시 슬롯 (삭제 보류·참조 확인 후 정리)** |

---

## 3. 구매 / T44 후속 교체 후보 (보유 Synty 팩에 대체 모델 없음)
1. **현대 제트기·수송기 4종** (`ff/cf_sortie_*`): 보유 `temp-assets/bridge/` CC BY GLB(`f-16cm`, `fa-18e`, `f-117`, `boeing_c-17b`)를 **T44**에서 감축 적용.
2. **현대 구축함** (`cf_artillery_ship`): `temp-assets/bridge/` CC BY GLB(`south_korean_destroyer_chung_mu`)를 **T44**에서 시범 변환 및 아치교(8m) 통과 검증.
3. **산업 컨베이어·소형 정찰/자폭 드론**: Synty 팩에 동등 모델 부재 → 현행 Kenney/CC BY 모델 유지 또는 별도 현대 산업/드론 팩 구매 후보.
