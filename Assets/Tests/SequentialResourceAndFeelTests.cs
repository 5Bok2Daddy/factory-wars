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
  /// Sequential Resource Logic & Visual Chain (1A Iron -> 1B Timber -> 1C Electricity),
  /// plus Naval Buoyancy/Wake, Air Altitude/Perspective, and Map4P West Yard/PMC verification captures.
  /// </summary>
  public unsafe partial class LocalMatchTests {
    static string PolishDir() =>
      Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "status_images", "PolishPass"));

    static void SetPlayerOutage(Frame frame, PlayerRef player, bool outage) {
      var p = frame.Global->Players.GetPointer(player);
      if (p != null) p->Outage = outage;
    }

    static int PlayerIron(Frame frame, PlayerRef player) => frame.Global->Players.GetPointer(player)->Stock.Iron.AsInt;
    static int PlayerWood(Frame frame, PlayerRef player) => frame.Global->Players.GetPointer(player)->Stock.Wood.AsInt;

    [UnityTest, Timeout(600000)]
    public IEnumerator Resource_Step1A_IronChainVerified() {
      var dir = PolishDir();
      Directory.CreateDirectory(dir);
      yield return StartMatch();
      yield return WaitForHud();
      var center = V(-30, 14);
      var engineer = Engineers(0)[0];
      yield return Build(0, "iron_extractor", center, engineer);
      var site = BuildingAt(center);
      Assert.AreNotEqual(EntityRef.None, site, "iron extractor created");

      Time.timeScale = 6f;
      yield return WaitFor(() => Finished(site) && CitySystem.Occupants(Verified, site) >= 1, 60f, "iron extractor built & staffed");
      Time.timeScale = 1f;
      var view = Battle();
      yield return WaitFor(() => view.Actors.TryGetValue(site, out var a) && a.ExtractorDelivery?.FirstCargo != null
        && a.ExtractorDelivery.FirstCargo.gameObject.activeInHierarchy, 15f, "iron conveyor & ore active");

      var delivery = view.Actors[site].ExtractorDelivery;
      Assert.IsNotNull(delivery.Root.transform.Find("IntakeHopper"), "Iron IntakeHopper present");
      Assert.IsNotNull(delivery.Root.transform.Find("IronStorageSilo"), "IronStorageSilo present");
      Assert.IsNotNull(delivery.Root.transform.Find("ExtractorStateBeacon"), "ExtractorStateBeacon present");

      var ironBefore = PlayerIron(Verified, 0);
      var beltBefore = delivery.BeltPhase;
      yield return WaitFrames(8);
      Assert.AreNotEqual(beltBefore, delivery.BeltPhase, "iron conveyor belt advances while producing");
      yield return WaitFrames(Fps * 3);
      Assert.Greater(PlayerIron(Verified, 0), ironBefore, "iron stock credits while producing");

      var camera = Camera.main;
      var rts = Controller().Camera;
      camera.aspect = 16f / 9f;
      rts.Height = 24f;
      rts.MoveTo(new Vector2(center.X.AsFloat, center.Z.AsFloat - 4f));
      rts.Apply();
      yield return WaitFrames(4);
      CaptureCameraPng(camera, Path.Combine(dir, "step1a_iron_active.png"), 1920, 1080);

      // Eject engineer -> extraction stops, beacon turns red, belt stops advancing after cargo clears.
      yield return Send(0, new ExitGarrisonCommand { Building = site });
      yield return WaitFor(() => CitySystem.Occupants(Verified, site) == 0, 15f, "engineer exited iron extractor");
      yield return WaitFrames(Fps * 2);
      var beltStoppedA = delivery.BeltPhase;
      yield return WaitFrames(Fps);
      Assert.AreEqual(beltStoppedA, delivery.BeltPhase, 0.001f, "iron conveyor stops when unstaffed");
      CaptureCameraPng(camera, Path.Combine(dir, "step1a_iron_stopped.png"), 1920, 1080);
    }

    [UnityTest, Timeout(600000)]
    public IEnumerator Resource_Step1B_TimberChainVerified() {
      var dir = PolishDir();
      Directory.CreateDirectory(dir);
      yield return StartMatch();
      yield return WaitForHud();
      var center = V(-38, 30);
      var engineer = Engineers(0)[0];
      yield return Build(0, "lumber_mill", center, engineer);
      var site = BuildingAt(center);
      Assert.AreNotEqual(EntityRef.None, site, "lumber mill created");

      Time.timeScale = 6f;
      yield return WaitFor(() => Finished(site) && CitySystem.Occupants(Verified, site) >= 1, 60f, "lumber mill built & staffed");
      Time.timeScale = 1f;
      var view = Battle();
      yield return WaitFor(() => view.Actors.TryGetValue(site, out var a) && a.ExtractorDelivery?.FirstCargo != null
        && a.ExtractorDelivery.FirstCargo.gameObject.activeInHierarchy, 15f, "timber conveyor & logs active");

      var delivery = view.Actors[site].ExtractorDelivery;
      Assert.IsNotNull(delivery.Root.transform.Find("TimberIntakeGuide"), "TimberIntakeGuide present");
      Assert.IsNotNull(delivery.Root.transform.Find("TimberLogRack"), "TimberLogRack present");
      Assert.IsNotNull(delivery.Root.transform.Find("ExtractorStateBeacon"), "ExtractorStateBeacon present");

      var woodBefore = PlayerWood(Verified, 0);
      var beltBefore = delivery.BeltPhase;
      yield return WaitFrames(8);
      Assert.AreNotEqual(beltBefore, delivery.BeltPhase, "timber conveyor belt advances while producing");
      yield return WaitFrames(Fps * 3);
      Assert.Greater(PlayerWood(Verified, 0), woodBefore, "wood stock credits while producing");

      var camera = Camera.main;
      var rts = Controller().Camera;
      camera.aspect = 16f / 9f;
      rts.Height = 24f;
      rts.MoveTo(new Vector2(center.X.AsFloat, center.Z.AsFloat - 2f));
      rts.Apply();
      yield return WaitFrames(4);
      CaptureCameraPng(camera, Path.Combine(dir, "step1b_timber_active.png"), 1920, 1080);
    }

    [UnityTest, Timeout(600000)]
    public IEnumerator Resource_Step1C_ElectricityChainVerified() {
      var dir = PolishDir();
      Directory.CreateDirectory(dir);
      yield return StartMatch(stock: Stock(2000, 2000, 1000, 200));
      yield return WaitForHud();
      var pos = V(-42, 0);
      var engineer = Engineers(0)[0];
      yield return Build(0, "power_plant", pos, engineer);
      var plant = BuildingAt(pos);
      Assert.AreNotEqual(EntityRef.None, plant, "power plant created");

      Time.timeScale = 6f;
      yield return WaitFor(() => Finished(plant), 60f, "power plant built");
      Time.timeScale = 1f;
      var view = Battle();
      yield return WaitFor(() => view.Actors.TryGetValue(plant, out var a) && a.PowerTurbine != null, 10f, "power plant turbine & transformer spawned");
      var actor = view.Actors[plant];
      Assert.IsNotNull(actor.View.transform.Find("PowerPlantDistribution/TransformerBank"), "TransformerBank present");

      var yawBefore = actor.PowerTurbine.localEulerAngles.y;
      yield return WaitFrames(Fps);
      var yawDelta = Mathf.Abs(Mathf.DeltaAngle(yawBefore, actor.PowerTurbine.localEulerAngles.y));
      Assert.Greater(yawDelta, 10f, "generator turbine spins while powered");
      Assert.Greater(actor.PowerBeaconMat.color.b, 0.8f, "power beacon glows cyan while running");

      var camera = Camera.main;
      var rts = Controller().Camera;
      camera.aspect = 16f / 9f;
      rts.Height = 22f;
      rts.MoveTo(new Vector2(pos.X.AsFloat + 2f, pos.Z.AsFloat));
      rts.Apply();
      yield return WaitFrames(4);
      CaptureCameraPng(camera, Path.Combine(dir, "step1c_power_running.png"), 1920, 1080);

      // Trigger outage on both Verified and Predicted frames so BattleView immediately reflects offline state.
      SetPlayerOutage(Verified, 0, true);
      if (Game?.Frames?.Predicted != null) SetPlayerOutage(Game.Frames.Predicted, 0, true);
      yield return null;
      if (Game?.Frames?.Predicted != null) SetPlayerOutage(Game.Frames.Predicted, 0, true);
      Assert.Greater(actor.PowerBeaconMat.color.r, 0.7f, "power beacon turns warning red during outage");
      CaptureCameraPng(camera, Path.Combine(dir, "step1c_power_offline.png"), 1920, 1080);
    }

    [UnityTest, Explicit, Timeout(1200000)]
    public IEnumerator PolishPass_CaptureNavalAirAndWestYard() {
      var dir = PolishDir();
      Directory.CreateDirectory(dir);
      yield return StartMatch(Map4PScene, 2, truce: 0, seed: 4288, startingTier: 3);
      yield return WaitForHud();
      yield return WaitForBattle();
      var doctrine = GameObject.Find("DoctrinePanel");
      if (doctrine != null) doctrine.GetComponentsInChildren<UnityEngine.UI.Button>(true).First().onClick.Invoke();
      GrantAgreement(0);
      Time.timeScale = 1f;

      var camera = Camera.main;
      var rts = Controller().Camera;
      camera.aspect = 16f / 9f;

      // 1. Naval Buoyancy & Wake under Arch Bridge (north S-canal bridge at x=-16, z=124).
      var ship = SpawnUnitByName(Verified, 0, "cf_artillery_ship", new FPVector3(-16, 0, 108));
      var boat = SpawnUnitByName(Verified, 0, "ff_scout_boat", new FPVector3(-22, 0, 114));
      yield return Send(0, new MoveUnitsCommand { Units = new[] { ship, boat }, Target = new FPVector3(-16, 0, 148) });
      yield return WaitFrames(Fps * 3);
      var view = Battle();
      Assert.IsTrue(view.Actors.TryGetValue(ship, out var shipActor) && shipActor.NavalWake != null, "naval waterline wake active");
      var shipPos = UnityPos(Verified, ship);
      rts.Height = 38f;
      rts.MoveTo(new Vector2(shipPos.x, shipPos.z + 4f));
      rts.Apply();
      yield return WaitFrames(6);
      CaptureCameraPng(camera, Path.Combine(dir, "p2_naval_buoyancy_wake.png"), 1920, 1080);

      // 2. Airborne perspective (helicopters, drones, bomber over canal/city).
      var airCenter = new FPVector3(0, 0, 56);
      SpawnUnitByName(Verified, 0, "ff_eagle_tank", airCenter + new FPVector3(-6, 0, -4));
      var air = new[] {
        SpawnUnitByName(Verified, 0, "ff_hawk_gunship", airCenter + new FPVector3(-10, 0, -4)),
        SpawnUnitByName(Verified, 0, "pmc_attack_heli", airCenter + new FPVector3(  2, 0, -2)),
        SpawnUnitByName(Verified, 0, "ff_skyfortress_bomber", airCenter + new FPVector3(8, 0, 10)),
        SpawnUnitByName(Verified, 0, "ff_recon_drone", airCenter + new FPVector3(-4, 0, 6)),
        SpawnUnitByName(Verified, 0, "ff_loiter_munition", airCenter + new FPVector3(4, 0, 4)),
      };
      yield return Send(0, new MoveUnitsCommand { Units = air, Target = airCenter + new FPVector3(20, 0, -4) });
      yield return WaitFrames(Fps * 2);
      var airPos = UnityPos(Verified, air[1]);
      rts.Height = 42f;
      rts.MoveTo(new Vector2(airPos.x, airPos.z + 2f));
      rts.Apply();
      yield return WaitFrames(6);
      CaptureCameraPng(camera, Path.Combine(dir, "p2_air_altitude_perspective.png"), 1920, 1080);

      // 3. T46 West (9 o'clock) working yard + quay + sea-gate overview.
      rts.Height = 68f;
      rts.MoveTo(new Vector2(-282f, -6f));
      rts.Apply();
      yield return WaitFrames(6);
      CaptureCameraPng(camera, Path.Combine(dir, "p3_west_yard_waterfront.png"), 1920, 1080);

      // 4. T39/T40 PMC camp (inner fence-free) + Drone Operator active launch.
      var op = SpawnUnitByName(Verified, 0, "pmc_drone_operator", new FPVector3(-4, 0, -60));
      SpawnUnitByName(Verified, 1, "cf_rampart_ifv", new FPVector3(10, 0, -60));
      yield return WaitFor(() => OperatorDronesOf(Verified, op).Count >= 1, 20f, "PMC drone operator launches drone");
      rts.Height = 44f;
      rts.MoveTo(new Vector2(0f, -34f));
      rts.Apply();
      yield return WaitFrames(6);
      CaptureCameraPng(camera, Path.Combine(dir, "p3_pmc_camp_and_operator.png"), 1920, 1080);
    }
  }
}
