namespace FactoryWars.Tests {
  using System.Collections;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using System.Threading.Tasks;
  using FactoryWars.View;
  using NUnit.Framework;
  using Photon.Deterministic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  public unsafe partial class LocalMatchTests {
    static readonly (int, string, int, int)[] MlrsField = {
      (0, "ff_mlrs_battery", -20, 34),
      (0, "ff_power_plant", -36, 30),
    };
    static readonly ResourceCost MlrsStock = Stock(8000, 4000, 4000, 400);

    [UnityTest]
    public IEnumerator Mlrs_ConstructsWithFoundationAndOccupancy() {
      yield return StartMatch(startingTier: 2, stock: MlrsStock,
        buildings: new[] { (0, "ff_vehicle_factory", -39, 18), (0, "ff_power_plant", -45, 14) });
      yield return WaitForBattle();
      var spec = Verified.FindAsset(BuildingOf(0, "mlrs_battery"));
      var site = Enumerable.Range(-40, 25).SelectMany(x => Enumerable.Range(6, 20).Select(z => V(x, z)))
        .Where(point => ProductionSystem.CanPlace(Verified, spec, point, 0) && GroundOccupancy.CanStand(Verified, point, FP._0_50))
        .OrderBy(point => FactoryWarsUtil.FlatDistanceSqr(point, V(-24, 12))).First();
      var shotCenter = site.ToUnityVector3();
      Assert.IsTrue(ProductionSystem.CanPlace(Verified, spec, site, 0));
      Assert.IsTrue(GroundOccupancy.CanStand(Verified, site, FP._0_50));
      var before = ExactFunds(0);
      yield return Build(0, "mlrs_battery", site);
      var battery = BuildingAt(site);
      Assert.AreNotEqual(EntityRef.None, battery);
      Assert.LessOrEqual(ExactFunds(0), before - spec.Cost.Iron.AsFloat);
      Assert.AreEqual(MlrsStatus.Disabled, Verified.Get<MlrsMagazine>(battery).Status);
      Assert.IsFalse(GroundOccupancy.CanStand(Verified, site, FP._0_50));
      Assert.IsFalse(ProductionSystem.CanPlace(Verified, spec, site, 0));
      yield return WaitFor(() => Battle().Actors.ContainsKey(battery), 6f, "MLRS construction is visible");
      var foundation = ActorOf(battery).View.GetComponentInChildren<FoundationView>();
      Assert.IsNotNull(foundation);
      yield return WaitFor(() => Finished(battery) && ActorOf(battery).Mlrs != null, 60f, "MLRS construction completes");
      Assert.AreEqual(spec.RocketCapacity, Verified.Get<MlrsMagazine>(battery).Ammo);
      Assert.AreSame(foundation, ActorOf(battery).View.GetComponentInChildren<FoundationView>());
      Assert.IsNotNull(ActorOf(battery).View.Model.GetComponent<CivilianActivityPoints>());
      Controller().Input.Selection.SelectBuilding(battery);
      var hud = Object.FindAnyObjectByType<MatchHud>();
      Assert.IsNotNull(hud);
      yield return WaitFor(() => hud.SelectionDetail.Contains("탄약 12/12") && hud.SelectionDetail.Contains("사거리 8–45"), 8f, "MLRS HUD reads the magazine");
      CaptureMlrs("completed_battery", shotCenter + Vector3.up * 0.6f, new Vector3(10f, 13f, 11f));
      CaptureMlrs("foundation", shotCenter + Vector3.up * 0.2f, new Vector3(-10f, 20f, 8f));
      CaptureMlrs("ammunition_storage", shotCenter + new Vector3(0f, 0.8f, 2f), new Vector3(6f, 6.8f, 7f));
      Verified.Destroy(battery);
      yield return WaitFor(() => !Verified.Exists(battery) && foundation == null, 8f, "MLRS footprint is released");
      Assert.IsTrue(GroundOccupancy.CanStand(Verified, site, FP._0_50));
    }

    [UnityTest]
    public IEnumerator Mlrs_SequentialMagazineAreaImpactAndReload() {
      var enemySpec = RaiseMlrsTargetHealth();
      yield return StartMatch(startingTier: 2, stock: MlrsStock, buildings: MlrsField, units: new[] {
        (1, "cf_engineer", 8, 34), (1, "cf_engineer", 10, 36),
        (0, "ff_engineer", 8, 36), (1, "cf_engineer", -15, 34), (1, "cf_engineer", 32, 34),
      });
      var battery = BuildingAt(V(-20, 34));
      var spec = Verified.FindAsset(Verified.Get<Building>(battery).Spec);
      var center = MlrsUnitAt(1, "cf_engineer", V(8, 34));
      var edge = MlrsUnitAt(1, "cf_engineer", V(10, 36));
      var friend = MlrsUnitAt(0, "ff_engineer", V(8, 36));
      var tooNear = MlrsUnitAt(1, "cf_engineer", V(-15, 34));
      var tooFar = MlrsUnitAt(1, "cf_engineer", V(32, 34));
      var friendHealth = Verified.Get<Unit>(friend).Health;
      var launched = new List<MlrsRocket>();
      var impacts = 0;
      QuantumCallback.Subscribe<CallbackSimulateFinished>(_listener, e => {
        if (!e.Frame.IsVerified) return;
        foreach (var pair in e.Frame.GetComponentIterator<MlrsRocket>()) {
          if (pair.Component.LaunchFrame == e.Frame.Number) launched.Add(pair.Component);
        }
      });
      QuantumEvent.Subscribe<EventMlrsImpact>(_listener, _ => impacts++);
      yield return WaitFor(() => launched.Count >= 1, 10f, "MLRS first launch");
      Assert.AreEqual(11, Verified.Get<MlrsMagazine>(battery).Ammo);
      Assert.AreEqual(0, impacts, "Damage must wait for impact");
      var rocket = launched[0];
      Assert.AreEqual(V(8, 34), rocket.Target);
      Assert.IsTrue(MlrsSystem.PositionAt(rocket, (rocket.LaunchFrame + rocket.ImpactFrame) / 2).Y > rocket.Origin.Y);
      yield return WaitFor(() => impacts > 0, 12f, "MLRS rocket impact");
      Assert.IsTrue(Verified.Get<Unit>(center).Health < Verified.Get<Unit>(edge).Health);
      Assert.IsTrue(Verified.Get<Unit>(edge).Health < enemySpec.MaxHealth);
      Assert.AreEqual(friendHealth, Verified.Get<Unit>(friend).Health);
      Assert.AreEqual(enemySpec.MaxHealth, Verified.Get<Unit>(tooNear).Health);
      Assert.AreEqual(enemySpec.MaxHealth, Verified.Get<Unit>(tooFar).Health);
      yield return WaitFor(() => Verified.Get<MlrsMagazine>(battery).Status == MlrsStatus.Reloading, 20f, "MLRS empties magazine");
      var reload = Verified.Get<MlrsMagazine>(battery);
      Assert.AreEqual(12, launched.Count);
      Assert.AreEqual(0, reload.Ammo);
      Assert.AreEqual(25 * Fps, reload.ReloadTotal);
      for (var i = 1; i < launched.Count; ++i) {
        Assert.AreEqual(FactoryWarsUtil.SecondsToFrames(Verified, spec.AttackInterval), launched[i].LaunchFrame - launched[i - 1].LaunchFrame);
      }
      Assert.AreNotEqual(launched[0].Origin, launched[1].Origin);
      yield return WaitFor(() => launched.Count == 13, 30f, "MLRS fires after reloading");
      Assert.AreEqual(FactoryWarsUtil.SecondsToFrames(Verified, spec.RocketReloadSeconds + spec.RocketAimSeconds),
        launched[12].LaunchFrame - launched[11].LaunchFrame);
      Assert.AreEqual(11, Verified.Get<MlrsMagazine>(battery).Ammo);
    }

    [UnityTest]
    public IEnumerator Mlrs_RetargetsAndStopsWhenDestroyed() {
      RaiseMlrsTargetHealth();
      yield return StartMatch(startingTier: 3, truce: 4, stock: MlrsStock, buildings: MlrsField, units: new[] {
        (1, "cf_dragonscale_tank", 10, 34), (1, "cf_engineer", 4, 39),
      });
      var battery = BuildingAt(V(-20, 34));
      var tank = MlrsUnitAt(1, "cf_dragonscale_tank", V(10, 34));
      var engineer = MlrsUnitAt(1, "cf_engineer", V(4, 39));
      var engineerPosition = At(engineer);
      SetUnitHealth(tank, 1);
      Time.timeScale = 2f;
      yield return WaitFor(() => Verified.Get<Building>(battery).TargetEntity == tank, 10f, "MLRS prioritizes armor");
      yield return WaitFor(() => !Verified.Exists(tank), 10f, "MLRS impact destroys armor");
      yield return WaitFor(() => Verified.Get<Building>(battery).TargetEntity == engineer, 10f, "MLRS replaces the destroyed target");
      yield return WaitFor(() => MlrsRocketTo(engineerPosition), 10f, "MLRS fires on the replacement target");
      var launched = 0;
      QuantumEvent.Subscribe<EventMlrsLaunched>(_listener, _ => launched++);
      Verified.Destroy(battery);
      yield return WaitFrames(2 * Fps);
      Assert.IsFalse(Verified.Exists(battery));
      Assert.IsFalse(Verified.Has<MlrsMagazine>(battery));
      Assert.AreEqual(0, Verified.ComponentCount<MlrsRocket>());
      Assert.AreEqual(0, launched);
    }

    [UnityTest]
    public IEnumerator Mlrs_FogRequiresVisibleTarget() {
      UseFog();
      yield return StartMatch(seed: 6091, startingTier: 3, stock: MlrsStock, buildings: MlrsField,
        units: new[] { (1, "cf_dragonscale_tank", 8, 34), (0, "ff_engineer", -7, 34) });
      var battery = BuildingAt(V(-20, 34));
      var scout = MlrsUnitAt(0, "ff_engineer", V(-7, 34));
      var target = MlrsUnitAt(1, "cf_dragonscale_tank", V(8, 34));
      yield return WaitFrames(2 * Fps);
      Assert.IsFalse(FactoryWarsUtil.IsVisibleTo(Verified, 0, target));
      Assert.AreEqual(12, Verified.Get<MlrsMagazine>(battery).Ammo);
      yield return Send(0, new MoveUnitsCommand { Units = new[] { scout }, Target = V(3, 34) });
      yield return WaitFor(() => FactoryWarsUtil.IsVisibleTo(Verified, 0, target), 16f, "Reconnaissance reveals the target");
      yield return WaitFor(() => Verified.Get<MlrsMagazine>(battery).Ammo < 12, 10f, "MLRS fires on a visible target");
    }

    [UnityTest]
    public IEnumerator Mlrs_TwoBatteriesRepeatVerifiedChecksum() {
      RaiseMlrsTargetHealth();
      var sums = new ulong[2];
      for (var run = 0; run < sums.Length; ++run) {
        yield return StartMatch(seed: 6091, startingTier: 3, stock: MlrsStock,
          buildings: MlrsField.Concat(new[] { (0, "ff_mlrs_battery", -20, 22) }).ToArray(),
          units: new[] { (1, "cf_engineer", 8, 27) });
        ulong checksum = 0;
        QuantumCallback.Subscribe<CallbackSimulateFinished>(_listener, e => {
          if (e.Frame.Number == 120 && e.Frame.ComponentCount<MlrsRocket>() >= 2) {
            checksum = e.Frame.CalculateChecksum();
          }
        });
        yield return WaitFor(() => checksum != 0, 10f, "Verified combat checksum");
        sums[run] = checksum;
        QuantumCallback.UnsubscribeListener(_listener);
        QuantumEvent.UnsubscribeListener(_listener);
        Object.Destroy(_listener);
        QuantumRunner.ShutdownAll();
        yield return null;
      }
      Assert.AreEqual(sums[0], sums[1]);
    }

    [UnityTest, Explicit, Category("CaptureCatalog")]
    public IEnumerator Mlrs_CaptureBatteryAndSalvo() {
      Assert.IsNotEmpty(System.Environment.GetEnvironmentVariable("FW_CAPTURE_DIR"));
      RaiseMlrsTargetHealth();
      yield return StartMatch(Map4PScene, startingTier: 3, truce: 10, stock: MlrsStock, buildings: new[] {
        (0, "ff_mlrs_battery", -119, -127), (0, "ff_mlrs_battery", -130, -114),
        (0, "ff_power_plant", -139, -124), (0, "ff_barracks", -142, -137), (0, "ff_vehicle_factory", -122, -143),
      }, units: new[] { (1, "cf_engineer", -96, -115) });
      yield return WaitForBattle();
      var battery = BuildingAt(V(-119, -127));
      var other = BuildingAt(V(-130, -114));
      Assert.AreNotEqual(EntityRef.None, battery);
      Assert.AreNotEqual(EntityRef.None, other);
      Controller().Input.Selection.SelectBuilding(battery);
      var camera = Camera.main;
      var controls = camera.GetComponent<RtsCamera>();
      if (controls != null) controls.enabled = false;
      var site = new Vector3(-119f, 0f, -127f);
      var wide = new Vector3(-114f, 0f, -120f);
      CaptureMlrs("battery_in_military_base", wide, new Vector3(30f, 38f, -20f));
      Time.timeScale = 1f;
      yield return WaitFor(() => Verified.Get<MlrsMagazine>(battery).Status == MlrsStatus.Aiming, 16f, "Launcher starts aiming");
      yield return new WaitForSeconds(0.45f);
      CaptureMlrs("launcher_aiming", site + Vector3.up * 1.3f, new Vector3(9f, 10f, 12f));
      var shot = false;
      var impact = false;
      QuantumEvent.Subscribe<EventMlrsLaunched>(_listener, e => { if (e.Battery == battery) shot = true; });
      QuantumEvent.Subscribe<EventMlrsImpact>(_listener, _ => impact = true);
      yield return WaitFor(() => shot, 8f, "Launcher fires");
      yield return new WaitForSeconds(0.08f);
      CaptureMlrs("rocket_launch", site + new Vector3(2f, 1.4f, 1.4f), new Vector3(10f, 11f, -10f));
      yield return new WaitForSeconds(0.12f);
      CaptureMlrs("multiple_batteries_firing", wide, new Vector3(29f, 32f, -16f));
      yield return WaitFor(() => Verified.ComponentCount<MlrsRocket>() >= 2, 8f, "Rockets leave both batteries");
      yield return new WaitForSeconds(0.24f);
      CaptureMlrs("rocket_flight", new Vector3(-107f, 4f, -120f), new Vector3(9f, 14f, -20f));
      yield return WaitFor(() => impact, 8f, "Impact is confirmed");
      CaptureMlrs("rocket_explosion", new Vector3(-96f, 1f, -115f), new Vector3(10f, 12f, -8f));
      Time.timeScale = 4f;
      yield return WaitFor(() => Verified.Get<MlrsMagazine>(battery).Status == MlrsStatus.Reloading, 12f, "Reload starts");
      Time.timeScale = 1f;
      yield return new WaitForSeconds(0.70f);
      var magazine = Verified.Get<MlrsMagazine>(battery);
      Assert.AreEqual(0, magazine.Ammo);
      Assert.AreEqual(20, magazine.ReloadTotal / Fps);
      Assert.Greater(magazine.ReloadRemaining, 0);
      CaptureMlrs("reloading", site + Vector3.up * 0.5f, new Vector3(8f, 11f, 9f));
    }

    [UnityTest, Explicit, Category("Online"), Timeout(180000)]
    public IEnumerator Mlrs_OnlineVerifiedSalvoAndImpact() {
      Time.timeScale = 1f;
#if UNITY_EDITOR
      yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(TestScene,
        new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Single));
#endif
      var local = Object.FindAnyObjectByType<QuantumRunnerLocalDebug>();
      local.enabled = false;
      QuantumRunner.ShutdownAll();
      var factions = local.LocalPlayers.Take(2).Select(p => p.Faction).ToArray();
      var template = local.RuntimeConfig;
      template.Map = Object.FindAnyObjectByType<QuantumMapData>().AssetRef;
      template.OverrideStartingResources = true;
      template.StartingResources = MlrsStock;
      template.StartingTier = 2;
      template.ScenarioBuildings = new[] {
        Scenario(0, "ff_mlrs_battery", -20, 34), Scenario(0, "ff_power_plant", -36, 30),
        Scenario(1, "cf_mlrs_battery", 24, 40), Scenario(1, "cf_power_plant", 38, 33),
      };
      var ff = new AssetRef<UnitSpec>(Resources.Load<UnitSpec>("FactoryWars/ff_engineer").Guid);
      var cf = new AssetRef<UnitSpec>(Resources.Load<UnitSpec>("FactoryWars/cf_engineer").Guid);
      template.ScenarioUnits = new[] {
        new ScenarioUnit { Player = 0, Unit = ff, Position = V(-3, 41) },
        new ScenarioUnit { Player = 1, Unit = cf, Position = V(6, 35) },
      };
      var host = OnlineRoom.CreateExtra("MlrsHost");
      var guest = OnlineRoom.CreateExtra("MlrsGuest");
      host.KeepScenario = true;
      guest.KeepScenario = true;
      var errors = 0;
      var subscription = QuantumCallback.SubscribeManual<CallbackChecksumError>(_ => ++errors);
      try {
        var create = host.Create(new MatchSettings { FactionIndex = 0 });
        yield return Await(create, 60f, "MLRS online host");
        Assert.IsTrue(((Task<bool>)create).Result, host.Error);
        var join = guest.Join(host.Code, new MatchSettings { FactionIndex = 1 });
        yield return Await(join, 60f, "MLRS online guest");
        Assert.IsTrue(((Task<bool>)join).Result, guest.Error);
        guest.SetMine(1, 1, true);
        host.HostSet(MatchMode.Duel, 0, new int[0]);
        yield return WaitForReal(() => host.StartBlocker() == null && host.TruceMinutes == 0, 20f, "MLRS lobby ready");
        Assert.IsTrue(host.HostStart());
        yield return WaitForReal(() => host.StartReceived && guest.StartReceived, 20f, "MLRS lobby started");
        var a = host.StartSession(template, factions);
        var b = guest.StartSession(template, factions);
        yield return Await(Task.WhenAll(a, b), 60f, "MLRS online match");
        Assert.IsTrue(a.Result && b.Result, $"{host.Error} / {guest.Error}");
        var gameA = host.Runner.Game;
        var gameB = guest.Runner.Game;
        yield return WaitForReal(() => gameA.Frames.Verified != null && gameB.Frames.Verified != null
          && BothActive(gameA.Frames.Verified) && BothActive(gameB.Frames.Verified), 35f, "MLRS players joined");
        var compared = 0;
        var lastFrame = -1;
        var sawFlight = false;
        var sawHit = false;
        var end = Time.realtimeSinceStartup + 20f;
        while (Time.realtimeSinceStartup < end && (!sawFlight || !sawHit || compared < 20)) {
          var left = gameA.Frames.Verified;
          var right = gameB.Frames.Verified;
          if (left.Number == right.Number && left.Number != lastFrame) {
            Assert.AreEqual(left.CalculateChecksum(), right.CalculateChecksum(), $"MLRS verified frame {left.Number}");
            ++compared;
            lastFrame = left.Number;
            sawFlight |= left.ComponentCount<MlrsRocket>() >= 2;
            foreach (var unit in left.GetComponentIterator<Unit>()) {
              if (left.Get<Transform3D>(unit.Entity).Position == V(6, 35)
                  && unit.Component.Health < left.FindAsset(unit.Component.Spec).MaxHealth) {
                sawHit = true;
              }
            }
          }
          yield return null;
        }
        Assert.IsTrue(sawFlight, "Both batteries fire on one verified frame");
        Assert.IsTrue(sawHit, "The rocket changes target health on both clients");
        Assert.GreaterOrEqual(compared, 20);
        Assert.AreEqual(0, errors);
        Debug.Log($"MLRS online checked {compared} shared frames and received {errors} checksum errors");
      } finally {
        subscription.Dispose();
        _ = host.Leave();
        _ = guest.Leave();
        Object.Destroy(host.gameObject);
        Object.Destroy(guest.gameObject);
      }
    }

    static bool MlrsRocketTo(FPVector3 position) {
      foreach (var pair in Verified.GetComponentIterator<MlrsRocket>()) {
        if (pair.Component.Target == position) return true;
      }
      return false;
    }

    static UnitSpec RaiseMlrsTargetHealth() {
      var durable = Resources.Load<UnitSpec>("FactoryWars/cf_engineer");
      var health = durable.MaxHealth;
      durable.MaxHealth = 12000;
      Restores.Add(() => durable.MaxHealth = health);
      return durable;
    }

    static EntityRef MlrsUnitAt(int owner, string spec, FPVector3 position) {
      return UnitsNamed(owner, spec).OrderBy(unit => FactoryWarsUtil.FlatDistanceSqr(At(unit), position)).First();
    }

    static void CaptureMlrs(string shot, Vector3 center, Vector3 offset) {
      if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("FW_CAPTURE_DIR"))) return;
      var camera = Camera.main;
      camera.transform.position = center + offset;
      camera.transform.LookAt(center);
      CaptureCameraPng(camera, Path.Combine(StatusImagesDir(), "MLRS", shot + ".png"), 1280, 720);
    }
  }
}
