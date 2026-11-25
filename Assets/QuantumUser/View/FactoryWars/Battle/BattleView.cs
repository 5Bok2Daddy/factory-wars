namespace FactoryWars.View {
  using System.Collections.Generic;
  using Photon.Deterministic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;

  /// <summary>
  /// 대전 화면. 시뮬레이션 상태를 읽어 프리팹을 놓고 움직인다. 시뮬레이션은 바꾸지 않는다.
  /// 사격은 공격 쿨다운이 다시 차는 것으로, 파괴는 엔티티가 사라지는 것으로 알아낸다.
  /// </summary>
  public unsafe class BattleView : MonoBehaviour {
    const float FollowRate = 16f;
    const float TurnRate = 300f;
    const float TurretRate = 220f;
    const float RecoilDistance = 0.25f;
    const float AimTolerance = 6f;
    const float AimMaxWait = 0.6f;
    const float AirElevation = 40f;
    const float AntiAirIdle = 14f;
    const float ElevationRate = 90f;
    const float RecoilRecover = 3.2f;
    const float SmokeSeconds = 0.42f;
    const float ShellSpeed = 90f;
    const float BulletSpeed = 120f;
    const float MissileSpeed = 42f;
    readonly HashSet<EntityRef> _workingSites = new HashSet<EntityRef>();
    readonly HashSet<EntityRef> _enteringExtractorSites = new HashSet<EntityRef>();

    /// <summary>화면에 놓인 유닛·건물 하나의 연출 상태.</summary>
    public class Actor {
      public ArtView View;
      public EntityRef Entity;
      public bool IsBuilding;
      public bool IsAir;
      public bool IsNaval;
      /// <summary>항목 34-D 작전기(비조작). 끝나면 폭발 없이 사라진다(격추는 이벤트가 폭발을 그린다).</summary>
      public bool IsSortie;
      public Vector3 Position;
      public float HullYaw;
      public float TurretYaw;
      public float Recoil;
      public float Cooldown;
      public float Phase;
      public int Color = -1;
      public bool Powered = true;
      public bool Active;
      public float SmokeAt;
      public float DamageAt;
      public float Radius;
      public Transform Turret;
      public Transform Barrel;
      public MlrsVisual Mlrs;
      public Vector3 BarrelHome;
      /// <summary>분대원마다 소총 자세(절차 IK). 비어 있으면 보병이 아니거나 사람형 뼈가 없다.</summary>
      public readonly List<RiflemanRig> Rigs = new List<RiflemanRig>();
      /// <summary>포 발사 때 차체가 뒤로 흔들리는 양(0~1).</summary>
      public float Rock;
      public Quaternion BarrelRest = Quaternion.identity;
      public float Elevation;
      public bool AntiAir;
      // 조준 먼저: 포탑이 목표를 향하기 전에 쏘라는 신호가 오면 맞출 때까지 미룬다.
      public bool Pending;
      public float PendingAt;
      public (AttackType Attack, float Range, Vector3 Target, bool Flame) PendingShot;
      public Transform Muzzle;
      public readonly List<(Transform Part, Vector3 Axis, float Speed, bool NeedsPower)> Spinners = new List<(Transform, Vector3, float, bool)>();
      public Transform ExtractorEquipment;
      public ExtractorDeliveryView ExtractorDelivery;
      public float ExtractorRouteCheckAt;
      public Transform PowerTurbine;
      public Material PowerBeaconMat;
      public ParticlePackVfx.NavalWakeHandle NavalWake;
      public ParticlePackVfx.DamageFireHandle DamageFire;
      public ParticlePackVfx.AirFlightHandle AirFlight;
      public bool InCombat;
      public readonly List<(Transform Part, Vector3 Home, Vector3 Reach, float Speed)> Sliders = new List<(Transform, Vector3, Vector3, float)>();
      public readonly List<(Transform Part, Quaternion Home, float Swing, float Speed)> Swingers = new List<(Transform, Quaternion, float, float)>();
      public GameObject Scaffold;
      public int ConstructionStage;
      public float ConstructionDisplayProgress = -1f;
      public FacilityWorkView Runway;
      public FacilityWorkView ShipWork;
      public AssetRef<UnitSpec> ShipWorkUnit;
      public float ShipWeldAt;
      public float BuildingModelY;
      public ConstructionWorkView EngineerWork;
      public FP LastUnitHealth;
      public bool EngineerWasVisible;
      public float ExtractorEntryTime;
      public GameObject ActivityCrew;
      public MixamoMotion ActivityMotion;
      public readonly List<GameObject> ActivityCrewList = new List<GameObject>(3);
      public readonly List<MixamoMotion> ActivityMotionList = new List<MixamoMotion>(3);
      public GameObject WorkerCountRoot;
      public TextMesh WorkerCountBadge;
      public bool ActivityUnavailable;
      public GameObject VehicleCrew;
      public MixamoMotion VehicleCrewMotion;
      public string VehicleCrewPose;
      public readonly List<ConstructionWorkView> ConstructionWork = new List<ConstructionWorkView>();
      public float WeldFxAt;
      public float FootprintRadius;
      public readonly List<GameObject> ConstructionCrew = new List<GameObject>();
      public GameObject Dressing;
      public float Height;
      // 항목 32 움직임: 속도(가감속 기울기), 기울기, 엄폐 표시.
      public Vector3 Velocity;
      public float Pitch;
      public float Roll;
      public GameObject Shield;
      public int VetRank;
      public TextMesh VetBadge;
      public int UpgradeMark = 1;
      public bool IsFreeFaction;
      public GameObject UpgradeKit;
      public readonly List<GameObject> UpgradeAttachments = new List<GameObject>(4);
      public TextMesh UpgradeChevron;
      public bool IsCarrier;
      public bool IsBomber;
      public bool BomberScaled;
      public Transform SiloDoorL;
      public Transform SiloDoorR;
      public Transform SiloMissile;
      public Renderer FactoryBeacon;
      public Vector3[] FactorySmoke;
      public int FactoryLevel = 1;
      public GameObject FactoryUpgradeGo;
      public float OverdriveFxAt;
      // 수송 유닛(APC · 상륙정 · 수송헬기): 후면 램프 & 월드 공간 탑승 핍([■][■][■][□]).
      public Transform CargoRamp;
      public float RampAngle;
      public GameObject PipRoot;
      public MeshRenderer[] PipRenderers;
      public int TransportCapacity;
    }

    readonly List<(GameObject Root, Transform Turn, Vector3 StartPos, float Elapsed, float NextSmoke)> _crashingCarriers =
      new List<(GameObject, Transform, Vector3, float, float)>();
    readonly List<(GameObject Rocket, Vector3 Velocity, float Until, float NextTrail)> _flyingMissiles =
      new List<(GameObject, Vector3, float, float)>();
    readonly Dictionary<EntityRef, GameObject> _strikeRockets = new Dictionary<EntityRef, GameObject>();
    readonly Dictionary<(EntityRef, EntityRef), GameObject> _synergyPipes = new Dictionary<(EntityRef, EntityRef), GameObject>();
    float _synergyPipeAt;
    float _placementRingAt;
    LineRenderer _foundationPreview;
    float _rotorSmokeAt;
    float _zoneFxAt;

    static Mesh _scaffold;

    readonly Dictionary<EntityRef, Actor> _actors = new Dictionary<EntityRef, Actor>();
    BaseAmbientWorkers _ambientWorkers;
    readonly HashSet<EntityRef> _seen = new HashSet<EntityRef>();
    readonly List<EntityRef> _gone = new List<EntityRef>();
    readonly List<Actor> _marked = new List<Actor>();
    ViewCatalog _catalog;
    MatchController _controller;
    FogOverlay _fog;
    /// <summary>시험용: 지금 안개 판.</summary>
    public FogOverlay Fog => _fog;
    /// <summary>기지 상주 건설·물류 작업자(순수 View 레이어).</summary>
    public BaseAmbientWorkers AmbientWorkers => _ambientWorkers;
    static bool FactoryWars_FogOn(Frame frame) => Quantum.FactoryWars.FogSystem.Enabled(frame);

    public WorldView World { get; private set; }
    public BattlefieldView Field { get; private set; }
    public CityView City { get; private set; }
    public BattleFx Fx { get; private set; }
    MlrsRocketView _mlrsRockets;
    public IReadOnlyDictionary<EntityRef, Actor> Actors => _actors;

    /// <summary>이름이 같은 자식을, 없으면 이름에 글자가 들어 있는 첫 자식을 찾는다(에셋 팩 모델은 이름이 길어 일부만 적는다).</summary>
    static Transform FindPart(Transform root, string text) {
      if (string.IsNullOrEmpty(text)) {
        return null;
      }
      var exact = FindDeep(root, text);
      if (exact != null) {
        return exact;
      }
      foreach (var child in root.GetComponentsInChildren<Transform>(true)) {
        if (child.name.Contains(text)) {
          return child;
        }
      }
      return null;
    }

    /// <summary>돌리는 부분의 회전축: 자기 메쉬가 가장 얇은 축(날개·프로펠러는 얇은 쪽이 회전축). "이름:X" 꼴로 적으면 그 축을 쓴다.</summary>
    static Vector3 SpinAxisOf(Transform part, char forced) {
      switch (forced) {
        case 'X': return Vector3.right;
        case 'Y': return Vector3.up;
        case 'Z': return Vector3.forward;
      }
      var filter = part.GetComponent<MeshFilter>();
      if (filter == null || filter.sharedMesh == null) {
        return Vector3.up;
      }
      var size = filter.sharedMesh.bounds.size;
      return size.x <= size.y && size.x <= size.z ? Vector3.right : size.y <= size.z ? Vector3.up : Vector3.forward;
    }

    static Transform FindDeep(Transform root, string name) {
      foreach (var child in root.GetComponentsInChildren<Transform>(true)) {
        if (child.name == name) {
          return child;
        }
      }
      return null;
    }

    static readonly Color StrikeColor = new Color(1f, 0.25f, 0.15f);
    float _strikeAt;
    /// <summary>지금 화면에 그리는 미사일 착탄 예고 수(시험용).</summary>
    public int StrikeMarks { get; private set; }

    Transform _stage;
    BattleAudio _audio;
    readonly List<(Vector3 Position, float Until)> _signals = new List<(Vector3, float)>();
    float _signalAt;

    void Init(Frame frame) {
      _stage = new GameObject("Stage").transform;
      _stage.SetParent(transform, false);
      GameAudio.InstallProceduralDefaults();
      _audio = new BattleAudio(_stage.gameObject);
      _catalog = Resources.Load<ViewCatalog>(ViewCatalog.ResourcePath);
      WorldView.HideBakedScene();
      WorldView.ApplyLighting(Camera.main);
      GraphicsQuality.Bind(Camera.main);
      // Kenney 모델 인스턴싱(항목 19B). 월드·도시가 만들어지며 그룹을 채운다.
      var dressing = new GameObject("Dressing").AddComponent<DressingRenderer>();
      dressing.transform.SetParent(_stage, false);
      // 전장 손상 지도(항목 27 K-2). 숲·바닥이 만들어지기 전에 있어야 한다.
      var scars = new GameObject("BattleScars").AddComponent<BattleScars>();
      scars.transform.SetParent(_stage, false);
      World = new WorldView(_stage, frame.FindAsset<MapSpec>(frame.Map.UserAsset), _catalog);
      WorldView.ApplyBiomeAtmosphere(World.Map, Camera.main);
      Fx = new GameObject("Fx").AddComponent<BattleFx>();
      _mlrsRockets = new MlrsRocketView(_stage, World.Map, Fx);
      Fx.transform.SetParent(_stage, false);
      World.Fx = Fx;
      scars.Fx = Fx;
      scars.Init(frame.FindAsset<MapSpec>(frame.Map.UserAsset));
      Field = new BattlefieldView(_stage);
      QuantumEvent.Subscribe<EventAreaImpact>(this, e => Field?.CollapseRoadsideBarriers(e.Position.ToUnityVector3(), e.Radius.AsFloat));
      QuantumEvent.Subscribe<EventSupplyDropIncoming>(this, e => {
        var spots = e.Game.Frames.Verified != null ? e.Game.Frames.Verified.FindAsset<MapSpec>(e.Game.Frames.Verified.Map.UserAsset).CrateSpots : null;
        var until = Time.time + FactoryWarsUtil.Rules(e.Game.Frames.Verified).DropWarningSeconds.AsFloat + 3f;
        for (var i = 0; spots != null && i < spots.Length; i++) {
          if (spots[i].Seat < 0 && spots[i].Group == e.Group) {
            _signals.Add((spots[i].Position.ToUnityVector3(), until));
          }
        }
      });
      QuantumEvent.Subscribe<EventCratePicked>(this, e => Fx.Burst(e.Position.ToUnityVector3(), new Color(1f, 0.85f, 0.3f), 1f));
      QuantumEvent.Subscribe<EventMlrsLaunched>(this, e => {
        var frame = e.Game.Frames.Verified;
        if (_actors.TryGetValue(e.Battery, out var launcherAct) && launcherAct.View?.Animation != null) {
          launcherAct.View.Animation.ThrowGrenade();
        }
        var viewer = _controller != null ? _controller.Player : PlayerRef.None;
        if (!viewer.IsValid || viewer == e.Owner || (frame != null && FogSystem.IsVisible(frame, viewer, e.Origin))) {
          var position = _mlrsRockets.AtSurface(e.Origin.ToUnityVector3());
          Fx.MlrsExhaust(position, e.Direction.ToUnityVector3(), false);
          _audio.NoteCombat(GameAudio.PlayAt(SoundEvent.RocketFire, position));
        }
      });
      QuantumEvent.Subscribe<EventMlrsImpact>(this, e => {
        var frame = e.Game.Frames.Verified;
        var viewer = _controller != null ? _controller.Player : PlayerRef.None;
        if (!viewer.IsValid || (frame != null && FogSystem.IsVisible(frame, viewer, e.Position))) {
          Fx.MlrsImpact(_mlrsRockets.AtSurface(e.Position.ToUnityVector3()), e.Radius.AsFloat);
        }
      });
      QuantumEvent.Subscribe<EventTraderShot>(this, e => {
        var to = e.To.ToUnityVector3() + Vector3.up * 0.8f;
        // Task 39: garrison shots start at the garrison soldier's muzzle.
        var garrison = _merc?.ShotOrigin(e.From.ToUnityVector3(), to);
        var from = garrison ?? (_trader != null ? _trader.ShotOrigin(e.From.ToUnityVector3(), to) : e.From.ToUnityVector3() + Vector3.up * 3f);
        var dir = (to - from).normalized;
        Fx.MuzzleFlash(from, dir, 0.45f);
        Fx.Needle(from + dir * 0.1f, to, 90f);
        Fx.Impact(to, 0.8f, dir);
      });
      QuantumEvent.Subscribe<EventSniperFired>(this, e => {
        var frame = e.Game.Frames.Verified;
        var viewer = _controller != null ? _controller.Player : PlayerRef.None;
        if (viewer.IsValid == false || viewer == e.Shooter || viewer == e.Victim
            || (frame != null && FogSystem.IsVisible(frame, viewer, e.From))) {
          _audio.NoteCombat(GameAudio.PlayAt(SoundEvent.SniperFire, e.From.ToUnityVector3()));
        }
      });
      QuantumEvent.Subscribe<EventSortieLaunched>(this, e => {
        var frame = e.Game.Frames.Verified;
        if (frame != null && frame.TryGet<Transform3D>(e.Sortie, out var plane)) {
          var viewer = _controller != null ? _controller.Player : PlayerRef.None;
          if (viewer.IsValid == false || e.Player == viewer || FogSystem.IsVisible(frame, viewer, plane.Position)) {
            GameAudio.PlayAt(SoundEvent.JetFlyby, plane.Position.ToUnityVector3());
          }
        }
      });
      QuantumEvent.Subscribe<EventSortieEnded>(this, e => { if (e.ShotDown) { Fx.Explosion(e.Position.ToUnityVector3() + Vector3.up * 8f, 1.4f); } });
      QuantumEvent.Subscribe<EventCaptureChanged>(this, e => {
        var frame = e.Game.Frames.Verified;
        if (e.Next.IsValid && frame != null && frame.Exists(e.Site)) {
          Fx.Pulse(frame.Get<Transform3D>(e.Site).Position.ToUnityVector3(), Palette.Of(frame, e.Next), frame.Get<CaptureSite>(e.Site).Radius.AsFloat);
        }
      });
      // 항목 33: 미사일 착탄·항모 격추는 기존 폭발 효과를 크게 쓴다(새 그래픽 없음).
      QuantumEvent.Subscribe<EventUltimateNotice>(this, e => {
        if (e.Kind == UltimateKind.StrategicMissile && e.Stage != UltimateStage.Impact) {
          return;
        }
        if (e.Stage == UltimateStage.Impact) {
          if (e.Value < 0) {
            Fx.Explosion(e.Position.ToUnityVector3(), 4.4f);
            Fx.Burst(e.Position.ToUnityVector3(), new Color(0.28f, 0.95f, 1f), 3.2f);
          } else {
            Fx.MushroomExplosion(e.Position.ToUnityVector3(), 5.5f);
          }
        } else if (e.Stage == UltimateStage.Lost) {
          Fx.MushroomExplosion(e.Position.ToUnityVector3(), 4.2f);
        }
      });
      QuantumEvent.Subscribe<EventMissileBackblast>(this, e => {
        var pos = e.Position.ToUnityVector3();
        Fx.SiloSteam(pos);
        Fx.Explosion(pos + Vector3.up * 0.6f, 2.2f);
        Fx.Ring(pos, new Color(1f, 0.48f, 0.12f, 0.9f), e.Radius.AsFloat, 0.65f);
      });
      QuantumEvent.Subscribe<EventDepotExploded>(this, e => {
        var pos = e.Position.ToUnityVector3();
        Fx.MushroomExplosion(pos, 3.2f);
      });
      QuantumEvent.Subscribe<EventTransportRamp>(this, e => {
        if (_actors.TryGetValue(e.Transport, out var a) && a.View != null) {
          var p = a.View.transform.position - a.View.Turn.forward * 2.2f + Vector3.up * 0.25f;
          Fx.Burst(p, e.Unload ? new Color(0.24f, 0.94f, 1f, 0.85f) : new Color(0.85f, 0.82f, 0.72f, 0.75f), 0.75f);
        }
      });
      QuantumEvent.Subscribe<EventMissileInterceptShot>(this, e => {
        var from = e.From.ToUnityVector3();
        var to = e.To.ToUnityVector3();
        Fx.LaunchMissile(from, to, 72f, e.Killed ? 3.2f : 1.1f);
        if (e.Killed) {
          Fx.Explosion(to, 4.0f);
          Fx.Burst(to, new Color(0.32f, 0.95f, 1f), 2.8f);
        }
      });
      QuantumEvent.Subscribe<EventHqProtocol>(this, e => {
        Fx.Pulse(e.Position.ToUnityVector3(), new Color(1f, 0.72f, 0.18f), 14f);
      });
      QuantumEvent.Subscribe<EventHqProtocolShot>(this, e => {
        var from = e.From.ToUnityVector3() + Vector3.up * 4.5f;
        var to = e.To.ToUnityVector3() + Vector3.up * 0.9f;
        var dir = (to - from).normalized;
        Fx.MuzzleFlash(from, dir, 0.5f);
        Fx.Needle(from + dir * 0.1f, to, 110f);
        Fx.Impact(to, 0.9f, dir);
      });
      City = new CityView(_stage, frame.FindAsset<MapSpec>(frame.Map.UserAsset)) { Fx = Fx };
      _ambientWorkers = new GameObject("AmbientWorkers").AddComponent<BaseAmbientWorkers>();
      _ambientWorkers.transform.SetParent(_stage, false);
      _ambientWorkers.Init(World.Map, _catalog, this);
      _controller = FindAnyObjectByType<MatchController>();
    }

    /// <summary>룩 비교용: 화면을 모두 지우고 다음 LateUpdate에서 지금 프로필로 다시 만든다.</summary>
    public void Rebuild() {
      QuantumEvent.UnsubscribeListener(this);
      // 화면이 만든 것은 모두 Stage 아래에 있다. 같은 물체 아래의 HUD는 그대로 둔다.
      if (_stage != null) {
        Destroy(_stage.gameObject);
        _stage = null;
      }
      _actors.Clear();
      _audio = null;
      _strikeRockets.Clear();
      _mlrsRockets = null;
      _ambientWorkers = null;
      _marked.Clear();
      World = null;
    }

    void UpdateBaseWorkers(Frame frame, float dt) {
      _ambientWorkers?.Tick(frame, dt, _controller != null ? _controller.Player : PlayerRef.None);
    }

    void OnDestroy() {
      GameAudio.StopWorldVoices();
    }

    void LateUpdate() {
      var frame = QuantumRunner.Default?.Game?.Frames?.Predicted;
      if (frame == null) {
        return;
      }
      if (World == null) {
        Init(frame);
      }
      var dt = Time.deltaTime;
      World.Update(frame, dt, _controller != null ? _controller.Player : PlayerRef.None);
      World.UpdateBridges(frame);
      Field.Update(frame, dt, Fx);
      City.Update(frame, _controller != null ? _controller.Player : PlayerRef.None, dt);
      // 항목 34-A: 전장의 안개 판(안개가 꺼져 있으면 만들지 않는다).
      if (FactoryWars_FogOn(frame)) {
        _fog ??= new FogOverlay(_stage, frame);
        _fog.Refresh(frame, _controller != null ? _controller.Player : PlayerRef.None);
      } else if (_fog != null) {
        _fog.Destroy();
        _fog = null;
      }
      if (_signals.Count > 0 && Time.time >= _signalAt) {
        _signalAt = Time.time + 0.18f / Mathf.Max(0.35f, GraphicsQuality.FxScale);
        _signals.RemoveAll(s => Time.time > s.Until || Field.CrateNear(s.Position));
        foreach (var signal in _signals) {
          Fx.Signal(signal.Position);
        }
      }
      UpdateWeather(frame);
      _trader ??= new TraderView(_stage);
      _trader.Update(frame, _catalog, Fx, _controller != null && _controller.Input != null && _controller.Input.Selection.Building != EntityRef.None && frame.Has<NpcTrader>(_controller.Input.Selection.Building), dt);
      _merc ??= new MercCampView(_stage);
      _merc.Update(frame, dt);
      _drawbridges ??= new DrawbridgeView(_stage);
      _drawbridges.Update(frame, dt);
      _arches ??= new ArchBridgeView(_stage);
      _arches.Update(frame);
      _logistics ??= new LogisticsView(_stage);
      _logistics.Update(frame);
      // Logistics trucks: the cargo crates in the truck model ("Cargo" child) show only while it carries a load.
      foreach (var pair in frame.GetComponentIterator<LogisticsTruck>()) {
        if (_actors.TryGetValue(pair.Entity, out var truckActor) == false || truckActor.View == null || truckActor.View.Model == null) continue;
        var cargo = truckActor.View.Model.transform.Find("Cargo");
        if (cargo == null) continue;
        var c = pair.Component.Cargo;
        var loaded = c.Iron + c.Wood + c.Steel + c.Power > FP._0;
        if (cargo.gameObject.activeSelf != loaded) cargo.gameObject.SetActive(loaded);
      }
      _waterway ??= new WaterwayView(_stage);
      _waterway.Update(frame);
      World.ShowNoBuild(_controller != null && _controller.Input != null && _controller.Input.IsPlacing);
      UpdatePlacementRings(frame);
      UpdateStrikeRockets(frame, dt);
      UpdateScorchedAndEmpFx(frame);
      // 기동형 MLS 전환에 따라 전략 미사일 발사/착탄 붉은 경고 링·레이저 표시를 비활성화한다.
      StrikeMarks = 0;
      UpdateSuperweaponVisuals(dt);

      _audio.Begin(Camera.main);
      _seen.Clear();
      _workingSites.Clear();
      _enteringExtractorSites.Clear();
      var units = frame.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out var entity, out var transform3D, out var unit)) {
        _seen.Add(entity);
        UpdateUnit(frame, entity, transform3D, unit, dt);
      }
      var buildings = frame.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out var entity, out var transform3D, out var building)) {
        _seen.Add(entity);
        UpdateBuilding(frame, entity, transform3D, building, dt);
      }
      _mlrsRockets.Tick(frame, _controller != null ? _controller.Player : PlayerRef.None, Camera.main);
      var sorties = frame.Filter<Transform3D, Sortie>();
      while (sorties.NextUnsafe(out var entity, out var transform3D, out var sortie)) {
        _seen.Add(entity);
        UpdateSortie(frame, entity, transform3D, sortie, dt);
      }
      _audio.Mix(Time.unscaledDeltaTime);

      _gone.Clear();
      foreach (var pair in _actors) {
        if (_seen.Contains(pair.Key) == false) {
          _gone.Add(pair.Key);
        }
      }
      foreach (var entity in _gone) {
        Remove(entity);
      }
      UpdateBaseWorkers(frame, dt);
      UpdateSelection(frame);
    }

    public Vector3 LastExplosionPosition { get; private set; }

    void Remove(EntityRef entity) {
      var actor = _actors[entity];
      var center = (actor.IsAir && actor.View.Turn != null)
        ? actor.View.Turn.position
        : actor.View.transform.position + Vector3.up * (actor.Height * 0.4f);
      if (actor.IsSortie) {
        // 작전기: 귀환은 조용히 사라지고, 격추면 SortieEnded 이벤트가 이미 폭발을 그렸다.
        Destroy(actor.View.gameObject);
        _actors.Remove(entity);
        return;
      }
      if (actor.IsCarrier) {
        actor.View.SetSelected(false);
        if (actor.View.Bar != null) {
          actor.View.Bar.SetActive(false);
        }
        _crashingCarriers.Add((actor.View.gameObject, actor.View.Turn, actor.View.Turn.localPosition, 0f, 0f));
        _marked.Remove(actor);
        _actors.Remove(entity);
        return;
      }
      LastExplosionPosition = center;
      Fx.Explosion(center, actor.IsBuilding ? 2.2f : actor.IsBomber ? 2.4f : Mathf.Clamp(actor.Radius * 0.5f, 0.5f, 1.5f));
      if (actor.IsBuilding) {
        // 파괴된 기지 건물은 사라지지 않고 영구 붕괴 콘크리트 잔해 더미 + 영구 그을음으로 남는다.
        Fx.PermanentRubble(actor.View.transform.position, Mathf.Max(2.8f, actor.Radius * 0.9f));
      } else if (actor.View.Slot != null && actor.View.Slot.WreckPrefab != null && actor.View.Model != null) {
        // The slot's own destroyed model (same pivot and scale as the live model), then the usual fire.
        var model = actor.View.Model.transform;
        Fx.ModelWreck(actor.View.Slot.WreckPrefab, model.position, model.rotation, model.lossyScale, 30f);
      } else if (actor.IsAir == false && actor.IsNaval == false && actor.Radius > 1.8f) {
        Fx.Wreck(actor.View.transform.position, actor.Radius * 0.85f, 30f);
      }
      _marked.Remove(actor);
      // 교체 모델에 사망 애니메이션이 있으면 그만큼 보여 주고 지운다.
      var linger = actor.View.Animation != null ? actor.View.Animation.Die() : 0f;
      if (linger > 0f) {
        actor.View.SetSelected(false);
        Destroy(actor.View.gameObject, linger);
      } else {
        Destroy(actor.View.gameObject);
      }
      _actors.Remove(entity);
    }

    TraderView _trader;
    public TraderView Trader => _trader;
    MercCampView _merc;
    DrawbridgeView _drawbridges;
    ArchBridgeView _arches;
    LogisticsView _logistics;
    public LogisticsView Logistics => _logistics;
    WaterwayView _waterway;
    public WaterwayView Waterway => _waterway;
    public ArchBridgeView ArchBridges => _arches;
    public DrawbridgeView Drawbridges => _drawbridges;
    public MercCampView MercCamp => _merc;
    GameObject _weatherFx;
    bool _weatherOn;
    bool _fogBefore;
    Color _fogColorBefore;
    float _fogDensityBefore;
    FogMode _fogModeBefore;

    /// <summary>
    /// 항목 37 기상 연출(화면 전용, 시뮬레이션 상태 WeatherOn만 읽음): 맵 테마에 맞는 팩 파티클(모래바람·비·안개)을 카메라 앞에 띄우고
    /// 장면 안개를 모래색·회색으로 잠깐 짙게 한다. 낮음 품질은 파티클 없이 안개 색만.
    /// </summary>
    void UpdateWeather(Frame frame) {
      var on = (bool)frame.Global->WeatherOn;
      if (on == _weatherOn) {
        return;
      }
      _weatherOn = on;
      var theme = frame.FindAsset<MapSpec>(frame.Map.UserAsset).Theme ?? "";
      var sand = theme.Contains("사막");
      if (on) {
        _fogBefore = RenderSettings.fog;
        _fogColorBefore = RenderSettings.fogColor;
        _fogDensityBefore = RenderSettings.fogDensity;
        _fogModeBefore = RenderSettings.fogMode;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogColor = sand ? new Color(0.78f, 0.68f, 0.52f) : new Color(0.62f, 0.66f, 0.70f);
        RenderSettings.fogDensity = 0.0045f;
        var camera = Camera.main;
        var slot = VisualCatalog.Current != null ? VisualCatalog.Current.Get(VisualCatalog.FxId(sand ? "weather_sand" : "weather_rain")) : null;
        if (camera != null && slot != null && slot.Prefab != null && GraphicsQuality.Current != QualityTier.Low) {
          _weatherFx = Instantiate(slot.Prefab, camera.transform);
          _weatherFx.name = "WeatherFx";
          _weatherFx.transform.localPosition = new Vector3(0f, -6f, 30f);
          _weatherFx.transform.localScale = Vector3.one * 4f;
        }
      } else {
        RenderSettings.fog = _fogBefore;
        RenderSettings.fogColor = _fogColorBefore;
        RenderSettings.fogDensity = _fogDensityBefore;
        RenderSettings.fogMode = _fogModeBefore;
        if (_weatherFx != null) {
          Destroy(_weatherFx);
          _weatherFx = null;
        }
      }
    }

    /// <summary>항목 34-D: 작전기. 진영 색을 입힌 빌린 모델(VisualCatalog 칸 unit/*_sortie_*)이 시뮬레이션 궤적을 부드럽게 따라간다.</summary>
    void UpdateSortie(Frame frame, EntityRef entity, Transform3D* transform3D, Sortie* sortie, float dt) {
      var spec = frame.FindAsset(sortie->Spec);
      var target = transform3D->Position.ToUnityVector3();
      if (_actors.TryGetValue(entity, out var actor) == false) {
        actor = Create(entity, spec.name, false, true);
        if (actor == null) {
          return;
        }
        actor.IsSortie = true;
        actor.View.transform.position = target;
        ArtLook.Apply(actor.View.Model, frame.Global->Players.GetPointer(sortie->Owner)->ColorIndex);
      }
      var view = actor.View.transform;
      var next = Vector3.Lerp(view.position, target, Mathf.Clamp01(dt * 12f));
      var move = next - view.position;
      if (move.sqrMagnitude > 0.0001f) {
        var bank = Mathf.Clamp(Vector3.SignedAngle(view.forward, move, Vector3.up) * 2f, -35f, 35f);
        view.rotation = Quaternion.Slerp(view.rotation, Quaternion.LookRotation(move.normalized, Vector3.up) * Quaternion.Euler(0, 0, -bank), Mathf.Clamp01(dt * 6f));
      }
      view.position = next;
      var viewer = _controller != null ? _controller.Player : PlayerRef.None;
      var visible = viewer.IsValid == false || sortie->Owner == viewer || FogSystem.IsVisible(frame, viewer, transform3D->Position);
      if (actor.View.gameObject.activeSelf != visible) {
        actor.View.gameObject.SetActive(visible);
      }
      if (visible) _audio.Engine(SoundEvent.AirEngine, next, 1f);
    }

    Actor Create(EntityRef entity, string id, bool isBuilding, bool isAir) {
      var view = ArtView.Create(_catalog, id, _stage, 0);
      if (view == null) {
        return null;
      }
      var actor = new Actor { View = view, Entity = entity, IsBuilding = isBuilding, IsAir = isAir, IsNaval = id.Contains("boat") || id.Contains("ship") || id.Contains("landing_craft"), Radius = view.Entry.RingRadius, Height = view.Entry.BarHeight, Phase = Random.value * 10f };
      var model = view.Model.transform;
      actor.Turret = FindDeep(model, "Turret") ?? FindDeep(model, "Launcher");
      actor.Barrel = FindDeep(model, "Barrel") ?? FindDeep(model, "Guns") ?? FindDeep(model, "Tubes");
      if (view.Slot != null) {
        actor.Turret = FindPart(model, view.Slot.TurretPart) ?? actor.Turret;
        actor.Barrel = FindPart(model, view.Slot.BarrelPart) ?? actor.Barrel;
      }
      if (actor.Barrel != null) {
        actor.BarrelHome = actor.Barrel.localPosition;
        actor.BarrelRest = actor.Barrel.localRotation;
      }
      actor.Muzzle = (view.Slot != null ? FindPart(model, view.Entry.MuzzlePart) : FindDeep(model, view.Entry.MuzzlePart)) ?? model;

      void Spin(string name, Vector3 axis, float speed, bool needsPower) {
        var part = FindDeep(model, name);
        if (part != null) {
          actor.Spinners.Add((part, axis, speed, needsPower));
        }
      }
      void Slide(string name, Vector3 reach, float speed) {
        var part = FindDeep(model, name);
        if (part != null) {
          actor.Sliders.Add((part, part.localPosition, reach, speed));
        }
      }
      void Swing(string name, float degrees, float speed) {
        var part = FindDeep(model, name);
        if (part != null) {
          actor.Swingers.Add((part, part.localRotation, degrees, speed));
        }
      }
      Spin("Rotor", Vector3.up, 1100f, false);
      Spin("TailFan", Vector3.forward, 1500f, false);
      Spin("CoreRing", Vector3.up, 70f, true);
      Spin("Coil", Vector3.up, 90f, true);
      if (isBuilding) {
        Spin("Radar", Vector3.up, 42f, true);
        Spin("Dish", Vector3.up, 16f, true);
        Spin("Antenna", Vector3.up, 26f, true);
      } else {
        Spin("Radar", Vector3.up, 80f, false);
      }
      if (view.Slot != null && view.Slot.SpinParts != null) {
        foreach (var entry in view.Slot.SpinParts) {
          var split = entry.Split(':');
          foreach (var part in model.GetComponentsInChildren<Transform>(true)) {
            if (part.name.Contains(split[0])) {
              actor.Spinners.Add((part, SpinAxisOf(part, split.Length > 1 && split[1].Length > 0 ? split[1][0] : ' '), 1100f, false));
            }
          }
        }
      }
      Swing("Flag", 12f, 2.4f);
      Swing("Crane", 34f, 0.5f);
      Swing("Arm", 16f, 2.6f);
      Slide("Ingot", new Vector3(0, 0, -1.4f), 0.5f);
      Slide("Forklift", new Vector3(1.3f, 0, 0), 0.32f);
      Slide("Hoist", new Vector3(1.9f, 0, 0), 0.22f);
      Slide("Door", new Vector3(0, 0.9f, 0), 0.4f);
      // 원시 도형(큐브·실린더·파란 원) 전면 금지: VisualCatalog의 100% 실제 Synty/Kenney 프리팹을 그대로 유지한다.
      if (id == "ff_stormnest_carrier") {
        actor.IsCarrier = true;
        AttachCarrierGroundShadow(actor);
      } else if (isBuilding && (id.EndsWith("_barracks") || id.EndsWith("_vehicle_factory") || id.EndsWith("_heavy_works")
          || id.EndsWith("_processor") || id.EndsWith("_power_plant") || id.EndsWith("_airfield") || id.EndsWith("_carrier_yard"))) {
        actor.FactorySmoke = new[] { new Vector3(-2.0f, Mathf.Max(5.2f, actor.Height * 0.78f), -1.2f) };
      }
      _actors[entity] = actor;
      return actor;
    }

    /// <summary>실제 Synty 공중 항모(SM_Veh_TransportPlane_01) 아래 부드러운 지상 그림자 데칼만 부착한다(파란 원·원시 블록 금지).</summary>
    void AttachCarrierGroundShadow(Actor actor) {
      var shadowGo = new GameObject("CarrierShadow", typeof(MeshFilter), typeof(MeshRenderer));
      shadowGo.transform.SetParent(actor.View.transform, false);
      shadowGo.transform.localPosition = new Vector3(0f, 0.08f, 0f);
      var sMesh = new Mesh { name = "carrier_ground_shadow" };
      const float sx = 6.8f;
      const float sz = 7.6f;
      sMesh.vertices = new[] { new Vector3(-sx, 0f, -sz), new Vector3(-sx, 0f, sz), new Vector3(sx, 0f, sz), new Vector3(sx, 0f, -sz) };
      sMesh.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f) };
      sMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
      sMesh.RecalculateNormals();
      shadowGo.GetComponent<MeshFilter>().sharedMesh = sMesh;
      var sRenderer = shadowGo.GetComponent<MeshRenderer>();
      sRenderer.sharedMaterial = BattleFx.ScorchMaterial();
      sRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
      sRenderer.receiveShadows = false;
    }

    float _aimRingAt;

    /// <summary>시험용: 궁극 목표 지정 중 화면 가운데 조준 고리의 자리·반경(조준하지 않으면 반경 0).</summary>
    public (Vector3 Position, float Radius) UltimateAim { get; private set; }

    /// <summary>
    /// 궁극 목표 지정(미사일) 중: 화면 가운데 땅에 실제 폭발 반경(UltimateSpec.BlastRadius) 고리를 그린다.
    /// 화면을 끌어 고리를 목표에 맞추고 그 자리를 누르면 발사된다. 시뮬레이션 값은 읽기만 한다.
    /// </summary>
    void UpdateUltimateAim(Frame frame) {
      var input = _controller != null ? _controller.Input : null;
      if (input == null || input.Mode != MatchInput.TapMode.Ultimate) {
        UltimateAim = default;
        return;
      }
      var spec = UltimateSystem.SpecOf(frame, _controller.Player);
      var cam = Camera.main;
      if (spec == null || spec.Kind != UltimateKind.StrategicMissile || cam == null) {
        return;
      }
      var ray = cam.ScreenPointToRay(new Vector3(cam.pixelWidth * 0.5f, cam.pixelHeight * 0.5f, 0f));
      if (new UnityEngine.Plane(Vector3.up, Vector3.zero).Raycast(ray, out var distance) == false) {
        return;
      }
      var point = ray.GetPoint(distance);
      UltimateAim = (point, spec.BlastRadius.AsFloat);
      if (Time.time < _aimRingAt) {
        return;
      }
      _aimRingAt = Time.time + 0.3f;
      Fx.Ring(point + Vector3.up * 0.2f, new Color(1f, 0.18f, 0.14f, 0.9f), spec.BlastRadius.AsFloat, 0.4f);
      Fx.Ring(point + Vector3.up * 0.2f, new Color(1f, 0.85f, 0.2f, 0.95f), 1.5f, 0.4f);
    }

    /// <summary>Task 46: reason shown for a red ghost (null when valid or no specific reason).</summary>
    public string PlacementHint { get; private set; }

    void UpdatePlacementRings(Frame frame) {
      UpdateUltimateAim(frame);
      if (_controller == null || _controller.Input == null || _controller.Input.IsPlacing == false) {
        if (_foundationPreview != null) _foundationPreview.enabled = false;
        return;
      }
      if (Time.time < _placementRingAt) return;
      _placementRingAt = Time.time + 0.28f;
      var spec = frame.FindAsset(_controller.Input.Placing);
      if (spec == null) {
        return;
      }
      var player = _controller.Player;
      // 전략 미사일 격납고 배치 모드: 점령한 중립 거점·암시장 주변 18m 허용 반경을 녹색 링으로 표시.
      if (spec.RequiresNeutralOutpost) {
        var ult = UltimateSystem.SpecOf(frame, player);
        var outpostRadius = ult != null && ult.SiloOutpostRadius > FP._0 ? ult.SiloOutpostRadius.AsFloat : 18f;
        var sites = frame.Filter<Transform3D, CaptureSite>();
        while (sites.NextUnsafe(out _, out var t, out var site)) {
          var mine = site->Owner == player;
          Fx.Ring(t->Position.ToUnityVector3(), mine ? new Color(0.2f, 0.95f, 0.38f, 0.9f) : new Color(0.95f, 0.25f, 0.2f, 0.7f), outpostRadius, 0.35f);
        }
        var traders = frame.Filter<Transform3D, NpcTrader>();
        var bit = player.IsValid ? (1 << (int)player) : 0;
        while (traders.NextUnsafe(out _, out var t, out var trader)) {
          if (trader->Destroyed == false) {
            var ok = (trader->HostileMask & bit) == 0;
            Fx.Ring(t->Position.ToUnityVector3(), ok ? new Color(0.2f, 0.95f, 0.38f, 0.9f) : new Color(0.95f, 0.25f, 0.2f, 0.7f), outpostRadius, 0.35f);
          }
        }
      }
      // 채취 시설: 지을 수 있는 자원 지점을 녹색 고리로, 이미 쓰였거나 막힌 지점을 회색 고리로 보인다(화면 안 지점만).
      if (spec.RequiresResourceNode) {
        foreach (var pair in frame.GetComponentIterator<ResourceNode>()) {
          var center = pair.Component.Shape.Center;
          var onScreen = Camera.main != null ? Camera.main.WorldToViewportPoint(center.ToUnityVector3()) : Vector3.zero;
          if (onScreen.z <= 0f || onScreen.x < -0.1f || onScreen.x > 1.1f || onScreen.y < -0.1f || onScreen.y > 1.1f) {
            continue;
          }
          var free = ProductionSystem.TryGetBuildPlacement(frame, spec, center, player, out _);
          Fx.Ring(center.ToUnityVector3() + Vector3.up * 0.2f, free ? new Color(0.25f, 0.95f, 0.45f, 0.9f) : new Color(0.55f, 0.58f, 0.62f, 0.55f),
            Mathf.Max(2.5f, pair.Component.Shape.Radius.AsFloat), 0.35f);
        }
      }
      // 카메라 중앙/포인터 지점의 건설 가능 여부를 녹색/적색 링으로 실시간 표시.
      var cam = Camera.main;
      if (cam != null) {
        var ray = cam.ScreenPointToRay(new Vector3(cam.pixelWidth * 0.5f, cam.pixelHeight * 0.5f, 0f));
        if (new UnityEngine.Plane(Vector3.up, Vector3.zero).Raycast(ray, out var hitDist)) {
          var world = ray.GetPoint(hitDist);
          var fp = new FPVector3(FP.FromFloat_UNSAFE(world.x), FP._0, FP.FromFloat_UNSAFE(world.z));
          var valid = ProductionSystem.TryGetBuildPlacement(frame, spec, fp, player, out var alignedPosition);
          if (spec.RequiresResourceNode) {
            world = alignedPosition.ToUnityVector3();
          }
          if (_foundationPreview == null) _foundationPreview = FoundationView.CreatePreview(_stage);
          FoundationView.UpdatePreview(_foundationPreview, World.Map, world, spec.FootprintRadius.AsFloat, valid);
          // Task 46: never fail silently: a red shipyard ghost says why (zone rule).
          PlacementHint = valid ? null : ProductionSystem.PlacementReason(frame, spec, fp, player) == RejectReason.ShipyardZone ? Texts.ShipyardZone : null;
          if (PlacementHint != null) _controller.Hud?.ShowTooltip(PlacementHint);
        }
      }
    }

    void UpdateStrikeRockets(Frame frame, float dt) {
      if (_stage == null) {
        return;
      }
      var active = new HashSet<EntityRef>();
      foreach (var pair in frame.GetComponentIterator<MissileStrike>()) {
        active.Add(pair.Entity);
        var pos = pair.Component.CurrentPosition.ToUnityVector3();
        if (_strikeRockets.TryGetValue(pair.Entity, out var rocket) == false || rocket == null) {
          if (VisualCatalog.TryFilled(VisualCatalog.FxId("icbm_rocket"), out var mSlot) && mSlot.Prefab != null) {
            rocket = Instantiate(mSlot.Prefab, pos, Quaternion.identity, _stage);
            rocket.name = "StrikeICBM";
            rocket.transform.localScale = Vector3.one * (mSlot.Scale > 0f ? mSlot.Scale : 2.2f);
          } else {
            rocket = new GameObject("StrikeICBM", typeof(MeshFilter), typeof(MeshRenderer));
            rocket.transform.SetParent(_stage, false);
            var kit = new MeshKit();
            kit.Paint(Swatch.White).Cylinder(new Vector3(0f, 0f, 0f), 0.46f, 3.0f, 10);
            kit.Paint(Swatch.GlowRed).Cone(new Vector3(0f, 1.95f, 0f), 0.48f, 1.0f, 10);
            rocket.GetComponent<MeshFilter>().sharedMesh = kit.ToMesh("strike_icbm");
            rocket.GetComponent<MeshRenderer>().sharedMaterial = ArtLook.NeutralMaterial;
            rocket.transform.position = pos;
          }
          _strikeRockets[pair.Entity] = rocket;
        }
        var delta = pos - rocket.transform.position;
        if (delta.sqrMagnitude > 0.001f) {
          rocket.transform.rotation = Quaternion.LookRotation(delta.normalized);
        }
        rocket.transform.position = pos;
        Fx.MuzzleFlash(pos - delta.normalized * 1.2f, -delta.normalized, 0.9f);
      }
      var stale = new List<EntityRef>();
      foreach (var kv in _strikeRockets) {
        if (active.Contains(kv.Key) == false) {
          if (kv.Value != null) {
            Destroy(kv.Value);
          }
          stale.Add(kv.Key);
        }
      }
      foreach (var key in stale) {
        _strikeRockets.Remove(key);
      }
    }

    void UpdateScorchedAndEmpFx(Frame frame) {
      if (Time.time < _zoneFxAt) {
        return;
      }
      _zoneFxAt = Time.time + 0.38f;
      // 전략 미사일 초토화 구역(15초): 지속 암흑 크레이터 + 중앙 잔불 스파크(점선 도트 링 대신 현실적인 그을음/화염).
      foreach (var pair in frame.GetComponentIterator<ScorchedZone>()) {
        var z = pair.Component;
        if (frame.Number >= z.UntilFrame) {
          continue;
        }
        var pos = z.Position.ToUnityVector3();
        var r = z.Radius.AsFloat;
        var ember = pos + new Vector3(Random.Range(-r * 0.45f, r * 0.45f), 0.35f, Random.Range(-r * 0.45f, r * 0.45f));
        Fx.Explosion(ember, 0.72f);
      }
      // EMP 충격파 마비(5초) 건물: 청록색 전기 스파크.
      foreach (var pair in frame.GetComponentIterator<Building>()) {
        if (pair.Component.EmpUntilFrame > frame.Number) {
          var pos = frame.Get<Transform3D>(pair.Entity).Position.ToUnityVector3() + Vector3.up * 2.2f;
          Fx.Burst(pos, new Color(0.25f, 0.92f, 1f), 1.1f);
        }
      }
      // 완공 비행 중인 공중 항모 지휘 오라(14m)는 BuildHelicarrierVisual의 회전 전술 오라 링(CarrierAuraRing)으로 깔끔하게 표시한다(흩어진 청록 점 제거).
    }

    Vector3 FindSiloPosition(Frame frame, PlayerRef owner, Vector3 fallback) {
      if (frame == null) {
        return fallback;
      }
      var buildings = frame.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out _, out var t, out var b)) {
        if (b->Owner == owner && frame.FindAsset(b->Spec).Role == BuildingRole.UltimateSite) {
          return t->Position.ToUnityVector3();
        }
      }
      return fallback;
    }

    void SpawnFlyingIcbm(Vector3 start, Vector3 velocity, float duration) {
      if (_stage == null) {
        return;
      }
      GameObject go;
      if (VisualCatalog.TryFilled(VisualCatalog.FxId("icbm_rocket"), out var mSlot) && mSlot.Prefab != null) {
        go = Instantiate(mSlot.Prefab, start, Quaternion.LookRotation(velocity.sqrMagnitude > 0.001f ? velocity.normalized : Vector3.up), _stage);
        go.name = "FlyingICBM";
        go.transform.localScale = Vector3.one * (mSlot.Scale > 0f ? mSlot.Scale : 2.2f);
      } else {
        go = new GameObject("FlyingICBM", typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(_stage, false);
        go.transform.position = start;
        go.transform.rotation = velocity.y >= 0f ? Quaternion.identity : Quaternion.Euler(180f, 0f, 0f);
        var kit = new MeshKit();
        kit.Paint(Swatch.White).Cylinder(new Vector3(0f, 1.4f, 0f), 0.42f, 2.8f, 10);
        kit.Paint(Swatch.MetalDark).Cylinder(new Vector3(0f, 0.3f, 0f), 0.46f, 0.6f, 10);
        kit.Paint(Swatch.GlowRed).Cone(new Vector3(0f, 3.25f, 0f), 0.44f, 0.95f, 10);
        go.GetComponent<MeshFilter>().sharedMesh = kit.ToMesh("icbm_rocket");
        go.GetComponent<MeshRenderer>().sharedMaterial = ArtLook.NeutralMaterial;
      }
      _flyingMissiles.Add((go, velocity, Time.time + duration, 0f));
    }

    void UpdateSuperweaponVisuals(float dt) {
      for (var i = _flyingMissiles.Count - 1; i >= 0; i--) {
        var m = _flyingMissiles[i];
        if (m.Rocket == null || Time.time >= m.Until) {
          if (m.Rocket != null) {
            Destroy(m.Rocket);
          }
          _flyingMissiles.RemoveAt(i);
          continue;
        }
        m.Rocket.transform.position += m.Velocity * dt;
        if (Time.time >= m.NextTrail) {
          var tail = m.Rocket.transform.position - m.Velocity.normalized * 0.6f;
          Fx.MuzzleFlash(tail, -m.Velocity.normalized, 1.1f);
          Fx.Signal(tail);
          m.NextTrail = Time.time + 0.06f;
        }
        _flyingMissiles[i] = m;
      }
      for (var i = _crashingCarriers.Count - 1; i >= 0; i--) {
        var c = _crashingCarriers[i];
        if (c.Root == null) {
          _crashingCarriers.RemoveAt(i);
          continue;
        }
        c.Elapsed += dt;
        var t = Mathf.Clamp01(c.Elapsed / 3f);
        if (c.Turn != null) {
          c.Turn.localPosition = Vector3.Lerp(c.StartPos, Vector3.up * 0.4f, t * t);
          c.Turn.localRotation = Quaternion.Euler(28f * t, c.Elapsed * 55f, 42f * t);
        }
        c.Root.transform.position += c.Root.transform.forward * (3.2f * (1f - 0.5f * t) * dt);
        if (c.Elapsed >= c.NextSmoke) {
          c.NextSmoke = c.Elapsed + 0.16f;
          var smokePos = c.Turn != null ? c.Turn.position : c.Root.transform.position;
          Fx.Explosion(smokePos + Random.insideUnitSphere * 1.4f, 0.9f);
        }
        if (c.Elapsed >= 3f) {
          Fx.MushroomExplosion(c.Root.transform.position, 4.8f);
          Destroy(c.Root);
          _crashingCarriers.RemoveAt(i);
          continue;
        }
        _crashingCarriers[i] = c;
      }
    }

    static float Wave(float time, float speed, float phase) => 0.5f + 0.5f * Mathf.Sin(time * speed * Mathf.PI * 2f + phase);

    static void Animate(Actor actor, float dt, bool powered, bool busy) {
      foreach (var (part, axis, speed, needsPower) in actor.Spinners) {
        if (needsPower == false || powered) {
          part.Rotate(axis, speed * dt, Space.Self);
        }
      }
      if (busy) {
        var time = Time.time;
        foreach (var (part, home, reach, speed) in actor.Sliders) {
          part.localPosition = home + reach * Wave(time, speed, actor.Phase);
        }
        foreach (var (part, home, swing, speed) in actor.Swingers) {
          part.localRotation = home * Quaternion.Euler(0, (Wave(time, speed, actor.Phase) - 0.5f) * 2f * swing, 0);
        }
      }
    }

    static float YawTo(Vector3 from, Vector3 to) => Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg;

    static bool TryTarget(Frame frame, EntityRef target, out Vector3 position, out bool air) {
      air = false;
      position = default;
      if (target == EntityRef.None || frame.Exists(target) == false || frame.Has<Transform3D>(target) == false) {
        return false;
      }
      position = frame.Get<Transform3D>(target).Position.ToUnityVector3();
      if (frame.Unsafe.TryGetPointer<Unit>(target, out var unit) && frame.FindAsset(unit->Spec).IsAir) {
        air = true;
      }
      position.y = air ? 3f : 0.8f;
      return true;
    }

    void Aim(Actor actor, bool hasTarget, Vector3 target, bool air, float dt) {
      if (actor.Turret != null) {
        var want = hasTarget ? Mathf.DeltaAngle(actor.HullYaw, YawTo(actor.Position, target)) : 0f;
        var turned = Quaternion.RotateTowards(Quaternion.Euler(0f, actor.TurretYaw, 0f), Quaternion.Euler(0f, want, 0f), TurretRate * dt);
        actor.TurretYaw = turned.eulerAngles.y;
        actor.Turret.localRotation = turned;
      }
      if (actor.Barrel != null) {
        actor.Recoil = Mathf.MoveTowards(actor.Recoil, 0f, RecoilRecover * dt);
        actor.Barrel.localPosition = actor.BarrelHome + Vector3.back * actor.Recoil;
        // 대공: 공중 목표를 노리면 포신을 크게 들고, 평소에도 살짝 들어 둔다.
        var lift = hasTarget && air ? AirElevation : actor.AntiAir ? AntiAirIdle : 0f;
        actor.Elevation = Mathf.MoveTowards(actor.Elevation, lift, ElevationRate * dt);
        actor.Barrel.localRotation = actor.BarrelRest * Quaternion.Euler(-actor.Elevation, 0f, 0f);
      }
      if (actor.Pending && (Aligned(actor, actor.PendingShot.Target) || Time.time - actor.PendingAt > AimMaxWait)) {
        actor.Pending = false;
        Shoot(actor, actor.PendingShot.Attack, actor.PendingShot.Range, true, actor.PendingShot.Target, actor.PendingShot.Flame);
      }
    }

    bool Aligned(Actor actor, Vector3 target) => actor.Turret == null || Mathf.Abs(Mathf.DeltaAngle(actor.TurretYaw, Mathf.DeltaAngle(actor.HullYaw, YawTo(actor.Position, target)))) <= AimTolerance;

    /// <summary>포탑이 목표를 보고 있으면 바로 쏘고, 아니면 돌 때까지(최대 0.6초) 미룬다.</summary>
    void Fire(Actor actor, AttackType attack, float range, bool hasTarget, Vector3 target, bool flame) {
      if (hasTarget && Aligned(actor, target) == false) {
        actor.Pending = true;
        actor.PendingAt = Time.time;
        actor.PendingShot = (attack, range, target, flame);
        return;
      }
      Shoot(actor, attack, range, hasTarget, target, flame);
    }

    /// <summary>소총을 든 분대원 수(화면 전용, 테스트용).</summary>
    public int RifleRigs { get { var n = 0; foreach (var a in _actors.Values) { n += a.Rigs.Count; } return n; } }

    /// <summary>표적을 겨누고 있는 분대원 수.</summary>
    public int AimingRigs { get { var n = 0; foreach (var a in _actors.Values) { foreach (var r in a.Rigs) { n += r.Aiming ? 1 : 0; } } return n; } }

    public int PendingShots { get { var n = 0; foreach (var a in _actors.Values) { if (a.Pending) { n++; } } return n; } }

    /// <summary>보병 병과별 전용 Synty 무기 선택(저격총 · 대전차 RPG · 돌격 유탄발사기 · 공병 카빈 · 돌격소총).</summary>
    public static DressKind WeaponKindFor(string unitId) {
      if (string.IsNullOrEmpty(unitId)) return DressKind.Rifle;
      if (unitId.Contains("sniper")) return DressKind.SniperRifle;
      if (unitId.Contains("at_rocket")) return DressKind.RocketLauncher;
      if (unitId.Contains("breacher") || unitId.Contains("flame")) return DressKind.BreacherWeapon;
      return DressKind.Rifle;
    }

    /// <summary>보병 무기 프리팹(팩 소총·RPG·유탄·카빈). 칸이 비어 있으면 기본 소총으로 폴백.</summary>
    public static GameObject Weapon(DressKind kind) {
      var entry = Dressing.Map != null ? Dressing.Map.Get(kind) : null;
      if (entry != null && entry.Variants.Count > 0) {
        return entry.Variants[0].Prefab;
      }
      if (kind != DressKind.Rifle && kind != DressKind.RocketLauncher && Dressing.Map != null) {
        var fallback = Dressing.Map.Get(DressKind.Rifle);
        if (fallback != null && fallback.Variants.Count > 0) {
          return fallback.Variants[0].Prefab;
        }
      }
      return null;
    }

    public static List<RiflemanRig> AttachInfantryWeapon(GameObject model, string unitId) {
      if (unitId.EndsWith("_engineer")) {
        return new List<RiflemanRig>();
      }
      var weapon = WeaponKindFor(unitId);
      return RiflemanRig.AttachAll(model, Weapon(weapon), weapon == DressKind.RocketLauncher);
    }

    public const float NeedleWidth = 0.035f;

    static Color? TracerColor(Actor actor) {
      if (actor.UpgradeMark <= 1) {
        return null;
      }
      if (actor.UpgradeMark == 2) {
        return new Color(1f, 0.48f, 0.1f, 1f);
      }
      return actor.IsFreeFaction ? new Color(0.2f, 0.94f, 1f, 1f) : new Color(1f, 0.23f, 0.19f, 1f);
    }

    void Shoot(Actor actor, AttackType attack, float range, bool hasTarget, Vector3 target, bool flame) {
      if (actor.View.gameObject.activeSelf && flame == false) {
        var weapon = WeaponKindFor(actor.View.Entry.Id);
        var sound = weapon == DressKind.SniperRifle ? SoundEvent.SniperFire
          : weapon == DressKind.RocketLauncher || attack == AttackType.AntiAir || attack == AttackType.Area ? SoundEvent.RocketFire
          : attack == AttackType.AntiArmor ? SoundEvent.TankCannon : SoundEvent.RifleFire;
        _audio.NoteCombat(GameAudio.PlayAt(sound, actor.View.Turn.position));
      }
      actor.View.Animation?.Fire();
      actor.Recoil = RecoilDistance;
      var tracerTint = TracerColor(actor);
      if (actor.Rigs.Count > 0 && flame == false) {
        // 보병: 분대원마다 소총 총구에서 섬광과 가는 예광탄(반동은 RiflemanRig가 그린다).
        foreach (var rig in actor.Rigs) {
          rig.Fire();
          var tip = rig.Muzzle;
          var aim = hasTarget ? (target - tip).normalized : rig.MuzzleForward;
          Fx.MuzzleFlash(tip, aim, 0.45f);
          if (hasTarget) {
            var spread = new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(-0.05f, 0.25f), Random.Range(-0.3f, 0.3f));
            Fx.Needle(tip, target + spread, BulletSpeed, tracerTint);
          }
        }
        if (hasTarget) {
          Fx.Impact(target, attack == AttackType.AntiInfantry ? 0.6f : 1.2f, target - actor.View.transform.position);
        }
        return;
      }
      if (attack == AttackType.AntiArmor || attack == AttackType.Area) {
        actor.Rock = 1f;
      }
      var muzzle = actor.Muzzle.TransformPoint(actor.View.Entry.Muzzle);
      var forward = hasTarget ? (target - muzzle).normalized : actor.Muzzle.forward;
      if (flame) {
        Fx.FlameJet(muzzle, forward, range);
        return;
      }
      Fx.MuzzleFlash(muzzle, forward, attack == AttackType.AntiInfantry ? 0.8f : 1.5f);
      if (hasTarget == false) {
        return;
      }
      var missile = attack == AttackType.AntiAir || attack == AttackType.Area;
      var speed = attack == AttackType.AntiInfantry ? BulletSpeed : missile ? MissileSpeed : ShellSpeed;
      if (attack == AttackType.AntiInfantry) {
        // 소총·기관총: 예광탄 3발 연사, 약간 흩어짐.
        for (var i = 0; i < 3; i++) {
          var spread = new Vector3(Random.Range(-0.35f, 0.35f), Random.Range(-0.1f, 0.2f), Random.Range(-0.35f, 0.35f));
          Fx.Needle(muzzle + forward * (0.3f * i), target + spread, speed, tracerTint);
        }
      } else if (missile) {
        // 미사일: 위로 솟았다가 휘어 내려가는 비행, 배기 불꽃과 연기 꼬리, 도착할 때 착탄.
        Fx.LaunchMissile(muzzle, target, speed, 1.6f);
        return;
      } else {
        Fx.Tracer(muzzle, target, speed, 0.34f, false, tracerTint);
      }
      Fx.Impact(target, attack == AttackType.AntiInfantry ? 0.8f : 1.6f, target - muzzle);
      if (attack == AttackType.Area) {
        // 크레이터는 확정 이벤트(AreaImpact)로 손상 지도가 만든다(항목 27 K-2). 예측 사격으로는 만들지 않는다.
      }
    }

    void UpdateUnit(Frame frame, EntityRef entity, Transform3D* transform3D, Unit* unit, float dt) {
      var spec = frame.FindAsset(unit->Spec);
      if (_actors.TryGetValue(entity, out var actor) == false) {
        actor = Create(entity, spec.name, false, spec.IsAir);
        if (actor == null) {
          return;
        }
        var start = transform3D->Position.ToUnityVector3();
        start.y = 0f;
        actor.Position = start;
        actor.View.transform.position = start;
        if (spec.IsNaval) {
          var facing = (transform3D->Rotation * new FPVector3(FP._0, FP._0, FP._1)).ToUnityVector3();
          actor.HullYaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
        }
        if (spec.IsVehicle == false && spec.IsAir == false && spec.IsNaval == false && spec.Size == UnitSize.Small) {
          actor.Rigs.AddRange(AttachInfantryWeapon(actor.View.Model, spec.name));
          actor.View.Animation?.EnableMixamoInfantry();
        } else if (spec.Size == UnitSize.Engineer) {
          actor.EngineerWork = ConstructionWorkView.Attach(actor.View.Model, _catalog.IronExtractorDrill);
        } else {
          AttachVehicleCrew(actor, spec);
        }
      }
      var color = frame.Global->Players.GetPointer(unit->Owner)->ColorIndex;
      if (actor.LastUnitHealth > unit->Health) actor.View.Animation?.Hit();
      actor.LastUnitHealth = unit->Health;
      if (color != actor.Color) {
        actor.Color = color;
        ArtLook.Apply(actor.View.Model, color);
      }
      // 탑승 중인 승객 유닛은 수송기 내부에 들어가므로 월드 모델을 숨긴다.
      if (unit->Transport != EntityRef.None) {
        actor.EngineerWasVisible = false;
        actor.ExtractorEntryTime = 0f;
        if (actor.View.gameObject.activeSelf) {
          actor.View.gameObject.SetActive(false);
        }
        return;
      }
      // 진지 안: 모델은 숨기고 사격은 건물 창문에서 보인다.
      if (unit->Garrison != EntityRef.None) {
        if (spec.Size == UnitSize.Engineer && DrawExtractorEntry(frame, actor, unit, dt)) {
          return;
        }
        if (actor.View.gameObject.activeSelf) {
          actor.View.gameObject.SetActive(false);
        }
        var inside = unit->AttackCooldown.AsFloat;
        if (inside > actor.Cooldown + 0.05f && frame.Exists(unit->Garrison)) {
          var window = frame.Get<Transform3D>(unit->Garrison).Position.ToUnityVector3() + Vector3.up * 4f;
          var hasAim = TryTarget(frame, unit->TargetEntity, out var shot, out _);
          var aim = hasAim ? (shot - window).normalized : Vector3.forward;
          var muzzle = window + aim * 2.5f;
          Fx.MuzzleFlash(muzzle, aim, 0.7f);
          if (hasAim) {
            Fx.Needle(muzzle, shot + Vector3.up * 1.1f, 95f);
          }
        }
        actor.Cooldown = inside;
        return;
      }
      // 고지대 은폐: 적에게 숨은 유닛은 그리지 않고, 내 숨은 유닛은 점선 고리로 알린다.
      var viewer = _controller != null ? _controller.Player : PlayerRef.None;
      var visible = viewer.IsValid == false || FactoryWarsUtil.IsVisibleTo(frame, viewer, entity);
      actor.EngineerWasVisible = spec.Size == UnitSize.Engineer && visible;
      actor.ExtractorEntryTime = 0f;
      if (actor.View.gameObject.activeSelf != visible) {
        actor.View.gameObject.SetActive(visible);
      }
      var hidden = unit->Owner == viewer && FactoryWarsUtil.MapInfo(frame).HighGroundAt(transform3D->Position) >= 0 && OthersCannotSee(frame, entity, unit->Owner);
      actor.View.SetHidden(hidden);

      var goal = transform3D->Position.ToUnityVector3();
      goal.y = 0f;
      var moved = goal - actor.Position;
      var before = actor.Position;
      actor.Position += moved * (1f - Mathf.Exp(-VisualProfile.Current.FollowRate * dt));
      var assemblingCarrier = actor.IsCarrier && frame.Unsafe.TryGetPointer<Carrier>(entity, out var cPtr) && cPtr->Assembling;
      var hover = (spec.IsAir && assemblingCarrier == false) ? Mathf.Sin(Time.time * 1.6f + actor.Phase) * 0.18f : 0f;
      var navalBob = spec.IsNaval ? Mathf.Sin(Time.time * 1.45f + actor.Phase) * 0.08f + 0.06f : 0f;
      actor.View.transform.position = (spec.IsAir && assemblingCarrier == false) ? actor.Position : actor.Position + Vector3.up * TerrainKit.SurfaceHeight(World.Map, actor.Position.x, actor.Position.z);
      if (spec.IsNaval) actor.View.transform.position = actor.Position + Vector3.up * (TerrainKit.WaterLevel + navalBob);
      actor.IsBomber = spec.name == "ff_skyfortress_bomber";
      if (actor.IsBomber && actor.BomberScaled == false && actor.View.Model != null) {
        actor.View.Model.transform.localScale *= 1.35f;
        actor.BomberScaled = true;
      }
      var baseAirAlt = spec.IsAir
        ? Mathf.Max(actor.View.Entry.Altitude, actor.IsBomber ? 25.5f : spec.Size == UnitSize.Large ? 22.0f : spec.Size == UnitSize.Medium ? 19.5f : 16.5f)
        : actor.View.Entry.Altitude;
      var altitude = assemblingCarrier ? 0.35f : (actor.IsCarrier ? Mathf.Max(baseAirAlt, 27.0f) : baseAirAlt);
      actor.View.Turn.localPosition = Vector3.up * (altitude + hover);
      if (spec.IsNaval) UpdateNavalWake(actor, spec, before, dt);
      if (spec.IsAir) UpdateAirFlight(actor, spec, before, dt, assemblingCarrier == false);
      if (spec.IsVehicle || spec.IsNaval || spec.IsAir || spec.Size == UnitSize.Medium || spec.Size == UnitSize.Large) {
        var hpRatio = spec.MaxHealth > FP._0 ? (unit->Health / spec.MaxHealth).AsFloat : 1f;
        UpdateDamageFire(actor, hpRatio, spec.IsNaval ? 1.5f : spec.Size == UnitSize.Large ? 1.25f : 0.9f);
      }
      if (spec.IsAir && actor.View.Bar != null) {
        actor.View.Bar.transform.localPosition = Vector3.up * (altitude + (actor.IsCarrier ? 4.4f : actor.IsBomber ? 4.2f : 2.8f));
      }
      if (visible && assemblingCarrier == false) {
        if (spec.IsAir) {
          var rotor = spec.name.EndsWith("_gunship") || spec.name.EndsWith("_heli") || actor.IsCarrier;
          _audio.Engine(rotor ? SoundEvent.RotorEngine : SoundEvent.AirEngine, actor.View.Turn.position, rotor ? 0.72f : 0.86f);
        } else if (spec.IsNaval == false && dt > 0f && (spec.IsVehicle || spec.Size == UnitSize.Medium || spec.Size == UnitSize.Large)) {
          var speedRatio = (actor.Position - before).magnitude / (dt * Mathf.Max(1f, spec.MoveSpeed.AsFloat));
          _audio.Engine(SoundEvent.GroundEngine, actor.View.transform.position, Mathf.InverseLerp(0.06f, 0.8f, speedRatio));
        }
      }
      if (actor.IsCarrier && actor.View.Bar != null) {
        actor.View.Bar.transform.localPosition = Vector3.up * (altitude + 4.4f);
      }

      var hasTarget = TryTarget(frame, unit->TargetEntity, out var target, out var air);
      actor.InCombat = hasTarget || (unit->LastFiredFrame > 0 && frame.Number - unit->LastFiredFrame < 150);
      Vector3 workTarget = default;
      var workingAtSite = frame.Unsafe.TryGetPointer<Worker>(entity, out var engineer)
        && TryEngineerWorkTarget(frame, transform3D->Position, engineer, out workTarget);
      if (actor.EngineerWork != null) {
        var gathering = engineer != null && engineer->Task == WorkerTask.Gather && engineer->WorkTimer > FP._0;
        actor.EngineerWork.Working = workingAtSite || gathering;
        actor.EngineerWork.WorkPose = gathering ? "Dig" : "Device";
      }
      if (workingAtSite && engineer->Task == WorkerTask.Build) _workingSites.Add(engineer->Target);
      foreach (var rig in actor.Rigs) {
        rig.Aiming = hasTarget && actor.View.Animation?.IsActing != true;
        rig.PreserveGesturePose = actor.View.Animation?.IsActing == true;
        rig.AimPoint = target;
      }
      if (workingAtSite) {
        actor.HullYaw = Mathf.MoveTowardsAngle(actor.HullYaw, YawTo(actor.Position, workTarget), TurnRate * dt);
      } else if (moved.sqrMagnitude > 0.002f) {
        actor.HullYaw = Mathf.MoveTowardsAngle(actor.HullYaw, Mathf.Atan2(moved.x, moved.z) * Mathf.Rad2Deg, TurnRate * dt);
      } else if (hasTarget && actor.Turret == null) {
        actor.HullYaw = Mathf.MoveTowardsAngle(actor.HullYaw, YawTo(actor.Position, target), TurnRate * dt);
      }
      actor.View.SetYaw(actor.HullYaw);
      Tilt(actor, spec, before, dt);
      actor.AntiAir = spec.Attack == AttackType.AntiAir;
      Aim(actor, hasTarget, target, air, dt);
      UpdateShield(actor, unit->InCover);
      UpdateVetBadge(actor, unit->VetRank);
      UpdateUpgradeVisual(frame, actor, unit, spec);
      UpdateTransportVisual(frame, actor, entity, unit, spec, dt);

      var cooldown = unit->AttackCooldown.AsFloat;
      if (cooldown > actor.Cooldown + 0.05f && spec.Attack != AttackType.None) {
        Fire(actor, spec.Attack, spec.Range.AsFloat, hasTarget, target, spec.Ability == SpecialAbility.FlameBurst || spec.Ability == SpecialAbility.GarrisonBreach);
      }
      actor.Cooldown = cooldown;

      // 공중 항모 체력 50% 미만 로터 손상 연출: VTOL 터빈에서 검은 연기·스파크 분출 및 로터 회전 저하.
      if (actor.IsCarrier && UltimateSystem.IsCarrierRotorDamaged(frame, entity, unit)) {
        Animate(actor, dt * 0.45f, true, false);
        if (Time.time >= _rotorSmokeAt) {
          _rotorSmokeAt = Time.time + 0.14f;
          var basePos = actor.View.Turn.position;
          Fx.Explosion(basePos + actor.View.Turn.right * 2.2f + Vector3.up * 0.4f, 0.55f);
          Fx.Impact(basePos - actor.View.Turn.right * 2.2f + Vector3.up * 0.4f, 0.7f, Vector3.down);
        }
      } else {
        var working = frame.Unsafe.TryGetPointer<Worker>(entity, out var worker) && worker->Task != WorkerTask.Idle && worker->Task != WorkerTask.Deliver;
        Animate(actor, dt, true, working);
      }
      if (actor.View.Animation != null) {
        var workingAnim = frame.Unsafe.TryGetPointer<Worker>(entity, out var wAnim) && wAnim->Task != WorkerTask.Idle && wAnim->Task != WorkerTask.Deliver;
        var speed = dt > 0f ? (actor.Position - before).magnitude / dt : 0f;
        actor.View.Animation.Cover = unit->InCover;
        actor.View.Animation.Prone = unit->StealthUntilFrame > frame.Number;
        actor.View.Animation.CombatAlert = actor.InCombat;
        actor.View.Animation.SprintUnlocked = TechSystem.HasSprintUpgrade(frame, unit->Owner, spec);
        actor.View.Animation.Set(AnimState(speed, hasTarget, workingAnim ? wAnim->Task : WorkerTask.Idle), speed);
      }
    }

    bool DrawExtractorEntry(Frame frame, Actor actor, Unit* unit, float dt) {
      var site = unit->Garrison;
      if (frame.TryGet<Building>(site, out var building) == false || frame.FindAsset(building.Spec).RequiresResourceNode == false) {
        return false;
      }
      var viewer = _controller != null ? _controller.Player : PlayerRef.None;
      if (viewer.IsValid && FactoryWarsUtil.IsVisibleTo(frame, viewer, site) == false) {
        actor.EngineerWasVisible = false;
        actor.ExtractorEntryTime = 0f;
        return false;
      }
      const float entrySeconds = 0.65f;
      if (actor.EngineerWasVisible) {
        actor.EngineerWasVisible = false;
        actor.ExtractorEntryTime = entrySeconds;
        if (actor.EngineerWork != null) actor.EngineerWork.Working = false;
      }
      if (actor.ExtractorEntryTime <= 0f) return false;

      actor.ExtractorEntryTime -= dt;
      _enteringExtractorSites.Add(site);
      var center = frame.Get<Transform3D>(site).Position.ToUnityVector3();
      center.y = 0f;
      var next = Vector3.MoveTowards(actor.Position, center, 4f * dt);
      actor.HullYaw = Mathf.MoveTowardsAngle(actor.HullYaw, YawTo(actor.Position, center), TurnRate * dt);
      actor.View.SetYaw(actor.HullYaw);
      actor.View.Animation?.Set(UnitAnimState.Move, 4f);
      actor.Position = next;
      actor.View.transform.position = next + Vector3.up * TerrainKit.SurfaceHeight(World.Map, next.x, next.z);
      return true;
    }

    /// <summary>
    /// 항목 32: 차량은 가감속에 따라 앞뒤로, 헬기·비행기는 선회 방향으로 옆으로 기울고 속도만큼 앞으로 숙인다. 보병·배는 그대로.
    /// 값은 VisualProfile(화면 전용). 시뮬레이션은 읽기만 한다.
    /// </summary>
    void Tilt(Actor actor, UnitSpec spec, Vector3 before, float dt) {
      if (dt <= 0f || (spec.Size == UnitSize.Small && spec.IsVehicle == false && spec.IsAir == false && spec.IsNaval == false) || spec.Size == UnitSize.Engineer) {
        return;
      }
      var profile = VisualProfile.Current;
      var velocity = (actor.Position - before) / dt;
      var accel = (velocity - actor.Velocity) / dt;
      actor.Velocity = Vector3.Lerp(actor.Velocity, velocity, 1f - Mathf.Exp(-8f * dt));
      var forward = Quaternion.Euler(0, actor.HullYaw, 0) * Vector3.forward;
      float pitch, roll = 0f;
      if (spec.IsNaval) {
        // Subtle wave buoyancy pitch/roll + slight bow lift under headway (preserves bridge vertical clearance).
        var speed = Mathf.Max(0f, Vector3.Dot(actor.Velocity, forward));
        pitch = Mathf.Sin(Time.time * 1.15f + actor.Phase * 1.3f) * 1.2f - Mathf.Clamp(speed * 0.25f, 0f, 1.5f);
        roll = Mathf.Sin(Time.time * 0.95f + actor.Phase * 0.7f) * 1.9f;
      } else if (spec.IsAir) {
        var speed = Vector3.Dot(actor.Velocity, forward);
        var right = Quaternion.Euler(0, actor.HullYaw, 0) * Vector3.right;
        var turn = Vector3.Dot(accel, right);
        pitch = Mathf.Clamp(speed * profile.AirPitchPerSpeed, -profile.AirMaxBank * 0.5f, profile.AirMaxBank * 0.5f);
        roll = Mathf.Clamp(-turn * profile.AirBankPerTurn * 4f, -profile.AirMaxBank, profile.AirMaxBank);
      } else {
        // 출발하면 뒤로, 멈추면 앞으로 숙는다.
        pitch = Mathf.Clamp(-Vector3.Dot(accel, forward) * profile.VehicleTiltPerAccel, -profile.VehicleMaxTilt, profile.VehicleMaxTilt);
      }
      var k = 1f - Mathf.Exp(-profile.TiltRate * dt);
      actor.Pitch = Mathf.Lerp(actor.Pitch, pitch, k);
      actor.Roll = Mathf.Lerp(actor.Roll, roll, k);
      // 포 발사 반동: 차체가 뒤로 4도 들렸다가 0.25초에 돌아온다.
      actor.Rock = Mathf.MoveTowards(actor.Rock, 0f, dt * 4f);
      actor.View.Turn.localRotation = Quaternion.Euler(0, actor.HullYaw, 0) * Quaternion.Euler(actor.Pitch - actor.Rock * 4f, 0, actor.Roll);
    }

    /// <summary>항목 32: 엄폐 중인 보병 머리 위 작은 방패.</summary>
    void UpdateShield(Actor actor, bool inCover) {
      if (inCover && actor.Shield == null) {
        actor.Shield = new GameObject("CoverShield");
        actor.Shield.transform.SetParent(actor.View.transform, false);
        actor.Shield.transform.localPosition = Vector3.up * (actor.View.Entry.BarHeight + 0.9f);
        actor.Shield.transform.localScale = Vector3.one * 0.9f;
        var sprite = actor.Shield.AddComponent<SpriteRenderer>();
        sprite.sprite = Ui.IconSprite("ui_armor");
      }
      if (actor.Shield != null) {
        if (actor.Shield.activeSelf != inCover) {
          actor.Shield.SetActive(inCover);
        }
        var camera = Camera.main;
        if (inCover && camera != null) {
          actor.Shield.transform.rotation = camera.transform.rotation;
        }
      }
    }

    /// <summary>항목 37 베테랑 계급 배지(★~★★★): 승급한 유닛 머리 위에 금색 별을 띄운다.</summary>
    void UpdateVetBadge(Actor actor, int rank) {
      if (rank <= 0) {
        if (actor.VetBadge != null && actor.VetBadge.gameObject.activeSelf) {
          actor.VetBadge.gameObject.SetActive(false);
        }
        return;
      }
      if (actor.VetBadge == null) {
        var go = new GameObject("VetBadge");
        go.transform.SetParent(actor.View.transform, false);
        go.transform.localPosition = Vector3.up * (actor.View.Entry.BarHeight + 0.55f);
        var tm = go.AddComponent<TextMesh>();
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.fontSize = 36;
        tm.characterSize = 0.14f;
        tm.color = new Color(1f, 0.84f, 0.16f, 1f);
        tm.fontStyle = FontStyle.Bold;
        actor.VetBadge = tm;
      }
      if (actor.VetRank != rank) {
        actor.VetRank = rank;
        actor.VetBadge.text = rank == 1 ? "★" : rank == 2 ? "★★" : "★★★";
      }
      if (actor.VetBadge.gameObject.activeSelf == false) {
        actor.VetBadge.gameObject.SetActive(true);
      }
      var camera = Camera.main;
      if (camera != null) {
        actor.VetBadge.transform.rotation = camera.transform.rotation;
      }
    }

    /// <summary>Mk.1 -> Mk.2 -> Mk.3 시각 진화. 보병은 승인된 Option A(Spine_03 밀착 저프로파일 전술 팩 + 진영 어깨 패치 + 선택 시 체력바 옆 단일 셰브론 ^).</summary>
    void UpdateUpgradeVisual(Frame frame, Actor actor, Unit* unit, UnitSpec spec) {
      if (spec.Size == UnitSize.Engineer || actor.IsCarrier) {
        return;
      }
      var mark = TechSystem.UnitMark(frame, unit->Owner, spec);
      var infantry = spec.Size == UnitSize.Small && spec.IsVehicle == false && spec.IsAir == false && spec.IsNaval == false;
      var sprintUnlocked = infantry && TechSystem.HasSprintUpgrade(frame, unit->Owner, spec);
      actor.IsFreeFaction = TechSystem.FactionIdOf(frame, unit->Owner) == "free_federation";
      if (mark != actor.UpgradeMark) {
        actor.UpgradeMark = mark;
        if (actor.UpgradeKit != null) {
          Destroy(actor.UpgradeKit);
          actor.UpgradeKit = null;
        }
        for (var i = 0; i < actor.UpgradeAttachments.Count; i++) {
          if (actor.UpgradeAttachments[i] != null) Destroy(actor.UpgradeAttachments[i]);
        }
        actor.UpgradeAttachments.Clear();
        if (mark >= 2) {
          if (infantry) {
            // Option A (승인안): 밝은 직사각형 블록 제거 -> Spine_03 밀착 저프로파일 전술 팩(군복 톤 매칭) + 소형 진영색 어깨 패치.
            AttachInfantryOptionAPack(actor, actor.IsFreeFaction);
          } else {
            var kit = new MeshKit();
            var glow = actor.IsFreeFaction ? Swatch.GlowCyan : Swatch.GlowRed;
            var s = spec.Size == UnitSize.Large ? 1.45f : spec.Size == UnitSize.Medium ? 1.05f : 0.72f;
            var y = spec.IsAir ? 0.25f : 0.52f * s;
            // 기갑·차량·항공 Mk.2: 반응장갑 사이드 스커트 + 연장 포신 덮개(공유 로직 보존).
            kit.Paint(Swatch.ArmorDark).Chamfer(new Vector3(-0.98f * s, y, 0f), new Vector3(0.22f * s, 0.44f * s, 1.9f * s), 0.05f);
            kit.Paint(Swatch.ArmorDark).Chamfer(new Vector3(0.98f * s, y, 0f), new Vector3(0.22f * s, 0.44f * s, 1.9f * s), 0.05f);
            kit.Paint(Swatch.MetalLight).Box(new Vector3(0f, y + 0.42f * s, 0.95f * s), new Vector3(0.28f * s, 0.22f * s, 0.95f * s));
            if (mark >= 3) {
              // Mk.3: 진영 발광 에너지 라인 + 이중 연장 포열/견부 중장갑.
              kit.Paint(glow).Box(new Vector3(-1.11f * s, y + 0.04f, 0f), new Vector3(0.06f * s, 0.14f * s, 1.75f * s));
              kit.Paint(glow).Box(new Vector3(1.11f * s, y + 0.04f, 0f), new Vector3(0.06f * s, 0.14f * s, 1.75f * s));
              kit.Paint(Swatch.MetalLight).Cylinder(new Vector3(-0.22f * s, y + 0.46f * s, 1.35f * s), 0.08f * s, 0.95f * s, 8);
              kit.Paint(Swatch.MetalLight).Cylinder(new Vector3(0.22f * s, y + 0.46f * s, 1.35f * s), 0.08f * s, 0.95f * s, 8);
              kit.Paint(glow).Box(new Vector3(0f, y + 0.56f * s, 1.1f * s), new Vector3(0.36f * s, 0.08f * s, 0.65f * s));
            }
            var go = new GameObject($"UpgradeKit_Mk{mark}", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(actor.View.Model.transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = kit.ToMesh($"upgrade_mk{mark}_{spec.name}");
            go.GetComponent<MeshRenderer>().sharedMaterial = ArtLook.NeutralMaterial;
            ArtLook.Apply(go, actor.Color, true);
            actor.UpgradeKit = go;
          }
        }
      }
      var showInfantryChevron = infantry && (sprintUnlocked || mark >= 2) && _marked.Contains(actor);
      var showVehicleBadge = !infantry && mark >= 2;
      if (!showInfantryChevron && !showVehicleBadge) {
        if (actor.UpgradeChevron != null && actor.UpgradeChevron.gameObject.activeSelf) {
          actor.UpgradeChevron.gameObject.SetActive(false);
        }
        return;
      }
      if (actor.UpgradeChevron == null) {
        var go = new GameObject("UpgradeChevron");
        go.transform.SetParent(actor.View.transform, false);
        var tm = go.AddComponent<TextMesh>();
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.fontSize = 32;
        tm.fontStyle = FontStyle.Bold;
        actor.UpgradeChevron = tm;
      }
      if (infantry) {
        // 보병 기동 업그레이드: 상시 "Mk.2" 라벨 제거, 선택 시에만 체력바 옆 단일 소형 셰브론("^") 표시.
        actor.UpgradeChevron.text = "^";
        actor.UpgradeChevron.characterSize = 0.085f;
        actor.UpgradeChevron.color = actor.IsFreeFaction ? new Color(0.42f, 0.86f, 1f, 1f) : new Color(1f, 0.72f, 0.36f, 1f);
        var barY = actor.View.Bar != null ? actor.View.Bar.transform.localPosition.y : actor.View.Entry.BarHeight;
        actor.UpgradeChevron.transform.localPosition = new Vector3(0.76f, barY, 0f);
      } else {
        actor.UpgradeChevron.text = mark >= 3 ? "^^ Mk.3" : "^ Mk.2";
        actor.UpgradeChevron.characterSize = 0.11f;
        actor.UpgradeChevron.color = mark >= 3 ? new Color(1f, 0.84f, 0.2f, 1f) : new Color(0.86f, 0.91f, 0.97f, 1f);
        actor.UpgradeChevron.transform.localPosition = Vector3.up * (actor.View.Entry.BarHeight + (actor.VetRank > 0 ? 0.92f : 0.44f));
      }
      if (actor.UpgradeChevron.gameObject.activeSelf == false) {
        actor.UpgradeChevron.gameObject.SetActive(true);
      }
      var cam = Camera.main;
      if (cam != null) {
        actor.UpgradeChevron.transform.rotation = cam.transform.rotation;
      }
    }

    static Material _packMatFf, _packMatCf, _patchMatFf, _patchMatCf;

    static Material MakeUniformMat(string name, Color color) {
      var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
      var mat = new Material(shader) { name = name, hideFlags = HideFlags.HideAndDontSave };
      if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
      if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
      if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.18f);
      return mat;
    }

    void AttachInfantryOptionAPack(Actor actor, bool isFreeFaction) {
      var packPrefab = Resources.Load<GameObject>("FactoryWarsMixamo/TacticalPack");
      var packMat = isFreeFaction
        ? (_packMatFf != null ? _packMatFf : (_packMatFf = MakeUniformMat("TacticalPack_FF", new Color(0.28f, 0.33f, 0.24f, 1f))))
        : (_packMatCf != null ? _packMatCf : (_packMatCf = MakeUniformMat("TacticalPack_CF", new Color(0.36f, 0.33f, 0.26f, 1f))));
      var patchMat = isFreeFaction
        ? (_patchMatFf != null ? _patchMatFf : (_patchMatFf = MakeUniformMat("ShoulderPatch_FF", new Color(0.18f, 0.52f, 0.88f, 1f))))
        : (_patchMatCf != null ? _patchMatCf : (_patchMatCf = MakeUniformMat("ShoulderPatch_CF", new Color(0.78f, 0.26f, 0.20f, 1f))));

      var members = new List<Transform>();
      foreach (var anim in actor.View.Model.GetComponentsInChildren<Animator>(true)) members.Add(anim.transform);
      if (members.Count == 0) members.Add(actor.View.Model.transform);

      foreach (var member in members) {
        var spine = RiflemanRig.Find(member, "Spine_03") ?? RiflemanRig.Find(member, "Spine_02") ?? member;
        GameObject pack;
        if (packPrefab != null) {
          pack = Instantiate(packPrefab, spine, false);
          pack.name = "OptionA_TacticalPack";
          pack.transform.localPosition = new Vector3(0f, 0.03f, -0.15f);
          pack.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
          pack.transform.localScale = Vector3.one * 0.66f;
          foreach (var c in pack.GetComponentsInChildren<Collider>(true)) c.enabled = false;
          foreach (var r in pack.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = packMat;
        } else {
          pack = GameObject.CreatePrimitive(PrimitiveType.Cube);
          pack.name = "OptionA_TacticalPack";
          pack.transform.SetParent(spine, false);
          pack.transform.localPosition = new Vector3(0f, 0.03f, -0.14f);
          pack.transform.localScale = new Vector3(0.24f, 0.30f, 0.11f);
          Destroy(pack.GetComponent<Collider>());
          pack.GetComponent<MeshRenderer>().sharedMaterial = packMat;
        }
        actor.UpgradeAttachments.Add(pack);
        if (actor.UpgradeKit == null) actor.UpgradeKit = pack;

        // 소형 진영색 어깨 패치(좌측 상완/어깨 밀착).
        var upperArmL = RiflemanRig.Find(member, "Shoulder_L") ?? RiflemanRig.Find(member, "UpperArm_L") ?? spine;
        var patch = GameObject.CreatePrimitive(PrimitiveType.Cube);
        patch.name = "OptionA_ShoulderPatch";
        patch.transform.SetParent(upperArmL, false);
        patch.transform.localPosition = upperArmL == spine ? new Vector3(-0.21f, 0.12f, 0.01f) : new Vector3(0f, 0.05f, -0.055f);
        patch.transform.localScale = new Vector3(0.035f, 0.095f, 0.075f);
        Destroy(patch.GetComponent<Collider>());
        patch.GetComponent<MeshRenderer>().sharedMaterial = patchMat;
        actor.UpgradeAttachments.Add(patch);
      }
    }

    /// <summary>교체 모델 애니메이션 상태(항목 30). 시뮬레이션 상태를 읽기만 한다.</summary>
    public static UnitAnimState AnimState(float speed, bool hasTarget, WorkerTask task) {
      if (speed >= 0.3f) {
        return UnitAnimState.Move;
      }
      if (task == WorkerTask.Build || task == WorkerTask.Repair) {
        return UnitAnimState.Build;
      }
      if (task != WorkerTask.Idle && task != WorkerTask.Deliver && speed < 0.3f) {
        return UnitAnimState.Gather;
      }
      return hasTarget ? UnitAnimState.Attack : UnitAnimState.Idle;
    }

    static bool TryEngineerWorkTarget(Frame frame, FPVector3 position, Worker* engineer, out Vector3 target) {
      target = default;
      if ((engineer->Task != WorkerTask.Build && engineer->Task != WorkerTask.Repair)
          || frame.TryGet<Building>(engineer->Target, out var site) == false
          || frame.TryGet<Transform3D>(engineer->Target, out var transform) == false) return false;
      var spec = frame.FindAsset(site.Spec);
      var work = transform.Position;
      var range = spec.FootprintRadius + FactoryWarsUtil.Rules(frame).WorkRange;
      if (engineer->Task == WorkerTask.Build) {
        work = AirfieldWorkSystem.WorkTarget(frame, engineer->Target, &site, spec, work, out range);
      }
      var delta = work - position;
      if (FPMath.Max(FPMath.Abs(delta.X), FPMath.Abs(delta.Z)) > range) return false;
      target = work.ToUnityVector3();
      return true;
    }

    /// <summary>적 중 누구에게도 보이지 않으면 숨은 것이다.</summary>
    static bool OthersCannotSee(Frame frame, EntityRef entity, PlayerRef owner) {
      for (var i = 0; i < frame.PlayerCount; i++) {
        PlayerRef other = i;
        if (other != owner && frame.Global->Players.GetPointer(i)->Active && DiplomacySystem.GetRelation(frame, other, owner) != Relation.Allied
            && FactoryWarsUtil.IsVisibleTo(frame, other, entity)) {
          return false;
        }
      }
      return true;
    }

    static Mesh ScaffoldMesh() {
      if (_scaffold == null) {
        var kit = new MeshKit().Paint(Swatch.ArmorDark);
        kit.Taper(Vector3.zero, new Vector2(5.96f, 5.96f), new Vector2(5.7f, 5.7f), 0.14f);
        kit.Paint(Swatch.Wood);
        foreach (var x in new[] { -2.7f, 2.7f }) {
          foreach (var z in new[] { -2.7f, 2.7f }) {
            kit.Beam(new Vector3(x, 0.1f, z), new Vector3(x, 3.2f, z), 0.12f);
          }
        }
        foreach (var y in new[] { 1.1f, 2.2f, 3.2f }) {
          kit.Beam(new Vector3(-2.7f, y, -2.7f), new Vector3(2.7f, y, -2.7f), 0.09f);
          kit.Beam(new Vector3(-2.7f, y, 2.7f), new Vector3(2.7f, y, 2.7f), 0.09f);
          kit.Beam(new Vector3(-2.7f, y, -2.7f), new Vector3(-2.7f, y, 2.7f), 0.09f);
          kit.Beam(new Vector3(2.7f, y, -2.7f), new Vector3(2.7f, y, 2.7f), 0.09f);
        }
        kit.Paint(Swatch.Hazard);
        kit.Box(new Vector3(0, 0.16f, -2.7f), new Vector3(5.4f, 0.05f, 0.14f));
        _scaffold = kit.ToMesh("scaffold");
      }
      return _scaffold;
    }

    void ClearConstructionCrew(Actor actor) {
      for (var i = 0; i < actor.ConstructionCrew.Count; i++) {
        if (actor.ConstructionCrew[i] != null) {
          Destroy(actor.ConstructionCrew[i]);
        }
      }
      actor.ConstructionCrew.Clear();
      actor.ConstructionWork.Clear();
    }

    void ShowConstruction(Actor actor, float progress, float dt) {
      var building = progress < 1f;
      actor.ConstructionDisplayProgress = actor.ConstructionDisplayProgress < 0f ? progress
        : Mathf.MoveTowards(actor.ConstructionDisplayProgress, progress, dt * 2.75f);
      if (building) {
        var wantStage = progress < 0.33f ? 1 : 2;
        var radius = Mathf.Max(2.4f, actor.FootprintRadius);
        var siteScale = Mathf.Clamp(radius / 4.2f, 0.55f, 1.32f);
        if (actor.ConstructionStage != wantStage) {
          if (actor.Scaffold != null) {
            Destroy(actor.Scaffold);
            actor.Scaffold = null;
          }

          var slotKey = wantStage == 1 ? "const_stage1" : "const_stage2";
          if (VisualCatalog.TryFilled(VisualCatalog.FxId(slotKey), out var sSlot) && sSlot.Prefab != null) {
            var item = Object.Instantiate(sSlot.Prefab, actor.View.transform);
            item.name = wantStage == 1 ? "ConstructionStage1" : "ConstructionStage2";
            item.transform.localPosition = Vector3.zero;
            item.transform.localRotation = Quaternion.identity;
            item.transform.localScale = Vector3.one * (siteScale * (sSlot.Scale > 0f ? sSlot.Scale : 1f));
            foreach (var col in item.GetComponentsInChildren<Collider>(true)) Destroy(col);
            actor.Scaffold = item;
          } else if (VisualCatalog.TryFilled(VisualCatalog.FxId("build_scaffold"), out var fbSlot) && fbSlot.Prefab != null) {
            var item = Object.Instantiate(fbSlot.Prefab, actor.View.transform);
            item.name = wantStage == 1 ? "ConstructionStage1" : "ConstructionStage2";
            item.transform.localPosition = Vector3.zero;
            item.transform.localScale = Vector3.one * (siteScale * (fbSlot.Scale > 0f ? fbSlot.Scale : 0.95f));
            actor.Scaffold = item;
          } else {
            var item = new GameObject(wantStage == 1 ? "ConstructionStage1" : "ConstructionStage2", typeof(MeshFilter), typeof(MeshRenderer));
            item.transform.SetParent(actor.View.transform, false);
            item.GetComponent<MeshFilter>().sharedMesh = ScaffoldMesh();
            item.GetComponent<MeshRenderer>().sharedMaterial = ArtLook.NeutralMaterial;
            actor.Scaffold = item;
          }

          if (EnvironmentArt.Uses(World.Map)) {
            var pad = FindDeep(actor.Scaffold.transform, "FoundationPad");
            if (pad != null) pad.gameObject.SetActive(false);
          }
          // 작업자를 단계 전환 때 재생성하지 않아 같은 동작이 이어진다.
          if (actor.ConstructionCrew.Count == 0 && GraphicsQuality.Current != QualityTier.Low) {
            var crewOffsets = new[] {
              new Vector3(radius * 0.95f + 0.9f, 0f, 1.35f),
              new Vector3(-(radius * 0.95f + 0.9f), 0f, -1.15f),
            };
            for (var c = 0; c < crewOffsets.Length; c++) {
              if (VisualCatalog.TryFilled(VisualCatalog.FxId($"const_worker_{c}"), out var wSlot) && wSlot.Prefab != null) {
                var wGo = Object.Instantiate(wSlot.Prefab, actor.View.transform);
                wGo.name = $"ConstructionCrew_{c}";
                wGo.transform.localPosition = crewOffsets[c];
                var look = -crewOffsets[c];
                look.y = 0f;
                wGo.transform.localRotation = look.sqrMagnitude > 0.001f ? Quaternion.LookRotation(look) : Quaternion.identity;
                wGo.transform.localScale = Vector3.one * (wSlot.Scale > 0f ? wSlot.Scale : 0.85f);
                foreach (var col in wGo.GetComponentsInChildren<Collider>(true)) Destroy(col);
                var anim = wGo.GetComponentInChildren<Animator>(true);
                if (anim != null) {
                  if (anim.runtimeAnimatorController == null && wSlot.Animator != null) {
                    anim.runtimeAnimatorController = wSlot.Animator;
                  }
                  anim.applyRootMotion = false;
                  anim.SetFloat("Speed", 0f);
                }
                actor.ConstructionCrew.Add(wGo);
                var work = ConstructionWorkView.Attach(wGo, _catalog.IronExtractorDrill, 0.43f * c);
                if (work != null) {
                  work.WorkPose = c % 2 == 0 ? "Inspect" : "Device";
                  actor.ConstructionWork.Add(work);
                }
              }
            }
          }
          actor.ConstructionStage = wantStage;
          actor.WeldFxAt = Time.time + 0.15f;
        }
        var activelyBuilding = _workingSites.Contains(actor.Entity);
        foreach (var worker in actor.ConstructionWork) worker.Working = activelyBuilding;

        if (actor.Scaffold != null && actor.Scaffold.activeSelf == false) {
          actor.Scaffold.SetActive(true);
        }
        if (actor.Dressing != null && actor.Dressing.activeSelf) {
          actor.Dressing.SetActive(false);
        }

        if (wantStage == 1) {
          // Stage 1 (0~33% 부지 정리): 본체 메쉬를 숨기고 콘크리트 기초 패드·안전 콘·바리케이드·자재 팔레트·저층 비계만 표시.
          if (actor.View.Model.activeSelf) {
            actor.View.Model.SetActive(false);
          }
        } else {
          // Stage 2 (33~99% 골조 상승): 본체 Synty 공장이 기초 패드 위로 서서히 올라오며 간헐적 War FX 용접 스파크 분출.
          if (actor.View.Model.activeSelf == false) {
            actor.View.Model.SetActive(true);
          }
          var rise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((actor.ConstructionDisplayProgress - 0.33f) / 0.67f));
          var sinkDepth = Mathf.Max(actor.Height, 4.2f) * 0.72f;
          var pos = actor.View.Model.transform.localPosition;
          pos.y = actor.BuildingModelY - (1f - rise) * sinkDepth;
          actor.View.Model.transform.localPosition = pos;

          if (activelyBuilding && Fx != null && Time.time >= actor.WeldFxAt) {
            actor.WeldFxAt = Time.time + Random.Range(0.45f, 0.88f) / Mathf.Max(0.35f, GraphicsQuality.FxScale);
            var ang = Random.Range(0f, Mathf.PI * 2f);
            var radial = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
            var weldY = Mathf.Lerp(0.95f, Mathf.Max(2.4f, actor.Height * 0.52f), rise);
            var weldPos = actor.View.transform.position + radial * (radius * 0.68f) + Vector3.up * weldY;
            Fx.WeldingSparks(weldPos, radial + Vector3.up * 0.45f);
          }
        }
        return;
      }

      // Stage 3 (100% 완공): 비계·안전 바리케이드·건설 작업자 깔끔히 철거 + 미세 완공 먼지 + 본체 가동!
      if (actor.View.Model.activeSelf == false) {
        actor.View.Model.SetActive(true);
      }
      var finalPos = actor.View.Model.transform.localPosition;
      if (Mathf.Abs(finalPos.y - actor.BuildingModelY) > 0.0001f) {
        finalPos.y = Mathf.MoveTowards(finalPos.y, actor.BuildingModelY, dt * Mathf.Max(actor.Height, 4.2f) * 3f);
        actor.View.Model.transform.localPosition = finalPos;
      }
      if (actor.ConstructionStage == 1 || actor.ConstructionStage == 2) {
        if (actor.Scaffold != null) {
          Destroy(actor.Scaffold);
          actor.Scaffold = null;
        }
        ClearConstructionCrew(actor);
        Fx?.ConstructionCompleteDust(actor.View.transform.position, Mathf.Max(2.5f, actor.FootprintRadius));
        actor.ConstructionStage = 3;
      } else if (actor.Scaffold != null) {
        actor.Scaffold.SetActive(false);
      }
      if (actor.Dressing != null && actor.Dressing.activeSelf == false) {
        actor.Dressing.SetActive(true);
      }
    }

    /// <summary>확장 사령부: 주인의 시작 자리에서 25m 넘게 떨어진 사령부.</summary>
    public const float ForwardDistance = 25f;
    const float ForwardRingExtra = 4.2f;

    static bool IsForward(Frame frame, PlayerRef owner, Vector3 position) {
      var home = FactoryWarsUtil.StartPosition(frame, owner).ToUnityVector3();
      return new Vector2(position.x - home.x, position.z - home.z).magnitude > ForwardDistance;
    }

    /// <summary>전진 사령부 수: 확장 사령부 표시를 단 건물 수(테스트용).</summary>
    public int ForwardHeadquarters { get; private set; }

    /// <summary>
    /// 전진(확장) 사령부 표시: 일반 사령부·도시 건물과 한눈에 구분되도록 이동식 레이더·위성 안테나, 둘레 헤스코 방벽 고리,
    /// 탄약 상자 더미, 위장망, 작업등을 사령부 둘레에 놓는다(공사 중에는 숨김). 화면 전용.
    /// </summary>
    bool OnAsphaltRoad(Vector3 worldPos, float curbSetback = 3.5f) {
      if (World?.Map?.Roads == null) {
        return false;
      }
      var fp = new Photon.Deterministic.FPVector3(
        Photon.Deterministic.FP.FromFloat_UNSAFE(worldPos.x),
        Photon.Deterministic.FP._0,
        Photon.Deterministic.FP.FromFloat_UNSAFE(worldPos.z));
      foreach (var road in World.Map.Roads) {
        if (road.Distance(fp).AsFloat <= curbSetback) {
          return true;
        }
      }
      return false;
    }

    void DressForward(Actor actor, BuildingSpec spec, Vector3 worldPos) {
      if (actor.Dressing == null) {
        var root = new GameObject("BuildingDressing").transform;
        root.SetParent(actor.View.Turn, false);
        actor.Dressing = root.gameObject;
      }
      var group = new GameObject("ForwardCommand").transform;
      group.SetParent(actor.Dressing.transform, false);
      var r = spec.FootprintRadius.AsFloat;
      var seed = KenneyAssetMap.Seed(worldPos, 911);
      void PlaceOffRoad(DressKind kind, Vector3 local, float yaw, int s) {
        if (OnAsphaltRoad(worldPos + local, 3.5f) == false) {
          DressingSpawn.Place(kind, group, local, yaw, s);
        }
      }
      PlaceOffRoad(DressKind.ForwardRadar, new Vector3(r + 2.2f, 0f, r * 0.2f), 90f, seed);
      PlaceOffRoad(DressKind.TraderDish, new Vector3(-r - 1.4f, 0f, r * 0.7f), 0f, seed + 1);
      PlaceOffRoad(DressKind.TraderNet, new Vector3(-r - 2.6f, 0f, -r * 0.6f), 0f, seed + 2);
      for (var i = 0; i < 4; i++) {
        PlaceOffRoad(DressKind.TraderAmmo, new Vector3(r * 0.3f + i * 1.5f, 0f, -r - 2f), 0f, seed + 3 + i);
      }
      PlaceOffRoad(DressKind.TraderLight, new Vector3(r + 1.2f, 0f, -r - 1.2f), 180f, seed + 8);
      // 헤스코 방벽 고리(앞쪽 출입구·아스팔트 차선 위는 비움).
      var ring = r + ForwardRingExtra;
      for (var i = 0; i < 12; i++) {
        var angle = i * 30f;
        if (angle > 150f && angle < 210f) {
          continue;
        }
        var local = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * ring;
        PlaceOffRoad(DressKind.TraderSandbag, local, angle, seed + 20 + i);
      }
      ForwardHeadquarters++;
    }

    void DressBuilding(Actor actor, BuildingSpec spec, Vector3 worldPos) {
      if (spec.Role == BuildingRole.MlrsBattery) return;
      var warehouse = EnvironmentArt.Uses(World.Map) && spec.Role == BuildingRole.SupplyDepot;
      if (warehouse || spec.Role is BuildingRole.Barracks or BuildingRole.VehicleFactory or BuildingRole.Processor or BuildingRole.PowerPlant
          or BuildingRole.HeavyWorks or BuildingRole.Airfield) {
        var radius = spec.FootprintRadius.AsFloat;
        var yard = new GameObject("FactoryYard").transform;
        yard.SetParent(actor.View.Turn, false);
        for (var corner = -1; corner <= 1; corner += 2) {
          var side = radius * corner * 0.79f;
          var key = corner < 0 || warehouse ? "Crates" : "Beams";
          MapPolishArt.Place(key, yard, new Vector3(side, 0.06f, radius * 0.69f), Quaternion.identity,
            new Vector3(radius * 0.27f, 0.72f, radius * 0.24f));
        }
        MapPolishArt.Place("Barrels", yard, new Vector3(radius * 0.79f, 0.06f, -radius * 0.70f), Quaternion.identity,
          new Vector3(radius * 0.23f, 0.8f, radius * 0.24f));
        actor.Dressing = yard.gameObject;
        return;
      }
      // 실제 Synty 복합 킷배시 프리팹이 연결된 건물에는 구형 Kenney 소품(SpaceGate 흰 아치 등)을 덧씌우지 않는다.
      if (actor.View != null && actor.View.Slot != null) {
        return;
      }
      if (spec.Role == BuildingRole.GroundTower || spec.Role == BuildingRole.AirDefense || spec.FootprintRadius.AsFloat < 2.2f) {
        return;
      }
      var group = new GameObject("BuildingDressing").transform;
      group.SetParent(actor.View.Turn, false);
      var r = spec.FootprintRadius.AsFloat * 0.62f;
      var seed = KenneyAssetMap.Seed(worldPos, 701);
      var useSpace = spec.name.StartsWith("ff_") && (spec.Role == BuildingRole.Headquarters || spec.Role == BuildingRole.ResearchLab);
      var first = spec.Role == BuildingRole.ResearchLab ? DressKind.SpaceGate
        : spec.Role == BuildingRole.VehicleFactory ? DressKind.GarageDoor
        : useSpace ? DressKind.SpaceProp
        : (spec.Role == BuildingRole.CollectionYard || spec.Role == BuildingRole.Processor ? DressKind.ScrapSkip : DressKind.Hopper);
      var second = (spec.Role == BuildingRole.Barracks || spec.Role == BuildingRole.VehicleFactory || spec.Role == BuildingRole.Headquarters) ? DressKind.RallyFlag
        : (spec.Role == BuildingRole.CollectionYard ? DressKind.Crane : DressKind.Prop);
      DressingSpawn.Place(first, group, new Vector3(-r, 0f, -r * 0.5f), 90f, seed);
      DressingSpawn.Place(second, group, new Vector3(r, 0f, r * 0.4f), -90f, seed + 1);
      actor.Dressing = group.gameObject;
    }

    void UpdateBuilding(Frame frame, EntityRef entity, Transform3D* transform3D, Building* building, float dt) {
      var spec = frame.FindAsset(building->Spec);
      if (_actors.TryGetValue(entity, out var actor) == false) {
        actor = Create(entity, spec.name, true, false);
        if (actor == null) {
          return;
        }
        var position = transform3D->Position.ToUnityVector3();
        position.y = TerrainKit.SurfaceHeight(World.Map, position.x, position.z);
        actor.Position = position;
        actor.FootprintRadius = spec.FootprintRadius.AsFloat;
        actor.View.transform.position = position;
        actor.BuildingModelY = actor.View.Model.transform.localPosition.y;
        // Task 46: the sea barrier floats on the waterway (no concrete foundation slab).
        if (spec.Role != BuildingRole.SeaBarrier) FoundationView.Attach(actor.View.transform, World.Map, spec, position, 180f);
        // Phase B: the barrier model spans the waterway across its side (built along x for the west waterway).
        else actor.View.transform.rotation = WaterwayView.SideTurn(position);
        DressBuilding(actor, spec, position);
        var forward = spec.Role == BuildingRole.Headquarters && IsForward(frame, building->Owner, position);
        if (forward) {
          DressForward(actor, spec, position);
        }
        // 건물(전진 사령부는 방벽 고리까지) 자리의 길가·흩뿌림 장식을 걷어낸다.
        var clearance = spec.FootprintRadius.AsFloat + (forward ? ForwardRingExtra + 1.5f : 1.5f);
        var footprint = new GroundFootprint(position, new Vector3(clearance, 0f, clearance), Quaternion.identity);
        DressingRenderer.Active?.ReserveSite(footprint);
        World.ClearBiomeProps(item => footprint.Overlaps(item));
        actor.HullYaw = 180f;
        actor.View.SetYaw(180f);
      }

      // 항목 34-A: 안개 속 적 건물은 그리지 않는다(마지막으로 본 잔상은 아직 없다).
      var buildingViewer = _controller != null ? _controller.Player : PlayerRef.None;
      var buildingSeen = buildingViewer.IsValid == false || FactoryWarsUtil.IsVisibleTo(frame, buildingViewer, entity);
      if (actor.View.gameObject.activeSelf != buildingSeen) {
        actor.View.gameObject.SetActive(buildingSeen);
      }
      var state = frame.Global->Players.GetPointer(building->Owner);
      var powered = state->Outage == false;
      if (state->ColorIndex != actor.Color || powered != actor.Powered) {
        actor.Color = state->ColorIndex;
        actor.Powered = powered;
        ArtLook.Apply(actor.View.Model, actor.Color, powered);
      }

      var remaining = building->ConstructionRemaining.AsFloat;
      var finished = remaining <= 0f;
      var bodyRemaining = remaining;
      if (frame.TryGet<AirfieldWorks>(entity, out var airfield)) {
        var extra = AirfieldWorkSystem.BuildSeconds(FactoryWarsUtil.Rules(frame), spec.Role).AsFloat;
        bodyRemaining = Mathf.Max(0f, remaining - extra);
        if (bodyRemaining <= 0f) {
          if (actor.Runway == null) actor.Runway = FacilityWorkView.CreatePaving(actor.View.transform, airfield.Site, World, spec.Role == BuildingRole.Helipad);
          if (actor.Runway.gameObject.activeSelf == false) actor.Runway.gameObject.SetActive(true);
          actor.Runway.UpdatePaving(finished || extra <= 0f ? 1f : Mathf.Clamp01(1f - remaining / extra), dt);
        } else if (actor.Runway != null && actor.Runway.gameObject.activeSelf) {
          actor.Runway.gameObject.SetActive(false);
        }
      }
      var total = spec.BuildTimeSeconds.AsFloat;
      ShowConstruction(actor, bodyRemaining <= 0f || total <= 0f ? 1f : 1f - bodyRemaining / total, dt);
      if (finished == false) {
        UpdateActivityCrew(actor, false, null);
        if (actor.ExtractorDelivery != null) {
          Destroy(actor.ExtractorDelivery.Root);
          actor.ExtractorDelivery = null;
          actor.ExtractorRouteCheckAt = 0f;
        }
        return;
      }

      var hasTarget = TryTarget(frame, building->TargetEntity, out var target, out var air);
      actor.AntiAir = spec.Attack == AttackType.AntiAir;
      if (frame.TryGet<MlrsMagazine>(entity, out var magazine)) {
        actor.Mlrs ??= new MlrsVisual(actor.View.Model.transform);
        if (buildingSeen) actor.Mlrs.Tick(magazine, spec, dt);
      } else {
        Aim(actor, hasTarget, target, air, dt);
      }
      var cooldown = building->AttackCooldown.AsFloat;
      // 손상: 체력 50% 이하는 연기, 25% 이하는 불.
      var ratio = building->Health.AsFloat / Mathf.Max(spec.MaxHealth.AsFloat, 1f);
      if (building->ConstructionRemaining <= FP._0 && ratio < 0.5f && Time.time >= actor.DamageAt) {
        actor.DamageAt = Time.time + (ratio < 0.25f ? 0.22f : 0.5f) / Mathf.Max(GraphicsQuality.FxScale, 0.2f);
        Fx.Burning(actor.View.transform.position + Vector3.up * (actor.Height * 0.7f + 0.5f), ratio < 0.25f);
      }
      if (cooldown > actor.Cooldown + 0.05f && spec.Attack != AttackType.None) {
        Fire(actor, spec.Attack, spec.Range.AsFloat, hasTarget, target, false);
      }
      actor.Cooldown = cooldown;

      var processing = spec.ProcessSeconds > FP._0 && building->ProcessorOff == false;
      var producing = building->QueueCount > 0;
      if (spec.Role == BuildingRole.Shipyard) UpdateShipWork(frame, actor, building, transform3D->Position, state, dt);
      var researching = building->Research > 0;
      var ultimateBusy = spec.Role == BuildingRole.UltimateSite && (state->UltimatePhase == UltimatePhase.Preparing || state->UltimatePhase == UltimatePhase.Ready);
      var overdriving = FactorySystem.IsOverdriving(frame, building);
      var yard = spec.IsDropOff && spec.Role == BuildingRole.CollectionYard;
      var staff = spec.RequiresResourceNode ? CitySystem.Occupants(frame, entity) : 0;
      var extracting = spec.RequiresResourceNode && building->Paused == PauseReason.None
        && building->EmpUntilFrame <= frame.Number && frame.TryGet<ResourceNode>(building->ExtractNode, out var node)
        && node.Remaining > FP._0 && staff > 0;
      if (spec.RequiresResourceNode && actor.ExtractorEquipment == null) {
        var equipment = spec.RequiredNodeKind == ResourceKind.Wood ? _catalog.LumberMillSaw : _catalog.IronExtractorDrill;
        if (equipment != null) {
          var prop = Instantiate(equipment, actor.View.transform);
          prop.transform.localPosition = new Vector3(actor.FootprintRadius * 0.86f, 0.55f, 0f);
          foreach (var collider in prop.GetComponentsInChildren<Collider>(true)) {
            collider.enabled = false;
          }
          actor.ExtractorEquipment = prop.transform;
        }
      }
      if (extracting && actor.ExtractorEquipment != null) {
        actor.ExtractorEquipment.Rotate(Vector3.up, dt * 160f, Space.Self);
      }
      if (spec.RequiresResourceNode && buildingSeen && (buildingViewer.IsValid == false || building->Owner == buildingViewer)) {
        var cap = Mathf.Max(1, spec.WorkerSlotCapacity);
        if (actor.PipRoot == null) {
          actor.PipRoot = HeroVisuals.CreatePassengerPips(actor.View, cap, out actor.PipRenderers);
          if (actor.PipRoot != null) actor.PipRoot.transform.localScale = Vector3.one * 1.65f;
        }
        UpdatePips(actor, staff, staff > 0 || _marked.Contains(actor), 0.65f);
        var stockpile = ProductionSystem.ExtractorStockpile(building).AsInt;
        UpdateWorkerCountBadge(actor, staff, cap, true, stockpile);
        var stage = (frame.Number / 50) % 5;
        var p0 = spec.RequiredNodeKind == ResourceKind.Wood ? "Chop" : "Dig";
        var p1 = spec.RequiredNodeKind == ResourceKind.Wood
          ? (stage % 2 == 0 ? "PickUp" : "BoxJog")
          : (stage % 2 == 0 ? "PickUp" : "HoldingWalk");
        var p2 = stage % 2 == 0 ? "PutDown" : "Inspect";
        UpdateActivityCrewMulti(actor, extracting ? Mathf.Clamp(staff, 1, 3) : 0, p0, p1, p2);
        // 컨베이어 직결 제거: 자원은 채취 시설에 적치되며 수송차량 '자원채취'로 사령부까지 운반한다.
        if (actor.ExtractorDelivery != null) {
          Destroy(actor.ExtractorDelivery.Root);
          actor.ExtractorDelivery = null;
        }
      } else if (spec.RequiresResourceNode) {
        UpdatePips(actor, 0, false);
        UpdateWorkerCountBadge(actor, 0, Mathf.Max(1, spec.WorkerSlotCapacity), false);
        UpdateActivityCrewMulti(actor, 0, null, null, null);
        if (actor.ExtractorDelivery != null) {
          Destroy(actor.ExtractorDelivery.Root);
          actor.ExtractorDelivery = null;
        }
      }
      if (spec.Role == BuildingRole.PowerPlant) {
        UpdatePowerPlantVisual(actor, powered, dt);
        var pCap = Mathf.Max(3, spec.WorkerSlotCapacity);
        var pStaff = CitySystem.Occupants(frame, entity);
        var showPower = buildingSeen && (buildingViewer.IsValid == false || building->Owner == buildingViewer);
        UpdateWorkerCountBadge(actor, pStaff, pCap, showPower);
        var pCount = (powered && buildingSeen) ? Mathf.Clamp(Mathf.Max(pStaff, 1), 1, 3) : 0;
        var powerPose0 = (frame.Number / 65) % 2 == 0 ? "Device" : "Inspect";
        UpdateActivityCrewMulti(actor, pCount, powerPose0, "Inspect", "Lever");
      }
      var bHpRatio = spec.MaxHealth > FP._0 ? (building->Health / spec.MaxHealth).AsFloat : 1f;
      UpdateDamageFire(actor, bHpRatio, 1.4f);
      var operating = powered && (processing || producing || researching || ultimateBusy || overdriving || extracting || spec.Role == BuildingRole.PowerPlant);
      var busy = operating || (powered && (yard || spec.Role == BuildingRole.Headquarters));
      Animate(actor, dt, powered, busy);

      // 현재 공장 단계에 맞는 정비동과 굴뚝을 표시한다.
      var fLevel = FactorySystem.LevelOf(building);
      if (fLevel != actor.FactoryLevel) {
        actor.FactoryLevel = fLevel;
        if (actor.FactoryUpgradeGo != null) Destroy(actor.FactoryUpgradeGo);
        actor.FactoryUpgradeGo = HeroVisuals.AttachFactoryUpgrade(actor.View.Model.transform, fLevel, actor.FootprintRadius, actor.Height);
        if (actor.FactoryUpgradeGo != null) actor.FactoryUpgradeGo.transform.localScale = Vector3.one * 0.84f;
      }
      if (actor.FactoryUpgradeGo != null && actor.FactoryUpgradeGo.transform.localScale.x < 1f) {
        var extension = actor.FactoryUpgradeGo.transform;
        extension.localScale = Vector3.MoveTowards(extension.localScale, Vector3.one, 1.3f * dt);
      }
      if (actor.FactoryBeacon != null) {
        var freq = overdriving ? 16f : 7.5f;
        var blinkOn = operating && Mathf.Sin(Time.time * freq + actor.Phase) > -0.15f;
        actor.FactoryBeacon.sharedMaterial = HeroVisuals.BeaconMaterial(blinkOn, overdriving);
      }
      if (overdriving && Time.time >= actor.OverdriveFxAt) {
        actor.OverdriveFxAt = Time.time + 0.28f;
        var basePos = actor.View.transform.position;
        Fx.Burning(basePos + Vector3.up * 3.6f, true);
        Fx.Burst(basePos + Vector3.up * 2.4f, new Color(1f, 0.25f, 0.06f), 0.95f);
      }
      SyncSynergyPipelines(frame);
      if (actor.SiloDoorL != null && actor.SiloDoorR != null) {
        var open = state->UltimatePhase == UltimatePhase.Preparing || state->UltimatePhase == UltimatePhase.Ready
          || (state->UltimateUseFrame > 0 && frame.Number - state->UltimateUseFrame < frame.SessionConfig.UpdateFPS * 4);
        var doorX = open ? 1.55f : 0.68f;
        actor.SiloDoorL.localPosition = Vector3.Lerp(actor.SiloDoorL.localPosition, new Vector3(-doorX, 1.48f, 0f), Mathf.Clamp01(dt * 4f));
        actor.SiloDoorR.localPosition = Vector3.Lerp(actor.SiloDoorR.localPosition, new Vector3(doorX, 1.48f, 0f), Mathf.Clamp01(dt * 4f));
        if (actor.SiloMissile != null) {
          var raised = state->UltimatePhase == UltimatePhase.Ready ? 0.85f : state->UltimatePhase == UltimatePhase.Preparing ? 0.35f : -0.8f;
          actor.SiloMissile.localPosition = Vector3.Lerp(actor.SiloMissile.localPosition, new Vector3(0f, raised, 0f), Mathf.Clamp01(dt * 4f));
        }
      }

      var smoke = actor.FactorySmoke != null && actor.FactorySmoke.Length > 0 ? actor.FactorySmoke : actor.View.Entry.Smoke;
      if (smoke != null && smoke.Length > 0 && Time.time >= actor.SmokeAt && operating) {
        actor.SmokeAt = Time.time + (overdriving ? SmokeSeconds * 0.45f : SmokeSeconds) / Mathf.Max(GraphicsQuality.FxScale, 0.1f);
        foreach (var point in smoke) {
          Fx.ChimneySmoke(actor.View.Model.transform.TransformPoint(point));
        }
      }
    }

    void UpdateShipWork(Frame frame, Actor actor, Building* building, FPVector3 origin, PlayerState* state, float dt) {
      var queued = building->QueueCount > 0 ? building->Queue[0].Unit : default;
      if (actor.ShipWork != null && actor.ShipWorkUnit != queued) {
        actor.ShipWork.gameObject.SetActive(false);
        Destroy(actor.ShipWork.gameObject);
        actor.ShipWork = null;
      }
      if (queued.IsValid == false) return;
      var unit = frame.FindAsset(queued);
      if (unit == null || unit.IsNaval == false) return;
      if (actor.ShipWork == null) {
        if (ProductionSystem.TryShipLaunchPoint(frame, origin, building, 0, out var launch) == false) return;
        actor.ShipWork = FacilityWorkView.CreateVessel(actor.View.transform, _catalog, unit.name, state->ColorIndex,
          launch.ToUnityVector3(), origin.ToUnityVector3());
        actor.ShipWorkUnit = queued;
      }
      var total = ProductionSystem.UnitProductionSeconds(frame, state, unit).AsFloat;
      if (total <= 0f || actor.ShipWork == null) return;
      var progress = Mathf.Clamp01(building->QueueProgress.AsFloat / total);
      actor.ShipWork.UpdateVessel(progress, dt);
      if (progress < 1f && building->Paused == PauseReason.None && building->EmpUntilFrame <= frame.Number
          && actor.View.gameObject.activeInHierarchy && Time.time >= actor.ShipWeldAt) {
        actor.ShipWeldAt = Time.time + 0.6f / Mathf.Max(0.3f, GraphicsQuality.FxScale);
        var hull = actor.ShipWork.transform;
        Fx.WeldingSparks(hull.position + hull.right * 0.85f + Vector3.up * (0.32f + progress * 0.54f), hull.right);
      }
    }

    void AttachVehicleCrew(Actor actor, UnitSpec spec) {
      if (actor?.View == null || spec == null || !MixamoMotion.HasWorkerController) return;
      var id = spec.name ?? "";
      string pose = null;
      Vector3 seat = Vector3.zero;
      float scale = 0.64f;
      if (id == "cf_scout_bike") { pose = "Drive"; seat = new Vector3(0f, 1.12f, -0.38f); scale = 0.92f; }
      else if (id == "ff_scout_buggy") { pose = "Drive"; seat = new Vector3(-0.22f, 0.48f, 0.05f); scale = 0.72f; }
      else if (id == "ff_scout_boat") { pose = "Drive"; seat = new Vector3(0f, 0.95f, -0.65f); scale = 0.85f; }
      else if (id == "ff_landing_craft") { pose = "Drive"; seat = new Vector3(0f, 1.35f, -1.75f); scale = 0.85f; }
      else if (id == "ff_artillery_ship" || id == "cf_artillery_ship") {
        // 함교 갑판 조타수 배치(마스트 높이 이하로 유지해 아치교 수직 통과 여유 보존).
        pose = "Drive"; seat = new Vector3(0f, 2.35f, 1.45f); scale = 0.78f;
      }
      else if (id == "ff_hawk_gunship" || id == "cf_gale_gunship" || id == "pmc_scout_heli" || id == "pmc_attack_heli") {
        pose = "Pilot"; seat = new Vector3(0f, 0.75f, 1.05f); scale = 0.78f;
      }
      if (pose == null) return;
      if (!VisualCatalog.TryFilled(VisualCatalog.FxId("const_worker_0"), out var slot) || slot.Prefab == null) return;
      var parent = actor.View.Turn != null ? actor.View.Turn : actor.View.transform;
      var crew = Instantiate(slot.Prefab, parent, false);
      crew.name = pose == "Pilot" ? "VehicleCrew_Pilot" : "VehicleCrew_Driver";
      foreach (var collider in crew.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
      crew.transform.localPosition = seat;
      crew.transform.localRotation = Quaternion.identity;
      crew.transform.localScale = Vector3.one * scale;
      var animator = crew.GetComponentInChildren<Animator>(true);
      var motion = MixamoMotion.Attach(animator, infantry: false);
      if (motion == null || !motion.Set(pose, (actor.Phase * 0.17f) % 1f)) {
        Destroy(crew);
        return;
      }
      actor.VehicleCrew = crew;
      actor.VehicleCrewMotion = motion;
      actor.VehicleCrewPose = pose;
    }

    void UpdateActivityCrew(Actor actor, bool active, string pose) {
      UpdateActivityCrewMulti(actor, active ? 1 : 0, pose, pose, pose);
    }

    static readonly Vector3[] WorkerSlotOffsets = {
      new Vector3(-1.65f, 0f, -0.95f),
      new Vector3( 0.05f, 0f, -1.08f),
      new Vector3( 1.75f, 0f, -0.95f),
    };

    void UpdateActivityCrewMulti(Actor actor, int count, string pose0, string pose1, string pose2) {
      count = Mathf.Clamp(count, 0, 3);
      if (count > 0 && actor.ActivityCrewList.Count < count && !actor.ActivityUnavailable && MixamoMotion.HasWorkerController) {
        if (VisualCatalog.TryFilled(VisualCatalog.FxId("const_worker_0"), out var slot) && slot.Prefab != null) {
          while (actor.ActivityCrewList.Count < count) {
            var idx = actor.ActivityCrewList.Count;
            var crew = Instantiate(slot.Prefab, actor.View.transform, false);
            crew.name = $"FacilityActivityWorker_{idx}";
            var animator = crew.GetComponentInChildren<Animator>(true);
            var motion = MixamoMotion.Attach(animator);
            if (motion == null) {
              actor.ActivityUnavailable = true;
              Destroy(crew);
              break;
            }
            if (idx == 0 && pose0 == "Chop") AttachChoppingAxe(animator);
            foreach (var collider in crew.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            var off = WorkerSlotOffsets[idx];
            crew.transform.localPosition = new Vector3(off.x, 0f, -(actor.FootprintRadius - off.z));
            crew.transform.localRotation = Quaternion.Euler(0f, idx == 0 ? 18f : idx == 2 ? -18f : 0f, 0f);
            crew.transform.localScale = Vector3.one * 0.95f;
            actor.ActivityCrewList.Add(crew);
            actor.ActivityMotionList.Add(motion);
            if (idx == 0) {
              actor.ActivityCrew = crew;
              actor.ActivityMotion = motion;
            }
          }
        }
      }
      var poses = new[] { pose0, pose1, pose2 };
      for (var i = 0; i < actor.ActivityCrewList.Count; i++) {
        var crew = actor.ActivityCrewList[i];
        var motion = actor.ActivityMotionList[i];
        var want = i < count;
        if (crew.activeSelf != want) {
          if (!want) motion.Set(null);
          crew.SetActive(want);
        }
        if (want && !string.IsNullOrEmpty(poses[i])) {
          motion.Set(poses[i], Mathf.Repeat(actor.Phase * 0.2f + i * 0.31f, 1f));
        }
      }
    }

    void UpdateWorkerCountBadge(Actor actor, int staff, int capacity, bool visible, int stockpile = -1) {
      if (!visible && actor.WorkerCountRoot == null) return;
      if (actor.WorkerCountRoot == null) {
        var root = new GameObject("WorkerCountBadge");
        root.transform.SetParent(actor.View.transform, false);
        var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
        bg.name = "BadgeBg";
        bg.transform.SetParent(root.transform, false);
        bg.transform.localScale = new Vector3(2.35f, 1.05f, 1f);
        bg.transform.localPosition = new Vector3(0f, 0f, 0.04f);
        Destroy(bg.GetComponent<Collider>());
        var bgMat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default")) {
          color = new Color(0.08f, 0.11f, 0.16f, 0.88f)
        };
        bg.GetComponent<MeshRenderer>().sharedMaterial = bgMat;

        var textGo = new GameObject("BadgeText");
        textGo.transform.SetParent(root.transform, false);
        var tm = textGo.AddComponent<TextMesh>();
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.fontSize = 48;
        tm.characterSize = 0.14f;
        tm.fontStyle = FontStyle.Bold;
        tm.color = new Color(0.32f, 0.96f, 0.56f, 1f);
        actor.WorkerCountRoot = root;
        actor.WorkerCountBadge = tm;
      }
      if (actor.WorkerCountRoot.activeSelf != visible) actor.WorkerCountRoot.SetActive(visible);
      if (!visible) return;
      actor.WorkerCountRoot.transform.localPosition = new Vector3(0f, 3.4f, -(actor.FootprintRadius + 0.35f));
      var cam = Camera.main;
      if (cam != null) actor.WorkerCountRoot.transform.rotation = cam.transform.rotation;
      var label = stockpile > 0 ? $"{staff}/{capacity} · 적치 {stockpile}" : $"{staff}/{capacity}";
      if (actor.WorkerCountBadge.text != label) {
        actor.WorkerCountBadge.text = label;
        var bgTf = actor.WorkerCountRoot.transform.Find("BadgeBg");
        if (bgTf != null) bgTf.localScale = new Vector3(stockpile > 0 ? 5.6f : 2.35f, 1.05f, 1f);
      }
      actor.WorkerCountBadge.color = staff >= capacity
        ? new Color(0.28f, 0.98f, 0.54f, 1f)
        : staff > 0 ? new Color(1f, 0.86f, 0.28f, 1f) : new Color(0.86f, 0.90f, 0.96f, 1f);
    }

    static void AttachChoppingAxe(Animator animator) {
      var prefab = Resources.Load<GameObject>("FactoryWarsMixamo/Axe");
      var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
      if (prefab == null || hand == null) return;
      var axe = Instantiate(prefab, hand, false);
      axe.name = "LumberWorkerAxe";
      foreach (var collider in axe.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
      var fingers = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
      if (fingers != null) axe.transform.position = Vector3.Lerp(hand.position, fingers.position, 0.65f);
      axe.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
      axe.transform.localScale = Vector3.one * 0.72f;
    }

    void UpdateExtractorDelivery(Frame frame, Actor actor, PlayerRef owner, ResourceKind kind, bool extracting, float dt) {
      var route = actor.ExtractorDelivery;
      // 첫 배치 때는 공병이 입구를 통과한 뒤 컨베이어를 표시한다.
      if (route == null && (extracting == false || _enteringExtractorSites.Contains(actor.Entity))) return;
      if (Time.time >= actor.ExtractorRouteCheckAt || (route != null && frame.Exists(route.Headquarters) == false)) {
        actor.ExtractorRouteCheckAt = Time.time + 0.6f;
        var best = EntityRef.None;
        var bestDistance = float.PositiveInfinity;
        var destination = Vector3.zero;
        var radius = 0f;
        var buildings = frame.Filter<Transform3D, Building>();
        while (buildings.NextUnsafe(out var entity, out var transform, out var building)) {
          if (building->Owner != owner || building->Health <= FP._0 || building->ConstructionRemaining > FP._0) continue;
          var spec = frame.FindAsset(building->Spec);
          if (spec.Role != BuildingRole.Headquarters) continue;
          var position = transform->Position.ToUnityVector3();
          var distance = (position - actor.Position).sqrMagnitude;
          if (distance >= bestDistance) continue;
          best = entity;
          bestDistance = distance;
          destination = position;
          radius = spec.FootprintRadius.AsFloat;
        }
        if (route != null && route.Headquarters != best) {
          Destroy(route.Root);
          route = null;
        }
        if (route == null && best != EntityRef.None) {
          var cargo = kind == ResourceKind.Wood ? _catalog.LumberCargo : _catalog.IronCargo;
          bool Clear(GroundFootprint footprint) {
            if (_ambientWorkers != null && !_ambientWorkers.ClearForYard(footprint)) return false;
            foreach (var road in World.Map.Roads ?? System.Array.Empty<MapShape>()) {
              if (footprint.Overlaps(road, 0.35f)) return false;
            }
            foreach (var other in _actors.Values) {
              if (other == actor || other.FootprintRadius <= 0f) continue;
              if (footprint.Overlaps(new Vector2(other.Position.x, other.Position.z), other.FootprintRadius + 0.2f)) return false;
            }
            return true;
          }
          route = ExtractorDeliveryView.Create(actor.View.transform, World.Map, best, actor.Position, actor.FootprintRadius,
            destination, radius, cargo, Clear, kind);
          if (route != null) _ambientWorkers?.AvoidYard(route.WalkObstacle);
        }
        actor.ExtractorDelivery = route;
      }
      route?.Update(extracting, dt);
    }

    void UpdatePowerPlantVisual(Actor actor, bool powered, float dt) {
      if (actor.PowerTurbine == null) {
        var root = new GameObject("PowerPlantDistribution").transform;
        root.SetParent(actor.View.transform, false);
        var offset = new Vector3(actor.FootprintRadius * 0.96f, 0.05f, -actor.FootprintRadius * 0.68f);
        root.localPosition = offset;

        // Transformer bank + distribution busbars (no solid black, clean industrial steel + hazard accents).
        var bank = new MeshKit();
        bank.Paint(Swatch.MetalLight).Box(new Vector3(0f, 0.65f, 0.95f), new Vector3(1.55f, 1.25f, 1.05f));
        bank.Paint(Swatch.MetalDark).Box(new Vector3(0f, 0.65f, 1.50f), new Vector3(1.25f, 0.95f, 0.14f));
        bank.Paint(Swatch.Hazard).Cylinder(new Vector3(-0.42f, 1.52f, 0.95f), 0.12f, 0.55f, 8);
        bank.Paint(Swatch.Hazard).Cylinder(new Vector3(0.42f, 1.52f, 0.95f), 0.12f, 0.55f, 8);
        // Generator housing base
        bank.Paint(Swatch.MetalLight).Cylinder(new Vector3(0f, 0.45f, -0.75f), 0.92f, 0.85f, 14);
        var bankGo = new GameObject("TransformerBank", typeof(MeshFilter), typeof(MeshRenderer));
        bankGo.transform.SetParent(root, false);
        bankGo.GetComponent<MeshFilter>().sharedMesh = bank.ToMesh("TransformerBank");
        bankGo.GetComponent<MeshRenderer>().sharedMaterial = ArtLook.NeutralMaterial;

        // Spinning turbine rotor on top of the generator housing
        var rotorKit = new MeshKit();
        rotorKit.Paint(Swatch.MetalDark).Cylinder(Vector3.zero, 0.28f, 0.36f, 10);
        rotorKit.Paint(Swatch.MetalLight).Box(new Vector3(0f, 0.08f, 0f), new Vector3(1.58f, 0.10f, 0.24f));
        rotorKit.Paint(Swatch.MetalLight).Box(new Vector3(0f, 0.08f, 0f), new Vector3(0.24f, 0.10f, 1.58f));
        var rotorGo = new GameObject("GeneratorTurbineRotor", typeof(MeshFilter), typeof(MeshRenderer));
        rotorGo.transform.SetParent(root, false);
        rotorGo.transform.localPosition = new Vector3(0f, 0.96f, -0.75f);
        rotorGo.GetComponent<MeshFilter>().sharedMesh = rotorKit.ToMesh("GeneratorTurbineRotor");
        rotorGo.GetComponent<MeshRenderer>().sharedMaterial = ArtLook.NeutralMaterial;
        actor.PowerTurbine = rotorGo.transform;

        // Running / Offline status beacon
        var beacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        beacon.name = "PowerStatusBeacon";
        Destroy(beacon.GetComponent<Collider>());
        beacon.transform.SetParent(root, false);
        beacon.transform.localPosition = new Vector3(0f, 1.92f, 0.95f);
        beacon.transform.localScale = new Vector3(0.42f, 0.16f, 0.42f);
        actor.PowerBeaconMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) {
          hideFlags = HideFlags.HideAndDontSave,
          color = new Color(0.20f, 0.92f, 1.0f),
        };
        beacon.GetComponent<Renderer>().sharedMaterial = actor.PowerBeaconMat;
      }
      if (powered) {
        actor.PowerTurbine.Rotate(Vector3.up, dt * 240f, Space.Self);
      }
      if (actor.PowerBeaconMat != null) {
        actor.PowerBeaconMat.color = powered ? new Color(0.20f, 0.92f, 1.0f) : new Color(0.84f, 0.20f, 0.18f);
      }
    }

    void UpdateNavalWake(Actor actor, UnitSpec spec, Vector3 before, float dt) {
      if (actor.NavalWake == null) {
        var hullHalfLen = spec.Size == UnitSize.Small ? 2.4f : 7.8f;
        actor.NavalWake = new ParticlePackVfx.NavalWakeHandle(actor.View.transform, hullHalfLen);
      }
      var speed = dt > 0f ? (actor.Position - before).magnitude / dt : 0f;
      actor.NavalWake.Update(speed, actor.HullYaw);
    }

    void UpdateAirFlight(Actor actor, UnitSpec spec, Vector3 before, float dt, bool flying) {
      if (actor.AirFlight == null) {
        var name = spec.name ?? "";
        var isRotor = name.EndsWith("_gunship") || name.EndsWith("_heli") || name.Contains("apache") || name.Contains("chopper") || actor.IsCarrier;
        var scale = actor.IsCarrier ? 1.75f : spec.Size == UnitSize.Large ? 1.35f : spec.Size == UnitSize.Medium ? 1.0f : 0.68f;
        actor.AirFlight = new ParticlePackVfx.AirFlightHandle(
          actor.View.Turn != null ? actor.View.Turn : actor.View.transform,
          actor.View.transform,
          isRotor,
          scale);
      }
      var speed = dt > 0f ? (actor.Position - before).magnitude / dt : 0f;
      var groundY = World != null && World.Map != null ? TerrainKit.SurfaceHeight(World.Map, actor.Position.x, actor.Position.z) : 0f;
      actor.AirFlight.Update(speed, flying, groundY);
    }

    void UpdateDamageFire(Actor actor, float hpRatio, float scale) {
      if (hpRatio >= 0.60f && actor.DamageFire == null) return;
      if (actor.DamageFire == null) {
        actor.DamageFire = new ParticlePackVfx.DamageFireHandle(actor.View.Turn != null ? actor.View.Turn : actor.View.transform, scale);
      }
      actor.DamageFire.Update(hpRatio);
    }

    void SyncSynergyPipelines(Frame frame) {
      if (_stage == null || Time.time < _synergyPipeAt) {
        return;
      }
      _synergyPipeAt = Time.time + 0.45f;
      var rules = FactoryWarsUtil.Rules(frame);
      var r = rules != null && rules.FactorySynergyRadius > FP._0 ? rules.FactorySynergyRadius.AsFloat : 12f;
      var r2 = r * r;
      var prods = new List<(EntityRef E, PlayerRef Owner, Vector3 Pos)>();
      var srcs = new List<(EntityRef E, PlayerRef Owner, Vector3 Pos, bool IsPower)>();
      var buildings = frame.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out var e, out var t, out var b)) {
        if (b->ConstructionRemaining > FP._0 || b->Health <= FP._0) continue;
        var role = frame.FindAsset(b->Spec).Role;
        var pos = t->Position.ToUnityVector3();
        if (FactorySystem.IsProductionFactory(role)) {
          prods.Add((e, b->Owner, pos));
        } else if (FactorySystem.IsSynergySource(role)) {
          srcs.Add((e, b->Owner, pos, role == BuildingRole.PowerPlant));
        }
      }
      var active = new HashSet<(EntityRef, EntityRef)>();
      for (var i = 0; i < prods.Count; i++) {
        for (var j = 0; j < srcs.Count; j++) {
          if (prods[i].Owner != srcs[j].Owner) continue;
          var dx = prods[i].Pos.x - srcs[j].Pos.x;
          var dz = prods[i].Pos.z - srcs[j].Pos.z;
          if (dx * dx + dz * dz > r2) continue;
          var key = (prods[i].E, srcs[j].E);
          active.Add(key);
          if (_synergyPipes.ContainsKey(key) == false) {
            var pipe = HeroVisuals.BuildSynergyPipeline(_stage, prods[i].Pos, srcs[j].Pos, srcs[j].IsPower);
            if (pipe != null) _synergyPipes[key] = pipe;
          }
        }
      }
      var remove = new List<(EntityRef, EntityRef)>();
      foreach (var kv in _synergyPipes) {
        if (active.Contains(kv.Key) == false) {
          if (kv.Value != null) Destroy(kv.Value);
          remove.Add(kv.Key);
        }
      }
      foreach (var k in remove) _synergyPipes.Remove(k);
    }

    void UpdateSelection(Frame frame) {
      foreach (var actor in _marked) {
        actor.View.SetSelected(false);
      }
      _marked.Clear();
      if (_controller == null) {
        _controller = FindAnyObjectByType<MatchController>();
      }
      if (_controller == null || _controller.IsReady == false) {
        return;
      }
      var selection = _controller.Input.Selection;
      foreach (var entity in selection.Units) {
        if (_actors.TryGetValue(entity, out var actor) && frame.Unsafe.TryGetPointer<Unit>(entity, out var unit)) {
          Mark(actor, unit->Health.AsFloat / frame.FindAsset(unit->Spec).MaxHealth.AsFloat);
        }
      }
      if (selection.Building != EntityRef.None && _actors.TryGetValue(selection.Building, out var selected)
          && frame.Unsafe.TryGetPointer<Building>(selection.Building, out var building)) {
        Mark(selected, building->Health.AsFloat / frame.FindAsset(building->Spec).MaxHealth.AsFloat);
      }
      CommandLines(frame, selection);
      DamageBars(frame);
    }

    // ---------- 명령선·조준 표시·체력바(전투 가독성) ----------

    readonly List<LineRenderer> _lines = new List<LineRenderer>();
    readonly List<GameObject> _reticles = new List<GameObject>();
    readonly HashSet<EntityRef> _targeted = new HashSet<EntityRef>();
    static Material _attackLine;
    static Material _moveLine;
    static Mesh _reticleMesh;
    public int AttackLines { get; private set; }
    public int MoveLines { get; private set; }

    static Material LineMaterial(Color color) {
      var shader = Shader.Find("FactoryWars/CaptureRing");
      var material = new Material(shader != null ? shader : Shader.Find("Sprites/Default"));
      material.SetColor("_Color", color);
      material.SetFloat("_Glow", 1.6f);
      material.SetFloat("_Pulse", 0.25f);
      return material;
    }

    LineRenderer Line(int index, Material material) {
      while (_lines.Count <= index) {
        var line = new GameObject("CommandLine").AddComponent<LineRenderer>();
        line.transform.SetParent(_stage, false);
        line.positionCount = 2;
        line.widthMultiplier = 0.12f;
        line.numCapVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        _lines.Add(line);
      }
      var result = _lines[index];
      result.sharedMaterial = material;
      result.gameObject.SetActive(true);
      return result;
    }

    GameObject Reticle(int index) {
      if (_reticleMesh == null) {
        var kit = new MeshKit().Paint(Swatch.White);
        ArtView.DashedRing(kit, 0.965f, 1f, 32);
        _reticleMesh = kit.ToMesh("target_reticle");
      }
      while (_reticles.Count <= index) {
        var item = new GameObject("Reticle", typeof(MeshFilter), typeof(MeshRenderer));
        item.transform.SetParent(_stage, false);
        item.GetComponent<MeshFilter>().sharedMesh = _reticleMesh;
        item.GetComponent<MeshRenderer>().sharedMaterial = _attackLine;
        item.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _reticles.Add(item);
      }
      _reticles[index].SetActive(true);
      return _reticles[index];
    }

    /// <summary>
    /// 고른 아군 유닛 → 표적: 붉은 공격선(#FF2A2A)과 표적 아래 맥동하는 붉은 점선 조준 고리. 이동 중이면 목적지까지 초록 이동선(#38F27A)과 목적지 고리.
    /// 화면 전용(시뮬레이션 TargetEntity·TargetPosition을 읽기만 함).
    /// </summary>
    const float MoveLineWidth = 0.075f;
    const float AttackLineWidth = 0.055f;
    const float MoveLineHold = 0.8f;
    const float MoveLineFade = 0.6f;
    const float AttackLineDuration = 1.0f;
    readonly Dictionary<EntityRef, (Vector3 Goal, float Since)> _moveSeen = new Dictionary<EntityRef, (Vector3, float)>();
    readonly Dictionary<EntityRef, (EntityRef Target, float Since)> _attackSeen = new Dictionary<EntityRef, (EntityRef, float)>();
    static Texture2D _dashTex;

    static Material DashedLineMaterial(Color color) {
      if (_dashTex == null) {
        _dashTex = new Texture2D(32, 4, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
        var px = new Color32[32 * 4];
        for (var y = 0; y < 4; y++) {
          for (var x = 0; x < 32; x++) {
            px[y * 32 + x] = x < 14 ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
          }
        }
        _dashTex.SetPixels32(px);
        _dashTex.Apply(false);
      }
      var mat = LineMaterial(color);
      mat.mainTexture = _dashTex;
      if (mat.HasProperty("_BaseMap")) {
        mat.SetTexture("_BaseMap", _dashTex);
      }
      if (mat.HasProperty("_Surface")) {
        mat.SetFloat("_Surface", 1f);
      }
      if (mat.HasProperty("_ZWrite")) {
        mat.SetFloat("_ZWrite", 0f);
      }
      mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
      mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
      mat.renderQueue = 2500;
      return mat;
    }

    void CommandLines(Frame frame, SelectionModel selection) {
      _attackLine ??= DashedLineMaterial(new Color(1f, 0.34f, 0.24f, 0.58f));
      _moveLine ??= DashedLineMaterial(new Color(0.22f, 0.95f, 0.48f, 0.62f));
      _attackLine.mainTextureOffset = new Vector2(-Time.time * 1.8f, 0f);
      _moveLine.mainTextureOffset = new Vector2(-Time.time * 1.4f, 0f);
      _targeted.Clear();
      var lines = 0;
      var reticles = 0;
      var attack = 0;
      var move = 0;
      foreach (var entity in selection.Units) {
        if (_actors.TryGetValue(entity, out var actor) == false || frame.Unsafe.TryGetPointer<Unit>(entity, out var unit) == false || actor.View.gameObject.activeInHierarchy == false) {
          continue;
        }
        var from = actor.View.transform.position + Vector3.up * 0.5f;
        if (TryTarget(frame, unit->TargetEntity, out var at, out _)) {
          attack++;
          if (_attackSeen.TryGetValue(entity, out var aSeen) == false || aSeen.Target != unit->TargetEntity) {
            aSeen = (unit->TargetEntity, Time.time);
            _attackSeen[entity] = aSeen;
          }
          // 가혹한 실선 붉은 선 제거: 명령/조준 시작 시 1.0초만 은은한 점선으로 페이드아웃하고 이후 교전은 노란/호박색 예광탄으로 표현.
          var aFade = 1f - Mathf.Clamp01((Time.time - aSeen.Since) / AttackLineDuration);
          if (aFade > 0.02f) {
            var line = Line(lines++, _attackLine);
            line.textureMode = LineTextureMode.Tile;
            line.widthMultiplier = AttackLineWidth * aFade;
            line.SetPosition(0, from);
            line.SetPosition(1, new Vector3(at.x, Mathf.Max(at.y, 1.1f), at.z));
          }
          if (_targeted.Add(unit->TargetEntity) && reticles < 12) {
            var ring = Reticle(reticles++);
            var radius = _actors.TryGetValue(unit->TargetEntity, out var targetActor) ? Mathf.Max(0.8f, targetActor.Radius) : 1.2f;
            var pulse = 1f + 0.12f * Mathf.Sin(Time.time * 6f);
            ring.transform.position = new Vector3(at.x, 0.12f, at.z);
            ring.transform.localScale = new Vector3(radius * pulse, 1f, radius * pulse);
            ring.transform.Rotate(0f, 40f * Time.deltaTime, 0f);
          }
          continue;
        }
        if ((unit->Order == UnitOrder.Move || unit->Order == UnitOrder.AttackMove)) {
          var goal = unit->TargetPosition.ToUnityVector3();
          goal.y = 0.15f;
          // 명령을 받은 뒤 0.8초 동안 보이고 0.6초에 걸쳐 가늘어지며 사라진다(새 목적지면 다시 처음부터).
          if (_moveSeen.TryGetValue(entity, out var seen) == false || (seen.Goal - goal).sqrMagnitude > 0.25f) {
            seen = (goal, Time.time);
            _moveSeen[entity] = seen;
          }
          var fade = 1f - Mathf.Clamp01((Time.time - seen.Since - MoveLineHold) / MoveLineFade);
          if ((goal - from).sqrMagnitude > 2.25f && fade > 0f) {
            var line = Line(lines++, _moveLine);
            line.textureMode = LineTextureMode.Tile;
            line.widthMultiplier = MoveLineWidth * fade;
            line.SetPosition(0, new Vector3(from.x, 0.15f, from.z));
            line.SetPosition(1, goal);
            move++;
          }
        }
      }
      for (var i = lines; i < _lines.Count; i++) {
        _lines[i].gameObject.SetActive(false);
      }
      for (var i = reticles; i < _reticles.Count; i++) {
        _reticles[i].SetActive(false);
      }
      AttackLines = attack;
      MoveLines = move;
    }

    /// <summary>체력바는 피해를 입었거나(100% 미만), 선택됐거나, 고른 아군이 조준 중일 때만 보인다.</summary>
    void DamageBars(Frame frame) {
      foreach (var pair in _actors) {
        var actor = pair.Value;
        if (actor.View == null || actor.View.Bar == null || _marked.Contains(actor)) {
          continue;
        }
        var ratio = 1f;
        if (frame.Unsafe.TryGetPointer<Unit>(pair.Key, out var unit)) {
          ratio = unit->Health.AsFloat / Mathf.Max(1f, TechSystem.MaxHealth(frame, unit->Owner, unit->Spec).AsFloat);
        } else if (frame.Unsafe.TryGetPointer<Building>(pair.Key, out var building)) {
          ratio = building->Health.AsFloat / Mathf.Max(1f, frame.FindAsset(building->Spec).MaxHealth.AsFloat);
        }
        var show = ratio < 0.995f || actor.InCombat || _targeted.Contains(pair.Key) || (actor.TransportCapacity > 0 && TransportSystem.UsedSlots(frame, pair.Key) > 0);
        if (actor.View.Bar.activeSelf != show) {
          actor.View.Bar.SetActive(show);
        }
        if (show) {
          actor.View.SetHealth(ratio);
        }
      }
    }

    /// <summary>수송 유닛·물류 트럭 후면 램프 개폐 애니메이션 및 월드 공간 탑승 인원 핍([■][■][■][□]) 갱신.</summary>
    void UpdateTransportVisual(Frame frame, Actor actor, EntityRef entity, Unit* unit, UnitSpec spec, float dt) {
      var isLogisticsTruck = frame.Has<LogisticsTruck>(entity) || spec.name == "merc_cargo_truck" || spec.name.Contains("truck");
      if (spec.TransportCapacity <= 0 && isLogisticsTruck == false) {
        return;
      }
      if (actor.CargoRamp == null) {
        actor.CargoRamp = HeroVisuals.AttachTransportRamp(actor.View.Turn, spec.IsAir, spec.IsNaval, actor.Color);
      }
      if (spec.TransportCapacity > 0 && actor.TransportCapacity != spec.TransportCapacity) {
        actor.TransportCapacity = spec.TransportCapacity;
        if (actor.PipRoot == null) {
          actor.PipRoot = HeroVisuals.CreatePassengerPips(actor.View, spec.TransportCapacity, out actor.PipRenderers);
        }
      }
      if (actor.CargoRamp != null) {
        var open = unit->RampOpenUntilFrame > frame.Number;
        var targetAngle = open ? (spec.IsNaval ? 74f : -74f) : 0f;
        actor.RampAngle = Mathf.MoveTowards(actor.RampAngle, targetAngle, 260f * dt);
        actor.CargoRamp.localRotation = Quaternion.Euler(actor.RampAngle, 0f, 0f);
      }
      if (spec.TransportCapacity > 0) {
        var used = TransportSystem.UsedSlots(frame, entity);
        UpdatePips(actor, used, used > 0 || _marked.Contains(actor));
      }
    }

    static void UpdatePips(Actor actor, int used, bool visible, float heightOffset = -0.36f) {
      if (actor.PipRoot == null || actor.PipRenderers == null) return;
      if (actor.PipRoot.activeSelf != visible) actor.PipRoot.SetActive(visible);
      if (!visible) return;
      var barY = actor.View.Bar != null ? actor.View.Bar.transform.localPosition.y : actor.View.Entry.BarHeight;
      actor.PipRoot.transform.localPosition = new Vector3(0f, barY + heightOffset, 0f);
      var camera = Camera.main;
      if (camera != null) actor.PipRoot.transform.rotation = camera.transform.rotation;
      for (var i = 0; i < actor.PipRenderers.Length; i++) {
        var mat = HeroVisuals.PipMaterial(i < used);
        if (actor.PipRenderers[i].sharedMaterial != mat) actor.PipRenderers[i].sharedMaterial = mat;
      }
    }

    void Mark(Actor actor, float health) {
      actor.View.SetSelected(true);
      actor.View.SetHealth(health);
      _marked.Add(actor);
    }
  }
}
