# 환경 아트 개편

## 현황 조사

지면은 URP의 `FactoryWars/KenneyLit`를 통해 정점 색상, 전장 손상, 아스팔트 노멀을 읽는다. 지면 격자의 색 차이가 넓은 평지에서 드러나고, 기초판 둘레의 단일 테두리가 포장과 흙 사이에 시각적인 경계를 만든다. 건물 역할별 장식과 밀도 설정은 이미 있으므로 여기에 연결한다.

| 항목 | 로컬에서 확인한 상태 |
| --- | --- |
| Synty POLYGON | `Assets/ExternalAssets/Synty/`에 Military, City, Construction, Battle Royale, Coffee Shop 확인 |
| 선별 외부 환경 | `Assets/ThirdParty/NewBundle/`에 Bridges, Desert, Woodland, Meadow, Generated 폴더 확인. 원본 EGG 32개는 작업 루트 `temp-assets/new bundle/`, 추출본은 `temp-asset/new bundle/_extracted/`에 있다. |
| 작업자 | `Assets/ThirdParty/MixamoAnimations/` 및 Synty 작업자 슬롯, 기존 View 경로 공유 |
| 지형 재질 | Kenney 팔레트, URP 지면 셰이더, 아스팔트 및 사막 노멀, 기존 밀도 단계 |
| Creator Bundle Volume 1 | Unity 프로젝트 안에 이 제품명으로 식별되는 임포트 폴더 또는 로컬 매니페스트가 없다. |
| Nature Megapack | 추출본에 `ModulaCraftSwampBuilderNatureMEGAPack` Unity 패키지가 있다. 이전에 배제하기로 한 늪지 테마이며 Unity 프로젝트에는 들여오지 않았다. |

## 구현

- `EnvironmentArt.cs`는 기지 자갈, 초지, 노출 토양, 자원 노드 주변의 색을 기존 지면 메시의 정점 색상으로 혼합한다. 거리와 노이즈를 사용해 재질 경계가 서서히 바뀐다. 기지 평지와 도로를 제외한 구역에는 작은 화면상 높이 차이를 줬다. 지형 높이 연출은 Unity View에서만 사용한다.
- `EnvironmentRoads.cs`는 구운 도로 조각의 접점을 찾아 접선을 맞춘다. 아스팔트, 흙길, 콘크리트 서비스 동선, 자갈 갓길, 차선과 바퀴 자국을 같은 배치에서 그린다. 교차 구간은 차선과 자국을 줄여 우선 통과선을 읽기 쉽게 했다.
- `FoundationView.cs`의 기초판은 기존 건설 부지를 따른다. 콘크리트 끝의 좁은 흙띠를 바닥 색과 섞고 공장, 보급창, 포대에 작업 표시를 둔다. 도로에 붙은 건물에서는 가장자리 확장분을 줄인다. 건설 중복 바닥판도 가려 둔다.
- 표준 맵에서 식생을 군락으로 배치한다. 전장 오브젝트와 도로의 간격은 기존 `Dressing` 및 배치 경계 검사로 유지한다. 협곡 바위, 수변 암벽, NatureForge 숲, Synty 공장·창고 적재물을 재사용했다.
- 기존 URP, 지면 노멀, 저사양 세부 표현 제한과 인스턴싱을 유지했다. 새 식생에 맞춰 직사광과 그림자 대비를 낮추고, 바닥과 도로 및 경계 능선이 같은 광원에 반응하도록 했다. 환경에 별도 실시간 조명과 반사 프로브를 추가하지 않았다.

## 전후 화면

변경 전은 Codex 작업 커밋 `1470cf5` 상태, 변경 후는 참고 맵 반영 커밋 `a5543be` 상태다. 같은 `Map4P` 경기 시나리오와 카메라 좌표로 GPU에서 찍었다.

| 화면 | 변경 전 | 변경 후 |
| --- | --- | --- |
| 표준 맵 전체 | [보기](status_images/EnvironmentOverhaul/Before/map_standard.png) | [보기](status_images/EnvironmentOverhaul/After/map_standard.png) |
| 사막 맵 전체 | [보기](status_images/EnvironmentOverhaul/Before/map_desert.png) | [보기](status_images/EnvironmentOverhaul/After/map_desert.png) |
| 다도해 맵 전체 | [보기](status_images/EnvironmentOverhaul/Before/map_archipelago.png) | [보기](status_images/EnvironmentOverhaul/After/map_archipelago.png) |
| 해협 맵 전체 | [보기](status_images/EnvironmentOverhaul/Before/map_strait.png) | [보기](status_images/EnvironmentOverhaul/After/map_strait.png) |
| 시가지 맵 전체 | [보기](status_images/EnvironmentOverhaul/Before/map_urban.png) | [보기](status_images/EnvironmentOverhaul/After/map_urban.png) |
| 사령부 | [보기](status_images/EnvironmentOverhaul/Before/02_headquarters.png) | [보기](status_images/EnvironmentOverhaul/After/02_headquarters.png) |
| 산업 구역 | [보기](status_images/EnvironmentOverhaul/Before/03_industrial_compound.png) | [보기](status_images/EnvironmentOverhaul/After/03_industrial_compound.png) |
| 공장 기초판 | [보기](status_images/EnvironmentOverhaul/Before/04_factory_foundation.png) | [보기](status_images/EnvironmentOverhaul/After/04_factory_foundation.png) |
| 도로와 교차로 | [보기](status_images/EnvironmentOverhaul/Before/05_road_intersection.png) | [보기](status_images/EnvironmentOverhaul/After/05_road_intersection.png) |
| 숲과 식생 | [보기](status_images/EnvironmentOverhaul/Before/06_forest_edge.png) | [보기](status_images/EnvironmentOverhaul/After/06_forest_edge.png) |
| 암벽 | [보기](status_images/EnvironmentOverhaul/Before/07_rocky_ridge.png) | [보기](status_images/EnvironmentOverhaul/After/07_rocky_ridge.png) |
| 자원 채취 | [보기](status_images/EnvironmentOverhaul/Before/08_resource_extraction.png) | [보기](status_images/EnvironmentOverhaul/After/08_resource_extraction.png) |
| 다연장 로켓 포대 | [보기](status_images/EnvironmentOverhaul/Before/09_mlrs_installation.png) | [보기](status_images/EnvironmentOverhaul/After/09_mlrs_installation.png) |
| 이동 중인 전차와 보병 | [보기](status_images/EnvironmentOverhaul/Before/10_units_in_motion.png) | [보기](status_images/EnvironmentOverhaul/After/10_units_in_motion.png) |

## 인수 후 보정 (2026-11-21, Claude)

- 표준 맵 전체 화면에서 통과 불가 대지(`TerrainKind.Hill`)의 짙은 올리브 단색 판이 밝아진 초지 위에서 위장 무늬 얼룩처럼 보였다. 환경 세부 표현이 켜진 맵에서는 `TerrainLook.CityLot`이 이 판을 깔지 않고 정점 색 지면을 그대로 보인다. 대지 위 건물과 시뮬레이션 판정은 바뀌지 않는다. 흙색(`Swatch.Dirt`) 판도 시험했으나 대비가 더 커서 쓰지 않았다.
- 통과 불가 대지는 판 대신 숲 군락으로 그린다. 표준 맵은 NatureForge `WoodlandTree`·`WoodlandTallTree`·`MeadowShrub`, 나머지 맵은 Synty 침엽수·활엽수·덤불을 쓴다. 시가전은 건물을 유지한다.
- Codex의 연속 도로가 조각별 루프를 건너뛰면서 길가 소품(가로등·표지·엄폐·도로 엄폐물)이 0개가 됐다. 도로 메시만 대체하고 소품 루프는 유지한다. 곡선 도로는 4.8m 조각이 자리별로 번갈아 저장되므로 끝점이 이어지는 조각끼리 묶어 16m·12m 간격을 잰다.

## 남은 문제

| 항목 | 상태 |
| --- | --- |
| 도로 끝 | 폭이 다른 도로가 만나거나 흙길·아스팔트가 바뀌는 지점에서 끝이 각지게 잘린다(`10_units_in_motion`, `07_rocky_ridge`). |
| 다도해 | 해로 굴곡을 넣었지만 맵 가장자리 쪽 섬 모서리는 암벽 띠 안에서 직선으로 남는다. |
| 외곽 암벽 | 바위 덩어리를 일정 간격으로 놓은 띠라 참고 맵의 연속된 절벽보다 성기다. |
| 표준 맵 대칭 | 자리 공정성을 위해 4방향 회전 대칭을 유지한다. 참고 맵처럼 사분면마다 다른 장식은 넣지 않았다. |
| 성능 | 데스크톱 GPU 캡처만 했다. Android 프레임·발열은 측정하지 않았다. |
