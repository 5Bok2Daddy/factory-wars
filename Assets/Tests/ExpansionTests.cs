namespace FactoryWars.Tests {
  using System.Collections;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using Assert = NUnit.Framework.Assert;
  using Photon.Deterministic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine.TestTools;

  /// <summary>사령부 추가 건설과 방어탑 2종.</summary>
  public partial class LocalMatchTests {
    static BuildingSpec SpecOf(int player, string suffix) => Verified.FindAsset(BuildingOf(player, suffix));

    /// <summary>테스트 맵에는 시작 유닛이 있다. 시나리오로 놓은 유닛만 고른다.</summary>
    static EntityRef ArenaUnit(int owner, string name) => UnitsNamed(owner, name).First(e => Verified.Get<Transform3D>(e).Position.Z > 30);

    [UnityTest]
    public IEnumerator Expansion_HeadquartersUsesProposedValues() {
      yield return StartMatch();
      var hq = SpecOf(0, "_headquarters");
      Assert.AreEqual(400, hq.Cost.Iron.AsInt);
      Assert.AreEqual(300, hq.Cost.Wood.AsInt);
      Assert.AreEqual(60, hq.BuildTimeSeconds.AsInt);
      Assert.AreEqual(3000, hq.MaxHealth.AsInt);
      Assert.IsTrue(FactionOf(0).Buildings.Contains(BuildingOf(0, "_headquarters")), "건설 목록에 있음");
      var state = PlayerOf(0);
      Assert.AreEqual(2, Verified.ComponentCount<Building>(), "시작 사령부는 비용 없이 완공");
      Assert.AreEqual(state.Stock.Iron.AsInt, Verified.FindAsset<MapSpec>(Verified.Map.UserAsset).StartingResources.Iron.AsInt);
    }

    [UnityTest]
    public IEnumerator Expansion_ChargesCostAndAddsDropOffAndPopulation() {
      yield return StartMatch(stock: Stock(1000, 1000, 0, 0));
      var cap = PlayerOf(0).PopulationCap.AsFloat;
      var position = V(-20, 10);
      yield return BuildAndFinish(0, "_headquarters", position);
      var hq = BuildingAt(position);
      Assert.IsTrue(Finished(hq));
      Assert.IsTrue(Verified.FindAsset(BuildingState(hq).Spec).IsDropOff, "반입 지점");
      yield return WaitFrames(CommandFrames);
      Assert.AreEqual(600, PlayerOf(0).Stock.Iron.AsInt, 60, "철 400");
      Assert.Greater(PlayerOf(0).PopulationCap.AsFloat, cap, "인구 증가");
      yield return Send(0, new ProduceCommand { Building = hq, Unit = FactionOf(0).Engineer, Count = 1 });
      Assert.AreEqual(1, BuildingState(hq).QueueCount, "공병 생산");
    }

    [UnityTest]
    public IEnumerator Expansion_HeadquartersRepairIsCharged() {
      yield return StartMatch(units: new[] { (1, "cf_dragonscale_tank", -38, 0), (1, "cf_dragonscale_tank", -38, 3), (1, "cf_dragonscale_tank", -38, -3) }, stock: Stock(3000, 3000, 0, 0));
      var hq = Headquarters(0);
      var engineer = Engineers(0)[0];
      yield return Send(0, new MoveUnitsCommand { Units = new[] { engineer }, Target = V(-45, 40) });
      var tanks = UnitsNamed(1, "cf_dragonscale_tank").ToArray();
      Game.SendCommand(1, new AttackTargetCommand { Units = tanks, Target = hq });
      yield return WaitFor(() => BuildingState(hq).Health.AsFloat < 2950f, 60f, "사령부 피해");
      yield return Send(1, new MoveUnitsCommand { Units = tanks, Target = V(30, 20) });
      yield return WaitFrames(3 * Fps);

      var damaged = BuildingState(hq).Health.AsFloat;
      var funds = ExactFunds(0);
      var wood = ExactWood(0);
      yield return Send(0, new RepairCommand { Units = new[] { engineer }, Target = hq });
      yield return WaitFor(() => BuildingState(hq).Health.AsFloat >= 3000f, 60f, "수리 완료");
      yield return WaitFrames(CommandFrames);
      var expected = (3000f - damaged) / 3000f * 0.5f;
      Assert.Greater(expected, 0f);
      Assert.AreEqual(expected * 300f, wood - ExactWood(0), 1f, "목재 수리비");
      Assert.Greater(funds - ExactFunds(0), expected * 400f * 0.5f, "철 수리비");
    }

    [UnityTest]
    public IEnumerator Expansion_AiRuleFollowsReservesCostAndLimit() {
      yield return StartMatch();
      var ai = Verified.FindAsset(Verified.FindAsset(Verified.RuntimeConfig.Rules).NormalAi);
      var rich = Stock(800, 500, 0, 0);
      var poor = Stock(799, 500, 0, 0);
      Assert.IsTrue(BotSystem.ExpandsWithHeadquarters(ai, rich, 5000, 20, 1), "큰 자원");
      Assert.IsFalse(BotSystem.ExpandsWithHeadquarters(ai, rich, 4999, 20, 1), "작은 자원은 집하장");
      Assert.IsFalse(BotSystem.ExpandsWithHeadquarters(ai, poor, 5000, 20, 1), "철 800 미만");
      Assert.IsFalse(BotSystem.ExpandsWithHeadquarters(ai, Stock(800, 499, 0, 0), 5000, 20, 1), "목재 500 미만");
      Assert.IsTrue(BotSystem.ExpandsWithHeadquarters(ai, rich, 100, 3, 1), "인구가 막혔을 때");
      Assert.IsFalse(BotSystem.ExpandsWithHeadquarters(ai, rich, 5000, 20, ai.MaxHeadquarters), "상한");
    }

    [UnityTest]
    public IEnumerator Ai_SeesEnemiesAcrossTheWholeMap() {
      yield return StartMatch(Map4PScene, 2, bots: new[] { BotDifficulty.Normal, BotDifficulty.Normal }, seed: 3, truce: 0);
      var far = new FPVector3(-300, 0, -300);
      Assert.IsTrue(BotSystem.FindEnemyTarget(Verified, 0, far, out _), "제곱 거리가 32768을 넘어도 찾음");
      var node = WorkerSystem.NearestNode(Verified, 0, far, FP.UseableMax, ResourceKind.Iron);
      Assert.AreNotEqual(EntityRef.None, node, "먼 철광산도 찾음");
    }

    [UnityTest]
    public IEnumerator Ai_ProcessorsStopWhileSavingIron() {
      yield return StartMatch();
      var ai = Verified.FindAsset(Verified.FindAsset(Verified.RuntimeConfig.Rules).NormalAi);
      FP breakCost = 500;
      Assert.IsFalse(BotSystem.ProcessorsOff(ai, Stock(400, 0, 100, 0), breakCost, false), "평소에는 켬");
      Assert.IsTrue(BotSystem.ProcessorsOff(ai, Stock(400, 0, 100, 0), breakCost, true), "파기 비용을 모으는 중");
      Assert.IsFalse(BotSystem.ProcessorsOff(ai, Stock(600, 0, 100, 0), breakCost, true), "다 모았으면 켬");
      Assert.IsTrue(BotSystem.ProcessorsOff(ai, Stock(200, 0, 1100, 0), breakCost, false), "강철이 넉넉하고(목표 1000 이상) 철이 적음");
      Assert.IsFalse(BotSystem.ProcessorsOff(ai, Stock(200, 0, 100, 0), breakCost, false), "강철이 모자라면 계속");
    }

    [UnityTest]
    public IEnumerator Ai_SweepsOnlyWhenEnemyArmyIsGone() {
      yield return StartMatch();
      var ai = Verified.FindAsset(Verified.FindAsset(Verified.RuntimeConfig.Rules).NormalAi);
      Assert.IsTrue(BotSystem.IsSweeping(10, 0, ai), "적 군대 없음");
      Assert.IsTrue(BotSystem.IsSweeping(40, 10, ai), "4배");
      Assert.IsFalse(BotSystem.IsSweeping(39, 10, ai));
      Assert.IsFalse(BotSystem.IsSweeping(0, 0, ai), "군대가 없으면 안 감");
    }

    [UnityTest]
    public IEnumerator Tower_GroundTowerIgnoresHawkAndShootsTanks() {
      yield return StartMatch(units: new[] { (1, "ff_hawk_gunship", 5, ArenaZ), (1, "cf_dragonscale_tank", 8, ArenaZ) },
        buildings: new[] { (0, "ff_ground_tower", 0, ArenaZ), (0, "ff_power_plant", -14, ArenaZ) }, stock: Stock(1000, 1000, 0, 500));
      Assert.IsTrue(SpecOf(0, "_ground_tower").GroundOnly);
      var hawk = UnitsNamed(1, "ff_hawk_gunship")[0];
      var tank = ArenaUnit(1, "cf_dragonscale_tank");
      var hawkHealth = Health(hawk);
      var tankHealth = Health(tank);
      yield return WaitFor(() => Health(tank) < tankHealth, 30f, "전차 피격");
      yield return WaitFrames(5 * Fps);
      Assert.AreEqual(hawkHealth, Health(hawk), "호크는 맞지 않음");
    }

    [UnityTest]
    public IEnumerator Tower_AirDefenseHitsAirAtLongRangeOnly() {
      yield return StartMatch(units: new[] { (1, "ff_hawk_gunship", 13, ArenaZ), (1, "cf_dragonscale_tank", 6, ArenaZ) },
        buildings: new[] { (0, "ff_air_defense", 0, ArenaZ), (0, "ff_power_plant", -14, ArenaZ) }, stock: Stock(1000, 1000, 0, 500));
      var spec = SpecOf(0, "_air_defense");
      Assert.AreEqual(30, spec.Damage.AsInt);
      Assert.AreEqual(14, spec.Range.AsInt);
      Assert.AreEqual(200, spec.Cost.Iron.AsInt);
      Assert.AreEqual(30, spec.Cost.Steel.AsInt);
      var hawk = UnitsNamed(1, "ff_hawk_gunship")[0];
      var tank = ArenaUnit(1, "cf_dragonscale_tank");
      var before = Health(hawk);
      var tankHealth = Health(tank);
      yield return WaitFor(() => Health(hawk) < before, 30f, "호크 피격");
      Assert.AreEqual(45f, before - Health(hawk), 0.5f, "공중 x1.5");
      yield return WaitFrames(3 * Fps);
      Assert.AreEqual(tankHealth, Health(tank), "지상은 맞지 않음");
    }

    [UnityTest]
    public IEnumerator Hud_IndustryMenuPlacesHeadquartersWithoutPaging() {
      yield return StartMatch();
      yield return WaitForHud();
      var hud = Controller().Hud;
      Controller().Input.Selection.SelectUnit(Engineers(0)[0]);
      hud.Refresh(Controller().Frame);
      var menu = hud.Entries.FindIndex(e => e.Label == "산업·보급");
      Assert.GreaterOrEqual(menu, 0);
      hud.PressContext(menu);
      var industryRoles = new[] { BuildingRole.Headquarters, BuildingRole.PowerPlant, BuildingRole.Processor,
        BuildingRole.SupplyDepot, BuildingRole.CollectionYard, BuildingRole.ResearchLab };
      var buildings = Verified.FindAsset(StateOf(0).Faction).Buildings.Select(reference => Verified.FindAsset(reference));
      var names = buildings.Where(spec => industryRoles.Contains(spec.Role)).Select(spec => Texts.NameOf(spec.name)).ToArray();
      CollectionAssert.AreEquivalent(names, hud.Entries.Where(e => e.Label.Length > 0 && e.Label != "뒤로")
        .Select(e => e.Label.Split('\n')[0]).ToArray());
      Assert.AreEqual(1, hud.ContextPages, "29 2-5: 단일 화면 한눈에 보기 패널로 쪽 넘김 없음");
      Assert.AreEqual(0, hud.ContextPage);

      var hqIndex = hud.Entries.FindIndex(e => e.Label.StartsWith(Texts.NameOf("ff_headquarters")));
      Assert.GreaterOrEqual(hqIndex, 0, "사령부 항목 존재");
      hud.PressContext(hqIndex);
      Assert.IsTrue(Controller().Input.IsPlacing, "사령부 칸이 맞는 건물을 고름");
      Assert.AreEqual(BuildingOf(0, "_headquarters"), Controller().Input.Placing);
    }

    [UnityTest]
    public IEnumerator Hud_ShortPanelsHaveNoPageButton() {
      yield return StartMatch();
      yield return WaitForHud();
      var hud = Controller().Hud;
      Controller().Input.Selection.SelectBuilding(Headquarters(0));
      hud.Refresh(Controller().Frame);
      Assert.AreEqual(1, hud.ContextPages);
    }
  }
}
