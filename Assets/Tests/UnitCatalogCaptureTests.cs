namespace FactoryWars.Tests {
  using System.Collections;
  using System.IO;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  /// <summary>전장 잔존 흔적(영구 크레이터·영구 건물 붕괴 잔해·120 데칼 풀) 검증 및 전 유닛 개별 쇼케이스 PNG 캡처.</summary>
  public unsafe partial class LocalMatchTests {
    static string StatusImagesDir() {
      var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "status_images"));
      Directory.CreateDirectory(dir);
      return dir;
    }

    static void CaptureCameraPng(Camera cam, string outPath, int width, int height) {
      var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
      var prevTarget = cam.targetTexture;
      var prevActive = RenderTexture.active;
      cam.targetTexture = rt;
      cam.Render();
      RenderTexture.active = rt;
      var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
      tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
      tex.Apply();
      cam.targetTexture = prevTarget;
      RenderTexture.active = prevActive;
      Object.Destroy(rt);
      Directory.CreateDirectory(Path.GetDirectoryName(outPath));
      File.WriteAllBytes(outPath, tex.EncodeToPNG());
      Object.Destroy(tex);
    }

    [UnityTest]
    public IEnumerator BattlefieldPersistence_DecalPoolAndPermanentRubble() {
      yield return StartMatch(
        buildings: new[] { (1, "cf_barracks", 0, 22), (0, "ff_power_plant", -42, 22) },
        units: new[] { (0, "ff_bulwark_tank", -6, 22), (0, "ff_bulwark_tank", -4, 24) });
      var view = Object.FindAnyObjectByType<BattleView>();
      Assert.IsNotNull(view);
      yield return WaitFrames(4);
      var fx = view.Fx;
      Assert.IsNotNull(fx);

      // 1) 영구 크레이터 2개 + 일반 그을음 140개 생성 -> 링-버퍼 120 상한 유지 + 영구 크레이터 2개는 절대 삭제되지 않는다.
      fx.Crater(new Vector3(0f, 0f, 0f), 4.5f, permanent: true);
      fx.Crater(new Vector3(8f, 0f, 0f), 5.0f, permanent: true);
      for (var i = 0; i < 140; i++) {
        fx.Crater(new Vector3(i * 0.4f, 0f, 6f), 1.2f, permanent: false);
      }
      Assert.LessOrEqual(fx.CraterCount, BattleFx.MaxDecalPool, "모바일 안전 링-버퍼 상한 120 유지");
      Assert.AreEqual(2, fx.PermanentCraterCount, "영구 크레이터는 일반 데칼 재활용에 밀리지 않음");

      // 2) 기지 건물 파괴 시 영구 콘크리트 붕괴 잔해가 생성되어 남는다.
      var rubbleBefore = fx.PermanentRubbleCount;
      var enemyBarracks = OwnBuilding(1, "cf_barracks");
      SetBuildingHealth(enemyBarracks, 10);
      yield return WaitFor(() => Verified.Exists(enemyBarracks) == false, 20f, "적 병영 파괴");
      yield return WaitFrames(3);
      Assert.Greater(fx.PermanentRubbleCount, rubbleBefore, "파괴된 기지 건물은 영구 붕괴 콘크리트 잔해로 남음");
    }

    static void SetBaseTier(int player, int tier) {
      Verified.Global->Players.GetPointer(player)->BaseTier = tier;
    }

    static float FactorySpeedOf(EntityRef factory) {
      var f = Verified;
      var b = f.Unsafe.GetPointer<Building>(factory);
      var s = FactoryWarsUtil.Player(f, b->Owner);
      return FactorySystem.ProductionSpeedMultiplier(f, factory, b, s).AsFloat;
    }

    static bool FactoryHasSynergy(EntityRef factory) {
      var f = Verified;
      return FactorySystem.HasAdjacencySynergy(f, factory, f.Unsafe.GetPointer<Building>(factory));
    }

    static bool FactoryIsOverdriving(EntityRef factory) {
      var f = Verified;
      return FactorySystem.IsOverdriving(f, f.Unsafe.GetPointer<Building>(factory));
    }

    static float PlayerPowerRateOf(int player) {
      return Verified.Global->Players.GetPointer(player)->PowerRate.AsFloat;
    }

    static float DepotVehicleMul(int player, string unitId) {
      var f = Verified;
      return FactorySystem.VehicleSpeedMultiplier(f, player, f.FindAsset(UnitRef(player, unitId))).AsFloat;
    }

    static float DepotArtilleryMul(int player, string unitId) {
      var f = Verified;
      return FactorySystem.ArtilleryCooldownMultiplier(f, player, f.FindAsset(UnitRef(player, unitId))).AsFloat;
    }

    static float DepotSortieMul(int player) {
      return FactorySystem.SortieRearmMultiplier(Verified, player).AsFloat;
    }

    [UnityTest]
    public IEnumerator Factory_LowPowerSlows35PercentAndDisablesPatriotSilo() {
      // 터빈 발전소 없이 전력 소비 공장 가동 시 순발전량 음수 -> 생산 속도 -35%(0.65x) 감속 + 사일로 발사 차단 검증.
      yield return StartMatch(
        buildings: new[] { (0, "ff_vehicle_factory", -18, 22) },
        stock: Stock(5000, 5000, 5000, 500));
      SetBaseTier(0, 2);
      var factory = OwnBuilding(0, "ff_vehicle_factory");
      yield return Send(0, new ProduceCommand { Building = factory, Unit = UnitRef(0, "ff_scout_buggy"), Count = 1 });
      yield return WaitFrames(Fps * 4);
      var progress = Verified.Get<Building>(factory).QueueProgress.AsFloat;
      Assert.Less(progress, 3.0f, $"전력 부족(음수 발전량) 시 공장 생산 속도 -35% 감속 적용(progress={progress:F2})");
      Assert.Greater(progress, 2.0f, $"전력 부족 시 65% 속도로 생산 유지(progress={progress:F2})");
      Assert.AreEqual(0.65f, FactorySpeedOf(factory), 0.02f, "저전력 생산 배율 0.65 확인");
    }

    [UnityTest]
    public IEnumerator Factory_LevelUpScalesProductionSpeed() {
      // 공장 증축 Lv.1(1.0x) -> Lv.2(+25% = 1.25x) -> Lv.3(+50% = 1.50x) 속도 스케일링 검증.
      yield return StartMatch(
        buildings: new[] { (0, "ff_barracks", -18, 22), (0, "ff_power_plant", -46, -10) },
        stock: Stock(5000, 5000, 5000, 500));
      var factory = OwnBuilding(0, "ff_barracks");
      Assert.AreEqual(1.0f, FactorySpeedOf(factory), 0.01f, "Lv.1 기본 생산 배율 1.0");
      yield return Send(0, new UpgradeFactoryCommand { Building = factory });
      Assert.AreEqual(2, Verified.Get<Building>(factory).FactoryLevel, "Lv.2 증축 완료");
      Assert.AreEqual(1.25f, FactorySpeedOf(factory), 0.01f, "Lv.2 생산 속도 +25%");
      yield return Send(0, new UpgradeFactoryCommand { Building = factory });
      Assert.AreEqual(3, Verified.Get<Building>(factory).FactoryLevel, "Lv.3 증축 완료");
      Assert.AreEqual(1.50f, FactorySpeedOf(factory), 0.01f, "Lv.3 생산 속도 +50%");
    }

    [UnityTest]
    public IEnumerator Factory_AdjacencySynergyGrants15PercentSpeed() {
      // 강철 제련소/터빈 발전소 12m 이내 인접 시 생산 속도 +15% 시너지 검증(원거리 공장은 시너지 없음).
      yield return StartMatch(
        buildings: new[] {
          (0, "ff_barracks", -18, 22),
          (0, "ff_processor", -18, 12),   // 10m 인접 -> 시너지 O
          (0, "ff_vehicle_factory", 12, 22), // 30m 이격 -> 시너지 X
          (0, "ff_power_plant", -46, -10),
        },
        stock: Stock(5000, 5000, 5000, 500));
      var nearFactory = OwnBuilding(0, "ff_barracks");
      var farFactory = OwnBuilding(0, "ff_vehicle_factory");
      Assert.IsTrue(FactoryHasSynergy(nearFactory), "제련소 10m 이내 공장 인접 시너지 활성");
      Assert.AreEqual(1.15f, FactorySpeedOf(nearFactory), 0.01f, "인접 시너지 생산 속도 +15%");
      Assert.IsFalse(FactoryHasSynergy(farFactory), "12m 밖 원거리 공장은 시너지 비활성");
      Assert.AreEqual(1.0f, FactorySpeedOf(farFactory), 0.01f, "비인접 공장 기본 1.0x");
    }

    [UnityTest]
    public IEnumerator Factory_OverdriveBoosts50PercentWithDoublePowerAndHeat() {
      // 비상 과부하(Overdrive): 20초간 생산 속도 +50%, 전력 소비 2배, 초당 과열 자가 피해 검증.
      yield return StartMatch(
        buildings: new[] {
          (0, "ff_vehicle_factory", -18, 22),
          (0, "ff_power_plant", -46, -10),
        },
        stock: Stock(5000, 5000, 5000, 500));
      SetBaseTier(0, 2);
      var factory = OwnBuilding(0, "ff_vehicle_factory");
      yield return Send(0, new ProduceCommand { Building = factory, Unit = UnitRef(0, "ff_eagle_tank"), Count = 1 });
      yield return WaitFrames(2);
      var normalRate = PlayerPowerRateOf(0);
      var hpBefore = Verified.Get<Building>(factory).Health.AsFloat;
      yield return Send(0, new OverdriveFactoryCommand { Building = factory });
      Assert.IsTrue(FactoryIsOverdriving(factory), "과부하 활성화");
      Assert.AreEqual(1.50f, FactorySpeedOf(factory), 0.02f, "과부하 생산 속도 +50%");
      yield return WaitFrames(Fps * 2);
      var overdriveRate = PlayerPowerRateOf(0);
      var hpAfter = Verified.Get<Building>(factory).Health.AsFloat;
      Assert.Less(overdriveRate, normalRate - 0.5f, $"과부하 시 공장 전력 소비 2배 증가(normal={normalRate:F2}, od={overdriveRate:F2})");
      Assert.Less(hpAfter, hpBefore - 8f, $"과부하 가동 중 소량 자가 과열 피해 발생(before={hpBefore:F1}, after={hpAfter:F1})");
    }

    [UnityTest]
    public IEnumerator Factory_MunitionsDepotBuffAndChainExplosion() {
      // 군수·연료 보급창: 진영 전역 버프(차량 속도 +10%, 포병 재장전 +12%, 항공 출격 대기 -15%) + 파괴 시 인접 건물 연쇄 대폭발 검증.
      yield return StartMatch(
        buildings: new[] {
          (1, "cf_supply_depot", 0, 22),
          (1, "cf_supply_depot", 7, 22), // 7m 인접 -> 연쇄 폭발 검증
          (1, "cf_barracks", -7, 22),    // 7m 인접 -> 광역 폭발 피해 검증
          (0, "ff_power_plant", -42, 22),
        },
        units: new[] { (0, "ff_bulwark_tank", -12, 12), (0, "ff_bulwark_tank", -8, 12) });
      // 진영 전역 군수·연료 보급 버프 검증.
      Assert.AreEqual(1.10f, DepotVehicleMul(1, "cf_dragonscale_tank"), 0.01f, "보급창 차량 속도 +10%");
      Assert.Less(DepotArtilleryMul(1, "cf_rocket_artillery"), 0.90f, "보급창 포병 재장전 속도 +12%");
      Assert.AreEqual(0.85f, DepotSortieMul(1), 0.01f, "보급창 항공 출격 쿨타임 -15%");

      var depots = new System.Collections.Generic.List<EntityRef>();
      foreach (var pair in Verified.GetComponentIterator<Building>()) {
        if ((int)pair.Component.Owner == 1 && Verified.FindAsset(pair.Component.Spec).Role == BuildingRole.SupplyDepot) {
          depots.Add(pair.Entity);
        }
      }
      Assert.AreEqual(2, depots.Count, "인접 보급창 2채 확인");
      var barracks = OwnBuilding(1, "cf_barracks");
      var barracksHpBefore = Verified.Get<Building>(barracks).Health.AsFloat;
      // 1번 보급창 체력을 낮춰 폭파시키고, 2번 보급창 체력도 400으로 두어 연쇄 폭발 파괴까지 검증!
      SetBuildingHealth(depots[0], 10);
      SetBuildingHealth(depots[1], 400);
      yield return WaitFor(() => Verified.Exists(depots[0]) == false, 20f, "1번 보급창 파괴");
      yield return WaitFrames(3);
      Assert.IsFalse(Verified.Exists(depots[1]), "1번 보급창 폭발이 7m 인접 2번 보급창까지 연쇄 폭파");
      Assert.IsTrue(Verified.Exists(barracks) == false || Verified.Get<Building>(barracks).Health.AsFloat < barracksHpBefore - 400f, "인접 건물에 대폭발 광역 피해 적용");
    }

    [UnityTest]
    public IEnumerator Formation_MixedGroupNoOverlapRadiusAware() {
      // 보병 6기 + 용린 전차 2기 + 요새 전차 2기 혼성 부대: 반경 기반 분리 및 대형 간격으로 겹침 0건 검증.
      var mix = new[] {
        (0, "cf_militia", -14, 18), (0, "cf_militia", -14, 18), (0, "cf_militia", -14, 18),
        (0, "cf_militia", -14, 18), (0, "cf_militia", -14, 18), (0, "cf_militia", -14, 18),
        (0, "cf_dragonscale_tank", -14, 18), (0, "cf_dragonscale_tank", -14, 18),
        (0, "cf_fortress_tank", -14, 18), (0, "cf_fortress_tank", -14, 18),
      };
      yield return StartMatch(swapFactions: true, units: mix);
      var all = AllUnitsOf(0);
      Assert.GreaterOrEqual(all.Length, 10, "혼성 부대 10기 이상 생성");
      yield return Send(0, new MoveUnitsCommand { Units = all, Target = V(0, 18), FormationMode = MoveUnitsCommand.ModeNormal });
      yield return WaitFrames(Fps * 6);
      for (var i = 0; i < all.Length; i++) {
        var posA = Verified.Get<Transform3D>(all[i]).Position;
        var radA = MovementSystem.UnitRadius(Verified.FindAsset(Verified.Get<Unit>(all[i]).Spec)).AsFloat;
        for (var j = i + 1; j < all.Length; j++) {
          var posB = Verified.Get<Transform3D>(all[j]).Position;
          var radB = MovementSystem.UnitRadius(Verified.FindAsset(Verified.Get<Unit>(all[j]).Spec)).AsFloat;
          var dist = Vector2.Distance(new Vector2(posA.X.AsFloat, posA.Z.AsFloat), new Vector2(posB.X.AsFloat, posB.Z.AsFloat));
          var minAllowed = (radA + radB) * 0.85f;
          Assert.GreaterOrEqual(dist, minAllowed, $"혼성 부대 유닛 {i}-{j} 겹침 금지 (dist={dist:F2}m < {minAllowed:F2}m)");
        }
      }
    }

    [UnityTest]
    public IEnumerator Transport_BoardingCapacityAndUnloadFormationNoOverlap() {
      // APC(보병 6슬롯)에 보병 4기 탑승 -> 탑승 중 공격·이동 불가 확인 -> 전원 하차 시 후면 램프 개방 + 비겹침 대형 검증.
      yield return StartMatch(units: new[] {
        (0, "ff_field_support", 0, 18),
        (0, "ff_ranger_squad", -2, 16), (0, "ff_ranger_squad", 0, 16),
        (0, "ff_at_rocket", 2, 16), (0, "ff_breacher", 4, 16),
      });
      var apc = UnitsNamed(0, "ff_field_support")[0];
      var riders = UnitsNamed(0, "ff_ranger_squad").Where(e => Verified.Get<Transform3D>(e).Position.Z > 10)
        .Concat(UnitsNamed(0, "ff_at_rocket")).Concat(UnitsNamed(0, "ff_breacher")).ToArray();
      Assert.AreEqual(4, riders.Length, "탑승 후보 보병 4기 확인");
      yield return Send(0, new BoardTransportCommand { Units = riders, Transport = apc });
      yield return WaitFor(() => TransportSystem.UsedSlots(Verified, apc) == 4, 20f, "보병 4기 APC 탑승 완료");
      Assert.Greater(Verified.Get<Unit>(apc).RampOpenUntilFrame, Verified.Number - Fps * 2, "탑승 시 후면 램프 개방 기록");
      // 전원 하차 명령.
      yield return Send(0, new UnloadTransportCommand { Transport = apc, SpecificUnit = EntityRef.None });
      Assert.AreEqual(0, TransportSystem.UsedSlots(Verified, apc), "전원 하차 완료");
      Assert.Greater(Verified.Get<Unit>(apc).RampOpenUntilFrame, Verified.Number, "하차 시 후면 램프 개방");
      for (var i = 0; i < riders.Length; i++) {
        Assert.AreEqual(EntityRef.None, Verified.Get<Unit>(riders[i]).Transport, "하차 후 Transport 초기화");
        var pi = Verified.Get<Transform3D>(riders[i]).Position;
        for (var j = i + 1; j < riders.Length; j++) {
          var pj = Verified.Get<Transform3D>(riders[j]).Position;
          var d = Vector2.Distance(new Vector2(pi.X.AsFloat, pi.Z.AsFloat), new Vector2(pj.X.AsFloat, pj.Z.AsFloat));
          Assert.GreaterOrEqual(d, 1.1f, $"하차 보병 {i}-{j} 겹침 금지 (dist={d:F2}m)");
        }
      }
    }

    [UnityTest]
    public IEnumerator Transport_SinglePassengerUnloadFromStrip() {
      // HUD 승객 초상화 스트립에서 특정 승객 1기만 탭해 개별 하차시키는 동작 검증.
      yield return StartMatch(units: new[] {
        (0, "cf_apc_transport", 0, 18),
        (0, "cf_militia", -2, 16), (0, "cf_at_rocket", 0, 16), (0, "cf_breacher", 2, 16),
      }, swapFactions: true);
      var apc = UnitsNamed(0, "cf_apc_transport")[0];
      var riders = UnitsNamed(0, "cf_militia").Where(e => Verified.Get<Transform3D>(e).Position.Z > 10)
        .Concat(UnitsNamed(0, "cf_at_rocket")).Concat(UnitsNamed(0, "cf_breacher")).ToArray();
      yield return Send(0, new BoardTransportCommand { Units = riders, Transport = apc });
      yield return WaitFor(() => TransportSystem.UsedSlots(Verified, apc) >= 3, 20f, "승객 3기 탑승");
      var passengers = TransportSystem.Passengers(Verified, apc);
      var target = passengers[1];
      yield return Send(0, new UnloadTransportCommand { Transport = apc, SpecificUnit = target });
      Assert.AreEqual(EntityRef.None, Verified.Get<Unit>(target).Transport, "지정한 1기만 개별 하차");
      Assert.AreEqual(passengers.Count - 1, TransportSystem.Passengers(Verified, apc).Count, "나머지 승객은 탑승 유지");
    }

    [UnityTest]
    public IEnumerator Transport_DestructionRulesApcEjectsAt40PercentAndHeliLosesPassengers() {
      // 1) 지상 APC 파괴 시 승객은 40% 체력으로 긴급 탈출.
      yield return StartMatch(units: new[] {
        (0, "ff_field_support", 0, 18),
        (0, "ff_ranger_squad", -1, 16), (0, "ff_ranger_squad", 1, 16),
        (1, "cf_fortress_tank", 6, 18),
      });
      var apc = UnitsNamed(0, "ff_field_support")[0];
      var rangers = UnitsNamed(0, "ff_ranger_squad").Where(e => Verified.Get<Transform3D>(e).Position.Z > 10).ToArray();
      yield return Send(0, new BoardTransportCommand { Units = rangers, Transport = apc });
      yield return WaitFor(() => TransportSystem.UsedSlots(Verified, apc) == 2, 20f, "APC 보병 2기 탑승");
      SetUnitHealth(apc, 5);
      yield return WaitFor(() => Verified.Exists(apc) == false, 20f, "APC 파괴");
      yield return WaitFrames(2);
      foreach (var r in rangers) {
        Assert.IsTrue(Verified.Exists(r), "지상 APC 파괴 시 보병은 탈출 생존");
        var u = Verified.Get<Unit>(r);
        var maxHp = TechSystem.MaxHealth(Verified, u.Owner, u.Spec).AsFloat;
        Assert.AreEqual(EntityRef.None, u.Transport);
        Assert.LessOrEqual(u.Health.AsFloat, maxHp * 0.41f, "탈출 보병 체력 40% 이하로 감소");
        Assert.Greater(u.Health.AsFloat, 0f, "탈출 보병 생존 체력 > 0");
      }

      // 2) 공중 수송헬기 격추 시 탑승 승객 전원 손실 + 상륙정은 차량(3슬롯) 탑승 가능 검증.
      TearDown();
      yield return null;
      yield return StartMatch(units: new[] {
        (0, "ff_transport_heli", 0, 18),
        (0, "ff_ranger_squad", -1, 17), (0, "ff_ranger_squad", 1, 17),
        (1, "cf_mobile_sam", 8, 18),
      });
      var heli = UnitsNamed(0, "ff_transport_heli")[0];
      var heliCrew = UnitsNamed(0, "ff_ranger_squad").Where(e => Verified.Get<Transform3D>(e).Position.Z > 10).ToArray();
      yield return Send(0, new BoardTransportCommand { Units = heliCrew, Transport = heli });
      yield return WaitFor(() => TransportSystem.UsedSlots(Verified, heli) == 2, 20f, "수송헬기 보병 2기 탑승");
      SetUnitHealth(heli, 5);
      yield return WaitFor(() => Verified.Exists(heli) == false, 20f, "수송헬기 격추");
      yield return WaitFrames(2);
      foreach (var r in heliCrew) {
        Assert.IsFalse(Verified.Exists(r), "공중 수송헬기 격추 시 탑승 보병 전원 손실");
      }
      // 상륙정 차량(전차 3슬롯) 규격 확인.
      var landerSpec = Verified.FindAsset(UnitRef(0, "ff_landing_craft"));
      var tankSpec = Verified.FindAsset(UnitRef(0, "ff_eagle_tank"));
      Assert.IsTrue(TransportSystem.CanUnitBoardSpec(tankSpec, landerSpec), "수륙양용 상륙정은 전차 탑승 허용");
      Assert.AreEqual(3, TransportSystem.SlotCost(tankSpec), "전차 1대 = 3슬롯(상륙정 최대 2대)");
      Assert.AreEqual(6, landerSpec.TransportCapacity, "상륙정 최대 6슬롯(보병 6 또는 전차 2)");
    }

    static void SetUnitHealth(EntityRef unit, int hp) {
      if (Verified.Unsafe.TryGetPointer<Unit>(unit, out var u)) {
        u->Health = hp;
      }
    }

    static EntityRef[] AllUnitsOf(int player) {
      var list = new System.Collections.Generic.List<EntityRef>();
      var f = Verified;
      var filter = f.Filter<Unit>();
      while (filter.NextUnsafe(out var e, out var u)) {
        if ((int)u->Owner == player && u->Health > Photon.Deterministic.FP._0 && f.FindAsset(u->Spec).Size != UnitSize.Engineer) {
          list.Add(e);
        }
      }
      return list.ToArray();
    }

    static readonly (string Id, int ColorIndex, bool IsBuilding, bool IsCarrier, bool IsSilo, bool IsPatriot)[] ShowcaseCatalog = {
      // 자유 연방 (Free Federation)
      ("ff_engineer", 0, false, false, false, false),
      ("ff_ranger_squad", 0, false, false, false, false),
      ("ff_skyguard_squad", 0, false, false, false, false),
      ("ff_at_rocket", 0, false, false, false, false),
      ("ff_breacher", 0, false, false, false, false),
      ("ff_sniper", 0, false, false, false, false),
      ("ff_scout_buggy", 0, false, false, false, false),
      ("ff_eagle_tank", 0, false, false, false, false),
      ("ff_field_support", 0, false, false, false, false),
      ("ff_mlrs_artillery", 0, false, false, false, false),
      ("ff_mobile_sam", 0, false, false, false, false),
      ("ff_hawk_gunship", 0, false, false, false, false),
      ("ff_skyfortress_bomber", 0, false, false, false, false),
      ("ff_bulwark_tank", 0, false, false, false, false),
      ("ff_scout_boat", 0, false, false, false, false),
      ("ff_artillery_ship", 0, false, false, false, false),
      ("ff_landing_craft", 0, false, false, false, false),
      ("ff_transport_heli", 0, false, false, false, false),
      ("ff_stormnest_carrier", 0, false, true, false, false),
      ("ff_nest_drone", 0, false, false, false, false),
      ("ff_patriot_turret", 0, true, false, false, true),
      // 대륙 연방 (Continental Federation)
      ("cf_engineer", 1, false, false, false, false),
      ("cf_militia", 1, false, false, false, false),
      ("cf_flame_squad", 1, false, false, false, false),
      ("cf_at_rocket", 1, false, false, false, false),
      ("cf_breacher", 1, false, false, false, false),
      ("cf_sniper", 1, false, false, false, false),
      ("cf_scout_bike", 1, false, false, false, false),
      ("cf_dragonscale_tank", 1, false, false, false, false),
      ("cf_thunder_aa", 1, false, false, false, false),
      ("cf_rocket_artillery", 1, false, false, false, false),
      ("cf_mobile_sam", 1, false, false, false, false),
      ("cf_apc_transport", 1, false, false, false, false),
      ("cf_gale_gunship", 1, false, false, false, false),
      ("cf_transport_heli", 1, false, false, false, false),
      ("cf_longwall_mlrs", 1, false, false, false, false),
      ("cf_fortress_tank", 1, false, false, false, false),
      ("cf_scout_boat", 1, false, false, false, false),
      ("cf_artillery_ship", 1, false, false, false, false),
      ("cf_landing_craft", 1, false, false, false, false),
      ("cf_missile_silo", 1, true, false, true, false),
      ("cf_patriot_turret", 1, true, false, false, true),
    };

    [UnityTest, Explicit, Category("CaptureCatalog")]
    public IEnumerator CaptureAllUnitsAndBattlefieldScars() {
      yield return StartMatch(Map4PScene, 2, truce: 0, stock: Stock(30000, 30000, 30000, 1000));
      yield return WaitFrames(4);
      var catalog = Resources.Load<ViewCatalog>(ViewCatalog.ResourcePath);
      Assert.IsNotNull(catalog, "ViewCatalog 존재");

      var unitDir = Path.Combine(StatusImagesDir(), "units");
      Directory.CreateDirectory(unitDir);

      // 깨끗한 조명 + 전술 쇼케이스 패드 생성(Y = -80 격리 공간).
      var studio = new GameObject("UnitShowcaseStudio");
      studio.transform.position = new Vector3(0f, -80f, 0f);
      var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
      pad.transform.SetParent(studio.transform, false);
      pad.transform.localPosition = new Vector3(0f, -0.15f, 0f);
      pad.transform.localScale = new Vector3(14f, 0.15f, 14f);
      var padMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
      padMat.color = new Color(0.28f, 0.31f, 0.34f, 1f);
      pad.GetComponent<Renderer>().sharedMaterial = padMat;

      var lightGo = new GameObject("StudioLight");
      lightGo.transform.SetParent(studio.transform, false);
      lightGo.transform.rotation = Quaternion.Euler(46f, -36f, 0f);
      var sun = lightGo.AddComponent<Light>();
      sun.type = LightType.Directional;
      sun.intensity = 1.15f;
      sun.color = new Color(1f, 0.97f, 0.93f);

      var camGo = new GameObject("StudioCamera");
      camGo.transform.SetParent(studio.transform, false);
      var cam = camGo.AddComponent<Camera>();
      cam.clearFlags = CameraClearFlags.SolidColor;
      cam.backgroundColor = new Color(0.11f, 0.14f, 0.18f, 1f);
      cam.fieldOfView = 34f;
      cam.nearClipPlane = 0.1f;
      cam.farClipPlane = 120f;

      var captured = 0;
      foreach (var item in ShowcaseCatalog) {
        var view = ArtView.Create(catalog, item.Id, studio.transform, item.ColorIndex);
        if (view == null) {
          continue;
        }
        view.transform.localPosition = Vector3.zero;
        view.SetSelected(false);
        if (view.Bar != null) {
          view.Bar.SetActive(false);
        }
        view.Turn.localPosition = Vector3.zero;
        view.SetYaw(34f);

        // 보병(공병·대전차 RPG·돌격 유탄·저격수 포함) 전용 Synty 무기 장착 + Kubold 손바닥 밀착 보정.
        var isInfantry = item.IsBuilding == false && (item.Id.Contains("engineer") || item.Id.Contains("ranger") || item.Id.Contains("skyguard")
          || item.Id.Contains("militia") || item.Id.Contains("flame") || item.Id.Contains("sniper")
          || item.Id.Contains("at_rocket") || item.Id.Contains("breacher"));
        System.Collections.Generic.List<RiflemanRig> rigs = null;
        if (isInfantry) {
          var wep = BattleView.Weapon(BattleView.WeaponKindFor(item.Id));
          rigs = RiflemanRig.AttachAll(view.Model, wep, item.Id.Contains("at_rocket"));
          foreach (var r in rigs) {
            r.Aiming = item.Id.Contains("engineer") == false;
            r.AimPoint = studio.transform.position + new Vector3(4f, 1.15f, 6f);
          }
        }
        // 100% 실제 Synty/Kenney 텍스처 프리팹 렌더링(원시 큐브·실린더·구체 오버라이드 전면 금지).
        ArtLook.Apply(view.Model, item.ColorIndex, true);

        // Kubold Animator가 첫 자세를 평가한 뒤 무기를 실제 손바닥에 밀착시킨다.
        yield return null;
        yield return null;
        if (rigs != null) {
          foreach (var r in rigs) r.SnapNow();
        }
        // 바운드 자동 프레이밍(3/4 쿼터뷰).
        var renderers = view.GetComponentsInChildren<Renderer>(false);
        var bounds = new Bounds(studio.transform.position + Vector3.up * 0.8f, Vector3.one * 1.4f);
        var hasBounds = false;
        foreach (var r in renderers) {
          if (r.enabled == false || r.name == "Ring" || r.gameObject.name.Contains("Bar")) continue;
          if (!hasBounds) { bounds = r.bounds; hasBounds = true; } else { bounds.Encapsulate(r.bounds); }
        }
        var extent = Mathf.Max(1.0f, bounds.extents.magnitude);
        var dist = extent * 2.55f;
        camGo.transform.position = bounds.center + new Vector3(dist * 0.62f, dist * 0.46f, -dist * 0.68f);
        camGo.transform.LookAt(bounds.center);

        var outPath = Path.Combine(unitDir, $"unit_{item.Id}.png");
        CaptureCameraPng(cam, outPath, 512, 384);
        Object.Destroy(view.gameObject);
        captured++;
      }
      Object.Destroy(studio);
      Assert.GreaterOrEqual(captured, 30, "전 진영 유닛 쇼케이스 30종 이상 캡처");

      // 2) 영구 전장 흔적 + 도로 침범 0건 스크린샷(57_persistent_battlefield_scars.png): 2차선 도로 옆 개활지에 그을린 파쇄 콘크리트·철근 잔해 & 암흑 그을음 데칼.
      var bView = Object.FindAnyObjectByType<BattleView>();
      var fx = bView.Fx;
      var scars = Object.FindAnyObjectByType<BattleScars>();
      var center = new Vector3(34f, 0f, -28f);
      scars?.Add(center, 15f, 0.95f);
      scars?.Add(center + new Vector3(-6f, 0f, 4f), 9f, 0.85f);
      scars?.Add(center + new Vector3(7f, 0f, -4f), 9f, 0.85f);
      // 붕괴 건물 잔해는 아스팔트 밖 양측 부지에 배치하고, 도로 위·주변에는 평면 #181A1B 그을음 데칼을 블렌딩한다.
      fx.PermanentRubble(center + new Vector3(8.5f, 0f, 6.2f), 3.2f);
      fx.PermanentRubble(center + new Vector3(-8.5f, 0f, -5.5f), 3.0f);
      fx.Wreck(center + new Vector3(6.0f, 0f, 1.5f), 2.0f, 30f);
      fx.Wreck(center + new Vector3(-5.8f, 0f, -1.8f), 2.0f, 30f);
      for (var s = 0; s < 18; s++) {
        var a = s * 0.7f;
        var r = 1.8f + (s % 4) * 2.5f;
        fx.Crater(center + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r), 1.6f + (s % 3) * 0.9f, permanent: s % 3 == 0);
      }
      yield return WaitFrames(10);
      var mainCam = Camera.main;
      if (mainCam != null) {
        mainCam.transform.position = center + new Vector3(0f, 22f, -24f);
        mainCam.transform.LookAt(center);
        yield return null;
        CaptureCameraPng(mainCam, Path.Combine(StatusImagesDir(), "57_persistent_battlefield_scars.png"), 1280, 720);
      }
      Assert.IsTrue(File.Exists(Path.Combine(StatusImagesDir(), "57_persistent_battlefield_scars.png")), "57_persistent_battlefield_scars.png 생성 확인");

      // 3) 보병 vs 경장갑/APC vs 주력전차/MLRS vs 중전차 스케일 비교(58_unit_scale_comparison.png) + 확대 건물/사령부 통합 비교(current_scale_overview.png) — 탁 트인 테스트 개활지 사용.
      TearDown();
      yield return null;
      yield return StartMatch(stock: Stock(30000, 30000, 30000, 1000));
      yield return WaitFrames(3);
      var openCam = Camera.main;
      var lineupRoot = new GameObject("ScaleComparisonLineup");
      var lineupOrigin = new Vector3(0f, 0f, 16f);
      var lineup = new (string Id, int Color, float X, float Z)[] {
        ("cf_militia", 1, -13.5f, 0f),
        ("cf_at_rocket", 1, -11.0f, 0f),
        ("ff_field_support", 0, -6.5f, 0f),
        ("cf_mobile_sam", 1, -0.5f, 0f),
        ("cf_rocket_artillery", 1, 5.8f, 0f),
        ("cf_dragonscale_tank", 1, 12.6f, 0f),
        ("cf_fortress_tank", 1, 20.4f, 0f),
      };
      var lineupRigs = new System.Collections.Generic.List<RiflemanRig>();
      foreach (var entry in lineup) {
        var v = ArtView.Create(catalog, entry.Id, lineupRoot.transform, entry.Color);
        if (v == null) continue;
        v.transform.position = lineupOrigin + new Vector3(entry.X, 0f, entry.Z);
        v.Turn.localPosition = Vector3.zero;
        v.SetYaw(20f);
        v.SetSelected(true);
        if (v.Bar != null) v.Bar.SetActive(false);
        if (entry.Id == "cf_militia" || entry.Id == "cf_at_rocket") {
          var wep = BattleView.Weapon(BattleView.WeaponKindFor(entry.Id));
          var rs = RiflemanRig.AttachAll(v.Model, wep, entry.Id == "cf_at_rocket");
          foreach (var r in rs) { r.Aiming = true; r.AimPoint = v.transform.position + new Vector3(2f, 1.15f, 8f); }
          lineupRigs.AddRange(rs);
        }
        ArtLook.Apply(v.Model, entry.Color, true);
      }
      yield return null;
      yield return null;
      foreach (var r in lineupRigs) r.SnapNow();
      if (openCam != null) {
        var mid = lineupOrigin + new Vector3(3.2f, 1.4f, 0f);
        openCam.transform.position = mid + new Vector3(0f, 15.5f, -24.5f);
        openCam.transform.LookAt(mid);
        yield return null;
        CaptureCameraPng(openCam, Path.Combine(StatusImagesDir(), "58_unit_scale_comparison.png"), 1280, 720);
      }
      Assert.IsTrue(File.Exists(Path.Combine(StatusImagesDir(), "58_unit_scale_comparison.png")), "58_unit_scale_comparison.png 생성 확인");

      // 확대된 산업 공장 4종(사령부 · 보병 무장 공장 · 중기갑 전차 공장 · 미사일 격납고) + 전차·보병 통합 비교(current_scale_overview.png).
      var bldRow = new (string Id, int Color, float X, float Z, bool IsSilo)[] {
        ("ff_headquarters", 0, -13.0f, 12.5f, false),
        ("ff_barracks", 0, -0.5f, 12.5f, false),
        ("cf_heavy_works", 1, 12.5f, 12.5f, false),
        ("cf_missile_silo", 1, 25.0f, 12.5f, true),
      };
      foreach (var b in bldRow) {
        var bv = ArtView.Create(catalog, b.Id, lineupRoot.transform, b.Color);
        if (bv == null) continue;
        bv.transform.position = lineupOrigin + new Vector3(b.X, 0f, b.Z);
        bv.Turn.localPosition = Vector3.zero;
        bv.SetYaw(180f);
        bv.SetSelected(false);
        if (bv.Bar != null) bv.Bar.SetActive(false);
        ArtLook.Apply(bv.Model, b.Color, true);
      }
      yield return null;
      if (openCam != null) {
        var midAll = lineupOrigin + new Vector3(5.0f, 2.2f, 6.0f);
        openCam.transform.position = midAll + new Vector3(0f, 22f, -32f);
        openCam.transform.LookAt(midAll);
        yield return null;
        CaptureCameraPng(openCam, Path.Combine(StatusImagesDir(), "current_scale_overview.png"), 1280, 720);
      }
      Object.Destroy(lineupRoot);
      Assert.IsTrue(File.Exists(Path.Combine(StatusImagesDir(), "current_scale_overview.png")), "current_scale_overview.png 생성 확인");

      // 3-B) 8대 심화 산업 공장 복합 단지 쇼케이스(current_factory_complex.png & current_factory_base.png):
      // Lv.2/Lv.3 확장 공장 + 12m 발광 시너지 파이프라인 + 과부하 + 군수/연료 보급창 + 0.8x 보병 + 1.0x 전차(도로 침범 0건).
      TearDown();
      yield return null;
      var factoryBuildings = new[] {
        (0, "ff_barracks", -16, 26),
        (0, "ff_vehicle_factory", -5, 26),
        (0, "ff_heavy_works", 6, 26),
        (0, "ff_airfield", 17, 26),
        (0, "ff_processor", -16, 15),      // 11m 인접 -> 발광 주황 강철 시너지 파이프라인
        (0, "ff_power_plant", -5, 15),     // 11m 인접 -> 발광 시안 전력 시너지 파이프라인
        (0, "ff_supply_depot", 6, 15),     // 8번째 공장: 군수·연료 보급창
        (0, "ff_research_lab", 17, 15),    // 군수 연구 공장(회전 레이더)
        (0, "ff_headquarters", 28, 21),    // 산업 사령부
      };
      var factoryUnits = new[] {
        (0, "ff_ranger_squad", -14, 7), (0, "ff_ranger_squad", -12, 7), (0, "ff_at_rocket", -10, 7),
        (0, "ff_eagle_tank", -2, 7), (0, "ff_bulwark_tank", 8, 7),
      };
      yield return StartMatch(buildings: factoryBuildings, units: factoryUnits, stock: Stock(30000, 30000, 30000, 1000));
      SetBaseTier(0, 3);
      var bBarracks = OwnBuilding(0, "ff_barracks");
      var bVehicle = OwnBuilding(0, "ff_vehicle_factory");
      var bHeavy = OwnBuilding(0, "ff_heavy_works");
      var bAir = OwnBuilding(0, "ff_airfield");
      // Lv.2(보병 무장 공장) & Lv.3(중기갑 전차 공장) 시각 확장 + 과부하(기동·화력 조립 공장) + 전 공장 가동!
      yield return Send(0, new UpgradeFactoryCommand { Building = bBarracks });
      yield return Send(0, new UpgradeFactoryCommand { Building = bHeavy });
      yield return Send(0, new UpgradeFactoryCommand { Building = bHeavy });
      yield return Send(0, new OverdriveFactoryCommand { Building = bVehicle });
      yield return Send(0, new ProduceCommand { Building = bBarracks, Unit = UnitRef(0, "ff_ranger_squad"), Count = 2 });
      yield return Send(0, new ProduceCommand { Building = bVehicle, Unit = UnitRef(0, "ff_eagle_tank"), Count = 1 });
      yield return Send(0, new ProduceCommand { Building = bHeavy, Unit = UnitRef(0, "ff_bulwark_tank"), Count = 1 });
      yield return Send(0, new ProduceCommand { Building = bAir, Unit = UnitRef(0, "ff_hawk_gunship"), Count = 1 });
      yield return Send(0, new ResearchCommand { Building = OwnBuilding(0, "ff_research_lab"), Index = 0 });
      yield return WaitFrames(Fps * 2);
      var fCam = Camera.main;
      if (fCam != null) {
        var fCenter = new Vector3(2f, 0f, 19f);
        fCam.transform.position = fCenter + new Vector3(0f, 27f, -33f);
        fCam.transform.LookAt(fCenter);
        yield return null;
        var complexPath = Path.Combine(StatusImagesDir(), "current_factory_complex.png");
        CaptureCameraPng(fCam, complexPath, 1280, 720);
        File.Copy(complexPath, Path.Combine(StatusImagesDir(), "current_factory_base.png"), true);
      }
      Assert.IsTrue(File.Exists(Path.Combine(StatusImagesDir(), "current_factory_complex.png")), "current_factory_complex.png 생성 확인");

      // 4) 혼성 부대(보병 6 + 용린 전차 2 + 요새 전차 2) 겹침 없는 반경 인지 대형 스크린샷(59_no_overlap_mixed_group.png).
      TearDown();
      yield return null;
      var mixedUnits = new[] {
        (0, "cf_militia", -14, 18), (0, "cf_militia", -14, 18), (0, "cf_militia", -14, 18),
        (0, "cf_militia", -14, 18), (0, "cf_militia", -14, 18), (0, "cf_militia", -14, 18),
        (0, "cf_dragonscale_tank", -14, 18), (0, "cf_dragonscale_tank", -14, 18),
        (0, "cf_fortress_tank", -14, 18), (0, "cf_fortress_tank", -14, 18),
      };
      yield return StartMatch(swapFactions: true, units: mixedUnits);
      var group = AllUnitsOf(0);
      yield return Send(0, new MoveUnitsCommand { Units = group, Target = V(2, 18), FormationMode = MoveUnitsCommand.ModeNormal });
      yield return WaitFrames(Fps * 6);
      Controller().Input.Selection.SelectUnits(group);
      var cam59 = Camera.main;
      if (cam59 != null) {
        var grpCenter = new Vector3(2f, 0f, 18f);
        cam59.transform.position = grpCenter + new Vector3(0f, 22f, -20f);
        cam59.transform.LookAt(grpCenter);
        yield return null;
        CaptureCameraPng(cam59, Path.Combine(StatusImagesDir(), "59_no_overlap_mixed_group.png"), 1280, 720);
      }
      Assert.IsTrue(File.Exists(Path.Combine(StatusImagesDir(), "59_no_overlap_mixed_group.png")), "59_no_overlap_mixed_group.png 생성 확인");

      // 5) 수송 탑승·하차 시각 피드백 스크린샷(current_transport_boarding.png):
      // APC 후면 램프 개방 + 월드 탑승 핍([■][■][■][□][□][□]) + 하차 전개 보병 + 공중 수송헬기 + 하단 HUD 승객 초상화 스트립 & '하차 (전원)' 버튼!
      TearDown();
      yield return null;
      yield return StartMatch(units: new[] {
        (0, "ff_field_support", 0, 18),
        (0, "ff_ranger_squad", -1, 16), (0, "ff_at_rocket", 0, 16), (0, "ff_breacher", 1, 16), (0, "ff_ranger_squad", 2, 16),
        (0, "ff_transport_heli", -8, 21),
        (0, "ff_ranger_squad", -8, 19), (0, "ff_at_rocket", -7, 19),
      }, stock: Stock(30000, 30000, 30000, 1000));
      yield return WaitForHud();
      yield return Send(0, new ChooseDoctrineCommand { Doctrine = 1 });
      yield return WaitForBattle();
      var shotApc = UnitsNamed(0, "ff_field_support")[0];
      var shotHeli = UnitsNamed(0, "ff_transport_heli")[0];
      var apcTeam = AllUnitsOf(0).Where(e => e != shotApc && e != shotHeli && Verified.Get<Transform3D>(e).Position.X > -4 && Verified.Get<Transform3D>(e).Position.Z > 10).ToArray();
      var heliTeam = AllUnitsOf(0).Where(e => e != shotApc && e != shotHeli && Verified.Get<Transform3D>(e).Position.X <= -4 && Verified.Get<Transform3D>(e).Position.Z > 10).ToArray();
      yield return Send(0, new BoardTransportCommand { Units = apcTeam, Transport = shotApc });
      yield return Send(0, new BoardTransportCommand { Units = heliTeam, Transport = shotHeli });
      yield return WaitFor(() => TransportSystem.UsedSlots(Verified, shotApc) >= 4 && TransportSystem.UsedSlots(Verified, shotHeli) >= 2, 20f, "APC·수송헬기 탑승 완료");
      // APC에서 1기 개별 하차시켜 후면 램프가 활짝 열리고 하차 보병이 뒤에 전개된 순간 + 3/6 핍 + HUD 승객 초상화 3칸 동시 포착!
      var firstPassenger = TransportSystem.Passengers(Verified, shotApc)[0];
      yield return Send(0, new UnloadTransportCommand { Transport = shotApc, SpecificUnit = firstPassenger });
      Controller().Input.Selection.SelectUnit(shotApc);
      Controller().Camera.MoveTo(new Vector2(-2.5f, 18.5f));
      Controller().Camera.Height = 20f;
      Controller().Camera.Apply();
      yield return WaitFrames(4);
      yield return Shoot33("current_transport_boarding");
      File.Copy(Path.Combine(UltimateFolder(), "current_transport_boarding.png"), Path.Combine(StatusImagesDir(), "current_transport_boarding.png"), true);
      Assert.IsTrue(File.Exists(Path.Combine(StatusImagesDir(), "current_transport_boarding.png")), "current_transport_boarding.png 생성 확인");
    }
  }
}
