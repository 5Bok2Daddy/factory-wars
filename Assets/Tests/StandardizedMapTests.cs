namespace FactoryWars.Tests {
  using System.Collections;
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
    static readonly (string Scene, string Shot)[] StandardizedMaps = {
      ("Map4P", "map_standard"), ("MapDesert", "map_desert"), ("MapArchipelago", "map_archipelago"),
      ("MapStrait", "map_strait"), ("MapUrban", "map_urban"),
    };

    [Test]
    public void Standardized_ExactBoundsPlateausAndResourceTiers() {
      foreach (var (id, _) in StandardizedMaps) {
        var map = MapNamed("map_" + id);
        Assert.IsTrue(map.SealedPerimeter, id);
        Assert.AreEqual(new FPVector2(-160, -160), map.BoundsMin, id);
        Assert.AreEqual(new FPVector2(160, 160), map.BoundsMax, id);
        Assert.IsEmpty(map.Channel, id);
        Assert.AreEqual((FP)10, map.ExtractorSnapRadius, id);
        Assert.AreEqual(4, map.BaseAreas.Length, id);
        Assert.GreaterOrEqual(map.Bridges.Length, 12, id);
        Assert.IsTrue(map.NoBuildZones.Any(zone => zone.HalfWidth == FP._3), $"{id} protected worker lane");
        foreach (var area in map.BaseAreas) {
          Assert.AreEqual((FP)35, area.HalfLength, id);
          Assert.AreEqual((FP)35, area.HalfWidth, id);
          Assert.GreaterOrEqual(map.ResourceNodes.Count(node => node.Tier == 1 && area.Distance(node.Shape.Center) < FP._0), 3, id);
        }
        Assert.IsTrue(map.ResourceNodes.Any(node => node.Tier == 2), id);
        Assert.IsTrue(map.ResourceNodes.Any(node => node.Tier == 3), id);
        if (id == "MapStrait") {
          Assert.IsTrue(map.Roads.All(road => FPMath.Abs(road.Center.X) >= (FP)24), "Roads on both banks approach the bridges from dry ground");
        }
      }
    }

    [UnityTest]
    public IEnumerator Standardized_BakedLandWaterAndBridgeAccess() {
      foreach (var (id, _) in StandardizedMaps) {
        yield return StartMatch($"Assets/QuantumUser/Scenes/{id}.unity", 4, truce: 0);
        var frame = Verified;
        var map = frame.FindAsset<MapSpec>(frame.Map.UserAsset);
        var rules = FactoryWarsUtil.Rules(frame);
        var ground = frame.Map.GetNavMesh(rules.NavMeshName);
        var water = frame.Map.GetNavMesh(rules.WaterNavMeshName);
        Assert.IsNotNull(ground, id);
        Assert.IsNotNull(water, id);
        var mask = StandardizedNavMask(frame);
        for (var t = -152; t <= 152; t += 8) {
          foreach (var p in new[] { V(t, -161), V(t, 161), V(-161, t), V(161, t) }) {
            Assert.IsFalse(ground.Contains(p, mask, true), $"{id} exterior land {p}");
            Assert.IsFalse(water.Contains(p, mask, true), $"{id} exterior water {p}");
          }
        }
        foreach (var start in map.StartPositions) {
          Assert.IsTrue(ground.Contains(start, mask, true), $"{id} playable pad {start}");
          Assert.IsFalse(water.Contains(start, mask, true), $"{id} pad is dry {start}");
          Assert.AreEqual(0f, TerrainKit.SurfaceHeight(map, start.X.AsFloat, start.Z.AsFloat), $"{id} flat pad");
        }
        for (var crossing = 0; crossing < 3; crossing++) {
          var bridge = map.Bridges[crossing * map.BaseAreas.Length];
          var angle = bridge.Yaw * FP.Deg2Rad;
          var forward = new FPVector3(FPMath.Cos(angle), FP._0, -FPMath.Sin(angle));
          var near = bridge.Center - forward * (bridge.HalfLength + FP._3);
          var far = bridge.Center + forward * (bridge.HalfLength + FP._3);
          Assert.IsTrue(FactoryWarsUtil.FindNavPointNear(frame, rules.NavMeshName, near, 5, out var entry), $"{id} bridge {crossing} entry");
          Assert.IsTrue(FactoryWarsUtil.FindNavPointNear(frame, rules.NavMeshName, far, 5, out var exit), $"{id} bridge {crossing} exit");
          var path = frame.Navigation.FindPath((FrameThreadSafe)frame, entry, exit, ground, in mask);
          Assert.AreEqual(Navigation.FindPathResult.Code.Success, path.Result, $"{id} bridge {crossing}");
        }
        if (id == "MapStrait") {
          Assert.AreEqual(Navigation.FindPathResult.Code.Success, WaterPath(V(0, -144), V(0, 144)), "해협의 내해 좌우 연결");
        }
        if (id == "MapArchipelago") {
          Assert.AreEqual(Navigation.FindPathResult.Code.Success, WaterPath(V(0, -130), V(130, 0)), "다도해의 라군과 수로 연결");
        }
        TearDown();
        yield return null;
      }
    }

    static NavMeshRegionMask StandardizedNavMask(Frame frame) => *frame.NavMeshRegionMask;

    [UnityTest]
    public IEnumerator Standardized_AirOrdersAndCameraStayInside() {
      yield return StartMatch(Map4PScene, units: new[] { (0, "ff_hawk_gunship", -130, -130) });
      yield return WaitForHud();
      var camera = Controller().Camera;
      var lens = camera.GetComponent<Camera>();
      camera.Zoom(0.01f);
      foreach (var requested in new[] { new Vector2(1000f, 1000f), new Vector2(-1000f, 1000f),
          new Vector2(-1000f, -1000f), new Vector2(1000f, -1000f) }) {
        camera.MoveTo(requested);
        for (var x = 0; x <= 1; x++) {
          for (var y = 0; y <= 1; y++) {
            var ray = lens.ViewportPointToRay(new Vector3(x, y, 0f));
            Assert.IsTrue(new UnityEngine.Plane(Vector3.up, Vector3.zero).Raycast(ray, out var distance));
            var point = ray.GetPoint(distance);
            Assert.LessOrEqual(Mathf.Max(Mathf.Abs(point.x), Mathf.Abs(point.z)), 160.05f, "camera viewport");
          }
        }
      }
      var air = FindOwnUnit(0, "ff_hawk_gunship");
      Assert.AreNotEqual(EntityRef.None, air);
      yield return Send(0, new MoveUnitsCommand { Units = new[] { air }, Target = V(-138, -1000) });
      Assert.AreEqual(V(-138, -160), Verified.Get<Unit>(air).TargetPosition);
      yield return WaitFor(() => Verified.Get<Unit>(air).Order == UnitOrder.Stop, 20f, "air reaches inside of the perimeter");
      Assert.AreEqual((FP)(-160), At(air).Z);
    }

    [UnityTest, Explicit, Category("CaptureCatalog")]
    public IEnumerator Standardized_CaptureFiveTopDownOverviews() {
      Assert.IsNotEmpty(System.Environment.GetEnvironmentVariable("FW_CAPTURE_DIR"));
      foreach (var (scene, shot) in StandardizedMaps) {
        yield return StartMatch($"Assets/QuantumUser/Scenes/{scene}.unity", 4, truce: 180);
        yield return WaitForBattle();
        var camera = Camera.main;
        var controls = camera.GetComponent<RtsCamera>();
        if (controls != null) controls.enabled = false;
        camera.orthographic = true;
        camera.orthographicSize = 194f;
        camera.farClipPlane = 600f;
        camera.transform.SetPositionAndRotation(new Vector3(0f, 335f, 0f), Quaternion.Euler(90f, 0f, 0f));
        yield return null;
        CaptureCameraPng(camera, Path.Combine(StatusImagesDir(), shot + ".png"), 1440, 1440);
        TearDown();
        yield return null;
      }
    }
  }
}
