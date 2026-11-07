# 팩토리 워즈 (Factory Wars) — 프로젝트 규칙

사용자: 김민규. 한국어로, 직접적이고 기술적으로 정확하게 답한다.

## 프로젝트 요약

- 모바일(가로 화면) 3D 로우폴리 RTS. iOS·Android, 유료 1회 구매.
- 엔진: Unity 6000.0 LTS + **Photon Quantum 3** (결정론 엔진). 동기화는 Quantum **soft lockstep**(`LockstepSimulation` 끔) + 입력 지연 약 200ms(`InputDelayMin` 4틱, 20Hz).
- 1차 출시(MVP): AI 대전 + 방 코드 온라인 대전 2~4인, 대전 중 동맹 변경, 정해진 문구 쪽지. 자유 입력 채팅 없음.
- MVP 진영 2개: 자유 연방(free_federation, 고가·고성능·공중) vs 대륙 연방(continental_federation, 물량·저가).
- 실존 국가명·국기·휘장·실존 무기 상표명을 코드·에셋·문자열 어디에도 쓰지 않는다 (가상 진영 원칙).
- 설계 문서(사용자 claude.ai에 있음): GDD https://claude.ai/artifact/LC9EQCZJwiBSiDmWktM7kg , 멀티플레이 아키텍처 https://claude.ai/artifact/Voe6UNf5PiFPrLcizcSvNZ (로그인 필요 — 열 수 없으면 이 파일과 TASK 파일 내용이 기준)

## 폴더

- `C:\Users\MinGyu\FactoryWars\` — 작업 루트 (이 파일 위치)
- `C:\Users\MinGyu\FactoryWars\Game\` — Unity 프로젝트 (Git 저장소)
- `C:\Users\MinGyu\FactoryWars\concept-art\` — 사용 안 함 (Gemini 이미지 생성은 폐기됨)

## 개발 환경

- Unity 6000.0.85f2에 Android 모듈 설치됨 (SDK: platforms 34~37, build-tools 36.0.0 / NDK r27c / OpenJDK 17.0.18). APK 빌드는 아직 시험하지 않았다.

## 결정론 규칙 (Quantum 시뮬레이션 코드 — 반드시 지킬 것)

1. 시뮬레이션(`Assets/QuantumUser/Simulation`)에서 `float`, `double`, `Mathf`, `UnityEngine.*` 사용 금지. `FP`, `FPVector2/3`, `FPMath`만.
2. 난수는 `frame.RNG`만. `System.Random`, `UnityEngine.Random` 금지.
3. 게임 상태는 전부 컴포넌트·싱글턴·프레임 데이터에. `static` 가변 상태 금지.
4. 시간은 프레임 수 또는 `frame.DeltaTime` 기반 `FP` 누적. 실제 시계 금지.
5. 플레이어의 일회성 행동(이동·공격·건설·생산·프리셋·외교·쪽지)은 **Quantum 커맨드**. 매 틱 입력(Input)은 최소화.
6. 길찾기는 Quantum NavMesh(Unity NavMesh를 굽고 Quantum으로 가져오기). Unity NavMeshAgent·Physics를 시뮬레이션에 쓰지 않는다.
7. 밸런스 수치는 Quantum `AssetObject`(UnitSpec 등)에 두고 코드에 하드코딩하지 않는다.
8. 화면(View) 코드는 시뮬레이션 상태를 읽기만 한다.

## 작업 원칙

- 모르는 Quantum API는 추측하지 말고 프로젝트 안 SDK 소스(`Assets/Photon/Quantum`)와 공식 문서(doc.photonengine.com/quantum/v3)를 확인한다.
- 변경 후에는 Unity 배치 모드로 컴파일해 `Editor.log`에 에러가 없는지 확인한다.
- 의미 있는 단위마다 Git 커밋하고, 작업 단위마다 `origin main`에 push한다 (원격: https://github.com/5Bok2Daddy/factory-wars.git, Private).
- 하지 말 것: 결제·구독 변경, Photon 대시보드 설정 변경(특히 무료 100 CCU 활성화), 계정 비밀번호·토큰 입력, 사용자 폴더 밖 파일 삭제.
- 유닛·건물·자원·지형·UI 그래픽 작업 전에 visual-style 스킬을 따른다.
- 막히면 우회하지 말고 `SETUP-REPORT.md`에 원인과 사용자가 할 일을 적는다.

## 사용량 한도 대비 (모든 작업에 적용)

- 지시받은 항목을 2~3개씩 끝낼 때마다 테스트 확인 후 커밋·push한다. 한도로 멈춰도 끝난 작업이 남게 한다.
- 한도가 풀려 자동으로 재개되거나 "이어서 진행해줘"를 받으면, 먼저 `SETUP-REPORT.md`와 `git log`·`git status`를 보고 끝난 항목은 건너뛰고 남은 항목부터 이어서 한다. 커밋 안 된 변경이 있으면 내용을 확인한 뒤 이어서 쓰거나 정리한다.
- 진행 중인 지시와 남은 항목은 `SETUP-REPORT.md`의 현재 단계 섹션에 적어 둔다 (재개할 때 기준).
