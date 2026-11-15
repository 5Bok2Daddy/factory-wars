namespace FactoryWars.EditorTools {
  using System.Collections.Generic;
  using System.Linq;
  using Photon.Deterministic;
  using Quantum;
  using UnityEngine;

  /// <summary>
  /// 도시 건물 배치(항목 21). 맵을 구울 때 한 번 계산한다. 한 플레이어 몫(2인은 x + z &lt; 0, 4인은 남서 사분면)에서 고르고 회전 대칭으로 복제한다.
  /// 길·자원·점령지·확장 후보·고지대·입구에서 여유를 두고, 도로를 따라 가까운 빈 땅부터 채운다.
  /// </summary>
  public static partial class FactoryWarsSetup {
    const int KindSmall = 0;
    const int KindMedium = 1;
    const int KindLarge = 2;
    const int KindTall = 3;
    const float AlleyWidth = 2.5f;
    const float RoadClearance = 1.5f;
    const float CityStep = 2f;
    const float FallGap = 1f;

    static readonly Vector2[] KindHalf = { new Vector2(3.5f, 3.5f), new Vector2(5, 4.5f), new Vector2(7, 6), new Vector2(3.5f, 3.5f) };

    class CityPlan {
      public int BuildingsPerSeat;
      public int ToppleCandidates;
      public int GarrisonsPerSeat;
      public MapShape[] Shortcuts;
    }

    static readonly Dictionary<string, CityPlan> CityPlans = new Dictionary<string, CityPlan> {
      { "Map4P", new CityPlan { BuildingsPerSeat = 30, ToppleCandidates = 4, GarrisonsPerSeat = 4, Shortcuts = new[] { Rect(-44, 0, 7, 7) } } },
    };

    static MapShape Rotated(MapShape shape, int seat, int players) => Turn(shape, seat * (4 / players));

    static bool InShare(MapDef def, Vector2 p) => p.x < -2f && p.y < -2f;

    static float Circum(MapShape shape) => shape.IsCircle ? shape.Radius.AsFloat : new Vector2(shape.HalfLength.AsFloat, shape.HalfWidth.AsFloat).magnitude;

    static float Gap(MapShape obstacle, Vector2 point) => obstacle.Distance(new FPVector3(FP.FromFloat_UNSAFE(point.x), FP._0, FP.FromFloat_UNSAFE(point.y))).AsFloat;

    /// <summary>건물을 둘 수 없는 것들과 필요한 여유.</summary>
    static List<(MapShape Shape, float Margin, string Name)> CityObstacles(MapSpec spec) {
      var list = new List<(MapShape, float, string)>();
      list.AddRange(spec.Terrain.Select(t => (t.Shape, 1f, "Terrain")));
      list.AddRange(spec.ResourceNodes.Select(n => (n.Shape, 3f, "ResourceNodes")));
      list.AddRange(spec.Roads.Select(r => (r, RoadClearance, "Roads")));
      list.AddRange(spec.BaseAreas.Select(b => (b, 4f, "BaseAreas")));
      list.AddRange(spec.CaptureSites.Select(c => (new MapShape { Center = c.Center, Radius = c.Radius }, 3f, "CaptureSites")));
      list.AddRange(spec.CrateSpots.Select(c => (new MapShape { Center = c.Position, Radius = FP._1 }, 3f, "CrateSpots")));
      list.AddRange(spec.Expansions.Select(e => (new MapShape { Center = e.Position, Radius = FP._1 }, 10f, "Expansions")));
      list.AddRange(spec.HighGrounds.Select(g => (g, 5f, "HighGrounds")));
      list.AddRange(spec.Ramps.Select(r => (r, 6f, "Ramps")));
      list.AddRange(spec.Fords.Select(r => (r, 2f, "Fords")));
      list.AddRange(spec.Barricades.Select(b => (b.Shape, 5f, "Barricades")));
      list.AddRange(spec.NoBuildZones.Select(z => (z, 2f, "NoBuildZones")));
      list.AddRange(spec.Bridges.Select(b => (b, 6f, "Bridges")));
      return list;
    }

    /// <summary>모양 위의 표본점: 중심, 꼭짓점, 변 가운데. 원은 중심과 둘레 여덟 점이다.</summary>
    static IEnumerable<Vector2> Samples(MapShape shape) {
      var c = new Vector2(shape.Center.X.AsFloat, shape.Center.Z.AsFloat);
      yield return c;
      if (shape.IsCircle) {
        for (var i = 0; i < 8; i++) {
          var a = i * Mathf.PI / 4f;
          yield return c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * shape.Radius.AsFloat;
        }
        yield break;
      }
      var yaw = shape.Yaw.AsFloat * Mathf.Deg2Rad;
      var along = new Vector2(Mathf.Cos(yaw), -Mathf.Sin(yaw)) * shape.HalfLength.AsFloat;
      var across = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw)) * shape.HalfWidth.AsFloat;
      for (var i = -1; i <= 1; i++) {
        for (var j = -1; j <= 1; j++) {
          if (i != 0 || j != 0) {
            yield return c + along * i + across * j;
          }
        }
      }
    }

    /// <summary>두 모양 사이의 간격. 서로의 표본점으로 잰다. 겹치면 0 이하다.</summary>
    static float ShapeGap(MapShape a, MapShape b) {
      var best = float.MaxValue;
      foreach (var p in Samples(a)) {
        best = Mathf.Min(best, Gap(b, p));
      }
      foreach (var p in Samples(b)) {
        best = Mathf.Min(best, Gap(a, p));
      }
      return best;
    }

    static readonly Dictionary<string, int> Rejections = new Dictionary<string, int>();

    static void Reject(string reason) => Rejections[reason] = Rejections.TryGetValue(reason, out var n) ? n + 1 : 1;

    static bool Fits(MapSpec spec, List<(MapShape Shape, float Margin, string Name)> obstacles, List<MapShape> placed, MapShape candidate) {
      var p = new Vector2(candidate.Center.X.AsFloat, candidate.Center.Z.AsFloat);
      var r = Circum(candidate);
      var edge = spec.BoundsMax.X.AsFloat - 2f;
      if (Mathf.Abs(p.x) + r > edge || Mathf.Abs(p.y) + r > edge) {
        Reject("경계");
        return false;
      }
      foreach (var (shape, margin, name) in obstacles) {
        if (Gap(shape, p) < r + margin && ShapeGap(shape, candidate) < margin) {
          Reject(name);
          return false;
        }
      }
      foreach (var other in placed) {
        if (Gap(other, p) < r + AlleyWidth && ShapeGap(other, candidate) < AlleyWidth) {
          Reject("다른 건물");
          return false;
        }
      }
      return true;
    }

    /// <summary>가장 가까운 도로 조각. 건물은 도로와 나란히 놓는다.</summary>
    static (float Distance, float Yaw) NearestRoad(MapSpec spec, Vector2 p) {
      var best = float.MaxValue;
      var yaw = 0f;
      foreach (var road in spec.Roads) {
        var distance = Gap(road, p);
        if (distance < best) {
          best = distance;
          yaw = road.Yaw.AsFloat;
        }
      }
      return (best, yaw);
    }

    static void CityLayout(MapDef def, MapSpec spec) {
      // 항목 35: 표준 지형을 쓰는 변형 맵(MapDesert)은 표준 맵의 도시 계획을 그대로 쓴다.
      var planId = def.Id == "MapDesert" ? "Map4P" : def.Id;
      if (CityPlans.TryGetValue(planId, out var plan) == false) {
        spec.CityBuildings = new CityBuildingPlacement[0];
        return;
      }
      var obstacles = CityObstacles(spec);
      var placed = new List<MapShape>();
      var share = new List<CityBuildingPlacement>();
      void Accept(CityBuildingPlacement placement) {
        share.Add(placement);
        for (var seat = 0; seat < def.Players; seat++) {
          placed.Add(Rotated(placement.Shape, seat, def.Players));
          if (placement.Topples) {
            placed.Add(Rotated(placement.FallZone, seat, def.Players));
          }
        }
      }

      // 지름길: 능선의 틈을 막는 건물. 정해진 자리다.
      foreach (var shortcut in plan.Shortcuts) {
        Accept(new CityBuildingPlacement { Shape = shortcut, Kind = KindMedium, Shortcut = true });
      }

      // 쓰러지는 건물: 도로 옆에 세우고 도로를 가로질러 쓰러진다.
      var topplers = 0;
      foreach (var road in spec.Roads) {
        if (topplers >= plan.ToppleCandidates) {
          break;
        }
        var yaw = road.Yaw.AsFloat * Mathf.Deg2Rad;
        var along = new Vector2(Mathf.Cos(yaw), -Mathf.Sin(yaw));
        var normal = new Vector2(-along.y, along.x);
        var half = KindHalf[KindTall];
        var height = 18f;
        var placedHere = false;
        foreach (var (side, t) in new[] { (1f, 0f), (-1f, 0f), (1f, 0.35f), (-1f, 0.35f), (1f, -0.35f), (-1f, -0.35f), (1f, 0.7f), (-1f, 0.7f), (1f, -0.7f), (-1f, -0.7f) }) {
          if (placedHere) {
            break;
          }
          var center = new Vector2(road.Center.X.AsFloat, road.Center.Z.AsFloat) + along * (t * road.HalfLength.AsFloat) + normal * side * (road.HalfWidth.AsFloat + RoadClearance + half.y);
          if (InShare(def, center) == false) {
            continue;
          }
          var shape = Rect(center.x, center.y, half.x, half.y, road.Yaw.AsFloat);
          var fallCenter = center - normal * side * (half.y + FallGap + height / 2);
          var fallYaw = Mathf.Atan2(-(-normal.y * side), -normal.x * side) * Mathf.Rad2Deg;
          var zone = Rect(fallCenter.x, fallCenter.y, height / 2, half.x, fallYaw);
          var roadObstacles = obstacles.Where(o => spec.Roads.Contains(o.Shape) == false).ToList();
          if (Fits(spec, roadObstacles, placed, shape) == false || ZoneIsSafe(spec, zone, placed) == false) {
            continue;
          }
          Accept(new CityBuildingPlacement { Shape = shape, Kind = KindTall, Topples = true, FallZone = zone });
          topplers++;
          placedHere = true;
        }
      }

      // 일반 건물: 도로에 가까운 빈 땅부터 큰 건물을 먼저 시도한다.
      var min = spec.BoundsMin.X.AsFloat;
      var max = spec.BoundsMax.X.AsFloat;
      var candidates = new List<(Vector2 Point, float Road, float Yaw)>();
      for (var x = min + 6f; x < max; x += CityStep) {
        for (var z = min + 6f; z < max; z += CityStep) {
          var p = new Vector2(x, z);
          if (InShare(def, p)) {
            var (distance, yaw) = NearestRoad(spec, p);
            candidates.Add((p, distance, yaw));
          }
        }
      }
      candidates.Sort((a, b) => a.Road != b.Road ? a.Road.CompareTo(b.Road) : a.Point.x != b.Point.x ? a.Point.x.CompareTo(b.Point.x) : a.Point.y.CompareTo(b.Point.y));

      // 도로 옆 자리가 모자라면 빈 땅에서 가까운 도로 쪽으로 쓰러지게 세운다.
      foreach (var candidate in candidates) {
        if (topplers >= plan.ToppleCandidates || candidate.Road > 14f) {
          break;
        }
        var road = spec.Roads.OrderBy(r => Gap(r, candidate.Point)).First();
        var towards = new Vector2(road.Center.X.AsFloat, road.Center.Z.AsFloat) - candidate.Point;
        var yaw = road.Yaw.AsFloat * Mathf.Deg2Rad;
        var along = new Vector2(Mathf.Cos(yaw), -Mathf.Sin(yaw));
        var normal = new Vector2(-along.y, along.x);
        var direction = Vector2.Dot(towards, normal) >= 0f ? normal : -normal;
        var half = KindHalf[KindTall];
        const float Height = 18f;
        var shape = Rect(candidate.Point.x, candidate.Point.y, half.x, half.y, road.Yaw.AsFloat);
        var fallCenter = candidate.Point + direction * (half.y + FallGap + Height / 2);
        var zone = Rect(fallCenter.x, fallCenter.y, Height / 2, half.x, Mathf.Atan2(-direction.y, direction.x) * Mathf.Rad2Deg);
        if (Fits(spec, obstacles, placed, shape) && ZoneIsSafe(spec, zone, placed)) {
          Accept(new CityBuildingPlacement { Shape = shape, Kind = KindTall, Topples = true, FallZone = zone });
          topplers++;
        }
      }
      var filled = 0;
      foreach (var candidate in candidates) {
        if (filled >= plan.BuildingsPerSeat) {
          break;
        }
        foreach (var kind in new[] { KindLarge, KindMedium, KindSmall }) {
          var half = KindHalf[kind];
          var shape = Rect(candidate.Point.x, candidate.Point.y, half.x, half.y, candidate.Yaw);
          if (Fits(spec, obstacles, placed, shape)) {
            Accept(new CityBuildingPlacement { Shape = shape, Kind = kind });
            filled++;
            break;
          }
        }
      }

      // 진지: 앞마당 확장 후보·중앙·공유 확장에 가까운 일반 건물.
      var anchors = spec.Expansions.Where(e => InShare(def, new Vector2(e.Position.X.AsFloat, e.Position.Z.AsFloat)) || e.Kind == ExpansionKind.Center)
        .Select(e => new Vector2(e.Position.X.AsFloat, e.Position.Z.AsFloat)).ToList();
      var chosen = 0;
      for (var round = 0; chosen < plan.GarrisonsPerSeat && round < 3; round++) {
        foreach (var anchor in anchors) {
          if (chosen >= plan.GarrisonsPerSeat) {
            break;
          }
          var best = -1;
          var bestDistance = 60f;
          for (var i = 0; i < share.Count; i++) {
            if (share[i].Garrison || share[i].Topples || share[i].Shortcut || share[i].Kind == KindLarge) {
              continue;
            }
            var distance = (new Vector2(share[i].Shape.Center.X.AsFloat, share[i].Shape.Center.Z.AsFloat) - anchor).magnitude;
            if (distance < bestDistance) {
              bestDistance = distance;
              best = i;
            }
          }
          if (best >= 0) {
            var item = share[best];
            item.Garrison = true;
            share[best] = item;
            chosen++;
          }
        }
      }

      var all = new List<CityBuildingPlacement>();
      for (var seat = 0; seat < def.Players; seat++) {
        foreach (var item in share) {
          var copy = item;
          copy.Shape = Rotated(item.Shape, seat, def.Players);
          copy.FallZone = Rotated(item.FallZone, seat, def.Players);
          all.Add(copy);
        }
      }
      for (var i = 0; i < all.Count; i++) {
        var item = all[i];
        item.Region = $"City{i}";
        item.FallRegion = item.Topples ? $"Fall{i}" : "";
        all[i] = item;
      }
      spec.CityBuildings = all.ToArray();
      Debug.Log($"FactoryWarsCity: {def.Id} 건물 {all.Count}, 쓰러짐 {all.Count(c => c.Topples)}, 진지 {all.Count(c => c.Garrison)}, 탈락 {string.Join(", ", Rejections.Select(r => $"{r.Key} {r.Value}"))}");
      Rejections.Clear();
    }

    /// <summary>낙하 구역이 다리·본진·입구·점령지·확장 후보·상자·비탈·다른 건물을 덮지 않는다.</summary>
    static bool ZoneIsSafe(MapSpec spec, MapShape zone, List<MapShape> placed) {
      bool Touches(MapShape other, float margin) {
        var p = new Vector2(other.Center.X.AsFloat, other.Center.Z.AsFloat);
        return Gap(zone, p) < Circum(other) + margin;
      }
      if (spec.Bridges.Any(b => Touches(b, 6f)) || spec.BaseAreas.Any(b => Touches(b, 2f)) || spec.NoBuildZones.Any(z => Touches(z, 2f))
          || spec.Ramps.Any(r => Touches(r, 4f)) || spec.Barricades.Any(b => Touches(b.Shape, 4f)) || placed.Any(o => Touches(o, 2f))) {
        Reject("낙하: 입구·다리·건물");
        return false;
      }
      var zonePoint = new Vector2(zone.Center.X.AsFloat, zone.Center.Z.AsFloat);
      if (spec.CaptureSites.Any(c => Gap(zone, new Vector2(c.Center.X.AsFloat, c.Center.Z.AsFloat)) < c.Radius.AsFloat + 3f)
          || spec.Expansions.Any(e => Gap(zone, new Vector2(e.Position.X.AsFloat, e.Position.Z.AsFloat)) < 8f)
          || spec.CrateSpots.Any(c => Gap(zone, new Vector2(c.Position.X.AsFloat, c.Position.Z.AsFloat)) < 3f)) {
        Reject("낙하: 점령지·확장·상자");
        return false;
      }
      var edge = spec.BoundsMax.X.AsFloat;
      return Mathf.Abs(zonePoint.x) + Circum(zone) < edge && Mathf.Abs(zonePoint.y) + Circum(zone) < edge;
    }
  }
}
