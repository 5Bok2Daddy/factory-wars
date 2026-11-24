namespace FactoryWars.View {
  using Quantum;
  using UnityEngine;

  /// <summary>채굴 설비와 옆 적재장을 잇는 화면 전용 컨베이어와 재사용 화물.</summary>
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
    readonly Renderer _statusBeacon;
    readonly Material _statusMat;
    bool _lastProducing;
    float _nextLaunch;
    int _nextCargo;

    public readonly GameObject Root;
    public EntityRef Headquarters { get; }
    public Transform FirstCargo => _cargo[0];
    public float BeltPhase => _belt != null ? _belt.Phase : 0f;
    public Bounds WalkObstacle { get; }

    public static ExtractorDeliveryView Create(Transform parent, MapSpec map, EntityRef headquarters, Vector3 from,
        float sourceRadius, Vector3 to, float destinationRadius, GameObject cargoPrefab,
        System.Predicate<GroundFootprint> clear, ResourceKind kind = ResourceKind.Iron) {
      var direction = Vector3.ProjectOnPlane(to - from, Vector3.up);
      var length = direction.magnitude;
      if (length <= sourceRadius + destinationRadius + 0.5f ||
          VisualCatalog.TryFilled(VisualCatalog.FxId("extractor_conveyor"), out var slot) == false) return null;
      direction /= length;
      foreach (var turn in new[] { 0f, 45f, -45f, 90f, -90f, 180f }) {
        var side = Quaternion.Euler(0f, turn, 0f) * direction;
        var edgeFactor = Mathf.Max(Mathf.Abs(side.x), Mathf.Abs(side.z));
        var start = from + side * (sourceRadius * 0.73f / edgeFactor);
        var end = from + side * (sourceRadius / edgeFactor + 2.8f);
        var angle = Quaternion.LookRotation(side);
        var area = new GroundFootprint((start + end + side) * 0.5f, new Vector3(1.10f, 0f, (end - start).magnitude * 0.5f + 1.10f), angle);
        if (!clear(area)) continue;
        return new ExtractorDeliveryView(parent, map, headquarters, start, end, slot, cargoPrefab, kind);
      }
      return null;
    }

    ExtractorDeliveryView(Transform parent, MapSpec map, EntityRef headquarters, Vector3 from, Vector3 to,
        VisualSlot conveyorSlot, GameObject cargoPrefab, ResourceKind kind) {
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
      var flatStart = new Vector3(from.x, 0f, from.z);
      var flatEnd = new Vector3(to.x, 0f, to.z);
      var clearance = new Bounds(flatStart, Vector3.one * 1.8f);
      clearance.Encapsulate(new Bounds(flatEnd, Vector3.one * 2.4f));
      WalkObstacle = clearance;
      var apron = _points[_points.Length - 1] + (flatEnd - flatStart).normalized * 0.65f;
      var heading = Quaternion.LookRotation(flatEnd - flatStart);
      MapPolishArt.Place("Foundation", Root.transform, Root.transform.InverseTransformPoint(apron) - Vector3.up * 0.08f,
        Quaternion.Inverse(Root.transform.rotation) * heading, new Vector3(2.55f, 0.18f, 2.6f));
      MapPolishArt.Place("Crates", Root.transform, Root.transform.InverseTransformPoint(apron) + Vector3.up * 0.1f,
        Quaternion.Inverse(Root.transform.rotation) * heading, new Vector3(1.48f, 0.76f, 0.85f));

      // Industrial intake & discharge stage props (Iron: intake hopper + steel storage silo; Timber: intake roller + stacked log rack).
      var stageKit = new MeshKit();
      var startLocal = Root.transform.InverseTransformPoint(_points[0]);
      var endLocal = Root.transform.InverseTransformPoint(_points[_points.Length - 1]);
      var localRot = Quaternion.Inverse(Root.transform.rotation) * heading;
      if (kind == ResourceKind.Iron) {
        // Intake hopper at belt start
        stageKit.Paint(Swatch.MetalLight).Box(new Vector3(0f, 0.78f, 0f), new Vector3(1.55f, 0.55f, 1.35f));
        stageKit.Paint(Swatch.MetalDark).Box(new Vector3(0f, 0.42f, 0f), new Vector3(1.10f, 0.35f, 0.95f));
        stageKit.Paint(Swatch.Hazard).Box(new Vector3(0f, 1.08f, 0f), new Vector3(1.62f, 0.09f, 1.42f));
        PlaceKit(Root.transform, "IntakeHopper", stageKit.ToMesh("IntakeHopper"), startLocal, localRot);

        // Storage silo at belt discharge
        var siloKit = new MeshKit();
        siloKit.Paint(Swatch.MetalLight).Cylinder(new Vector3(1.35f, 1.35f, 0.65f), 0.92f, 2.5f, 14);
        siloKit.Paint(Swatch.Hazard).Cylinder(new Vector3(1.35f, 2.25f, 0.65f), 0.96f, 0.24f, 14);
        siloKit.Paint(Swatch.MetalDark).Cylinder(new Vector3(1.35f, 2.68f, 0.65f), 0.65f, 0.28f, 14);
        PlaceKit(Root.transform, "IronStorageSilo", siloKit.ToMesh("IronStorageSilo"), endLocal, localRot);
      } else {
        // Timber intake roller guide at belt start
        stageKit.Paint(Swatch.ArmorDark).Box(new Vector3(0f, 0.62f, 0f), new Vector3(1.55f, 0.36f, 0.85f));
        stageKit.Paint(Swatch.Wood).Cylinder(new Vector3(0f, 0.88f, 0f), 0.24f, 1.35f, 10);
        PlaceKit(Root.transform, "TimberIntakeGuide", stageKit.ToMesh("TimberIntakeGuide"), startLocal, localRot);

        // Timber log stack rack at belt discharge
        var rackKit = new MeshKit();
        rackKit.Paint(Swatch.ArmorDark).Box(new Vector3(1.30f, 0.16f, 0.65f), new Vector3(1.35f, 0.24f, 1.75f));
        rackKit.Paint(Swatch.Wood);
        for (var row = 0; row < 2; row++) {
          for (var col = -1; col <= 1; col++) {
            rackKit.Box(new Vector3(1.30f + col * 0.34f, 0.44f + row * 0.28f, 0.65f), new Vector3(0.28f, 0.24f, 1.55f));
          }
        }
        PlaceKit(Root.transform, "TimberLogRack", rackKit.ToMesh("TimberLogRack"), endLocal, localRot);
      }
      // Active / stopped state indicator beacon on the discharge stage.
      var beaconGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
      beaconGo.name = "ExtractorStateBeacon";
      Object.Destroy(beaconGo.GetComponent<Collider>());
      beaconGo.transform.SetParent(Root.transform, false);
      beaconGo.transform.localPosition = endLocal + localRot * new Vector3(1.35f, kind == ResourceKind.Iron ? 2.95f : 1.25f, 0.65f);
      beaconGo.transform.localScale = new Vector3(0.38f, 0.14f, 0.38f);
      _statusBeacon = beaconGo.GetComponent<Renderer>();
      _statusMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) {
        hideFlags = HideFlags.HideAndDontSave,
        color = new Color(0.22f, 0.90f, 0.38f),
      };
      _statusBeacon.sharedMaterial = _statusMat;
      _lastProducing = true;

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

    static void PlaceKit(Transform parent, string name, Mesh mesh, Vector3 localPos, Quaternion localRot) {
      var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
      go.transform.SetParent(parent, false);
      go.transform.localPosition = localPos;
      go.transform.localRotation = localRot;
      go.GetComponent<MeshFilter>().sharedMesh = mesh;
      go.GetComponent<MeshRenderer>().sharedMaterial = ArtLook.NeutralMaterial;
    }

    static void DisableColliders(Transform root) {
      foreach (var collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
    }

    public void Update(bool producing, float dt) {
      if (_statusMat != null && producing != _lastProducing) {
        _lastProducing = producing;
        _statusMat.color = producing ? new Color(0.22f, 0.90f, 0.38f) : new Color(0.88f, 0.28f, 0.20f);
      }
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
