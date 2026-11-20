# AI 교대 인계

## 2026-11-20 현장 음향과 에셋 목록

- 사용자가 `temp-assets/Sounds`에 제공한 MP3 11개를 디코딩해 확인했다. `Tools/prepare_local_audio.py`가 9개의 24kHz 모노 WAV를 `Assets/ExternalAssets/FactoryWarsLocalAudio`에 생성한다. 단발은 끝을 페이드하고 반복음은 앞뒤를 교차 처리한다. `AudioCatalogTools.cs`는 이 폴더만 PCM으로 가져온다. 입력과 생성 음원은 로컬에 보존한다.
- `GameFeel.cs`와 `AudioCatalog.cs`는 기존 UI·진동·합성 효과를 재사용한다. `BattleView.cs`와 `BattleAudio.cs`가 총성·로켓 발사·출격을 화면 위치에 따라 재생하고 주행·로터·항공기·전투 배경을 최대 네 루프로 섞는다. `MatchHud.cs`에서는 상대 전략 미사일 발사 경보를 연결한다. Quantum 데이터와 씬은 이번 작업에서 변경하지 않았다.
- Unity 6000.0.85f2 GPU PlayMode `Game-logs/codex-audio-release.xml` **13/13 통과**. 초기 가져오기에서 압축 후 최고 레벨이 올라 PCM으로 고정했다. `IsVehicle`이 꺼진 이글 전차도 중·대형 지상 기갑으로 찾아 반복음이 재생되도록 고쳤다. Unity에서 카메라 밖 감쇠, 단발 재생 간격, 실제 전차 출발과 정차의 페이드아웃을 확인했다. Android 실기기 청취는 남았다.
- 배경은 [BeauTheBeau의 battlefield ambience](https://pixabay.com/sound-effects/film-special-effects-battlefield-ambience-38564/)를 다듬었다. 제공본의 `Military equipment movement`는 [spinopel의 원본 페이지](https://pixabay.com/sound-effects/film-special-effects-military-equipment-movement-336354/)를 확인했고 현재 주행음과 중복되어 예비로 보존했다. 약 97초의 `Missile.mp3`와 한글 파일명의 출처는 미확인이다. 배포 전에 확인한다.
- 준비 스크립트를 다시 실행할 때 저장소가 들어 있는 작업 루트에서 `py Game/Tools/prepare_local_audio.py --ffmpeg <FFmpeg 실행 파일>`을 실행하고 Unity에서 가져온 클립을 확인한다. `Audio_LocalClipsAndMovingEngineInMatch`는 로컬 클립이 전부 없으면 건너뛴다.
- 사막·해안 맵을 정리하겠다는 사용자 방향을 [에셋 검토 목록](../ASSET-SHOPPING.md)에 반영했다. 보유 Synty와 War FX를 우선하고, 모바일 VFX는 무료 다운로드 후보만 남겼다.

## 2026-11-20 조명·표면과 HUD

- `VisualProfile.cs`, `VisualPresets.cs`, `VisualProfile.asset`, `WorldView.cs`, `GraphicsQuality.cs`에서 햇빛 강도와 대비를 낮추고 그늘의 주변광을 보강했다. 바이옴별 조명을 기본 프로필과 섞는다. 낮음에서 SSAO가 실제로 꺼지고, 높음에는 FXAA가 적용된다.
- `FactoryWarsKenney.shader`에서 URP의 소프트 섀도 변형과 바닥·도로의 미세 굴곡을 추가했다. `FactoryWarsUrbanWater.shader`에서 보통·높음에 수면의 파형과 시점에 따른 반사를 추가했다. 카메라가 멀어지면 세부 표면 효과를 줄인다.
- `VisualCatalog.cs`에서 대형 비오토핏 모델의 선택 링 크기 상한을 제거했다. `ViewCatalog.asset`과 `SyntyAssign.cs`에서 기준선에서 빠진 용접 프리팹을 공용 VFX 카탈로그에도 연결했다. `MatchHud.cs`에서 출격·하차 터치 영역을 최소 크기에 맞추고 건설 메뉴가 길어질 때만 한 줄을 추가했다.
- Unity 6000.0.85f2 GPU PlayMode `Game-logs/codex-soft-look-verified.xml` **22/22 통과**. 직전 전체 실패와 대응하는 항목 10개는 선택 링, 용접, 터치 크기, 공병 메뉴와 비용, 산업 메뉴에서 사령부 배치, 잠금 사유, 패널 겹침, 위치 고수 버튼, 건설 3단계, 보병 이동·대기·엄폐다. 이전 메뉴 명칭과 프리팹 구성에 의존하던 검사는 현재 동작을 확인하도록 바꿨다. 검증 로그에서 C# 및 셰이더 오류가 없었다.
- 직전 전체 회귀 `Game-logs/codex-yard-full.xml`의 잔여 실패 중 `Assets_PackFilesUnchanged`는 이전 작업 트리의 구입 재질 17개 크기 차이, `Visual_FilledSlotsWithinBudgetAndChecked`는 포격함 25,457 삼각형/35 드로우와 보병 크기 차이를 보고한다. 포격함 기준은 4,000 삼각형/6 드로우다. 배치·광산 수, AI·경제, 수송·궁극기 실패도 추가 진단이 필요하다.
- 이전 실패 목록에서 이번에 통과를 확인한 10개를 뺀 24개는 아트·팩 6개, 맵·배치 6개, AI·외교·경제·기술 8개, 수송·궁극기 4개다. 이 수치는 새 전체 회귀 결과를 의미하지 않는다.
- 원본 팩과 Quantum 시뮬레이션은 이번 변경 대상에서 제외했다. 로컬 대시보드는 [화면 전후 비교](../../README.md#조명표면과-모바일-hud--2026-11-20-kst)와 [환경 에셋 후보](../ASSET-SHOPPING.md)를 포함한다. 최신 전체 회귀와 Android 프레임 측정이 남아 작업 트리는 로컬에 보존한다.

## 2026-11-20 조선소·공항 공사와 맵 겹침 교정

- `Dressing.cs`, `UrbanDecor.cs`, `TerrainLook.cs`, `WorldView.cs`, `BattlefieldView.cs`에서 프리팹 실제 회전 범위로 길가, 배경 소품, 광산, 점령지 장식을 검사했다. 주변에 빈자리가 있으면 소품을 옮기고, 활주로나 건물이 들어올 때는 해당 부지와 겹치는 장식만 비운다. 교량 접속로는 육지에서 그리고 언덕 하단 비탈을 좁혀 차선을 확보했다.
- `AirfieldWorkSystem.cs`, `WorkerSystem.cs`, `GameplaySystems.cs`, `FactoryWars.qtn`에서 배치 시 인접 부속 시설의 부지를 예약한다. 본체가 끝나면 기존 공병 건설 작업을 부속 시설로 전환한다. 고정소수점 시간과 치수는 `GameRulesSpec` 및 `game_rules.asset`에 추가했다. Quantum 코드 생성 완료.
- `GameplaySystems.cs`에서 수상 NavMesh와 교량·수문 간격을 검사해 선박 진수 위치를 정한다. `FacilityWorkView.cs`, `BattleView.cs`, `MatchHud.cs`가 조선소 대기열과 포장 진행률을 그린다.
- `BattlefieldView.cs`에서 전술 바리케이드 본체와 장식 방벽에 붕괴 연출을 붙였다. 전술 바리케이드의 체력·길 개방은 기존 Quantum 규칙을 사용하고 장식 연출은 확정 `AreaImpact`를 구독한다.
- GPU PlayMode `Game-logs/codex-yard-release.xml` **21/21 통과**. `FacilityWorkTests.cs`에서 실제 건설·생산 명령, 완공 전 잠금, 수상 진수, 바리케이드 붕괴, 실제 기본 5개 씬의 소품 배치와 캡처를 확인한다. 자체 Quantum 소스의 결정론 금지어 검색은 0건이다.
- 전체 GPU 미사용 PlayMode `Game-logs/codex-yard-full.xml`은 542건 중 387건 통과, 34건 실패, 121건 건너뜀. `codex-baseline-playmode.xml`의 실패 38건에서 `AiDiplomacy_SameSeedSameRelations`, `ConstructionStages_AndAmbientWorkersLifecycle`, `Hud_BuildingPanelProducesAndEditsPreset`, `Urban_RoadsidesDressedAlongRoads`가 통과했다. 신규 실패 0건이다.
- 다음은 기준선에서 남은 실패 분류, Android에서 다중 조선소·공항 프레임 측정, 실서버에서 공병 시공·생산 잠금 동기화 확인이다. 기존 실패가 섞인 작업 트리는 로컬에 보존했다. [로컬 대시보드](../../README.md)

## 2026-11-20 컨베이어, 증축, 전투 UI

- `ExtractorDeliveryView.cs`와 `ConveyorBeltSurface.cs`는 Kenney 컨베이어와 전용 셰이더로 벨트·화물을 이동시킨다. 철광석 또는 목재가 가장 가까운 소유 사령부로 간다. 배치 인원이 0이면 새로운 화물을 올리지 않고 마지막 화물을 보낸 뒤 멈춘다.
- `HeroVisuals.cs`, `BattleView.cs`, `SyntyAssign.cs`에서 보유한 POLYGON Construction 정비동과 굴뚝을 Lv.2/3에 연결했다. `MatchHud.cs`, `Ui.cs`, `Texts.cs`에서 공장 관리, 증축 비용, 배지 및 도크린 화면을 변경했다.
- Unity 6000.0.85f2 GPU PlayMode `Game-logs/codex-conveyor-factory-verified.xml` 13/13 통과. `UiCaptureTests.cs`에서 실제 공장 관리 버튼으로 Lv.1→2→3 반영을 확인했다. `HudTacticsTests.cs`는 도크린 선택 반영을 검증했다. `UnitCatalogCaptureTests.cs`에서 벨트와 광석 이동 및 공병 퇴거 후 정지를 확인했다.
- 60fps 제한 PC 표본은 공병 도구 47/48, 광석 30/30, 원거리 작업자 36/36 프레임에서 움직였고 최대 간격은 16.9ms였다. 16:9, 19.5:9, 21:9 공장 관리 캡처와 16:9 도크린 화면을 열어 확인했다.
- 이전 전체 회귀 기준선 실패 38건과 섞인 작업 트리는 로컬에 보존 중이다. 다음 작업은 실패 재현 조건 분류, 최신 전체 회귀, Android에서 여러 컨베이어 동시 가동과 HUD 터치 확인이다. [로컬 대시보드](../../README.md)

## 2026-11-19 Codex

- 인수 HEAD는 `efd28dc`. 기존 미커밋 변경에는 에디터 에셋 배정, 시각 카탈로그, 전투·작업자 표시, 채굴 공장 스키마 및 생성 코드, 테스트가 섞여 있다.
- 미추적 파일은 `Assets/QuantumUser/View/FactoryWars/Battle/BaseAmbientWorkers.cs`, `Assets/Tests/WarFxVerificationTests.cs` 및 각 `.meta`다.
- 자체 Factory Wars 시뮬레이션 C#과 QTN에서 결정론 금지어 검색 결과 일치 0건. `git diff --check`는 기존 VisualCatalog YAML과 Quantum 생성 이벤트 파일에서 줄 끝 공백을 보고했다.
- Codex 작성 파일은 `Docs/ai/`의 상태, 계획, 인계, 결정 문서다.

### 기준선 결과

Unity 6000.0.85f2에서 변경 전 어셈블리로 전체 PlayMode를 실행했다. 532건 중 통과 377, 실패 38, 건너뜀 117. 원본은 작업 루트의 `Game-logs/codex-baseline-playmode.log`와 `Game-logs/codex-baseline-playmode.xml`이다. 실패에는 지도 배치, AI 외교, 수송 파괴, HUD, War FX 용접 슬롯 연결, 작업자 배치, 구입 팩 해시 검사가 포함된다. 그래픽 관련 실패는 GPU 실행으로 재현 여부를 확인한다.

### 현재 작업과 다음 명령

- `GameplaySystems.cs`에 노드 스냅, 중복 방지, 고정소수점 생산과 실제 노드 차감을 연결했다. `CitySystem.cs`와 기존 주둔 명령에 공병 전용 배치·방출을 넣고 `MatchInput.cs`, `MatchHud.cs`를 연결했다.
- `FactoryWarsSetup.BuildExtractorData`는 기존 진영과 규칙을 확장하는 한정된 에디터 명령이다. 자동 검증 로그는 `Game-logs/codex-extractor-data.log`, 집중 PlayMode 결과는 `Game-logs/codex-extractor.xml`에서 확인한다.
- `BaseAmbientWorkers.cs`는 완공 시설 중 작업자와 자원 잔량이 있는 시설을 물류 목적지에 포함한다. 전용 운반 소품·기계 동작, 장애물 회피, 애니메이션·실기기 검증은 남아 있다.
- 새 PlayMode 케이스는 `EconomyCombatTests.cs`의 `Extractor_SnapsToIronNodeAndProducesOnlyWhileStaffed`다. 기준선 회귀와 새 실패를 구분한 다음 전체 PlayMode를 재실행한다.

## 2026-11-20 채굴 시설 검증

- 컴파일과 한정 데이터 생성 성공. 양 진영 제재소·철 채굴 공장 4개와 각 메타, 양 진영 건설 목록, 규칙, View카탈로그 소품 참조를 저장했다. 로그는 `Game-logs/codex-extractor-verified-data.log`.
- 집중 PlayMode 2/2 통과. 철 노드 스냅·중복 거절, 무인·퇴거 후 소비 정지, 배치 중 철 재고와 노드 잔량 변화, 4명 상한과 초과 거절 및 퇴거를 확인했다. XML은 `Game-logs/codex-extractor-verified.xml`.
- GPU 캡처 1/1 통과 후 `Game-logs/codex-staffed-iron-extractor.png`를 열어 확인했다. 철 채굴 공장 렌더링을 확인했다. 작업자 동선·손 파지·발 미끄러짐은 정지 이미지로 확인할 수 없다.
- `BaseAmbientWorkers.cs`에 가동 시설과 본부 왕복, 목재·광석을 나타내는 소품, 건물 부지 우회, 원거리 갱신 제한을 추가했다. 시각 품질과 모바일 성능은 검증 대기다.
- 중간 버전 전체 회귀 `Game-logs/codex-extractor-regression.xml`은 535건 중 377 통과, 40 실패, 118 건너뜀. 기준선 실패 38건이 그대로 재현됐고, `Build_PanelFitsAllFactionBuildings`와 `Visual_CatalogHasSlotForEveryElement`가 추가로 실패했다. 후속 코드에서 자원 채굴 전용 메뉴와 분류 검증, Synty 시설 슬롯 생성 경로를 추가했다. 슬롯 에셋 생성 및 두 검사 재실행이 필요하다.
- 회귀 실행이 컴파일한 버전 이후에 봇 건물 역할 배열 크기와 배치 공병 제외, 채굴 노드 장식 복구, 안개 가시성, 제재소 테스트를 보완했다. 해당 보완분은 전체 회귀가 끝난 뒤 `Game-logs/codex-extractor-followup.xml`과 `Game-logs/codex-extractor-followup-visual.xml`에서 확인한다.
- 후속 코드에는 버튼 1회당 공병 한 명 퇴거, 기존 채굴 교란 배율과 전후 누적 자원 반영이 포함된다. 시설 파괴 시에는 기존 긴급 퇴거 경로에서 모든 공병을 처리한다.
- `BuildExtractorData`는 `SyntyAssign.BuildLivingBaseVisuals`를 통해 작업자·비계·운반 소품과 시설 슬롯만 생성·연결한다. 기존 파생 프리팹이나 Animator가 있으면 재사용한다. 자산 생성과 작업자·건설 GPU 결과는 `Game-logs/codex-living-base-assets.log`와 `Game-logs/codex-living-base.xml`에서 확인한다.
- 후속 배치 실행은 Unity 6.6에서 열린 `FactoryWars - SampleScene` 편집 창이 종료되기를 기다린다. 사용자가 저장·종료한 뒤 후속 로그의 시각과 XML을 확인한다. 대기 명령이 만료됐으면 Unity 6000.0.85f2에서 `-executeMethod FactoryWars.EditorTools.FactoryWarsSetup.BuildExtractorData`를 먼저 실행한다. 이어서 PlayMode 필터 `Extractor_|Build_PanelFitsAllFactionBuildings|Visual_CatalogHasSlotForEveryElement`와 GPU 필터 `CaptureStaffedIronExtractor|ConstructionStages_AndAmbientWorkersLifecycle|Kenney_BaseWorkersWalk`를 실행한다.
- 사용자 첨부 카탈로그에서 공병의 소총 자세와 로켓병 발사관 견착 문제가 확인됐다. `BattleView.cs`, `ArtView.cs`, `ViewCatalog.cs`, `UnitAnimatorBridge.cs`, `RiflemanRig.cs`, `SyntyAssign.cs`, `UnitCatalogCaptureTests.cs`를 수정했다. 작업자 Animator 바인딩 후 양 진영 공병·소총병·로켓병을 전투 화면과 카탈로그에서 각각 육안 확인한다.

## 2026-11-20 건설과 사령부 이송

- 작업자 Animator, Synty 시설 슬롯, 건설 비계와 이송관을 `BuildExtractorData`로 재적용했다. 확인한 구매 팩 원본 12개는 SHA-256이 이전과 같았다.
- `ConstructionWorkView.cs`는 실제 공병과 현장 작업자의 반복 도구 동작을 그린다. `BattleView.cs`는 건설 진행률을 보간하고 작업 반경과 임무를 읽는다. 완공 시 모델의 바닥 보정값을 복원한다.
- `ExtractorDeliveryView.cs`는 완공 시설을 같은 진영의 가장 가까운 사령부에 연결하고, 시설 가동 중 목재 또는 철광석 화물을 이송한다. 자원 지급 시점과 결정론 시뮬레이션에는 영향이 없다.
- `BaseAmbientWorkers.cs`는 경로 탐색 주기와 위치 갱신 주기를 분리한다. 60fps 제한 PC 캡처에서 공병 도구 47/48, 화물 30/30, 원거리 작업자 36/36 프레임 이동. 최대 프레임 간격 16.8ms.
- Unity GPU PlayMode `Game-logs/codex-delivery-final.xml` 8/8 통과. 건설 메뉴와 카탈로그 슬롯의 이전 신규 실패도 통과했다. 최신 작업의 전체 회귀, Android 장기 성능, 로켓병 등 전투 유닛 카탈로그 재촬영은 별도 검증 항목이다.
