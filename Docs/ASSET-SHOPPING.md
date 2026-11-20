# 에셋 구매·다운로드 후보

## 제공 무료 에셋 적용

- 시가지 저층 상점에 Synty 카페를 배치했다. 프리팹·모델·재질·텍스처만 원본 GUID로 들여왔다. [POLYGON Coffee Shop](https://syntystore.com/products/polygon-coffee-shop)
- 도로용 Asphalt 02 노멀맵을 1K로 만들어 보통·높음에 적용한다. 차선, 보도와 사막 도로는 아스팔트 텍스처 범위에서 제외했다. [Asphalt 02](https://polyhaven.com/a/asphalt_02), [CC0](https://polyhaven.com/license)
- Starter와 Generic은 기존 소품과 겹치는 모델이 많아 보유본을 재사용한다. Stunt Plane, Simple Sky, Holiday, Pride Flags, Water Guns, Sidekick은 각각 민간기, 하늘 연출, 장식, 캐릭터 구성이 별도로 필요할 때 검토한다.

## 환경 그래픽 후보 (2026-11-20)

현재 조명·그림자·수면 개선에는 보유 에셋을 재사용했다. 표면의 사실감을 더 높일 경우 아래 순서로 검토한다.

| 우선순위 | 후보 | 적용 범위 |
| --- | --- | --- |
| 1 · 보유 | [Poly Haven Asphalt 02](https://polyhaven.com/a/asphalt_02), [CC0 라이선스](https://polyhaven.com/license) | `temp-assets`에 이미 있다. 도로·활주로의 마모 표현에는 1K 노멀과 색상부터 비교한다. |
| 2 · 무료 | [Concrete Props Pack HDRP/URP/SRP](https://assetstore.unity.com/packages/3d/environments/concrete-props-pack-hdrp-urp-srp-306471) | 시가지 방벽의 콘크리트 표현을 바꿀 때 재질과 치수를 일부 장면에서 비교한다. |
| 3 · 유료 | [Industrial Props Pack - PBR](https://assetstore.unity.com/packages/3d/props/industrial/industrial-props-pack-pbr-106769) | 배관·탱크·저장 설비를 보강할 때 첫 유료 후보. 산업 단지 전체를 교체하기 전에 대표 공장 1곳에 적용한다. |

상점 페이지는 두 모델 팩의 이전 Unity 버전 URP 호환성을 안내한다. Factory Wars의 Unity 6000.0.85f2와 Android에서 가져오기와 성능은 확인 전이다. 후보 패키지를 내려받거나 프로젝트에 설치하지 않았다.

## 전투 HUD 후보 (2026-11-20)

- 현재 공장 관리와 도크린 화면에는 보유한 [Kenney UI Pack - Sci-Fi](https://kenney.nl/assets/ui-pack-sci-fi)와 기존 `UiCatalog` 아이콘을 재사용한다. 컨베이어와 증축 부품도 각각 보유한 Kenney Factory Kit와 Synty POLYGON Construction으로 구성한다.
- [Synty INTERFACE – Sci-Fi Menus](https://syntystore.com/products/interface-sci-fi-menus)는 버튼·패널·팝업을 통일할 때 우선 검토할 팩이다. 제작사가 Unity 2022.3 이상과 uGUI 지원을 명시한다.
- [Layer Lab GUI Pro – Survival Clean](https://layerlab.io/products/gui-pro-survival-clean)은 군용 보급 화면에 어울리는 후보이다. 제작사 페이지 기준 Unity 패키지는 6000.3.9f1 이상을 요구한다. 현재 프로젝트(6000.0.85f2)에서는 PNG 스프라이트를 `UiCatalog`에 연결하는 범위로 검토한다.

## 1순위 · 오디오

제공된 11개 중 9개를 단발 효과음과 엔진·전투 배경 루프로 편집해 로컬에 연결했다. 추가 수급은 출처가 확인된 포탄 착탄·건물 붕괴·공장 가동음, 그리고 메뉴·전투 음악 순으로 검토한다. 음원별 출처와 가공 내역은 [오디오 인계](ai/HANDOVER.md)에 있다.

## 2순위 · 보유 에셋 확인

| 항목 | 실제 보유·연결 상태 | 판단 |
| --- | --- | --- |
| 모래주머니·헤스코 방벽 | Synty Military의 방벽 단품과 줄·그룹 모델이 있다. 시가지 방벽과 암시장 방벽 슬롯도 채워져 있다. | 보유 모델 우선. 헤스코 외형이 꼭 필요할 때만 후보 검토. [Military Barriers Props Pack](https://assetstore.unity.com/packages/3d/props/military-barriers-props-pack-425812)은 스토어 기준 Unity 6000.3.22f1/PC 대상이라 현재 버전과 모바일은 확인 전. |
| 항만 크레인·컨테이너 야적장 | `dress/Crane`은 비어 있고 Synty Construction의 크레인 부품, 컨테이너는 보유했다. | 해안 맵 정리 계획을 반영해 구매 보류. 남는 시가지 산업 구역이 필요할 때 보유 부품을 먼저 시험한다. |
| 신호등 일체형 | Synty City의 `SM_Prop_TrafficLight_01~03`, Kenney City Kit Roads의 신호등 모델이 있다. | 보유 에셋을 교차로에 연결하면 된다. |
| 일체형 수문 | `dress/WaterGate`에 실제 작동하는 조립 수문이 연결돼 있다. | 남는 수로에서는 기존 수문 재사용. |
| 소총 사격·엎드림 | 현재 보병 컨트롤러는 이동·웅크림을 사용하며 조준·사격 표시도 있다. 엎드림 전용 상태는 확인되지 않았다. | 기존 구입 기록이 있는 Kubold 클립부터 확인한다. 엎드림이 필요할 때만 제조사의 클립 목록에서 진입·유지·사격·복귀를 확인한다. [Kubold 애니메이션 목록](https://www.kubold.com/downloads) |
| 저격수 위장복 | `ff_sniper`, `cf_sniper` 모두 Synty Ghillie 모델이 연결돼 있다. | 보유 모델 사용. |

## 3순위 · 실제로 받을 것

| 항목 | 권장 선택 |
| --- | --- |
| 모바일 섬광·피격·연막 | 먼저 보유 War FX의 `_Effects (Mobile)`을 비교한다. 추가 다운로드는 무료 CC0 [Kenney Particle Pack](https://kenney.nl/assets/particle-pack)과 [Smoke Particles](https://kenney.nl/assets/smoke-particles). 파티클용 PNG이며 현재 효과에 소량 연결해 쓴다. 바로 비교할 Unity 프리팹이 필요하면 무료 [Cartoon FX Remaster Free](https://assetstore.unity.com/packages/vfx/particles/cartoon-fx-remaster-free-109565)도 후보이다. Android 프레임 시간은 비교 전이다. |
| 물 셰이더·물결 | 기존 수면 셰이더에 깊이 거품, 낮은 품질 경로, 잔물결 반사가 있다. 추가 구매 우선순위가 낮다. |
| 상륙정·수송선·현대 함정 | 해전이 남는 맵에 포함될 때만 검토한다. 기존 함정을 모바일 예산에 맞추고 보유 Kenney Watercraft 모델을 먼저 비교한다. |
| 사막·해안 지형 소품 | 사용자 맵 정리 계획에 따라 수급 대상에서 뺐다. |
