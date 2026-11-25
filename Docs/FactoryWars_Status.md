# Factory Wars — 개발 현황 & 작업 이력 대시보드 (A2UI / Gemini 붙여넣기용)

- **갱신 시각:** `2026-11-25 12:41 KST` (`[gemini]`)
- **엔진 / 파이프라인:** Unity `6000.0.85f2` · URP `17.0.4` · Photon Quantum 3 (결정론 `FP`, 금지어 **0건**)
- **핵심 맵:** `Map4P v4` (`640×640m`, `80×80` 셀, `8m/셀`) — 아치교 전함 통과·PMC 내부 무울타리·용병 얼굴 가시성·무단색 검정 규칙 준수
- **A2UI JSON 소스:** `harness/A2UI-DASHBOARD.json` · 상태 파일: `harness/STATE.json`

## 최근 완료 마일스톤 & 검증 이력

| 시각/커밋 | 작업 내용 | 검증 결과 | 대표 캡처 |
| --- | --- | --- | --- |
| 2026-11-25 · 최신 | 연속 부드러운 이동·업그레이드 연동 질주(`SprintForward`, +25%), 자원지 최대 3인 작업자(`3/3` 월드/HUD 배지), 전함·보트 조타수 보존, A2UI 대시보드 | **3/3 PASS** · 결정론 0건 | `Docs/status_images/SprintAndWorkers/sprint_before_after.png`, `three_workers_badge_3of3.png`, `naval_helmsman_and_particle_wake.png` |
| 2026-11-25 · `01765f9` | Mixamo 휴머노이드 42종 통합 + 버기·오토바이·초계정·상륙정·헬기 승무원 착석 & 발전소 전면 작업자 | **2/2 PASS** · 결정론 0건 | `Docs/status_images/Mixamo/mixamo_patrol_vs_combat_before_after.png`, `mixamo_vehicle_and_crew_poses.png`, `mixamo_naval_crew_poses.png` |
| 2026-11-25 · `3411c4d` | 고정 사일로 → 기동형 MLS(`SM_Veh_Rocket_Truck_01`) 교체, 이동 사격, 발사 경고 제거, CF MLS & FF 폭격기 각 1기 보유 상한 | **3/3 PASS** · 결정론 0건 | `Docs/status_images/MobileMls/mobile_mls_firing_on_the_move.png`, `mobile_mls_fire_while_moving.gif` |
| 2026-11-25 · `63e2abe` | 공중 비행 VFX(URP 17.0.4)·순항 고도 상향(`11.5/15/19/20.5m`)·고층 빌딩(`H`) 결정론 회피 | **4/4 PASS** · 결정론 0건 | `Docs/status_images/AirVfx/air_altitude_vfx_before_after.png`, `air_flight_avoidance_motion.gif` |
| 2026-11-25 · `f18338a` | Unity Particle Pack URP 17.0.4 함선 3중 항적(V바 제거) & 손상/교전 화염·연기·불씨 | **2/2 PASS** · 결정론 0건 | `Docs/status_images/ParticleVfx/vfx_naval_particle_wake.gif`, `vfx_combat_fire_smoke.gif` |

## 잔여 미검증 / 다음 예정 항목
- **미검증:** Map4P Seat 0 본진 철광석 노드 3곳 `BlockedLocation` 여백 보정, 2클라이언트 실서버 온라인 동기화 재확인, `-batchmode` T40 상점 HUD 8슬롯 UI 캡처.
- **다음 예정:** Task 43(도시 사실감·구역화) → Task 44(사용자 GLB 적용) → Task 45(파괴 가능 소품·건물).
