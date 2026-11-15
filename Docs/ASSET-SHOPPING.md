# 에셋 구매 후보 (2026-11-15, 블랙프라이데이 대비)

지금 가진 것: Synty POLYGON Military · City · Battle Royale · Generic, Synty ANIMATION Base Locomotion, Kenney 여러 키트(무료).
아래는 **게임 안에 이미 칸(슬롯)이 있고 지금 비어 있거나 대체품으로 채운 자리**만 모은 것이다. 사서 `temp-assets/`에 넣으면 칸에 연결만 하면 된다(원본 수정 없음).
상품명은 검색어 예시다. **구매 전에 상점 페이지에서 포함 모델·Unity 6/URP 지원을 꼭 확인**할 것(내가 상점 내용을 직접 확인하지 못했다).

## 1순위 — 지금 게임에 아예 없는 것
| 필요한 것 | 왜 | 들어갈 칸 | 검색어 예시 |
| --- | --- | --- | --- |
| **사운드 효과 묶음**(총성·포성·폭발·엔진·헬기·제트·UI 클릭·경고음) | 게임에 소리 파일이 **0개**다. 체감 완성도에 가장 크다 | `AudioCatalog` 전체, `drop_warning`·저격·작전 경고 | "military sound effects", "war SFX bundle", "UI sound pack" |
| **배경 음악**(메뉴·전투 긴장·승리/패배) | 음악 없음 | `AudioCatalog` 음악 칸 | "military orchestral music pack", "RTS soundtrack" |
| **UI 팩**(이미 계획하신 Layer Lab GUI Pro 계열) | 도크린 카드·격납고·명령 버튼이 지금 도형 카드 | `UiCatalog`(`doctrine_air/shadow/armor`, 버튼 프레임, 아이콘) | "GUI Pro", "casual game UI", "military UI kit" |

## 2순위 — 대체품으로 버티는 것
| 필요한 것 | 지금 대체 | 들어갈 칸 | 검색어 예시 |
| --- | --- | --- | --- |
| 모래주머니 더미·헤스코 방벽 | 콘크리트 방벽·컨테이너 | `dress/Barrier`, `dress/OutpostWall` | "sandbag", "hesco barrier" (Synty POLYGON 계열 전쟁 팩 확인) |
| 항만 크레인·컨테이너 야적장 설비 | 없음(해안 옹벽은 기둥·난간만) | `dress/Crane`, 해안 모듈 | "port crane", "harbor props", "industrial" |
| 신호등 기둥 일체형 | 가로등만 | `dress/RoadProp` | "traffic light low poly" |
| 수문(댐 게이트) 일체형 | 옹벽+기둥+철문 조립 | `dress/WaterGate` | "dam gate", "canal lock" |
| 보병 소총 사격·엎드림 애니메이션 | 걷기·달리기·웅크리기만 | 보병 애니메이터 | "rifle animation set", "shooter animation pack" |
| 저격수 전용 캐릭터·위장복 | 팩의 `Ghillie` 캐릭터 후보 있음(연결만 남음) | `unit/ff_sniper`, `unit/cf_sniper` | (구매 불필요, 연결 작업만) |

## 3순위 — 있으면 좋은 것
| 필요한 것 | 메모 |
| --- | --- |
| 모바일용 가벼운 파티클 팩(포구 섬광·피격·연막) | 지금 팩 FX는 무거워 포구 섬광은 절차 효과를 유지. 모바일 최적화 FX면 교체 |
| 물 셰이더·물결 텍스처 | 우리 셰이더로 대체 중(깊이 거품 완료). 급하지 않음 |
| 상륙정·수송선·현대 함정 | 배는 Kenney 모델. 해협·다도해 맵(35 남은 일)에 필요 |
| 사막·해안 지형 소품(야자수 아님, 모래 둔덕·암반) | 사막 맵이 표준 지형 재사용 중 |

## 사지 않아도 되는 것(이미 있음)
- 폭발·연기·불·비·안개·모래바람 FX: Synty 팩에 있어 이번에 효과 칸에 연결했다.
- 레이더탑(`Mobile_Radar`)·유류고(`Fuel_Bladder`)·기갑 잔해(`Veh_*_Destroyed`, 92종)·관측 초소: 팩에 있음(37 남은 기능에 쓸 예정).
- 작전기(`Veh_Jet`·`TransportPlane`)·낙하산: 팩에 있음(연결 작업만 남음).
