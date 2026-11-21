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
      var pBiome = BiomeOf(Dressing.RoadMask);
      var desert = pBiome == Biome.Desert;
      if (desert && Dressing.Collecting && NewBundleArt.CanyonFeature(Dressing.RoadMask, shape, height, seed)) return;
      if (desert) {
        height *= 0.48f;
        jag *= 1.65f;
      }
      if (!desert && dark == false && VisualProfile.Current.FieldLook && Dressing.Collecting && Dressing.Has(DressKind.CitySmall)) {
        CityLot(k, shape, seed);
        return;
      }
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
      // 29 2-1: Kenney 디오라마 깔끔한 단색 슬래브 (옆면 #9AA5B8, 윗면 잔디 #7BE5BA)
      Swatch[] faces = desert ? new[] { Swatch.CanyonMid, Swatch.CliffMid, Swatch.CliffLight }
        : dark ? new[] { Swatch.ConcreteDark, Swatch.ConcreteDark, Swatch.ConcreteDark } : new[] { Swatch.ConcreteDark, Swatch.ConcreteDark, Swatch.Concrete };
      // Kenney 절벽 조각이 있으면 층마다 둘레 조각을 늘인 바위 블록으로 쌓는다(윗면 풀 턱이 층을 만든다). 판정은 그대로다.
      var dressed = !desert && Dressing.Collecting && Dressing.Has(DressKind.Cliff);
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
          // 성능(항목 25-1): Kenney 절벽 블록은 둘레 두 칸을 한 블록으로 늘여 놓는다(블록 수 절반). 홀수 끝 칸은 혼자 놓는다.
          if (dressed && i % 2 == 0) {
            var k2 = i + 2 < from.Length ? i + 2 : (i + 1) % from.Length;
            b = new Vector3(from[k2].x, bottom, from[k2].y);
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
              // 단마다 밝기를 달리해 단색 면에서도 층 경계가 보이게 한다(항목 27 K): 아래 단 어둡게, 위 단 밝게.
              var tier = layers > 1 ? (float)l / (layers - 1) : 1f;
              var shadeTier = 1f - VisualProfile.Current.CliffTierShade * (1f - tier);
              Dressing.Add(DressKind.Cliff, new Vector3(mid.x, bottom, mid.z), seed * 7919 + l * 977 + i, new Color(shadeTier, shadeTier, shadeTier, 1f), new Vector3(length + (VisualProfile.Current.UrbanDecor ? 0.1f : 0.6f), top - bottom, Depth), yaw);
              // 아래층 바깥 가장자리에 바위, 맨 위층 가장자리 안쪽에 나무를 듬성듬성(판정 경계 안쪽).
              var edge = (a + b) * 0.5f - normal * 1.4f;
              if (VisualProfile.Current.UrbanDecor == false && l == 0 && MeshKit.Hash(seed, 3000 + i) < 0.14f) {
                Dressing.Add(DressKind.RockLarge, new Vector3(edge.x, 0f, edge.z), seed * 131 + i, Vector3.one * 0.65f);
              }
              if (VisualProfile.Current.UrbanDecor == false && l == layers - 1 && MeshKit.Hash(seed, 5000 + i) < 0.12f) {
                var inside = (a + b) * 0.5f - normal * 2.2f;
                ForestKit.DressTree(new Vector3(inside.x, top, inside.z), seed * 977 + i, ForestKit.TreeState.Full);
              }
            }
          }
          if (dressed == false) {
            b = new Vector3(from[j].x, bottom, from[j].y);
            var shade = faces[Mathf.Min(l * faces.Length / layers, faces.Length - 1)];
            k.Paint(shade);
            k.Quad(a, b, c, d, outward);
          }
        }
      }
      // 장식 숲(항목 27 2-4): 통과 불가 대지 윗면을 3.5m 격자로 채운다. 판정 없음, 걷지 않는 땅이다.
      var lastRing = rings[layers];
      if (dressed && VisualProfile.Current.PlateauTrees > 0f && dark == false) {
        var minP = lastRing[0];
        var maxP = lastRing[0];
        foreach (var q in lastRing) {
          minP = Vector2.Min(minP, q);
          maxP = Vector2.Max(maxP, q);
        }
        var topY = height * (0.92f + 0.08f * MeshKit.Hash(seed, 900 + layers - 1));
        var n = 0;
        for (var x = minP.x + 2f; x < maxP.x - 1f; x += 3.5f) {
          for (var z = minP.y + 2f; z < maxP.y - 1f; z += 3.5f, n++) {
            var spot = new Vector2(x + (MeshKit.Hash(seed, 7000 + n) - 0.5f) * 2f, z + (MeshKit.Hash(seed, 8000 + n) - 0.5f) * 2f);
            if (MeshKit.Hash(seed, 9000 + n) < VisualProfile.Current.PlateauTrees && InsidePolygon(lastRing, spot, 1.5f)) {
              ForestKit.DressTree(new Vector3(spot.x, topY, spot.y), seed * 1301 + n, ForestKit.TreeState.Full);
            }
          }
        }
      }
      // 항목 36 시각 교정: 건물 블록 둘레 보도블록 패드(높이 0.15m, 밝은 회색 콘크리트). 도로보다 높아 구획이 나뉜다.
      if (dressed && dark == false && VisualProfile.Current.UrbanDecor && VisualProfile.Current.FieldLook == false) {
        var ringTop = rings[layers];
        var apron = new Vector2[ringTop.Length];
        for (var i = 0; i < ringTop.Length; i++) {
          var away = (ringTop[i] - center).normalized;
          apron[i] = ringTop[i] + away * 1.8f;
        }
        for (var i = 0; i < ringTop.Length; i++) {
          var j = (i + 1) % ringTop.Length;
          var inner = new Vector3(ringTop[i].x, 0.15f, ringTop[i].y);
          var innerNext = new Vector3(ringTop[j].x, 0.15f, ringTop[j].y);
          var outer = new Vector3(apron[i].x, 0.15f, apron[i].y);
          var outerNext = new Vector3(apron[j].x, 0.15f, apron[j].y);
          k.Paint(Swatch.Concrete);
          k.Quad(inner, innerNext, outerNext, outer, Vector3.up);
          // 바깥 연석 옆면.
          k.Paint(Swatch.ConcreteDark);
          k.Quad(new Vector3(outer.x, 0f, outer.z), new Vector3(outerNext.x, 0f, outerNext.z), outerNext, outer, new Vector3(apron[i].x + apron[j].x, 0, apron[i].y + apron[j].y) * 0.5f - new Vector3(center.x, 0, center.y));
        }
      }
      // 윗면: 맨 위 고리를 가운데 점으로 덮는다.
      var last = rings[layers];
      var cap = new Vector3(center.x, height, center.y);
      var capSwatch = desert ? Swatch.CliffLight
        : dark ? Swatch.RockFace
        : pBiome == Biome.Urban ? Swatch.Asphalt
        : pBiome == Biome.Strait ? Swatch.Boulder
        : ArtPalette.Turf(Swatch.Grass);
      k.Paint(capSwatch);
      for (var i = 0; i < last.Length; i++) {
        var j = (i + 1) % last.Length;
        var top = height * (0.92f + 0.08f * MeshKit.Hash(seed, 900 + layers - 1));
        k.Tri(new Vector3(last[i].x, top, last[i].y), new Vector3(last[j].x, top, last[j].y), new Vector3(cap.x, top, cap.z), Vector3.up);
      }
    }

    /// <summary>야전 룩: 지금 지형에 놓은 건물 자리(겹침 검사용). WorldView가 지형을 만들기 전에 비운다.</summary>
    public static readonly List<(Vector2 Center, float Radius)> Lots = new List<(Vector2, float)>();
    const float LotGap = 3f;

    /// <summary>
    /// 야전 룩(2026-11-15 사용자 지시): 통과 불가 대지를 회전된 건물 조각 고리로 쌓지 않는다. 대지 모양(사각형)의 축에 맞춘 격자 칸마다
    /// 진짜 팩 건물 1채를 반듯하게(0/90/180/270도) 놓는다. 건물끼리 3m 이상 띄우고(이웃 대지와도), 길 폭+4m 안·도시 건물·본진·다리·물가는 피한다.
    /// 바닥은 땅 높이의 짙은 올리브 판 하나(벽·층 없음).
    /// </summary>
    static void CityLot(MeshKit k, MapShape shape, int seed) {
      var map = Dressing.RoadMask;
      var center = new Vector2(shape.Center.X.AsFloat, shape.Center.Z.AsFloat);
      var circle = shape.IsCircle;
      var halfL = circle ? shape.Radius.AsFloat * 0.7f : shape.HalfLength.AsFloat;
      var halfW = circle ? shape.Radius.AsFloat * 0.7f : shape.HalfWidth.AsFloat;
      var yaw = circle ? 0f : shape.Yaw.AsFloat;
      var rot = Quaternion.Euler(0f, yaw, 0f);
      var axisL = rot * Vector3.right;
      var axisW = rot * Vector3.forward;
      // 낮은 대지 판(높이 0.03m): 바이옴별 바닥색(사막=모래, 시가전=짙은 아스팔트 콘크리트(흰 얼룩 제거), 해협=암반, 표준=잔디).
      var biome = BiomeOf(map);
      var lotSwatch = biome == Biome.Desert ? Swatch.Sand
        : biome == Biome.Urban ? Swatch.Asphalt
        : biome == Biome.Archipelago ? Swatch.Sand
        : biome == Biome.Strait ? Swatch.Rock
        : ArtPalette.Turf(Swatch.GrassDark);
      k.Paint(lotSwatch);
      var ring = Outline(shape, OutlineSpacing);
      for (var i = 0; i < ring.Count; i++) {
        var a = ring[i];
        var b = ring[(i + 1) % ring.Count];
        k.Tri(new Vector3(a.x, 0.03f, a.y), new Vector3(b.x, 0.03f, b.y), new Vector3(center.x, 0.03f, center.y), Vector3.up);
      }
      const float Cell = 12f;
      var countL = Mathf.Max(0, Mathf.FloorToInt((halfL * 2f + LotGap) / (Cell + LotGap)));
      var countW = Mathf.Max(0, Mathf.FloorToInt((halfW * 2f + LotGap) / (Cell + LotGap)));
      var cellL = countL > 0 ? Cell : halfL * 2f - 1f;
      var cellW = countW > 0 ? Cell : halfW * 2f - 1f;
      countL = Mathf.Max(1, countL);
      countW = Mathf.Max(1, countW);
      if (cellL < 5f || cellW < 5f) {
        return;
      }
      var placed = 0;
      for (var i = 0; i < countL && placed < 16; i++) {
        for (var j = 0; j < countW && placed < 16; j++) {
          var offL = (i - (countL - 1) * 0.5f) * (cellL + LotGap);
          var offW = (j - (countW - 1) * 0.5f) * (cellW + LotGap);
          var world = new Vector3(center.x, 0f, center.y) + axisL * offL + axisW * offW;
          var spot = new Vector2(world.x, world.z);
          var radius = Mathf.Sqrt(cellL * cellL + cellW * cellW) * 0.5f;
          if (Lots.Exists(l => (l.Center - spot).magnitude < l.Radius + radius + LotGap) || Blocked(map, world, radius)) {
            continue;
          }
          // 가까운 길 쪽을 보게 0/90/180/270 중에서 고른다.
          var face = yaw + FaceRoad(map, world, yaw);
          var kind = MeshKit.Hash(seed, 300 + i * 7 + j) < 0.6f ? DressKind.CitySmall : DressKind.CityMedium;
          // 사막에서는 초록 숲 대신 바위, 시가전에서는 100% 고밀도 건물, 표준/다도해/해협에서는 33% 작은 숲.
          if (biome != Biome.Urban && MeshKit.Hash(seed, 400 + i * 7 + j) < 0.33f) {
            if (biome == Biome.Desert) {
              for (var r = 0; r < 3; r++) {
                var off = new Vector3((MeshKit.Hash(seed + i * 13 + j, 30 + r) - 0.5f) * cellL * 0.7f, 0f, (MeshKit.Hash(seed + i * 13 + j, 40 + r) - 0.5f) * cellW * 0.7f);
                Dressing.Add(DressKind.RockSmall, world + off, seed * 11 + r);
              }
            } else {
              Grove(world, cellL, cellW, seed * 211 + i * 13 + j);
            }
            Lots.Add((spot, radius));
            continue;
          }
          if (Dressing.AddFitted(kind, world, face, cellL, cellW, seed * 131 + i * 17 + j, 0.92f)) {
            Lots.Add((spot, radius));
            placed++;
          }
        }
      }
    }

    /// <summary>빈 칸 작은 숲: 팩 나무 3~4그루, 덤불·바위 몇 개, 한쪽 변에 낮은 나무 울타리.</summary>
    static void Grove(Vector3 center, float length, float width, int seed) {
      var count = 3 + (MeshKit.Hash(seed, 1) < 0.5f ? 1 : 0);
      for (var n = 0; n < count; n++) {
        var offset = new Vector3((MeshKit.Hash(seed, 10 + n) - 0.5f) * length * 0.7f, 0f, (MeshKit.Hash(seed, 20 + n) - 0.5f) * width * 0.7f);
        Dressing.Add(n % 2 == 0 ? DressKind.Conifer : DressKind.Broadleaf, center + offset, seed * 7 + n);
      }
      for (var n = 0; n < 3; n++) {
        var offset = new Vector3((MeshKit.Hash(seed, 30 + n) - 0.5f) * length * 0.8f, 0f, (MeshKit.Hash(seed, 40 + n) - 0.5f) * width * 0.8f);
        Dressing.Add(n == 2 ? DressKind.RockSmall : DressKind.Bush, center + offset, seed * 11 + n);
      }
      if (Dressing.Has(DressKind.BaseWall)) {
        for (var x = -length * 0.5f + 2f; x <= length * 0.5f - 2f; x += 4.2f) {
          Dressing.Add(DressKind.BaseWall, center + new Vector3(x, 0f, -width * 0.5f + 0.5f), seed * 13 + (int)x, null, 0f);
        }
      }
    }

    static bool Blocked(MapSpec map, Vector3 world, float radius) {
      if (map == null) {
        return false;
      }
      var p = new Photon.Deterministic.FPVector3(Photon.Deterministic.FP.FromFloat_UNSAFE(world.x), default, Photon.Deterministic.FP.FromFloat_UNSAFE(world.z));
      bool Near(MapShape[] shapes, float margin) {
        foreach (var s in shapes ?? System.Array.Empty<MapShape>()) {
          if (s.Distance(p).AsFloat < margin) {
            return true;
          }
        }
        return false;
      }
      if (Near(map.Roads, Dressing.RoadClearance + radius) || Near(map.BaseAreas, radius) || Near(map.Bridges, radius + 4f) || Near(map.WaterGates, radius + 4f)) {
        return true;
      }
      foreach (var city in map.CityBuildings ?? System.Array.Empty<CityBuildingPlacement>()) {
        if (city.Shape.Distance(p).AsFloat < radius + LotGap) {
          return true;
        }
      }
      return TerrainKit.RiverDistance(TerrainKit.RiverPaths(map), new Vector2(world.x, world.z)) < radius + 3f;
    }

    static float FaceRoad(MapSpec map, Vector3 world, float yaw) {
      if (map?.Roads == null || map.Roads.Length == 0) {
        return 0f;
      }
      var best = Vector3.zero;
      var bestDistance = float.MaxValue;
      foreach (var road in map.Roads) {
        var c = new Vector3(road.Center.X.AsFloat, 0f, road.Center.Z.AsFloat);
        var dir = Quaternion.Euler(0f, road.Yaw.AsFloat, 0f) * Vector3.right;
        var t = Mathf.Clamp(Vector3.Dot(world - c, dir), -road.HalfLength.AsFloat, road.HalfLength.AsFloat);
        var q = c + dir * t;
        var distance = (q - world).sqrMagnitude;
        if (distance < bestDistance) {
          bestDistance = distance;
          best = q - world;
        }
      }
      var local = Quaternion.Euler(0f, -yaw, 0f) * best;
      return Mathf.Abs(local.x) > Mathf.Abs(local.z) ? (local.x > 0f ? 90f : 270f) : (local.z > 0f ? 0f : 180f);
    }

    /// <summary>점이 다각형 안쪽이고 가장자리에서 margin 이상 떨어져 있는지(짝수-홀수 규칙 + 꼭짓점 거리 근사).</summary>
    static bool InsidePolygon(Vector2[] ring, Vector2 point, float margin) {
      var inside = false;
      for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++) {
        if ((ring[i].y > point.y) != (ring[j].y > point.y) && point.x < (ring[j].x - ring[i].x) * (point.y - ring[i].y) / (ring[j].y - ring[i].y + 1e-6f) + ring[i].x) {
          inside = !inside;
        }
      }
      if (inside == false) {
        return false;
      }
      foreach (var q in ring) {
        if ((q - point).sqrMagnitude < margin * margin) {
          return false;
        }
      }
      return true;
    }

    /// <summary>중앙 암시장 둘레 순환로: 울타리 바깥 0.6m부터 5m 폭. 들어오는 길은 바깥 가장자리 1.5m 안에서 끝난다.</summary>
    public const float RingInner = TraderView.Half + 0.6f;
    public const float RingWidth = 5f;
    public static float RingOuter => RingInner + RingWidth;
    const float ShoulderWidth = 2.4f;
    const float CurbPiece = 2f;

    static bool OnOtherRoad(MapSpec map, MapShape road, Vector3 point) {
      var fp = new Photon.Deterministic.FPVector3(Photon.Deterministic.FP.FromFloat_UNSAFE(point.x), default, Photon.Deterministic.FP.FromFloat_UNSAFE(point.z));
      foreach (var other in map.Roads) {
        if (other.Center != road.Center && other.Distance(fp).AsFloat < 0.9f) {
          return true;
        }
      }
      return false;
    }

    /// <summary>길 하나를 암시장 순환로 바깥 조각들로 자른다(각 조각의 중심, 반길이). 암시장이 없으면 그대로 한 조각.</summary>
    public static List<(Vector3 Center, float Half)> RoadPieces(MapSpec map, MapShape road) {
      var center = new Vector3(road.Center.X.AsFloat, 0f, road.Center.Z.AsFloat);
      var half = road.HalfLength.AsFloat;
      var result = new List<(Vector3, float)>();
      if (map.HasTrader == false) {
        result.Add((center, half));
        return result;
      }
      var dir = Quaternion.Euler(0f, road.Yaw.AsFloat, 0f) * Vector3.right;
      var t = new Vector3(map.TraderPosition.X.AsFloat, 0f, map.TraderPosition.Z.AsFloat);
      var r = RingOuter - 1.5f;
      var a = center - dir * half - t;
      var d = dir * (half * 2f);
      // Liang-Barsky: 선분 중 정사각형 [-r, r]² 안에 드는 구간 [t0, t1].
      var t0 = 0f;
      var t1 = 1f;
      var inside = true;
      foreach (var (p, q) in new[] { (-d.x, a.x + r), (d.x, r - a.x), (-d.z, a.z + r), (d.z, r - a.z) }) {
        if (Mathf.Abs(p) < 1e-5f) {
          if (q < 0f) {
            inside = false;
          }
          continue;
        }
        var u = q / p;
        if (p < 0f) {
          t0 = Mathf.Max(t0, u);
        } else {
          t1 = Mathf.Min(t1, u);
        }
      }
      if (inside == false || t0 >= t1) {
        result.Add((center, half));
        return result;
      }
      var start = center - dir * half;
      foreach (var (from, to) in new[] { (0f, t0), (t1, 1f) }) {
        var length = (to - from) * half * 2f;
        if (length > 1f) {
          result.Add((start + dir * ((from + to) * half), length * 0.5f));
        }
      }
      return result;
    }

    static void FieldRoad(MeshKit k, MapSpec map, MapShape road, Vector3 center, float yaw, Vector2 half, int roadIndex) {
      // 야전 룩 아스팔트: 둥근 끝 마개, 양옆 높인 연석(0.14m)과 자갈 갓길(2.4m), 흰 가장자리선·중앙 점선. 겹치는 길끼리 높이를 조금씩 달리한다.
      var y = 0.06f + (roadIndex % 8) * 0.003f;
      k.Push(center, new Vector3(0, yaw, 0));
      k.Paint(Swatch.RockDark);
      foreach (var side in new[] { -1f, 1f }) {
        var inner = side * (half.y + CurbWidth);
        var outer = side * (half.y + CurbWidth + ShoulderWidth);
        k.Quad(new Vector3(-half.x, 0.03f, Mathf.Min(inner, outer)), new Vector3(half.x, 0.03f, Mathf.Min(inner, outer)), new Vector3(half.x, 0.03f, Mathf.Max(inner, outer)), new Vector3(-half.x, 0.03f, Mathf.Max(inner, outer)), Vector3.up);
      }
      k.Paint(Swatch.Concrete);
      k.Quad(new Vector3(-half.x, y - 0.01f, -half.y - CurbWidth), new Vector3(half.x, y - 0.01f, -half.y - CurbWidth), new Vector3(half.x, y - 0.01f, half.y + CurbWidth), new Vector3(-half.x, y - 0.01f, half.y + CurbWidth), Vector3.up);
      // 높인 연석은 2m 조각으로 놓고, 다른 길 아스팔트 위(교차로)에 걸리는 조각은 뺀다.
      var turn = Quaternion.Euler(0f, yaw, 0f);
      var from = -half.x + half.y * 0.6f;
      var to = half.x - half.y * 0.6f;
      foreach (var side in new[] { -1f, 1f }) {
        for (var x = from; x < to; x += CurbPiece) {
          var len = Mathf.Min(CurbPiece, to - x);
          var local = new Vector3(x + len * 0.5f, 0f, side * (half.y + CurbWidth * 0.55f));
          if (OnOtherRoad(map, road, center + turn * local)) {
            continue;
          }
          k.Box(local + Vector3.up * (y + 0.07f), new Vector3(len - 0.04f, 0.14f, CurbWidth * 0.7f));
        }
      }
      k.Paint(Swatch.Asphalt);
      k.Quad(new Vector3(-half.x, y, -half.y), new Vector3(half.x, y, -half.y), new Vector3(half.x, y, half.y), new Vector3(-half.x, y, half.y), Vector3.up);
      k.Disc(new Vector3(-half.x, y, 0f), half.y, 16);
      k.Disc(new Vector3(half.x, y, 0f), half.y, 16);
      k.Paint(Swatch.LaneWhite);
      foreach (var side in new[] { -1f, 1f }) {
        var z = side * (half.y - 0.45f);
        k.Quad(new Vector3(-half.x + half.y, y + 0.01f, z - LaneWidth / 2), new Vector3(half.x - half.y, y + 0.01f, z - LaneWidth / 2), new Vector3(half.x - half.y, y + 0.01f, z + LaneWidth / 2), new Vector3(-half.x + half.y, y + 0.01f, z + LaneWidth / 2), Vector3.up);
      }
      for (var x = -half.x + half.y; x < half.x - half.y; x += LaneDash + LaneGap) {
        var end = Mathf.Min(x + LaneDash, half.x - half.y);
        k.Quad(new Vector3(x, y + 0.012f, -LaneWidth / 2), new Vector3(end, y + 0.012f, -LaneWidth / 2), new Vector3(end, y + 0.012f, LaneWidth / 2), new Vector3(x, y + 0.012f, LaneWidth / 2), Vector3.up);
      }
      k.Pop();
    }

    /// <summary>암시장 울타리를 감싸는 사각 순환로(아스팔트, 안쪽 콘크리트 띠, 양쪽 흰 선). 들어오는 길 끝이 이 위에서 만난다.</summary>
    static void RingRoad(MeshKit k, MapSpec map) {
      var c = new Vector3(map.TraderPosition.X.AsFloat, 0f, map.TraderPosition.Z.AsFloat);
      var i = RingInner;
      var o = RingOuter;
      const float y = 0.088f;
      void Rect(float x0, float z0, float x1, float z1, float lift) {
        k.Quad(c + new Vector3(x0, y + lift, z0), c + new Vector3(x1, y + lift, z0), c + new Vector3(x1, y + lift, z1), c + new Vector3(x0, y + lift, z1), Vector3.up);
      }
      k.Paint(Swatch.Concrete);
      Rect(-i, -i, i, i, -0.005f);
      k.Paint(Swatch.Asphalt);
      Rect(-o, i, o, o, 0f);
      Rect(-o, -o, o, -i, 0f);
      Rect(-o, -i, -i, i, 0f);
      Rect(i, -i, o, i, 0f);
      k.Paint(Swatch.LaneWhite);
      foreach (var e in new[] { i + 0.35f, o - 0.45f }) {
        Rect(-e, e - LaneWidth / 2, e, e + LaneWidth / 2, 0.01f);
        Rect(-e, -e - LaneWidth / 2, e, -e + LaneWidth / 2, 0.01f);
        Rect(-e - LaneWidth / 2, -e, -e + LaneWidth / 2, e, 0.01f);
        Rect(e - LaneWidth / 2, -e, e + LaneWidth / 2, e, 0.01f);
      }
      // 바깥 연석(높인 콘크리트 띠).
      k.Paint(Swatch.Concrete);
      foreach (var s in new[] { -1f, 1f }) {
        k.Box(c + new Vector3(0f, y + 0.06f, s * (o + 0.2f)), new Vector3(o * 2f + 0.8f, 0.14f, 0.4f));
        k.Box(c + new Vector3(s * (o + 0.2f), y + 0.06f, 0f), new Vector3(0.4f, 0.14f, o * 2f));
      }
    }

    /// <summary>MapSpec.Roads 조각마다 아스팔트, 가장자리 연석, 가운데 노란 점선, 양옆 흰 선.</summary>
    public static void Roads(MeshKit k, MapSpec map) {
      var roadIndex = 0;
      var continuous = EnvironmentArt.Uses(map);
      if (continuous) EnvironmentRoads.Build(k, map);
      foreach (var road in continuous ? System.Array.Empty<MapShape>() : map.Roads ?? System.Array.Empty<MapShape>()) {
        var yaw = road.Yaw.AsFloat;
        if (VisualProfile.Current.FieldLook) {
          foreach (var (pieceCenter, pieceHalf) in RoadPieces(map, road)) {
            FieldRoad(k, map, road, pieceCenter, yaw, new Vector2(pieceHalf, road.HalfWidth.AsFloat), roadIndex++);
          }
          continue;
        }
        var center = new Vector3(road.Center.X.AsFloat, 0f, road.Center.Z.AsFloat);
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
        k.Paint(VisualProfile.Current.WhiteLanes ? Swatch.LaneWhite : Swatch.LaneYellow);
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
      if (VisualProfile.Current.FieldLook && map.HasTrader) {
        RingRoad(k, map);
      }
      // 본진 외곽 방벽·검문소(building-kit border-high*)와 기지 보급 텐트(mini-forest tent). 도로·다리와 겹치지 않는 가장자리에만 둔다(항목 19D).
      if (Dressing.Collecting && map.BaseAreas != null) {
        for (var b = 0; b < map.BaseAreas.Length; b++) {
          var area = map.BaseAreas[b];
          var center = new Vector3(area.Center.X.AsFloat, 0f, area.Center.Z.AsFloat);
          var hx = area.HalfLength.AsFloat;
          var hz = area.HalfWidth.AsFloat;
          var signX = center.x < 0f ? 1f : -1f;
          var signZ = center.z < 0f ? 1f : -1f;
          if (Dressing.Has(DressKind.BaseWall)) {
            var postA = center + new Vector3(signX * (hx - 2f), 0f, signZ * (hz * 0.45f));
            var postB = center + new Vector3(signX * (hx * 0.45f), 0f, signZ * (hz - 2f));
            if (Inside(map.Roads, postA.x, postA.z) == false) {
              Dressing.Add(DressKind.BaseWall, postA, 9100 + b * 7, null, 0f);
            }
            if (Inside(map.Roads, postB.x, postB.z) == false) {
              Dressing.Add(DressKind.BaseWall, postB, 9101 + b * 7, null, 90f);
            }
          }
          if (Dressing.Has(DressKind.BaseTent)) {
            var tentSpot = center + new Vector3(-signX * (hx * 0.55f), 0f, signZ * (hz * 0.55f));
            if (Inside(map.Roads, tentSpot.x, tentSpot.z) == false) {
              Dressing.Add(DressKind.BaseTent, tentSpot, 9200 + b * 11, null, b * 90f);
            }
          }
          if (Dressing.Has(DressKind.RoadProp)) {
            var lampA = center + new Vector3(signX * (hx * 0.45f), 0f, -signZ * (hz * 0.45f));
            var lampB = center + new Vector3(-signX * (hx * 0.45f), 0f, signZ * (hz * 0.45f));
            if (Inside(map.Roads, lampA.x, lampA.z) == false) {
              Dressing.Add(DressKind.RoadProp, lampA, 9300 + b * 5, null, b * 90f);
            }
            if (Inside(map.Roads, lampB.x, lampB.z) == false) {
              Dressing.Add(DressKind.RoadProp, lampB, 9301 + b * 5, null, b * 90f + 180f);
            }
          }
          var roadBiome = BiomeOf(map);
          if (roadBiome != Biome.Desert && roadBiome != Biome.Urban && Dressing.Has(DressKind.Bush)) {
            var flowerA = center + new Vector3(signX * (hx * 0.52f), 0f, -signZ * (hz * 0.15f));
            var flowerB = center + new Vector3(-signX * (hx * 0.15f), 0f, signZ * (hz * 0.52f));
            if (Inside(map.Roads, flowerA.x, flowerA.z) == false) {
              Dressing.Add(DressKind.Bush, flowerA, 9400 + b * 7, null, 0f);
            }
            if (Inside(map.Roads, flowerB.x, flowerB.z) == false) {
              Dressing.Add(DressKind.Bush, flowerB, 9401 + b * 7, null, 45f);
            }
          }
          // 29 2-3: 본진 광장 밝은 보도블록 패드 단차 및 마을 기지 소품 (벤치·가로등·상자)
          if (Dressing.Has(DressKind.SuburbanProp)) {
            var propSpotA = center + new Vector3(signX * (hx * 0.35f), 0f, signZ * (hz * 0.25f));
            if (Inside(map.Roads, propSpotA.x, propSpotA.z) == false) {
              Dressing.Add(DressKind.SuburbanProp, propSpotA, 9500 + b * 13, null, b * 90f);
            }
          }
          if (Dressing.Has(DressKind.Prop)) {
            var propSpotB = center + new Vector3(-signX * (hx * 0.35f), 0f, -signZ * (hz * 0.35f));
            if (Inside(map.Roads, propSpotB.x, propSpotB.z) == false) {
              Dressing.Add(DressKind.Prop, propSpotB, 9600 + b * 17, null, b * 90f);
            }
          }
          k.Push(center, Vector3.zero);
          k.Paint(Swatch.Concrete);
          k.Box(new Vector3(0, 0.025f, 0), new Vector3(14f, 0.05f, 14f));
          k.Pop();
        }
      }
    }

    public static float HighGroundHeight => VisualProfile.Current.HighGroundHeight;

    /// <summary>
    /// 고지대 윗면(평지)과 비탈길. 판정은 2D라 지상 유닛은 시뮬레이션에서 높이 0이다. 화면에서만 이 높이로 올린다.
    /// </summary>
    public static void HighGrounds(MeshKit k, MapSpec map) {
      var height = HighGroundHeight;
      var hgBiome = BiomeOf(map);
      if (VisualProfile.Current.FieldLook) {
        var topSw = hgBiome == Biome.Desert ? Swatch.Sand
          : hgBiome == Biome.Urban ? Swatch.Asphalt
          : hgBiome == Biome.Archipelago ? Swatch.Olive
          : hgBiome == Biome.Strait ? Swatch.OliveDark
          : ArtPalette.Turf(Swatch.Grass);
        var slopeSw = hgBiome == Biome.Desert ? Swatch.Dirt
          : hgBiome == Biome.Urban ? Swatch.Black
          : hgBiome == Biome.Archipelago ? Swatch.Sand
          : hgBiome == Biome.Strait ? Swatch.RockDark
          : Swatch.OliveDark;
        // 야전 룩: 고지대(사거리 이점, 판정 그대로)를 바이옴별 사암 메사/도심 고가/열대 언덕/암반 구릉으로 그린다.
        for (var g = 0; map.HighGrounds != null && g < map.HighGrounds.Length; g++) {
          var ground = map.HighGrounds[g];
          var c = new Vector3(ground.Center.X.AsFloat, 0f, ground.Center.Z.AsFloat);
          var top = ground.Radius.AsFloat;
          var foot = top + 7f;
          const int Sides = 28;
          var toe = new Vector3[Sides];
          for (var i = 0; i < Sides; i++) {
            var angle = Mathf.PI * 2f * i / Sides;
            var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            var wobble = 1f + (MeshKit.Hash(g * 97 + i, 3) - 0.5f) * 0.12f;
            var distance = foot * wobble;
            for (var probe = top * wobble; probe < distance; probe += 0.6f) {
              var p = c + direction * probe;
              var spot = new Photon.Deterministic.FPVector3(Photon.Deterministic.FP.FromFloat_UNSAFE(p.x),
                default, Photon.Deterministic.FP.FromFloat_UNSAFE(p.z));
              var roadReached = false;
              foreach (var road in map.Roads ?? System.Array.Empty<MapShape>()) {
                if (road.Distance(spot).AsFloat > CurbWidth + 0.3f) continue;
                roadReached = true;
                break;
              }
              if (roadReached == false) continue;
              distance = Mathf.Max(top * wobble, probe - 0.6f);
              break;
            }
            toe[i] = c + direction * distance + Vector3.up * 0.02f;
          }
          for (var i = 0; i < Sides; i++) {
            var a = Mathf.PI * 2f * i / Sides;
            var b = Mathf.PI * 2f * (i + 1) / Sides;
            var da = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            var db = new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b));
            var wobA = 1f + (MeshKit.Hash(g * 97 + i, 3) - 0.5f) * 0.12f;
            var wobB = 1f + (MeshKit.Hash(g * 97 + (i + 1) % Sides, 3) - 0.5f) * 0.12f;
            k.Paint(topSw);
            k.Tri(c + Vector3.up * height, c + da * top * wobA + Vector3.up * height, c + db * top * wobB + Vector3.up * height, Vector3.up);
            k.Paint(slopeSw);
            k.Quad(c + da * top * wobA + Vector3.up * height, c + db * top * wobB + Vector3.up * height,
              toe[(i + 1) % Sides], toe[i], Vector3.up);
          }
        }
        return;
      }
      var dressed = Dressing.Collecting && Dressing.Has(DressKind.Cliff);
      for (var g = 0; map.HighGrounds != null && g < map.HighGrounds.Length; g++) {
        var ground = map.HighGrounds[g];
        var center = new Vector3(ground.Center.X.AsFloat, height, ground.Center.Z.AsFloat);
        var radius = ground.Radius.AsFloat + (dressed ? 0.2f : CliffThicknessView);
        const int Sides = 24;
        // 고지대 윗면: 깔끔한 민트 잔디(#7BE5BA)로 덮음
        k.Paint(ArtPalette.Turf(Swatch.Grass));
        for (var i = 0; i < Sides; i++) {
          var a = Mathf.PI * 2f * i / Sides;
          var b = Mathf.PI * 2f * (i + 1) / Sides;
          k.Tri(center, center + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius, center + new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b)) * radius, Vector3.up);
          // 얼룩말 메쉬 겹침(Z-fighting) 완전 제거: Kenney 절벽 블록(dressed)이 있을 땐 옛 절차적 갈색 기둥 메쉬 생성을 완전히 생략
          if (dressed == false) {
            k.Paint(Swatch.RockFace);
            k.Quad(center + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius, center + new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b)) * radius,
              new Vector3(center.x, 0, center.z) + new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b)) * radius, new Vector3(center.x, 0, center.z) + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius,
              new Vector3(Mathf.Cos((a + b) / 2), 0, Mathf.Sin((a + b) / 2)));
          }
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
        // 비탈길 상판: 밝은 보도/아스팔트 계열로 깔끔하게 처리
        k.Paint(Swatch.Concrete);
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
      return EnvironmentArt.Elevation(map, x, z);
    }

    /// <summary>다리 상판 아래 교각. 수면까지 내려간다. 다리 프리팹은 상판과 난간이다.</summary>
    public static void BridgePiers(MeshKit k, MapSpec map) {
      foreach (var bridge in map.Bridges ?? System.Array.Empty<MapShape>()) {
        if (map.SealedPerimeter) {
          var center = bridge.Center.ToUnityVector3();
          var length = bridge.HalfLength.AsFloat;
          var depth = -0.25f - WaterLevel;
          k.Push(center, new Vector3(0f, bridge.Yaw.AsFloat, 0f));
          k.Paint(Swatch.ConcreteDark);
          for (var i = -1; i <= 1; i += 2) {
            k.Chamfer(new Vector3(i * length * 0.58f, WaterLevel + depth / 2f, 0f),
              new Vector3(1.65f, depth + 0.5f, bridge.HalfWidth.AsFloat * 1.6f), 0.15f);
          }
          k.Pop();
          continue;
        }
        var rect = Extent(bridge);
        var longStrait = Mathf.Max(rect.width, rect.height) > 20f;
        var acrossX = longStrait ? rect.width >= rect.height : rect.width < rect.height;
        var span = acrossX ? rect.width : rect.height;
        var deck = acrossX ? rect.height : rect.width;
        var stops = longStrait ? new[] { -0.36f, -0.12f, 0.12f, 0.36f } : new[] { -0.28f, 0.28f };
        foreach (var t in stops) {
          var offset = span * t;
          var center = new Vector3(rect.center.x + (acrossX ? offset : 0f), WaterLevel - 0.5f, rect.center.y + (acrossX ? 0f : offset));
          var size = acrossX ? new Vector3(1.6f, 0, deck * 0.85f) : new Vector3(deck * 0.85f, 0, 1.6f);
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
