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

    /// <summary>Side of a point as quarter turns from the west frame (west 0, south 1, east 2, north 3), like the map generator.</summary>
    static int SideOf(float x, float z) => Mathf.Abs(x) >= Mathf.Abs(z) ? (x < 0f ? 0 : 2) : (z < 0f ? 1 : 3);
    /// <summary>Undo t quarter turns of (x, z) -> (-z, x): back to the west frame.</summary>
    static Vector2 ToWest(float x, float z, int t) {
      for (var i = 0; i < t; i++) (x, z) = (z, -x);
      return new Vector2(x, z);
    }

    [UnityTest, Timeout(600000)]
    public IEnumerator Waterway_LayoutAndRange() {
      yield return WaterwayMatch();
      var map = Map4PSpec;
      Assert.AreEqual(8, map.SeaGates.Length, "phase B: two sea gates per side");
      CollectionAssert.AreEquivalent(new[] { 3, 0, 0, 1, 1, 2, 2, 3 }, map.SeaGateSeats, "each seat guards one gate on each of its two sides");
      for (var i = 0; i < 80; i++) {
        foreach (var (r, c) in new[] { (i, 0), (i, 1), (i, 78), (i, 79), (0, i), (1, i), (78, i), (79, i) }) {
          Assert.AreEqual('W', (char)map.CellGrid[r * 80 + c], $"edge cell ({r},{c}) is waterway");
        }
      }
      Assert.AreEqual(4, map.Harbours.Length, "one neutral harbour per side");
      foreach (var h in map.Harbours) Assert.GreaterOrEqual(Mathf.Min(h.HalfLength.AsFloat, h.HalfWidth.AsFloat) * 2f, 24f, "harbour turning water >= 24 m wide");
      var ship = Resources.Load<UnitSpec>("FactoryWars/ff_artillery_ship");
      var report = new List<string>();
      var nearestBySide = new float[4];
      for (var g = 0; g < map.SeaGates.Length; g++) {
        var seat = map.SeaGateSeats[g];
        var gate = map.SeaGates[g];
        var side = SideOf(gate.Center.X.AsFloat, gate.Center.Z.AsFloat);
        var gw = ToWest(gate.Center.X.AsFloat, gate.Center.Z.AsFloat, side);
        var dir = Mathf.Sign(gw.y);
        // Closest point a ship outside the barrier can hold: barrier half thickness + hull radius on the far side (west frame).
        var outside = gw.y - dir * (gate.HalfWidth.AsFloat + ship.ColliderRadius.AsFloat);
        var nearest = float.MaxValue;
        for (var r = 0; r < 80; r++) for (var c = 0; c < 80; c++) {
          var ch = (char)map.CellGrid[r * 80 + c];
          if (ch != 'B' && ch != 'p' && ch != 'v') continue;
          var cell = ToWest(-320f + 8f * c + 4f, 320f - 8f * r - 4f, side);
          // Base wedge cells of this sector only (west frame: its side of the barrier, away from the coast strip).
          // Excludes the coast sidewalk strip (west-frame col 2, x -300) as the phase A test did (c <= 2).
          if (Mathf.Sign(cell.y) != dir || cell.x > 0f || cell.x < -296f) continue;
          var dx = Mathf.Max(0f, Mathf.Abs(cell.x - (-312f)) - 4f);
          var dz = Mathf.Max(0f, Mathf.Abs(cell.y - outside) - 4f);
          nearest = Mathf.Min(nearest, Mathf.Sqrt(dx * dx + dz * dz));
        }
        var hq = FactoryWarsUtil.StartPosition(Verified, Mathf.Max(0, PlayerOfSeat(seat))).ToUnityVector3();
        var hqWest = ToWest(hq.x, hq.z, side);
        var hqWater = hqWest.x - (-304f);
        report.Add($"gate {g} side {side} seat {seat}: barrier ({gate.Center.X.AsFloat:0},{gate.Center.Z.AsFloat:0}), nearest base cell from outside {nearest:0.0} m, HQ from water {hqWater:0.0} m, ship range {ship.Range.AsFloat:0}");
        Assert.Greater(nearest, ship.Range.AsFloat, $"a ship outside barrier {g} cannot reach base ground");
        Assert.Greater(hqWater, ship.Range.AsFloat + 3.8f, "HQ is out of artillery range even after a breach");
        var tower = map.SeaGateTowers[g];
        Assert.AreNotEqual('W', CellAt(tower), "watchtower on land");
        nearestBySide[side] = nearestBySide[side] == 0f ? nearest : Mathf.Min(nearestBySide[side], nearest);
      }
      Debug.Log("[FW-T46] " + string.Join("\n", report));
      System.IO.Directory.CreateDirectory(T46Root());
      System.IO.File.WriteAllLines(System.IO.Path.Combine(T46Root(), "layout_b.txt"), report);
      Assert.Less(nearestBySide.Max() - nearestBySide.Min(), 0.5f, "the four sides are mirror images");
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
      // Phase B: every sector of every side (west frame NW/SW turned t quarter turns: (x, z) -> (-z, x)).
      FPVector3 Turned(float x, float z, int t) {
        for (var i = 0; i < t; i++) (x, z) = (-z, x);
        return Pos(x, z);
      }
      foreach (var (t, westSeat) in Enumerable.Range(0, 4).SelectMany(t => new[] { (t, 3), (t, 0) })) {
        var seat = (westSeat + t) % 4;
        var owner = PlayerOfSeat(seat);
        var dir = westSeat == 3 ? 1f : -1f;
        var from = Turned(-308f, dir * 300f, t);
        var to = Turned(-308f, 0f, t);
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
        var bound = (Mathf.Sqrt(FactoryWarsUtil.FlatDistanceSqr(from, to).AsFloat) - 8f) / spec.MoveSpeed.AsFloat;
        rows.Add($"side {t} seat {seat}: {seconds:0.0} s (straight bound {bound:0.0} s x1.1 = {bound * 1.1f:0.0} s), left water {landed}");
        times.Add(seconds);
        Assert.IsFalse(landed, "ship stays on water cells");
        Assert.LessOrEqual(seconds, bound * 1.1f + WaySpec.OpenSeconds.AsFloat, $"seat {seat}: sail time within 1.1 x distance/speed (+ gate open delay)");
        Verified.Destroy(ship);
      }
      var spread = (times.Max() - times.Min()) / times.Average() * 100f;
      rows.Add($"8 sectors spread {spread:0.0}% (limit 5%)");
      Debug.Log("[FW-T46] " + string.Join("\n", rows));
      System.IO.Directory.CreateDirectory(T46Root());
      System.IO.File.WriteAllLines(System.IO.Path.Combine(T46Root(), "sail_fairness.txt"), rows);
      Assert.LessOrEqual(spread, 5f);
    }

    static void Pin(EntityRef e, FPVector3 p) {
      if (Verified.Exists(e) == false) return;
      var t = Verified.Unsafe.GetPointer<Transform3D>(e);
      if (FactoryWarsUtil.FlatDistanceSqr(t->Position, p) > FP._4) t->Position = p;
    }
    static readonly List<string> _frameLog = new List<string>();
    static string FrameLine(Frame f, EntityRef e) {
      if (f.Exists(e) == false) return "gone";
      var u = f.Get<Unit>(e);
      var p = f.Get<Transform3D>(e).Position;
      var pf = f.Get<NavMeshPathfinder>(e);
      var t = f.Get<LogisticsTruck>(e);
      return $"#{f.Number} ({p.X.AsFloat:0.0},{p.Z.AsFloat:0.0}) order {u.Order} tgt ({u.TargetPosition.X.AsFloat:0},{u.TargetPosition.Z.AsFloat:0}) path {pf.IsActive} ({pf.Target.X.AsFloat:0},{pf.Target.Z.AsFloat:0}) wp {pf.WaypointIndex}/{pf.WaypointCount} st {t.State} stuck {t.StuckTimer.AsFloat:0.00}";
    }
    static string PathReport(EntityRef e) {
      if (Verified.Unsafe.TryGetPointer<NavMeshPathfinder>(e, out var pf) == false) return "no pathfinder";
      var pts = new List<string>();
      for (var i = 0; i < pf->WaypointCount; i++) { var w = pf->GetWaypoint(Verified, i); pts.Add($"({w.X.AsFloat:0},{w.Z.AsFloat:0})"); }
      var v = Verified.Unsafe.TryGetPointer<NavMeshSteeringAgent>(e, out var st) ? $" speed {st->Velocity.Magnitude.AsFloat:0.0}" : "";
      return $"path active {pf->IsActive} target {pf->Target} idx {pf->WaypointIndex}/{pf->WaypointCount}: {string.Join(" ", pts)}{v}";
    }
    static string NearbyReport(FPVector3 p, float radius) {
      var items = new List<string>();
      foreach (var pair in Verified.GetComponentIterator<Transform3D>()) {
        var q = pair.Component.Position;
        var d = Mathf.Sqrt(FactoryWarsUtil.FlatDistanceSqr(q, p).AsFloat);
        if (d > radius || d < 0.01f) continue;
        var name = Verified.Has<Unit>(pair.Entity) ? "unit " + Verified.FindAsset(Verified.Get<Unit>(pair.Entity).Spec).name + " p" + Verified.Get<Unit>(pair.Entity).Owner + " " + Verified.Get<Unit>(pair.Entity).Order
          : Verified.Has<Building>(pair.Entity) ? "bld " + Verified.FindAsset(Verified.Get<Building>(pair.Entity).Spec).name + " p" + Verified.Get<Building>(pair.Entity).Owner : "ent";
        items.Add($"{name} d{d:0.0} ({q.X.AsFloat:0},{q.Z.AsFloat:0})");
      }
      return string.Join("; ", items);
    }
    static FPVector3 BayNow(int site) => LogisticsSystem.BayOf(Verified, LogiSpec, YardPtr(site));
    static int TruckDetours(EntityRef truck) => Verified.Exists(truck) ? Truck(truck).Detours : 0;

    /// <summary>
    /// One delivery cycle at the west yard for the SW seat. Returns (cycle seconds, seconds within <paramref name="range"/> of
    /// <paramref name="probe"/>, closest approach to the west water lane, detours, credited stock, truck positions).
    /// With <paramref name="threat"/> an enemy artillery ship is held at <paramref name="probe"/>.
    /// </summary>
    IEnumerator TruckCycle(bool threat, FPVector3 probe, float range, List<float> result, List<Vector3> track) {
      yield return WaterwayMatch(4611);
      var owner = PlayerOfSeat(0);
      var west = WestSite();
      GiveSite(owner, west.DropOff);
      SetBuffer(west.Site, new ResourceCost { Iron = 200 });
      var ship = EntityRef.None;
      if (threat) {
        ship = SpawnSeat(PlayerOfSeat(1), "artillery_ship", probe);
        yield return WaitFrames(2);
        probe = At(ship);
      }
      yield return WaitFor(() => Verified.Exists(Yard(west.Site).Truck), 10f, "truck leaves the garage");
      var truck = Yard(west.Site).Truck;
      var before = StockOf(owner);
      var start = Verified.Number;
      var exposed = 0;
      var coast = float.MaxValue;
      var lost = false;
      var states = new List<string>();
      _frameLog.Clear();
      QuantumCallback.Subscribe<CallbackSimulateFinished>(_listener, e => {
        if (e.Frame.IsVerified == false) return;
        _frameLog.Add(FrameLine(e.Frame, truck));
        if (_frameLog.Count > 40) _frameLog.RemoveAt(0);
      });
      var lastState = (LogisticsState)255;
      var lastDetours = 0;
      yield return WaitFor(() => {
        if (threat) Pin(ship, probe);
        if (Verified.Exists(truck) == false) { lost = true; return true; }
        var p = At(truck);
        track.Add(p.ToUnityVector3());
        var st = Truck(truck).State;
        var dn = Truck(truck).Detours;
        if (dn != lastDetours) { var w = Truck(truck).Waypoint; states.Add($"wp{dn}({w.X.AsFloat:0},{w.Z.AsFloat:0})@{(Verified.Number - start) / (float)Fps:0}s from({p.X.AsFloat:0},{p.Z.AsFloat:0})"); lastDetours = dn; }
        if (st != lastState) { states.Add($"{st}@{(Verified.Number - start) / (float)Fps:0}s({p.X.AsFloat:0},{p.Z.AsFloat:0})"); lastState = st; }
        Assert.AreNotEqual('W', CellAt(p), "a truck never enters the water");
        if (FactoryWarsUtil.FlatDistanceSqr(p, probe) <= FP.FromFloat_UNSAFE(range * range)) exposed++;
        coast = Mathf.Min(coast, p.X.AsFloat + 312f);
        if (Verified.Number - start > 250 * Fps || (Verified.Number - start > 60 * Fps && track.Count > 0 && track.Max(q => (q - track[0]).magnitude) < 20f)) {
          var tt = Truck(truck);
          var line = $"threat {threat}: truck {tt.State} detour {tt.Detour}/{tt.Detours} waypoint {tt.Waypoint} at {p} stuck {tt.StuckTimer}, yard owner {Yard(west.Site).Owner} buffer {Yard(west.Site).Buffer.Iron}, bay {BayNow(west.Site)}, dock {LogisticsSystem.DockOf(Verified, LogiSpec, owner, west.Site)}, order {Verified.Get<Unit>(truck).Order} target {Verified.Get<Unit>(truck).TargetPosition}";
          line += " | states " + string.Join(" ", states) + " | " + PathReport(truck) + " | near: " + NearbyReport(p, 14f);
          System.IO.Directory.CreateDirectory(T46Root());
          System.IO.File.WriteAllText(System.IO.Path.Combine(T46Root(), "truck_detour_timeout.txt"), line + "\n" + string.Join("\n", _frameLog) + "\n" + string.Join(" ", track.Where((q, i) => i % (5 * Fps) == 0).Select(q => $"({q.x:0},{q.z:0})")));
          Assert.Fail(line);
        }
        return Yard(west.Site).Delivered >= 1;
      }, 1200f, "one delivery cycle");
      Assert.IsFalse(lost, "the truck survives the cycle");
      var t = Truck(truck);
      Assert.AreEqual(t.DeliveryId, t.CreditedId, "the delivery is credited");
      var gained = StockOf(owner).Iron - before.Iron;
      Debug.Log("[FW-T46] truck states " + string.Join(" ", states));
      if (threat) { System.IO.Directory.CreateDirectory(T46Root()); System.IO.File.WriteAllText(System.IO.Path.Combine(T46Root(), "truck_detour_states.txt"), string.Join(" ", states)); }
      result.AddRange(new[] { (Verified.Number - start) / (float)Fps, exposed / (float)Fps, coast, TruckDetours(truck), gained.AsFloat });
      if (threat) {
        // Credit-once still holds: replaying the unload of the credited delivery pays nothing.
        var again = StockOf(owner);
        ReplayUnload(truck, t.Cargo);
        yield return WaitFrames(Fps * 6);
        Assert.AreEqual(again.Iron.AsFloat, StockOf(owner).Iron.AsFloat, 0.01f, "no second credit for the same delivery id");
      }
      TearDown();
      yield return null;
    }

    /// <summary>
    /// Reroute: baseline cycle at the west yard, then the same cycle with an enemy artillery ship held on the water next to the
    /// truck's closest approach to the coast. The truck must detour inland (fewer seconds in ship range), report the extra time %.
    /// </summary>
    [UnityTest, Timeout(2400000)]
    public IEnumerator Waterway_TruckThreatDetour() {
      var ship = Resources.Load<UnitSpec>("FactoryWars/ff_artillery_ship");
      var range = ship.Range.AsFloat;
      var baseTrack = new List<Vector3>();
      var baseline = new List<float>();
      yield return TruckCycle(false, FPVector3.Zero, 0f, baseline, baseTrack);
      // Probe: the water lane abreast of the baseline route point nearest the coast.
      // Real geometry: the closest the route gets to the west water lane (x -308) is reported as the coastal exposure.
      var coastGap = baseTrack.Min(p => p.x + 308f);
      var coastExposed = coastGap <= range;
      // Mechanism probe: an enemy armed ship held 20 m west of the outbound route's westmost point (a ship on a coastal
      // waterway beside the road; phase B adds such roads). The truck must leave the leg for an inland waypoint.
      var dockU = baseTrack[0];
      var bayU = baseTrack.OrderBy(p => p.z).Last();
      var nearest = baseTrack.Where(p => (p - dockU).magnitude > 50f && (p - bayU).magnitude > 50f).OrderBy(p => p.x).First();
      var probe = Pos(nearest.x - 20f, nearest.z);
      var baseExposed = baseTrack.Count(p => (p - probe.ToUnityVector3()).sqrMagnitude <= range * range) / (float)Fps;
      var threatTrack = new List<Vector3>();
      var threatened = new List<float>();
      yield return TruckCycle(true, probe, range, threatened, threatTrack);
      var extra = (threatened[0] - baseline[0]) / baseline[0] * 100f;
      var rows = new List<string> {
        $"real map: SW dock -> W yard route stays {coastGap:0.0} m from the west water lane (ship range {range:0} m) -> exposed to harbour ships: {coastExposed}",
        $"mechanism probe: enemy artillery ship held at ({probe.X.AsFloat:0},{probe.Z.AsFloat:0}), 20 m beside the route; threat radius = range {range:0} m + margin {LogiSpec.ThreatMargin.AsFloat:0} m",
        $"baseline: cycle {baseline[0]:0.0} s, closest to lane {baseline[2]:0.0} m, exposed {baseExposed:0.0} s, credited {baseline[4]:0}",
        $"threat:   cycle {threatened[0]:0.0} s, closest to lane {threatened[2]:0.0} m, exposed {threatened[1]:0.0} s, detours {threatened[3]:0}, credited {threatened[4]:0}",
        $"extra time {extra:0.0}%",
      };
      Debug.Log("[FW-T46] " + string.Join("\n", rows));
      System.IO.Directory.CreateDirectory(T46Root());
      System.IO.File.WriteAllLines(System.IO.Path.Combine(T46Root(), "truck_detour.txt"), rows);
      Assert.Greater(baseExposed, 0f, "baseline route passes within ship range of the probe (otherwise the probe is wrong)");
      Assert.GreaterOrEqual(threatened[3], 1f, "the truck detoured");
      Assert.Less(threatened[1], baseExposed, "fewer seconds in ship range with the detour");
      Assert.AreEqual(baseline[4], threatened[4], 0.01f, "same cargo credited");
    }


    /// <summary>Two artillery ships of one owner sail past each other in the 16 m west waterway: no stall over 2 s, both arrive.</summary>
    [UnityTest, Timeout(900000)]
    public IEnumerator Waterway_ShipsPassEachOther() {
      yield return WaterwayMatch();
      var owner = PlayerOfSeat(0);
      var a = SpawnSeat(owner, "artillery_ship", Lane(-60f));
      var b = SpawnSeat(owner, "artillery_ship", Lane(60f));
      yield return WaitFrames(2);
      var aTo = Lane(60f);
      var bTo = Lane(-60f);
      yield return Send(owner, new MoveUnitsCommand { Units = new[] { a }, Target = aTo });
      yield return Send(owner, new MoveUnitsCommand { Units = new[] { b }, Target = bTo });
      var last = new[] { At(a), At(b) };
      var stall = new int[2];
      var worst = new int[2];
      var done = new bool[2];
      var landed = false;
      yield return WaitFor(() => {
        var ships = new[] { a, b };
        var goals = new[] { aTo, bTo };
        for (var i = 0; i < 2; i++) {
          if (done[i]) continue;
          var p = At(ships[i]);
          if (CellAt(p) != 'W') landed = true;
          stall[i] = (p - last[i]).SqrMagnitude < FP._0_01 * FP._0_01 ? stall[i] + 1 : 0;
          worst[i] = Mathf.Max(worst[i], stall[i]);
          last[i] = p;
          done[i] = FactoryWarsUtil.FlatDistanceSqr(p, goals[i]) < 64;
        }
        return done[0] && done[1];
      }, 180f, "both ships reach the other end");
      var row = $"pass: worst stall A {worst[0] / (float)Fps:0.00} s, B {worst[1] / (float)Fps:0.00} s, left water {landed}";
      Debug.Log("[FW-T46] " + row);
      System.IO.Directory.CreateDirectory(T46Root());
      System.IO.File.WriteAllText(System.IO.Path.Combine(T46Root(), "ships_pass.txt"), row);
      Assert.IsFalse(landed, "ships stay on water");
      Assert.LessOrEqual(worst[0] / (float)Fps, 2f, "ship A never stalls over 2 s");
      Assert.LessOrEqual(worst[1] / (float)Fps, 2f, "ship B never stalls over 2 s");
    }

    static string ShipDiag(EntityRef ship, EntityRef target, int raider) {
      var u = Verified.Get<Unit>(ship);
      var tu = Verified.Get<Unit>(target);
      var d = Mathf.Sqrt(FactoryWarsUtil.FlatDistanceSqr(At(ship), At(target)).AsFloat);
      var ts = Verified.FindAsset(tu.Spec);
      return $"ship order {u.Order} target {u.TargetEntity} cooldown {u.AttackCooldown} at {At(ship)}; truck {target} owner {tu.Owner} at {At(target)} cell '{CellAt(At(target))}' d {d:0.0} hp {tu.Health}; canAttack {DiplomacySystem.CanAttack(Verified, raider, tu.Owner)} visible {FactoryWarsUtil.IsVisibleTo(Verified, raider, target)} truce {FactoryWarsUtil.InTruce(Verified)} truckSpec attackable air {ts.IsAir} naval {ts.IsNaval}";
    }
    static EntityRef SpawnRef(int player, AssetRef<UnitSpec> spec, FPVector3 at) => ProductionSystem.SpawnUnitAt(Verified, player, spec, at, null);
    static FP HealthOf(EntityRef e) {
      if (Verified.Exists(e) == false) return FP._0;
      if (Verified.Unsafe.TryGetPointer<Unit>(e, out var u)) return u->Health;
      return Verified.Unsafe.TryGetPointer<Building>(e, out var b) ? b->Health : FP._0;
    }

    /// <summary>
    /// Harbour targeting with fog on: an enemy artillery ship in the W harbour hits a truck on the yard quay within its 12 m
    /// sight; a truck 18 m away (inside range, outside sight) is not fired on until a spotter of the ship's owner sees it.
    /// </summary>
    [UnityTest, Timeout(900000)]
    public IEnumerator Waterway_HarbourTargetingNeedsSight() {
      UseFog();
      yield return WaterwayMatch();
      var map = Map4PSpec;
      var harbour = map.Harbours[0].Center;
      var victim = PlayerOfSeat(0);
      var raider = PlayerOfSeat(1);
      var ship = SpawnSeat(raider, "artillery_ship", Pos(-302f, harbour.Z.AsFloat));
      yield return WaitFrames(2);
      var at = At(ship);
      var spec = Verified.FindAsset(Verified.Get<Unit>(ship).Spec);
      var truckSpec = LogiSpec.Truck;
      // Quay targets east of the ship (inland): one at 10 m (inside sight), one at 18 m (in range, outside sight).
      // Fog is a 96 x 96 grid (about 6.9 m cells), so "12 m sight" covers 8.5-15 m depending on cell alignment: pick a quay spot
      // the grid shows to the raider (>= min range) and a spot inside range that it does not show.
      var nearOff = Enumerable.Range(0, 13).Select(i => 8.5f + i * 0.5f).FirstOrDefault(o => FogSystem.IsVisible(Verified, raider, at + new FPVector3(FP.FromFloat_UNSAFE(o), 0, 0)) && CellAt(at + new FPVector3(FP.FromFloat_UNSAFE(o), 0, 0)) != 'W');
      var farOff = Enumerable.Range(0, 15).Select(i => 15f + i * 0.5f).FirstOrDefault(o => FogSystem.IsVisible(Verified, raider, at + new FPVector3(FP.FromFloat_UNSAFE(o), 0, 0)) == false);
      Assert.Greater(nearOff, 0f, "a visible quay spot within 8.5-14.5 m");
      Assert.Greater(farOff, 0f, "an unseen spot within 15-22 m");
      var near = SpawnRef(victim, truckSpec, at + new FPVector3(FP.FromFloat_UNSAFE(nearOff), 0, 0));
      yield return WaitFrames(2);
      var nearHp = HealthOf(near);
      var nearPos = At(near);
      Assert.AreNotEqual('W', CellAt(nearPos), "near truck on the quay");
      var waited = Verified.Number;
      yield return WaitFor(() => {
        if (HealthOf(near) < nearHp) return true;
        if (Verified.Number - waited > 20 * Fps) Assert.Fail("quay truck not hit: " + ShipDiag(ship, near, raider));
        return false;
      }, 120f, "the ship hits a truck on the quay inside its sight");
      Verified.Destroy(near);
      var far = SpawnRef(victim, truckSpec, at + new FPVector3(FP.FromFloat_UNSAFE(farOff), 0, 0));
      yield return WaitFrames(2);
      var farPos = At(far);
      var distance = Mathf.Sqrt(FactoryWarsUtil.FlatDistanceSqr(farPos, at).AsFloat);
      Assert.Greater(distance, spec.SightRange.AsFloat, "far truck outside the ship's sight");
      Assert.Less(distance, spec.Range.AsFloat, "far truck inside the ship's range");
      var farHp = HealthOf(far);
      yield return WaitFrames(10 * Fps);
      var unspotted = HealthOf(far);
      Assert.AreEqual(farHp.AsFloat, unspotted.AsFloat, 0.01f, "no fire past 12 m sight without a spotter");
      // A spotter (raider scout boat next to the truck) reveals it → the ship fires.
      var spotter = SpawnSeat(raider, "scout_boat", Pos(-300f, farPos.Z.AsFloat));
      yield return WaitFrames(2);
      Assert.Less(Mathf.Sqrt(FactoryWarsUtil.FlatDistanceSqr(At(spotter), farPos).AsFloat), 18f, "spotter boat sees the far truck");
      yield return WaitFor(() => HealthOf(far) < farHp || Verified.Exists(far) == false, 30f, "spotted truck is fired on");
      var row = $"harbour targeting (fog on): ship ({at.X.AsFloat:0},{at.Z.AsFloat:0}), quay truck {nearOff:0.0} m hit, far {distance:0.0} m (sight {spec.SightRange.AsFloat:0}, range {spec.Range.AsFloat:0}) untouched 10 s unspotted, hit after spotter";
      Debug.Log("[FW-T46] " + row);
      System.IO.Directory.CreateDirectory(T46Root());
      System.IO.File.WriteAllText(System.IO.Path.Combine(T46Root(), "harbour_targeting.txt"), row);
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
      // Phase B: the north harbour with the low culvert to the canal, and a south-side barrier (turned with its side).
      yield return Shot("north_harbour_culvert", new Vector2(10f, 270f), 80f);
      var south = Map4PSpec.SeaGates.First(g => g.Center.Z.AsFloat < -250f).Center;
      yield return Shot("south_barrier", new Vector2(south.X.AsFloat, south.Z.AsFloat + 10f), 45f);
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
