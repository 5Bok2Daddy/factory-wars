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
    const float AlleyWidth = 3.4f;
    const float RoadClearance = 3.8f;
    const float CityStep = 2f;
    const float FallGap = 1f;

    static readonly Vector2[] KindHalf = { new Vector2(4.3f, 4.3f), new Vector2(6.0f, 5.4f), new Vector2(8.2f, 7.2f), new Vector2(4.3f, 4.3f) };

    class CityPlan {
      public int BuildingsPerSeat;
      public int ToppleCandidates;
      public int GarrisonsPerSeat;
      public MapShape[] Shortcuts;
    }

    static readonly Dictionary<string, CityPlan> CityPlans = new Dictionary<string, CityPlan> {
      // 표준 맵(2026-11-22 재구성): 공장·창고 줄은 길 사이 빈 땅에만, 지름길 건물은 없다(중앙 고리 운하 자리).
      { "Map4P", new CityPlan { BuildingsPerSeat = 16, ToppleCandidates = 3, GarrisonsPerSeat = 3, Shortcuts = new MapShape[0] } },
      // 사막 보급전은 예전 표준 맵 도시 계획을 그대로 쓴다.
      { "MapDesert", new CityPlan { BuildingsPerSeat = 30, ToppleCandidates = 4, GarrisonsPerSeat = 4, Shortcuts = new[] { Rect(-44, 0, 7, 7) } } },
      { "MapUrban", new CityPlan { BuildingsPerSeat = 62, ToppleCandidates = 6, GarrisonsPerSeat = 6, Shortcuts = new[] { Rect(-44, 0, 7, 7) } } },
      { "MapArchipelago", new CityPlan { BuildingsPerSeat = 12, ToppleCandidates = 2, GarrisonsPerSeat = 3, Shortcuts = new MapShape[0] } },
      { "MapStrait", new CityPlan { BuildingsPerSeat = 14, ToppleCandidates = 2, GarrisonsPerSeat = 3, Shortcuts = new MapShape[0] } },
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
      // 중앙 암시장 구역(철조망 반지름 10m + 경비 공간)에는 도시 건물을 두지 않는다.
      if (spec.HasTrader) {
        list.Add((new MapShape { Center = spec.TraderPosition, Radius = FP.FromFloat_UNSAFE(22f) }, 3f, "Trader"));
      }
      list.AddRange(spec.CrateSpots.Select(c => (new MapShape { Center = c.Position, Radius = FP._1 }, 3f, "CrateSpots")));
      list.AddRange(spec.Expansions.Select(e => (new MapShape { Center = e.Position, Radius = FP._1 }, 10f, "Expansions")));
      list.AddRange(spec.HighGrounds.Select(g => (g, 5f, "HighGrounds")));
      list.AddRange(spec.Ramps.Select(r => (r, 6f, "Ramps")));
      list.AddRange(spec.Fords.Select(r => (r, 2f, "Fords")));
      list.AddRange(spec.Barricades.Select(b => (b.Shape, 5f, "Barricades")));
      list.AddRange(spec.NoBuildZones.Select(z => (z, 2f, "NoBuildZones")));
      list.AddRange(spec.Bridges.Select(b => (b, 6f, "Bridges")));
      if (spec.Theme == "다도해" || spec.Theme == "해협") {
        list.AddRange((spec.River ?? new MapShape[0]).Select(r => (r, 4f, "River")));
      }
      // 3단 표준 맵: 운하는 90도 대칭이 아니므로 네 회전 원상을 모두 피한다(어느 자리 복사본도 물에 빠지지 않는다). 테라스·관문·참호도 피한다.
      if (spec.Tiered) {
        foreach (var river in spec.River ?? new MapShape[0]) {
          for (var turn = 0; turn < 4; turn++) {
            list.Add((Turn(river, turn), 5f, "River"));
          }
        }
        list.AddRange((spec.MidGrounds ?? new MapShape[0]).Select(m => (m, 4f, "Terrace")));
        list.AddRange((spec.TruceGates ?? new MapShape[0]).Select(g => (g, 6f, "Gate")));
        list.AddRange((spec.Trenches ?? new MapShape[0]).Select(t => (t, 3f, "Trench")));
        // 채집 길: 자원 둘레 10m는 비운다(공병이 건물을 돌아가지 않게).
        list.AddRange(spec.ResourceNodes.Select(n => (n.Shape, 10f, "Harvest")));
        list.Add((new MapShape { Center = FPVector3.Zero, Radius = FP.FromFloat_UNSAFE(62f) }, 4f, "Island"));
      }
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
        var reach = shape.Reach.AsFloat + r + margin;
        if (Mathf.Abs(shape.Center.X.AsFloat - p.x) >= reach || Mathf.Abs(shape.Center.Z.AsFloat - p.y) >= reach) continue;
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
      // 맵마다 자체 도시 계획(사막 보급전은 예전 표준 맵 계획과 같은 값을 따로 가진다).
      var planId = def.Id;
      // 칸 지도 맵: ExplicitCityBuildings가 있으면 T45 파괴 가능 도시 건물로 그대로 사용하고, 없으면 비운다.
      if (def.Explicit) {
        spec.CityBuildings = def.ExplicitCityBuildings ?? new CityBuildingPlacement[0];
        if (spec.CityBuildings.Length > 0) {
          Debug.Log($"FactoryWarsCity: {def.Id} T45 명시 건물 {spec.CityBuildings.Length}, 진지 {spec.CityBuildings.Count(c => c.Garrison)}");
        }
        return;
      }
      if (CityPlans.TryGetValue(planId, out var plan) == false) {
        spec.CityBuildings = new CityBuildingPlacement[0];
        return;
      }
      var obstacles = CityObstacles(spec);
      var withoutRoads = obstacles.Where(item => item.Name != "Roads").ToList();
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
          if (Fits(spec, withoutRoads, placed, shape) == false || ZoneIsSafe(spec, zone, placed) == false) {
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
          // 비대칭 남북 해협(MapStrait) 또는 다도해(MapArchipelago)에서 회전 복제 시 물/해협/교량 위로 떨어지는 건물을 차단한다.
          if (def.Id == "MapStrait" && Mathf.Abs(copy.Shape.Center.X.AsFloat) < 36f) {
            continue;
          }
          if ((def.Id == "MapStrait" || def.Id == "MapArchipelago" || spec.Tiered) && (spec.River ?? new MapShape[0]).Any(r => r.Distance(copy.Shape.Center).AsFloat < 5f)) {
            continue;
          }
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
