# Factory Wars PlayMode 회귀 보고서

2026-11-21 KST · Unity 6000.0.85f2 · 전체 실행 종료 11:36:07 · 후속 집중 실행 종료 11:42:58

| 실행 | 전체 | 통과 | 실패 | 건너뜀 |
| --- | ---: | ---: | ---: | ---: |
| 맵 폴리시 전체 PlayMode, `-nographics` | 548 | 398 | 25 | 125 |
| 저품질 장식 밀도·AI 외교 보완 후 GPU·Photon 집중 검사 | 5 | 5 | 0 | 0 |

전체 실행에서 판정된 423건의 통과율은 94.1%다. 직전 전체 실행과 비교해 기존 실패 23건이 재현됐고, 이전에 실패한 수송 파괴와 시가지 교량 표시 2건은 통과했다. 이번 전체 실행에서 새로 실패한 2건은 수정 후 집중 검사에서 통과했다. 전체 집계는 11:36 종료 시점의 기록이다.

## 실패 25건

### AI·외교·경제·테크 8건

| 테스트 | 전체 실행에서 관찰한 결과 | 후속 상태 |
| --- | --- | --- |
| `Accept_InsufficientIron_KeepsProposalPending` | 철 부족 시 동맹 제안을 보류하는 시나리오가 180초 제한에 도달했다. | 기존 실패 |
| `Ai_LateExpansionOnlyForNormal` | 보통 AI의 확장 시작 기준은 420초 초과였으며 관측값은 150초였다. | 기존 실패 |
| `AiDiplomacy_SameSeedSameRelations` | 같은 시드 재실행에서 두 경기의 외교 점수 배열이 달랐다. 양쪽 동맹 쌍은 2개였다. | 200초에 해당하는 정확한 Quantum 틱에서 기록하도록 수정 후 통과 |
| `Bot_ReachesTierTwoAndBuildsFactory` | 군수공장 1개 이상을 기대했으며 관측값은 0개였다. | 기존 실패 |
| `Break_InsufficientIron_Fails` | 철 부족 시 동맹 파기를 거절하는 시나리오가 180초 제한에 도달했다. | 기존 실패 |
| `Produce_PopulationLimitCountsQueue` | 인구 한도에서 한 대의 비용을 차감한 재고 4,750을 기대했으며 관측값은 4,780이었다. | 기존 실패 |
| `Proposal_TargetDefeated_Refunds150` | 제안부터 패배 확인까지 600틱 미만을 기대했으며 805틱이 걸렸다. 해당 실행은 환급액 단언 전에 중단됐다. | 기존 실패 |
| `Tech_DataFollowsSizeRules` | 대륙 연방 비해군 중형 유닛은 5종을 기대했으며 실제 7종이었다. | 기존 실패 |

### 맵·자원·주둔 7건

| 테스트 | 전체 실행에서 관찰한 결과 | 후속 상태 |
| --- | --- | --- |
| `ArtAndMap_SyntyPrefabsBiomesAndRoadClearance` | 해협의 `(-10, -118)` 자원 노드는 중앙축에서 10m 떨어져 있다. 검사는 28m 이상을 요구한다. | 기존 실패 |
| `City_LayoutCountsAndSymmetry` | `Map4P` 도시 건물은 80개 이상을 기대했으며 28개가 집계됐다. | 기존 실패 |
| `Garrison_NotOutsideBaseDuringTruce` | 휴전 시 본진 밖 주둔 거절 사유로 `Truce`를 기대했으며 결과는 `Invalid`였다. | 기존 실패 |
| `Map17B_ThreePlayerSpawnsThreeSeatsAndLeavesOneNeutral` | 점유 기지 세 곳의 100% 철광산은 6개를 기대했으며 0개가 집계됐다. | 기존 실패 |
| `Map17B_TwoPlayerDiagonalLayoutAndCrossing` | 점유 기지 두 곳의 100% 철광산은 4개를 기대했으며 0개가 집계됐다. | 기존 실패 |
| `Map4P_LayoutAndCrossing` | 중앙 암시장을 제외한 변 거점은 4개를 기대했으며 12개가 집계됐다. | 기존 실패 |
| `Terrain_BuildingKeepsThreeFromResources` | 철광산과 3m 떨어진 건설에서 거절 0건을 기대했으며 1건을 받았다. | 기존 실패 |

### 모델·에셋·렌더링 7건

| 테스트 | 전체 실행에서 관찰한 결과 | 후속 상태 |
| --- | --- | --- |
| `Art_EveryBuildingSpecHasAModelInsideItsFootprint` | 건물 스펙 개수 단언에서 기대 43개, 실제 49개로 중단됐다. 개별 모델 경계 검사는 그 실행에서 미수행이다. | 기존 실패 |
| `Assets_PackFilesUnchanged` | 저장된 팩 목록과 로컬 파일 사이에 차이 32건을 보고했다. 출력에는 `PolygonCoffeeShop` 추가 파일과 `PolygonConstruction` 일부 재질 크기 차이가 있다. | 기존 실패 |
| `Battle_FiringShowsRecoilEffectsAndDestructionExplodes` | `ArenaUnit`이 조건에 맞는 유닛을 찾지 못해 `Sequence contains no matching element` 예외로 중단됐다. | 기존 실패 |
| `Synty_SlotsConnectedWithSupportedShaders` | Synty 건물 슬롯은 40개 이상을 기대했으며 20개가 집계됐다. 셰이더 검사는 해당 단언 뒤에 있다. | 기존 실패 |
| `Synty_TurretAimsAndSpinPartsMove` | 포탑 조준과 반동 조건을 제한 시간 안에 충족하지 못했다. | 기존 실패 |
| `Urban_DensityByQuality` | 저품질 장식 유지 비율 기대값은 40% ±15%p였으며 관측값은 79.5%였다. | 호안의 연속 구조물과 품질별 장식을 분리한 뒤 통과 |
| `Visual_FilledSlotsWithinBudgetAndChecked` | 모델 크기, 삼각형, 드로우 호출 제한을 초과한 항목이 있었다. 양 진영 포격함은 각각 25,457 삼각형과 35회 호출로, 예산은 각각 4,000과 6회다. 양 진영 사령부도 각각 24,033 삼각형으로 12,000 예산을 초과했다. | 기존 실패 |

### 궁극기 3건

| 테스트 | 전체 실행에서 관찰한 결과 | 후속 상태 |
| --- | --- | --- |
| `Ultimate_CarrierLaunchesRecoversAndRebuildsDrones` | 항모 드론의 출격·복귀·재건 시나리오가 180초 제한에 도달했다. | 기존 실패 |
| `Ultimate_DeterministicAcrossRunsAndOnline` | 미사일 사용 횟수가 0회여서 시나리오 전제 조건에서 중단됐다. 해당 케이스의 체크섬 비교 결과는 미확정이다. | 기존 실패 |
| `Ultimate_MissileCountdownThenRadialDamage` | 착탄 지점에서 12.1m 떨어진 전차의 남은 체력은 약 540을 기대했으며 720이었다. | 기존 실패 |

## 건너뜀 125건

XML에서 125건 모두 `Skipped`의 `Explicit` 라벨이었다. 스킵 목록은 직전 전체 실행과 동일하다. 47건은 개별 테스트가 `Explicit`으로 표시됐고, 78건은 `Explicit` 메서드에 딸린 파라미터별 케이스다. 전체 PlayMode 실행에서 이 목록에 `Ignored`나 환경 조건으로 인한 건너뜀은 기록되지 않았다.

### 파라미터별 케이스 78건

| 테스트 메서드 | 전체 실행에서 건너뛴 인자 | 건수 |
| --- | --- | ---: |
| `AiBatch_DesertTwoPlayer` | `0`–`9` | 10 |
| `AiBatch_FourPlayer` | `0`–`19` | 20 |
| `AiBatch_Mirror` | `0`–`19` | 20 |
| `AiBatch_TwoPlayer` | `0`–`19` | 20 |
| `Probe_TierOneFieldBattle` | `0`–`5` | 6 |
| `Lookdev_Times` | `0`, `2` | 2 |

### 개별 테스트 47건

| 항목 | 건수 | 테스트 |
| --- | ---: | --- |
| 전투·맵·기지 GPU 캡처 | 17 | `Capture32_CoverTanksHelicopters`, `Capture33_CarrierMissileLanding`, `CaptureAllUnitsAndBattlefieldScars`, `CaptureCombat_LinesTracersScorch`, `CaptureEntireStandardMap`, `CaptureFog_BeforeAfter`, `CaptureFog_DropStages`, `CaptureIndividualFormationAndThumbHud`, `CaptureLivingBase`, `CaptureMaps35_AllFive`, `CaptureMinimap_TerritoryAndExpansion`, `CaptureNewBundleMapArt`, `CaptureStaffedIronExtractor`, `CaptureStaffedLumberMill`, `CaptureSynty_BeforeAfter`, `CaptureTrader_SelectedAndClean`, `CaptureUrban_BeforeAfter` |
| 경기 화면 | 8 | `Ingame_City21Collapse`, `Ingame_City21FourPlayer`, `Ingame_City21Overview`, `Ingame_FourPlayerOverview`, `Ingame_Hud`, `Ingame_Map17`, `Ingame_Map17FourPlayer`, `Ingame_TwoPlayer` |
| 아트 룩 검증 | 11 | `Lookdev_19B`, `Lookdev_19C`, `Lookdev_19D`, `Lookdev_27K1`, `Lookdev_27K2`, `Lookdev_Axes`, `Lookdev_DressingBudget`, `Lookdev_Final`, `Lookdev_KenneyColorMatch`, `Lookdev_KenneyColorMatchNeutral`, `Lookdev_Measure` |
| UI·시가지 | 5 | `Ui_CaptureFactoryUpgrades`, `Ui_CaptureScreens`, `Urban_CafeAndAsphaltAreRendered`, `Urban_CaptureBuiltInMapDressing`, `Urban_RoadsideBarrierCollapsesWithoutRemovingNeighboringProps` |
| Photon 실서버 | 2 | `Online_Live_CreateJoinLobby`, `Online_Live_SessionChecksumsMatch` |
| 장시간 안정성 | 2 | `Stability_ThirtyMinuteFourAiTickTimeAndChecksum`, `Stability_TwentyMinuteMemoryAndGc` |
| 항모 교전 성능 | 1 | `Perf_FourCarriersMaxEngagementTickTime` |
| 기기 터치 영역 | 1 | `Device_TouchTargets` |

### 같은 작업 주기의 별도 실행

전체 실행에서 건너뛴 항목 가운데 8개의 별도 통과 기록을 확인했다.

- GPU 6건 · `Capture33_CarrierMissileLanding`, `CaptureAllUnitsAndBattlefieldScars`, `CaptureLivingBase`, `CaptureNewBundleMapArt`, `CaptureStaffedIronExtractor`, `CaptureStaffedLumberMill`
- Photon 2건 · `Online_Live_CreateJoinLobby`, `Online_Live_SessionChecksumsMatch`. 최종 체크섬 검증에서 확정 프레임 586개가 일치했고 디싱크는 0건이었다.

## 현재 진행 상황

표준·다도해·해협·사막 시각 개선과 공장 야드, 자원 시설 배치 표시, Mixamo 현장 동작을 적용했다. 5개 맵의 지상·수상 NavMesh 재베이크와 GPU 이미지 9장 확인을 마쳤다. 남은 검증은 기존 실패 23건, Android 실기기 성능과 터치, 전차 출고 전체 경로다. 일부 공병·의무지원·승무원·보병 클립의 경기 상태 연결이 남아 있다.

[GitHub 대시보드](../README.md) · [프로젝트 상태와 GPU 캡처](FactoryWars_Status.md)

### 근거 로그

로컬 `Game-logs/`의 `map-polish-full.xml`과 `bundle-full-final.xml`에서 전체 결과와 직전 기준선을 대조했다. `map-polish-followup-final.xml`에서 5개 집중 결과를, `map-polish-final-gpu.xml`과 `map-polish-final-captures.xml`에서 별도 통과 기록을 확인했다.
