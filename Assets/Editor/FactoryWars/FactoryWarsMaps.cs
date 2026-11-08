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

    /// <summary>폴리라인을 도로 조각으로 나눈다.</summary>
    static IEnumerable<MapShape> Road(params Vector2[] points) {
      for (var i = 0; i + 1 < points.Length; i++) {
        yield return Segment(points[i], points[i + 1], RoadHalfWidth);
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

    static readonly MapDef TwoPlayerMap = BuildTwoPlayer();
    static readonly MapDef FourPlayerMap = BuildFourPlayer();

    static MapDef BuildTwoPlayer() {
      var terrain = new List<TerrainFeature>();
      // 앞마당 벽. 대각선 거울로 숲 앞마당도 같은 모양이다.
      terrain.AddRange(Features(TerrainKind.Hill, WithMirror(Ridge(1, 6, P(-62, -50), P(-62, -42)).Concat(Ridge(11, 6, P(-62, -21), P(-62, -16))).Concat(Ridge(2, 6, P(-96, 18), P(-58, 18))))));
      // 공유 모서리 확장을 두르는 벽과 안쪽 돌출부. 측면 길은 이 안을 지나야 한다.
      var box = Ridge(3, 6, P(-96, 62), P(-62, 62));
      terrain.AddRange(Features(TerrainKind.Hill, box.Concat(box.Select(Anti)).Concat(Ridge(4, 6, P(-62, 62), P(-90, 90)))));
      terrain.Add(Feature(TerrainKind.CanyonWall, Rect(-116, 60, 12, 4, 90)));
      terrain.Add(Feature(TerrainKind.CanyonWall, Rect(-60, 116, 12, 4)));
      // 중앙과 측면을 나누는 능선. 뒷길 틈은 A쪽으로 치우쳐 있다.
      terrain.AddRange(Features(TerrainKind.Hill, Ridge(5, 6, P(-60, 16), P(-52, 23)).Concat(Ridge(6, 6, P(-35, 40), P(-18, 56))).Concat(Ridge(7, 6, P(-32, 44), P(-62, 62)))));
      terrain.AddRange(Rocks(8, (-14, -24), (-26, 12), (-44, -12), (12, -44)));

      var grounds = new List<MapShape>();
      var ramps = new List<MapShape>();
      HighGround(-36, -36, 10, new[] { 45f }, terrain, grounds, ramps);
      HighGround(-78, 47, 8, new[] { -90f }, terrain, grounds, ramps);

      var bridge = P(-100, -50);
      var iron = P(-82, -24);
      var gate = P(-62, 3);
      var relay = P(-106, 106);
      var center = P(-8, 0);
      var lane = new[] { iron, P(-111, 18), P(-107, 46), P(-107, 72), relay };
      var roads = Road(bridge, iron, gate, P(-30, 2), center).Concat(Road(lane)).ToList();

      return new MapDef {
        Id = "Map2P", Players = 2, HalfSize = 120, Start = Position(-90, -90), RiverAmplitude = 4.5f, RiverSeed = 11,
        BaseArea = Rect(-90, -90, 30, 30),
        Bridges = new[] { Rect(-57, -100, 3.5, 6.5), Rect(-100, -57, 6.5, 3.5) },
        Nodes = new[] {
          Mine(-105, -78, 4000), Mine(-78, -105, 4000), Grove(-106, -106, 9, 6000),
          Mine(-108, -28, 3000), Mine(-84, -20, 3000), Grove(-109, -5, 6, 2000),
          Grove(-30, -105, 11, 5000), Grove(-5, -106, 8, 3000), Mine(-30, -84, 1500, 2.5),
          Mine(-113, 88, 5000, 3.5), Grove(-86, 108, 9, 5000),
          Grove(-24, 26, 6, 2000),
        },
        CenterNodes = new[] { Mine(0, 0, 8000, 5) },
        Terrain = terrain.ToArray(),
        NoBuildZones = WithMirror(new[] {
          Rect(-100, -57, 9, 8), Rect(-62, 3, 8, 12), Rect(-111, 18, 10, 8), Rect(-107, 60, 6, 14),
        }).Concat(new[] { Rect(-44, 31, 10, 9, -45) }).Concat(ramps.Select(ramp => Rect(ramp.Center.X.AsFloat, ramp.Center.Z.AsFloat, 6, 6, ramp.Yaw.AsFloat))).ToArray(),
        CameraPosition = new Vector3(0, 190, -155),
        CaptureSites = new[] { Site(CaptureKind.Relay, relay.x, relay.y, 8) },
        CenterSites = new[] { Site(CaptureKind.Comms, 0, 0, 12) },
        Barricades = new[] { Rect(-44, 31, 8, 3, -45) },
        BaseCrates = new[] { Position(-70, -88), Position(-88, -70) },
        DropCrates = new[] { Position(-36, 8), Position(8, -36), Position(-96, 34), Position(-114, 114) },
        HighGrounds = grounds.ToArray(),
        Ramps = ramps.ToArray(),
        Roads = roads.Concat(roads.Select(Mirror)).ToArray(),
        Fords = new[] { Segment(P(-50, 8), P(8, -50), FordHalfWidth) },
        Expansions = new[] { Expansion(ExpansionKind.IronForecourt, -96, -30), Expansion(ExpansionKind.WoodForecourt, -30, -88) },
        SharedExpansions = new[] { Expansion(ExpansionKind.Shared, -106, 106) },
        CenterExpansions = new[] { Expansion(ExpansionKind.Center, 0, 0) },
        SharedSeatOffsets = new[] { 0, 1 },
      };
    }

    static MapDef BuildFourPlayer() {
      var terrain = new List<TerrainFeature>();
      terrain.AddRange(Features(TerrainKind.Hill, WithMirror(Ridge(1, 6, P(-102, -90), P(-102, -80)).Concat(Ridge(2, 6, P(-136, -48), P(-98, -48))))));
      // 서쪽 변 공유 확장. 나머지 세 곳은 회전 복사본이다.
      var box = Ridge(3, 6, P(-136, -24), P(-100, -24));
      terrain.AddRange(Features(TerrainKind.Hill, box.Concat(box.Select(ReflectZ)).Concat(Ridge(4, 6, P(-100, -24), P(-100, 24)))));
      var canyon = Rect(-156, -24, 10, 4, 90);
      terrain.Add(Feature(TerrainKind.CanyonWall, canyon));
      terrain.Add(Feature(TerrainKind.CanyonWall, ReflectZ(canyon)));
      // 상자에서 중앙 고리까지 이어지는 능선. 축 위에 뒷길 틈이 있다.
      terrain.AddRange(Features(TerrainKind.Hill, Ridge(5, 6, P(-96, 0), P(-84, 0)).Concat(Ridge(6, 6, P(-60, 0), P(-50, 0)))));
      // 중앙 고리의 벽. 모서리 네 곳이 입구다.
      terrain.AddRange(Features(TerrainKind.Hill, Ridge(7, 6, P(-44, -24), P(-44, -12)).Concat(Ridge(12, 6, P(-44, 12), P(-44, 24)))));
      terrain.AddRange(Rocks(8, (-74, -46), (-46, -74)));

      var grounds = new List<MapShape>();
      var ramps = new List<MapShape>();
      HighGround(-66, -66, 10, new[] { 45f, 225f }, terrain, grounds, ramps);

      var bridge = P(-140, -90);
      var iron = P(-124, -74);
      var gate = P(-102, -64);
      var relay = P(-134, 0);
      var lane = new[] { iron, P(-151, -48), P(-147, -24), relay };
      var roads = Road(bridge, iron, gate, P(-60, -40), P(-36, -36), P(-8, -8)).Concat(Road(lane)).ToList();

      return new MapDef {
        Id = "Map4P", Players = 4, HalfSize = 160, Start = Position(-130, -130), RiverAmplitude = 5f, RiverSeed = 23,
        BaseArea = Rect(-130, -130, 30, 30),
        Bridges = new[] { Rect(-97, -140, 3.5, 6.5), Rect(-140, -97, 6.5, 3.5) },
        Nodes = new[] {
          Mine(-145, -118, 4000), Mine(-118, -145, 4000), Grove(-146, -146, 9, 6000),
          Mine(-148, -76, 3000), Mine(-124, -66, 3000), Grove(-150, -58, 5, 2000),
          Grove(-76, -147, 10, 5000), Grove(-67, -122, 7, 3000), Mine(-82, -118, 1500, 2.5),
          Mine(-118, 10, 5000, 3.5), Grove(-118, -6, 6, 5000),
          Grove(-16, -16, 6, 2000),
        },
        CenterNodes = new[] { Mine(0, 0, 8000, 5) },
        Terrain = terrain.ToArray(),
        NoBuildZones = WithMirror(new[] {
          Rect(-140, -97, 9, 8), Rect(-102, -64, 8, 12), Rect(-151, -48, 10, 8), Rect(-147, -24, 6, 12),
        }).Concat(new[] { Rect(-72, 0, 9, 9), Rect(-36, -36, 12, 8, 45) }).Concat(ramps.Select(ramp => Rect(ramp.Center.X.AsFloat, ramp.Center.Z.AsFloat, 6, 6, ramp.Yaw.AsFloat))).ToArray(),
        CameraPosition = new Vector3(0, 250, -205),
        CaptureSites = new[] { Site(CaptureKind.Relay, relay.x, relay.y, 8) },
        CenterSites = new[] { Site(CaptureKind.Comms, 0, 0, 12) },
        Barricades = new[] { Rect(-72, 0, 7, 3) },
        BaseCrates = new[] { Position(-110, -128), Position(-128, -110) },
        DropCrates = new[] { Position(-70, -30), Position(-142, 12) },
        HighGrounds = grounds.ToArray(),
        Ramps = ramps.ToArray(),
        Roads = roads.Concat(roads.Select(Mirror)).ToArray(),
        Fords = new[] { Segment(P(-74, -30), P(-30, -74), FordHalfWidth) },
        Expansions = new[] { Expansion(ExpansionKind.IronForecourt, -136, -72), Expansion(ExpansionKind.WoodForecourt, -72, -136) },
        SharedExpansions = new[] { Expansion(ExpansionKind.Shared, -134, 0) },
        CenterExpansions = new[] { Expansion(ExpansionKind.Center, 0, 0) },
        SharedSeatOffsets = new[] { 0, 3 },
      };
    }
  }
}
