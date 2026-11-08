# 항목 19B — 외부 CC0 3D 에셋(Kenney) 도입과 맵·건물 모델 교체

시작 조건: 19A(룩 개발)의 비교 환경·`VisualProfile`이 있는 상태에서 시작한다. 19A의 "외부 에셋 다운로드 금지"(6번)는 **이 파일 범위에 한해 해제**한다(2026-11-08 사용자 결정).
CLAUDE.md 규칙, 결정론 유지. **모델 교체는 화면(View) 전용**이며 시뮬레이션·판정·NavMesh는 건드리지 않는다. 단계마다 커밋·push.

## 왜 하나
`screenshots/lookdev/02_scale.png`, `04_post.png`를 본 사용자 평가: 기본 도형(Cube·Sphere) 조합으로는 목표 퀄리티가 나오지 않는다.
- 동그란 브로콜리 같은 나무 → 침엽수·활엽수 실루엣이 있는 나무 모델로
- 검은 벽돌 같은 절벽 → 층진 바위 절벽 모델로
- 회색 상자 빌딩 → 창문·지붕·굴뚝 디테일이 있는 산업/도시 건물 모델로
- 목표 방향(19A): Northgard풍 자연 지형·나무 + Art of War 3풍 밀리터리 구조물

---


## 0. 받은 키트 (2026-11-08 15:00, Claude가 사용자 Downloads·Documents에서 옮김) — 이 절이 1·2절보다 우선
`temp-assets/`에 zip 6개가 있다. 모두 무결성 확인 완료. **다운로드 단계는 건너뛰고 압축 풀기부터 한다.**

| zip | FBX | 용도 |
| --- | --- | --- |
| kenney_city-kit-commercial_2.1.zip | 41 | 도시 건물(building-a~n), 고층(building-skyscraper-a~e = **쓰러지는 건물 후보**), 원거리용 low-detail-building-* (LOD), 차양·파라솔 소품 |
| kenney_city-kit-industrial_2.0.zip | 37 | 공장·창고(building-a~t), 굴뚝(chimney-*), 저장 탱크(detail-tank*), 컨테이너(shipping-container-a~c), 급수탑(water-tower = **쓰러지는 건물 후보**), 태양광 패널 |
| kenney_city-kit-roads.zip | 95 | 도로 조각(직선·커브·교차로·로터리·경사), 다리(road-bridge, bridge-pillar*), 공사 바리케이드·콘·펜스, 가로등(light-*), 전봇대·전선, 표지판, 쓰레기통 |
| kenney_factory-kit_3.0.zip | 143 | 컨베이어, 크레인(crane*), 호퍼, 캣워크, 상자, 문, 기어 — **플레이어 건물(가공소·군수공장·중공업 기지·집하장) 부품** |
| kenney_tower-defense-kit.zip | 160 | detail-tree(-large), detail-rocks(-large), detail-crystal(-large = **철광석 결정 후보**), detail-dirt, 강·언덕 타일. 성·캐릭터·UFO(enemy-ufo-*)는 쓰지 않는다 |
| kenney_nature-kit.zip | 329 | 나무(침엽·활엽·단풍 fall·짙은 dark 변종), 바위·돌, **층진 절벽 조각(cliff_*)**, 강·흙길 바닥 조각, 그루터기·통나무, 덤불·풀·꽃 |
| kenney_train-kit.zip | 103 | 철로·손상된 철로(railroad-damaged-*) — 공업 지구·중앙 폐허 장식. 기차 차량은 쓰지 않는다(사용자 확인 전) |

- **nature-kit도 받았다(15:05 추가, zip 7개).** `kenney_nature-kit.zip`, FBX 329개, 무결성 확인 완료. 숲·바위·절벽·강변은 아래 0-1-N 매핑대로 nature-kit을 1순위로 쓴다.
- `kenney-install.ps1`의 `$kits`를 위 6개 + nature-kit(총 7개)으로 바꾸고, zip 이름의 버전 표기(`_2.0`, `_2.1`, `_3.0`)를 허용하게 패턴을 `kenney_<kit>*.zip`로 고친다. FBX와 그 텍스처(png)만 `Assets/ExternalAssets/Kenney/<kit>/`로 복사한다(glb·obj·mtl은 빼서 저장소 크기를 줄인다). 각 키트 License.txt도 같이 둔다.

### 0-1. 구체 매핑 (KenneyAssetMap 초안 — 접촉 시트를 본 뒤 조정)
| 게임 요소 | 1순위 모델 | 비고 |
| --- | --- | --- |
| 도시 건물 소형 | commercial building-a~f | 지붕·창문 디테일. 폐허 단계는 같은 모델 상단을 잘라내고 잔해 소품 |
| 도시 건물 중형 | commercial building-g~n, industrial building-a~h | |
| 도시 건물 대형 | industrial building-i~t | 창고·공장형 |
| 쓰러지는 건물 | commercial building-skyscraper-a~e, industrial water-tower, industrial chimney-large | 쓰러지는 방향 축에 맞춰 회전 |
| 파괴 불가 고층 블록(능선이 도시 쪽일 때) | skyscraper + low-detail-building-wide-* | 19A 결정대로 기본은 바위 절벽, 도시 구간만 |
| 진지 건물 | commercial building 중 입구가 큰 것 + roads construction-barrier·모래주머니(절차) | 문·창 강조 |
| 원거리 LOD | commercial low-detail-building-* | 줌 아웃·4인 전체 화면 |
| 도로·교차로·다리 | roads road-*, road-bridge, bridge-pillar* | 바닥 도로 메쉬를 교체하거나 위에 얹는다. 판정용 Roads 데이터는 그대로 |
| 길가 소품 | roads light-*, electricity-pole*, road-sign-*(글자 없는 empty/object형만), dumpster | 실존 표지 문구 금지 |
| 바리케이드·공사 구역 | roads construction-barrier/cone/fence | 뒷길 바리케이드 위에 얹어 폐차 더미와 섞기 |
| 고철 하치장(철) | industrial shipping-container-*, factory box-*, tower-defense detail-crystal(-large) | 결정은 철광석 발광(청록 아님, 기존 규칙 색) |
| 공원 숲(목재) | tower-defense detail-tree(-large) + 기존 절차 침엽수 | 단풍색 틴트 섞기. nature-kit 오면 교체 |
| 바위 | tower-defense detail-rocks(-large) + 절차 바위 | |
| 가공소 | factory hopper-*, conveyor*, industrial chimney-* + 기존 용광로(주황 쇳물) | |
| 군수공장·중공업 기지 | factory crane*, door-wide-*, catwalk-*, industrial building 부품 | 진영 형태 대비 유지(자유=곡면, 대륙=각진) |
| 집하장·보급창 | factory box-*, industrial shipping-container-*, detail-tank | |
| 발전소 | industrial detail-tank-large, chimney-* + 기존 냉각탑·청록 코어 | |
| 방어탑 받침 | tower-defense의 받침·벽 부품만 | 포탑·포신은 기존 절차 |
| 중앙 폐허 장식 | train railroad-damaged-* | |


### 0-1-N. nature-kit 매핑 (숲·바위·절벽은 이것이 1순위)
| 게임 요소 | 모델 | 비고 |
| --- | --- | --- |
| 공원 숲(목재) | tree_pineTall*·tree_pineRound*·tree_pineDefault*(침엽 주력) + tree_oak·tree_detailed·tree_default의 **_fall**(단풍)·**_dark** 변종 | 목표 이미지처럼 침엽 60%, 단풍 25%, 짙은 활엽 15%(값은 VisualProfile). tree_palm*·cactus*는 쓰지 않는다 |
| 채집에 따른 숲 변화 | stump_*(그루터기), log·log_stack(쓰러진 통나무) | 채집량에 비례해 바깥 나무부터 그루터기로 교체(기존 규칙 유지) |
| 숲 가장자리 | plant_bush*, grass*, grass_leafs*, mushroom_* (소량) | 통과 가능 장식. low에서는 줄인다 |
| 바위·자원 주변 돌 | rock_large*·rock_small*·rock_tall*, stone_* | 2~4개 무리, 해시 기반 크기·회전 |
| 능선·맵 경계·고지대 측면 절벽 | **cliff_* 조각**(block·corner·cornerInner·half·steps·top·diagonal, _rock은 회색, _stone은 밝은 회색) | 판정 경계를 따라 조각을 이어 붙여 층진 절벽을 만든다. 높이 2~3단 쌓기. 회색=_rock 기본, 황토 틴트 변종 섞기. 기존 절차 절벽은 이 조각으로 교체 |
| 고지대 비탈길 | cliff_steps*, cliff_blockSlope* | 판정상 비탈길 위치에만 |
| 강변·운하 가장자리(자연 구간) | ground_riverSide*·ground_riverBank*, ground_riverRocks, rock_small* | 도시 구간 운하는 콘크리트 제방(절차) 유지 |
| 흙길(도시 밖 구간) | ground_path* | 판정용 Roads 데이터는 그대로, 겉모습만 |
| 폭포(선택) | cliff_waterfall* | 강이 단차를 넘는 곳이 있으면 1~2곳 |
| 쓰지 않는 것 | bed, tent, canoe, crops_*, statue_*, pot_*, campfire_*, fence_*(목재 울타리), sign | 전장 분위기와 맞지 않음 |

nature-kit도 원래 색이 밝고 원색이므로 0-2의 KenneyLit 교정을 똑같이 적용한다. 특히 숲은 목표 이미지(aow3-ref 01·03)의 짙은 초록·주황·황금 톤으로, 절벽은 층마다 윗면이 밝게(높이 그라데이션).

### 0-2. 렌더링 적용 (Kenney 원래 색 → 목표 그림체)
Kenney 모델은 밝은 원색·평면 색이라 그대로 두면 장난감처럼 보인다. **반드시** 아래를 거친다.
1. **공용 셰이더 `FactoryWars/KenneyLit`로 전부 교체.** Kenney 원본 머티리얼은 쓰지 않는다. 팔레트 텍스처(colormap)는 그대로 읽되:
   - 채도 −35~−50%, 밝기 −10%로 눌러 군용 톤으로(값은 VisualProfile, 비교 시트로 정함)
   - 재질별 틴트: 콘크리트·벽돌은 회색·황토, 지붕은 짙은 회색, 금속은 슬레이트, 녹색 요소는 국방색으로
   - 월드 높이 그라데이션(아래 어둡게), 저주파 색 노이즈(건물마다 조금씩 다른 색), 바닥 접촉 AO
   - 손상 단계 파라미터: 그을음 양·균열 데칼 강도(도시 건물 체력 단계와 연결)
2. **스케일 통일:** 전차 1대 기준. 건물 1층 높이 ≈ 전차 높이 × 1.5, 나무 ≈ 전차 × 2~3. KenneyAssetMap에 키트별 기본 스케일.
3. **피벗·바닥 정렬:** 모델마다 바닥이 y=0에 오도록 오프셋 보정, 건물 부지(사각형) 안에 들어오게 크기 맞춤. 부지 밖으로 튀어나오면 판정과 어긋나 보이므로 테스트로 검사.
4. **배칭:** 같은 모델은 GPU 인스턴싱 또는 정적 배칭. 그리기 호출 목표는 19A 표(보통 품질 기준 +30% 이내).
5. **유닛은 교체하지 않는다.** 대신 유닛도 KenneyLit와 같은 톤(채도·AO)으로 맞춰 화면이 따로 놀지 않게 한다.

### 0-3. 진행 순서
1) zip 풀기·임포트(FBX import 설정: 스케일 1, 노멀 import, 애니메이션 없음, 읽기/쓰기 꺼짐) → 2) 키트별 접촉 시트 `screenshots/kenney/<kit>_contact.png` → 3) KenneyAssetMap 작성 → 4) KenneyLit 셰이더 값 탐색(채도·틴트 3안 비교 시트) → 5) 맵에 적용 → 6) 19A 고정 시점 5개로 before/after 비교 `screenshots/lookdev/19b_compare_*.png` → 7) 테스트·커밋·push → 이어서 19 → 22 → 23.

## 1. 원격 3D 에셋 자동 설치 (PowerShell)
로그인·결제가 필요 없는 CC0 퍼블릭 도메인 에셋(Kenney.nl)에서 자연물(소나무·바위·절벽), 산업/도시 건물, 타워 FBX 모델을 `Game/Assets/ExternalAssets/Kenney/<kit>/`로 자동 다운로드·추출한다.

```powershell
$ErrorActionPreference = "Stop"
$ProgressPreference    = "SilentlyContinue"   # 진행 표시줄 끄기 (Invoke-WebRequest 속도)
$root = "C:\Users\MinGyu\FactoryWars"
$dest = "$root\Game\Assets\ExternalAssets\Kenney"
$temp = "$root\temp-assets"                    # Unity 프로젝트 밖 (임포트·커밋 대상 아님)
New-Item -ItemType Directory -Force -Path $dest,$temp | Out-Null

$kits = @("nature-kit", "city-kit-industrial", "city-kit-commercial", "tower-defense-kit")
foreach ($kit in $kits) {
    try {
        $page   = "https://kenney.nl/assets/$kit"
        $html   = Invoke-WebRequest -Uri $page -UseBasicParsing
        $zipUrl = ($html.Links | Where-Object { $_.href -like "*.zip" } | Select-Object -First 1).href
        if (-not $zipUrl) { Write-Warning "$kit : 페이지에서 zip 링크를 못 찾음"; continue }
        if ($zipUrl -notmatch '^https?://') { $zipUrl = ([Uri]::new([Uri]$page, $zipUrl)).AbsoluteUri }

        $zipFile = "$temp\$kit.zip"
        $exDir   = "$temp\$kit"
        Invoke-WebRequest -Uri $zipUrl -OutFile $zipFile -UseBasicParsing
        Expand-Archive -Path $zipFile -DestinationPath $exDir -Force

        $out = "$dest\$kit"
        New-Item -ItemType Directory -Force -Path $out | Out-Null
        # 키트마다 폴더 구조가 조금 다르므로 "FBX format" 폴더를 찾아서 복사 (텍스처 하위 폴더 포함)
        $fbxDir = Get-ChildItem $exDir -Recurse -Directory | Where-Object { $_.Name -eq "FBX format" } | Select-Object -First 1
        if ($fbxDir) { Copy-Item -Path "$($fbxDir.FullName)\*" -Destination $out -Recurse -Force }
        else         { Get-ChildItem $exDir -Recurse -Include *.fbx, *.png | Copy-Item -Destination $out -Force }
        # 라이선스 사본 보관 (CC0)
        Get-ChildItem $exDir -Recurse -Include "License*.txt" | Select-Object -First 1 |
            Copy-Item -Destination "$out\License.txt" -Force

        "{0}: FBX {1}개" -f $kit, (Get-ChildItem $out -Recurse -Filter *.fbx).Count
    } catch {
        Write-Warning "$kit 실패: $($_.Exception.Message)"
    }
}
Remove-Item $temp -Recurse -Force
```

원본 명령에서 고친 점: 마크다운 링크로 깨진 URL(`[https://...](https://...)`), 파이프 기호(`\vert{}` → `|`), `-OutFile$zipFile` 띄어쓰기, 존재하지 않는 대상 폴더로의 `Copy-Item`, 상대 경로 zip 링크, 키트별 폴더 구조 차이, 한 키트 실패 시 전체 중단.

**설치 후 확인**
- 키트별 FBX 개수를 SETUP-REPORT.md에 적는다. 0개인 키트가 있으면 원인(링크 구조 변경, 차단 등)을 적는다.
- 다운로드가 막히면 **우회하지 말고**(다른 미러·캐시·스크래핑 금지) SETUP-REPORT.md에 "사용자가 kenney.nl에서 <kit> zip을 받아 `temp-assets/`에 넣어 주세요"라고 적고, 받은 키트만으로 진행한다. zip이 `temp-assets/`에 있으면 다운로드 단계는 건너뛰고 추출부터 하도록 스크립트를 고쳐도 된다.
- Unity 배치 모드로 임포트·컴파일해 Editor.log에 에러가 없는지 확인한다.
- 커밋: `Assets/ExternalAssets/Kenney/**`(FBX·텍스처·.meta·License.txt). 저장소 크기 증가량을 보고서에 적는다. `temp-assets/`는 커밋하지 않는다.

## 2. 3D 모델 매핑 및 비주얼 교정 사항

### 2-1. 매핑은 데이터로
- 파일 이름을 추측하지 않는다. 먼저 키트별 FBX 목록과 미리보기(`screenshots/kenney/<kit>_contact.png`, 모델을 격자로 찍은 접촉 시트)를 만든 뒤 고른다.
- `KenneyAssetMap` ScriptableObject(View 전용)에 "게임 오브젝트 종류 → 프리팹 후보 목록(가중치)·스케일·회전 범위·틴트"를 둔다. 코드에 파일명을 하드코딩하지 않는다. 19A의 `VisualProfile`에서 이 맵을 참조해, 절차 생성 메쉬 ↔ Kenney 모델을 프로필 전환으로 비교할 수 있게 한다.
- 변종 선택(어느 나무, 회전각)은 **오브젝트 ID·좌표 해시**로 정한다(`UnityEngine.Random` 금지 원칙을 화면에도 적용해 매번 같은 화면이 나오게).

### 2-2. 교체 대상
| 게임 요소 | 키트 | 교체 기준 |
|---|---|---|
| 숲(나무) | nature-kit | 침엽수 위주 + 단풍·활엽 섞기(19A 축 8). 브로콜리형 구 나무 전부 제거. 군락 가장자리에 덤불·작은 나무 |
| 바위·자원 주변 돌 | nature-kit | 큰/작은 바위 변종을 섞고 크기·회전 무작위(해시 기반) |
| 절벽·능선·맵 경계 | nature-kit | 층진 바위 절벽 모델을 이어 붙여 능선·맵 경계 표현. 검은 벽돌형 메쉬 제거. **판정 경계(높이·통행 불가 영역)는 그대로**, 겉모습만 덮는다 |
| 도시 구조물·폐허 | city-kit-industrial, city-kit-commercial | 회색 상자 빌딩을 창고·공장·굴뚝·사무 건물로 교체. 일부는 잘라 낸 폐허(지붕 제거, 잔해 소품)로 |
| 방어 타워·포탑 받침 | tower-defense-kit | 포탑 받침·벽·바리케이드 등 구조 부품만 쓴다. 귀여운 캐릭터·성(城) 장식은 쓰지 않는다 |
| 플레이어 건물(본진·공장 등) | city-kit-industrial 부품 + 기존 절차 메쉬 | 진영 형태 대비가 있는 기존 디자인을 유지하되, 지붕·굴뚝·탱크·배관 같은 부품을 Kenney에서 가져와 붙여 상자 느낌을 없앤다. 콘크리트 패드는 19A 02 기준 유지 |
| 유닛 | 교체 안 함 | 이번 범위 밖. 기존 절차 메쉬 유지 |

### 2-3. 비주얼 교정 (Kenney 기본 룩 → 목표 룩)
Kenney 모델은 밝은 원색·평면 색이라 그대로 두면 장난감처럼 보인다. 아래로 맞춘다.
1. **공용 셰이더 1개(URP Shader Graph 또는 HLSL):** Kenney 머티리얼을 전부 이 셰이더로 교체. 기능: 원래 색 × 팔레트 틴트, 월드 높이 그라데이션(위 밝게·아래 어둡게), 월드 좌표 저주파 색 노이즈, 바닥 접촉부 어둡게(가짜 AO). 외곽선 없음. 머티리얼 수를 줄여 SRP Batcher·GPU 인스턴싱이 되게 한다.
2. **팔레트:** 건물은 콘크리트·슬레이트 회색·카키·녹슨 갈색으로 채도 낮춤. 숲은 짙은 녹색 + 주황·황금 단풍. 바위는 회색/황토, 층 윗면 밝게. 팀 색은 19A 축 11의 결정값대로 띠·패널에만.
3. **스케일:** 전차 1대(1080p 가로 40px 목표) 기준으로 나무 높이 약 전차 2~3배, 건물 1층 높이 약 전차 높이 1.5배. 스케일 값은 `KenneyAssetMap`에 둔다.
4. **조명·후처리:** 19A에서 고른 값 유지. Kenney 교체 후 블룸·채도가 과하면 프로필에서만 조정.
5. **성능:** 나무·바위는 GPU 인스턴싱, 정적 구조물은 Static Batching. low 품질에서는 나무 밀도·소품 수를 줄인다. 19A의 성능 기준(low 현재 수준 이하, medium +30% 이내)을 지킨다. 그리기 호출·삼각형 수를 품질별로 측정.

## 3. 비교와 결정
- 19A의 고정 장면·고정 시점 5개로 **before(현재 절차 메쉬) / after(Kenney 교체)** 를 찍어 `screenshots/lookdev/19b_compare_<시점>.png`로 나란히 둔다. 특히 02_scale·04_post와 같은 구도를 반드시 포함한다.
- 19A 평가 기준표(가독성·입체감·목표 유사도·정보 전달·눈의 피로·성능)로 다시 점수를 매긴다.
- 결과·고른 매핑·버린 모델과 이유를 LOOKDEV.md에 "19B" 섹션으로 추가한다. visual-style 스킬에 "자연물·도시 구조물은 Kenney CC0 + 공용 셰이더, 유닛은 절차 생성" 규칙을 추가한다.

## 4. 하지 말 것
- Kenney 외 출처 에셋 다운로드 금지. 유료·로그인·결제가 필요한 에셋 금지.
- 시뮬레이션 수치·판정·NavMesh·충돌 변경 금지. 모델 교체는 View 코드와 프리팹만.
- 실존 국가·로고·상표를 닮은 간판·표식을 붙이지 않는다(Kenney 상업 건물 간판 텍스처가 실존 브랜드처럼 보이면 지운다).
- `Assets/ExternalAssets` 밖에 외부 파일을 두지 않는다.

## 5. 보고
SETUP-REPORT.md "항목 19B" 섹션 맨 위에: 한 줄 요약, 키트별 FBX 개수, 비교 시트 경로, 교체한 요소 목록, 품질별 비용 표, 아직 부족한 점(예: Northgard 회화풍과의 차이).
