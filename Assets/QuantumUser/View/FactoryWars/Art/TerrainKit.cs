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
    public enum Biome { Standard, Desert, Urban, Archipelago, Strait }

    public static Biome BiomeOf(MapSpec map) {
      var theme = map != null ? (map.Theme ?? "") : "";
      if (theme.Contains("사막")) return Biome.Desert;
      if (theme.Contains("시가")) return Biome.Urban;
      if (theme.Contains("다도해")) return Biome.Archipelago;
      if (theme.Contains("해협")) return Biome.Strait;
      return Biome.Standard;
    }

    static MapSpec _cachedPathsMap;
    static List<List<RiverPoint>> _cachedPaths;

    static List<List<RiverPoint>> CachedRiverPaths(MapSpec map) {
      if (map != _cachedPathsMap || _cachedPaths == null) {
        _cachedPathsMap = map;
        _cachedPaths = RiverPaths(map);
      }
      return _cachedPaths;
    }

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
    /// <summary>외곽 수로 너비(항목 24). 없으면 0이다.</summary>
    public static float Channel(MapSpec map) => map.Channel != null && map.Channel.Length > 0 ? map.ChannelWidth.AsFloat : 0f;

    /// <param name="includeChannel">외곽 수로 네 변(곧은 강)을 넣을지. 운하만 검사하는 테스트는 false를 준다.</param>
    public static List<List<RiverPoint>> RiverPaths(MapSpec map, bool includeChannel = true) {
      var paths = new List<List<RiverPoint>>();
      var pieces = map.River ?? Array.Empty<MapShape>();
      List<RiverPoint> path = null;
      var last = Vector2.zero;
      // 외곽 수로가 있으면 운하는 수로 바깥 끝에서 끊고(그 너머로 뻗지 않게), 수로 네 변을 곧은 강으로 더한다.
      var channel = Channel(map);
      var reach = map.BoundsMax.X.AsFloat + channel + 2f;
      foreach (var piece in pieces) {
        var center = new Vector2(piece.Center.X.AsFloat, piece.Center.Z.AsFloat);
        if (channel > 0f && (Mathf.Abs(center.x) > reach || Mathf.Abs(center.y) > reach)) {
          continue;
        }
        if (path == null || (center - last).magnitude > 12f) {
          path = new List<RiverPoint>();
          paths.Add(path);
        }
        path.Add(new RiverPoint { Position = center, Half = piece.HalfWidth.AsFloat });
        last = center;
      }
      foreach (var strip in channel > 0f && includeChannel ? map.Channel : Array.Empty<MapShape>()) {
        var center = new Vector2(strip.Center.X.AsFloat, strip.Center.Z.AsFloat);
        var alongX = strip.HalfLength.AsFloat >= strip.HalfWidth.AsFloat;
        var length = Mathf.Max(strip.HalfLength.AsFloat, strip.HalfWidth.AsFloat);
        var half = Mathf.Min(strip.HalfLength.AsFloat, strip.HalfWidth.AsFloat);
        var line = new List<RiverPoint>();
        for (var t = -length; t <= length + 0.01f; t += 4f) {
          line.Add(new RiverPoint { Position = center + (alongX ? new Vector2(t, 0f) : new Vector2(0f, t)), Half = half });
        }
        paths.Add(line);
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

    static List<float> Lines(float min, float max, bool margins = true) {
      var lines = new List<float>();
      for (var x = min; x <= max + 0.01f; x += Step) {
        lines.Add(x);
      }
      if (!margins) {
        if (lines[lines.Count - 1] < max - 0.01f) lines.Add(max);
        return lines;
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
    /// <summary>바닥 얼룩 노이즈 0~1. 펄린 두 겹이라 경계가 둥글고 불규칙하다(항목 19C, 네모 얼룩 제거).</summary>
    static float Noise(float x, float z, int seed) {
      var patch = Mathf.Max(6f, VisualProfile.Current.PatchSize);
      var o = seed * 17.37f;
      var n = Mathf.PerlinNoise(x / patch + o, z / patch - o) * 0.7f + Mathf.PerlinNoise(x / (patch * 0.37f) - o, z / (patch * 0.37f) + o) * 0.3f;
      return Mathf.Clamp01((n - 0.5f) * 1.6f + 0.5f);
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

    /// <summary>
    /// 정점 하나의 바닥 색(항목 25-1 + 5개 맵 바이옴 차별화).
    /// 사막(모래 100%, 초록 0%), 시가전(콘크리트·아스팔트 도심), 다도해(황금 해변 + 열대 섬), 해협(암반 해안 + 북부 고지대), 표준(온대 계곡).
    /// </summary>
    public static Color GroundTint(MapSpec map, float x, float z, int seed) {
      var biome = BiomeOf(map);
      var noise = Noise(x, z, seed);
      var outside = x < map.BoundsMin.X.AsFloat || x > map.BoundsMax.X.AsFloat || z < map.BoundsMin.Y.AsFloat || z > map.BoundsMax.Y.AsFloat;
      if (biome == Biome.Desert) {
        if (outside) return new Color(0.58f, 0.45f, 0.28f);
        if (Inside(map.BaseAreas, x, z)) {
          return Color.Lerp(new Color(0.76f, 0.65f, 0.47f), new Color(0.84f, 0.72f, 0.52f), noise);
        }
        // 100% 따뜻한 사막 황금 모래·사구·황토 톤 (초록색 0%).
        var sandLight = new Color(0.91f, 0.80f, 0.56f); // #E8CC8F
        var sandMid = new Color(0.84f, 0.70f, 0.45f);   // #D6B373
        var sandOchre = new Color(0.74f, 0.58f, 0.36f); // #BD945C
        var duneShade = new Color(0.65f, 0.49f, 0.30f); // #A67D4D
        if (NearRoadOrCity(map, x, z, 3.6f)) {
          return Color.Lerp(sandOchre, sandMid, noise);
        }
        return noise < 0.32f ? Color.Lerp(duneShade, sandOchre, noise / 0.32f)
          : noise < 0.72f ? Color.Lerp(sandOchre, sandMid, (noise - 0.32f) / 0.40f)
          : Color.Lerp(sandMid, sandLight, (noise - 0.72f) / 0.28f);
      }
      if (biome == Biome.Urban) {
        if (outside) return new Color(0.16f, 0.17f, 0.19f);
        // 95%+ 중후한 시가지 짙은 콘크리트·아스팔트 격자 (푸른 눈 느낌 제거, 무채색 도심 콘크리트 톤).
        var plaza = new Color(0.38f, 0.39f, 0.41f);
        var concrete = new Color(0.30f, 0.31f, 0.33f);
        var asphalt = new Color(0.21f, 0.22f, 0.24f);
        var slate = new Color(0.17f, 0.18f, 0.20f);
        if (Inside(map.BaseAreas, x, z)) {
          return Color.Lerp(concrete, plaza, noise * 0.6f);
        }
        if (NearRoadOrCity(map, x, z, 5.5f)) {
          return Color.Lerp(concrete, plaza, noise * 0.5f);
        }
        return noise < 0.45f ? Color.Lerp(slate, asphalt, noise / 0.45f) : Color.Lerp(asphalt, concrete, (noise - 0.45f) / 0.55f);
      }
      if (biome == Biome.Archipelago) {
        var shore = RiverDistance(CachedRiverPaths(map), new Vector2(x, z));
        var edgeDist = Mathf.Min(
          Mathf.Min(x - map.BoundsMin.X.AsFloat, map.BoundsMax.X.AsFloat - x),
          Mathf.Min(z - map.BoundsMin.Y.AsFloat, map.BoundsMax.Y.AsFloat - z));
        var coast = Mathf.Min(shore, edgeDist);
        var wetSand = new Color(0.93f, 0.87f, 0.68f);   // #EDE0AD 밝은 백사장
        var warmSand = new Color(0.86f, 0.75f, 0.52f);  // #DBBF85 황금 모래해변
        var tropGrass = new Color(0.31f, 0.57f, 0.25f); // #4F9140 열대 섬 녹지
        var tropDeep = new Color(0.24f, 0.47f, 0.20f);  // #3D7833
        if (coast < 0f) return Color.Lerp(wetSand, new Color(0.75f, 0.67f, 0.48f), Mathf.Clamp01(-coast / 10f));
        if (coast < 9.5f) {
          return Color.Lerp(wetSand, warmSand, Mathf.Clamp01(coast / 9.5f));
        }
        var inland = Color.Lerp(tropDeep, tropGrass, noise);
        if (coast < 16f) {
          return Color.Lerp(warmSand, inland, (coast - 9.5f) / 6.5f);
        }
        return Inside(map.BaseAreas, x, z) ? Color.Lerp(warmSand, new Color(0.68f, 0.66f, 0.56f), 0.55f) : inland;
      }
      if (biome == Biome.Strait) {
        if (outside) return new Color(0.18f, 0.23f, 0.28f);
        var shore = RiverDistance(CachedRiverPaths(map), new Vector2(x, z));
        var rockCoast = new Color(0.47f, 0.51f, 0.55f);
        var cliffSlate = new Color(0.36f, 0.40f, 0.45f);
        var highland = new Color(0.29f, 0.43f, 0.27f);
        var highlandDark = new Color(0.23f, 0.35f, 0.22f);
        var grass = Color.Lerp(highlandDark, highland, noise);
        if (shore < 8.5f) {
          return Color.Lerp(rockCoast, cliffSlate, Mathf.Clamp01(shore / 8.5f));
        }
        if (shore < 13.5f) {
          return Color.Lerp(cliffSlate, grass, (shore - 8.5f) / 5f);
        }
        if (Inside(map.BaseAreas, x, z)) {
          return Color.Lerp(cliffSlate, rockCoast, noise * 0.6f);
        }
        return NearRoadOrCity(map, x, z, 3.2f) ? Color.Lerp(cliffSlate, grass, 0.45f) : grass;
      }
      var profile = VisualProfile.Current;
      var swatch = GroundColor(map, x, z, seed);
      switch (swatch) {
        case Swatch.Grass: return profile.GroundGrass;
        case Swatch.GrassLight: return profile.GroundGrassLight;
        case Swatch.GrassDark: return profile.GroundGrassDark;
        case Swatch.Olive: return Color.Lerp(profile.GroundGrass, profile.GroundDirt, 0.35f);
        case Swatch.Dirt: return profile.GroundDirt;
        case Swatch.DirtDark: return profile.GroundDirtDark;
        case Swatch.Earth: return Color.Lerp(profile.GroundDirt, profile.GroundDirtDark, 0.5f);
        case Swatch.Concrete: return profile.GroundPlaza;
        case Swatch.ConcreteDark: return profile.GroundPlazaDark;
        default: return ArtPalette.ColorOf(swatch);
      }
    }

    static Swatch GroundColor(MapSpec map, float x, float z, int seed) {
      var noise = Noise(x, z, seed);
      var profile = VisualProfile.Current;
      var outside = x < map.BoundsMin.X.AsFloat || x > map.BoundsMax.X.AsFloat || z < map.BoundsMin.Y.AsFloat || z > map.BoundsMax.Y.AsFloat;
      // 닫힌 맵: 경계 밖은 경계 벽 뒤로 어둡게 끊는다.
      if (outside) {
        return Swatch.ConcreteDark;
      }
      if (profile.SolidGround) {
        // 항목 27 K: 본진 내부는 시작지점/건물 중심 14m 밝은 보도블록 패드(#C1C9E4), 외곽은 회보라 바닥(#ABB3CC)
        if (Inside(map.BaseAreas, x, z)) {
          var nearStart = false;
          if (map.StartPositions != null) {
            for (var s = 0; s < map.StartPositions.Length; s++) {
              var dx = x - map.StartPositions[s].X.AsFloat;
              var dz = z - map.StartPositions[s].Z.AsFloat;
              if (dx * dx + dz * dz <= 14f * 14f) {
                nearStart = true;
                break;
              }
            }
          }
          return nearStart ? Swatch.ConcreteDark : Swatch.Concrete;
        }
        return NearRoadOrCity(map, x, z, 3.2f) ? Swatch.Concrete : Swatch.Grass;
      }
      if (profile.NoBuildAlways && Inside(map.NoBuildZones, x, z)) {
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
      // 도로·건물·자원 둘레 2~4m는 흙 색을 자연스럽게 섞는다(항목 19C).
      var fp = new Photon.Deterministic.FPVector3(Photon.Deterministic.FP.FromFloat_UNSAFE(x), default, Photon.Deterministic.FP.FromFloat_UNSAFE(z));
      var nearEdge = false;
      foreach (var r in map.Roads ?? Array.Empty<MapShape>()) {
        if (r.Distance(fp).AsFloat <= 3.2f) {
          nearEdge = true;
          break;
        }
      }
      if (nearEdge == false && map.CityBuildings != null) {
        for (var i = 0; i < map.CityBuildings.Length; i++) {
          if (map.CityBuildings[i].Shape.Distance(fp).AsFloat <= 3.2f) {
            nearEdge = true;
            break;
          }
        }
      }
      if (nearEdge && noise < 0.62f) {
        return noise < 0.34f ? Swatch.Dirt : Swatch.DirtDark;
      }
      if (profile.GroundPalette == 1) {
        return noise < 0.18f ? Swatch.Dirt : noise < 0.42f ? Swatch.GrassLight : noise < 0.72f ? Swatch.Grass : Swatch.GrassDark;
      }
      if (profile.GroundPalette == 2) {
        return noise < 0.28f ? Swatch.Dirt : noise < 0.4f ? Swatch.DirtDark : noise < 0.7f ? Swatch.Olive : Swatch.GrassDark;
      }
      return noise < 0.2f ? Swatch.Dirt : noise < 0.3f ? Swatch.DirtDark : noise < 0.66f ? Swatch.Grass : Swatch.GrassDark;
    }

    /// <summary>도로나 도시 건물 가장자리에서 reach 안인가.</summary>
    /// <summary>시가지 바닥에서 보도(콘크리트)로 칠해지는 자리인가(도로·건물 3.2m 안). 아니면 잔디.</summary>
    public static bool IsPavedGround(MapSpec map, float x, float z) => NearRoadOrCity(map, x, z, 3.2f);

    static bool NearRoadOrCity(MapSpec map, float x, float z, float reach) {
      var fp = new Photon.Deterministic.FPVector3(Photon.Deterministic.FP.FromFloat_UNSAFE(x), default, Photon.Deterministic.FP.FromFloat_UNSAFE(z));
      foreach (var r in map.Roads ?? Array.Empty<MapShape>()) {
        if (r.Distance(fp).AsFloat <= reach) {
          return true;
        }
      }
      for (var i = 0; map.CityBuildings != null && i < map.CityBuildings.Length; i++) {
        if (map.CityBuildings[i].Shape.Distance(fp).AsFloat <= reach) {
          return true;
        }
      }
      return false;
    }

    static void Triangle(MeshKit k, MapSpec map, int seed, Vector3 a, Vector3 b, Vector3 c) {
      var center = (a + b + c) / 3;
      k.Paint(GroundColor(map, center.x, center.z, seed));
      k.Tri(a, b, c, Vector3.up);
    }

    static void Cell(MeshKit k, MapSpec map, int seed, float x0, float x1, float z0, float z1, bool flip,
        List<List<RiverPoint>> beach = null) {
      float Elevation(float x, float z) {
        if (beach == null) return 0f;
        var distance = RiverDistance(beach, new Vector2(x, z));
        var slopeWidth = 3.2f + 3.3f * Mathf.PerlinNoise(x * 0.078f + 43f, z * 0.078f + 29f);
        var blend = Mathf.Clamp01((GroundCut - distance) / slopeWidth);
        return Mathf.Lerp(0f, WaterLevel - 1f, blend);
      }
      var a = new Vector3(x0, Elevation(x0, z0), z0);
      var b = new Vector3(x1, Elevation(x1, z0), z0);
      var c = new Vector3(x1, Elevation(x1, z1), z1);
      var d = new Vector3(x0, Elevation(x0, z1), z1);
      if (flip) {
        Triangle(k, map, seed, a, b, c);
        Triangle(k, map, seed, a, c, d);
      } else {
        Triangle(k, map, seed, a, b, d);
        Triangle(k, map, seed, b, c, d);
      }
    }

    /// <summary>강 근처 칸만 잘게 나눠 강 자리를 파낸다. 잘린 가장자리는 강변 띠가 덮는다.</summary>
    static void Carve(MeshKit k, MapSpec map, int seed, List<List<RiverPoint>> paths, float x0, float x1, float z0, float z1, bool flip,
        List<List<RiverPoint>> coast = null) {
      var center = new Vector2((x0 + x1) / 2, (z0 + z1) / 2);
      var reach = new Vector2(x1 - x0, z1 - z0).magnitude / 2;
      var distance = RiverDistance(paths, center) - GroundCut;
      if (distance > reach) {
        Cell(k, map, seed, x0, x1, z0, z1, flip);
        return;
      }
      var beach = coast != null && RiverDistance(coast, center) - GroundCut <= distance + 0.45f ? coast : null;
      if (distance < -reach - (beach != null ? 6.5f : 0f)) {
        return;
      }
      if (x1 - x0 <= MinCell && z1 - z0 <= MinCell) {
        if (distance > 0f || beach != null) {
          Cell(k, map, seed, x0, x1, z0, z1, flip, beach);
        }
        return;
      }
      var mx = (x0 + x1) / 2;
      var mz = (z0 + z1) / 2;
      Carve(k, map, seed, paths, x0, mx, z0, mz, flip, coast);
      Carve(k, map, seed, paths, mx, x1, z0, mz, flip == false, coast);
      Carve(k, map, seed, paths, x0, mx, mz, z1, flip == false, coast);
      Carve(k, map, seed, paths, mx, x1, mz, z1, flip, coast);
    }

    /// <summary>강 자리를 파낸 바닥.</summary>
    public static void Ground(MeshKit k, MapSpec map, int seed) {
      var paths = RiverPaths(map);
      var biome = BiomeOf(map);
      // 다도해는 외곽 콘크리트 육지 테두리를 만들지 않고 끝없는 에메랄드 바다가 수평선까지 펼쳐지도록 Bounds 안쪽 섬만 생성한다.
      if (biome == Biome.Archipelago) {
        var rivers = RiverPaths(map, false);
        var broadWater = rivers.Where(path => path.Count > 1 && path[0].Half >= 18f).ToList();
        var minA = new Vector2(map.BoundsMin.X.AsFloat, map.BoundsMin.Y.AsFloat);
        var maxA = new Vector2(map.BoundsMax.X.AsFloat, map.BoundsMax.Y.AsFloat);
        var xsA = Lines(minA.x, maxA.x, false);
        var zsA = Lines(minA.y, maxA.y, false);
        for (var i = 0; i + 1 < xsA.Count; i++) {
          for (var j = 0; j + 1 < zsA.Count; j++) {
            Carve(k, map, seed, paths, xsA[i], xsA[i + 1], zsA[j], zsA[j + 1], (i + j) % 2 == 0, broadWater);
          }
        }
        IslandBeaches(k, map, rivers);
        return;
      }
      // 외곽 수로가 있으면 바닥을 수로 바깥 둑까지 넓혀 수로를 같은 방식으로 파낸다(항목 24).
      var extra = Channel(map) > 0f ? Channel(map) + 6f : 0f;
      var min = new Vector2(map.BoundsMin.X.AsFloat - extra, map.BoundsMin.Y.AsFloat - extra);
      var max = new Vector2(map.BoundsMax.X.AsFloat + extra, map.BoundsMax.Y.AsFloat + extra);
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
      var depth = WallDepth + Channel(map);
      var min = new Vector2(map.BoundsMin.X.AsFloat - depth, map.BoundsMin.Y.AsFloat - depth);
      var max = new Vector2(map.BoundsMax.X.AsFloat + depth, map.BoundsMax.Y.AsFloat + depth);
      return new Rect(min, max - min);
    }

    static Vector3 Clip(Vector3 point, Rect land) => new Vector3(Mathf.Clamp(point.x, land.xMin, land.xMax), point.y, Mathf.Clamp(point.z, land.yMin, land.yMax));

    static void IslandBeaches(MeshKit k, MapSpec map, List<List<RiverPoint>> paths) {
      var land = MapPolishArt.PlayableRect(map);
      Vector3 Vertex(Vector2 point, Vector2 normal, float ratio) {
        if (ratio > 0f) {
          var edgeX = Mathf.Approximately(point.x, land.xMin) ? -1f : Mathf.Approximately(point.x, land.xMax) ? 1f : 0f;
          var edgeZ = Mathf.Approximately(point.y, land.yMin) ? -1f : Mathf.Approximately(point.y, land.yMax) ? 1f : 0f;
          if (edgeX != 0f && edgeZ != 0f) normal = new Vector2(edgeX, edgeZ).normalized;
        }
        var spread = 4.8f + 7.0f * Mathf.PerlinNoise(point.x * 0.043f + 81f, point.y * 0.047f + 19f);
        var position = point + normal * Mathf.Lerp(-3.8f, spread, ratio);
        return new Vector3(position.x, Mathf.Lerp(-0.035f, WaterLevel - 0.55f, ratio * ratio), position.y);
      }
      foreach (var (from, to, outward) in MapPolishArt.Edges(land)) {
        var count = Mathf.CeilToInt(Vector2.Distance(from, to) / 3f);
        for (var i = 0; i < count; i++) {
          var a = Vector2.Lerp(from, to, i / (float)count);
          var b = Vector2.Lerp(from, to, (i + 1f) / count);
          if (RiverDistance(paths, (a + b) * 0.5f - outward * 2.8f) < GroundCut + 0.55f) continue;
          k.Paint(Swatch.Sand);
          k.Quad(Vertex(a, outward, 0f), Vertex(b, outward, 0f), Vertex(b, outward, 0.52f), Vertex(a, outward, 0.52f), Vector3.up);
          k.Paint(Swatch.Dirt);
          k.Quad(Vertex(a, outward, 0.52f), Vertex(b, outward, 0.52f), Vertex(b, outward, 1f), Vertex(a, outward, 1f), Vector3.up);
        }
      }
    }

    /// <summary>안쪽 굽이에서 띠 바깥선이 곡률 반경을 넘어 물 쪽으로 접혔는지 본다.</summary>
    static bool Folded(List<RiverPoint> path, RiverPoint point, float side) {
      var outer = Bank(point, side, BandOuter, 0f);
      return RiverDistance(new List<List<RiverPoint>> { path }, new Vector2(outer.x, outer.z)) < BandOuter - 0.5f;
    }

    /// <summary>물 면, 경사 강변, 땅 위의 모래 띠. 강줄기 하나를 리본으로 잇는다.</summary>
    public static void River(MeshKit k, List<RiverPoint> path, int seed, Rect land, MeshKit water = null, Rect? banks = null, Biome biome = Biome.Standard) {
      // 물 면은 외곽 수로까지 이어지고, 둑(벽)은 banks(해안 옹벽 선) 안에서 끊는다(항목 36 시각 교정: 벽이 바다로 튀어나오지 않게).
      var bankLand = banks ?? land;
      Vector3 Edge(RiverPoint point, float side, float offset, float y) => Clip(Bank(point, side, offset, y), land);
      Vector3 BankEdge(RiverPoint point, float side, float offset, float y) => Clip(Bank(point, side, offset, y), bankLand);
      // 항목 36: water를 주면 물 면과 가장자리 거품 띠를 따로 모은다(물 셰이더로 그린다).
      var surface = water ?? k;
      var seascape = water != null && (biome == Biome.Archipelago || biome == Biome.Strait);
      for (var i = 0; i + 1 < path.Count; i++) {
        var a = path[i];
        var b = path[i + 1];
        if (land.Contains(a.Position) == false && land.Contains(b.Position) == false) {
          continue;
        }
        // 다도해의 초대형 외해 채널(Half >= 18)은 연속 바다판(WaterBase)으로 이음새 없이 렌더링해 직선 거품 줄무늬를 제거한다.
        if (biome == Biome.Archipelago && a.Half >= 18f) {
          continue;
        }
        if (!seascape) {
          surface.Paint(Swatch.WaterDeep);
          surface.Quad(Edge(a, -1, SlopeInner, WaterLevel), Edge(a, 1, SlopeInner, WaterLevel), Edge(b, 1, SlopeInner, WaterLevel), Edge(b, -1, SlopeInner, WaterLevel), Vector3.up);
        }
        if (water != null && !seascape) {
          surface.Paint(Swatch.White);
          foreach (var foamSide in new[] { -1f, 1f }) {
            surface.Quad(Edge(a, foamSide, SlopeInner - 1.3f, WaterLevel + 0.03f), Edge(a, foamSide, SlopeInner, WaterLevel + 0.03f), Edge(b, foamSide, SlopeInner, WaterLevel + 0.03f), Edge(b, foamSide, SlopeInner - 1.3f, WaterLevel + 0.03f), Vector3.up);
          }
        }
        foreach (var side in new[] { -1f, 1f }) {
          var middle = (BankY + WaterLevel) * 0.5f;
          var ledge = (BandInner + SlopeInner) * 0.5f;
          var urban = biome == Biome.Urban || (VisualProfile.Current.UrbanDecor && VisualProfile.Current.FieldLook == false);
          var sandy = biome == Biome.Desert || biome == Biome.Archipelago;
          var rocky = biome == Biome.Strait;
          k.Paint(urban ? Swatch.Concrete : sandy ? Swatch.Sand : rocky ? Swatch.Boulder : MeshKit.Hash(seed, i * 2 + (side > 0 ? 1 : 0)) < 0.5f ? Swatch.Earth : Swatch.CanyonMid);
          k.Quad(BankEdge(a, side, BandInner, BankY), BankEdge(a, side, ledge, middle), BankEdge(b, side, ledge, middle), BankEdge(b, side, BandInner, BankY), Vector3.up);
          k.Paint(urban ? Swatch.ConcreteDark : sandy ? Swatch.Dirt : rocky ? Swatch.BoulderDark : Swatch.CanyonDark);
          k.Quad(BankEdge(a, side, ledge, middle), BankEdge(a, side, SlopeInner, WaterLevel - 0.1f), BankEdge(b, side, SlopeInner, WaterLevel - 0.1f), BankEdge(b, side, ledge, middle), Vector3.up);
          if (Folded(path, a, side) || Folded(path, b, side)) {
            continue;
          }
          var bandSwatch = urban ? Swatch.ConcreteDark : sandy ? Swatch.Sand : rocky ? Swatch.Boulder : VisualProfile.Current.FieldLook ? ArtPalette.Turf(Swatch.GrassDark) : MeshKit.Hash(seed, i * 2 + 300 + (side > 0 ? 1 : 0)) < 0.55f ? Swatch.Sand : Swatch.Dirt;
          k.Paint(bandSwatch);
          k.Quad(BankEdge(a, side, BandOuter, BankY), BankEdge(a, side, BandInner, BankY), BankEdge(b, side, BandInner, BankY), BankEdge(b, side, BandOuter, BankY), Vector3.up);
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

    /// <summary>Features가 채우는 점령지 목록. 바위 위 장식이 점령지 원을 덮지 않게 할 때 쓴다(화면 전용).</summary>
    static CaptureSitePlacement[] _sites;

    /// <summary>크기와 각도가 다른 회색 바위 무더기.</summary>
    public static void Boulders(MeshKit k, MapShape shape, int seed) {
      var center = new Vector3(shape.Center.X.AsFloat, 0, shape.Center.Z.AsFloat);
      var radius = shape.IsCircle ? shape.Radius.AsFloat : Mathf.Min(shape.HalfLength.AsFloat, shape.HalfWidth.AsFloat);
      // Kenney 바위: 큰 바위 하나와 둘레의 큰·작은 바위 무리. 크기·회전은 해시로 정한다.
      if (Dressing.Collecting && Dressing.Has(DressKind.RockLarge)) {
        Dressing.Add(DressKind.RockLarge, center, seed, Vector3.one * (radius * 0.34f));
        var around = 4 + (int)(MeshKit.Hash(seed, 2) * 4f);
        for (var i = 0; i < around; i++) {
          var angle = (i + MeshKit.Hash(seed, i + 10)) * Mathf.PI * 2f / around;
          var distance = radius * (0.4f + 0.25f * MeshKit.Hash(seed, i + 20));
          var spot = center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * distance;
          var small = MeshKit.Hash(seed, i + 30) < 0.5f;
          Dressing.Add(small ? DressKind.RockSmall : DressKind.RockLarge, spot, seed * 31 + i, Vector3.one * (radius * (small ? 0.18f : 0.2f)));
        }
        // 중앙 폐허의 대칭 바위 엄폐물에는 손상된 철로와 탈선한 화차 더미를 함께 놓는다(항목 19D, 기존 바위 판정 공유).
        var nearSite = false;
        foreach (var site in _sites ?? Array.Empty<CaptureSitePlacement>()) {
          nearSite |= (new Vector2(site.Center.X.AsFloat - center.x, site.Center.Z.AsFloat - center.z)).magnitude < site.Radius.AsFloat + radius + 4f;
        }
        if (nearSite == false && new Vector2(center.x, center.z).magnitude < 62f && Dressing.Has(DressKind.TrainWreck)) {
          var yaw = Mathf.Atan2(center.z, center.x) * Mathf.Rad2Deg + 90f;
          if (Dressing.Has(DressKind.TrainTrack)) {
            Dressing.Add(DressKind.TrainTrack, center + new Vector3(0f, 0.04f, 0f), seed + 701, null, yaw);
          }
          Dressing.Add(DressKind.TrainWreck, center + new Vector3(0f, 0.1f, 0f), seed + 709, null, yaw + 18f);
        }
        return;
      }
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
    public const float FenceHeight = 3.7f;

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

    /// <summary>깔끔한 화이트/파스텔 그레이 둥근 블록형 바리케이드 (city-kit-roads / modular-buildings 스타일)</summary>
    public static void Fence(MeshKit body, MeshKit lamps, FencePiece piece) {
      var delta = piece.To - piece.From;
      var length = delta.magnitude;
      var yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
      body.Push(piece.From, new Vector3(0, yaw, 0));
      lamps.Push(piece.From, new Vector3(0, yaw, 0));
      const float Post = 1.35f;
      foreach (var z in new[] { 0f, length }) {
        body.Paint(Swatch.Concrete);
        body.Cylinder(new Vector3(0, Post * 0.5f, z), 0.48f, Post, 8);
        body.Paint(Swatch.ConcreteDark);
        body.Dome(new Vector3(0, Post, z), 0.5f, 0.22f, 8, 3);
        lamps.Glow(Swatch.GlowCyan);
        lamps.Dome(new Vector3(0, Post + 0.14f, z), 0.24f, 0.18f, 6, 2);
      }
      var inner = Mathf.Max(0.1f, length - 0.7f);
      body.Paint(Swatch.Concrete);
      body.Box(new Vector3(0, 0.5f, length / 2), new Vector3(0.44f, 0.75f, inner));
      body.Team();
      body.Box(new Vector3(0, 0.88f, length / 2), new Vector3(0.48f, 0.12f, inner));
      body.Paint(Swatch.ConcreteDark);
      body.Box(new Vector3(0, 0.96f, length / 2), new Vector3(0.52f, 0.08f, inner));
      body.Pop();
      lamps.Pop();
    }

    /// <summary>언덕·협곡·바위·강·줄무늬를 메쉬 하나로 모은다. 번호는 MapSpec.Terrain의 순서다.</summary>
    public static void Features(MeshKit k, MapSpec map, bool roads = true, MeshKit water = null) {
      _sites = map.CaptureSites;
      var profile = VisualProfile.Current;
      var paths = RiverPaths(map);
      var features = map.Terrain ?? Array.Empty<TerrainFeature>();
      // 품질 낮음은 절벽 층을 절반으로 합친다(높이는 같다, 절벽 블록·삼각형 절반, 항목 27 성능).
      var layers = GraphicsQuality.Current == QualityTier.Low ? Mathf.Max(1, (profile.CliffLayers + 1) / 2) : profile.CliffLayers;
      var biome = BiomeOf(map);
      for (var i = 0; i < features.Length; i++) {
        var seed = 100 + i * 17;
        switch (features[i].Kind) {
          case TerrainKind.Hill: Plateau(k, features[i].Shape, seed, profile.PlateauHeight * (0.85f + 0.3f * MeshKit.Hash(seed, 5)), layers, profile.Jagged, biome == Biome.Desert); break;
          case TerrainKind.Cliff:
            if (profile.FieldLook) {
              // 야전 룩: 고지대 둘레 절벽 조각은 건물 대신 바위 하나(언덕 비탈 위).
              var at = features[i].Shape.Center;
              var point = new Vector3(at.X.AsFloat, 0f, at.Z.AsFloat);
              if (biome != Biome.Desert || !NewBundleArt.CanyonFeature(map, features[i].Shape, HighGroundHeight + 1.4f, seed)) {
                Dressing.Add(DressKind.RockLarge, point, seed);
              }
              break;
            }
            Plateau(k, features[i].Shape, seed, HighGroundHeight + 0.8f, 1, profile.Jagged * 0.5f, false);
            break;
          case TerrainKind.CanyonWall: Plateau(k, features[i].Shape, seed, profile.PlateauHeight * 1.25f, layers + 1, profile.Jagged, true); break;
          case TerrainKind.Rock: Boulders(k, features[i].Shape, seed); break;
        }
      }
      var coast = biome == Biome.Archipelago || biome == Biome.Strait;
      var playable = MapPolishArt.PlayableRect(map);
      var banks = coast || Channel(map) > 0f ? playable : (Rect?)null;
      var riverPaths = coast ? RiverPaths(map, false) : paths;
      for (var i = 0; i < riverPaths.Count; i++) {
        River(k, riverPaths[i], 40 + i, coast ? playable : Land(map), water, banks, biome);
        if (!coast) RiverRocks(k, riverPaths[i], 70 + i, map);
      }
      if (roads) {
        Roads(k, map);
      }
      HighGrounds(k, map);
      if (!NewBundleArt.UsesBridges(biome)) BridgePiers(k, map);
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
