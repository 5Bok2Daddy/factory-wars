namespace FactoryWars.View {
  using System.Collections.Generic;
  using Photon.Deterministic;
  using Quantum;
  using UnityEngine;

  /// <summary>
  /// 3단 표준 맵(MapSpec.Tiered, 2026-11-22 참고 그림 재구성) 화면. 판정은 2D 그대로이고 높이·색·장식만 화면에서 만든다.
  /// 저지대 = 운하(물, 기존 물 그리기)와 참호(어두운 흙 바닥 + 모래주머니 둑), 중지대 = 길·통로(0m)와 본진 콘크리트 테라스(+2m),
  /// 고지대 = 능선 대지(+3.5m, 은폐). 층 사이는 비탈로만 잇는다. 테라스 옹벽·중앙 단지 담장·맵 둘레 띠는 콘크리트.
  /// 장식 테마는 VisualProfile.DecorTheme(시가지·산업). 숲 테마 칸은 비워 둔다.
  /// </summary>
  public static class TieredTerrain {
    public const float MainHeight = 3.5f;
    public const float MidHeight = 2f;
    const float PerimeterWidth = 16f;
    const float PerimeterHeight = 6f;

    public static readonly Color TerraceColor = new Color(0.60f, 0.59f, 0.56f);
    public static readonly Color RidgeColor = new Color(0.47f, 0.44f, 0.38f);
    public static readonly Color RampColor = new Color(0.36f, 0.36f, 0.37f);
    public static readonly Color LaneColor = new Color(0.47f, 0.44f, 0.39f);
    public static readonly Color YardColor = new Color(0.40f, 0.39f, 0.37f);
    public static readonly Color BankColor = new Color(0.69f, 0.63f, 0.50f);
    public static readonly Color IslandColor = new Color(0.52f, 0.52f, 0.50f);
    public static readonly Color TrenchColor = new Color(0.27f, 0.22f, 0.17f);
    public static readonly Color ConcreteFace = new Color(0.55f, 0.55f, 0.53f);
    public static readonly Color ConcreteTop = new Color(0.68f, 0.68f, 0.66f);
    public static readonly Color RockFace = new Color(0.33f, 0.30f, 0.27f);

    public static bool Uses(MapSpec map) => map != null && map.Tiered;

    static FPVector3 Point(float x, float z) => new FPVector3(FP.FromFloat_UNSAFE(x), FP._0, FP.FromFloat_UNSAFE(z));

    static bool Inside(MapShape[] shapes, FPVector3 p) {
      foreach (var shape in shapes ?? System.Array.Empty<MapShape>()) {
        if (shape.Distance(p) <= FP._0) {
          return true;
        }
      }
      return false;
    }

    /// <summary>화면에서 지상 유닛·건물·자원을 올릴 높이.</summary>
    public static float SurfaceHeight(MapSpec map, float x, float z) {
      var p = Point(x, z);
      if (Inside(map.HighGrounds, p)) {
        return MainHeight;
      }
      foreach (var ramp in map.Ramps ?? System.Array.Empty<MapShape>()) {
        if (ramp.Distance(p) <= FP._0) {
          return SlopeHeight(ramp, map.HighGrounds, MainHeight, x, z);
        }
      }
      if (Inside(map.MidGrounds, p)) {
        return MidHeight;
      }
      foreach (var gate in map.TruceGates ?? System.Array.Empty<MapShape>()) {
        if (gate.Distance(p) <= FP._0) {
          return SlopeHeight(gate, map.MidGrounds, MidHeight, x, z);
        }
      }
      return 0f;
    }

    /// <summary>
    /// 비탈 높이: 위 대지 중심 → 비탈 중심 방향과 가장 가까운 비탈 축(내리막)으로 투영해, 대지의 그쪽 끝(top)에서 비탈 먼 변(bottom)까지 선형으로 내려간다.
    /// 모서리 대각 비탈도 같은 식이라 옹벽 끝 바깥은 대지 높이의 평평한 층계참이 된다.
    /// </summary>
    static float SlopeHeight(MapShape slope, MapShape[] uppers, float top, float x, float z) {
      var upper = Nearest(uppers, slope.Center);
      if (upper.HasValue == false) {
        return 0f;
      }
      var center = slope.Center.ToUnityVector3();
      var yaw = slope.Yaw.AsFloat * Mathf.Deg2Rad;
      var axisL = new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));
      var axisW = new Vector3(-axisL.z, 0f, axisL.x);
      var toward = center - upper.Value.Center.ToUnityVector3();
      toward.y = 0f;
      var alongW = Mathf.Abs(Vector3.Dot(toward, axisW)) >= Mathf.Abs(Vector3.Dot(toward, axisL));
      var down = alongW ? axisW * Mathf.Sign(Vector3.Dot(toward, axisW)) : axisL * Mathf.Sign(Vector3.Dot(toward, axisL));
      var reach = alongW ? slope.HalfWidth.AsFloat : slope.HalfLength.AsFloat;
      var edge = float.MinValue;
      foreach (var corner in Corners(upper.Value, 0f)) {
        edge = Mathf.Max(edge, Vector3.Dot(corner, down));
      }
      var bottom = Vector3.Dot(center, down) + reach;
      var t = Mathf.Clamp01((Vector3.Dot(new Vector3(x, 0f, z), down) - edge) / Mathf.Max(1f, bottom - edge));
      return Mathf.Lerp(top, 0f, t);
    }

    static float DistanceToNearest(MapShape[] shapes, FPVector3 p) {
      var best = float.MaxValue;
      foreach (var shape in shapes ?? System.Array.Empty<MapShape>()) {
        best = Mathf.Min(best, shape.Distance(p).AsFloat);
      }
      return best == float.MaxValue ? 1000f : best;
    }

    /// <summary>저지대 지면 색: 길 사이 흙·자갈 야적장, 운하 둑 모래·자갈, 중앙 섬 콘크리트 마당, 참호 짙은 흙.</summary>
    public static Color GroundColor(MapSpec map, float x, float z) {
      var p = Point(x, z);
      var grain = Mathf.PerlinNoise(x * 0.11f + 3.1f, z * 0.10f - 7.3f) * 0.6f + Mathf.PerlinNoise(x * 0.031f, z * 0.029f) * 0.4f;
      var patch = Mathf.PerlinNoise(x * 0.018f + 11f, z * 0.018f - 5f);
      var color = Color.Lerp(LaneColor, YardColor, Mathf.SmoothStep(0.35f, 0.75f, patch));
      var water = DistanceToNearest(map.River, p);
      if (water < 6f) {
        color = Color.Lerp(BankColor, color, Mathf.SmoothStep(2f, 6f, water));
      } else if (new Vector2(x, z).magnitude < 46f) {
        color = IslandColor;
      }
      if (Inside(map.Trenches, p)) {
        color = TrenchColor;
      }
      color *= 0.93f + grain * 0.14f;
      color.a = 1f;
      return color;
    }

    /// <summary>테라스·능선 대지·비탈·참호 바닥(k)과 옹벽·담장·절벽·둘레 띠(walls). 색은 Tint·TintWalls가 정점마다 칠한다.</summary>
    public static void Build(MeshKit k, MeshKit walls, MapSpec map) {
      foreach (var high in map.HighGrounds ?? System.Array.Empty<MapShape>()) {
        Slab(k, high, MainHeight);
      }
      foreach (var mid in map.MidGrounds ?? System.Array.Empty<MapShape>()) {
        Slab(k, mid, MidHeight);
      }
      foreach (var ramp in map.Ramps ?? System.Array.Empty<MapShape>()) {
        Patch(k, map, ramp);
      }
      foreach (var gate in map.TruceGates ?? System.Array.Empty<MapShape>()) {
        Patch(k, map, gate);
      }
      foreach (var trench in map.Trenches ?? System.Array.Empty<MapShape>()) {
        var corners = Corners(trench, 0f);
        for (var i = 0; i < 4; i++) {
          corners[i].y = 0.05f;
        }
        k.Paint(Swatch.White);
        k.Quad(corners[0], corners[1], corners[2], corners[3], Vector3.up);
      }
      foreach (var feature in map.Terrain ?? System.Array.Empty<TerrainFeature>()) {
        switch (feature.Kind) {
          case TerrainKind.Cliff: Block(walls, feature.Shape, MainHeight + 0.4f, 0.3f); break;
          case TerrainKind.Wall:
            var onTerrace = DistanceToNearest(map.MidGrounds, feature.Shape.Center) < 2.5f;
            Block(walls, feature.Shape, onTerrace ? MidHeight + 1.1f : 2.6f, 0.08f);
            break;
          case TerrainKind.Hill:
          case TerrainKind.CanyonWall: Block(walls, feature.Shape, 4f, 0.5f); break;
        }
      }
      Perimeter(walls, map);
    }

    /// <summary>사각 대지: 윗면 + 네 옆면(땅까지).</summary>
    static void Slab(MeshKit k, MapShape shape, float height) {
      var corners = Corners(shape, 0f);
      for (var i = 0; i < 4; i++) {
        corners[i].y = height;
      }
      k.Paint(Swatch.White);
      k.Quad(corners[0], corners[1], corners[2], corners[3], Vector3.up);
      var middle = (corners[0] + corners[1] + corners[2] + corners[3]) / 4f;
      for (var i = 0; i < 4; i++) {
        var a = corners[i];
        var b = corners[(i + 1) % 4];
        var outward = (a + b) / 2f - middle;
        outward.y = 0f;
        k.Quad(b, a, new Vector3(a.x, 0f, a.z), new Vector3(b.x, 0f, b.z), outward.normalized);
      }
    }

    /// <summary>비탈: 비탈 사각형을 1.5m 격자로 나눠 SurfaceHeight로 높이를 매긴 면 + 둘레 옆면(땅까지). 모서리 대각 비탈도 같다.</summary>
    static void Patch(MeshKit k, MapSpec map, MapShape shape) {
      var c = shape.Center.ToUnityVector3();
      c.y = 0f;
      var yaw = shape.Yaw.AsFloat * Mathf.Deg2Rad;
      var axisL = new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));
      var axisW = new Vector3(-axisL.z, 0f, axisL.x);
      var hl = shape.HalfLength.AsFloat;
      var hw = shape.HalfWidth.AsFloat;
      var nl = Mathf.Max(2, Mathf.CeilToInt(hl * 2f / 1.5f));
      var nw = Mathf.Max(2, Mathf.CeilToInt(hw * 2f / 1.5f));
      Vector3 At(int i, int j) {
        var p = c + axisL * Mathf.Lerp(-hl, hl, i / (float)nl) + axisW * Mathf.Lerp(-hw, hw, j / (float)nw);
        p.y = SurfaceHeight(map, p.x, p.z) + 0.02f;
        return p;
      }
      k.Paint(Swatch.White);
      for (var i = 0; i < nl; i++) {
        for (var j = 0; j < nw; j++) {
          var p00 = At(i, j);
          var p01 = At(i, j + 1);
          var p11 = At(i + 1, j + 1);
          var p10 = At(i + 1, j);
          var normal = Vector3.Cross(p01 - p00, p10 - p00).normalized;
          if (normal.y < 0f) {
            normal = -normal;
          }
          k.Quad(p00, p01, p11, p10, normal);
        }
      }
      void Skirt(int i0, int j0, int i1, int j1, Vector3 outward) {
        var steps = Mathf.Max(Mathf.Abs(i1 - i0), Mathf.Abs(j1 - j0));
        for (var s = 0; s < steps; s++) {
          var a = At(i0 + (i1 - i0) * s / steps, j0 + (j1 - j0) * s / steps);
          var b = At(i0 + (i1 - i0) * (s + 1) / steps, j0 + (j1 - j0) * (s + 1) / steps);
          if (a.y < 0.05f && b.y < 0.05f) {
            continue;
          }
          k.Quad(b, a, new Vector3(a.x, 0f, a.z), new Vector3(b.x, 0f, b.z), outward);
        }
      }
      Skirt(0, 0, nl, 0, -axisW);
      Skirt(0, nw, nl, nw, axisW);
      Skirt(0, 0, 0, nw, -axisL);
      Skirt(nl, 0, nl, nw, axisL);
    }

    /// <summary>반듯한 블록(윗면 모서리만 살짝 깎는다). 판정 모양보다 0.1m 작게 그려 통로 폭이 보이는 그대로다.</summary>
    static void Block(MeshKit k, MapShape shape, float height, float bevel) {
      var corners = Corners(shape, -0.1f);
      var center = (corners[0] + corners[1] + corners[2] + corners[3]) / 4f;
      var tops = new Vector3[4];
      for (var i = 0; i < 4; i++) {
        var inward = center - corners[i];
        inward.y = 0f;
        tops[i] = corners[i] + inward.normalized * bevel + Vector3.up * height;
      }
      k.Paint(Swatch.White);
      k.Quad(tops[0], tops[1], tops[2], tops[3], Vector3.up);
      for (var i = 0; i < 4; i++) {
        var a = corners[i];
        var b = corners[(i + 1) % 4];
        var outward = (a + b) / 2f - center;
        outward.y = 0f;
        k.Quad(tops[(i + 1) % 4], tops[i], a, b, outward.normalized);
      }
    }

    /// <summary>맵 바깥 둘레: 콘크리트 방벽 띠(안쪽 벽 + 윗면). 띠 위에는 Decor가 공장·창고 정면을 세운다.</summary>
    static void Perimeter(MeshKit k, MapSpec map) {
      var min = new Vector2(map.BoundsMin.X.AsFloat, map.BoundsMin.Y.AsFloat);
      var max = new Vector2(map.BoundsMax.X.AsFloat, map.BoundsMax.Y.AsFloat);
      void Band(Vector2 from, Vector2 to, Vector2 outward) {
        var a = new Vector3(from.x, 0f, from.y);
        var b = new Vector3(to.x, 0f, to.y);
        var o = new Vector3(outward.x, 0f, outward.y);
        var lift = Vector3.up * PerimeterHeight;
        k.Paint(Swatch.White);
        k.Quad(b + lift + o * 0.6f, a + lift + o * 0.6f, a, b, -o);
        k.Quad(a + lift + o * 0.6f, b + lift + o * 0.6f, b + lift + o * PerimeterWidth, a + lift + o * PerimeterWidth, Vector3.up);
      }
      var pad = PerimeterWidth;
      Band(new Vector2(min.x - pad, min.y), new Vector2(max.x + pad, min.y), Vector2.down);
      Band(new Vector2(max.x + pad, max.y), new Vector2(min.x - pad, max.y), Vector2.up);
      Band(new Vector2(min.x, max.y), new Vector2(min.x, min.y), Vector2.left);
      Band(new Vector2(max.x, min.y), new Vector2(max.x, max.y), Vector2.right);
    }

    static Vector3[] Corners(MapShape shape, float grow) {
      var c = shape.Center.ToUnityVector3();
      c.y = 0f;
      var yaw = shape.Yaw.AsFloat * Mathf.Deg2Rad;
      var axisL = new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));
      var axisW = new Vector3(-axisL.z, 0f, axisL.x);
      var hl = shape.IsCircle ? shape.Radius.AsFloat : shape.HalfLength.AsFloat;
      var hw = shape.IsCircle ? shape.Radius.AsFloat : shape.HalfWidth.AsFloat;
      hl += grow;
      hw += grow;
      return new[] {
        c - axisL * hl - axisW * hw,
        c - axisL * hl + axisW * hw,
        c + axisL * hl + axisW * hw,
        c + axisL * hl - axisW * hw,
      };
    }

    static Color Grain(Color color, Vector3 v) {
      var grain = Mathf.PerlinNoise(v.x * 0.13f + 1.7f, v.z * 0.12f + 4.2f);
      color *= 0.94f + grain * 0.12f;
      color.a = 1f;
      return color;
    }

    /// <summary>바닥 정점 색: 테라스 윗면(콘크리트), 능선 윗면(흙빛 바위), 비탈(아스팔트), 테라스 옆면(콘크리트 옹벽), 능선 옆면(바위), 참호(짙은 흙).</summary>
    public static void Tint(Mesh mesh, MapSpec map) {
      var vertices = mesh.vertices;
      var normals = mesh.normals;
      var colors = new Color[vertices.Length];
      var linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
      for (var i = 0; i < vertices.Length; i++) {
        var v = vertices[i];
        var n = normals.Length > i ? normals[i] : Vector3.up;
        Color color;
        if (n.y < 0.5f) {
          color = DistanceToNearest(map.HighGrounds, Point(v.x, v.z)) < 0.6f ? RockFace : ConcreteFace;
        } else if (n.y < 0.995f) {
          color = RampColor;
        } else if (v.y > MainHeight - 0.2f) {
          color = RidgeColor;
        } else if (v.y > MidHeight - 0.2f) {
          color = TerraceColor;
        } else if (v.y < 0.2f) {
          color = TrenchColor;
        } else {
          color = RampColor;
        }
        color = Grain(color, v);
        colors[i] = linear ? color.linear : color;
      }
      mesh.colors = colors;
    }

    /// <summary>담장 정점 색: 콘크리트(옆면·윗면), 능선 절벽은 바위색.</summary>
    public static void TintWalls(Mesh mesh, MapSpec map) {
      var vertices = mesh.vertices;
      var normals = mesh.normals;
      var colors = new Color[vertices.Length];
      var linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
      for (var i = 0; i < vertices.Length; i++) {
        var v = vertices[i];
        var n = normals.Length > i ? normals[i] : Vector3.up;
        var rock = DistanceToNearest(map.HighGrounds, Point(v.x, v.z)) < 2f;
        var color = rock ? (n.y > 0.5f ? RidgeColor * 0.9f : RockFace) : (n.y > 0.5f ? ConcreteTop : ConcreteFace);
        color = Grain(color, v);
        colors[i] = linear ? color.linear : color;
      }
      mesh.colors = colors;
    }

    /// <summary>휴전 관문 울타리 조각: 관문 사각형의 긴 축을 따라 3m씩. 높이는 비탈 높이.</summary>
    public static List<TerrainKit.FencePiece> GatePieces(MapSpec map, int seat) {
      var pieces = new List<TerrainKit.FencePiece>();
      var gates = map.TruceGates ?? System.Array.Empty<MapShape>();
      var seats = Mathf.Max(1, map.BaseAreas != null ? map.BaseAreas.Length : 1);
      var perSeat = Mathf.Max(1, gates.Length / seats);
      for (var i = seat * perSeat; i < (seat + 1) * perSeat && i < gates.Length; i++) {
        var gate = gates[i];
        var center = gate.Center.ToUnityVector3();
        var yaw = gate.Yaw.AsFloat * Mathf.Deg2Rad;
        var axisL = new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));
        var half = gate.HalfLength.AsFloat - 0.6f;
        var count = Mathf.Max(1, Mathf.CeilToInt(half * 2f / 3f));
        for (var p = 0; p < count; p++) {
          var from = center + axisL * Mathf.Lerp(-half, half, p / (float)count);
          var to = center + axisL * Mathf.Lerp(-half, half, (p + 1) / (float)count);
          from.y = SurfaceHeight(map, from.x, from.z);
          to.y = SurfaceHeight(map, to.x, to.z);
          pieces.Add(new TerrainKit.FencePiece { From = from, To = to });
        }
      }
      return pieces;
    }

    // ---------------------------------------------------------------- 장식(시가지·산업 테마)

    static float Hash(int seed, int salt) => MeshKit.Hash(seed, salt);

    /// <summary>장식 자리가 비어 있는가: 물·지형·자원·점령지·상자·건설 금지·테라스·능선·비탈·관문·참호·도시 건물·확장·다리·길에서 떨어져 있다.</summary>
    static bool Free(MapSpec map, Vector3 position, float radius) {
      var p = Point(position.x, position.z);
      if (Mathf.Abs(position.x) > map.BoundsMax.X.AsFloat - radius - 1f || Mathf.Abs(position.z) > map.BoundsMax.Y.AsFloat - radius - 1f) {
        return false;
      }
      if (DistanceToNearest(map.River, p) < radius + 2.5f || DistanceToNearest(map.MidGrounds, p) < radius + 1f
          || DistanceToNearest(map.HighGrounds, p) < radius + 1.5f || DistanceToNearest(map.Ramps, p) < radius + 3f
          || DistanceToNearest(map.TruceGates, p) < radius + 4f || DistanceToNearest(map.Trenches, p) < radius + 1.5f
          || DistanceToNearest(map.NoBuildZones, p) < radius + 1f || DistanceToNearest(map.Bridges, p) < radius + 5f) {
        return false;
      }
      foreach (var feature in map.Terrain ?? System.Array.Empty<TerrainFeature>()) {
        if (feature.Shape.Distance(p).AsFloat < radius + 1f) return false;
      }
      foreach (var node in map.ResourceNodes ?? System.Array.Empty<ResourceNodePlacement>()) {
        if (node.Shape.Distance(p).AsFloat < radius + 5f) return false;
      }
      foreach (var site in map.CaptureSites ?? System.Array.Empty<CaptureSitePlacement>()) {
        if (Flat(site.Center, position) < site.Radius.AsFloat + radius + 3f) return false;
      }
      foreach (var crate in map.CrateSpots ?? System.Array.Empty<CrateSpot>()) {
        if (Flat(crate.Position, position) < radius + 4f) return false;
      }
      foreach (var site in map.Expansions ?? System.Array.Empty<ExpansionSite>()) {
        if (Flat(site.Position, position) < radius + 12f) return false;
      }
      foreach (var city in map.CityBuildings ?? System.Array.Empty<CityBuildingPlacement>()) {
        if (city.Shape.Distance(p).AsFloat < radius + 2.5f) return false;
        if (city.Topples && city.FallZone.Distance(p).AsFloat < radius + 1f) return false;
      }
      return Dressing.IsOnAnyRoad(position, Dressing.RoadClearance + radius) == false && Dressing.InTraderClear(position) == false;
    }

    static float Flat(FPVector3 a, Vector3 b) => new Vector2(a.X.AsFloat - b.x, a.Z.AsFloat - b.z).magnitude;

    static void Put(DressKind kind, Vector3 position, float yaw, int seed) {
      Dressing.Add(kind, position, seed, null, yaw);
    }

    /// <summary>
    /// 장식: 본진 감시탑·헬기장, 참호 모래주머니 둑, 중앙 단지 문 감시탑, 길 사이 컨테이너 야적장·잔해·방벽 줄, 맵 둘레 공장·창고 정면.
    /// 길·건설 부지·채집 길(자원 둘레)·물 위에는 놓지 않는다. 모두 통과 가능한 화면 장식이다.
    /// </summary>
    public static void Decor(MapSpec map) {
      if (VisualProfile.Current.DecorTheme != MapDecorTheme.Urban || Dressing.Map == null) {
        // 숲 테마 칸은 비어 있다(숲 팩이 없다). 지면·대지·담장만 그린다.
        return;
      }
      var seats = map.StartPositions != null ? map.StartPositions.Length : 0;
      var gates = map.TruceGates ?? System.Array.Empty<MapShape>();
      var perSeat = seats > 0 ? Mathf.Max(1, gates.Length / seats) : 1;
      // 본진: 관문 비탈 양옆 테라스 위 감시탑, 헬기장(건설 금지 원 안).
      for (var g = 0; g < gates.Length; g++) {
        var gate = gates[g];
        var center = gate.Center.ToUnityVector3();
        var terrace = Nearest(map.MidGrounds, gate.Center);
        if (terrace.HasValue == false) {
          continue;
        }
        var inward = terrace.Value.Center.ToUnityVector3() - center;
        inward.y = 0f;
        inward.Normalize();
        var side = Vector3.Cross(Vector3.up, inward).normalized;
        var reach = Mathf.Min(gate.HalfLength.AsFloat, gate.HalfWidth.AsFloat) + 6f;
        foreach (var sign in new[] { -1f, 1f }) {
          var spot = center + inward * reach + side * sign * (gate.HalfLength.AsFloat + 3f);
          spot.y = SurfaceHeight(map, spot.x, spot.z);
          if (Inside(map.MidGrounds, Point(spot.x, spot.z))) {
            Put(DressKind.OutpostTower, spot, Mathf.Atan2(-inward.x, -inward.z) * Mathf.Rad2Deg, 9100 + g * 7 + (sign > 0 ? 1 : 0));
          }
        }
      }
      foreach (var zone in map.NoBuildZones ?? System.Array.Empty<MapShape>()) {
        var center = zone.Center.ToUnityVector3();
        if (zone.IsCircle && Inside(map.MidGrounds, zone.Center)) {
          center.y = MidHeight + 0.02f;
          Put(DressKind.TraderPad, center, 0f, 9300 + (int)(center.x * 3 + center.z));
        }
      }
      // 참호 둑: 양옆을 따라 모래주머니(3.2m 간격).
      var trenches = map.Trenches ?? System.Array.Empty<MapShape>();
      for (var t = 0; t < trenches.Length; t++) {
        var trench = trenches[t];
        var c = trench.Center.ToUnityVector3();
        c.y = 0f;
        var yaw = trench.Yaw.AsFloat;
        var axisL = new Vector3(Mathf.Cos(yaw * Mathf.Deg2Rad), 0f, -Mathf.Sin(yaw * Mathf.Deg2Rad));
        var axisW = new Vector3(-axisL.z, 0f, axisL.x);
        var hl = trench.HalfLength.AsFloat;
        var count = Mathf.Max(2, Mathf.FloorToInt(hl * 2f / 3.2f));
        for (var i = 0; i <= count; i++) {
          foreach (var sign in new[] { -1f, 1f }) {
            var spot = c + axisL * Mathf.Lerp(-hl, hl, i / (float)count) + axisW * sign * (trench.HalfWidth.AsFloat + 0.7f);
            if (Dressing.IsOnAnyRoad(spot, 2.5f) == false && DistanceToNearest(map.River, Point(spot.x, spot.z)) > 3f) {
              Put(DressKind.TraderSandbag, spot, -yaw, 9400 + t * 64 + i * 2 + (sign > 0 ? 1 : 0));
            }
          }
        }
      }
      // 중앙 단지: 담장 문 양옆 감시탑.
      if (map.HasTrader) {
        var trader = map.TraderPosition.ToUnityVector3();
        for (var q = 0; q < 4; q++) {
          var angle = (225f + q * 90f) * Mathf.Deg2Rad;
          var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
          var along = new Vector3(-outward.z, 0f, outward.x);
          foreach (var sign in new[] { -1f, 1f }) {
            var spot = trader + outward * 25.5f + along * sign * 7.5f;
            Put(DressKind.OutpostTower, spot, Mathf.Atan2(outward.x, outward.z) * Mathf.Rad2Deg, 9600 + q * 2 + (sign > 0 ? 1 : 0));
          }
        }
      }
      // 중앙 단지 안쪽 네 변: 컨테이너 야적장(암시장 비움 반지름 밖, 담장 안).
      if (map.HasTrader) {
        var trader = map.TraderPosition.ToUnityVector3();
        for (var q = 0; q < 4; q++) {
          var angle = q * 90f * Mathf.Deg2Rad;
          var inward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
          var along = new Vector3(-inward.z, 0f, inward.x);
          var face = Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg - 90f;
          for (var s = -2; s <= 2; s++) {
            if (s == 0) {
              continue;
            }
            Put(DressKind.Container, trader + inward * 24.2f + along * (s * 3f), face, 9700 + q * 8 + s + 2);
          }
        }
      }
      // 변 가운데 자원지 뒤(맵 가장자리 쪽): 큰 바위 노두(참고 그림의 검은 바위 무더기). 채집 길 밖이다.
      foreach (var node in map.ResourceNodes ?? System.Array.Empty<ResourceNodePlacement>()) {
        if (node.Tier != 3 || node.Kind != ResourceKind.Iron) {
          continue;
        }
        var c = node.Shape.Center.ToUnityVector3();
        var outward = Mathf.Abs(c.x) > Mathf.Abs(c.z) ? new Vector3(Mathf.Sign(c.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(c.z));
        var side = Vector3.Cross(Vector3.up, outward);
        var pivot = new Vector3(outward.x * (map.BoundsMax.X.AsFloat - 5f), 0f, outward.z * (map.BoundsMax.Y.AsFloat - 5f));
        pivot += side * Vector3.Dot(c, side);
        for (var r = -1; r <= 1; r++) {
          Put(DressKind.RockLarge, pivot + side * (r * 6f), r * 50f, 9750 + (int)(c.x + c.z) + r);
        }
      }
      // 길 사이 빈 땅: 9m 격자, 해시로 고른 자리에 컨테이너 야적장(2~4개 묶음) / 잔해 더미 / 방벽 줄 / 공장 설비.
      var bound = map.BoundsMax.X.AsFloat;
      var n = 0;
      for (var x = -bound + 6f; x < bound - 4f; x += 9f) {
        for (var z = -bound + 6f; z < bound - 4f; z += 9f) {
          n++;
          var jitter = new Vector3((Hash(n, 1) - 0.5f) * 4f, 0f, (Hash(n, 2) - 0.5f) * 4f);
          var spot = new Vector3(x, 0f, z) + jitter;
          var roll = Hash(n, 3);
          if (roll > 0.5f || Free(map, spot, 5f) == false) {
            continue;
          }
          var yaw = Mathf.Round(Hash(n, 4) * 4f) * 90f + (Hash(n, 5) - 0.5f) * 10f;
          if (roll < 0.22f) {
            var stack = 2 + (int)(Hash(n, 6) * 2.99f);
            var along = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
            for (var s = 0; s < stack; s++) {
              Put(DressKind.Container, spot + along * ((s - (stack - 1) / 2f) * 2.7f), yaw, 9800 + n * 5 + s);
            }
          } else if (roll < 0.32f) {
            Put(DressKind.Debris, spot, yaw, 9800 + n * 5);
            Put(DressKind.Ruin, spot + Quaternion.Euler(0f, yaw, 0f) * new Vector3(3f, 0f, 1f), yaw + 30f, 9801 + n * 5);
          } else if (roll < 0.42f) {
            var along = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
            for (var s = -1; s <= 1; s++) {
              Put(DressKind.Barrier, spot + along * (s * 2.4f), yaw, 9800 + n * 5 + s + 1);
            }
          } else {
            Put(Hash(n, 7) < 0.5f ? DressKind.Tank : DressKind.Hopper, spot, yaw, 9800 + n * 5);
          }
        }
      }
      // 맵 둘레 띠 위: 공장·창고 정면(바깥 띠, 판정 밖).
      var edge = bound + 7f;
      var k = 0;
      for (var t = -bound - 8f; t <= bound + 8f; t += 15f) {
        foreach (var (pos, face) in new[] {
          (new Vector3(t, PerimeterHeight, -edge), 0f), (new Vector3(t, PerimeterHeight, edge), 180f),
          (new Vector3(-edge, PerimeterHeight, t), 90f), (new Vector3(edge, PerimeterHeight, t), 270f) }) {
          k++;
          var kind = Hash(k, 11) < 0.5f ? DressKind.CityIndustrial : Hash(k, 12) < 0.6f ? DressKind.CityMedium : DressKind.Container;
          Put(kind, pos, face, 9900 + k);
        }
      }
    }

    static MapShape? Nearest(MapShape[] shapes, FPVector3 point) {
      MapShape? best = null;
      var bestDistance = float.MaxValue;
      foreach (var shape in shapes ?? System.Array.Empty<MapShape>()) {
        var distance = shape.Distance(point).AsFloat;
        if (distance < bestDistance) {
          bestDistance = distance;
          best = shape;
        }
      }
      return best;
    }
  }
}
