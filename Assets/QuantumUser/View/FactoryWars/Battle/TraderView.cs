namespace FactoryWars.View {
  using System.Collections.Generic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;

  /// <summary>
  /// 중앙 암시장 주둔지(시뮬레이션 NpcTrader를 읽기만 함). 20m 정사각 철조망(0·90·180·270도, 틈 없이 2.5m 칸 8개씩),
  /// 길 방향 두 출입구(차단기 + 방벽), 가운데 단층 막사(거래 건물, 지붕 위성 안테나·작업등), 모서리 감시탑 2개와 탑 위 보초,
  /// 출입구 보초 2명, 울타리 안쪽을 도는 순찰 2명, 둘레를 도는 정찰 드론 3대. 선택하면 점선 고리.
  /// 적대 상태면 보초가 표적을 향해 사격 자세, 파괴되면 폐허와 연기.
  /// </summary>
  public class TraderView {
    public const float Half = 10f;
    const float FenceStep = 2.5f;
    const int FencePerSide = 8;
    const float PatrolInset = 9f;
    const float PatrolSpeed = 1.3f;
    const int DroneCount = 3;

    readonly Transform _root;
    GameObject _compound;
    GameObject _ring;
    GameObject _alive;
    GameObject _ruin;
    bool _destroyed;
    bool _gatesNorthSouth = true;

    class Guard {
      public Transform View;
      public UnitAnimatorBridge Anim;
      public List<RiflemanRig> Rigs;
      public Vector3 Home;
      public float Yaw;
      public bool Patrol;
      public float Distance;
      public bool Tower;
    }
    readonly List<Guard> _guards = new List<Guard>();

    class Drone {
      public Transform Body;
      public readonly List<Transform> Rotors = new List<Transform>();
      public float Angle;
      public float Radius;
      public float Height;
      public float Speed;
      public float Phase;
    }
    readonly List<Drone> _drones = new List<Drone>();

    public EntityRef Entity { get; private set; }
    public bool RingVisible => _ring != null && _ring.activeSelf;
    public bool Built => _compound != null;
    public int FenceCount { get; private set; }
    public int GateCount { get; private set; }
    public int TowerSentries { get; private set; }
    public int Patrols { get; private set; }
    public int DroneTotal => _drones.Count;
    public bool HasHq { get; private set; }
    public Vector3 Center => _compound != null ? _compound.transform.position : Vector3.zero;
    public bool GatesNorthSouth => _gatesNorthSouth;
    /// <summary>가장 높은 드론 높이와 가장 낮은 드론 높이(테스트·캡처 확인용).</summary>
    public Vector2 DroneHeights {
      get {
        var min = float.MaxValue;
        var max = float.MinValue;
        foreach (var d in _drones) {
          min = Mathf.Min(min, d.Body.position.y);
          max = Mathf.Max(max, d.Body.position.y);
        }
        return _drones.Count > 0 ? new Vector2(min, max) : Vector2.zero;
      }
    }
    /// <summary>울타리 칸의 회전(도). 전부 90의 배수여야 한다.</summary>
    public readonly List<float> FenceYaws = new List<float>();

    public TraderView(Transform root) {
      _root = root;
    }

    public void Update(Frame frame, ViewCatalog catalog, BattleFx fx, bool selected, float dt) {
      var found = false;
      var hostile = false;
      var target = Vector3.zero;
      foreach (var pair in frame.GetComponentIterator<NpcTrader>()) {
        found = true;
        if (_compound == null) {
          Build(frame, pair.Entity, catalog);
        }
        if (pair.Component.Destroyed && _destroyed == false) {
          _destroyed = true;
          _alive.SetActive(false);
          _ruin.SetActive(true);
          fx?.Explosion(_compound.transform.position + Vector3.up * 2f, 2.2f);
        }
        hostile = pair.Component.HostileMask != 0 && pair.Component.Destroyed == false;
        if (hostile && frame.Exists(pair.Component.Target)) {
          target = frame.Get<Transform3D>(pair.Component.Target).Position.ToUnityVector3();
        } else {
          hostile = false;
        }
      }
      if (found == false || _ring == null) {
        return;
      }
      if (_ring.activeSelf != selected) {
        _ring.SetActive(selected);
      }
      if (selected) {
        _ring.transform.Rotate(0f, 25f * dt, 0f, Space.Self);
      }
      if (_destroyed) {
        return;
      }
      UpdateGuards(dt, hostile, target);
      UpdateDrones(dt);
    }

    /// <summary>경비 사격이 나가는 자리: 표적에 가장 가까운 보초(탑 위 포함)의 소총 높이.</summary>
    public Vector3 ShotOrigin(Vector3 from, Vector3 to) {
      var best = from + Vector3.up * 3f;
      var bestDistance = float.MaxValue;
      RiflemanRig shooter = null;
      foreach (var guard in _guards) {
        if (guard.View == null) {
          continue;
        }
        var at = guard.Rigs.Count > 0 ? guard.Rigs[0].Muzzle : guard.View.position + Vector3.up * 1.4f;
        var distance = (at - to).sqrMagnitude;
        if (distance < bestDistance) {
          bestDistance = distance;
          best = at;
          shooter = guard.Rigs.Count > 0 ? guard.Rigs[0] : null;
        }
      }
      shooter?.Fire();
      return best;
    }

    // ---------- 짓기 ----------

    void Build(Frame frame, EntityRef entity, ViewCatalog catalog) {
      Entity = entity;
      var map = frame.FindAsset<MapSpec>(frame.Map.UserAsset);
      var center = frame.Get<Transform3D>(entity).Position.ToUnityVector3();
      center.y = 0f;
      _compound = new GameObject("TraderCompound");
      _compound.transform.SetParent(_root, false);
      _compound.transform.position = center;
      _alive = new GameObject("Alive");
      _alive.transform.SetParent(_compound.transform, false);
      _ruin = new GameObject("Ruin");
      _ruin.transform.SetParent(_compound.transform, false);
      _ruin.SetActive(false);

      _gatesNorthSouth = GatesAlongNorthSouth(map, center);
      // Task 39 card 4: inside the PMC camp the HQ hub has no own fence (the camp fence is the perimeter; no internal fences).
      if (map.MercenaryCamp == false) {
        Perimeter();
        Interior();
      }
      DressingSpawn.Place(DressKind.Ruin, _ruin.transform, Vector3.zero, 0f, 9904);
      DressingSpawn.Place(DressKind.Ruin, _ruin.transform, new Vector3(5f, 0f, -4f), 90f, 9905);
      if (map.MercenaryCamp == false) {
        Guards(catalog);
      }
      Drones();

      var kit = new MeshKit().Paint(Swatch.White);
      ArtView.DashedRing(kit, Half * 1.45f, Half * 1.45f + 0.7f, 56);
      _ring = new GameObject("TraderRing", typeof(MeshFilter), typeof(MeshRenderer));
      _ring.transform.SetParent(_compound.transform, false);
      _ring.transform.localPosition = Vector3.up * 0.06f;
      _ring.GetComponent<MeshFilter>().sharedMesh = kit.ToMesh("trader_ring");
      _ring.GetComponent<MeshRenderer>().sharedMaterial = ArtLook.NeutralMaterial;
      _ring.SetActive(false);
    }

    /// <summary>가운데로 들어오는 길이 남북 쪽이 많으면 남북, 아니면 동서에 출입구를 낸다.</summary>
    static bool GatesAlongNorthSouth(MapSpec map, Vector3 center) {
      var northSouth = 0f;
      var eastWest = 0f;
      foreach (var road in map.Roads ?? new MapShape[0]) {
        var c = new Vector3(road.Center.X.AsFloat, 0f, road.Center.Z.AsFloat);
        var dir = Quaternion.Euler(0f, road.Yaw.AsFloat, 0f) * Vector3.right;
        var half = road.HalfLength.AsFloat;
        foreach (var end in new[] { c - dir * half, c + dir * half }) {
          var offset = end - center;
          if (offset.magnitude < 40f) {
            var weight = 1f / Mathf.Max(offset.magnitude, 4f);
            northSouth += Mathf.Abs(dir.z) * weight;
            eastWest += Mathf.Abs(dir.x) * weight;
          }
        }
      }
      return northSouth >= eastWest;
    }

    /// <summary>로컬 좌표(남북 출입구 기준)를 출입구 방향에 맞게 돌린다.</summary>
    Vector3 L(float x, float z) => _gatesNorthSouth ? new Vector3(x, 0f, z) : new Vector3(z, 0f, -x);
    float Y(float yaw) => _gatesNorthSouth ? yaw : yaw + 90f;

    GameObject Put(DressKind kind, float x, float z, float yaw, int seed, Transform parent = null) {
      return DressingSpawn.Place(kind, parent ?? _alive.transform, L(x, z), Snap(Y(yaw)), seed);
    }

    static float Snap(float yaw) => Mathf.Repeat(Mathf.Round(yaw / 90f) * 90f, 360f);

    void Perimeter() {
      // 네 변 × 8칸(2.5m). 남·북 변의 가운데 두 칸은 출입구(5m).
      for (var side = 0; side < 4; side++) {
        var yaw = side * 90f;
        for (var i = 0; i < FencePerSide; i++) {
          var along = -Half + FenceStep * (i + 0.5f);
          var gate = (side == 0 || side == 2) && (i == FencePerSide / 2 - 1 || i == FencePerSide / 2);
          if (gate) {
            continue;
          }
          var local = Quaternion.Euler(0f, yaw, 0f) * new Vector3(along, 0f, Half);
          if (Put(DressKind.TraderFence, local.x, local.z, yaw, 9800 + side * 16 + i) != null) {
            FenceCount++;
            FenceYaws.Add(Snap(Y(yaw)));
          }
        }
      }
      // 출입구: 차단기(팔이 출입구를 가로지른다) + 양옆 방벽 2개씩(바깥쪽).
      foreach (var z in new[] { Half, -Half }) {
        var outward = Mathf.Sign(z);
        if (Put(DressKind.TraderBoom, 0f, z + outward * 0.6f, z > 0f ? 0f : 180f, 9850 + (z > 0f ? 0 : 1)) != null) {
          GateCount++;
        }
        foreach (var x in new[] { -3.3f, 3.3f }) {
          Put(DressKind.TraderSandbag, x, z + outward * 1.1f, 0f, 9860 + (int)(x + z));
          Put(DressKind.TraderSandbag, x * 1.38f, z + outward * 1.1f, 0f, 9870 + (int)(x - z));
        }
      }
    }

    void Interior() {
      // 다진 흙 마당: 가운데로 들어오던 길 아스팔트를 울타리 안에서 덮는다.
      var yard = new MeshKit().Paint(Swatch.RockDark);
      yard.Box(new Vector3(0f, 0.06f, 0f), new Vector3(Half * 2f + 0.4f, 0.12f, Half * 2f + 0.4f));
      var floor = new GameObject("Yard", typeof(MeshFilter), typeof(MeshRenderer));
      floor.transform.SetParent(_alive.transform, false);
      floor.GetComponent<MeshFilter>().sharedMesh = yard.ToMesh("trader_yard");
      floor.GetComponent<MeshRenderer>().sharedMaterial = ArtLook.NeutralMaterial;
      // 가운데 단층 막사(문이 남북 출입구를 본다).
      var hq = Put(DressKind.TraderHQ, 0f, 0f, 0f, 9901);
      HasHq = hq != null;
      var roof = 3.6f;
      if (hq != null) {
        var bounds = Bounds(hq);
        roof = bounds.max.y - _compound.transform.position.y;
        // 지붕 위성 안테나 + 출입문 옆 작업등(따뜻한 빛).
        var dish = Put(DressKind.TraderDish, 1.2f, -1.5f, 180f, 9910);
        if (dish != null) {
          dish.transform.localPosition += Vector3.up * roof;
        }
        var mast = Put(DressKind.TraderDish, -1.2f, 2f, 0f, 9911);
        if (mast != null) {
          mast.transform.localPosition += Vector3.up * roof;
        }
      }
      foreach (var (x, z, yaw) in new[] { (-3.6f, 5.6f, 0f), (3.6f, -5.6f, 180f) }) {
        Put(DressKind.TraderLight, x, z, yaw, 9920 + (int)x);
        WorkLight(L(x, z) + Vector3.up * 2.1f);
      }
      // 북서: 드론 착륙장 + 관제 단말.
      Put(DressKind.TraderPad, -6.2f, 6.2f, 0f, 9930);
      Put(DressKind.TraderConsole, -3.9f, 3.1f, 90f, 9931);
      // 남동: 위장망 천막 아래 탄약 상자 줄.
      Put(DressKind.TraderNet, 6.3f, -6.3f, 0f, 9940);
      for (var i = 0; i < 3; i++) {
        Put(DressKind.TraderAmmo, 5.4f + i * 1.5f, -5.3f, 0f, 9941 + i);
        Put(DressKind.TraderAmmo, 5.4f + i * 1.5f, -6.6f, 0f, 9944 + i);
      }
      // 동쪽: 총기 거치대가 막사 벽을 따라 한 줄.
      for (var i = 0; i < 3; i++) {
        Put(DressKind.TraderRack, 4.1f, -1.5f + i * 1.5f, 90f, 9950 + i);
      }
      Put(DressKind.TraderAmmo, 6.6f, 1.6f, 90f, 9955);
      Put(DressKind.TraderAmmo, 6.6f, 3.1f, 90f, 9956);
      // 서쪽: 연료 드럼통 더미.
      Put(DressKind.TraderBarrel, -6.4f, -0.6f, 0f, 9960);
      Put(DressKind.TraderBarrel, -6.4f, -2.3f, 0f, 9961);
      Put(DressKind.TraderBarrel, -4.7f, -1.4f, 90f, 9962);
      // 모서리 감시탑(북동·남서): 지붕 없는 난간 갑판이라 위에서도 보초가 보인다. 사다리는 마당 쪽.
      foreach (var (x, z, yaw) in Towers) {
        Put(DressKind.TraderTower, x, z, yaw, 9970 + (int)x);
      }
    }

    static readonly (float X, float Z, float Yaw)[] Towers = { (8f, 7f, 0f), (-8f, -7f, 180f) };

    void WorkLight(Vector3 local) {
      if (GraphicsQuality.Current == QualityTier.Low) {
        return;
      }
      var item = new GameObject("WorkLight");
      item.transform.SetParent(_alive.transform, false);
      item.transform.localPosition = local;
      var light = item.AddComponent<Light>();
      light.type = LightType.Point;
      light.color = new Color(1f, 0.76f, 0.46f);
      light.range = 8f;
      light.intensity = 2.2f;
      light.shadows = LightShadows.None;
      // 전구 자리의 따뜻한 발광 점.
      var kit = new MeshKit().Glow(Swatch.GlowOrange);
      kit.Box(Vector3.zero, Vector3.one * 0.22f);
      var bulb = new GameObject("Bulb", typeof(MeshFilter), typeof(MeshRenderer));
      bulb.transform.SetParent(item.transform, false);
      bulb.GetComponent<MeshFilter>().sharedMesh = kit.ToMesh("trader_bulb");
      bulb.GetComponent<MeshRenderer>().sharedMaterial = ArtLook.NeutralMaterial;
    }

    static Bounds Bounds(GameObject item) {
      var renderers = item.GetComponentsInChildren<Renderer>();
      if (renderers.Length == 0) {
        return new Bounds(item.transform.position, Vector3.zero);
      }
      var bounds = renderers[0].bounds;
      foreach (var r in renderers) {
        bounds.Encapsulate(r.bounds);
      }
      return bounds;
    }

    // ---------- 경비 ----------

    void Guards(ViewCatalog catalog) {
      // 탑 위 보초: 갑판(꼭대기 오두막 아래, 높이의 66%) 앞쪽 끝, 사다리 위에 서서 바깥을 본다.
      foreach (var (x, z, yaw) in Towers) {
        var height = 3.1f;
        foreach (Transform child in _alive.transform) {
          if ((child.localPosition - L(x, z)).sqrMagnitude < 0.5f) {
            height = (Bounds(child.gameObject).max.y - _compound.transform.position.y) * 0.66f;
          }
        }
        var front = Quaternion.Euler(0f, Snap(Y(yaw)), 0f) * new Vector3(0.7f, 0f, -1.5f);
        if (AddGuard(catalog, L(x, z) + front + Vector3.up * height, Y(yaw + 180f), false, true)) {
          TowerSentries++;
        }
      }
      // 출입구 보초(차단기 옆, 바깥을 본다).
      AddGuard(catalog, L(2.2f, Half - 1.2f), Y(0f), false, false);
      AddGuard(catalog, L(-2.2f, -Half + 1.2f), Y(180f), false, false);
      // 순찰 2명: 안쪽 둘레를 반대편에서 같은 방향으로 돈다.
      for (var i = 0; i < 2; i++) {
        if (AddGuard(catalog, Vector3.zero, 0f, true, false)) {
          _guards[_guards.Count - 1].Distance = i * PatrolInset * 4f;
          Patrols++;
        }
      }
    }

    bool AddGuard(ViewCatalog catalog, Vector3 local, float yaw, bool patrol, bool tower) {
      Transform view = null;
      UnitAnimatorBridge anim = null;
      List<RiflemanRig> rigs = null;
      if (VisualCatalog.TryFilled(GuardSlot, out var slot)) {
        // 팩 군인 한 명(경비 전용 칸). 진영 색을 입히지 않는다.
        view = new GameObject("TraderGuard").transform;
        view.SetParent(_alive.transform, false);
        var model = Object.Instantiate(slot.Prefab, view);
        var (offset, _) = VisualCatalog.Measure(slot.Prefab, slot);
        var scale = slot.Scale > 0f ? slot.Scale : 1f;
        model.transform.localScale = Vector3.one * scale;
        model.transform.localPosition = offset * scale;
        anim = UnitAnimatorBridge.Attach(model, slot);
        rigs = RiflemanRig.AttachAll(model, BattleView.Weapon(DressKind.Rifle));
      } else {
        var art = ArtView.Create(catalog, "cf_engineer", _alive.transform, Palette.Count - 1);
        if (art == null) {
          return false;
        }
        view = art.transform;
        anim = art.Animation;
      }
      view.localPosition = local;
      view.localRotation = Quaternion.Euler(0f, yaw, 0f);
      _guards.Add(new Guard { View = view, Anim = anim, Rigs = rigs ?? new List<RiflemanRig>(), Home = local, Yaw = yaw, Patrol = patrol, Tower = tower });
      return true;
    }

    const string GuardSlot = "unit/trader_guard";
    const string DroneSlot = "unit/shared_scout_drone";
    public bool PackGuards => VisualCatalog.TryFilled(GuardSlot, out _);

    /// <summary>순찰 경로: 안쪽 정사각형 둘레의 거리 → 위치·진행 방향.</summary>
    static (Vector3 Position, float Yaw) PatrolAt(float distance) {
      var side = PatrolInset * 2f;
      var d = Mathf.Repeat(distance, side * 4f);
      var leg = Mathf.FloorToInt(d / side);
      var t = d - leg * side;
      switch (leg) {
        case 0: return (new Vector3(-PatrolInset + t, 0f, PatrolInset), 90f);
        case 1: return (new Vector3(PatrolInset, 0f, PatrolInset - t), 180f);
        case 2: return (new Vector3(PatrolInset - t, 0f, -PatrolInset), 270f);
        default: return (new Vector3(-PatrolInset, 0f, -PatrolInset + t), 0f);
      }
    }

    void UpdateGuards(float dt, bool hostile, Vector3 target) {
      foreach (var guard in _guards) {
        if (guard.View == null) {
          continue;
        }
        var anim = guard.Anim;
        foreach (var rig in guard.Rigs) {
          rig.Aiming = hostile;
          rig.AimPoint = target + Vector3.up;
        }
        if (hostile) {
          // 적대: 모두 표적을 겨눈다(순찰은 멈춘다).
          var delta = target - guard.View.position;
          guard.View.localRotation = Quaternion.Euler(0f, Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg, 0f);
          anim?.Set(UnitAnimState.Attack, 0f);
          continue;
        }
        if (guard.Patrol) {
          guard.Distance += PatrolSpeed * dt;
          var (position, yaw) = PatrolAt(guard.Distance);
          guard.View.localPosition = L(position.x, position.z);
          guard.View.localRotation = Quaternion.Euler(0f, Mathf.MoveTowardsAngle(guard.Yaw, Y(yaw), 360f * dt), 0f);
          guard.Yaw = Mathf.MoveTowardsAngle(guard.Yaw, Y(yaw), 360f * dt);
          anim?.Set(UnitAnimState.Move, PatrolSpeed);
        } else {
          guard.View.localRotation = Quaternion.Euler(0f, guard.Yaw, 0f);
          anim?.Set(guard.Tower ? UnitAnimState.Attack : UnitAnimState.Idle, 0f);
        }
      }
    }

    // ---------- 드론 ----------

    static Material _beam;

    static Material BeamMaterial() {
      if (_beam == null) {
        var shader = Shader.Find("FactoryWars/CaptureRing");
        _beam = new Material(shader != null ? shader : Shader.Find("Sprites/Default")) { name = "DroneBeam" };
        _beam.SetColor("_Color", new Color(0.45f, 1f, 0.85f, 0.16f));
        _beam.SetFloat("_Glow", 1.2f);
        _beam.SetFloat("_Pulse", 0.2f);
      }
      return _beam;
    }

    static Mesh _droneBody;
    static Mesh _rotor;
    static Mesh _cone;

    static void DroneMeshes() {
      if (_droneBody != null) {
        return;
      }
      var kit = new MeshKit().Paint(Swatch.Black);
      kit.Chamfer(Vector3.zero, new Vector3(0.55f, 0.16f, 0.7f), 0.06f);
      kit.Paint(Swatch.Black);
      kit.Box(new Vector3(0f, 0.11f, -0.05f), new Vector3(0.3f, 0.08f, 0.35f));
      foreach (var (x, z) in new[] { (1f, 1f), (-1f, 1f), (1f, -1f), (-1f, -1f) }) {
        kit.Paint(Swatch.MetalDark);
        kit.Beam(Vector3.zero, new Vector3(x * 0.55f, 0.02f, z * 0.55f), 0.07f);
        kit.Paint(Swatch.Black);
        kit.Cylinder(new Vector3(x * 0.55f, -0.04f, z * 0.55f), 0.07f, 0.12f, 8);
      }
      // 센서 불빛(청록) + 카메라 짐벌.
      kit.Glow(Swatch.GlowCyan);
      kit.Box(new Vector3(0f, -0.06f, 0.36f), new Vector3(0.12f, 0.06f, 0.04f));
      kit.Paint(Swatch.Black);
      kit.Box(new Vector3(0f, -0.12f, 0.2f), new Vector3(0.14f, 0.1f, 0.14f));
      _droneBody = kit.ToMesh("trader_drone");
      var rotor = new MeshKit().Paint(Swatch.MetalLight);
      rotor.Box(Vector3.zero, new Vector3(0.62f, 0.012f, 0.06f));
      rotor.Box(Vector3.zero, new Vector3(0.06f, 0.012f, 0.62f));
      _rotor = rotor.ToMesh("trader_drone_rotor");
      var cone = new MeshKit().Paint(Swatch.White);
      cone.Frustum(new Vector3(0f, -4.5f, 0f), 1.4f, 0.08f, 4.5f, 12);
      _cone = cone.ToMesh("trader_drone_beam");
    }

    static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material material) {
      var item = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
      item.transform.SetParent(parent, false);
      item.GetComponent<MeshFilter>().sharedMesh = mesh;
      var renderer = item.GetComponent<MeshRenderer>();
      renderer.sharedMaterial = material;
      renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
      return item;
    }

    void Drones() {
      DroneMeshes();
      for (var i = 0; i < DroneCount; i++) {
        var root = new GameObject("PatrolDrone").transform;
        root.SetParent(_alive.transform, false);
        // 120도 간격, 반경 10·13·16m, 같은 방향·같은 각속도라 간격이 그대로 유지된다(높이만 조금씩 다름).
        var drone = new Drone { Body = root, Angle = i * 120f, Radius = DroneRadii[i % DroneRadii.Length], Height = 5.2f + i * 0.8f, Speed = DroneSpeed, Phase = i * 1.7f };
        if (VisualCatalog.TryFilled(DroneSlot, out var slot)) {
          // 공용 고정익 드론 모델(사용자 CC BY 모델). 회전익이 없으므로 로터를 만들지 않는다.
          var model = Object.Instantiate(slot.Prefab, root);
          var (offset, _) = VisualCatalog.Measure(slot.Prefab, slot);
          var scale = slot.Scale > 0f ? slot.Scale : 1f;
          model.transform.localScale = Vector3.one * scale;
          model.transform.localPosition = offset * scale;
          if (GraphicsQuality.Current != QualityTier.Low) {
            MeshObject("Searchlight", root, _cone, BeamMaterial());
          }
          _drones.Add(drone);
          UpdateDrone(drone, 0f);
          continue;
        }
        var body = MeshObject("Body", root, _droneBody, ArtLook.NeutralMaterial).transform;
        body.localScale = Vector3.one * 1.6f;
        foreach (var (x, z) in new[] { (1f, 1f), (-1f, 1f), (1f, -1f), (-1f, -1f) }) {
          var rotor = MeshObject("Rotor", body, _rotor, ArtLook.NeutralMaterial).transform;
          rotor.localPosition = new Vector3(x * 0.55f, 0.06f, z * 0.55f);
          drone.Rotors.Add(rotor);
        }
        if (GraphicsQuality.Current != QualityTier.Low) {
          MeshObject("Searchlight", root, _cone, BeamMaterial());
        }
        _drones.Add(drone);
        UpdateDrone(drone, 0f);
      }
    }

    static readonly float[] DroneRadii = { 10f, 13f, 16f };
    const float DroneSpeed = 14f;

    /// <summary>드론 사이 가장 작은 궤도 위상 차(도).</summary>
    public float DroneMinSpacing {
      get {
        var min = 360f;
        for (var a = 0; a < _drones.Count; a++) {
          for (var b = a + 1; b < _drones.Count; b++) {
            min = Mathf.Min(min, Mathf.Abs(Mathf.DeltaAngle(_drones[a].Angle, _drones[b].Angle)));
          }
        }
        return min;
      }
    }

    void UpdateDrones(float dt) {
      foreach (var drone in _drones) {
        UpdateDrone(drone, dt);
      }
    }

    void UpdateDrone(Drone drone, float dt) {
      drone.Angle += drone.Speed * dt;
      var rad = drone.Angle * Mathf.Deg2Rad;
      var bob = Mathf.Sin(Time.time * 1.7f + drone.Phase) * 0.35f;
      drone.Body.localPosition = new Vector3(Mathf.Cos(rad) * drone.Radius, drone.Height + bob, Mathf.Sin(rad) * drone.Radius);
      // 원을 도는 방향을 보고, 안쪽으로 기울인다(뱅크).
      var heading = Mathf.Atan2(-Mathf.Sin(rad) * Mathf.Sign(drone.Speed), Mathf.Cos(rad) * Mathf.Sign(drone.Speed)) * Mathf.Rad2Deg;
      var bank = Mathf.Sign(drone.Speed) * 14f;
      drone.Body.localRotation = Quaternion.Euler(6f, heading, bank);
      foreach (var rotor in drone.Rotors) {
        rotor.Rotate(0f, 1440f * dt, 0f, Space.Self);
      }
    }
  }
}
