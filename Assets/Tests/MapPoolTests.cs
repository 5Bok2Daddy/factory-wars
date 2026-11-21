namespace FactoryWars.Tests {
  using System.Collections;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using Photon.Deterministic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  /// <summary>항목 35 맵 풀(지금은 표준 + 사막 보급전). 사막은 표준 지형을 그대로 쓰므로 대칭·연결은 표준과 같고, 데이터(광산·보급)만 다르다.</summary>
  public unsafe partial class LocalMatchTests {
    const string DesertScene = "Assets/QuantumUser/Scenes/MapDesert.unity";

    static MapSpec MapNamed(string name) => Resources.LoadAll<MapSpec>("FactoryWars").First(m => m.name == name);

    static int DropDelayFrames() => Verified.Global->NextDropFrame - Verified.Global->TruceEndFrame;

    [Test]
    public void Map_Desert_PresetData() {
      var standard = MapNamed("map_Map4P");
      var desert = MapNamed("map_MapDesert");
      Assert.AreEqual("사막 보급전", desert.Theme);
      Assert.AreEqual(0.5f, desert.SupplyIntervalMultiplier.AsFloat, 0.01f);
      Assert.AreEqual(2f, desert.SupplyRewardMultiplier.AsFloat, 0.01f);
      Assert.AreEqual(standard.ResourceNodes.Length, desert.ResourceNodes.Length, "같은 광산 자리");
      for (var i = 0; i < standard.ResourceNodes.Length; i++) {
        Assert.AreEqual(standard.ResourceNodes[i].Shape.Center, desert.ResourceNodes[i].Shape.Center, $"광산 {i} 위치");
        Assert.AreEqual(standard.ResourceNodes[i].Amount.AsFloat * 0.55f, desert.ResourceNodes[i].Amount.AsFloat, 1f, $"광산 {i} 양 55%");
      }
      Assert.AreEqual(standard.CrateSpots.Length, desert.CrateSpots.Length, "보급 자리 수");
      Assert.AreEqual(standard.CityBuildings.Length, desert.CityBuildings.Length, "도시 건물 수");
      Assert.AreEqual(standard.StartPositions.Length, desert.StartPositions.Length, "시작 자리(회전 대칭 그대로)");
      Assert.AreEqual(1f, standard.SupplyIntervalMultiplier.AsFloat, 0.01f, "표준은 배율 1");
    }

    [Test]
    public void Map_Catalog_CyclesAndFindsScenes() {
      Assert.AreEqual(5, MapCatalog.Maps.Length);
      Assert.AreEqual("Map4P", MapCatalog.Maps[0].Id);
      Assert.AreEqual("MapDesert", MapCatalog.Next("Map4P").Id);
      Assert.AreEqual("MapUrban", MapCatalog.Next("MapDesert").Id);
      Assert.AreEqual("MapArchipelago", MapCatalog.Next("MapUrban").Id);
      Assert.AreEqual("MapStrait", MapCatalog.Next("MapArchipelago").Id);
      Assert.AreEqual("Map4P", MapCatalog.Next("MapStrait").Id, "한 바퀴");
      var settings = new MatchSettings { MapId = "MapDesert" };
      Assert.AreEqual("MapDesert", settings.Scene);
      foreach (var entry in MapCatalog.Maps) {
        Assert.IsTrue(System.IO.File.Exists($"Assets/QuantumUser/Scenes/{entry.Id}.unity"), $"{entry.Id} 씬");
      }
    }

    /// <summary>사막 보급전: 첫 낙하까지 간격이 규칙의 절반이고, 보상 배율이 2다. 사람과 AI가 같은 규칙을 쓴다(시뮬레이션 데이터).</summary>
    [UnityTest]
    public IEnumerator Map_Desert_SupplyRunsFaster() {
      yield return StartMatch(DesertScene, 2, bots: new[] { BotDifficulty.Normal }, seed: 5, truce: 20);
      yield return WaitFrames(Fps);
      var rules = Resources.Load<GameRulesSpec>("FactoryWars/game_rules");
      Assert.AreEqual(rules.DropIntervalSeconds.AsFloat * 0.5f, DropDelayFrames() / (float)Fps, 0.5f, "낙하 간격 절반");
      Assert.AreEqual(2f, BattlefieldSystem.SupplyReward(Verified).AsFloat, 0.01f);
      Assert.AreEqual(0.5f, BattlefieldSystem.SupplyInterval(Verified).AsFloat, 0.01f);
    }

    [UnityTest, Explicit]
    public IEnumerator CaptureMaps35_AllFive() {
      var pairs = new[] {
        ("Map4P", "map_standard", new Vector2(0f, 0f), 125f),
        ("MapDesert", "map_desert", new Vector2(-62f, -36f), 98f),
        ("MapUrban", "map_urban", new Vector2(-44f, -44f), 92f),
        ("MapArchipelago", "map_archipelago", new Vector2(0f, 0f), 235f),
        ("MapStrait", "map_strait", new Vector2(0f, 0f), 210f),
      };
      var statusDir = System.IO.Path.Combine(Application.dataPath, "..", "Docs", "status_images");
      System.IO.Directory.CreateDirectory(statusDir);
      foreach (var (sceneId, shotName, focus, height) in pairs) {
        TearDown();
        yield return null;
        yield return StartMatch($"Assets/QuantumUser/Scenes/{sceneId}.unity", 4, truce: 0);
        yield return WaitForHud();
        yield return WaitForBattle();
        Time.timeScale = 1f;
        var cam = Controller().Camera;
        cam.MoveTo(focus);
        cam.Height = height;
        cam.Apply();
        yield return new WaitForSeconds(0.4f);
        ShootUrban(shotName);
        var src = System.IO.Path.Combine(UrbanFolder(), shotName + ".png");
        if (System.IO.File.Exists(src)) {
          System.IO.File.Copy(src, System.IO.Path.Combine(statusDir, shotName + ".png"), true);
        }
      }
    }

    [UnityTest, Explicit]
    public IEnumerator CaptureEntireStandardMap() {
      yield return StartMatch(Map4PScene, 4, truce: 0);
      yield return WaitForBattle();
      var map = FactoryWarsUtil.MapInfo(Verified);
      var size = (map.BoundsMax - map.BoundsMin).ToUnityVector2();
      var center = ((map.BoundsMin + map.BoundsMax) / FP._2).ToUnityVector2();
      var camera = Camera.main;
      var position = camera.transform.position;
      var rotation = camera.transform.rotation;
      var clip = camera.farClipPlane;
      var wasOrthographic = camera.orthographic;
      var oldSize = camera.orthographicSize;
      try {
        camera.orthographic = true;
        camera.orthographicSize = Mathf.Max(size.y, size.x) * 0.54f;
        camera.farClipPlane = 1200f;
        camera.transform.SetPositionAndRotation(new Vector3(center.x, 420f, center.y), Quaternion.Euler(90f, 0f, 0f));
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../Game-logs/recovery-full-map.png"));
        CaptureCameraPng(camera, path, 1440, 1440);
      } finally {
        camera.transform.SetPositionAndRotation(position, rotation);
        camera.farClipPlane = clip;
        camera.orthographic = wasOrthographic;
        camera.orthographicSize = oldSize;
      }
    }

    [UnityTest, Explicit, Category("CaptureCatalog")]
    public IEnumerator CaptureNewBundleMapArt() {
      Assert.IsTrue(NewBundleArt.HasBridge, "Backroad Bridges 준비 완료");
      foreach (var key in new[] { "CanyonRock", "SandRidge", "DesertBoulder", "WoodlandTree", "MeadowShrub", "MeadowRock" }) {
        var prefab = NewBundleArt.Prefab(key);
        Assert.IsNotNull(prefab, key);
        Assert.IsNotEmpty(prefab.GetComponentsInChildren<MeshRenderer>(), key);
        foreach (var renderer in prefab.GetComponentsInChildren<MeshRenderer>()) {
          Assert.IsTrue(renderer.sharedMaterials.All(material => material != null
            && material.shader.isSupported && material.shader.name.StartsWith("Universal Render Pipeline/")), key);
        }
      }
      var maps = new[] {
        ("Map4P", "map_standard"), ("MapDesert", "map_desert"), ("MapUrban", "map_urban"),
        ("MapArchipelago", "map_archipelago"), ("MapStrait", "map_strait"),
      };
      foreach (var (scene, shot) in maps) {
        TearDown();
        yield return null;
        GraphicsQuality.Apply(QualityTier.High);
        yield return StartMatch($"Assets/QuantumUser/Scenes/{scene}.unity", 4, truce: 0);
        yield return WaitForBattle();
        yield return WaitFrames(5);
        var world = Object.FindAnyObjectByType<BattleView>().World;
        var map = world.Map;
        Assert.IsNotNull(Verified.Map.GetNavMesh(FactoryWarsUtil.Rules(Verified).NavMeshName), $"지상 NavMesh {scene}");
        var models = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
        if (MapPolishArt.Prefab("Fence") != null) {
          var props = DressingRenderer.Active.Group("urban");
          if (scene == "Map4P") {
            Assert.Greater(props.Count(item => item.Prefab.name == "Fence"), 15, "운하 경계 철조망");
            Assert.IsFalse(props.Any(item => item.Kind == DressKind.QuayPillar), "독립 호안 기둥 제거");
          }
          if (scene == "MapArchipelago" || scene == "MapStrait") {
            Assert.IsTrue(props.Any(item => item.Prefab.name == "Dock"), $"{scene} 부두");
            Assert.IsFalse(models.Any(model => model.name == "Water" && model.parent?.name == "World"), $"{scene} 중복 수면 스트립 제거");
          }
          foreach (var item in props.Where(item => item.Prefab.name is "Fence" or "Guardrail" or "Jersey" or "Tower" or "Sandbags" or "Dock" or "CoastalRock" or "CoastalCliff")) {
            Assert.IsFalse(map.Roads.Any(road => item.Footprint.Overlaps(road, 0f)), $"{scene} 소품이 차도를 침범 {item.Prefab.name}");
          }
        }
        var decks = models.Where(model => model.name == "BundleBridgeDeck").ToArray();
        if (scene == "Map4P" || scene == "MapArchipelago" || scene == "MapStrait") {
          Assert.AreEqual(map.Bridges.Length, decks.Length, scene);
          Assert.IsNotEmpty(decks);
          foreach (var deck in decks) {
            var laneHalfWidth = deck.GetComponent<MeshFilter>().sharedMesh.bounds.extents.x;
            var rails = deck.Cast<Transform>().Where(part => part.name == "BundleBridgeRail").ToArray();
            Assert.IsNotEmpty(rails);
            foreach (var rail in rails) {
              Assert.IsEmpty(rail.GetComponentsInChildren<Collider>(), "교량 장식은 물리 통행에 영향 없음");
              foreach (var filter in rail.GetComponentsInChildren<MeshFilter>()) {
                var bounds = filter.sharedMesh.bounds;
                var toDeck = deck.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                var side = Mathf.Sign(rail.localPosition.x);
                for (var x = -1; x <= 1; x += 2)
                  for (var y = -1; y <= 1; y += 2)
                    for (var z = -1; z <= 1; z += 2) {
                      var local = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                      Assert.That(toDeck.MultiplyPoint3x4(local).x * side, Is.GreaterThanOrEqualTo(laneHalfWidth - 0.02f),
                        $"난간 메쉬가 차도를 침범 {scene}");
                    }
              }
            }
          }
        }
        var focus = Vector3.zero;
        if (scene == "MapDesert") {
          var sand = models.Single(model => model.name == "Ground" && model.parent != null && model.parent.name == "World")
            .GetComponent<MeshRenderer>().sharedMaterial;
          Assert.AreEqual("SandNormal", sand.GetTexture("_AsphaltNormalMap").name);
          Assert.Greater(sand.GetFloat("_AsphaltNormalStrength"), 0f);
          var desert = models.Where(model => model.name is "CanyonRock(Clone)" or "SandRidge(Clone)" or "DesertBoulder(Clone)")
            .OrderBy(model => model.position.sqrMagnitude).ToArray();
          Assert.IsNotEmpty(desert);
          foreach (var footprint in world.BiomePropFootprints) {
            Assert.IsFalse(map.Roads.Any(road => footprint.Overlaps(road, 0f)), "사막 소품이 차도를 침범");
          }
          focus = desert[0].position;
        } else if (decks.Length > 0) {
          focus = decks.OrderBy(deck => deck.position.sqrMagnitude).First().position;
        }
        if (scene == "Map4P") {
          var woodland = map.ResourceNodes.SelectMany((node, index) =>
              DressingRenderer.Active.Group($"forest{11 + index * 7}"))
            .Where(item => item.Prefab.name is "WoodlandTree" or "WoodlandTallTree" or "MeadowShrub" or "MeadowRock").ToArray();
          Assert.IsTrue(woodland.Any(item => item.Prefab.name.StartsWith("Woodland")), "목재 노드 숲");
          Assert.IsTrue(woodland.Any(item => item.Prefab.name.StartsWith("Meadow")), "목재 노드 주변 풀·돌");
          foreach (var item in woodland) {
            Assert.IsFalse(map.Roads.Any(road => item.Footprint.Overlaps(road, 0f)), "목재 노드 소품이 차도를 침범");
          }
        }
        var camera = Camera.main;
        var offset = scene == "MapStrait" ? new Vector3(-53f, 95f, -75f) : new Vector3(-20f, 85f, -60f);
        camera.transform.position = focus + offset;
        camera.transform.LookAt(focus);
        camera.fieldOfView = 48f;
        var target = System.IO.Path.Combine(StatusImagesDir(), $"{shot}.png");
        CaptureCameraPng(camera, target, 1600, 900);
      }
    }
  }
}
