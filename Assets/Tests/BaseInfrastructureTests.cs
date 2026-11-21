namespace FactoryWars.Tests {
  using System.Collections;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;
  using Object = UnityEngine.Object;

  public unsafe partial class LocalMatchTests {
    [UnityTest]
    public IEnumerator Infrastructure_MoveAroundLiveBuildingAndReleaseFootprint() {
      var from = V(-20, 23);
      var site = V(-20, 34);
      var goal = V(-20, 45);
      yield return StartMatch(units: new[] { (0, "ff_eagle_tank", -20, 23) }, buildings: new[] { (0, "ff_supply_depot", -20, 34) });
      yield return WaitForBattle();
      var tank = UnitsNamed(0, "ff_eagle_tank").OrderBy(unit => FactoryWarsUtil.FlatDistanceSqr(At(unit), from)).First();
      var building = BuildingAt(site);
      Assert.AreNotEqual(EntityRef.None, tank);
      Assert.AreNotEqual(EntityRef.None, building);
      var spec = Verified.FindAsset(Verified.Get<Unit>(tank).Spec);
      var radius = MovementSystem.UnitRadius(spec);
      var legal = GroundOccupancy.TryGoal(Verified, site, radius, out var alternate);
      Assert.IsTrue(legal);
      Assert.IsTrue(GroundOccupancy.CanStand(Verified, alternate, radius));
      Assert.IsFalse(GroundOccupancy.CanStand(Verified, site, radius));
      Assert.IsTrue(GroundOccupancy.TryDetour(Verified, Verified.Get<Transform3D>(tank).Position, goal, radius, out _));

      var entered = false;
      var boundary = Verified.FindAsset(Verified.Get<Building>(building).Spec).FootprintRadius + radius;
      QuantumCallback.Subscribe<CallbackSimulateFinished>(_listener, e => {
        if (e.Frame.TryGet<Transform3D>(tank, out var tf) && GroundOccupancy.Overlaps(tf.Position, site, boundary)) entered = true;
      });
      yield return Send(0, new MoveUnitsCommand { Units = new[] { tank }, Target = goal });
      yield return WaitFor(() => FactoryWarsUtil.FlatDistanceSqr(Verified.Get<Transform3D>(tank).Position, goal) < 3, 25f, "Tank completes a detour");
      Assert.IsFalse(entered, "The unit radius stays outside the occupied square");
      CaptureInfrastructureScene(site.ToUnityVector3(), "Navigation", "tank_factory_detour");

      Assert.IsFalse(GroundOccupancy.TryGoal(Verified, V(3000, 3000), radius, out _));
      Verified.Destroy(building);
      yield return WaitFor(() => GroundOccupancy.CanStand(Verified, site, radius), 8f, "The demolished footprint is available");
    }

    [UnityTest]
    public IEnumerator Infrastructure_FoundationFollowsBuildingLifecycle() {
      yield return StartMatch();
      yield return WaitForBattle();
      var site = V(-20, 10);
      yield return Build(0, "_power_plant", site);
      var building = BuildingAt(site);
      Assert.AreNotEqual(EntityRef.None, building);
      yield return WaitFor(() => Battle().Actors.ContainsKey(building), 10f, "Construction view is present");
      var view = ActorOf(building).View;
      var foundation = view.GetComponentInChildren<FoundationView>(true);
      Assert.IsNotNull(foundation, "The foundation is present during construction");
      Assert.IsNotEmpty(foundation.GetComponent<MeshFilter>().sharedMesh.vertices);
      Assert.IsEmpty(foundation.GetComponentsInChildren<Collider>());
      CaptureInfrastructureScene(site.ToUnityVector3(), "BaseOverview", "foundation_under_construction");
      yield return WaitFor(() => Finished(building), 60f, "Construction completes");
      Assert.AreSame(foundation, view.GetComponentInChildren<FoundationView>(true));
      Verified.Destroy(building);
      yield return WaitFor(() => foundation == null, 8f, "The demolition removes the foundation");
      CaptureInfrastructureScene(site.ToUnityVector3(), "BaseOverview", "foundation_after_demolition");
    }

    static void CaptureInfrastructureScene(Vector3 center, string group, string shot) {
      if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("FW_CAPTURE_DIR"))) return;
      var camera = Camera.main;
      camera.transform.position = center + new Vector3(14f, 23f, -19f);
      camera.transform.LookAt(center);
      CaptureCameraPng(camera, Path.Combine(StatusImagesDir(), group, shot + ".png"), 1280, 720);
    }

#if UNITY_EDITOR
    [UnityTest, Explicit, Category("CaptureCatalog")]
    public IEnumerator CaptureInfrastructureCatalog() {
      Assert.IsNotNull(System.Environment.GetEnvironmentVariable("FW_CAPTURE_DIR"), "Choose a staging output directory");
      yield return StartMatch(Map4PScene);
      yield return WaitForBattle();
      var root = new GameObject("InfrastructureStudio");
      var position = new Vector3(0f, -100f, 0f);
      root.transform.position = position;
      var camera = new GameObject("InfrastructureCamera").AddComponent<Camera>();
      camera.transform.SetParent(root.transform, false);
      camera.clearFlags = CameraClearFlags.SolidColor;
      camera.backgroundColor = new Color(0.17f, 0.20f, 0.24f);
      camera.fieldOfView = 35f;
      camera.farClipPlane = 145f;
      var lamp = new GameObject("InfrastructureLamp").AddComponent<Light>();
      lamp.transform.SetParent(root.transform, false);
      lamp.type = LightType.Directional;
      lamp.intensity = 1.25f;
      lamp.transform.rotation = Quaternion.Euler(48f, -38f, 0f);

      var catalog = Resources.Load<ViewCatalog>(ViewCatalog.ResourcePath);
      Assert.IsNotNull(catalog);
      var rows = new List<string> { "id\tcategory\tspec\tmodel\timage\tstatus" };
      var specs = Resources.LoadAll<UnitSpec>("FactoryWars").Cast<Quantum.AssetObject>()
        .Concat(Resources.LoadAll<BuildingSpec>("FactoryWars"))
        .OrderBy(spec => spec.name, System.StringComparer.Ordinal);
      foreach (var spec in specs) {
        var group = spec is UnitSpec ? "Units" : "Buildings";
        var view = ArtView.Create(catalog, spec.name, root.transform, spec.name.StartsWith("cf_") ? 1 : 0);
        var prefab = view != null ? view.Slot?.Prefab ?? view.Entry.Prefab : null;
        if (view != null) {
          view.SetSelected(false);
          if (view.Bar != null) view.Bar.SetActive(false);
          view.Turn.localPosition = Vector3.zero;
          view.SetYaw(25f);
          if (spec is UnitSpec unit && !unit.IsAir && !unit.IsNaval && !unit.IsVehicle && unit.Size <= UnitSize.Small) {
            BattleView.AttachInfantryWeapon(view.Model, spec.name);
          }
          yield return null;
        }
        CaptureItem(rows, camera, view != null ? view.Model : null, prefab, spec, spec.name, group);
        if (view != null) {
          view.gameObject.SetActive(false);
          Object.Destroy(view.gameObject);
        }
      }
      for (var i = 0; i < 4; ++i) {
        var id = $"const_worker_{i}";
        var prefab = VisualCatalog.TryFilled(VisualCatalog.FxId(id), out var slot) ? slot.Prefab : null;
        var visual = prefab != null ? Object.Instantiate(prefab, root.transform, false) : null;
        if (visual != null) yield return null;
        CaptureItem(rows, camera, visual, prefab, null, id, "Workers");
        if (visual != null) {
          visual.SetActive(false);
          Object.Destroy(visual);
        }
      }
      File.WriteAllLines(Path.Combine(StatusImagesDir(), "catalog.tsv"), rows);
      Object.Destroy(root);
      Assert.Greater(rows.Count, 80);
    }

    static void CaptureItem(List<string> rows, Camera camera, GameObject visual, GameObject prefab, Object spec, string id, string group) {
      var source = spec != null ? UnityEditor.AssetDatabase.GetAssetPath(spec) : string.Empty;
      var model = prefab != null ? UnityEditor.AssetDatabase.GetAssetPath(prefab) : string.Empty;
      var renderers = visual != null ? visual.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray() : System.Array.Empty<Renderer>();
      if (renderers.Length == 0) {
        rows.Add($"{id}\t{group}\t{source}\t{model}\t\tmissing-renderer");
        return;
      }
      var bounds = renderers[0].bounds;
      foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
      var distance = Mathf.Max(1.3f, bounds.extents.magnitude * 3.1f);
      camera.transform.position = bounds.center + distance * new Vector3(0.49f, 0.49f, -0.72f);
      camera.transform.LookAt(bounds.center);
      var image = $"{group}/{id}.png";
      CaptureCameraPng(camera, Path.Combine(StatusImagesDir(), image), 640, 480);
      rows.Add($"{id}\t{group}\t{source}\t{model}\t{image}\tcaptured");
    }
#endif
  }
}
