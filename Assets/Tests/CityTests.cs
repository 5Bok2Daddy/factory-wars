namespace FactoryWars.Tests {
  using System.Collections;
  using System.Collections.Generic;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using Assert = NUnit.Framework.Assert;
  using Photon.Deterministic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.TestTools;

  /// <summary>항목 21의 도시 규칙. 시험 맵의 진지 건물(-10, -24)과 쓰러지는 건물(14, -12)로 잰다.</summary>
  public unsafe partial class LocalMatchTests {
    static EntityRef CityAt(int index) {
      foreach (var pair in Verified.GetComponentIterator<CityBuilding>()) {
        if (pair.Component.Index == index) {
          return pair.Entity;
        }
      }
      return EntityRef.None;
    }

    static float CityHealth(int index) => Verified.TryGet<CityBuilding>(CityAt(index), out var city) ? city.Health.AsFloat : 0f;

    static bool Collapsed(int index) => CitySystem.IsCollapsed(Verified, index);

    static EntityRef GarrisonOf(EntityRef unit) => Verified.Get<Unit>(unit).Garrison;

    static bool Walkable(FPVector3 from, FPVector3 to) => Verified.Navigation.LineOfSight(from, to, *Verified.NavMeshRegionMask, MapNavMesh());

    IEnumerator Siege(int player, string unitName, int index, float seconds) {
      var units = UnitsNamed(player, unitName).ToArray();
      yield return Send(player, new AttackTargetCommand { Units = units, Target = CityAt(index) });
      Time.timeScale = 12f;
      yield return WaitFor(() => Collapsed(index), seconds, "붕괴");
      Time.timeScale = TimeScale;
      yield return WaitFrames(2);
    }

    // 도시 건물

    [UnityTest]
    public IEnumerator City_OnlyDesignatedAttackHurtsAndStagesAdvance() {
      yield return StartMatch(units: new[] { (0, "ff_eagle_tank", -10, -15), (0, "ff_eagle_tank", -6, -15) });
      var stages = new List<int>();
      QuantumEvent.Subscribe<EventCityBuildingDamaged>(_listener, e => stages.Add(e.Stage));
      yield return WaitFrames(3 * Fps);
      Assert.AreEqual(1500f, CityHealth(0), "자동 조준 대상이 아님");
      yield return Send(0, new AttackTargetCommand { Units = UnitsNamed(0, "ff_eagle_tank").ToArray(), Target = CityAt(0) });
      Time.timeScale = 12f;
      yield return WaitFor(() => CityHealth(0) < 1500f * 0.6f, 60f, "금 간 단계");
      Time.timeScale = TimeScale;
      Assert.Contains(1, stages, "단계 이벤트");
    }

    [UnityTest]
    public IEnumerator City_CollapseOpensGroundSlowsAndHurtsNeighbors() {
      var tanks = Enumerable.Range(0, 5).Select(i => (0, "ff_eagle_tank", -14 + i * 2, -14)).ToArray();
      yield return StartMatch(units: tanks.Concat(new[] { (1, "cf_militia", -10, -31) }).ToArray());
      var collapsed = new List<int>();
      QuantumEvent.Subscribe<EventCityBuildingCollapsed>(_listener, e => collapsed.Add(e.Index));
      var across = (V(-10, -32), V(-10, -16));
      Assert.IsFalse(Walkable(across.Item1, across.Item2), "건물이 막음");
      var militia = UnitsNamed(1, "cf_militia").ToArray();
      var before = militia.Sum(m => Health(m));
      yield return Siege(0, "ff_eagle_tank", 0, 90f);
      Assert.AreEqual(new List<int> { 0 }, collapsed);
      Assert.IsTrue(Walkable(across.Item1, across.Item2), "붕괴하면 열림");
      Assert.Less(militia.Where(Verified.Exists).Sum(m => Health(m)), before, "주변 붕괴 피해");
      var spec = Verified.FindAsset<MapSpec>(Verified.Map.UserAsset);
      Assert.AreEqual(0, spec.CityAt(V(-10, -24)), "잔해 칸");
      var rules = Verified.FindAsset(Verified.RuntimeConfig.Rules);
      Assert.AreEqual(0.7f, rules.RubbleSpeedMultiplier.AsFloat, 0.001f);
    }

    [UnityTest]
    public IEnumerator City_RubbleSlowsGroundUnits() {
      var tanks = Enumerable.Range(0, 5).Select(i => (0, "ff_eagle_tank", -14 + i * 2, -14)).ToArray();
      yield return StartMatch(units: tanks);
      yield return Siege(0, "ff_eagle_tank", 0, 90f);
      var tank = UnitsNamed(0, "ff_eagle_tank").OrderBy(e => (Verified.Get<Transform3D>(e).Position - V(-10, -14)).SqrMagnitude.AsFloat).First();
      yield return Send(0, new MoveUnitsCommand { Units = new[] { tank }, Target = V(-10, -40) });
      yield return WaitFor(() => Verified.FindAsset<MapSpec>(Verified.Map.UserAsset).CityAt(Verified.Get<Transform3D>(tank).Position) == 0, 30f, "잔해 위");
      yield return WaitFrames(1);
      Assert.AreEqual(3.5f, Verified.Get<NavMeshSteeringAgent>(tank).MaxSpeed.AsFloat, 0.01f, "잔해 위 -30%");
    }

    // 진지

    [UnityTest]
    public IEnumerator Garrison_OnlyInfantryUpToCapacity() {
      var units = new[] { (0, "ff_eagle_tank", -10, -14), (0, "ff_engineer", -12, -14) }
        .Concat(Enumerable.Range(0, 4).Select(i => (0, "ff_ranger_squad", -16 + i * 2, -15))).ToArray();
      yield return StartMatch(units: units);
      var building = CityAt(0);
      var tank = UnitsNamed(0, "ff_eagle_tank")[0];
      var rangers = UnitsNamed(0, "ff_ranger_squad").Where(e => Verified.Get<Transform3D>(e).Position.Z.AsFloat < -10f).ToArray();
      Assert.AreEqual(4, rangers.Length);
      yield return Send(0, new EnterGarrisonCommand { Units = rangers.Append(tank).Append(Engineers(0)[0]).ToArray(), Building = building });
      yield return WaitFor(() => rangers.Count(r => GarrisonOf(r) == building) == 3, 30f, "보병 3분대 진입");
      yield return WaitFrames(2 * Fps);
      Assert.AreEqual(3, rangers.Count(r => GarrisonOf(r) == building), "수용 3");
      Assert.AreEqual(EntityRef.None, GarrisonOf(tank), "차량 불가");
      Assert.AreEqual(0, (int)Verified.Get<CityBuilding>(building).Occupant);
    }

    [UnityTest]
    public IEnumerator Garrison_EnemyCannotJoinAllyCan() {
      var units = new[] { (0, "ff_ranger_squad", -10, -15), (1, "cf_militia", -6, -15), (2, "cf_flame_squad", -14, -15) };
      yield return StartMatch(players: 3, units: units);
      yield return Ally(0, 2);
      var building = CityAt(0);
      var ranger = UnitsNamed(0, "ff_ranger_squad").First(e => Verified.Get<Transform3D>(e).Position.Z.AsFloat < -10f);
      yield return Send(0, new EnterGarrisonCommand { Units = new[] { ranger }, Building = building });
      yield return WaitFor(() => GarrisonOf(ranger) == building, 20f, "진입");
      _rejected.Clear();
      var militia = UnitsNamed(1, "cf_militia").Where(e => Verified.Get<Transform3D>(e).Position.Z.AsFloat < -10f).ToArray();
      yield return Send(1, new EnterGarrisonCommand { Units = militia, Building = building });
      AssertRejected(1, ActionKind.Garrison, RejectReason.Invalid);
      var flame = UnitsNamed(2, "cf_flame_squad").First(e => Verified.Get<Transform3D>(e).Position.Z.AsFloat < -10f);
      yield return Send(2, new EnterGarrisonCommand { Units = new[] { flame }, Building = building });
      yield return WaitFor(() => GarrisonOf(flame) == building, 20f, "동맹 진입");
    }

    [UnityTest]
    public IEnumerator Garrison_InsideIsSafeAndShootsFurther() {
      var units = new[] { (0, "ff_ranger_squad", -10, -15), (1, "cf_militia", -10, -33) };
      yield return StartMatch(units: units);
      var building = CityAt(0);
      var ranger = UnitsNamed(0, "ff_ranger_squad").First(e => Verified.Get<Transform3D>(e).Position.Z.AsFloat < -10f);
      yield return Send(0, new EnterGarrisonCommand { Units = new[] { ranger }, Building = building });
      yield return WaitFor(() => GarrisonOf(ranger) == building, 20f, "진입");
      var health = Health(ranger);
      var militia = UnitsNamed(1, "cf_militia").Where(e => Verified.Get<Transform3D>(e).Position.Z.AsFloat < -30f).ToArray();
      // 사거리 7 × 1.1 + 2 = 9.7. 민병대는 건물 중심에서 9로, 진지 보너스가 없으면 닿지 않는다(7.7).
      yield return WaitFor(() => militia.Any(m => Verified.Exists(m) == false || Health(m) < 80f), 30f, "안에서 사격");
      Assert.AreEqual(health, Health(ranger), "안의 분대는 맞지 않음");
      Assert.IsFalse(militia.Any(m => Verified.Exists(m) && Verified.Get<Unit>(m).TargetEntity == ranger), "진지 안 유닛은 조준 대상이 아님");
    }

    [UnityTest]
    public IEnumerator Garrison_ExitCommandAndCollapseEject() {
      var units = new[] { (0, "ff_ranger_squad", -10, -15) }.Concat(Enumerable.Range(0, 5).Select(i => (1, "cf_dragonscale_tank", -14 + i * 2, -36))).ToArray();
      yield return StartMatch(units: units);
      var building = CityAt(0);
      var ranger = UnitsNamed(0, "ff_ranger_squad").First(e => Verified.Get<Transform3D>(e).Position.Z.AsFloat < -10f);
      yield return Send(0, new EnterGarrisonCommand { Units = new[] { ranger }, Building = building });
      yield return WaitFor(() => GarrisonOf(ranger) == building, 20f, "진입");
      yield return Send(0, new ExitGarrisonCommand { Building = building });
      Assert.AreEqual(EntityRef.None, GarrisonOf(ranger), "나가기");
      Assert.IsTrue(Verified.Has<NavMeshPathfinder>(ranger), "다시 걸을 수 있음");
      yield return Send(0, new EnterGarrisonCommand { Units = new[] { ranger }, Building = building });
      yield return WaitFor(() => GarrisonOf(ranger) == building, 20f, "다시 진입");
      var max = Verified.FindAsset(Verified.Get<Unit>(ranger).Spec).MaxHealth.AsFloat;
      yield return Siege(1, "cf_dragonscale_tank", 0, 120f);
      Assert.IsTrue(Verified.Exists(ranger), "살아서 튀어나옴");
      Assert.AreEqual(EntityRef.None, GarrisonOf(ranger));
      Assert.LessOrEqual(Health(ranger), max * 0.75f + 0.01f, "최대 체력 25% 피해");
    }

    [UnityTest]
    public IEnumerator Garrison_NotOutsideBaseDuringTruce() {
      // 시험 맵에는 본진 구역이 없어 실제 맵으로 잰다.
      yield return StartMatch(Map4PScene, 2, units: new[] { (0, "ff_ranger_squad", -120, -120) }, truce: 300);
      var ranger = UnitsNamed(0, "ff_ranger_squad")[0];
      var spec = Verified.FindAsset<MapSpec>(Verified.Map.UserAsset);
      var index = System.Array.FindIndex(spec.CityBuildings, c => c.Garrison);
      yield return Send(0, new EnterGarrisonCommand { Units = new[] { ranger }, Building = CityAt(index) });
      AssertRejected(0, ActionKind.Garrison, RejectReason.Truce);
    }

    [UnityTest]
    public IEnumerator City_FlameAndAreaMultipliers() {
      yield return StartMatch();
      CheckCityMultipliers();
    }

    static void CheckCityMultipliers() {
      var city = Verified.Unsafe.GetPointer<CityBuilding>(CityAt(0));
      var rules = Verified.FindAsset(Verified.RuntimeConfig.Rules);
      var flameSpec = Resources.Load<UnitSpec>("FactoryWars/cf_flame_squad");
      var mlrs = Resources.Load<UnitSpec>("FactoryWars/cf_longwall_mlrs");
      Assert.AreEqual((CombatSystem.BuildingMultiplier(rules, AttackType.AntiInfantry) * 2).AsFloat, CitySystem.Multiplier(Verified, city, AttackType.AntiInfantry, flameSpec).AsFloat, 0.001f, "화염방사 ×2");
      Assert.AreEqual(1.5f, CitySystem.Multiplier(Verified, city, AttackType.Area, mlrs).AsFloat, 0.001f, "범위 ×1.5");
    }

    // 쓰러지는 건물

    [UnityTest]
    public IEnumerator City_ToppleClosesFallZoneOpensFootprintAndCrushes() {
      var tanks = Enumerable.Range(0, 5).Select(i => (0, "ff_eagle_tank", 6 + i * 2, -4)).ToArray();
      yield return StartMatch(units: tanks.Concat(new[] { (1, "cf_dragonscale_tank", 14, -27) }).ToArray());
      var toppled = new List<int>();
      QuantumEvent.Subscribe<EventCityBuildingToppled>(_listener, e => toppled.Add(e.Index));
      var zoneAcross = (V(7, -25), V(21, -25));
      Assert.IsTrue(Walkable(zoneAcross.Item1, zoneAcross.Item2), "낙하 구역은 처음에 열림");
      var victim = UnitsNamed(1, "cf_dragonscale_tank")[0];
      yield return Send(1, new MoveUnitsCommand { Units = new[] { victim }, Target = V(14, -27) });
      yield return Siege(0, "ff_eagle_tank", 1, 120f);
      Assert.AreEqual(new List<int> { 1 }, toppled);
      Assert.IsFalse(Walkable(zoneAcross.Item1, zoneAcross.Item2), "쓰러지면 막힘");
      Assert.IsTrue(Walkable(V(14, -6), V(14, -15)), "원래 자리는 열림");
      if (Verified.Exists(victim)) {
        var spec = Verified.FindAsset<MapSpec>(Verified.Map.UserAsset);
        Assert.Greater(spec.CityBuildings[1].FallZone.Distance(Verified.Get<Transform3D>(victim).Position).AsFloat, 0f, "밀려남");
        Assert.Less(Health(victim), Verified.FindAsset(Verified.Get<Unit>(victim).Spec).MaxHealth.AsFloat, "피해");
      }
    }

    [UnityTest]
    public IEnumerator City_ToppleIsDeterministic() {
      var results = new string[2];
      for (var run = 0; run < 2; run++) {
        var tanks = Enumerable.Range(0, 5).Select(i => (0, "ff_eagle_tank", 6 + i * 2, -4)).ToArray();
        yield return StartMatch(units: tanks.Concat(new[] { (1, "cf_militia", 14, -24), (1, "cf_militia", 13, -28) }).ToArray(), seed: 31);
        yield return Siege(0, "ff_eagle_tank", 1, 120f);
        results[run] = string.Join(";", UnitsNamed(1, "cf_militia").Select(e => $"{Verified.Get<Transform3D>(e).Position}:{Health(e)}"));
        TearDown();
        yield return null;
      }
      Assert.AreEqual(results[0], results[1]);
    }
  }
}
