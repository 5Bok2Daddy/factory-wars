namespace FactoryWars.Tests {
  using System.Collections;
  using System.Collections.Generic;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using Photon.Deterministic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  /// <summary>
  /// Task 41 (2026-11-23): expansion-site logistics on Map4P. Resources gathered at the four edge sites fill the site buffer; the
  /// owner's automatic truck hauls them home and they are credited once, on unload. Truck loss drops a crate and respawns the truck,
  /// an owner change aborts the old truck and hands the buffer over, bays/docks stay off the through roads, deterministic.
  /// </summary>
  public unsafe partial class LocalMatchTests {
    static LogisticsSpec LogiSpec => Resources.Load<LogisticsSpec>("FactoryWars/logistics");

    static List<(EntityRef Entity, LogisticsYard Yard)> Yards() {
      var list = new List<(EntityRef, LogisticsYard)>();
      foreach (var pair in Verified.GetComponentIterator<LogisticsYard>()) list.Add((pair.Entity, pair.Component));
      return list.OrderBy(y => y.Item2.Site).ToList();
    }

    static LogisticsYard* YardPtr(int site) {
      foreach (var pair in Verified.GetComponentIterator<LogisticsYard>()) if (pair.Component.Site == site) return Verified.Unsafe.GetPointer<LogisticsYard>(pair.Entity);
      return null;
    }

    /// <summary>West edge site of Map4P and a drop-off position inside it (toward the map edge, away from the truck bay).</summary>
    static (int Site, FPVector3 Position, FPVector3 DropOff) WestSite() {
      var map = Map4P();
      for (var i = 0; i < map.Expansions.Length; i++) {
        var e = map.Expansions[i];
        if (e.Kind != ExpansionKind.Shared || e.Position.X > -100) continue;
        return (i, e.Position, e.Position + new FPVector3(-8, 0, 0));
      }
      Assert.Fail("Map4P has a west edge expansion site");
      return default;
    }

    static EntityRef GiveSite(int player, FPVector3 dropOff) =>
      ProductionSystem.CreateFinishedBuilding(Verified, player, FactionOf(player).Buildings.First(b => Verified.FindAsset(b).name.EndsWith("collection_yard")), dropOff);

    static FP Sum(ResourceCost c) => c.Iron + c.Wood + c.Steel + c.Power;

    IEnumerator SiteWithBuffer(int player, FP iron, FP wood) {
      yield return StartMatch(Map4PScene, 2, truce: 0, seed: 4101);
      yield return WaitFrames(Fps / 2);
      Assert.IsTrue(FactoryWarsUtil.MapInfo(Verified).LogisticsEconomy, "Map4P uses the logistics economy");
      Assert.AreEqual(4, Yards().Count, "one yard per edge expansion site");
      var west = WestSite();
      GiveSite(player, west.DropOff);
      SetBuffer(west.Site, new ResourceCost { Iron = iron, Wood = wood });
    }

    static LogisticsTruck Truck(EntityRef e) => Verified.Get<LogisticsTruck>(e);

    // Pointer access lives in these helpers (C# forbids unsafe code inside iterators).
    static LogisticsYard Yard(int site) => *YardPtr(site);
    static void SetBuffer(int site, ResourceCost c) => YardPtr(site)->Buffer = c;
    static ResourceCost StockOf(int player) => FactoryWarsUtil.Player(Verified, player)->Stock;
    static void ReplayUnload(EntityRef truck, ResourceCost cargo) {
      var t = Verified.Unsafe.GetPointer<LogisticsTruck>(truck);
      t->Cargo = cargo;
      t->State = LogisticsState.Unloading;
      t->Timer = FP._0;
    }
    static void KillTruck(EntityRef truck) {
      Verified.Unsafe.GetPointer<Unit>(truck)->Health = FP._0;
      Verified.Destroy(truck);
    }
    static void DropSiteBuildings(int player, FPVector3 site) {
      var doomed = new List<EntityRef>();
      foreach (var pair in Verified.GetComponentIterator<Building>()) {
        if (pair.Component.Owner == player && FactoryWarsUtil.FlatDistanceSqr(Verified.Get<Transform3D>(pair.Entity).Position, site) < 30 * 30) doomed.Add(pair.Entity);
      }
      foreach (var e in doomed) Verified.Destroy(e);
    }
    static List<string> OffRoadReport() {
      var map = Map4P();
      var spec = LogiSpec;
      var report = new List<string>();
      foreach (var (entity, y) in Yards()) {
        for (var p = 0; p < 4; p++) {
          var yard = YardPtr(y.Site);
          yard->Owner = p;
          var bay = LogisticsSystem.BayOf(Verified, spec, yard).ToUnityVector3();
          var dock = LogisticsSystem.DockOf(Verified, spec, p, y.Site).ToUnityVector3();
          foreach (var (name, at) in new[] { ("bay", bay), ("dock", dock) }) {
            var ch = GridMapView.Cell(map, Mathf.FloorToInt((320f - at.z) / 8f), Mathf.FloorToInt((at.x + 320f) / 8f));
            if (ch == 'R' || ch == 's') report.Add($"site {y.Site} seat {p} {name} at ({at.x:0},{at.z:0}) on road cell '{ch}'");
          }
          yard->Owner = PlayerRef.None;
        }
      }
      return report;
    }
    static void AssertBufferRules(FPVector3 dropOff, int site) {
      var handled = LogisticsSystem.TryDeposit(Verified, 0, dropOff, ResourceKind.Wood, 10, out var accepted);
      Assert.IsTrue(handled, "a drop-off inside the site goes to the yard");
      Assert.IsFalse(accepted, "full buffer: the worker must wait (extraction stops)");
      Assert.AreEqual(LogiSpec.BufferCapacity.AsFloat, Sum(Yard(site).Buffer).AsFloat, 0.01f, "buffer unchanged");
      SetBuffer(site, default);
      handled = LogisticsSystem.TryDeposit(Verified, 0, dropOff, ResourceKind.Wood, 10, out accepted);
      Assert.IsTrue(handled && accepted, "room again: the deposit goes into the buffer");
      Assert.AreEqual(10f, Yard(site).Buffer.Wood.AsFloat, 0.01f);
      handled = LogisticsSystem.TryDeposit(Verified, 1, dropOff, ResourceKind.Wood, 10, out accepted);
      Assert.IsTrue(handled && accepted == false, "another player's workers cannot fill this owner's yard");
      handled = LogisticsSystem.TryDeposit(Verified, 0, FactoryWarsUtil.StartPosition(Verified, 0), ResourceKind.Wood, 10, out accepted);
      Assert.IsFalse(handled, "base drop-offs keep the normal stock credit");
    }


    [UnityTest, Timeout(900000)]
    public IEnumerator Logistics_FullLoopCreditsOnceOnUnload() {
      yield return SiteWithBuffer(0, 150, 50);
      var site = WestSite().Site;
      yield return WaitFor(() => Yard(site).Owner == 0, 5f, "the drop-off secures the yard");
      yield return WaitFor(() => Verified.Exists(Yard(site).Truck), 5f, "a truck leaves the owner's garage");
      var truck = Yard(site).Truck;
      yield return WaitFor(() => Truck(truck).State == LogisticsState.Loading, 240f, "truck reaches the yard bay");
      yield return WaitFor(() => Truck(truck).State == LogisticsState.ToDock, 30f, "truck loads a shipment");
      Assert.AreEqual(200f, Sum(Truck(truck).Cargo).AsFloat, 0.01f, "one shipment = buffer up to ShipmentSize");
      var before = StockOf(0);
      yield return WaitFor(() => Truck(truck).State == LogisticsState.Unloading, 240f, "truck returns to the dock");
      var during = StockOf(0);
      Assert.LessOrEqual((during.Iron + during.Wood - before.Iron - before.Wood).AsFloat, 0.01f, "nothing is credited before the unload finishes");
      yield return WaitFor(() => Truck(truck).State == LogisticsState.ToYard, 30f, "unload finishes");
      var after = StockOf(0);
      Assert.AreEqual(150f, (after.Iron - before.Iron).AsFloat, 1f, "iron credited on unload");
      Assert.AreEqual(50f, (after.Wood - before.Wood).AsFloat, 1f, "wood credited on unload");
      Assert.AreEqual(1, Yard(site).Delivered, "one delivery counted");
      Assert.AreEqual(Truck(truck).DeliveryId, Truck(truck).CreditedId, "this delivery id is credited");
    }

    [UnityTest, Timeout(900000)]
    public IEnumerator Logistics_InterruptedDeliveryNoDuplicate() {
      yield return SiteWithBuffer(0, 200, 0);
      var site = WestSite().Site;
      yield return WaitFor(() => Verified.Exists(Yard(site).Truck), 5f, "truck");
      var truck = Yard(site).Truck;
      yield return WaitFor(() => Truck(truck).State == LogisticsState.ToDock, 300f, "loaded");
      var cargo = Truck(truck).Cargo;
      yield return WaitFor(() => Truck(truck).State == LogisticsState.ToYard && Truck(truck).CreditedId == Truck(truck).DeliveryId, 300f, "first unload");
      var credited = StockOf(0);
      // Retry the same delivery (an interrupted unload replayed): same id, same cargo, unloading again.
      ReplayUnload(truck, cargo);
      yield return WaitFor(() => Truck(truck).State == LogisticsState.ToYard, 30f, "replayed unload finishes");
      var after = StockOf(0);
      Assert.AreEqual(credited.Iron.AsFloat, after.Iron.AsFloat, 0.01f, "a replayed delivery id never pays twice");
      Assert.AreEqual(1, Yard(site).Delivered + 0, "still one credited delivery");
    }

    [UnityTest, Timeout(600000)]
    public IEnumerator Logistics_BufferFullStopsExtraction() {
      yield return SiteWithBuffer(0, LogiSpec.BufferCapacity, 0);
      var west = WestSite();
      yield return WaitFor(() => Yard(west.Site).Owner == 0, 5f, "owner");
      AssertBufferRules(west.DropOff, west.Site);
    }

    [UnityTest, Timeout(900000)]
    public IEnumerator Logistics_TruckLostRespawns() {
      yield return SiteWithBuffer(0, 200, 0);
      var site = WestSite().Site;
      yield return WaitFor(() => Verified.Exists(Yard(site).Truck), 5f, "truck");
      var truck = Yard(site).Truck;
      yield return WaitFor(() => Truck(truck).State == LogisticsState.ToDock, 300f, "loaded");
      yield return WaitFrames(Fps);
      var crates = Verified.ComponentCount<SupplyCrate>();
      KillTruck(truck);
      yield return WaitFor(() => Yard(site).Truck == EntityRef.None, 5f, "the yard notices the lost truck");
      Assert.AreEqual(crates + 1, Verified.ComponentCount<SupplyCrate>(), "part of the cargo drops as a lootable crate");
      var reward = default(ResourceCost);
      foreach (var pair in Verified.GetComponentIterator<SupplyCrate>()) reward = pair.Component.Reward;
      Assert.AreEqual(100f, Sum(reward).AsFloat, 1f, "CrateShare of the cargo");
      Assert.AreEqual(0f, Sum(Yard(site).InTransit).AsFloat, 0.01f, "lost cargo is not credited");
      yield return WaitFor(() => Verified.Exists(Yard(site).Truck), LogiSpec.RespawnSeconds.AsFloat + 15f, "a new truck after RespawnSeconds");
    }

    [UnityTest, Timeout(900000)]
    public IEnumerator Logistics_OwnershipChangeAbortsAndHandsOverBuffer() {
      yield return SiteWithBuffer(0, 0, 0);
      var west = WestSite();
      yield return WaitFor(() => Verified.Exists(Yard(west.Site).Truck), 5f, "player 0 truck");
      var old = Yard(west.Site).Truck;
      yield return WaitFrames(3 * Fps);
      // Player 0 loses the drop-off; player 1 builds one: the yard changes hands, the empty old truck aborts and goes home.
      DropSiteBuildings(0, west.Position);
      SetBuffer(west.Site, new ResourceCost { Wood = 120 });
      GiveSite(1, west.DropOff + new FPVector3(0, 0, 10));
      yield return WaitFor(() => Yard(west.Site).Owner == 1, 5f, "player 1 owns the yard");
      Assert.IsTrue(Verified.Exists(old) == false || Truck(old).State == LogisticsState.Aborting, "the old owner's empty truck aborts");
      Assert.AreEqual(120f, Yard(west.Site).Buffer.Wood.AsFloat, 0.01f, "the buffer stays with the site");
      yield return WaitFor(() => Verified.Exists(Yard(west.Site).Truck) && Verified.Get<Unit>(Yard(west.Site).Truck).Owner == 1, 5f, "player 1's truck is dispatched");
      yield return WaitFor(() => Verified.Exists(old) == false, 300f, "the aborted truck reaches home and is removed");
    }

    [UnityTest, Timeout(600000)]
    public IEnumerator Logistics_NoQueueOnRingRoad() {
      yield return StartMatch(Map4PScene, 4, truce: 0, seed: 4102);
      yield return WaitFrames(Fps / 2);
      var report = OffRoadReport();
      Assert.IsEmpty(report, string.Join("\n", report));
    }

    [UnityTest, Timeout(2400000)]
    public IEnumerator Logistics_Deterministic() {
      var checksums = new ulong[2];
      var delivered = new int[2];
      for (var run = 0; run < 2; run++) {
        FixedStep = true;
        Restores.Add(() => FixedStep = false);
        yield return SiteWithBuffer(0, 200, 120);
        var site = WestSite().Site;
        var stop = 300 * Fps;
        var sum = 0UL;
        var count = 0;
        QuantumCallback.Subscribe<CallbackSimulateFinished>(_listener, e => {
          if (e.Frame.Number != stop || sum != 0UL) return;
          sum = e.Frame.CalculateChecksum();
          foreach (var pair in e.Frame.GetComponentIterator<LogisticsYard>()) if (pair.Component.Site == site) count = pair.Component.Delivered;
        });
        yield return WaitFor(() => sum != 0UL, 900f, "300 s checksum");
        checksums[run] = sum;
        delivered[run] = count;
        Debug.Log($"[FW-TEST] logistics run {run}: checksum {sum} delivered {count}");
        TearDown();
        yield return null;
      }
      Assert.AreEqual(checksums[0], checksums[1], "same seed and inputs give the same logistics state");
      Assert.AreEqual(delivered[0], delivered[1]);
      Assert.Greater(delivered[0], 0, "at least one delivery inside 300 s");
    }

    static string T41Root() => System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../screenshots/t41"));

    /// <summary>Per seat: the edge site it is rotationally paired with (W→seat 0, S→1, E→2, N→3 by SeatMask) and a drop-off in it.</summary>
    static List<(int Player, int Seat, int Site, FPVector3 Position)> SeatSites() {
      var map = Map4P();
      var list = new List<(int, int, int, FPVector3)>();
      for (var player = 0; player < 4; player++) {
        var seat = FactoryWarsUtil.SeatOf(Verified, player);
        var wanted = new[] { 0, 1, 2, 3 }; // seat s takes the site whose mask contains s and (s+3)%4 → W, S, E, N in turn
        for (var i = 0; i < map.Expansions.Length; i++) {
          var e = map.Expansions[i];
          if (e.Kind != ExpansionKind.Shared) continue;
          var prev = (seat + 3) % 4;
          if ((e.SeatMask & (1 << seat)) != 0 && (e.SeatMask & (1 << prev)) != 0) list.Add((player, seat, i, e.Position));
        }
      }
      return list;
    }

    static void GiveSiteOutward(int player, FPVector3 site) {
      var outward = new FPVector3(site.X, 0, site.Z).Normalized;
      GiveSite(player, site + outward * 8);
    }

    static Vector3 BayPos(LogisticsSpec spec, int site) => LogisticsSystem.BayOf(Verified, spec, YardPtr(site)).ToUnityVector3();

    static void FillAll(FP amount) {
      foreach (var pair in Verified.GetComponentIterator<LogisticsYard>()) SetBuffer(pair.Component.Site, new ResourceCost { Iron = amount });
    }

    /// <summary>
    /// Fairness (Task 41 phase 5): every seat runs its own rotationally paired edge site. Measures the first cycle (truck leaves the
    /// garage → first credited unload), the second cycle, the straight dock-bay distance and the resulting throughput.
    /// </summary>
    [UnityTest, Explicit, Timeout(2400000)]
    public IEnumerator Logistics_FairnessTable() {
      // Not a determinism test: run at the normal test time scale (FixedStep made 4 seats x 2 cycles exceed 30 min real time).
      yield return StartMatch(Map4PScene, 4, truce: 0, seed: 4103);
      yield return WaitFrames(Fps / 2);
      var pairs = SeatSites();
      Assert.AreEqual(4, pairs.Count, "each seat has a paired edge site");
      foreach (var (player, seat, site, position) in pairs) GiveSiteOutward(player, position);
      FillAll(600);
      var start = Verified.Number;
      var first = new Dictionary<int, int>();
      var second = new Dictionary<int, int>();
      yield return WaitFor(() => {
        foreach (var (player, seat, site, position) in pairs) {
          var d = Yard(site).Delivered;
          if (d >= 1 && first.ContainsKey(site) == false) first[site] = Verified.Number;
          if (d >= 2 && second.ContainsKey(site) == false) second[site] = Verified.Number;
          if (Sum(Yard(site).Buffer) < 300) SetBuffer(site, new ResourceCost { Iron = 600 });
        }
        if (second.Count == 4) return true;
        if (Verified.Number - start > 900 * Fps) {
          // Diagnose instead of a bare timeout: which seats delivered, and where each truck is.
          var lines = pairs.Select(p => $"seat {p.Seat} site {p.Site}: delivered {Yard(p.Site).Delivered}, truck " +
            (Verified.Exists(Yard(p.Site).Truck) ? $"{Truck(Yard(p.Site).Truck).State} at {Verified.Get<Transform3D>(Yard(p.Site).Truck).Position}" : "none")).ToList();
          System.IO.Directory.CreateDirectory(T41Root());
          System.IO.File.WriteAllLines(System.IO.Path.Combine(T41Root(), "fairness_timeout.txt"), lines);
          Assert.Fail("two deliveries at every seat within 900 s:
" + string.Join("
", lines));
        }
        return false;
      }, 1800f, "two deliveries at every seat");
      var spec = LogiSpec;
      var rows = new List<string> { "seat | site | dock-bay straight m | first cycle s | repeat cycle s | throughput /min" };
      var cycles = new List<float>();
      foreach (var (player, seat, site, position) in pairs.OrderBy(p => p.Seat)) {
        var bay = BayPos(spec, site);
        var dock = LogisticsSystem.DockOf(Verified, spec, player, site).ToUnityVector3();
        var firstS = (first[site] - start) / (float)Fps;
        var repeat = (second[site] - first[site]) / (float)Fps;
        cycles.Add(repeat);
        rows.Add($"{seat} | {site} | {Vector3.Distance(bay, dock):0} | {firstS:0.0} | {repeat:0.0} | {spec.ShipmentSize.AsFloat * 60f / repeat:0}");
      }
      var spread = (cycles.Max() - cycles.Min()) / cycles.Average() * 100f;
      rows.Add($"repeat-cycle spread: {spread:0.0}% (limit 5%)");
      System.IO.Directory.CreateDirectory(T41Root());
      System.IO.File.WriteAllLines(System.IO.Path.Combine(T41Root(), "fairness.txt"), rows);
      Debug.Log(string.Join("\n", rows));
      Assert.LessOrEqual(spread, 5f, string.Join("\n", rows));
    }

    /// <summary>Task 41 captures: west depot (same focus/height as the handoff "before" shot), a base garage + dock, a truck at the bay.</summary>
    [UnityTest, Explicit, Timeout(1800000)]
    public IEnumerator Logistics_CaptureT41() {
      var iteration = System.Environment.GetEnvironmentVariable("FW_MAP_ITER");
      iteration = string.IsNullOrEmpty(iteration) ? "r1" : iteration;
      var dir = T41Root();
      System.IO.Directory.CreateDirectory(dir);
      yield return StartMatch(Map4PScene, 2, truce: 0, seed: 4101);
      yield return WaitForHud();
      yield return WaitForBattle();
      var doctrine = GameObject.Find("DoctrinePanel");
      if (doctrine != null) doctrine.GetComponentsInChildren<UnityEngine.UI.Button>(true).First().onClick.Invoke();
      var west = WestSite();
      GiveSite(0, west.DropOff);
      SetBuffer(west.Site, new ResourceCost { Iron = 400, Wood = 150 });
      Time.timeScale = 1f;
      yield return WaitFrames(2 * Fps);
      var camera = Camera.main;
      var rts = Controller().Camera;
      camera.aspect = 16f / 9f;
      var report = new List<string> { $"Task 41 captures {iteration}" };
      IEnumerator Shot(string name, Vector2 focus, float height) {
        rts.MoveTo(focus);
        rts.Height = height;
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
      var view = Battle().Logistics;
      Assert.IsNotNull(view, "logistics view");
      report.Add($"depot props {view.DepotProps}, docks {view.Docks}");
      yield return Shot("west_depot", new Vector2(-282f, -6f), 70f);
      yield return Shot("west_depot_close", new Vector2(west.Position.X.AsFloat, west.Position.Z.AsFloat), 36f);
      var dock = view.DockOfSeat0;
      yield return Shot("base_dock", new Vector2(dock.x, dock.z), 45f);
      var truck = Yard(west.Site).Truck;
      yield return WaitFor(() => Verified.Exists(truck) && (Truck(truck).State == LogisticsState.Loading || Truck(truck).State == LogisticsState.ToDock), 400f, "truck at the bay");
      var at = Verified.Get<Transform3D>(truck).Position.ToUnityVector3();
      yield return Shot("truck_loading", new Vector2(at.x, at.z), 28f);
      yield return WaitFor(() => Truck(truck).State == LogisticsState.Unloading, 400f, "truck at the dock");
      at = Verified.Get<Transform3D>(truck).Position.ToUnityVector3();
      yield return Shot("truck_unloading", new Vector2(at.x, at.z), 28f);
      yield return Shot("overview", new Vector2(-150f, -110f), 170f);
      System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, $"perf_{iteration}.txt"), report);
      Debug.Log(string.Join("\n", report));
    }
  }
}
