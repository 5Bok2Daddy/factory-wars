namespace FactoryWars.View {
  using System;
  using System.Collections.Generic;
  using Photon.Deterministic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.EventSystems;
  using UnityEngine.UI;

  /// <summary>
  /// 왼쪽 아래 미니맵(화면 전용, 시뮬레이션 상태를 읽기만 한다). 층 순서:
  /// 바탕(지형·물·길·순환로·다리·수문·도시 건물, 한 번만 칠함). 진영 영역 = 사령부·확장 사령부·건물·점령 거점 둘레, 반투명 + 1px 테두리.
  /// → 남은 자원지 마름모 → 안개 3단계(미탐색 검정, 탐색 50%, 시야 맑음) → 진영 영역(안개 위) → 건물 사각형·유닛 점(적은 시야 안만, 적 건물은 마지막으로 본 자리를 흐리게)
  /// → 암시장 아이콘 → 궁극 전력 → 카메라 시야 사다리꼴 → 경보 고리(퍼지는 고리) → 테두리.
  /// 색은 보는 사람 기준 관계색: 나 #2B8AEE, 동맹 #3FC9B4, 적 #E53E3E.
  /// 왼쪽 클릭·끌기 = 카메라 이동, 오른쪽 클릭 = 선택 유닛 이동(Shift = 공격 이동).
  /// </summary>
  public class Minimap : MonoBehaviour, IPointerClickHandler, IDragHandler, IPointerDownHandler {
    public const int Size = 192;

    static readonly Color32 Ground = new Color32(0x5E, 0x6B, 0x4A, 0xFF);
    static readonly Color32 RockColor = new Color32(0x70, 0x68, 0x62, 0xFF);
    static readonly Color32 HillColor = new Color32(0x7A, 0x5E, 0x40, 0xFF);
    static readonly Color32 WallColor = new Color32(0x42, 0x2E, 0x24, 0xFF);
    static readonly Color32 RiverColor = new Color32(0x24, 0x4C, 0x66, 0xFF);
    static readonly Color32 RoadColor = new Color32(0x34, 0x37, 0x3E, 0xFF);
    static readonly Color32 BridgeColor = new Color32(0xB9, 0xB4, 0xA8, 0xFF);
    static readonly Color32 GateColor = new Color32(0x9A, 0xA8, 0xBA, 0xFF);
    static readonly Color32 CityColor = new Color32(118, 116, 112, 255);
    static readonly Color32 RubbleColor = new Color32(150, 128, 98, 255);
    static readonly Color32 BlockedColor = new Color32(140, 40, 34, 255);
    static readonly Color32 BorderColor = new Color32(24, 22, 20, 255);
    static readonly Color32 FogBlack = new Color32(8, 9, 12, 255);
    static readonly Color32 IronDiamond = new Color32(0xF2, 0xC1, 0x4E, 0xFF);
    static readonly Color32 WoodDiamond = new Color32(0x4F, 0xD8, 0xE8, 0xFF);
    static readonly Color32 TraderGold = new Color32(0xFF, 0xC8, 0x2E, 0xFF);
    static readonly Color32 TraderGrey = new Color32(0x9A, 0x9A, 0x9A, 0xFF);
    static readonly Color32 CameraColor = new Color32(255, 255, 255, 255);

    /// <summary>관계색: 0 나, 1 동맹, 2 적.</summary>
    public static readonly Color32[] RelationColors = { new Color32(0x2B, 0x8A, 0xEE, 0xFF), new Color32(0x3F, 0xC9, 0xB4, 0xFF), new Color32(0xE5, 0x3E, 0x3E, 0xFF) };
    public const float TerritoryAlpha = 0.28f;
    // 영역 반경(m, 화면 전용): 사령부(첫 기지·확장 사령부) 30, 그 밖의 건물 9, 점령 거점은 반경 + 8.
    public const float HeadquartersReach = 30f;
    public const float BuildingReach = 9f;
    public const float SiteReach = 8f;

    const float MarkSize = 34f;

    public Action<Vector2> OnPick;
    /// <summary>오른쪽 클릭 명령(월드 x·z, 공격 이동인가).</summary>
    public Action<Vector2, bool> OnOrder;
    public RtsCamera View;
    public int MarkCount => CountMarks();
    public int UltimateMarks { get; private set; }

    readonly Dictionary<int, Image> _marks = new Dictionary<int, Image>();

    Texture2D _texture;
    Color32[] _base;
    Color32[] _pixels;
    byte[] _zone;
    byte[] _fogState;
    Vector2 _min;
    Vector2 _max;
    MapSpec _map;
    MatchController _controller;

    // ---------- 바탕 ----------

    public void Init(MapSpec map) {
      var channel = TerrainKit.Channel(map);
      _min = new Vector2(map.BoundsMin.X.AsFloat - channel, map.BoundsMin.Y.AsFloat - channel);
      _max = new Vector2(map.BoundsMax.X.AsFloat + channel, map.BoundsMax.Y.AsFloat + channel);
      _texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
      _base = new Color32[Size * Size];
      _zone = new byte[Size * Size];
      _fogState = new byte[Size * Size];
      for (var i = 0; i < _base.Length; i++) {
        _base[i] = Ground;
      }
      _map = map;
      foreach (var feature in map.Terrain ?? Array.Empty<TerrainFeature>()) {
        Paint(feature.Shape, feature.Kind == TerrainKind.Rock ? RockColor : feature.Kind == TerrainKind.Hill || feature.Kind == TerrainKind.Cliff ? HillColor : feature.Kind == TerrainKind.River ? RiverColor : WallColor);
      }
      foreach (var strip in map.Channel ?? new MapShape[0]) {
        Paint(strip, RiverColor);
      }
      foreach (var piece in map.River ?? new MapShape[0]) {
        Paint(piece, RiverColor);
      }
      // 길: 3D와 같은 조각(암시장 둘레에서 잘린 길) + 사각 순환로.
      foreach (var road in map.Roads ?? new MapShape[0]) {
        foreach (var (center, half) in TerrainKit.RoadPieces(map, road)) {
          PaintBox(new Vector2(center.x, center.z), road.Yaw.AsFloat, half + road.HalfWidth.AsFloat, road.HalfWidth.AsFloat, RoadColor);
        }
      }
      if (map.HasTrader) {
        var t = new Vector2(map.TraderPosition.X.AsFloat, map.TraderPosition.Z.AsFloat);
        var o = TerrainKit.RingOuter;
        var i = TerrainKit.RingInner;
        PaintBox(t + new Vector2(0f, (i + o) / 2f), 0f, o, (o - i) / 2f, RoadColor);
        PaintBox(t - new Vector2(0f, (i + o) / 2f), 0f, o, (o - i) / 2f, RoadColor);
        PaintBox(t + new Vector2((i + o) / 2f, 0f), 0f, (o - i) / 2f, o, RoadColor);
        PaintBox(t - new Vector2((i + o) / 2f, 0f), 0f, (o - i) / 2f, o, RoadColor);
      }
      foreach (var bridge in map.Bridges ?? new MapShape[0]) {
        Paint(bridge, BridgeColor);
      }
      foreach (var gate in map.WaterGates ?? new MapShape[0]) {
        Paint(gate, GateColor);
      }
      foreach (var city in map.CityBuildings ?? Array.Empty<CityBuildingPlacement>()) {
        Paint(city.Shape, CityColor);
      }
      _pixels = new Color32[_base.Length];
      GetComponent<RawImage>().texture = _texture;
    }

    int ToPixel(float value, float min, float max) => Mathf.Clamp((int)((value - min) / (max - min) * Size), 0, Size - 1);
    int Px(float x) => ToPixel(x, _min.x, _max.x);
    int Py(float z) => ToPixel(z, _min.y, _max.y);
    float PixelsPerMeter => Size / (_max.x - _min.x);

    Vector2 ToWorld(int x, int y) => new Vector2(_min.x + (x + 0.5f) / Size * (_max.x - _min.x), _min.y + (y + 0.5f) / Size * (_max.y - _min.y));

    /// <summary>월드 좌표(x, z) → 미니맵 픽셀(0..Size). 테스트·캡처 확인용.</summary>
    public Vector2Int WorldToPixel(Vector3 world) => new Vector2Int(Px(world.x), Py(world.z));

    public Color32 PixelAt(Vector3 world) => _pixels[Py(world.z) * Size + Px(world.x)];

    void Paint(MapShape shape, Color32 color) {
      var center = new Vector2(shape.Center.X.AsFloat, shape.Center.Z.AsFloat);
      var reach = shape.Reach.AsFloat;
      for (var y = Py(center.y - reach); y <= Py(center.y + reach); y++) {
        for (var x = Px(center.x - reach); x <= Px(center.x + reach); x++) {
          var world = ToWorld(x, y);
          if (shape.Distance(new FPVector3(FP.FromFloat_UNSAFE(world.x), FP._0, FP.FromFloat_UNSAFE(world.y))) <= FP._0) {
            _base[y * Size + x] = color;
          }
        }
      }
    }

    /// <summary>돌린 직사각형(반길이 hx, 반폭 hz, yaw도)을 바탕에 칠한다.</summary>
    void PaintBox(Vector2 center, float yaw, float hx, float hz, Color32 color) {
      var reach = Mathf.Sqrt(hx * hx + hz * hz);
      var rad = yaw * Mathf.Deg2Rad;
      var cos = Mathf.Cos(rad);
      var sin = Mathf.Sin(rad);
      for (var y = Py(center.y - reach); y <= Py(center.y + reach); y++) {
        for (var x = Px(center.x - reach); x <= Px(center.x + reach); x++) {
          var d = ToWorld(x, y) - center;
          // Unity yaw: 길 방향 = (cos, -sin). 로컬 x = d·방향, 로컬 z = d·(sin, cos).
          var lx = d.x * cos - d.y * sin;
          var lz = d.x * sin + d.y * cos;
          if (Mathf.Abs(lx) <= hx && Mathf.Abs(lz) <= hz) {
            _base[y * Size + x] = color;
          }
        }
      }
    }

    // ---------- 매 갱신 ----------

    public PlayerRef Viewer {
      get {
        if (_controller == null) {
          _controller = FindAnyObjectByType<MatchController>();
        }
        return _controller != null ? _controller.Player : PlayerRef.None;
      }
    }

    /// <summary>보는 사람 기준 관계: 0 나, 1 동맹, 2 적. 보는 사람이 없으면(관전·테스트) 0번 플레이어 기준.</summary>
    public static int RelationOf(Frame frame, PlayerRef viewer, PlayerRef owner) {
      if (viewer.IsValid == false) {
        viewer = 0;
      }
      if (owner == viewer) {
        return 0;
      }
      return DiplomacySystem.GetRelation(frame, viewer, owner) == Relation.Allied ? 1 : 2;
    }

    // 적 건물 기억(마지막으로 본 자리). 화면 전용.
    readonly Dictionary<EntityRef, (Vector3 Position, bool Headquarters, PlayerRef Owner)> _known = new Dictionary<EntityRef, (Vector3, bool, PlayerRef)>();
    readonly List<(Vector2 Center, float Radius, int Relation)> _zones = new List<(Vector2, float, int)>();

    /// <summary>지금 그린 영역 픽셀 수(관계별). 테스트용.</summary>
    public readonly int[] TerritoryPixels = new int[3];
    public int GhostCount { get; private set; }
    public int EnemyUnitDots { get; private set; }
    public int ResourceDiamonds { get; private set; }
    public bool CameraDrawn { get; private set; }
    /// <summary>암시장 표시 상태: 0 없음, 1 평화(금색), 2 적대(빨강), 3 파괴(회색 X).</summary>
    public int TraderState { get; private set; }

    public unsafe void Refresh(Frame frame) {
      if (_texture == null) {
        return;
      }
      Array.Copy(_base, _pixels, _base.Length);
      var fogOn = FogSystem.Enabled(frame);
      var viewer = Viewer;
      var fogViewer = fogOn && viewer.IsValid;
      if (fogViewer) {
        BuildFogState(frame, viewer);
      }

      // 1. 영역: 내·동맹 건물은 전부, 적 건물은 지금 보이거나 기억한 것만.
      _zones.Clear();
      var seen = new HashSet<EntityRef>();
      var buildings = frame.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out var entity, out var transform, out var building)) {
        var position = transform->Position.ToUnityVector3();
        var hq = frame.FindAsset(building->Spec).Role == BuildingRole.Headquarters;
        var relation = RelationOf(frame, viewer, building->Owner);
        if (relation == 2 && fogViewer && FactoryWarsUtil.IsVisibleTo(frame, viewer, entity) == false) {
          continue;
        }
        if (relation == 2) {
          _known[entity] = (position, hq, building->Owner);
          seen.Add(entity);
        }
        _zones.Add((new Vector2(position.x, position.z), hq ? HeadquartersReach : BuildingReach, relation));
      }
      // 기억만 남은 적 건물: 그 자리가 지금 보이는데 없어졌으면 지운다.
      var forget = new List<EntityRef>();
      foreach (var pair in _known) {
        if (seen.Contains(pair.Key)) {
          continue;
        }
        if (frame.Exists(pair.Key) == false && (fogViewer == false || VisibleAt(pair.Value.Position))) {
          forget.Add(pair.Key);
          continue;
        }
        _zones.Add((new Vector2(pair.Value.Position.x, pair.Value.Position.z), pair.Value.Headquarters ? HeadquartersReach : BuildingReach, 2));
      }
      foreach (var entity in forget) {
        _known.Remove(entity);
      }
      foreach (var pair in frame.GetComponentIterator<CaptureSite>()) {
        if (pair.Component.Owner.IsValid == false) {
          continue;
        }
        var at = frame.Get<Transform3D>(pair.Entity).Position.ToUnityVector3();
        _zones.Add((new Vector2(at.x, at.z), pair.Component.Radius.AsFloat + SiteReach, RelationOf(frame, viewer, pair.Component.Owner)));
      }

      // 2. 무너진 도시 건물, 남은 자원지 마름모.
      DrawCity(frame);
      ResourceDiamonds = 0;
      foreach (var pair in frame.GetComponentIterator<ResourceNode>()) {
        if (pair.Component.Remaining <= FP._0) {
          continue;
        }
        var c = pair.Component.Shape.Center.ToUnityVector3();
        Diamond(c, 2, pair.Component.Kind == ResourceKind.Iron ? IronDiamond : WoodDiamond);
        ResourceDiamonds++;
      }

      // 3. 안개.
      if (fogViewer) {
        ApplyFog();
      }
      // 영역은 안개 위에: 내·동맹 영역은 늘 알고, 적 영역은 보았거나 기억한 건물에서만 나온다.
      DrawTerritory();

      // 4. 건물 사각형(사령부 6px, 그 밖 4px), 유닛 점(3px). 적 유닛은 시야 안만.
      buildings = frame.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out var entity, out var transform, out var building)) {
        var relation = RelationOf(frame, viewer, building->Owner);
        if (relation == 2 && fogViewer && FactoryWarsUtil.IsVisibleTo(frame, viewer, entity) == false) {
          continue;
        }
        var hq = frame.FindAsset(building->Spec).Role == BuildingRole.Headquarters;
        Square(transform->Position.ToUnityVector3(), hq ? 6 : 4, RelationColors[relation], true);
      }
      GhostCount = 0;
      foreach (var pair in _known) {
        if (seen.Contains(pair.Key)) {
          continue;
        }
        // 마지막으로 본 적 건물: 어두운 빨강 테두리만.
        Square(pair.Value.Position, pair.Value.Headquarters ? 6 : 4, Color32.Lerp(RelationColors[2], FogBlack, 0.45f), false);
        GhostCount++;
      }
      EnemyUnitDots = 0;
      foreach (var pair in frame.GetComponentIterator<Unit>()) {
        if (pair.Component.Garrison != EntityRef.None) {
          continue;
        }
        var relation = RelationOf(frame, viewer, pair.Component.Owner);
        if (relation == 2 && fogViewer && FactoryWarsUtil.IsVisibleTo(frame, viewer, pair.Entity) == false) {
          continue;
        }
        EnemyUnitDots += relation == 2 ? 1 : 0;
        Square(frame.Get<Transform3D>(pair.Entity).Position.ToUnityVector3(), 3, Brighten(RelationColors[relation]), true);
      }

      // 5. 암시장, 궁극 전력, 카메라, 경보, 테두리.
      DrawTrader(frame, viewer);
      DrawUltimates(frame);
      DrawCamera();
      DrawPings();
      DrawBorder();
      _texture.SetPixels32(_pixels);
      _texture.Apply(false);
      RefreshMarks(frame, viewer, fogViewer);
    }

    static Color32 Brighten(Color32 c) => Color32.Lerp(c, new Color32(255, 255, 255, 255), 0.25f);

    // ---------- 영역 ----------

    void DrawTerritory() {
      Array.Clear(_zone, 0, _zone.Length);
      var ppm = PixelsPerMeter;
      foreach (var (center, radius, relation) in _zones) {
        var bit = (byte)(1 << relation);
        var cx = Px(center.x);
        var cy = Py(center.y);
        var r = Mathf.Max(1, Mathf.RoundToInt(radius * ppm));
        var r2 = r * r;
        for (var y = Mathf.Max(0, cy - r); y <= Mathf.Min(Size - 1, cy + r); y++) {
          for (var x = Mathf.Max(0, cx - r); x <= Mathf.Min(Size - 1, cx + r); x++) {
            if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r2) {
              _zone[y * Size + x] |= bit;
            }
          }
        }
      }
      TerritoryPixels[0] = TerritoryPixels[1] = TerritoryPixels[2] = 0;
      for (var y = 0; y < Size; y++) {
        for (var x = 0; x < Size; x++) {
          var i = y * Size + x;
          var mask = _zone[i];
          if (mask == 0) {
            continue;
          }
          var blended = _pixels[i];
          for (var rel = 0; rel < 3; rel++) {
            var bit = 1 << rel;
            if ((mask & bit) == 0) {
              continue;
            }
            TerritoryPixels[rel]++;
            // 1px 테두리: 네 이웃 중 하나라도 이 관계 영역 밖이면 진하게.
            var edge = x == 0 || y == 0 || x == Size - 1 || y == Size - 1 || (_zone[i - 1] & bit) == 0 || (_zone[i + 1] & bit) == 0 || (_zone[i - Size] & bit) == 0 || (_zone[i + Size] & bit) == 0;
            blended = Color32.Lerp(blended, RelationColors[rel], edge ? 0.9f : TerritoryAlpha);
          }
          _pixels[i] = blended;
        }
      }
    }

    // ---------- 안개 ----------

    int[] _fogIndex;

    unsafe void BuildFogState(Frame frame, PlayerRef viewer) {
      var fog = frame.Unsafe.GetPointerSingleton<Quantum.Fog>();
      var size = FogSystem.Size;
      if (_fogIndex == null) {
        _fogIndex = new int[Size * Size];
        var minX = fog->MinX.AsFloat;
        var minZ = fog->MinZ.AsFloat;
        var cell = fog->Cell.AsFloat;
        for (var y = 0; y < Size; y++) {
          for (var x = 0; x < Size; x++) {
            var w = ToWorld(x, y);
            var cx = Mathf.FloorToInt((w.x - minX) / cell);
            var cz = Mathf.FloorToInt((w.y - minZ) / cell);
            _fogIndex[y * Size + x] = cx < 0 || cx >= size || cz < 0 || cz >= size ? -1 : cz * size + cx;
          }
        }
      }
      var index = (int)viewer;
      if (index < 0 || index >= 4) {
        Array.Clear(_fogState, 0, _fogState.Length);
        return;
      }
      var grid = fog->Grids.GetPointer(index);
      for (var i = 0; i < _fogState.Length; i++) {
        var c = _fogIndex[i];
        if (c < 0) {
          _fogState[i] = 0;
          continue;
        }
        var bit = 1 << (c & 7);
        _fogState[i] = (byte)((grid->Visible[c >> 3] & bit) != 0 ? 2 : (grid->Explored[c >> 3] & bit) != 0 ? 1 : 0);
      }
    }

    bool VisibleAt(Vector3 world) => _fogState[Py(world.z) * Size + Px(world.x)] == 2;

    /// <summary>미탐색 = 검정, 탐색 = 50% 어둡게, 시야 = 그대로.</summary>
    void ApplyFog() {
      for (var i = 0; i < _pixels.Length; i++) {
        var state = _fogState[i];
        if (state == 0) {
          _pixels[i] = FogBlack;
        } else if (state == 1) {
          _pixels[i] = Color32.Lerp(_pixels[i], FogBlack, 0.5f);
        }
      }
    }

    // ---------- 아이콘 ----------

    void Square(Vector3 world, int size, Color32 color, bool fill) {
      var cx = Px(world.x);
      var cy = Py(world.z);
      var x0 = cx - size / 2;
      var y0 = cy - size / 2;
      for (var y = y0; y < y0 + size; y++) {
        for (var x = x0; x < x0 + size; x++) {
          if (x < 0 || y < 0 || x >= Size || y >= Size) {
            continue;
          }
          var border = x == x0 || y == y0 || x == x0 + size - 1 || y == y0 + size - 1;
          if (fill || border) {
            // 4px 이상 사각형은 어두운 1px 외곽선으로 바탕과 갈라 선명하게.
            _pixels[y * Size + x] = fill && border && size >= 4 ? Color32.Lerp(color, FogBlack, 0.55f) : color;
          }
        }
      }
    }

    void Diamond(Vector3 world, int radius, Color32 color) {
      var cx = Px(world.x);
      var cy = Py(world.z);
      for (var y = -radius; y <= radius; y++) {
        for (var x = -radius; x <= radius; x++) {
          if (Mathf.Abs(x) + Mathf.Abs(y) <= radius) {
            Set(cx + x, cy + y, color);
          }
        }
      }
    }

    void Set(int x, int y, Color32 color) {
      if (x >= 0 && y >= 0 && x < Size && y < Size) {
        _pixels[y * Size + x] = color;
      }
    }

    unsafe void DrawTrader(Frame frame, PlayerRef viewer) {
      TraderState = 0;
      foreach (var pair in frame.GetComponentIterator<NpcTrader>()) {
        var at = frame.Get<Transform3D>(pair.Entity).Position.ToUnityVector3();
        var cx = Px(at.x);
        var cy = Py(at.z);
        if (pair.Component.Destroyed) {
          TraderState = 3;
          for (var i = -3; i <= 3; i++) {
            Set(cx + i, cy + i, TraderGrey);
            Set(cx + i, cy - i, TraderGrey);
          }
          continue;
        }
        var index = viewer.IsValid ? (int)viewer : 0;
        var hostile = (pair.Component.HostileMask & (1 << index)) != 0;
        TraderState = hostile ? 2 : 1;
        var color = hostile ? RelationColors[2] : TraderGold;
        Diamond(at, 4, FogBlack);
        Diamond(at, 3, color);
      }
    }

    /// <summary>무너진 건물은 잔해 색, 쓰러진 건물이 막은 구역은 붉게 칠한다.</summary>
    void DrawCity(Frame frame) {
      var cities = _map?.CityBuildings;
      for (var i = 0; cities != null && i < cities.Length; i++) {
        if (CitySystem.IsCollapsed(frame, i) == false) {
          continue;
        }
        PaintLive(cities[i].Shape, RubbleColor);
        if (cities[i].Topples) {
          PaintLive(cities[i].FallZone, BlockedColor);
        }
      }
    }

    void PaintLive(MapShape shape, Color32 color) {
      var center = new Vector2(shape.Center.X.AsFloat, shape.Center.Z.AsFloat);
      var reach = shape.Reach.AsFloat;
      for (var y = Py(center.y - reach); y <= Py(center.y + reach); y++) {
        for (var x = Px(center.x - reach); x <= Px(center.x + reach); x++) {
          var world = ToWorld(x, y);
          if (shape.Distance(new FPVector3(FP.FromFloat_UNSAFE(world.x), FP._0, FP.FromFloat_UNSAFE(world.y))) <= FP._0) {
            _pixels[y * Size + x] = color;
          }
        }
      }
    }

    static readonly Color32 UltimateColor = new Color32(255, 255, 255, 255);
    static readonly Color32 StrikeColor = new Color32(255, 60, 40, 255);

    void Ring(Vector3 world, int radius, Color32 color) {
      var cx = Px(world.x);
      var cy = Py(world.z);
      var steps = Mathf.Max(16, radius * 8);
      for (var a = 0; a < steps; a++) {
        var angle = a * Mathf.PI * 2f / steps;
        Set(cx + Mathf.RoundToInt(Mathf.Cos(angle) * radius), cy + Mathf.RoundToInt(Mathf.Sin(angle) * radius), color);
      }
    }

    /// <summary>궁극 전력 표시(모두에게 보인다): 준비 중·발동 대기인 전용 건물에 흰 고리, 미사일 착탄 지점에 폭발 반경만큼 붉은 고리.</summary>
    unsafe void DrawUltimates(Frame frame) {
      UltimateMarks = 0;
      for (var i = 0; i < frame.PlayerCount; i++) {
        var state = frame.Global->Players.GetPointer(i);
        if (state->Active == false || (state->UltimatePhase != UltimatePhase.Preparing && state->UltimatePhase != UltimatePhase.Ready)
            || frame.Exists(state->UltimateSite) == false) {
          continue;
        }
        var world = frame.Get<Transform3D>(state->UltimateSite).Position.ToUnityVector3();
        Square(world, 5, Palette.Of(frame, i), true);
        Ring(world, 6, UltimateColor);
        Ring(world, 7, UltimateColor);
        UltimateMarks++;
      }
      foreach (var pair in frame.GetComponentIterator<MissileStrike>()) {
        var spec = frame.FindAsset(pair.Component.Spec);
        var radius = Mathf.Max(3, Mathf.RoundToInt(spec.BlastRadius.AsFloat * PixelsPerMeter));
        var world = pair.Component.Target.ToUnityVector3();
        Ring(world, radius, StrikeColor);
        Ring(world, Mathf.Max(1, radius - 1), StrikeColor);
        UltimateMarks++;
      }
    }

    // ---------- 카메라 시야 ----------

    /// <summary>주 카메라 화면 네 모서리가 땅에 닿는 자리를 잇는 흰 사다리꼴.</summary>
    void DrawCamera() {
      CameraDrawn = false;
      if (View == null) {
        return;
      }
      var camera = View.GetComponent<Camera>();
      if (camera == null) {
        return;
      }
      var w = camera.pixelWidth;
      var h = camera.pixelHeight;
      var screen = new[] { new Vector2(0, 0), new Vector2(w, 0), new Vector2(w, h), new Vector2(0, h) };
      var corners = new Vector2Int[4];
      for (var i = 0; i < 4; i++) {
        if (View.ToGround(screen[i], out var ground) == false) {
          return;
        }
        corners[i] = new Vector2Int(Px(ground.x), Py(ground.z));
      }
      for (var i = 0; i < 4; i++) {
        Line(corners[i], corners[(i + 1) % 4], CameraColor);
      }
      CameraDrawn = true;
    }

    void Line(Vector2Int a, Vector2Int b, Color32 color) {
      var dx = Mathf.Abs(b.x - a.x);
      var dy = -Mathf.Abs(b.y - a.y);
      var sx = a.x < b.x ? 1 : -1;
      var sy = a.y < b.y ? 1 : -1;
      var err = dx + dy;
      var x = a.x;
      var y = a.y;
      for (var guard = 0; guard < Size * 4; guard++) {
        Set(x, y, color);
        if (x == b.x && y == b.y) {
          break;
        }
        var e2 = 2 * err;
        if (e2 >= dy) {
          err += dy;
          x += sx;
        }
        if (e2 <= dx) {
          err += dx;
          y += sy;
        }
      }
    }

    void DrawBorder() {
      for (var i = 0; i < Size; i++) {
        _pixels[i] = BorderColor;
        _pixels[(Size - 1) * Size + i] = BorderColor;
        _pixels[i * Size] = BorderColor;
        _pixels[i * Size + Size - 1] = BorderColor;
      }
    }

    // ---------- 경보 고리 ----------

    const float PingSeconds = 4f;
    const float PingPeriod = 1f;
    public static readonly Color32 PingColor = new Color32(255, 220, 60, 255);
    readonly List<(Vector3 World, float Until, Color32 Color)> _pings = new List<(Vector3, float, Color32)>();

    public int PingCount => _pings.Count;

    /// <summary>미니맵에 퍼지는 경보 고리를 잠시 띄운다(보급 낙하·공격받음·암시장 적대 등).</summary>
    public void Ping(Vector3 world) => Ping(world, PingColor);

    public void Ping(Vector3 world, Color32 color) => _pings.Add((world, Time.unscaledTime + PingSeconds, color));

    void DrawPings() {
      _pings.RemoveAll(p => Time.unscaledTime >= p.Until);
      foreach (var (world, until, color) in _pings) {
        // 1초마다 반지름 3 → 11px로 퍼지는 고리 두 겹.
        var phase = Mathf.Repeat((PingSeconds - (until - Time.unscaledTime)) / PingPeriod, 1f);
        var radius = Mathf.RoundToInt(Mathf.Lerp(3f, 11f, phase));
        Ring(world, radius, color);
        Ring(world, radius + 1, color);
        Ring(world, 2, color);
      }
    }

    // ---------- 진영 마크(사령부 위) ----------

    int CountMarks() {
      var count = 0;
      foreach (var mark in _marks.Values) {
        count += mark.enabled ? 1 : 0;
      }
      return count;
    }

    /// <summary>플레이어마다 첫 사령부 자리에 진영 마크. 적 사령부는 보이거나 기억한 것만.</summary>
    unsafe void RefreshMarks(Frame frame, PlayerRef viewer, bool fogViewer) {
      foreach (var mark in _marks.Values) {
        mark.enabled = false;
      }
      var rect = ((RectTransform)transform).rect;
      var buildings = frame.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out var entity, out var position, out var building)) {
        var player = (int)building->Owner;
        if (frame.FindAsset(building->Spec).Role != BuildingRole.Headquarters || (_marks.TryGetValue(player, out var mark) && mark.enabled)) {
          continue;
        }
        var relation = RelationOf(frame, viewer, building->Owner);
        if (relation == 2 && fogViewer && FactoryWarsUtil.IsVisibleTo(frame, viewer, entity) == false && _known.ContainsKey(entity) == false) {
          continue;
        }
        if (mark == null) {
          var state = frame.Global->Players.GetPointer(building->Owner);
          var faction = frame.FindAsset(state->Faction).FactionId;
          mark = Ui.Icon($"Mark{player}", transform, $"mark_{faction}", Vector2.zero, Vector2.zero, new Vector2(MarkSize, MarkSize));
          ((RectTransform)mark.transform).pivot = new Vector2(0.5f, 0.5f);
          _marks[player] = mark;
        }
        mark.color = RelationColors[relation];
        mark.enabled = true;
        var world = position->Position.ToUnityVector3();
        ((RectTransform)mark.transform).anchoredPosition = new Vector2(
          Mathf.InverseLerp(_min.x, _max.x, world.x) * rect.width, Mathf.InverseLerp(_min.y, _max.y, world.z) * rect.height);
      }
    }

    // ---------- 입력 ----------

    bool ToWorldPoint(PointerEventData data, out Vector2 world) {
      var rect = (RectTransform)transform;
      world = default;
      if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, data.position, data.pressEventCamera, out var local) == false) {
        return false;
      }
      var box = rect.rect;
      world = new Vector2(Mathf.Lerp(_min.x, _max.x, (local.x - box.xMin) / box.width), Mathf.Lerp(_min.y, _max.y, (local.y - box.yMin) / box.height));
      return true;
    }

    /// <summary>미니맵 위 비율(0..1) → 월드 x·z. 테스트용.</summary>
    public Vector2 NormalizedToWorld(Vector2 normalized) => new Vector2(Mathf.Lerp(_min.x, _max.x, normalized.x), Mathf.Lerp(_min.y, _max.y, normalized.y));

    public void OnPointerDown(PointerEventData data) {
      if (data.button == PointerEventData.InputButton.Left && ToWorldPoint(data, out var world)) {
        OnPick?.Invoke(world);
      }
    }

    public void OnPointerClick(PointerEventData data) {
      if (ToWorldPoint(data, out var world) == false) {
        return;
      }
      if (data.button == PointerEventData.InputButton.Right) {
        var attack = UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);
        OnOrder?.Invoke(world, attack);
      } else if (data.button == PointerEventData.InputButton.Left) {
        OnPick?.Invoke(world);
      }
    }

    public void OnDrag(PointerEventData data) {
      if (data.button == PointerEventData.InputButton.Left && ToWorldPoint(data, out var world)) {
        OnPick?.Invoke(world);
      }
    }

    void OnDestroy() {
      if (_texture != null) {
        Destroy(_texture);
      }
    }
  }

  /// <summary>
  /// 플레이어 색 8개. 색각 이상 시뮬레이션에서도 어떤 두 색이든 구분된다(LocalMatchTests.Palette_*).
  /// 앞의 둘은 진영 기본 추천색이다.
  /// </summary>
  public static unsafe class Palette {
    public const int Count = 8;

    public static readonly Color32[] Colors = {
      new Color32(0x5B, 0x8D, 0xEF, 0xFF), new Color32(0xFF, 0x6B, 0x6B, 0xFF), new Color32(0x56, 0xB4, 0xE9, 0xFF), new Color32(0xE6, 0x9F, 0x00, 0xFF),
      new Color32(0x00, 0x9E, 0x73, 0xFF), new Color32(0xF0, 0xE4, 0x42, 0xFF), new Color32(0xCC, 0x79, 0xA7, 0xFF), new Color32(0xF2, 0xF2, 0xF2, 0xFF),
    };

    public static readonly string[] Names = { "남색", "진홍", "하늘", "주황", "청록", "노랑", "분홍", "흰색" };

    /// <summary>진영 기본 추천색. 자유 연방은 남색, 대륙 연방은 진홍이다.</summary>
    public static int Recommended(int factionIndex) => factionIndex % 2;

    public static Color32 Of(Frame frame, PlayerRef player) => Colors[frame.Global->Players.GetPointer(player)->ColorIndex % Count];

    public static string Hex(Color32 color) => $"#{color.r:X2}{color.g:X2}{color.b:X2}";

    /// <summary>색 위에 쓸 글자가 잘 보이도록 밝은 색이면 true.</summary>
    public static bool IsLight(Color32 color) => 0.299f * color.r + 0.587f * color.g + 0.114f * color.b > 150f;
  }
}
