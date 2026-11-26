namespace FactoryWars.View {
  using System.Collections.Generic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;

  public sealed class MlrsVisual {
    readonly Transform _turret;
    readonly Transform _port;
    readonly Transform _starboard;
    readonly Transform _hatch;
    float _pitch;
    float _door;

    public MlrsVisual(Transform model) {
      _turret = model.Find("Turret");
      if (_turret != null) {
        _port = _turret.Find("PortRack");
        _starboard = _turret.Find("StarboardRack");
      }
      _hatch = model.Find("LoadingHatch");
    }

    public void Tick(in MlrsMagazine state, BuildingSpec spec, float dt) {
      var aiming = state.Status == MlrsStatus.Aiming || state.Status == MlrsStatus.Firing;
      if (aiming && _turret != null) {
        var heading = state.AimDirection.ToUnityVector3();
        if (heading.sqrMagnitude > 0.0001f) {
          var target = Quaternion.LookRotation(heading, Vector3.up);
          _turret.rotation = Quaternion.RotateTowards(_turret.rotation, target, 260f * dt);
        }
      }
      _pitch = Mathf.MoveTowards(_pitch, aiming ? spec.RocketElevation.AsFloat : 0f, 85f * dt);
      var tilt = Quaternion.Euler(-_pitch, 0f, 0f);
      if (_port != null) _port.localRotation = tilt;
      if (_starboard != null) _starboard.localRotation = tilt;
      _door = Mathf.MoveTowards(_door, state.Status == MlrsStatus.Reloading ? 64f : 0f, 85f * dt);
      if (_hatch != null) _hatch.localRotation = Quaternion.Euler(-_door, 0f, 0f);
    }
  }

  public sealed class MlrsRocketView {
    const int MaxModels = 48;
    const int MaxTrails = 12;
    sealed class Flight {
      public Transform Model;
      public MeshFilter Filter;
      public bool IsGrenade;
      public float TrailAt;
      public int Stamp;
    }
    readonly Dictionary<EntityRef, Flight> _active = new Dictionary<EntityRef, Flight>(MaxModels);
    readonly Stack<Flight> _pool = new Stack<Flight>(MaxModels);
    readonly List<EntityRef> _stale = new List<EntityRef>(MaxModels);
    readonly Transform _parent;
    readonly MapSpec _map;
    readonly BattleFx _fx;
    static Mesh _mesh;
    static Mesh _grenadeMesh;
    int _stamp;
    int _frame;
    float _newFrameAt;

    public static bool IsGrenadeRound(in MlrsRocket rocket) =>
      rocket.BlastRadius <= (Photon.Deterministic.FP)3 + Photon.Deterministic.FP._0_33
      && rocket.Arc <= (Photon.Deterministic.FP)2 + Photon.Deterministic.FP._0_50;

    public MlrsRocketView(Transform parent, MapSpec map, BattleFx fx) {
      _parent = parent;
      _map = map;
      _fx = fx;
    }

    public Vector3 AtSurface(Vector3 point) {
      point.y += TerrainKit.SurfaceHeight(_map, point.x, point.z);
      return point;
    }

    public void Tick(Frame frame, PlayerRef viewer, Camera camera) {
      ++_stamp;
      var now = Time.time;
      if (_frame != frame.Number) {
        _frame = frame.Number;
        _newFrameAt = now;
      }
      var progress = Mathf.Clamp01((now - _newFrameAt) * frame.SessionConfig.UpdateFPS);
      var count = 0;
      foreach (var pair in frame.GetComponentIterator<MlrsRocket>()) {
        var rocket = pair.Component;
        var point = MlrsSystem.PositionAt(rocket, frame.Number);
        if (viewer.IsValid && rocket.Owner != viewer && !FogSystem.IsVisible(frame, viewer, point)) continue;
        var previous = MlrsSystem.PositionAt(rocket, frame.Number - 1);
        var before = previous.ToUnityVector3();
        var after = point.ToUnityVector3();
        var from = rocket.Origin.ToUnityVector3();
        var to = rocket.Target.ToUnityVector3();
        var duration = rocket.ImpactFrame - rocket.LaunchFrame;
        var position = Vector3.Lerp(before, after, progress);
        var along = (frame.Number - 1f - rocket.LaunchFrame + progress) / duration;
        position.y += Mathf.Lerp(TerrainKit.SurfaceHeight(_map, from.x, from.z), TerrainKit.SurfaceHeight(_map, to.x, to.z), along);
        if (camera != null) {
          var viewport = camera.WorldToViewportPoint(position);
          if (viewport.z < 0f || viewport.x < -0.06f || viewport.x > 1.06f || viewport.y < -0.06f || viewport.y > 1.06f) continue;
        }
        var isGrenade = IsGrenadeRound(rocket);
        if (!_active.TryGetValue(pair.Entity, out var flight)) {
          if (_active.Count >= MaxModels) continue;
          flight = _pool.Count > 0 ? _pool.Pop() : CreateFlight();
          _active.Add(pair.Entity, flight);
          flight.Model.gameObject.SetActive(true);
          flight.TrailAt = now;
        }
        if (flight.IsGrenade != isGrenade) {
          flight.IsGrenade = isGrenade;
          flight.Filter.sharedMesh = isGrenade ? GrenadeMesh() : _mesh;
        }
        flight.Stamp = _stamp;
        if (after == before) after = MlrsSystem.PositionAt(rocket, frame.Number + 1).ToUnityVector3();
        var heading = (after - before).normalized;
        var rotation = heading.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(heading) : flight.Model.rotation;
        flight.Model.SetPositionAndRotation(position, rotation);
        if (count < MaxTrails && now >= flight.TrailAt) {
          if (isGrenade) {
            // 40mm 유탄: 무거운 로켓 화염 대신 가벼운 연막/궤적 점만 남겨 유탄 탄도를 선명하게 표시.
            flight.TrailAt = now + 0.09f / Mathf.Max(0.3f, GraphicsQuality.FxScale);
            _fx.Burst(position - heading * 0.18f, new Color(0.86f, 0.84f, 0.78f, 0.7f), 0.16f);
          } else {
            flight.TrailAt = now + 0.075f / Mathf.Max(0.3f, GraphicsQuality.FxScale);
            _fx.MlrsExhaust(position - heading * 0.44f, heading, true);
          }
        }
        if (++count >= MaxModels) break;
      }
      _stale.Clear();
      foreach (var entry in _active) {
        if (entry.Value.Stamp == _stamp) continue;
        entry.Value.Model.gameObject.SetActive(false);
        _pool.Push(entry.Value);
        _stale.Add(entry.Key);
      }
      foreach (var entity in _stale) _active.Remove(entity);
    }

    Flight CreateFlight() {
      if (_mesh == null) {
        var k = new MeshKit();
        k.Push(new Vector3(0f, 0f, -0.45f), new Vector3(90f, 0f, 0f));
        k.Paint(Swatch.MetalLight);
        k.Cylinder(Vector3.zero, 0.12f, 0.74f, 8);
        k.Paint(Swatch.Hazard);
        k.Cylinder(new Vector3(0f, 0.62f, 0f), 0.127f, 0.1f, 8);
        k.Paint(Swatch.MetalDark);
        k.Lathe(Vector3.zero, new[] { new Vector2(0.12f, 0.74f), new Vector2(0.02f, 1.04f) }, 8);
        k.Pop();
        k.Paint(Swatch.MetalDark);
        k.Box(new Vector3(0f, 0f, -0.33f), new Vector3(0.36f, 0.035f, 0.24f));
        k.Box(new Vector3(0f, 0f, -0.33f), new Vector3(0.035f, 0.36f, 0.24f));
        _mesh = k.ToMesh("MLRS shared rocket");
      }
      var root = new GameObject("MLRS Rocket", typeof(MeshFilter), typeof(MeshRenderer));
      root.transform.SetParent(_parent, false);
      var mf = root.GetComponent<MeshFilter>();
      mf.sharedMesh = _mesh;
      var renderer = root.GetComponent<MeshRenderer>();
      renderer.sharedMaterial = ArtLook.NeutralMaterial;
      renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
      return new Flight { Model = root.transform, Filter = mf, IsGrenade = false };
    }

    /// <summary>40mm 유탄발사기 탄두 메시(날개 없는 콤팩트 황동 탄피 + 올리브 탄두).</summary>
    static Mesh GrenadeMesh() {
      if (_grenadeMesh == null) {
        var k = new MeshKit();
        k.Push(new Vector3(0f, 0f, -0.18f), new Vector3(90f, 0f, 0f));
        k.Paint(Swatch.Hazard);
        k.Cylinder(Vector3.zero, 0.11f, 0.14f, 8);
        k.Paint(Swatch.Olive);
        k.Lathe(Vector3.zero, new[] { new Vector2(0.11f, 0.14f), new Vector2(0.09f, 0.28f), new Vector2(0.03f, 0.36f) }, 8);
        k.Pop();
        _grenadeMesh = k.ToMesh("GrenadeLauncher 40mm");
      }
      return _grenadeMesh;
    }
  }
}
