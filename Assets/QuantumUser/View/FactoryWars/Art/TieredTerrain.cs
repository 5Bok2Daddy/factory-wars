namespace FactoryWars.View {
  using System.Collections.Generic;
  using Photon.Deterministic;
  using Quantum;
  using UnityEngine;

  /// <summary>
  /// 3단 지형 표준 맵(MapSpec.Tiered, 2026-11-22) 화면. 판정은 2D 그대로이고 높이·색만 화면에서 만든다.
  /// 1단 본진 고지대(+3m, 밝은 황토) · 2단 앞마당 중간 대지(+1.5m, 황토) · 3단 중앙 저지대(0m, 짙은 슬레이트 자갈).
  /// 통과 불가 지형은 반듯한 짙은 암벽 블록, 맵 바깥은 매끈한 암벽 띠. 도로·흩뿌린 풀·나무·바위 액자는 그리지 않는다.
  /// </summary>
  public static class TieredTerrain {
    public const float MainHeight = 3f;
    public const float MidHeight = 1.5f;
    const float CliffTop = MainHeight + 1.3f;
    const float RidgeTop = 4.2f;
    const float SlopeRun = 3.4f;
    const float PerimeterWidth = 14f;
    const float PerimeterHeight = 7.5f;

    public static readonly Color MainColor = new Color(0.74f, 0.64f, 0.47f);
    public static readonly Color MidColor = new Color(0.63f, 0.55f, 0.41f);
    public static readonly Color SlopeColor = new Color(0.52f, 0.45f, 0.34f);
    public static readonly Color LowColor = new Color(0.26f, 0.27f, 0.28f);
    public static readonly Color LaneColor = new Color(0.33f, 0.33f, 0.32f);
    public static readonly Color RockFace = new Color(0.22f, 0.21f, 0.21f);
    public static readonly Color RockTop = new Color(0.36f, 0.34f, 0.32f);

    public static bool Uses(MapSpec map) => map != null && map.Tiered;

    static FPVector3 Point(float x, float z) => new FPVector3(FP.FromFloat_UNSAFE(x), FP._0, FP.FromFloat_UNSAFE(z));

    /// <summary>화면에서 지상 유닛·건물·자원을 올릴 높이.</summary>
    public static float SurfaceHeight(MapSpec map, float x, float z) {
      var p = Point(x, z);
      foreach (var high in map.HighGrounds ?? System.Array.Empty<MapShape>()) {
        if (high.Distance(p) <= FP._0) {
          return MainHeight;
        }
      }
      foreach (var ramp in map.Ramps ?? System.Array.Empty<MapShape>()) {
        if (ramp.Distance(p) <= FP._0) {
          var outside = DistanceToNearest(map.HighGrounds, p);
          return Mathf.Lerp(MainHeight, MidHeight, Mathf.Clamp01(outside / SlopeRun));
        }
      }
      foreach (var mid in map.MidGrounds ?? System.Array.Empty<MapShape>()) {
        if (mid.Distance(p) <= FP._0) {
          return MidHeight;
        }
      }
      foreach (var gate in map.TruceGates ?? System.Array.Empty<MapShape>()) {
        if (gate.Distance(p) <= FP._0) {
          var outside = DistanceToNearest(map.MidGrounds, p);
          return Mathf.Lerp(MidHeight, 0f, Mathf.Clamp01(outside / SlopeRun));
        }
      }
      return 0f;
    }

    static float DistanceToNearest(MapShape[] shapes, FPVector3 p) {
      var best = float.MaxValue;
      foreach (var shape in shapes ?? System.Array.Empty<MapShape>()) {
        best = Mathf.Min(best, shape.Distance(p).AsFloat);
      }
      return best == float.MaxValue ? 0f : best;
    }

    /// <summary>저지대 지면 색: 가운데 전장은 짙은 슬레이트, 맵 가장자리 측면 통로로 갈수록 조금 밝다. 잔잔한 자갈 결만 넣는다.</summary>
    public static Color GroundColor(MapSpec map, float x, float z) {
      var p = Point(x, z);
      var grain = Mathf.PerlinNoise(x * 0.11f + 3.1f, z * 0.10f - 7.3f) * 0.6f + Mathf.PerlinNoise(x * 0.031f, z * 0.029f) * 0.4f;
      Color color;
      if (DistanceToNearest(map.HighGrounds, p) <= 0f) {
        color = MainColor;
      } else if (DistanceToNearest(map.MidGrounds, p) <= 0f) {
        color = MidColor;
      } else {
        var edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
        color = Color.Lerp(LowColor, LaneColor, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(80f, 125f, edge)));
      }
      color *= 0.93f + grain * 0.14f;
      color.a = 1f;
      return color;
    }

    /// <summary>대지 윗면·비탈·관문 비탈·통과 불가 암벽·맵 둘레 암벽 띠를 메쉬 하나로. 색은 Tint가 정점마다 칠한다.</summary>
    public static void Build(MeshKit k, MapSpec map) {
      foreach (var high in map.HighGrounds ?? System.Array.Empty<MapShape>()) {
        Slab(k, high, MainHeight);
      }
      foreach (var mid in map.MidGrounds ?? System.Array.Empty<MapShape>()) {
        Slab(k, mid, MidHeight);
      }
      foreach (var ramp in map.Ramps ?? System.Array.Empty<MapShape>()) {
        Slope(k, ramp, map.HighGrounds, MainHeight, MidHeight);
      }
      foreach (var gate in map.TruceGates ?? System.Array.Empty<MapShape>()) {
        Slope(k, gate, map.MidGrounds, MidHeight, 0f);
      }
      foreach (var feature in map.Terrain ?? System.Array.Empty<TerrainFeature>()) {
        switch (feature.Kind) {
          case TerrainKind.Cliff: Block(k, feature.Shape, CliffTop, 0.35f); break;
          case TerrainKind.Hill:
          case TerrainKind.CanyonWall: Block(k, feature.Shape, RidgeTop, 0.6f); break;
        }
      }
      Perimeter(k, map);
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

    /// <summary>비탈: 높은 대지 가장자리에서 바깥으로 SlopeRun만큼 내려간다. 비탈 사각형의 긴 변이 대지 가장자리와 나란하다.</summary>
    static void Slope(MeshKit k, MapShape shape, MapShape[] uppers, float top, float bottom) {
      var center = shape.Center.ToUnityVector3();
      var up = Nearest(uppers, shape.Center);
      if (up.HasValue == false) {
        return;
      }
      // 높은 대지 중심에서 비탈 쪽으로: 사각형 축 가운데 그 방향과 더 가까운 축이 비탈 방향이다.
      var yaw = shape.Yaw.AsFloat * Mathf.Deg2Rad;
      var axisL = new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));
      var axisW = new Vector3(-axisL.z, 0f, axisL.x);
      var toward = center - up.Value.Center.ToUnityVector3();
      toward.y = 0f;
      var down = Mathf.Abs(Vector3.Dot(toward, axisW)) >= Mathf.Abs(Vector3.Dot(toward, axisL)) ? axisW * Mathf.Sign(Vector3.Dot(toward, axisW)) : axisL * Mathf.Sign(Vector3.Dot(toward, axisL));
      var side = Vector3.Cross(Vector3.up, down).normalized;
      var halfAcross = Mathf.Abs(Vector3.Dot(side, axisL)) > 0.5f ? shape.HalfLength.AsFloat : shape.HalfWidth.AsFloat;
      // 대지 가장자리(비탈 위 끝) = 비탈 중심에서 높은 대지 쪽으로 대지 경계까지.
      var edgeOffset = up.Value.Distance(shape.Center).AsFloat;
      var topCenter = center - down * edgeOffset;
      var bottomCenter = topCenter + down * SlopeRun;
      var a = topCenter - side * halfAcross + Vector3.up * (top + 0.02f);
      var b = topCenter + side * halfAcross + Vector3.up * (top + 0.02f);
      var c = bottomCenter + side * halfAcross + Vector3.up * (bottom + 0.02f);
      var d = bottomCenter - side * halfAcross + Vector3.up * (bottom + 0.02f);
      k.Paint(Swatch.White);
      k.Quad(a, b, c, d, Vector3.up);
      // 비탈 양옆 삼각 벽(대지 옆면과 이어진다).
      k.Tri(a, d, new Vector3(d.x, 0f, d.z), -side);
      k.Tri(a, new Vector3(d.x, 0f, d.z), new Vector3(a.x, 0f, a.z), -side);
      k.Tri(b, new Vector3(c.x, 0f, c.z), c, side);
      k.Tri(b, new Vector3(b.x, 0f, b.z), new Vector3(c.x, 0f, c.z), side);
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

    /// <summary>반듯한 암벽 블록(윗면 모서리만 살짝 깎는다). 판정 모양보다 0.2m 작게 그려 통로 폭이 보이는 그대로다.</summary>
    static void Block(MeshKit k, MapShape shape, float height, float bevel) {
      var corners = Corners(shape, -0.2f);
      var center = (corners[0] + corners[1] + corners[2] + corners[3]) / 4f;
      var tops = new Vector3[4];
      for (var i = 0; i < 4; i++) {
        var inward = (center - corners[i]);
        inward.y = 0f;
        tops[i] = corners[i] + inward.normalized * bevel + Vector3.up * height;
      }
      k.Paint(Swatch.Black);
      k.Quad(tops[0], tops[1], tops[2], tops[3], Vector3.up);
      for (var i = 0; i < 4; i++) {
        var a = corners[i];
        var b = corners[(i + 1) % 4];
        var outward = (a + b) / 2f - center;
        outward.y = 0f;
        k.Quad(tops[(i + 1) % 4], tops[i], a, b, outward.normalized);
      }
    }

    /// <summary>맵 바깥 둘레를 매끈한 암벽 띠로 막는다(바위 액자·나무 줄 대신).</summary>
    static void Perimeter(MeshKit k, MapSpec map) {
      var min = new Vector2(map.BoundsMin.X.AsFloat, map.BoundsMin.Y.AsFloat);
      var max = new Vector2(map.BoundsMax.X.AsFloat, map.BoundsMax.Y.AsFloat);
      void Band(Vector2 from, Vector2 to, Vector2 outward) {
        var a = new Vector3(from.x, 0f, from.y);
        var b = new Vector3(to.x, 0f, to.y);
        var o = new Vector3(outward.x, 0f, outward.y);
        var lift = Vector3.up * PerimeterHeight;
        k.Paint(Swatch.Black);
        // 안쪽 벽(살짝 기울여 위가 바깥으로 1.5m 물러난다), 윗면, 바깥 넓은 판.
        k.Quad(b + lift + o * 1.5f, a + lift + o * 1.5f, a, b, -o);
        k.Quad(a + lift + o * 1.5f, b + lift + o * 1.5f, b + lift + o * PerimeterWidth, a + lift + o * PerimeterWidth, Vector3.up);
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
      // 반시계(위에서 볼 때): 윗면 법선이 위를 향한다.
      return new[] {
        c - axisL * hl - axisW * hw,
        c - axisL * hl + axisW * hw,
        c + axisL * hl + axisW * hw,
        c + axisL * hl - axisW * hw,
      };
    }

    /// <summary>정점 색: 높이·법선으로 대지 윗면(황토 두 단), 비탈(흙), 암벽 옆면(짙은 바위), 암벽 윗면(밝은 바위).</summary>
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
          color = RockFace;
        } else if (n.y < 0.98f) {
          color = SlopeColor;
        } else if (v.y > MainHeight + 0.6f) {
          color = RockTop;
        } else if (v.y > MainHeight - 0.3f) {
          color = MainColor;
        } else if (v.y > MidHeight - 0.3f) {
          color = MidColor;
        } else {
          color = SlopeColor;
        }
        var grain = Mathf.PerlinNoise(v.x * 0.13f + 1.7f, v.z * 0.12f + 4.2f);
        color *= 0.94f + grain * 0.12f;
        color.a = 1f;
        colors[i] = linear ? color.linear : color;
      }
      mesh.colors = colors;
    }

    /// <summary>휴전 관문 울타리 조각: 관문 사각형의 긴 축을 따라 3m씩. 높이는 비탈 가운데.</summary>
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
  }
}
