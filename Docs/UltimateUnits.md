# 궁극·최상위 유닛 3D 에셋 점검 (2026-11-21)

## 결론

사용자가 받은 `temp-assets/ultimate/`의 모델 1종(GLB 2개)은 이번에 게임에 넣지 않았다(아래 판정). 나머지 외부 후보 5종은 로컬에 없고, 저장소 규칙(`AGENTS.md`)이 외부 에셋 임의 다운로드를 금지한다. 요청한 네 범주는 모두 구매한 Synty POLYGON Military 프리팹으로 이미 표시되며, 원시 도형 임시물이 아니다. 이번 작업은 이 모델들의 4방향·실전장 캡처, 삼각형·재질 점검, 출처 기록이다.

## 사용자 다운로드 모델 판정 (`temp-assets/ultimate/`)

| 파일 | 크기 | 텍스처 | 삼각형 | 메시/재질 |
| --- | --- | --- | --- | --- |
| `sky_sabre_sam.glb` | 57MB | 기본색 2048(JPEG 1.3MB×2), 노멀 5MB×2 | 502,244 | 62 / 15 |
| `sky_sabre_sam_1k.glb` | 48MB | 기본색 1024(JPEG 0.6MB×2), 나머지 동일 | 502,244 | 62 / 15 |

- 출처·라이선스: GLB `asset.extras`에 작성자 Jeyhun1985, CC BY 4.0, https://sketchfab.com/3d-models/sky-sabre-sam-461cd7f020814360a2a48b4715ac792c 가 들어 있다. 출처 표기 조건으로 상업 사용이 가능하다.
- **제외 사유 1, 예산:** 50만 삼각형은 유닛 예산(4,000, 구매 팩 상한 수만)의 100배 이상이다. 1K/2K는 텍스처만 다르고 형상은 같다. 텍스처를 줄여도 정점·그리기 비용은 그대로다.
- **제외 사유 2, 가상 진영 원칙:** 이름이 실존 방공 체계 상표명이고, 텍스처에 실존 미사일·제조사 표기("CAMM-ER", "MBDA")가 인쇄돼 있다. `CLAUDE.md`는 실존 무기 상표명을 코드·에셋·문자열에 쓰지 않게 한다.
- **제외 사유 3, 도구:** 이 PC에 Blender가 없고 Unity 프로젝트에 glTF 가져오기 패키지(glTFast)가 없다. 감면·텍스처 수정·FBX 변환을 할 수 없다.
- **제외 사유 4, 화풍:** 실사풍 PBR(노멀·금속도 지도)이라 Synty 로우폴리 단색 아틀라스와 섞이면 튄다.

쓰려면: Blender(무료)를 설치해 주면 ① 6~8천 삼각형으로 감면, ② 표기 문구를 지운 1K 아틀라스 하나로 굽기, ③ 가상 이름(예: `ff_skylance_sam`)으로 `Assets/ThirdParty/UltimateUnits/`에 원본 GLB와 `LICENSE.txt`를 함께 두고 파생 프리팹만 만드는 순서로 진행한다. 게임 크레딧에 "Jeyhun1985, CC BY 4.0"을 적어야 한다. 원본 GLB(57MB)는 Git에 올리지 않는다(50MB 초과, LFS 미설정).

## 외부 후보 라이선스 확인

| 후보 | 작성자 | 라이선스 | 삼각형 | 판단 |
| --- | --- | --- | --- | --- |
| [Pantsir-S1](https://sketchfab.com/3d-models/pantsir-s1-3cb67b0bae10418190e7ce32142231e4) | SanderWolf | CC BY 4.0, 무료 | 87.6k | **제외.** 실존 무기 체계 복제라 가상 진영 원칙(실존 무기 상표명·외형 금지)에 어긋난다. 모바일 유닛 예산의 수십 배다. |
| [SciFi Missile Launcher](https://sketchfab.com/3d-models/scifi-missile-launcher-3e4608397b4a464ca5563fb94cdb4221) | Pippa (Planetrix23) | CC BY 4.0, 무료 | 9.4k | **후보.** 가상 디자인. 이동식 미사일 유닛 포탑으로 쓸 수 있다. 출처 표기 필요. 사용자가 받아 `temp-assets/`에 넣어야 한다. |
| [Missile Launcher Tank](https://opengameart.org/content/missile-launcher-tank) | 미확인 | 미확인 | 미확인 | 자동 조회가 HTTP 403으로 막혔다. 브라우저에서 라이선스(CC0/CC-BY 등)를 확인해야 한다. |
| [Low Poly Tank Assets](https://opengameart.org/content/low-poly-tank-assets-0) | 미확인 | 미확인 | 미확인 | 위와 같다. |
| [Polygon TD Turret Collection](https://opengameart.org/content/polygon-td-turret-collection-0) | 미확인 | 미확인 | 미확인 | 위와 같다. |

외부 모델을 쓰려면 사용자가 위 페이지에서 원본(FBX/GLB와 라이선스 표기)을 받아 `C:\Users\MinGyu\FactoryWars\temp-assets\ultimate\<모델명>\`에 넣는다. 그 뒤 `Assets/ThirdParty/UltimateUnits/<모델명>/`에 원본과 `LICENSE.txt`를 보존하고, 파생 프리팹만 따로 만든다. `temp-assets/`는 Git 제외 대상이다. CC BY 모델은 `Docs/Credits.md`와 게임 크레딧에 작성자와 라이선스를 적는다.

## 현재 표시 모델

| 범주 | 유닛 | 표시 프리팹 | 원본 | 라이선스 | 삼각형 | 상태 |
| --- | --- | --- | --- | --- | --- | --- |
| 초중전차 | `ff_bulwark_tank` | `Assets/FactoryWarsArt/Synty/Variants/tank_a__Land_Vehicles_04.prefab` | Synty POLYGON Military 전차 변형 | Synty 구매 라이선스 | 19,432 | 사용 중, 재질 오류 0개 |
| 초중전차 | `cf_fortress_tank` | `Assets/FactoryWarsArt/Synty/Variants/tank_b__Land_Vehicles_05.prefab` | Synty POLYGON Military 전차 변형 | Synty 구매 라이선스 | 11,739 | 사용 중, 재질 오류 0개 |
| 공격 헬기 | `ff_hawk_gunship` | `Assets/ExternalAssets/Synty/PolygonMilitary/Prefabs/Vehicles/SM_Veh_Helicopter_Attack_01.prefab` | Synty POLYGON Military | Synty 구매 라이선스 | 5,702 | 사용 중, 재질 오류 0개 |
| 공격 헬기 | `cf_gale_gunship` | `Assets/FactoryWarsArt/Synty/Variants/heli_b__Helicopters_01_C.prefab` | Synty `SM_Veh_Helicopter_Attack_02` 변형 | Synty 구매 라이선스 | 4,007 | 사용 중, 재질 오류 0개 |
| 이동식 미사일 | `ff_mobile_sam` | `Assets/FactoryWarsArt/Synty/Variants/radar_tank__Land_Vehicles_04.prefab` | Synty `SM_Veh_Radar_Tank_01` 변형 | Synty 구매 라이선스 | 8,701 | 사용 중, 재질 오류 0개 |
| 이동식 미사일 | `cf_rocket_artillery` | `Assets/FactoryWarsArt/Synty/Variants/rocket_truck__Land_Vehicles_05.prefab` | Synty `SM_Veh_Rocket_Truck_01` 변형 | Synty 구매 라이선스 | 10,314 | 사용 중, 재질 오류 0개 |
| 궁극 전력 | `ff_stormnest_carrier` | `Assets/FactoryWarsArt/Synty/Prefabs/ff_stormnest_carrier_kit.prefab` | Synty 수송기·드론 조립 프리팹 | Synty 구매 라이선스 | 28,235 | 사용 중, 재질 오류 0개 |
| 궁극 전력 | `cf_missile_silo` | `Assets/FactoryWarsArt/Synty/Prefabs/cf_missile_silo_kit.prefab` | Synty 사일로 조립 프리팹 | Synty 구매 라이선스 | 12,665 | 사용 중, 재질 오류 0개 |

삼각형은 `Assets/Tests/UltimateUnitCaptureTests.cs`가 화면에 만든 모델에서 센 값이다(선택 고리·체력바 제외). Synty 팩은 구매 라이선스이고 원본은 저장소에 올리지 않는다(`ExternalAssets` 제외 규칙).

## 캡처

`DevelopmentReports/Screenshots/UltimateUnits/`에 유닛마다 `<id>_front.png`, `_rear.png`, `_side.png`, `_iso.png`가 있다. 실제 Map4P 전장 장면은 `battlefield_ultimate_units.png`, 수치는 `ultimate_units.csv`다.

## 게임 연동 상태

- 이동·전투·선택 고리·체력바·파괴 연출은 기존 `BattleView`·`ArtView` 경로를 그대로 쓴다. Quantum 시뮬레이션과 Unity 물리는 바꾸지 않았다.
- 전자전 지원 차량은 대응하는 게임 유닛·규칙이 없어 만들지 않았다. Synty `SM_Veh_APC_Heavy_01`은 아직 쓰지 않은 차체로, 규칙이 생기면 후보가 된다.

## 남은 작업

- 실존 무기명 정리: UI 문구 "패트리어트 요격 포대"를 "광역 요격 포대"로 바꿨다. 내부 식별자(`ff/cf_patriot_turret`, `BuildingRole.PatriotTurret` 등)와 에디터 주석에는 아직 남아 있다. 저장·시험 데이터가 이 이름을 참조하므로 별도 작업으로 일괄 변경해야 한다.
- 외부 모델은 사용자가 받아 준 뒤 라이선스 파일과 함께 가져오기, LOD, 포탑 분리, 4방향 재캡처를 진행한다.
- Android 실기기 프레임은 측정하지 않았다.
