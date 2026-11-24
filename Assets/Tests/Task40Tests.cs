namespace FactoryWars.Tests {
  using System.Collections;
  using System.Collections.Generic;
  using System.IO;
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
  /// Task 40 (2026-11-24): PMC shop roster + drone operator (launch, leash, battery, death cascade, determinism, HUD captures).
  /// </summary>
  public unsafe partial class LocalMatchTests {
    static EntityRef SpawnUnitByName(Frame frame, PlayerRef owner, string unitName, FPVector3 pos) {
      var spec = Resources.Load<UnitSpec>($"FactoryWars/{unitName}");
      Assert.IsNotNull(spec, $"UnitSpec '{unitName}' must exist in Resources/FactoryWars");
      return ProductionSystem.SpawnUnitAt(frame, owner, new AssetRef<UnitSpec>(spec.Guid), pos, null);
    }

    static List<EntityRef> OperatorDronesOf(Frame frame, EntityRef op) {
      var list = new List<EntityRef>();
      var filter = frame.Filter<OperatorDrone>();
      while (filter.NextUnsafe(out var e, out var d)) {
        if (d->Operator == op) list.Add(e);
      }
      return list;
    }

    static void MoveEntityTo(Frame frame, EntityRef entity, FPVector3 pos) {
      if (frame.Unsafe.TryGetPointer<Transform3D>(entity, out var t)) t->Position = pos;
    }

    static void KillEntity(Frame frame, EntityRef entity) {
      if (frame.Unsafe.TryGetPointer<Unit>(entity, out var u)) u->Health = FP._0;
      frame.Destroy(entity);
    }

    static void SetBattery(Frame frame, EntityRef drone, FP seconds) {
      if (frame.Unsafe.TryGetPointer<OperatorDrone>(drone, out var d)) d->Battery = seconds;
    }

    static UnitOrder OrderOf(Frame frame, EntityRef entity) {
      return frame.Unsafe.TryGetPointer<Unit>(entity, out var u) ? u->Order : default;
    }

    static bool IsDroneAutonomous(Frame frame, EntityRef entity) => UltimateSystem.IsAutonomous(frame, entity);

    static EntityRef FindTraderEntity(Frame frame) {
      var traders = frame.Filter<NpcTrader>();
      return traders.NextUnsafe(out var traderEntity, out _) ? traderEntity : EntityRef.None;
    }

    static long SimChecksum(Frame frame) {
      unchecked {
        long h = 17;
        h = h * 31 + frame.Number;
        var units = frame.Filter<Transform3D, Unit>();
        while (units.NextUnsafe(out var e, out var t, out var u)) {
          h = h * 31 + e.Index;
          h = h * 31 + (int)u->Owner;
          h = h * 31 + u->Health.RawValue;
          h = h * 31 + t->Position.X.RawValue;
          h = h * 31 + t->Position.Z.RawValue;
        }
        var drones = frame.Filter<OperatorDrone>();
        while (drones.NextUnsafe(out var e, out var d)) {
          h = h * 31 + e.Index;
          h = h * 31 + d->Operator.Index;
          h = h * 31 + d->Battery.RawValue;
        }
        return h;
      }
    }

    [UnityTest, Timeout(600000)]
    public IEnumerator T40_ShopRosterPurchaseOwnedByBuyer() {
      yield return StartMatch(Map4PScene, 2, truce: 0, stock: new ResourceCost { Iron = 5000, Wood = 5000, Steel = 2000, Power = 2000 });
      yield return WaitForHud();
      GrantAgreement(0);

      // Verify VisualCatalog slots exist for both new Task 40 units.
      Assert.IsTrue(VisualCatalog.TryFilled("unit/pmc_drone_operator", out _), "unit/pmc_drone_operator slot must be filled");
      Assert.IsTrue(VisualCatalog.TryFilled("unit/pmc_strike_drone", out _), "unit/pmc_strike_drone slot must be filled");

      var opOffer = MercSpec.Offers.Select((o, i) => (o, i)).First(t => t.o.Unit.IsValid && Verified.FindAsset(t.o.Unit).name == "pmc_drone_operator").i;
      yield return Send(0, new TradeCommand { Offer = MercenaryCamp.OfferShop + opOffer });
      var ops = UnitsNamed(0, "pmc_drone_operator");
      Assert.AreEqual(1, ops.Count, "bought pmc_drone_operator belongs to buyer 0");
      Assert.AreEqual((PlayerRef)0, Verified.Get<Unit>(ops[0]).Owner, "owner matches buyer");

      var tankOffer = MercSpec.Offers.Select((o, i) => (o, i)).First(t => t.o.Unit.IsValid && Verified.FindAsset(t.o.Unit).name == "pmc_tank").i;
      yield return Send(0, new TradeCommand { Offer = MercenaryCamp.OfferShop + tankOffer });
      Assert.AreEqual(1, UnitsNamed(0, "pmc_tank").Count, "bought pmc_tank belongs to buyer 0");
    }

    [UnityTest, Timeout(600000)]
    public IEnumerator T40_DroneOperator_LaunchLeashBatteryDeathCascade() {
      yield return StartMatch(Map4PScene, 2, truce: 0, stock: new ResourceCost { Iron = 5000, Wood = 5000, Steel = 2000, Power = 2000 });
      yield return WaitForHud();
      Time.timeScale = 4f;

      // Spawn operator and an enemy target 12 m apart on open ring road (within 22 m launch range).
      var opPos = new FPVector3(0, 0, -68);
      var enemyPos = new FPVector3(12, 0, -68);
      var op = SpawnUnitByName(Verified, 0, "pmc_drone_operator", opPos);
      var enemy = SpawnUnitByName(Verified, 1, "cf_dragonscale_tank", enemyPos);

      // 1. Launch: operator detects enemy and launches strike drone.
      yield return WaitFor(() => OperatorDronesOf(Verified, op).Count >= 1, 20f, "operator launches strike drone");
      var drone = OperatorDronesOf(Verified, op)[0];
      Assert.IsTrue(IsDroneAutonomous(Verified, drone), "operator strike drone must be autonomous");
      Assert.IsTrue(Verified.FindAsset(Verified.Get<Unit>(drone).Spec).IsAir, "strike drone must be an air unit");

      // 2. Leash: move operator 40 m away (> 26 m leash) -> drone switches to Move back to operator.
      MoveEntityTo(Verified, op, new FPVector3(-40, 0, -68));
      yield return WaitFor(() => OperatorDronesOf(Verified, op).Count == 0 || OrderOf(Verified, drone) == UnitOrder.Move, 10f, "drone returns on leash exceed");
      if (OperatorDronesOf(Verified, op).Count > 0) {
        Assert.AreEqual(UnitOrder.Move, OrderOf(Verified, drone), "drone beyond leash must return to operator");
      }

      // 3. Battery: shorten remaining battery to 0.3 s -> drone crashes when depleted.
      if (OperatorDronesOf(Verified, op).Count > 0) {
        SetBattery(Verified, drone, FP._0_25);
        yield return WaitFor(() => OperatorDronesOf(Verified, op).Count == 0, 10f, "drone crashes on battery expiry");
      }

      // 4. Death cascade: move operator back near enemy, wait for relaunch, kill operator -> drone crashes immediately.
      MoveEntityTo(Verified, op, opPos);
      if (Verified.Exists(enemy) == false) enemy = SpawnUnitByName(Verified, 1, "cf_dragonscale_tank", enemyPos);
      yield return WaitFor(() => OperatorDronesOf(Verified, op).Count >= 1, 25f, "operator relaunches drone after cooldown");
      KillEntity(Verified, op);
      yield return WaitFor(() => OperatorDronesOf(Verified, op).Count == 0, 5f, "drone crashes immediately on operator death");
    }

    [UnityTest, Timeout(600000)]
    public IEnumerator T40_DroneOperator_Deterministic() {
      var sums = new ulong[2];
      for (var run = 0; run < 2; run++) {
        FixedStep = true;
        Restores.Add(() => FixedStep = false);
        yield return StartMatch(Map4PScene, 2, truce: 0, stock: new ResourceCost { Iron = 5000, Wood = 5000, Steel = 2000, Power = 2000 });
        var op = SpawnUnitByName(Verified, 0, "pmc_drone_operator", new FPVector3(0, 0, -68));
        SpawnUnitByName(Verified, 1, "cf_dragonscale_tank", new FPVector3(12, 0, -68));
        var stop = Verified.Number + 180;
        var sum = 0UL;
        QuantumCallback.Subscribe<CallbackSimulateFinished>(_listener, e => {
          if (e.Frame.Number != stop || sum != 0UL) return;
          sum = e.Frame.CalculateChecksum();
        });
        yield return WaitFor(() => sum != 0UL, 60f, $"sim 180 frames run {run}");
        Assert.GreaterOrEqual(OperatorDronesOf(Verified, op).Count, 1, $"run {run} launched drone");
        sums[run] = sum;
        TearDown();
        yield return null;
      }
      Assert.AreEqual(sums[0], sums[1], $"Task 40 determinism mismatch: {sums[0]} vs {sums[1]}");
    }

    [UnityTest, Explicit, Timeout(600000)]
    public IEnumerator T40_CaptureShopAndOperator() {
      var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "screenshots", "t40"));
      Directory.CreateDirectory(dir);
      yield return StartMatch(Map4PScene, 2, truce: 0, stock: new ResourceCost { Iron = 5000, Wood = 5000, Steel = 2000, Power = 2000 });
      yield return WaitForHud();
      yield return WaitForBattle();
      var doctrine = GameObject.Find("DoctrinePanel");
      if (doctrine != null) doctrine.GetComponentsInChildren<UnityEngine.UI.Button>(true).First().onClick.Invoke();
      GrantAgreement(0);
      Time.timeScale = 1f;

      var camera = Camera.main;
      var rts = Controller().Camera;
      camera.aspect = 16f / 9f;

      // 1. Trading-post sign close-up at gameplay zoom.
      rts.Height = 28f;
      rts.MoveTo(new Vector2(0f, -6f));
      rts.Apply();
      yield return WaitFrames(Fps / 2);
      CaptureCameraPng(camera, Path.Combine(dir, "t40_trading_post_sign.png"), 1920, 1080);

      // 2. Spawn drone operator + enemy engineer on south ring road and capture active strike drone launch.
      var op = SpawnUnitByName(Verified, 0, "pmc_drone_operator", new FPVector3(-4, 0, -64));
      SpawnUnitByName(Verified, 1, "cf_engineer", new FPVector3(8, 0, -64));
      yield return WaitFor(() => OperatorDronesOf(Verified, op).Count >= 1, 20f, "drone launched for capture");
      rts.Height = 28f;
      rts.MoveTo(new Vector2(2f, -64f));
      rts.Apply();
      yield return WaitFrames(Fps / 2);
      CaptureCameraPng(camera, Path.Combine(dir, "t40_operator_launch.png"), 1920, 1080);

      // 3. Wide camp + south road view showing operator, strike drone, and PMC camp gate together.
      rts.Height = 46f;
      rts.MoveTo(new Vector2(0f, -52f));
      rts.Apply();
      yield return WaitFrames(Fps / 2);
      CaptureCameraPng(camera, Path.Combine(dir, "t40_camp_and_operator.png"), 1920, 1080);
    }
  }
}
