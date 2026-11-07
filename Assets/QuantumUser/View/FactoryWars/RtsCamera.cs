namespace FactoryWars.View {
  using UnityEngine;

  /// <summary>고정 각도로 땅의 한 점을 내려다보는 카메라. 초점은 맵 경계 안에 묶는다.</summary>
  public class RtsCamera : MonoBehaviour {
    public const float MinHeight = 30f;
    public const float MaxHeight = 170f;
    const float Pitch = 50f;

    public Vector2 BoundsMin = new Vector2(-50, -50);
    public Vector2 BoundsMax = new Vector2(50, 50);
    public Vector2 Focus;
    public float Height = 70f;

    public static Vector2 ClampFocus(Vector2 focus, Vector2 min, Vector2 max) {
      return new Vector2(Mathf.Clamp(focus.x, min.x, max.x), Mathf.Clamp(focus.y, min.y, max.y));
    }

    public static float ClampHeight(float height) => Mathf.Clamp(height, MinHeight, MaxHeight);

    public void MoveTo(Vector2 focus) {
      Focus = ClampFocus(focus, BoundsMin, BoundsMax);
      Apply();
    }

    public void Zoom(float scale) {
      Height = ClampHeight(Height / Mathf.Max(scale, 0.01f));
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
      var back = Height / Mathf.Tan(Pitch * Mathf.Deg2Rad);
      transform.SetPositionAndRotation(new Vector3(Focus.x, Height, Focus.y - back), Quaternion.Euler(Pitch, 0, 0));
    }
  }
}
