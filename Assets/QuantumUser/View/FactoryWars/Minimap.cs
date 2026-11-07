namespace FactoryWars.View {
  using System;
  using Photon.Deterministic;
  using Quantum;
  using UnityEngine;
  using UnityEngine.EventSystems;
  using UnityEngine.UI;

  /// <summary>왼쪽 아래 미니맵. 지형은 MapSpec에서 한 번 칠하고 유닛·건물 점만 다시 찍는다.</summary>
  public class Minimap : MonoBehaviour, IPointerClickHandler, IDragHandler {
    public const int Size = 160;

    static readonly Color32 Ground = new Color32(0x6E, 0x80, 0x58, 0xFF);
    static readonly Color32 IronColor = new Color32(0xE8, 0x82, 0x28, 0xFF);
    static readonly Color32 WoodColor = new Color32(0x1C, 0x60, 0x30, 0xFF);
    static readonly Color32 RockColor = new Color32(0x70, 0x68, 0x62, 0xFF);
    static readonly Color32 HillColor = new Color32(0x80, 0x5E, 0x3C, 0xFF);
    static readonly Color32 WallColor = new Color32(0x42, 0x2E, 0x24, 0xFF);
    static readonly Color32 RiverColor = new Color32(0x24, 0x4C, 0x66, 0xFF);

    const float MarkSize = 34f;

    public Action<Vector2> OnPick;
    public int MarkCount => _marks.Count;

    readonly System.Collections.Generic.Dictionary<int, Image> _marks = new System.Collections.Generic.Dictionary<int, Image>();

    Texture2D _texture;
    Color32[] _base;
    Color32[] _pixels;
    Vector2 _min;
    Vector2 _max;

    public void Init(MapSpec map) {
      _min = new Vector2(map.BoundsMin.X.AsFloat, map.BoundsMin.Y.AsFloat);
      _max = new Vector2(map.BoundsMax.X.AsFloat, map.BoundsMax.Y.AsFloat);
      _texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
      _base = new Color32[Size * Size];
      for (var i = 0; i < _base.Length; i++) {
        _base[i] = Ground;
      }
      foreach (var feature in map.Terrain ?? Array.Empty<TerrainFeature>()) {
        Paint(feature.Shape, feature.Kind == TerrainKind.Rock ? RockColor : feature.Kind == TerrainKind.Hill || feature.Kind == TerrainKind.Cliff ? HillColor : feature.Kind == TerrainKind.River ? RiverColor : WallColor);
      }
      foreach (var node in map.ResourceNodes) {
        Paint(node.Shape, node.Kind == ResourceKind.Iron ? IronColor : WoodColor);
      }
      _pixels = new Color32[_base.Length];
      GetComponent<RawImage>().texture = _texture;
    }

    int ToPixel(float value, float min, float max) => Mathf.Clamp((int)((value - min) / (max - min) * Size), 0, Size - 1);

    Vector2 ToWorld(int x, int y) {
      return new Vector2(_min.x + (x + 0.5f) / Size * (_max.x - _min.x), _min.y + (y + 0.5f) / Size * (_max.y - _min.y));
    }

    void Paint(MapShape shape, Color32 color) {
      var center = new Vector2(shape.Center.X.AsFloat, shape.Center.Z.AsFloat);
      var reach = shape.Reach.AsFloat;
      var x0 = ToPixel(center.x - reach, _min.x, _max.x);
      var x1 = ToPixel(center.x + reach, _min.x, _max.x);
      var y0 = ToPixel(center.y - reach, _min.y, _max.y);
      var y1 = ToPixel(center.y + reach, _min.y, _max.y);
      for (var y = y0; y <= y1; y++) {
        for (var x = x0; x <= x1; x++) {
          var world = ToWorld(x, y);
          var point = new FPVector3(FP.FromFloat_UNSAFE(world.x), FP._0, FP.FromFloat_UNSAFE(world.y));
          if (shape.Distance(point) <= FP._0) {
            _base[y * Size + x] = color;
          }
        }
      }
    }

    void Dot(Vector3 world, int radius, Color32 color) {
      var cx = ToPixel(world.x, _min.x, _max.x);
      var cy = ToPixel(world.z, _min.y, _max.y);
      for (var y = Mathf.Max(cy - radius, 0); y <= Mathf.Min(cy + radius, Size - 1); y++) {
        for (var x = Mathf.Max(cx - radius, 0); x <= Mathf.Min(cx + radius, Size - 1); x++) {
          _pixels[y * Size + x] = color;
        }
      }
    }

    public void Refresh(Frame frame) {
      if (_texture == null) {
        return;
      }
      Array.Copy(_base, _pixels, _base.Length);
      foreach (var pair in frame.GetComponentIterator<Building>()) {
        Dot(frame.Get<Transform3D>(pair.Entity).Position.ToUnityVector3(), 2, Palette.Of(frame, pair.Component.Owner));
      }
      foreach (var pair in frame.GetComponentIterator<Unit>()) {
        Dot(frame.Get<Transform3D>(pair.Entity).Position.ToUnityVector3(), 1, Palette.Of(frame, pair.Component.Owner));
      }
      _texture.SetPixels32(_pixels);
      _texture.Apply(false);
      RefreshMarks(frame);
    }

    /// <summary>플레이어마다 첫 사령부 자리에 진영 마크를 놓는다. 사령부를 다 잃으면 숨긴다.</summary>
    unsafe void RefreshMarks(Frame frame) {
      foreach (var mark in _marks.Values) {
        mark.enabled = false;
      }
      var rect = ((RectTransform)transform).rect;
      var buildings = frame.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out _, out var position, out var building)) {
        var player = (int)building->Owner;
        if (frame.FindAsset(building->Spec).Role != BuildingRole.Headquarters || (_marks.TryGetValue(player, out var mark) && mark.enabled)) {
          continue;
        }
        if (mark == null) {
          var state = frame.Global->Players.GetPointer(building->Owner);
          var faction = frame.FindAsset(state->Faction).FactionId;
          mark = Ui.Icon($"Mark{player}", transform, $"mark_{faction}", Vector2.zero, Vector2.zero, new Vector2(MarkSize, MarkSize));
          ((RectTransform)mark.transform).pivot = new Vector2(0.5f, 0.5f);
          mark.color = Palette.Of(frame, building->Owner);
          _marks[player] = mark;
        }
        mark.enabled = true;
        var world = position->Position.ToUnityVector3();
        ((RectTransform)mark.transform).anchoredPosition = new Vector2(
          Mathf.InverseLerp(_min.x, _max.x, world.x) * rect.width, Mathf.InverseLerp(_min.y, _max.y, world.z) * rect.height);
      }
    }

    void Pick(PointerEventData data) {
      var rect = (RectTransform)transform;
      if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, data.position, data.pressEventCamera, out var local)) {
        var box = rect.rect;
        OnPick?.Invoke(new Vector2(
          Mathf.Lerp(_min.x, _max.x, (local.x - box.xMin) / box.width),
          Mathf.Lerp(_min.y, _max.y, (local.y - box.yMin) / box.height)));
      }
    }

    public void OnPointerClick(PointerEventData data) => Pick(data);

    public void OnDrag(PointerEventData data) => Pick(data);

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
      new Color32(0x1F, 0x3A, 0x6B, 0xFF), new Color32(0xA3, 0x22, 0x2B, 0xFF), new Color32(0x56, 0xB4, 0xE9, 0xFF), new Color32(0xE6, 0x9F, 0x00, 0xFF),
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
