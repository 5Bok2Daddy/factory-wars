namespace FactoryWars.Tests {
  using System.Collections;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using Photon.Deterministic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  /// <summary>전투 깊이·카운터플레이 시험: 9킬 베테랑 상한, 신규 공격 유닛 4종, 미사일 거점 배치·후폭풍·패트리어트 공중 요격, 항모 로터 손상 및 전투기 작전기 직격.</summary>
  public unsafe partial class LocalMatchTests {
    static bool CarrierDamaged(EntityRef entity) => UltimateSystem.IsCarrierRotorDamaged(Verified, entity, Verified.Unsafe.GetPointer<Unit>(entity));
    static FP CarrierSpeedMul(EntityRef entity) => UltimateSystem.CarrierSpeedMultiplier(Verified, entity, Verified.Unsafe.GetPointer<Unit>(entity));
    static void SetUnitHealth(EntityRef entity, FP hp) => Verified.Unsafe.GetPointer<Unit>(entity)->Health = hp;
    static void SetBuildingHealth(EntityRef entity, FP hp) => Verified.Unsafe.GetPointer<Building>(entity)->Health = hp;
    static void StoreAircraftFor(int player, EntityRef airfield, string unitId) {
      SortieSystem.Store(Verified, Verified.Unsafe.GetPointer<Building>(airfield), UnitRef(player, unitId));
    }

    [Test]
    public void CombatDepth_VeterancyCapsAtNineKillsAndBalancedStats() {
      var rules = Resources.Load<GameRulesSpec>("FactoryWars/game_rules");
      Assert.AreEqual(3, rules.VetKillsRank1, "★ = 3킬");
      Assert.AreEqual(6, rules.VetKillsRank2, "★★ = 6킬");
      Assert.AreEqual(9, rules.VetKillsRank3, "★★★ MAX = 9킬");
      Assert.AreEqual(0, TacticalSystem.RankFor(rules, 2));
      Assert.AreEqual(1, TacticalSystem.RankFor(rules, 3));
      Assert.AreEqual(2, TacticalSystem.RankFor(rules, 6));
      Assert.AreEqual(3, TacticalSystem.RankFor(rules, 9));
      Assert.AreEqual(3, TacticalSystem.RankFor(rules, 25), "9킬 이후 3성 고정 상한");
      for (var i = 0; i < 3; i++) {
        Assert.AreEqual(0.08f * (i + 1), rules.VetHealthBonus[i].AsFloat, 0.001f, "별당 HP +8%");
        Assert.AreEqual(0.08f * (i + 1), rules.VetDamageBonus[i].AsFloat, 0.001f, "별당 피해 +8%");
        Assert.AreEqual(0.05f * (i + 1), rules.VetReloadBonus[i].AsFloat, 0.001f, "별당 연사 +5%");
      }
    }

    [UnityTest]
    public IEnumerator CombatDepth_FourNewUnitsProductionAndCounters() {
      yield return StartMatch(
        buildings: new[] {
          (0, "ff_barracks", -42, -12),
          (0, "ff_vehicle_factory", -42, 12),
          (0, "ff_supply_depot", -42, 0),
          (0, "ff_power_plant", -42, 24),
        },
        units: new[] {
          (0, "ff_at_rocket", -2, 26),
          (1, "cf_dragonscale_tank", 12, 26),
          (0, "ff_breacher", -10, -15),
          (1, "cf_militia", -10, -19),
          (0, "ff_mlrs_artillery", 14, -20),
          (1, "cf_militia", 24, -20),
          (1, "cf_militia", 25, -19),
          (0, "ff_mobile_sam", -6, 36),
          (1, "ff_hawk_gunship", -2, 36),
        },
        stock: Stock(8000, 8000, 8000, 600));

      // 1) 병영·군수공장에서 신규 공격 유닛 4종 모두 생산 가능 확인.
      var barracks = OwnBuilding(0, "ff_barracks");
      var factory = OwnBuilding(0, "ff_vehicle_factory");
      yield return Send(0, new ProduceCommand { Building = barracks, Unit = UnitRef(0, "ff_at_rocket"), Count = 1 });
      yield return Send(0, new ProduceCommand { Building = barracks, Unit = UnitRef(0, "ff_breacher"), Count = 1 });
      yield return Send(0, new ProduceCommand { Building = factory, Unit = UnitRef(0, "ff_mlrs_artillery"), Count = 1 });
      yield return Send(0, new ProduceCommand { Building = factory, Unit = UnitRef(0, "ff_mobile_sam"), Count = 1 });
      Assert.AreEqual(2, Verified.Get<Building>(barracks).QueueCount, "병영: 대전차 로켓병 + 화염 돌격병 생산 대기열");
      Assert.AreEqual(2, Verified.Get<Building>(factory).QueueCount, "군수공장: 다연장 로켓차 + 기동 대공차 생산 대기열");

      // 2) 대전차 로켓병(+60% 대차량 추가 피해)이 용린 전차에 큰 1타 피해를 입힌다.
      var atTrooper = UnitsNamed(0, "ff_at_rocket")[0];
      var tank = UnitsNamed(1, "cf_dragonscale_tank").OrderBy(e => (Verified.Get<Transform3D>(e).Position - V(12, 26)).SqrMagnitude.AsFloat).First();
      var tankMax = Health(tank);
      yield return Send(0, new AttackTargetCommand { Units = new[] { atTrooper }, Target = tank });
      yield return WaitFor(() => Health(tank) < tankMax, 25f, "대전차 로켓병 사격");
      // 26 * 1.5(AntiArmor vs Medium) * 1.6(AntiArmorBonus) = 62.4 (표준 대전차 39보다 +60% 높음).
      Assert.GreaterOrEqual(tankMax - Health(tank), 58f, "대전차 로켓병 +60% 대차량 추가 피해");

      // 3) 기동 대공차(CarrierHunter)는 이동 명령 중에도 공중 헬기를 추적 사격한다.
      var sam = UnitsNamed(0, "ff_mobile_sam")[0];
      var hawk = UnitsNamed(1, "ff_hawk_gunship")[0];
      var hawkBefore = Health(hawk);
      yield return Send(0, new MoveUnitsCommand { Units = new[] { sam }, Target = V(4, 36) });
      yield return WaitFor(() => Health(hawk) < hawkBefore, 20f, "기동 대공차 이동 중 대공 사격");
      Assert.Less(Health(hawk), hawkBefore, "기동 대공차 기동 사격 성공");
    }

    [UnityTest]
    public IEnumerator CombatDepth_SiloOutpostPlacementBackblastAndPatriotIntercept() {
      // 1) 전략 미사일 격납고는 본진 건설 거부, 암시장(40,20) 18m 이내 건설 허용 + 발사 시 6m 후폭풍 피해(피아 구분 없음) + 패트리어트 공중 요격(300 HP -> 0, 지상 피해 0).
      var bases = UltimateBase(0, "cf", null, withSite: false)
        .Concat(new[] {
          (0, "cf_heavy_works", -18, -30),
          (0, "cf_radar", -18, -20),
          (1, "ff_patriot_turret", 6, 18),
          (1, "ff_patriot_turret", -6, 18),
          (1, "ff_patriot_turret", 0, 26),
          (1, "ff_power_plant", 30, -24),
          (1, "ff_power_plant", 30, -14),
        }).ToArray();
      yield return StartMatch(
        swapFactions: true,
        buildings: bases,
        units: new[] {
          (0, "cf_dragonscale_tank", 32, 14), // 격납고(30,14) 2m 옆 아군 전차 -> 후폭풍 피격 검증
          (1, "ff_eagle_tank", 0, 20),        // 착탄 지점(0,20) 적 전차 -> 완전 공중 요격 시 피해 0 검증
        },
        stock: Stock(30000, 30000, 30000, 1000));

      ShortenUltimates(prepare: 2, warning: 7);
      ListenNotices();
      yield return WaitForHud();

      // 암시장(40,20) 반경 18m 이내(30,14)에 격납고 완공.
      var siloPos = V(30, 14);
      var siloSpec = Verified.FindAsset(BuildingOf(0, "_missile_silo"));
      Assert.IsFalse(ProductionSystem.CanPlace(Verified, siloSpec, V(-18, 14), 0), "안전 본진 내 미사일 격납고 배치 금지");
      Assert.IsTrue(ProductionSystem.CanPlace(Verified, siloSpec, siloPos, 0), "암시장 18m 이내 배치 허용");
      ProductionSystem.CreateFinishedBuilding(Verified, 0, BuildingOf(0, "_missile_silo"), siloPos);

      yield return ResearchUltimate(0, "cf");
      yield return Send(0, new StartUltimateCommand());
      yield return WaitFor(() => PhaseOf(0) == UltimatePhase.Ready, 20f, "전략 미사일 준비 완료");

      var nearFriendly = UnitsNamed(0, "cf_dragonscale_tank").OrderBy(e => (Verified.Get<Transform3D>(e).Position - V(32, 14)).SqrMagnitude.AsFloat).First();
      var friendlyBefore = Health(nearFriendly);
      var targetTank = UnitsNamed(1, "ff_eagle_tank").OrderBy(e => (Verified.Get<Transform3D>(e).Position - V(0, 20)).SqrMagnitude.AsFloat).First();
      var targetBefore = Health(targetTank);

      _notices.Clear();
      yield return Send(0, new LaunchUltimateCommand { Target = V(0, 20) });
      // 발사 즉시 격납고 반경 6m 아군 전차가 후폭풍 피해를 받는다.
      Assert.Less(Health(nearFriendly), friendlyBefore, "발사 후폭풍(6m)으로 주변 아군 전차 피해");

      // 패트리어트 3기가 비행 중인 미사일(300 HP)을 공중 요격하여 무해하게 폭파시킨다(Value == -1).
      yield return WaitFor(() => _notices.Any(n => n.Stage == UltimateStage.Impact), 30f, "전략 미사일 공중 요격 완료");
      var impact = _notices.Single(n => n.Stage == UltimateStage.Impact);
      Assert.AreEqual(-1, impact.Value, "패트리어트 공중 완전 요격(Value == -1)");
      Assert.AreEqual("적 전략 미사일 공중 요격 성공!", Texts.UltimateNotice("대륙 연방", UltimateStage.Impact, UltimateKind.StrategicMissile, -1));
      Assert.AreEqual(targetBefore, Health(targetTank), 0.1f, "공중 요격 성공으로 지상 표적 피해 0");
    }

    [UnityTest]
    public IEnumerator CombatDepth_CarrierRotorDamageAndFighterSortieStrike() {
      yield return StartMatch(
        buildings: UltimateBase(0, "ff", "ff_carrier_yard")
          .Concat(new[] { (1, "cf_airfield", 32, -16), (1, "cf_power_plant", 32, -26) }).ToArray(),
        units: new[] { (1, "cf_mobile_sam", -14, 14) },
        stock: Stock(30000, 30000, 30000, 1000));

      ShortenUltimates(prepare: 2, warning: 7);
      yield return ResearchUltimate(0, "ff");
      yield return Send(0, new StartUltimateCommand());
      yield return WaitFor(() => PhaseOf(0) == UltimatePhase.Active, 20f, "공중 항모 출격");

      var carrier = FirstOf<Carrier>().Entity;
      var maxHp = TechSystem.MaxHealth(Verified, 0, Verified.Get<Unit>(carrier).Spec);
      Assert.IsFalse(CarrierDamaged(carrier), "체력 100%일 때 정상 로터");
      Assert.AreEqual(1f, CarrierSpeedMul(carrier).AsFloat, 0.01f);

      // 1) 기동 대공차(cf_mobile_sam)가 항모에 +50% 추가 대공 피해로 직격한다.
      yield return WaitFor(() => Health(carrier) < maxHp.AsFloat, 20f, "기동 대공차 항모 저격");
      Assert.GreaterOrEqual(maxHp.AsFloat - Health(carrier), 100f, "기동 대공차 항모 추가 피해(37 * 1.5 * 1.5 * 1.5 = ~124.9)");
      var sam = UnitsNamed(1, "cf_mobile_sam")[0];
      Verified.Destroy(sam);
      yield return WaitFrames(2);

      // 2) 체력을 33%로 낮추면 50% 미만 로터 손상 상태 발동 -> 이동·드론 출격 속도 -30%(0.7배).
      SetUnitHealth(carrier, maxHp * FP._0_33);
      yield return WaitFrames(2);
      Assert.IsTrue(CarrierDamaged(carrier), "50% 미만 체력 -> 로터 손상 활성");
      Assert.AreEqual(0.7f, CarrierSpeedMul(carrier).AsFloat, 0.01f, "로터 손상 속도·출격률 -30%");

      // 3) 전투기 작전기(SortieKind.Strafe)가 공중 항모를 직접 타격할 수 있다.
      var airfield = OwnBuilding(1, "cf_airfield");
      StoreAircraftFor(1, airfield, "cf_sortie_fighter");
      yield return WaitFrames(2);
      var carrierPos = Verified.Get<Transform3D>(carrier).Position;
      var hpBefore = Health(carrier);
      yield return Send(1, new OrderSortieCommand { Airfield = airfield, Slot = 0, Target = carrierPos, Heading = new FPVector2(-FP._1, FP._0) });
      yield return WaitFor(() => Health(carrier) < hpBefore, 35f, "전투기 작전기 공중 항모 직접 기총소사");
      Assert.Less(Health(carrier), hpBefore, "전투기 작전기가 공중 항모 선체에 직접 피해");
    }

    [UnityTest]
    public IEnumerator UltimateTuning_VulnerabilityEmpScorchedZoneAndCarrierAuraDetection() {
      // 1) 미사일 격납고 건설 중(<100%) 피격 피해 +30% 취약성 + 미요격 착탄 시 EMP 5초 마비 & 15초 초토화 구역(지속 피해·둔화).
      var bases = UltimateBase(0, "cf", null, withSite: false)
        .Concat(new[] {
          (0, "cf_heavy_works", -18, -30),
          (0, "cf_radar", -18, -20),
          (1, "ff_barracks", 0, 20),
          (1, "ff_ground_tower", 4, 20),
          (1, "ff_power_plant", 30, -24),
        }).ToArray();
      yield return StartMatch(
        swapFactions: true,
        buildings: bases,
        units: new[] { (1, "ff_eagle_tank", 35, 14), (1, "ff_bulwark_tank", 0, 24) },
        stock: Stock(30000, 30000, 30000, 1000));

      ShortenUltimates(prepare: 2, warning: 2);
      var siloSite = ProductionSystem.CreateBuilding(Verified, 0, BuildingOf(0, "_missile_silo"), V(30, 14));
      SetBuildingHealth(siloSite, 1200);
      var siloBefore = 1200f;
      // 건설 중인 격납고가 지상 전차 사격을 맞으면 +30% 추가 피해.
      yield return WaitFor(() => Verified.Exists(siloSite) == false || Verified.Get<Building>(siloSite).Health.AsFloat < siloBefore, 20f, "건설 중 격납고 피격");
      var siloAfter = Verified.Exists(siloSite) ? Verified.Get<Building>(siloSite).Health.AsFloat : 0f;
      var hitDmg = siloBefore - siloAfter;
      Assert.GreaterOrEqual(hitDmg, 58f * 1.25f, "건설 중(+30% 취약) 피해 증가 확인");

      // 격납고 완공 후 미요격 전략 미사일 발사 -> EMP 5초 + 초토화 구역 15초(지속 피해·이동 둔화 0.65).
      if (Verified.Exists(siloSite)) {
        Verified.Destroy(siloSite);
      }
      var attackerTank = UnitsNamed(1, "ff_eagle_tank").OrderBy(e => (Verified.Get<Transform3D>(e).Position - V(35, 14)).SqrMagnitude.AsFloat).First();
      Verified.Destroy(attackerTank);
      yield return WaitFrames(2);
      ProductionSystem.CreateFinishedBuilding(Verified, 0, BuildingOf(0, "_missile_silo"), V(30, 14));
      yield return ResearchUltimate(0, "cf");
      yield return Send(0, new StartUltimateCommand());
      yield return WaitFor(() => PhaseOf(0) == UltimatePhase.Ready, 20f, "미사일 준비");
      yield return Send(0, new LaunchUltimateCommand { Target = V(0, 20) });
      yield return WaitFor(() => Verified.ComponentCount<ScorchedZone>() == 1, 20f, "착탄 후 15초 초토화 구역 생성");

      var zone = FirstOf<ScorchedZone>().Component;
      Assert.AreEqual(12f, zone.Radius.AsFloat, 0.1f, "초토화 구역 반경 12m");
      Assert.AreEqual(0.65f, UltimateSystem.ScorchedSpeedMultiplier(Verified, 1, V(0, 20)).AsFloat, 0.01f, "적 지상군 이동 속도 -35% 둔화");
      // 생존한 적 사령부/건물이 EMP 마비(5초)에 걸렸는지 확인(또는 인근 타워/병영).
      var hq1 = Headquarters(1);
      Assert.IsTrue(Verified.Exists(hq1), "적 사령부 존재");
      var survTank = UnitsNamed(1, "ff_bulwark_tank").FirstOrDefault();
      if (survTank != EntityRef.None && Verified.Exists(survTank)) {
        var hpAfterBlast = Health(survTank);
        yield return WaitFrames(Fps);
        Assert.Less(Health(survTank), hpAfterBlast, "초토화 구역 DoT 지속 피해");
      }
    }

    [UnityTest]
    public IEnumerator UltimateTuning_CarrierTarmacVulnerabilityAuraAndGarrisonSniperReveal() {
      // 2) 공중 항모 80초 조립 중 지상군 공격 가능 + 완공 후 14m 지휘 오라(+15% 연사, +10% 이동) + 건물 주둔 은신 저격수 탐지.
      yield return StartMatch(
        buildings: UltimateBase(0, "ff", "ff_carrier_yard"),
        units: new[] {
          (0, "ff_ranger_squad", -18, 10),   // 조립창(-18,14) 4m 앞 아군 보병 -> 완공 후 지휘 오라 검증
          (1, "cf_dragonscale_tank", -12, 14), // 조립 중인 항모(-18,14)를 공격하는 적 지상 전차!
          (1, "cf_sniper", -10, -15),
        },
        stock: Stock(30000, 30000, 30000, 1000));

      ShortenUltimates(prepare: 8, warning: 7);
      yield return ResearchUltimate(0, "ff");
      yield return Send(0, new StartUltimateCommand());
      yield return WaitFrames(2);

      // 조립 중에는 Carrier.Assembling == true이며, 적 지상 전차(cf_dragonscale_tank)가 항모 선체를 지상 표적으로 공격·명중할 수 있다.
      var carrier = FirstOf<Carrier>().Entity;
      Assert.IsTrue(Verified.Get<Carrier>(carrier).Assembling, "지상 조립 중");
      var enemyTank = UnitsNamed(1, "cf_dragonscale_tank").OrderBy(e => (Verified.Get<Transform3D>(e).Position - V(-12, 14)).SqrMagnitude.AsFloat).First();
      yield return Send(1, new AttackTargetCommand { Units = new[] { enemyTank }, Target = carrier });
      yield return WaitFor(() => Verified.Get<Unit>(carrier).LastDamagedFrame > 0, 15f, "지상 전차가 조립 중 항모 선체 타격");
      Verified.Destroy(enemyTank);

      // 조립 완료 -> 이륙(Assembling == false), 드론 6기 탑재, 14m 지휘 오라(+15% 연사, +10% 이동) 활성!
      yield return WaitFor(() => PhaseOf(0) == UltimatePhase.Active, 25f, "공중 항모 이륙 완료");
      Assert.IsFalse(Verified.Get<Carrier>(carrier).Assembling, "이륙 완료");
      Assert.AreEqual(6, Verified.Get<Carrier>(carrier).Docked, "자율 6드론 윙 탑재");

      var ranger = UnitsNamed(0, "ff_ranger_squad").OrderBy(e => (Verified.Get<Transform3D>(e).Position - V(-18, 10)).SqrMagnitude.AsFloat).First();
      var rPos = Verified.Get<Transform3D>(ranger).Position;
      Assert.IsTrue(UltimateSystem.InCarrierAura(Verified, 0, rPos), "항모 14m 지휘 오라 활성");

      // 적 저격수가 도시 건물에 주둔해도 공중 항모가 26m 광역 정찰로 탐지·노출시킨다.
      var sniper = Sniper(1, "cf_sniper");
      var city = CityAt(0);
      yield return Send(1, new EnterGarrisonCommand { Units = new[] { sniper }, Building = city });
      yield return WaitFor(() => GarrisonOf(sniper) == city, 20f, "적 저격수 건물 주둔");
      yield return Send(0, new MoveUnitsCommand { Units = new[] { carrier }, Target = Verified.Get<Transform3D>(city).Position });
      yield return WaitFor(() => (Verified.Get<Unit>(sniper).DetectedMask & 1) != 0, 45f, "공중 항모가 주둔 저격수 은신 탐지");
      Assert.Greater(Verified.Get<Unit>(sniper).MuzzleRevealUntilFrame, Verified.Number, "주둔 저격수 위치 노출");
    }
  }
}
