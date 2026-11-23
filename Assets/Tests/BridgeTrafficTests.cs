namespace FactoryWars.Tests {
  using System.Collections;
  using System.Collections.Generic;
  using System.Linq;
  using NUnit.Framework;
  using Photon.Deterministic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  /// <summary>
  /// Task 39 card 1 (2026-11-23): Map4P grid v5 (smaller compound), bridges as decks above the water, and drawbridges.
  /// Ships pass under a bridge when HullHeight &lt;= clearance; drawbridges raise for taller ships only when the deck is clear,
  /// block ground traffic while not down, and lower after the ship has gone.
  /// </summary>
  public unsafe partial class LocalMatchTests {
    static AssetRef<UnitSpec> SpecRef(string id) => new AssetRef<UnitSpec>(Resources.Load<UnitSpec>($"FactoryWars/{id}").Guid);

    static MapSpec Map4PSpec => FactoryWarsUtil.MapInfo(Verified);

    static Bridge BridgeState(int index) {
      foreach (var pair in Verified.GetComponentIterator<Bridge>()) {
        if (pair.Component.Index == index) return pair.Component;
      }
      Assert.Fail($"bridge {index} entity");
      return default;
    }

    /// <summary>Water points on each side of a bridge, along the canal (the bridge's short axis).</summary>
    static (FPVector3 A, FPVector3 B) AcrossBridge(MapShape bridge, float gap) {
      var c = bridge.Center;
      var water = FactoryWarsUtil.Rules(Verified).WaterNavMeshName;
      var alongX = bridge.HalfLength < bridge.HalfWidth;
      var half = (alongX ? bridge.HalfLength : bridge.HalfWidth).AsFloat + gap;
      var a = alongX ? c + Pos(half, 0) : c + Pos(0, half);
      var b = alongX ? c - Pos(half, 0) : c - Pos(0, half);
      Assert.IsTrue(FactoryWarsUtil.FindNavPointNear(Verified, water, a, 6, out var wa), $"water north/east of bridge {c}");
      Assert.IsTrue(FactoryWarsUtil.FindNavPointNear(Verified, water, b, 6, out var wb), $"water south/west of bridge {c}");
      return (wa, wb);
    }

    static float AcrossOf(MapShape bridge, FPVector3 p) =>
      bridge.HalfLength < bridge.HalfWidth ? (p - bridge.Center).X.AsFloat : (p - bridge.Center).Z.AsFloat;

    static EntityRef Spawn(string id, FPVector3 p) => ProductionSystem.SpawnUnitAt(Verified, 0, SpecRef(id), p, null);

    [UnityTest, Timeout(600000)]
    public IEnumerator Map4P_v5_CompoundSize() {
      yield return StartMatch(Map4PScene, 2, truce: 0);
      var map = Map4PSpec;
      // Width of the compound (C, Y, p cells) on the two middle rows and columns: 12 cells = 96 m (was 18 cells = 147 m in v4).
      int Run(System.Func<int, char> at) => Enumerable.Range(0, 80).Count(i => "CYp".IndexOf(at(i)) >= 0);
      foreach (var mid in new[] { 39, 40 }) {
        Assert.AreEqual(12, Run(c => (char)map.CellGrid[mid * 80 + c]), $"row {mid} compound width");
        Assert.AreEqual(12, Run(r => (char)map.CellGrid[r * 80 + mid]), $"column {mid} compound height");
      }
      for (var r = 0; r < 80; r++) for (var c = 0; c < 80; c++) {
        if ("CYp".IndexOf((char)map.CellGrid[r * 80 + c]) < 0) continue;
        var p = CellCenter(r, c);
        if (Mathf.Abs(p.X.AsFloat) > 60f || Mathf.Abs(p.Z.AsFloat) > 60f) continue;
        Assert.LessOrEqual(Mathf.Max(Mathf.Abs(p.X.AsFloat), Mathf.Abs(p.Z.AsFloat)), 48f, $"compound cell ({r},{c}) inside 96 m");
      }
      Assert.Less(map.TraderPosition.Magnitude.AsFloat, 0.01f, "trader hub at the compound centre");
      // The trader hub is reachable through each gate.
      foreach (var gate in CompoundEntries()) {
        Assert.AreEqual(Navigation.FindPathResult.Code.Success, GroundPath(Verified, gate * FP._2, gate), $"gate {gate} reachable from the ring road");
      }
    }

    [UnityTest, Timeout(600000)]
    public IEnumerator Bridge_WaterAndBoatsPassUnder() {
      yield return StartMatch(Map4PScene, 2, truce: 0);
      var map = Map4PSpec;
      Assert.AreEqual(map.Bridges.Length, map.BridgeClearances.Length, "clearance per bridge");
      Assert.AreEqual(2, map.Drawbridges.Length, "two drawbridges");
      var rules = FactoryWarsUtil.Rules(Verified);
      var water = Verified.Map.GetNavMesh(rules.WaterNavMeshName);
      var mask = NavigationMask(Verified);
      for (var i = 0; i < map.Bridges.Length; i++) {
        var (a, b) = AcrossBridge(map.Bridges[i], 14);
        Assert.IsTrue(water.Contains(map.Bridges[i].Center, mask, true), $"water continues under bridge {i}");
        Assert.AreEqual(Navigation.FindPathResult.Code.Success, Verified.Navigation.FindPath((FrameThreadSafe)Verified, a, b, water, in mask).Result, $"water path under bridge {i}");
        Assert.IsTrue(OnGroundNavMesh(Verified, map.Bridges[i].Center), $"deck {i} carries ground units");
        Assert.AreEqual(4f, BridgeState(i).Clearance.AsFloat, 0.01f, $"bridge {i} clearance");
      }
      Assert.Greater(Resources.Load<UnitSpec>("FactoryWars/ff_artillery_ship").HullHeight.AsFloat, 4f, "artillery ship is taller than a fixed bridge");
      Assert.LessOrEqual(Resources.Load<UnitSpec>("FactoryWars/ff_scout_boat").HullHeight.AsFloat, 4f, "scout boat fits under a fixed bridge");

      // A scout boat sails under a fixed bridge; an artillery ship waits at it.
      var fixedIndex = Enumerable.Range(0, map.Bridges.Length).First(i => map.Drawbridges.Contains(i) == false);
      var bridge = map.Bridges[fixedIndex];
      var (from, to) = AcrossBridge(bridge, 14);
      var boat = Spawn("ff_scout_boat", from);
      var ship = Spawn("ff_artillery_ship", from + (from - to).Normalized * FP._6);
      yield return Send(0, new MoveUnitsCommand { Units = new[] { boat, ship }, Target = to });
      yield return WaitFor(() => FactoryWarsUtil.FlatDistanceSqr(Verified.Get<Transform3D>(boat).Position, to) < 36, 60f, "scout boat passes under the fixed bridge");
      yield return WaitFrames(10 * Fps);
      var side = Mathf.Sign(AcrossOf(bridge, from));
      var shipAcross = AcrossOf(bridge, Verified.Get<Transform3D>(ship).Position);
      Assert.AreEqual(side, Mathf.Sign(shipAcross), $"artillery ship stays on its side of the fixed bridge ({shipAcross:F1})");
      Assert.GreaterOrEqual(Mathf.Abs(shipAcross), (bridge.HalfLength < bridge.HalfWidth ? bridge.HalfLength : bridge.HalfWidth).AsFloat, "and does not slide under the deck");
    }

    [UnityTest, Timeout(600000)]
    public IEnumerator Drawbridge_OpensForTallShipOnlyWhenDeckClear() {
      yield return StartMatch(Map4PScene, 2, truce: 0);
      var map = Map4PSpec;
      var index = map.Drawbridges[0];
      var bridge = map.Bridges[index];
      var (from, to) = AcrossBridge(bridge, 16);
      // A small boat alone does not raise it.
      var boat = Spawn("ff_scout_boat", from);
      yield return WaitFrames(6 * Fps);
      Assert.AreEqual(BridgeTraffic.Down, BridgeState(index).Lift, "small boat passes under; the deck stays down");
      // A tank parked on the deck keeps it down even with a tall ship waiting.
      var tank = Spawn("ff_eagle_tank", bridge.Center);
      var ship = Spawn("ff_artillery_ship", from);
      yield return WaitFrames(8 * Fps);
      Assert.AreEqual(BridgeTraffic.Down, BridgeState(index).Lift, "occupied deck never raises");
      // Tank leaves; after the deck has been clear for DeckClearSeconds the bridge raises and the ship passes.
      var off = bridge.Center + (bridge.HalfLength >= bridge.HalfWidth ? Pos(bridge.HalfLength.AsFloat + 14f, 0) : Pos(0, bridge.HalfWidth.AsFloat + 14f));
      Assert.IsTrue(FactoryWarsUtil.FindNavPointNear(Verified, FactoryWarsUtil.Rules(Verified).NavMeshName, off, 8, out off), "road beyond the deck");
      yield return Send(0, new MoveUnitsCommand { Units = new[] { tank }, Target = off });
      yield return WaitFor(() => BridgeState(index).Lift != BridgeTraffic.Down, 30f, "raises once the deck is clear");
      Assert.Greater(map.Bridges[index].Distance(Verified.Get<Transform3D>(tank).Position).AsFloat, 1f, "nobody on the deck when it starts to raise");
      yield return WaitFor(() => BridgeState(index).Lift == BridgeTraffic.Up, 30f, "fully raised");
      yield return Send(0, new MoveUnitsCommand { Units = new[] { ship }, Target = to });
      yield return WaitFor(() => FactoryWarsUtil.FlatDistanceSqr(Verified.Get<Transform3D>(ship).Position, to) < 64, 90f, "tall ship passes the raised bridge");
      // Back the way it came, beyond the approach radius (the next fixed bridge north holds this tall ship 33 m out).
      var radius = FactoryWarsUtil.Rules(Verified).DrawbridgeApproachRadius.AsFloat;
      Assert.IsTrue(FactoryWarsUtil.FindNavPointNear(Verified, FactoryWarsUtil.Rules(Verified).WaterNavMeshName, bridge.Center + (from - to).Normalized * FP.FromFloat_UNSAFE(radius + 6f), 6, out var away), "open water away from the bridge");
      yield return Send(0, new MoveUnitsCommand { Units = new[] { ship }, Target = away });
      yield return WaitFrames(25 * Fps);
      Debug.Log($"[FW-TEST] drawbridge away: ship {Verified.Get<Transform3D>(ship).Position} target {away} from {from} to {to} lift {BridgeState(index).Lift} centre {bridge.Center}");
      yield return WaitFor(() => BridgeState(index).Lift == BridgeTraffic.Down, 60f, "lowers after the ship has gone");
    }

    [UnityTest, Timeout(600000)]
    public IEnumerator Drawbridge_BlocksGroundWhileOpen() {
      yield return StartMatch(Map4PScene, 2, truce: 0);
      var map = Map4PSpec;
      var index = map.Drawbridges[0];
      var bridge = map.Bridges[index];
      var (from, _) = AcrossBridge(bridge, 16);
      var ground = FactoryWarsUtil.Rules(Verified).NavMeshName;
      var alongX = bridge.HalfLength >= bridge.HalfWidth;
      var reach = (alongX ? bridge.HalfLength : bridge.HalfWidth).AsFloat + 12f;
      FPVector3 End(float sign) {
        var p = bridge.Center + (alongX ? Pos(sign * reach, 0) : Pos(0, sign * reach));
        Assert.IsTrue(FactoryWarsUtil.FindNavPointNear(Verified, ground, p, 8, out var q), $"road end {sign}");
        return q;
      }
      var west = End(-1);
      var east = End(1);
      var tank = Spawn("ff_eagle_tank", west);
      var ship = Spawn("ff_artillery_ship", from);
      yield return WaitFor(() => BridgeState(index).Lift == BridgeTraffic.Up, 30f, "raised for the waiting ship");
      Assert.IsFalse(OnGroundNavMesh(Verified, bridge.Center), "raised deck is closed to ground units");
      yield return Send(0, new MoveUnitsCommand { Units = new[] { tank }, Target = east });
      var onDeck = 0;
      for (var t = 0; t < 12 * Fps; t++) {
        yield return WaitFrames(1);
        if (bridge.Distance(Verified.Get<Transform3D>(tank).Position) <= FP._0) onDeck++;
        Assert.AreNotEqual(BridgeTraffic.Down, BridgeState(index).Lift, "ship still waiting keeps it raised");
      }
      Assert.AreEqual(0, onDeck, "no ground unit drives onto a raised drawbridge");
      // The ship leaves; the deck lowers and the tank crosses.
      SetEntityPosition(Verified, ship, from + (from - bridge.Center).Normalized * 60);
      yield return WaitFor(() => BridgeState(index).Lift == BridgeTraffic.Down, 30f, "lowers when the ship is gone");
      Assert.IsTrue(OnGroundNavMesh(Verified, bridge.Center), "lowered deck is open again");
      yield return Send(0, new MoveUnitsCommand { Units = new[] { tank }, Target = east });
      yield return WaitFor(() => FactoryWarsUtil.FlatDistanceSqr(Verified.Get<Transform3D>(tank).Position, east) < 25, 60f, "tank crosses the lowered drawbridge");
    }

    [UnityTest, Timeout(900000)]
    public IEnumerator Drawbridge_Deterministic() {
      var checksums = new ulong[2];
      var lifts = new List<int>[2];
      for (var run = 0; run < 2; run++) {
        FixedStep = true;
        Restores.Add(() => FixedStep = false);
        // Map4P v5 drawbridge 0 is the row-24 bridge (centre -16, 124). Units are scenario units so they exist from frame 0,
        // and there is a single command right after the start (a later Send lands on a real-time dependent frame).
        yield return StartMatch(Map4PScene, 2, truce: 0, seed: 3939,
          units: new[] { (0, "ff_artillery_ship", -16, 146), (0, "ff_scout_boat", -10, 146), (0, "ff_eagle_tank", 20, 124) });
        var map = Map4PSpec;
        var index = map.Drawbridges[0];
        var bridge = map.Bridges[index];
        Assert.Less((bridge.Center - Pos(-16, 124)).Magnitude.AsFloat, 0.5f, "drawbridge 0 centre");
        var (_, to) = AcrossBridge(bridge, 16);
        var moves = new List<int>();
        lifts[run] = moves;
        QuantumEvent.Subscribe<EventDrawbridgeMoved>(_listener, e => { if (e.Index == index) moves.Add(e.Lift); });
        var ship = UnitsNamed(0, "ff_artillery_ship").First();
        var boat = UnitsNamed(0, "ff_scout_boat").First();
        var stop = 60 * Fps;
        var sum = 0UL;
        QuantumCallback.Subscribe<CallbackSimulateFinished>(_listener, e => {
          if (e.Frame.Number == stop && sum == 0UL) sum = e.Frame.CalculateChecksum();
        });
        yield return Send(0, new MoveUnitsCommand { Units = new[] { ship, boat }, Target = to });
        yield return WaitFor(() => sum != 0UL, 120f, "60 s checksum");
        checksums[run] = sum;
        Debug.Log($"[FW-TEST] drawbridge run {run}: checksum {sum} lifts {string.Join(",", moves)}");
        Assert.Contains(BridgeTraffic.Up, moves, "the drawbridge raised in this scene");
        TearDown();
        yield return null;
      }
      Assert.AreEqual(checksums[0], checksums[1], "same seed and commands give the same state");
      CollectionAssert.AreEqual(lifts[0], lifts[1], "same lift sequence");
    }
  }
}
