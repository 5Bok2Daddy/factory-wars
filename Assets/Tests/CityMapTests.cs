namespace FactoryWars.Tests {
  using System.Collections.Generic;
  using System.Linq;
  using NUnit.Framework;
  using Assert = NUnit.Framework.Assert;
  using Photon.Deterministic;
  using Quantum;
  using UnityEngine;

  /// <summary>도시 배치 규칙. 씬 없이 맵 데이터만으로 격자 연결성을 본다.</summary>
  public partial class LocalMatchTests {
    /// <summary>1m 격자의 통과 가능 칸. 지형·자원·닫힌 도시 건물이 막는다.</summary>
    static bool[] BaseGrid(MapSpec spec, out int size, out float min) {
      min = spec.BoundsMin.X.AsFloat;
      size = (int)(spec.BoundsMax.X.AsFloat - min);
      var free = new bool[size * size];
      var shapes = spec.Terrain.Select(t => t.Shape).Concat(spec.ResourceNodes.Select(n => n.Shape)).Concat(spec.CityBuildings.Select(c => c.Shape))
        .Concat(spec.Barricades.Select(b => b.Shape)).ToArray();
      for (var z = 0; z < size; z++) {
        for (var x = 0; x < size; x++) {
          free[z * size + x] = true;
        }
      }
      foreach (var shape in shapes) {
        Paint(free, size, min, shape, false);
      }
      return free;
    }

    static void Paint(bool[] grid, int size, float min, MapShape shape, bool value) {
      var reach = shape.Reach.AsFloat + 1f;
      var cx = shape.Center.X.AsFloat - min;
      var cz = shape.Center.Z.AsFloat - min;
      for (var z = Mathf.Max(0, (int)(cz - reach)); z < Mathf.Min(size, (int)(cz + reach) + 1); z++) {
        for (var x = Mathf.Max(0, (int)(cx - reach)); x < Mathf.Min(size, (int)(cx + reach) + 1); x++) {
          var point = new FPVector3(FP.FromFloat_UNSAFE(x + min + 0.5f), FP._0, FP.FromFloat_UNSAFE(z + min + 0.5f));
          if (shape.Distance(point) <= FP._0) {
            grid[z * size + x] = value;
          }
        }
      }
    }

    static HashSet<int> Flood(bool[] free, int size, int start) {
      var seen = new HashSet<int> { start };
      var open = new Stack<int>();
      open.Push(start);
      while (open.Count > 0) {
        var cell = open.Pop();
        var x = cell % size;
        var z = cell / size;
        foreach (var next in new[] { x > 0 ? cell - 1 : -1, x < size - 1 ? cell + 1 : -1, z > 0 ? cell - size : -1, z < size - 1 ? cell + size : -1 }) {
          if (next >= 0 && free[next] && seen.Add(next)) {
            open.Push(next);
          }
        }
      }
      return seen;
    }

    static int Nearest(bool[] free, int size, float min, Vector2 point) {
      var cx = (int)(point.x - min);
      var cz = (int)(point.y - min);
      for (var r = 0; r < 20; r++) {
        for (var dz = -r; dz <= r; dz++) {
          for (var dx = -r; dx <= r; dx++) {
            var x = cx + dx;
            var z = cz + dz;
            if (x >= 0 && z >= 0 && x < size && z < size && free[z * size + x]) {
              return z * size + x;
            }
          }
        }
      }
      return -1;
    }

    /// <summary>쓰러진 건물 조합마다 본진·앞마당·공유 확장·점령지가 모두 이어져 있어야 한다.</summary>
    static void AssertConnectedAfterTopples(MapSpec spec, string id) {
      var grid = BaseGrid(spec, out var size, out var min);
      var topplers = Enumerable.Range(0, spec.CityBuildings.Length).Where(i => spec.CityBuildings[i].Topples).ToArray();
      var targets = spec.StartPositions.Select(p => new Vector2(p.X.AsFloat, p.Z.AsFloat))
        .Concat(spec.Expansions.Where(e => e.Kind != ExpansionKind.Center).Select(e => new Vector2(e.Position.X.AsFloat, e.Position.Z.AsFloat)))
        .Concat(spec.CaptureSites.Select(c => new Vector2(c.Center.X.AsFloat + c.Radius.AsFloat - 2f, c.Center.Z.AsFloat))).ToArray();
      var combos = new List<int[]> { new int[0], topplers };
      combos.AddRange(topplers.Select(t => new[] { t }));
      var random = new System.Random(17);
      for (var i = 0; i < 200; i++) {
        combos.Add(topplers.Where(_ => random.Next(2) == 0).ToArray());
      }
      foreach (var combo in combos) {
        var free = (bool[])grid.Clone();
        foreach (var index in combo) {
          Paint(free, size, min, spec.CityBuildings[index].Shape, true);
        }
        foreach (var index in combo) {
          Paint(free, size, min, spec.CityBuildings[index].FallZone, false);
        }
        var start = Nearest(free, size, min, targets[0]);
        var reached = Flood(free, size, start);
        foreach (var target in targets) {
          Assert.IsTrue(reached.Contains(Nearest(free, size, min, target)), $"{id}: 쓰러짐 [{string.Join(",", combo)}]에서 {target}에 못 감");
        }
      }
    }

    [Test]
    public void City_LayoutCountsAndSymmetry() {
      foreach (var (id, players, min, max, topMin, topMax, garMin, garMax) in new[] { ("Map2P", 2, 40, 60, 6, 10, 6, 8), ("Map4P", 4, 80, 120, 12, 20, 12, 16) }) {
        var spec = MapAsset(id);
        var cities = spec.CityBuildings;
        Debug.Log($"[FW-TEST] {id} 도시 건물 {cities.Length}, 쓰러짐 {cities.Count(c => c.Topples)}, 진지 {cities.Count(c => c.Garrison)}, 지름길 {cities.Count(c => c.Shortcut)}");
        Assert.GreaterOrEqual(cities.Length, min, id);
        Assert.LessOrEqual(cities.Length, max, id);
        Assert.GreaterOrEqual(cities.Count(c => c.Topples), topMin, id);
        Assert.LessOrEqual(cities.Count(c => c.Topples), topMax, id);
        Assert.GreaterOrEqual(cities.Count(c => c.Garrison), garMin, id);
        Assert.LessOrEqual(cities.Count(c => c.Garrison), garMax, id);
        Assert.Greater(cities.Count(c => c.Shortcut), 0, id);
        foreach (var city in cities) {
          Assert.IsTrue(HasTurned(cities.Select(c => c.Shape), city.Shape, 4 / players), $"{id} 대칭 {city.Shape.Center}");
        }
      }
    }

    [Test]
    public void City_ToppleCombinationsKeepEverythingConnected() {
      foreach (var id in new[] { "Map2P", "Map4P" }) {
        AssertConnectedAfterTopples(MapAsset(id), id);
      }
    }

    [Test]
    public void City_FallZonesAvoidBridgesEntrancesAndBases() {
      foreach (var id in new[] { "Map2P", "Map4P" }) {
        var spec = MapAsset(id);
        foreach (var city in spec.CityBuildings.Where(c => c.Topples)) {
          var zone = city.FallZone;
          foreach (var shape in spec.Bridges.Concat(spec.BaseAreas).Concat(spec.NoBuildZones)) {
            Assert.Greater(zone.Distance(shape.Center).AsFloat, shape.Reach.AsFloat * 0.5f, $"{id}: 낙하 구역 {zone.Center}이 {shape.Center}를 덮음");
          }
        }
      }
    }
  }
}
