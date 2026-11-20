namespace FactoryWars.View {
  using System.Collections.Generic;
  using System.Linq;
  using Photon.Deterministic;
  using Quantum;
  using UnityEngine;

  /// <summary>
  /// 항목 36 시가지 장식: 맵 가장자리 건물 외벽, 길가 장식 소품(컨테이너·방벽·잔해·폐차), 교차로 횡단보도, 수로 물 표면.
  /// 화면 전용이다(시뮬레이션·NavMesh·콜라이더·건설 부지는 그대로). 배치는 좌표 해시라 모든 기기에서 같다.
  /// 구입한 팩이 없으면 소품 칸(DressKind)이 비어 있어 아무것도 놓이지 않는다. 값은 VisualProfile 데이터.
  /// </summary>
  public static class UrbanDecor {
    public struct Spot {
      public DressKind Kind;
      public Vector3 Position;
      public float Yaw;
      public string Role;
      public GroundFootprint Footprint;
    }

    /// <summary>마지막으로 놓은 장식 자리(시험용). 높음 품질 기준 전체다.</summary>
    public static readonly List<Spot> Spots = new List<Spot>();

    const float RoadCoverStep = 12f;
    const float CoverChance = 0.42f;
    const float CurbGap = 3.2f;

    static float Hash(int seed, int salt) => MeshKit.Hash(seed, salt);

    static FPVector3 Fp(Vector3 p) => new FPVector3(FP.FromFloat_UNSAFE(p.x), default, FP.FromFloat_UNSAFE(p.z));

    static bool Near(MapShape[] shapes, Vector3 point, float margin) {
      if (shapes == null) {
        return false;
      }
      var p = Fp(point);
      foreach (var shape in shapes) {
        if (shape.Distance(p).AsFloat <= margin) {
          return true;
        }
      }
      return false;
    }

    /// <summary>장식을 놓아도 되는 자리인지: 길 위·본진·건물 부지·자원지·점령지·보급 낙하 자리·다리·물 위는 피한다.</summary>
    public static bool Allowed(MapSpec map, Vector3 point, MapShape? ignoreRoad = null) {
      if (Near(map.BaseAreas, point, 2f) || Near(map.Bridges, point, 3f) || Near(map.NoBuildZones, point, 0f)) {
        return false;
      }
      foreach (var city in map.CityBuildings ?? new CityBuildingPlacement[0]) {
        if (city.Shape.Distance(Fp(point)).AsFloat <= 2.5f) {
          return false;
        }
      }
      foreach (var node in map.ResourceNodes ?? new ResourceNodePlacement[0]) {
        if (node.Shape.Distance(Fp(point)).AsFloat <= 4f) {
          return false;
        }
      }
      foreach (var site in map.CaptureSites ?? new CaptureSitePlacement[0]) {
        if ((new Vector2(site.Center.X.AsFloat, site.Center.Z.AsFloat) - new Vector2(point.x, point.z)).magnitude <= site.Radius.AsFloat + 5f) {
          return false;
        }
      }
      foreach (var spot in map.CrateSpots ?? new CrateSpot[0]) {
        if ((new Vector2(spot.Position.X.AsFloat, spot.Position.Z.AsFloat) - new Vector2(point.x, point.z)).magnitude <= 8f) {
          return false;
        }
      }
      var fp = Fp(point);
      foreach (var road in map.Roads ?? new MapShape[0]) {
        // 길(차선 폭) 위에는 놓지 않는다. 길 자기 자신 옆에 놓는 것은 호출하는 쪽이 간격을 두고 부른다.
        if (ignoreRoad.HasValue && road.Center == ignoreRoad.Value.Center) {
          continue;
        }
        if (road.Distance(fp).AsFloat <= 1.6f) {
          return false;
        }
      }
      return TerrainKit.RiverDistance(TerrainKit.RiverPaths(map), new Vector2(point.x, point.z)) >= 7f;
    }

    /// <summary>모아 둔 개체를 그룹으로 넘기기 전에 호출한다(Dressing.Begin ~ End 사이).</summary>
    public static void Build(MapSpec map) {
      Spots.Clear();
      var profile = VisualProfile.Current;
      if (profile.UrbanDecor == false || Dressing.Collecting == false) {
        return;
      }
      var biome = TerrainKit.BiomeOf(map);
      // 다도해·사막은 수평선까지 열린 바다/사구를 유지하기 위해 외곽 도심 빌딩 벽과 항만 옹벽을 생략한다.
      if (biome != TerrainKit.Biome.Archipelago && biome != TerrainKit.Biome.Desert) {
        Boundary(map, profile);
      }
      Checkpoints(map, profile);
      if (biome != TerrainKit.Biome.Archipelago && biome != TerrainKit.Biome.Desert) {
        Seawall(map, profile);
      }
      Street(map, profile);
      Roadside(map, profile);
    }

    public const float FurnitureStep = 16f;

    /// <summary>
    /// 야전 길가(2026-11-16): 길 양쪽 갓길에 16m마다 가로등·전신주·표지판·쓰레기통/소화전(가끔 버스 정류장)을 길과 나란히,
    /// 32m마다 엄폐(헤스코·방벽·상자·드럼통), 가끔 버려진 차량, 갓길 뒤에는 바위·잡풀·덤불·나무. 화면 전용(판정 없음).
    /// </summary>
    static void Roadside(MapSpec map, VisualProfile profile) {
      if (profile.FieldLook == false || map.Roads == null) {
        return;
      }
      var n = 0;
      var roadNo = 0;
      foreach (var road in map.Roads) {
        roadNo++;
        var yaw = road.Yaw.AsFloat;
        var turn = Quaternion.Euler(0f, yaw, 0f);
        var edge = road.HalfWidth.AsFloat + 0.5f;
        foreach (var (center, half) in TerrainKit.RoadPieces(map, road)) {
          foreach (var side in new[] { 1f, -1f }) {
            var face = yaw + (side > 0f ? 180f : 0f);
            var slot = 0;
            for (var x = -half + 3f + (side > 0f ? 0f : FurnitureStep * 0.5f); x <= half - 3f; x += FurnitureStep, slot++, n++) {
              // 길가 가구: 갓길 1m.
              // 길마다 순서를 밀어 짧은 길에도 가로등·전신주·쓰레기통·표지판이 고루 나온다.
              var cycle = (slot + roadNo * 2 + (side > 0f ? 0 : 3)) % 5;
              var furniture = cycle == 0 ? DressKind.RoadLight : cycle == 1 ? DressKind.RoadPole : cycle == 2 ? DressKind.RoadBin : cycle == 3 ? DressKind.RoadLight : DressKind.RoadSign;
              if (Hash(n, 40) < 0.1f && Dressing.Has(DressKind.RoadBusStop)) {
                furniture = DressKind.RoadBusStop;
              }
              Side(map, road, furniture, center + turn * new Vector3(x, 0f, side * (edge + (furniture == DressKind.RoadBusStop ? 2.2f : 1f))), 6100 + n * 7, face, "roadside", profile);
              // 엄폐: 32m마다 갓길 2.4m(가구와 엇갈려).
              if (slot % 2 == 1) {
                ShoulderProp(DressKind.RoadCover, x + 7f, 3.5f, 6200 + n * 7, yaw + (Hash(n, 41) < 0.5f ? 0f : 180f), "cover");
              }
              // 버려진 차량: 갓길 3.6m, 길에서 15도 안팎 비스듬히.
              if (Hash(n, 42) < 0.28f) {
                ShoulderProp(DressKind.RoadWreck, x - 5f, 3.6f, 6300 + n * 7,
                  yaw + (Hash(n, 43) - 0.5f) * 30f + (Hash(n, 44) < 0.5f ? 0f : 180f), "wreck");
              }
            }
            void ShoulderProp(DressKind kind, float x, float offset, int seed, float heading, string role) {
              for (var row = 0; row < 5; row++) {
                var along = Mathf.Clamp(x + (row % 2 == 0 ? -row : row) * 1.35f, -half + 2f, half - 2f);
                var spot = center + turn * new Vector3(along, 0f, side * (edge + offset + row * 2.3f));
                if (Side(map, road, kind, spot, seed, heading, role, profile)) return;
              }
            }
            // 갓길 뒤 자연: 사막·시가전에는 초록 잔디/덤불/나무를 절대 놓지 않는다.
            var rBiome = TerrainKit.BiomeOf(map);
            if (rBiome != TerrainKit.Biome.Urban) {
              var v = 0;
              for (var x = -half + 2f; x <= half - 2f; x += 5f, v++, n++) {
                var pick = Hash(n, 45);
                var kind = rBiome == TerrainKit.Biome.Desert ? DressKind.RockSmall
                  : pick < 0.38f ? DressKind.Grass : pick < 0.58f ? DressKind.RockSmall : pick < 0.78f ? DressKind.Bush : pick < 0.9f ? DressKind.Broadleaf : DressKind.Conifer;
                if (rBiome == TerrainKit.Biome.Desert && pick > 0.25f) {
                  continue;
                }
                var off = edge + 4.5f + Hash(n, 46) * 3.5f;
                Side(map, road, kind, center + turn * new Vector3(x + (Hash(n, 47) - 0.5f) * 2.5f, 0f, side * off), 6400 + n * 7, Hash(n, 48) * 360f, "verge", profile);
              }
            }
          }
        }
      }
    }

    static bool Side(MapSpec map, MapShape road, DressKind kind, Vector3 spot, int seed, float yaw, string role, VisualProfile profile) {
      if (Dressing.Has(kind) == false || Allowed(map, spot, road) == false) {
        return false;
      }
      foreach (var lot in TerrainKit.Lots) {
        if ((lot.Center - new Vector2(spot.x, spot.z)).magnitude < lot.Radius + 0.5f) {
          return false;
        }
      }
      // 다른 길 아스팔트 위(교차로 근처)는 피한다.
      var fp = Fp(spot);
      foreach (var other in map.Roads) {
        if (other.Center != road.Center && other.Distance(fp).AsFloat <= 1.2f) {
          return false;
        }
      }
      return Put(map, kind, spot, seed, yaw, role, profile);
    }

    public static bool ClearOfProps(GroundFootprint footprint) => Spots.TrueForAll(spot => footprint.Overlaps(spot.Footprint, 0.3f) == false);

    public static bool Fits(MapSpec map, GroundFootprint footprint) {
      if (ClearOfProps(footprint) == false) return false;
      foreach (var road in map.Roads ?? System.Array.Empty<MapShape>())
        if (footprint.Overlaps(road, 0.35f)) return false;
      foreach (var bridge in map.Bridges ?? System.Array.Empty<MapShape>())
        if (footprint.Overlaps(bridge, 0.55f)) return false;
      foreach (var barricade in map.Barricades ?? System.Array.Empty<BarricadePlacement>())
        if (footprint.Overlaps(barricade.Shape, 0.75f)) return false;
      foreach (var city in map.CityBuildings ?? System.Array.Empty<CityBuildingPlacement>())
        if (footprint.Overlaps(city.Shape, 0.55f)) return false;
      foreach (var ground in map.HighGrounds ?? System.Array.Empty<MapShape>())
        if (footprint.Overlaps(ground, 3f)) return false;
      foreach (var terrain in map.Terrain ?? System.Array.Empty<TerrainFeature>())
        if (terrain.Kind != TerrainKind.River && footprint.Overlaps(terrain.Shape, 0.55f)) return false;
      foreach (var other in DressingRenderer.Active?.Group("world") ?? System.Array.Empty<Dressing.Item>())
        if (footprint.Overlaps(other.Footprint, 0.25f)) return false;
      return true;
    }

    static bool Put(MapSpec map, DressKind kind, Vector3 position, int seed, float yaw, string role, VisualProfile profile) {
      var footprint = default(GroundFootprint);
      // 기둥과 난간은 같은 안벽의 결합 파츠다.
      var placed = Dressing.AddKept(kind, position, seed, yaw, profile.UrbanDensityLow, profile.UrbanDensityMedium, bounds => {
        footprint = bounds;
        return role == "quay" || Fits(map, bounds);
      });
      if (placed) {
        Spots.Add(new Spot { Kind = kind, Position = position, Yaw = yaw, Role = role, Footprint = footprint });
      }
      return placed;
    }

    /// <summary>맵 가장자리 바깥에 건물 외벽 열(높은 사무 건물과 낮은 아파트를 섞어). 안쪽은 통과 불가이고 건물 뒷면은 단순해도 된다.</summary>
    static void Boundary(MapSpec map, VisualProfile profile) {
      if (Dressing.Has(DressKind.CityTower) == false) {
        return;
      }
      // 수로가 맵 가장자리를 따라 흐를 수 있어, 강이 그려지는 땅 범위(Land: 경계 + 벽 깊이 + 수로 폭) 바깥에 세운다.
      var land = TerrainKit.Land(map);
      var min = land.min;
      var max = land.max;
      var paths = TerrainKit.RiverPaths(map);
      var n = 0;
      // 변마다 (시작점, 끝점, 바깥 방향, 건물 회전).
      var edges = new[] {
        (new Vector2(min.x, min.y), new Vector2(max.x, min.y), new Vector2(0, -1), 0f),
        (new Vector2(min.x, max.y), new Vector2(max.x, max.y), new Vector2(0, 1), 180f),
        (new Vector2(min.x, min.y), new Vector2(min.x, max.y), new Vector2(-1, 0), 90f),
        (new Vector2(max.x, min.y), new Vector2(max.x, max.y), new Vector2(1, 0), 270f),
      };
      foreach (var (from, to, outward, yaw) in edges) {
        var length = (to - from).magnitude;
        var direction = (to - from) / Mathf.Max(0.01f, length);
        for (var s = 4f; s < length - 2f; s += 12.5f, n++) {
          var tower = Hash(n, 11) < 0.55f;
          var inset = tower ? 11f : 9f;
          var point = from + direction * (s + (Hash(n, 12) - 0.5f) * 3f) + outward * (inset + Hash(n, 13) * 2f);
          var position = new Vector3(point.x, 0f, point.y);
          if (TerrainKit.RiverDistance(paths, point) < 9f) {
            continue;
          }
          Put(map, tower ? DressKind.CityTower : DressKind.CityMedium, position, 4400 + n * 7, yaw + (Hash(n, 14) < 0.5f ? 0f : 180f), "boundary", profile);
        }
      }
    }

    /// <summary>
    /// 야전 룩: 중앙으로 들어가는 길과 다리 입구에 검문소. 길을 가로질러(길과 직각) 콘크리트 방벽·바리케이드를 양쪽 갓길부터 놓고 가운데 좁은 통로를 남긴다.
    /// 화면 전용(판정 없음). 모래주머니·헤스코는 팩에 없어 방벽으로 대신한다.
    /// </summary>
    static void Checkpoints(MapSpec map, VisualProfile profile) {
      if (profile.FieldLook == false || map.Roads == null || Dressing.Has(DressKind.Barrier) == false) {
        return;
      }
      var n = 0;
      foreach (var road in map.Roads) {
        var c = new Vector3(road.Center.X.AsFloat, 0f, road.Center.Z.AsFloat);
        var dir = Quaternion.Euler(0f, road.Yaw.AsFloat, 0f) * Vector3.right;
        var half = road.HalfLength.AsFloat;
        var a = c - dir * half;
        var b = c + dir * half;
        var inner = a.magnitude < b.magnitude ? a : b;
        var outward = (inner == a ? dir : -dir);
        if (inner.magnitude > 70f || half < 14f) {
          continue;
        }
        Gate(map, inner + outward * (map.HasTrader ? Dressing.TraderClearRadius + 8f : 16f), outward, road.HalfWidth.AsFloat, 7700 + n++ * 13, profile);
      }
      foreach (var bridge in map.Bridges ?? new MapShape[0]) {
        var c = new Vector3(bridge.Center.X.AsFloat, 0f, bridge.Center.Z.AsFloat);
        var alongX = bridge.HalfLength.AsFloat >= bridge.HalfWidth.AsFloat;
        var dir = Quaternion.Euler(0f, bridge.Yaw.AsFloat + (alongX ? 0f : 90f), 0f) * Vector3.right;
        var half = Mathf.Max(bridge.HalfLength.AsFloat, bridge.HalfWidth.AsFloat);
        var width = Mathf.Min(bridge.HalfLength.AsFloat, bridge.HalfWidth.AsFloat);
        Gate(map, c + dir * (half + 5f), dir, width, 7900 + n++ * 13, profile);
        Gate(map, c - dir * (half + 5f), -dir, width, 7900 + n++ * 13, profile);
      }
    }

    /// <summary>길 바깥 갓길 양옆에만 검문 방벽 배치(아스팔트 차선 침범 금지).</summary>
    static void Gate(MapSpec map, Vector3 at, Vector3 along, float halfWidth, int seed, VisualProfile profile) {
      var across = new Vector3(-along.z, 0f, along.x);
      var yaw = Mathf.Atan2(across.x, across.z) * Mathf.Rad2Deg;
      foreach (var side in new[] { -1f, 1f }) {
        var offset = side * (halfWidth + 4.8f);
        var spot = at + across * offset;
        if (Dressing.IsOnAnyRoad(spot, 4.2f)) {
          continue;
        }
        Put(map, DressKind.OutpostWall, spot, seed + (side > 0 ? 1 : 0), yaw, "checkpoint", profile);
      }
    }

    /// <summary>길가 장식 소품. 길 바깥 충분한 여유 거리(4.8m)에만 배치해 도로 침범 차단.</summary>
    static void Street(MapSpec map, VisualProfile profile) {
      var idx = 0;
      foreach (var road in map.Roads ?? new MapShape[0]) {
        var center = new Vector3(road.Center.X.AsFloat, 0f, road.Center.Z.AsFloat);
        var yaw = road.Yaw.AsFloat;
        var turn = Quaternion.Euler(0, yaw, 0);
        var half = new Vector2(road.HalfLength.AsFloat, road.HalfWidth.AsFloat);
        for (var x = -half.x + 8f; x <= half.x - 8f; x += RoadCoverStep, idx++) {
          if (Hash(idx, 31) > CoverChance) {
            continue;
          }
          var side = Hash(idx, 32) < 0.5f ? 1f : -1f;
          var along = x + (Hash(idx, 33) - 0.5f) * 3f;
          // 항목 36 시각 교정: 초소형 잔해를 빼고 실제 엄폐가 되는 컨테이너·방벽만 길 방향에 맞춰 놓는다.
          var pick = Hash(idx, 34);
          var kind = pick < 0.5f ? DressKind.Container : DressKind.Barrier;
          if (Dressing.Has(kind) == false) {
            continue;
          }
          for (var row = 0; row < 6; row++) {
            var sideOfRoad = row < 3 ? side : -side;
            var spot = center + turn * new Vector3(along, 0f, sideOfRoad * (half.y + 6.2f + 2.3f * (row % 3)));
            if (Allowed(map, spot, road) && Dressing.IsOnAnyRoad(spot, 5.2f) == false
                && Put(map, kind, spot, 5200 + idx * 5, yaw + (sideOfRoad > 0f ? 0f : 180f), "street", profile)) break;
          }
        }
      }
    }

    /// <summary>길 중심선이 서로 가로지르는 교차로(길 중심선 교점이 두 길의 길이 안에 있는 것).</summary>
    public static List<(Vector2 Point, Vector2 DirA, float HalfA, Vector2 DirB, float HalfB)> Crossings(MapSpec map) {
      var result = new List<(Vector2, Vector2, float, Vector2, float)>();
      var roads = map.Roads ?? new MapShape[0];
      for (var i = 0; i < roads.Length; i++) {
        for (var j = i + 1; j < roads.Length; j++) {
          var a = new Vector2(roads[i].Center.X.AsFloat, roads[i].Center.Z.AsFloat);
          var b = new Vector2(roads[j].Center.X.AsFloat, roads[j].Center.Z.AsFloat);
          var da3 = Quaternion.Euler(0, roads[i].Yaw.AsFloat, 0) * Vector3.right;
          var db3 = Quaternion.Euler(0, roads[j].Yaw.AsFloat, 0) * Vector3.right;
          var da = new Vector2(da3.x, da3.z);
          var db = new Vector2(db3.x, db3.z);
          var cross = da.x * db.y - da.y * db.x;
          if (Mathf.Abs(cross) < 0.5f) {
            continue;
          }
          var diff = b - a;
          var t = (diff.x * db.y - diff.y * db.x) / cross;
          var s = (diff.x * da.y - diff.y * da.x) / cross;
          // 두 길 모두 교점에 닿는다(광장에서 끝이 만나는 길은 8m까지 봐 준다).
          if (Mathf.Abs(t) <= roads[i].HalfLength.AsFloat + 8f && Mathf.Abs(s) <= roads[j].HalfLength.AsFloat + 8f) {
            var point = a + da * t;
            if (Dressing.InTraderClear(new Vector3(point.x, 0f, point.y)) || (map.HasTrader && (point - new Vector2(map.TraderPosition.X.AsFloat, map.TraderPosition.Z.AsFloat)).magnitude < Dressing.TraderClearRadius)) {
              continue;
            }
            if (result.Exists(c => (c.Item1 - point).magnitude < 6f) == false) {
              result.Add((point, da, roads[i].HalfWidth.AsFloat, db, roads[j].HalfWidth.AsFloat));
            }
          }
        }
      }
      return result;
    }

    /// <summary>교차로마다 네 갈래에 횡단보도(흰 줄무늬)를 깐다. 도로 메쉬에 더하는 장식이라 판정이 없다.</summary>
    public static void Crosswalks(MeshKit k, MapSpec map) {
      if (VisualProfile.Current.UrbanDecor == false) {
        return;
      }
      k.Paint(Swatch.LaneWhite);
      foreach (var (point, dirA, halfA, dirB, halfB) in Crossings(map)) {
        foreach (var arm in new[] { (dirA, halfA, halfB), (-dirA, halfA, halfB), (dirB, halfB, halfA), (-dirB, halfB, halfA) }) {
          var d = arm.Item1;
          var n = new Vector2(-d.y, d.x);
          // 그 갈래로 길이 실제로 뻗어 있을 때만(광장 한쪽에 길이 없으면 줄무늬를 깔지 않는다).
          var probe = point + d * (arm.Item3 + 5f);
          var onRoad = false;
          foreach (var road in map.Roads) {
            if (road.Distance(Fp(new Vector3(probe.x, 0f, probe.y))).AsFloat <= 0.3f) {
              onRoad = true;
              break;
            }
          }
          if (onRoad == false) {
            continue;
          }
          var center = point + d * (RadiusAt(map, point, halfA, halfB) + 1.3f);
          var width = arm.Item2 - 0.9f;
          for (var lateral = -width + 0.6f; lateral <= width - 0.6f; lateral += 1.5f) {
            var c = center + n * lateral;
            Vector3 P(float along, float across) => new Vector3(c.x + d.x * along + n.x * across, 0.075f, c.y + d.y * along + n.y * across);
            k.Quad(P(-1.4f, -0.35f), P(1.4f, -0.35f), P(1.4f, 0.35f), P(-1.4f, 0.35f), Vector3.up);
          }
        }
      }
    }

    /// <summary>교차로 광장 패드 반지름: 두 길이 겹치는 사각 모서리(폭 반의 1.41배)를 덮는 4~5.2m.</summary>
    public static float PadRadius(float halfA, float halfB) => Mathf.Clamp(Mathf.Max(halfA, halfB) * 1.45f, 4f, 5.2f);

    const float PlazaY = 0.095f;
    const float SitePlazaRadius = 9f;

    static float RadiusAt(MapSpec map, Vector2 point, float halfA, float halfB) {
      foreach (var site in map.CaptureSites ?? new CaptureSitePlacement[0]) {
        if ((new Vector2(site.Center.X.AsFloat, site.Center.Z.AsFloat) - point).magnitude < 2.5f) {
          return SitePlazaRadius;
        }
      }
      return PadRadius(halfA, halfB);
    }

    /// <summary>
    /// 항목 36 시각 교정 1·2: 교차로 정중앙과 점령 거점 아래에 원형 콘크리트 광장 패드를 도로보다 0.05m 높게 깔아
    /// 두 길의 아스팔트·차선이 같은 높이로 겹쳐 깨지는 자리(Z-fighting)를 덮는다. 가장자리에 낮은 연석 고리. 장식이라 판정이 없다.
    /// </summary>
    public static void Plazas(MeshKit k, MapSpec map) {
      // 야전 룩: 흰 패드 대신 교차로마다 아스팔트 팔각 마개(길보다 높게)로 겹친 길 조각과 차선을 깔끔하게 덮는다.
      if (VisualProfile.Current.FieldLook) {
        foreach (var (point, _, halfA, _, halfB) in Crossings(map)) {
          k.Paint(Swatch.Asphalt);
          k.Disc(new Vector3(point.x, 0.08f, point.y), RadiusAt(map, point, halfA, halfB), 8);
        }
        return;
      }
      if (VisualProfile.Current.UrbanDecor == false) {
        return;
      }
      var centers = new List<(Vector2 Point, float Radius)>();
      foreach (var (point, _, halfA, _, halfB) in Crossings(map)) {
        centers.Add((point, RadiusAt(map, point, halfA, halfB)));
      }
      foreach (var site in (map.CaptureSites ?? new CaptureSitePlacement[0])) {
        var at = new Vector2(site.Center.X.AsFloat, site.Center.Z.AsFloat);
        if (centers.Exists(c => (c.Point - at).magnitude < 2.5f) == false) {
          centers.Add((at, SitePlazaRadius));
        }
      }
      foreach (var (point, radius) in centers) {
        // 바닥(광장 색)과 구분되도록 밝은 회색 패드 + 어두운 연석 고리.
        k.Paint(Swatch.MetalLight);
        k.Disc(new Vector3(point.x, PlazaY, point.y), radius, 40);
        k.Paint(Swatch.Asphalt);
        k.Annulus(new Vector3(point.x, PlazaY + 0.012f, point.y), radius - 0.4f, radius, 40);
      }
    }

    /// <summary>
    /// 항목 36 시각 교정 4: 도시 건물 부지 둘레 보도블록 패드(밝은 회색, 높이 0.15m)와 연석. 건물이 맨바닥 위에 바로 서 보이지 않게 한다.
    /// </summary>
    public static void Sidewalks(MeshKit k, MapSpec map) {
      // 야전 룩(PUBG식, 2026-11-15 사용자 지시): 건물·거점·부두 밑 패드와 횡단보도를 그리지 않는다.
      if (VisualProfile.Current.FieldLook) {
        return;
      }
      if (VisualProfile.Current.UrbanDecor == false) {
        return;
      }
      foreach (var city in map.CityBuildings ?? new CityBuildingPlacement[0]) {
        var shape = city.Shape;
        var center = new Vector3(shape.Center.X.AsFloat, 0f, shape.Center.Z.AsFloat);
        var hx = (shape.IsCircle ? shape.Radius.AsFloat : shape.HalfLength.AsFloat) + 1.8f;
        var hz = (shape.IsCircle ? shape.Radius.AsFloat : shape.HalfWidth.AsFloat) + 1.8f;
        k.Push(center, new Vector3(0, shape.Yaw.AsFloat, 0));
        k.Paint(Swatch.Concrete);
        k.Quad(new Vector3(-hx, 0.15f, -hz), new Vector3(hx, 0.15f, -hz), new Vector3(hx, 0.15f, hz), new Vector3(-hx, 0.15f, hz), Vector3.up);
        k.Paint(Swatch.ConcreteDark);
        k.Quad(new Vector3(-hx, 0f, -hz), new Vector3(hx, 0f, -hz), new Vector3(hx, 0.15f, -hz), new Vector3(-hx, 0.15f, -hz), Vector3.back);
        k.Quad(new Vector3(hx, 0f, hz), new Vector3(-hx, 0f, hz), new Vector3(-hx, 0.15f, hz), new Vector3(hx, 0.15f, hz), Vector3.forward);
        k.Quad(new Vector3(hx, 0f, -hz), new Vector3(hx, 0f, hz), new Vector3(hx, 0.15f, hz), new Vector3(hx, 0.15f, -hz), Vector3.right);
        k.Quad(new Vector3(-hx, 0f, hz), new Vector3(-hx, 0f, -hz), new Vector3(-hx, 0.15f, -hz), new Vector3(-hx, 0.15f, hz), Vector3.left);
        k.Pop();
      }
    }

    /// <summary>
    /// 항목 36 시각 교정(부두 바닥): 외곽 수로와 맞닿는 땅 가장자리 안쪽 4m를 밝은 항만 콘크리트로 덮고, 물가 1m 안쪽에 노란 안전선을 긋는다.
    /// 수로·수문·다리가 지나는 곳은 끊는다. 도로 메쉬에 더하는 장식이라 판정이 없다.
    /// </summary>
    public static void QuayApron(MeshKit k, MapSpec map) {
      // 야전 룩(PUBG식, 2026-11-15 사용자 지시): 건물·거점·부두 밑 패드와 횡단보도를 그리지 않는다.
      if (VisualProfile.Current.FieldLook) {
        return;
      }
      if (VisualProfile.Current.UrbanDecor == false || TerrainKit.Channel(map) <= 0f) {
        return;
      }
      var h = Mathf.Max(map.BoundsMax.X.AsFloat, map.BoundsMax.Y.AsFloat);
      var paths = TerrainKit.RiverPaths(map, false);
      const float Step = 2f;
      const float Width = 4f;
      foreach (var (from, to, inward) in new[] {
        (new Vector2(-h, -h), new Vector2(h, -h), new Vector2(0, 1)), (new Vector2(-h, h), new Vector2(h, h), new Vector2(0, -1)),
        (new Vector2(-h, -h), new Vector2(-h, h), new Vector2(1, 0)), (new Vector2(h, -h), new Vector2(h, h), new Vector2(-1, 0)),
      }) {
        var length = (to - from).magnitude;
        var d = (to - from) / length;
        for (var s = 0f; s < length; s += Step) {
          var a = from + d * s;
          var b = from + d * Mathf.Min(length, s + Step);
          var mid = (a + b) * 0.5f + inward * (Width * 0.5f);
          var mid3 = new Vector3(mid.x, 0f, mid.y);
          if (TerrainKit.RiverDistance(paths, mid) < 8f || Near(map.WaterGates, mid3, 6f) || Near(map.Bridges, mid3, 5f) || Near(map.Roads, mid3, 0.5f)) {
            continue;
          }
          Vector3 P(Vector2 p, float across, float y) => new Vector3(p.x + inward.x * across, y, p.y + inward.y * across);
          k.Paint(Swatch.Concrete);
          k.Quad(P(a, 0f, 0.12f), P(b, 0f, 0.12f), P(b, Width, 0.12f), P(a, Width, 0.12f), Vector3.up);
          k.Paint(Swatch.StripeYellow);
          k.Quad(P(a, 0.8f, 0.13f), P(b, 0.8f, 0.13f), P(b, 1.15f, 0.13f), P(a, 1.15f, 0.13f), Vector3.up);
        }
      }
    }

    /// <summary>
    /// 항목 36 시각 교정 5: 외곽 수로와 땅이 맞닿는 해안 옹벽을 단색 박스로 두지 않고 11m마다 기둥, 그 사이 난간, 6m 간격 계선주를 놓는다.
    /// 수문·다리·강 근처는 건너뛴다. 장식이라 판정이 없다. 컨테이너 크레인은 팩에 없어 놓지 않는다.
    /// </summary>
    static void Seawall(MapSpec map, VisualProfile profile) {
      if (TerrainKit.Channel(map) <= 0f || Dressing.Has(DressKind.QuayPillar) == false) {
        return;
      }
      var h = Mathf.Max(map.BoundsMax.X.AsFloat, map.BoundsMax.Y.AsFloat);
      var paths = TerrainKit.RiverPaths(map, false);
      var n = 0;
      var edges = new[] {
        (new Vector2(-h, -h), new Vector2(h, -h), 0f), (new Vector2(-h, h), new Vector2(h, h), 180f),
        (new Vector2(-h, -h), new Vector2(-h, h), 90f), (new Vector2(h, -h), new Vector2(h, h), 270f),
      };
      foreach (var (from, to, yaw) in edges) {
        var length = (to - from).magnitude;
        var direction = (to - from) / length;
        for (var s = 6f; s < length - 4f; s += 11f, n++) {
          var point = from + direction * s;
          var spot = new Vector3(point.x, 0f, point.y);
          if (TerrainKit.RiverDistance(paths, point) < 9f || Near(map.WaterGates, spot, 7f) || Near(map.Bridges, spot, 6f)) {
            continue;
          }
          Put(map, DressKind.QuayPillar, spot, 6100 + n * 3, yaw, "quay", profile);
          var mid = spot + new Vector3(direction.x, 0f, direction.y) * 5.5f;
          if (VisualProfile.Current.FieldLook == false && s + 11f < length - 4f && Dressing.Has(DressKind.QuayRail)) {
            Put(map, DressKind.QuayRail, mid, 6101 + n * 3, yaw + 90f, "quay", profile);
          }
          if (Dressing.Has(DressKind.QuayBollard)) {
            Put(map, DressKind.QuayBollard, spot + new Vector3(direction.x, 0f, direction.y) * 2.8f, 6102 + n * 3, yaw, "quay", profile);
          }
        }
      }
    }

    /// <summary>
    /// 야전 룩: 다리 양 끝에서 가장 가까운 길까지 흙길을 이어 다리 입구와 길이 끊겨 보이지 않게 한다(화면 전용, 길 판정은 맵 데이터 그대로).
    /// </summary>
    public static void BridgeLinks(MeshKit k, MapSpec map) {
      if (VisualProfile.Current.FieldLook == false || map.Bridges == null || map.Roads == null) {
        return;
      }
      var samples = new List<Vector2>();
      foreach (var road in map.Roads) {
        var c = new Vector2(road.Center.X.AsFloat, road.Center.Z.AsFloat);
        var d3 = Quaternion.Euler(0, road.Yaw.AsFloat, 0) * Vector3.right;
        var d = new Vector2(d3.x, d3.z);
        for (var s = -road.HalfLength.AsFloat; s <= road.HalfLength.AsFloat; s += 2f) {
          samples.Add(c + d * s);
        }
      }
      k.Paint(Swatch.Asphalt);
      var water = TerrainKit.RiverPaths(map);
      var themedBridge = NewBundleArt.UsesBridges(TerrainKit.BiomeOf(map));
      foreach (var bridge in map.Bridges) {
        var c = new Vector2(bridge.Center.X.AsFloat, bridge.Center.Z.AsFloat);
        var halfLong = Mathf.Max(bridge.HalfLength.AsFloat, bridge.HalfWidth.AsFloat);
        var halfShort = Mathf.Min(bridge.HalfLength.AsFloat, bridge.HalfWidth.AsFloat);
        var shortAxis = themedBridge && halfLong <= 10f;
        var alongX = (bridge.HalfLength.AsFloat >= bridge.HalfWidth.AsFloat) != shortAxis;
        var d3 = Quaternion.Euler(0, bridge.Yaw.AsFloat + (alongX ? 0f : 90f), 0) * Vector3.right;
        var d = new Vector2(d3.x, d3.z);
        var half = shortAxis ? halfShort : halfLong;
        var width = (shortAxis ? halfLong : halfShort) * 0.85f;
        foreach (var sign in new[] { -1f, 1f }) {
          var end = c + d * sign * (half + 0.5f);
          var best = Vector2.zero;
          var bestDistance = 45f;
          foreach (var p in samples) {
            var distance = (p - end).magnitude;
            // 다리 바깥쪽(같은 방향)의 길만 잇는다.
            if (distance < bestDistance && Vector2.Dot(p - end, d * sign) > -1f) {
              best = p;
              bestDistance = distance;
            }
          }
          if (bestDistance >= 45f) {
            continue;
          }
          // 다리 축으로 곧게 나온 뒤(직진 구간) 길까지 잇는다.
          var straight = end + d * sign * Mathf.Min(6f, bestDistance * 0.5f);
          Strip(k, water, end, straight, width);
          Strip(k, water, straight, best, width);
        }
      }
    }

    static void Strip(MeshKit k, List<List<TerrainKit.RiverPoint>> water, Vector2 a, Vector2 b, float halfWidth) {
      var dir = b - a;
      if (dir.sqrMagnitude < 0.01f) {
        return;
      }
      var n = new Vector2(-dir.y, dir.x).normalized * halfWidth;
      Vector3 P(Vector2 p) => new Vector3(p.x, 0.07f, p.y);
      var along = Mathf.CeilToInt(dir.magnitude / 1.2f);
      var across = Mathf.Max(1, Mathf.CeilToInt(halfWidth * 2f / 1.2f));
      var edge = new float[across + 1];
      Vector2 Point(int x, int z) => a + dir * ((float)x / along) + n * (2f * z / across - 1f);
      for (var z = 0; z <= across; z++) edge[z] = TerrainKit.RiverDistance(water, Point(0, z));
      for (var x = 0; x < along; x++) {
        var next = TerrainKit.RiverDistance(water, Point(x + 1, 0));
        for (var z = 0; z < across; z++) {
          var upper = TerrainKit.RiverDistance(water, Point(x + 1, z + 1));
          if (edge[z] >= 0f && edge[z + 1] >= 0f && next >= 0f && upper >= 0f)
            k.Quad(P(Point(x, z)), P(Point(x + 1, z)), P(Point(x + 1, z + 1)), P(Point(x, z + 1)), Vector3.up);
          edge[z] = next;
          next = upper;
        }
        edge[across] = next;
      }
      if (TerrainKit.RiverDistance(water, b) > halfWidth)
        k.Disc(new Vector3(b.x, 0.07f, b.y), halfWidth, 12);
    }

    // ---------- 수로 물 표면 ----------

    static Material _water;

    /// <summary>물 표면 공용 머티리얼(우리 셰이더, 인스턴스마다 새로 만들지 않음). 셰이더가 없으면 null.</summary>
    public static Material WaterMaterial() {
      if (_water == null) {
        var shader = Shader.Find("FactoryWars/UrbanWater");
        if (shader == null) {
          return null;
        }
        var profile = VisualProfile.Current;
        _water = new Material(shader) { name = "UrbanWater" };
        // 항목 36 추가: 파스텔 에메랄드. 프로필 값이 있으면 그것, 없으면(옛 프로필) 물색에서 계산한다.
        var baseColor = profile.WaterColor.a > 0f ? (Color)profile.WaterColor : new Color(0.29f, 0.64f, 0.84f);
        _water.SetColor("_Shallow", profile.WaterShallowTint.a > 0f ? (Color)profile.WaterShallowTint : Color.Lerp(baseColor, Color.white, 0.28f));
        _water.SetColor("_Deep", profile.WaterDeepTint.a > 0f ? (Color)profile.WaterDeepTint : baseColor * 0.78f);
        _water.SetColor("_Foam", profile.WaterFoamTint);
        _water.SetFloat("_FoamWidth", profile.WaterFoamWidth);
        _water.SetVector("_Flow", new Vector4(profile.WaterFlowSpeed.x, profile.WaterFlowSpeed.y, 0f, 0f));
      }
      RefreshWater();
      return _water;
    }

    /// <summary>바이옴별 물 색상·거품 폭을 실시간 반영한다(다도해=에메랄드 청록, 해협=짙은 군청 대양, 사막=오아시스 청록, 시가전=항만 슬레이트).</summary>
    public static void ApplyWaterBiome(MapSpec map) {
      var mat = WaterMaterial();
      if (mat == null) {
        return;
      }
      var profile = VisualProfile.Current;
      switch (TerrainKit.BiomeOf(map)) {
        case TerrainKit.Biome.Archipelago:
          mat.SetColor("_Shallow", new Color(0.18f, 0.87f, 0.94f, 1f)); // #2EDEF0
          mat.SetColor("_Deep", new Color(0.04f, 0.49f, 0.66f, 1f));    // #0A7DA8
          mat.SetColor("_Foam", new Color(0.96f, 1f, 1f, 1f));
          mat.SetFloat("_FoamWidth", 2.6f);
          break;
        case TerrainKit.Biome.Strait:
          mat.SetColor("_Shallow", new Color(0.11f, 0.46f, 0.70f, 1f)); // #1C75B3
          mat.SetColor("_Deep", new Color(0.04f, 0.22f, 0.42f, 1f));    // #0A386B
          mat.SetColor("_Foam", new Color(0.88f, 0.95f, 1f, 1f));
          mat.SetFloat("_FoamWidth", 2.0f);
          break;
        case TerrainKit.Biome.Desert:
          mat.SetColor("_Shallow", new Color(0.28f, 0.64f, 0.60f, 1f));
          mat.SetColor("_Deep", new Color(0.15f, 0.42f, 0.41f, 1f));
          mat.SetColor("_Foam", new Color(0.92f, 0.88f, 0.76f, 1f));
          mat.SetFloat("_FoamWidth", 1.4f);
          break;
        case TerrainKit.Biome.Urban:
          mat.SetColor("_Shallow", new Color(0.19f, 0.39f, 0.49f, 1f));
          mat.SetColor("_Deep", new Color(0.10f, 0.25f, 0.34f, 1f));
          mat.SetColor("_Foam", new Color(0.82f, 0.88f, 0.92f, 1f));
          mat.SetFloat("_FoamWidth", 1.4f);
          break;
        default:
          var baseColor = profile.WaterColor.a > 0f ? (Color)profile.WaterColor : new Color(0.29f, 0.64f, 0.84f);
          mat.SetColor("_Shallow", profile.WaterShallowTint.a > 0f ? (Color)profile.WaterShallowTint : Color.Lerp(baseColor, Color.white, 0.28f));
          mat.SetColor("_Deep", profile.WaterDeepTint.a > 0f ? (Color)profile.WaterDeepTint : baseColor * 0.78f);
          mat.SetColor("_Foam", profile.WaterFoamTint);
          mat.SetFloat("_FoamWidth", profile.WaterFoamWidth);
          break;
      }
      RefreshWater();
    }

    /// <summary>깊이 거품은 높음·보통에서만 켠다(낮음은 깊이 텍스처를 읽지 않고 불투명 단색+가장자리 줄무늬). 품질을 바꿀 때도 부른다.</summary>
    public static bool DepthFoamOn => GraphicsQuality.Current != QualityTier.Low;

    public static void RefreshWater() {
      if (_water == null) {
        return;
      }
      if (DepthFoamOn) {
        _water.EnableKeyword("_FW_DEPTH_FOAM");
        _water.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
      } else {
        _water.DisableKeyword("_FW_DEPTH_FOAM");
        _water.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry + 50;
      }
    }
  }
}
