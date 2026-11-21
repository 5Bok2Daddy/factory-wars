# Factory Wars 인수인계

> **2026-11-21 저녁 갱신(Claude):** 최신 상태와 남은 일은 `SETUP-REPORT.md`의 "현재 단계 · 2026-11-21 (Claude)"가 기준이다. 아래 본문은 오전 Codex 인수 시점 기록이다. 전체 PlayMode는 564건 중 411 통과·23 실패·130 건너뜀, 실서버 체크섬 561회 일치·디싱크 0. 다음 작업은 Strategy UI Kit 통합이다.

2026-11-21 KST · 인수인계 전 기준 커밋 `42c25ab` · 브랜치 `main`

작업 루트는 `C:\Users\MinGyu\FactoryWars`이고 Unity 프로젝트 및 Git 저장소 루트는 그 아래 `Game\`이다. 아래 `Assets/`, `Docs/`, `Tools/` 경로는 `Game\` 기준이다. `CLAUDE.md`, `AGENTS.md`, `OVERNIGHT-QUEUE.md`, `Game-logs\`, `DevelopmentReports\`, `temp-asset\`과 `temp-assets\`는 상위 작업 루트에 있다. 다른 에이전트는 먼저 `CLAUDE.md` 전체, 대기열, `SETUP-REPORT.md`의 **현재 단계**를 읽고 `git status`, `git log --oneline -20`, `git diff --stat`을 확인한다.

## 1. 목표와 완료 기준

기존 작업을 보존하면서 Unity 6000.0.85f2와 Photon Quantum 3 기반 모바일 RTS의 5개 맵·공장 야드·자원 생산·작업자 연출을 완성하고 검증한다. 표준은 군용 시설 경계, 다도해와 해협은 지형에 붙는 해안과 교량, 사막은 Canyon 암석과 군용 야전 시설로 구분한다. 공병은 자원 지점에 시공하고 시설에 배치되어 생산을 시작하며 퇴거할 수 있어야 한다.

- 담당 에셋이 URP에서 정상으로 보이고 도로·해안·교량·건물·차량 출입 경로가 실제 경기에서 자연스럽게 연결된다. 마젠타 셰이더, 흰색 임시 도형, 작업자와 운반 장비의 관통, 공장 출고 시 차량 관통을 화면과 이동 표본에서 확인한다.
- 5개 맵의 지상·수상 NavMesh, 전체 PlayMode 100% 통과 목표, 별도 GPU·Photon 및 Android 실기기 검증을 충족한다. 실서버 체크섬 비교에서 디싱크 0건을 확인한다. 전체 실행에서 제외되는 `Explicit` 검사는 필요한 케이스를 명시적으로 선택해 결과를 남긴다.
- 검증된 PNG, 테스트 결과와 남은 조건을 `Docs/status_images/`, `README.md`, `SETUP-REPORT.md`에 기록하고 원격 `origin/main`에 동기화한다.

## 2. 완료한 작업

| 작업 | 구현·검증 위치 |
| --- | --- |
| 이전 미커밋 작업 복구와 보존. 완공 공병 자동 배치, 시설 생산·퇴거, 기지 작업자의 근무·휴식·복귀 확인. | `7900bb6`, `ad81652`, `264f82f` · `Assets/QuantumUser/Simulation/FactoryWars/Systems/{GameplaySystems,WorkerSystem,CitySystem}.cs` · 상위 `DevelopmentReports/FactoryWars_Status.md` |
| EGG 32개 볼륨에서 파일 100개를 풀고 Desert World, Backroad Bridges, NatureForge Woodland·Meadow를 선별했다. 표준·다도해·해협 교량, 표준 목재 노드와 사막에 적용했다. | `Tools/import_new_bundle.py`, `Assets/Editor/FactoryWars/NewBundleTools.cs`, `Assets/QuantumUser/View/FactoryWars/Art/NewBundleArt.cs` · 스테이징 `../temp-asset/new bundle/_extracted/` |
| 표준의 호안 기둥을 철조망·안전 난간·저지벽으로 교체했다. 다도해·해협 수면 띠를 정리하고 암석·해변·부두를 배치했다. 사막의 고층 외형을 Canyon 암석과 낮은 군용 시설로 교체했다. | `Assets/QuantumUser/View/FactoryWars/Art/{MapPolishArt,TerrainKit,TerrainLook,UrbanDecor,Dressing}.cs`, `Assets/QuantumUser/View/FactoryWars/Battle/{WorldView,CityView}.cs` |
| 공장 콘크리트 야드, 적재장까지 짧아진 컨베이어, 작업자 우회, 배치 3칸과 `공병 빼기` HUD를 적용했다. 철광 시설 2개의 정원 데이터가 3이다. | `Assets/QuantumUser/View/FactoryWars/Battle/{BaseAmbientWorkers,BattleView,ExtractorDeliveryView}.cs`, `Assets/QuantumUser/View/FactoryWars/MatchHud.cs`, `Assets/QuantumUser/Resources/FactoryWars/{ff_iron_extractor,cf_iron_extractor}.asset`, `Assets/Editor/FactoryWars/FactoryWarsSetup.cs` |
| Mixamo·Axe FBX 162개를 스테이징했다. 34개를 Humanoid로 가져왔고 23개를 운반·시공·채굴·보병 동작에 연결했다. | `Tools/import_mixamo.py`, `Assets/Editor/FactoryWars/MixamoTools.cs`, `Assets/QuantumUser/View/FactoryWars/Art/{MixamoMotion,ConstructionWorkView,RiflemanRig,UnitAnimatorBridge}.cs` |
| 5개 맵 지상·수상 NavMesh를 재베이크하고 맵·NavMesh를 온라인 버전 해시에 포함했다. GPU로 9장을 촬영·검수했고 최종 Photon 실서버 확정 프레임 586개가 일치했다. | `Assets/QuantumUser/Scenes/`, `Assets/QuantumUser/View/FactoryWars/Online/BuildVersion.cs`, `Assets/Tests/{MapPoolTests,OnlineMatchTests,UnitCatalogCaptureTests}.cs` · `Docs/status_images/` · 상위 `DevelopmentReports/Screenshots/map-polish/` |
| 회귀 원본의 실패 25건과 `Explicit` 125건을 전수 대조해 대시보드를 갱신했다. | `README.md`, `Docs/FactoryWars_Status.md`, `Docs/FactoryWars_PlayMode_2026-11-21.md`, `SETUP-REPORT.md` · `42c25ab` |

## 3. 중단 지점

- 마지막 전체 PlayMode는 **548건 중 398 통과, 25 실패, 125 건너뜀**이다. 기존 실패 23건이 재현됐다. 저품질 장식 밀도와 AI 외교 비교 시점을 보완한 뒤 `88a285b`의 후속 집중 테스트 **5/5**를 통과했다. 후속 수정이 반영된 전체 테스트 실행까지 진행하면 된다. 상세 케이스와 기대값·관측값은 `Docs/FactoryWars_PlayMode_2026-11-21.md`에 있다.
- `Explicit` 건너뜀은 전체 실행의 결과다. 같은 작업 주기의 별도 실행에서 GPU 캡처 6건과 Photon 실서버 2건이 통과했다. 장시간·AI 배치·기기 케이스의 증거는 상세 보고서에서 확인한다.
- Mixamo 11개는 Rig 준비 단계에 있다. `OpenLid`, `CloseLid`, `Push`, `Pull`, `DeathProne`, `Aid`, `InjuredIdle`, `InjuredRun`, `Drive`, `ExitVehicle`, `Pilot`의 경기 상태 연결이 남았다. 수류탄·경례 등 추가 동작은 대응 이벤트 또는 요구를 확인하고 진행한다.
- 데스크톱 GPU에서 작업자와 컨베이어 동작, 공병 배치, 정지 차량 사이의 간격을 확인했다. 공장에서 나온 전차의 출고 전 구간, Android 터치·발열·프레임 시간은 미검증이다. 현재 변경분을 포함한 Android APK 재빌드 결과도 없다.

## 4. 남은 작업과 우선순위

1. 이어받은 브랜치에서 `Urban_DensityByQuality`, `AiDiplomacy_SameSeedSameRelations`, `CaptureNewBundleMapArt`, `Online_Live_SessionChecksumsMatch`의 기준선을 확인한다. 전체 PlayMode도 다시 실행해 `88a285b` 후속 수정이 포함된 548건 집계를 확보한다.
2. 실패 23건의 실제 결함과 오래된 테스트 가정을 확인한다. 먼저 `MapStrait` 중앙 수역의 자원 노드 `(-10, -118)`, 도시 주둔 `Invalid` 거절, 항모 시나리오 180초 제한과 미사일 사용 0회·반경 피해를 재현한다. 이어 AI 확장·군수공장·외교·인구·테크와 아트 카탈로그 항목을 진단한다. 맵·시뮬레이션 데이터 변경은 사용자 지시의 범위를 확인한다.
3. 공장 건설→완공→차량 출고의 위치를 프레임별로 검증한다. 제재소·철광 시설 주변 작업자 동선과 실제 벽·컨베이어·도로 겹침도 확인한다. 포격함·사령부·차량 공장 렌더링 예산을 점검하고 Android 개발 빌드로 터치 영역·프레임·발열·GC·장시간 안정성을 측정한다.
4. 실제 경기 상태와 맞는 남은 Mixamo 클립을 연결한다. 애니메이션이 손·도구·무기의 접점과 충돌하지 않는지 확인한다. 해당 변화가 있는 5맵과 기지 화면을 다시 캡처하고 대시보드·상태 보고를 갱신한다.

## 5. 결정 사항

- 공병의 소유권·배치·생산은 Photon Quantum의 FP로 처리한다. 장식 물류 작업자, 운반품, 컨베이어와 애니메이션은 View에 둔다. 맵 외형과 애니메이션 개선에서 시뮬레이션 소스를 수정하지 않는 것이 사용자 지시다.
- 구매 원본은 로컬에서 읽고 필요한 파생 프리팹·머티리얼만 생성한다. `temp-asset/`, `temp-assets/`, `.egg`, `.zip`, `.unitypackage`와 구매 에셋 파생 폴더는 Git 제외 규칙을 따른다. 새 환경 에셋은 현대 산업·전술 테마로 선별했고 Cartoon City 등 나머지는 스테이징에 두었다.
- 울타리·난간·부두는 낮은 그래픽 품질에서도 유지한다. 주변 장식은 품질별 유지 비율을 적용한다. AI 외교 결정론 검사는 화면 갱신 타이밍의 영향을 피하도록 정확히 같은 Quantum 틱을 비교한다.
- 화물 이송 연출은 가까운 적재장까지 표시한다. 장식용 컨베이어가 통행로를 가로지르는 경우를 줄이고 기지 작업자가 우회할 수 있게 하기 위한 설정이다.

## 6. 주의사항과 알려진 버그

- 기존 작업 보존이 최우선이다. `git reset --hard`, `git clean -fd`, 강제 체크아웃·푸시를 금지한다. 동기화 스크립트의 `-PreserveExisting`을 사용해 기존 캡처와 기록을 보존한다. Unity 프로세스는 에디터·배치 포함 한 개만 실행한다.
- 분할 EGG는 Bandizip CLI `C:\Program Files\Bandizip\bz.exe`로 확인·해제했다. 최초 볼륨은 `../temp-assets/new bundle/assets.vol1.egg`이고, 평소 작업은 기존 `_extracted`를 재사용한다. ALZip EGG를 일반 ZIP처럼 `Expand-Archive`나 7-Zip으로 처리하는 절차는 사용하지 않는다.
- 시뮬레이션에서 `float`, `double`, `Mathf`, `UnityEngine.*`, 시스템 난수·시간 API를 금지한다. SDK, 에셋 팩 원본, 사용자 데이터, Photon 대시보드와 계정 설정을 보호한다. 외부 에셋 임의 다운로드 및 결제·구독 변경을 금지한다.
- 로컬 구매 에셋이 없는 클론에서는 Mixamo·구매 번들 외형이 재현되지 않는다. Git과 캡처에 있는 읽기 자료를 확인한 뒤 승인된 로컬 원본을 준비한다. Unity에서 실제 게임 캡처를 촬영하고, 색·스케일·도로와 물가 접합을 직접 확인한다.
- 시각 예산 검사에서 양쪽 포격함은 각각 25,457 삼각형·35 드로우 호출로 예산 4,000·6을 초과했다. Synty 팩 인벤토리 차이 32건과 건물 슬롯의 기대 40 이상·관측 20도 보고됐다. `Ultimate_DeterministicAcrossRunsAndOnline`은 미사일 발사 전제 조건에서 중단됐으므로 그 케이스의 결정론은 미확정이다. 값이나 테스트 조건을 바꾸기 전에 원본 기획·에셋 기준을 확인한다.
- `AndroidBuild.Run`은 `../Builds/FactoryWars-dev.apk`와 `../Builds/FW-Release-Full.apk`를 갱신한다. 이전 APK를 보존해야 하면 먼저 다른 이름으로 복사한다. `FW-Release-Full.apk`는 이 루틴에서 개발 빌드 APK의 복사본으로 작성된다.

## 7. 실행·빌드·테스트

### Unity 실행

Unity Hub에서 `Game\`을 Unity **6000.0.85f2**로 연다. `Assets/QuantumUser/Scenes/Menu.unity`에서 Play로 진입한다. 메뉴에서 로컬 연습을 선택할 수 있다. 씬 목록은 `ProjectSettings/EditorBuildSettings.asset`의 `Menu`, `Map4P`, `MapDesert`, `MapUrban`, `MapArchipelago`, `MapStrait`다.

### 배치 실행 준비와 PlayMode 검사

작업 루트 `C:\Users\MinGyu\FactoryWars`에서 PowerShell로 실행한다. 종료 코드는 Unity 결과이고 테스트별 판정은 XML에 있다. 전체 기준선은 실패가 있어 Unity가 0 이외의 종료 코드를 반환할 수 있다.

```powershell
$Unity = 'C:\Program Files\Unity\Hub\Editor\6000.0.85f2\Editor\Unity.exe'
$Project = (Resolve-Path 'Game').Path
$Logs = (New-Item -ItemType Directory -Force 'Game-logs').FullName
function Invoke-FactoryUnity([string]$UnityArguments) {
    if (Get-Process Unity -ErrorAction SilentlyContinue) { throw 'Unity already running' }
    $process = Start-Process -FilePath $Unity -ArgumentList ('-projectPath "{0}" {1}' -f $Project,$UnityArguments) -Wait -PassThru -WindowStyle Hidden
    Write-Host "Unity exit code $($process.ExitCode)"
}

Invoke-FactoryUnity ('-batchmode -nographics -runTests -testPlatform PlayMode -testResults "{0}\handoff-full.xml" -logFile "{0}\handoff-full.log"' -f $Logs)

$env:FW_CAPTURE_DIR = Join-Path $Logs 'handoff-captures'
$filter = 'Urban_DensityByQuality|AiDiplomacy_SameSeedSameRelations|CaptureNewBundleMapArt|Urban_BridgeMatchesSimulationState|Online_Live_SessionChecksumsMatch'
Invoke-FactoryUnity ('-batchmode -runTests -testPlatform PlayMode -testFilter "{0}" -testResults "{1}\handoff-focused.xml" -logFile "{1}\handoff-focused.log"' -f $filter,$Logs)
```

### 로컬 에셋 준비·NavMesh 및 Android 빌드

필요한 명령만 실행한다. Synty 원본이 없는 클론에서는 승인된 로컬 팩으로 `python Game/Tools/import_synty.py`를 실행한다. 초기 파생 컨트롤러와 소품 연결은 Unity 메뉴 **Tools → Factory Wars → Assets → 5. Assign Synty Pack**, **6. Assign Urban Dressing** 순으로 준비한다. Mixamo 컨트롤러 준비는 Synty 파생 컨트롤러가 있어야 한다. NavMesh 에디터 명령의 저장 결과는 Git에서 검토한다.

```powershell
python Game/Tools/import_new_bundle.py
python Game/Tools/import_mixamo.py
Invoke-FactoryUnity ('-batchmode -nographics -quit -executeMethod FactoryWars.EditorTools.NewBundleTools.Prepare -logFile "{0}\handoff-bundle.log"' -f $Logs)
Invoke-FactoryUnity ('-batchmode -nographics -quit -executeMethod FactoryWars.EditorTools.MixamoTools.Prepare -logFile "{0}\handoff-mixamo.log"' -f $Logs)
Invoke-FactoryUnity ('-batchmode -nographics -quit -executeMethod FactoryWars.EditorTools.NewBundleTools.PrepareMapPolish -logFile "{0}\handoff-polish.log"' -f $Logs)
Invoke-FactoryUnity ('-batchmode -nographics -quit -executeMethod FactoryWars.EditorTools.NewBundleTools.RebakeFiveMaps -logFile "{0}\handoff-navmesh.log"' -f $Logs)
Invoke-FactoryUnity ('-batchmode -nographics -buildTarget Android -executeMethod FactoryWars.EditorTools.AndroidBuild.Run -logFile "{0}\handoff-android.log"' -f $Logs)
```

검증한 변경을 동기화한다.

```powershell
powershell -ExecutionPolicy Bypass -File scripts/sync_github.ps1 -PreserveExisting -Message '[claude] Describe verified Factory Wars changes'
git -C Game status -sb
git -C Game log -n 2 --oneline
```

## 8. 현재 상태와 근거

| 항목 | 확인된 상태 |
| --- | --- |
| Git | 문서 작성 전 `main`·`origin/main`이 `42c25ab`로 일치했고, 미커밋 변경 및 스테이징은 0건이었다. 인수인계 문서는 별도 커밋으로 남긴다. |
| 에디터 컴파일·실행 | Unity 6000.0.85f2에서 5맵 NavMesh 재베이크와 최종 GPU·Photon PlayMode가 실행됐다. 인수인계 문서 작업에서는 새 빌드나 테스트를 실행하지 않았다. |
| 전체 PlayMode | 2026-11-21 11:36:07 KST 종료. 548건 중 398 통과·25 실패·125 `Explicit` 건너뜀. |
| 후속 집중 검사 | 11:42:58 KST 종료. 5/5 통과. AI 외교, 저품질 장식 밀도, 시가지 교량, GPU 5맵, Photon 실서버를 포함한다. 실서버 확정 프레임 586개 일치, 디싱크 0건이다. |
| 원본 증거 | 상위 `Game-logs/map-polish-full.xml`, `map-polish-followup-final.xml`, `map-polish-navmesh.log`, `mixamo-prepare-verified.log`. 캡처 9장은 `Docs/status_images/`와 상위 `DevelopmentReports/Screenshots/map-polish/`에 있다. |
| 최신 Android APK·기기 | 현재 변경 기준의 새 빌드 및 기기 검증 기록이 없다. |
