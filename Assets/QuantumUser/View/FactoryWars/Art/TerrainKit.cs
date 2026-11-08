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
  public static partial class TerrainKit {
    /// <summary>수면 높이. 프로필의 강 깊이를 따른다.</summary>
    public static float WaterLevel => -Mathf.Max(0.8f, VisualProfile.Current.WaterDepth);
    const float Step = 4f;
    const float OuterStep = 20f;
    const float Margin = 80f;
    const float Patch = 18f;
    const float StripeLength = 1.3f;
    const float StripeWidth = 0.45f;
    // 강변: 땅 위에 얹는 평평한 모래 띠, 그 안쪽의 경사, 물 순서다. 숫자는 강 가장자리에서 바깥쪽으로 잰 거리다.
    const float BandOuter = 1.8f;
    const float BandInner = 0.4f;
    const float SlopeInner = -0.4f;
    const float GroundCut = 1f;
    const float MinCell = 1f;
    const float BankY = 0.03f;

    /// <summary>강줄기의 한 점. Half는 그 자리의 강폭 절반이다.</summary>
    public struct RiverPoint {
      public Vector2 Position;
      public Vector2 Normal;
      public float Half;
    }

    /// <summary>축에 나란한 사각형 범위. 회전이 90도 단위인 모양만 받는다.</summary>
    public static Rect Extent(MapShape shape, float grow = 0f) {
      var turned = Mathf.Abs(Mathf.Repeat(shape.Yaw.AsFloat, 180f) - 90f) < 1f;
      var half = turned ? new Vector2(shape.HalfWidth.AsFloat, shape.HalfLength.AsFloat) : new Vector2(shape.HalfLength.AsFloat, shape.HalfWidth.AsFloat);
      half += Vector2.one * grow;
      var center = new Vector2(shape.Center.X.AsFloat, shape.Center.Z.AsFloat);
      return new Rect(center - half, half * 2);
    }

    /// <summary>MapSpec.River의 조각을 이어 강줄기마다 점 목록으로 푼다. 조각이 끊기면 다른 강줄기다.</summary>
    public static List<List<RiverPoint>> RiverPaths(MapSpec map) {
      var paths = new List<List<RiverPoint>>();
      var pieces = map.River ?? Array.Empty<MapShape>();
      List<RiverPoint> path = null;
      var last = Vector2.zero;
      foreach (var piece in pieces) {
        var center = new Vector2(piece.Center.X.AsFloat, piece.Center.Z.AsFloat);
        if (path == null || (center - last).magnitude > 12f) {
          path = new List<RiverPoint>();
          paths.Add(path);
        }
        path.Add(new RiverPoint { Position = center, Half = piece.HalfWidth.AsFloat });
        last = center;
      }
      foreach (var points in paths) {
        for (var i = 0; i < points.Count; i++) {
          var from = points[Mathf.Max(i - 1, 0)].Position;
          var to = points[Mathf.Min(i + 1, points.Count - 1)].Position;
          var along = (to - from).normalized;
          var point = points[i];
          point.Normal = new Vector2(-along.y, along.x);
          points[i] = point;
        }
      }
      return paths;
    }

    /// <summary>강 가장자리까지의 거리. 물 위면 음수다.</summary>
    public static float RiverDistance(List<List<RiverPoint>> paths, Vector2 point) {
      var best = float.MaxValue;
      foreach (var path in paths) {
        for (var i = 0; i + 1 < path.Count; i++) {
          var a = path[i].Position;
          var ab = path[i + 1].Position - a;
          var t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / Mathf.Max(ab.sqrMagnitude, 0.001f));
          var distance = (point - (a + ab * t)).magnitude - Mathf.Lerp(path[i].Half, path[i + 1].Half, t);
          best = Mathf.Min(best, distance);
        }
      }
      return best;
    }

    static List<float> Lines(float min, float max) {
      var lines = new List<float>();
      for (var x = min; x <= max + 0.01f; x += Step) {
        lines.Add(x);
      }
      for (var x = min - Margin; x < min; x += OuterStep) {
        lines.Add(x);
      }
      for (var x = max + Margin; x > max; x -= OuterStep) {
        lines.Add(x);
      }
      lines.Sort();
      return lines;
    }

    static float Smooth(float t) => t * t * (3f - 2f * t);

    /// <summary>0~1의 값 노이즈. 흙과 풀의 큰 얼룩을 만든다.</summary>
    static float Noise(float x, float z, int seed) {
      var patch = Mathf.Max(6f, VisualProfile.Current.PatchSize);
      var cx = Mathf.FloorToInt(x / patch);
      var cz = Mathf.FloorToInt(z / patch);
      var tx = Smooth(x / patch - cx);
      var tz = Smooth(z / patch - cz);
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
      var profile = VisualProfile.Current;
      var outside = x < map.BoundsMin.X.AsFloat || x > map.BoundsMax.X.AsFloat || z < map.BoundsMin.Y.AsFloat || z > map.BoundsMax.Y.AsFloat;
      // 닫힌 맵: 경계 밖은 경계 벽 뒤로 어둡게 끊는다.
      if (outside) {
        return Swatch.ConcreteDark;
      }
      if (Inside(map.NoBuildZones, x, z)) {
        return noise < 0.55f ? Swatch.Dirt : Swatch.DirtDark;
      }
      if (Inside(map.BaseAreas, x, z)) {
        if (profile.ZoneColors) {
          return noise < 0.45f ? Swatch.Dirt : noise < 0.6f ? Swatch.Concrete : noise < 0.8f ? Swatch.Grass : Swatch.DirtDark;
        }
        return noise < 0.3f ? Swatch.Dirt : noise < 0.42f ? Swatch.DirtDark : noise < 0.78f ? Swatch.Grass : Swatch.GrassDark;
      }
      if (profile.ZoneColors && new Vector2(x, z).magnitude < 26f) {
        // 중앙 교전지: 그을린 흙.
        return noise < 0.5f ? Swatch.DirtDark : noise < 0.75f ? Swatch.Earth : Swatch.GrassDark;
      }
      if (profile.GroundPalette == 1) {
        return noise < 0.18f ? Swatch.Dirt : noise < 0.42f ? Swatch.GrassLight : noise < 0.72f ? Swatch.Grass : Swatch.GrassDark;
      }
      if (profile.GroundPalette == 2) {
        return noise < 0.28f ? Swatch.Dirt : noise < 0.4f ? Swatch.DirtDark : noise < 0.7f ? Swatch.Olive : Swatch.GrassDark;
      }
      return noise < 0.2f ? Swatch.Dirt : noise < 0.3f ? Swatch.DirtDark : noise < 0.66f ? Swatch.Grass : Swatch.GrassDark;
    }

    static void Triangle(MeshKit k, MapSpec map, int seed, Vector3 a, Vector3 b, Vector3 c) {
      var center = (a + b + c) / 3;
      k.Paint(GroundColor(map, center.x, center.z, seed));
      k.Tri(a, b, c, Vector3.up);
    }

    static void Cell(MeshKit k, MapSpec map, int seed, float x0, float x1, float z0, float z1, bool flip) {
      var a = new Vector3(x0, 0, z0);
      var b = new Vector3(x1, 0, z0);
      var c = new Vector3(x1, 0, z1);
      var d = new Vector3(x0, 0, z1);
      if (flip) {
        Triangle(k, map, seed, a, b, c);
        Triangle(k, map, seed, a, c, d);
      } else {
        Triangle(k, map, seed, a, b, d);
        Triangle(k, map, seed, b, c, d);
      }
    }

    /// <summary>강 근처 칸만 잘게 나눠 강 자리를 파낸다. 잘린 가장자리는 강변 띠가 덮는다.</summary>
    static void Carve(MeshKit k, MapSpec map, int seed, List<List<RiverPoint>> paths, float x0, float x1, float z0, float z1, bool flip) {
      var center = new Vector2((x0 + x1) / 2, (z0 + z1) / 2);
      var reach = new Vector2(x1 - x0, z1 - z0).magnitude / 2;
      var distance = RiverDistance(paths, center) - GroundCut;
      if (distance > reach) {
        Cell(k, map, seed, x0, x1, z0, z1, flip);
        return;
      }
      if (distance < -reach) {
        return;
      }
      if (x1 - x0 <= MinCell && z1 - z0 <= MinCell) {
        if (distance > 0f) {
          Cell(k, map, seed, x0, x1, z0, z1, flip);
        }
        return;
      }
      var mx = (x0 + x1) / 2;
      var mz = (z0 + z1) / 2;
      Carve(k, map, seed, paths, x0, mx, z0, mz, flip);
      Carve(k, map, seed, paths, mx, x1, z0, mz, flip == false);
      Carve(k, map, seed, paths, x0, mx, mz, z1, flip == false);
      Carve(k, map, seed, paths, mx, x1, mz, z1, flip);
    }

    /// <summary>강 자리를 파낸 바닥.</summary>
    public static void Ground(MeshKit k, MapSpec map, int seed) {
      var paths = RiverPaths(map);
      var min = new Vector2(map.BoundsMin.X.AsFloat, map.BoundsMin.Y.AsFloat);
      var max = new Vector2(map.BoundsMax.X.AsFloat, map.BoundsMax.Y.AsFloat);
      var xs = Lines(min.x, max.x);
      var zs = Lines(min.y, max.y);
      for (var i = 0; i + 1 < xs.Count; i++) {
        for (var j = 0; j + 1 < zs.Count; j++) {
          Carve(k, map, seed, paths, xs[i], xs[i + 1], zs[j], zs[j + 1], (i + j) % 2 == 0);
        }
      }
    }

    static Vector3 Bank(RiverPoint point, float side, float offset, float y) {
      var position = point.Position + point.Normal * (side * (point.Half + offset));
      return new Vector3(position.x, y, position.y);
    }

    /// <summary>강이 그려지는 범위. 경계 벽 안쪽까지다.</summary>
    public const float WallDepth = 14f;

    public static Rect Land(MapSpec map) {
      var min = new Vector2(map.BoundsMin.X.AsFloat - WallDepth, map.BoundsMin.Y.AsFloat - WallDepth);
      var max = new Vector2(map.BoundsMax.X.AsFloat + WallDepth, map.BoundsMax.Y.AsFloat + WallDepth);
      return new Rect(min, max - min);
    }

    static Vector3 Clip(Vector3 point, Rect land) => new Vector3(Mathf.Clamp(point.x, land.xMin, land.xMax), point.y, Mathf.Clamp(point.z, land.yMin, land.yMax));

    /// <summary>안쪽 굽이에서 띠 바깥선이 곡률 반경을 넘어 물 쪽으로 접혔는지 본다.</summary>
    static bool Folded(List<RiverPoint> path, RiverPoint point, float side) {
      var outer = Bank(point, side, BandOuter, 0f);
      return RiverDistance(new List<List<RiverPoint>> { path }, new Vector2(outer.x, outer.z)) < BandOuter - 0.5f;
    }

    /// <summary>물 면, 경사 강변, 땅 위의 모래 띠. 강줄기 하나를 리본으로 잇는다.</summary>
    public static void River(MeshKit k, List<RiverPoint> path, int seed, Rect land) {
      Vector3 Edge(RiverPoint point, float side, float offset, float y) => Clip(Bank(point, side, offset, y), land);
      for (var i = 0; i + 1 < path.Count; i++) {
        var a = path[i];
        var b = path[i + 1];
        if (land.Contains(a.Position) == false && land.Contains(b.Position) == false) {
          continue;
        }
        k.Paint(Swatch.WaterDeep);
        k.Quad(Edge(a, -1, SlopeInner, WaterLevel), Edge(a, 1, SlopeInner, WaterLevel), Edge(b, 1, SlopeInner, WaterLevel), Edge(b, -1, SlopeInner, WaterLevel), Vector3.up);
        foreach (var side in new[] { -1f, 1f }) {
          var middle = (BankY + WaterLevel) * 0.5f;
          var ledge = (BandInner + SlopeInner) * 0.5f;
          k.Paint(MeshKit.Hash(seed, i * 2 + (side > 0 ? 1 : 0)) < 0.5f ? Swatch.Earth : Swatch.CanyonMid);
          k.Quad(Edge(a, side, BandInner, BankY), Edge(a, side, ledge, middle), Edge(b, side, ledge, middle), Edge(b, side, BandInner, BankY), Vector3.up);
          k.Paint(Swatch.CanyonDark);
          k.Quad(Edge(a, side, ledge, middle), Edge(a, side, SlopeInner, WaterLevel - 0.1f), Edge(b, side, SlopeInner, WaterLevel - 0.1f), Edge(b, side, ledge, middle), Vector3.up);
          if (Folded(path, a, side) || Folded(path, b, side)) {
            continue;
          }
          k.Paint(MeshKit.Hash(seed, i * 2 + 300 + (side > 0 ? 1 : 0)) < 0.55f ? Swatch.Sand : Swatch.Dirt);
          k.Quad(Edge(a, side, BandOuter, BankY), Edge(a, side, BandInner, BankY), Edge(b, side, BandInner, BankY), Edge(b, side, BandOuter, BankY), Vector3.up);
        }
      }
    }

    /// <summary>물가의 작은 바위. 강폭 안쪽, 즉 막힌 영역 안에만 놓는다.</summary>
    public static void RiverRocks(MeshKit k, List<RiverPoint> path, int seed, MapSpec map) {
      k.ShadeFaces(seed, Swatch.BoulderDark, Swatch.Boulder, Swatch.BoulderLight);
      // 바위 크기만큼 땅 끝에서 물러난다.
      var land = Land(map);
      land = new Rect(land.xMin + 2f, land.yMin + 2f, land.width - 4f, land.height - 4f);
      for (var i = 2; i + 2 < path.Count; i++) {
        if (MeshKit.Hash(seed, i) > 0.32f || land.Contains(path[i].Position) == false) {
          continue;
        }
        var side = MeshKit.Hash(seed, i + 500) < 0.5f ? -1f : 1f;
        var size = 0.35f + 0.4f * MeshKit.Hash(seed, i + 900);
        var position = Bank(path[i], side, -size - 0.25f, WaterLevel - 0.1f);
        if (IsNearBridge(map, position)) {
          continue;
        }
        k.Rock(position, size, size * 1.6f, seed + i);
      }
      k.EndShade();
    }

    static bool IsNearBridge(MapSpec map, Vector3 position) {
      foreach (var bridge in map.Bridges ?? Array.Empty<MapShape>()) {
        var center = new Vector2(bridge.Center.X.AsFloat, bridge.Center.Z.AsFloat);
        if ((center - new Vector2(position.x, position.z)).magnitude < 9f) {
          return true;
        }
      }
      return false;
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
      // 원은 안에 들어가는 사각형으로 그린다. 항목 19의 지형 메쉬가 이 자리를 대체한다.
      var length = shape.IsCircle ? shape.Radius.AsFloat * 0.7f : shape.HalfLength.AsFloat;
      var width = shape.IsCircle ? shape.Radius.AsFloat * 0.7f : shape.HalfWidth.AsFloat;
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

    /// <summary>건설 금지 구역 둘레의 노랑·검정 줄무늬. 강 위는 건너뛴다.</summary>
    public static void NoBuildStripes(MeshKit k, MapShape zone, List<List<RiverPoint>> paths) {
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
          if (RiverDistance(paths, new Vector2(middle.x, middle.z)) < BandOuter) {
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

    /// <summary>울타리 한 칸. 두 기둥 사이다.</summary>
    public struct FencePiece {
      public Vector3 From;
      public Vector3 To;
    }

    const float EntranceGap = 2.4f;
    const float EntrancePieces = 3f;
    const float BankStart = 6.5f;
    const float BankPiece = 4f;
    const int BankPieces = 3;
    const float BankOffset = 2f;
    // 기둥이 강가에서 이보다 가까우면 안쪽 굽이에서 오프셋이 접힌 것이다.
    public const float BankClearance = 1.2f;
    public const float FenceHeight = 2.7f;

    static RiverPoint Sample(List<RiverPoint> path, float[] lengths, float s) {
      var i = 0;
      while (i + 2 < path.Count && lengths[i + 1] < s) {
        i++;
      }
      var t = Mathf.Clamp01((s - lengths[i]) / Mathf.Max(lengths[i + 1] - lengths[i], 0.001f));
      return new RiverPoint {
        Position = Vector2.Lerp(path[i].Position, path[i + 1].Position, t),
        Normal = Vector2.Lerp(path[i].Normal, path[i + 1].Normal, t).normalized,
        Half = Mathf.Lerp(path[i].Half, path[i + 1].Half, t),
      };
    }

    static bool OnBank(List<List<RiverPoint>> paths, Vector3 point) => RiverDistance(paths, new Vector2(point.x, point.z)) >= BankClearance;

    /// <summary>다리 양쪽 입구와, 입구 좌우 강가 12m씩. 안쪽 굽이는 물에 닿기 전까지만 간다. 강 전체를 두르지 않는다.</summary>
    public static List<FencePiece> BarrierPieces(MapSpec map, int bridgeIndex) {
      var pieces = new List<FencePiece>();
      var bridge = map.Bridges[bridgeIndex];
      var extent = Extent(bridge);
      var acrossX = extent.width < extent.height;
      var span = acrossX ? Vector3.right : Vector3.forward;
      var lateral = acrossX ? Vector3.forward : Vector3.right;
      var halfSpan = (acrossX ? extent.width : extent.height) / 2;
      var halfWidth = (acrossX ? extent.height : extent.width) / 2;
      var center = new Vector3(extent.center.x, 0, extent.center.y);
      foreach (var side in new[] { -1f, 1f }) {
        var entrance = center + span * (side * (halfSpan + EntranceGap));
        for (var i = 0; i < EntrancePieces; i++) {
          var from = -halfWidth + halfWidth * 2 * i / EntrancePieces;
          var to = -halfWidth + halfWidth * 2 * (i + 1) / EntrancePieces;
          pieces.Add(new FencePiece { From = entrance + lateral * from, To = entrance + lateral * to });
        }
      }

      // 다리와 가장 가까운 강줄기를 찾아 강가를 따라 걷는다.
      var paths = RiverPaths(map);
      var nearest = float.MaxValue;
      List<RiverPoint> path = null;
      var index = 0;
      foreach (var candidate in paths) {
        for (var i = 0; i < candidate.Count; i++) {
          var distance = (candidate[i].Position - extent.center).sqrMagnitude;
          if (distance < nearest) {
            nearest = distance;
            path = candidate;
            index = i;
          }
        }
      }
      if (path == null) {
        return pieces;
      }
      var lengths = new float[path.Count];
      for (var i = 1; i < path.Count; i++) {
        lengths[i] = lengths[i - 1] + (path[i].Position - path[i - 1].Position).magnitude;
      }
      Vector3 Along(float s, float side) {
        var point = Sample(path, lengths, s);
        var position = point.Position + point.Normal * (side * (point.Half + BankOffset));
        return new Vector3(position.x, 0, position.y);
      }
      foreach (var direction in new[] { -1f, 1f }) {
        foreach (var side in new[] { -1f, 1f }) {
          for (var i = 0; i < BankPieces; i++) {
            var from = Along(lengths[index] + direction * (BankStart + i * BankPiece), side);
            var to = Along(lengths[index] + direction * (BankStart + (i + 1) * BankPiece), side);
            // 안쪽 굽이에서는 여기서 끊는다. 더 걸으면 물을 건너간다.
            if (OnBank(paths, from) == false || OnBank(paths, (from + to) / 2) == false || OnBank(paths, to) == false) {
              break;
            }
            pieces.Add(new FencePiece { From = from, To = to });
          }
        }
      }
      return pieces;
    }

    /// <summary>묵직한 금속 기둥과 가로 패널. 팀 색은 패널 테두리에만 쓴다. 경고등은 따로 모아 깜빡인다.</summary>
    public static void Fence(MeshKit body, MeshKit lamps, FencePiece piece) {
      var delta = piece.To - piece.From;
      var length = delta.magnitude;
      var yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
      body.Push(piece.From, new Vector3(0, yaw, 0));
      lamps.Push(piece.From, new Vector3(0, yaw, 0));
      foreach (var z in new[] { 0f, length }) {
        body.Paint(Swatch.MetalDark);
        body.Taper(new Vector3(0, 0, z), new Vector2(0.6f, 0.6f), new Vector2(0.42f, 0.42f), 2.3f);
        body.Paint(Swatch.Metal);
        body.Box(new Vector3(0, 2.34f, z), new Vector3(0.56f, 0.1f, 0.56f));
        body.Paint(Swatch.LampDark);
        body.Cylinder(new Vector3(0, 2.39f, z), 0.17f, 0.08f, 6);
        lamps.Glow(Swatch.GlowRed);
        lamps.Dome(new Vector3(0, 2.46f, z), 0.2f, 0.24f, 6, 2);
      }
      var inner = length - 0.4f;
      foreach (var y in new[] { 0.72f, 1.62f }) {
        body.Paint(Swatch.Armor);
        body.Box(new Vector3(0, y, length / 2), new Vector3(0.16f, 0.62f, inner));
        body.Team();
        body.Box(new Vector3(0, y + 0.33f, length / 2), new Vector3(0.2f, 0.08f, inner));
        body.Box(new Vector3(0, y - 0.33f, length / 2), new Vector3(0.2f, 0.08f, inner));
      }
      body.Pop();
      lamps.Pop();
    }

    /// <summary>언덕·협곡·바위·강·줄무늬를 메쉬 하나로 모은다. 번호는 MapSpec.Terrain의 순서다.</summary>
    public static void Features(MeshKit k, MapSpec map) {
      var profile = VisualProfile.Current;
      var paths = RiverPaths(map);
      var features = map.Terrain ?? Array.Empty<TerrainFeature>();
      for (var i = 0; i < features.Length; i++) {
        var seed = 100 + i * 17;
        switch (features[i].Kind) {
          case TerrainKind.Hill: Plateau(k, features[i].Shape, seed, profile.PlateauHeight * (0.85f + 0.3f * MeshKit.Hash(seed, 5)), profile.CliffLayers, profile.Jagged, false); break;
          case TerrainKind.Cliff: Plateau(k, features[i].Shape, seed, HighGroundHeight + 0.8f, 1, profile.Jagged * 0.5f, false); break;
          case TerrainKind.CanyonWall: Plateau(k, features[i].Shape, seed, profile.PlateauHeight * 1.25f, profile.CliffLayers + 1, profile.Jagged, true); break;
          case TerrainKind.Rock: Boulders(k, features[i].Shape, seed); break;
        }
      }
      for (var i = 0; i < paths.Count; i++) {
        River(k, paths[i], 40 + i, Land(map));
        RiverRocks(k, paths[i], 70 + i, map);
      }
      Roads(k, map);
      HighGrounds(k, map);
      BridgePiers(k, map);
      if (profile.NoBuildAlways) {
        NoBuildMarks(k, map);
      }
    }

    /// <summary>건설 금지 표시. 프로필에 따라 늘 보이거나, 건설 위치를 고를 때만 보인다.</summary>
    public static void NoBuildMarks(MeshKit k, MapSpec map) {
      var paths = RiverPaths(map);
      foreach (var zone in map.NoBuildZones ?? Array.Empty<MapShape>()) {
        NoBuildStripes(k, zone, paths);
      }
    }
  }
}
