namespace FactoryWars.View {
  using UnityEngine;

  /// <summary>고정 각도로 땅의 한 점을 내려다보는 카메라. 초점은 맵 경계 안에 묶는다.</summary>
  public class RtsCamera : MonoBehaviour {
    public static float MinHeight => VisualProfile.Current.MinHeight;
    public static float MaxHeight => VisualProfile.Current.MaxHeight;
    public static float DefaultHeight => VisualProfile.Current.DefaultHeight;
    static float Pitch => VisualProfile.Current.Pitch;

    public Vector2 BoundsMin = new Vector2(-50, -50);
    public Vector2 BoundsMax = new Vector2(50, 50);
    public Vector2 Focus;
    public float Height = 45f;

    void Awake() {
      Height = DefaultHeight;
    }

    public static Vector2 ClampFocus(Vector2 focus, Vector2 min, Vector2 max) {
      return new Vector2(Mathf.Clamp(focus.x, min.x, max.x), Mathf.Clamp(focus.y, min.y, max.y));
    }

    public static float ClampHeight(float height) => Mathf.Clamp(height, MinHeight, MaxHeight);

    /// <summary>경계 밖이 화면 가로의 10%를 넘지 않는 범위. 화면 가로 절반의 80%만큼 경계 안쪽으로 초점을 묶는다.</summary>
    const float EdgeShare = 0.8f;

    float HalfWidthAt(float height) {
      var camera = GetComponent<Camera>();
      var distance = height / Mathf.Sin(Pitch * Mathf.Deg2Rad);
      return distance * Mathf.Tan(VisualProfile.Current.FieldOfView * 0.5f * Mathf.Deg2Rad) * Mathf.Max(camera.aspect, 1f);
    }

    /// <summary>맵 가로를 다 보여 주는 높이. 이보다 높이 올라가면 경계 밖이 넓게 보인다.</summary>
    public float FitHeight() {
      var width = BoundsMax.x - BoundsMin.x;
      var perUnit = HalfWidthAt(1f);
      return width * 0.5f / EdgeShare / Mathf.Max(perUnit, 0.0001f);
    }

    Vector2 ClampToView(Vector2 focus) {
      var half = HalfWidthAt(Height) * EdgeShare;
      var depth = half / Mathf.Max(GetComponent<Camera>().aspect, 1f) / Mathf.Sin(Pitch * Mathf.Deg2Rad);
      float Axis(float value, float min, float max, float reach) => max - min <= reach * 2 ? (min + max) / 2 : Mathf.Clamp(value, min + reach, max - reach);
      return new Vector2(Axis(focus.x, BoundsMin.x, BoundsMax.x, half), Axis(focus.y, BoundsMin.y, BoundsMax.y, depth));
    }

    public void MoveTo(Vector2 focus) {
      Focus = ClampFocus(focus, BoundsMin, BoundsMax);
      Apply();
    }

    public void Zoom(float scale) {
      Height = Mathf.Min(ClampHeight(Height / Mathf.Max(scale, 0.01f)), Mathf.Max(MinHeight, FitHeight()));
      Apply();
    }

    /// <summary>손가락이 짚은 땅이 손가락을 따라오도록 초점을 옮긴다.</summary>
    public void Drag(Vector2 screenFrom, Vector2 screenTo) {
      if (ToGround(screenFrom, out var from) && ToGround(screenTo, out var to)) {
        MoveTo(Focus + new Vector2(from.x - to.x, from.z - to.z));
      }
    }

    public bool ToGround(Vector2 screen, out Vector3 world) {
      var ray = GetComponent<Camera>().ScreenPointToRay(screen);
      if (Mathf.Abs(ray.direction.y) < 0.0001f) {
        world = default;
        return false;
      }
      world = ray.origin + ray.direction * (-ray.origin.y / ray.direction.y);
      return true;
    }

    public void Apply() {
      GetComponent<Camera>().fieldOfView = VisualProfile.Current.FieldOfView;
      Focus = ClampToView(ClampFocus(Focus, BoundsMin, BoundsMax));
      var back = Height / Mathf.Tan(Pitch * Mathf.Deg2Rad);
      transform.SetPositionAndRotation(new Vector3(Focus.x, Height, Focus.y - back), Quaternion.Euler(Pitch, 0, 0));
    }
  }
}
