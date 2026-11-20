namespace FactoryWars.View {
  using Quantum;
  using UnityEngine;

  /// <summary>채굴 시설에서 사령부로 원자재를 보내는 화면 전용 컨베이어와 재사용 화물.</summary>
  public sealed class ExtractorDeliveryView {
    const float CargoSpeed = 4.8f;
    const int CargoCount = 2;
    const float ConveyorWidth = 1.42f;
    const float ConveyorHeight = 0.57f;
    const float BeltHeight = 0.59f;
    readonly Vector3[] _points;
    readonly Transform[] _cargo = new Transform[CargoCount];
    readonly float[] _distance = new float[CargoCount];
    readonly float _length;
    readonly float _cargoScale;
    readonly ConveyorBeltSurface _belt;
    float _nextLaunch;
    int _nextCargo;

    public readonly GameObject Root;
    public EntityRef Headquarters { get; }
    public Transform FirstCargo => _cargo[0];
    public float BeltPhase => _belt != null ? _belt.Phase : 0f;

    public static ExtractorDeliveryView Create(Transform parent, MapSpec map, EntityRef headquarters, Vector3 from,
        float sourceRadius, Vector3 to, float destinationRadius, GameObject cargoPrefab) {
      var direction = Vector3.ProjectOnPlane(to - from, Vector3.up);
      var length = direction.magnitude;
      if (length <= sourceRadius + destinationRadius + 0.5f ||
          VisualCatalog.TryFilled(VisualCatalog.FxId("extractor_conveyor"), out var slot) == false) return null;
      direction /= length;
      var edgeFactor = Mathf.Max(Mathf.Abs(direction.x), Mathf.Abs(direction.z));
      // 판정 부지보다 작은 모델에도 컨베이어 끝이 닿도록 안으로 넣는다.
      var start = from + direction * (sourceRadius * 0.76f / edgeFactor);
      var end = to - direction * (destinationRadius * 0.86f / edgeFactor);
      if (Vector3.Dot(end - start, direction) < 0.5f) return null;
      return new ExtractorDeliveryView(parent, map, headquarters, start, end, slot, cargoPrefab);
    }

    ExtractorDeliveryView(Transform parent, MapSpec map, EntityRef headquarters, Vector3 from, Vector3 to,
        VisualSlot conveyorSlot, GameObject cargoPrefab) {
      Headquarters = headquarters;
      Root = new GameObject("ExtractorDelivery");
      Root.transform.SetParent(parent, false);
      _length = Vector3.ProjectOnPlane(to - from, Vector3.up).magnitude;
      var segments = Mathf.Clamp(Mathf.CeilToInt(_length / 6f), 1, 18);
      _points = new Vector3[segments + 1];
      for (var i = 0; i <= segments; i++) {
        var point = Vector3.Lerp(from, to, (float)i / segments);
        point.y = TerrainKit.SurfaceHeight(map, point.x, point.z) + 0.035f;
        _points[i] = point;
      }

      var bounds = VisualCatalog.LocalBounds(conveyorSlot.Prefab);
      var alongX = bounds.size.x > bounds.size.z;
      var modelLength = Mathf.Max(0.1f, alongX ? bounds.size.x : bounds.size.z);
      var widthScale = ConveyorWidth / Mathf.Max(0.1f, alongX ? bounds.size.z : bounds.size.x);
      var heightScale = ConveyorHeight / Mathf.Max(0.1f, bounds.size.y);
      var align = Quaternion.Euler(0f, alongX ? 90f : 0f, 0f);
      for (var i = 0; i < segments; i++) {
        var a = _points[i];
        var b = _points[i + 1];
        var frame = Object.Instantiate(conveyorSlot.Prefab, Root.transform).transform;
        frame.name = "ConveyorFrame";
        var stretch = (b - a).magnitude / modelLength;
        var scale = alongX ? new Vector3(stretch, heightScale, widthScale) : new Vector3(widthScale, heightScale, stretch);
        var rotation = Quaternion.LookRotation(b - a, Vector3.up) * align;
        var pivot = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        frame.localScale = scale;
        frame.SetPositionAndRotation((a + b) * 0.5f - rotation * Vector3.Scale(pivot, scale), rotation);
        DisableColliders(frame);
      }
      _belt = ConveyorBeltSurface.Create(Root.transform, _points, ConveyorWidth * 0.74f, BeltHeight, _length);
      if (cargoPrefab == null) return;
      var cargoBounds = VisualCatalog.LocalBounds(cargoPrefab);
      _cargoScale = 0.65f / Mathf.Max(0.10f, cargoBounds.size.x, cargoBounds.size.y, cargoBounds.size.z);
      for (var i = 0; i < CargoCount; i++) {
        var anchor = new GameObject("ExtractedCargo").transform;
        anchor.SetParent(Root.transform, false);
        var prop = Object.Instantiate(cargoPrefab, anchor).transform;
        prop.localPosition = -new Vector3(cargoBounds.center.x, cargoBounds.min.y, cargoBounds.center.z);
        DisableColliders(prop);
        _cargo[i] = anchor;
        _distance[i] = _length;
        anchor.gameObject.SetActive(false);
      }
    }

    static void DisableColliders(Transform root) {
      foreach (var collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
    }

    public void Update(bool producing, float dt) {
      if (_cargo[0] == null) {
        if (producing) _belt?.Advance(CargoSpeed * dt);
        return;
      }
      _nextLaunch = Mathf.Max(0f, _nextLaunch - dt);
      if (producing && _nextLaunch == 0f) {
        _distance[_nextCargo] = 0f;
        _nextCargo = (_nextCargo + 1) % CargoCount;
        _nextLaunch = Mathf.Max(0.65f, _length / (CargoSpeed * CargoCount));
      }
      var movingBelt = producing;
      for (var i = 0; i < CargoCount; i++) {
        movingBelt |= _distance[i] < _length;
        _distance[i] = Mathf.Min(_length, _distance[i] + CargoSpeed * dt);
        var active = _distance[i] < _length;
        if (_cargo[i].gameObject.activeSelf != active) _cargo[i].gameObject.SetActive(active);
        if (active == false) continue;
        var position = _distance[i] / _length * (_points.Length - 1);
        var segment = Mathf.Min(_points.Length - 2, Mathf.FloorToInt(position));
        var a = _points[segment];
        var b = _points[segment + 1];
        var fade = Mathf.Clamp01(Mathf.Min(_distance[i], _length - _distance[i]) / 0.55f);
        _cargo[i].localScale = Vector3.one * (_cargoScale * fade);
        _cargo[i].SetPositionAndRotation(Vector3.Lerp(a, b, position - segment) + Vector3.up * (BeltHeight + 0.018f),
          Quaternion.LookRotation(b - a, Vector3.up));
      }
      if (movingBelt) _belt?.Advance(CargoSpeed * dt);
    }
  }
}
