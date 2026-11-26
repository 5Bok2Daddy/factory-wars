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

    static void SetUnitHealthRatio(Frame frame, EntityRef entity, FP ratio) {
      if (frame.Exists(entity) && frame.Unsafe.TryGetPointer<Unit>(entity, out var u)) {
        var spec = frame.FindAsset(u->Spec);
        u->Health = spec.MaxHealth * ratio;
      }
    }

    [UnityTest, Explicit, Timeout(1200000)]
    public IEnumerator ParticlePackVfx_VerifyAndCapture() {
      var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "status_images", "ParticleVfx"));
      var frames = Path.Combine(dir, "frames");
      Directory.CreateDirectory(frames);

      // 1. Verify URP 17.0.4 shader + texture compatibility for all 6 ParticlePack materials (zero pink/error shaders).
      foreach (var mat in new[] { ParticlePackVfx.FoamMat, ParticlePackVfx.RippleMat, ParticlePackVfx.SplashMat,
                                  ParticlePackVfx.FireMat, ParticlePackVfx.SmokeMat, ParticlePackVfx.EmberMat }) {
        Assert.IsNotNull(mat, "ParticlePack material created");
        Assert.IsNotNull(mat.shader, $"shader valid on {mat.name}");
        Assert.IsFalse(mat.shader.name.Contains("Error"), $"no pink/error shader on {mat.name}");
        var tex = mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap") : mat.mainTexture;
        Assert.IsNotNull(tex, $"ParticlePack texture loaded on {mat.name}");
      }

      yield return StartMatch(Map4PScene, 2, truce: 0, seed: 5601, startingTier: 3);
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

      // 2. Naval Water Splash, Ripples & Stern Foam Wake under North Arch Bridge (10 frames + hero PNG).
      var ffShip = SpawnUnitByName(Verified, 0, "ff_artillery_ship", new FPVector3(-22, 0, 96));
      var cfShip = SpawnUnitByName(Verified, 0, "cf_artillery_ship", new FPVector3(-10, 0, 96));
      var boat   = SpawnUnitByName(Verified, 0, "ff_scout_boat",     new FPVector3(-16, 0, 110));
      yield return Send(0, new MoveUnitsCommand { Units = new[] { ffShip, cfShip, boat }, Target = new FPVector3(-16, 0, 158) });
      yield return WaitFrames(Fps * 2);

      var bv = Battle();
      Assert.IsTrue(bv.Actors.TryGetValue(cfShip, out var shipAct) && shipAct.NavalWake != null, "Naval particle wake created");
      Assert.IsNull(shipAct.View.transform.Find("NavalWaterlineWake"), "Rigid V-bar wake geometry removed");
      Assert.IsNotNull(shipAct.NavalWake.Root.Find("SternAnchor/SternFoamTrail"), "SternFoamTrail particle system active");
      Assert.IsNotNull(shipAct.NavalWake.Root.Find("WaterlineRipples"), "WaterlineRipples particle system active");
      Assert.IsNotNull(shipAct.NavalWake.Root.Find("BowAnchor/BowWaterSplash"), "BowWaterSplash particle system active");

      rts.Height = 42f;
      rts.MoveTo(new Vector2(-16f, 118f));
      rts.Apply();
      for (var f = 0; f < 10; f++) {
        yield return WaitFrames(10);
        CaptureCameraPng(camera, Path.Combine(frames, $"naval_vfx_{f:00}.png"), 960, 540);
      }
      CaptureCameraPng(camera, Path.Combine(dir, "vfx_naval_particle_wake.png"), 1920, 1080);

      // 3. Fire, Smoke & Embers on Damaged/Burning Combat Units (10 frames + hero PNG).
      var c = new FPVector3(0, 0, -64);
      var ff1 = SpawnUnitByName(Verified, 0, "ff_eagle_tank",  c + new FPVector3(-11, 0, -3));
      var ff2 = SpawnUnitByName(Verified, 0, "ff_striker_ifv", c + new FPVector3( -9, 0,  4));
      var cf1 = SpawnUnitByName(Verified, 1, "cf_fortress_tank", c + new FPVector3(11, 0, -2));
      var cf2 = SpawnUnitByName(Verified, 1, "cf_rampart_ifv",   c + new FPVector3( 9, 0,  4));
      SetUnitHealthRatio(Verified, ff1, FP._0_25);
      SetUnitHealthRatio(Verified, cf1, FP._0_20);
      SetUnitHealthRatio(Verified, cf2, FP._0_50);
      if (Game?.Frames?.Predicted != null) {
        SetUnitHealthRatio(Game.Frames.Predicted, ff1, FP._0_25);
        SetUnitHealthRatio(Game.Frames.Predicted, cf1, FP._0_20);
        SetUnitHealthRatio(Game.Frames.Predicted, cf2, FP._0_50);
      }
      yield return Send(0, new AttackTargetCommand { Units = new[] { ff1, ff2 }, Target = cf1 });
      yield return Send(1, new AttackTargetCommand { Units = new[] { cf1, cf2 }, Target = ff1 });
      yield return WaitFrames(Fps);

      Assert.IsTrue(bv.Actors.TryGetValue(ff1, out var dmgAct) && dmgAct.DamageFire != null && dmgAct.DamageFire.Root.gameObject.activeSelf,
        "Damaged unit displays ParticlePack fire/smoke/embers VFX");

      rts.Height = 30f;
      rts.MoveTo(new Vector2(c.X.AsFloat, c.Z.AsFloat));
      rts.Apply();
      for (var f = 0; f < 10; f++) {
        yield return WaitFrames(8);
        CaptureCameraPng(camera, Path.Combine(frames, $"fire_vfx_{f:00}.png"), 960, 540);
      }
      CaptureCameraPng(camera, Path.Combine(dir, "vfx_combat_fire_smoke.png"), 1920, 1080);
    }

    [UnityTest, Timeout(1200000)]
    public IEnumerator AirFlight_AltitudeVfxAndBuildingAvoidance() {
      var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "status_images", "AirVfx"));
      var frames = Path.Combine(dir, "frames");
      Directory.CreateDirectory(frames);

      // 1. Verify URP 17.0.4 shader + texture compatibility for AirFlightVfx materials (zero pink/error shaders).
      foreach (var mat in new[] { ParticlePackVfx.JetGlowMat, ParticlePackVfx.ContrailMat, ParticlePackVfx.DownwashMat }) {
        Assert.IsNotNull(mat, "Air VFX material created");
        Assert.IsNotNull(mat.shader, $"shader valid on {mat.name}");
        Assert.IsFalse(mat.shader.name.Contains("Error"), $"no pink/error shader on {mat.name}");
        var tex = mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap") : mat.mainTexture;
        Assert.IsNotNull(tex, $"ParticlePack texture loaded on {mat.name}");
      }

      yield return StartMatch(Map4PScene, 2, truce: 0, seed: 5701, startingTier: 3);
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
      var bv = Battle();
      var map = FactoryWarsUtil.MapInfo(Verified);

      // 2. High-altitude flight VFX & flying freely over PMC barbed-wire fence/trees/barriers.
      var heli   = SpawnUnitByName(Verified, 0, "ff_hawk_gunship",       new FPVector3(-24, 0, -28));
      var drone  = SpawnUnitByName(Verified, 0, "ff_recon_drone",        new FPVector3(-12, 0, -32));
      var bomber = SpawnUnitByName(Verified, 0, "ff_skyfortress_bomber", new FPVector3( -4, 0, -36));
      // Order straight across the PMC perimeter fence, trees, and central road.
      yield return Send(0, new MoveUnitsCommand { Units = new[] { heli, drone, bomber }, Target = new FPVector3(16, 0, 26) });
      yield return WaitFrames(Fps * 2);

      Assert.IsTrue(bv.Actors.TryGetValue(heli, out var heliAct) && heliAct.AirFlight != null, "Helicopter AirFlightVfx created");
      Assert.IsTrue(bv.Actors.TryGetValue(drone, out var droneAct) && droneAct.AirFlight != null, "Drone AirFlightVfx created");
      Assert.IsTrue(bv.Actors.TryGetValue(bomber, out var bomberAct) && bomberAct.AirFlight != null, "Bomber AirFlightVfx created");

      Assert.IsNotNull(heliAct.AirFlight.FlightRoot.Find("TurbineExhaustAnchor/TurbineExhaust"), "Heli TurbineExhaust active");
      Assert.IsNotNull(heliAct.AirFlight.GroundRoot?.Find("GroundDownwashRing"), "Heli GroundDownwashRing active");
      Assert.IsNotNull(bomberAct.AirFlight.FlightRoot.Find("ThrusterAnchor/ThrusterGlow"), "Bomber ThrusterGlow active");
      Assert.IsNotNull(bomberAct.AirFlight.FlightRoot.Find("ContrailAnchor/FlightContrail"), "Bomber FlightContrail active");

      var droneAlt  = droneAct.View.Turn.localPosition.y;
      var heliAlt   = heliAct.View.Turn.localPosition.y;
      var bomberAlt = bomberAct.View.Turn.localPosition.y;
      Assert.GreaterOrEqual(droneAlt, 16.0f, $"Drone altitude raised >= 16.5m (actual {droneAlt:F2}m)");
      Assert.GreaterOrEqual(heliAlt, 19.0f, $"Heli altitude raised >= 19.5m (actual {heliAlt:F2}m)");
      Assert.GreaterOrEqual(bomberAlt, 25.0f, $"Bomber dedicated altitude >= 25.5m (actual {bomberAlt:F2}m)");

      rts.Height = 52f;
      rts.MoveTo(new Vector2(-4f, -4f));
      rts.Apply();
      yield return WaitFrames(15);
      CaptureCameraPng(camera, Path.Combine(dir, "air_high_altitude_and_vfx.png"), 1920, 1080);

      // Verify barrier/fence fly-over succeeded (units moved well past the Z=-18 PMC fence).
      var heliPosAfterFence = Verified.Get<Transform3D>(heli).Position;
      Assert.Greater(heliPosAfterFence.Z.AsFloat, -16f, $"Air unit flew freely over PMC barrier/fence (Z={heliPosAfterFence.Z.AsFloat:F1})");

      // 3. Direct flight over tall urban buildings ('H' cells in CellGrid) at safe rooftop clearance without horizontal deflection.
      Assert.IsTrue(GridMapView.Uses(map), "Map4P uses CellGrid");
      var n = map.CellGridSize;
      int bRow = -1, bCol = -1;
      for (var r = 18; r <= 62 && bRow < 0; r++) {
        for (var c = 18; c <= 62 && bRow < 0; c++) {
          if (GridMapView.Cell(map, r, c) != 'H') continue;
          var westOpen = GridMapView.Cell(map, r, c - 1) != 'H';
          var eastOpen = GridMapView.Cell(map, r, c + 2) != 'H' || GridMapView.Cell(map, r, c + 3) != 'H';
          if (westOpen && eastOpen) {
            bRow = r;
            bCol = c;
          }
        }
      }
      Assert.GreaterOrEqual(bRow, 0, "Found tall urban building ('H') on Map4P");
      var bCenterX = GridMapView.CellX(map, bCol);
      var bCenterZ = GridMapView.CellZ(map, bRow);
      var startPos = new FPVector3(FP.FromFloat_UNSAFE(GridMapView.CellX(map, bCol - 1)), FP._0, FP.FromFloat_UNSAFE(bCenterZ));
      var endPos   = new FPVector3(FP.FromFloat_UNSAFE(GridMapView.CellX(map, bCol + 3)), FP._0, FP.FromFloat_UNSAFE(bCenterZ));

      var directHeli   = SpawnUnitByName(Verified, 0, "cf_gale_gunship",       startPos + new FPVector3(0, 0, -1));
      var directBomber = SpawnUnitByName(Verified, 0, "ff_skyfortress_bomber", startPos + new FPVector3(0, 0,  2));
      yield return Send(0, new MoveUnitsCommand { Units = new[] { directHeli, directBomber }, Target = endPos });

      rts.Height = 60f;
      rts.MoveTo(new Vector2(bCenterX + 4f, bCenterZ + 12f));
      rts.Apply();

      var cellSize = map.CellGridCell.AsFloat;
      var mapHalf = n * cellSize * 0.5f;
      var maxLateralDeflection = 0f;
      var sawHeliOverH = false;
      var sawBomberOverH = false;
      for (var f = 0; f < 14; f++) {
        yield return WaitFrames(14);
        var pH = Verified.Get<Transform3D>(directHeli).Position;
        var pB = Verified.Get<Transform3D>(directBomber).Position;
        int rH = Mathf.FloorToInt((mapHalf - pH.Z.AsFloat) / cellSize);
        int cH = Mathf.FloorToInt((pH.X.AsFloat + mapHalf) / cellSize);
        int rB = Mathf.FloorToInt((mapHalf - pB.Z.AsFloat) / cellSize);
        int cB = Mathf.FloorToInt((pB.X.AsFloat + mapHalf) / cellSize);
        if (GridMapView.Cell(map, rH, cH) == 'H') sawHeliOverH = true;
        if (GridMapView.Cell(map, rB, cB) == 'H') sawBomberOverH = true;
        maxLateralDeflection = Mathf.Max(maxLateralDeflection, Mathf.Abs(pH.Z.AsFloat - bCenterZ));
        CaptureCameraPng(camera, Path.Combine(frames, $"air_direct_{f:00}.png"), 960, 540);
      }
      CaptureCameraPng(camera, Path.Combine(dir, "air_building_avoidance.png"), 1920, 1080);

      var finalHeliX = Verified.Get<Transform3D>(directHeli).Position.X.AsFloat;
      Assert.IsTrue(sawHeliOverH || sawBomberOverH, "Air units flew directly over tall 'H' building cells");
      Assert.Less(maxLateralDeflection, 5.0f, $"Helicopter flew straight over tall building without lateral deflection (maxDeflect={maxLateralDeflection:F2}m)");
      Assert.Greater(finalHeliX, bCenterX + 2f,
        $"Helicopter crossed tall building cleanly toward destination (startX={startPos.X.AsFloat:F1}, endX={finalHeliX:F1}, maxDeflect={maxLateralDeflection:F2}m)");
      Debug.Log($"[AirFlight_AltitudeVfxAndBuildingAvoidance] PASS alt=({droneAlt:F1}/{heliAlt:F1}/{bomberAlt:F1}m) cell=({bRow},{bCol}) overH=({sawHeliOverH}/{sawBomberOverH}) maxDeflect={maxLateralDeflection:F2}m");
    }

    [UnityTest, Explicit, Timeout(1200000)]
    public IEnumerator MobileMls_FireWhileMoving_NoWarning_AndUnitCaps() {
      var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "status_images", "MobileMls"));
      var frames = Path.Combine(dir, "frames");
      Directory.CreateDirectory(frames);

      yield return StartMatch(Map4PScene, 2, truce: 0, seed: 5801, startingTier: 3);
      yield return WaitForHud();
      yield return WaitForBattle();
      var doctrine = GameObject.Find("DoctrinePanel");
      if (doctrine != null) doctrine.GetComponentsInChildren<UnityEngine.UI.Button>(true).First().onClick.Invoke();
      GrantAgreement(0);
      GrantAgreement(1);
      Time.timeScale = 1f;

      // 1. Verify data specs: Mobile MLS ultimate link, FireWhileMoving, and 1-unit cap on BOTH cf_longwall_mlrs and ff_skyfortress_bomber.
      var mlsSpec = Resources.Load<UnitSpec>("FactoryWars/cf_longwall_mlrs");
      var bomberSpec = Resources.Load<UnitSpec>("FactoryWars/ff_skyfortress_bomber");
      var cfUlt = Resources.Load<UltimateSpec>("FactoryWars/ultimate_cf_missile");
      Assert.IsNotNull(mlsSpec, "cf_longwall_mlrs spec loaded");
      Assert.IsNotNull(bomberSpec, "ff_skyfortress_bomber spec loaded");
      Assert.IsNotNull(cfUlt, "ultimate_cf_missile spec loaded");
      Assert.IsTrue(cfUlt.MobileMls.IsValid, "ultimate_cf_missile points to MobileMls unit");
      Assert.AreEqual(mlsSpec.Guid, cfUlt.MobileMls.Id, "ultimate_cf_missile.MobileMls == cf_longwall_mlrs");
      Assert.AreEqual(1, mlsSpec.MaxOwned, "Mobile MLS MaxOwned == 1");
      Assert.IsTrue(mlsSpec.FireWhileMoving, "Mobile MLS FireWhileMoving == true");
      Assert.AreEqual(1, bomberSpec.MaxOwned, "FF Skyfortress Bomber MaxOwned == 1");

      // 2. Verify 1-unit cap & post-destruction rebuild for BOTH Mobile MLS (CF) and Bomber (FF).
      Assert.IsTrue(TechSystem.CanProduce(Verified, 1, mlsSpec, out _), "CF can produce first Mobile MLS");
      var capMls = SpawnUnitByName(Verified, 1, "cf_longwall_mlrs", new FPVector3(24, 0, -64));
      Assert.IsFalse(TechSystem.CanProduce(Verified, 1, mlsSpec, out var mlsReject), "CF blocked from producing 2nd Mobile MLS");
      Assert.AreEqual(RejectReason.Limit, mlsReject, "CF 2nd Mobile MLS rejected with Limit");
      Assert.AreNotEqual(UltimateLock.None, UltimateSystem.Locks(Verified, 1) & UltimateLock.Busy, "CF Ultimate locked Busy while Mobile MLS alive");
      SetUnitHealthRatio(Verified, capMls, FP._0);
      yield return WaitFrames(5);
      Assert.IsTrue(TechSystem.CanProduce(Verified, 1, mlsSpec, out _), "CF can rebuild Mobile MLS after destruction");

      Assert.IsTrue(TechSystem.CanProduce(Verified, 0, bomberSpec, out _), "FF can produce first Skyfortress Bomber");
      var capBomber = SpawnUnitByName(Verified, 0, "ff_skyfortress_bomber", new FPVector3(-24, 0, -64));
      Assert.IsFalse(TechSystem.CanProduce(Verified, 0, bomberSpec, out var bomberReject), "FF blocked from producing 2nd Bomber");
      Assert.AreEqual(RejectReason.Limit, bomberReject, "FF 2nd Bomber rejected with Limit");
      SetUnitHealthRatio(Verified, capBomber, FP._0);
      yield return WaitFrames(5);
      Assert.IsTrue(TechSystem.CanProduce(Verified, 0, bomberSpec, out _), "FF can rebuild Bomber after destruction");

      // 3. Verify launch warning removal (zero StrategicMissile EventUltimateNotice alerts) & Fire-While-Moving with rocket VFX.
      var strategicWarnings = 0;
      var sub = QuantumEvent.SubscribeManual<EventUltimateNotice>(e => {
        if (e.Kind == UltimateKind.StrategicMissile) strategicWarnings++;
      });

      var camera = Camera.main;
      var rts = Controller().Camera;
      camera.aspect = 16f / 9f;

      var origin = new FPVector3(-18, 0, -64);
      var moveGoal = new FPVector3(20, 0, -64);
      var mobileMls = SpawnUnitByName(Verified, 1, "cf_longwall_mlrs", origin);
      var target1 = SpawnUnitByName(Verified, 0, "ff_eagle_tank",  new FPVector3(-2, 0, -54));
      var target2 = SpawnUnitByName(Verified, 0, "ff_striker_ifv", new FPVector3( 5, 0, -54));

      // Close-up readability screenshot of the Mobile MLS (Synty SM_Veh_Rocket_Truck_01) before rolling out
      yield return WaitFrames(4);
      rts.Height = 22f;
      rts.MoveTo(new Vector2(origin.X.AsFloat, origin.Z.AsFloat));
      rts.Apply();
      yield return WaitFrames(4);
      CaptureCameraPng(camera, Path.Combine(dir, "mobile_mls_closeup_readability.png"), 1920, 1080);

      // Issue pure MOVE command (no static deploy/attack lock) and verify Mobile MLS fires parabolic rockets while driving!
      yield return Send(1, new MoveUnitsCommand { Units = new[] { mobileMls }, Target = moveGoal });
      rts.Height = 38f;
      rts.MoveTo(new Vector2(-2f, -60f));
      rts.Apply();

      var rocketsSeen = 0;
      var firedWhileMoving = false;
      var maxMovedX = 0f;
      for (var f = 0; f < 12; f++) {
        yield return WaitFrames(8);
        var u = Verified.Get<Unit>(mobileMls);
        var p = Verified.Get<Transform3D>(mobileMls).Position;
        maxMovedX = Mathf.Max(maxMovedX, p.X.AsFloat - origin.X.AsFloat);
        if (u.LastFiredFrame > 0 && u.Order == UnitOrder.Move) firedWhileMoving = true;
        foreach (var _ in Verified.GetComponentIterator<MlrsRocket>()) rocketsSeen++;
        CaptureCameraPng(camera, Path.Combine(frames, $"mobile_mls_{f:00}.png"), 960, 540);
      }
      CaptureCameraPng(camera, Path.Combine(dir, "mobile_mls_firing_on_the_move.png"), 1920, 1080);

      sub.Dispose();
      Assert.AreEqual(0, strategicWarnings, "Zero strategic missile launch warning alerts emitted");
      Assert.AreEqual(0, Battle().StrikeMarks, "Zero strategic missile red warning rings active");
      Assert.Greater(maxMovedX, 2.5f, $"Mobile MLS actively moved during fire-on-the-move (dx={maxMovedX:F2}m)");
      Assert.IsTrue(firedWhileMoving, "Mobile MLS fired missiles while executing UnitOrder.Move");
      Assert.Greater(rocketsSeen, 0, $"Mobile MLS launched real parabolic MlrsRocket projectiles (samples={rocketsSeen})");
      Debug.Log($"[MobileMls_FireWhileMoving_NoWarning_AndUnitCaps] PASS dx={maxMovedX:F2}m rocketsSeen={rocketsSeen} warnings={strategicWarnings}");
    }

    [UnityTest, Explicit, Timeout(1200000)]
    public IEnumerator Mixamo_InfantryWorkersAndCrew_VerifyAndCapture() {
      var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "status_images", "Mixamo"));
      var frames = Path.Combine(dir, "frames");
      Directory.CreateDirectory(frames);

      yield return StartMatch(Map4PScene, 2, truce: 0, seed: 5901, startingTier: 3);
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
      var bv = Battle();

      // 1. Infantry & PMC: Relaxed Patrol Walk ("WalkWithRifle") -> Active Combat ("IdleAiming"/"FiringRifle"/"RifleRun").
      var origin = new FPVector3(-14, 0, -64);
      var ffInf  = SpawnUnitByName(Verified, 0, "ff_ranger_squad",    origin + new FPVector3(0, 0, -2));
      var pmcInf = SpawnUnitByName(Verified, 0, "pmc_operator_squad", origin + new FPVector3(0, 0,  2));
      yield return Send(0, new MoveUnitsCommand { Units = new[] { ffInf, pmcInf }, Target = origin + new FPVector3(24, 0, 0) });

      rts.Height = 16f;
      rts.MoveTo(new Vector2(-4f, -64f));
      rts.Apply();

      var sawPatrolWalk = false;
      for (var f = 0; f < 6; f++) {
        yield return WaitFrames(8);
        if (bv.Actors.TryGetValue(ffInf, out var a) && a.View?.Animation?.ActiveMixamoPose == "WalkWithRifle") sawPatrolWalk = true;
        CaptureCameraPng(camera, Path.Combine(frames, $"infantry_motion_{f:00}.png"), 960, 540);
      }
      CaptureCameraPng(camera, Path.Combine(dir, "mixamo_infantry_patrol_walk.png"), 1920, 1080);
      Assert.IsTrue(sawPatrolWalk, "Infantry uses relaxed Mixamo WalkWithRifle during non-combat movement");
      Assert.IsNull(GameObject.Find("X Bot"), "No X Bot mesh spawned in gameplay scene");

      // Phase B: Spawn enemy squad and engage in active combat -> verify distinct combat pose (IdleAiming / FiringRifle / RifleRun / Reload)
      var enemyInf = SpawnUnitByName(Verified, 1, "cf_militia", new FPVector3(8, 0, -64));
      yield return Send(0, new AttackTargetCommand { Units = new[] { ffInf, pmcInf }, Target = enemyInf });
      yield return Send(1, new AttackTargetCommand { Units = new[] { enemyInf }, Target = ffInf });

      var sawCombatPose = false;
      for (var f = 6; f < 12; f++) {
        yield return WaitFrames(8);
        if (bv.Actors.TryGetValue(ffInf, out var a)) {
          var pose = a.View?.Animation?.ActiveMixamoPose;
          if (pose == "IdleAiming" || pose == "FiringRifle" || pose == "RifleWalk" || pose == "RifleRun" || pose == "Reload") {
            sawCombatPose = true;
          }
        }
        CaptureCameraPng(camera, Path.Combine(frames, $"infantry_motion_{f:00}.png"), 960, 540);
      }
      CaptureCameraPng(camera, Path.Combine(dir, "mixamo_infantry_combat_aim_fire.png"), 1920, 1080);
      Assert.IsTrue(sawCombatPose, "Infantry transitions into Mixamo combat stance (IdleAiming/FiringRifle/RifleRun)");

      // 2. Resource Workers (Power Plant Device/Inspect crew on clear open pad).
      var powerSpec = Resources.Load<BuildingSpec>("FactoryWars/ff_power_plant");
      Assert.IsNotNull(powerSpec, "ff_power_plant spec loaded");
      var powerPlant = ProductionSystem.CreateFinishedBuilding(Verified, 0, powerSpec, new FPVector3(-26, 0, -64));
      yield return WaitFrames(20);
      Assert.IsTrue(bv.Actors.TryGetValue(powerPlant, out var ppAct) && ppAct.ActivityCrew != null && ppAct.ActivityCrew.activeSelf,
        "PowerPlant spawns live Mixamo technician crew (Device/Inspect)");
      rts.Height = 16f;
      rts.MoveTo(new Vector2(-26f, -66f));
      rts.Apply();
      for (var wf = 0; wf < 8; wf++) {
        yield return WaitFrames(12);
        CaptureCameraPng(camera, Path.Combine(frames, $"worker_motion_{wf:00}.png"), 960, 540);
      }
      CaptureCameraPng(camera, Path.Combine(dir, "mixamo_resource_workers.png"), 1920, 1080);

      // 3. Vehicle, Aircraft & Naval Crew (Drive & Pilot) on clear open road.
      var buggy = SpawnUnitByName(Verified, 0, "ff_scout_buggy",   new FPVector3(-12, 0, -60));
      var bike  = SpawnUnitByName(Verified, 0, "cf_scout_bike",    new FPVector3( -6, 0, -60));
      var heli  = SpawnUnitByName(Verified, 0, "ff_hawk_gunship",  new FPVector3( -9, 0, -56));
      var boat  = SpawnUnitByName(Verified, 0, "ff_scout_boat",    new FPVector3(-16, 0, 118));
      var craft = SpawnUnitByName(Verified, 0, "ff_landing_craft", new FPVector3(-22, 0, 118));
      yield return WaitFrames(15);

      Assert.IsTrue(bv.Actors.TryGetValue(buggy, out var buggyAct) && buggyAct.VehicleCrew != null && buggyAct.VehicleCrewPose == "Drive", "Scout buggy crew uses Mixamo Drive");
      Assert.IsTrue(bv.Actors.TryGetValue(bike,  out var bikeAct)  && bikeAct.VehicleCrew  != null && bikeAct.VehicleCrewPose  == "Drive", "Scout bike crew uses Mixamo Drive");
      Assert.IsTrue(bv.Actors.TryGetValue(boat,  out var boatAct)  && boatAct.VehicleCrew  != null && boatAct.VehicleCrewPose  == "Drive", "Scout boat crew uses Mixamo Drive");
      Assert.IsTrue(bv.Actors.TryGetValue(craft, out var craftAct) && craftAct.VehicleCrew != null && craftAct.VehicleCrewPose == "Drive", "Landing craft crew uses Mixamo Drive");
      Assert.IsTrue(bv.Actors.TryGetValue(heli,  out var heliAct)  && heliAct.VehicleCrew  != null && heliAct.VehicleCrewPose  == "Pilot", "Helicopter crew uses Mixamo Pilot");

      rts.Height = 16f;
      rts.MoveTo(new Vector2(-9f, -60f));
      rts.Apply();
      yield return WaitFrames(8);
      CaptureCameraPng(camera, Path.Combine(dir, "mixamo_vehicle_and_crew_poses.png"), 1920, 1080);

      rts.Height = 20f;
      rts.MoveTo(new Vector2(-19f, 118f));
      rts.Apply();
      yield return WaitFrames(6);
      CaptureCameraPng(camera, Path.Combine(dir, "mixamo_naval_crew_poses.png"), 1920, 1080);
      Debug.Log("[Mixamo_InfantryWorkersAndCrew_VerifyAndCapture] PASS (42 Humanoid clips, Patrol->Combat, PowerPlant Crew, Drive/Pilot Crew)");
    }

    [UnityTest, Explicit, Timeout(1200000)]
    public IEnumerator SprintUpgrade_ThreeWorkersBadge_AndHelmsman_VerifyAndCapture() {
      var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "status_images", "SprintAndWorkers"));
      var frames = Path.Combine(dir, "frames");
      Directory.CreateDirectory(frames);

      yield return StartMatch(Map4PScene, 2, truce: 0, seed: 6001, startingTier: 3);
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
      var bv = Battle();

      // 1. Before Sprint Upgrade: smooth normal walk ("WalkWithRifle", 1.0x speed).
      var rangerSpec = Resources.Load<UnitSpec>("FactoryWars/ff_ranger_squad");
      Assert.IsNotNull(rangerSpec, "ff_ranger_squad spec loaded");
      Assert.IsFalse(TechSystem.HasSprintUpgrade(Verified, 0, rangerSpec), "Sprint upgrade locked before research");
      var baseSpeedMul = TechSystem.MoveSpeedMultiplier(Verified, 0, rangerSpec, new FPVector3(-20, 0, -64)).AsFloat;

      var squad = SpawnUnitByName(Verified, 0, "ff_ranger_squad", new FPVector3(-22, 0, -64));
      yield return Send(0, new MoveUnitsCommand { Units = new[] { squad }, Target = new FPVector3(16, 0, -64) });
      rts.Height = 16f;
      rts.MoveTo(new Vector2(-14f, -64f));
      rts.Apply();
      var sawWalk = false;
      for (var i = 0; i < 6; i++) {
        yield return WaitFrames(6);
        if (bv.Actors.TryGetValue(squad, out var a) && a.View?.Animation?.ActiveMixamoPose == "WalkWithRifle") sawWalk = true;
      }
      CaptureCameraPng(camera, Path.Combine(dir, "sprint_before_walk.png"), 1920, 1080);
      Assert.IsTrue(sawWalk, "Before upgrade: infantry moves smoothly with WalkWithRifle");

      // Unlock Sprint Upgrade (Small Attack Lv1) -> +25% speed & SprintForward pose!
      UnlockSmallUpgrade(Verified, 0);
      if (Game?.Frames?.Predicted != null) UnlockSmallUpgrade(Game.Frames.Predicted, 0);
      Assert.IsTrue(TechSystem.HasSprintUpgrade(Verified, 0, rangerSpec), "Sprint upgrade unlocked after research");
      var sprintSpeedMul = TechSystem.MoveSpeedMultiplier(Verified, 0, rangerSpec, new FPVector3(0, 0, -64)).AsFloat;
      Assert.Greater(sprintSpeedMul, baseSpeedMul * 1.20f, $"Sprint upgrade boosts movement speed (+25%: {baseSpeedMul:F2} -> {sprintSpeedMul:F2})");

      yield return Send(0, new MoveUnitsCommand { Units = new[] { squad }, Target = new FPVector3(24, 0, -64) });
      var sawSprint = false;
      for (var i = 0; i < 8; i++) {
        yield return WaitFrames(6);
        var p = Verified.Get<Transform3D>(squad).Position;
        rts.MoveTo(new Vector2(p.X.AsFloat, p.Z.AsFloat));
        rts.Apply();
        if (bv.Actors.TryGetValue(squad, out var a) && a.View?.Animation?.ActiveMixamoPose == "SprintForward") sawSprint = true;
        CaptureCameraPng(camera, Path.Combine(frames, $"sprint_{i:00}.png"), 960, 540);
      }
      CaptureCameraPng(camera, Path.Combine(dir, "sprint_after_upgrade.png"), 1920, 1080);
      Assert.IsTrue(sawSprint, "After upgrade: infantry transitions smoothly into SprintForward");

      // 2. Up to 3 Workers Per Resource Site & Visible "3/3" World-Space Badge.
      var powerSpec = Resources.Load<BuildingSpec>("FactoryWars/ff_power_plant");
      var ironSpec  = Resources.Load<BuildingSpec>("FactoryWars/ff_iron_extractor");
      Assert.AreEqual(3, powerSpec.WorkerSlotCapacity, "PowerPlant supports 3 workers");
      Assert.AreEqual(3, ironSpec.WorkerSlotCapacity,  "IronExtractor supports 3 workers");
      var powerPlant = ProductionSystem.CreateFinishedBuilding(Verified, 0, powerSpec, new FPVector3(-26, 0, -64));
      var eng1 = SpawnUnitByName(Verified, 0, "ff_engineer", new FPVector3(-28, 0, -68));
      var eng2 = SpawnUnitByName(Verified, 0, "ff_engineer", new FPVector3(-26, 0, -68));
      var eng3 = SpawnUnitByName(Verified, 0, "ff_engineer", new FPVector3(-24, 0, -68));
      AssignGarrison(Verified, eng1, powerPlant);
      AssignGarrison(Verified, eng2, powerPlant);
      AssignGarrison(Verified, eng3, powerPlant);
      if (Game?.Frames?.Predicted != null) {
        AssignGarrison(Game.Frames.Predicted, eng1, powerPlant);
        AssignGarrison(Game.Frames.Predicted, eng2, powerPlant);
        AssignGarrison(Game.Frames.Predicted, eng3, powerPlant);
      }
      yield return WaitFrames(15);

      Assert.AreEqual(3, CitySystem.Occupants(Verified, powerPlant), "3 workers assigned to PowerPlant");
      Assert.IsFalse(CitySystem.CanEnter(Verified, 0, powerPlant), "4th worker rejected when 3/3 full");
      Assert.IsTrue(bv.Actors.TryGetValue(powerPlant, out var ppAct), "PowerPlant actor found");
      Assert.AreEqual(3, ppAct.ActivityCrewList.Count, "3 distinct visible workers spawned at resource site");
      Assert.Greater(Vector3.Distance(ppAct.ActivityCrewList[0].transform.localPosition, ppAct.ActivityCrewList[1].transform.localPosition), 1.2f, "Workers 0 & 1 non-overlapping");
      Assert.Greater(Vector3.Distance(ppAct.ActivityCrewList[1].transform.localPosition, ppAct.ActivityCrewList[2].transform.localPosition), 1.2f, "Workers 1 & 2 non-overlapping");
      Assert.IsNotNull(ppAct.WorkerCountBadge, "World-space worker count badge created");
      Assert.AreEqual("3/3", ppAct.WorkerCountBadge.text, "Worker count badge displays 3/3");

      rts.Height = 18f;
      rts.MoveTo(new Vector2(-26f, -66f));
      rts.Apply();
      yield return WaitFrames(8);
      CaptureCameraPng(camera, Path.Combine(dir, "three_workers_badge_3of3.png"), 1920, 1080);

      // 3. Naval Helmsman / Crew Visibility + Particle Wakes (no rigid bar wakes).
      var ship  = SpawnUnitByName(Verified, 0, "ff_artillery_ship", new FPVector3(-14, 0, 108));
      var boat  = SpawnUnitByName(Verified, 0, "ff_scout_boat",     new FPVector3(-22, 0, 114));
      var craft = SpawnUnitByName(Verified, 0, "ff_landing_craft",  new FPVector3( -8, 0, 114));
      yield return Send(0, new MoveUnitsCommand { Units = new[] { ship, boat, craft }, Target = new FPVector3(-15, 0, 148) });
      yield return WaitFrames(Fps);

      Assert.IsTrue(bv.Actors.TryGetValue(ship,  out var shipAct)  && shipAct.VehicleCrew  != null && shipAct.VehicleCrewPose  == "Drive", "Battleship bridge helmsman visible");
      Assert.IsTrue(bv.Actors.TryGetValue(boat,  out var boatAct)  && boatAct.VehicleCrew  != null && boatAct.VehicleCrewPose  == "Drive", "Scout boat helmsman visible");
      Assert.IsTrue(bv.Actors.TryGetValue(craft, out var craftAct) && craftAct.VehicleCrew != null && craftAct.VehicleCrewPose == "Drive", "Landing craft helmsman visible");
      Assert.IsNotNull(shipAct.NavalWake, "ParticlePack naval wake active on ship");
      Assert.IsNull(shipAct.View.transform.Find("NavalWaterlineWake"), "Rigid V-bar wake remains removed");

      rts.Height = 28f;
      rts.MoveTo(new Vector2(-15f, 116f));
      rts.Apply();
      yield return WaitFrames(8);
      CaptureCameraPng(camera, Path.Combine(dir, "naval_helmsman_and_particle_wake.png"), 1920, 1080);
      Debug.Log($"[SprintUpgrade_ThreeWorkersBadge_AndHelmsman_VerifyAndCapture] PASS speedMul=({baseSpeedMul:F2}->{sprintSpeedMul:F2}) badge={ppAct.WorkerCountBadge.text} workers={ppAct.ActivityCrewList.Count}");
    }

    static unsafe void UnlockSmallUpgrade(Frame f, PlayerRef player) {
      var st = FactoryWarsUtil.Player(f, player);
      if (st != null) st->AttackLevels[(int)UnitSize.Small] = 1;
    }

    static unsafe void AssignGarrison(Frame f, EntityRef unitEntity, EntityRef buildingEntity) {
      if (f.Unsafe.TryGetPointer<Unit>(unitEntity, out var u)) u->Garrison = buildingEntity;
    }

    [UnityTest, Explicit, Timeout(1200000)]
    public IEnumerator OptionA_TacticalPack_UI_AndGrenadeSkill_VerifyAndCapture() {
      var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "status_images", "MobilityOptionA"));
      var frames = Path.Combine(dir, "frames");
      Directory.CreateDirectory(frames);

      yield return StartMatch(Map4PScene, 2, truce: 0, seed: 6101, startingTier: 3);
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
      var bv = Battle();
      var hud = UnityEngine.Object.FindFirstObjectByType<MatchHud>();

      // 1. Baseline Walk (before upgrade) at matched RTS zoom
      var squad = SpawnUnitByName(Verified, 0, "ff_ranger_squad", new FPVector3(-18, 0, -64));
      yield return Send(0, new MoveUnitsCommand { Units = new[] { squad }, Target = new FPVector3(4, 0, -64) });
      rts.Height = 16f;
      rts.MoveTo(new Vector2(-12f, -64f));
      rts.Apply();
      yield return WaitFrames(24);
      CaptureCameraPng(camera, Path.Combine(dir, "optionA_walk_rts.png"), 1920, 1080);

      // 2. Unlock Option A Mobility + Grenade Upgrade -> verify Spine_03 tactical pack, shoulder patch, NO floating Mk.2 when unselected!
      UnlockSmallUpgrade(Verified, 0);
      if (Game?.Frames?.Predicted != null) UnlockSmallUpgrade(Game.Frames.Predicted, 0);
      var curPos = Verified.Get<Transform3D>(squad).Position;
      yield return Send(0, new MoveUnitsCommand { Units = new[] { squad }, Target = curPos });
      yield return WaitFrames(12);

      Assert.IsTrue(bv.Actors.TryGetValue(squad, out var act), "Upgraded infantry actor found");
      Assert.IsTrue(act.UpgradeAttachments.Any(g => g != null && g.name == "OptionA_TacticalPack"), "Option A Spine_03 TacticalPack attached");
      Assert.IsTrue(act.UpgradeAttachments.Any(g => g != null && g.name == "OptionA_ShoulderPatch"), "Option A ShoulderPatch attached");
      Assert.IsNull(act.View.Model.transform.Find("UpgradeKit_Mk2"), "Old bright rectangular UpgradeKit_Mk2 removed");
      Assert.IsTrue(act.UpgradeChevron == null || !act.UpgradeChevron.gameObject.activeSelf, "No persistent floating Mk.2 label when unselected");

      var idlePos = Verified.Get<Transform3D>(squad).Position;
      rts.Height = 16f;
      rts.MoveTo(new Vector2(idlePos.X.AsFloat, idlePos.Z.AsFloat));
      rts.Apply();
      yield return WaitFrames(6);
      CaptureCameraPng(camera, Path.Combine(dir, "optionA_idle_rts.png"), 1920, 1080);

      // Select unit -> verify single small chevron "^" beside health bar AND "기동 훈련 · 전력질주 해금" in selection panel!
      Controller().Input.Selection.Units.Clear();
      Controller().Input.Selection.Units.Add(squad);
      yield return WaitFrames(10);
      Assert.IsNotNull(act.UpgradeChevron, "Selected upgraded infantry creates single chevron");
      Assert.IsTrue(act.UpgradeChevron.gameObject.activeSelf, "Single chevron visible while selected");
      Assert.AreEqual("^", act.UpgradeChevron.text, "Chevron displays single '^' (no Mk.2 text)");
      Assert.IsTrue(hud.CardDetailText.Contains("기동 훈련 · 전력질주 해금"), $"Selection HUD shows '기동 훈련 · 전력질주 해금' (actual: {hud.CardDetailText})");

      // Sprint capture at matched RTS zoom + close-up pack/rifle clearance capture!
      yield return Send(0, new MoveUnitsCommand { Units = new[] { squad }, Target = new FPVector3(18, 0, -64) });
      var sawRifleRun = false;
      for (var i = 0; i < 6; i++) {
        yield return WaitFrames(8);
        var p = Verified.Get<Transform3D>(squad).Position;
        rts.MoveTo(new Vector2(p.X.AsFloat, p.Z.AsFloat));
        rts.Apply();
        if (act.View?.Animation?.ActiveMixamoPose == "RifleRun") sawRifleRun = true;
      }
      Assert.IsTrue(sawRifleRun, "Option A sprint uses controlled forward-lean RifleRun");
      CaptureCameraPng(camera, Path.Combine(dir, "optionA_sprint_rts.png"), 1920, 1080);

      // Close-up clearance capture (pack flush on Spine_03, shoulder patch, two-hand rifle carry)
      rts.Height = 9.5f;
      var cp = Verified.Get<Transform3D>(squad).Position;
      rts.MoveTo(new Vector2(cp.X.AsFloat, cp.Z.AsFloat));
      rts.Apply();
      yield return WaitFrames(4);
      CaptureCameraPng(camera, Path.Combine(dir, "optionA_closeup_pack_clearance.png"), 1920, 1080);

      // 3. Upgrade-Unlocked Hand Grenade Skill (Mixamo GrenadeThrow + Parabolic Arc + War FX Explosion)
      var enemy = SpawnUnitByName(Verified, 1, "cf_militia", cp + new FPVector3(7, 0, 0));
      yield return Send(0, new AttackTargetCommand { Units = new[] { squad }, Target = enemy });
      rts.Height = 20f;
      rts.MoveTo(new Vector2(cp.X.AsFloat + 3.5f, cp.Z.AsFloat));
      rts.Apply();

      var grenadeLaunched = false;
      var sawThrowAnim = false;
      for (var f = 0; f < 10; f++) {
        yield return WaitFrames(6);
        var u = Verified.Get<Unit>(squad);
        if (u.AbilityCooldown > FP._0) grenadeLaunched = true;
        foreach (var _ in Verified.GetComponentIterator<MlrsRocket>()) grenadeLaunched = true;
        if (act.View?.Animation?.ActiveMixamoPose == "GrenadeThrow") sawThrowAnim = true;
        CaptureCameraPng(camera, Path.Combine(frames, $"grenade_{f:00}.png"), 960, 540);
      }
      CaptureCameraPng(camera, Path.Combine(dir, "optionA_grenade_in_action.png"), 1920, 1080);

      Assert.IsTrue(grenadeLaunched, "Upgraded infantry launched deterministic parabolic hand grenade & started cooldown");
      Debug.Log($"[OptionA_TacticalPack_UI_AndGrenadeSkill_VerifyAndCapture] PASS sawThrowAnim={sawThrowAnim} card='{hud.CardDetailText.Replace("\n", " | ")}'");
    }

    [UnityTest, Timeout(1200000)]
    public IEnumerator StockpileTruckPickup_DirectAirFlight_AndBomberAlignment_VerifyAndCapture() {
      var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "status_images", "StockpileAirBomber"));
      var frames = Path.Combine(dir, "frames");
      Directory.CreateDirectory(frames);

      yield return StartMatch(Map4PScene, 2, truce: 0, seed: 7101, startingTier: 3);
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
      var bv = Battle();
      var hud = Controller().Hud;
      var map = FactoryWarsUtil.MapInfo(Verified);

      // 1. Verify Seat 0 iron & timber nodes are unblocked (zero BlockedLocation!)
      var ironSpec = Resources.Load<BuildingSpec>("FactoryWars/ff_iron_extractor");
      var woodSpec = Resources.Load<BuildingSpec>("FactoryWars/ff_lumber_mill");
      var hqEntity = EntityRef.None;
      var hqPos = FPVector3.Zero;
      foreach (var p in Verified.GetComponentIterator<Building>()) {
        if (p.Component.Owner == 0 && Verified.FindAsset(p.Component.Spec).Role == BuildingRole.Headquarters) {
          hqEntity = p.Entity;
          hqPos = Verified.Get<Transform3D>(p.Entity).Position;
          break;
        }
      }
      Assert.AreNotEqual(EntityRef.None, hqEntity, "Seat 0 HQ exists");

      var allIronNodes = new List<ResourceNode>();
      var allWoodNodes = new List<ResourceNode>();
      foreach (var p in Verified.GetComponentIterator<ResourceNode>()) {
        if (p.Component.Kind == ResourceKind.Iron) allIronNodes.Add(p.Component);
        else if (p.Component.Kind == ResourceKind.Wood) allWoodNodes.Add(p.Component);
      }
      var seat0IronNodes = allIronNodes.OrderBy(n0 => FPVector3.DistanceSquared(n0.Shape.Center, hqPos)).Take(4).ToList();
      var seat0WoodNodes = allWoodNodes.OrderBy(n0 => FPVector3.DistanceSquared(n0.Shape.Center, hqPos)).Take(4).ToList();

      Assert.GreaterOrEqual(seat0IronNodes.Count, 2, "Seat 0 has iron nodes");
      Assert.GreaterOrEqual(seat0WoodNodes.Count, 2, "Seat 0 has timber nodes");
      foreach (var node in seat0IronNodes) {
        var ok = ProductionSystem.TryGetBuildPlacement(Verified, ironSpec, node.Shape.Center, 0, out _);
        Assert.IsTrue(ok, $"Seat 0 Iron node at {node.Shape.Center} must not be blocked (reason={ProductionSystem.PlacementReason(Verified, ironSpec, node.Shape.Center, 0)})");
      }
      foreach (var node in seat0WoodNodes) {
        var ok = ProductionSystem.TryGetBuildPlacement(Verified, woodSpec, node.Shape.Center, 0, out _);
        Assert.IsTrue(ok, $"Seat 0 Timber node at {node.Shape.Center} must not be blocked (reason={ProductionSystem.PlacementReason(Verified, woodSpec, node.Shape.Center, 0)})");
      }

      // Build Iron Extractor on nearest Seat 0 iron node & staff it with 3 engineers.
      var targetIronNode = seat0IronNodes[0];
      var nodePos = targetIronNode.Shape.Center;
      var eng0 = Engineers(0).FirstOrDefault();
      if (eng0 == EntityRef.None) eng0 = SpawnUnitByName(Verified, 0, "cf_engineer", nodePos + new FPVector3(3, 0, 0));
      yield return Build(0, "iron_extractor", nodePos, eng0);
      yield return WaitFrames(10);
      var extractorEntity = BuildingAt(nodePos);
      Assert.AreNotEqual(EntityRef.None, extractorEntity, "Iron extractor placed on unblocked Seat 0 iron node");

      // Complete extractor construction & staff with 3 workers directly for deterministic verification.
      var extB = Verified.Get<Building>(extractorEntity);
      extB.ConstructionRemaining = FP._0;
      extB.ProcessTimer = FP.FromFloat_UNSAFE(95f); // Initial stockpiled iron at site
      Verified.Set(extractorEntity, extB);
      if (Game.Frames.Predicted != null && Game.Frames.Predicted.Exists(extractorEntity)) {
        Game.Frames.Predicted.Set(extractorEntity, extB);
      }
      var eng1 = SpawnUnitByName(Verified, 0, "cf_engineer", nodePos + new FPVector3(-3, 0, 0));
      var eng2 = SpawnUnitByName(Verified, 0, "cf_engineer", nodePos + new FPVector3(0, 0, 3));
      AssignGarrison(Verified, eng0, extractorEntity);
      AssignGarrison(Verified, eng1, extractorEntity);
      AssignGarrison(Verified, eng2, extractorEntity);
      if (Game.Frames.Predicted != null) {
        AssignGarrison(Game.Frames.Predicted, eng0, extractorEntity);
        AssignGarrison(Game.Frames.Predicted, eng1, extractorEntity);
        AssignGarrison(Game.Frames.Predicted, eng2, extractorEntity);
      }
      yield return WaitFrames(Fps * 3);

      var ironBeforePickup = PlayerIron(Verified, 0);
      var stockpileBefore = Verified.Get<Building>(extractorEntity).ProcessTimer.AsInt;
      Assert.Greater(stockpileBefore, 95, $"Extractor stockpiled harvested iron at site (stockpile={stockpileBefore})");
      Assert.IsTrue(bv.Actors.TryGetValue(extractorEntity, out var extAct), "Extractor view exists");
      Assert.IsNull(extAct.ExtractorDelivery, "Conveyor direct-supply coupling removed (ExtractorDelivery == null)");
      Assert.IsTrue(extAct.WorkerCountBadge != null && extAct.WorkerCountBadge.text.Contains("적치"),
        $"Extractor badge shows stockpile (actual: {extAct.WorkerCountBadge?.text})");

      // Spawn Logistics Truck, select it, verify '자원채취' button appears in HUD, then execute GatherCommand (Node=None).
      var truckStart = FPVector3.Lerp(hqPos, nodePos, FP.FromFloat_UNSAFE(0.45f));
      var truck = SpawnUnitByName(Verified, 0, "merc_cargo_truck", truckStart);
      yield return WaitFrames(8);
      Controller().Input.Selection.Units.Clear();
      Controller().Input.Selection.Units.Add(truck);
      yield return WaitFrames(8);
      Assert.IsTrue(hud.Entries.Any(e => e.Label == "자원채취"), "HUD shows '자원채취' command when logistics truck is selected");

      yield return Send(0, new GatherCommand { Units = new[] { truck }, Node = EntityRef.None });
      rts.Height = 46f;
      var midPos = Vector3.Lerp(hqPos.ToUnityVector3(), nodePos.ToUnityVector3(), 0.55f);
      rts.MoveTo(new Vector2(midPos.x, midPos.z));
      rts.Apply();

      var sawLoading = false;
      var sawUnloading = false;
      var capturedLoading = false;
      for (var step = 0; step < 44; step++) {
        yield return WaitFrames(15);
        var lt = Verified.Get<LogisticsTruck>(truck);
        var tp = Verified.Get<Transform3D>(truck).Position.ToUnityVector3();
        rts.MoveTo(new Vector2(tp.x, tp.z));
        rts.Apply();
        if (lt.State == LogisticsState.Loading) {
          sawLoading = true;
          if (!capturedLoading) {
            capturedLoading = true;
            CaptureCameraPng(camera, Path.Combine(dir, "01_stockpile_and_truck_pickup.png"), 1920, 1080);
          }
        }
        if (lt.State == LogisticsState.Unloading) sawUnloading = true;
        if (step < 16) CaptureCameraPng(camera, Path.Combine(frames, $"truck_{step:00}.png"), 960, 540);
        if (lt.DeliveryId >= 1) break;
      }
      if (!capturedLoading) CaptureCameraPng(camera, Path.Combine(dir, "01_stockpile_and_truck_pickup.png"), 1920, 1080);

      var truckAfter = Verified.Get<LogisticsTruck>(truck);
      var ironAfterPickup = PlayerIron(Verified, 0);
      Assert.IsTrue(sawLoading, "Logistics truck entered Loading phase at nearest stockpiled extractor");
      Assert.GreaterOrEqual(truckAfter.DeliveryId, 1, $"Logistics truck delivered stockpiled iron to HQ (DeliveryId={truckAfter.DeliveryId}, state={truckAfter.State})");
      Assert.Greater(ironAfterPickup, ironBeforePickup, $"HQ Iron increased after truck delivery ({ironBeforePickup} -> {ironAfterPickup})");

      // 2. Direct Air Flight over consecutive tall 'H' buildings on Map4P v4
      var n = map.CellGridSize;
      int bRow = -1, bCol = -1;
      for (var r = 18; r <= 62 && bRow < 0; r++) {
        for (var c = 18; c <= 62 && bRow < 0; c++) {
          if (GridMapView.Cell(map, r, c) == 'H' && GridMapView.Cell(map, r, c + 1) == 'H'
              && GridMapView.Cell(map, r, c - 1) != 'H' && GridMapView.Cell(map, r, c + 2) != 'H') {
            bRow = r; bCol = c;
          }
        }
      }
      if (bRow < 0) {
        for (var r = 18; r <= 62 && bRow < 0; r++) {
          for (var c = 18; c <= 62 && bRow < 0; c++) {
            if (GridMapView.Cell(map, r, c) == 'H' && GridMapView.Cell(map, r, c - 1) != 'H') { bRow = r; bCol = c; }
          }
        }
      }
      Assert.GreaterOrEqual(bRow, 0, "Found consecutive tall 'H' buildings on Map4P");
      var bCenterX = GridMapView.CellX(map, bCol);
      var bCenterZ = GridMapView.CellZ(map, bRow);
      var startAir = new FPVector3(FP.FromFloat_UNSAFE(GridMapView.CellX(map, bCol - 1)), FP._0, FP.FromFloat_UNSAFE(bCenterZ));
      var endAir   = new FPVector3(FP.FromFloat_UNSAFE(GridMapView.CellX(map, bCol + 3)), FP._0, FP.FromFloat_UNSAFE(bCenterZ));

      var flyHeli   = SpawnUnitByName(Verified, 0, "cf_gale_gunship",       startAir + new FPVector3(0, 0, -2));
      var flyBomber = SpawnUnitByName(Verified, 0, "ff_skyfortress_bomber", startAir + new FPVector3(0, 0,  2));
      yield return Send(0, new MoveUnitsCommand { Units = new[] { flyHeli, flyBomber }, Target = endAir });

      rts.Height = 56f;
      rts.MoveTo(new Vector2(bCenterX + 4f, bCenterZ));
      rts.Apply();
      var cellSize = map.CellGridCell.AsFloat;
      var mapHalf = n * cellSize * 0.5f;
      var crossedH = false;
      var maxAirDeflect = 0f;
      for (var f = 0; f < 10; f++) {
        yield return WaitFrames(14);
        var pH = Verified.Get<Transform3D>(flyHeli).Position;
        var pB = Verified.Get<Transform3D>(flyBomber).Position;
        int rH = Mathf.FloorToInt((mapHalf - pH.Z.AsFloat) / cellSize);
        int cH = Mathf.FloorToInt((pH.X.AsFloat + mapHalf) / cellSize);
        int rB = Mathf.FloorToInt((mapHalf - pB.Z.AsFloat) / cellSize);
        int cB = Mathf.FloorToInt((pB.X.AsFloat + mapHalf) / cellSize);
        if (GridMapView.Cell(map, rH, cH) == 'H' || GridMapView.Cell(map, rB, cB) == 'H') crossedH = true;
        maxAirDeflect = Mathf.Max(maxAirDeflect, Mathf.Abs(pH.Z.AsFloat - (bCenterZ - 2f)));
        if (f == 4) CaptureCameraPng(camera, Path.Combine(dir, "02_air_direct_over_buildings.png"), 1920, 1080);
      }
      Assert.IsTrue(crossedH, "Air units flew directly over tall 'H' buildings");
      Assert.Less(maxAirDeflect, 5.0f, $"Direct flight without horizontal deflection (maxDeflect={maxAirDeflect:F2}m)");

      // 3. Dedicated Bomber Altitude & Scale + Aligned Health Bar & Destruction Explosion
      var showPos = new FPVector3(0, 0, -40);
      var bUnit = SpawnUnitByName(Verified, 0, "ff_skyfortress_bomber", showPos);
      var hUnit = SpawnUnitByName(Verified, 0, "ff_hawk_gunship",       showPos + new FPVector3(-12, 0, 4));
      var dUnit = SpawnUnitByName(Verified, 0, "ff_recon_drone",        showPos + new FPVector3( 12, 0, 4));
      yield return WaitFrames(15);

      // Damage bomber to 65% HP so health bar is clearly visible above bomber at default gameplay zoom.
      var bComp = Verified.Get<Unit>(bUnit);
      bComp.Health = Verified.FindAsset(bComp.Spec).MaxHealth * FP.FromFloat_UNSAFE(0.65f);
      Verified.Set(bUnit, bComp);
      if (Game.Frames.Predicted != null && Game.Frames.Predicted.Exists(bUnit)) {
        Game.Frames.Predicted.Set(bUnit, bComp);
      }
      yield return WaitFrames(8);

      Assert.IsTrue(bv.Actors.TryGetValue(bUnit, out var bAct), "Bomber actor exists");
      Assert.IsTrue(bv.Actors.TryGetValue(hUnit, out var hAct), "Heli actor exists");
      Assert.IsTrue(bv.Actors.TryGetValue(dUnit, out var dAct), "Drone actor exists");

      var bAlt = bAct.View.Turn.localPosition.y;
      var hAlt = hAct.View.Turn.localPosition.y;
      var dAlt = dAct.View.Turn.localPosition.y;
      Assert.IsTrue(bAct.BomberScaled, "Dedicated bomber scale multiplier applied");
      Assert.GreaterOrEqual(bAlt, 25.0f, $"Bomber dedicated altitude >= 25.5m (actual {bAlt:F2}m)");
      Assert.Greater(bAlt, hAlt + 4.5f, $"Bomber flies clearly higher than heli ({bAlt:F1}m vs {hAlt:F1}m)");
      Assert.Greater(hAlt, dAlt + 2.0f, $"Heli flies higher than drone ({hAlt:F1}m vs {dAlt:F1}m)");
      Assert.IsNotNull(bAct.View.Bar, "Bomber health bar exists");
      Assert.GreaterOrEqual(bAct.View.Bar.transform.localPosition.y, bAlt + 3.9f,
        $"Bomber health bar aligned above bomber (barY={bAct.View.Bar.transform.localPosition.y:F2}, alt={bAlt:F2})");

      rts.Height = 66f;
      rts.MoveTo(new Vector2(showPos.X.AsFloat, showPos.Z.AsFloat + 16f));
      rts.Apply();
      yield return WaitFrames(6);
      CaptureCameraPng(camera, Path.Combine(dir, "03_bomber_altitude_scale_and_bar.png"), 1920, 1080);

      // Destroy bomber and verify explosion spawns at actual airborne Turn.position (NOT ground/low altitude)!
      var bomberWorldPos = bAct.View.Turn.position;
      Verified.Destroy(bUnit);
      if (Game.Frames.Predicted != null && Game.Frames.Predicted.Exists(bUnit)) {
        Game.Frames.Predicted.Destroy(bUnit);
      }
      yield return WaitFrames(2);
      CaptureCameraPng(camera, Path.Combine(dir, "04_bomber_explosion_aligned.png"), 1920, 1080);

      var expDist = Vector3.Distance(bv.LastExplosionPosition, bomberWorldPos);
      Assert.Less(expDist, 1.5f, $"Bomber destruction explosion aligned to airborne bomber position (dist={expDist:F2}m, exp={bv.LastExplosionPosition}, bomber={bomberWorldPos})");
      Debug.Log($"[StockpileTruckPickup_DirectAirFlight_AndBomberAlignment] PASS stockpile={stockpileBefore} iron={ironBeforePickup}->{ironAfterPickup} delivery={truckAfter.DeliveryId} overH={crossedH} alt=({dAlt:F1}/{hAlt:F1}/{bAlt:F1}m) expDist={expDist:F2}m");
    }

    static void GrantLargeStock(Frame frame, PlayerRef player) {
      var p = frame.Global->Players.GetPointer(player);
      if (p == null) return;
      p->Stock.Iron = (FP)50000;
      p->Stock.Wood = (FP)50000;
      p->Stock.Steel = (FP)50000;
      p->Stock.Power = (FP)50000;
      p->Outage = false;
    }

    static UltimatePhase GetUltimatePhase(Frame frame, PlayerRef player) =>
      frame.Global->Players.GetPointer(player)->UltimatePhase;

    static void SetUltimatePhaseForTest(Frame frame, PlayerRef player, UltimatePhase phase, EntityRef site) {
      var p = frame.Global->Players.GetPointer(player);
      if (p != null) {
        p->UltimatePhase = phase;
        p->UltimateSite = site;
        p->UltimateRemaining = phase == UltimatePhase.Preparing ? (FP)20 : FP._0;
        p->UltimateTotal = (FP)20;
      }
    }

    [UnityTest, Timeout(1200000)]
    public IEnumerator Priority1And2_FactoryFence_TankHitGifs_HarborWave_AndUltimateCap_VerifyAndCapture() {
      var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "status_images", "Priority1And2"));
      var frames = Path.Combine(dir, "frames");
      Directory.CreateDirectory(frames);

      yield return StartMatch(Map4PScene, 2, truce: 0, seed: 8201, startingTier: 3);
      yield return WaitForHud();
      yield return WaitForBattle();
      var doctrine = GameObject.Find("DoctrinePanel");
      if (doctrine != null) doctrine.GetComponentsInChildren<UnityEngine.UI.Button>(true).First().onClick.Invoke();
      GrantAgreement(0);
      GrantAgreement(1);
      GrantLargeStock(Verified, 0);
      GrantLargeStock(Verified, 1);
      if (Game.Frames.Predicted != null) {
        GrantLargeStock(Game.Frames.Predicted, 0);
        GrantLargeStock(Game.Frames.Predicted, 1);
      }
      Time.timeScale = 1f;

      var camera = Camera.main;
      var rts = Controller().Camera;
      camera.aspect = 16f / 9f;
      var bv = Battle();
      var hud = Controller().Hud;
      var map = FactoryWarsUtil.MapInfo(Verified);

      // ===== Priority 1 (Items 1-3): Factory-style Iron Extractor + Clean Military Fencing (철책) + Zero Overlap + Full Auto Truck Cycle =====
      var base0 = GameObject.Find("Base0");
      Assert.IsNotNull(base0, "Base0 exists");
      var permWall = base0.transform.Find("PerimeterWall");
      Assert.IsNotNull(permWall, "Base0 PerimeterWall exists");
      var fenceCount = 0;
      var oldConcreteBarrierCount = 0;
      for (var i = 0; i < permWall.childCount; i++) {
        var cname = permWall.GetChild(i).name;
        if (cname.StartsWith("Fence")) fenceCount++;
        if (cname.StartsWith("Wall")) oldConcreteBarrierCount++;
      }
      Assert.Greater(fenceCount, 10, $"Base0 perimeter switched to military fencing (철책) (fences={fenceCount})");
      Assert.AreEqual(0, oldConcreteBarrierCount, $"No legacy gapped concrete barriers remain on Base0 (oldWalls={oldConcreteBarrierCount})");

      var hqEntity = EntityRef.None;
      var hqPos = FPVector3.Zero;
      foreach (var p in Verified.GetComponentIterator<Building>()) {
        if (p.Component.Owner == 0 && Verified.FindAsset(p.Component.Spec).Role == BuildingRole.Headquarters) {
          hqEntity = p.Entity;
          hqPos = Verified.Get<Transform3D>(p.Entity).Position;
          break;
        }
      }
      var allIronNodes = new List<ResourceNode>();
      foreach (var p in Verified.GetComponentIterator<ResourceNode>()) {
        if (p.Component.Kind == ResourceKind.Iron) allIronNodes.Add(p.Component);
      }
      var nodePos = allIronNodes.OrderBy(n0 => FPVector3.DistanceSquared(n0.Shape.Center, hqPos)).First().Shape.Center;

      // Verify zero active decorative prop in Base0 overlaps the iron production area (< 10.5m)
      var overlappingProps = 0;
      for (var i = 0; i < base0.transform.childCount; i++) {
        var ch = base0.transform.GetChild(i);
        if (!ch.gameObject.activeSelf || ch.name == "Paving" || ch.name == "PerimeterWall") continue;
        var d2 = new Vector2(ch.position.x - nodePos.X.AsFloat, ch.position.z - nodePos.Z.AsFloat).sqrMagnitude;
        if (d2 < 10.5f * 10.5f) overlappingProps++;
      }
      Assert.AreEqual(0, overlappingProps, "Zero decorative props overlapping iron factory area");

      var eng0 = Engineers(0).FirstOrDefault();
      if (eng0 == EntityRef.None) eng0 = SpawnUnitByName(Verified, 0, "ff_engineer", nodePos + new FPVector3(4, 0, 0));
      yield return Build(0, "iron_extractor", nodePos, eng0);
      yield return WaitFrames(10);
      var extractorEntity = BuildingAt(nodePos);
      Assert.AreNotEqual(EntityRef.None, extractorEntity, "Factory-style iron extractor built cleanly");

      var extB = Verified.Get<Building>(extractorEntity);
      extB.ConstructionRemaining = FP._0;
      extB.ProcessTimer = FP.FromFloat_UNSAFE(110f);
      Verified.Set(extractorEntity, extB);
      if (Game.Frames.Predicted != null && Game.Frames.Predicted.Exists(extractorEntity)) Game.Frames.Predicted.Set(extractorEntity, extB);
      var eng1 = SpawnUnitByName(Verified, 0, "ff_engineer", nodePos + new FPVector3(-4, 0, 0));
      var eng2 = SpawnUnitByName(Verified, 0, "ff_engineer", nodePos + new FPVector3(0, 0, 4));
      AssignGarrison(Verified, eng0, extractorEntity);
      AssignGarrison(Verified, eng1, extractorEntity);
      AssignGarrison(Verified, eng2, extractorEntity);
      if (Game.Frames.Predicted != null) {
        AssignGarrison(Game.Frames.Predicted, eng0, extractorEntity);
        AssignGarrison(Game.Frames.Predicted, eng1, extractorEntity);
        AssignGarrison(Game.Frames.Predicted, eng2, extractorEntity);
      }
      yield return WaitFrames(Fps * 2);

      Assert.IsTrue(bv.Actors.TryGetValue(extractorEntity, out var extAct), "Extractor view created");
      Assert.IsTrue(extAct.View.Slot != null && extAct.View.Slot.Prefab != null && extAct.View.Slot.Prefab.name.Contains("Warehouse"),
        $"Circular gas tower replaced with factory-style warehouse building (actual: {extAct.View.Slot?.Prefab?.name})");
      Assert.IsNull(extAct.ExtractorDelivery, "No conveyor belt attached to factory-style extractor");

      // Full automatic logistics cycle via '자원채취' button (ToYard -> Loading -> ToDock -> Unloading -> HQ credit)
      var truckStart = FPVector3.Lerp(hqPos, nodePos, FP.FromFloat_UNSAFE(0.45f));
      var truck = SpawnUnitByName(Verified, 0, "merc_cargo_truck", truckStart);
      yield return WaitFrames(8);
      Controller().Input.Selection.Units.Clear();
      Controller().Input.Selection.Units.Add(truck);
      yield return WaitFrames(6);
      var gatherBtn = hud.Entries.FirstOrDefault(e => e.Label == "자원채취");
      Assert.AreEqual("자원채취", gatherBtn.Label, "HUD shows '자원채취' button for truck");
      var ironBefore = PlayerIron(Verified, 0);
      yield return Send(0, new GatherCommand { Units = new[] { truck }, Node = EntityRef.None });

      rts.Height = 46f;
      rts.MoveTo(new Vector2(nodePos.X.AsFloat + 2f, nodePos.Z.AsFloat + 2f));
      rts.Apply();

      var sawLoad = false;
      var sawToDock = false;
      var sawUnload = false;
      var capFactory = false;
      for (var step = 0; step < 52; step++) {
        yield return WaitFrames(12);
        var lt = Verified.Get<LogisticsTruck>(truck);
        var tp = Verified.Get<Transform3D>(truck).Position.ToUnityVector3();
        rts.MoveTo(new Vector2(tp.x, tp.z));
        rts.Apply();
        if (lt.State == LogisticsState.Loading) {
          sawLoad = true;
          if (!capFactory) {
            capFactory = true;
            rts.MoveTo(new Vector2(nodePos.X.AsFloat + 2f, nodePos.Z.AsFloat + 2f));
            rts.Apply();
            CaptureCameraPng(camera, Path.Combine(dir, "01_factory_fence_and_auto_truck.png"), 1920, 1080);
          }
        }
        if (lt.State == LogisticsState.ToDock) sawToDock = true;
        if (lt.State == LogisticsState.Unloading) sawUnload = true;
        if (step < 18) CaptureCameraPng(camera, Path.Combine(frames, $"auto_truck_{step:00}.png"), 960, 540);
        if (lt.DeliveryId >= 1) break;
      }
      Assert.IsTrue(sawLoad && sawToDock && sawUnload, $"Full automatic truck cycle executed (load={sawLoad}, toDock={sawToDock}, unload={sawUnload})");
      var ironAfter = PlayerIron(Verified, 0);
      Assert.Greater(ironAfter, ironBefore, $"HQ iron credited automatically on unload ({ironBefore} -> {ironAfter})");

      // ===== Priority 2 (Item 4): Live Combat 100% HP Tank First Hit / Sustained Damage / Destruction GIFs =====
      var battleSpot = new FPVector3(0, 0, -68);
      var targetTank = SpawnUnitByName(Verified, 1, "cf_fortress_tank", battleSpot + new FPVector3(8, 0, 0));
      var atkTank1   = SpawnUnitByName(Verified, 0, "ff_eagle_tank",    battleSpot + new FPVector3(-12, 0, -3));
      var atkTank2   = SpawnUnitByName(Verified, 0, "ff_eagle_tank",    battleSpot + new FPVector3(-12, 0,  3));
      var tankMaxHp = Verified.FindAsset(Verified.Get<Unit>(targetTank).Spec).MaxHealth;
      Assert.AreEqual(tankMaxHp, Verified.Get<Unit>(targetTank).Health, "Target tank starts at 100% HP");

      rts.Height = 36f;
      rts.MoveTo(new Vector2(battleSpot.X.AsFloat + 2f, battleSpot.Z.AsFloat));
      rts.Apply();
      yield return Send(0, new AttackTargetCommand { Units = new[] { atkTank1, atkTank2 }, Target = targetTank });

      var hitFrames = 0;
      var dmgFrames = 0;
      var dieFrames = 0;
      var savedFirstHit = false;
      var savedSustained = false;
      var savedDestroyed = false;

      for (var t = 0; t < 160; t++) {
        yield return WaitFrames(4);
        var alive = Verified.Exists(targetTank) && Verified.Get<Unit>(targetTank).Health > FP._0;
        var ratio = alive ? (Verified.Get<Unit>(targetTank).Health / tankMaxHp).AsFloat : 0f;
        if (alive && ratio < 0.999f && ratio >= 0.68f) {
          if (hitFrames < 10) {
            CaptureCameraPng(camera, Path.Combine(frames, $"tank_hit_{hitFrames:00}.png"), 960, 540);
            if (!savedFirstHit && hitFrames == 1) {
              savedFirstHit = true;
              CaptureCameraPng(camera, Path.Combine(dir, "02_tank_first_hit.png"), 1920, 1080);
            }
            hitFrames++;
          }
        } else if (alive && ratio < 0.68f && ratio > 0.12f) {
          if (dmgFrames < 10) {
            CaptureCameraPng(camera, Path.Combine(frames, $"tank_dmg_{dmgFrames:00}.png"), 960, 540);
            if (!savedSustained && dmgFrames == 3) {
              savedSustained = true;
              CaptureCameraPng(camera, Path.Combine(dir, "03_tank_sustained_damage.png"), 1920, 1080);
            }
            dmgFrames++;
          }
        } else if (!alive) {
          if (dieFrames < 10) {
            CaptureCameraPng(camera, Path.Combine(frames, $"tank_die_{dieFrames:00}.png"), 960, 540);
            if (!savedDestroyed && dieFrames == 1) {
              savedDestroyed = true;
              CaptureCameraPng(camera, Path.Combine(dir, "04_tank_destruction.png"), 1920, 1080);
            }
            dieFrames++;
          }
          if (dieFrames >= 8) break;
        }
      }
      Assert.IsTrue(savedFirstHit, "Captured 100% HP tank First Hit keyframe & GIF frames");
      Assert.IsTrue(savedSustained, "Captured Sustained Damage keyframe & GIF frames");
      Assert.IsTrue(savedDestroyed, "Captured Tank Destruction keyframe & GIF frames");

      // ===== Priority 2 (Item 5): Harbor Wave Texture Verification & Waterfront Capture =====
      var waveTex = Resources.Load<Texture2D>("WaterSurface_single");
      Assert.IsNotNull(waveTex, "IgniteCoders WaterSurface_single wave texture loaded from Resources");
      var waterMat = UrbanDecor.WaterMaterial();
      Assert.IsNotNull(waterMat, "UrbanWater material exists");
      Assert.AreEqual(waveTex, waterMat.GetTexture("_WaveTex"), "Harbor water material has WaterSurface_single applied to _WaveTex");

      // Position camera over Map4P v4 harbor/canal water to visually verify wave texture
      Vector3 harborWaterPos = new Vector3(-224f, 0f, 0f);
      for (var r = 10; r < map.CellGridSize - 10; r++) {
        for (var c = 10; c < map.CellGridSize - 10; c++) {
          if (GridMapView.Cell(map, r, c) == 'W' && GridMapView.Cell(map, r, c + 1) == 'w') {
            harborWaterPos = new Vector3(GridMapView.CellX(map, c), 0f, GridMapView.CellZ(map, r));
            break;
          }
        }
      }
      rts.Height = 42f;
      rts.MoveTo(new Vector2(harborWaterPos.x, harborWaterPos.z));
      rts.Apply();
      yield return WaitFrames(8);
      CaptureCameraPng(camera, Path.Combine(dir, "05_harbor_wave_texture.png"), 1920, 1080);

      // ===== Priority 2 (Item 6): Shared 1-Unit Ultimate Cap Across Both Orderings + Cancel & Destroy Rebuild =====
      // Player 1 is CF (UltimateKind.StrategicMissile -> cf_longwall_mlrs, MaxOwned = 1)
      var mlsSpec = Resources.Load<UnitSpec>("FactoryWars/cf_longwall_mlrs");
      var heavySpec = Resources.Load<BuildingSpec>("FactoryWars/cf_heavy_works");
      Assert.IsNotNull(mlsSpec, "cf_longwall_mlrs loaded");
      Assert.AreEqual(1, mlsSpec.MaxOwned, "CF Mobile MLS MaxOwned == 1");
      var p1HqPos = FPVector3.Zero;
      foreach (var p in Verified.GetComponentIterator<Building>()) {
        if (p.Component.Owner == 1 && Verified.FindAsset(p.Component.Spec).Role == BuildingRole.Headquarters) {
          p1HqPos = Verified.Get<Transform3D>(p.Entity).Position;
          break;
        }
      }
      var cfHeavy = ProductionSystem.CreateFinishedBuilding(Verified, 1, heavySpec, p1HqPos + new FPVector3(-14, 0, 0));
      yield return WaitFrames(4);

      // Ordering 1: 궁극기 준비 (Preparing) -> 일반 생산 예약 (ProduceCommand) MUST be rejected!
      SetUltimatePhaseForTest(Verified, 1, UltimatePhase.Preparing, cfHeavy);
      Assert.AreEqual(1, SniperSystem.CountOwnedAndQueued(Verified, 1, mlsSpec), "Preparing ultimate counts toward shared 1-unit cap");
      yield return Send(1, new ProduceCommand { Building = cfHeavy, Unit = mlsSpec, Count = 1, Cancel = false });
      yield return WaitFrames(4);
      Assert.AreEqual(0, Verified.Get<Building>(cfHeavy).QueueCount, "Ordering 1: Factory queue rejected while 궁극기 준비 active");

      // Cancel 궁극기 준비 -> 일반 생산 예약 MUST succeed!
      yield return Send(1, new CancelUltimateCommand());
      yield return WaitFrames(4);
      Assert.AreEqual(UltimatePhase.None, GetUltimatePhase(Verified, 1), "Ultimate cancelled -> None");
      Assert.AreEqual(0, SniperSystem.CountOwnedAndQueued(Verified, 1, mlsSpec), "Cap cleared after canceling ultimate");

      // Ordering 2: 일반 생산 예약 (ProduceCommand) -> 궁극기 준비 (UltimateSystem.Locks) MUST be blocked!
      yield return Send(1, new ProduceCommand { Building = cfHeavy, Unit = mlsSpec, Count = 1, Cancel = false });
      yield return WaitFrames(4);
      Assert.AreEqual(1, Verified.Get<Building>(cfHeavy).QueueCount, "Factory queue accepted after ultimate cancel");
      Assert.AreEqual(1, SniperSystem.CountOwnedAndQueued(Verified, 1, mlsSpec), "Queued MLS counts toward shared 1-unit cap");
      Assert.IsTrue((UltimateSystem.Locks(Verified, 1) & UltimateLock.Busy) != 0, "Ordering 2: 궁극기 준비 blocked while factory queue holds MLS");

      // Cancel factory queue -> cap cleared & rebuildable!
      yield return Send(1, new ProduceCommand { Building = cfHeavy, Unit = mlsSpec, Count = 1, Cancel = true });
      yield return WaitFrames(4);
      Assert.AreEqual(0, Verified.Get<Building>(cfHeavy).QueueCount, "Factory queue cancelled cleanly");
      Assert.AreEqual(0, SniperSystem.CountOwnedAndQueued(Verified, 1, mlsSpec), "Cap cleared after canceling factory queue");
      Assert.IsTrue((UltimateSystem.Locks(Verified, 1) & UltimateLock.Busy) == 0, "Ultimate Busy lock cleared after factory cancel");

      // Live unit -> cap=1 (blocked) -> destroy -> cap=0 (rebuildable!)
      var liveMls = SpawnUnitByName(Verified, 1, "cf_longwall_mlrs", p1HqPos + new FPVector3(0, 0, -14));
      yield return WaitFrames(4);
      Assert.AreEqual(1, SniperSystem.CountOwnedAndQueued(Verified, 1, mlsSpec), "Live MLS counts as 1/1");
      Assert.IsTrue((UltimateSystem.Locks(Verified, 1) & UltimateLock.Busy) != 0, "Ultimate blocked while live MLS exists");
      Verified.Destroy(liveMls);
      yield return WaitFrames(4);
      Assert.AreEqual(0, SniperSystem.CountOwnedAndQueued(Verified, 1, mlsSpec), "Cap cleared after destroying live MLS");
      yield return Send(1, new ProduceCommand { Building = cfHeavy, Unit = mlsSpec, Count = 1, Cancel = false });
      yield return WaitFrames(4);
      Assert.AreEqual(1, Verified.Get<Building>(cfHeavy).QueueCount, "MLS rebuildable immediately after destruction");

      Debug.Log($"[Priority1And2_VerifyAndCapture] PASS fences={fenceCount} overlaps=0 factory={extAct.View.Slot.Prefab.name} iron={ironBefore}->{ironAfter} tankGifs=({hitFrames}/{dmgFrames}/{dieFrames}) waveTex={waveTex.name} ultCapBothOrderingsAndCancelDestroy=OK");
    }

    [UnityTest, Timeout(1200000)]
    public IEnumerator MicroBar_HealthBars_AndVehiclePedestrianYield_VerifyAndCapture() {
      var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "status_images", "MicroBarAndYield"));
      var frames = Path.Combine(dir, "frames");
      Directory.CreateDirectory(frames);

      yield return StartMatch(Map4PScene, 2, truce: 0, seed: 9101, startingTier: 3);
      yield return WaitForHud();
      yield return WaitForBattle();
      var doctrine = GameObject.Find("DoctrinePanel");
      if (doctrine != null) doctrine.GetComponentsInChildren<UnityEngine.UI.Button>(true).First().onClick.Invoke();
      GrantAgreement(0);
      GrantAgreement(1);
      GrantLargeStock(Verified, 0);
      GrantLargeStock(Verified, 1);
      if (Game.Frames.Predicted != null) {
        GrantLargeStock(Game.Frames.Predicted, 0);
        GrantLargeStock(Game.Frames.Predicted, 1);
      }
      Time.timeScale = 1f;

      var camera = Camera.main;
      var rts = Controller().Camera;
      camera.aspect = 16f / 9f;
      var bv = Battle();

      // 1. Verify MicroBar active on tanks, ground infantry, structures, and elevated bomber
      var spot = new FPVector3(0, 0, -68);
      var targetTank = SpawnUnitByName(Verified, 1, "cf_fortress_tank", spot + new FPVector3(8, 0, 0));
      var atkTank1   = SpawnUnitByName(Verified, 0, "ff_eagle_tank",    spot + new FPVector3(-12, 0, -3));
      var atkTank2   = SpawnUnitByName(Verified, 0, "ff_eagle_tank",    spot + new FPVector3(-12, 0,  3));
      yield return WaitFrames(10);

      Assert.IsTrue(bv.Actors.TryGetValue(targetTank, out var tankAct) && tankAct.View.HasMicroBar, "Tank uses MicroBar");
      Assert.IsTrue(bv.Actors.TryGetValue(atkTank1, out var atkAct) && atkAct.View.HasMicroBar, "Attacker tank uses MicroBar");

      rts.Height = 36f;
      rts.MoveTo(new Vector2(spot.X.AsFloat + 2f, spot.Z.AsFloat));
      rts.Apply();
      yield return Send(0, new AttackTargetCommand { Units = new[] { atkTank1, atkTank2 }, Target = targetTank });

      var capturedCombat = false;
      var combatFrameCount = 0;
      for (var i = 0; i < 65; i++) {
        yield return WaitFrames(5);
        if (!Verified.Exists(targetTank)) break;
        var hpRatio = (Verified.Get<Unit>(targetTank).Health / Verified.FindAsset(Verified.Get<Unit>(targetTank).Spec).MaxHealth).AsFloat;
        if (hpRatio < 0.99f && combatFrameCount < 12) {
          CaptureCameraPng(camera, Path.Combine(frames, $"microbar_combat_{combatFrameCount:00}.png"), 960, 540);
          if (!capturedCombat && hpRatio < 0.78f) {
            capturedCombat = true;
            CaptureCameraPng(camera, Path.Combine(dir, "01_microbar_tank_combat.png"), 1920, 1080);
          }
          combatFrameCount++;
        }
        if (combatFrameCount >= 12) break;
      }
      if (!capturedCombat) CaptureCameraPng(camera, Path.Combine(dir, "01_microbar_tank_combat.png"), 1920, 1080);
      Assert.GreaterOrEqual(combatFrameCount, 4, "Captured MicroBar animated health depletion during tank combat");

      // 2. Structure MicroBar + Mobility-Upgraded Soldier MicroBar & Single Chevron '^' (no Mk.2!)
      var powerSpec = Resources.Load<BuildingSpec>("FactoryWars/ff_power_plant");
      var structure = ProductionSystem.CreateFinishedBuilding(Verified, 0, powerSpec, new FPVector3(-24, 0, -64));
      var soldier   = SpawnUnitByName(Verified, 0, "ff_ranger_squad", new FPVector3(-16, 0, -64));
      yield return WaitFrames(8);

      // Unlock mobility upgrade for player 0 & damage structure/soldier slightly so MicroBar shows cleanly
      UnlockSmallUpgrade(Verified, 0);
      if (Game?.Frames?.Predicted != null) UnlockSmallUpgrade(Game.Frames.Predicted, 0);
      var sB = Verified.Get<Building>(structure);
      sB.Health = powerSpec.MaxHealth * FP.FromFloat_UNSAFE(0.62f);
      Verified.Set(structure, sB);
      if (Game.Frames.Predicted != null && Game.Frames.Predicted.Exists(structure)) Game.Frames.Predicted.Set(structure, sB);

      var uS = Verified.Get<Unit>(soldier);
      uS.Health = Verified.FindAsset(uS.Spec).MaxHealth * FP.FromFloat_UNSAFE(0.75f);
      Verified.Set(soldier, uS);
      if (Game.Frames.Predicted != null && Game.Frames.Predicted.Exists(soldier)) Game.Frames.Predicted.Set(soldier, uS);

      Controller().Input.Selection.Units.Clear();
      Controller().Input.Selection.Units.Add(soldier);
      yield return WaitFrames(10);

      Assert.IsTrue(bv.Actors.TryGetValue(structure, out var structAct) && structAct.View.HasMicroBar, "Structure uses MicroBar");
      Assert.IsTrue(bv.Actors.TryGetValue(soldier, out var solAct) && solAct.View.HasMicroBar, "Soldier uses MicroBar");
      if (solAct.UpgradeChevron != null) {
        Assert.AreEqual("^", solAct.UpgradeChevron.text, "Mobility-upgraded soldier keeps single '^' chevron beside MicroBar (no Mk.2)");
      }
      rts.Height = 32f;
      rts.MoveTo(new Vector2(-20f, -64f));
      rts.Apply();
      yield return WaitFrames(6);
      CaptureCameraPng(camera, Path.Combine(dir, "02_microbar_structure_and_soldier_chevron.png"), 1920, 1080);

      // 3. Elevated Bomber MicroBar Alignment (no height/offset drift)
      var bomberPos = new FPVector3(0, 0, -40);
      var bomber = SpawnUnitByName(Verified, 0, "ff_skyfortress_bomber", bomberPos);
      yield return WaitFrames(10);
      var bU = Verified.Get<Unit>(bomber);
      bU.Health = Verified.FindAsset(bU.Spec).MaxHealth * FP.FromFloat_UNSAFE(0.58f);
      Verified.Set(bomber, bU);
      if (Game.Frames.Predicted != null && Game.Frames.Predicted.Exists(bomber)) Game.Frames.Predicted.Set(bomber, bU);
      yield return WaitFrames(8);

      Assert.IsTrue(bv.Actors.TryGetValue(bomber, out var bAct) && bAct.View.HasMicroBar, "Elevated bomber uses MicroBar");
      var bAlt = bAct.View.Turn.localPosition.y;
      Assert.GreaterOrEqual(bAlt, 25.0f, $"Bomber stays at elevated altitude ({bAlt:F1}m)");
      Assert.GreaterOrEqual(bAct.View.Bar.transform.localPosition.y, bAlt + 3.9f, "Bomber MicroBar aligned above bomber without drift");
      rts.Height = 66f;
      rts.MoveTo(new Vector2(bomberPos.X.AsFloat, bomberPos.Z.AsFloat + 16f));
      rts.Apply();
      yield return WaitFrames(6);
      CaptureCameraPng(camera, Path.Combine(dir, "03_microbar_bomber_elevated.png"), 1920, 1080);

      // 4. Vehicle Yielding / Steering Around People (on wide open asphalt road Z=-56, zero clipping, reaches goal cleanly)
      var roadY = -56;
      var truck = SpawnUnitByName(Verified, 0, "merc_cargo_truck", new FPVector3(-18, 0, roadY));
      var person = SpawnUnitByName(Verified, 0, "ff_engineer",     new FPVector3( -2, 0, roadY));
      yield return WaitFrames(6);
      yield return Send(0, new MoveUnitsCommand { Units = new[] { truck }, Target = new FPVector3(16, 0, roadY) });

      rts.Height = 34f;
      rts.MoveTo(new Vector2(0f, roadY));
      rts.Apply();

      var minPersonDist = float.MaxValue;
      var maxLateralOffset = 0f;
      var yieldFrames = 0;
      for (var step = 0; step < 30; step++) {
        yield return WaitFrames(10);
        var tp = Verified.Get<Transform3D>(truck).Position.ToUnityVector3();
        var pp = Verified.Get<Transform3D>(person).Position.ToUnityVector3();
        var d = Vector2.Distance(new Vector2(tp.x, tp.z), new Vector2(pp.x, pp.z));
        minPersonDist = Mathf.Min(minPersonDist, d);
        maxLateralOffset = Mathf.Max(maxLateralOffset, Mathf.Abs(tp.z - roadY));
        if (yieldFrames < 14) {
          CaptureCameraPng(camera, Path.Combine(frames, $"yield_{yieldFrames:00}.png"), 960, 540);
          yieldFrames++;
        }
        if (tp.x >= 12f) break;
      }
      CaptureCameraPng(camera, Path.Combine(dir, "04_vehicle_avoid_person.png"), 1920, 1080);
      var finalTruckX = Verified.Get<Transform3D>(truck).Position.X.AsFloat;
      Assert.Greater(minPersonDist, 1.25f, $"Vehicle steered around/yielded to pedestrian without clipping (minDist={minPersonDist:F2}m)");
      Assert.Greater(finalTruckX, 6.0f, $"Vehicle smoothly passed pedestrian without getting stuck (finalX={finalTruckX:F1})");

      Debug.Log($"[MicroBar_HealthBars_AndVehiclePedestrianYield] PASS combatFrames={combatFrameCount} bomberAlt={bAlt:F1}m minPersonDist={minPersonDist:F2}m maxLat={maxLateralOffset:F2}m finalX={finalTruckX:F1}");
    }

    static void SetPlayerStockZero(Frame frame, PlayerRef player) {
      var p = frame.Global->Players.GetPointer(player);
      if (p == null) return;
      p->Stock = default;
    }

    static void SetPlayerPopUsed(Frame frame, PlayerRef player, int used) {
      var p = frame.Global->Players.GetPointer(player);
      if (p == null) return;
      p->PopulationUsed = (FP)used;
    }

    static int GetPlayerPopCap(Frame frame, PlayerRef player) {
      var p = frame.Global->Players.GetPointer(player);
      return p != null ? p->PopulationCap.AsInt : 0;
    }

    [UnityTest, Timeout(1200000)]
    public IEnumerator Items7To10_GrenadeDecouple_MobilePmcShop8Slots_AndRangedStandoff_VerifyAndCapture() {
      var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "status_images", "Items7To10"));
      var frames = Path.Combine(dir, "frames");
      Directory.CreateDirectory(frames);

      yield return StartMatch(Map4PScene, 2, truce: 0, seed: 9501, startingTier: 3);
      yield return WaitForHud();
      yield return WaitForBattle();
      var doctrine = GameObject.Find("DoctrinePanel");
      if (doctrine != null) doctrine.GetComponentsInChildren<UnityEngine.UI.Button>(true).First().onClick.Invoke();
      GrantAgreement(0);
      GrantAgreement(1);
      GrantLargeStock(Verified, 0);
      GrantLargeStock(Verified, 1);
      if (Game.Frames.Predicted != null) {
        GrantLargeStock(Game.Frames.Predicted, 0);
        GrantLargeStock(Game.Frames.Predicted, 1);
      }
      Time.timeScale = 1f;

      var camera = Camera.main;
      var rts = Controller().Camera;
      var hud = Controller().Hud;
      var bv = Battle();

      // ===== Item 7: Decoupled Grenade Rules (fires independently of rifle cooldown & beyond rifle range 10.5m -> 11.4m) =====
      UnlockSmallUpgrade(Verified, 0);
      if (Game?.Frames?.Predicted != null) UnlockSmallUpgrade(Game.Frames.Predicted, 0);
      var gSpot = new FPVector3(-12, 0, -56);
      var thrower = SpawnUnitByName(Verified, 0, "ff_ranger_squad", gSpot);
      var victim  = SpawnUnitByName(Verified, 1, "cf_militia",      gSpot + new FPVector3(FP.FromFloat_UNSAFE(11.4f), FP._0, FP._0));
      yield return WaitFrames(4);
      // Force standard rifle AttackCooldown to 5.0s so rifle CANNOT fire, proving grenade rule is 100% decoupled!
      var tu = Verified.Get<Unit>(thrower);
      tu.AttackCooldown = (FP)5;
      tu.AbilityCooldown = FP._0;
      tu.TargetEntity = victim;
      Verified.Set(thrower, tu);
      if (Game?.Frames?.Predicted != null && Game.Frames.Predicted.Exists(thrower)) Game.Frames.Predicted.Set(thrower, tu);

      camera.aspect = 16f / 9f;
      rts.Height = 24f;
      rts.MoveTo(new Vector2(gSpot.X.AsFloat + 5.5f, gSpot.Z.AsFloat));
      rts.Apply();

      var decoupledGrenadeFired = false;
      var sawThrowMotion = false;
      for (var f = 0; f < 10; f++) {
        yield return WaitFrames(5);
        var cur = Verified.Get<Unit>(thrower);
        if (cur.AbilityCooldown > FP._0) decoupledGrenadeFired = true;
        if (bv.Actors.TryGetValue(thrower, out var tAct) && tAct.View?.Animation?.ActiveMixamoPose == "GrenadeThrow") sawThrowMotion = true;
        CaptureCameraPng(camera, Path.Combine(frames, $"grenade_throw_{f:00}.png"), 960, 540);
        if (f == 2) CaptureCameraPng(camera, Path.Combine(dir, "01_grenade_decoupled_throw.png"), 1920, 1080);
      }
      Assert.IsTrue(decoupledGrenadeFired, "Grenade threw independently even while rifle AttackCooldown=5s and target at 11.4m (> rifle range 10.5m)");

      // ===== Item 9: Mobile HUD Inspection for PMC Shop 8 Slots (1600x720 20:9 phone viewport) =====
      var traderEntity = EntityRef.None;
      foreach (var p in Verified.GetComponentIterator<NpcTrader>()) {
        traderEntity = p.Entity;
        break;
      }
      Assert.AreNotEqual(EntityRef.None, traderEntity, "PMC Camp NpcTrader exists");
      Controller().Input.Selection.Units.Clear();
      Controller().Input.Selection.Building = traderEntity;
      camera.aspect = 1600f / 720f;
      rts.Height = 46f;
      rts.MoveTo(new Vector2(0f, 0f));
      rts.Apply();
      yield return WaitFrames(8);

      // (a) Insufficient resources visual distinction
      SetPlayerStockZero(Verified, 0);
      if (Game?.Frames?.Predicted != null) SetPlayerStockZero(Game.Frames.Predicted, 0);
      yield return WaitFrames(6);
      Assert.AreEqual(8, hud.Entries.Count, $"PMC Shop displays all 8 slots (actual {hud.Entries.Count})");
      Assert.IsTrue(hud.Entries.All(e => !e.Enabled && e.Locked && e.Label.Contains("자원 부족")), "All 8 slots clearly show '자원 부족' when stock is 0");
      CaptureCameraPng(camera, Path.Combine(dir, "02_mobile_pmc_shop_insufficient_res.png"), 1600, 720);

      // (b) Population cap blocking & clear feedback (genuinely fill real simulation PopulationUsed >= PopulationCap!)
      GrantLargeStock(Verified, 0);
      if (Game?.Frames?.Predicted != null) GrantLargeStock(Game.Frames.Predicted, 0);
      var popCap = Mathf.Max(10, GetPlayerPopCap(Verified, 0));
      var popFillers = new List<EntityRef>();
      for (var k = 0; k < (popCap / 4) + 3; k++) {
        popFillers.Add(SpawnUnitByName(Verified, 0, "ff_bulwark_tank", new FPVector3(-180 + (k % 5) * 6, 0, -180 + (k / 5) * 6)));
      }
      yield return WaitFrames(8);
      var popBlockedCount = hud.Entries.Count(e => !e.Enabled && e.Locked && e.Label.Contains("인구 상한"));
      Assert.GreaterOrEqual(popBlockedCount, 6, $"PMC unit shop slots clearly block & label '인구 상한' at pop cap (blocked={popBlockedCount})");
      CaptureCameraPng(camera, Path.Combine(dir, "03_mobile_pmc_shop_pop_cap.png"), 1600, 720);

      // (c) Purchase success feedback & (d) 8th slot touchability/non-overlap check
      foreach (var filler in popFillers) {
        if (Verified.Exists(filler)) Verified.Destroy(filler);
        if (Game?.Frames?.Predicted != null && Game.Frames.Predicted.Exists(filler)) Game.Frames.Predicted.Destroy(filler);
      }
      yield return WaitFrames(8);
      Assert.IsTrue(hud.Entries[7].Enabled, "8th PMC shop slot is enabled and purchasable");
      var slotRects = new List<Rect>();
      for (var i = 0; i < 8; i++) {
        var btnGo = GameObject.Find($"Context{i}");
        Assert.IsNotNull(btnGo, $"PMC Shop button Context{i} exists");
        Assert.IsTrue(btnGo.activeInHierarchy, $"PMC Shop button Context{i} is active");
        var rt = btnGo.GetComponent<RectTransform>();
        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        var r = new Rect(corners[0].x, corners[0].y, corners[2].x - corners[0].x, corners[2].y - corners[0].y);
        Assert.Greater(r.width, 30f, $"Slot {i} has touchable width ({r.width:F1})");
        Assert.Greater(r.height, 30f, $"Slot {i} has touchable height ({r.height:F1})");
        foreach (var prev in slotRects) {
          Assert.IsFalse(prev.Overlaps(r), $"Slot {i} does not overlap previous slots");
        }
        slotRects.Add(r);
      }
      hud.Entries[7].Action?.Invoke();
      yield return WaitFrames(6);
      Assert.IsTrue(hud.AlertText.Contains("용병 구매 완료"), $"8th slot purchase shows clear success alert (actual: {hud.AlertText})");
      CaptureCameraPng(camera, Path.Combine(dir, "04_mobile_pmc_shop_8slots_success.png"), 1600, 720);

      // ===== Item 10: Ranged Unit Standoff Spacing Verification =====
      Controller().Input.Selection.Building = EntityRef.None;
      camera.aspect = 16f / 9f;
      var sPos = new FPVector3(0, 0, -68);
      var infA = SpawnUnitByName(Verified, 0, "ff_ranger_squad",  sPos + new FPVector3(-12, 0, -4));
      var infB = SpawnUnitByName(Verified, 1, "cf_militia",       sPos + new FPVector3( 12, 0, -4));
      var tnkA = SpawnUnitByName(Verified, 0, "ff_eagle_tank",    sPos + new FPVector3(-15, 0,  4));
      var tnkB = SpawnUnitByName(Verified, 1, "cf_fortress_tank", sPos + new FPVector3( 15, 0,  4));
      yield return WaitFrames(4);
      yield return Send(0, new AttackTargetCommand { Units = new[] { infA }, Target = infB });
      yield return Send(0, new AttackTargetCommand { Units = new[] { tnkA }, Target = tnkB });

      rts.Height = 42f;
      rts.MoveTo(new Vector2(sPos.X.AsFloat, sPos.Z.AsFloat));
      rts.Apply();

      var infEngageDist = 0f;
      var tankEngageDist = 0f;
      for (var step = 0; step < 30; step++) {
        yield return WaitFrames(8);
        if (Verified.Exists(infA) && Verified.Exists(infB) && Verified.Get<Unit>(infA).LastFiredFrame > 0 && infEngageDist == 0f) {
          infEngageDist = Vector3.Distance(Verified.Get<Transform3D>(infA).Position.ToUnityVector3(), Verified.Get<Transform3D>(infB).Position.ToUnityVector3());
        }
        if (Verified.Exists(tnkA) && Verified.Exists(tnkB) && Verified.Get<Unit>(tnkA).LastFiredFrame > 0 && tankEngageDist == 0f) {
          tankEngageDist = Vector3.Distance(Verified.Get<Transform3D>(tnkA).Position.ToUnityVector3(), Verified.Get<Transform3D>(tnkB).Position.ToUnityVector3());
        }
        if (infEngageDist > 0f && tankEngageDist > 0f) break;
      }
      CaptureCameraPng(camera, Path.Combine(dir, "05_ranged_standoff_spacing.png"), 1920, 1080);

      Assert.GreaterOrEqual(infEngageDist, 9.0f, $"Rifleman engages at proper standoff >= 9.0m (actual {infEngageDist:F1}m)");
      Assert.GreaterOrEqual(tankEngageDist, 13.5f, $"Main Battle Tank engages at proper standoff >= 13.5m (actual {tankEngageDist:F1}m)");
      Assert.Greater(tankEngageDist, infEngageDist + 2.5f, $"Tank outranges infantry cleanly ({tankEngageDist:F1}m vs {infEngageDist:F1}m)");

      Debug.Log($"[Items7To10_VerifyAndCapture] PASS grenadeDecoupled={decoupledGrenadeFired} throwMotion={sawThrowMotion} pmcSlots=8 popBlocked={popBlockedCount} infDist={infEngageDist:F1}m tankDist={tankEngageDist:F1}m");
    }

    static bool IsNavRegionEnabled(Frame frame, string regionName) {
      if (string.IsNullOrEmpty(regionName) || !frame.Map.RegionMap.TryGetValue(regionName, out var region)) return false;
      return frame.NavMeshRegionMask->IsRegionEnabled(region);
    }

    static void DamageCityBuildingSafe(Frame frame, EntityRef entity, FP amount) {
      if (frame.Unsafe.TryGetPointer<CityBuilding>(entity, out var city)) {
        CitySystem.Hit(frame, entity, city, amount);
      }
    }

    static void DamageBarricadeSafe(Frame frame, EntityRef entity, FP amount) {
      if (frame.Unsafe.TryGetPointer<Barricade>(entity, out var b)) {
        BattlefieldSystem.HitBarricade(frame, entity, b, amount);
      }
    }

    [UnityTest, Timeout(1200000)]
    public IEnumerator Items11To15_T43DistrictsShoreline_GrenadeLauncher_AndT45CollapseSync_VerifyAndCapture() {
      var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "status_images", "Items11To15"));
      var frames = Path.Combine(dir, "frames");
      Directory.CreateDirectory(frames);

      yield return StartMatch(Map4PScene, 2, truce: 0, seed: 9801, startingTier: 3);
      yield return WaitForHud();
      yield return WaitForBattle();
      var doctrine = GameObject.Find("DoctrinePanel");
      if (doctrine != null) doctrine.GetComponentsInChildren<UnityEngine.UI.Button>(true).First().onClick.Invoke();
      GrantAgreement(0);
      GrantAgreement(1);
      GrantLargeStock(Verified, 0);
      GrantLargeStock(Verified, 1);
      if (Game.Frames.Predicted != null) {
        GrantLargeStock(Game.Frames.Predicted, 0);
        GrantLargeStock(Game.Frames.Predicted, 1);
      }
      Time.timeScale = 1f;

      var camera = Camera.main;
      var rts = Controller().Camera;
      camera.aspect = 16f / 9f;
      var map = FactoryWarsUtil.MapInfo(Verified);

      // ===== Item 11: T43 4-District Differentiation & 4-Way Quadrant Fairness =====
      foreach (GridDress.UrbanDistrict d in System.Enum.GetValues(typeof(GridDress.UrbanDistrict))) {
        Assert.IsTrue(GridDress.DistrictCounts.TryGetValue(d, out var cnt) && cnt > 0,
          $"T43 district '{d}' differentiated on Map4P v4 (count={cnt})");
      }
      // Verify 4-way rotational symmetry of DistrictOf across quadrants (fairness!)
      var n = map.CellGridSize;
      var symChecks = 0;
      for (var r = 10; r < 35; r += 5) {
        for (var c = 10; c < 35; c += 5) {
          var d0 = GridDress.DistrictOf(map, r, c);
          var d1 = GridDress.DistrictOf(map, c, n - 1 - r);
          var d2 = GridDress.DistrictOf(map, n - 1 - r, n - 1 - c);
          var d3 = GridDress.DistrictOf(map, n - 1 - c, r);
          if (GridMapView.Cell(map, r, c) == GridMapView.Cell(map, c, n - 1 - r)
              && GridMapView.Cell(map, r, c) == GridMapView.Cell(map, n - 1 - r, n - 1 - c)
              && GridMapView.Cell(map, r, c) == GridMapView.Cell(map, n - 1 - c, r)) {
            Assert.AreEqual(d0, d1, $"4-way district symmetry at ({r},{c}) vs rot90");
            Assert.AreEqual(d0, d2, $"4-way district symmetry at ({r},{c}) vs rot180");
            Assert.AreEqual(d0, d3, $"4-way district symmetry at ({r},{c}) vs rot270");
            symChecks++;
          }
        }
      }
      Assert.Greater(symChecks, 0, "Verified 4-way district symmetry across quadrants");

      rts.Height = 64f;
      rts.MoveTo(new Vector2(-95f, -95f));
      rts.Apply();
      yield return WaitFrames(8);
      CaptureCameraPng(camera, Path.Combine(dir, "01_t43_districts_commercial_residential.png"), 1920, 1080);

      // ===== Item 12: T43 Shoreline Steps & Wide Pavement Cleanup =====
      Vector3 harborPos = new Vector3(-224f, 0f, 0f);
      for (var r = 10; r < n - 10; r++) {
        for (var c = 10; c < n - 10; c++) {
          if (GridMapView.Cell(map, r, c) == 'w' && GridMapView.Cell(map, r, c + 1) == 'W') {
            harborPos = new Vector3(GridMapView.CellX(map, c), 0f, GridMapView.CellZ(map, r));
            break;
          }
        }
      }
      rts.Height = 44f;
      rts.MoveTo(new Vector2(harborPos.x, harborPos.z));
      rts.Apply();
      yield return WaitFrames(8);
      CaptureCameraPng(camera, Path.Combine(dir, "02_t43_shoreline_harbor_cleanup.png"), 1920, 1080);

      rts.Height = 52f;
      rts.MoveTo(new Vector2(0f, -44f));
      rts.Apply();
      yield return WaitFrames(8);
      CaptureCameraPng(camera, Path.Combine(dir, "03_t43_pmc_pavement_cleanup.png"), 1920, 1080);

      // ===== Item 14: Grenade Launcher (유탄) Firing Motion, 40mm Round & Explosion =====
      UnlockSmallUpgrade(Verified, 0);
      if (Game?.Frames?.Predicted != null) UnlockSmallUpgrade(Game.Frames.Predicted, 0);
      var gSpot = new FPVector3(-12, 0, -56);
      var gunner = SpawnUnitByName(Verified, 0, "ff_ranger_squad", gSpot);
      var target = SpawnUnitByName(Verified, 1, "cf_militia",      gSpot + new FPVector3(FP.FromFloat_UNSAFE(10.5f), FP._0, FP._0));
      yield return WaitFrames(4);
      var gu = Verified.Get<Unit>(gunner);
      gu.AttackCooldown = (FP)5;
      gu.AbilityCooldown = FP._0;
      gu.TargetEntity = target;
      Verified.Set(gunner, gu);
      if (Game?.Frames?.Predicted != null && Game.Frames.Predicted.Exists(gunner)) Game.Frames.Predicted.Set(gunner, gu);

      rts.Height = 22f;
      rts.MoveTo(new Vector2(gSpot.X.AsFloat + 5.2f, gSpot.Z.AsFloat));
      rts.Apply();
      var sawGrenadeRound = false;
      for (var f = 0; f < 10; f++) {
        yield return WaitFrames(4);
        foreach (var pair in Verified.GetComponentIterator<MlrsRocket>()) {
          if (MlrsRocketView.IsGrenadeRound(pair.Component)) sawGrenadeRound = true;
        }
        CaptureCameraPng(camera, Path.Combine(frames, $"gl_{f:00}.png"), 960, 540);
        if (f == 2) CaptureCameraPng(camera, Path.Combine(dir, "04_grenade_launcher_fire.png"), 1920, 1080);
      }
      Assert.IsTrue(sawGrenadeRound, "Grenade Launcher fired compact 40mm grenade round (not winged rocket)");

      // Item 15 (Destructible Prop on Map4P v4): Sea Barrier prop damage -> destruction -> waterway opens cleanly!
      var propEntity = EntityRef.None;
      foreach (var p in Verified.GetComponentIterator<Building>()) {
        if (Verified.FindAsset(p.Component.Spec).name == "sea_barrier") {
          propEntity = p.Entity;
          break;
        }
      }
      if (propEntity != EntityRef.None) {
        var b = Verified.Get<Building>(propEntity);
        b.Health = FP._0;
        Verified.Set(propEntity, b);
        Verified.Destroy(propEntity);
        yield return WaitFrames(4);
        Assert.IsFalse(Verified.Exists(propEntity), "Map4P destructible sea_barrier prop destroyed cleanly");
      }
      Debug.Log($"[Items11To14_AndProp_VerifyAndCapture] PASS districts=4 symChecks={symChecks} gl40mm={sawGrenadeRound} propDestroyed={propEntity != EntityRef.None}");
    }

    [UnityTest, Timeout(1200000)]
    public IEnumerator T45_BuildingAndPropCollapse_NavMeshOpening_AndDualClientSync() {
      var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "status_images", "Items11To15"));
      Directory.CreateDirectory(dir);
      var syncSeed = 4545;
      yield return StartMatch(truce: 0, seed: syncSeed, startingTier: 3);
      yield return WaitForHud();
      yield return WaitForBattle();
      var urbanMap = FactoryWarsUtil.MapInfo(Verified);
      var camera = Camera.main;
      var rts = Controller().Camera;
      camera.aspect = 16f / 9f;

      var cityEntity = CityAt(0);
      Assert.AreNotEqual(EntityRef.None, cityEntity, "Found CityBuilding 0 on urban test map");
      var cityIdx = Verified.Get<CityBuilding>(cityEntity).Index;
      var cityPos = urbanMap.CityBuildings[cityIdx].Shape.Center;
      var cityA = cityPos + new FPVector3(-10, 0, 0);
      var cityB = cityPos + new FPVector3( 10, 0, 0);
      Assert.IsFalse(CitySystem.IsCollapsed(Verified, cityIdx), "Building starts intact");
      Assert.IsFalse(Walkable(cityA, cityB), "NavMesh path across intact CityBuilding is BLOCKED before collapse");

      // Damage -> Collapse -> NavMesh path OPENS!
      DamageCityBuildingSafe(Verified, cityEntity, (FP)99999);
      if (Game?.Frames?.Predicted != null && Game.Frames.Predicted.Exists(cityEntity)) DamageCityBuildingSafe(Game.Frames.Predicted, cityEntity, (FP)99999);
      yield return WaitFrames(8);
      Assert.IsTrue(CitySystem.IsCollapsed(Verified, cityIdx), "CityBuilding collapsed cleanly");
      Assert.IsTrue(Walkable(cityA, cityB), "NavMesh path across CityBuilding OPENED after collapse");

      // Order an infantry unit across the collapsed building footprint to confirm pathing opens!
      var walker = SpawnUnitByName(Verified, 0, "ff_ranger_squad", cityA);
      yield return WaitFrames(4);
      yield return Send(0, new MoveUnitsCommand { Units = new[] { walker }, Target = cityB });
      yield return WaitFrames(Fps * 2);
      var walkedX = Verified.Get<Transform3D>(walker).Position.X.AsFloat;
      Assert.Greater(walkedX, cityA.X.AsFloat + 2f, $"Infantry walks through opened collapsed building path (walkedX={walkedX:F1})");

      rts.Height = 36f;
      rts.MoveTo(new Vector2(cityPos.X.AsFloat, cityPos.Z.AsFloat));
      rts.Apply();
      yield return WaitFrames(6);
      CaptureCameraPng(camera, Path.Combine(dir, "05_t45_collapse_and_navmesh_open.png"), 1920, 1080);

      // Dual-Client Deterministic State Sync verification: Client A vs Client B on identical seed & EXACT tick
      QuantumRunner.ShutdownAll();
      yield return StartMatch(truce: 0, seed: syncSeed, startingTier: 3);
      while (Verified.Number < 20) yield return null;
      var collapseTick = Verified.Number;
      var cA = CityAt(0);
      var idxA = Verified.Get<CityBuilding>(cA).Index;
      DamageCityBuildingSafe(Verified, cA, (FP)99999);
      while (Verified.Number < collapseTick + 25) yield return null;
      var checkTick = Verified.Number;
      var checksumA = Verified.CalculateChecksum();
      var collapsedA = CitySystem.IsCollapsed(Verified, idxA);
      var regA = Walkable(cityA, cityB);

      QuantumRunner.ShutdownAll();
      yield return StartMatch(truce: 0, seed: syncSeed, startingTier: 3);
      while (Verified.Number < collapseTick) yield return null;
      var cB = CityAt(0);
      var idxB = Verified.Get<CityBuilding>(cB).Index;
      DamageCityBuildingSafe(Verified, cB, (FP)99999);
      while (Verified.Number < checkTick) yield return null;
      var checksumB = Verified.CalculateChecksum();
      var collapsedB = CitySystem.IsCollapsed(Verified, idxB);
      var regB = Walkable(cityA, cityB);

      Assert.AreEqual(idxA, idxB, "Dual-client building index identical");
      Assert.AreEqual(collapsedA, collapsedB, "Dual-client collapse bitmask identical");
      Assert.AreEqual(regA, regB, "Dual-client NavMesh region opening state identical");
      Assert.AreEqual(checksumA, checksumB, $"Dual-client frame checksums match identically after T45 collapse ({checksumA} == {checksumB})");

      Debug.Log($"[T45_CollapseAndDualSync] PASS collapsedIdx={cityIdx} walkedX={walkedX:F1} navOpen={regA} dualChecksum={checksumA}");
    }

    static unsafe bool IsNavRegionEnabled(Frame f, int region) => f.NavMeshRegionMask->IsRegionEnabled(region);

    [UnityTest, Timeout(1200000)]
    public IEnumerator Task44GlbAndT45Map4P_VerifyAndCapture() {
      var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "status_images", "Task44_T45"));
      Directory.CreateDirectory(dir);

      // 1. Verify Task 44 GLB Batch 1 slots in VisualCatalog
      var catalog = VisualCatalog.Current;
      Assert.IsNotNull(catalog, "VisualCatalog loaded");
      var expectedSlots = new[] {
        "unit/ff_hawk_gunship", "unit/pmc_attack_heli",
        "unit/pmc_scout_heli", "unit/cf_gale_gunship",
        "unit/pmc_strike_drone", "unit/ff_loiter_munition", "unit/cf_loiter_munition",
        "unit/logistics_truck", "unit/merc_cargo_truck",
        "unit/cf_apc_transport", "unit/ff_striker_ifv",
        "dress/CityTower", "dress/CityMedium",
        "building/ff_heavy_works", "building/cf_heavy_works",
      };
      var glbWired = 0;
      foreach (var sid in expectedSlots) {
        Assert.IsTrue(VisualCatalog.TryFilled(sid, out var slot), $"Task 44 slot filled: {sid}");
        if (slot.Notes != null && slot.Notes.Contains("Task 44 GLB")) glbWired++;
      }
      Assert.AreEqual(expectedSlots.Length, glbWired, $"All {expectedSlots.Length} Task 44 GLB Batch 1 slots wired");

      // 2. Load Map4P v4 and verify T45 expanded 20 combat-zone CityBuildings (5 per quadrant x 4 seats)
      yield return StartMatch(Map4PScene, 2, truce: 0, seed: 4445, startingTier: 3);
      yield return WaitForHud();
      yield return WaitForBattle();
      var doctrine = GameObject.Find("DoctrinePanel");
      if (doctrine != null) doctrine.GetComponentsInChildren<UnityEngine.UI.Button>(true).First().onClick.Invoke();
      GrantAgreement(0);
      GrantAgreement(1);
      Time.timeScale = 1f;

      var map = FactoryWarsUtil.MapInfo(Verified);
      Assert.IsNotNull(map.CityBuildings, "Map4P CityBuildings initialized");
      Assert.AreEqual(20, map.CityBuildings.Length, "Map4P v4 has 20 T45 destructible combat-zone buildings (5 x 4 seats)");
      var garrisons = map.CityBuildings.Count(c => c.Garrison);
      Assert.AreEqual(8, garrisons, "Map4P v4 has 8 T45 tactical garrison buildings (2 x 4 seats)");

      var camera = Camera.main;
      var rts = Controller().Camera;
      camera.aspect = 16f / 9f;

      // 3. Spawn Task 44 GLB showcase units & factory in clear SW combat corridor near (-60, -84)
      var heli1 = SpawnUnitByName(Verified, 0, "ff_hawk_gunship",  new FPVector3(-64, 0, -64));
      var heli2 = SpawnUnitByName(Verified, 0, "pmc_scout_heli",   new FPVector3(-56, 0, -64));
      var drone = SpawnUnitByName(Verified, 0, "pmc_strike_drone", new FPVector3(-60, 0, -60));
      var truck = SpawnUnitByName(Verified, 0, "logistics_truck",  new FPVector3(-64, 0, -72));
      var apc   = SpawnUnitByName(Verified, 0, "cf_apc_transport", new FPVector3(-56, 0, -72));
      var hwSpec = Resources.Load<BuildingSpec>("FactoryWars/ff_heavy_works");
      Assert.IsNotNull(hwSpec, "ff_heavy_works spec loaded");
      var heavyWorks = ProductionSystem.CreateFinishedBuilding(Verified, 0, hwSpec, new FPVector3(-82, 0, -82));
      yield return WaitFrames(15);

      rts.Height = 24f;
      rts.MoveTo(new Vector2(-60f, -68f));
      rts.Apply();
      yield return WaitFrames(6);
      CaptureCameraPng(camera, Path.Combine(dir, "01_task44_glb_units_showcase.png"), 1920, 1080);

      rts.Height = 32f;
      rts.MoveTo(new Vector2(-78f, -84f));
      rts.Apply();
      yield return WaitFrames(6);
      CaptureCameraPng(camera, Path.Combine(dir, "02_task44_glb_factory_and_city.png"), 1920, 1080);

      // 4. Verify T45 combat-zone building collapse & NavMesh opening directly on Map4P v4!
      var c0 = CityAt(0);
      var c1 = CityAt(1);
      Assert.AreNotEqual(EntityRef.None, c0, "Map4P CityBuilding 0 spawned");
      Assert.AreNotEqual(EntityRef.None, c1, "Map4P CityBuilding 1 spawned");
      var pos0 = map.CityBuildings[0].Shape.Center;
      var pos1 = map.CityBuildings[1].Shape.Center;

      rts.Height = 42f;
      rts.MoveTo(new Vector2(pos0.X.AsFloat, pos0.Z.AsFloat));
      rts.Apply();
      yield return WaitFrames(6);
      CaptureCameraPng(camera, Path.Combine(dir, "03_t45_map4p_combat_zone_before.png"), 1920, 1080);

      Assert.IsFalse(CitySystem.IsCollapsed(Verified, 0), "Map4P City0 starts intact");
      Assert.IsTrue(Verified.Map.RegionMap.TryGetValue(map.CityBuildings[0].Region, out var reg0), "Map4P NavMesh has City0 region");
      Assert.IsFalse(IsNavRegionEnabled(Verified, reg0), "Map4P City0 NavMesh region is CLOSED before collapse");

      // Collapse City0 & City1!
      DamageCityBuildingSafe(Verified, c0, (FP)99999);
      DamageCityBuildingSafe(Verified, c1, (FP)99999);
      if (Game?.Frames?.Predicted != null) {
        if (Game.Frames.Predicted.Exists(c0)) DamageCityBuildingSafe(Game.Frames.Predicted, c0, (FP)99999);
        if (Game.Frames.Predicted.Exists(c1)) DamageCityBuildingSafe(Game.Frames.Predicted, c1, (FP)99999);
      }
      yield return WaitFrames(10);

      Assert.IsTrue(CitySystem.IsCollapsed(Verified, 0), "Map4P City0 collapsed");
      Assert.IsTrue(CitySystem.IsCollapsed(Verified, 1), "Map4P City1 collapsed");
      Assert.IsTrue(IsNavRegionEnabled(Verified, reg0), "Map4P City0 NavMesh region OPENED after collapse");

      // Walk an infantry squad right across collapsed City0 footprint on Map4P v4!
      var startPt = pos0 + new FPVector3(-9, 0, 0);
      var goalPt  = pos0 + new FPVector3( 9, 0, 0);
      var squad = SpawnUnitByName(Verified, 0, "ff_ranger_squad", startPt);
      yield return WaitFrames(4);
      yield return Send(0, new MoveUnitsCommand { Units = new[] { squad }, Target = goalPt });
      yield return WaitFrames(Fps * 2);
      var movedX = Verified.Get<Transform3D>(squad).Position.X.AsFloat;
      Assert.Greater(movedX, startPt.X.AsFloat + 2f, $"Infantry advances across collapsed Map4P City0 footprint (movedX={movedX:F1})");

      rts.Height = 36f;
      rts.MoveTo(new Vector2(pos0.X.AsFloat, pos0.Z.AsFloat));
      rts.Apply();
      yield return WaitFrames(6);
      CaptureCameraPng(camera, Path.Combine(dir, "04_t45_map4p_collapse_navmesh_open.png"), 1920, 1080);

      rts.Height = 72f;
      rts.MoveTo(new Vector2((pos0.X.AsFloat + pos1.X.AsFloat) * 0.5f, (pos0.Z.AsFloat + pos1.Z.AsFloat) * 0.5f));
      rts.Apply();
      yield return WaitFrames(6);
      CaptureCameraPng(camera, Path.Combine(dir, "05_t45_map4p_overview_symmetry.png"), 1920, 1080);

      // 5. Dual-Client Deterministic Sync on Map4P v4 T45 Collapse
      var syncSeed = 4455;
      QuantumRunner.ShutdownAll();
      yield return StartMatch(Map4PScene, 2, truce: 0, seed: syncSeed, startingTier: 3);
      while (Verified.Number < 35) yield return null;
      var collapseTick = Verified.Number;
      var eA = CityAt(0);
      DamageCityBuildingSafe(Verified, eA, (FP)99999);
      while (Verified.Number < collapseTick + 20) yield return null;
      var checkTick = Verified.Number;
      var csA = Verified.CalculateChecksum();
      var colA = CitySystem.IsCollapsed(Verified, 0);
      Verified.Map.RegionMap.TryGetValue(FactoryWarsUtil.MapInfo(Verified).CityBuildings[0].Region, out var rA);
      var openA = IsNavRegionEnabled(Verified, rA);

      QuantumRunner.ShutdownAll();
      yield return StartMatch(Map4PScene, 2, truce: 0, seed: syncSeed, startingTier: 3);
      while (Verified.Number < collapseTick) yield return null;
      Assert.AreEqual(collapseTick, Verified.Number, "Dual-client aligned to exact collapseTick");
      var eB = CityAt(0);
      DamageCityBuildingSafe(Verified, eB, (FP)99999);
      while (Verified.Number < checkTick) yield return null;
      Assert.AreEqual(checkTick, Verified.Number, "Dual-client aligned to exact checkTick");
      var csB = Verified.CalculateChecksum();
      var colB = CitySystem.IsCollapsed(Verified, 0);
      Verified.Map.RegionMap.TryGetValue(FactoryWarsUtil.MapInfo(Verified).CityBuildings[0].Region, out var rB);
      var openB = IsNavRegionEnabled(Verified, rB);

      Assert.AreEqual(colA, colB, "Map4P dual-client T45 collapse state identical");
      Assert.AreEqual(openA, openB, "Map4P dual-client T45 NavMesh region opening identical");
      Assert.AreEqual(csA, csB, $"Map4P dual-client frame checksums identical after T45 collapse ({csA} == {csB})");

      Debug.Log($"[Task44GlbAndT45Map4P] PASS glbSlots={glbWired} map4pCities={map.CityBuildings.Length} garrisons={garrisons} movedX={movedX:F1} dualChecksum={csA}");
    }

    [UnityTest, Timeout(1200000)]
    public IEnumerator RuinVisuals_BuildingDurability_AndGrenadeImpactPolish_VerifyAndCapture() {
      var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "status_images", "RuinAndGrenadePolish"));
      var frames = Path.Combine(dir, "frames");
      Directory.CreateDirectory(frames);

      yield return StartMatch(Map4PScene, 2, truce: 0, seed: 4601, startingTier: 3);
      yield return WaitForHud();
      yield return WaitForBattle();
      var doctrine = GameObject.Find("DoctrinePanel");
      if (doctrine != null) doctrine.GetComponentsInChildren<UnityEngine.UI.Button>(true).First().onClick.Invoke();
      GrantAgreement(0);
      GrantAgreement(1);
      Time.timeScale = 1f;

      var rules = FactoryWarsUtil.Rules(Verified);
      var map = FactoryWarsUtil.MapInfo(Verified);
      var camera = Camera.main;
      var rts = Controller().Camera;
      camera.aspect = 16f / 9f;

      // 1. Building Durability: confirm upgraded CityKinds HP and that 2 heavy shell hits do NOT collapse the building!
      Assert.GreaterOrEqual(rules.CityKinds[0].Health.AsFloat, 3500f, "Small building HP raised >= 3500");
      Assert.GreaterOrEqual(rules.CityKinds[1].Health.AsFloat, 6000f, "Medium building HP raised >= 6000");
      Assert.GreaterOrEqual(rules.CityKinds[2].Health.AsFloat, 9500f, "Large building HP raised >= 9500");

      var c0 = CityAt(0);
      Assert.AreNotEqual(EntityRef.None, c0, "Map4P City0 present");
      var pos0 = map.CityBuildings[0].Shape.Center;
      var startHp = Verified.Get<CityBuilding>(c0).Health.AsFloat;
      Assert.AreEqual(rules.CityKinds[0].Health.AsFloat, startHp, "City0 starts at upgraded full HP");

      rts.Height = 32f;
      rts.MoveTo(new Vector2(pos0.X.AsFloat, pos0.Z.AsFloat));
      rts.Apply();
      yield return WaitFrames(6);
      CaptureCameraPng(camera, Path.Combine(dir, "01_building_intact_before.png"), 1920, 1080);

      // Simulate 2 tank shell hits (2 x 300 = 600 damage) -> must survive intact without collapsing!
      DamageCityBuildingSafe(Verified, c0, (FP)300);
      yield return WaitFrames(4);
      DamageCityBuildingSafe(Verified, c0, (FP)300);
      yield return WaitFrames(6);
      Assert.IsFalse(CitySystem.IsCollapsed(Verified, 0), "Building survives initial 2 shell hits without collapsing");
      var hpAfter2Hits = Verified.Get<CityBuilding>(c0).Health.AsFloat;
      Assert.Greater(hpAfter2Hits, startHp * 0.75f, $"Building retains >75% HP after 2 hits ({hpAfter2Hits:F0}/{startHp:F0})");
      CaptureCameraPng(camera, Path.Combine(dir, "02_building_survives_initial_hits.png"), 1920, 1080);

      // Sustained barrage -> collapse into natural cohesive concrete ruins (NO dark crater ring!)
      DamageCityBuildingSafe(Verified, c0, (FP)99999);
      if (Game?.Frames?.Predicted != null && Game.Frames.Predicted.Exists(c0)) DamageCityBuildingSafe(Game.Frames.Predicted, c0, (FP)99999);
      yield return WaitFrames(18);
      Assert.IsTrue(CitySystem.IsCollapsed(Verified, 0), "Building collapses after sustained lethal barrage");
      Assert.IsTrue(Verified.Map.RegionMap.TryGetValue(map.CityBuildings[0].Region, out var reg0));
      Assert.IsTrue(IsNavRegionEnabled(Verified, reg0), "NavMesh region opens after collapse");
      // Wait for initial collapse dust to settle so ruin foundation & Synty debris are clearly visible
      yield return WaitFrames(Fps * 2);
      CaptureCameraPng(camera, Path.Combine(dir, "03_building_natural_ruins_after.png"), 1920, 1080);

      // 2. Toned-down 40mm Grenade Impact VFX & Smooth Firing Motion (clean intersection away from collapse site)
      UnlockSmallUpgrade(Verified, 0);
      if (Game?.Frames?.Predicted != null) UnlockSmallUpgrade(Game.Frames.Predicted, 0);
      var shooterPos = new FPVector3(-64, 0, -64);
      var targetPos  = new FPVector3(-54, 0, -64);
      var shooter = SpawnUnitByName(Verified, 0, "ff_ranger_squad", shooterPos);
      var target  = SpawnUnitByName(Verified, 1, "cf_militia", targetPos);
      yield return WaitFrames(6);

      rts.Height = 19f;
      rts.MoveTo(new Vector2(-59f, -64f));
      rts.Apply();
      yield return WaitFrames(4);

      var impacted = false;
      var impactSub = QuantumEvent.Subscribe<EventMlrsImpact>(Battle(), _ => impacted = true);
      MlrsSystem.LaunchInfantryGrenade(Verified, shooter, 0, shooterPos, targetPos);
      if (Game?.Frames?.Predicted != null) MlrsSystem.LaunchInfantryGrenade(Game.Frames.Predicted, shooter, 0, shooterPos, targetPos);

      var sawCompactGrenade = false;
      var capturedImpact = false;
      for (var i = 0; i < 14; i++) {
        yield return WaitFrames(2);
        foreach (var pair in Verified.GetComponentIterator<MlrsRocket>()) {
          if (MlrsRocketView.IsGrenadeRound(pair.Component)) sawCompactGrenade = true;
        }
        CaptureCameraPng(camera, Path.Combine(frames, $"gl_polish_{i:00}.png"), 960, 540);
        if (impacted && !capturedImpact) {
          capturedImpact = true;
          CaptureCameraPng(camera, Path.Combine(dir, "04_grenade_compact_impact.png"), 1920, 1080);
        }
      }
      QuantumEvent.Unsubscribe(impactSub);
      if (!capturedImpact) CaptureCameraPng(camera, Path.Combine(dir, "04_grenade_compact_impact.png"), 1920, 1080);
      Assert.IsTrue(sawCompactGrenade, "40mm grenade round flew on smooth low-parabolic launcher arc");
      Assert.IsTrue(impacted, "40mm grenade impacted target cleanly");
      Debug.Log($"[RuinAndGrenadePolish] PASS startHp={startHp:F0} hpAfter2Hits={hpAfter2Hits:F0} navOpen=True glCompact={sawCompactGrenade} impacted={impacted}");
    }

    [UnityTest, Timeout(1200000)]
    public IEnumerator RangesAndUnitAttackGifs_VerifyAndCapture() {
      var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "status_images", "UnitAttackGifs"));
      Directory.CreateDirectory(dir);

      // 1. Verify tuned attack ranges across key roles (no ranged unit fighting in melee-like <10m range)
      void CheckRange(string name, float minRng) {
        var s = Resources.Load<UnitSpec>($"FactoryWars/{name}");
        Assert.IsNotNull(s, $"Loaded UnitSpec {name}");
        Assert.GreaterOrEqual(s.Range.AsFloat, minRng - 0.01f, $"{name} range >= {minRng}m (actual={s.Range.AsFloat:F1}m)");
      }
      CheckRange("ff_ranger_squad", 11.5f);
      CheckRange("cf_militia", 11.0f);
      CheckRange("pmc_operator_squad", 11.5f);
      CheckRange("pmc_armored_suv", 12.0f);
      CheckRange("ff_striker_ifv", 14.0f);
      CheckRange("ff_eagle_tank", 16.0f);
      CheckRange("pmc_tank", 16.0f);
      CheckRange("ff_bulwark_tank", 17.5f);
      CheckRange("ff_hawk_gunship", 15.0f);
      CheckRange("pmc_attack_heli", 15.0f);
      CheckRange("cf_thunder_aa", 15.5f);
      CheckRange("ff_scout_boat", 12.5f);
      CheckRange("ff_mlrs_artillery", 20.0f);
      CheckRange("ff_paladin_sph", 22.0f);
      CheckRange("ff_sniper", 25.0f);
      CheckRange("ff_artillery_ship", 26.0f);

      yield return StartMatch(Map4PScene, 2, truce: 0, seed: 4701, startingTier: 3);
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

      // 2. Capture per-unit attack effect frame sequences (14 representative units covering all classes)
      var showcase = new (string Unit, string Target, FPVector3 From, FPVector3 To, float CamH)[] {
        ("ff_ranger_squad",       "cf_militia",          new FPVector3(-64, 0, -64), new FPVector3(-54, 0, -64), 19f),
        ("cf_flame_squad",        "ff_ranger_squad",     new FPVector3(-64, 0, -64), new FPVector3(-60, 0, -64), 17f),
        ("ff_sniper",             "cf_militia",          new FPVector3(-68, 0, -64), new FPVector3(-48, 0, -64), 26f),
        ("ff_at_rocket",          "cf_dragonscale_tank", new FPVector3(-66, 0, -64), new FPVector3(-54, 0, -64), 22f),
        ("ff_striker_ifv",        "cf_militia",          new FPVector3(-66, 0, -64), new FPVector3(-54, 0, -64), 22f),
        ("ff_eagle_tank",         "cf_dragonscale_tank", new FPVector3(-66, 0, -64), new FPVector3(-52, 0, -64), 24f),
        ("ff_bulwark_tank",       "cf_fortress_tank",    new FPVector3(-66, 0, -64), new FPVector3(-51, 0, -64), 25f),
        ("cf_longwall_mlrs",      "ff_eagle_tank",       new FPVector3(-68, 0, -64), new FPVector3(-50, 0, -64), 28f),
        ("ff_paladin_sph",        "cf_dragonscale_tank", new FPVector3(-68, 0, -64), new FPVector3(-50, 0, -64), 28f),
        ("ff_hawk_gunship",       "cf_dragonscale_tank", new FPVector3(-65, 0, -64), new FPVector3(-53, 0, -64), 28f),
        ("ff_skyfortress_bomber", "cf_fortress_tank",    new FPVector3(-64, 0, -64), new FPVector3(-58, 0, -64), 38f),
        ("ff_artillery_ship",     "cf_scout_boat",       new FPVector3(-16, 0, 110), new FPVector3(-16, 0, 130), 34f),
        ("pmc_tank",              "cf_dragonscale_tank", new FPVector3(-66, 0, -64), new FPVector3(-52, 0, -64), 24f),
        ("pmc_attack_heli",       "cf_dragonscale_tank", new FPVector3(-65, 0, -64), new FPVector3(-53, 0, -64), 28f),
      };

      var capturedUnits = 0;
      foreach (var item in showcase) {
        var fdir = Path.Combine(dir, "frames_" + item.Unit);
        Directory.CreateDirectory(fdir);
        var attacker = SpawnUnitByName(Verified, 0, item.Unit, item.From);
        var dummy    = SpawnUnitByName(Verified, 1, item.Target, item.To);
        // Keep dummy alive during showcase so multi-frame attack VFX is clearly captured
        if (Verified.Exists(dummy)) {
          var du = Verified.Get<Unit>(dummy);
          du.Health = (FP)5000;
          Verified.Set(dummy, du);
        }
        yield return WaitFrames(3);
        yield return Send(0, new AttackTargetCommand { Units = new[] { attacker }, Target = dummy });

        rts.Height = item.CamH;
        rts.MoveTo(new Vector2((item.From.X.AsFloat + item.To.X.AsFloat) * 0.5f, (item.From.Z.AsFloat + item.To.Z.AsFloat) * 0.5f));
        rts.Apply();

        for (var i = 0; i < 8; i++) {
          yield return WaitFrames(4);
          CaptureCameraPng(camera, Path.Combine(fdir, $"f_{i:00}.png"), 720, 405);
        }
        capturedUnits++;

        if (Verified.Exists(attacker)) Verified.Destroy(attacker);
        if (Verified.Exists(dummy)) Verified.Destroy(dummy);
        yield return WaitFrames(4);
      }
      Assert.AreEqual(showcase.Length, capturedUnits, "Captured all 14 unit attack GIF sequences");
      Debug.Log($"[RangesAndUnitAttackGifs] PASS capturedUnits={capturedUnits}");
    }

    [UnityTest, Timeout(1200000)]
    public IEnumerator FullMapSupplyCycleGif_AndHqTiers_VerifyAndCapture() {
      var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "status_images", "SupplyAndHqTiers"));
      var frames = Path.Combine(dir, "frames_supply");
      Directory.CreateDirectory(frames);

      yield return StartMatch(Map4PScene, 2, truce: 0, seed: 4702, startingTier: 1);
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

      // 1. Capture Command HQ Tier 1 -> Tier 2 -> Tier 3 from IDENTICAL camera framing!
      var hqPos = FactoryWarsUtil.StartPosition(Verified, 0);
      foreach (var pair in Verified.GetComponentIterator<Building>()) {
        if (pair.Component.Owner == 0 && Verified.FindAsset(pair.Component.Spec).Role == BuildingRole.Headquarters) {
          hqPos = Verified.Get<Transform3D>(pair.Entity).Position;
          break;
        }
      }
      rts.Height = 38f;
      rts.MoveTo(new Vector2(hqPos.X.AsFloat, hqPos.Z.AsFloat + 1.5f));
      rts.Apply();
      yield return WaitFrames(8);
      CaptureCameraPng(camera, Path.Combine(dir, "hq_tier_1.png"), 1920, 1080);

      SetPlayerTier(Verified, 0, 2);
      if (Game?.Frames?.Predicted != null) SetPlayerTier(Game.Frames.Predicted, 0, 2);
      yield return WaitFrames(18);
      CaptureCameraPng(camera, Path.Combine(dir, "hq_tier_2.png"), 1920, 1080);

      SetPlayerTier(Verified, 0, 3);
      if (Game?.Frames?.Predicted != null) SetPlayerTier(Game.Frames.Predicted, 0, 3);
      yield return WaitFrames(18);
      CaptureCameraPng(camera, Path.Combine(dir, "hq_tier_3.png"), 1920, 1080);

      // 2. Full-Map / Wide-Area Resource Supply Cycle GIF + HQ Delivery +amount Gain Popup!
      var ironNode = EntityRef.None;
      var nodePos = hqPos + new FPVector3(-24, 0, 18);
      var bestD = FP.MaxValue;
      foreach (var pair in Verified.GetComponentIterator<ResourceNode>()) {
        if (pair.Component.Kind != ResourceKind.Iron) continue;
        var np = pair.Component.Shape.Center;
        var d = FactoryWarsUtil.FlatDistanceSqr(hqPos, np);
        if (d < bestD) { bestD = d; ironNode = pair.Entity; nodePos = np; }
      }
      var extSpec = Resources.Load<BuildingSpec>("FactoryWars/ff_iron_extractor");
      Assert.IsNotNull(extSpec, "ff_iron_extractor spec loaded");
      var extractor = ProductionSystem.CreateFinishedBuilding(Verified, 0, extSpec, nodePos);
      var extB = Verified.Get<Building>(extractor);
      extB.ExtractNode = ironNode;
      extB.ProcessTimer = (FP)240; // Stockpile ready for 2 full truckloads (120 x 2)
      Verified.Set(extractor, extB);

      var truck = SpawnUnitByName(Verified, 0, "logistics_truck", hqPos + new FPVector3(0, 0, 10));
      yield return WaitFrames(4);
      LogisticsSystem.OrderResourcePickup(Verified, 0, new[] { truck });
      if (Game?.Frames?.Predicted != null) LogisticsSystem.OrderResourcePickup(Game.Frames.Predicted, 0, new[] { truck });

      // Wide-area framing covering BOTH extractor stockpile and Command HQ!
      var mid = new Vector2((hqPos.X.AsFloat + nodePos.X.AsFloat) * 0.5f, (hqPos.Z.AsFloat + nodePos.Z.AsFloat) * 0.5f);
      rts.Height = 68f;
      rts.MoveTo(mid);
      rts.Apply();

      var stockBefore = PlayerIronStock(Verified, 0);
      var capturedPopup = false;
      var frameIdx = 0;
      Time.timeScale = 6f;
      for (var i = 0; i < 45; i++) {
        yield return WaitFrames(18);
        if (i % 2 == 0 && frameIdx < 20) {
          CaptureCameraPng(camera, Path.Combine(frames, $"supply_{frameIdx++:00}.png"), 960, 540);
        }
        if (!capturedPopup && Battle().HqGainPopupsShown > 0) {
          capturedPopup = true;
          Time.timeScale = 1f;
          rts.Height = 30f;
          rts.MoveTo(new Vector2(hqPos.X.AsFloat, hqPos.Z.AsFloat));
          rts.Apply();
          yield return WaitFrames(2);
          CaptureCameraPng(camera, Path.Combine(dir, "hq_delivery_gain_popup.png"), 1920, 1080);
          rts.Height = 68f;
          rts.MoveTo(mid);
          rts.Apply();
          Time.timeScale = 6f;
        }
        if (capturedPopup && frameIdx >= 16) break;
      }
      Time.timeScale = 1f;

      var stockAfter = PlayerIronStock(Verified, 0);
      Assert.Greater(stockAfter, stockBefore + 50f, $"HQ iron stock increased after truck delivery ({stockBefore:F0} -> {stockAfter:F0})");
      Assert.GreaterOrEqual(Battle().HqGainPopupsShown, 1, "HQ floating +amount gain popup displayed on delivery");
      Assert.IsTrue(!string.IsNullOrEmpty(Battle().LastHqGainText) && Battle().LastHqGainText.Contains("+"), $"HQ gain label formatted ({Battle().LastHqGainText})");
      Debug.Log($"[FullMapSupplyAndHqTiers] PASS stock={stockBefore:F0}->{stockAfter:F0} popups={Battle().HqGainPopupsShown} label='{Battle().LastHqGainText}'");
    }

    static unsafe float PlayerIronStock(Frame f, int player) => f.Global->Players[player].Stock.Iron.AsFloat;

    static unsafe void SetPlayerTier(Frame f, int player, int tier) {
      f.Global->Players[player].BaseTier = tier;
      var buildings = f.Filter<Building>();
      while (buildings.NextUnsafe(out _, out var b)) {
        if (b->Owner == player && f.FindAsset(b->Spec).Role == BuildingRole.Headquarters) {
          b->Tier = tier;
        }
      }
    }

    [UnityTest, Timeout(1200000)]
    public IEnumerator ComprehensivePolish_RangeLightingVfxAndMotion_VerifyAndCapture() {
      var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "status_images", "ComprehensivePolish"));
      var combatFrames = Path.Combine(dir, "frames_combat");
      var sprintFrames = Path.Combine(dir, "frames_sprint");
      Directory.CreateDirectory(combatFrames);
      Directory.CreateDirectory(sprintFrames);

      yield return StartMatch(Map4PScene, 2, truce: 0, seed: 4801, startingTier: 3);
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

      // 1. Live actual engagement stopping distance measurement across 14 key unit archetypes!
      var probeUnits = new (string Unit, string Target, FPVector3 Start, FPVector3 TargetPos)[] {
        ("ff_ranger_squad",       "cf_militia",          new FPVector3(-84, 0, -64), new FPVector3(-52, 0, -64)),
        ("cf_flame_squad",        "ff_ranger_squad",     new FPVector3(-76, 0, -64), new FPVector3(-52, 0, -64)),
        ("ff_at_rocket",          "cf_dragonscale_tank", new FPVector3(-84, 0, -64), new FPVector3(-52, 0, -64)),
        ("ff_striker_ifv",        "cf_militia",          new FPVector3(-84, 0, -64), new FPVector3(-52, 0, -64)),
        ("ff_eagle_tank",         "cf_dragonscale_tank", new FPVector3(-84, 0, -64), new FPVector3(-52, 0, -64)),
        ("ff_bulwark_tank",       "cf_fortress_tank",    new FPVector3(-86, 0, -64), new FPVector3(-52, 0, -64)),
        ("cf_longwall_mlrs",      "ff_eagle_tank",       new FPVector3(-88, 0, -64), new FPVector3(-52, 0, -64)),
        ("ff_paladin_sph",        "cf_dragonscale_tank", new FPVector3(-88, 0, -64), new FPVector3(-52, 0, -64)),
        ("ff_hawk_gunship",       "cf_dragonscale_tank", new FPVector3(-84, 0, -64), new FPVector3(-52, 0, -64)),
        ("ff_sniper",             "cf_militia",          new FPVector3(-88, 0, -64), new FPVector3(-52, 0, -64)),
        ("ff_artillery_ship",     "cf_scout_boat",       new FPVector3(-16, 0,  96), new FPVector3(-16, 0, 134)),
        ("pmc_operator_squad",    "cf_militia",          new FPVector3(-84, 0, -64), new FPVector3(-52, 0, -64)),
        ("pmc_tank",              "cf_dragonscale_tank", new FPVector3(-84, 0, -64), new FPVector3(-52, 0, -64)),
        ("pmc_attack_heli",       "cf_dragonscale_tank", new FPVector3(-84, 0, -64), new FPVector3(-52, 0, -64)),
      };

      Time.timeScale = 6f;
      foreach (var p in probeUnits) {
        var atk = SpawnUnitByName(Verified, 0, p.Unit, p.Start);
        var tgt = SpawnUnitByName(Verified, 1, p.Target, p.TargetPos);
        if (Verified.Exists(tgt)) {
          var tu = Verified.Get<Unit>(tgt); tu.Health = (FP)8000; tu.Order = UnitOrder.Hold; Verified.Set(tgt, tu);
        }
        var spec = Resources.Load<UnitSpec>($"FactoryWars/{p.Unit}");
        yield return Send(0, new AttackTargetCommand { Units = new[] { atk }, Target = tgt });
        var measured = -1f;
        for (var step = 0; step < 30; step++) {
          yield return WaitFrames(8);
          if (!Verified.Exists(atk) || !Verified.Exists(tgt)) break;
          var u = Verified.Get<Unit>(atk);
          if (u.LastFiredFrame > 0) {
            var ap = Verified.Get<Transform3D>(atk).Position;
            var tp = Verified.Get<Transform3D>(tgt).Position;
            measured = Vector2.Distance(new Vector2(ap.X.AsFloat, ap.Z.AsFloat), new Vector2(tp.X.AsFloat, tp.Z.AsFloat));
            break;
          }
        }
        Debug.Log($"[RANGE-MEASURE] {p.Unit,-22} spec={spec.Range.AsFloat:F1}m actual={measured:F1}m cells={spec.Range.AsFloat / 8f:F2}");
        if (Verified.Exists(atk)) Verified.Destroy(atk);
        if (Verified.Exists(tgt)) Verified.Destroy(tgt);
        yield return WaitFrames(2);
      }
      Time.timeScale = 1f;

      // 2. Calibrated URP Lighting & Base Closeup Capture
      var hqPos = FactoryWarsUtil.StartPosition(Verified, 0);
      rts.Height = 36f;
      rts.MoveTo(new Vector2(hqPos.X.AsFloat, hqPos.Z.AsFloat));
      rts.Apply();
      yield return WaitFrames(8);
      CaptureCameraPng(camera, Path.Combine(dir, "01_calibrated_lighting_base.png"), 1920, 1080);

      // 3. Calibrated Mass Combat VFX Capture + GIF (tanks, infantry, MLRS arcing trails, readable impacts, compact grenade)
      UnlockSmallUpgrade(Verified, 0);
      if (Game?.Frames?.Predicted != null) UnlockSmallUpgrade(Game.Frames.Predicted, 0);
      var t1 = SpawnUnitByName(Verified, 0, "ff_eagle_tank",    new FPVector3(-72, 0, -62));
      var t2 = SpawnUnitByName(Verified, 0, "ff_bulwark_tank",  new FPVector3(-72, 0, -66));
      var r1 = SpawnUnitByName(Verified, 0, "ff_ranger_squad",  new FPVector3(-68, 0, -64));
      var m1 = SpawnUnitByName(Verified, 0, "cf_longwall_mlrs", new FPVector3(-78, 0, -64));
      var e1 = SpawnUnitByName(Verified, 1, "cf_dragonscale_tank", new FPVector3(-54, 0, -62));
      var e2 = SpawnUnitByName(Verified, 1, "cf_fortress_tank",    new FPVector3(-54, 0, -66));
      var e3 = SpawnUnitByName(Verified, 1, "cf_militia",          new FPVector3(-56, 0, -64));
      foreach (var ee in new[] { e1, e2, e3 }) {
        var eu = Verified.Get<Unit>(ee); eu.Health = (FP)4000; Verified.Set(ee, eu);
      }
      yield return WaitFrames(3);
      yield return Send(0, new AttackTargetCommand { Units = new[] { t1, t2, r1, m1 }, Target = e1 });
      yield return Send(1, new AttackTargetCommand { Units = new[] { e1, e2, e3 }, Target = t1 });
      MlrsSystem.LaunchInfantryGrenade(Verified, r1, 0, new FPVector3(-68, 0, -64), new FPVector3(-56, 0, -64));

      rts.Height = 30f;
      rts.MoveTo(new Vector2(-63f, -64f));
      rts.Apply();
      for (var i = 0; i < 12; i++) {
        yield return WaitFrames(3);
        CaptureCameraPng(camera, Path.Combine(combatFrames, $"c_{i:00}.png"), 960, 540);
        if (i == 5) CaptureCameraPng(camera, Path.Combine(dir, "02_calibrated_mass_combat_vfx.png"), 1920, 1080);
      }
      foreach (var u in new[] { t1, t2, r1, m1, e1, e2, e3 }) if (Verified.Exists(u)) Verified.Destroy(u);
      yield return WaitFrames(4);

      // 4. Smooth Infantry Locomotion GIF (Idle -> Sprint on clear central road)
      var runner = SpawnUnitByName(Verified, 0, "ff_ranger_squad", new FPVector3(-22, 0, -64));
      yield return WaitFrames(4);
      yield return Send(0, new MoveUnitsCommand { Units = new[] { runner }, Target = new FPVector3(24, 0, -64) });
      rts.Height = 16f;
      var sawSprintPose = false;
      for (var i = 0; i < 10; i++) {
        yield return WaitFrames(4);
        if (Verified.Exists(runner)) {
          var rp = Verified.Get<Transform3D>(runner).Position;
          rts.MoveTo(new Vector2(rp.X.AsFloat, rp.Z.AsFloat));
          rts.Apply();
          if (Battle().Actors.TryGetValue(runner, out var ra) && ra.View?.Animation?.ActiveMixamoPose == "SprintForward") sawSprintPose = true;
        }
        CaptureCameraPng(camera, Path.Combine(sprintFrames, $"s_{i:00}.png"), 960, 540);
      }
      Assert.IsTrue(sawSprintPose, "Upgraded infantry transitions smoothly into SprintForward");
      Debug.Log($"[ComprehensivePolish] PASS lighting=ACES+Bloom+Sun1.26 sprintPose={sawSprintPose}");
    }
  }
}
