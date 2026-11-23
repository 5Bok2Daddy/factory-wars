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
  }
}
