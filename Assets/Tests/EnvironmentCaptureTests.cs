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

  public unsafe partial class LocalMatchTests {
    [UnityTest, Explicit, Category("CaptureCatalog")]
    public IEnumerator Environment_CaptureMatchingViews() {
      Assert.IsNotEmpty(System.Environment.GetEnvironmentVariable("FW_CAPTURE_DIR"));
      var profile = VisualProfile.Current;
      var details = profile.EnvironmentDetails;
      profile.EnvironmentDetails = System.Environment.GetEnvironmentVariable("FW_ENV_COMPARISON") != "before";
      Restores.Add(() => profile.EnvironmentDetails = details);
      var scenario = EnvironmentCompound();
      yield return StartMatch(Map4PScene, startingTier: 3, seed: 5217, stock: Stock(8000, 5000, 5000, 400), buildings: scenario,
        units: new[] { (0, "ff_eagle_tank", -135, -119), (0, "ff_ranger_squad", -132, -120) });
      yield return WaitForBattle();
      var camera = Camera.main;
      var controls = camera.GetComponent<RtsCamera>();
      if (controls != null) controls.enabled = false;
      Assert.IsTrue(ProductionSystem.CanPlace(Verified, Verified.FindAsset(BuildingOf(0, "lumber_mill")), V(-144, -144), 0),
        "The rear woodland provides sufficient construction clearance");
      var mill = V(-144, -144);
      yield return Build(0, "lumber_mill", mill);
      var extractor = BuildingAt(mill);
      Assert.AreNotEqual(EntityRef.None, extractor, $"Resource facility is built by the engineer: {string.Join(", ", _rejected.Select(e => e.Reason))}");
      yield return WaitFor(() => Finished(extractor), 45f, "The lumber mill finishes");
      yield return WaitFor(() => Battle().Actors.ContainsKey(extractor), 8f, "The lumber mill view is active");
      yield return WaitFor(() => CitySystem.Occupants(Verified, extractor) > 0, 16f, "The extractor is staffed");

      SetShot("01_map_overview", Vector3.zero, new Vector3(0f, 335f, 0.02f), true);
      SetShot("02_headquarters", At(Headquarters(0)).ToUnityVector3() + Vector3.up, new Vector3(13f, 17f, -13f));
      var factory = Site("ff_vehicle_factory");
      var processor = Site("ff_processor");
      SetShot("03_industrial_compound", Vector3.Lerp(factory, processor, 0.5f), new Vector3(22f, 31f, -21f));
      SetShot("04_factory_foundation", factory + Vector3.up, new Vector3(9f, 12f, -11f));
      SetShot("05_road_intersection", new Vector3(-82f, 0f, -103f), new Vector3(-22f, 31f, -24f));
      SetShot("06_forest_edge", new Vector3(-62f, 0f, -147f), new Vector3(14f, 19f, 12f));
      SetShot("07_rocky_ridge", new Vector3(-150f, 6f, -61f), new Vector3(24f, 34f, -21f));
      SetShot("08_resource_extraction", new Vector3(-144f, 0f, -144f), new Vector3(14f, 16f, 13f));
      SetShot("09_mlrs_installation", Site("ff_mlrs_battery"), new Vector3(11f, 16f, 12f));

      var tank = FindOwnUnit(0, "ff_eagle_tank");
      var infantry = FindOwnUnit(0, "ff_ranger_squad");
      var start = At(tank);
      yield return Send(0, new MoveUnitsCommand { Units = new[] { tank, infantry }, Target = V(-125, -119) });
      yield return WaitFor(() => (At(tank) - start).SqrMagnitude > 6, 12f, "Combat units move through the base");
      SetShot("10_units_in_motion", new Vector3(-130f, 0f, -119f), new Vector3(12f, 17f, -12f));
      var world = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
        .Where(item => item.name is "Roads" or "NaturalPerimeter" or "ConcreteFoundation"
          || (item.name == "Ground" && item.transform.parent != null && item.transform.parent.name == "World")).ToArray();
      Assert.IsNotEmpty(world);
      Assert.IsTrue(world.All(item => item.sharedMaterial != null && item.sharedMaterial.shader.isSupported),
        "Ground, roads, perimeter and foundations have supported materials");
      var checksum = Verified.CalculateChecksum();
      var previous = profile.EnvironmentDetails;
      profile.EnvironmentDetails = !previous;
      Assert.AreEqual(checksum, Verified.CalculateChecksum(), "Presentation profile has no Quantum state");
      profile.EnvironmentDetails = previous;
      Time.timeScale = 1f;
      var timing = new float[60];
      for (var i = 0; i < timing.Length; i++) {
        yield return null;
        timing[i] = Time.unscaledDeltaTime * 1000f;
      }
      System.Array.Sort(timing);
      Debug.Log($"[FW-ENV] details={profile.EnvironmentDetails} quality={GraphicsQuality.Current} scene=Map4P samples={timing.Length}"
        + $" editorMedianMs={timing[timing.Length / 2]:F2} editorP95Ms={timing[56]:F2}");

      void SetShot(string name, Vector3 focus, Vector3 offset, bool overview = false) {
        camera.orthographic = overview;
        camera.orthographicSize = 194f;
        camera.fieldOfView = 44f;
        camera.farClipPlane = 650f;
        camera.transform.position = focus + offset;
        camera.transform.LookAt(focus);
        CaptureCameraPng(camera, Path.Combine(StatusImagesDir(), name + ".png"), overview ? 1440 : 1280, overview ? 1440 : 720);
      }

      Vector3 Site(string id) {
        var item = scenario.Single(candidate => candidate.Building == id);
        return new Vector3(item.X, 0f, item.Z);
      }
    }

    static (int Owner, string Building, int X, int Z)[] EnvironmentCompound() {
      var map = MapNamed("map_Map4P");
      var definitions = new[] {
        ("ff_vehicle_factory", -109, -139), ("ff_processor", -113, -127), ("ff_supply_depot", -100, -132),
        ("ff_mlrs_battery", -122, -104), ("ff_power_plant", -124, -113), ("ff_barracks", -138, -105),
      };
      var blockers = map.Roads.Concat(map.ResourceNodes.Select(node => node.Shape)).Concat(map.Bridges)
        .Concat(map.Terrain.Select(feature => feature.Shape)).Where(shape => shape.Center.X < FP._0 && shape.Center.Z < FP._0).ToArray();
      var parked = new List<(int X, int Z, float Clearance)> { (-130, -130, 4f), (-144, -144, 5.5f), (-132, -120, 3f) };
      var scenario = new List<(int, string, int, int)>();
      foreach (var (id, x, z) in definitions) {
        var radius = Resources.Load<BuildingSpec>("FactoryWars/" + id).FootprintRadius;
        var options = from px in Enumerable.Range(-151, 56)
                      from pz in Enumerable.Range(-151, 56)
                      orderby (px - x) * (px - x) + (pz - z) * (pz - z)
                      select (px, pz);
        var (left, back) = options.First(p => parked.All(other => Mathf.Max(Mathf.Abs(p.px - other.X), Mathf.Abs(p.pz - other.Z))
          >= radius.AsFloat + other.Clearance + 2.3f) && blockers.All(shape => Clear(shape, V(p.px, p.pz), radius + FP._2)));
        parked.Add((left, back, radius.AsFloat));
        scenario.Add((0, id, left, back));
      }
      return scenario.ToArray();

      static bool Clear(MapShape shape, FPVector3 point, FP margin) {
        var offset = point - shape.Center;
        var reach = shape.Reach + margin;
        return FPMath.Abs(offset.X) > reach || FPMath.Abs(offset.Z) > reach || shape.Distance(point) > margin;
      }
    }
  }
}
