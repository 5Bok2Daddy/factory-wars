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

    [UnityTest, Explicit, Timeout(1200000)]
    public IEnumerator PolishPass_CaptureMotionClipsAndDiagnoseMap4P() {
      var dir = PolishDir();
      var framesDir = Path.Combine(dir, "frames");
      Directory.CreateDirectory(framesDir);
      var diag = new List<string> { "Map4P Seat 0 Iron/Wood Node Placement & Motion Clip Diagnostics" };

      // --- Part A: Diagnose Map4P Seat 0 resource nodes + capture Naval & Air/Ground motion bursts ---
      yield return StartMatch(Map4PScene, 2, truce: 0, seed: 4289, startingTier: 3,
        stock: new ResourceCost { Iron = 8000, Wood = 8000, Steel = 4000, Power = 4000 });
      yield return WaitForHud();
      yield return WaitForBattle();
      var doctrine = GameObject.Find("DoctrinePanel");
      if (doctrine != null) doctrine.GetComponentsInChildren<UnityEngine.UI.Button>(true).First().onClick.Invoke();
      GrantAgreement(0);
      GrantAgreement(1);

      var hqPos = Verified.Get<Transform3D>(Headquarters(0)).Position;
      var ironSpec = Verified.FindAsset(BuildingOf(0, "iron_extractor"));
      var woodSpec = Verified.FindAsset(BuildingOf(0, "lumber_mill"));
      var placeableIron = 0;
      var placeableWood = 0;
      foreach (var pair in Verified.GetComponentIterator<ResourceNode>()) {
        var p = Verified.Get<Transform3D>(pair.Entity).Position;
        var d = FPMath.Sqrt(FactoryWarsUtil.FlatDistanceSqr(hqPos, p)).AsFloat;
        if (d > 140f) continue;
        var spec = pair.Component.Kind == ResourceKind.Iron ? ironSpec : woodSpec;
        var ok = ProductionSystem.TryGetBuildPlacement(Verified, spec, p, 0, out var aligned);
        var reason = ok ? "OK" : ProductionSystem.PlacementReason(Verified, spec, p, 0).ToString();
        if (ok && pair.Component.Kind == ResourceKind.Iron) placeableIron++;
        if (ok && pair.Component.Kind == ResourceKind.Wood) placeableWood++;
        diag.Add($"Node {pair.Component.Kind} at ({p.X.AsFloat:0.0},{p.Z.AsFloat:0.0}) dist={d:0.0}m canPlace={ok} reason={reason}");
      }
      diag.Add($"Summary: placeableIron={placeableIron}, placeableWood={placeableWood}");
      File.WriteAllLines(Path.Combine(dir, "map4p_node_diag.txt"), diag);
      Debug.Log(string.Join("\n", diag));

      var camera = Camera.main;
      var rts = Controller().Camera;
      camera.aspect = 16f / 9f;
      Time.timeScale = 1f;

      // Motion Clip 1: Naval Buoyancy & V-Wake sailing under north arch bridge (10 frames @ 0.25s = 2.5s)
      var ship = SpawnUnitByName(Verified, 0, "cf_artillery_ship", new FPVector3(-16, 0, 102));
      var boat = SpawnUnitByName(Verified, 0, "ff_scout_boat", new FPVector3(-22, 0, 108));
      yield return Send(0, new MoveUnitsCommand { Units = new[] { ship, boat }, Target = new FPVector3(-16, 0, 156) });
      yield return WaitFrames(Fps);
      rts.Height = 40f;
      rts.MoveTo(new Vector2(-16f, 122f));
      rts.Apply();
      for (var f = 0; f < 10; f++) {
        yield return WaitFrames(15);
        CaptureCameraPng(camera, Path.Combine(framesDir, $"naval_{f:00}.png"), 960, 540);
      }

      // Motion Clip 2: Air units (helicopters/drones/bomber) + ground infantry/tanks moving in formation (10 frames)
      var center = new FPVector3(-8, 0, -64);
      var ground = new[] {
        SpawnUnitByName(Verified, 0, "ff_striker_ifv", center + new FPVector3(-16, 0, -3)),
        SpawnUnitByName(Verified, 0, "ff_eagle_tank",  center + new FPVector3(-16, 0,  3)),
        SpawnUnitByName(Verified, 0, "ff_ranger_squad", center + new FPVector3(-10, 0, -2)),
        SpawnUnitByName(Verified, 0, "pmc_operator_squad", center + new FPVector3(-10, 0, 2)),
      };
      var air = new[] {
        SpawnUnitByName(Verified, 0, "ff_hawk_gunship", center + new FPVector3(-14, 0, 0)),
        SpawnUnitByName(Verified, 0, "pmc_attack_heli", center + new FPVector3(-6, 0, 4)),
        SpawnUnitByName(Verified, 0, "ff_recon_drone",  center + new FPVector3(-8, 0, -5)),
      };
      yield return Send(0, new MoveUnitsCommand { Units = ground.Concat(air).ToArray(), Target = center + new FPVector3(30, 0, 0) });
      rts.Height = 36f;
      rts.MoveTo(new Vector2(center.X.AsFloat + 4f, center.Z.AsFloat));
      rts.Apply();
      for (var f = 0; f < 10; f++) {
        yield return WaitFrames(12);
        CaptureCameraPng(camera, Path.Combine(framesDir, $"airground_{f:00}.png"), 960, 540);
      }

      // --- Part B: Resource Chain Motion Clip (Iron conveyor + Power turbine running, 10 frames) ---
      TearDown();
      yield return null;
      yield return StartMatch(stock: Stock(2000, 2000, 1000, 200));
      yield return WaitForHud();
      var ironPos = V(-30, 14);
      var powerPos = V(-42, 0);
      var eng0 = Engineers(0)[0];
      var eng1 = SpawnUnitByName(Verified, 0, "ff_engineer", V(-38, 2));
      yield return Build(0, "iron_extractor", ironPos, eng0);
      var ironSite = BuildingAt(ironPos);
      yield return Build(0, "power_plant", powerPos, eng1);
      var plant = BuildingAt(powerPos);
      Time.timeScale = 6f;
      yield return WaitFor(() => Finished(ironSite) && CitySystem.Occupants(Verified, ironSite) >= 1 && Finished(plant), 90f, "iron & power built");
      Time.timeScale = 1f;
      var bv = Battle();
      yield return WaitFor(() => bv.Actors.TryGetValue(ironSite, out var a) && a.ExtractorDelivery?.FirstCargo != null && a.ExtractorDelivery.FirstCargo.gameObject.activeInHierarchy, 15f, "conveyor active");
      camera = Camera.main;
      rts = Controller().Camera;
      camera.aspect = 16f / 9f;
      rts.Height = 30f;
      rts.MoveTo(new Vector2(-35f, 7f));
      rts.Apply();
      for (var f = 0; f < 10; f++) {
        yield return WaitFrames(12);
        CaptureCameraPng(camera, Path.Combine(framesDir, $"resource_{f:00}.png"), 960, 540);
      }

      File.WriteAllLines(Path.Combine(dir, "map4p_node_diag.txt"), diag);
      Debug.Log(string.Join("\n", diag));
    }

    [UnityTest, Timeout(600000)]
    public IEnumerator T42_CombinedArmsBalanceCheck() {
      var ffWins = 0;
      var cfWins = 0;
      var lines = new List<string> { "Task 42 Equal-Cost Combined-Arms Skirmish Balance (4 seeds)" };
      for (var run = 0; run < 4; run++) {
        yield return StartMatch(Map4PScene, 2, truce: 0, seed: 4250 + run, startingTier: 3);
        yield return WaitForBattle();
        GrantAgreement(0);
        GrantAgreement(1);
        var center = new FPVector3(0, 0, -64);
        var flip = run % 2 == 0 ? 1 : -1;
        var ff = new[] {
          SpawnUnitByName(Verified, 0, "ff_striker_ifv",      center + new FPVector3(-18 * flip, 0, -4)),
          SpawnUnitByName(Verified, 0, "ff_vanguard_mrap",    center + new FPVector3(-14 * flip, 0,  4)),
          SpawnUnitByName(Verified, 0, "ff_paladin_sph",      center + new FPVector3(-24 * flip, 0,  0)),
          SpawnUnitByName(Verified, 0, "ff_aegis_ew",         center + new FPVector3(-16 * flip, 0,  5)),
          SpawnUnitByName(Verified, 0, "ff_drone_operator",   center + new FPVector3(-11 * flip, 0, -3)),
          SpawnUnitByName(Verified, 0, "ff_antidrone_gunner", center + new FPVector3(-11 * flip, 0,  3)),
          SpawnUnitByName(Verified, 0, "ff_loiter_munition",  center + new FPVector3( -9 * flip, 0,  0)),
        };
        var cf = new[] {
          SpawnUnitByName(Verified, 1, "cf_rampart_ifv",      center + new FPVector3( 18 * flip, 0, -4)),
          SpawnUnitByName(Verified, 1, "cf_ironhide_mrap",    center + new FPVector3( 14 * flip, 0,  4)),
          SpawnUnitByName(Verified, 1, "cf_bastion_sph",      center + new FPVector3( 24 * flip, 0,  0)),
          SpawnUnitByName(Verified, 1, "cf_shroud_ew",        center + new FPVector3( 16 * flip, 0,  5)),
          SpawnUnitByName(Verified, 1, "cf_drone_operator",   center + new FPVector3( 11 * flip, 0, -3)),
          SpawnUnitByName(Verified, 1, "cf_antidrone_gunner", center + new FPVector3( 11 * flip, 0,  3)),
          SpawnUnitByName(Verified, 1, "cf_loiter_munition",  center + new FPVector3(  9 * flip, 0,  0)),
        };
        yield return Send(0, new AttackTargetCommand { Units = ff, Target = cf[0] });
        yield return Send(1, new AttackTargetCommand { Units = cf, Target = ff[0] });
        Time.timeScale = 6f;
        yield return WaitFrames(Fps * 22);
        var ffHp = ff.Sum(e => UnitHealth(Verified, e).AsFloat);
        var cfHp = cf.Sum(e => UnitHealth(Verified, e).AsFloat);
        var ffAlive = ff.Count(e => Verified.Exists(e));
        var cfAlive = cf.Count(e => Verified.Exists(e));
        if (ffHp > cfHp) ffWins++;
        else if (cfHp > ffHp) cfWins++;
        lines.Add($"seed {4250 + run} (flip={flip}): FF alive={ffAlive} (HP {ffHp:0}) vs CF alive={cfAlive} (HP {cfHp:0})");
        TearDown();
        yield return null;
      }
      lines.Add($"Result: FF wins={ffWins}, CF wins={cfWins}");
      Directory.CreateDirectory(PolishDir());
      File.WriteAllLines(Path.Combine(PolishDir(), "t42_balance.txt"), lines);
      Debug.Log(string.Join("\n", lines));
      Assert.IsTrue(ffWins + cfWins > 0, "at least one decisive skirmish");
    }

    [UnityTest, Explicit, Timeout(1200000)]
    public IEnumerator FiveFixes_VerifyAndCapture() {
      var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "status_images", "FiveFixes"));
      Directory.CreateDirectory(dir);
      yield return StartMatch(Map4PScene, 2, truce: 0, seed: 5501, startingTier: 3);
      yield return WaitForHud();
      yield return WaitForBattle();
      var doctrine = GameObject.Find("DoctrinePanel");
      if (doctrine != null) doctrine.GetComponentsInChildren<UnityEngine.UI.Button>(true).First().onClick.Invoke();
      GrantAgreement(0);
      GrantAgreement(1);
      Time.timeScale = 1f;

      var camera = Camera.main;
      var rts = Controller().Camera;
      camera.aspect = 16f / 9f;

      // Fix 1 & Fix 3B: Spawn tanks/IFVs/infantry close together & engage -> verify 2-pass separation prevents overlap + combat health bars visible.
      var c = new FPVector3(0, 0, -64);
      var ff = new[] {
        SpawnUnitByName(Verified, 0, "ff_eagle_tank",   c + new FPVector3(-12, 0, -1)),
        SpawnUnitByName(Verified, 0, "ff_eagle_tank",   c + new FPVector3(-11, 0,  1)),
        SpawnUnitByName(Verified, 0, "ff_striker_ifv",  c + new FPVector3(-10, 0,  0)),
        SpawnUnitByName(Verified, 0, "ff_ranger_squad", c + new FPVector3( -7, 0, -2)),
        SpawnUnitByName(Verified, 0, "ff_ranger_squad", c + new FPVector3( -7, 0,  2)),
      };
      var cf = new[] {
        SpawnUnitByName(Verified, 1, "cf_fortress_tank", c + new FPVector3(12, 0, -1)),
        SpawnUnitByName(Verified, 1, "cf_rampart_ifv",   c + new FPVector3(11, 0,  1)),
        SpawnUnitByName(Verified, 1, "cf_militia",       c + new FPVector3( 7, 0,  0)),
      };
      yield return Send(0, new AttackTargetCommand { Units = ff, Target = cf[0] });
      yield return Send(1, new AttackTargetCommand { Units = cf, Target = ff[0] });
      yield return WaitFrames(Fps * 2);

      // Assert no two living ground vehicles are severely overlapping (dist >= sum(radius) * 0.85).
      var allGround = ff.Concat(cf).Where(e => Verified.Exists(e)).ToList();
      var bv = Battle();
      var visibleBars = 0;
      foreach (var e in allGround) {
        if (bv.Actors.TryGetValue(e, out var act) && act.View?.Bar != null && act.View.Bar.activeSelf) visibleBars++;
      }
      Assert.GreaterOrEqual(visibleBars, 4, "engaged/damaged units display combat health bars");
      var tankDist = Vector3.Distance(UnityPos(Verified, ff[0]), UnityPos(Verified, ff[1]));
      Assert.Greater(tankDist, 3.8f, $"adjacent tanks separated cleanly without overlap (dist={tankDist:0.00}m)");

      rts.Height = 32f;
      rts.MoveTo(new Vector2(c.X.AsFloat, c.Z.AsFloat));
      rts.Apply();
      yield return WaitFrames(6);
      CaptureCameraPng(camera, Path.Combine(dir, "fix1_3_spacing_and_healthbars.png"), 1920, 1080);

      // Fix 2: Raised airborne unit altitude (9.2 / 12.4 / 15.8 m) & shadow separation.
      var ac = new FPVector3(0, 0, 56);
      SpawnUnitByName(Verified, 0, "ff_eagle_tank", ac + new FPVector3(-6, 0, -2));
      var air = new[] {
        SpawnUnitByName(Verified, 0, "ff_hawk_gunship",       ac + new FPVector3(-10, 0, -2)),
        SpawnUnitByName(Verified, 0, "pmc_attack_heli",       ac + new FPVector3(  2, 0, -2)),
        SpawnUnitByName(Verified, 0, "ff_skyfortress_bomber", ac + new FPVector3(  8, 0,  8)),
        SpawnUnitByName(Verified, 0, "ff_recon_drone",        ac + new FPVector3( -4, 0,  5)),
      };
      yield return Send(0, new MoveUnitsCommand { Units = air, Target = ac + new FPVector3(16, 0, -2) });
      yield return WaitFrames(Fps * 2);
      var aPos = UnityPos(Verified, air[1]);
      rts.Height = 46f;
      rts.MoveTo(new Vector2(aPos.x, aPos.z + 2f));
      rts.Apply();
      yield return WaitFrames(6);
      CaptureCameraPng(camera, Path.Combine(dir, "fix2_air_high_altitude.png"), 1920, 1080);

      // Fix 3A: PMC camp perimeter continuous 3-coil barbed wire (no gaps).
      rts.Height = 28f;
      rts.MoveTo(new Vector2(28f, -38f));
      rts.Apply();
      yield return WaitFrames(6);
      CaptureCameraPng(camera, Path.Combine(dir, "fix3_barbed_wire_continuous.png"), 1920, 1080);

      // Fix 5: Upgraded FF Aegis Destroyer (19m) + restored CF Ironwall Battleship (20m) under arch bridge.
      var ffShip = SpawnUnitByName(Verified, 0, "ff_artillery_ship", new FPVector3(-22, 0, 104));
      var cfShip = SpawnUnitByName(Verified, 0, "cf_artillery_ship", new FPVector3(-10, 0, 104));
      yield return Send(0, new MoveUnitsCommand { Units = new[] { ffShip, cfShip }, Target = new FPVector3(-16, 0, 152) });
      yield return WaitFrames(Fps * 3);
      var sPos = (UnityPos(Verified, ffShip) + UnityPos(Verified, cfShip)) * 0.5f;
      rts.Height = 44f;
      rts.MoveTo(new Vector2(sPos.x, sPos.z + 4f));
      rts.Apply();
      yield return WaitFrames(6);
      CaptureCameraPng(camera, Path.Combine(dir, "fix5_upgraded_warships.png"), 1920, 1080);
    }
  }
}
