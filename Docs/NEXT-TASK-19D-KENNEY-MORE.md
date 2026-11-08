# 항목 19D — 받아 둔 Kenney 키트 추가 활용

시작 조건: 19C가 push된 뒤에 한다. 19C의 색 재도색(colormap_military)·KenneyLit·인스턴싱·성능 기준을 그대로 따른다. 화면(View) 전용이고 판정은 바꾸지 않는다.

> **2026-11-08 변경:** 2인 맵은 없앤다. 맵은 4인 맵 하나이고, 2인 대전은 그 맵의 대각선 두 자리로 한다(NEXT-TASK-17B-ONE-MAP.md). 이 파일의 "2인 맵" 언급은 "4인 맵 2명 대각선"으로 읽는다.

19C에서 "쓰지 않음"으로 뺀 키트 중 **전장에 맞는 부품만 골라** 쓴다. 키트별 접촉 시트(`screenshots/kenney/<kit>_contact.png`)를 먼저 만들고, 고른 모델과 버린 모델·이유를 LOOKDEV.md "19D"에 적는다.

| 키트 | 쓸 것 | 어디에 | 쓰지 않을 것 |
| --- | --- | --- | --- |
| building-kit | border-high*(높은 콘크리트 방벽), barricade-window-*·barricade-doorway-*, plating*, roof-flat-*, gutter-*, stairs-* | 본진 외곽 방벽·검문소, 진지 건물 창·문 막음, 폐허 단면(벗겨진 지붕·철판), 건물 옆 배관 | - |
| city-kit-suburban | building-type-*(1~2층), fence*, driveway-*, path-stones-*, planter, tree-large/small | 맵 가장자리 주거지·외곽 마을. 폐허 단계 적용 | - |
| modular-buildings | building-sample-house-*, 창·문·모서리 조각 | 중형 도시 건물 조립 변종. 폐허 단면 조립 | building-sample-tower-*(고층 금지) |
| furniture-kit | cardboardBox*, desk, bookcase*, chair*, bench, kitchenFridge 등 소수 | **무너진 건물 안이 드러난 폐허 내부 잔해**(지붕이 날아간 건물 속), 길가 버려진 물건. high에서만 | 욕실·침실 가구, 곰 인형(bear) 등 |
| space-station-kit | structure*, structure-barrier, rail*, container*, skip·skip-rocks, pipe* | 자유 연방 시설 외곽 구조물, 고철 하치장(skip-rocks), 철 결정 주변 장비 | bed*, chair*, computer*(실내) |
| modular-space-kit | gate*, cables, template-floor-detail*, gate-lasers | 연구소 정문, 중앙 통신소 기단 바닥 | corridor*, room*(우주선 내부) |
| prototype-kit | indicator-special-area·indicator-special-arrow·indicator-round-*, flag, hat-hard, door-garage, crate | **건설 모드 바닥 표시**(19A 축 10 대안), 집결 지점 깃발, 작업자 안전모, 공장 셔터 | animal-*, figurine*, number-*, coin |
| mini-forest | rocks-high·rocks-low·rocks-ramp, stones, patch-dirt·patch-grass, tent | 바위 변종 추가, **바닥 흙·풀 얼룩 데칼**(19C 5번 네모 패치 대체), 기지 보급 텐트 | character-archer, weapon-*, target, building-* |
| blaster-kit | crate-medium·crate-small·crate-wide | 보급 상자·탄약 상자 변종 | blaster-*(장난감 총), bullet-foam*, target* |
| mini-arcade | vending-machine | 상업 폐허 거리 소품 1~2종만 | 나머지 전부 |
| car-kit (19C 추가분) | tractor-shovel, garbage-truck(표식 지움), delivery-flat | 고철 하치장 작업 차량(장식), 도로 잔해 변종 | police·taxi·ambulance·firetruck·race·kart |
| train-kit | 손상된 철로 + 화차가 있으면 **탈선한 화차 더미** | 중앙 폐허의 대형 엄폐 장식(판정이 필요하면 19C 10번처럼 바위 판정 추가, 대칭) | - |
| mini-characters | character-worker | 본진 장식 작업자(19C 8번) | character-soldier 등(아기자기한 비율이라 보병으로 쓰지 않음) |
| animated-characters-protagonists | characterMedium 골격 + idle·run 애니메이션 | **장식 작업자에 걷기 애니메이션**(작업복 텍스처를 새로 칠함). 보병 분대 적용은 high 품질 실험만, 비용 측정 후 보고 | 범죄자·스케이터 스킨 |

규칙:
- 모든 모델은 colormap_military 재도색과 KenneyLit을 거친다. 원색·흰색이 남으면 안 된다.
- 소품 수량 상한과 품질별 비율(low 40%, medium 70%, high 100%)은 19C와 같다. 폐허 내부 가구는 high만.
- 실존 상표·문자·로고가 있는 텍스처 칸은 지운다.
- 비교 화면: 19A 고정 시점 5개에서 19C 대비 after를 `screenshots/lookdev/19d_compare_<시점>.png`로 남긴다.

끝나면 push하고 NEXT-TASK-24-NAVAL.md로 넘어간다. 보안 규칙은 이전과 같다.
