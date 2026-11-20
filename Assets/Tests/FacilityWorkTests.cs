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
    static void CaptureFacility(string name, Vector3 target, Vector3 offset) {
      if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
      var camera = Camera.main;
      var oldPosition = camera.transform.position;
      var oldRotation = camera.transform.rotation;
      var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Game-logs"));
      Directory.CreateDirectory(folder);
      camera.transform.position = target + offset;
      camera.transform.LookAt(target);
      CaptureCameraPng(camera, Path.Combine(folder, $"codex-yard-{name}.png"), 1280, 720);
      camera.transform.SetPositionAndRotation(oldPosition, oldRotation);
    }

    [UnityTest]
    public IEnumerator Airfield_RunwayAndHelipadGateProduction() {
      yield return StartMatch(Map4PScene, startingTier: 3, stock: Stock(9000, 9000, 9000, 900),
        buildings: new[] { (0, "ff_vehicle_factory", -98, -112), (0, "ff_processor", -105, -101),
          (0, "ff_power_plant", -115, -104), (0, "ff_supply_depot", -101, -89) });
      yield return WaitForBattle();
      var origin = FactoryWarsUtil.MapInfo(Verified).StartPositions[0];
      var worker = UnitsNamed(0, "ff_engineer").First();
      foreach (var name in new[] { "ff_airfield", "ff_helipad" }) {
        var buildingRef = BuildingOf(0, name);
        var spec = Verified.FindAsset(buildingRef);
        Assert.IsTrue(ProductionSystem.IsUnlocked(Verified, 0, spec), name);
        var candidate = FPVector3.Zero;
        for (var ring = 12; ring <= 64 && candidate == FPVector3.Zero; ring += 4) {
          for (var z = -ring; z <= ring && candidate == FPVector3.Zero; z += 4) {
            for (var x = -ring; x <= ring; x += 4) {
              var point = origin + new FPVector3(x, 0, z);
              if (!ProductionSystem.CanPlace(Verified, spec, point, 0)) continue;
              candidate = point;
              break;
            }
          }
        }
        Assert.AreNotEqual(FPVector3.Zero, candidate, $"사용 가능한 인접 부지: {name}");
        yield return Send(0, new BuildCommand { Engineer = worker, Building = buildingRef, Position = candidate });
        var entity = OwnBuilding(0, name);
        Assert.AreNotEqual(EntityRef.None, entity);
        var construction = Verified.Get<AirfieldWorks>(entity).Site;
        var extra = AirfieldWorkSystem.BuildSeconds(FactoryWarsUtil.Rules(Verified), spec.Role);
        Assert.Greater(Verified.Get<Building>(entity).ConstructionRemaining.AsFloat, extra.AsFloat);
        Assert.IsFalse(ProductionSystem.CanPlace(Verified, Verified.FindAsset(BuildingOf(0, "ff_supply_depot")), construction.Center, 0));
        yield return WaitFor(() => Verified.Get<Building>(entity).ConstructionRemaining <= extra, 30f, "본체가 완성되면 부속 공사로 전환");
        Assert.AreEqual(WorkerTask.Build, Verified.Get<Worker>(worker).Task);
        Assert.AreEqual(entity, Verified.Get<Worker>(worker).Target);
        var aircraft = spec.Producible[0];
        yield return Send(0, new ProduceCommand { Building = entity, Unit = aircraft });
        Assert.AreEqual(0, Verified.Get<Building>(entity).QueueCount, "활주로 완공 전에는 생산 불가");
        var actor = Battle().Actors[entity];
        yield return WaitFor(() => actor.Runway != null && actor.Runway.Progress > 0.42f, 30f, "포장이 실제로 진행됨");
        var footprint = new GroundFootprint(construction.Center.ToUnityVector3(),
          new Vector3(construction.HalfLength.AsFloat, 0f, construction.HalfWidth.AsFloat),
          Quaternion.Euler(0f, construction.Yaw.AsFloat, 0f));
        var dressing = DressingRenderer.Active;
        Assert.IsFalse(dressing.GroupNames.Where(group => group.StartsWith("iron"))
          .SelectMany(dressing.Group).Any(item => footprint.Overlaps(item.Footprint)), "광산 장식이 포장 부지와 겹치지 않음");
        var airport = (candidate + construction.Center).ToUnityVector3() * 0.5f;
        var offset = spec.Role == BuildingRole.Helipad ? new Vector3(16f, 24f, 22f) : new Vector3(-19f, 23f, -21f);
        CaptureFacility(name + "-paving", airport, offset);
        yield return WaitFor(() => actor.Runway.Progress >= 1f, 30f, "포장 및 흰색 표식 완료");
        Assert.AreEqual(FP._0, Verified.Get<Building>(entity).ConstructionRemaining);
        Assert.IsTrue(actor.Runway.transform.Find("AirfieldMarkings").gameObject.activeSelf);
        CaptureFacility(name + "-complete", airport, offset);
        yield return Send(0, new ProduceCommand { Building = entity, Unit = aircraft });
        Assert.AreEqual(1, Verified.Get<Building>(entity).QueueCount, "부속 시설 완공 뒤 생산 가능");
      }
    }

    [UnityTest]
    public IEnumerator Naval_YardQueueShowsHullAndLaunchesOnWater() {
      yield return StartMatch(Map4PScene, startingTier: 3, stock: Stock(9000, 9000, 9000, 900),
        buildings: new[] { (0, "ff_power_plant", -115, -104), (0, "ff_supply_depot", -101, -89) });
      var specRef = BuildingOf(0, "_shipyard");
      var spec = Verified.FindAsset(specRef);
      var canal = CanalPoint(FactoryWarsUtil.MapInfo(Verified));
      var valid = Enumerable.Range(-10, 21).SelectMany(x => Enumerable.Range(-10, 21).Select(z => canal + new FPVector3(x * 2, 0, z * 2)))
        .First(point => ProductionSystem.CanPlace(Verified, spec, point, 0));
      var yard = ProductionSystem.CreateFinishedBuilding(Verified, 0, specRef, valid);
      yield return WaitForBattle();
      yield return WaitFor(() => Battle().Actors.ContainsKey(yard), 20f, "조선소 표시");
      var actor = Battle().Actors[yard];
      var boat = spec.Producible[0];
      var boatName = Verified.FindAsset(boat).name;
      yield return Send(0, new ProduceCommand { Building = yard, Unit = boat });
      yield return WaitFor(() => actor.ShipWork != null && actor.ShipWork.Progress > 0.3f, 15f, "선체와 조립 지지대");
      Assert.IsNotNull(actor.ShipWork.transform.Find("FloatingCradle"));
      var bay = actor.ShipWork.transform.position;
      Assert.AreEqual(TerrainKit.WaterLevel, bay.y, 0.1f);
      Assert.IsEmpty(UnitsNamed(0, boatName));
      var outward = bay - valid.ToUnityVector3();
      outward.y = 0f;
      outward.Normalize();
      var cameraOffset = outward * 18f + Vector3.up * 23f + Vector3.Cross(Vector3.up, outward) * 10f;
      var focus = Vector3.Lerp(bay, valid.ToUnityVector3(), 0.35f);
      CaptureFacility("shipbuilding", focus, cameraOffset);
      yield return WaitFor(() => UnitsNamed(0, boatName).Any(), 15f, "완성 함선 진수");
      var ship = UnitsNamed(0, boatName).First();
      var position = Verified.Get<Transform3D>(ship).Position;
      var water = Verified.Map.GetNavMesh(FactoryWarsUtil.Rules(Verified).WaterNavMeshName);
      Assert.IsTrue(water.Contains(position, NavMeshRegionMask.Default, true));
      yield return WaitFor(() => actor.ShipWork == null, 10f, "진수 뒤 건조 모델 정리");
      CaptureFacility("ship-launched", focus, cameraOffset);
    }

    [Test]
    public void Urban_RotatedPropFootprintsStaySeparateOnEveryMap() {
      var before = Dressing.RoadMask;
      try {
        foreach (var map in Resources.LoadAll<MapSpec>("FactoryWars").Where(map => map.name.StartsWith("map_Map"))) {
          Dressing.RoadMask = map;
          Dressing.Begin();
          UrbanDecor.Build(map);
          Dressing.End("urban_footprint_test");
          var spots = UrbanDecor.Spots.Where(spot => spot.Role != "quay").ToArray();
          foreach (var spot in spots) {
            Assert.IsFalse(map.Roads.Any(road => spot.Footprint.Overlaps(road, 0f)), $"{map.name}: 도로와 {spot.Kind} 겹침");
          }
          for (var a = 0; a < spots.Length; a++) {
            for (var b = a + 1; b < spots.Length; b++) {
              Assert.IsFalse(spots[a].Footprint.Overlaps(spots[b].Footprint), $"{map.name}: {spots[a].Kind}, {spots[b].Kind} 겹침");
            }
          }
          Debug.Log($"[UrbanFootprints] {map.name}: {spots.Length} copies");
        }
      } finally {
        Dressing.RoadMask = before;
      }
    }

    [UnityTest, Explicit, Category("UiCapture")]
    public IEnumerator Urban_CaptureBuiltInMapDressing() {
      foreach (var name in new[] { "Map4P", "MapDesert", "MapUrban", "MapArchipelago", "MapStrait" }) {
        yield return StartMatch($"Assets/QuantumUser/Scenes/{name}.unity");
        yield return WaitForBattle();
        var world = Battle().World;
        Assert.AreEqual("map_" + name, world.Map.name, "캡처할 맵이 실제 로드됨");
        var dressing = DressingRenderer.Active;
        var urban = dressing.Group("urban").Where(item => item.Kind != DressKind.QuayPillar
          && item.Kind != DressKind.QuayRail && item.Kind != DressKind.QuayBollard).ToArray();
        var biome = world.BiomePropFootprints.ToArray();
        var scatter = dressing.Group("scatter");
        for (var i = 0; i < biome.Length; i++) {
          Assert.IsFalse(world.Map.Roads.Any(road => biome[i].Overlaps(road, 0f)), $"{name}: 배경 프리팹이 도로를 침범");
          Assert.IsFalse(urban.Any(item => biome[i].Overlaps(item.Footprint)), $"{name}: 배경과 길가 소품 겹침");
          Assert.IsFalse(scatter.Any(item => biome[i].Overlaps(item.Footprint)), $"{name}: 배경과 흩뿌림 장식 겹침");
          for (var j = 0; j < i; j++) {
            Assert.IsFalse(biome[i].Overlaps(biome[j]), $"{name}: 배경 프리팹끼리 겹침");
          }
        }
        var roadside = urban.FirstOrDefault(item => item.Kind == DressKind.Container);
        if (roadside.Prefab == null) roadside = urban.FirstOrDefault();
        Assert.IsTrue(roadside.Prefab != null || biome.Length > 0, $"검사할 소품이 없음: {name}");
        var point = biome.Length > 0 ? biome[0].Center : roadside.Footprint.Center;
        CaptureFacility(name + "-dressing", new Vector3(point.x, 0f, point.y), new Vector3(-22f, 33f, -28f));
        Debug.Log($"[DressingAudit] {name}, roadside={urban.Length}, biome={biome.Length}, scatter={scatter.Count}");
        TearDown();
        yield return null;
      }
    }

    [UnityTest, Explicit, Category("UiCapture")]
    public IEnumerator Urban_RoadsideBarrierCollapsesWithoutRemovingNeighboringProps() {
      yield return StartMatch(Map4PScene);
      yield return WaitForBattle();
      GraphicsQuality.Apply(QualityTier.High);
      yield return null;
      var dress = DressingRenderer.Active;
      var prop = dress.Group("urban").First(item => item.Kind == DressKind.Barrier || item.Kind == DressKind.RoadCover);
      var at = new Vector3(prop.Footprint.Center.x, 0f, prop.Footprint.Center.y);
      var count = dress.Group("urban").Count;
      var cameraOffset = new Vector3(-7f, 9f, -10f);
      CaptureFacility("roadside-clearance", at, cameraOffset);
      Battle().Field.CollapseRoadsideBarriers(at, 0.6f);
      Assert.AreEqual(count - 1, dress.Group("urban").Count);
      yield return WaitFor(() => GameObject.Find("CollapsingRoadBarrier") != null, 5f, "벽 붕괴");
      var piece = GameObject.Find("CollapsingRoadBarrier");
      yield return WaitFor(() => piece == null || piece.transform.localEulerAngles.x > 6f, 5f, "벽이 기울어짐");
      CaptureFacility("barrier-collapse", at, cameraOffset);
      yield return WaitFor(() => piece == null, 8f, "붕괴 뒤 오브젝트 정리");
    }
  }
}
