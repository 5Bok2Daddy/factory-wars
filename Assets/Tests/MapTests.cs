namespace FactoryWars.Tests {
  using System.Collections;
  using System.Collections.Generic;
  using System.Globalization;
  using System.IO;
  using System.Linq;
  using NUnit.Framework;
  using Assert = NUnit.Framework.Assert;
  using Photon.Deterministic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.TestTools;

  /// <summary>항목 17의 맵 규칙. 측정값은 Game-logs/maps/measure.csv에 남겨 보고서에 옮긴다.</summary>
  public unsafe partial class LocalMatchTests {
    const float Gap = 2f;
    const float UsageReach = 14f;
    const float UsageStep = 8f;
    const float MeasureTimeScale = 8f;

    static void Measure(string name, string value) {
      var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Game-logs", "maps"));
      Directory.CreateDirectory(folder);
      File.AppendAllLines(Path.Combine(folder, "measure.csv"), new[] { $"{name},{value}" });
      Debug.Log($"[FW-TEST] {name}: {value}");
    }

    static string T(float value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary>전차들을 동시에 보내 각각 도착까지 걸린 시뮬레이션 초를 잰다.</summary>
    IEnumerator TimeRoutes((EntityRef Unit, int Player, FPVector3 Target)[] routes, float[] seconds) {
      var startFrame = Verified.Number;
      for (var i = 0; i < routes.Length; i++) {
        Game.SendCommand(routes[i].Player, new MoveUnitsCommand { Units = new[] { routes[i].Unit }, Target = routes[i].Target });
        seconds[i] = -1f;
      }
      Time.timeScale = MeasureTimeScale;
      yield return WaitFor(() => {
        for (var i = 0; i < routes.Length; i++) {
          if (seconds[i] < 0f && (Verified.Get<Transform3D>(routes[i].Unit).Position - routes[i].Target).Magnitude < 3) {
            seconds[i] = (Verified.Number - startFrame) / (float)Fps;
          }
        }
        return seconds.All(s => s >= 0f);
      }, 200f, "모든 경로 도착");
      Time.timeScale = 1f;
    }

    EntityRef TankNear(int player, FPVector3 position) {
      return UnitsNamed(player, player % 2 == 0 ? "ff_eagle_tank" : "cf_dragonscale_tank").OrderBy(e => (Verified.Get<Transform3D>(e).Position - position).SqrMagnitude.AsFloat).First();
    }

    static IEnumerable<MapShape> Content(MapSpec spec) {
      return spec.ResourceNodes.Select(n => n.Shape).Concat(spec.Terrain.Select(t => t.Shape)).Concat(spec.BaseAreas)
        .Concat(spec.Roads).Concat(spec.Fords).Concat(spec.HighGrounds);
    }

    /// <summary>경계 안 8m 격자점 중 지형·자원·본진·도로에서 14m 안에 있는 비율.</summary>
    static float UsedArea(MapSpec spec) {
      var shapes = Content(spec).ToArray();
      var min = spec.BoundsMin.X.AsFloat;
      var max = spec.BoundsMax.X.AsFloat;
      var total = 0;
      var used = 0;
      for (var x = min + UsageStep / 2; x < max; x += UsageStep) {
        for (var z = min + UsageStep / 2; z < max; z += UsageStep) {
          var point = new FPVector3(FP.FromFloat_UNSAFE(x), FP._0, FP.FromFloat_UNSAFE(z));
          total++;
          used += shapes.Any(s => s.Distance(point).AsFloat <= UsageReach) ? 1 : 0;
        }
      }
      return used / (float)total;
    }

    /// <summary>본진에 사령부 크기 건물이 간격 2m로 몇 채 들어가는지 격자로 센다.</summary>
    static int HeadquartersCapacity(MapSpec spec) {
      var hq = Verified.FindAsset(FactionOf(0).Buildings.First(b => Verified.FindAsset(b).Role == BuildingRole.Headquarters));
      var pitch = hq.FootprintRadius.AsFloat * 2f + Gap;
      var area = spec.BaseAreas[0];
      var reach = area.HalfLength.AsFloat;
      var best = 0;
      // 격자 시작점을 바꿔 가장 많이 들어가는 배치를 쓴다.
      for (var ox = 0f; ox < pitch; ox += 2f) {
        for (var oz = 0f; oz < pitch; oz += 2f) {
          var count = 0;
          for (var x = area.Center.X.AsFloat - reach + ox; x < area.Center.X.AsFloat + reach; x += pitch) {
            for (var z = area.Center.Z.AsFloat - reach + oz; z < area.Center.Z.AsFloat + reach; z += pitch) {
              var point = new FPVector3(FP.FromFloat_UNSAFE(x), FP._0, FP.FromFloat_UNSAFE(z));
              if (area.Distance(point).AsFloat <= -hq.FootprintRadius.AsFloat && ProductionSystem.CanPlace(Verified, hq, point)) {
                count++;
              }
            }
          }
          best = Mathf.Max(best, count);
        }
      }
      return best + 1;
    }

    static void AssertBattlefieldSymmetry(MapSpec spec, int quarterTurns) {
      foreach (var site in spec.CaptureSites) {
        Assert.IsTrue(spec.CaptureSites.Any(s => s.Kind == site.Kind && s.Radius == site.Radius && s.Center == Rotated(site.Center, quarterTurns)), $"점령지 대칭 {site.Center}");
      }
      foreach (var spot in spec.CrateSpots) {
        Assert.IsTrue(spec.CrateSpots.Any(s => s.Group == spot.Group && (s.Seat < 0) == (spot.Seat < 0) && s.Position == Rotated(spot.Position, quarterTurns)), $"상자 대칭 {spot.Position}");
      }
      var barricades = spec.Barricades.Select(b => b.Shape).ToArray();
      foreach (var (name, shapes) in new[] { ("바리케이드", barricades), ("고지대", spec.HighGrounds), ("비탈", spec.Ramps), ("도로", spec.Roads), ("여울", spec.Fords) }) {
        foreach (var shape in shapes) {
          Assert.IsTrue(HasTurned(shapes, shape, quarterTurns), $"{name} 대칭 {shape.Center}");
        }
      }
    }

    /// <summary>도로·여울·비탈·점령지·상자가 모두 걸어서 닿는 곳에 있어야 한다.</summary>
    static void AssertBattlefieldReachable(MapSpec spec) {
      var navMesh = MapNavMesh();
      var mask = NavMeshRegionMask.Default;
      var reachable = ReachableTriangles(navMesh, spec.StartPositions[0]);
      void Check(FPVector3 point, string label) {
        var triangle = navMesh.FindTriangle2D(point, mask, true);
        Assert.IsTrue(triangle >= 0 && reachable.Contains(triangle), $"{label} 연결 {point}");
      }
      foreach (var site in spec.CaptureSites) {
        var ring = site.Center + new FPVector3(site.Radius - FP._2, FP._0, FP._0);
        Check(ring, "점령지");
      }
      foreach (var spot in spec.CrateSpots) {
        Check(spot.Position, "상자");
      }
      foreach (var ground in spec.HighGrounds) {
        Check(ground.Center, "고지대");
      }
      foreach (var ramp in spec.Ramps) {
        Check(ramp.Center, "비탈");
      }
      foreach (var ford in spec.Fords) {
        Check(ford.Center, "여울");
      }
      foreach (var site in spec.Expansions.Where(e => e.Kind != ExpansionKind.Center)) {
        Check(site.Position, "확장 후보");
      }
    }

    /// <summary>능선은 원·사각형 덩어리이고 숲은 원형 군락이다.</summary>
    static void AssertNoSticks(MapSpec spec) {
      foreach (var feature in spec.Terrain.Where(t => t.Kind == TerrainKind.Hill && t.Shape.IsCircle == false)) {
        Assert.GreaterOrEqual(feature.Shape.HalfWidth.AsFloat * 2f, 12f, $"능선 두께 {feature.Shape.Center}");
      }
      foreach (var node in spec.ResourceNodes.Where(n => n.Kind == ResourceKind.Wood)) {
        Assert.IsTrue(node.Shape.IsCircle, $"숲 군락 {node.Shape.Center}");
        Assert.GreaterOrEqual(node.Shape.Radius.AsFloat, 5f);
        Assert.LessOrEqual(node.Shape.Radius.AsFloat, 12f);
      }
    }

    /// <summary>점을 지나는 직선 위에서 지형에 막히지 않는 구간의 폭.</summary>
    static float ClearWidth(MapSpec spec, Vector2 point, Vector2 direction) {
      var shapes = spec.Terrain.Select(t => t.Shape).Concat(spec.ResourceNodes.Select(n => n.Shape)).ToArray();
      var edge = spec.BoundsMax.X.AsFloat;
      bool Free(Vector2 p) {
        var position = new FPVector3(FP.FromFloat_UNSAFE(p.x), FP._0, FP.FromFloat_UNSAFE(p.y));
        return Mathf.Abs(p.x) <= edge && Mathf.Abs(p.y) <= edge && shapes.All(s => s.Distance(position).AsFloat > 0f);
      }
      var unit = direction.normalized;
      Assert.IsTrue(Free(point), $"폭 측정점이 막힘 {point}");
      var low = 0f;
      var high = 0f;
      while (low > -80f && Free(point + unit * (low - 0.1f))) {
        low -= 0.1f;
      }
      while (high < 80f && Free(point + unit * (high + 0.1f))) {
        high += 0.1f;
      }
      return high - low;
    }

    static void AssertWidth(MapSpec spec, string label, Vector2 point, Vector2 direction, float min, float max) {
      var width = ClearWidth(spec, point, direction);
      Measure($"{spec.name} {label}", T(width));
      Assert.GreaterOrEqual(width, min, label);
      Assert.LessOrEqual(width, max, label);
    }

    /// <summary>입구를 막으면 from에서 to로 갈 수 없어야 한다. 입구 말고 샛길이 없다는 뜻이다. 격자 칸 1m로 훑는다.</summary>
    static void AssertSealed(MapSpec spec, Vector2 from, Vector2 to, MapShape[] gates, string label) {
      var shapes = spec.Terrain.Select(t => t.Shape).Concat(spec.ResourceNodes.Select(n => n.Shape)).Concat(gates).ToArray();
      var half = (int)spec.BoundsMax.X.AsFloat;
      var seen = new HashSet<(int, int)>();
      var open = new Stack<(int X, int Z)>();
      bool Free(int x, int z) {
        var point = new FPVector3(x, 0, z);
        foreach (var shape in shapes) {
          if (FPMath.Abs(shape.Center.X - x) <= shape.Reach + 1 && FPMath.Abs(shape.Center.Z - z) <= shape.Reach + 1 && shape.Distance(point) <= FP._0) {
            return false;
          }
        }
        return true;
      }
      var start = ((int)from.x, (int)from.y);
      open.Push(start);
      seen.Add(start);
      while (open.Count > 0) {
        var (x, z) = open.Pop();
        foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }) {
          var next = (x + dx, z + dz);
          if (Mathf.Abs(next.Item1) <= half && Mathf.Abs(next.Item2) <= half && seen.Contains(next) == false && Free(next.Item1, next.Item2)) {
            seen.Add(next);
            open.Push(next);
          }
        }
      }
      Assert.Greater(seen.Count, 200, $"{label}: 출발점이 넓은 땅");
      Assert.IsFalse(seen.Contains(((int)to.x, (int)to.y)), $"{label}: 입구 말고 샛길이 있음");
    }

    static MapShape GateBlock(float x, float z, float halfLength, float halfWidth, float yaw = 0f) {
      return new MapShape { Center = new FPVector3(FP.FromFloat_UNSAFE(x), FP._0, FP.FromFloat_UNSAFE(z)), HalfLength = FP.FromFloat_UNSAFE(halfLength), HalfWidth = FP.FromFloat_UNSAFE(halfWidth), Yaw = FP.FromFloat_UNSAFE(yaw) };
    }

    static void AssertBarricadesBlock(MapSpec spec) {
      var navMesh = MapNavMesh();
      Assert.Greater(spec.Barricades.Length, 0);
      foreach (var barricade in spec.Barricades) {
        Assert.IsTrue(Verified.Map.RegionMap.ContainsKey(barricade.Region), $"영역 {barricade.Region}");
        var along = barricade.Shape;
        var angle = along.Yaw.AsFloat * Mathf.Deg2Rad;
        var across = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * (along.HalfWidth.AsFloat + 5f);
        var center = along.Center.ToUnityVector3();
        var from = (center + across).ToFPVector3();
        var to = (center - across).ToFPVector3();
        Assert.IsFalse(Verified.Navigation.LineOfSight(from, to, *Verified.NavMeshRegionMask, navMesh), $"닫힌 뒷길 {center}");
      }
      Measure("Quantum 영역 한도", Navigation.Constants.MaxRegions.ToString());
      Measure($"{spec.name} 영역 수", Verified.Map.Regions.Length.ToString());
    }

    [UnityTest]
    public IEnumerator Map2P_LayoutAndCrossing() {
      var tanks = new[] {
        (0, "ff_eagle_tank", -100, -57), (0, "ff_eagle_tank", -106, 106), (0, "ff_eagle_tank", -82, -82), (0, "ff_eagle_tank", -78, -82), (0, "ff_eagle_tank", -82, -78), (0, "ff_eagle_tank", -78, -78),
        (1, "cf_dragonscale_tank", 82, 82),
      };
      yield return StartMatch(Map2PScene, 2, units: tanks, truce: 0);
      var spec = Verified.FindAsset<MapSpec>(Verified.Map.UserAsset);
      DumpMap(spec, "map2p");
      Assert.AreEqual(2, spec.StartPositions.Length);
      Assert.AreEqual(new FPVector2(240, 240), spec.BoundsMax - spec.BoundsMin);
      Assert.AreEqual(V(-90, -90), spec.StartPositions[0]);
      Assert.AreEqual(25, Verified.ComponentCount<ResourceNode>());
      Assert.AreEqual(4, NodeCount(ResourceKind.Iron, 4000), "본진 철광산");
      Assert.AreEqual(2, NodeCount(ResourceKind.Wood, 6000), "본진 숲");
      Assert.AreEqual(4, NodeCount(ResourceKind.Iron, 3000), "철 앞마당");
      Assert.AreEqual(2, NodeCount(ResourceKind.Iron, 1500), "숲 앞마당 작은 철");
      Assert.AreEqual(2, NodeCount(ResourceKind.Iron, 5000), "공유 확장 철");
      Assert.AreEqual(4, NodeCount(ResourceKind.Wood, 5000), "숲 앞마당 군락과 공유 확장 숲");
      Assert.AreEqual(2, NodeCount(ResourceKind.Wood, 3000), "숲 앞마당 군락");
      Assert.AreEqual(1, NodeCount(ResourceKind.Iron, 8000), "중앙 대형 철");
      AssertRotationalSymmetry(spec, 2);
      AssertBattlefieldSymmetry(spec, 2);
      AssertEverythingConnected(spec);
      AssertBattlefieldReachable(spec);
      AssertNoSticks(spec);
      AssertBarricadesBlock(spec);
      Assert.AreEqual(3, spec.CaptureSites.Length, "중앙 1 + 공유 확장 2");
      Assert.AreEqual(2, spec.Barricades.Length);
      Assert.AreEqual(4, spec.HighGrounds.Length);
      Assert.AreEqual(2, spec.Fords.Length);
      Assert.AreEqual(8, spec.CrateSpots.Count(s => s.Seat < 0), "투하 후보지");
      Assert.AreEqual(2, spec.CrateSpots.Count(s => s.Seat == 0), "본진 상자");

      var ironGates = new[] { GateBlock(-100, -57, 7, 4), GateBlock(-62, 3, 8, 12), GateBlock(-111, 18, 11, 8) };
      AssertSealed(spec, new Vector2(-96, -30), new Vector2(-20, 10), ironGates, "철 앞마당에서 중앙");
      var relayGates = new[] { GateBlock(-107, 60, 7, 2), GateBlock(-60, 107, 2, 7) };
      AssertSealed(spec, new Vector2(-106, 106), new Vector2(-96, 34), relayGates, "공유 확장은 협곡으로만");
      AssertWidth(spec, "철 앞마당 중앙 입구", new Vector2(-62, 3), Vector2.up, 16f, 20f);
      AssertWidth(spec, "철 앞마당 측면 입구", new Vector2(-111, 18), Vector2.right, 16f, 20f);
      AssertWidth(spec, "숲 앞마당 중앙 입구", new Vector2(3, -62), Vector2.right, 16f, 20f);
      AssertWidth(spec, "숲 앞마당 측면 입구", new Vector2(18, -111), Vector2.up, 16f, 20f);
      AssertWidth(spec, "서쪽 협곡", new Vector2(-107, 60), Vector2.right, 8f, 10.5f);
      AssertWidth(spec, "북쪽 협곡", new Vector2(-60, 107), Vector2.up, 8f, 10.5f);
      AssertWidth(spec, "뒷길 틈", new Vector2(-44, 31), new Vector2(1, 1), 10f, 12f);

      var usage = UsedArea(spec);
      Measure("Map2P 사용 영역", T(usage * 100f));
      Assert.GreaterOrEqual(usage, 0.85f);
      var capacity = HeadquartersCapacity(spec);
      Measure("Map2P 사령부 수용", capacity.ToString());
      Assert.GreaterOrEqual(capacity, 25);

      var seconds = new float[7];
      yield return TimeRoutes(new[] {
        (TankNear(0, V(-100, -57)), 0, V(57, 100)),
        (TankNear(0, V(-106, 106)), 0, V(57, 100)),
        (TankNear(0, V(-82, -82)), 0, V(-96, -30)),
        (TankNear(0, V(-78, -82)), 0, V(-24, -96)),
        (TankNear(0, V(-82, -78)), 0, V(-106, 106)),
        (TankNear(0, V(-78, -78)), 0, V(106, -106)),
        (TankNear(1, V(82, 82)), 1, V(-106, 106)),
      }, seconds);
      Measure("Map2P 중앙 길 초", T(seconds[0]));
      Measure("Map2P 공유 확장에서 상대 다리 초", T(seconds[1]));
      Measure("Map2P 본진에서 철 앞마당 초", T(seconds[2]));
      Measure("Map2P 본진에서 숲 앞마당 초", T(seconds[3]));
      Measure("Map2P 본진에서 북서 공유 확장 초", T(seconds[4]));
      Measure("Map2P 본진에서 남동 공유 확장 초", T(seconds[5]));
      Measure("Map2P 상대 본진에서 북서 공유 확장 초", T(seconds[6]));
      Assert.GreaterOrEqual(seconds[0], 45f, "중앙 길 하한");
      Assert.LessOrEqual(seconds[0], 60f, "중앙 길 상한");
      Assert.GreaterOrEqual(seconds[2], 10f);
      Assert.LessOrEqual(seconds[2], 15f);
      Assert.GreaterOrEqual(seconds[3], 10f);
      Assert.LessOrEqual(seconds[3], 15f);
      Assert.LessOrEqual(Mathf.Abs(seconds[4] - seconds[6]), 2f, "공유 확장 거리 차이");
    }

    [UnityTest]
    public IEnumerator Map2P_SideLaneIsLongerThanCenter() {
      // 측면 길은 공유 확장을 지나므로 다리에서 공유 확장까지와 공유 확장에서 상대 다리까지를 더한다.
      var tanks = new[] { (0, "ff_eagle_tank", -100, -57), (0, "ff_eagle_tank", -106, 106), (0, "ff_eagle_tank", -98, -55) };
      yield return StartMatch(Map2PScene, 2, units: tanks, truce: 0);
      var seconds = new float[3];
      yield return TimeRoutes(new[] {
        (TankNear(0, V(-100, -57)), 0, V(57, 100)),
        (TankNear(0, V(-106, 106)), 0, V(57, 100)),
        (TankNear(0, V(-98, -55)), 0, V(-106, 106)),
      }, seconds);
      var side = seconds[1] + seconds[2];
      Measure("Map2P 측면 길 초", T(side));
      Measure("Map2P 측면 배율", (side / seconds[0]).ToString("0.00", CultureInfo.InvariantCulture));
      Assert.GreaterOrEqual(side / seconds[0], 1.4f);
      Assert.LessOrEqual(side / seconds[0], 1.7f);
    }

    [UnityTest]
    public IEnumerator Map4P_LayoutAndCrossing() {
      var tanks = new[] { (0, "ff_eagle_tank", -140, -97), (0, "ff_eagle_tank", -122, -122), (0, "ff_eagle_tank", -118, -122), (0, "ff_eagle_tank", -122, -118), (0, "ff_eagle_tank", -118, -118), (0, "ff_eagle_tank", -116, -116) };
      yield return StartMatch(Map4PScene, 4, units: tanks, truce: 0);
      var spec = Verified.FindAsset<MapSpec>(Verified.Map.UserAsset);
      DumpMap(spec, "map4p");
      Assert.AreEqual(4, spec.StartPositions.Length);
      Assert.AreEqual(new FPVector2(320, 320), spec.BoundsMax - spec.BoundsMin);
      Assert.AreEqual(4, Verified.ComponentCount<Building>());
      Assert.AreEqual(49, Verified.ComponentCount<ResourceNode>());
      Assert.AreEqual(1, NodeCount(ResourceKind.Iron, 8000), "중앙 대형 철");
      Assert.AreEqual(4, NodeCount(ResourceKind.Iron, 5000), "공유 확장 철");
      Assert.AreEqual(4, NodeCount(ResourceKind.Wood, 2000) - 4, "중앙 숲");
      AssertRotationalSymmetry(spec, 1);
      AssertBattlefieldSymmetry(spec, 1);
      AssertEverythingConnected(spec);
      AssertBattlefieldReachable(spec);
      AssertNoSticks(spec);
      AssertBarricadesBlock(spec);
      Assert.AreEqual(5, spec.CaptureSites.Length, "중앙 1 + 변 4");
      Assert.AreEqual(4, spec.Barricades.Length);
      Assert.AreEqual(4, spec.HighGrounds.Length);
      Assert.AreEqual(4, spec.Fords.Length);
      Assert.AreEqual(8, spec.CrateSpots.Count(s => s.Seat < 0), "투하 후보지");

      var ironGates = new[] { GateBlock(-140, -97, 7, 4), GateBlock(-102, -64, 8, 12), GateBlock(-151, -48, 11, 8) };
      AssertSealed(spec, new Vector2(-136, -72), new Vector2(-60, -40), ironGates, "철 앞마당에서 중앙");
      var relayGates = new[] { GateBlock(-147, -24, 7, 2), GateBlock(-147, 24, 7, 2) };
      AssertSealed(spec, new Vector2(-134, 0), new Vector2(-150, -36), relayGates, "공유 확장은 협곡으로만");
      var ringGates = Enumerable.Range(0, 4).Select(i => GateBlock(i < 2 ? -36 : 36, i % 2 == 0 ? -36 : 36, 13, 5, i == 0 || i == 3 ? 45 : -45)).ToArray();
      AssertSealed(spec, new Vector2(-60, -40), new Vector2(60, 40), ringGates.Concat(new[] { GateBlock(-140, -97, 7, 4), GateBlock(-97, -140, 4, 7), GateBlock(-147, -24, 7, 2), GateBlock(-24, -147, 2, 7) }).Concat(spec.Barricades.Select(b => b.Shape)).ToArray(), "대각선 상대에게는 중앙을 지남");
      AssertWidth(spec, "철 앞마당 중앙 입구", new Vector2(-102, -64), Vector2.up, 16f, 20f);
      AssertWidth(spec, "철 앞마당 측면 입구", new Vector2(-151, -48), Vector2.right, 16f, 20f);
      AssertWidth(spec, "협곡", new Vector2(-147, -24), Vector2.right, 8f, 10.5f);
      AssertWidth(spec, "뒷길 틈", new Vector2(-72, 0), Vector2.right, 10f, 12f);
      AssertWidth(spec, "중앙 고리 입구", new Vector2(-36, -36), new Vector2(1, -1), 14f, 22f);

      var usage = UsedArea(spec);
      Measure("Map4P 사용 영역", T(usage * 100f));
      Assert.GreaterOrEqual(usage, 0.85f);
      var capacity = HeadquartersCapacity(spec);
      Measure("Map4P 사령부 수용", capacity.ToString());
      Assert.GreaterOrEqual(capacity, 25);

      var seconds = new float[6];
      yield return TimeRoutes(new[] {
        (TankNear(0, V(-140, -97)), 0, V(97, 140)),
        (TankNear(0, V(-122, -122)), 0, V(-136, -72)),
        (TankNear(0, V(-118, -122)), 0, V(-72, -136)),
        (TankNear(0, V(-122, -118)), 0, V(-134, 0)),
        (TankNear(0, V(-118, -118)), 0, V(0, -134)),
        (TankNear(0, V(-116, -116)), 0, V(-140, 97)),
      }, seconds);
      Measure("Map4P 대각선 상대 다리 초", T(seconds[0]));
      Measure("Map4P 본진에서 철 앞마당 초", T(seconds[1]));
      Measure("Map4P 본진에서 숲 앞마당 초", T(seconds[2]));
      Measure("Map4P 본진에서 서쪽 공유 확장 초", T(seconds[3]));
      Measure("Map4P 본진에서 남쪽 공유 확장 초", T(seconds[4]));
      Measure("Map4P 이웃 다리 초", T(seconds[5]));
      Assert.GreaterOrEqual(seconds[1], 10f);
      Assert.LessOrEqual(seconds[1], 15f);
      Assert.GreaterOrEqual(seconds[2], 10f);
      Assert.LessOrEqual(seconds[2], 15f);
      Assert.LessOrEqual(Mathf.Abs(seconds[3] - seconds[4]), 2f, "공유 확장 거리 차이");
    }
  }
}
