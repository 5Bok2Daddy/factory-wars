namespace FactoryWars.Tests {
  using System.Collections;
  using System.Collections.Generic;
  using System.Linq;
  using NUnit.Framework;
  using Assert = NUnit.Framework.Assert;
  using Photon.Deterministic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.TestTools;

  /// <summary>공병 작업, 건설 위치 검사, 공격 상성, 유닛 특수 기능.</summary>
  public unsafe partial class LocalMatchTests {
    const float Tolerance = 0.05f;
    // 테스트 씬의 시작 유닛이 닿지 않는 곳.
    const int ArenaZ = 40;

    static FPVector3 V(int x, int z) => new FPVector3(x, 0, z);

    static AssetRef<BuildingSpec> BuildingOf(int player, string suffix) {
      return FactionOf(player).Buildings.First(b => Verified.FindAsset(b).name.EndsWith(suffix));
    }

    static List<EntityRef> Engineers(int player) {
      var result = new List<EntityRef>();
      foreach (var pair in Verified.GetComponentIterator<Worker>()) {
        if (Verified.Get<Unit>(pair.Entity).Owner == player) {
          result.Add(pair.Entity);
        }
      }
      return result;
    }

    static List<EntityRef> UnitsNamed(int owner, string name) {
      var result = new List<EntityRef>();
      foreach (var pair in Verified.GetComponentIterator<Unit>()) {
        if (pair.Component.Owner == owner && Verified.FindAsset(pair.Component.Spec).name == name) {
          result.Add(pair.Entity);
        }
      }
      return result;
    }

    static float Health(EntityRef unit) => Verified.TryGet<Unit>(unit, out var value) ? value.Health.AsFloat : 0f;

    static Worker WorkerOf(EntityRef entity) => Verified.Get<Worker>(entity);

    static PlayerState PlayerOf(int player) => *Verified.Global->Players.GetPointer(player);

    static float Power(int player) => PlayerOf(player).Stock.Power.AsFloat;

    static ResourceCost Stock(int iron, int wood, int steel, int power) {
      return new ResourceCost { Iron = iron, Wood = wood, Steel = steel, Power = power };
    }

    static float ExactWood(int player) => Verified.Global->Players.GetPointer(player)->Stock.Wood.AsFloat;

    static int Wood(int player) => Verified.Global->Players.GetPointer(player)->Stock.Wood.AsInt;

    static float ExactFunds(int player) => Verified.Global->Players.GetPointer(player)->Stock.Iron.AsFloat;

    static EntityRef NodeAt(FPVector3 position) {
      foreach (var pair in Verified.GetComponentIterator<ResourceNode>()) {
        if (Verified.Get<Transform3D>(pair.Entity).Position == position) {
          return pair.Entity;
        }
      }
      return EntityRef.None;
    }

    static float NodeRemaining(EntityRef node) => Verified.TryGet<ResourceNode>(node, out var value) ? value.Remaining.AsFloat : 0f;

    static EntityRef BuildingAt(FPVector3 position) {
      foreach (var pair in Verified.GetComponentIterator<Building>()) {
        if (Verified.Get<Transform3D>(pair.Entity).Position == position) {
          return pair.Entity;
        }
      }
      return EntityRef.None;
    }

    static Building BuildingState(EntityRef entity) => Verified.Get<Building>(entity);

    static bool Finished(EntityRef building) => Verified.TryGet<Building>(building, out var value) && value.ConstructionRemaining <= FP._0;

    IEnumerator Build(int player, string suffix, FPVector3 position, EntityRef engineer = default) {
      if (engineer == default) {
        engineer = Engineers(player)[0];
      }
      yield return Send(player, new BuildCommand { Engineer = engineer, Building = BuildingOf(player, suffix), Position = position });
    }

    IEnumerator BuildAndFinish(int player, string suffix, FPVector3 position) {
      yield return Build(player, suffix, position);
      var site = BuildingAt(position);
      Assert.AreNotEqual(EntityRef.None, site, "부지 생성");
      yield return WaitFor(() => Finished(site), 60f, "건설 완료");
    }

    static readonly (int, string, int, int)[] Plant = { (0, "ff_power_plant", -42, 0) };

    // 채집

    [UnityTest]
    public IEnumerator Gather_CarriesIronToHeadquarters() {
      yield return StartMatch();
      var engineer = Engineers(0)[0];
      var node = NodeAt(V(-30, 14));
      yield return Send(0, new GatherCommand { Units = new[] { engineer }, Node = node });
      yield return WaitFor(() => Funds(0) > 5000, 60f, "철 반입");
      Assert.AreEqual(5050, Funds(0), "한 번에 50");

      yield return WaitFor(() => Funds(0) > 5050, 60f, "반복 채집");
      var carried = WorkerOf(engineer).Carried.AsFloat;
      Assert.AreEqual(5000f, Funds(0) - 5000 + carried + NodeRemaining(node), Tolerance, "철은 노드에서 옮겨질 뿐이다");
      Assert.AreEqual(10000, Wood(0), "목재는 그대로");
    }

    /// <summary>작업 지점에서 일을 시작해 자원을 들 때까지 걸린 초.</summary>
    IEnumerator MeasureGather(EntityRef engineer, EntityRef node, System.Action<float> result) {
      yield return Send(0, new GatherCommand { Units = new[] { engineer }, Node = node });
      yield return WaitFor(() => WorkerOf(engineer).WorkTimer > FP._0, 60f, "채집 시작");
      var start = Verified.Number;
      yield return WaitFor(() => WorkerOf(engineer).Carried > FP._0, 60f, "채집 완료");
      result((Verified.Number - start) / (float)Fps);
    }

    [UnityTest]
    public IEnumerator Gather_IronTwoSecondsWoodThreeSeconds() {
      yield return StartMatch(units: new[] { (0, "ff_engineer", -30, 20) });
      var engineers = Engineers(0);
      var iron = 0f;
      var wood = 0f;
      yield return MeasureGather(engineers[0], NodeAt(V(-30, 14)), s => iron = s);
      yield return MeasureGather(engineers[1], NodeAt(V(-38, 30)), s => wood = s);
      // 검증 프레임을 폴링하므로 몇 프레임 늦게 볼 수 있다.
      Assert.AreEqual(2f, iron, 0.4f, "철 2초");
      Assert.AreEqual(3f, wood, 0.4f, "목재 3초");
      Assert.AreEqual(ResourceKind.Wood, WorkerOf(engineers[1]).CarriedKind);
      yield return WaitFor(() => Wood(0) > 10000, 60f, "목재 반입");
      Assert.AreEqual(10050, Wood(0));
    }

    [UnityTest]
    public IEnumerator Gather_SwitchingResourceDeliversFirst() {
      yield return StartMatch();
      var engineer = Engineers(0)[0];
      yield return Send(0, new GatherCommand { Units = new[] { engineer }, Node = NodeAt(V(-30, 14)) });
      yield return WaitFor(() => WorkerOf(engineer).Carried > FP._0, 60f, "철 채집");

      var forest = NodeAt(V(-38, 30));
      yield return Send(0, new GatherCommand { Units = new[] { engineer }, Node = forest });
      Assert.AreEqual(WorkerTask.Deliver, WorkerOf(engineer).Task, "철을 들고는 숲에서 일하지 않는다");
      Assert.AreEqual(forest, WorkerOf(engineer).Target, "반입 뒤 갈 곳은 숲");
      yield return WaitFor(() => Funds(0) > 5000, 60f, "철 반입");
      Assert.AreEqual(5050, Funds(0));
      yield return WaitFor(() => Wood(0) > 10000, 60f, "이어서 목재 채집");
      Assert.AreEqual(5050, Funds(0), "철은 더 늘지 않음");
    }

    [UnityTest]
    public IEnumerator Gather_EngineersSpreadOverSpots() {
      var extra = Enumerable.Range(0, 6).Select(i => (0, "ff_engineer", -36 + i * 2, 8)).ToArray();
      yield return StartMatch(units: extra);
      var engineers = Engineers(0);
      Assert.AreEqual(7, engineers.Count);
      var mine = NodeAt(V(-30, 14));
      var other = NodeAt(V(-30, -14));
      yield return Send(0, new GatherCommand { Units = engineers.ToArray(), Node = mine });

      var atMine = engineers.Where(e => WorkerOf(e).Target == mine).ToList();
      Assert.AreEqual(6, atMine.Count, "철광산 지점 6개");
      Assert.AreEqual(6, atMine.Select(e => WorkerOf(e).Spot).Distinct().Count(), "지점이 겹치지 않음");
      Assert.AreEqual(1, engineers.Count(e => WorkerOf(e).Target == other), "지점이 차면 가까운 다른 노드로");
    }

    [UnityTest]
    public IEnumerator Gather_CollectionYardAcceptsDelivery() {
      yield return StartMatch();
      var position = V(-20, 30);
      yield return BuildAndFinish(0, "_collection_yard", position);
      Assert.AreEqual(9850, Wood(0), "집하장 목재 150");
      var engineer = Engineers(0)[0];
      yield return Send(0, new GatherCommand { Units = new[] { engineer }, Node = NodeAt(V(-38, 30)) });
      yield return WaitFor(() => Wood(0) > 9850, 60f, "목재 반입");
      var distance = (Verified.Get<Transform3D>(engineer).Position - position).Magnitude;
      Assert.Less(distance.AsFloat, 8f, "사령부보다 가까운 집하장에 반입");
    }

    [UnityTest]
    public IEnumerator Gather_WithoutDropOff_WaitsCarrying() {
      var tanks = Enumerable.Range(0, 10).Select(i => (1, "cf_dragonscale_tank", -44 + i * 3, -24)).ToArray();
      yield return StartMatch(units: tanks, buildings: new[] { (0, "ff_barracks", -42, -40) });
      var engineer = Engineers(0)[0];
      var node = NodeAt(V(-38, 30));
      yield return Send(0, new GatherCommand { Units = new[] { engineer }, Node = node });

      var headquarters = Headquarters(0);
      var attackers = UnitsNamed(1, "cf_dragonscale_tank").Where(e => Verified.Get<Transform3D>(e).Position.Z < -20).ToArray();
      Game.SendCommand(1, new AttackTargetCommand { Units = attackers, Target = headquarters });
      yield return WaitFor(() => Verified.Exists(headquarters) == false, 90f, "사령부 파괴");
      Assert.IsFalse(Defeated(0), "병영이 남아 있음");

      yield return WaitFor(() => WorkerOf(engineer).Carried > FP._0, 60f, "채집");
      var wood = Wood(0);
      yield return WaitFrames(8 * Fps);
      Assert.AreEqual(WorkerTask.Deliver, WorkerOf(engineer).Task, "든 채 대기");
      Assert.AreEqual(50f, WorkerOf(engineer).Carried.AsFloat, Tolerance);
      Assert.AreEqual(wood, Wood(0));
    }

    [UnityTest]
    public IEnumerator Gather_DepletedNodeStaysAndWorkerMovesOn() {
      yield return StartMatch();
      var engineer = Engineers(0)[0];
      var small = NodeAt(V(-30, -14));
      var large = NodeAt(V(-30, 14));
      var count = Verified.ComponentCount<ResourceNode>();
      yield return Send(0, new GatherCommand { Units = new[] { engineer }, Node = small });

      yield return WaitFor(() => NodeRemaining(small) <= 0f, 60f, "노드 고갈");
      Assert.IsTrue(Verified.Exists(small), "고갈돼도 남는다");
      Assert.AreEqual(count, Verified.ComponentCount<ResourceNode>());
      yield return WaitFor(() => Funds(0) >= 5100, 60f, "마지막 철 반입");
      Assert.AreEqual(5100, Funds(0), "매장량 100");
      yield return WaitFor(() => NodeRemaining(large) < 5000, 60f, "가까운 노드로 이동");

      _rejected.Clear();
      yield return Build(0, "_power_plant", V(-30, -14), Engineers(0)[0]);
      AssertRejected(0, ActionKind.Build, RejectReason.BlockedLocation);
    }

    [UnityTest]
    public IEnumerator Gather_MoveOrderCancelsWork() {
      yield return StartMatch();
      var engineer = Engineers(0)[0];
      yield return Send(0, new GatherCommand { Units = new[] { engineer }, Node = NodeAt(V(-30, 14)) });
      Assert.AreEqual(WorkerTask.Gather, WorkerOf(engineer).Task);
      yield return Send(0, new MoveUnitsCommand { Units = new[] { engineer }, Target = V(-20, 0) });
      Assert.AreEqual(WorkerTask.Idle, WorkerOf(engineer).Task);
    }

    // 건설

    [UnityTest]
    public IEnumerator Build_ProgressesOnlyWhileEngineerWorks() {
      yield return StartMatch();
      var engineer = Engineers(0)[0];
      var position = V(-20, 25);
      yield return Build(0, "_power_plant", position, engineer);
      Assert.AreEqual(4850, Funds(0), "부지를 잡을 때 철 150 차감");
      var site = BuildingAt(position);
      Assert.AreEqual(15f, BuildingState(site).ConstructionRemaining.AsFloat, Tolerance, "도착 전에는 진행 안 됨");
      Assert.AreEqual(60f, BuildingState(site).Health.AsFloat, Tolerance, "부지 체력 10%");

      yield return WaitFor(() => BuildingState(site).ConstructionRemaining < 10, 60f, "건설 시작");
      yield return Send(0, new MoveUnitsCommand { Units = new[] { engineer }, Target = V(-20, 0) });
      var paused = BuildingState(site).ConstructionRemaining;
      yield return WaitFrames(2 * Fps);
      Assert.AreEqual(paused, BuildingState(site).ConstructionRemaining, "공병이 떠나면 멈춤");
      Assert.AreEqual(200f, PlayerOf(0).PowerCap.AsFloat, Tolerance, "미완성 건물은 저장 한도를 늘리지 않음");
      Assert.AreEqual(0f, PlayerOf(0).PowerRate.AsFloat, Tolerance, "미완성 건물은 전기를 만들지 않음");

      yield return Send(0, new RepairCommand { Units = new[] { engineer }, Target = site });
      yield return WaitFor(() => Finished(site), 60f, "건설 재개");
      yield return WaitFrames(CommandFrames);
      Assert.AreEqual(WorkerTask.Idle, WorkerOf(engineer).Task);
      Assert.AreEqual(600f, BuildingState(site).Health.AsFloat, 1f, "완공 체력");
      Assert.AreEqual(500f, PlayerOf(0).PowerCap.AsFloat, Tolerance, "발전소 저장 +300");
      Assert.AreEqual(5f, PlayerOf(0).PowerRate.AsFloat, Tolerance, "발전소 전기 +5/초");
    }

    [UnityTest]
    public IEnumerator Build_TwoEngineersBuildFaster() {
      yield return StartMatch(units: new[] { (0, "ff_engineer", -14, 10) });
      var engineers = Engineers(0);
      Assert.AreEqual(2, engineers.Count);
      var position = V(-20, 10);
      yield return Build(0, "_power_plant", position, engineers[0]);
      var site = BuildingAt(position);
      yield return Send(0, new RepairCommand { Units = new[] { engineers[1] }, Target = site });

      var start = Verified.Number;
      yield return WaitFor(() => Finished(site), 60f, "건설 완료");
      Assert.Less((Verified.Number - start) / (float)Fps, 12f, "혼자면 15초");
    }

    [UnityTest]
    public IEnumerator Build_RejectsBadRequests() {
      yield return StartMatch();
      var engineer = Engineers(0)[0];

      yield return Build(0, "_barracks", V(-30, 0), engineer);
      AssertRejected(0, ActionKind.Build, RejectReason.BlockedLocation);
      _rejected.Clear();

      yield return Build(0, "_barracks", V(-30, 14), engineer);
      AssertRejected(0, ActionKind.Build, RejectReason.BlockedLocation);
      _rejected.Clear();

      yield return Build(0, "_barracks", V(49, 0), engineer);
      AssertRejected(0, ActionKind.Build, RejectReason.BlockedLocation);
      _rejected.Clear();

      var soldier = Army(0, true)[0];
      yield return Build(0, "_barracks", V(-20, 10), soldier);
      AssertRejected(0, ActionKind.Build, RejectReason.Invalid);
      _rejected.Clear();

      yield return Send(0, new BuildCommand { Engineer = engineer, Building = BuildingOf(1, "_barracks"), Position = V(-20, 10) });
      AssertRejected(0, ActionKind.Build, RejectReason.Invalid);
      _rejected.Clear();

      yield return Build(1, "_barracks", V(0, 0), engineer);
      AssertRejected(1, ActionKind.Build, RejectReason.Invalid);

      Assert.AreEqual(5000, Funds(0), "거부되면 비용 없음");
      Assert.AreEqual(2, Verified.ComponentCount<Building>());
    }

    [UnityTest]
    public IEnumerator Build_RejectsWithEveryMissingResource() {
      yield return StartMatch(buildings: Plant, stock: Stock(100, 500, 0, 0));
      var count = Verified.ComponentCount<Building>();
      // 연구소: 철 250, 목재 200, 전기 150. 목재만 충분하다.
      yield return Build(0, "_research_lab", V(-20, 10));
      AssertRejected(0, ActionKind.Build, RejectReason.InsufficientResources, MissingResource.Iron | MissingResource.Power);
      Assert.AreEqual(100, Funds(0));
      Assert.AreEqual(500, Wood(0));
      Assert.AreEqual(count, Verified.ComponentCount<Building>());
    }

    [UnityTest]
    public IEnumerator Produce_PopulationLimitCountsQueue() {
      // 시작 유닛 인구 6, 한도 10. 전차는 인구 3이라 대기열까지 합해 한 대만 들어간다.
      yield return StartMatch(buildings: new[] { (0, "ff_vehicle_factory", -42, 12) });
      var factory = BuildingAt(V(-42, 12));
      yield return Send(0, new ProduceCommand { Building = factory, Unit = UnitOf(0, "ff_eagle_tank"), Count = 2 });
      Assert.AreEqual(1, BuildingState(factory).QueueCount);
      Assert.AreEqual(4750, Funds(0), "한 대만 비용을 낸다");
      AssertRejected(0, ActionKind.Produce, RejectReason.PopulationLimit, MissingResource.Population);
    }

    [UnityTest]
    public IEnumerator Produce_RejectsWithResourcesAndPopulationTogether() {
      var extra = Enumerable.Range(0, 2).Select(i => (0, "ff_eagle_tank", -48 + i * 4, -30)).ToArray();
      yield return StartMatch(units: extra, buildings: new[] { (0, "ff_vehicle_factory", -42, 12) }, stock: Stock(100, 0, 500, 200));
      yield return WaitFrames(2);
      var factory = BuildingAt(V(-42, 12));
      yield return Send(0, new ProduceCommand { Building = factory, Unit = UnitOf(0, "ff_eagle_tank"), Count = 1 });
      AssertRejected(0, ActionKind.Produce, RejectReason.InsufficientResources, MissingResource.Iron | MissingResource.Population);
      Assert.AreEqual(0, BuildingState(factory).QueueCount);
    }

    [UnityTest]
    public IEnumerator Build_OnObstacleRejected() {
      yield return StartMatch(Map2PScene, 2);
      yield return Build(0, "_barracks", V(-33, -68));
      AssertRejected(0, ActionKind.Build, RejectReason.BlockedLocation);
      Assert.AreEqual(500, Funds(0), "실제 맵 시작 철");
      _rejected.Clear();

      yield return Build(0, "_power_plant", V(-45, -60));
      Assert.AreEqual(0, _rejected.Count, "빈 땅은 허용");
      Assert.AreEqual(350, Funds(0));
    }

    [UnityTest]
    public IEnumerator Build_LockedUntilPrerequisitesFinished() {
      yield return StartMatch();
      var engineer = Engineers(0)[0];
      yield return Build(0, "_processor", V(-20, 10), engineer);
      AssertRejected(0, ActionKind.Build, RejectReason.Locked);
      Assert.AreEqual(5000, Funds(0), "잠긴 건물은 비용 없음");
      _rejected.Clear();

      // 미완성 발전소는 선행 조건이 아니다.
      yield return Build(0, "_power_plant", V(-20, 25), engineer);
      yield return Build(0, "_processor", V(-20, 10), engineer);
      AssertRejected(0, ActionKind.Build, RejectReason.Locked);
      _rejected.Clear();

      var plant = BuildingAt(V(-20, 25));
      yield return Send(0, new RepairCommand { Units = new[] { engineer }, Target = plant });
      yield return WaitFor(() => Finished(plant), 60f, "발전소 완공");
      yield return Build(0, "_processor", V(-20, 10), engineer);
      Assert.AreEqual(0, _rejected.Count, "발전소가 있으면 가공소 가능");
    }

    [UnityTest]
    public IEnumerator Build_HeavyWorksNeedsFactoryAndLab() {
      // 가공소가 빠지면 군수공장과 연구소가 있어도 잠겨 있다.
      yield return StartMatch(buildings: new[] { (0, "ff_vehicle_factory", -42, 12), (0, "ff_research_lab", -42, -12) });
      yield return Build(0, "_heavy_works", V(-20, 10));
      AssertRejected(0, ActionKind.Build, RejectReason.Locked);
    }

    [UnityTest]
    public IEnumerator Build_TechTreeFollowsDesign() {
      yield return StartMatch();
      var f = Verified;
      string Needs(string suffix) => string.Join(",", (f.FindAsset(BuildingOf(0, suffix)).Prerequisites)
        .Select(b => f.FindAsset(b).name.Substring(3)).OrderBy(n => n));
      Assert.AreEqual("headquarters", Needs("_power_plant"));
      Assert.AreEqual("headquarters", Needs("_supply_depot"));
      Assert.AreEqual("headquarters", Needs("_collection_yard"));
      Assert.AreEqual("headquarters", Needs("_barracks"));
      Assert.AreEqual("power_plant", Needs("_processor"));
      Assert.AreEqual("power_plant", Needs("_research_lab"));
      Assert.AreEqual("barracks,power_plant", Needs("_vehicle_factory"));
      Assert.AreEqual("barracks,power_plant", Needs("_ground_tower"));
      Assert.AreEqual("barracks,power_plant", Needs("_air_defense"));
      Assert.AreEqual("headquarters", Needs("_headquarters"));
      Assert.AreEqual("processor,vehicle_factory", Needs("_airfield"));
      Assert.AreEqual("processor,research_lab,vehicle_factory", Needs("_heavy_works"));
    }

    // 전기

    [UnityTest]
    public IEnumerator Power_AccumulatesAtFivePerSecondUpToCap() {
      yield return StartMatch(buildings: Plant, stock: Stock(1000, 1000, 0, 0));
      var from = Power(0);
      var start = Verified.Number;
      yield return WaitFrames(10 * Fps);
      var seconds = (Verified.Number - start) / (float)Fps;
      Assert.AreEqual(from + 5f * seconds, Power(0), 0.2f, "초당 5");
      Assert.AreEqual(500f, PlayerOf(0).PowerCap.AsFloat, Tolerance, "사령부 200 + 발전소 300");
      Assert.IsFalse((bool)PlayerOf(0).Outage, "수요가 없으면 정전이 아니다");
    }

    [UnityTest]
    public IEnumerator Power_ExcessDisappearsWhenPlantDestroyed() {
      var tanks = Enumerable.Range(0, 8).Select(i => (1, "cf_dragonscale_tank", -44 + i * 3, -42)).ToArray();
      yield return StartMatch(units: tanks, buildings: new[] { (0, "ff_power_plant", -36, -34) }, stock: Stock(1000, 1000, 0, 500));
      Assert.AreEqual(500f, Power(0), Tolerance);
      var plant = BuildingAt(V(-36, -34));
      var attackers = UnitsNamed(1, "cf_dragonscale_tank").Where(e => Verified.Get<Transform3D>(e).Position.Z < -30).ToArray();
      Game.SendCommand(1, new AttackTargetCommand { Units = attackers, Target = plant });
      yield return WaitFor(() => Verified.Exists(plant) == false, 60f, "발전소 파괴");
      yield return WaitFrames(2);
      Assert.AreEqual(200f, PlayerOf(0).PowerCap.AsFloat, Tolerance);
      Assert.AreEqual(200f, Power(0), Tolerance, "한도를 넘는 전기는 삭제");
    }

    [UnityTest]
    public IEnumerator Power_RefundStopsAtCap() {
      yield return StartMatch(buildings: new[] { (0, "ff_power_plant", -42, 0), (0, "ff_barracks", -42, 12) }, stock: Stock(1000, 1000, 0, 500));
      var barracks = BuildingAt(V(-42, 12));
      var skyguard = FactionOf(0).Units.First(u => Verified.FindAsset(u).name == "ff_skyguard_squad");
      yield return Send(0, new ProduceCommand { Building = barracks, Unit = skyguard, Count = 1 });
      Assert.Less(Power(0), 500f, "스카이가드 전기 20");
      yield return WaitFor(() => Power(0) >= 500f, 30f, "다시 가득 참");
      yield return Send(0, new ProduceCommand { Building = barracks, Unit = skyguard, Count = 1, Cancel = true });
      Assert.AreEqual(500f, Power(0), Tolerance, "한도까지만 환불");
      Assert.AreEqual(1000, Funds(0), "철은 전부 환불");
    }

    static AssetRef<UnitSpec> UnitOf(int player, string name) => FactionOf(player).Units.First(u => Verified.FindAsset(u).name == name);

    [UnityTest]
    public IEnumerator Power_UpkeepOnlyWhileProducing() {
      yield return StartMatch(buildings: new[] { (0, "ff_vehicle_factory", -42, 12) }, stock: Stock(2000, 2000, 2000, 100));
      var factory = BuildingAt(V(-42, 12));
      yield return WaitFrames(2 * Fps);
      Assert.AreEqual(100f, Power(0), Tolerance, "쉬는 공장은 유지비 없음");

      yield return Send(0, new ProduceCommand { Building = factory, Unit = UnitOf(0, "ff_eagle_tank"), Count = 1 });
      var from = Power(0);
      Assert.Less(from, 41f, "전차 전기 60");
      var start = Verified.Number;
      yield return WaitFrames(5 * Fps);
      var seconds = (Verified.Number - start) / (float)Fps;
      Assert.AreEqual(from - seconds, Power(0), 0.2f, "초당 1");
      Assert.AreEqual(-1f, PlayerOf(0).PowerRate.AsFloat, Tolerance, "순증감");

      yield return Send(0, new ProduceCommand { Building = factory, Unit = UnitOf(0, "ff_eagle_tank"), Count = 1, Cancel = true });
      var stopped = Power(0);
      yield return WaitFrames(2 * Fps);
      Assert.AreEqual(stopped, Power(0), Tolerance, "취소하면 소모도 멈춤");
    }

    [UnityTest]
    public IEnumerator Power_OutagePausesFactoryButNotBarracksThenResumesAtFive() {
      yield return StartMatch(buildings: new[] { (0, "ff_vehicle_factory", -42, 12), (0, "ff_barracks", -42, -12) }, stock: Stock(3000, 3000, 2000, 61));
      var outages = new List<bool>();
      var paused = new List<PauseReason>();
      QuantumEvent.Subscribe<EventPowerOutage>(_listener, e => outages.Add(e.Started));
      QuantumEvent.Subscribe<EventBuildingPaused>(_listener, e => paused.Add(e.Reason));

      var factory = BuildingAt(V(-42, 12));
      var barracks = BuildingAt(V(-42, -12));
      yield return Send(0, new ProduceCommand { Building = barracks, Unit = UnitOf(0, "ff_ranger_squad"), Count = 1 });
      Game.SendCommand(0, new ProduceCommand { Building = factory, Unit = UnitOf(0, "ff_eagle_tank"), Count = 1 });
      yield return WaitFor(() => PlayerOf(0).Outage, 30f, "정전");
      yield return WaitFrames(CommandFrames);
      Assert.AreEqual(PauseReason.Outage, BuildingState(factory).Paused);
      Assert.AreEqual(1, PlayerOf(0).OutageCount);
      Assert.AreEqual(new List<bool> { true }, outages);
      Assert.Contains(PauseReason.Outage, paused);

      var factoryProgress = BuildingState(factory).QueueProgress;
      var barracksProgress = BuildingState(barracks).QueueProgress;
      yield return WaitFrames(2 * Fps);
      Assert.AreEqual(factoryProgress.AsFloat, BuildingState(factory).QueueProgress.AsFloat, 0.001f, "공장 타이머 정지");
      Assert.AreEqual(1, BuildingState(factory).QueueCount, "대기열 유지");
      Assert.Greater(BuildingState(barracks).QueueProgress.AsFloat, barracksProgress.AsFloat, "병영은 영향 없음");

      yield return BuildAndFinish(0, "_power_plant", V(-20, 10));
      yield return WaitFor(() => PlayerOf(0).Outage == false, 30f, "전기 5 이상에서 해제");
      Assert.GreaterOrEqual(Power(0), 4.9f);
      yield return WaitFor(() => BuildingState(factory).QueueProgress > factoryProgress, 30f, "자동 재개");
      Assert.AreEqual(PauseReason.None, BuildingState(factory).Paused);
      Assert.AreEqual(new List<bool> { true, false }, outages);
      Assert.Greater(PlayerOf(0).OutageSeconds.AsFloat, 2f, "정전 시간 기록");
    }

    [UnityTest]
    public IEnumerator Power_OutageDoublesTowerInterval() {
      yield return StartMatch(units: new[] { (1, "cf_thunder_aa", 6, ArenaZ) }, buildings: new[] { (0, "ff_ground_tower", 0, ArenaZ) }, stock: Stock(1000, 1000, 0, 0));
      var target = UnitsNamed(1, "cf_thunder_aa")[0];
      yield return WaitFor(() => Health(target) < 450f, 30f, "첫 사격");
      Assert.IsTrue((bool)PlayerOf(0).Outage, "방어탑은 상시 유지비");
      var first = Health(target);
      var start = Verified.Number;
      yield return WaitFor(() => Health(target) < first, 30f, "둘째 사격");
      Assert.AreEqual(3f, (Verified.Number - start) / (float)Fps, 0.4f, "간격 1.5초 x 2");
    }

    // 가공소

    static readonly (int, string, int, int)[] Processor = { (0, "ff_processor", -42, 12) };

    static float Steel(int player) => PlayerOf(player).Stock.Steel.AsFloat;

    [UnityTest]
    public IEnumerator Processor_ConvertsTwoIronAndOnePowerPerSecond() {
      yield return StartMatch(buildings: Processor, stock: Stock(1000, 0, 0, 100));
      var start = Verified.Number;
      var startSteel = Steel(0);
      yield return WaitFrames(5 * Fps);
      var seconds = (Verified.Number - start) / (float)Fps;
      var made = Steel(0) - startSteel;
      Assert.AreEqual(seconds, made, 1.1f, "1초마다 1");
      Assert.AreEqual(1000f - 2f * Steel(0), ExactFunds(0), Tolerance, "강철 1당 철 2");
      Assert.AreEqual(100f - Steel(0), Power(0), Tolerance, "강철 1당 전기 1");
      Assert.AreEqual(-1f, PlayerOf(0).PowerRate.AsFloat, Tolerance, "순증감에 변환 소모 포함");
    }

    [UnityTest]
    public IEnumerator Processor_StopsWithoutIron_NotAnOutage() {
      yield return StartMatch(buildings: Processor, stock: Stock(3, 0, 0, 100));
      yield return WaitFrames(4 * Fps);
      Assert.AreEqual(1f, Steel(0), Tolerance, "철 2가 있을 때 한 번만");
      Assert.AreEqual(1f, ExactFunds(0), Tolerance, "남은 철 1은 쓰지 않음");
      Assert.AreEqual(99f, Power(0), Tolerance);
      Assert.IsFalse((bool)PlayerOf(0).Outage, "철 부족은 정전이 아니다");
    }

    [UnityTest]
    public IEnumerator Processor_StopsWithoutPower() {
      yield return StartMatch(buildings: Processor, stock: Stock(1000, 0, 0, 1));
      yield return WaitFor(() => PlayerOf(0).Outage, 30f, "전기 소진");
      Assert.AreEqual(1f, Steel(0), Tolerance);
      Assert.AreEqual(998f, ExactFunds(0), Tolerance);
      yield return WaitFrames(3 * Fps);
      Assert.AreEqual(1f, Steel(0), Tolerance, "정전 중 변환 없음");
      Assert.AreEqual(PauseReason.Outage, BuildingState(BuildingAt(V(-42, 12))).Paused);
    }

    [UnityTest]
    public IEnumerator Processor_SwitchOffStopsSpending() {
      yield return StartMatch(buildings: Processor, stock: Stock(1000, 0, 0, 100));
      var processor = BuildingAt(V(-42, 12));
      yield return Send(0, new SetProcessorCommand { Building = processor, Enabled = false });
      var steel = Steel(0);
      var iron = ExactFunds(0);
      yield return WaitFrames(3 * Fps);
      Assert.AreEqual(steel, Steel(0), Tolerance, "끄면 변환 없음");
      Assert.AreEqual(iron, ExactFunds(0), Tolerance);
      Assert.AreEqual(0f, PlayerOf(0).PowerRate.AsFloat, Tolerance);

      yield return Send(0, new SetProcessorCommand { Building = processor, Enabled = true });
      yield return WaitFor(() => Steel(0) > steel, 30f, "켜면 재개");
    }

    [UnityTest]
    public IEnumerator Processor_SwitchRejectsOtherBuildings() {
      yield return StartMatch(buildings: Processor);
      yield return Send(0, new SetProcessorCommand { Building = Headquarters(0), Enabled = false });
      AssertRejected(0, ActionKind.Processor, RejectReason.Invalid);
      _rejected.Clear();
      yield return Send(1, new SetProcessorCommand { Building = BuildingAt(V(-42, 12)), Enabled = false });
      AssertRejected(1, ActionKind.Processor, RejectReason.Invalid);
      Assert.IsFalse((bool)BuildingState(BuildingAt(V(-42, 12))).ProcessorOff, "남의 가공소는 못 끈다");
    }

    // 건물 충돌

    static float DistanceToSquare(FPVector3 unit, FPVector3 center) {
      return Mathf.Max(Mathf.Abs((unit - center).X.AsFloat), Mathf.Abs((unit - center).Z.AsFloat));
    }

    [UnityTest]
    public IEnumerator Units_WalkAroundBuildings() {
      // 건물 정중앙을 지나는 직선 경로.
      var center = V(0, 40);
      yield return StartMatch(units: new[] { (0, "ff_eagle_tank", -15, 40) }, buildings: new[] { (0, "ff_barracks", 0, 40) });
      var tank = UnitsNamed(0, "ff_eagle_tank").First(e => Verified.Get<Transform3D>(e).Position.Z > 30);
      var target = V(15, 40);
      Game.SendCommand(0, new MoveUnitsCommand { Units = new[] { tank }, Target = target });

      var closest = float.MaxValue;
      yield return WaitFor(() => {
        closest = Mathf.Min(closest, DistanceToSquare(Verified.Get<Transform3D>(tank).Position, center));
        return (Verified.Get<Transform3D>(tank).Position - target).Magnitude < 2;
      }, 30f, "건물을 돌아 도착");
      Assert.GreaterOrEqual(closest, 3f, "부지 안으로 들어가지 않음");
    }

    [UnityTest]
    public IEnumerator Units_EscapePocketBetweenBuildings() {
      // AI 경기에서 공병이 갇혔던 배치: 8칸 간격 건물 세 채가 만든 구석.
      var inside = (0, "ff_engineer", -8, 36);
      yield return StartMatch(units: new[] { inside }, buildings: new[] {
        (0, "ff_barracks", -8, 40), (0, "ff_power_plant", -8, 32), (0, "ff_supply_depot", 0, 32),
      });
      var engineer = Engineers(0).First(e => Verified.Get<Transform3D>(e).Position.Z > 30);
      var target = V(20, 45);
      Game.SendCommand(0, new MoveUnitsCommand { Units = new[] { engineer }, Target = target });
      yield return WaitFor(() => (Verified.Get<Transform3D>(engineer).Position - target).Magnitude < 2, 30f, "구석 탈출");
    }

    [UnityTest]
    public IEnumerator Units_FollowRowOfTouchingBuildings() {
      // 틈 없이 붙은 건물 다섯 채. 경로는 건물을 모르므로 벽을 따라 끝까지 돌아가야 한다.
      var row = Enumerable.Range(-2, 5).Select(i => (0, "ff_supply_depot", i * 6, 40)).ToArray();
      yield return StartMatch(units: new[] { (0, "ff_eagle_tank", 1, 30) }, buildings: row);
      var tank = UnitsNamed(0, "ff_eagle_tank").First(e => Verified.Get<Transform3D>(e).Position.Z > 25);
      var target = V(1, 48);
      Game.SendCommand(0, new MoveUnitsCommand { Units = new[] { tank }, Target = target });
      yield return WaitFor(() => (Verified.Get<Transform3D>(tank).Position - target).Magnitude < 2, 30f, "줄지은 건물을 돌아 도착");
    }

    // 지형

    [UnityTest]
    public IEnumerator Terrain_GroundUnitsWalkAroundForest() {
      yield return StartMatch(units: new[] { (0, "ff_eagle_tank", -38, 20) });
      var tank = UnitsNamed(0, "ff_eagle_tank").First(e => Verified.Get<Transform3D>(e).Position.Z > 15);
      var forest = Verified.Get<ResourceNode>(NodeAt(V(-38, 30))).Shape;
      var target = V(-38, 40);
      Game.SendCommand(0, new MoveUnitsCommand { Units = new[] { tank }, Target = target });
      var closest = float.MaxValue;
      yield return WaitFor(() => {
        var position = Verified.Get<Transform3D>(tank).Position;
        closest = Mathf.Min(closest, forest.Distance(position).AsFloat);
        return (position - target).Magnitude < 2;
      }, 30f, "숲을 돌아 도착");
      Assert.Greater(closest, 0f, "숲 안으로 들어가지 않음");
    }

    [UnityTest]
    public IEnumerator Terrain_AirUnitsFlyOverForest() {
      yield return StartMatch(units: new[] { (0, "ff_hawk_gunship", -38, 20) });
      var hawk = UnitsNamed(0, "ff_hawk_gunship")[0];
      var forest = Verified.Get<ResourceNode>(NodeAt(V(-38, 30))).Shape;
      var target = V(-38, 40);
      Game.SendCommand(0, new MoveUnitsCommand { Units = new[] { hawk }, Target = target });
      var crossed = false;
      yield return WaitFor(() => {
        var position = Verified.Get<Transform3D>(hawk).Position;
        crossed |= forest.Distance(position) < FP._0;
        return FactoryWarsUtil.FlatDistanceSqr(position, target) < 1;
      }, 30f, "직선 비행");
      Assert.IsTrue(crossed, "숲 위를 지나감");
    }

    [UnityTest]
    public IEnumerator Terrain_BuildingKeepsThreeFromResources() {
      yield return StartMatch();
      var engineer = Engineers(0)[0];
      // 철광산 (-30, 14) 반지름 3, 부지 반지름 3: 중심 거리 9가 가장자리 3이다.
      yield return Build(0, "_supply_depot", V(-22, 14), engineer);
      AssertRejected(0, ActionKind.Build, RejectReason.BlockedLocation);
      _rejected.Clear();
      yield return Build(0, "_supply_depot", V(-21, 14), engineer);
      Assert.AreEqual(0, _rejected.Count, "철광산에서 3 떨어지면 허용");

      // 숲 (-38, 30) 긴 변 z = 27: 부지 중심 z = 21이 가장자리 3이다.
      yield return Build(0, "_supply_depot", V(-38, 22), engineer);
      AssertRejected(0, ActionKind.Build, RejectReason.BlockedLocation);
      _rejected.Clear();
      yield return Build(0, "_supply_depot", V(-38, 21), engineer);
      Assert.AreEqual(0, _rejected.Count, "숲에서 3 떨어지면 허용");
    }

    [UnityTest]
    public IEnumerator Terrain_RejectsRockAndNoBuildZone() {
      yield return StartMatch();
      var engineer = Engineers(0)[0];
      yield return Build(0, "_supply_depot", V(40, -40), engineer);
      AssertRejected(0, ActionKind.Build, RejectReason.BlockedLocation);
      _rejected.Clear();
      yield return Build(0, "_supply_depot", V(40, -22), engineer);
      AssertRejected(0, ActionKind.Build, RejectReason.BlockedLocation);
      _rejected.Clear();
      // 금지 구역 가장자리에 부지 모서리만 걸쳐도 안 된다.
      yield return Build(0, "_supply_depot", V(32, -22), engineer);
      AssertRejected(0, ActionKind.Build, RejectReason.BlockedLocation);
      _rejected.Clear();
      yield return Build(0, "_supply_depot", V(30, -22), engineer);
      Assert.AreEqual(0, _rejected.Count, "구역 밖은 허용");
    }

    // 패배 조건

    [UnityTest]
    public IEnumerator Defeat_RequiresLosingHeadquartersAndProductionBuildings() {
      var tanks = Enumerable.Range(0, 10).Select(i => (1, "cf_dragonscale_tank", -48 + i * 2, -40)).ToArray();
      yield return StartMatch(units: tanks, buildings: new[] { (0, "ff_barracks", -40, -40), (0, "ff_power_plant", -40, 40) });
      var attackers = UnitsNamed(1, "cf_dragonscale_tank").Where(e => Verified.Get<Transform3D>(e).Position.Z < -30).ToArray();

      var barracks = BuildingAt(V(-40, -40));
      Game.SendCommand(1, new AttackTargetCommand { Units = attackers, Target = barracks });
      yield return WaitFor(() => Verified.Exists(barracks) == false, 60f, "병영 파괴");
      yield return WaitFrames(CommandFrames);
      Assert.IsFalse(Defeated(0), "사령부가 남아 있음");

      var headquarters = Headquarters(0);
      var survivors = attackers.Where(e => Verified.Exists(e)).ToArray();
      Game.SendCommand(1, new AttackTargetCommand { Units = survivors, Target = headquarters });
      yield return WaitFor(() => Verified.Exists(headquarters) == false, 90f, "사령부 파괴");
      yield return WaitFrames(CommandFrames);
      Assert.IsTrue(Defeated(0), "발전소만 남으면 패배");
      Assert.IsTrue(Verified.Exists(BuildingAt(V(-40, 40))));
      Assert.IsTrue(GameEnded());
    }

    // 수리

    [UnityTest]
    public IEnumerator Repair_ChargesEveryResource() {
      yield return StartMatch(units: new[] { (1, "cf_dragonscale_tank", -8, 10), (1, "cf_dragonscale_tank", -8, 13), (1, "cf_dragonscale_tank", -8, 7) });
      var position = V(-20, 10);
      yield return BuildAndFinish(0, "_power_plant", position);
      var plant = BuildingAt(position);
      var engineer = Engineers(0)[0];

      var tanks = UnitsNamed(1, "cf_dragonscale_tank").Where(e => Verified.Get<Transform3D>(e).Position.Z > 5).ToArray();
      Game.SendCommand(1, new AttackTargetCommand { Units = tanks, Target = plant });
      yield return WaitFor(() => BuildingState(plant).Health < 400, 60f, "건물 피해");
      yield return Send(1, new MoveUnitsCommand { Units = tanks, Target = V(30, 20) });
      yield return WaitFrames(3 * Fps);

      var damaged = BuildingState(plant).Health.AsFloat;
      Assert.Less(damaged, 600f);
      var funds = Funds(0);
      var wood = ExactWood(0);
      yield return Send(0, new RepairCommand { Units = new[] { engineer }, Target = plant });
      yield return WaitFor(() => BuildingState(plant).Health.AsFloat >= 600f, 60f, "수리 완료");
      yield return WaitFrames(CommandFrames);

      Assert.AreEqual(WorkerTask.Idle, WorkerOf(engineer).Task, "다 고치면 멈춤");
      // 회복 비율 x 건물 비용(철 150, 목재 150) x 0.5
      var expected = (600f - damaged) / 600f * 150f * 0.5f;
      Assert.AreEqual(expected, funds - ExactFunds(0), 2f, "철 수리비");
      Assert.AreEqual(expected, wood - ExactWood(0), 2f, "목재 수리비");
    }

    [UnityTest]
    public IEnumerator Repair_StopsWhenOneResourceRunsOut() {
      var tanks = new[] { (1, "cf_dragonscale_tank", -8, 10), (1, "cf_dragonscale_tank", -8, 13), (1, "cf_dragonscale_tank", -8, 7) };
      // 발전소 목재 150을 내면 2가 남는다. 철은 충분하다.
      yield return StartMatch(units: tanks, stock: Stock(3000, 152, 0, 0));
      var position = V(-20, 10);
      yield return BuildAndFinish(0, "_power_plant", position);
      var plant = BuildingAt(position);
      var engineer = Engineers(0)[0];
      var attackers = UnitsNamed(1, "cf_dragonscale_tank").Where(e => Verified.Get<Transform3D>(e).Position.Z > 5).ToArray();
      Game.SendCommand(1, new AttackTargetCommand { Units = attackers, Target = plant });
      yield return WaitFor(() => BuildingState(plant).Health < 400, 60f, "건물 피해");
      yield return Send(1, new MoveUnitsCommand { Units = attackers, Target = V(30, 20) });
      yield return WaitFrames(3 * Fps);

      yield return Send(0, new RepairCommand { Units = new[] { engineer }, Target = plant });
      yield return WaitFor(() => WorkerOf(engineer).Task == WorkerTask.Idle, 60f, "수리 중단");
      Assert.Less(BuildingState(plant).Health.AsFloat, 600f, "다 고치지 못함");
      Assert.Less(ExactWood(0), 0.5f, "목재가 떨어짐");
      Assert.Greater(Funds(0), 2000, "철은 남아 있음");
    }

    [UnityTest]
    public IEnumerator Repair_RejectsEnemyAndFullBuildings() {
      yield return StartMatch();
      var engineer = Engineers(0)[0];
      yield return Send(0, new RepairCommand { Units = new[] { engineer }, Target = Headquarters(1) });
      AssertRejected(0, ActionKind.Repair, RejectReason.Invalid);
      _rejected.Clear();
      yield return Send(0, new RepairCommand { Units = new[] { engineer }, Target = Headquarters(0) });
      AssertRejected(0, ActionKind.Repair, RejectReason.Invalid);
    }

    // 공격 상성

    [UnityTest]
    public IEnumerator Matchups_FollowDesign() {
      yield return StartMatch();
      var rules = Verified.FindAsset(Verified.RuntimeConfig.Rules);
      var units = FactionOf(0).Units.Concat(FactionOf(1).Units).Select(u => Verified.FindAsset(u)).ToArray();
      UnitSpec Named(string name) => units.First(u => u.name == name);
      float Vs(AttackType attack, string name) => CombatSystem.UnitMultiplier(rules, attack, Named(name)).AsFloat;

      Assert.Greater(Vs(AttackType.AntiInfantry, "ff_ranger_squad"), Vs(AttackType.AntiInfantry, "ff_eagle_tank"), "대인은 소형에 강함");
      Assert.Greater(Vs(AttackType.AntiInfantry, "ff_ranger_squad"), 1f);
      Assert.Greater(Vs(AttackType.AntiArmor, "ff_eagle_tank"), Vs(AttackType.AntiArmor, "ff_ranger_squad"), "대장갑은 중형에 강함");
      Assert.Greater(Vs(AttackType.AntiArmor, "cf_longwall_mlrs"), 1f, "대장갑은 대형에 강함");
      Assert.Greater(Vs(AttackType.AntiAir, "ff_hawk_gunship"), 1f, "대공은 공중에 강함");
      Assert.AreEqual(1f, Vs(AttackType.Area, "ff_eagle_tank"), Tolerance);
    }

    [UnityTest]
    public IEnumerator Matchups_AppliedInBattle() {
      // 레인저(대인 15)가 공병(소형 x1.5)과 대공차(중형 x0.6)를 각각 공격한다.
      yield return StartMatch(units: new[] { (0, "ff_ranger_squad", 0, ArenaZ), (1, "cf_thunder_aa", 4, ArenaZ) });
      var target = UnitsNamed(1, "cf_thunder_aa")[0];
      yield return WaitFor(() => Health(target) < 450f, 30f, "첫 피해");
      Assert.AreEqual(441f, Health(target), Tolerance, "15 x 0.6");
    }

    // 유닛 특수 기능

    [UnityTest]
    public IEnumerator Ability_TargetDesignator_BoostsNextRangedHit() {
      yield return StartMatch(units: new[] { (0, "ff_ranger_squad", 0, ArenaZ), (1, "cf_thunder_aa", 4, ArenaZ) });
      var target = UnitsNamed(1, "cf_thunder_aa")[0];
      yield return WaitFor(() => Health(target) < 450f, 30f, "첫 피해");
      Assert.AreEqual(441f, Health(target), Tolerance, "표적 지시 전");
      yield return WaitFor(() => Health(target) < 441f, 30f, "두 번째 피해");
      Assert.AreEqual(429.75f, Health(target), Tolerance, "9 x 1.25");
    }

    [UnityTest]
    public IEnumerator Ability_FirstShotDouble() {
      yield return StartMatch(units: new[] { (0, "ff_eagle_tank", 0, ArenaZ), (1, "cf_thunder_aa", 5, ArenaZ) });
      var target = UnitsNamed(1, "cf_thunder_aa")[0];
      yield return WaitFor(() => Health(target) < 450f, 30f, "첫 발");
      Assert.AreEqual(270f, Health(target), Tolerance, "60 x 1.5 x 2");
      yield return WaitFor(() => Health(target) < 270f, 30f, "둘째 발");
      Assert.AreEqual(180f, Health(target), Tolerance, "60 x 1.5");
    }

    [UnityTest]
    public IEnumerator Ability_GroundArmorResist_HitByGroundArmorAtHalf() {
      yield return StartMatch(units: new[] { (0, "ff_eagle_tank", 0, ArenaZ), (1, "ff_hawk_gunship", 5, ArenaZ) });
      var target = UnitsNamed(1, "ff_hawk_gunship")[0];
      yield return WaitFor(() => Health(target) < 450f, 30f, "헬기 피격");
      Assert.AreEqual(390f, Health(target), Tolerance, "60 x 공중 1 x 저공 0.5 x 첫 발 2");
    }

    [UnityTest]
    public IEnumerator Ability_GroundUnitsCannotHitOtherAir() {
      yield return StartMatch(units: new[] { (0, "ff_eagle_tank", 0, ArenaZ), (1, "ff_skyfortress_bomber", 5, ArenaZ) });
      var target = UnitsNamed(1, "ff_skyfortress_bomber")[0];
      yield return WaitFrames(5 * Fps);
      Assert.AreEqual(1600f, Health(target), Tolerance, "저공 비행이 아닌 공중 유닛은 대장갑에 맞지 않음");
    }

    [UnityTest]
    public IEnumerator Ability_AirPriority_PrefersAirThenGround() {
      yield return StartMatch(units: new[] { (0, "ff_skyguard_squad", 0, ArenaZ), (1, "cf_militia", 3, ArenaZ), (1, "ff_hawk_gunship", -6, ArenaZ) });
      var militia = UnitsNamed(1, "cf_militia").First(e => Verified.Get<Transform3D>(e).Position.Z > 30);
      var hawk = UnitsNamed(1, "ff_hawk_gunship")[0];
      yield return WaitFor(() => Health(hawk) < 450f, 30f, "공중 우선");
      Assert.AreEqual(80f, Health(militia), Tolerance, "더 가까운 지상을 두고 공중을 쏨");
    }

    [UnityTest]
    public IEnumerator Ability_AirPriority_ShootsGroundWhenNoAir() {
      yield return StartMatch(units: new[] { (0, "ff_skyguard_squad", 0, ArenaZ), (1, "cf_thunder_aa", 3, ArenaZ) });
      var target = UnitsNamed(1, "cf_thunder_aa")[0];
      yield return WaitFor(() => Health(target) < 450f, 30f, "지상 공격");
      Assert.AreEqual(445f, Health(target), Tolerance, "20 x 0.25");
    }

    [UnityTest]
    public IEnumerator Ability_CarpetBomb_FiresOnCrowd() {
      yield return StartMatch(units: new[] {
        (0, "ff_skyfortress_bomber", 0, ArenaZ),
        (1, "cf_militia", 6, ArenaZ), (1, "cf_militia", -6, ArenaZ), (1, "cf_militia", 0, ArenaZ + 6), (1, "cf_militia", 0, ArenaZ - 6), (1, "cf_militia", 5, ArenaZ + 5),
      });
      var bomber = UnitsNamed(0, "ff_skyfortress_bomber")[0];
      yield return WaitFor(() => Verified.Get<Unit>(bomber).AbilityCooldown > FP._0, 30f, "융단폭격 발동");
      Assert.Greater(Verified.Get<Unit>(bomber).AbilityCooldown.AsFloat, 40f, "쿨타임 45초");
      yield return WaitFrames(2);
      var inArena = UnitsNamed(1, "cf_militia").Count(e => Verified.Get<Transform3D>(e).Position.Z > 30);
      Assert.AreEqual(0, inArena, "민병대 전멸");
    }

    [UnityTest]
    public IEnumerator Ability_CarpetBomb_IgnoresSmallGroups() {
      yield return StartMatch(units: new[] {
        (0, "ff_skyfortress_bomber", 0, ArenaZ),
        (1, "cf_militia", 6, ArenaZ), (1, "cf_militia", -6, ArenaZ), (1, "cf_militia", 0, ArenaZ + 6), (1, "cf_militia", 0, ArenaZ - 6),
      });
      var bomber = UnitsNamed(0, "ff_skyfortress_bomber")[0];
      yield return WaitFrames(3 * Fps);
      Assert.AreEqual(0f, Verified.Get<Unit>(bomber).AbilityCooldown.AsFloat, Tolerance, "4기면 발동 안 함");
    }

    [UnityTest]
    public IEnumerator Ability_SwarmSpeed_NeedsFiveTanks() {
      // 시작 유닛 1대 포함 5대.
      yield return StartMatch(units: new[] {
        (1, "cf_dragonscale_tank", 20, ArenaZ), (1, "cf_dragonscale_tank", 24, ArenaZ), (1, "cf_dragonscale_tank", 28, ArenaZ), (1, "cf_dragonscale_tank", 32, ArenaZ),
      });
      yield return WaitFrames(2 * Fps);
      var agent = Verified.Get<NavMeshSteeringAgent>(UnitsNamed(1, "cf_dragonscale_tank")[0]);
      Assert.AreEqual(5.75f, agent.MaxSpeed.AsFloat, Tolerance, "5 x 1.15");
    }

    [UnityTest]
    public IEnumerator Ability_SwarmSpeed_NotBelowFive() {
      yield return StartMatch(units: new[] {
        (1, "cf_dragonscale_tank", 20, ArenaZ), (1, "cf_dragonscale_tank", 24, ArenaZ), (1, "cf_dragonscale_tank", 28, ArenaZ),
      });
      yield return WaitFrames(2 * Fps);
      var agent = Verified.Get<NavMeshSteeringAgent>(UnitsNamed(1, "cf_dragonscale_tank")[0]);
      Assert.AreEqual(5f, agent.MaxSpeed.AsFloat, Tolerance);
    }

    [UnityTest]
    public IEnumerator Ability_MultiAirTarget_HitsThree() {
      yield return StartMatch(units: new[] {
        (1, "cf_thunder_aa", 0, ArenaZ),
        (0, "ff_hawk_gunship", 3, ArenaZ), (0, "ff_hawk_gunship", -3, ArenaZ), (0, "ff_hawk_gunship", 0, ArenaZ + 3), (0, "ff_hawk_gunship", 0, ArenaZ - 3),
      });
      var hawks = UnitsNamed(0, "ff_hawk_gunship");
      yield return WaitFor(() => hawks.Any(h => Health(h) < 450f), 30f, "대공 사격");
      Assert.AreEqual(3, hawks.Count(h => Health(h) < 450f), "동시 3기");
      Assert.AreEqual(390f, hawks.Min(h => Health(h)), Tolerance, "40 x 1.5");
    }

    [UnityTest]
    public IEnumerator Ability_FlameBurst_ExtraBuildingDamage() {
      yield return StartMatch(units: new[] { (1, "cf_flame_squad", -12, 25) });
      var flame = UnitsNamed(1, "cf_flame_squad")[0];
      var engineer = Engineers(0)[0];
      var position = V(-20, 25);
      // 부지만 잡고 공병을 물린다. 부지 체력은 60으로 고정된다.
      yield return Build(0, "_power_plant", position, engineer);
      yield return Send(0, new MoveUnitsCommand { Units = new[] { engineer }, Target = V(-30, -8) });
      var site = BuildingAt(position);
      Assert.AreEqual(60f, BuildingState(site).Health.AsFloat, Tolerance);

      Game.SendCommand(1, new AttackTargetCommand { Units = new[] { flame }, Target = site });
      yield return WaitFor(() => BuildingState(site).Health < 50, 30f, "건물 피해");
      Assert.AreEqual(30f, BuildingState(site).Health.AsFloat, Tolerance, "20 x 건물 0.5 x 화염 3");
    }

    [UnityTest]
    public IEnumerator Ability_MinRange_CannotHitCloseTargets() {
      yield return StartMatch(units: new[] { (1, "cf_longwall_mlrs", 0, ArenaZ), (0, "ff_skyguard_squad", 4, ArenaZ) });
      var target = UnitsNamed(0, "ff_skyguard_squad")[0];
      yield return WaitFrames(8 * Fps);
      Assert.AreEqual(120f, Health(target), Tolerance, "최소 사거리 안");

      yield return Send(0, new MoveUnitsCommand { Units = new[] { target }, Target = V(12, ArenaZ) });
      yield return WaitFor(() => Health(target) < 120f, 30f, "사거리 안으로 나오면 공격");
    }

    [UnityTest]
    public IEnumerator Ability_MilitiaPair_SpawnsTwoFromOneOrder() {
      yield return StartMatch();
      var barracks = V(20, 12);
      yield return BuildAndFinish(1, "_barracks", barracks);
      var site = BuildingAt(barracks);
      var before = UnitsNamed(1, "cf_militia").Count;
      var militia = FactionOf(1).Units.First(u => Verified.FindAsset(u).name == "cf_militia");
      yield return Send(1, new ProduceCommand { Building = site, Unit = militia, Count = 1 });
      yield return WaitFor(() => UnitsNamed(1, "cf_militia").Count > before, 60f, "민병대 생산");
      Assert.AreEqual(before + 2, UnitsNamed(1, "cf_militia").Count, "1회 생산 2명");
    }
  }
}
