# 에셋 팩으로 그래픽 바꾸기

산 에셋 팩(Synty SIMPLE·POLYGON 등)의 모델을 게임에 넣는 순서입니다. 코드는 고치지 않습니다. 표(`VisualCatalog`)에 모델을 적으면 바로 바뀌고, 칸을 비우면 지금 모델로 돌아갑니다.

## 1. 팩 넣기

1. Unity 에디터로 `Game` 프로젝트를 엽니다.
2. 산 `.unitypackage`를 더블클릭하거나 `Assets > Import Package > Custom Package…`로 가져옵니다.
   - 가져올 위치는 팩이 정한 폴더(예: `Assets/Synty/…`) 그대로 둡니다.
3. 모델이 분홍색이면 4번 메뉴로 머티리얼을 바꿉니다.

## 2. 메뉴 실행 (`Tools > Factory Wars > Assets`)

| 순서 | 메뉴 | 하는 일 |
| --- | --- | --- |
| 1 | `1. Build Visual Slots` | 유닛·건물·자원·도시·지형·소품·효과마다 빈 칸을 만듭니다. 이미 채운 칸은 그대로 둡니다. |
| 2 | `2. Scan Pack Folder…` | 팩 폴더를 고르면 모델 목록, 미리보기 사진, **추천 표(CSV)**를 만듭니다. |
| 3 | `3. Import Slot CSV…` | 고친 CSV를 표에 넣습니다. |
| 4 | `4. Convert Pack Materials to URP…` | 분홍 머티리얼을 이 게임의 렌더러(URP)용으로 바꿉니다. |

만들어지는 파일(`Game-logs/assets/`):

- `pack-prefabs.txt` — 팩의 모델 목록
- `pack-contact-sheet.png` — 모델 미리보기 사진(목록과 같은 순서, 한 줄에 12개)
- `visual-slots.csv` — 칸마다 추천 모델

## 3. CSV 확인 (엑셀로 열기)

| 열 | 뜻 | 예 |
| --- | --- | --- |
| Id | 칸 이름. 고치지 마세요 | `unit/ff_eagle_tank`, `building/cf_barracks`, `dress/CitySmall`, `fx/explosion` |
| Prefabs | 쓸 모델 경로. 여러 개면 `|`로 잇습니다(소품은 섞어 씀). 비우면 지금 모델 | `Assets/Synty/…/SM_Veh_Tank_01.prefab` |
| Alternatives | 다른 후보(참고용) | |
| Scale | 크기 배율(AutoFit이 0일 때만) | `1` |
| AutoFit | 1이면 게임 속 크기(부지·유닛 반경)에 자동으로 맞춤 | `1` |
| YawOffset | 모델이 옆을 보고 있으면 돌리는 각도 | `90` |
| TeamColor | 팀 색 입히는 방법 | 아래 표 |
| TeamToken | MaterialName 방식에서 팀 색으로 바꿀 머티리얼 이름 조각 | `Team` |
| RealWorld | **실존 무기 닮음 확인** | 아래 표 |
| Notes | 메모 | `포탑 모양 바꿈` |

팀 색 방법:

| 값 | 뜻 |
| --- | --- |
| None | 팀 색 없음(자원·소품·지형) |
| MaterialName | 이름에 TeamToken이 들어간 머티리얼만 팀 색으로 |
| MaterialSlots | 정해진 칸 번호의 머티리얼만 팀 색으로(표에서 직접 지정) |
| PaletteTexture | 팀마다 다른 색 텍스처로 통째로 교체(Synty처럼 텍스처 한 장 쓰는 팩) |
| Tint | 모든 색에 팀 색을 곱함(가장 간단, 기본 추천) |

실존 무기 확인(가상 진영 원칙 — 실존 국가·무기 이름과 모양을 쓰지 않습니다):

| 값 | 뜻 |
| --- | --- |
| Unchecked | 아직 안 봄. **유닛·건물은 이 상태면 시험이 실패합니다** |
| Clear | 실존 무기와 닮지 않음 |
| Modified | 닮아서 모양·색을 바꿨음 |
| Rejected | 닮아서 쓰지 않음(채우면 시험 실패) |

전차·전투기·헬기·함선은 실제 기종과 쉽게 닮습니다. 포탑·차체 모양이 특정 기종과 같아 보이면 Rejected로 두거나 부품을 바꾼 뒤 Modified로 적으세요.

## 4. 시험 실행

에디터에서 `Window > General > Test Runner > PlayMode`를 열고 아래 시험을 돌립니다(또는 Claude에게 "에셋 교체 시험 돌려줘").

| 시험 | 확인하는 것 |
| --- | --- |
| `Visual_CatalogHasSlotForEveryElement` | 모든 게임 요소에 칸이 있는지 |
| `Visual_FilledSlotsWithinBudgetAndChecked` | 채운 칸의 모델이 게임 속 크기의 0.5~1.6배인지, 삼각형(유닛 4천·건물 1만 2천)·그리기 호출(유닛 6·건물 12) 예산, 실존 무기 확인 칸 |
| `Visual_FilledSlotReplacesModelAndFollowsFootprint` | 칸을 채우면 모델이 바뀌고 선택 고리·체력바·팀 색·애니메이션이 따라가는지 |

결과 표: `Game-logs/assets/swap-check.csv`.

그다음 메뉴의 `AI 대전 2인`(개발 빌드)으로 한 판 보면서 크기·방향을 확인합니다.

## 5. 애니메이션

모델에 Animator가 있거나, 표의 칸에 컨트롤러를 넣으면 게임이 아래 값을 보냅니다. 컨트롤러에 없는 값은 건너뛰고, Animator가 없으면 애니메이션 없이 움직입니다.

| 파라미터 | 종류 | 언제 |
| --- | --- | --- |
| Speed | float | 이동 속도(m/초) |
| Moving | bool | 걷는/달리는 중 |
| Attack | trigger | 쏠 때마다 |
| Building | bool | 공병이 건설·수리 중 |
| Gathering | bool | 공병이 채집 중 |
| Die / Dead | trigger / bool | 죽을 때(칸의 DeathSeconds만큼 보여 주고 지움) |

## 6. 되돌리기

- 한 칸만: CSV에서 그 줄의 Prefabs를 비우고 다시 가져오기.
- 전부: `Assets/QuantumUser/Resources/FactoryWars/Visual/VisualCatalog.asset`을 선택하고 Inspector에서 `Enabled` 체크를 끕니다.

## 참고 (개발자용)

- 표: `VisualCatalog`(`Assets/QuantumUser/View/FactoryWars/Art/VisualCatalog.cs`). 화면 전용이며 시뮬레이션 판정(크기·사거리·충돌)과 무관합니다.
- 연결 지점: 유닛·건물·철광산·다리 `ArtView.Create`, 소품·도시·지형 `KenneyAssetMap.Get`, 효과 `BattleFx`(muzzle·impact·explosion·flame·smoke).
- 배치 모드: `-executeMethod FactoryWars.EditorTools.VisualCatalogTools.ScanBatch -packFolder Assets/…`, `ImportBatch -slotCsv 경로`, `ConvertBatch -packFolder Assets/…`.
