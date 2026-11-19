namespace FactoryWars.Tests {
  using System.Collections;
  using System.Linq;
  using NUnit.Framework;
  using Photon.Deterministic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  /// <summary>항목 31 테크: 기지 단계 잠금·업그레이드, 연구(크기별 공격·방어, 정예화), 대형 보유 상한, 야전 정비.</summary>
  public unsafe partial class LocalMatchTests {
    static int ResearchIndex(int player, string name) {
      var list = Verified.FindAsset(StateOf(player).Faction).Researches;
      for (var i = 0; i < list.Length; i++) {
        if (Verified.FindAsset(list[i]).name == name) {
          return i;
        }
      }
      Assert.Fail($"연구 없음: {name}");
      return -1;
    }

    static int TierFrame(int player, int index) => Verified.Global->Players.GetPointer(player)->TierFrames[index];

    static int AttackLevel(int player, UnitSize size) => Verified.Global->Players.GetPointer(player)->AttackLevels[(int)size];

    static FP Outgoing(EntityRef entity) {
      var unit = Verified.Unsafe.GetPointer<Unit>(entity);
      return TechSystem.OutgoingMultiplier(Verified, unit->Owner, unit, Verified.FindAsset(unit->Spec));
    }

    static bool IsEntrenchedUnit(EntityRef entity) {
      var unit = Verified.Unsafe.GetPointer<Unit>(entity);
      return TechSystem.IsEntrenched(Verified, entity, unit, Verified.FindAsset(unit->Spec));
    }

    static FP SpecialIncomingOf(EntityRef entity) {
      var unit = Verified.Unsafe.GetPointer<Unit>(entity);
      return TechSystem.SpecialIncomingMultiplier(Verified, entity, unit, Verified.FindAsset(unit->Spec));
    }

    static AssetRef<UnitSpec> UnitRef(int player, string name) => Verified.FindAsset(StateOf(player).Faction).Units.First(u => Verified.FindAsset(u).name == name);

    [UnityTest]
    public IEnumerator Tech_TierLocksBuildingsUnitsAndUpgradeUnlocks() {
      yield return StartMatch(startingTier: 1, buildings: new[] { (0, "ff_vehicle_factory", -42, -22), (0, "ff_power_plant", -42, 22) }, stock: Stock(5000, 5000, 5000, 200));
      Assert.AreEqual(1, StateOf(0).BaseTier, "1단계로 시작");
      Assert.AreEqual(1, TierFrame(0, 0), "1단계 도달 프레임 기록");

      // 2단계 건물(군수공장)은 지을 수 없다.
      yield return Build(0, "_vehicle_factory", V(-20, 10));
      AssertRejected(0, ActionKind.Build, RejectReason.TierLocked);
      // 군수공장이 있어도 중형(이글 전차)은 못 만들고 소형(정찰 버기)은 된다.
      var factory = OwnBuilding(0, "ff_vehicle_factory");
      yield return Send(0, new ProduceCommand { Building = factory, Unit = UnitRef(0, "ff_eagle_tank"), Count = 1 });
      AssertRejected(0, ActionKind.Produce, RejectReason.TierLocked);
      yield return Send(0, new ProduceCommand { Building = factory, Unit = UnitRef(0, "ff_scout_buggy"), Count = 1 });
      Assert.AreEqual(1, Verified.Get<Building>(factory).QueueCount, "소형은 1단계에서 생산");

      // 연구소 없이 업그레이드하면 잠김.
      _rejected.Clear();
      yield return Send(0, new UpgradeBaseCommand { Building = Headquarters(0) });
      AssertRejected(0, ActionKind.Upgrade, RejectReason.Locked);

      // 연구소를 짓고 업그레이드. 비용을 내고, 올리는 동안에도 사령부는 공병을 만든다.
      yield return BuildAndFinish(0, "_research_lab", V(-20, 10));
      var before = StateOf(0).Stock;
      yield return Send(0, new UpgradeBaseCommand { Building = Headquarters(0) });
      var hq = Verified.Get<Building>(Headquarters(0));
      Assert.AreEqual(2, hq.UpgradeTier, "업그레이드 시작");
      Assert.Less(StateOf(0).Stock.Iron.AsFloat, before.Iron.AsFloat, "비용 지불");
      yield return Send(0, new ProduceCommand { Building = Headquarters(0), Unit = Verified.FindAsset(StateOf(0).Faction).Engineer, Count = 1 });
      Assert.AreEqual(1, Verified.Get<Building>(Headquarters(0)).QueueCount, "업그레이드 중에도 생산");
      _rejected.Clear();
      yield return Send(0, new UpgradeBaseCommand { Building = Headquarters(0) });
      AssertRejected(0, ActionKind.Upgrade, RejectReason.Busy);

      var rules = Verified.FindAsset(Verified.RuntimeConfig.Rules);
      var seconds = rules.BaseTiers.First(t => t.Tier == 2).Seconds.AsInt;
      yield return WaitFor(() => StateOf(0).BaseTier == 2, 10f + seconds * 0.2f, "2단계 도달");
      Assert.Greater(TierFrame(0, 1), 0, "2단계 도달 프레임");
      _rejected.Clear();
      yield return Send(0, new ProduceCommand { Building = factory, Unit = UnitRef(0, "ff_eagle_tank"), Count = 1 });
      Assert.IsFalse(_rejected.Any(e => e.Kind == ActionKind.Produce && e.Reason == RejectReason.TierLocked), "2단계면 중형 생산");
      // 3단계는 중공업 기지가 필요하다.
      yield return Send(0, new UpgradeBaseCommand { Building = Headquarters(0) });
      AssertRejected(0, ActionKind.Upgrade, RejectReason.Locked);
    }

    [UnityTest]
    public IEnumerator Tech_ResearchLevelsAndEliteApplyToExistingUnits() {
      yield return StartMatch(buildings: new[] { (0, "ff_research_lab", -42, -22), (0, "ff_power_plant", -42, 22), (0, "ff_power_plant", -18, -22) }, stock: Stock(5000, 5000, 5000, 200));
      var lab = OwnBuilding(0, "ff_research_lab");
      // 2단계 연구는 1단계 연구가 먼저다.
      yield return Send(0, new ResearchCommand { Building = lab, Index = ResearchIndex(0, "research_attack_small_2") });
      AssertRejected(0, ActionKind.Research, RejectReason.Locked);

      var ranger = UnitsNamed(0, "ff_ranger_squad")[0];
      var spec = Verified.FindAsset(Verified.Get<Unit>(ranger).Spec);
      var healthBefore = Verified.Get<Unit>(ranger).Health;
      var elite = ResearchIndex(0, "research_elite_ff");
      yield return Send(0, new ResearchCommand { Building = lab, Index = elite });
      Assert.Greater(Verified.Get<Building>(lab).Research, 0, "연구 시작");
      _rejected.Clear();
      yield return Send(0, new ResearchCommand { Building = lab, Index = ResearchIndex(0, "research_attack_small_1") });
      AssertRejected(0, ActionKind.Research, RejectReason.Busy);
      yield return WaitFor(() => (StateOf(0).ResearchDone & (1 << elite)) != 0, 30f, "정예화 완료");
      var eliteSpec = Verified.FindAsset(Verified.FindAsset(StateOf(0).Faction).Researches[elite]);
      Assert.AreEqual((healthBefore * eliteSpec.EliteHealthMultiplier).AsFloat, Verified.Get<Unit>(ranger).Health.AsFloat, 0.01f, "이미 있는 유닛도 체력 증가");
      Assert.AreEqual((spec.MaxHealth * eliteSpec.EliteHealthMultiplier).AsFloat, TechSystem.MaxHealth(Verified, 0, Verified.Get<Unit>(ranger).Spec).AsFloat, 0.01f);
      Assert.AreEqual(eliteSpec.EliteRangeBonus.AsFloat, TechSystem.RangeBonus(Verified, 0, Verified.Get<Unit>(ranger).Spec).AsFloat, 0.01f, "사거리 보너스");

      var attack = ResearchIndex(0, "research_attack_small_1");
      yield return Send(0, new ResearchCommand { Building = lab, Index = attack });
      yield return WaitFor(() => (StateOf(0).ResearchDone & (1 << attack)) != 0, 30f, "소형 공격 1단계 완료");
      Assert.AreEqual(1, AttackLevel(0, UnitSize.Small));
      Assert.AreEqual(0, AttackLevel(0, UnitSize.Medium), "다른 크기는 그대로");
      var rules = Verified.FindAsset(Verified.RuntimeConfig.Rules);
      Assert.IsTrue(TechSystem.FindSizeRule(rules, UnitSize.Small, out var rule));
      Assert.AreEqual(((FP._1 + rule.AttackPerLevel) * eliteSpec.EliteDamageMultiplier).AsFloat, Outgoing(ranger).AsFloat, 0.01f, "공격 연구 + 정예 피해");
    }

    [UnityTest]
    public IEnumerator Tech_LargeUnitOwnedLimit() {
      var tanks = Enumerable.Range(0, 4).Select(i => (0, "ff_bulwark_tank", -40 + i * 5, 30)).ToArray();
      yield return StartMatch(units: tanks, buildings: new[] { (0, "ff_heavy_works", -42, -22), (0, "ff_supply_depot", -18, -22), (0, "ff_power_plant", -42, 22) }, stock: Stock(5000, 5000, 5000, 400));
      var rules = Verified.FindAsset(Verified.RuntimeConfig.Rules);
      Assert.IsTrue(TechSystem.FindSizeRule(rules, UnitSize.Large, out var rule));
      Assert.AreEqual(4, rule.OwnedLimit, "대형 동시 보유 4");
      yield return Send(0, new ProduceCommand { Building = OwnBuilding(0, "ff_heavy_works"), Unit = UnitRef(0, "ff_bulwark_tank"), Count = 1 });
      AssertRejected(0, ActionKind.Produce, RejectReason.SizeLimit);
      Assert.AreEqual(0, Verified.Get<Building>(OwnBuilding(0, "ff_heavy_works")).QueueCount);
    }

    [UnityTest]
    public IEnumerator Tech_FieldSupportRepairsDamagedVehicles() {
      // 정비차는 멀리 두고, 전차가 민병대에게 맞은 뒤 정비차를 옆으로 보낸다.
      yield return StartMatch(units: new[] { (0, "ff_field_support", -20, 40), (0, "ff_eagle_tank", 6, 30), (1, "cf_militia", 11, 30), (1, "cf_militia", 11, 32) });
      var tank = UnitsNamed(0, "ff_eagle_tank").OrderBy(u => (Verified.Get<Transform3D>(u).Position - V(6, 30)).SqrMagnitude).First();
      var support = UnitsNamed(0, "ff_field_support")[0];
      var max = Verified.FindAsset(Verified.Get<Unit>(tank).Spec).MaxHealth;
      yield return WaitFor(() => Verified.Get<Unit>(tank).Health < max, 30f, "전차가 맞음");
      yield return WaitFor(() => UnitsNamed(1, "cf_militia").All(u => (Verified.Get<Transform3D>(u).Position - V(11, 30)).Magnitude > 20), 60f, "민병대 정리");
      var hurt = Verified.Get<Unit>(tank).Health;
      Assert.Less(hurt.AsFloat, max.AsFloat, "정비 전에는 깎인 채");
      yield return Send(0, new MoveUnitsCommand { Units = new[] { support }, Target = V(3, 30) });
      yield return WaitFor(() => Verified.Get<Unit>(tank).Health > hurt, 60f, "야전 정비차가 수리");
    }

    [Test]
    public void Tech_DataFollowsSizeRules() {
      var rules = Resources.Load<GameRulesSpec>("FactoryWars/game_rules");
      foreach (var faction in Resources.LoadAll<FactionSpec>("FactoryWars")) {
        var units = faction.Units.Select(u => Resources.LoadAll<UnitSpec>("FactoryWars").First(s => s.Guid == u.Id)).Where(u => u.GarrisonFireOnly == false && u.SortieKind == SortieKind.None).ToArray();
        // 항목 34 + 신규 공격 유닛 4종: 주둔 저격수·작전기는 크기별 구성 수에 넣지 않는다. 진영마다 지상·공중 소형 5, 중형 5, 대형 2(해군 제외).
        Assert.AreEqual(5, units.Count(u => u.Size == UnitSize.Small && u.IsNaval == false), $"{faction.name} 소형");
        Assert.AreEqual(5, units.Count(u => u.Size == UnitSize.Medium && u.IsNaval == false), $"{faction.name} 중형");
        Assert.AreEqual(2, units.Count(u => u.Size == UnitSize.Large && u.IsNaval == false), $"{faction.name} 대형");
        foreach (var unit in units.Where(u => u.Size != UnitSize.Engineer && u.IsNaval == false)) {
          Assert.IsTrue(TechSystem.FindSizeRule(rules, unit.Size, out var rule), unit.name);
          var population = unit.Population * unit.SpawnCount;
          Assert.GreaterOrEqual(population.AsFloat, rule.MinPopulation.AsFloat * (unit.SpawnCount > 1 ? unit.SpawnCount : 1) - 0.01f, $"{unit.name} 인구 하한");
          Assert.LessOrEqual(unit.Population.AsFloat, rule.MaxPopulation.AsFloat + 0.01f, $"{unit.name} 인구 상한");
          Assert.AreEqual(unit.Size == UnitSize.Large ? 3 : unit.Size == UnitSize.Medium ? 2 : 1, unit.RequiredTier, $"{unit.name} 단계");
        }
        // B1·B2: 모든 생산 건물이 무언가를 만든다.
        foreach (var building in faction.Buildings.Select(b => Resources.LoadAll<BuildingSpec>("FactoryWars").First(s => s.Guid == b.Id))) {
          if (building.Role == BuildingRole.Airfield || building.Role == BuildingRole.HeavyWorks || building.Role == BuildingRole.Barracks || building.Role == BuildingRole.VehicleFactory) {
            Assert.Greater(building.Producible.Length, 0, $"{building.name} 생산 유닛 있음");
          }
        }
        Assert.GreaterOrEqual(faction.Researches.Length, 15, $"{faction.name} 연구 12 + 정예화 1 + 궁극 1 + 특화 2");
      }
    }

    [UnityTest]
    public IEnumerator Tech_MkUpgradesAndFactionSpecialsAndPassives() {
      // 사령부(HQ)에서 직접 연구 시작(모바일 UX) + Mk.1->Mk.2->Mk.3 단계 진화 + 자유/대륙 고유 패시브 및 진영 특화 연구 검증.
      yield return StartMatch(
        units: new[] {
          (0, "ff_ranger_squad", -24, 0),   // 자유 연방 사령부(-30,0) 반경 25m 안(전술 네트워크 활성)
          (0, "ff_ranger_squad", -4, 34),   // 사령부·레이더 밖(전술 네트워크 비활성, 적 전차와 교전 거리 밖)
          (0, "ff_hawk_gunship", -22, 6),
          (1, "cf_dragonscale_tank", 32, 20),
        },
        buildings: new[] {
          (0, "ff_research_lab", -42, -22),
          (0, "ff_power_plant", -42, 22),
          (1, "cf_research_lab", 42, -22),
          (1, "cf_power_plant", 42, 22),
        },
        stock: Stock(12000, 12000, 12000, 600));

      var rangers = UnitsNamed(0, "ff_ranger_squad");
      var nearHq = rangers.OrderBy(e => (Verified.Get<Transform3D>(e).Position - V(-24, 0)).SqrMagnitude.AsFloat).First();
      var farHq = rangers.OrderBy(e => (Verified.Get<Transform3D>(e).Position - V(-4, 34)).SqrMagnitude.AsFloat).First();
      var rangerSpec = Verified.FindAsset(Verified.Get<Unit>(nearHq).Spec);
      var hawk = UnitsNamed(0, "ff_hawk_gunship")[0];
      var hawkSpec = Verified.FindAsset(Verified.Get<Unit>(hawk).Spec);
      var cfTank = UnitsNamed(1, "cf_dragonscale_tank").OrderBy(e => (Verified.Get<Transform3D>(e).Position - V(32, 20)).SqrMagnitude.AsFloat).First();
      var cfTankSpec = Verified.FindAsset(Verified.Get<Unit>(cfTank).Spec);

      // 1) 초기 Mk.1 확인.
      Assert.AreEqual(1, TechSystem.UnitMark(Verified, 0, rangerSpec), "초기 Mk.1");

      // 2) 자유 연방 패시브(전술 네트워크): HQ 25m 안 연사 +10% · 이동 +8%, 밖은 기본값.
      Assert.IsTrue(TechSystem.InTacticalNetwork(Verified, 0, Verified.Get<Transform3D>(nearHq).Position), "HQ 반경 내 전술 네트워크 활성");
      Assert.IsFalse(TechSystem.InTacticalNetwork(Verified, 0, Verified.Get<Transform3D>(farHq).Position), "HQ·레이더 밖 비활성");
      Assert.AreEqual(10f / 11f, TechSystem.AttackCooldownMultiplier(Verified, 0, rangerSpec, Verified.Get<Transform3D>(nearHq).Position).AsFloat, 0.01f, "전술 네트워크 연사 +10%");
      Assert.AreEqual(1.08f, TechSystem.MoveSpeedMultiplier(Verified, 0, rangerSpec, Verified.Get<Transform3D>(nearHq).Position).AsFloat, 0.01f, "전술 네트워크 이동 +8%");

      // 3) 대륙 연방 패시브(참호 교리): 정지(Hold) 명령 시 피격 피해 -15%.
      yield return Send(1, new HoldUnitsCommand { Units = new[] { cfTank } });
      Assert.IsTrue(IsEntrenchedUnit(cfTank), "대륙 연방 Hold 참호 교리 활성");
      Assert.AreEqual(0.85f, SpecialIncomingOf(cfTank).AsFloat, 0.01f, "참호 교리 피격 -15%");

      // 4) HQ에서 직접 소형 공격 Mk.2 -> Mk.3 연구 수행 + 진영 특화 연구(고속 볼트 / 스텔스 도료 / 반응장갑 ERA / 고폭 철갑탄).
      var hq0 = Headquarters(0);
      var lab0 = OwnBuilding(0, "ff_research_lab");
      var hq1 = Headquarters(1);
      var lab1 = OwnBuilding(1, "cf_research_lab");
      var atk1 = ResearchIndex(0, "research_attack_small_1");
      var atk2 = ResearchIndex(0, "research_attack_small_2");
      var bolts = ResearchIndex(0, "research_ff_rapid_bolts");
      var stealth = ResearchIndex(0, "research_ff_stealth_coating");
      var era = ResearchIndex(1, "research_cf_era");
      var heap = ResearchIndex(1, "research_cf_heap");

      yield return Send(0, new ResearchCommand { Building = hq0, Index = atk1 });
      yield return Send(0, new ResearchCommand { Building = lab0, Index = bolts });
      yield return Send(1, new ResearchCommand { Building = hq1, Index = era });
      yield return Send(1, new ResearchCommand { Building = lab1, Index = heap });
      var cfHpBefore = Verified.Get<Unit>(cfTank).Health.AsFloat;

      yield return WaitFor(() => TechSystem.IsResearched(Verified, 0, atk1) && TechSystem.IsResearched(Verified, 0, bolts)
        && TechSystem.IsResearched(Verified, 1, era) && TechSystem.IsResearched(Verified, 1, heap), 60f, "1차 연구 4종 완료");

      Assert.AreEqual(2, TechSystem.UnitMark(Verified, 0, rangerSpec), "공격 1단계 완료 -> Mk.2");
      Assert.AreEqual(5f / 6f, TechSystem.AttackCooldownMultiplier(Verified, 0, rangerSpec, Verified.Get<Transform3D>(farHq).Position).AsFloat, 0.01f, "고속 볼트 보병 연사 +20%");
      Assert.AreEqual(cfHpBefore * 1.25f, Verified.Get<Unit>(cfTank).Health.AsFloat, 1f, "반응장갑 ERA 중형 전차 HP +25%");
      Assert.AreEqual(1.25f, TechSystem.SplashRadiusMultiplier(Verified, 1, cfTankSpec).AsFloat, 0.01f, "반응장갑 ERA 폭발 반경 +25%");
      Assert.AreEqual(1.30f, TechSystem.StructureDamageMultiplier(Verified, 1).AsFloat, 0.01f, "고폭 철갑탄 건물 피해 +30%");

      yield return Send(0, new ResearchCommand { Building = hq0, Index = atk2 });
      yield return Send(0, new ResearchCommand { Building = lab0, Index = stealth });
      yield return WaitFor(() => TechSystem.IsResearched(Verified, 0, atk2) && TechSystem.IsResearched(Verified, 0, stealth), 75f, "Mk.3 및 스텔스 도료 완료");

      Assert.AreEqual(3, TechSystem.UnitMark(Verified, 0, rangerSpec), "공격 2단계 완료 -> Mk.3");
      Assert.AreEqual(0.75f, TechSystem.SortieRearmMultiplier(Verified, 0).AsFloat, 0.01f, "스텔스 도료 재정비 -25%");
      Assert.AreEqual(0.85f, SpecialIncomingOf(hawk).AsFloat, 0.01f, "스텔스 도료 공중 피격 -15%");
    }

    [UnityTest]
    public IEnumerator Tech_ThirdFactionBattlefieldSalvageAndSpecials() {
      yield return StartMatch(
        factionNames: new[] { "faction_test_assault", null },
        units: new[] { (0, "ff_eagle_tank", -6, 26), (0, "ff_eagle_tank", -3, 26), (1, "cf_militia", 14, 26) },
        buildings: new[] { (0, "ff_research_lab", -42, -22), (0, "ff_power_plant", -42, 22) },
        stock: Stock(5000, 5000, 5000, 400));

      var ironBefore = StateOf(0).Stock.Iron.AsFloat;
      var steelBefore = StateOf(0).Stock.Steel.AsFloat;
      var killedBefore = StateOf(0).UnitsKilled;
      var attackers = UnitsNamed(0, "ff_eagle_tank").ToArray();
      yield return Send(0, new MoveUnitsCommand { Units = attackers, Target = V(14, 26), AttackMove = true });
      yield return WaitFor(() => StateOf(0).UnitsKilled > killedBefore, 35f, "제3진영 적 유닛 격파");
      var kills = StateOf(0).UnitsKilled - killedBefore;
      Assert.GreaterOrEqual(StateOf(0).Stock.Iron.AsFloat, ironBefore + 15f * kills - 0.1f, "전장 노획 철 +15/킬");
      Assert.GreaterOrEqual(StateOf(0).Stock.Steel.AsFloat, steelBefore + 5f * kills - 0.1f, "전장 노획 강철 +5/킬");

      var rapid = ResearchIndex(0, "research_tf_rapid_deploy");
      var smoke = ResearchIndex(0, "research_tf_tactical_smoke");
      yield return Send(0, new ResearchCommand { Building = Headquarters(0), Index = rapid });
      yield return Send(0, new ResearchCommand { Building = OwnBuilding(0, "ff_research_lab"), Index = smoke });
      yield return WaitFor(() => TechSystem.IsResearched(Verified, 0, rapid) && TechSystem.IsResearched(Verified, 0, smoke), 60f, "제3진영 특화 연구 완료");
      var tank = UnitsNamed(0, "ff_eagle_tank")[0];
      var tankSpec = Verified.FindAsset(Verified.Get<Unit>(tank).Spec);
      Assert.AreEqual(1.25f, TechSystem.MoveSpeedMultiplier(Verified, 0, tankSpec, Verified.Get<Transform3D>(tank).Position).AsFloat, 0.01f, "신속 전개 이동 +25%");
      Assert.AreEqual(0.85f, SpecialIncomingOf(tank).AsFloat, 0.01f, "전술 연막 피격 -15%");
    }
  }
}
