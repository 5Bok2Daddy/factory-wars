namespace FactoryWars.EditorTools {
  using System.Collections.Generic;
  using System.Linq;
  using Photon.Deterministic;
  using Quantum;
  using UnityEngine;

  /// <summary>
  /// 2인·4인 맵 배치. 한 플레이어 몫만 적고 맵 중심 회전으로 나머지를 만든다. 좌표는 맵 중심이 원점이다.
  /// 지형은 본진 대각선 거울까지 맞춰, 양쪽 앞마당에서 공유 확장까지의 거리가 같다. 자원과 뒷길 틈만 거울을 따르지 않는다.
  /// 배치 근거와 측정값은 SETUP-REPORT.md "항목 17"에 있다.
  /// </summary>
  public static partial class FactoryWarsSetup {
    struct WaterStrip {
      public Vector2 From;
      public Vector2 To;
      public float HalfWidth;
      public bool Ring;
      public float RingRadius;
      /// <summary>곧은 수로 폭을 이만큼까지 좁혀 해안선이 굽이치게 한다(0이면 곧은 띠).</summary>
      public float Wobble;
      public int Seed;
      /// <summary>끝점을 빼고 만든다. 다각형 물길을 이을 때 꼭짓점이 겹치지 않게 한다.</summary>
      public bool OpenEnd;
    }

    class MapDef {
      public string Id;
      public int Players;
      public int HalfSize;
      public FPVector3 Start;
      public ResourceNodePlacement[] Nodes;
      public ResourceNodePlacement[] CenterNodes;
      public TerrainFeature[] Terrain;
      public MapShape[] NoBuildZones;
      public MapShape[] Bridges;
      public MapShape[] ExtraBridges;
      public MapShape[] ExtraRoads;
      public WaterStrip[] ExtraWater;
      public MapShape BaseArea;
      public Vector3 CameraPosition;
      public float RiverAmplitude;
      public int RiverSeed;
      public CaptureSitePlacement[] CaptureSites;
      public CaptureSitePlacement[] CenterSites;
      public MapShape[] Barricades;
      public FPVector3[] BaseCrates;
      public FPVector3[] DropCrates;
      public MapShape[] HighGrounds;
      public MapShape[] Ramps;
      public MapShape[] Roads;
      public MapShape[] Fords;
      public ExpansionSite[] Expansions;
      public ExpansionSite[] SharedExpansions;
      public ExpansionSite[] CenterExpansions;
      /// <summary>공유 확장 i번에 닿는 자리들. 자리 s의 복사본은 이 자리를 s만큼 돌린 것이다.</summary>
      public int[] SharedSeatOffsets;
      /// <summary>항목 35 맵 프리셋: 테마, 보급 낙하 간격·보상 배율.</summary>
      public string Theme = "표준";
      public double SupplyInterval = 1;
      public double SupplyReward = 1;
      /// <summary>항목 37 기상(초). 0이면 없음.</summary>
      public int WeatherFirst, WeatherDuration, WeatherPeriod;
      /// <summary>중앙 암시장.</summary>
      public bool Trader;
      public FPVector3 TraderPosition;
      public double WeatherSight = 1;
      /// <summary>항목 37 블랙박스 추락 자리(순환, 회전 대칭 복제 없음: 중앙 부근).</summary>
      public FPVector3[] BlackBoxSpots = new FPVector3[0];
      /// <summary>0보다 크면 |x|가 이 값보다 작은 공통 도로 조각을 뺀다(해협: 해안 간선도로·교량 접근로로 대신한다).</summary>
      public float RoadClearHalfX;
      public MapDef Clone() => (MapDef)MemberwiseClone();
    }

    const float RoadHalfWidth = 3.5f;
    const float FordHalfWidth = 3f;
    const float RampHalfWidth = 4f;
    const float CliffThickness = 3f;
    const int CliffPieces = 16;

    static FPVector3 Position(double x, double z) => new FPVector3(N(x), 0, N(z));

    static MapShape Circle(double x, double z, double radius) => new MapShape { Center = Position(x, z), Radius = N(radius) };

    static MapShape Rect(double x, double z, double halfLength, double halfWidth, double yaw = 0) {
      return new MapShape { Center = Position(x, z), HalfLength = N(halfLength), HalfWidth = N(halfWidth), Yaw = N(yaw) };
    }

    static ResourceNodePlacement Mine(double x, double z, int amount, double radius = 3) {
      return new ResourceNodePlacement { Kind = ResourceKind.Iron, Amount = amount, Shape = Circle(x, z, radius) };
    }

    /// <summary>사각형 숲. 테스트 맵에서만 쓴다.</summary>
    static ResourceNodePlacement Forest(double x, double z, double halfLength, double halfWidth, double yaw, int amount) {
      return new ResourceNodePlacement { Kind = ResourceKind.Wood, Amount = amount, Shape = Rect(x, z, halfLength, halfWidth, yaw) };
    }

    /// <summary>원형 군락. 군락 하나가 노드 하나다.</summary>
    static ResourceNodePlacement Grove(double x, double z, double radius, int amount) {
      return new ResourceNodePlacement { Kind = ResourceKind.Wood, Amount = amount, Shape = Circle(x, z, radius) };
    }

    static TerrainFeature Feature(TerrainKind kind, MapShape shape) => new TerrainFeature { Kind = kind, Shape = shape };

    static IEnumerable<TerrainFeature> Features(TerrainKind kind, IEnumerable<MapShape> shapes) => shapes.Select(shape => Feature(kind, shape));

    /// <summary>두 점을 잇는 사각형.</summary>
    static MapShape Segment(Vector2 a, Vector2 b, float halfWidth) {
      var delta = b - a;
      var middle = (a + b) / 2;
      return Rect(middle.x, middle.y, delta.magnitude / 2, halfWidth, Mathf.Atan2(-delta.y, delta.x) * Mathf.Rad2Deg);
    }

    static float Hash01(int seed, int index) {
      unchecked {
        var v = (uint)seed * 374761393u + (uint)index * 668265263u;
        v = (v ^ (v >> 13)) * 1274126177u;
        return ((v ^ (v >> 16)) & 0xffff) / 65535f;
      }
    }

    /// <summary>
    /// 능선 덩어리. 중심 띠에 크기와 회전이 다른 사각형과 원을 겹친다. 판정은 합집합이고 두께는 halfWidth의 두 배 이상이다.
    /// </summary>
    static List<MapShape> Ridge(int seed, float halfWidth, params Vector2[] points) {
      var shapes = new List<MapShape>();
      var total = 0f;
      for (var i = 0; i + 1 < points.Length; i++) {
        shapes.Add(Segment(points[i], points[i + 1], halfWidth));
        total += (points[i + 1] - points[i]).magnitude;
      }
      var lobes = Mathf.Clamp(Mathf.RoundToInt(total / 11f), 2, 5);
      for (var lobe = 0; lobe < lobes; lobe++) {
        var t = (lobe + 0.5f) / lobes * total;
        var from = points[0];
        var to = points[1];
        for (var i = 0; i + 1 < points.Length; i++) {
          from = points[i];
          to = points[i + 1];
          var length = (to - from).magnitude;
          if (t <= length) {
            break;
          }
          t -= length;
        }
        var along = (to - from).normalized;
        var normal = new Vector2(-along.y, along.x);
        var center = from + along * t + normal * ((Hash01(seed, lobe) * 2f - 1f) * 3f);
        if (lobe % 2 == 0) {
          shapes.Add(Circle(center.x, center.y, halfWidth + 1f + 3f * Hash01(seed, lobe + 50)));
        } else {
          var yaw = Mathf.Atan2(-along.y, along.x) * Mathf.Rad2Deg + (Hash01(seed, lobe + 90) * 2f - 1f) * 35f;
          shapes.Add(Rect(center.x, center.y, halfWidth + 2f + 3f * Hash01(seed, lobe + 70), halfWidth + Hash01(seed, lobe + 80), yaw));
        }
      }
      shapes.Add(Circle(points[0].x, points[0].y, halfWidth));
      shapes.Add(Circle(points[points.Length - 1].x, points[points.Length - 1].y, halfWidth));
      return shapes;
    }

    /// <summary>본진 대각선(z = x) 거울.</summary>
    static MapShape Mirror(MapShape shape) {
      shape.Center = new FPVector3(shape.Center.Z, 0, shape.Center.X);
      shape.Yaw = -90 - shape.Yaw;
      return shape;
    }

    /// <summary>반대각선(z = -x) 거울.</summary>
    static MapShape Anti(MapShape shape) {
      shape = Mirror(shape);
      shape.Center = -shape.Center;
      return shape;
    }

    /// <summary>z = 0 축 거울.</summary>
    static MapShape ReflectZ(MapShape shape) {
      shape.Center = new FPVector3(shape.Center.X, 0, -shape.Center.Z);
      shape.Yaw = -shape.Yaw;
      return shape;
    }

    static List<MapShape> WithMirror(IEnumerable<MapShape> shapes) {
      var list = shapes.ToList();
      return list.Concat(list.Select(Mirror)).ToList();
    }

    static IEnumerable<MapShape> Road(params Vector2[] points) => CurvedStrip(RoadHalfWidth, points);

    static IEnumerable<MapShape> CurvedStrip(float halfWidth, params Vector2[] points) {
      for (var i = 0; i + 1 < points.Length; i++) {
        var previous = points[Mathf.Max(0, i - 1)];
        var from = points[i];
        var to = points[i + 1];
        var next = points[Mathf.Min(points.Length - 1, i + 2)];
        var count = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(from, to) / 4.8f));
        var last = from;
        for (var sample = 1; sample <= count; sample++) {
          var t = sample / (float)count;
          var t2 = t * t;
          var t3 = t2 * t;
          var point = 0.5f * (2f * from + (to - previous) * t
            + (2f * previous - 5f * from + 4f * to - next) * t2
            + (3f * from - previous - 3f * to + next) * t3);
          var shape = Segment(last, point, halfWidth);
          shape.HalfLength += N(0.14);
          yield return shape;
          last = point;
        }
      }
    }

    /// <summary>고지대의 절벽 고리와 비탈. 평지 반지름 radius 바깥을 짧은 사각형으로 두르고 비탈 자리만 비운다.</summary>
    static void HighGround(double x, double z, float radius, float[] rampDegrees, List<TerrainFeature> terrain, List<MapShape> grounds, List<MapShape> ramps) {
      grounds.Add(Circle(x, z, radius));
      var middle = radius + CliffThickness / 2;
      var gap = RampHalfWidth / middle + 0.1f;
      foreach (var degrees in rampDegrees) {
        var angle = degrees * Mathf.Deg2Rad;
        ramps.Add(Rect(x + middle * Mathf.Cos(angle), z + middle * Mathf.Sin(angle), RampHalfWidth, CliffThickness, -degrees + 90));
      }
      for (var i = 0; i < CliffPieces; i++) {
        var angle = Mathf.PI * 2f * (i + 0.5f) / CliffPieces;
        if (rampDegrees.Any(degrees => Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, degrees)) * Mathf.Deg2Rad < gap)) {
          continue;
        }
        terrain.Add(Feature(TerrainKind.Cliff, Rect(x + middle * Mathf.Cos(angle), z + middle * Mathf.Sin(angle), 3.2, CliffThickness / 2, -angle * Mathf.Rad2Deg + 90)));
      }
    }

    static IEnumerable<TerrainFeature> Rocks(int seed, params (double X, double Z)[] centers) {
      for (var i = 0; i < centers.Length; i++) {
        var count = 2 + (int)(Hash01(seed, i) * 2.99f);
        for (var j = 0; j < count; j++) {
          var angle = Hash01(seed, i * 7 + j + 20) * Mathf.PI * 2f;
          var distance = j == 0 ? 0f : 2.5f + Hash01(seed, i * 7 + j + 40) * 1.5f;
          yield return Feature(TerrainKind.Rock, Circle(centers[i].X + Mathf.Cos(angle) * distance, centers[i].Z + Mathf.Sin(angle) * distance, 1.8 + Hash01(seed, i * 7 + j + 60) * 1.4));
        }
      }
    }

    static Vector2 P(float x, float z) => new Vector2(x, z);

    static CaptureSitePlacement Site(CaptureKind kind, double x, double z, double radius) {
      return new CaptureSitePlacement { Kind = kind, Center = Position(x, z), Radius = N(radius) };
    }

    static ExpansionSite Expansion(ExpansionKind kind, double x, double z) => new ExpansionSite { Kind = kind, Position = Position(x, z) };

    /// <summary>사막·시가지·다도해·해협이 복제하는 공통 지형(중앙 고지 벽).</summary>
    static readonly MapDef FourPlayerBase = BuildFourPlayer(false);
    /// <summary>표준 맵: 참고 맵처럼 중앙 섬을 원형 강이 두르고 X자 간선도로가 다리로 건넌다.</summary>
    static readonly MapDef FourPlayerMap = BuildFourPlayer(true);

    /// <summary>
    /// 항목 35 "사막 보급전": 표준 맵 지형을 그대로 쓰고 광산을 40%로 줄이고, 보급 낙하 간격을 절반·보상을 2배로 한다(보급 쟁탈이 핵심).
    /// 같은 지형이라 회전 대칭·자리 공정성·경로 연결은 표준 맵과 같다. 새 지형·건물 배치는 만들지 않았다.
    /// </summary>
    static readonly MapDef DesertMap = BuildDesert();
    static readonly MapDef UrbanMap = BuildUrban();
    static readonly MapDef ArchipelagoMap = BuildArchipelago();
    static readonly MapDef StraitMap = BuildStrait();

    static MapDef BuildDesert() {
      var def = FourPlayerBase.Clone();
      def.Id = "MapDesert";
      def.Theme = "사막 보급전";
      def.SupplyInterval = 0.5;
      def.SupplyReward = 2;
      // 모래폭풍: 8분에 첫 발동, 60초 지속, 4분 주기, 기본 시야 -35%.
      def.WeatherFirst = 480;
      def.WeatherDuration = 60;
      def.WeatherPeriod = 240;
      def.WeatherSight = 0.65;
      ResourceNodePlacement Scaled(ResourceNodePlacement node) {
        node.Amount = node.Amount * N(0.55);
        return node;
      }
      def.Nodes = def.Nodes.Select(Scaled).ToArray();
      def.CenterNodes = def.CenterNodes.Select(Scaled).ToArray();
      return def;
    }

    /// <summary>항목 35 시가전: 고밀도 시가지 블록, 붕괴 우회 통로(Rubble Passage Rule), 주둔 저격·보병 엄폐 중심.</summary>
    static MapDef BuildUrban() {
      var def = FourPlayerBase.Clone();
      def.Id = "MapUrban";
      def.Theme = "시가전";
      def.SupplyInterval = 0.9;
      def.SupplyReward = 1.2;
      def.CaptureSites = new[] { Site(CaptureKind.Relay, -134, 0, 8), Site(CaptureKind.Radar, -30, -30, 7), Site(CaptureKind.Fuel, -60, -98, 6) };
      return def;
    }

    /// <summary>항목 35 다도해: 65%+ 에메랄드 바다와 5개의 뚜렷한 모래해변 섬(4코너 본진 섬 + 중앙 섬), 얕은 여울·해상/상륙전 중심.</summary>
    static MapDef BuildArchipelago() {
      var def = FourPlayerBase.Clone();
      def.Id = "MapArchipelago";
      def.Theme = "다도해";
      def.SupplyInterval = 0.85;
      def.SupplyReward = 1.25;
      def.WeatherFirst = 420;
      def.WeatherDuration = 60;
      def.WeatherPeriod = 240;
      def.WeatherSight = 0.65;
      // 중앙 섬(반경 42m)을 360도 감싸는 환형 라군(r=42~90) + 4방위 100m 광폭 외해 해협 -> 플레이 영역 내 65%+(화면 75%+) 에메랄드 바다!
      def.ExtraWater = new[] {
        new WaterStrip { Ring = true, RingRadius = 66f, HalfWidth = 24f },
        // 해협 폭을 0~22m 굽이치게(중심선도 ±7m 흔든다) 좁혀 사각형 섬 대신 둥근 곶·만이 생기게 한다(참고 맵 2026-11-21).
        new WaterStrip { From = P(0f, 74f), To = P(-7f, 161f), HalfWidth = 55f, Wobble = 22f, Seed = 1 },
        new WaterStrip { From = P(7f, -161f), To = P(0f, -74f), HalfWidth = 55f, Wobble = 22f, Seed = 2 },
        new WaterStrip { From = P(-161f, -7f), To = P(-74f, 0f), HalfWidth = 55f, Wobble = 22f, Seed = 3 },
        new WaterStrip { From = P(74f, 0f), To = P(161f, 7f), HalfWidth = 55f, Wobble = 22f, Seed = 4 },
      };
      // 섬 육지 위에만 정확히 안착하는 자원·거점·블랙박스 배치(바다에 뜨는 광산/언덕 0).
      def.Nodes = new[] {
        Mine(-146, -119, 4600), Mine(-119, -146, 4600), Grove(-144, -144, 5.5, 6000),
        Mine(-150, -73, 3200), Mine(-124, -66, 3200), Grove(-150, -64, 5, 2200),
        Grove(-62, -147, 9, 5000), Grove(-68, -122, 6, 3000), Mine(-71, -111, 2200, 2.5),
        Grove(-18, -18, 5, 2400),
      };
      def.CenterNodes = new[] { Mine(-12, -24, 6500, 3.5), Grove(11, 21, 6, 7000) };
      def.Fords = new[] { Segment(P(-67f, -67f), P(-25f, -25f), 5.6f) };
      def.ExtraBridges = Enumerable.Range(0, 4).Select(i => Turn(Rect(-48, -48, 30, 4.8, -45), i)).ToArray();
      def.CaptureSites = new[] {
        Site(CaptureKind.Relay, -88, -88, 7),
        Site(CaptureKind.Radar, -22, -22, 6),
        Site(CaptureKind.Fuel, -66, -114, 6),
      };
      def.SharedExpansions = new ExpansionSite[0];
      def.BlackBoxSpots = new[] { Position(-30, 0), Position(0, 30), Position(30, 0), Position(0, -30) };
      def.DropCrates = new[] { Position(-86, -86), Position(-132, -66), Position(-28, -14) };
      return def;
    }

    /// <summary>항목 35 해협 도하전: 맵 정중앙을 수직으로 양분하는 48m 광폭 짙은 남색 해협 + 북·중·남 3대 전략 교량.</summary>
    static MapDef BuildStrait() {
      var def = FourPlayerBase.Clone();
      def.Id = "MapStrait";
      def.Theme = "해협";
      def.SupplyInterval = 0.9;
      def.SupplyReward = 1.2;
      def.WeatherFirst = 450;
      def.WeatherDuration = 60;
      def.WeatherPeriod = 240;
      def.WeatherSight = 0.65;
      def.Trader = false;
      // 중앙을 남북으로 완벽히 가르는 48m(HalfWidth=24) 대양 해협.
      def.ExtraWater = new[] {
        new WaterStrip { From = P(0f, -162f), To = P(0f, 162f), HalfWidth = 24f },
      };
      // 해협을 가로지르는 3개의 뚜렷한 전략 교량(북·중앙 대교·남).
      def.ExtraBridges = new[] {
        Rect(0, 84, 27, 5.5),
        Rect(0, 0, 27, 6.5),
        Rect(0, -84, 27, 5.5),
      };
      // 3개 교량 진입 도로 + 동·서 해안 종단 간선도로.
      var coastalRoad = Road(P(-56f, -124f), P(-62f, -81f), P(-58f, -40f), P(-52f, 0f), P(-58f, 40f), P(-62f, 81f), P(-56f, 124f));
      var approaches = new[] { -84f, 0f, 84f }.SelectMany(z => Road(P(-107f, z + 8f), P(-63f, z + 3f), P(-25f, z)));
      MapShape OppositeBank(MapShape road) {
        road.Center.X = -road.Center.X;
        road.Yaw = 180 - road.Yaw;
        return road;
      }
      // 양안 안쪽은 해안 간선도로와 교량 접근로만 둔다. 기지에서 오는 공통 도로는 해안도로(x≈±56~62)에서 끝나 합류한다.
      def.RoadClearHalfX = 62f;
      def.ExtraRoads = coastalRoad.Concat(approaches).SelectMany(road => new[] { road, OppositeBank(road) }).ToArray();
      def.Fords = new MapShape[0];
      def.Nodes = new[] {
        Mine(-146, -119, 4600), Mine(-119, -146, 4600), Grove(-144, -144, 5.5, 6000),
        Mine(-150, -73, 3000), Mine(-124, -66, 3000), Grove(-150, -58, 5, 2000),
        Grove(-62, -147, 10, 5000), Grove(-67, -122, 7, 3000), Mine(-71, -111, 1500, 2.5),
        Mine(-118, -38, 5000, 3.5), Grove(-118, -54, 6, 5000),
        Grove(-62, -46, 6, 2500),
      };
      def.CenterNodes = new[] { Mine(-38, -21, 6500, 3.5), Mine(38, 21, 6500, 3.5), Grove(-41, 22, 5, 7000), Grove(41, -22, 5, 7000) };
      def.CaptureSites = new[] {
        Site(CaptureKind.Relay, -134, 0, 8),
        Site(CaptureKind.Radar, -44, -44, 6),
        Site(CaptureKind.Fuel, -60, -98, 7),
      };
      def.BlackBoxSpots = new[] { Position(-44, 0), Position(44, 0), Position(-44, 84), Position(44, -84) };
      def.DropCrates = new[] { Position(-68, -32), Position(-142, 12), Position(-44, 42) };
      return def;
    }

    /// <summary>
    /// 중앙 섬을 두르는 12각 물길(안쪽 반지름 50m, 폭 14m). 변의 가운데가 대각선 다리라 다리 앞뒤 13m가 곧고, 꼭짓점 꺾임은 30도다.
    /// </summary>
    static WaterStrip[] CentralRiver() {
      const float Inner = 50f;
      var corner = Inner / Mathf.Cos(15f * Mathf.Deg2Rad);
      Vector2 Vertex(int k) => new Vector2(Mathf.Cos(k * 30f * Mathf.Deg2Rad), Mathf.Sin(k * 30f * Mathf.Deg2Rad)) * corner;
      return Enumerable.Range(0, 12).Select(k => new WaterStrip { From = Vertex(k), To = Vertex(k + 1), HalfWidth = 7f, OpenEnd = true }).ToArray();
    }

    static MapDef BuildFourPlayer(bool centralRiver) {
      var terrain = new List<TerrainFeature>();
      terrain.AddRange(Features(TerrainKind.Hill, WithMirror(Ridge(1, 6, P(-106, -59), P(-98, -62)).Concat(Ridge(2, 6, P(-136, -48), P(-98, -48))))));
      // 서쪽 변 공유 확장. 나머지 세 곳은 회전 복사본이다.
      var box = Ridge(3, 6, P(-136, -24), P(-100, -24));
      terrain.AddRange(Features(TerrainKind.Hill, box.Concat(box.Select(ReflectZ)).Concat(Ridge(4, 6, P(-100, -24), P(-100, 24)))));
      var canyon = Rect(-156, -24, 10, 4, 90);
      terrain.Add(Feature(TerrainKind.CanyonWall, canyon));
      terrain.Add(Feature(TerrainKind.CanyonWall, ReflectZ(canyon)));
      // 상자에서 중앙 고리까지 이어지는 능선. 축 위에 뒷길 틈이 있다.
      terrain.AddRange(Features(TerrainKind.Hill, Ridge(5, 6, P(-96, 0), P(-84, 0))));
      if (!centralRiver) {
        terrain.AddRange(Features(TerrainKind.Hill, Ridge(6, 6, P(-60, 0), P(-50, 0))));
        // 중앙 고리의 벽. 모서리 네 곳이 입구다.
        terrain.AddRange(Features(TerrainKind.Hill, Ridge(7, 6, P(-44, -24), P(-44, -12)).Concat(Ridge(12, 6, P(-44, 12), P(-44, 24)))));
      }
      terrain.AddRange(Rocks(8, (-74, -46), (-46, -74)));

      var grounds = new List<MapShape>();
      var ramps = new List<MapShape>();
      HighGround(-66, -66, 10, new[] { 45f, 225f }, terrain, grounds, ramps);

      var bridge = P(-135, -81.7f);
      var iron = P(-124, -74);
      var gate = P(-102, -64);
      var relay = P(-134, 0);
      var flank = Road(P(-136, -110), bridge, P(-148, -67), P(-146, -48), P(-143, -25), relay);
      // 중앙 강이 있으면 간선도로는 대각선으로 곧게 강을 건너고, 교차로는 강 앞에서 간선에 합류한다.
      var main = centralRiver
        ? Road(P(-120, -123), P(-105, -111), P(-90, -103), P(-81.7f, -103), P(-68, -102), P(-56, -82), P(-53, -63), P(-44, -44), P(-30, -30), P(-8, -8))
        : Road(P(-120, -123), P(-105, -111), P(-90, -103), P(-81.7f, -103), P(-68, -102), P(-55, -82), P(-51, -57), P(-37, -31), P(-8, -8));
      var crossing = centralRiver
        ? Road(P(-136, -108), bridge, P(-128, -67), iron, gate, P(-74, -58), P(-58, -64))
        : Road(P(-136, -108), bridge, P(-128, -67), iron, gate, P(-68, -49), P(-43, -33));
      var commute = CurvedStrip(3f, P(-136, -151), P(-128, -151), P(-125, -136), P(-119, -124));

      return new MapDef {
        Id = "Map4P", Players = 4, HalfSize = 160, Start = Position(-130, -130), RiverAmplitude = 5f, RiverSeed = 23,
        BaseArea = Rect(-121, -121, 35, 35),
        Bridges = new[] { Rect(-81.7, -135, 7.3, 4.4), Rect(-135, -81.7, 7.3, 4.4, 90), Rect(-81.7, -103, 8.8, 5.2) },
        Nodes = new[] {
          // 항목 33 3차에서 본진 철광산을 4000→5500으로 늘려 봤으나(4인전은 10분쯤 본진 철이 바닥나 첫 교전을 진 쪽이 무너진다)
          // 4인 시간 초과가 6 → 14판으로 늘어(ai-33-4p-t3) 4000으로 되돌렸다.
          Mine(-146, -119, 4600), Mine(-119, -146, 4600), Grove(-144, -144, 5.5, 6000),
          Mine(-150, -73, 3000), Mine(-124, -66, 3000), Grove(-150, -58, 5, 2000),
          Grove(-62, -147, 10, 5000), Grove(-67, -122, 7, 3000), Mine(-71, -111, 1500, 2.5),
          Mine(-118, 10, 5000, 3.5), Grove(-118, -6, 6, 5000),
          Grove(-16, -16, 6, 2000),
        },
        // 중앙: 광산·통신 점령지·확장 부지 대신 철조망 암시장(NPC 무기상).
        CenterNodes = new[] { Mine(-22, -9, 6800, 3.5), Mine(22, 9, 6800, 3.5), Grove(-12, 21, 5.5, 7000), Grove(12, -21, 5.5, 7000) },
        Trader = true,
        TraderPosition = Position(0, 0),
        // 블랙박스: 중앙 암시장 둘레 40m 네 곳을 돌아가며 떨어진다(어느 진영에도 같은 거리).
        // 중앙 강(안쪽 43m)이 있으면 블랙박스를 섬 안쪽 32m로 당긴다.
        BlackBoxSpots = centralRiver
          ? new[] { Position(-32, 0), Position(0, 32), Position(32, 0), Position(0, -32) }
          : new[] { Position(-40, 0), Position(0, 40), Position(40, 0), Position(0, -40) },
        Terrain = terrain.ToArray(),
        NoBuildZones = WithMirror(new[] {
          Rect(-140, -97, 9, 8), Rect(-102, -64, 8, 12), Rect(-151, -48, 10, 8), Rect(-147, -24, 6, 12),
        }).Concat(new[] { Rect(-72, 0, 9, 9), Rect(-36, -36, 12, 8, 45) }).Concat(commute)
          .Concat(ramps.Select(ramp => Rect(ramp.Center.X.AsFloat, ramp.Center.Z.AsFloat, 6, 6, ramp.Yaw.AsFloat))).ToArray(),
        CameraPosition = new Vector3(0, 250, -205),
        CaptureSites = new[] { Site(CaptureKind.Relay, relay.x, relay.y, 8), Site(CaptureKind.Radar, -26, -26, 6), Site(CaptureKind.Fuel, -60, -98, 6) },
        CenterSites = new CaptureSitePlacement[0],
        Barricades = new[] { Rect(-72, 0, 7, 3) },
        BaseCrates = new[] { Position(-110, -128), Position(-128, -110) },
        DropCrates = new[] { Position(-70, -30), Position(-142, 12), Position(-149, -18) },
        HighGrounds = grounds.ToArray(),
        Ramps = ramps.ToArray(),
        Roads = main.Concat(crossing).Concat(crossing.Select(Mirror)).Concat(flank).Concat(flank.Select(Mirror)).Concat(commute).ToArray(),
        Fords = new[] { Segment(P(-74, -30), P(-30, -74), FordHalfWidth) },
        Expansions = new[] { Expansion(ExpansionKind.IronForecourt, -136, -72), Expansion(ExpansionKind.WoodForecourt, -72, -136) },
        SharedExpansions = new[] { Expansion(ExpansionKind.Shared, -134, 0) },
        CenterExpansions = new ExpansionSite[0],
        SharedSeatOffsets = new[] { 0, 3 },
        // 반지름 43~57m 물길. 다리 끝은 38m·62m라 강 양쪽 도로와 겹친다.
        ExtraWater = centralRiver ? CentralRiver() : null,
        ExtraBridges = centralRiver ? Enumerable.Range(0, 4).Select(i => Turn(Rect(-35.36, -35.36, 12, 4.6, -45), i)).ToArray() : null,
      };
    }
  }
}
