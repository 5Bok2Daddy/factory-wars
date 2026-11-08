namespace FactoryWars.Tests {
  using System.Collections;
  using System.Collections.Generic;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using Assert = NUnit.Framework.Assert;
  using Photon.Deterministic;
  using Quantum;
  using UnityEngine;
  using UnityEngine.TestTools;

  /// <summary>구불구불한 강의 제약과 개폐식 방어 울타리.</summary>
  public partial class LocalMatchTests {
    static readonly string[] RiverMaps = { "Map2P", "Map4P" };

    static FPVector3 Fp(Vector2 point) => new FPVector3(FP.FromFloat_UNSAFE(point.x), default, FP.FromFloat_UNSAFE(point.y));

    static Vector2 Flat(FPVector3 point) => new Vector2(point.X.AsFloat, point.Z.AsFloat);

    static IEnumerable<MapShape> RiverBlocks(MapSpec map) => map.Terrain.Where(t => t.Kind == TerrainKind.River).Select(t => t.Shape);

    [Test]
    public void River_IsStraightForTenMetersAroundEveryBridge() {
      foreach (var id in RiverMaps) {
        var map = MapAsset(id);
        var points = TerrainKit.RiverPaths(map).SelectMany(p => p).ToArray();
        foreach (var bridge in map.Bridges) {
          var center = Flat(bridge.Center);
          var extent = TerrainKit.Extent(bridge);
          var acrossX = extent.width < extent.height;
          var near = points.Where(p => (p.Position - center).magnitude <= 10f).ToArray();
          Assert.Greater(near.Length, 0, id);
          foreach (var point in near) {
            Assert.AreEqual(acrossX ? center.x : center.y, acrossX ? point.Position.x : point.Position.y, 0.05f, $"{id}: 다리 앞뒤는 직선");
            Assert.AreEqual(RiverHalfMin, point.Half, 0.01f, $"{id}: 폭 일정");
          }
        }
      }
    }

    const float RiverHalfMin = 3f;
    const float RiverHalfMax = 5f;

    [Test]
    public void River_CurvatureGrowsSmoothly() {
      foreach (var id in RiverMaps) {
        foreach (var path in TerrainKit.RiverPaths(MapAsset(id))) {
          for (var i = 1; i + 1 < path.Count; i++) {
            var a = (path[i].Position - path[i - 1].Position).normalized;
            var b = (path[i + 1].Position - path[i].Position).normalized;
            Assert.Less(Vector2.Angle(a, b), 32f, $"{id}: 꺾임 {i}");
          }
        }
      }
    }

    [Test]
    public void River_ActuallyMeanders() {
      foreach (var id in RiverMaps) {
        foreach (var path in TerrainKit.RiverPaths(MapAsset(id))) {
          var turns = 0;
          var last = 0f;
          for (var i = 1; i + 1 < path.Count; i++) {
            var a = (path[i].Position - path[i - 1].Position).normalized;
            var b = (path[i + 1].Position - path[i].Position).normalized;
            var cross = a.x * b.y - a.y * b.x;
            if (Mathf.Abs(cross) > 0.02f) {
              turns += last != 0f && Mathf.Sign(cross) != Mathf.Sign(last) ? 1 : 0;
              last = cross;
            }
          }
          Assert.GreaterOrEqual(turns, 2, $"{id}: 곡선 방향이 바뀌어야 구불구불함");
        }
      }
    }

    [Test]
    public void River_WidthStaysBetweenSixAndTenMeters() {
      foreach (var id in RiverMaps) {
        foreach (var piece in MapAsset(id).River) {
          var half = piece.HalfWidth.AsFloat;
          Assert.GreaterOrEqual(half, RiverHalfMin - 0.001f, id);
          Assert.LessOrEqual(half, RiverHalfMax + 0.001f, id);
        }
      }
    }

    [Test]
    public void River_BanksStayOutsideEveryBase() {
      foreach (var id in RiverMaps) {
        var map = MapAsset(id);
        foreach (var path in TerrainKit.RiverPaths(map)) {
          foreach (var point in path) {
            foreach (var side in new[] { -1f, 1f }) {
              var bank = Fp(point.Position + point.Normal * (side * point.Half));
              foreach (var area in map.BaseAreas) {
                Assert.GreaterOrEqual(area.Distance(bank).AsFloat, -0.02f, $"{id}: 강가가 본진 안");
              }
            }
          }
        }
      }
    }

    /// <summary>직선 강이던 때의 언덕. 본진 건설 가능 면적을 비교하는 기준이다.</summary>
    static int BuildableCells(MapShape area, IEnumerable<MapShape> blocks, float margin) {
      var list = blocks.ToArray();
      var count = 0;
      var reach = area.HalfLength.AsFloat;
      var center = Flat(area.Center);
      for (var x = -reach; x <= reach; x += 1f) {
        for (var z = -reach; z <= reach; z += 1f) {
          var point = Fp(center + new Vector2(x, z));
          if (list.All(b => b.Distance(point).AsFloat >= margin)) {
            count++;
          }
        }
      }
      return count;
    }

    [Test]
    public void River_BlocksEveryPointExceptBridges() {
      foreach (var id in RiverMaps) {
        var map = MapAsset(id);
        var blocks = RiverBlocks(map).ToArray();
        var limit = map.BoundsMax.X.AsFloat;
        foreach (var path in TerrainKit.RiverPaths(map)) {
          for (var i = 0; i + 1 < path.Count; i++) {
            for (var t = 0f; t <= 1f; t += 0.25f) {
              var center = Vector2.Lerp(path[i].Position, path[i + 1].Position, t);
              if (Mathf.Abs(center.x) > limit - 1f || Mathf.Abs(center.y) > limit - 1f || map.Bridges.Any(b => (Flat(b.Center) - center).magnitude < 7.5f)) {
                continue;
              }
              var half = Mathf.Lerp(path[i].Half, path[i + 1].Half, t) - 0.4f;
              for (var across = -half; across <= half; across += 1f) {
                var point = Fp(center + path[i].Normal * across);
                Assert.IsTrue(blocks.Any(b => b.Distance(point).AsFloat <= 0f), $"{id}: 강에 틈 {center} {across}");
              }
            }
          }
        }
      }
    }

    [Test]
    public void River_KeepsBridgeCount() {
      Assert.AreEqual(4, MapAsset("Map2P").Bridges.Length);
      Assert.AreEqual(8, MapAsset("Map4P").Bridges.Length);
    }

    [Test]
    public void River_IsRotationallySymmetric() {
      foreach (var (id, turns) in new[] { ("Map2P", 2), ("Map4P", 4) }) {
        var pieces = MapAsset(id).River;
        var count = pieces.Length / turns;
        Assert.AreEqual(count * turns, pieces.Length, id);
        for (var quarter = 1; quarter < turns; quarter++) {
          var steps = 4 / turns * quarter;
          for (var i = 0; i < count; i++) {
            var point = Flat(pieces[i].Center);
            for (var s = 0; s < steps; s++) {
              point = new Vector2(-point.y, point.x);
            }
            Assert.AreEqual(point.x, pieces[quarter * count + i].Center.X.AsFloat, 0.01f, id);
            Assert.AreEqual(point.y, pieces[quarter * count + i].Center.Z.AsFloat, 0.01f, id);
          }
        }
      }
    }

    [Test]
    public void River_LeavesResourcesAndBridgesClear() {
      foreach (var id in RiverMaps) {
        var map = MapAsset(id);
        foreach (var block in RiverBlocks(map)) {
          foreach (var node in map.ResourceNodes) {
            Assert.Greater(node.Shape.Distance(block.Center).AsFloat, block.HalfWidth.AsFloat, $"{id}: 자원을 덮음");
          }
          foreach (var bridge in map.Bridges) {
            var gap = TerrainKit.Extent(bridge);
            Assert.IsFalse(gap.Contains(Flat(block.Center)), $"{id}: 다리 통로를 막음");
          }
        }
      }
    }

    [Test]
    public void River_RocksStayInsideTheBlockedWater() {
      var map = MapAsset("Map2P");
      var path = TerrainKit.RiverPaths(map)[0];
      var kit = new MeshKit();
      TerrainKit.RiverRocks(kit, path, 70, map);
      var mesh = kit.ToMesh("test");
      Assert.Greater(mesh.vertexCount, 0, "바위가 있음");
      foreach (var vertex in mesh.vertices) {
        Assert.Less(TerrainKit.RiverDistance(new List<List<TerrainKit.RiverPoint>> { path }, new Vector2(vertex.x, vertex.z)), 0.15f, "물가 안쪽");
      }
    }

    [Test]
    public void River_MeshIsSingleSubmeshNeutralArt() {
      var kit = new MeshKit();
      TerrainKit.Features(kit, MapAsset("Map2P"));
      var mesh = kit.ToMesh("test");
      Assert.AreEqual(1, mesh.subMeshCount);
      Assert.Less(kit.TriangleCount, 45000, "삼각형 예산");
    }

    // 울타리

    [Test]
    public void Barrier_CoversEntrancesAndNearbyBanksOnly() {
      foreach (var id in RiverMaps) {
        var map = MapAsset(id);
        for (var i = 0; i < map.Bridges.Length; i++) {
          var pieces = TerrainKit.BarrierPieces(map, i);
          Assert.LessOrEqual(pieces.Count, 18, id);
          Assert.GreaterOrEqual(pieces.Count, 14, $"{id}: 입구 6칸과 바깥쪽 강가는 남음");
          var center = map.Bridges[i].Center.ToUnityVector3();
          foreach (var piece in pieces) {
            Assert.Less((piece.From - center).magnitude, 26f, $"{id}: 강 전체를 두르지 않음");
          }
        }
      }
    }

    [Test]
    public void Barrier_BankFenceNeverStandsInWater() {
      foreach (var id in RiverMaps) {
        var map = MapAsset(id);
        var paths = TerrainKit.RiverPaths(map);
        for (var i = 0; i < map.Bridges.Length; i++) {
          // 앞 6칸은 다리 입구라 다리 위를 지난다.
          foreach (var piece in TerrainKit.BarrierPieces(map, i).Skip(6)) {
            foreach (var t in new[] { 0f, 0.5f, 1f }) {
              var point = Vector3.Lerp(piece.From, piece.To, t);
              Assert.GreaterOrEqual(TerrainKit.RiverDistance(paths, new Vector2(point.x, point.z)), TerrainKit.BankClearance, $"{id} 다리 {i}: {point}");
            }
          }
        }
      }
    }

    [Test]
    public void River_MeshStopsAtTheLandEdge() {
      foreach (var id in RiverMaps) {
        var map = MapAsset(id);
        var land = TerrainKit.Land(map);
        var kit = new MeshKit();
        TerrainKit.Features(kit, map);
        foreach (var vertex in kit.ToMesh("test").vertices) {
          Assert.GreaterOrEqual(vertex.x, land.xMin - 0.01f, id);
          Assert.LessOrEqual(vertex.x, land.xMax + 0.01f, id);
          Assert.GreaterOrEqual(vertex.z, land.yMin - 0.01f, id);
          Assert.LessOrEqual(vertex.z, land.yMax + 0.01f, id);
        }
      }
    }

    [Test]
    public void Barrier_LampIsRedAndOrangeStaysForFire() {
      var on = ArtPalette.EmissionPixels(true);
      var red = on[(int)Swatch.GlowRed];
      Assert.Greater(red.r, 200);
      Assert.Less(red.g, 60, "경고는 적색");
      Assert.Less(red.b, 60);
      Assert.Greater(on[(int)Swatch.GlowOrange].g, red.g, "주황과 구분");
    }

    [Test]
    public void Barrier_BlinkGetsFasterForLastTenSeconds() {
      Assert.Greater(WorldView.BlinkPeriod(11f), WorldView.BlinkPeriod(10f) * 2f);
      Assert.AreEqual(WorldView.BlinkPeriod(10f), WorldView.BlinkPeriod(1f));
    }

    [UnityTest]
    public IEnumerator Barrier_SinksAndWarnsWhenTruceEnds() {
      yield return StartMatch(Map2PScene, 2, bots: new[] { BotDifficulty.Normal }, seed: 5, truce: 20);
      yield return WaitForHud();
      yield return WaitForBattle();
      var world = Battle().World;
      Assert.AreEqual(4, world.BarrierCount, "다리마다");
      Assert.AreEqual(0f, world.BarrierDrop);
      Assert.IsTrue(world.BarrierVisible);
      yield return WaitFor(BarrierOpen, 30f, "휴전 종료");
      yield return null;
      yield return null;
      Assert.AreEqual(Texts.BarrierAlert, Controller().Hud.AlertText);
      Assert.AreEqual("전쟁 시작 - 방어 울타리가 해제됩니다!", Texts.BarrierAlert);
      yield return WaitFor(() => world.BarrierDrop >= 1f, 20f, "하강");
      yield return null;
      Assert.IsFalse(world.BarrierVisible, "내려가면 비활성화");
    }

    [UnityTest]
    public IEnumerator Barrier_IsGoneFromTheStartWithoutTruce() {
      yield return StartMatch(Map2PScene, 2, bots: new[] { BotDifficulty.Normal, BotDifficulty.Normal }, seed: 5, truce: 0);
      yield return WaitForBattle();
      Assert.IsFalse(Battle().World.BarrierVisible);
    }
  }
}
