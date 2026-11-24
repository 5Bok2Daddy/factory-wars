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
  /// Pre-Task 42 visual verification captures ONLY (no simulation changes):
  /// 1. Infantry movement/locomotion & combat formation (non-T-pose active stances)
  /// 2. Ground combat stance/aiming/firing + tanks/APCs/PMC vehicles
  /// 3. Air units (helicopters, drones, aircraft) altitude, ground shadows & perspective (wide + close)
  /// 4. Main-base conveyor auto-supply flow (iron extractor & lumber mill -> moving belt -> HQ)
  /// 5. Multi-base resource transport truck logistics flow (west yard -> truck loading/enroute -> base dock)
  /// </summary>
  public unsafe partial class LocalMatchTests {
    static string PreT42Dir() =>
      Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "status_images", "PreT42"));

    static (EntityRef entity, FPVector3 pos) NearestPlaceableNode(Frame frame, FPVector3 origin, ResourceKind kind, BuildingSpec spec) {
      var best = EntityRef.None;
      var bestPos = FPVector3.Zero;
      var bestDist = FP.UseableMax;
      foreach (var pair in frame.GetComponentIterator<ResourceNode>()) {
        if (pair.Component.Kind != kind || pair.Component.Remaining <= FP._0) continue;
        var pos = frame.Get<Transform3D>(pair.Entity).Position;
        if (ProductionSystem.TryGetBuildPlacement(frame, spec, pos, 0, out var aligned) == false) continue;
        var d = FactoryWarsUtil.FlatDistanceSqr(origin, aligned);
        if (d < bestDist) {
          bestDist = d;
          best = pair.Entity;
          bestPos = aligned;
        }
      }
      return (best, bestPos);
    }

    static Vector3 UnityPos(Frame frame, EntityRef entity) =>
      frame.Exists(entity) ? frame.Get<Transform3D>(entity).Position.ToUnityVector3() : Vector3.zero;

    static void FinishBuilding(Frame frame, EntityRef entity) {
      if (entity != EntityRef.None && frame.Unsafe.TryGetPointer<Building>(entity, out var b)) b->ConstructionRemaining = FP._0;
    }

    [UnityTest, Explicit, Timeout(1800000)]
    public IEnumerator PreT42_CaptureVerificationShots() {
      var dir = PreT42Dir();
      Directory.CreateDirectory(dir);
      var log = new List<string> { "Pre-Task 42 Verification Captures (Map4P v4)" };

      yield return StartMatch(Map4PScene, 2, truce: 0, seed: 4200, startingTier: 3,
        stock: new ResourceCost { Iron = 8000, Wood = 8000, Steel = 4000, Power = 4000 });
      yield return WaitForHud();
      yield return WaitForBattle();
      var doctrine = GameObject.Find("DoctrinePanel");
      if (doctrine != null) doctrine.GetComponentsInChildren<UnityEngine.UI.Button>(true).First().onClick.Invoke();
      GrantAgreement(0);

      var camera = Camera.main;
      var rts = Controller().Camera;
      camera.aspect = 16f / 9f;
      Time.timeScale = 1f;

      // ---------------------------------------------------------------------
      // 1. Infantry movement / locomotion & formation (non-T-pose active march)
      // ---------------------------------------------------------------------
      var marchOrigin = new FPVector3(-96, 0, -64);
      var marchUnits = new List<EntityRef> {
        SpawnUnitByName(Verified, 0, "ff_ranger_squad", marchOrigin + new FPVector3(-6, 0, -3)),
        SpawnUnitByName(Verified, 0, "ff_ranger_squad", marchOrigin + new FPVector3(-2, 0, -3)),
        SpawnUnitByName(Verified, 0, "ff_at_rocket",    marchOrigin + new FPVector3( 2, 0, -3)),
        SpawnUnitByName(Verified, 0, "ff_breacher",     marchOrigin + new FPVector3( 6, 0, -3)),
        SpawnUnitByName(Verified, 0, "pmc_operator_squad", marchOrigin + new FPVector3(-4, 0, 2)),
        SpawnUnitByName(Verified, 0, "pmc_drone_operator", marchOrigin + new FPVector3( 0, 0, 2)),
        SpawnUnitByName(Verified, 0, "pmc_marksman",       marchOrigin + new FPVector3( 4, 0, 2)),
      };
      var cfMarch = new List<EntityRef> {
        SpawnUnitByName(Verified, 1, "cf_militia",   marchOrigin + new FPVector3(-4, 0, 8)),
        SpawnUnitByName(Verified, 1, "cf_at_rocket", marchOrigin + new FPVector3( 0, 0, 8)),
        SpawnUnitByName(Verified, 1, "cf_engineer",  marchOrigin + new FPVector3( 4, 0, 8)),
      };
      // Issue march orders along the wide boulevard so all infantry enter active walk/run animation.
      yield return Send(0, new MoveUnitsCommand { Units = marchUnits.ToArray(), Target = marchOrigin + new FPVector3(36, 0, 0) });
      yield return Send(1, new MoveUnitsCommand { Units = cfMarch.ToArray(), Target = marchOrigin + new FPVector3(36, 0, 8) });
      yield return WaitFrames(Fps * 2); // 2 s into active stride
      var leadPos = UnityPos(Verified, marchUnits[1]);
      rts.Height = 19f;
      rts.MoveTo(new Vector2(leadPos.x + 1f, leadPos.z + 2f));
      rts.Apply();
      yield return WaitFrames(6);
      CaptureCameraPng(camera, Path.Combine(dir, "01_infantry_movement_formation.png"), 1920, 1080);
      log.Add($"01_infantry_movement_formation.png | focus=({leadPos.x:0.0},{leadPos.z:0.0}) height=19m");

      // ---------------------------------------------------------------------
      // 2. Combat stance / aiming / firing + tanks, APCs & PMC vehicles
      // ---------------------------------------------------------------------
      var combatCenter = new FPVector3(4, 0, -64);
      var ffCombat = new List<EntityRef> {
        SpawnUnitByName(Verified, 0, "ff_eagle_tank",      combatCenter + new FPVector3(-22, 0, -4)),
        SpawnUnitByName(Verified, 0, "ff_field_support",   combatCenter + new FPVector3(-22, 0,  4)),
        SpawnUnitByName(Verified, 0, "pmc_tank",           combatCenter + new FPVector3(-14, 0, -9)),
        SpawnUnitByName(Verified, 0, "pmc_armored_suv",    combatCenter + new FPVector3(-14, 0,  9)),
        SpawnUnitByName(Verified, 0, "ff_ranger_squad",    combatCenter + new FPVector3(-10, 0, -4)),
        SpawnUnitByName(Verified, 0, "ff_ranger_squad",    combatCenter + new FPVector3(-10, 0,  0)),
        SpawnUnitByName(Verified, 0, "ff_at_rocket",       combatCenter + new FPVector3(-10, 0,  4)),
        SpawnUnitByName(Verified, 0, "pmc_operator_squad", combatCenter + new FPVector3( -7, 0, -7)),
        SpawnUnitByName(Verified, 0, "pmc_drone_operator", combatCenter + new FPVector3( -7, 0,  7)),
      };
      var cfCombat = new List<EntityRef> {
        SpawnUnitByName(Verified, 1, "cf_fortress_tank",    combatCenter + new FPVector3(18, 0, -4)),
        SpawnUnitByName(Verified, 1, "cf_dragonscale_tank", combatCenter + new FPVector3(18, 0,  4)),
        SpawnUnitByName(Verified, 1, "cf_militia",          combatCenter + new FPVector3(11, 0, -4)),
        SpawnUnitByName(Verified, 1, "cf_militia",          combatCenter + new FPVector3(11, 0,  0)),
        SpawnUnitByName(Verified, 1, "cf_at_rocket",        combatCenter + new FPVector3(11, 0,  4)),
      };
      yield return Send(0, new AttackTargetCommand { Units = ffCombat.ToArray(), Target = cfCombat[0] });
      yield return Send(1, new AttackTargetCommand { Units = cfCombat.ToArray(), Target = ffCombat[0] });
      yield return WaitFrames(Fps); // 1 s: turrets swivel, infantry aim/fire in clean formation without clumping
      rts.Height = 36f;
      rts.MoveTo(new Vector2(combatCenter.X.AsFloat, combatCenter.Z.AsFloat));
      rts.Apply();
      yield return WaitFrames(4);
      CaptureCameraPng(camera, Path.Combine(dir, "02_combat_stance_and_vehicles.png"), 1920, 1080);
      log.Add($"02_combat_stance_and_vehicles.png | focus=({combatCenter.X.AsFloat:0.0},{combatCenter.Z.AsFloat:0.0}) height=36m");

      // ---------------------------------------------------------------------
      // 3 & 4. Air units (helicopters, drones, aircraft): altitude, shadows & perspective
      // ---------------------------------------------------------------------
      var airCenter = new FPVector3(0, 0, 56);
      // Ground reference tank + squad right on the road below so scale & shadows read clearly.
      SpawnUnitByName(Verified, 0, "ff_eagle_tank", airCenter + new FPVector3(-4, 0, -4));
      SpawnUnitByName(Verified, 0, "ff_ranger_squad", airCenter + new FPVector3(4, 0, -4));

      var airUnits = new List<EntityRef> {
        SpawnUnitByName(Verified, 0, "ff_hawk_gunship",      airCenter + new FPVector3(-12, 0, -6)),
        SpawnUnitByName(Verified, 0, "pmc_attack_heli",      airCenter + new FPVector3(  2, 0, -2)),
        SpawnUnitByName(Verified, 0, "ff_transport_heli",    airCenter + new FPVector3(-18, 0,  8)),
        SpawnUnitByName(Verified, 1, "cf_gale_gunship",      airCenter + new FPVector3( 14, 0,  4)),
        SpawnUnitByName(Verified, 0, "ff_skyfortress_bomber",airCenter + new FPVector3(  8, 0, 14)),
        SpawnUnitByName(Verified, 0, "pmc_strike_drone",     airCenter + new FPVector3( -4, 0,  6)),
        SpawnUnitByName(Verified, 0, "ff_nest_drone",        airCenter + new FPVector3(  6, 0, -8)),
      };
      // Move air units slightly forward so they bank/cruise naturally with rotors spinning.
      yield return Send(0, new MoveUnitsCommand { Units = airUnits.Take(3).Concat(airUnits.Skip(4)).ToArray(), Target = airCenter + new FPVector3(18, 0, -4) });
      yield return WaitFrames(Fps * 2);

      // 3. Wide aerial overview showing altitude separation, shadows on roads/canal/buildings, and scale.
      var airFocus = UnityPos(Verified, airUnits[1]);
      rts.Height = 64f;
      rts.MoveTo(new Vector2(airFocus.x, airFocus.z + 4f));
      rts.Apply();
      yield return WaitFrames(6);
      CaptureCameraPng(camera, Path.Combine(dir, "03_air_units_altitude_shadows.png"), 1920, 1080);
      log.Add($"03_air_units_altitude_shadows.png | focus=({airFocus.x:0.0},{airFocus.z:0.0}) height=64m");

      // 4. Closer gameplay-height perspective framing helicopters + bomber + ground tank + ground shadows.
      rts.Height = 36f;
      rts.MoveTo(new Vector2(airFocus.x - 2f, airFocus.z));
      rts.Apply();
      yield return WaitFrames(6);
      CaptureCameraPng(camera, Path.Combine(dir, "04_air_units_closeup_perspective.png"), 1920, 1080);
      log.Add($"04_air_units_closeup_perspective.png | focus=({airFocus.x - 2f:0.0},{airFocus.z:0.0}) height=36m");

      // ---------------------------------------------------------------------
      // 6. Multi-base resource transport truck logistics flow (yard -> truck -> dock) on Map4P v4
      // ---------------------------------------------------------------------
      var west = WestSite();
      GiveSite(0, west.DropOff);
      SetBuffer(west.Site, new ResourceCost { Iron = 500, Wood = 250 });
      Time.timeScale = 4f;
      yield return WaitFrames(Fps * 2); // Let logistics system spawn the transport truck for west site
      yield return WaitFor(() => {
        var t = Yard(west.Site).Truck;
        return Verified.Exists(t) && (Truck(t).State == LogisticsState.ToDock || Truck(t).State == LogisticsState.Loading);
      }, 180f, "transport truck loading or enroute to base dock");
      var truck = Yard(west.Site).Truck;
      yield return WaitFor(() => Truck(truck).State == LogisticsState.ToDock, 90f, "transport truck enroute to base dock");
      Time.timeScale = 4f;
      yield return WaitFrames(Fps * 12); // Let the truck drive clear of the depot buildings onto the open avenue
      Time.timeScale = 1f;
      yield return WaitFrames(8);
      var truckPos = UnityPos(Verified, truck);
      rts.Height = 36f;
      rts.MoveTo(new Vector2(truckPos.x, truckPos.z));
      rts.Apply();
      yield return WaitFrames(6);
      CaptureCameraPng(camera, Path.Combine(dir, "06_multibase_truck_enroute.png"), 1920, 1080);
      log.Add($"06_multibase_truck_enroute.png | truck=({truckPos.x:0.0},{truckPos.z:0.0}) state={Truck(truck).State} height=36m");

      // ---------------------------------------------------------------------
      // 5. Main-base conveyor auto-supply flow (extractor -> moving belt -> HQ)
      // ---------------------------------------------------------------------
      var hqEntity = Headquarters(0);
      var hqPos = Verified.Get<Transform3D>(hqEntity).Position;
      var ironSpec = Verified.FindAsset(BuildingOf(0, "iron_extractor"));
      var (_, ironPos) = NearestPlaceableNode(Verified, hqPos, ResourceKind.Iron, ironSpec);
      if (ironPos == FPVector3.Zero) {
        // Log Map4P node diagnostics and capture the verified main-base conveyor flow on the default match scene.
        log.Add("NOTE: No placeable iron node returned by TryGetBuildPlacement near Seat 0 on Map4P; capturing main-base conveyor on default match scene.");
        TearDown();
        yield return null;
        yield return StartMatch();
        yield return WaitForHud();
        hqEntity = Headquarters(0);
        hqPos = Verified.Get<Transform3D>(hqEntity).Position;
        ironPos = V(-30, 14);
        camera = Camera.main;
        rts = Controller().Camera;
        camera.aspect = 16f / 9f;
      }

      var engIron = Engineers(0)[0];
      yield return Build(0, "iron_extractor", ironPos, engIron);
      var ironSite = BuildingAt(ironPos);
      Assert.AreNotEqual(EntityRef.None, ironSite, "iron extractor site created: " + string.Join(", ", _rejected.Select(r => r.Reason)));

      Time.timeScale = 6f;
      yield return WaitFor(() => Finished(ironSite) && CitySystem.Occupants(Verified, ironSite) >= 1, 90f, "iron extractor built & staffed");
      Time.timeScale = 1f;
      var battleView = Battle();
      yield return WaitFor(() => battleView.Actors.TryGetValue(ironSite, out var actor)
        && actor.ExtractorDelivery?.FirstCargo != null && actor.ExtractorDelivery.FirstCargo.gameObject.activeInHierarchy,
        15f, "main-base conveyor belt and moving cargo active");
      yield return WaitFrames(Fps * 2);

      var beltMid = (hqPos.ToUnityVector3() + ironPos.ToUnityVector3()) * 0.5f;
      rts.Height = 32f;
      rts.MoveTo(new Vector2(beltMid.x, beltMid.z));
      rts.Apply();
      yield return WaitFrames(8);
      CaptureCameraPng(camera, Path.Combine(dir, "05_mainbase_conveyor_flow.png"), 1920, 1080);
      log.Add($"05_mainbase_conveyor_flow.png | hq=({hqPos.X.AsFloat:0},{hqPos.Z.AsFloat:0}) iron=({ironPos.X.AsFloat:0},{ironPos.Z.AsFloat:0}) height=32m");

      File.WriteAllLines(Path.Combine(dir, "capture_log.txt"), log);
      Debug.Log(string.Join("\n", log));
    }
  }
}
