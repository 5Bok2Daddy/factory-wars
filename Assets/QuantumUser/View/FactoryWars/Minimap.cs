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

    public Action<Vector2> OnPick;

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
        Paint(feature.Shape, feature.Kind == TerrainKind.Rock ? RockColor : feature.Kind == TerrainKind.Hill ? HillColor : WallColor);
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
        Dot(frame.Get<Transform3D>(pair.Entity).Position.ToUnityVector3(), 2, Palette.Player(pair.Component.Owner));
      }
      foreach (var pair in frame.GetComponentIterator<Unit>()) {
        Dot(frame.Get<Transform3D>(pair.Entity).Position.ToUnityVector3(), 1, Palette.Player(pair.Component.Owner));
      }
      _texture.SetPixels32(_pixels);
      _texture.Apply(false);
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

  public static class Palette {
    public static readonly Color32 FreeFederation = new Color32(0x1F, 0x3A, 0x6B, 0xFF);
    public static readonly Color32 ContinentalFederation = new Color32(0xA3, 0x22, 0x2B, 0xFF);
    public static readonly Color32 ThirdPlayer = new Color32(0x46, 0x8C, 0xD7, 0xFF);
    public static readonly Color32 FourthPlayer = new Color32(0xE1, 0xAF, 0x32, 0xFF);

    /// <summary>자리별 색. 1번은 남색, 2번은 진홍, 3·4번은 별도 색이다.</summary>
    public static Color32 Player(PlayerRef player) {
      switch ((int)player) {
        case 0: return FreeFederation;
        case 1: return ContinentalFederation;
        case 2: return ThirdPlayer;
        default: return FourthPlayer;
      }
    }
  }
}
