# 항목 19C — Kenney 맵 전면 적용 (밀도·스케일·색 교정, 도로·차량·공장·우주 키트)

시작 조건: 지금 하던 작업(19B/19)을 커밋·push한 뒤 바로 시작한다. **22·23보다 먼저** 한다.

> **2026-11-08 변경:** 2인 맵은 없앤다. 맵은 4인 맵 하나이고, 2인 대전은 그 맵의 대각선 두 자리로 한다(NEXT-TASK-17B-ONE-MAP.md). 이 파일의 "2인 맵" 언급은 "4인 맵 2명 대각선"으로 읽는다.

CLAUDE.md 규칙과 결정론 유지. 화면(View) 작업이 중심이다. 시뮬레이션·맵 판정 데이터를 바꾸는 곳은 아래 "판정 변경"에 적은 두 군데뿐이다. 단계마다 커밋·push.

## 사용자 평가 (19b_compare_C 확인)
Kenney가 일부만 소극적으로 바뀌었고, 스케일과 색이 맞지 않아 맵 전체가 휑하다. 목표는 aow3-ref 01·04의 밀도다.
1. 나무가 작고 듬성듬성해서 숲이 아니라 화단 같다.
2. 빌딩이 눈부신 흰색·은색 통짜 기둥이고, 전장과 안 맞는 고층 오피스다.
3. 절벽이 작고 얇은 갈색 돌담 같다.
4. 바닥에 네모난 베이지 패치가 남아 있고, 빈 땅이 허전하다.
5. 노란·검은 빗금이 아직 보인다.

## 받은 키트 (temp-assets, 2026-11-08 16:20 추가분 포함)
이미 쓰는 것: nature-kit, city-kit-commercial·industrial·roads, factory-kit, tower-defense-kit, train-kit

새로 쓸 것:
- **car-kit:** truck, truck-flat, delivery, van, sedan, suv, tractor-shovel, debris-* 부품, wheel-*
- **modular-buildings:** building-* 조각, building-sample-house-a~c
- **city-kit-suburban:** building-type-a~u(1~2층 주택), fence-*, path-stones-*
- **building-kit:** barricade-doorway-*, barricade-window-*(진지 건물용), plating-*, roof-flat-*, column-*
- **modular-space-kit, space-station-kit:** pipe*, container*, gate*, cables, display-wall, floor-panel*. 위성 접시는 없다. 접시는 기존 절차 메쉬를 쓴다.
- **mini-characters:** character-worker(장식 작업자만). character-soldier 등은 쓰지 않는다.

쓰지 않을 것: blaster-kit, mini-arcade, furniture-kit, prototype-kit, animated-characters-protagonists, mini-forest. 그리고 car-kit의 police, taxi, ambulance, firetruck, race*, kart*(실존 표식·장난감 느낌).
- `kenney_tower-defense-kit (1).zip`은 같은 파일이 중복된 것이다. 무시한다.
- 설치 스크립트에 새 키트를 추가하고 FBX와 colormap 계열 png만 가져온다. 저장소 증가량을 기록한다.

## 0. 원인 진단 먼저 (보고서에 기록)
- **흰색·은색 빌딩의 원인을 확인한다.** Kenney 최신 키트는 대부분 **서브메쉬별 머티리얼이 아니라 `colormap.png` 한 장(색 칸 아틀라스) + UV**로 색을 낸다.
  - 키트별로 FBX의 머티리얼 슬롯 수와 colormap 사용 여부를 표로 만든다.
  - KenneyLit 셰이더가 colormap을 샘플링하지 않거나, 채도·밝기 보정이 과해서 흰색이 됐는지 확인한다.
- **스케일 원인:** 모델 원본 단위(1유닛 = 몇 m)와 KenneyAssetMap 스케일을 키트별로 표로 만든다.

## 1. 색: 키트 전체를 군용 팔레트로 다시 칠하기 (모든 Kenney 공통)
- **colormap 아틀라스 키트**
  - 에디터 도구로 키트마다 `colormap_military.png`를 굽는다.
  - 아틀라스의 각 색 칸을 색상 범주(벽·지붕·창문·금속·초록·장식 원색 등)로 분류해 목표색으로 바꾼다.
  - 바꾼 색 칸 표를 `Game/Docs/kenney-palette.csv`로 남긴다.
- **다중 머티리얼 키트:** 슬롯별로 같은 목표색을 입힌다.
- **목표색 (VisualProfile에서 조정 가능)**
  - 벽·콘크리트: #6E6A63
  - 지붕: #3D4043
  - 창문·유리: #222A30
  - 금속·구조물: #5A626A
  - 장식 원색(빨강·파랑·노랑): 녹슨 철 #6B4A35 또는 카키 #7A7454
  - 초록 원색: 국방색 #4E5A3A
  - 나무 잎·풀은 2번 숲 규칙
  - 팀 색은 19A에서 정한 노출 정도대로 별도 칸에 입힌다.
- KenneyLit 셰이더의 높이 그라데이션, 접촉 AO, 저주파 색 노이즈는 그대로 둔다. 채도는 아틀라스에서 이미 내렸으므로 셰이더에서 다시 깎지 않는다.
- **유닛도 같은 톤 검사를 한다.** 유닛만 튀지 않는지 확인한다.

## 2. 숲: 크게, 빽빽하게, 바닥은 어둡게
- **나무 크기:** 지금의 1.8~2.5배. 1080p 기준 나무 높이 ≈ 전차 길이의 2~3배.
- **나무 수:** 숲 군락당 2.5~3배. 위에서 내려다볼 때 **숲 면적의 85% 이상이 잎으로 덮이게** 한다(화면 픽셀로 측정해 기록).
  - 배치는 해시 기반으로 위치 흔들림, 크기 0.8~1.3배, Y 회전을 준다.
  - 비율은 침엽 60%, 단풍 25%, 짙은 활엽 15%.
- **숲 바닥색:** #2A3322(부엽토·그림자). 가장자리는 2~3m에 걸쳐 풀색으로 부드럽게 섞는다.
- **가장자리 장식:** 덤불, 쓰러진 통나무, 작은 바위. 채집으로 줄어드는 그루터기 규칙은 유지한다.
- **성능:** GPU 인스턴싱. low는 나무 수 60%, 원거리는 낮은 면수 변종.

## 3. 건물: 고층 오피스 금지, 낮고 넓은 산업·주거·폐허
- **쓰지 않는다:** commercial의 building-skyscraper-*와 높은 상업 빌딩
- **도시 건물 배치 (종류별)**
  - 소형: suburban building-type-*, modular-buildings building-sample-house-*
  - 중형: industrial building-a~j(창고·공장)
  - 대형: industrial building-k~t + 저장 탱크·사일로(detail-tank*)
  - 높이는 1~2층 위주이고 3층까지만.
- **폐허 단계:** 손상 단계마다 지붕을 걷어 내고 위층을 잘라 낸다. 균열·그을음을 입히고 잔해 소품(building-kit plating 조각, car-kit debris-*)을 둔다.
- **쓰러지는 건물 후보 교체:** 급수탑(water-tower), 큰 굴뚝(chimney-large), 송전탑(roads electricity-pole-wide 계열을 키운 것). 고층 빌딩 대신 이것들을 쓴다.
- **진지 건물:** building-kit의 barricade-window-*·barricade-doorway-*를 창문과 문에 붙인다. 모래주머니는 절차 메쉬다.
- 건물마다 콘크리트 패드는 유지하고(19A 02), 모델이 부지 밖으로 나가지 않는지 테스트한다.

## 4. 절벽: 크게, 2~3단으로 쌓아 능선을 꽉 채우기
- nature-kit의 cliff_block*, cliff_large*, cliff_corner*, cliff_top*, rock_tall*를 **2~3배 크기**로 쓰고, **2~3단으로 쌓는다.**
- **적용 범위:** 모든 능선(통과 불가 지형), 본진 외곽, 강가 절벽(자연 구간), 맵 경계 전체. 판정 경계를 따라 조각을 이어 붙인다.
- **빈틈 금지:** 능선 길이 대비 절벽 모델 덮임 비율 95% 이상을 측정해 기록한다.
- **색:** 회색·회갈색 바위(_rock), 층마다 윗면을 밝게, 윗면에는 풀·나무 몇 그루.
- 판정 경계는 바꾸지 않는다. 모델이 통행 가능 구역으로 1m 넘게 튀어나오면 테스트로 잡는다.

## 5. 바닥: 네모 패치 제거, 자연스러운 흙 섞임, 소품 수백 개 흩뿌리기
- 사각형 베이지 흙 패치를 만드는 코드를 찾아 없애고, 노이즈 기반 불규칙 섞임으로 바꾼다.
  - 흙길·도로변·건물 패드 주변·교전 구역은 흙과 그을음
  - 나머지는 풀 2~3단계
  - 경계는 부드럽게
- **흩뿌리기:** nature-kit rock_small*, plant_bush*, grass*, grass_leafs*, log, stump 등. 맵당 수백~천 개 단위로, GPU 인스턴싱, 해시 기반 배치.
  - 넣지 않는 곳: 도로 위, 건물 부지, 건설 가능 구역 한가운데, 다리
  - 큰 바위·통나무는 통과 불가 영역이나 그 가장자리에만 둔다.
  - low는 40%, medium 70%, high 100%.
- **빈 땅 비율 측정:** 고정 시점 5개에서 "평평한 단색 바닥" 픽셀 비율을 before/after로 잰다. 목표는 절반 이하.

## 6. 노란·검은 빗금 완전히 끄기
- 빗금·점선 테두리를 만드는 **모든** 코드(건설 금지 표시, 바리케이드·점령지·자원 주변, 지형 테두리, 다리 구역 등)를 찾아 목록을 보고서에 적는다.
- 기본 상태에서는 생성하지 않거나 렌더러를 끈다. 건설 위치 지정 모드일 때만 그 근처에 표시한다.
- **테스트:** 건설 모드가 아닐 때 빗금 렌더러가 0개인지 확인한다.

## 7. 도로·다리 (city-kit-roads)
- **판정 변경 1:** Kenney 도로 조각은 격자·90°다. 도시 구역의 `Roads` 중심선을 **8m 격자에 맞춰 직선·직각 교차**로 정리한다(점대칭·90도 대칭 유지). 자연 구간 흙길은 기존 곡선 그대로 둔다.
  - 도로 속도 격자(`GroundGrid`)를 다시 굽고, 항목 17 이동 시간 표를 다시 재서 목표(중앙 45~60초 등)를 지킨다.
- **도로 메쉬:** road-straight, road-crossroad, road-intersection, road-bend, road-end, road-roundabout을 쓰고, 연결 모양에 맞는 조각을 자동으로 고른다.
- **다리:** road-bridge + bridge-pillar*로 교체한다. 폭·길이는 시뮬레이션의 다리 사각형에 맞춘다. 휴전 울타리는 그대로 다리 입구에 둔다.
- **길가 소품:** 가로등(light-*), 전봇대·전선, 가드레일(road-*-barrier), 공사 바리케이드·콘·펜스, 표지판(글자 없는 empty·object형만), 쓰레기통. 간격 규칙과 수량 상한을 둔다.

## 8. 공장·기지 디테일 (factory-kit + 장식 작업자)
- **가공소:** hopper-*, conveyor*, robot-arm-a/b, pipe-large*, machine* + 기존 용광로(주황 쇳물)
- **집하장·보급창:** factory box-*, car-kit box, industrial shipping-container-*, 팔레트 더미
- **군수공장·중공업 기지:** crane*, door-wide-*, catwalk-*, machine-fortified, robot-arm
- 진영 형태 대비(자유=곡면·돔, 대륙=각진)는 유지한다. Kenney 부품은 지붕·측면·마당에 붙여 상자 느낌을 없앤다.
- **장식 작업자:** mini-characters character-worker를 주황 작업복 톤으로 칠해, 본진 건물 사이를 상자를 들고 오가게 한다. 화면 전용이고 판정이 없다. 본진당 6~10명, low는 끈다.

## 9. 중앙 점령지·연구소·자유 연방 사령부 (space 키트)
- modular-space-kit / space-station-kit의 pipe*, cables, container*, gate*, floor-panel*, display-wall을 붙여 근미래 군사 시설 느낌을 낸다.
- 위성 접시는 기존 절차 메쉬를 키우고 다듬어서 쓴다.
- **색:** 슬레이트 #5A626A + 카키 + 팀 색 포인트. 흰색 금지. 발광은 기능색 규칙(전기=청록)대로 약하게.
- 대륙 연방 건물에는 space 부품을 쓰지 않는다. 각진 요새 형태를 유지한다.

## 10. 시가전 차량 잔해·엄폐물 (car-kit)
- truck, truck-flat, delivery, van, sedan, suv, tractor-shovel를 약간 기울이거나 옆으로 눕혀 배치한다. 불탄 고철 톤 #2B2927에 그을음과 녹을 입히고, debris-*와 wheel-*를 주변에 흩뿌린다.
- **위치:** 도로변, 뒷길 바리케이드(폐차 더미), 중앙 교전 구역, 폐허 주변.
- **판정 변경 2:** 길 한가운데 엄폐물처럼 놓는 큰 잔해는 MapSpec에 작은 바위 판정으로 추가한다(대칭 유지).
  - 그 밖의 잔해는 통과 불가 영역 안이나 가장자리에만 둔다. 유닛이 잔해를 뚫고 지나가는 장면이 나오면 안 된다.
  - 이동 시간을 다시 잰다.

## 11. 유닛 일부 디테일 (선택적, 진영 실루엣 유지)
- 대륙 연방 공병 트럭과 만리 다연장포의 **차체 아래쪽(운전석·바퀴)**에 car-kit의 truck·truck-flat 부품과 wheel-truck을 쓸 수 있다.
  - 만리 다연장포는 **8륜 규칙**을 지킨다(바퀴 추가).
  - 발사대·크레인·장갑 부품은 기존 절차 메쉬다.
  - 실존 차량과 닮지 않게, 색은 군용 팔레트로 한다.
- 다른 유닛은 이번에 바꾸지 않는다.

## 12. 해군은 이 항목에서 하지 않는다 (NEXT-TASK-24-NAVAL.md에서 한다)
- 사용자 요청 중 "해군(정찰선·포격함), 조선소·부두"는 **이번에 하지 않는다.** 게임 설계에 해군이 없고, 강은 본진을 둘러싼 짧은 운하뿐이라 배가 다닐 길이 없다. watercraft 키트도 받지 않았다.
- 해군을 넣으려면 물길 맵, 수상 길찾기, 새 유닛·건물, 밸런스, AI가 필요하다. 보고서 "남은 결정 사항"에 적고 사용자 결정을 기다린다.

## 성능 기준
- 고정 장면(19A)에서 품질별 그리기 호출·삼각형을 19B와 비교해 표로 남긴다.
- 목표
  - medium: 삼각형 40만 이하, 그리기 호출은 19B 대비 +30% 이내(인스턴싱·정적 배칭·LOD로)
  - low: 19A low 수준 유지
- 4인 맵 전체 화면도 측정한다. 넘치면 무엇을 줄였는지 적는다.

## 검증
- 전체 테스트(PlayMode) 통과, 결정론 체크섬, 금지어 0건
- 새 테스트
  - 빗금 0개(건설 모드 아님)
  - Kenney 모델이 부지·판정 경계 밖 1m 이상으로 나가지 않음
  - 쓰러지는 건물 후보 교체 후 낙하 구역 규칙 유지
  - 도로 격자 정리 후 연결성과 이동 시간
- AI 대 AI 2인 5판·4인 5판: 끼임과 시간 초과 확인(엄폐물·도로 변경 때문)
- **비교 화면:** 19A 고정 시점 5개(A~E) × before(19B)/after(19C)를 `screenshots/lookdev/19c_compare_<시점>.png`로 만들고, aow3-ref 01·04를 옆에 붙인 `19c_vs_ref.png`도 만든다.
- 측정값을 보고서에 기록한다: 숲 덮임 비율, 절벽 덮임 비율, 빈 땅 비율, 성능 표
- LOOKDEV.md에 "19C" 절을 추가하고, visual-style 스킬에 이번 규칙을 반영한다.
  - Kenney 색 아틀라스 재도색
  - 고층 금지
  - 숲 밀도와 바닥색
  - 절벽 쌓기
  - 흩뿌리기 수량
  - 빗금 기본 끔

끝나면 push하고, 이어서 NEXT-TASK-17B-ONE-MAP.md → NEXT-TASK-19D-KENNEY-MORE.md → NEXT-TASK-24-NAVAL.md → NEXT-TASK-22-BALANCE.md → NEXT-TASK-23-DEVICE.md를 진행한 뒤 멈춘다. 보안 규칙은 이전과 같다.
