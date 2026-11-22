# 교차 리뷰 요청 — Map4P v4 꾸미기 (만든 AI: [claude], 2026-11-22)

리뷰는 **만든 AI가 아닌** 다음 에이전트(Gemini 또는 Codex)가 한다. 아래 캡처를 직접 열어 보고 `design/maps/Map4P-BRIEF.md` §3 채점표로 항목별 0~2점을 매긴다. **18점 이상 + 금지 항목 0개**여야 사용자 승인 요청으로 넘긴다.

## 볼 것
- 전체 개요 + 시안 A 나란히: `Docs/status_images/Map4PV4/dress_vs_conceptA.png`, `dress_map_top.png`
- 게임 카메라: `dress_base_close.png`, `dress_base_wide.png`, `dress_base_ramp.png`, `dress_diagonal_road.png`, `dress_city.png`, `dress_city_close.png`, `dress_canal_bridge.png`, `dress_compound.png`, `dress_compound_close.png`, `dress_outer_edge.png`, `dress_outer_west.png`, `dress_rubble.png`, `dress_outpost.png`
- 자동 검사·성능 표: `dress_audit.txt` (원본 `screenshots/map4p-v4/audit_v11.txt`)
- 재현: `Game-logs/map4p-codex-run.ps1 -Name x -Method FactoryWars.EditorTools.DressKit.BuildBakeDress` → `-Iteration v12 -Graphics -Filter FactoryWars.Tests.LocalMatchTests.Map4P_CaptureTopAndGameplayViews`

## 만든 AI 자기 채점(참고, 리뷰어가 덮어쓴다)
| # | 항목 | 자기 점수 | 근거 |
|---|---|---|---|
| 1 | 구도 일치 | 2 | 칸 글자마다 블록·칠하기, 칸 지도 일치 시험 통과 |
| 2 | 시안 A 분위기 | 1 | 건물·공원·주차 밀도는 비슷, 컨테이너 한 색·요새 중앙 헬기장 없음 |
| 3 | 길 가독성 | 2 | 간선 노란 중앙선·흰 가장자리선·횡단보도, 골목은 밝은 아스팔트+배수로, 다리 난간 |
| 4 | 본진 | 1 | 시설 군집 3개 이상·경사로 3개, 그러나 빈 사각형 12m(한계값) |
| 5 | 중앙 요새 | 1 | 팔각 T벽·정문 4·컨테이너 고리·건물 4·엄폐, 중앙 헬기장 없음(상인 기지) |
| 6 | 수로 | 2 | S자 연속, 부두 난간·갓돌·물때 띠, 다리 위치 정확 |
| 7 | 높이 3단 | 1 | 고지(본진)·중간·낮음(수로) 읽힘, 중간 단 내부 높이 변화는 건물뿐 |
| 8 | 경계 | 2 | 고층 배경 건물 + 방벽 + 잔해 + 컨테이너 + 맵 밖 스카이라인 |
| 9 | 크기 비율 | 2 | 5m 모듈 건물·8m 차선·3.3m 방벽 |
| 10 | 색 | 2 | 아스팔트 회색·벽돌 웜톤·청록 물·주황/노랑 포인트 |
| 11 | 성능 | 1 | 게임 줌 보통 예산 안, 낮음은 수로 시점 삼각형 324k(8% 초과) |
| | **합계** | **17** | 금지 항목: 빈 구역 2건(본진 12m·요새 14m) |

## 리뷰어가 특히 볼 것
1. 동·서 길을 보는 건물의 카메라 쪽 옆면이 민벽인지(`dress_city_close.png` 오른쪽 열).
2. 대각 도로·본진 대각 옹벽의 계단 결이 게임 줌에서 거슬리는지.
3. 요새 안 엄폐 군집·컨테이너가 유닛 길을 시각적으로 막는지(시뮬레이션 벽은 담장·기존 엄폐 4곳뿐).
