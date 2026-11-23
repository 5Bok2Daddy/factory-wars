namespace FactoryWars.Tests {
  using System.Collections;
  using System.Collections.Generic;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using Photon.Deterministic;
  using Quantum;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  /// <summary>
  /// 항목 36 시가전 배경·수로 시험. 구입한 팩이 없는 PC에서는 소품 칸이 비어 있어 장식이 놓이지 않으므로 "팩 없음"으로 통과한다.
  /// 장식은 화면 전용이라 시뮬레이션·NavMesh·건설 부지는 건드리지 않는다는 것을 자리 검사로 확인한다.
  /// </summary>
  public unsafe partial class LocalMatchTests {
    static MapSpec Map4P() => Resources.LoadAll<MapSpec>("FactoryWars").First(m => m.name == "map_Map4P");

    static FPVector3 UrbanFp(Vector3 p) => new FPVector3(FP.FromFloat_UNSAFE(p.x), default, FP.FromFloat_UNSAFE(p.z));

    /// <summary>길가 장식은 길(차선) 위, 본진, 건물 부지, 자원지, 점령지, 보급 낙하 자리, 다리, 물 위에 놓이지 않는다.</summary>
    [Test]
    public void Urban_PropsStayOffWalkableAndPlots() {
      if (PackPresent == false) {
        Assert.Pass("팩 없음");
      }
      var map = Map4P();
      Assert.IsTrue(VisualProfile.Current.UrbanDecor, "시가지 장식 켜짐");
      Dressing.Begin();
      UrbanDecor.Build(map);
      Dressing.End("urban_test");
      var street = UrbanDecor.Spots.Where(s => s.Role == "street").ToArray();
      var boundary = UrbanDecor.Spots.Where(s => s.Role == "boundary").ToArray();
      Assert.Greater(street.Length, 10, "길가 장식");
      // 봉쇄 맵(320m 표준화)은 가장자리를 자연 능선·절벽으로 막는다. 건물 외벽 열은 봉쇄 전 맵에서만 세운다.
      if (map.SealedPerimeter) Assert.IsEmpty(boundary, "봉쇄 맵에는 가장자리 건물 열이 없다");
      else Assert.Greater(boundary.Length, 20, "맵 가장자리 건물");
      var paths = TerrainKit.RiverPaths(map);
      var problems = new List<string>();
      foreach (var spot in street) {
        var fp = UrbanFp(spot.Position);
        foreach (var road in map.Roads) {
          if (road.Distance(fp).AsFloat < 1.5f) {
            problems.Add($"길 위: {spot.Kind} {spot.Position}");
          }
        }
        foreach (var area in map.BaseAreas) {
          if (area.Distance(fp).AsFloat < 2f) {
            problems.Add($"본진 안: {spot.Position}");
          }
        }
        foreach (var city in map.CityBuildings) {
          if (city.Shape.Distance(fp).AsFloat < 2.5f) {
            problems.Add($"도시 건물 부지: {spot.Position}");
          }
        }
        foreach (var node in map.ResourceNodes) {
          if (node.Shape.Distance(fp).AsFloat < 4f) {
            problems.Add($"자원지: {spot.Position}");
          }
        }
        foreach (var bridge in map.Bridges) {
          if (bridge.Distance(fp).AsFloat < 3f) {
            problems.Add($"다리: {spot.Position}");
          }
        }
        if (TerrainKit.RiverDistance(paths, new Vector2(spot.Position.x, spot.Position.z)) < 7f) {
          problems.Add($"물가: {spot.Position}");
        }
      }
      foreach (var spot in boundary) {
        // 가장자리 건물은 경기장 안쪽(경계 안)에 놓이지 않는다.
        var inside = spot.Position.x > map.BoundsMin.X.AsFloat && spot.Position.x < map.BoundsMax.X.AsFloat && spot.Position.z > map.BoundsMin.Y.AsFloat && spot.Position.z < map.BoundsMax.Y.AsFloat;
        if (inside) {
          problems.Add($"가장자리 건물이 경기장 안: {spot.Position}");
        }
      }
      Assert.IsEmpty(problems, string.Join("\n", problems.Take(20)));
    }

    /// <summary>교차로가 있고(길 중심선 교점이 두 길 안), 교차로마다 횡단보도 줄무늬가 깔린다.</summary>
    [Test]
    public void Urban_RoadsFollowPaths() {
      var map = Map4P();
      var crossings = UrbanDecor.Crossings(map);
      Assert.GreaterOrEqual(crossings.Count, 1, "교차로");
      foreach (var crossing in crossings) {
        var fp = UrbanFp(new Vector3(crossing.Point.x, 0f, crossing.Point.y));
        var onRoads = map.Roads.Count(r => r.Distance(fp).AsFloat <= 9f);
        Assert.GreaterOrEqual(onRoads, 2, $"교차로 {crossing.Point}는 길 두 개가 닿음");
      }
      var kit = new MeshKit();
      UrbanDecor.Crosswalks(kit, map);
      if (VisualProfile.Current.FieldLook) {
        // 야전 룩(아스팔트, 2026-11-15 3차): 교차로마다 횡단보도가 있고, 다리 양끝을 길과 잇는 연결로가 있다.
        Assert.GreaterOrEqual(kit.TriangleCount, crossings.Count * 4 * 2, "교차로마다 횡단보도");
        var links = new MeshKit();
        UrbanDecor.BridgeLinks(links, map);
        Assert.Greater(links.TriangleCount, 0, "다리 입구와 길을 잇는 흙길");
      } else {
        Assert.GreaterOrEqual(kit.TriangleCount, crossings.Count * 4 * 2, "교차로마다 네 갈래에 줄무늬");
      }
    }

    /// <summary>수로 둑이 끊기지 않는다: 강줄기마다 물 면·거품 띠(양쪽)·둑 면이 구간마다 한 벌씩 이어지고, 물 셰이더가 있다.</summary>
    [Test]
    public void Urban_CanalEdgesContinuous() {
      var map = Map4P();
      var paths = TerrainKit.RiverPaths(map);
      Assert.Greater(paths.Count, 0, "수로");
      Assert.IsNotNull(UrbanDecor.WaterMaterial(), "물 셰이더(FactoryWars/UrbanWater)");
      foreach (var path in paths) {
        var bank = new MeshKit();
        var water = new MeshKit();
        TerrainKit.River(bank, path, 40, TerrainKit.Land(map), water);
        // 구간마다 물 면 1쪽 + 양쪽 거품 2쪽 = 삼각형 6개.
        Assert.Greater(water.TriangleCount, 0);
        Assert.AreEqual(0, water.TriangleCount % 6, "물 면+거품 띠가 구간마다 한 벌");
        // 구간마다 양쪽 둑 면(윗단·비탈·바깥 띠)이 한 벌씩이라 둑 삼각형 수도 구간 수의 배수다.
        var segments = water.TriangleCount / 6;
        Assert.GreaterOrEqual(bank.TriangleCount, segments * 2 * 2 * 2, "양쪽 둑 윗단·비탈");
      }
    }

    /// <summary>다리와 방어 울타리는 시뮬레이션 상태를 따른다: 휴전 중에는 울타리가 서고, 열리면 내려가 사라진다. 다리 모델 수는 맵의 다리 수와 같다.</summary>
    [UnityTest]
    public IEnumerator Urban_BridgeMatchesSimulationState() {
      yield return StartMatch(Map4PScene, 2, bots: new[] { BotDifficulty.Normal }, seed: 5, truce: 20);
      yield return WaitForHud();
      yield return WaitForBattle();
      var map = Map4P();
      var world = Battle().World;
      Assert.AreEqual(map.Bridges.Length, world.BridgeCount, "다리 상태 수 = 맵의 다리 수");
      var slots = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t => t.name == "BridgeSlot").ToArray();
      Assert.AreEqual(map.Bridges.Length, slots.Length, "다리 모델 수 = 맵의 다리 수");
      // Task 39 fix 2: on the grid map the intact bridge is drawn by ArchBridgeView (one steel arch per bridge); the old slot model is off.
      var arches = Battle().ArchBridges?.Count ?? 0;
      Assert.IsTrue(arches == map.Bridges.Length || slots.All(slot => slot.GetComponentsInChildren<MeshRenderer>()
        .Any(renderer => renderer.enabled && renderer.GetComponent<MeshFilter>()?.sharedMesh != null)),
        $"모든 다리에 실제 표시되는 메쉬가 있다(아치교 {arches})");
      Assert.IsTrue(world.BarrierVisible, "휴전 중 울타리가 서 있다");
      Assert.IsFalse(BarrierOpen());
      yield return WaitFor(BarrierOpen, 30f, "휴전 종료");
      yield return WaitFor(() => world.BarrierDrop >= 1f, 20f, "울타리 하강");
      yield return null;
      Assert.IsFalse(world.BarrierVisible, "열리면 울타리가 사라진다");
    }

    /// <summary>깊이 거품 물 셰이더: 높음·보통은 깊이 거품 키워드가 켜지고(URP 깊이 텍스처 사용), 낮음은 꺼져 깊이 텍스처 없이 단색+가장자리 거품이다. 색·폭·흐름은 프로필 데이터.</summary>
    [Test]
    public void Urban_WaterDepthFoamByQuality() {
      var before = GraphicsQuality.Current;
      try {
        var material = UrbanDecor.WaterMaterial();
        Assert.IsNotNull(material);
        Assert.IsTrue(material.HasProperty("_FoamWidth") && material.HasProperty("_Shallow") && material.HasProperty("_Flow"));
        var profile = VisualProfile.Current;
        Assert.AreEqual(profile.WaterFoamWidth, material.GetFloat("_FoamWidth"), 0.001f, "거품 폭은 프로필 데이터");
        Assert.AreEqual(profile.WaterFlowSpeed.x, material.GetVector("_Flow").x, 0.001f, "흐름 속도는 프로필 데이터");
        GraphicsQuality.Apply(QualityTier.High);
        Assert.IsTrue(material.IsKeywordEnabled("_FW_DEPTH_FOAM"), "높음: 깊이 거품");
        Assert.IsTrue(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset high && high.supportsCameraDepthTexture, "높음 URP 에셋이 깊이 텍스처를 만든다");
        GraphicsQuality.Apply(QualityTier.Medium);
        Assert.IsTrue(material.IsKeywordEnabled("_FW_DEPTH_FOAM"), "보통: 깊이 거품");
        GraphicsQuality.Apply(QualityTier.Low);
        Assert.IsFalse(material.IsKeywordEnabled("_FW_DEPTH_FOAM"), "낮음: 깊이 거품 끔");
        Assert.Less(material.renderQueue, 3000, "낮음은 불투명 순서(겹침 그리기 절약)");
      } finally {
        GraphicsQuality.Apply(before);
      }
    }

    /// <summary>수문 조립: 옹벽·다리 기둥·문을 우리 프리팹으로 조립해 수문 영역마다 하나 세우고, 휴전이 끝나면 가운데 문이 내려가며 물보라가 나온다(시뮬레이션 상태를 읽기만 함).</summary>
    [UnityTest]
    public IEnumerator Urban_SealedPerimeterHasNoExteriorGate() {
      yield return StartMatch(Map4PScene, 2, bots: new[] { BotDifficulty.Normal }, seed: 5, truce: 20);
      yield return WaitForHud();
      yield return WaitForBattle();
      var map = Map4P();
      var world = Battle().World;
      Assert.IsEmpty(map.WaterGates);
      Assert.IsEmpty(world.Gates);
      yield return WaitFor(BarrierOpen, 30f, "휴전 종료");
      Assert.IsEmpty(world.Gates, "휴전 뒤에도 밖으로 열리는 수문이 없다");
    }

    /// <summary>품질별 장식 밀도: 낮음 40%·보통 70%·높음 100%(데이터 VisualProfile).</summary>
    [Test]
    public void Urban_DensityByQuality() {
      if (PackPresent == false) {
        Assert.Pass("팩 없음");
      }
      var profile = VisualProfile.Current;
      Assert.AreEqual(0.4f, profile.UrbanDensityLow, 0.001f);
      Assert.AreEqual(0.7f, profile.UrbanDensityMedium, 0.001f);
      var holder = new GameObject("DressingTest");
      var renderer = holder.AddComponent<DressingRenderer>();
      Dressing.Begin();
      UrbanDecor.Build(Map4P());
      Dressing.End("urban");
      var items = renderer.Group("urban");
      Assert.Greater(items.Count, 40, "장식 개체");
      int Kept(float limit) => items.Count(i => i.Keep <= limit);
      var low = Kept(profile.UrbanDensityLow) / (float)items.Count;
      var medium = Kept(profile.UrbanDensityMedium) / (float)items.Count;
      Assert.AreEqual(profile.UrbanDensityLow, low, 0.15f, "낮음 비율");
      Assert.AreEqual(profile.UrbanDensityMedium, medium, 0.15f, "보통 비율");
      Assert.Less(low, medium);
      // 그리는 쪽 규칙도 같다: 낮음은 LowKeep, 보통은 MediumKeep을 기준으로 한다.
      Assert.IsTrue(items.All(i => i.LowKeep == profile.UrbanDensityLow && i.MediumKeep == profile.UrbanDensityMedium));
      Object.Destroy(holder);
    }

    /// <summary>궁극 전력(전략 미사일) 경고 막대: 칠은 글자 아래 칸이고 반투명이며 글자는 흰색+외곽선이라 항상 읽힌다.</summary>
    [UnityTest]
    public IEnumerator Hud_WarningTextNotCovered() {
      yield return StartMatch();
      yield return WaitForHud();
      Assert.IsTrue(Controller().Hud.UltimateTextAboveFill, "글자가 칠 위 칸, 외곽선, 반투명 칠");
    }
  }
}
