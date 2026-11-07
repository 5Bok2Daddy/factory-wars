namespace FactoryWars.View {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using Quantum;
  using UnityEngine;

  /// <summary>
  /// MapSpec에서 바닥과 지형 메쉬를 만든다. 변형값은 지형 번호를 시드로 정해 모든 기기에서 같다.
  /// 화면 전용이다. 통과와 건설 판정은 시뮬레이션의 MapSpec 그대로다.
  /// </summary>
  public static class TerrainKit {
    public const float WaterLevel = -0.8f;
    const float MoatBottom = -1.1f;
    const float Step = 4f;
    const float OuterStep = 20f;
    const float Margin = 80f;
    const float Patch = 18f;
    const float MoatHalfWidth = 3.5f;
    const float StripeLength = 1.3f;
    const float StripeWidth = 0.45f;

    /// <summary>축에 나란한 사각형 범위. 회전이 90도 단위인 모양만 받는다.</summary>
    public static Rect Extent(MapShape shape, float grow = 0f) {
      var turned = Mathf.Abs(Mathf.Repeat(shape.Yaw.AsFloat, 180f) - 90f) < 1f;
      var half = turned ? new Vector2(shape.HalfWidth.AsFloat, shape.HalfLength.AsFloat) : new Vector2(shape.HalfLength.AsFloat, shape.HalfWidth.AsFloat);
      half += Vector2.one * grow;
      var center = new Vector2(shape.Center.X.AsFloat, shape.Center.Z.AsFloat);
      return new Rect(center - half, half * 2);
    }

    static bool IsAxisAligned(MapShape shape) => Mathf.Repeat(shape.Yaw.AsFloat + 1f, 90f) < 2f;

    /// <summary>다리와 일직선으로 이어진 언덕은 해자다. 둘 다 시뮬레이션에서 막힌 곳이라 파내도 판정은 같다.</summary>
    public static bool IsMoat(MapSpec map, TerrainFeature feature) {
      if (feature.Kind != TerrainKind.Hill || feature.Shape.IsCircle || IsAxisAligned(feature.Shape) == false) {
        return false;
      }
      var hill = Extent(feature.Shape);
      foreach (var bridge in map.Bridges ?? Array.Empty<MapShape>()) {
        var gap = Extent(bridge);
        var acrossX = gap.width < gap.height;
        var inLine = acrossX ? Mathf.Abs(gap.center.x - hill.center.x) < 0.6f && hill.height > hill.width
                             : Mathf.Abs(gap.center.y - hill.center.y) < 0.6f && hill.width > hill.height;
        var touches = acrossX
          ? hill.yMax >= gap.yMin - 1.5f && hill.yMin <= gap.yMax + 1.5f
          : hill.xMax >= gap.xMin - 1.5f && hill.xMin <= gap.xMax + 1.5f;
        if (inLine && touches) {
          return true;
        }
      }
      return false;
    }

    /// <summary>파낼 사각형들. 언덕은 다리 폭에 맞춰 조금 넓히고, 맵 끝에 닿으면 바깥까지 잇는다.</summary>
    public static List<Rect> MoatRects(MapSpec map) {
      var rects = new List<Rect>();
      foreach (var bridge in map.Bridges ?? Array.Empty<MapShape>()) {
        rects.Add(Extent(bridge));
      }
      var min = new Vector2(map.BoundsMin.X.AsFloat, map.BoundsMin.Y.AsFloat);
      var max = new Vector2(map.BoundsMax.X.AsFloat, map.BoundsMax.Y.AsFloat);
      foreach (var feature in map.Terrain ?? Array.Empty<TerrainFeature>()) {
        if (IsMoat(map, feature) == false) {
          continue;
        }
        var rect = Extent(feature.Shape);
        var wide = rect.width > rect.height;
        var grow = wide ? new Vector2(0, MoatHalfWidth - rect.height / 2) : new Vector2(MoatHalfWidth - rect.width / 2, 0);
        rect = Rect.MinMaxRect(rect.xMin - grow.x, rect.yMin - grow.y, rect.xMax + grow.x, rect.yMax + grow.y);
        if (rect.xMin <= min.x + 1f) {
          rect.xMin = min.x - Margin;
        }
        if (rect.yMin <= min.y + 1f) {
          rect.yMin = min.y - Margin;
        }
        if (rect.xMax >= max.x - 1f) {
          rect.xMax = max.x + Margin;
        }
        if (rect.yMax >= max.y - 1f) {
          rect.yMax = max.y + Margin;
        }
        rects.Add(rect);
      }
      return rects;
    }

    static List<float> Lines(float min, float max, IEnumerable<float> edges) {
      var fixedLines = edges.Where(e => e > min - Margin && e < max + Margin).Distinct().OrderBy(e => e).ToList();
      var lines = new List<float>(fixedLines);
      void Add(float value) {
        if (fixedLines.All(e => Mathf.Abs(e - value) > Step * 0.3f)) {
          lines.Add(value);
        }
      }
      for (var x = min; x <= max + 0.01f; x += Step) {
        Add(x);
      }
      for (var x = min - Margin; x < min; x += OuterStep) {
        Add(x);
      }
      for (var x = max + Margin; x > max; x -= OuterStep) {
        Add(x);
      }
      lines.Sort();
      return lines;
    }

    static float Smooth(float t) => t * t * (3f - 2f * t);

    /// <summary>0~1의 값 노이즈. 흙과 풀의 큰 얼룩을 만든다.</summary>
    static float Noise(float x, float z, int seed) {
      var cx = Mathf.FloorToInt(x / Patch);
      var cz = Mathf.FloorToInt(z / Patch);
      var tx = Smooth(x / Patch - cx);
      var tz = Smooth(z / Patch - cz);
      float Corner(int i, int j) => MeshKit.Hash(seed + (cx + i) * 73, (cz + j) * 31 + 1000);
      return Mathf.Lerp(Mathf.Lerp(Corner(0, 0), Corner(1, 0), tx), Mathf.Lerp(Corner(0, 1), Corner(1, 1), tx), tz);
    }

    static bool Inside(MapShape[] shapes, float x, float z) {
      var point = new Photon.Deterministic.FPVector3(Photon.Deterministic.FP.FromFloat_UNSAFE(x), default, Photon.Deterministic.FP.FromFloat_UNSAFE(z));
      foreach (var shape in shapes ?? Array.Empty<MapShape>()) {
        if (shape.Distance(point).AsFloat <= 0f) {
          return true;
        }
      }
      return false;
    }

    static Swatch GroundColor(MapSpec map, float x, float z, int seed) {
      var noise = Noise(x, z, seed);
      var outside = x < map.BoundsMin.X.AsFloat || x > map.BoundsMax.X.AsFloat || z < map.BoundsMin.Y.AsFloat || z > map.BoundsMax.Y.AsFloat;
      if (outside) {
        return Swatch.Grass;
      }
      if (Inside(map.NoBuildZones, x, z)) {
        return noise < 0.55f ? Swatch.Dirt : Swatch.DirtDark;
      }
      if (Inside(map.BaseAreas, x, z)) {
        return noise < 0.3f ? Swatch.Dirt : noise < 0.42f ? Swatch.DirtDark : noise < 0.78f ? Swatch.Grass : Swatch.GrassDark;
      }
      return noise < 0.2f ? Swatch.Dirt : noise < 0.3f ? Swatch.DirtDark : noise < 0.66f ? Swatch.Grass : Swatch.GrassDark;
    }

    static void Triangle(MeshKit k, MapSpec map, int seed, Vector3 a, Vector3 b, Vector3 c) {
      var center = (a + b + c) / 3;
      k.Paint(GroundColor(map, center.x, center.z, seed));
      k.Tri(a, b, c, Vector3.up);
    }

    /// <summary>해자 자리를 뚫은 바닥. 격자선을 해자 경계에 맞춰서 구멍이 곧게 나온다.</summary>
    public static void Ground(MeshKit k, MapSpec map, int seed) {
      var moat = MoatRects(map);
      var min = new Vector2(map.BoundsMin.X.AsFloat, map.BoundsMin.Y.AsFloat);
      var max = new Vector2(map.BoundsMax.X.AsFloat, map.BoundsMax.Y.AsFloat);
      var xs = Lines(min.x, max.x, moat.SelectMany(r => new[] { r.xMin, r.xMax })).ToArray();
      var zs = Lines(min.y, max.y, moat.SelectMany(r => new[] { r.yMin, r.yMax })).ToArray();
      var hole = new bool[xs.Length - 1, zs.Length - 1];
      for (var i = 0; i + 1 < xs.Length; i++) {
        for (var j = 0; j + 1 < zs.Length; j++) {
          var center = new Vector2((xs[i] + xs[i + 1]) / 2, (zs[j] + zs[j + 1]) / 2);
          hole[i, j] = moat.Any(r => r.Contains(center));
        }
      }
      bool Solid(int i, int j) => i >= 0 && j >= 0 && i < hole.GetLength(0) && j < hole.GetLength(1) && hole[i, j] == false;

      for (var i = 0; i + 1 < xs.Length; i++) {
        for (var j = 0; j + 1 < zs.Length; j++) {
          float x0 = xs[i], x1 = xs[i + 1], z0 = zs[j], z1 = zs[j + 1];
          if (hole[i, j] == false) {
            // 삼각형마다 칠해서 얼룩 경계가 대각선으로 꺾인다. 사각 칸이 드러나지 않는다.
            var a = new Vector3(x0, 0, z0);
            var b = new Vector3(x1, 0, z0);
            var c = new Vector3(x1, 0, z1);
            var d = new Vector3(x0, 0, z1);
            if ((i + j) % 2 == 0) {
              Triangle(k, map, seed, a, b, c);
              Triangle(k, map, seed, a, c, d);
            } else {
              Triangle(k, map, seed, a, b, d);
              Triangle(k, map, seed, b, c, d);
            }
            continue;
          }
          k.Paint(Swatch.WaterDeep);
          k.Quad(new Vector3(x0, WaterLevel, z0), new Vector3(x1, WaterLevel, z0), new Vector3(x1, WaterLevel, z1), new Vector3(x0, WaterLevel, z1), Vector3.up);
          // 땅과 닿는 변마다 둑을 세운다.
          k.Paint(Swatch.DirtDark);
          if (Solid(i - 1, j)) {
            k.Quad(new Vector3(x0, 0, z0), new Vector3(x0, 0, z1), new Vector3(x0, MoatBottom, z1), new Vector3(x0, MoatBottom, z0), Vector3.right);
          }
          if (Solid(i + 1, j)) {
            k.Quad(new Vector3(x1, 0, z0), new Vector3(x1, 0, z1), new Vector3(x1, MoatBottom, z1), new Vector3(x1, MoatBottom, z0), Vector3.left);
          }
          if (Solid(i, j - 1)) {
            k.Quad(new Vector3(x0, 0, z0), new Vector3(x1, 0, z0), new Vector3(x1, MoatBottom, z0), new Vector3(x0, MoatBottom, z0), Vector3.forward);
          }
          if (Solid(i, j + 1)) {
            k.Quad(new Vector3(x0, 0, z1), new Vector3(x1, 0, z1), new Vector3(x1, MoatBottom, z1), new Vector3(x0, MoatBottom, z1), Vector3.back);
          }
        }
      }
    }

    /// <summary>
    /// 길이 방향으로 단면을 이어 붙인 능선. x가 길이, 단면은 (z, y)다. 벽돌처럼 끊기지 않고 한 덩어리로 이어진다.
    /// 첫 단면과 마지막 단면은 막는다. 단면 점 수는 모두 같아야 한다.
    /// </summary>
    static void Ridge(MeshKit k, float[] stations, Vector2[][] sections, Swatch side, Swatch top, int topFace) {
      for (var i = 0; i + 1 < stations.Length; i++) {
        var from = sections[i];
        var to = sections[i + 1];
        var middle = new Vector3((stations[i] + stations[i + 1]) / 2, 0, 0);
        for (var j = 0; j + 1 < from.Length; j++) {
          var a = new Vector3(stations[i], from[j].y, from[j].x);
          var b = new Vector3(stations[i], from[j + 1].y, from[j + 1].x);
          var c = new Vector3(stations[i + 1], to[j + 1].y, to[j + 1].x);
          var d = new Vector3(stations[i + 1], to[j].y, to[j].x);
          var outward = (a + b + c + d) / 4 - middle;
          if (j == topFace) {
            k.Paint(top);
            outward = Vector3.up;
          } else {
            k.Paint(side);
          }
          k.Quad(a, b, c, d, outward);
        }
      }
      foreach (var (index, direction) in new[] { (0, Vector3.left), (stations.Length - 1, Vector3.right) }) {
        k.Paint(side);
        var section = sections[index];
        for (var j = 1; j + 1 < section.Length; j++) {
          k.Tri(new Vector3(stations[index], section[0].y, section[0].x), new Vector3(stations[index], section[j].y, section[j].x),
            new Vector3(stations[index], section[j + 1].y, section[j + 1].x), direction);
        }
      }
    }

    static float[] Stations(float half, float spacing, int seed) {
      var parts = Mathf.Max(3, Mathf.CeilToInt(half * 2 / spacing));
      var stations = new float[parts + 1];
      for (var i = 0; i <= parts; i++) {
        var jitter = i == 0 || i == parts ? 0f : (MeshKit.Hash(seed, i + 200) - 0.5f) * spacing * 0.5f;
        stations[i] = -half + half * 2 * i / parts + jitter;
      }
      return stations;
    }

    /// <summary>층층 절벽. 층마다 옆면은 어둡고 윗면은 밝다. 위로 갈수록 좁아지고 끝은 낮아진다.</summary>
    public static void Hill(MeshKit k, MapShape shape, int seed) {
      var length = shape.HalfLength.AsFloat;
      var width = shape.HalfWidth.AsFloat;
      Swatch[] sides = { Swatch.CliffDark, Swatch.CliffMid, Swatch.CliffMid };
      Swatch[] tops = { Swatch.CliffMid, Swatch.CliffLight, Swatch.Grass };
      float[] heights = { 1.0f, 1.0f, 0.9f };
      float[] shrink = { 1f, 0.72f, 0.46f };
      k.Push(new Vector3(shape.Center.X.AsFloat, 0, shape.Center.Z.AsFloat), new Vector3(0, shape.Yaw.AsFloat, 0));
      var y = 0f;
      for (var t = 0; t < sides.Length; t++) {
        var layer = seed + t * 31;
        var half = length - t * 0.9f;
        var stations = Stations(half, 3.4f, layer);
        var sections = new Vector2[stations.Length][];
        for (var i = 0; i < stations.Length; i++) {
          var end = i == 0 || i == stations.Length - 1 ? 0.72f : 1f;
          var near = width * shrink[t] * end * (0.86f + 0.14f * MeshKit.Hash(layer, i));
          var far = width * shrink[t] * end * (0.86f + 0.14f * MeshKit.Hash(layer, i + 40));
          var top = y + heights[t] * (0.88f + 0.24f * MeshKit.Hash(layer, i + 80));
          var lean = 0.18f + 0.1f * MeshKit.Hash(layer, i + 120);
          sections[i] = new[] { new Vector2(-near, y), new Vector2(-near * (1f - lean), top), new Vector2(far * (1f - lean), top), new Vector2(far, y) };
        }
        Ridge(k, stations, sections, sides[t], tops[t], 1);
        y += heights[t];
      }
      k.Pop();
    }

    /// <summary>협곡을 이루는 높고 어두운 바위 벽. 윗선이 들쭉날쭉한 한 덩어리다.</summary>
    public static void CanyonWall(MeshKit k, MapShape shape, int seed) {
      var length = shape.HalfLength.AsFloat;
      var width = shape.HalfWidth.AsFloat * 1.5f;
      var stations = Stations(length, 1.9f, seed);
      var sections = new Vector2[stations.Length][];
      for (var i = 0; i < stations.Length; i++) {
        var end = i == 0 || i == stations.Length - 1 ? 0.55f : 1f;
        var height = (3.2f + 3.8f * MeshKit.Hash(seed, i + 3)) * end;
        var lean = (MeshKit.Hash(seed, i + 40) - 0.5f) * 1.5f;
        var near = width * (0.8f + 0.4f * MeshKit.Hash(seed, i + 70));
        var far = width * (0.8f + 0.4f * MeshKit.Hash(seed, i + 90));
        sections[i] = new[] {
          new Vector2(-near, 0), new Vector2(-near * 0.8f, height * 0.5f), new Vector2(-near * 0.42f + lean * 0.3f, height * 0.88f),
          new Vector2(lean * 0.3f, height), new Vector2(far * 0.42f + lean * 0.3f, height * 0.84f), new Vector2(far * 0.8f, height * 0.5f), new Vector2(far, 0),
        };
      }
      k.Push(new Vector3(shape.Center.X.AsFloat, 0, shape.Center.Z.AsFloat), new Vector3(0, shape.Yaw.AsFloat, 0));
      for (var i = 0; i + 1 < stations.Length; i++) {
        var from = sections[i];
        var to = sections[i + 1];
        var middle = new Vector3((stations[i] + stations[i + 1]) / 2, 0, 0);
        for (var j = 0; j + 1 < from.Length; j++) {
          var a = new Vector3(stations[i], from[j].y, from[j].x);
          var b = new Vector3(stations[i], from[j + 1].y, from[j + 1].x);
          var c = new Vector3(stations[i + 1], to[j + 1].y, to[j + 1].x);
          var d = new Vector3(stations[i + 1], to[j].y, to[j].x);
          k.Paint(MeshKit.Hash(seed, i * 11 + j) < 0.34f ? Swatch.CanyonLight : j == 2 || j == 3 ? Swatch.CanyonMid : Swatch.CanyonDark);
          k.Quad(a, b, c, d, (a + b + c + d) / 4 - middle);
        }
      }
      foreach (var (index, direction) in new[] { (0, Vector3.left), (stations.Length - 1, Vector3.right) }) {
        k.Paint(Swatch.CanyonDark);
        var section = sections[index];
        for (var j = 1; j + 1 < section.Length; j++) {
          k.Tri(new Vector3(stations[index], section[0].y, section[0].x), new Vector3(stations[index], section[j].y, section[j].x),
            new Vector3(stations[index], section[j + 1].y, section[j + 1].x), direction);
        }
      }
      k.Pop();
    }

    /// <summary>크기와 각도가 다른 회색 바위 무더기.</summary>
    public static void Boulders(MeshKit k, MapShape shape, int seed) {
      var center = new Vector3(shape.Center.X.AsFloat, 0, shape.Center.Z.AsFloat);
      var radius = shape.IsCircle ? shape.Radius.AsFloat : Mathf.Min(shape.HalfLength.AsFloat, shape.HalfWidth.AsFloat);
      k.ShadeFaces(seed, Swatch.BoulderDark, Swatch.Boulder, Swatch.BoulderLight);
      k.Rock(center, radius * 0.72f, radius * (1.05f + 0.4f * MeshKit.Hash(seed, 1)), seed);
      var count = 5 + (int)(MeshKit.Hash(seed, 2) * 3f);
      for (var i = 0; i < count; i++) {
        var angle = (i + MeshKit.Hash(seed, i + 10)) * Mathf.PI * 2f / count;
        var distance = radius * (0.55f + 0.35f * MeshKit.Hash(seed, i + 20));
        var size = radius * (0.18f + 0.22f * MeshKit.Hash(seed, i + 30));
        k.Rock(center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * distance, size, size * 1.3f, seed + i + 1);
      }
      k.EndShade();
    }

    /// <summary>건설 금지 구역 둘레의 노랑·검정 줄무늬. 해자 위는 건너뛴다.</summary>
    public static void NoBuildStripes(MeshKit k, MapShape zone, List<Rect> moat) {
      var half = new Vector2(zone.HalfLength.AsFloat, zone.HalfWidth.AsFloat);
      var center = new Vector3(zone.Center.X.AsFloat, 0.04f, zone.Center.Z.AsFloat);
      var turn = Quaternion.Euler(0, zone.Yaw.AsFloat, 0);
      var index = 0;
      void Edge(Vector3 from, Vector3 to, Vector3 inward) {
        var length = (to - from).magnitude;
        var count = Mathf.Max(2, Mathf.RoundToInt(length / StripeLength));
        for (var i = 0; i < count; i++) {
          var a = Vector3.Lerp(from, to, i / (float)count);
          var b = Vector3.Lerp(from, to, (i + 1f) / count);
          var middle = center + turn * ((a + b) / 2);
          index++;
          if (moat.Any(r => r.Contains(new Vector2(middle.x, middle.z)))) {
            continue;
          }
          k.Paint(index % 2 == 0 ? Swatch.StripeYellow : Swatch.Black);
          // 사선 끝을 주면 줄무늬가 평행사변형이 된다.
          var slant = (b - a).normalized * StripeWidth;
          k.Quad(center + turn * a, center + turn * b, center + turn * (b + inward * StripeWidth + slant), center + turn * (a + inward * StripeWidth + slant), Vector3.up);
        }
      }
      var c0 = new Vector3(-half.x, 0, -half.y);
      var c1 = new Vector3(half.x, 0, -half.y);
      var c2 = new Vector3(half.x, 0, half.y);
      var c3 = new Vector3(-half.x, 0, half.y);
      Edge(c0, c1, Vector3.forward);
      Edge(c1, c2, Vector3.left);
      Edge(c2, c3, Vector3.back);
      Edge(c3, c0, Vector3.right);
    }

    /// <summary>언덕·협곡·바위·줄무늬를 메쉬 하나로 모은다. 번호는 MapSpec.Terrain의 순서다.</summary>
    public static void Features(MeshKit k, MapSpec map) {
      var moat = MoatRects(map);
      var features = map.Terrain ?? Array.Empty<TerrainFeature>();
      for (var i = 0; i < features.Length; i++) {
        var seed = 100 + i * 17;
        if (IsMoat(map, features[i])) {
          continue;
        }
        switch (features[i].Kind) {
          case TerrainKind.Hill: Hill(k, features[i].Shape, seed); break;
          case TerrainKind.CanyonWall: CanyonWall(k, features[i].Shape, seed); break;
          default: Boulders(k, features[i].Shape, seed); break;
        }
      }
      foreach (var zone in map.NoBuildZones ?? Array.Empty<MapShape>()) {
        NoBuildStripes(k, zone, moat);
      }
    }
  }
}
