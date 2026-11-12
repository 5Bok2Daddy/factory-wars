# UI 팩 교체하기 (항목 31)

HUD 그림·색·글꼴은 코드가 아니라 에셋 두 개로 정합니다. 칸이 비어 있으면 지금 임시 그림(Kenney ui-pack-sci-fi 모양 + UiTheme 색)이 나옵니다.

| 에셋 | 위치 | 내용 |
| --- | --- | --- |
| UiTheme | `Assets/QuantumUser/Resources/FactoryWars/UI/UiTheme.asset` | 색(패널·글자·버튼 보통/누름/비활성·강조·경고·위험·체력·크기 배지 3색), 모서리 배율, 글자 크기 3단계(작게·보통·크게), 간격, 버튼 최소 크기(9mm / 154px) |
| UiCatalog | `Assets/QuantumUser/Resources/FactoryWars/UI/UiCatalog.asset` | 그림 칸 19개와 글꼴 칸 |

## 칸 목록

| 칸 | 쓰는 곳 | 임시 그림 |
| --- | --- | --- |
| panel | 명령 카드·선택 카드·외교 창 바탕 | UI/panel_rectangle |
| button / button_pressed / button_disabled | 모든 버튼(보통·누름·잠김) | UI/button_square_depth, button_square |
| icon_frame | 선택 카드 아이콘 테두리, 대기열 빈 칸 | UI/button_square_header_small_square |
| res_iron · res_wood · res_steel · res_power · res_population | 상단 자원 줄 | Icons/res_* |
| badge_small · badge_medium · badge_large | 선택 카드 크기 배지(소·중·대) | UI/bar_round_small |
| health_bar / health_fill | 선택 카드 체력 막대 | UI/bar_round_small |
| minimap_frame | 미니맵 테두리 | UI/panel_rectangle |
| tab | 자원 칸·휴전 막대·알림 말풍선 | UI/bar_round_large |
| tooltip | 잠긴 칸 이유 | UI/panel_glass |
| lock | 잠긴 칸 자물쇠 | Icons/ui_lock |

## 순서

1. UI 팩을 `Assets/Art/UIPack/`에 넣습니다(PNG). 텍스처 형식을 **Sprite (2D and UI)** 로 바꿉니다.
2. 9-slice가 필요한 그림(패널·버튼·탭·툴팁·체력 막대)은 Sprite Editor에서 테두리(Border)를 정합니다. 스프라이트에 테두리를 못 넣으면 칸의 **Border** 값(왼쪽·아래·오른쪽·위 픽셀)에 적어도 됩니다.
3. `UiCatalog.asset`을 열고 칸마다 **Sprite**에 끌어다 놓습니다. 비워 둔 칸은 임시 그림 그대로입니다.
4. 색은 `UiTheme.asset`에서 고칩니다. 그림이 이미 색이 있으면 패널·버튼 색을 흰색(1,1,1,1)으로 두면 원래 색이 나옵니다.
5. 글꼴: Pretendard 또는 Noto Sans KR(SIL OFL, 무료)을 `Assets/Art/Fonts/`에 넣고 `UiCatalog.asset`의 **Font**에 연결합니다. 비우면 Unity 기본 글꼴입니다.
6. 유닛·건물 아이콘: 에셋 팩으로 모델을 바꾼 뒤(ASSET-SWAP.md) 메뉴 **Tools > Factory Wars > UI > Capture Model Icons**를 한 번 누르면 `Icons/model_<id>.png`가 다시 찍힙니다(고정 각도·투명 배경). HUD는 model_ 아이콘이 있으면 그것을, 없으면 SVG 아이콘을 씁니다.
7. 시험: Test Runner에서 `Ui_` 로 시작하는 시험(겹침·글자 넘침·9mm·빈 패널·칸 목록)을 돌립니다. 배치 모드는 `-testFilter Ui_`.

## HUD 구역(바꾸지 않는 것)

- 상단 왼쪽 자원 한 줄 / 상단 가운데 휴전·경고(글자 없으면 숨김) / 상단 오른쪽 외교·메뉴 아이콘
- 하단 왼쪽 미니맵 + 유휴 공병(숫자 배지)·전체 군대·부대 1~3(누르기 선택, 두 번 카메라, **길게 누르기 지정**), 미니맵 위 범위 선택 토글
- 하단 가운데 선택 카드(아이콘·이름·크기 배지·체력·공격/방어 단계·대기열 5칸, 여러 종류면 종류 버튼)
- 하단 오른쪽 명령 카드 3x3(공병: 기본 건물 / 2단계 건물 하위 메뉴, 건물: 생산·연구·기지 업그레이드·집결·프리셋)
- 잠긴 칸: 회색 + 자물쇠, 누르거나 길게 누르면 이유("기지 2단계 필요 / 군수공장 필요"). 지금 단계보다 2단계 이상 높은 것은 숨김.
