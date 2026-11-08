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

  /// <summary>초반 휴전과 본진 다리.</summary>
  public unsafe partial class LocalMatchTests {
    static bool BarrierOpen() => Verified.Global->BarrierOpen;

    static int TruceEnd() => Verified.Global->TruceEndFrame;

    /// <summary>지금 켜진 영역만 따라 갈 수 있는 삼각형을 모은다.</summary>
    static HashSet<int> ReachableNow(NavMesh navMesh, FPVector3 from) {
      var active = *Verified.NavMeshRegionMask;
      var start = navMesh.FindTriangle2D(from, active, true);
      Assert.GreaterOrEqual(start, 0, $"NavMesh 위 {from}");
      var seen = new HashSet<int> { start };
      var open = new Stack<int>();
      open.Push(start);
      while (open.Count > 0) {
        var neighbors = navMesh.Triangles[open.Pop()].Neighbors;
        for (var i = 0; neighbors != null && i < neighbors.Length; i++) {
          var next = neighbors[i].Neighbor;
          if (next >= 0 && active.Overlaps(navMesh.Triangles[next].Regions) && seen.Add(next)) {
            open.Push(next);
          }
        }
      }
      return seen;
    }

    IEnumerator AssertBridgesAreOnlyExit(string scene, int players) {
      yield return StartMatch(scene, players, truce: 600);
      var spec = Verified.FindAsset<MapSpec>(Verified.Map.UserAsset);
      var navMesh = MapNavMesh();
      Assert.IsTrue(Verified.Map.RegionMap.ContainsKey("Barrier0"), "다리 영역이 구워짐");
      Assert.AreEqual(1 + spec.BaseAreas.Length + spec.Barricades.Length + spec.CityBuildings.Length + spec.CityBuildings.Count(c => c.Topples), Verified.Map.RegionMap.Count, "기본 영역 + 자리별 다리 영역 + 바리케이드 + 도시 건물 + 낙하 구역");
      var mask = NavMeshRegionMask.Default;
      var rules = Verified.FindAsset(Verified.RuntimeConfig.Rules);
      for (var p = 0; p < players; p++) {
        var seat = FactoryWarsUtil.SeatOf(Verified, p);
        var reachable = ReachableNow(navMesh, spec.StartPositions[seat]);
        for (var other = 0; other < players; other++) {
          if (other != p) {
            var otherSeat = FactoryWarsUtil.SeatOf(Verified, other);
            Assert.IsFalse(reachable.Contains(navMesh.FindTriangle2D(spec.StartPositions[otherSeat], mask, true)), $"{p}번에서 {other}번 본진으로 못 감");
          }
        }
        foreach (var node in spec.ResourceNodes) {
          var inside = spec.BaseAreas[seat].Distance(node.Shape.Center) <= FP._0;
          var spot = navMesh.FindTriangle2D(node.Shape.Spot(0, rules), mask, true);
          Assert.AreEqual(inside, reachable.Contains(spot), $"{seat}번 본진에서 {node.Shape.Center}");
        }
      }
      for (var seat = 0; seat < spec.BaseAreas.Length; seat++) {
        if (FactoryWarsUtil.PlayerAtSeat(Verified, seat) < 0) {
          var emptyReachable = ReachableNow(navMesh, spec.StartPositions[seat]);
          Assert.IsTrue(emptyReachable.Contains(navMesh.FindTriangle2D(V(-8, -8), mask, true)), $"빈 본진 {seat}번은 휴전 중에도 중앙과 열려 있음");
        }
      }
    }

    [UnityTest]
    public IEnumerator Truce_BridgesAreOnlyExit_Map2P() {
      yield return AssertBridgesAreOnlyExit(Map4PScene, 2);
    }

    [UnityTest]
    public IEnumerator Truce_BridgesAreOnlyExit_Map4P() {
      yield return AssertBridgesAreOnlyExit(Map4PScene, 4);
    }

    [UnityTest]
    public IEnumerator Truce_ClosedBridgeStopsGroundUnits() {
      yield return StartMatch(Map4PScene, 2, units: new[] { (0, "ff_eagle_tank", -140, -108) }, truce: 120);
      var tank = UnitsNamed(0, "ff_eagle_tank")[0];
      yield return Send(0, new MoveUnitsCommand { Units = new[] { tank }, Target = V(-140, -84) });
      yield return WaitFrames(8 * Fps);
      Assert.Less(Verified.Get<Transform3D>(tank).Position.Z.AsFloat, -99f, "다리를 건너지 못함");
    }

    [UnityTest]
    public IEnumerator Truce_BarrierOpensOnTimeAndUnitsCross() {
      yield return StartMatch(Map4PScene, 2, units: new[] { (0, "ff_eagle_tank", -140, -108) }, truce: 10);
      var opened = new List<int>();
      QuantumEvent.Subscribe<EventBarrierOpened>(_listener, e => opened.Add(e.Frame));
      Assert.IsFalse(BarrierOpen());
      var spec = Verified.FindAsset<MapSpec>(Verified.Map.UserAsset);
      var navMesh = MapNavMesh();
      var start0 = spec.StartPositions[FactoryWarsUtil.SeatOf(Verified, 0)];
      var start1 = spec.StartPositions[FactoryWarsUtil.SeatOf(Verified, 1)];
      Assert.IsFalse(ReachableNow(navMesh, start0).Contains(navMesh.FindTriangle2D(start1, NavMeshRegionMask.Default, true)), "휴전 중에는 경로가 없음");
      yield return WaitFor(() => BarrierOpen(), 30f, "울타리 개방");
      yield return WaitFrames(2);
      Assert.AreEqual(new List<int> { 10 * Fps }, opened, "10초에 이벤트 한 번");
      Assert.AreEqual(10 * Fps, TruceEnd(), "이벤트 프레임이 TruceEndFrame");
      Assert.IsTrue(ReachableNow(navMesh, start0).Contains(navMesh.FindTriangle2D(start1, NavMeshRegionMask.Default, true)), "개방 뒤에는 경로가 있음");

      var tank = UnitsNamed(0, "ff_eagle_tank")[0];
      var target = V(-140, -84);
      yield return Send(0, new MoveUnitsCommand { Units = new[] { tank }, Target = target });
      yield return WaitFor(() => (Verified.Get<Transform3D>(tank).Position - target).Magnitude < 2, 30f, "다리 통과");
    }

    [UnityTest]
    public IEnumerator Truce_ZeroMeansOpenFromStart() {
      yield return StartMatch(Map4PScene, 2, truce: 0);
      Assert.IsTrue(BarrierOpen());
      var spec = Verified.FindAsset<MapSpec>(Verified.Map.UserAsset);
      var navMesh = MapNavMesh();
      var start0 = spec.StartPositions[FactoryWarsUtil.SeatOf(Verified, 0)];
      var start1 = spec.StartPositions[FactoryWarsUtil.SeatOf(Verified, 1)];
      Assert.IsTrue(ReachableNow(navMesh, start0).Contains(navMesh.FindTriangle2D(start1, NavMeshRegionMask.Default, true)));
    }

    [UnityTest]
    public IEnumerator Truce_DefaultIsSevenMinutes() {
      yield return StartMatch(Map4PScene, 2, truce: -1);
      Assert.AreEqual(420 * Fps, TruceEnd());
      Assert.IsFalse(BarrierOpen());
    }

    [UnityTest]
    public IEnumerator Truce_BlocksGroundAirAndArtillery() {
      yield return StartMatch(units: new[] {
        (0, "ff_eagle_tank", 0, ArenaZ), (0, "ff_hawk_gunship", 2, ArenaZ), (0, "cf_longwall_mlrs", -8, ArenaZ), (1, "cf_thunder_aa", 5, ArenaZ),
      }, truce: 20);
      var target = UnitsNamed(1, "cf_thunder_aa")[0];
      yield return WaitFrames(3 * Fps);
      Assert.AreEqual(450f, Health(target), Tolerance, "자동 공격 없음");

      foreach (var name in new[] { "ff_eagle_tank", "ff_hawk_gunship", "cf_longwall_mlrs" }) {
        _rejected.Clear();
        var unit = UnitsNamed(0, name).First(e => Verified.Get<Transform3D>(e).Position.Z > 30);
        yield return Send(0, new AttackTargetCommand { Units = new[] { unit }, Target = target });
        AssertRejected(0, ActionKind.Attack, RejectReason.Truce);
      }
      Assert.AreEqual(450f, Health(target), Tolerance, "명령해도 공격 없음");

      yield return WaitFor(() => Health(target) < 450f, 30f, "휴전이 끝나면 공격");
    }

    [UnityTest]
    public IEnumerator Truce_AllowsDiplomacyNotesAndScouting() {
      yield return StartMatch(players: 3, units: new[] { (0, "ff_hawk_gunship", 0, ArenaZ) }, truce: 300);
      yield return Ally(0, 1);
      Assert.AreEqual(Relation.Allied, DiplomacySystem.GetRelation(Verified, 0, 1), "휴전 중 외교");
      yield return Send(0, new SendNoteCommand { PhraseId = 1, TargetMode = NoteTarget.Player, Receiver = 2 });
      Assert.AreEqual(4650, Funds(0), "제안 300 + 쪽지 50");

      var hawk = UnitsNamed(0, "ff_hawk_gunship")[0];
      var target = V(20, ArenaZ);
      yield return Send(0, new MoveUnitsCommand { Units = new[] { hawk }, Target = target });
      yield return WaitFor(() => FactoryWarsUtil.FlatDistanceSqr(Verified.Get<Transform3D>(hawk).Position, target) < 1, 30f, "정찰 이동");
    }

    [UnityTest]
    public IEnumerator Truce_LimitsGatherAndBuildToOwnBase() {
      yield return StartMatch(Map4PScene, 2, truce: 300);
      var engineer = Engineers(0)[0];
      yield return Send(0, new GatherCommand { Units = new[] { engineer }, Node = NodeAt(V(-148, -76)) });
      AssertRejected(0, ActionKind.Gather, RejectReason.Truce);
      Assert.AreEqual(WorkerTask.Idle, WorkerOf(engineer).Task);
      _rejected.Clear();

      yield return Build(0, "_supply_depot", V(-80, -80), engineer);
      AssertRejected(0, ActionKind.Build, RejectReason.Truce);
      _rejected.Clear();

      yield return Build(0, "_supply_depot", V(-120, -120), engineer);
      Assert.AreEqual(0, _rejected.Count, "본진 안은 허용");
      yield return Send(0, new GatherCommand { Units = new[] { Engineers(0)[1] }, Node = NodeAt(V(-145, -118)) });
      Assert.AreEqual(0, _rejected.Count, "본진 철광산은 허용");
    }

    [UnityTest]
    public IEnumerator Truce_NoBuildOnBridgesOrTheirEnds() {
      yield return StartMatch(Map4PScene, 2, truce: 0);
      var engineer = Engineers(0)[0];
      foreach (var site in new[] { V(-97, -140), V(-102, -140), V(-92, -140) }) {
        _rejected.Clear();
        yield return Build(0, "_supply_depot", site, engineer);
        AssertRejected(0, ActionKind.Build, RejectReason.BlockedLocation);
      }
    }

    [UnityTest]
    public IEnumerator Bot_WaitsForBridgesBeforeAttacking() {
      yield return StartMatch(Map4PScene, 2, bots: new[] { BotDifficulty.Normal }, truce: 60);
      yield return Sim(50);
      Assert.AreEqual(0, StateOf(0).UnitsLost, "휴전 중 피해 없음");
      foreach (var pair in Verified.GetComponentIterator<Unit>()) {
        if (pair.Component.Owner == 1) {
          Assert.AreNotEqual(UnitOrder.AttackMove, pair.Component.Order, "휴전 중 공격 명령 없음");
        }
      }
      var botSeat = FactoryWarsUtil.SeatOf(Verified, 1);
      foreach (var engineer in Engineers(1)) {
        var worker = WorkerOf(engineer);
        if (Verified.TryGet<ResourceNode>(worker.Target, out var node)) {
          Assert.LessOrEqual(Verified.FindAsset<MapSpec>(Verified.Map.UserAsset).BaseAreas[botSeat].Distance(node.Shape.Center).AsFloat, 0f, "본진 노드만 채집");
        }
      }
    }

    [Test]
    public void Truce_CountdownFormat() {
      Assert.AreEqual("전쟁 시작까지 6:12", Texts.TruceCountdown(371.3f));
      Assert.AreEqual("전쟁 시작까지 0:05", Texts.TruceCountdown(5f));
    }

    [UnityTest]
    public IEnumerator Truce_HudCountsDownAndAnnouncesOpening() {
      yield return StartMatch(truce: 6);
      yield return WaitForHud();
      Assert.IsTrue(Controller().Hud.TruceText.StartsWith("전쟁 시작까지 0:0"), Controller().Hud.TruceText);
      yield return WaitFor(() => BarrierOpen(), 30f, "울타리 개방");
      yield return WaitFrames(2);
      Assert.AreEqual(Texts.BarrierAlert, Controller().Hud.AlertText);
      Controller().Hud.Refresh(Controller().Frame);
      Assert.AreEqual("", Controller().Hud.TruceText);
    }

    [UnityTest]
    public IEnumerator Menu_ChoosesTruceLength() {
      yield return OpenMenu();
      var menu = Object.FindAnyObjectByType<MenuController>();
      menu.Show(MenuController.Screen.Options);
      Assert.AreEqual(7, menu.Settings.TruceMinutes, "기본 7분");
      menu.NextTruce();
      Assert.AreEqual(10, menu.Settings.TruceMinutes);
      menu.NextTruce();
      Assert.AreEqual(0, menu.Settings.TruceMinutes);
      menu.NextTruce();
      Assert.AreEqual(5, menu.Settings.TruceMinutes);
      menu.Begin();
      yield return WaitFor(() => Game?.Frames?.Verified != null && Verified.ComponentCount<Building>() >= 2, 30f, "대전 시작");
      Assert.AreEqual(300 * Fps, TruceEnd(), "고른 휴전 시간");
    }
  }
}
