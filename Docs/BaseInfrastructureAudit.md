# 기지 인프라 초기 감사

2026-11-21 KST · 구현 전 `cca8db2` · `main`과 `origin/main` 일치 · 미커밋 및 미추적 파일 0건.

| 항목 | 확인 결과 |
| --- | --- |
| 기존 기록 | `HANDOFF.md`, 최근 커밋, 로컬 Codex 세션 목록과 기존 Unity 결과를 확인했다. 이전 자원 시설 자동 배치·작업자 순환·맵 개선이 커밋되어 있다. |
| Unity | Unity 6000.0.85f2에서 배치 GPU PlayMode가 컴파일되고 기존 검사 5/5가 통과했다. 장식 작업자 순환, 건설 단계, 표시 배율, 교량 파괴·가교, 수상·지상 분리를 확인했다. 원본은 상위 `Game-logs/infrastructure-baseline.xml`이다. |
| 맵과 이동 | `Assets/QuantumUser/Scenes/`의 5개 기본 맵, `FactoryWarsSetup.cs`의 지형 생성·베이크, `MapSpec`과 `MovementSystem`을 확인했다. 고정 장애물과 파괴 가능 지형은 NavMesh에 반영된다. 경기 중 건물은 바깥으로 밀고 벽을 따라 우회하는 방식이다. |
| 배치와 출고 | `ProductionSystem`이 건물·자원·지형 부지를 검사한다. 생산 유닛은 고정 거리의 방향별 위치를 쓰므로 건물과 다른 유닛이 출구를 점유한 경우를 검증해야 한다. |
| 공사 외형 | `ArtView.AddPlate`는 일부 그래픽 프로필에서만 보인다. `BattleView.DressBuilding`의 산업 야드와 공사 단계는 존재한다. 전 건물에 공통 적용되는 지형 밀착 기초판과 배치 미리보기가 필요하다. |
| 작업자·에셋 | `BaseAmbientWorkers.cs`는 View 전용이며 Synty 안전모 모델·기존 근무와 휴식·Mixamo 운반 동작이 연결돼 있다. 건물별 일정, 활동점과 지형 장애물 고려가 추가 대상이다. |
| 화면 기록 | `UnitCatalogCaptureTests.cs`에 실제 Unity 카메라 PNG 자동화가 있다. 기존 목록은 선별된 전투 유닛과 일부 건물을 대상으로 한다. 전체 구현 스펙을 열거할 경로가 필요하다. |

기존 구매 파일과 장면, Quantum NavMesh, 건설 데이터는 보존한다. 옛 전체 회귀는 398 통과·25 실패·125 명시 실행 제외로, 후속 밀도·AI 외교 검사 2건의 통과도 `FactoryWars_PlayMode_2026-11-21.md`에 별도로 기록돼 있다.
