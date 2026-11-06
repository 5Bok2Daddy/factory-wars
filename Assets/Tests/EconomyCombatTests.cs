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

    static int PowerProduced(int player) => Verified.Global->Players.GetPointer(player)->PowerProduced;

    static float ExactFunds(int player) => Verified.Global->Players.GetPointer(player)->Funds.AsFloat;

    static EntityRef NodeAt(FPVector3 position) {
      foreach (var pair in Verified.GetComponentIterator<ResourceNode>()) {
        if (Verified.Get<Transform3D>(pair.Entity).Position == position) {
          return pair.Entity;
        }
      }
      return EntityRef.None;
    }

    static float NodeRemaining(EntityRef node) => Verified.TryGet<ResourceNode>(node, out var value) ? value.RemainingFunds.AsFloat : 0f;

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
    public IEnumerator Gather_CarriesFundsToRefinery() {
      yield return StartMatch(buildings: Plant);
      yield return BuildAndFinish(0, "_refinery", V(-20, 10));
      Assert.AreEqual(4500, Funds(0), "정제소 500");

      var engineer = Engineers(0)[0];
      var node = NodeAt(V(-30, 14));
      yield return Send(0, new GatherCommand { Units = new[] { engineer }, Node = node });
      yield return WaitFor(() => Funds(0) > 4500, 60f, "자금 반입");
      Assert.AreEqual(4550, Funds(0), "한 번에 50");

      yield return WaitFor(() => Funds(0) > 4550, 60f, "반복 채집");
      var carried = WorkerOf(engineer).Carried.AsFloat;
      Assert.AreEqual(5000f, Funds(0) - 4500 + carried + NodeRemaining(node), Tolerance, "자금은 노드에서 옮겨질 뿐이다");
    }

    [UnityTest]
    public IEnumerator Gather_WithoutRefinery_WaitsCarrying() {
      yield return StartMatch(buildings: Plant);
      var engineer = Engineers(0)[0];
      var node = NodeAt(V(-30, 14));
      yield return Send(0, new GatherCommand { Units = new[] { engineer }, Node = node });
      yield return WaitFor(() => WorkerOf(engineer).Carried > FP._0, 60f, "채집");
      yield return WaitFrames(10 * Fps);
      Assert.AreEqual(5000, Funds(0));
      Assert.AreEqual(WorkerTask.Deliver, WorkerOf(engineer).Task);
      Assert.AreEqual(4950f, NodeRemaining(node), Tolerance, "한 번만 채집");

      // 정제소를 지은 뒤 다시 채집을 시키면 들고 있던 자금부터 반입한다.
      yield return BuildAndFinish(0, "_refinery", V(-20, 10));
      Assert.AreEqual(50f, WorkerOf(engineer).Carried.AsFloat, Tolerance, "건설 중에도 자금을 들고 있음");
      yield return Send(0, new GatherCommand { Units = new[] { engineer }, Node = node });
      yield return WaitFor(() => Funds(0) > 4500, 60f, "자금 반입");
      Assert.AreEqual(4550, Funds(0));
      Assert.GreaterOrEqual(NodeRemaining(node), 4900f, "반입 전에 다시 캐지 않음");
    }

    [UnityTest]
    public IEnumerator Gather_DepletedNodeIsRemovedAndWorkerMovesOn() {
      yield return StartMatch(buildings: Plant);
      yield return BuildAndFinish(0, "_refinery", V(-20, -10));
      var engineer = Engineers(0)[0];
      var small = NodeAt(V(-30, -14));
      var large = NodeAt(V(-30, 14));
      yield return Send(0, new GatherCommand { Units = new[] { engineer }, Node = small });

      yield return WaitFor(() => Verified.Exists(small) == false, 60f, "노드 고갈");
      Assert.AreEqual(1, Verified.ComponentCount<ResourceNode>());
      yield return WaitFor(() => Funds(0) >= 4600, 60f, "마지막 자금 반입");
      Assert.AreEqual(4600, Funds(0), "매장량 100");
      yield return WaitFor(() => NodeRemaining(large) < 5000, 60f, "가까운 노드로 이동");
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
      Assert.AreEqual(4700, Funds(0), "부지를 잡을 때 300 차감");
      var site = BuildingAt(position);
      Assert.AreEqual(15f, BuildingState(site).ConstructionRemaining.AsFloat, Tolerance, "도착 전에는 진행 안 됨");
      Assert.AreEqual(60f, BuildingState(site).Health.AsFloat, Tolerance, "부지 체력 10%");

      yield return WaitFor(() => BuildingState(site).ConstructionRemaining < 10, 60f, "건설 시작");
      yield return Send(0, new MoveUnitsCommand { Units = new[] { engineer }, Target = V(-20, 0) });
      var paused = BuildingState(site).ConstructionRemaining;
      yield return WaitFrames(2 * Fps);
      Assert.AreEqual(paused, BuildingState(site).ConstructionRemaining, "공병이 떠나면 멈춤");
      Assert.AreEqual(50, PowerProduced(0), "미완성 건물은 전력 없음");

      yield return Send(0, new RepairCommand { Units = new[] { engineer }, Target = site });
      yield return WaitFor(() => Finished(site), 60f, "건설 재개");
      yield return WaitFrames(CommandFrames);
      Assert.AreEqual(WorkerTask.Idle, WorkerOf(engineer).Task);
      Assert.AreEqual(600f, BuildingState(site).Health.AsFloat, 1f, "완공 체력");
      Assert.AreEqual(150, PowerProduced(0), "발전소 전력");
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
    public IEnumerator Build_InsufficientFundsRejected() {
      yield return StartMatch();
      yield return SpendBelow(0, 300);
      var funds = Funds(0);
      var count = Verified.ComponentCount<Building>();
      yield return Build(0, "_power_plant", V(20, 40));
      AssertRejected(0, ActionKind.Build, RejectReason.InsufficientFunds);
      Assert.AreEqual(funds, Funds(0));
      Assert.AreEqual(count, Verified.ComponentCount<Building>());
    }

    [UnityTest]
    public IEnumerator Build_OnObstacleRejected() {
      yield return StartMatch(Map2PScene, 2);
      yield return Build(0, "_barracks", V(-26, -26));
      AssertRejected(0, ActionKind.Build, RejectReason.BlockedLocation);
      Assert.AreEqual(5000, Funds(0));
      _rejected.Clear();

      yield return Build(0, "_power_plant", V(-30, -60));
      Assert.AreEqual(0, _rejected.Count, "빈 땅은 허용");
      Assert.AreEqual(4700, Funds(0));
    }

    [UnityTest]
    public IEnumerator Build_LockedUntilPrerequisitesFinished() {
      yield return StartMatch();
      var engineer = Engineers(0)[0];
      yield return Build(0, "_barracks", V(-20, 10), engineer);
      AssertRejected(0, ActionKind.Build, RejectReason.Locked);
      Assert.AreEqual(5000, Funds(0), "잠긴 건물은 비용 없음");
      _rejected.Clear();

      // 미완성 발전소는 선행 조건이 아니다.
      yield return Build(0, "_power_plant", V(-20, 25), engineer);
      yield return Build(0, "_barracks", V(-20, 10), engineer);
      AssertRejected(0, ActionKind.Build, RejectReason.Locked);
      _rejected.Clear();

      var plant = BuildingAt(V(-20, 25));
      yield return Send(0, new RepairCommand { Units = new[] { engineer }, Target = plant });
      yield return WaitFor(() => Finished(plant), 60f, "발전소 완공");
      yield return Build(0, "_barracks", V(-20, 10), engineer);
      Assert.AreEqual(0, _rejected.Count, "발전소가 있으면 병영 가능");
    }

    [UnityTest]
    public IEnumerator Build_HeavyWorksNeedsFactoryAndLab() {
      yield return StartMatch(buildings: new[] { (0, "ff_vehicle_factory", -42, 12) });
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
      Assert.AreEqual("power_plant", Needs("_refinery"));
      Assert.AreEqual("power_plant", Needs("_barracks"));
      Assert.AreEqual("barracks", Needs("_research_lab"));
      Assert.AreEqual("barracks", Needs("_vehicle_factory"));
      Assert.AreEqual("barracks", Needs("_defense_tower"));
      Assert.AreEqual("vehicle_factory", Needs("_airfield"));
      Assert.AreEqual("research_lab,vehicle_factory", Needs("_heavy_works"));
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
    public IEnumerator Repair_RestoresHealthForFunds() {
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
      yield return Send(0, new RepairCommand { Units = new[] { engineer }, Target = plant });
      yield return WaitFor(() => BuildingState(plant).Health.AsFloat >= 600f, 60f, "수리 완료");
      yield return WaitFrames(CommandFrames);

      Assert.AreEqual(WorkerTask.Idle, WorkerOf(engineer).Task, "다 고치면 멈춤");
      // 회복 비율 x 건물 비용 300 x 0.5
      var expected = (600f - damaged) / 600f * 300f * 0.5f;
      Assert.AreEqual(expected, funds - ExactFunds(0), 2f, "수리비");
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
      yield return StartMatch(buildings: new[] { (1, "cf_power_plant", 42, 0) });
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
