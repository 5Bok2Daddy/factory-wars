namespace FactoryWars.View {
  using System.Collections.Generic;
  using Photon.Deterministic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;

  /// <summary>
  /// 기지 상주 건설·물류 작업자(순수 Unity View 전용, Quantum 시뮬레이션 무영향 · 비선택 · 콜라이더/NavMesh 차단 0).
  /// - POLYGON - Construction Pack 안전모 작업자 + ANIMATION - Base Locomotion(Idle/Walk/Run)
  /// - 목재 상자/보급 박스 운반(양손 2-Bone IK 파지) 및 핸드트럭 밀기
  /// - 기지 안에서 운반과 적재를 하고, 근무 시간이 지나면 사령부 주변에서 휴식한 뒤 복귀
  /// - 건물 파괴 시 해당 경로 작업자가 바깥으로 대피(Run) 후 부드럽게 사라짐
  /// </summary>
  public unsafe class BaseAmbientWorkers : MonoBehaviour {
    const float RouteScanInterval = 0.35f;
    const float MaxBaseSpan = 56f;
    const float ScatterSeconds = 1.5f;
    [SerializeField, Min(1)] int _mediumGlobalBudget = 12;
    [SerializeField, Min(1)] int _highGlobalBudget = 16;
    static readonly int SpeedParam = Animator.StringToHash("Speed");

    struct BuildingNode {
      public EntityRef Entity;
      public int Owner;
      public Vector3 Position;
      public float Footprint;
      public BuildingRole Role;
      public bool UnderConstruction;
    }

    enum WorkerState { Walking, Working, WalkingToRest, Resting, Waiting, Scattering }

    class WorkerAgent {
      public GameObject Root;
      public Animator Animator;
      public MixamoMotion Motion;
      public Animation LegacyAnimation;
      public WorkerCarryRig CarryRig;
      public int Owner;
      public EntityRef FromEntity;
      public EntityRef ToEntity;
      public Vector3 FromPos;
      public Vector3 ToPos;
      public Vector3 TargetPoint;
      public float WalkSpeed;
      public float PauseTimer;
      public float PauseDuration;
      public float ShiftRemaining;
      public float RestTravelRemaining;
      public int Seed;
      public float ScatterTimer;
      public Vector3 ScatterDir;
      public WorkerState State;
      public bool IsTrolley;
      public float CurrentSpeed;
      public float RepathAt;
      public Vector3 WalkingTarget;
    }

    /// <summary>운반 상자·핸드트럭을 손바닥에 자연스럽게 밀착시키는 경량 2-Bone IK(화면 전용).</summary>
    public class WorkerCarryRig : MonoBehaviour {
      Transform _shoulderL, _elbowL, _handL;
      Transform _shoulderR, _elbowR, _handR;
      Transform _chest;
      Transform _prop;
      bool _isTrolley;
      bool _carrying = true;

      public GameObject PropObject => _prop != null ? _prop.gameObject : null;
      public bool IsCarrying => _carrying && _prop != null;
      public bool IsTrolley => _isTrolley;
      public float LoadingProgress { get; set; }

      static Transform FindBone(Transform root, string boneName) {
        if (root.name == boneName) return root;
        foreach (Transform c in root) {
          var f = FindBone(c, boneName);
          if (f != null) return f;
        }
        return null;
      }

      public static WorkerCarryRig Attach(GameObject workerRoot, GameObject propPrefab, bool isTrolley, bool fitCargo = false) {
        if (workerRoot == null || propPrefab == null) return null;
        var handR = FindBone(workerRoot.transform, "Hand_R");
        var handL = FindBone(workerRoot.transform, "Hand_L");
        var rig = workerRoot.AddComponent<WorkerCarryRig>();
        rig._isTrolley = isTrolley;
        if (handR != null && handR.parent != null && handR.parent.parent != null) {
          rig._handR = handR;
          rig._elbowR = handR.parent;
          rig._shoulderR = handR.parent.parent;
        }
        if (handL != null && handL.parent != null && handL.parent.parent != null) {
          rig._handL = handL;
          rig._elbowL = handL.parent;
          rig._shoulderL = handL.parent.parent;
        }
        rig._chest = FindBone(workerRoot.transform, "Spine_02") ?? FindBone(workerRoot.transform, "Spine_03");
        var prop = Instantiate(propPrefab, workerRoot.transform);
        prop.name = isTrolley ? "CarriedTrolley" : "CarriedCrate";
        foreach (var col in prop.GetComponentsInChildren<Collider>(true)) {
          col.enabled = false;
          Destroy(col);
        }
        var renderers = prop.GetComponentsInChildren<Renderer>(true);
        if (fitCargo && renderers.Length > 0) {
          var bounds = renderers[0].bounds;
          foreach (var renderer in renderers) {
            bounds.Encapsulate(renderer.bounds);
          }
          var largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
          if (largest > 0.01f) {
            prop.transform.localScale *= 0.38f / largest;
          }
        }
        foreach (var r in renderers) {
          r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        rig._prop = prop.transform;
        rig.PositionProp();
        return rig;
      }

      public void SetCarrying(bool carrying) {
        _carrying = carrying;
        if (_prop != null && _prop.gameObject.activeSelf != carrying) {
          _prop.gameObject.SetActive(carrying);
        }
      }

      void PositionProp() {
        if (_prop == null) return;
        if (_isTrolley) {
          // 핸드트럭: 작업자 앞 바닥에 바퀴가 닿도록 세우고 약간 앞으로 기울여 미는 자세.
          _prop.localPosition = new Vector3(0f, 0.02f, 0.68f);
          _prop.localRotation = Quaternion.Euler(14f - 12f * LoadingProgress, 0f, 0f);
        } else {
          // 목재 상자/보급 박스: 가슴 앞 팔 안쪽에 밀착.
          _prop.localPosition = new Vector3(0f, 1.06f - 0.32f * LoadingProgress, 0.44f + 0.11f * LoadingProgress);
          _prop.localRotation = Quaternion.identity;
        }
      }

      void LateUpdate() {
        if (_carrying == false || _prop == null) return;
        PositionProp();
        if (_shoulderL == null || _shoulderR == null || _handL == null || _handR == null) return;
        var scale = Mathf.Max(0.05f, transform.lossyScale.y);
        var forward = transform.forward;
        var right = transform.right;
        var up = Vector3.up;
        if (_isTrolley) {
          var handleCenter = _prop.position + up * ((0.94f + 0.02f * LoadingProgress) * scale)
            - forward * ((0.06f + 0.17f * LoadingProgress) * scale);
          var leftGrip = handleCenter - right * (0.20f * scale);
          var rightGrip = handleCenter + right * (0.20f * scale);
          SolveTwoBone(_shoulderL, _elbowL, _handL, leftGrip, _shoulderL.position - up * (0.45f * scale) - right * (0.28f * scale));
          SolveTwoBone(_shoulderR, _elbowR, _handR, rightGrip, _shoulderR.position - up * (0.45f * scale) + right * (0.28f * scale));
        } else {
          // 상자 좌우 옆면을 양손으로 단단히 받쳐 쥔다.
          var boxCenter = _prop.position;
          var leftGrip = boxCenter - right * (0.23f * scale) - up * (0.04f * scale);
          var rightGrip = boxCenter + right * (0.23f * scale) - up * (0.04f * scale);
          SolveTwoBone(_shoulderL, _elbowL, _handL, leftGrip, _shoulderL.position - up * (0.48f * scale) - right * (0.34f * scale));
          SolveTwoBone(_shoulderR, _elbowR, _handR, rightGrip, _shoulderR.position - up * (0.48f * scale) + right * (0.34f * scale));
        }
      }

      static void SolveTwoBone(Transform upper, Transform lower, Transform hand, Vector3 target, Vector3 pole) {
        var a = upper.position;
        var b = lower.position;
        var c = hand.position;
        var la = (b - a).magnitude;
        var lb = (c - b).magnitude;
        if (la < 1e-4f || lb < 1e-4f) return;
        var toTarget = target - a;
        var distance = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(la - lb) + 1e-3f, (la + lb) * 0.999f);
        var direction = toTarget.sqrMagnitude > 1e-6f ? toTarget.normalized : (c - a).normalized;
        var cosA = Mathf.Clamp((la * la + distance * distance - lb * lb) / (2f * la * distance), -1f, 1f);
        var angle = Mathf.Acos(cosA) * Mathf.Rad2Deg;
        var bend = Vector3.Cross(direction, pole - a);
        if (bend.sqrMagnitude < 1e-6f) bend = Vector3.Cross(direction, Vector3.down);
        var elbowDir = Quaternion.AngleAxis(angle, bend.normalized) * direction;
        var newElbow = a + elbowDir * la;
        upper.rotation = Quaternion.FromToRotation(b - a, newElbow - a) * upper.rotation;
        b = lower.position;
        c = hand.position;
        lower.rotation = Quaternion.FromToRotation(c - b, a + direction * distance - b) * lower.rotation;
      }
    }

    readonly List<WorkerAgent> _workers = new List<WorkerAgent>();
    readonly Dictionary<int, List<BuildingNode>> _byOwner = new Dictionary<int, List<BuildingNode>>();
    readonly Dictionary<EntityRef, BuildingNode> _activeBuildings = new Dictionary<EntityRef, BuildingNode>();
    readonly List<Bounds> _buildingObstacles = new List<Bounds>();
    MapSpec _map;
    ViewCatalog _catalog;
    BattleView _battle;
    float _nextScanAt;
    int _spawnCounter;

    public int ActiveWorkerCount {
      get {
        var n = 0;
        for (var i = 0; i < _workers.Count; i++) {
          if (_workers[i].Root != null && _workers[i].State != WorkerState.Scattering) n++;
        }
        return n;
      }
    }

    public int ScatteringCount {
      get {
        var n = 0;
        for (var i = 0; i < _workers.Count; i++) {
          if (_workers[i].Root != null && _workers[i].State == WorkerState.Scattering) n++;
        }
        return n;
      }
    }

    public int RestingCount {
      get {
        var count = 0;
        foreach (var worker in _workers) {
          if (worker.Root != null && worker.State == WorkerState.Resting) count++;
        }
        return count;
      }
    }

    public void Init(MapSpec map, ViewCatalog catalog = null, BattleView battle = null) {
      _map = map;
      _catalog = catalog;
      _battle = battle;
      _nextScanAt = 0f;
    }

    public bool ClearForYard(GroundFootprint footprint) {
      foreach (var worker in _workers) {
        if (worker.Root == null) continue;
        var point = worker.Root.transform.position;
        if (footprint.Overlaps(new Vector2(point.x, point.z), 0.45f)) return false;
      }
      return true;
    }

    public void AvoidYard(Bounds bounds) => _buildingObstacles.Add(bounds);

    public void ClearAll() {
      for (var i = 0; i < _workers.Count; i++) {
        if (_workers[i].Root != null) Destroy(_workers[i].Root);
      }
      _workers.Clear();
      _byOwner.Clear();
      _activeBuildings.Clear();
    }

    static bool IsLogisticsBuilding(BuildingRole role, bool underConstruction) {
      if (underConstruction) return true;
      switch (role) {
        case BuildingRole.Headquarters:
        case BuildingRole.Processor:
        case BuildingRole.PowerPlant:
        case BuildingRole.SupplyDepot:
        case BuildingRole.Barracks:
        case BuildingRole.VehicleFactory:
        case BuildingRole.HeavyWorks:
        case BuildingRole.Airfield:
        case BuildingRole.ResearchLab:
        case BuildingRole.CollectionYard:
        case BuildingRole.UltimateSite:
          return true;
        default:
          return false;
      }
    }

    static int MaxPerBase(int buildingCount) {
      switch (GraphicsQuality.Current) {
        case QualityTier.Low: return 0;
        case QualityTier.Medium: return Mathf.Clamp(2 + buildingCount / 2, 3, 5);
        default: return Mathf.Clamp(3 + buildingCount / 2, 4, 6);
      }
    }

    int GlobalMaxWorkers() {
      switch (GraphicsQuality.Current) {
        case QualityTier.Low: return 0;
        case QualityTier.Medium: return _mediumGlobalBudget;
        default: return _highGlobalBudget;
      }
    }

    static bool HasSyntyWorkers() => VisualCatalog.TryFilled(VisualCatalog.FxId("const_worker_0"), out var slot) && slot.Prefab != null;

    public void Tick(Frame frame, float dt, PlayerRef viewer = default) {
      if (frame == null || _map == null) return;
      var allow = GraphicsQuality.Current != QualityTier.Low && (HasSyntyWorkers() || Dressing.Has(DressKind.AnimatedWorker) || Dressing.Has(DressKind.Worker));
      if (allow == false) {
        if (_workers.Count > 0) ClearAll();
        return;
      }

      if (Time.time >= _nextScanAt || _workers.Count == 0) {
        _nextScanAt = Time.time + RouteScanInterval;
        ScanBuildingsAndMaintainRoster(frame, viewer);
      }
      UpdateAgents(frame, dt);
    }

    void ScanBuildingsAndMaintainRoster(Frame frame, PlayerRef viewer) {
      _byOwner.Clear();
      _activeBuildings.Clear();
      _buildingObstacles.Clear();
      if (_battle != null) {
        foreach (var actor in _battle.Actors.Values) {
          if (actor.ExtractorDelivery != null) _buildingObstacles.Add(actor.ExtractorDelivery.WalkObstacle);
        }
      }

      var filter = frame.Filter<Transform3D, Building>();
      while (filter.NextUnsafe(out var e, out var t, out var b)) {
        if (b->Health <= FP._0) continue;
        var spec = frame.FindAsset(b->Spec);
        if (spec == null || (spec.RequiresResourceNode && viewer.IsValid && FactoryWarsUtil.IsVisibleTo(frame, viewer, e) == false)) continue;
        var buildingPosition = t->Position.ToUnityVector3();
        var diameter = spec.FootprintRadius.AsFloat * 2f;
        _buildingObstacles.Add(new Bounds(new Vector3(buildingPosition.x, 0f, buildingPosition.z), new Vector3(diameter, 1f, diameter)));
        var underConst = b->ConstructionRemaining > FP._0;
        if (spec.RequiresResourceNode && underConst == false) {
          if (b->Paused != PauseReason.None || b->EmpUntilFrame > frame.Number
              || frame.Unsafe.TryGetPointer<ResourceNode>(b->ExtractNode, out var resource) == false
              || resource->Remaining <= FP._0 || CitySystem.Occupants(frame, e) == 0) {
            continue;
          }
        } else if (IsLogisticsBuilding(spec.Role, underConst) == false) {
          continue;
        }
        var owner = (int)b->Owner;
        var pos = buildingPosition;
        pos.y = TerrainKit.SurfaceHeight(_map, pos.x, pos.z);
        var node = new BuildingNode {
          Entity = e,
          Owner = owner,
          Position = pos,
          Footprint = Mathf.Max(2.2f, spec.FootprintRadius.AsFloat),
          Role = spec.Role,
          UnderConstruction = underConst,
        };
        _activeBuildings[e] = node;
        if (_byOwner.TryGetValue(owner, out var list) == false) {
          list = new List<BuildingNode>();
          _byOwner[owner] = list;
        }
        list.Add(node);
      }

      // 1) 경로의 출발·도착 건물이 파괴되었는지 확인 -> 해당 작업자는 자연스럽게 외곽으로 대피(Scatter) 후 제거.
      for (var i = 0; i < _workers.Count; i++) {
        var w = _workers[i];
        if (w.State == WorkerState.Scattering) continue;
        var fromGone = w.FromEntity != EntityRef.None && _activeBuildings.ContainsKey(w.FromEntity) == false;
        var toGone = w.ToEntity != EntityRef.None && _activeBuildings.ContainsKey(w.ToEntity) == false;
        if (fromGone && frame.TryGet<Building>(w.FromEntity, out var from) && from.Health > FP._0) {
          w.FromEntity = EntityRef.None;
          fromGone = false;
        }
        if (toGone && frame.TryGet<Building>(w.ToEntity, out var to) && to.Health > FP._0
            && w.Root != null && _byOwner.TryGetValue(w.Owner, out var homeSites) && homeSites.Count > 0) {
          var home = FindAnchor(homeSites);
          w.ToEntity = home.Entity;
          w.ToPos = home.Position;
          w.TargetPoint = EntrancePoint(home, w.Root.transform.position);
          w.State = WorkerState.Walking;
          w.RepathAt = 0f;
          if (w.CarryRig != null) w.CarryRig.LoadingProgress = 0f;
          w.CarryRig?.SetCarrying(false);
          toGone = false;
        }
        if (fromGone || toGone) {
          var blastPos = toGone ? w.ToPos : w.FromPos;
          var away = w.Root != null ? (w.Root.transform.position - blastPos) : Vector3.forward;
          away.y = 0f;
          if (away.sqrMagnitude < 0.01f) away = new Vector3(Mathf.Cos(i * 1.9f), 0f, Mathf.Sin(i * 1.9f));
          w.ScatterDir = away.normalized;
          w.ScatterTimer = ScatterSeconds;
          w.State = WorkerState.Scattering;
          w.CarryRig?.SetCarrying(false);
        }
      }

      // 2) 기지별 목표 상주 인원(3~6명, 전역 모바일 캡) 유지.
      var globalCap = GlobalMaxWorkers();
      foreach (var kv in _byOwner) {
        var owner = kv.Key;
        var nodes = kv.Value;
        if (nodes.Count == 0) continue;
        var hq = FindAnchor(nodes);
        nodes.RemoveAll(node => (node.Position - hq.Position).sqrMagnitude > MaxBaseSpan * MaxBaseSpan);
        var desired = MaxPerBase(nodes.Count);
        var existing = 0;
        for (var i = 0; i < _workers.Count; i++) {
          if (_workers[i].Owner == owner && _workers[i].State != WorkerState.Scattering) existing++;
        }
        while (existing < desired && ActiveWorkerCount < globalCap) {
          var agent = SpawnWorker(owner, nodes, existing);
          if (agent == null) break;
          _workers.Add(agent);
          existing++;
        }
      }
    }

    static BuildingNode FindAnchor(List<BuildingNode> nodes) {
      for (var i = 0; i < nodes.Count; i++) {
        if (nodes[i].Role == BuildingRole.Headquarters) return nodes[i];
      }
      return nodes[0];
    }

    static bool IsExtractor(BuildingNode node) =>
      node.Role == BuildingRole.LumberMill || node.Role == BuildingRole.IronExtractor;

    static Vector3 EntrancePoint(BuildingNode from, Vector3 toward) {
      var delta = toward - from.Position;
      delta.y = 0f;
      var dir = delta.sqrMagnitude > 0.01f ? delta.normalized : Vector3.forward;
      var edge = from.Footprint / Mathf.Max(Mathf.Abs(dir.x), Mathf.Abs(dir.z));
      return from.Position + dir * (edge + 0.55f);
    }

    Vector3 SafeEntrance(BuildingNode from, Vector3 toward) {
      var direction = toward - from.Position;
      var first = EntrancePoint(from, toward);
      foreach (var degrees in new[] { 0f, 45f, -45f, 90f, -90f, 180f }) {
        var entrance = EntrancePoint(from, from.Position + Quaternion.Euler(0f, degrees, 0f) * direction);
        var flat = new Vector3(entrance.x, 0f, entrance.z);
        if (!_buildingObstacles.Exists(bounds => bounds.Contains(flat))) return entrance;
      }
      return first;
    }

    bool FindBlockingBuilding(Vector3 from, Vector3 to, out Bounds obstacle) {
      from.y = 0f;
      to.y = 0f;
      var change = to - from;
      var closest = change.magnitude;
      obstacle = default;
      if (closest <= 0.001f) {
        return false;
      }
      var ray = new Ray(from, change / closest);
      var found = false;
      foreach (var bounds in _buildingObstacles) {
        if (bounds.IntersectRay(ray, out var distance) && distance < closest) {
          obstacle = bounds;
          closest = distance;
          found = true;
        }
      }
      return found;
    }

    bool TryWalkingTarget(Vector3 from, Vector3 to, out Vector3 target) {
      target = to;
      if (FindBlockingBuilding(from, to, out var obstacle) == false) {
        return true;
      }
      from.y = 0f;
      to.y = 0f;
      var best = float.PositiveInfinity;
      for (var x = -1; x <= 1; x += 2) {
        for (var z = -1; z <= 1; z += 2) {
          var corner = obstacle.center + new Vector3(x * (obstacle.extents.x + 0.4f), 0f, z * (obstacle.extents.z + 0.4f));
          var score = (corner - from).magnitude + (corner - to).magnitude;
          if (score >= best || (corner - from).sqrMagnitude < 0.09f || FindBlockingBuilding(from, corner, out _)) {
            continue;
          }
          best = score;
          target = corner;
        }
      }
      return best < float.PositiveInfinity;
    }

    (BuildingNode A, BuildingNode B, Vector3 Start, Vector3 End) PickRoute(List<BuildingNode> nodes, int seed, EntityRef excludeTarget) {
      var anchor = FindAnchor(nodes);
      // 건설 중인 건물(<100%)이 있으면 우선적으로 물류/건설 작업자를 투입한다.
      var sites = new List<BuildingNode>();
      for (var i = 0; i < nodes.Count; i++) {
        if (nodes[i].UnderConstruction && nodes[i].Entity != excludeTarget) sites.Add(nodes[i]);
      }
      if (nodes.Count == 1) {
        // 본진에 사령부 1채만 있는 경기 초반: 사령부 앞 야적장 ↔ 출입구 순환 경로.
        var ang = (seed % 6) * 60f * Mathf.Deg2Rad;
        var outward = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
        var aPt = EntrancePoint(anchor, anchor.Position + outward);
        var bPt = aPt + outward * 5.2f + new Vector3(-outward.z, 0f, outward.x) * 3f;
        aPt.y = TerrainKit.SurfaceHeight(_map, aPt.x, aPt.z);
        bPt.y = TerrainKit.SurfaceHeight(_map, bPt.x, bPt.z);
        return (anchor, anchor, aPt, bPt);
      }

      BuildingNode startNode;
      BuildingNode endNode;
      var previous = nodes.Find(node => node.Entity == excludeTarget);
      var extractor = nodes.Find(node => node.Entity != excludeTarget && IsExtractor(node) && node.UnderConstruction == false);
      var barracks = nodes.Find(node => node.Role == BuildingRole.Barracks && node.UnderConstruction == false);
      var depot = barracks.Entity != EntityRef.None && seed % 2 == 0 ? barracks : anchor;
      if (IsExtractor(previous) && previous.UnderConstruction == false && previous.Entity != anchor.Entity) {
        startNode = previous;
        endNode = depot;
      } else if (extractor.Entity != EntityRef.None && seed % 3 == 0) {
        startNode = depot;
        endNode = extractor;
      } else if (sites.Count > 0 && seed % 2 == 0) {
        startNode = anchor.Entity != sites[seed % sites.Count].Entity ? anchor : nodes[(seed + 1) % nodes.Count];
        endNode = sites[seed % sites.Count];
      } else {
        var idxA = seed % nodes.Count;
        var idxB = (seed + 1 + (seed / nodes.Count) % Mathf.Max(1, nodes.Count - 1)) % nodes.Count;
        if (idxA == idxB) idxB = (idxA + 1) % nodes.Count;
        startNode = nodes[idxA];
        endNode = nodes[idxB];
        if (endNode.Entity == excludeTarget && nodes.Count > 2) {
          endNode = nodes[(idxB + 1) % nodes.Count];
        }
      }
      var startPt = SafeEntrance(startNode, endNode.Position);
      var endPt = SafeEntrance(endNode, startNode.Position);
      startPt.y = TerrainKit.SurfaceHeight(_map, startPt.x, startPt.z);
      endPt.y = TerrainKit.SurfaceHeight(_map, endPt.x, endPt.z);
      return (startNode, endNode, startPt, endPt);
    }

    WorkerAgent SpawnWorker(int owner, List<BuildingNode> localNodes, int slotIdx) {
      var seed = ++_spawnCounter + owner * 37 + slotIdx * 13;
      var (nodeA, nodeB, startPt, endPt) = PickRoute(localNodes, seed, EntityRef.None);
      // 초기 스폰 시 경로 상에 고르게 분산 배치해 스폰 직후부터 활기찬 동선을 보여 준다.
      var frac = Mathf.Clamp(0.15f + (slotIdx % 5) * 0.17f, 0.12f, 0.85f);
      var spawnPos = Vector3.Lerp(startPt, endPt, frac);
      if (FindBlockingBuilding(startPt, spawnPos, out _)) {
        spawnPos = startPt;
      }
      spawnPos.y = TerrainKit.SurfaceHeight(_map, spawnPos.x, spawnPos.z);

      var dir = endPt - spawnPos;
      dir.y = 0f;
      var yaw = dir.sqrMagnitude > 0.001f ? Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg : 0f;

      GameObject go = null;
      Animator anim = null;
      Animation legacy = null;
      WorkerCarryRig carryRig = null;
      var isTrolley = (seed % 3 == 2);

      if (HasSyntyWorkers()) {
        var variantIdx = seed % 4;
        if (VisualCatalog.TryFilled(VisualCatalog.FxId($"const_worker_{variantIdx}"), out var wSlot) == false || wSlot.Prefab == null) {
          VisualCatalog.TryFilled(VisualCatalog.FxId("const_worker_0"), out wSlot);
        }
        if (wSlot != null && wSlot.Prefab != null) {
          go = Instantiate(wSlot.Prefab, spawnPos, Quaternion.Euler(0f, yaw, 0f), transform);
          go.name = "BaseWorker";
          var scale = wSlot.Scale > 0f ? wSlot.Scale : 0.85f;
          go.transform.localScale = Vector3.one * scale;
          foreach (var col in go.GetComponentsInChildren<Collider>(true)) Destroy(col);
          anim = go.GetComponentInChildren<Animator>(true);
          if (anim != null) {
            if (anim.runtimeAnimatorController == null && wSlot.Animator != null) {
              anim.runtimeAnimatorController = wSlot.Animator;
            }
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            anim.SetFloat(SpeedParam, 2.0f);
            // 작업자마다 걸음 위상을 다르게 시작해 군무처럼 보이지 않게 한다.
            anim.Play(0, 0, (seed * 0.23f) % 1f);
          }
          // 운반 목재 상자 또는 핸드트럭 장착 + 2-Bone IK 파지.
          var cargoRole = IsExtractor(nodeA) ? nodeA.Role : nodeB.Role;
          var cargo = cargoRole == BuildingRole.LumberMill ? _catalog?.LumberCargo
            : cargoRole == BuildingRole.IronExtractor ? _catalog?.IronCargo : null;
          if (cargo != null && isTrolley == false) {
            carryRig = WorkerCarryRig.Attach(go, cargo, false, fitCargo: true);
          } else {
            var propKey = isTrolley ? "const_trolley" : (seed % 2 == 0 ? "const_crate_0" : "const_crate_1");
            if (VisualCatalog.TryFilled(VisualCatalog.FxId(propKey), out var propSlot) && propSlot.Prefab != null) {
              carryRig = WorkerCarryRig.Attach(go, propSlot.Prefab, isTrolley);
            }
          }
        }
      }

      if (go == null) {
        // Kenney-only 프로필 호환 폴백.
        var kind = Dressing.Has(DressKind.AnimatedWorker) ? DressKind.AnimatedWorker : DressKind.Worker;
        go = DressingSpawn.Place(kind, transform, spawnPos, yaw, 9500 + seed);
        if (go == null) return null;
        go.name = "BaseWorker";
        foreach (var col in go.GetComponentsInChildren<Collider>(true)) Destroy(col);
        if (Dressing.Map != null && Dressing.Map.WorkerRun != null) {
          var targetObj = go.GetComponentInChildren<Animation>()?.gameObject ?? (go.transform.childCount > 0 ? go.transform.GetChild(0).gameObject : go);
          legacy = targetObj.GetComponent<Animation>() ?? targetObj.AddComponent<Animation>();
          legacy.AddClip(Dressing.Map.WorkerRun, "run");
          legacy["run"].wrapMode = WrapMode.Loop;
          if (Dressing.Map.WorkerIdle != null) {
            legacy.AddClip(Dressing.Map.WorkerIdle, "idle");
            legacy["idle"].wrapMode = WrapMode.Loop;
          }
          legacy.clip = Dressing.Map.WorkerRun;
          legacy.Play("run");
        }
      }

      return new WorkerAgent {
        Root = go,
        Animator = anim,
        Motion = MixamoMotion.Attach(anim),
        LegacyAnimation = legacy,
        CarryRig = carryRig,
        Owner = owner,
        FromEntity = nodeA.Entity,
        ToEntity = nodeB.Entity,
        FromPos = nodeA.Position,
        ToPos = nodeB.Position,
        TargetPoint = endPt,
        WalkSpeed = isTrolley ? 1.75f : (1.85f + (seed % 4) * 0.14f),
        State = WorkerState.Walking,
        IsTrolley = isTrolley,
        Seed = seed,
        ShiftRemaining = 9f + seed % 7,
      };
    }

    static void SetMotion(WorkerAgent worker, float speed, float dt) {
      if (worker.Animator != null) worker.Animator.SetFloat(SpeedParam, speed, 0.09f, dt);
      if (worker.Motion != null) {
        string pose = null;
        if (worker.State == WorkerState.Working) {
          pose = worker.IsTrolley ? "HoldingIdle" : (worker.Seed % 3 == 0 ? "Lift" : worker.Seed % 3 == 1 ? "PickUp" : "PutDown");
        } else if (worker.CarryRig != null && worker.CarryRig.IsCarrying) {
          pose = speed > 0.15f
            ? worker.IsTrolley ? "WheelbarrowWalk" : worker.Seed % 3 == 0 ? "BoxJog" : "HoldingWalk"
            : worker.IsTrolley ? "HoldingIdle" : "BoxIdle";
        }
        var clipSpeed = speed > 0.15f ? Mathf.Clamp(speed / (pose == "BoxJog" ? 2.7f : 1.8f), 0.55f, 1.3f) : 1f;
        worker.Motion.Set(pose, worker.State == WorkerState.Working ? 0f : (worker.Seed * 0.23f) % 1f, clipSpeed);
      }
      if (worker.LegacyAnimation == null) return;
      var clip = speed > 0f ? "run" : "idle";
      if (worker.LegacyAnimation[clip] == null) {
        worker.LegacyAnimation.Stop();
      } else if (worker.LegacyAnimation.IsPlaying(clip) == false) {
        worker.LegacyAnimation.CrossFade(clip, 0.12f);
      }
    }

    bool StartRestTrip(Frame frame, WorkerAgent worker, List<BuildingNode> nodes) {
      var home = FindAnchor(nodes);
      var ground = frame.Map.GetNavMesh(FactoryWarsUtil.Rules(frame).NavMeshName);
      if (ground == null) return false;
      for (var i = 0; i < 8; i++) {
        var angle = (worker.Seed % 16 + i * 2) * 22.5f * Mathf.Deg2Rad;
        var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        var point = EntrancePoint(home, home.Position + direction) + direction * (3f + worker.Seed % 3);
        var flat = new Vector3(point.x, 0f, point.z);
        if (_buildingObstacles.Exists(bounds => bounds.Contains(flat))
            || ground.Contains(flat.ToFPVector3(), *frame.NavMeshRegionMask, true) == false) continue;
        worker.FromEntity = worker.ToEntity;
        worker.FromPos = worker.ToPos;
        worker.ToEntity = home.Entity;
        worker.ToPos = home.Position;
        worker.TargetPoint = point;
        worker.RepathAt = 0f;
        worker.RestTravelRemaining = MaxBaseSpan / worker.WalkSpeed + 4f;
        worker.State = WorkerState.WalkingToRest;
        worker.CarryRig?.SetCarrying(false);
        return true;
      }
      return false;
    }

    void ResumeLogistics(WorkerAgent worker, List<BuildingNode> nodes) {
      var (_, next, _, end) = PickRoute(nodes, ++worker.Seed, worker.ToEntity);
      worker.FromEntity = worker.ToEntity;
      worker.FromPos = worker.ToPos;
      worker.ToEntity = next.Entity;
      worker.ToPos = next.Position;
      worker.TargetPoint = end;
      worker.RepathAt = 0f;
      worker.State = WorkerState.Walking;
      worker.CarryRig?.SetCarrying(worker.IsTrolley || worker.Seed % 4 != 0);
    }

    void UpdateAgents(Frame frame, float frameDelta) {
      if (frameDelta <= 0f) return;
      var dt = frameDelta;
      for (var i = _workers.Count - 1; i >= 0; i--) {
        var w = _workers[i];
        if (w.Root == null) {
          _workers.RemoveAt(i);
          continue;
        }
        var tr = w.Root.transform;
        if (w.State == WorkerState.Scattering) {
          w.ScatterTimer -= dt;
          if (w.ScatterTimer <= 0f) {
            Destroy(w.Root);
            _workers.RemoveAt(i);
            continue;
          }
          var step = w.ScatterDir * (4.1f * dt);
          var next = tr.position + step;
          next.y = TerrainKit.SurfaceHeight(_map, next.x, next.z);
          var blocked = FindBlockingBuilding(tr.position, next, out _);
          if (!blocked) tr.position = next;
          var runYaw = Mathf.Atan2(w.ScatterDir.x, w.ScatterDir.z) * Mathf.Rad2Deg;
          tr.rotation = Quaternion.RotateTowards(tr.rotation, Quaternion.Euler(0f, runYaw, 0f), 480f * dt);
          SetMotion(w, blocked ? 0f : 4.1f, dt);
          // 마지막 0.35초에 부드럽게 축소 퇴장.
          if (w.ScatterTimer < 0.35f) {
            var k = Mathf.Clamp01(w.ScatterTimer / 0.35f);
            tr.localScale = Vector3.one * (0.85f * k);
          }
          continue;
        }

        if (w.State == WorkerState.Walking || w.State == WorkerState.Working) w.ShiftRemaining -= dt;
        if (w.State == WorkerState.WalkingToRest) {
          w.RestTravelRemaining -= dt;
          if (w.RestTravelRemaining <= 0f) {
            w.State = WorkerState.Waiting;
            w.PauseTimer = 0.5f;
          }
        }
        if (w.State == WorkerState.Working || w.State == WorkerState.Resting || w.State == WorkerState.Waiting) {
          w.PauseTimer -= dt;
          w.CurrentSpeed = 0f;
          SetMotion(w, 0f, dt);
          if (w.State == WorkerState.Working) {
            var face = w.ToPos - tr.position;
            face.y = 0f;
            if (face.sqrMagnitude > 0.01f) {
              var faceYaw = Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg;
              tr.rotation = Quaternion.RotateTowards(tr.rotation, Quaternion.Euler(0f, faceYaw, 0f), 260f * dt);
            }
            if (w.CarryRig != null) {
              var progress = 1f - Mathf.Clamp01(w.PauseTimer / w.PauseDuration);
              w.CarryRig.LoadingProgress = Mathf.Sin(progress * Mathf.PI);
            }
          }
          if (w.PauseTimer <= 0f && _byOwner.TryGetValue(w.Owner, out var nodes) && nodes.Count > 0) {
            if (w.CarryRig != null) w.CarryRig.LoadingProgress = 0f;
            if (w.State == WorkerState.Working && w.ShiftRemaining <= 0f && StartRestTrip(frame, w, nodes)) {
              continue;
            }
            if (w.State == WorkerState.Resting) w.ShiftRemaining = 12f + w.Seed % 9;
            ResumeLogistics(w, nodes);
          }
          continue;
        }

        // Walking
        var delta = w.TargetPoint - tr.position;
        delta.y = 0f;
        var dist = delta.magnitude;
        if (dist <= 0.35f) {
          if (w.State == WorkerState.WalkingToRest) {
            w.State = WorkerState.Resting;
            w.PauseTimer = 3.5f + (w.Seed % 5) * 0.45f;
          } else {
            w.State = WorkerState.Working;
            w.PauseDuration = w.PauseTimer = 1.15f + (w.Seed % 4) * 0.25f;
            w.CarryRig?.SetCarrying(true);
          }
          SetMotion(w, 0f, dt);
          continue;
        }
        if (Time.time >= w.RepathAt) {
          w.RepathAt = Time.time + 0.12f;
          if (TryWalkingTarget(tr.position, w.TargetPoint, out w.WalkingTarget) == false) {
            if (w.State == WorkerState.Walking) {
              w.State = WorkerState.Waiting;
              w.PauseTimer = 0.5f;
            }
            w.CurrentSpeed = 0f;
            SetMotion(w, 0f, dt);
            continue;
          }
        }
        delta = w.WalkingTarget - tr.position;
        delta.y = 0f;
        dist = delta.magnitude;
        if (dist < 0.015f) {
          w.RepathAt = 0f;
          continue;
        }
        var dir = delta / dist;
        var targetSpeed = Mathf.Min(w.WalkSpeed, Mathf.Sqrt(2f * 5f * (w.TargetPoint - tr.position).magnitude));
        w.CurrentSpeed = Mathf.MoveTowards(w.CurrentSpeed, targetSpeed, 5f * dt);
        var move = Mathf.Min(dist, w.CurrentSpeed * dt);
        var pos = tr.position + dir * move;
        pos.y = TerrainKit.SurfaceHeight(_map, pos.x, pos.z);
        if (FindBlockingBuilding(tr.position, pos, out _)) {
          w.RepathAt = 0f;
          SetMotion(w, 0f, dt);
          continue;
        }
        tr.position = pos;
        var targetYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        tr.rotation = Quaternion.RotateTowards(tr.rotation, Quaternion.Euler(0f, targetYaw, 0f), 360f * dt);
        SetMotion(w, move / dt, dt);
      }
    }
  }
}
