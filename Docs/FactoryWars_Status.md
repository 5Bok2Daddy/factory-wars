# FactoryWars 복구 및 진행 현황

2026-11-20 KST · 인수 검증 및 보존 기록

## 인수 상태

- Unity 6000.0.85f2 · Photon Quantum 3 · Android/iOS용 모바일 RTS. Git 저장소는 `Game/`이다.
- 로컬 `main`과 실제 GitHub `origin/main`은 `efd28dc`에서 일치했다. 마지막 커밋은 2026-11-19 18:46:17 KST의 Synty·War FX·5맵 작업이다.
- 인수 당시 수정된 추적 파일 71개, 새 파일 39개, 스테이징된 변경 0개였다. 추적 파일 diff는 3,999줄 추가, 857줄 삭제다. Unity 씬·프리팹·애니메이션·Animator Controller의 직접 Git 변경은 0개였다. 외형은 카탈로그, 파생 프리팹, View에서 연결한다.
- 변경은 Quantum DSL 및 생성 파일, 공병·자원 시설, 비행장·조선소, 기지 연출 및 오디오, Synty·지형·HUD 카탈로그, 4개의 자원 시설 에셋과 메타, 테스트, 문서 및 캡처에 걸쳐 있다. 이전 로컬 작업은 `staging/recovery-2026-11-20/`의 파일 목록과 추적 바이너리 패치에도 보관했다.
- 2026-11-20 09:26 KST Codex 세션(`01a1f658-decf-7622-9b36-a56f23acfce1`), `Game/Docs/ai/HANDOVER.md` 및 Unity 로그를 대조했다. 음향 집중 검증은 16:53 KST에 13/13 통과했다. 이후 17:05 KST까지 로컬 Synty 카페 및 Asphalt 02 노멀 가져오기가 정상 종료했다. 에셋 원본·중간 인벤토리·로그는 로컬에 있다.

## 인수 시 기능 검증

| 기능 | 분류 | 확인 결과 |
| --- | --- | --- |
| 사령부와 기본 건물 | 완료 및 실제 동작 확인 | 로컬 경기에서 사령부 생성, 공장 관리와 증축, 두 공병의 공사 가속을 재검증했다. |
| 공병 생성과 이동 | 완료 및 실제 동작 확인 | 경기 시작 공병이 자원 노드까지 이동해 시공했다. |
| 공병의 자원 시설 건설 | 완료 및 실제 동작 확인 | 철 노드 스냅, 중복 배치 거절과 완공, 목재 시설의 건설을 검증했다. |
| 자원 시설 내부 공병 배치 | 일부 구현 | 기존 수동 명령으로 정원과 퇴거, `Unit.Garrison` 및 `WorkerTask.Station`을 확인했다. 완공 공병 자동 진입이 남아 있었다. |
| 자원 채취와 생산 | 완료 및 실제 동작 확인 | 배치 중 두 자원 생산, 노드 잔량·재고 일치, 무인 상태의 생산 정지를 확인했다. |
| 사령부 주변 작업자 이동 | 완료 및 실제 동작 확인 | 보통 품질에서 작업자 모델, 뼈대 애니메이션, 원거리 렌더 프레임 이동을 재검증했다. |
| 작업자 순환 근무 및 휴식 | 일부 구현 | 기존 운반과 적재 지점 대기. 개별 근무 시간과 별도의 휴식 목적지는 개발 대상이다. |
| 유닛 이동 및 애니메이션 | 완료 및 실제 동작 확인 | 이동·집결·순찰, 공병과 장식 작업자의 뼈대 동작을 확인했다. 보병 달리기·엄폐는 기존 22/22 검증에도 포함됐다. |
| 자원 채취 모션과 이펙트 | 일부 구현 | 철광산 공구 동작과 벨트·광석 이동을 GPU 프레임으로 확인했다. 제재소 현장 동작과 Android 성능은 검증 대기다. |
| RTS 카메라 및 유닛 선택 | 완료 및 실제 동작 확인 | 카메라 경계와 줌, 선택 후 공장 HUD를 확인했다. 기존 13/13 검증에 유닛 선택 링 검사가 포함됐다. |
| UI 및 자원 표시 | 완료 및 실제 동작 확인 | 자원바와 미니맵, 공장 관리·증축을 GPU 화면에서 확인했다. |
| Photon Quantum 멀티플레이 동기화 | 구현되었지만 검증 필요 | 기존 오프라인 방 코드·버전·재접속 검증 기록 3/3, 자체 시뮬레이션의 결정론 금지 표현 0건. 현 작업 트리의 실제 Photon 경기 체크섬과 롤백 검증은 남았다. |

## Unity 결과와 스크린샷

Unity 6000.0.85f2에서 C# 컴파일 후 GPU 기반 PlayMode **13/13 통과**. `Game-logs/recovery-before.xml`과 `recovery-before.log`에 결과가 있다. 이번 전체 검사는 초기 컴파일과 씬 실행을 확인한 다음 집중 검증을 위해 종료했다. 기존 전체 기록은 542개 중 387 통과, 34 실패, 121 건너뜀(`codex-yard-full.xml`)이며 기존 기준선의 38개 실패 중 4개가 해소된 상태였다. 개발 후 전체 검사를 다시 수행한다.

- 5맵 경기 시야와 사령부·장식 작업자, 공병 시공, 공병 배치 후 광석 이송, 자원 HUD는 `Docs/status_images/recovery/before/`에 있다.
- 화면 이름은 `map_*.png`, `headquarters-workers.png`, `engineer-construction.png`, `staffed-iron-extractor.png`, `factory-hud.png`다. `map_*.png`는 대표 구역의 경기 카메라 화면이다.
- 공병 건물 진입은 기존 PlayMode에서 상태를 확인했다. 입구 전환의 독립 화면은 개발 단계에서 촬영한다.

## 인수 당시 개발 계획

1. 검증된 로컬 변경을 보존하고 `origin/main`에 동기화한다.
2. 건설을 끝낸 공병이 배치되도록 기존 Quantum 진입 경로를 재사용한다.
3. 장식 작업자가 작업·휴식·복귀를 반복하도록 View의 운반 동작과 휴식 경로를 연결한다.
4. 건설과 배치 전환, 작업자 동작을 GPU에서 재검증하고 최신 전체 PlayMode와 온라인 체크섬을 실행한다.

## 개발 재개 결과

| 기능 | 분류 | 확인 결과 |
| --- | --- | --- |
| 완공 공병의 자원 시설 자동 배치 | 완료 및 실제 동작 확인 | 완공 틱을 처리한 공병이 Quantum 진입 경로를 타고 자신이 지은 시설에 배치된다. 추가 공병 수동 배치와 한 명씩 퇴거도 유지된다. 철광산·제재소의 소유자 및 배치 엔티티를 검사했다. |
| 자원 생산과 중단 | 완료 및 실제 동작 확인 | 시설 배치에 따라 광석·목재를 재고에 반영한다. 기존 생산량과 정원 데이터를 재사용했다. 퇴거 후 생산과 이송이 멈추는 것을 확인했다. |
| 기지 작업자의 순환 근무와 휴식 | 완료 및 실제 동작 확인 | 개별 근무 시간, 적재·하역 동작, 사령부 주변 휴식 지점, 업무 복귀를 View에서 처리한다. 경기 중 일부 작업자가 쉴 때 다른 작업자가 활동했고 휴식 인원이 다시 줄었다. |
| 공병 이동·시공·진입과 시설 가동 | 완료 및 실제 동작 확인 | Synty 모델·Idle/Walk와 기존 공구를 재사용했다. 시공 도구 이동, 공병의 진입 이동과 월드 모델 숨김, 철광산 벨트 및 제재소 톱 회전과 퇴거 후 정지를 확인했다. |
| 건물 내부 작업 및 모바일 성능 | 일부 구현 | 건물 내부의 공병은 Quantum 엔티티로 배치된다. 내부 전용 카메라·모션은 없으며 외부 설비로 가동을 표시한다. Android 실기기 성능은 연결된 장치가 없어 미측정이다. |

### 검증 화면

| 화면 | Unity GPU 캡처 |
| --- | --- |
| 표준 4인 맵 전역 | [전체 맵](status_images/recovery/after/full-standard-map.png) |
| 사령부·주변 장식 작업자 | [기지와 작업자](status_images/recovery/after/headquarters-workers.png) |
| 공병의 건설과 시설 진입 | [시공](status_images/recovery/after/engineer-construction.png) · [진입](status_images/recovery/after/engineer-entry.png) |
| 인력 배치 후 자원 생산 | [철광산과 이송](status_images/recovery/after/staffed-iron-extractor.png) · [제재소](status_images/recovery/after/staffed-lumber-mill.png) |
| 자원 표시·미니맵·공장 선택 UI | [경기 HUD](status_images/recovery/after/factory-hud.png) |

캡처는 Unity 6000.0.85f2 GPU PlayMode의 실제 프레임이다. 60fps 제한 표본에서 시공 도구 47/48, 광석 이송 30/30, 원거리 장식 작업자 36/36 프레임에서 위치 변화가 측정됐다. 표본 최대 프레임 간격은 각각 16.9ms, 16.9ms, 19.6ms였다.

### Unity와 Photon Quantum

- Unity 6000.0.85f2 최종 집중 실행에서 C#·셰이더 컴파일 오류 0건, GPU 및 실서버 PlayMode 16/16 통과. 결과는 `Game-logs/recovery-final-focused.xml`, 실행 로그는 `Game-logs/recovery-final-focused-retry.log`다.
- Photon 서울 서버에서 방 생성과 참가, 없는 방 거부, 준비 상태 전달을 확인했다. 두 클라이언트가 동일한 확정 프레임의 체크섬 590개에서 일치했다. 시공 공병의 자동 배치와 소유자 철 생산을 상대 클라이언트에서도 확인했고 디싱크 콜백은 0건이었다. 관측 핑은 16/16ms였다. 두 클라이언트는 동일 PC에서 실행했다.
- 자체 Quantum 시뮬레이션과 DSL의 금지 타입·API·시각 참조 정적 검색은 0건이다. 최신 전체 PlayMode는 그래픽 없이 **546건 중 398 통과, 25 실패, 123 건너뜀**이다. 결과는 `Game-logs/recovery-final-full.xml`에 있다. 자동 배치, 자원 생산·퇴거, 장식 작업자 휴식·이동, 공병 선택·이동, 자원바 테스트가 통과했다.
- 이전 전체 실행(`codex-yard-full.xml`)과 비교하면 실패 24건이 이어졌고 7건은 같은 이름에서 통과했다. 이전 실패 3건은 이름 또는 검사 방식이 바뀌었다. 새로 실패한 `Ultimate_MissileIgnoresCoverAndCollapsesCity`는 착탄 시 엄폐 상태 검사에서 실패한 뒤 같은 실행 옵션의 후속 검사 두 번에서 통과했다.
- `Cover_DeterministicAcrossRuns`는 전체 검사에서 두 로컬 실행의 체크섬이 일치했고 후속 두 번에서는 달랐다. 임시 계측에서 두 실행의 이동 명령이 각각 틱 83과 69에 적용된 것을 확인했다. 계측 코드는 제거했다. 테스트 재현 시 명령 실행 틱부터 고정한다. 후속 결과는 `Game-logs/recovery-full-followup.xml`, `recovery-full-followup-repeat.xml`, `recovery-cover-diagnostic.log`에 있다.
- 기존 잔여 실패에는 맵·자원 배치, AI·경제, 에셋 카탈로그, 수송 및 궁극기 문제가 포함된다. 휴전 중 도시 주둔 검사는 현재 표준 맵에 주둔 대상으로 지정된 도시 건물이 없어 유효하지 않은 대상을 보낸다. 포격함 기준선에는 25,457 삼각형과 35 드로우가 보고됐으며 예산은 각각 4,000과 6이다.

### 개발 재개 변경 파일

- 시뮬레이션 · `FactoryWarsCommands.cs`, `CitySystem.cs`, `GameplaySystems.cs`, `WorkerSystem.cs`
- 화면 · `BaseAmbientWorkers.cs`, `BattleView.cs`
- 검증 · `EconomyCombatTests.cs`, `MapPoolTests.cs`, `OnlineMatchTests.cs`, `UnitCatalogCaptureTests.cs`, `WorkerAnimationTests.cs`
- 문서·증거 · 양쪽 README, SETUP-REPORT 세 사본, 이 보고서와 GitHub 사본, `Docs/status_images/current_base_alive.png` 및 `recovery/after/`
- 로컬 동기화 · `scripts/sync_github.ps1`에 `-PreserveExisting`을 추가했다. 기존 캡처·보고서 이력과 Git 작업을 보존하면서 일반 푸시를 사용한다.

### Git과 후속 확인

- 인수 상태를 `7900bb6` (`[claude] Preserve recovered Codex work and verify extraction baseline`)로 커밋해 20:57 KST에 `origin/main`으로 푸시했다. Git에서 업스트림과 일치하며 작업 트리가 깨끗하다고 확인했다.
- 자동 배치, 순환 근무, 테스트와 최신 캡처는 `ad81652` (`[claude] Staff completed extractors and add ambient worker shifts`)로 21:07 KST에 `origin/main`에 푸시했다. README 이미지 링크 85개를 Git 추적 경로의 대소문자까지 확인했다. 최종 캡처 7개는 로컬 보고서용과 GitHub용의 파일 해시가 일치한다.
- 다음 우선순위는 엄폐·미사일 검사의 실행별 차이 재현, Android에서 4인 경기 프레임 시간·터치·현장 작업 확인, 남은 맵과 렌더링 예산 회귀다.
