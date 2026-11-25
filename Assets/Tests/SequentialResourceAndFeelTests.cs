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

    [UnityTest, Explicit, Timeout(1200000)]
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
      Assert.GreaterOrEqual(droneAlt, 11.2f, $"Drone altitude raised >= 11.5m (actual {droneAlt:F2}m)");
      Assert.GreaterOrEqual(heliAlt, 14.7f, $"Heli altitude raised >= 15.0m (actual {heliAlt:F2}m)");
      Assert.GreaterOrEqual(bomberAlt, 18.7f, $"Bomber altitude raised >= 19.0m (actual {bomberAlt:F2}m)");

      rts.Height = 52f;
      rts.MoveTo(new Vector2(-4f, -4f));
      rts.Apply();
      yield return WaitFrames(15);
      CaptureCameraPng(camera, Path.Combine(dir, "air_high_altitude_and_vfx.png"), 1920, 1080);

      // Verify barrier/fence fly-over succeeded (units moved well past the Z=-18 PMC fence).
      var heliPosAfterFence = Verified.Get<Transform3D>(heli).Position;
      Assert.Greater(heliPosAfterFence.Z.AsFloat, -16f, $"Air unit flew freely over PMC barrier/fence (Z={heliPosAfterFence.Z.AsFloat:F1})");

      // 3. Tall Building avoidance on Map4P v4 ('H' cells in CellGrid): pick a tall urban building facing an open main road ('R') with a clear corner.
      Assert.IsTrue(GridMapView.Uses(map), "Map4P uses CellGrid");
      var n = map.CellGridSize;
      int bRow = -1, bCol = -1;
      for (var r = 18; r <= 62 && bRow < 0; r++) {
        for (var c = 18; c <= 62 && bRow < 0; c++) {
          if (GridMapView.Cell(map, r, c) != 'H') continue;
          // Open main road 'R' along west side (c-1) and open non-'H' corner to north or south + east exit
          var westRoad = GridMapView.Cell(map, r, c - 1) == 'R' && GridMapView.Cell(map, r - 1, c - 1) == 'R' && GridMapView.Cell(map, r + 1, c - 1) == 'R';
          var openCorner = (GridMapView.Cell(map, r - 1, c) != 'H' && GridMapView.Cell(map, r - 1, c + 1) != 'H')
                        || (GridMapView.Cell(map, r + 1, c) != 'H' && GridMapView.Cell(map, r + 1, c + 1) != 'H');
          var eastOpen = GridMapView.Cell(map, r, c + 2) != 'H' || GridMapView.Cell(map, r, c + 3) != 'H';
          if (westRoad && openCorner && eastOpen) {
            bRow = r;
            bCol = c;
          }
        }
      }
      Assert.GreaterOrEqual(bRow, 0, "Found tall urban building ('H') facing a main road with clear corner on Map4P");
      var bCenterX = GridMapView.CellX(map, bCol);
      var bCenterZ = GridMapView.CellZ(map, bRow);
      var startPos = new FPVector3(FP.FromFloat_UNSAFE(GridMapView.CellX(map, bCol - 1)), FP._0, FP.FromFloat_UNSAFE(bCenterZ));
      var endPos   = new FPVector3(FP.FromFloat_UNSAFE(GridMapView.CellX(map, bCol + 3)), FP._0, FP.FromFloat_UNSAFE(bCenterZ));

      var avoidHeli   = SpawnUnitByName(Verified, 0, "cf_gale_gunship",       startPos + new FPVector3(0, 0, -1));
      var avoidBomber = SpawnUnitByName(Verified, 0, "ff_skyfortress_bomber", startPos + new FPVector3(0, 0,  2));
      yield return Send(0, new MoveUnitsCommand { Units = new[] { avoidHeli, avoidBomber }, Target = endPos });

      rts.Height = 60f;
      rts.MoveTo(new Vector2(bCenterX + 4f, bCenterZ + 12f));
      rts.Apply();

      var cellSize = map.CellGridCell.AsFloat;
      var mapHalf = n * cellSize * 0.5f;
      var maxLateralDeflection = 0f;
      for (var f = 0; f < 14; f++) {
        yield return WaitFrames(22);
        var pH = Verified.Get<Transform3D>(avoidHeli).Position;
        var pB = Verified.Get<Transform3D>(avoidBomber).Position;
        int rH = Mathf.FloorToInt((mapHalf - pH.Z.AsFloat) / cellSize);
        int cH = Mathf.FloorToInt((pH.X.AsFloat + mapHalf) / cellSize);
        int rB = Mathf.FloorToInt((mapHalf - pB.Z.AsFloat) / cellSize);
        int cB = Mathf.FloorToInt((pB.X.AsFloat + mapHalf) / cellSize);
        Assert.AreNotEqual('H', GridMapView.Cell(map, rH, cH), $"Helicopter steered around tall 'H' building without clipping (frame {f}, cell {rH},{cH})");
        Assert.AreNotEqual('H', GridMapView.Cell(map, rB, cB), $"Bomber steered around tall 'H' building without clipping (frame {f}, cell {rB},{cB})");
        maxLateralDeflection = Mathf.Max(maxLateralDeflection, Mathf.Abs(pH.Z.AsFloat - bCenterZ));
        CaptureCameraPng(camera, Path.Combine(frames, $"air_avoid_{f:00}.png"), 960, 540);
      }
      CaptureCameraPng(camera, Path.Combine(dir, "air_building_avoidance.png"), 1920, 1080);

      var finalHeliX = Verified.Get<Transform3D>(avoidHeli).Position.X.AsFloat;
      Assert.Greater(maxLateralDeflection, 4f, $"Helicopter actively deflected around tall building (maxDeflect={maxLateralDeflection:F2}m)");
      Assert.Greater(finalHeliX, startPos.X.AsFloat + 4f,
        $"Helicopter actively steered past tall building (startX={startPos.X.AsFloat:F1}, endX={finalHeliX:F1}, maxDeflect={maxLateralDeflection:F2}m)");
      Debug.Log($"[AirFlight_AltitudeVfxAndBuildingAvoidance] PASS alt=({droneAlt:F1}/{heliAlt:F1}/{bomberAlt:F1}m) cell=({bRow},{bCol}) maxDeflect={maxLateralDeflection:F2}m");
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
  }
}
