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
  /// Task 46 phase A (2026-11-24): west outer waterway on Map4P v6. Barrier gates per sector (NW seat 3, SW seat 0) with a
  /// watchtower and sentry, an owner-only gate (2 s open delay, 10 s passable close), destroyed barrier opens the passage until it is
  /// rebuilt, one alert per attack window, shipyard zones (own coast inside the barrier, canal banks for small vessels only).
  /// </summary>
  public unsafe partial class LocalMatchTests {
    static WaterwaySpec WaySpec => Resources.Load<WaterwaySpec>("FactoryWars/waterway");

    static char CellAt(FPVector3 p) {
      var map = Map4PSpec;
      var c = Mathf.FloorToInt((p.X.AsFloat + 320f) / 8f);
      var r = Mathf.FloorToInt((320f - p.Z.AsFloat) / 8f);
      return r < 0 || c < 0 || r >= 80 || c >= 80 ? 'E' : (char)map.CellGrid[r * 80 + c];
    }

    static SeaGate GateState(int index) {
      foreach (var pair in Verified.GetComponentIterator<SeaGate>()) if (pair.Component.Index == index) return pair.Component;
      Assert.Fail($"sea gate {index}");
      return default;
    }
    static SeaGate* GatePtr(int index) => SeaGateSystem.Gate(Verified, index);
    static bool BarrierUp(int g) => SeaGateSystem.BarrierAlive(Verified, GatePtr(g));
    static void AddStock(int player, ResourceCost c) => FactoryWarsUtil.Player(Verified, player)->Stock += c;
    static int GateOfSeat(int seat) => System.Array.IndexOf(Map4PSpec.SeaGateSeats, seat);
    static int PlayerOfSeat(int seat) => FactoryWarsUtil.PlayerAtSeat(Verified, seat);
    static EntityRef SpawnSeat(int player, string suffix, FPVector3 p) {
      var faction = Verified.FindAsset(FactoryWarsUtil.Player(Verified, player)->Faction);
      var prefix = faction.name.Contains("continental") ? "cf" : "ff";
      return ProductionSystem.SpawnUnitAt(Verified, player, SpecRef($"{prefix}_{suffix}"), p, null);
    }
    static void KillBarrier(int gate) {
      var b = GatePtr(gate)->Barrier;
      Verified.Unsafe.GetPointer<Building>(b)->Health = FP._0;
    }
    static FP BarrierHealth(int gate) {
      var b = GatePtr(gate)->Barrier;
      return b != EntityRef.None && Verified.Unsafe.TryGetPointer<Building>(b, out var bb) ? bb->Health : FP._0;
    }
    /// <summary>Water point of the west waterway at z (cols 0-1 centre line).</summary>
    static FPVector3 Lane(float z, float x = -312f) => Pos(x, z);

    IEnumerator WaterwayMatch(int seed = 4601) {
      yield return StartMatch(Map4PScene, 4, truce: 0, seed: seed);
      yield return WaitFrames(Fps / 2);
      Assert.IsNotNull(SeaGateSystem.Spec(Verified), "Map4P v6 has the waterway (rules.Waterway + MapSpec.SeaGates)");
    }

    [UnityTest, Timeout(600000)]
    public IEnumerator Waterway_LayoutAndRange() {
      yield return WaterwayMatch();
      var map = Map4PSpec;
      Assert.AreEqual(2, map.SeaGates.Length, "phase A: NW and SW sea gates");
      CollectionAssert.AreEquivalent(new[] { 3, 0 }, map.SeaGateSeats);
      for (var r = 0; r < 80; r++) {
        Assert.AreEqual('W', (char)map.CellGrid[r * 80], $"row {r} col 0 is waterway");
        Assert.AreEqual('W', (char)map.CellGrid[r * 80 + 1], $"row {r} col 1 is waterway");
      }
      Assert.AreEqual(1, map.Harbours.Length, "W neutral harbour");
      Assert.GreaterOrEqual(map.Harbours[0].HalfLength.AsFloat * 2f, 24f, "harbour turning water >= 24 m wide");
      var ship = Resources.Load<UnitSpec>("FactoryWars/ff_artillery_ship");
      var report = new List<string>();
      for (var g = 0; g < map.SeaGates.Length; g++) {
        var seat = map.SeaGateSeats[g];
        var gate = map.SeaGates[g];
        var dir = Mathf.Sign(gate.Center.Z.AsFloat); // sector side (+z for NW, -z for SW)
        // Closest point a ship outside the barrier can hold: barrier half thickness + hull radius on the far side.
        var outside = gate.Center.Z.AsFloat - dir * (gate.HalfWidth.AsFloat + ship.ColliderRadius.AsFloat);
        var nearest = float.MaxValue;
        for (var r = 0; r < 80; r++) for (var c = 0; c < 80; c++) {
          var ch = (char)map.CellGrid[r * 80 + c];
          if (ch != 'B' && ch != 'p' && ch != 'v') continue;
          var cz = 320f - 8f * r - 4f;
          var cx = -320f + 8f * c + 4f;
          // Base wedge cells of this seat only (high ground flood of its corner).
          if (Mathf.Sign(cz) != dir || cx > 0f || c <= 2) continue;
          var dx = Mathf.Max(0f, Mathf.Abs(cx - (-312f)) - 4f);
          var dz = Mathf.Max(0f, Mathf.Abs(cz - outside) - 4f);
          nearest = Mathf.Min(nearest, Mathf.Sqrt(dx * dx + dz * dz));
        }
        var hq = FactoryWarsUtil.StartPosition(Verified, Mathf.Max(0, PlayerOfSeat(seat))).ToUnityVector3();
        var hqWater = hq.x - (-304f);
        report.Add($"gate {g} seat {seat}: barrier z {gate.Center.Z.AsFloat:0.0}, nearest base cell from outside {nearest:0.0} m, HQ from water {hqWater:0.0} m, ship range {ship.Range.AsFloat:0}");
        Assert.Greater(nearest, ship.Range.AsFloat, $"a ship outside barrier {g} cannot reach base ground");
        Assert.Greater(hqWater, ship.Range.AsFloat + 3.8f, "HQ is out of artillery range even after a breach");
        var tower = map.SeaGateTowers[g];
        Assert.AreNotEqual('W', CellAt(tower), "watchtower on land");
      }
      Debug.Log("[FW-T46] " + string.Join("\n", report));
    }

    [UnityTest, Timeout(900000)]
    public IEnumerator Waterway_GateCycleAndBarrier() {
      yield return WaterwayMatch();
      var g = GateOfSeat(0);
      var owner = PlayerOfSeat(0);
      var enemy = PlayerOfSeat(1);
      Assert.GreaterOrEqual(owner, 0);
      Assert.GreaterOrEqual(enemy, 0);
      var gate = Map4PSpec.SeaGates[g];
      var z = gate.Center.Z.AsFloat; // SW barrier, sector is south (more negative)
      Assert.AreEqual(owner, (int)GateState(g).Owner, "sector owner owns the gate");
      Assert.IsTrue(BarrierUp(g), "barrier placed at start");
      Assert.AreNotEqual(EntityRef.None, GateState(g).Tower, "watchtower placed at start");
      Assert.IsFalse(GateState(g).RegionOpen, "closed with the barrier alive");

      // Enemy boat in the harbour cannot sail into the sector.
      var raider = SpawnSeat(enemy, "scout_boat", Lane(0f));
      yield return Send(enemy, new MoveUnitsCommand { Units = new[] { raider }, Target = Lane(-300f) });
      yield return WaitFrames(25 * Fps);
      Assert.Greater(At(raider).Z.AsFloat, z, "barrier alive: no enemy path into the sector");
      yield return Send(enemy, new MoveUnitsCommand { Units = new[] { raider }, Target = Lane(z + 16f, -308f) });
      yield return WaitFor(() => At(raider).Z.AsFloat < z + 22f, 60f, "raider waits just outside the barrier");

      // Owner ship leaves the dock basin for the harbour: the gate opens for it (2 s), it passes, then closes over 10 s.
      var own = SpawnSeat(owner, "artillery_ship", Lane(-300f, -308f));
      var states = new List<int>();
      var closingAt = -1;
      var closedAt = -1;
      QuantumEvent.Subscribe<EventSeaGateChanged>(_listener, e => {
        if (e.Gate != g) return;
        states.Add(e.State);
        if (e.State == SeaGateSystem.Closing) closingAt = e.Tick;
        if (e.State == SeaGateSystem.Closed && closingAt >= 0 && closedAt < 0) closedAt = e.Tick;
      });
      yield return Send(owner, new MoveUnitsCommand { Units = new[] { own }, Target = Lane(-10f) });
      yield return WaitFor(() => At(own).Z.AsFloat > z + 20f, 120f, "owner ship passes its own gate");
      CollectionAssert.Contains(states, SeaGateSystem.Opening);
      CollectionAssert.Contains(states, SeaGateSystem.Open);
      yield return WaitFor(() => GateState(g).State == SeaGateSystem.Closing, 60f, "gate starts closing after the owner ship left");
      Assert.IsTrue(GateState(g).RegionOpen, "passage still open while closing");
      // An enemy that slips in during the 10 s close gets in.
      yield return Send(enemy, new MoveUnitsCommand { Units = new[] { raider }, Target = Lane(-300f) });
      yield return WaitFor(() => GateState(g).State == SeaGateSystem.Closed, 60f, "gate closed");
      var seconds = (closedAt - closingAt) / (float)Fps;
      Debug.Log($"[FW-T46] gate close took {seconds:0.0} s; raider z {At(raider).Z.AsFloat:0.0} (barrier z {z:0.0})");
      Assert.AreEqual(WaySpec.CloseSeconds.AsFloat, seconds, 0.6f, "closing takes 10 s");
      Assert.Less(At(raider).Z.AsFloat, z, "the enemy that slipped in during closing is inside the sector");

      // Ground units can damage the barrier.
      var before = BarrierHealth(g);
      var gunner = SpawnSeat(enemy, "at_rocket", Pos(-301f, z + 4f));
      yield return Send(enemy, new AttackTargetCommand { Units = new[] { gunner }, Target = GateState(g).Barrier });
      _rejected.Clear();
      yield return WaitFor(() => BarrierHealth(g) < before, 40f, $"a ground unit on the coast damages the barrier (rejected: {string.Join(",", _rejected.Select(r => r.Reason))})");

      // Destroyed barrier opens the passage the same frame; rebuilding closes it again.
      KillBarrier(g);
      var killFrame = Verified.Number;
      yield return WaitFor(() => GateState(g).RegionOpen && GateState(g).Barrier == EntityRef.None, 10f, "destroyed barrier opens the passage");
      Assert.LessOrEqual(Verified.Number - killFrame, 2, "opens on the next simulation update");
      AddStock(owner, new ResourceCost { Iron = 1000, Steel = 500 });
      yield return Send(owner, new RebuildSeaBarrierCommand { Gate = g });
      Assert.AreNotEqual(EntityRef.None, GateState(g).Barrier, "rebuild started");
      Assert.IsTrue(GateState(g).RegionOpen, "open until the rebuild finishes");
      var build = Verified.FindAsset(WaySpec.Barrier).BuildTimeSeconds.AsFloat;
      yield return WaitFor(() => GateState(g).RegionOpen == false, build * 1.5f / 4f + 30f, "rebuilt barrier closes the passage");
    }

    [UnityTest, Timeout(600000)]
    public IEnumerator Waterway_AlertOncePerWindow() {
      yield return WaterwayMatch();
      var g = GateOfSeat(3);
      var owner = PlayerOfSeat(3);
      var enemy = PlayerOfSeat(2);
      var alerts = new List<EventSeaBarrierAlert>();
      QuantumEvent.Subscribe<EventSeaBarrierAlert>(_listener, e => alerts.Add(e));
      var z = Map4PSpec.SeaGates[g].Center.Z.AsFloat;
      var ship = SpawnSeat(enemy, "artillery_ship", Lane(z - 18f));
      yield return Send(enemy, new AttackTargetCommand { Units = new[] { ship }, Target = GateState(g).Barrier });
      yield return WaitFor(() => alerts.Count > 0, 60f, "first hit raises the alert");
      yield return WaitFrames(20 * Fps);
      Assert.AreEqual(1, alerts.Count, "continuous attack: exactly one alert in the window");
      Assert.AreEqual(owner, (int)alerts[0].Player);
      Assert.Less(BarrierHealth(g).AsFloat, Verified.FindAsset(WaySpec.Barrier).MaxHealth.AsFloat, "the barrier took damage");
    }

    [UnityTest, Timeout(600000)]
    public IEnumerator Waterway_ShipyardZones() {
      yield return WaterwayMatch();
      var map = Map4PSpec;
      var owner = PlayerOfSeat(0);
      var other = PlayerOfSeat(3);
      var faction = FactionOf(owner);
      var yardRef = faction.Buildings.First(b => Verified.FindAsset(b).name.EndsWith("_shipyard"));
      var yard = Verified.FindAsset(yardRef);
      var own = System.Array.FindIndex(map.ShipyardZoneSeats, s => s == 0);
      Assert.GreaterOrEqual(own, 0, "seat 0 has a coast zone");
      var zone = map.ShipyardZones[own];
      FPVector3 found = default;
      var ok = false;
      for (var t = -0.9f; t <= 0.9f && ok == false; t += 0.1f) {
        var p = zone.Center + Pos(0f, t * zone.HalfWidth.AsFloat);
        if (ProductionSystem.CanPlace(Verified, yard, p, owner)) { found = p; ok = true; }
      }
      Assert.IsTrue(ok, "a shipyard fits on the owner's coast inside its barrier");
      Assert.IsFalse(ProductionSystem.CanPlace(Verified, yard, found, other), "another player cannot use this sector coast");
      var inland = FactoryWarsUtil.StartPosition(Verified, owner) + Pos(10f, 10f);
      Assert.AreEqual(RejectReason.ShipyardZone, ProductionSystem.PlacementReason(Verified, yard, Pos(-300f, 0f), owner), "harbour quay is not a shipyard zone");
      Assert.IsFalse(ProductionSystem.CanPlace(Verified, yard, inland, owner));
      // Canal bank: small vessels only.
      var canal = System.Array.FindIndex(map.ShipyardZoneSmallOnly, s => s);
      Assert.GreaterOrEqual(canal, 0, "canal-bank zones exist");
      var placed = EntityRef.None;
      for (var i = 0; i < map.ShipyardZones.Length && placed == EntityRef.None; i++) {
        if (map.ShipyardZoneSmallOnly[i] == false) continue;
        var c = map.ShipyardZones[i].Center;
        if (ProductionSystem.CanPlace(Verified, yard, c, owner)) placed = ProductionSystem.CreateFinishedBuilding(Verified, owner, yardRef, c);
      }
      Assert.AreNotEqual(EntityRef.None, placed, "a shipyard fits on a canal bank");
      var warship = Verified.FindAsset(yard.Producible.First(u => Verified.FindAsset(u).HullHeight > WaySpec.SmallVesselMaxHull));
      Assert.IsTrue(SeaGateSystem.TooTallForYard(Verified, At(placed), warship), "canal shipyard refuses the warship");
      _rejected.Clear();
      AddStock(owner, new ResourceCost { Iron = 3000, Wood = 3000, Steel = 3000, Power = 3000 });
      yield return Send(owner, new ProduceCommand { Building = placed, Unit = yard.Producible.First(u => Verified.FindAsset(u).HullHeight > WaySpec.SmallVesselMaxHull) });
      Assert.IsTrue(_rejected.Any(r => r.Reason == RejectReason.CanalTooNarrow), "production refused with the canal reason");
      var small = yard.Producible.FirstOrDefault(u => Verified.FindAsset(u).HullHeight <= WaySpec.SmallVesselMaxHull);
      if (small.IsValid) Assert.IsFalse(SeaGateSystem.TooTallForYard(Verified, At(placed), Verified.FindAsset(small)), "small vessels are fine at the canal");
    }

    /// <summary>Sail time from each sector's dock basin to the W harbour (NW vs SW mirror) and the 1.1 x straight-line bound.</summary>
    [UnityTest, Timeout(1200000)]
    public IEnumerator Waterway_SailFairness() {
      yield return WaterwayMatch();
      var rows = new List<string>();
      var times = new List<float>();
      foreach (var seat in new[] { 3, 0 }) {
        var owner = PlayerOfSeat(seat);
        var dir = seat == 3 ? 1f : -1f;
        var from = Lane(dir * 300f, -308f);
        var to = Lane(0f, -308f);
        var ship = SpawnSeat(owner, "artillery_ship", from);
        var spec = Verified.FindAsset(Verified.Get<Unit>(ship).Spec);
        yield return WaitFrames(2);
        var start = Verified.Number;
        from = At(ship);
        yield return Send(owner, new MoveUnitsCommand { Units = new[] { ship }, Target = to });
        var last = At(ship);
        var stall = 0;
        var landed = false;
        yield return WaitFor(() => {
          var p = At(ship);
          if (CellAt(p) != 'W') landed = true;
          stall = (p - last).SqrMagnitude < FP._0_01 ? stall + 1 : 0;
          last = p;
          return FactoryWarsUtil.FlatDistanceSqr(p, to) < 64;
        }, 240f, $"seat {seat} ship reaches the harbour");
        var seconds = (Verified.Number - start - CommandFrames) / (float)Fps;
        var bound = (Mathf.Abs(from.Z.AsFloat - to.Z.AsFloat) - 8f) / spec.MoveSpeed.AsFloat;
        rows.Add($"seat {seat}: {seconds:0.0} s (straight bound {bound:0.0} s x1.1 = {bound * 1.1f:0.0} s), left water {landed}");
        times.Add(seconds);
        Assert.IsFalse(landed, "ship stays on water cells");
        Assert.LessOrEqual(seconds, bound * 1.1f + WaySpec.OpenSeconds.AsFloat, $"seat {seat}: sail time within 1.1 x distance/speed (+ gate open delay)");
        Verified.Destroy(ship);
      }
      var spread = Mathf.Abs(times[0] - times[1]) / times.Average() * 100f;
      rows.Add($"NW vs SW spread {spread:0.0}% (limit 5%)");
      Debug.Log("[FW-T46] " + string.Join("\n", rows));
      System.IO.Directory.CreateDirectory(T46Root());
      System.IO.File.WriteAllLines(System.IO.Path.Combine(T46Root(), "sail_fairness.txt"), rows);
      Assert.LessOrEqual(spread, 5f);
    }

    static string T46Root() => System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../screenshots/t46"));

    /// <summary>Two runs with the same seed and commands (gate cycle + barrier break) end on the same checksum.</summary>
    [UnityTest, Timeout(2400000)]
    public IEnumerator Waterway_Deterministic() {
      var sums = new ulong[2];
      for (var run = 0; run < 2; run++) {
        FixedStep = true;
        Restores.Add(() => FixedStep = false);
        yield return WaterwayMatch(4602);
        var g = GateOfSeat(0);
        var owner = PlayerOfSeat(0);
        var enemy = PlayerOfSeat(1);
        var own = SpawnSeat(owner, "scout_boat", Lane(-300f, -308f));
        var raider = SpawnSeat(enemy, "artillery_ship", Lane(-60f));
        yield return Send(owner, new MoveUnitsCommand { Units = new[] { own }, Target = Lane(0f) });
        yield return Send(enemy, new AttackTargetCommand { Units = new[] { raider }, Target = GateState(g).Barrier });
        var stop = 180 * Fps;
        var sum = 0UL;
        QuantumCallback.Subscribe<CallbackSimulateFinished>(_listener, e => {
          if (e.Frame.Number != stop || sum != 0UL) return;
          sum = e.Frame.CalculateChecksum();
        });
        yield return WaitFor(() => sum != 0UL, 900f, "180 s checksum");
        sums[run] = sum;
        Debug.Log($"[FW-T46] run {run}: checksum {sum}, gate {g} barrier {(GateState(g).Barrier == EntityRef.None ? "destroyed" : "alive")}");
        TearDown();
        yield return null;
      }
      Assert.AreEqual(sums[0], sums[1], "same seed and inputs give the same waterway state");
    }

    /// <summary>Task 46 phase A captures: full map, west sector, barrier + watchtower, harbour with a truck, canal shipyard tooltip.</summary>
    [UnityTest, Explicit, Timeout(1800000)]
    public IEnumerator Waterway_CaptureT46() {
      var iteration = System.Environment.GetEnvironmentVariable("FW_MAP_ITER");
      iteration = string.IsNullOrEmpty(iteration) ? "r1" : iteration;
      var dir = T46Root();
      System.IO.Directory.CreateDirectory(dir);
      yield return StartMatch(Map4PScene, 4, truce: 0, seed: 4601);
      yield return WaitForHud();
      yield return WaitForBattle();
      var doctrine = GameObject.Find("DoctrinePanel");
      if (doctrine != null) doctrine.GetComponentsInChildren<UnityEngine.UI.Button>(true).First().onClick.Invoke();
      var me = 0;
      var west = WestSite();
      GiveSite(me, west.DropOff);
      SetBuffer(west.Site, new ResourceCost { Iron = 400, Wood = 150 });
      var owner = PlayerOfSeat(0);
      SpawnSeat(owner, "artillery_ship", Lane(-60f));
      SpawnSeat(owner, "scout_boat", Lane(-20f, -300f));
      Time.timeScale = 1f;
      yield return WaitFrames(2 * Fps);
      var camera = Camera.main;
      var rts = Controller().Camera;
      camera.aspect = 16f / 9f;
      var report = new List<string> { $"Task 46 captures {iteration}" };
      IEnumerator Shot(string name, Vector2 focus, float height) {
        rts.Height = height;
        rts.MoveTo(focus);
        rts.Apply();
        yield return WaitFrames(Fps / 2);
        CaptureCameraPng(camera, System.IO.Path.Combine(dir, $"{name}_{iteration}.png"), 1920, 1080);
        var planes = GeometryUtility.CalculateFrustumPlanes(camera);
        var rs = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled && r.gameObject.activeInHierarchy && GeometryUtility.TestPlanesAABB(planes, r.bounds)).ToList();
        long tris = 0;
        foreach (var r in rs) {
          var m = r is SkinnedMeshRenderer sk ? sk.sharedMesh : r.TryGetComponent<MeshFilter>(out var mf) ? mf.sharedMesh : null;
          for (var i = 0; m != null && i < m.subMeshCount; i++) tris += m.GetIndexCount(i) / 3;
        }
        report.Add($"{name} | {height:0}m | draws {rs.Sum(r => r.sharedMaterials.Length)} | tris {tris}");
      }
      var gate = Map4PSpec.SeaGates[GateOfSeat(0)].Center;
      var raider = SpawnSeat(PlayerOfSeat(1), "artillery_ship", Lane(gate.Z.AsFloat + 18f));
      Game.SendCommand(PlayerOfSeat(1), new AttackTargetCommand { Units = new[] { raider }, Target = GateState(GateOfSeat(0)).Barrier });
      yield return Shot("full_map", new Vector2(0f, 0f), 560f);
      yield return Shot("west_sector", new Vector2(-250f, -60f), 190f);
      yield return Shot("barrier_tower", new Vector2(gate.X.AsFloat + 10f, gate.Z.AsFloat), 40f);
      var truck = Yard(west.Site).Truck;
      yield return WaitFor(() => Verified.Exists(truck), 120f, "logistics truck");
      yield return Shot("harbour_truck", new Vector2(-285f, 0f), 60f);
      // Canal shipyard: the warship button is disabled with the canal reason.
      var map = Map4PSpec;
      var yardRef = FactionOf(me).Buildings.First(b => Verified.FindAsset(b).name.EndsWith("_shipyard"));
      var placed = EntityRef.None;
      for (var i = 0; i < map.ShipyardZones.Length && placed == EntityRef.None; i++) {
        if (map.ShipyardZoneSmallOnly[i] == false) continue;
        var c = map.ShipyardZones[i].Center;
        if (ProductionSystem.CanPlace(Verified, Verified.FindAsset(yardRef), c, me)) placed = ProductionSystem.CreateFinishedBuilding(Verified, me, yardRef, c);
      }
      if (placed != EntityRef.None) {
        yield return WaitFrames(Fps / 2);
        Controller().Input.Selection.SelectBuilding(placed);
        Controller().Hud.ShowTooltip(FactoryWars.View.Texts.CanalYard);
        var at = At(placed).ToUnityVector3();
        yield return Shot("canal_shipyard_tooltip", new Vector2(at.x, at.z), 45f);
      } else {
        report.Add("canal_shipyard_tooltip | no canal-bank shipyard spot found");
      }
      var view = Battle().Waterway;
      report.Add($"waterway props {(view != null ? view.Props : -1)}");
      System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, $"perf_{iteration}.txt"), report);
      Debug.Log(string.Join(" | ", report));
    }
  }
}
