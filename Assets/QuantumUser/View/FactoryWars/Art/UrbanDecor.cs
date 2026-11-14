namespace FactoryWars.View {
  using System.Collections.Generic;
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
      Boundary(map, profile);
      Street(map, profile);
    }

    static void Put(DressKind kind, Vector3 position, int seed, float yaw, string role, VisualProfile profile) {
      if (Dressing.AddKept(kind, position, seed, yaw, profile.UrbanDensityLow, profile.UrbanDensityMedium)) {
        Spots.Add(new Spot { Kind = kind, Position = position, Yaw = yaw, Role = role });
      }
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
          Put(tower ? DressKind.CityTower : DressKind.CityMedium, position, 4400 + n * 7, yaw + (Hash(n, 14) < 0.5f ? 0f : 180f), "boundary", profile);
        }
      }
    }

    /// <summary>길가 장식 소품. 길 옆 땅에 12m마다 확률로 놓고 비스듬히 돌려 엄폐 시설이 아닌 장식임이 보이게 한다.</summary>
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
          var spot = center + turn * new Vector3(x + (Hash(idx, 33) - 0.5f) * 3f, 0f, side * (half.y + CurbGap));
          if (Allowed(map, spot, road) == false) {
            continue;
          }
          var pick = Hash(idx, 34);
          var kind = pick < 0.34f ? DressKind.Container : pick < 0.62f ? DressKind.Barrier : pick < 0.82f ? DressKind.Debris : DressKind.Wreck;
          if (Dressing.Has(kind) == false) {
            continue;
          }
          Put(kind, spot, 5200 + idx * 5, yaw + (Hash(idx, 35) - 0.5f) * 28f + (Hash(idx, 36) < 0.5f ? 0f : 180f), "street", profile);
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
          var center = point + d * (arm.Item3 + 2.4f);
          var width = arm.Item2 - 0.9f;
          for (var lateral = -width + 0.6f; lateral <= width - 0.6f; lateral += 1.5f) {
            var c = center + n * lateral;
            Vector3 P(float along, float across) => new Vector3(c.x + d.x * along + n.x * across, 0.075f, c.y + d.y * along + n.y * across);
            k.Quad(P(-1.4f, -0.35f), P(1.4f, -0.35f), P(1.4f, 0.35f), P(-1.4f, 0.35f), Vector3.up);
          }
        }
      }
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
        // 참고 그림의 밝고 깨끗한 파란 물: 프로필 물 색(WaterColor)에서 얕은 쪽은 밝게, 깊은 쪽은 어둡게.
        var baseColor = profile.WaterColor.a > 0f ? (Color)profile.WaterColor : new Color(0.29f, 0.64f, 0.84f);
        _water.SetColor("_Shallow", Color.Lerp(baseColor, Color.white, 0.28f));
        _water.SetColor("_Deep", baseColor * 0.78f);
      }
      return _water;
    }
  }
}
