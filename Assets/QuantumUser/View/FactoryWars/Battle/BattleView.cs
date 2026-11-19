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
      public readonly List<(Transform Part, Vector3 Home, Vector3 Reach, float Speed)> Sliders = new List<(Transform, Vector3, Vector3, float)>();
      public readonly List<(Transform Part, Quaternion Home, float Swing, float Speed)> Swingers = new List<(Transform, Quaternion, float, float)>();
      public GameObject Scaffold;
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
      public TextMesh UpgradeChevron;
      public bool IsCarrier;
      public Transform SiloDoorL;
      public Transform SiloDoorR;
      public Transform SiloMissile;
      public Renderer FactoryBeacon;
      public Vector3[] FactorySmoke;
      public int FactoryLevel = 1;
      public GameObject FactoryUpgradeGo;
      public float OverdriveFxAt;
    }

    readonly List<(GameObject Root, Transform Turn, Vector3 StartPos, float Elapsed, float NextSmoke)> _crashingCarriers =
      new List<(GameObject, Transform, Vector3, float, float)>();
    readonly List<(GameObject Rocket, Vector3 Velocity, float Until, float NextTrail)> _flyingMissiles =
      new List<(GameObject, Vector3, float, float)>();
    readonly Dictionary<EntityRef, GameObject> _strikeRockets = new Dictionary<EntityRef, GameObject>();
    readonly Dictionary<(EntityRef, EntityRef), GameObject> _synergyPipes = new Dictionary<(EntityRef, EntityRef), GameObject>();
    float _synergyPipeAt;
    float _placementRingAt;
    float _rotorSmokeAt;
    float _zoneFxAt;

    class BaseWorker {
      public GameObject Root;
      public Vector3 From;
      public Vector3 To;
      public float Phase;
    }

    static Mesh _scaffold;

    readonly Dictionary<EntityRef, Actor> _actors = new Dictionary<EntityRef, Actor>();
    readonly List<BaseWorker> _baseWorkers = new List<BaseWorker>();
    readonly HashSet<EntityRef> _seen = new HashSet<EntityRef>();
    readonly List<EntityRef> _gone = new List<EntityRef>();
    readonly List<Actor> _marked = new List<Actor>();
    ViewCatalog _catalog;
    MatchController _controller;
    FogOverlay _fog;
    /// <summary>시험용: 지금 안개 판.</summary>
    public FogOverlay Fog => _fog;
    static bool FactoryWars_FogOn(Frame frame) => Quantum.FactoryWars.FogSystem.Enabled(frame);

    public WorldView World { get; private set; }
    public BattlefieldView Field { get; private set; }
    public CityView City { get; private set; }
    public BattleFx Fx { get; private set; }
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
    readonly List<(Vector3 Position, float Until)> _signals = new List<(Vector3, float)>();
    float _signalAt;

    void Init(Frame frame) {
      _stage = new GameObject("Stage").transform;
      _stage.SetParent(transform, false);
      GameAudio.InstallProceduralDefaults();
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
      Fx = new GameObject("Fx").AddComponent<BattleFx>();
      Fx.transform.SetParent(_stage, false);
      World.Fx = Fx;
      scars.Fx = Fx;
      scars.Init(frame.FindAsset<MapSpec>(frame.Map.UserAsset));
      Field = new BattlefieldView(_stage);
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
      QuantumEvent.Subscribe<EventTraderShot>(this, e => {
        var to = e.To.ToUnityVector3() + Vector3.up * 0.8f;
        var from = _trader != null ? _trader.ShotOrigin(e.From.ToUnityVector3(), to) : e.From.ToUnityVector3() + Vector3.up * 3f;
        var dir = (to - from).normalized;
        Fx.MuzzleFlash(from, dir, 0.45f);
        Fx.Needle(from + dir * 0.1f, to, 90f);
        Fx.Impact(to, 0.8f, dir);
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
        if (e.Stage == UltimateStage.Launched && e.Kind == UltimateKind.StrategicMissile) {
          var siloPos = FindSiloPosition(e.Game.Frames.Verified, e.Player, e.Position.ToUnityVector3());
          Fx.SiloSteam(siloPos);
        } else if (e.Stage == UltimateStage.Impact) {
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
      _strikeRockets.Clear();
      _baseWorkers.Clear();
      _marked.Clear();
      World = null;
    }

    void UpdateBaseWorkers(Frame frame) {
      var kind = Dressing.Has(DressKind.AnimatedWorker) ? DressKind.AnimatedWorker : DressKind.Worker;
      var allow = GraphicsQuality.Current != QualityTier.Low && Dressing.Has(kind);
      if (allow && _baseWorkers.Count == 0 && World?.Map?.StartPositions != null) {
        for (var i = 0; i < Mathf.Min(frame.PlayerCount, World.Map.StartPositions.Length) && _baseWorkers.Count < 8; i++) {
          if (frame.Global->Players.GetPointer(i)->Active == false) {
            continue;
          }
          var home = Quantum.FactoryWars.FactoryWarsUtil.StartPosition(frame, i).ToUnityVector3();
          for (var w = 0; w < 2 && _baseWorkers.Count < 8; w++) {
            var from = home + new Vector3(w == 0 ? 4.2f : -4.2f, 0f, 3.2f);
            var to = home + new Vector3(w == 0 ? 8.5f : -8.5f, 0f, -4.5f);
            var worker = DressingSpawn.Place(kind, _stage, from, 0f, 9500 + i * 11 + w);
            if (worker != null) {
              worker.name = "BaseWorker";
              var ws = Mathf.Max(0.0001f, worker.transform.localScale.y);
              var hat = DressingSpawn.Place(DressKind.HardHat, worker.transform, new Vector3(0f, 1.72f / ws, 0f), 0f, 9600 + i * 11 + w);
              if (hat != null) {
                hat.name = "HardHat";
                hat.transform.localScale /= ws;
              }
              if (Dressing.Map != null && Dressing.Map.WorkerRun != null) {
                var targetObj = worker.GetComponentInChildren<Animation>()?.gameObject ?? (worker.transform.childCount > 0 ? worker.transform.GetChild(0).gameObject : worker);
                var anim = targetObj.GetComponent<Animation>() ?? targetObj.AddComponent<Animation>();
                Dressing.Map.WorkerRun.legacy = true;
                Dressing.Map.WorkerRun.wrapMode = WrapMode.Loop;
                anim.AddClip(Dressing.Map.WorkerRun, "run");
                if (Dressing.Map.WorkerIdle != null) {
                  Dressing.Map.WorkerIdle.legacy = true;
                  Dressing.Map.WorkerIdle.wrapMode = WrapMode.Loop;
                  anim.AddClip(Dressing.Map.WorkerIdle, "idle");
                }
                anim.clip = Dressing.Map.WorkerRun;
                anim.Play("run");
              }
              _baseWorkers.Add(new BaseWorker { Root = worker, From = from, To = to, Phase = i * 1.7f + w * 2.3f });
            }
          }
        }
      }
      var time = Time.time;
      foreach (var worker in _baseWorkers) {
        if (worker.Root == null) {
          continue;
        }
        if (worker.Root.activeSelf != allow) {
          worker.Root.SetActive(allow);
        }
        if (allow == false) {
          continue;
        }
        var ping = Mathf.PingPong(time * 0.22f + worker.Phase, 1f);
        var pos = Vector3.Lerp(worker.From, worker.To, ping);
        pos.y = TerrainKit.SurfaceHeight(World.Map, pos.x, pos.z) + Mathf.Abs(Mathf.Sin((time * 4.5f + worker.Phase) * Mathf.PI)) * 0.08f;
        var dir = ping < Mathf.PingPong((time + 0.05f) * 0.22f + worker.Phase, 1f) ? (worker.To - worker.From) : (worker.From - worker.To);
        worker.Root.transform.localPosition = pos;
        if (dir.sqrMagnitude > 0.01f) {
          worker.Root.transform.localRotation = Quaternion.Euler(0f, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, 0f);
        }
      }
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
      World.Update(frame, dt);
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
      World.ShowNoBuild(_controller != null && _controller.Input != null && _controller.Input.IsPlacing);
      UpdatePlacementRings(frame);
      UpdateStrikeRockets(frame, dt);
      UpdateScorchedAndEmpFx(frame);
      // 항목 33: 날아오는 전략 미사일의 착탄 지점을 모든 플레이어에게 보인다(폭발 반경만큼 붉은 고리 + 연막 신호).
      if (Time.time >= _strikeAt) {
        _strikeAt = Time.time + 0.3f;
        StrikeMarks = 0;
        foreach (var pair in frame.GetComponentIterator<MissileStrike>()) {
          var target = pair.Component.Target.ToUnityVector3();
          var radius = frame.FindAsset(pair.Component.Spec).BlastRadius.AsFloat;
          // 붉은 레이저 조준 레티클: 외곽 반경 고리 + 박동 내부 고리 + 십자선 레이저 4줄 + 상공 수직 유도 레이저.
          var pulse = 0.25f + 0.45f * Mathf.PingPong(Time.time * 2.8f, 1f);
          Fx.Ring(target, StrikeColor, radius, 0.45f);
          Fx.Ring(target, new Color(1f, 0.14f, 0.14f, 0.95f), radius * pulse, 0.35f);
          Fx.Ring(target, new Color(1f, 0.28f, 0.18f, 0.9f), 2.2f, 0.35f);
          Fx.Needle(target + Vector3.up * 26f, target + Vector3.up * 0.2f, 130f);
          foreach (var dir in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right }) {
            Fx.Needle(target + dir * (radius * 0.85f) + Vector3.up * 0.25f, target + dir * 1.4f + Vector3.up * 0.25f, 80f);
          }
          Fx.Signal(target);
          StrikeMarks++;
        }
      }
      UpdateSuperweaponVisuals(dt);

      _seen.Clear();
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
      var sorties = frame.Filter<Transform3D, Sortie>();
      while (sorties.NextUnsafe(out var entity, out var transform3D, out var sortie)) {
        _seen.Add(entity);
        UpdateSortie(frame, entity, transform3D, sortie, dt);
      }

      _gone.Clear();
      foreach (var pair in _actors) {
        if (_seen.Contains(pair.Key) == false) {
          _gone.Add(pair.Key);
        }
      }
      foreach (var entity in _gone) {
        Remove(entity);
      }
      UpdateBaseWorkers(frame);
      UpdateSelection(frame);
    }

    void Remove(EntityRef entity) {
      var actor = _actors[entity];
      var center = actor.View.transform.position + Vector3.up * (actor.IsAir ? actor.View.Entry.Altitude : actor.Height * 0.4f);
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
      Fx.Explosion(center, actor.IsBuilding ? 2.2f : Mathf.Clamp(actor.Radius * 0.5f, 0.5f, 1.5f));
      if (actor.IsBuilding) {
        // 파괴된 기지 건물은 사라지지 않고 영구 붕괴 콘크리트 잔해 더미 + 영구 그을음으로 남는다.
        Fx.PermanentRubble(actor.View.transform.position, Mathf.Max(2.8f, actor.Radius * 0.9f));
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
    }

    Actor Create(EntityRef entity, string id, bool isBuilding, bool isAir) {
      var view = ArtView.Create(_catalog, id, _stage, 0);
      if (view == null) {
        return null;
      }
      var actor = new Actor { View = view, Entity = entity, IsBuilding = isBuilding, IsAir = isAir, IsNaval = id.Contains("boat") || id.Contains("ship"), Radius = view.Entry.RingRadius, Height = view.Entry.BarHeight, Phase = Random.value * 10f };
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
      if (id == "ff_stormnest_carrier") {
        BuildHelicarrierVisual(actor);
      } else if (id == "cf_missile_silo") {
        BuildMissileSiloVisual(actor);
      } else if (id.EndsWith("_patriot_turret")) {
        BuildPatriotAccent(actor);
      } else if (id == "ff_artillery_ship" || id == "cf_artillery_ship") {
        HeroVisuals.BuildArtilleryShip(actor.View, actor.Color);
      } else if (isBuilding && HeroVisuals.TryBuildFactory(actor.View, id, actor.Color, out var chimneys, out var beacon, out var radarDish)) {
        actor.FactorySmoke = chimneys;
        actor.FactoryBeacon = beacon;
        if (radarDish != null) {
          actor.Spinners.Add((radarDish, Vector3.up, 52f, true));
        }
      }
      _actors[entity] = actor;
      return actor;
    }

    void BuildPatriotAccent(Actor actor) {
      var kit = new MeshKit();
      kit.Paint(Swatch.ArmorDark).Chamfer(new Vector3(0f, 0.9f, 0f), new Vector3(1.5f, 0.55f, 1.5f), 0.12f);
      for (var c = 0; c < 4; c++) {
        var ox = (c % 2 == 0 ? -0.34f : 0.34f);
        var oy = 1.5f + (c / 2) * 0.38f;
        kit.Paint(Swatch.MetalLight).Box(new Vector3(ox, oy, 0.1f), new Vector3(0.28f, 0.28f, 1.65f));
        kit.Paint(Swatch.GlowCyan).Box(new Vector3(ox, oy, 0.96f), new Vector3(0.22f, 0.22f, 0.08f));
      }
      var go = new GameObject("PatriotQuadRack", typeof(MeshFilter), typeof(MeshRenderer));
      go.transform.SetParent(actor.View.Model.transform, false);
      go.transform.localRotation = Quaternion.Euler(-28f, 0f, 0f);
      go.GetComponent<MeshFilter>().sharedMesh = kit.ToMesh("patriot_rack");
      go.GetComponent<MeshRenderer>().sharedMaterial = ArtLook.NeutralMaterial;
      ArtLook.Apply(go, actor.Color, true);
    }

    void UpdatePlacementRings(Frame frame) {
      if (_controller == null || _controller.Input == null || _controller.Input.IsPlacing == false || Time.time < _placementRingAt) {
        return;
      }
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
      // 카메라 중앙/포인터 지점의 건설 가능 여부를 녹색/적색 링으로 실시간 표시.
      var cam = Camera.main;
      if (cam != null) {
        var ray = cam.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));
        if (new UnityEngine.Plane(Vector3.up, Vector3.zero).Raycast(ray, out var hitDist)) {
          var world = ray.GetPoint(hitDist);
          var fp = new FPVector3(FP.FromFloat_UNSAFE(world.x), FP._0, FP.FromFloat_UNSAFE(world.z));
          var valid = ProductionSystem.CanPlace(frame, spec, fp, player);
          Fx.Ring(world, valid ? new Color(0.18f, 0.96f, 0.42f, 0.95f) : new Color(0.98f, 0.22f, 0.18f, 0.95f), spec.FootprintRadius.AsFloat + 0.6f, 0.32f);
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
          rocket = new GameObject("StrikeICBM", typeof(MeshFilter), typeof(MeshRenderer));
          rocket.transform.SetParent(_stage, false);
          var kit = new MeshKit();
          kit.Paint(Swatch.White).Cylinder(new Vector3(0f, 0f, 0f), 0.46f, 3.0f, 10);
          kit.Paint(Swatch.GlowRed).Cone(new Vector3(0f, 1.95f, 0f), 0.48f, 1.0f, 10);
          rocket.GetComponent<MeshFilter>().sharedMesh = kit.ToMesh("strike_icbm");
          rocket.GetComponent<MeshRenderer>().sharedMaterial = ArtLook.NeutralMaterial;
          rocket.transform.position = pos;
          _strikeRockets[pair.Entity] = rocket;
        }
        var delta = pos - rocket.transform.position;
        if (delta.sqrMagnitude > 0.001f) {
          rocket.transform.rotation = Quaternion.LookRotation(delta.normalized) * Quaternion.Euler(90f, 0f, 0f);
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
      var go = new GameObject("FlyingICBM", typeof(MeshFilter), typeof(MeshRenderer));
      go.transform.SetParent(_stage, false);
      go.transform.position = start;
      go.transform.rotation = velocity.y >= 0f ? Quaternion.identity : Quaternion.Euler(180f, 0f, 0f);
      var kit = new MeshKit();
      kit.Paint(Swatch.White).Cylinder(new Vector3(0f, 1.4f, 0f), 0.42f, 2.8f, 10);
      kit.Paint(Swatch.MetalDark).Cylinder(new Vector3(0f, 0.3f, 0f), 0.46f, 0.6f, 10);
      kit.Paint(Swatch.GlowRed).Cone(new Vector3(0f, 3.25f, 0f), 0.44f, 0.95f, 10);
      for (var f = 0; f < 4; f++) {
        var yaw = f * 90f;
        kit.Paint(Swatch.GlowRed).Box(Quaternion.Euler(0f, yaw, 0f) * new Vector3(0.45f, 0.35f, 0f), Quaternion.Euler(0f, yaw, 0f) * new Vector3(0.38f, 0.65f, 0.08f));
      }
      go.GetComponent<MeshFilter>().sharedMesh = kit.ToMesh("icbm_rocket");
      go.GetComponent<MeshRenderer>().sharedMaterial = ArtLook.NeutralMaterial;
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

    void BuildHelicarrierVisual(Actor actor) {
      actor.IsCarrier = true;
      actor.View.Slot = null;
      var model = actor.View.Model.transform;
      model.localPosition = Vector3.zero;
      model.localRotation = Quaternion.identity;
      model.localScale = Vector3.one * 1.35f;
      foreach (var r in model.GetComponents<Renderer>()) {
        r.enabled = false;
      }
      for (var i = model.childCount - 1; i >= 0; i--) {
        DestroyImmediate(model.GetChild(i).gameObject);
      }
      actor.Spinners.Clear();
      var kit = new MeshKit();
      // 장갑 선체 + 상부 활주 비행갑판 + 우현 함교 아일랜드 + 발광 활주로 유도선(GlowCyan).
      kit.Team().Chamfer(new Vector3(0f, 0.1f, 0f), new Vector3(2.2f, 0.75f, 5.4f), 0.22f);
      kit.Paint(Swatch.MetalDark).Chamfer(new Vector3(0f, 0.58f, 0.1f), new Vector3(3.4f, 0.24f, 6.2f), 0.14f);
      kit.Paint(Swatch.ConcreteDark).Box(new Vector3(-0.35f, 0.72f, 0.1f), new Vector3(1.55f, 0.05f, 5.8f));
      kit.Team().Chamfer(new Vector3(1.18f, 1.15f, -0.35f), new Vector3(0.68f, 0.95f, 1.45f), 0.08f);
      kit.Paint(Swatch.GlowCyan).Box(new Vector3(1.18f, 1.38f, 0.39f), new Vector3(0.52f, 0.22f, 0.08f));
      // 활주로 청록색 발광 유도등 2열.
      for (var s = -5; s <= 5; s++) {
        var z = s * 0.5f;
        kit.Paint(Swatch.GlowCyan).Box(new Vector3(-1.02f, 0.75f, z), new Vector3(0.11f, 0.06f, 0.28f));
        kit.Paint(Swatch.GlowCyan).Box(new Vector3(0.32f, 0.75f, z), new Vector3(0.11f, 0.06f, 0.28f));
      }
      // 4기 VTOL 터빈 나셀 암 & 덕트 링.
      var mounts = new[] {
        new Vector3(-2.35f, 0.42f, -1.75f),
        new Vector3(2.35f, 0.42f, -1.75f),
        new Vector3(-2.45f, 0.42f, 1.85f),
        new Vector3(2.45f, 0.42f, 1.85f),
      };
      for (var m = 0; m < mounts.Length; m++) {
        var p = mounts[m];
        kit.Paint(Swatch.MetalDark).Box(new Vector3(p.x * 0.55f, p.y, p.z), new Vector3(Mathf.Abs(p.x), 0.22f, 0.42f));
        kit.Team().Cylinder(p, 0.92f, 0.34f, 14);
        kit.Paint(Swatch.Black).Cylinder(p + Vector3.up * 0.04f, 0.76f, 0.36f, 14);
        kit.Paint(Swatch.GlowCyan).Cylinder(p + Vector3.up * 0.08f, 0.24f, 0.38f, 10);

        var rotorGo = new GameObject($"VTOLRotor{m}", typeof(MeshFilter), typeof(MeshRenderer));
        rotorGo.transform.SetParent(model, false);
        rotorGo.transform.localPosition = p + Vector3.up * 0.16f;
        var rKit = new MeshKit();
        rKit.Paint(Swatch.MetalLight).Box(Vector3.zero, new Vector3(1.42f, 0.04f, 0.16f));
        rKit.Paint(Swatch.MetalLight).Box(Vector3.zero, new Vector3(0.16f, 0.04f, 1.42f));
        rKit.Paint(Swatch.GlowCyan).Cylinder(Vector3.zero, 0.22f, 0.12f, 8);
        rotorGo.GetComponent<MeshFilter>().sharedMesh = rKit.ToMesh($"vtol_rotor_{m}");
        rotorGo.GetComponent<MeshRenderer>().sharedMaterial = ArtLook.NeutralMaterial;
        actor.Spinners.Add((rotorGo.transform, Vector3.up, 1480f * (m % 2 == 0 ? 1f : -1f), false));
      }
      var hullGo = new GameObject("HelicarrierHull", typeof(MeshFilter), typeof(MeshRenderer));
      hullGo.transform.SetParent(model, false);
      hullGo.GetComponent<MeshFilter>().sharedMesh = kit.ToMesh("helicarrier_hull");
      hullGo.GetComponent<MeshRenderer>().sharedMaterial = ArtLook.Team(actor.Color, true) ?? ArtLook.NeutralMaterial;

      var radarGo = new GameObject("BridgeRadar", typeof(MeshFilter), typeof(MeshRenderer));
      radarGo.transform.SetParent(model, false);
      radarGo.transform.localPosition = new Vector3(1.18f, 1.75f, -0.35f);
      var radKit = new MeshKit();
      radKit.Paint(Swatch.GlowCyan).Box(Vector3.zero, new Vector3(0.65f, 0.22f, 0.1f));
      radarGo.GetComponent<MeshFilter>().sharedMesh = radKit.ToMesh("helicarrier_radar");
      radarGo.GetComponent<MeshRenderer>().sharedMaterial = ArtLook.Team(actor.Color, true) ?? ArtLook.NeutralMaterial;
      actor.Spinners.Add((radarGo.transform, Vector3.up, 180f, false));

      // 건물·도로 위를 스쳐 지나가는 거대한 지상 그림자 데칼(CarrierShadow).
      var shadowGo = new GameObject("CarrierShadow", typeof(MeshFilter), typeof(MeshRenderer));
      shadowGo.transform.SetParent(actor.View.transform, false);
      shadowGo.transform.localPosition = new Vector3(0f, 0.08f, 0f);
      var sMesh = new Mesh { name = "carrier_ground_shadow" };
      const float sx = 7.2f;
      const float sz = 9.4f;
      sMesh.vertices = new[] { new Vector3(-sx, 0f, -sz), new Vector3(-sx, 0f, sz), new Vector3(sx, 0f, sz), new Vector3(sx, 0f, -sz) };
      sMesh.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
      sMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
      sMesh.RecalculateNormals();
      shadowGo.GetComponent<MeshFilter>().sharedMesh = sMesh;
      var sRenderer = shadowGo.GetComponent<MeshRenderer>();
      sRenderer.sharedMaterial = BattleFx.ScorchMaterial();
      sRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
      sRenderer.receiveShadows = false;

      // 깔끔하게 회전하는 전술 지휘 오라 링(CarrierAuraRing, 반경 14m, 얇은 세그먼트 링).
      var auraGo = new GameObject("CarrierAuraRing", typeof(MeshFilter), typeof(MeshRenderer));
      auraGo.transform.SetParent(actor.View.transform, false);
      auraGo.transform.localPosition = new Vector3(0f, 0.14f, 0f);
      var auraKit = new MeshKit().Paint(Swatch.GlowCyan);
      ArtView.DashedRing(auraKit, 13.78f, 14.0f, 72);
      auraGo.GetComponent<MeshFilter>().sharedMesh = auraKit.ToMesh("carrier_aura_ring");
      var auraMat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
      auraMat.color = new Color(0.22f, 0.90f, 1f, 0.42f);
      if (auraMat.HasProperty("_Surface")) auraMat.SetFloat("_Surface", 1f);
      if (auraMat.HasProperty("_SrcBlend")) auraMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
      if (auraMat.HasProperty("_DstBlend")) auraMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
      if (auraMat.HasProperty("_ZWrite")) auraMat.SetFloat("_ZWrite", 0f);
      auraMat.renderQueue = 3000;
      var auraRend = auraGo.GetComponent<MeshRenderer>();
      auraRend.sharedMaterial = auraMat;
      auraRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
      auraRend.receiveShadows = false;
      actor.Spinners.Add((auraGo.transform, Vector3.up, 14f, false));
      ArtLook.Apply(actor.View.Model, actor.Color, true);
    }

    void BuildMissileSiloVisual(Actor actor) {
      actor.View.Slot = null;
      var model = actor.View.Model.transform;
      model.localPosition = Vector3.zero;
      model.localRotation = Quaternion.identity;
      model.localScale = Vector3.one * 1.65f;
      foreach (var r in model.GetComponents<Renderer>()) {
        r.enabled = false;
      }
      for (var i = model.childCount - 1; i >= 0; i--) {
        DestroyImmediate(model.GetChild(i).gameObject);
      }
      var kit = new MeshKit();
      // 철근 콘크리트 요새 벙커 + 중앙 발사 수직 갱도 + 경보 램프.
      kit.Paint(Swatch.ConcreteDark).Chamfer(new Vector3(0f, 0.55f, 0f), new Vector3(4.6f, 1.1f, 4.6f), 0.35f);
      kit.Team().Chamfer(new Vector3(0f, 1.18f, 0f), new Vector3(3.6f, 0.36f, 3.6f), 0.2f);
      kit.Paint(Swatch.Black).Cylinder(new Vector3(0f, 0.85f, 0f), 1.25f, 1.5f, 16);
      kit.Paint(Swatch.MetalDark).Box(new Vector3(-1.95f, 1.65f, 0f), new Vector3(0.35f, 1.3f, 2.8f));
      kit.Paint(Swatch.MetalDark).Box(new Vector3(1.95f, 1.65f, 0f), new Vector3(0.35f, 1.3f, 2.8f));
      kit.Paint(Swatch.GlowRed).Box(new Vector3(-1.95f, 2.4f, 1.2f), new Vector3(0.28f, 0.28f, 0.28f));
      kit.Paint(Swatch.GlowRed).Box(new Vector3(1.95f, 2.4f, -1.2f), new Vector3(0.28f, 0.28f, 0.28f));
      var bunkerGo = new GameObject("SiloBunker", typeof(MeshFilter), typeof(MeshRenderer));
      bunkerGo.transform.SetParent(model, false);
      bunkerGo.GetComponent<MeshFilter>().sharedMesh = kit.ToMesh("silo_bunker");
      bunkerGo.GetComponent<MeshRenderer>().sharedMaterial = ArtLook.Team(actor.Color, true) ?? ArtLook.NeutralMaterial;

      // 좌우 슬라이딩 방폭문(BlastDoorL / BlastDoorR).
      for (var side = -1; side <= 1; side += 2) {
        var dKit = new MeshKit();
        dKit.Paint(Swatch.MetalLight).Chamfer(Vector3.zero, new Vector3(1.32f, 0.24f, 2.65f), 0.06f);
        dKit.Paint(Swatch.GlowRed).Box(new Vector3(-side * 0.52f, 0.14f, 0f), new Vector3(0.16f, 0.05f, 2.4f));
        var doorGo = new GameObject(side < 0 ? "BlastDoorL" : "BlastDoorR", typeof(MeshFilter), typeof(MeshRenderer));
        doorGo.transform.SetParent(model, false);
        doorGo.transform.localPosition = new Vector3(side * 0.68f, 1.48f, 0f);
        doorGo.GetComponent<MeshFilter>().sharedMesh = dKit.ToMesh(side < 0 ? "silo_door_l" : "silo_door_r");
        doorGo.GetComponent<MeshRenderer>().sharedMaterial = ArtLook.NeutralMaterial;
        if (side < 0) {
          actor.SiloDoorL = doorGo.transform;
        } else {
          actor.SiloDoorR = doorGo.transform;
        }
      }

      // 격납고 내부 수직 ICBM 탄두 미사일.
      var mKit = new MeshKit();
      mKit.Paint(Swatch.White).Cylinder(new Vector3(0f, 1.3f, 0f), 0.44f, 2.6f, 12);
      mKit.Paint(Swatch.GlowRed).Cone(new Vector3(0f, 3.05f, 0f), 0.45f, 0.95f, 12);
      var mGo = new GameObject("SiloMissile", typeof(MeshFilter), typeof(MeshRenderer));
      mGo.transform.SetParent(model, false);
      mGo.transform.localPosition = new Vector3(0f, 0.1f, 0f);
      mGo.GetComponent<MeshFilter>().sharedMesh = mKit.ToMesh("silo_icbm");
      mGo.GetComponent<MeshRenderer>().sharedMaterial = ArtLook.NeutralMaterial;
      actor.SiloMissile = mGo.transform;
      ArtLook.Apply(actor.View.Model, actor.Color, true);
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
      if (unitId.Contains("engineer")) return DressKind.EngineerCarbine;
      return DressKind.Rifle;
    }

    /// <summary>보병 무기 프리팹(팩 소총·RPG·유탄·카빈). 칸이 비어 있으면 기본 소총으로 폴백.</summary>
    public static GameObject Weapon(DressKind kind) {
      var entry = Dressing.Map != null ? Dressing.Map.Get(kind) : null;
      if (entry != null && entry.Variants.Count > 0) {
        return entry.Variants[0].Prefab;
      }
      if (kind != DressKind.Rifle && Dressing.Map != null) {
        var fallback = Dressing.Map.Get(DressKind.Rifle);
        if (fallback != null && fallback.Variants.Count > 0) {
          return fallback.Variants[0].Prefab;
        }
      }
      return null;
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
      if (actor.View.gameObject.activeSelf) {
        GameAudio.Play(attack == AttackType.AntiArmor || attack == AttackType.Area ? SoundEvent.TankCannon : SoundEvent.RifleFire);
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
        if (spec.IsVehicle == false && spec.IsAir == false && spec.IsNaval == false && (spec.Size == UnitSize.Small || spec.Size == UnitSize.Engineer)) {
          actor.Rigs.AddRange(RiflemanRig.AttachAll(actor.View.Model, Weapon(WeaponKindFor(spec.name)), spec.name.Contains("at_rocket")));
        }
      }
      var color = frame.Global->Players.GetPointer(unit->Owner)->ColorIndex;
      if (color != actor.Color) {
        actor.Color = color;
        ArtLook.Apply(actor.View.Model, color);
      }
      // 진지 안: 모델은 숨기고 사격은 건물 창문에서 보인다.
      if (unit->Garrison != EntityRef.None) {
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
      var hover = (spec.IsAir && assemblingCarrier == false) ? Mathf.Sin(Time.time * 1.6f + actor.Phase) * 0.14f : 0f;
      actor.View.transform.position = (spec.IsAir && assemblingCarrier == false) ? actor.Position : actor.Position + Vector3.up * TerrainKit.SurfaceHeight(World.Map, actor.Position.x, actor.Position.z);
      var altitude = assemblingCarrier ? 0.35f : (actor.IsCarrier ? Mathf.Max(actor.View.Entry.Altitude, 10.5f) : actor.View.Entry.Altitude);
      actor.View.Turn.localPosition = Vector3.up * (altitude + hover);
      if (actor.IsCarrier && actor.View.Bar != null) {
        actor.View.Bar.transform.localPosition = Vector3.up * (altitude + 3.6f);
      }

      var hasTarget = TryTarget(frame, unit->TargetEntity, out var target, out var air);
      foreach (var rig in actor.Rigs) {
        rig.Aiming = hasTarget;
        rig.AimPoint = target;
      }
      if (moved.sqrMagnitude > 0.002f) {
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
        actor.View.Animation.Set(AnimState(speed, hasTarget, workingAnim ? wAnim->Task : WorkerTask.Idle), speed);
      }
    }

    /// <summary>
    /// 항목 32: 차량은 가감속에 따라 앞뒤로, 헬기·비행기는 선회 방향으로 옆으로 기울고 속도만큼 앞으로 숙인다. 보병·배는 그대로.
    /// 값은 VisualProfile(화면 전용). 시뮬레이션은 읽기만 한다.
    /// </summary>
    void Tilt(Actor actor, UnitSpec spec, Vector3 before, float dt) {
      if (dt <= 0f || spec.IsNaval || (spec.Size == UnitSize.Small && spec.IsVehicle == false && spec.IsAir == false) || spec.Size == UnitSize.Engineer) {
        return;
      }
      var profile = VisualProfile.Current;
      var velocity = (actor.Position - before) / dt;
      var accel = (velocity - actor.Velocity) / dt;
      actor.Velocity = Vector3.Lerp(actor.Velocity, velocity, 1f - Mathf.Exp(-8f * dt));
      var forward = Quaternion.Euler(0, actor.HullYaw, 0) * Vector3.forward;
      float pitch, roll = 0f;
      if (spec.IsAir) {
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

    /// <summary>Mk.1 -> Mk.2 -> Mk.3 시각 진화(절차적 장갑판·포신 보강·진영 발광 트림 + 은/금 셰브론 배지).</summary>
    void UpdateUpgradeVisual(Frame frame, Actor actor, Unit* unit, UnitSpec spec) {
      if (spec.Size == UnitSize.Engineer || actor.IsCarrier) {
        return;
      }
      var mark = TechSystem.UnitMark(frame, unit->Owner, spec);
      actor.IsFreeFaction = TechSystem.FactionIdOf(frame, unit->Owner) == "free_federation";
      if (mark != actor.UpgradeMark) {
        actor.UpgradeMark = mark;
        if (actor.UpgradeKit != null) {
          Destroy(actor.UpgradeKit);
          actor.UpgradeKit = null;
        }
        if (mark >= 2) {
          var kit = new MeshKit();
          var glow = actor.IsFreeFaction ? Swatch.GlowCyan : Swatch.GlowRed;
          var infantry = spec.Size == UnitSize.Small && spec.IsVehicle == false && spec.IsAir == false && spec.IsNaval == false;
          if (infantry) {
            // 보병 Mk.2: 강화 어깨·흉부 장갑판 / Mk.3: 중장갑 견갑 + 진영 발광 바이저·백팩 코어.
            kit.Paint(Swatch.ArmorDark).Box(new Vector3(-0.24f, 1.32f, 0f), new Vector3(0.14f, 0.14f, 0.22f));
            kit.Paint(Swatch.ArmorDark).Box(new Vector3(0.24f, 1.32f, 0f), new Vector3(0.14f, 0.14f, 0.22f));
            kit.Paint(Swatch.MetalLight).Box(new Vector3(0f, 1.18f, 0.14f), new Vector3(0.3f, 0.26f, 0.08f));
            if (mark >= 3) {
              kit.Paint(glow).Box(new Vector3(0f, 1.56f, 0.13f), new Vector3(0.2f, 0.07f, 0.08f));
              kit.Paint(Swatch.ArmorDark).Box(new Vector3(0f, 1.24f, -0.18f), new Vector3(0.28f, 0.34f, 0.14f));
              kit.Paint(glow).Box(new Vector3(0f, 1.26f, -0.26f), new Vector3(0.18f, 0.16f, 0.05f));
            }
          } else {
            var s = spec.Size == UnitSize.Large ? 1.45f : spec.Size == UnitSize.Medium ? 1.05f : 0.72f;
            var y = spec.IsAir ? 0.25f : 0.52f * s;
            // 기갑·차량·항공 Mk.2: 반응장갑 사이드 스커트 + 연장 포신 덮개.
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
          }
          var go = new GameObject($"UpgradeKit_Mk{mark}", typeof(MeshFilter), typeof(MeshRenderer));
          go.transform.SetParent(actor.View.Model.transform, false);
          go.GetComponent<MeshFilter>().sharedMesh = kit.ToMesh($"upgrade_mk{mark}_{spec.name}");
          go.GetComponent<MeshRenderer>().sharedMaterial = ArtLook.NeutralMaterial;
          ArtLook.Apply(go, actor.Color, true);
          actor.UpgradeKit = go;
        }
      }
      if (mark <= 1) {
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
        tm.characterSize = 0.11f;
        tm.fontStyle = FontStyle.Bold;
        actor.UpgradeChevron = tm;
      }
      actor.UpgradeChevron.text = mark >= 3 ? "^^ Mk.3" : "^ Mk.2";
      actor.UpgradeChevron.color = mark >= 3 ? new Color(1f, 0.84f, 0.2f, 1f) : new Color(0.86f, 0.91f, 0.97f, 1f);
      actor.UpgradeChevron.transform.localPosition = Vector3.up * (actor.View.Entry.BarHeight + (actor.VetRank > 0 ? 0.92f : 0.44f));
      if (actor.UpgradeChevron.gameObject.activeSelf == false) {
        actor.UpgradeChevron.gameObject.SetActive(true);
      }
      var cam = Camera.main;
      if (cam != null) {
        actor.UpgradeChevron.transform.rotation = cam.transform.rotation;
      }
    }

    /// <summary>교체 모델 애니메이션 상태(항목 30). 시뮬레이션 상태를 읽기만 한다.</summary>
    public static UnitAnimState AnimState(float speed, bool hasTarget, WorkerTask task) {
      if (task == WorkerTask.Build || task == WorkerTask.Repair) {
        return UnitAnimState.Build;
      }
      if (task != WorkerTask.Idle && task != WorkerTask.Deliver && speed < 0.3f) {
        return UnitAnimState.Gather;
      }
      if (speed >= 0.3f) {
        return UnitAnimState.Move;
      }
      return hasTarget ? UnitAnimState.Attack : UnitAnimState.Idle;
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

    static void ShowConstruction(Actor actor, float progress) {
      var building = progress < 1f;
      if (building && actor.Scaffold == null) {
        var item = new GameObject("Scaffold", typeof(MeshFilter), typeof(MeshRenderer));
        item.transform.SetParent(actor.View.transform, false);
        item.GetComponent<MeshFilter>().sharedMesh = ScaffoldMesh();
        item.GetComponent<MeshRenderer>().sharedMaterial = ArtLook.NeutralMaterial;
        actor.Scaffold = item;
      }
      if (actor.Scaffold != null) {
        actor.Scaffold.SetActive(building);
      }
      if (actor.Dressing != null) {
        actor.Dressing.SetActive(building == false);
      }
      // 땅 밑에서 올라온다. 바닥이 불투명해서 아래쪽은 가려진다.
      var position = actor.View.Model.transform.localPosition;
      position.y = building ? -(1f - progress) * actor.Height : 0f;
      actor.View.Model.transform.localPosition = position;
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

    static void DressBuilding(Actor actor, BuildingSpec spec, Vector3 worldPos) {
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
        actor.View.transform.position = position;
        actor.View.AddPlate(frame.FindAsset(building->Spec).FootprintRadius.AsFloat, frame.Global->Players.GetPointer(building->Owner)->ColorIndex);
        DressBuilding(actor, spec, position);
        var forward = spec.Role == BuildingRole.Headquarters && IsForward(frame, building->Owner, position);
        if (forward) {
          DressForward(actor, spec, position);
        }
        // 건물(전진 사령부는 방벽 고리까지) 자리의 길가·흩뿌림 장식을 걷어낸다.
        DressingRenderer.Active?.ClearAround(position, spec.FootprintRadius.AsFloat + (forward ? ForwardRingExtra + 1.5f : 1.5f), "urban", "scatter");
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

      var total = spec.BuildTimeSeconds.AsFloat;
      var remaining = building->ConstructionRemaining.AsFloat;
      var finished = remaining <= 0f;
      ShowConstruction(actor, finished || total <= 0f ? 1f : 1f - remaining / total);
      if (finished == false) {
        return;
      }

      var hasTarget = TryTarget(frame, building->TargetEntity, out var target, out var air);
      actor.AntiAir = spec.Attack == AttackType.AntiAir;
      Aim(actor, hasTarget, target, air, dt);
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
      var researching = building->Research > 0;
      var ultimateBusy = spec.Role == BuildingRole.UltimateSite && (state->UltimatePhase == UltimatePhase.Preparing || state->UltimatePhase == UltimatePhase.Ready);
      var overdriving = FactorySystem.IsOverdriving(frame, building);
      var yard = spec.IsDropOff && spec.Role == BuildingRole.CollectionYard;
      var operating = powered && (processing || producing || researching || ultimateBusy || overdriving || spec.Role == BuildingRole.PowerPlant);
      var busy = operating || (powered && (yard || spec.Role == BuildingRole.Headquarters));
      Animate(actor, dt, powered, busy);

      // 공장 Lv.2 / Lv.3 시각적 확장 동기화(추가 굴뚝 · 갠트리 크레인 · 발광 배관).
      var fLevel = FactorySystem.LevelOf(building);
      if (fLevel != actor.FactoryLevel) {
        actor.FactoryLevel = fLevel;
        if (actor.FactoryUpgradeGo != null) Destroy(actor.FactoryUpgradeGo);
        actor.FactoryUpgradeGo = HeroVisuals.AttachFactoryUpgrade(actor.View.Model.transform, fLevel, actor.Color);
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

    /// <summary>12m 이내 인접한 생산 공장과 제련소·발전소 사이를 발광 지상 파이프라인으로 자동 연결한다.</summary>
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
        var show = ratio < 0.995f || _targeted.Contains(pair.Key);
        if (actor.View.Bar.activeSelf != show) {
          actor.View.Bar.SetActive(show);
        }
        if (show) {
          actor.View.SetHealth(ratio);
        }
      }
    }

    void Mark(Actor actor, float health) {
      actor.View.SetSelected(true);
      actor.View.SetHealth(health);
      _marked.Add(actor);
    }
  }
}
