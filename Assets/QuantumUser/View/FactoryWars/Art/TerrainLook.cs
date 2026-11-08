namespace FactoryWars.View {
  using System.Collections.Generic;
  using Quantum;
  using UnityEngine;

  /// <summary>
  /// 그림체 지형(항목 19): 층진 암벽 대지, 아스팔트 도로와 차선, 고지대 윗면과 비탈, 다리 교각, 화면 높이.
  /// 판정은 MapSpec 그대로이고 높낮이는 화면에서만 만든다. 값은 VisualProfile에서 읽는다.
  /// </summary>
  public static partial class TerrainKit {
    const float OutlineSpacing = 2.4f;
    const float LayerInset = 0.45f;
    const float LaneDash = 3f;
    const float LaneGap = 3f;
    const float LaneWidth = 0.22f;
    const float CurbWidth = 0.5f;

    /// <summary>모양의 가장자리를 같은 간격의 점으로. 반시계 방향이다.</summary>
    static List<Vector2> Outline(MapShape shape, float spacing) {
      var points = new List<Vector2>();
      var center = new Vector2(shape.Center.X.AsFloat, shape.Center.Z.AsFloat);
      if (shape.IsCircle) {
        var radius = shape.Radius.AsFloat;
        var count = Mathf.Max(10, Mathf.CeilToInt(Mathf.PI * 2f * radius / spacing));
        for (var i = 0; i < count; i++) {
          var a = Mathf.PI * 2f * i / count;
          points.Add(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
        }
        return points;
      }
      var yaw = shape.Yaw.AsFloat * Mathf.Deg2Rad;
      var along = new Vector2(Mathf.Cos(yaw), -Mathf.Sin(yaw)) * shape.HalfLength.AsFloat;
      var across = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw)) * shape.HalfWidth.AsFloat;
      var corners = new[] { center - along - across, center + along - across, center + along + across, center - along + across };
      if (Vector3.Cross(corners[1] - corners[0], corners[2] - corners[1]).z < 0f) {
        System.Array.Reverse(corners);
      }
      for (var c = 0; c < 4; c++) {
        var from = corners[c];
        var to = corners[(c + 1) % 4];
        var count = Mathf.Max(1, Mathf.CeilToInt((to - from).magnitude / spacing));
        for (var i = 0; i < count; i++) {
          points.Add(Vector2.Lerp(from, to, i / (float)count));
        }
      }
      return points;
    }

    /// <summary>
    /// 층진 암벽 대지. 층마다 안쪽으로 물러나며 가장자리가 들쭉날쭉하다. 옆면은 바위 결, 층 사이 턱과 윗면은 풀이다.
    /// 점은 안쪽으로만 움직여서 화면이 판정 모양 밖으로 나가지 않는다.
    /// </summary>
    public static void Plateau(MeshKit k, MapShape shape, int seed, float height, int layers, float jag, bool dark) {
      var ring = Outline(shape, OutlineSpacing);
      var center = Vector2.zero;
      foreach (var p in ring) {
        center += p;
      }
      center /= ring.Count;
      layers = Mathf.Max(1, layers);
      var rings = new List<Vector2[]>();
      for (var l = 0; l <= layers; l++) {
        var points = new Vector2[ring.Count];
        for (var i = 0; i < ring.Count; i++) {
          var toCenter = center - ring[i];
          var room = toCenter.magnitude * 0.4f;
          var inset = Mathf.Min(room, l * LayerInset + jag * MeshKit.Hash(seed + l * 13, i) * 0.9f);
          points[i] = ring[i] + toCenter.normalized * inset;
        }
        rings.Add(points);
      }
      Swatch[] faces = dark ? new[] { Swatch.RockFaceDark, Swatch.RockFaceDark, Swatch.RockFace } : new[] { Swatch.RockFaceDark, Swatch.RockFace, Swatch.RockFaceLight };
      // Kenney 절벽 조각이 있으면 층마다 둘레 조각을 늘인 바위 블록으로 쌓는다(윗면 풀 턱이 층을 만든다). 판정은 그대로다.
      var dressed = Dressing.Collecting && Dressing.Has(DressKind.Cliff);
      for (var l = 0; l < layers; l++) {
        var bottom = height * l / layers;
        var top = height * (l + 1) / layers * (0.92f + 0.08f * MeshKit.Hash(seed, 900 + l));
        var from = rings[l];
        var to = rings[l + 1];
        for (var i = 0; i < from.Length; i++) {
          var j = (i + 1) % from.Length;
          var a = new Vector3(from[i].x, bottom, from[i].y);
          var b = new Vector3(from[j].x, bottom, from[j].y);
          var c = new Vector3(to[j].x, top, to[j].y);
          var d = new Vector3(to[i].x, top, to[i].y);
          var outward = new Vector3(from[i].x + from[j].x, 0, from[i].y + from[j].y) * 0.5f - new Vector3(center.x, 0, center.y);
          if (dressed) {
            var along = b - a;
            var length = along.magnitude;
            if (length > 0.05f) {
              var direction = along / length;
              var normal = new Vector3(direction.z, 0, -direction.x);
              if (Vector3.Dot(normal, outward) < 0f) {
                normal = -normal;
              }
              const float Depth = 3.6f;
              var mid = (a + b) * 0.5f - normal * (Depth * 0.5f - 0.15f);
              var yaw = Mathf.Atan2(-direction.z, direction.x) * Mathf.Rad2Deg;
              Dressing.Add(DressKind.Cliff, new Vector3(mid.x, bottom, mid.z), seed * 7919 + l * 977 + i, new Vector3(length + 0.6f, top - bottom, Depth), yaw);
              // 아래층 바깥 가장자리에 바위, 맨 위층 가장자리 안쪽에 나무를 듬성듬성(판정 경계 안쪽).
              var edge = (a + b) * 0.5f - normal * 1.4f;
              if (l == 0 && MeshKit.Hash(seed, 3000 + i) < 0.22f) {
                Dressing.Add(DressKind.RockLarge, new Vector3(edge.x, 0f, edge.z), seed * 131 + i, Vector3.one * 0.65f);
              }
              if (l == layers - 1 && MeshKit.Hash(seed, 5000 + i) < 0.12f) {
                var inside = (a + b) * 0.5f - normal * 2.2f;
                ForestKit.DressTree(new Vector3(inside.x, top, inside.z), seed * 977 + i, ForestKit.TreeState.Full);
              }
            }
          }
          var shade = faces[Mathf.Min(l * faces.Length / layers, faces.Length - 1)];
          k.Paint(MeshKit.Hash(seed, l * 977 + i) < 0.22f ? Swatch.RockFaceLight : shade);
          k.Quad(a, b, c, d, outward);
        }
      }
      // 윗면: 맨 위 고리를 가운데 점으로 덮는다.
      var last = rings[layers];
      var cap = new Vector3(center.x, height, center.y);
      k.Paint(dark ? Swatch.RockFace : Swatch.GrassDark);
      for (var i = 0; i < last.Length; i++) {
        var j = (i + 1) % last.Length;
        var top = height * (0.92f + 0.08f * MeshKit.Hash(seed, 900 + layers - 1));
        k.Tri(new Vector3(last[i].x, top, last[i].y), new Vector3(last[j].x, top, last[j].y), new Vector3(cap.x, top, cap.z), Vector3.up);
      }
    }

    /// <summary>MapSpec.Roads 조각마다 아스팔트, 가장자리 연석, 가운데 노란 점선, 양옆 흰 선.</summary>
    public static void Roads(MeshKit k, MapSpec map) {
      foreach (var road in map.Roads ?? System.Array.Empty<MapShape>()) {
        var center = new Vector3(road.Center.X.AsFloat, 0f, road.Center.Z.AsFloat);
        var yaw = road.Yaw.AsFloat;
        var half = new Vector2(road.HalfLength.AsFloat, road.HalfWidth.AsFloat);
        k.Push(center, new Vector3(0, yaw, 0));
        k.Paint(Swatch.Concrete);
        k.Quad(new Vector3(-half.x - CurbWidth, 0.035f, -half.y - CurbWidth), new Vector3(half.x + CurbWidth, 0.035f, -half.y - CurbWidth),
          new Vector3(half.x + CurbWidth, 0.035f, half.y + CurbWidth), new Vector3(-half.x - CurbWidth, 0.035f, half.y + CurbWidth), Vector3.up);
        k.Paint(Swatch.Asphalt);
        k.Quad(new Vector3(-half.x - half.y, 0.045f, -half.y), new Vector3(half.x + half.y, 0.045f, -half.y),
          new Vector3(half.x + half.y, 0.045f, half.y), new Vector3(-half.x - half.y, 0.045f, half.y), Vector3.up);
        k.Paint(Swatch.LaneWhite);
        foreach (var side in new[] { -1f, 1f }) {
          var z = side * (half.y - 0.45f);
          k.Quad(new Vector3(-half.x, 0.055f, z - LaneWidth / 2), new Vector3(half.x, 0.055f, z - LaneWidth / 2),
            new Vector3(half.x, 0.055f, z + LaneWidth / 2), new Vector3(-half.x, 0.055f, z + LaneWidth / 2), Vector3.up);
        }
        k.Paint(Swatch.LaneYellow);
        for (var x = -half.x; x < half.x; x += LaneDash + LaneGap) {
          var end = Mathf.Min(x + LaneDash, half.x);
          k.Quad(new Vector3(x, 0.06f, -LaneWidth / 2), new Vector3(end, 0.06f, -LaneWidth / 2), new Vector3(end, 0.06f, LaneWidth / 2), new Vector3(x, 0.06f, LaneWidth / 2), Vector3.up);
        }
        k.Pop();
        if (Dressing.Collecting && Dressing.Has(DressKind.RoadProp)) {
          var turn = Quaternion.Euler(0, yaw, 0);
          var step = 18f;
          var idx = 0;
          for (var x = -half.x + 6f; x <= half.x - 6f; x += step, idx++) {
            var side = idx % 2 == 0 ? 1f : -1f;
            var spot = center + turn * new Vector3(x, 0f, side * (half.y + CurbWidth + 0.9f));
            var fp = new Photon.Deterministic.FPVector3(Photon.Deterministic.FP.FromFloat_UNSAFE(spot.x), default, Photon.Deterministic.FP.FromFloat_UNSAFE(spot.z));
            var blocked = false;
            foreach (var other in map.Roads ?? System.Array.Empty<MapShape>()) {
              if (other.Center != road.Center && other.Distance(fp).AsFloat <= 1f) {
                blocked = true;
                break;
              }
            }
            if (blocked == false && Inside(map.Bridges, spot.x, spot.z) == false && Inside(map.BaseAreas, spot.x, spot.z) == false) {
              Dressing.Add(DressKind.RoadProp, spot, KenneyAssetMap.Seed(spot, 801 + idx), null, yaw + (side > 0 ? 180f : 0f));
            }
          }
        }
      }
    }

    public static float HighGroundHeight => VisualProfile.Current.HighGroundHeight;

    /// <summary>
    /// 고지대 윗면(평지)과 비탈길. 판정은 2D라 지상 유닛은 시뮬레이션에서 높이 0이다. 화면에서만 이 높이로 올린다.
    /// </summary>
    public static void HighGrounds(MeshKit k, MapSpec map) {
      var height = HighGroundHeight;
      for (var g = 0; map.HighGrounds != null && g < map.HighGrounds.Length; g++) {
        var ground = map.HighGrounds[g];
        var center = new Vector3(ground.Center.X.AsFloat, height, ground.Center.Z.AsFloat);
        var radius = ground.Radius.AsFloat + CliffThicknessView;
        const int Sides = 20;
        k.Paint(Swatch.Concrete);
        for (var i = 0; i < Sides; i++) {
          var a = Mathf.PI * 2f * i / Sides;
          var b = Mathf.PI * 2f * (i + 1) / Sides;
          k.Tri(center, center + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius, center + new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b)) * radius, Vector3.up);
          k.Paint(Swatch.RockFace);
          k.Quad(center + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius, center + new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b)) * radius,
            new Vector3(center.x, 0, center.z) + new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b)) * radius, new Vector3(center.x, 0, center.z) + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius,
            new Vector3(Mathf.Cos((a + b) / 2), 0, Mathf.Sin((a + b) / 2)));
          k.Paint(Swatch.Concrete);
        }
      }
      foreach (var ramp in map.Ramps ?? System.Array.Empty<MapShape>()) {
        var g = NearestHighGround(map, new Vector2(ramp.Center.X.AsFloat, ramp.Center.Z.AsFloat));
        if (g < 0) {
          continue;
        }
        var ground = map.HighGrounds[g];
        var hub = new Vector2(ground.Center.X.AsFloat, ground.Center.Z.AsFloat);
        var mid = new Vector2(ramp.Center.X.AsFloat, ramp.Center.Z.AsFloat);
        var outward = (mid - hub).normalized;
        var tangent = new Vector2(-outward.y, outward.x) * ramp.HalfLength.AsFloat;
        var inner = hub + outward * (ground.Radius.AsFloat - 0.2f);
        var outer = hub + outward * (ground.Radius.AsFloat + CliffThicknessView + 3f);
        k.Paint(Swatch.Dirt);
        k.Quad(new Vector3(inner.x - tangent.x, height + 0.02f, inner.y - tangent.y), new Vector3(inner.x + tangent.x, height + 0.02f, inner.y + tangent.y),
          new Vector3(outer.x + tangent.x, 0.03f, outer.y + tangent.y), new Vector3(outer.x - tangent.x, 0.03f, outer.y - tangent.y), Vector3.up);
      }
    }

    const float CliffThicknessView = 3f;

    static int NearestHighGround(MapSpec map, Vector2 point) {
      var best = -1;
      var bestDistance = float.MaxValue;
      for (var i = 0; map.HighGrounds != null && i < map.HighGrounds.Length; i++) {
        var distance = (new Vector2(map.HighGrounds[i].Center.X.AsFloat, map.HighGrounds[i].Center.Z.AsFloat) - point).magnitude;
        if (distance < bestDistance) {
          best = i;
          bestDistance = distance;
        }
      }
      return best;
    }

    /// <summary>화면에서 지상 유닛·건물을 올릴 높이. 고지대 위는 윗면 높이, 비탈은 바깥으로 갈수록 낮아진다.</summary>
    public static float SurfaceHeight(MapSpec map, float x, float z) {
      for (var i = 0; map.HighGrounds != null && i < map.HighGrounds.Length; i++) {
        var ground = map.HighGrounds[i];
        var distance = new Vector2(x - ground.Center.X.AsFloat, z - ground.Center.Z.AsFloat).magnitude;
        var radius = ground.Radius.AsFloat;
        if (distance <= radius) {
          return HighGroundHeight;
        }
        if (distance <= radius + CliffThicknessView + 3f) {
          var point = new Photon.Deterministic.FPVector3(Photon.Deterministic.FP.FromFloat_UNSAFE(x), Photon.Deterministic.FP._0, Photon.Deterministic.FP.FromFloat_UNSAFE(z));
          foreach (var ramp in map.Ramps ?? System.Array.Empty<MapShape>()) {
            if (ramp.Distance(point).AsFloat <= 3f) {
              return HighGroundHeight * Mathf.Clamp01(1f - (distance - radius) / (CliffThicknessView + 3f));
            }
          }
        }
      }
      return 0f;
    }

    /// <summary>다리 상판 아래 교각. 수면까지 내려간다. 다리 프리팹은 상판과 난간이다.</summary>
    public static void BridgePiers(MeshKit k, MapSpec map) {
      foreach (var bridge in map.Bridges ?? System.Array.Empty<MapShape>()) {
        var rect = Extent(bridge);
        var acrossX = rect.width < rect.height;
        var span = acrossX ? rect.width : rect.height;
        var deck = acrossX ? rect.height : rect.width;
        foreach (var t in new[] { -0.28f, 0.28f }) {
          var offset = span * t;
          var center = new Vector3(rect.center.x + (acrossX ? offset : 0f), WaterLevel - 0.5f, rect.center.y + (acrossX ? 0f : offset));
          var size = acrossX ? new Vector3(1.4f, 0, deck * 0.8f) : new Vector3(deck * 0.8f, 0, 1.4f);
          var height = -0.25f - (WaterLevel - 0.5f);
          k.Paint(Swatch.ConcreteDark);
          k.Box(center + Vector3.up * height / 2, new Vector3(size.x, height, size.z));
          if (Dressing.Collecting && Dressing.Has(DressKind.BridgePillar)) {
            Dressing.Add(DressKind.BridgePillar, center, KenneyAssetMap.Seed(center, 911), Vector3.one * 0.35f, acrossX ? 90f : 0f);
          }
        }
      }
    }
  }
}
