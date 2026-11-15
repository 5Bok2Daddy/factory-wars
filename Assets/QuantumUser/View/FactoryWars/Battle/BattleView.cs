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
    const float RecoilDistance = 0.32f;
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
    }

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
      QuantumEvent.Subscribe<EventTraderShot>(this, e => Fx.MuzzleFlash(e.From.ToUnityVector3() + Vector3.up * 3f, (e.To - e.From).ToUnityVector3().normalized, 0.8f));
      QuantumEvent.Subscribe<EventSortieEnded>(this, e => { if (e.ShotDown) { Fx.Explosion(e.Position.ToUnityVector3() + Vector3.up * 8f, 1.4f); } });
      QuantumEvent.Subscribe<EventCaptureChanged>(this, e => {
        var frame = e.Game.Frames.Verified;
        if (e.Next.IsValid && frame != null && frame.Exists(e.Site)) {
          Fx.Pulse(frame.Get<Transform3D>(e.Site).Position.ToUnityVector3(), Palette.Of(frame, e.Next), frame.Get<CaptureSite>(e.Site).Radius.AsFloat);
        }
      });
      // 항목 33: 미사일 착탄·항모 격추는 기존 폭발 효과를 크게 쓴다(새 그래픽 없음).
      QuantumEvent.Subscribe<EventUltimateNotice>(this, e => {
        if (e.Stage == UltimateStage.Impact) {
          Fx.Explosion(e.Position.ToUnityVector3(), 4f);
        } else if (e.Stage == UltimateStage.Lost) {
          Fx.Explosion(e.Position.ToUnityVector3(), 2.5f);
        }
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
      Field.Update(frame, dt);
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
      // 항목 33: 날아오는 전략 미사일의 착탄 지점을 모든 플레이어에게 보인다(폭발 반경만큼 붉은 고리 + 연막 신호).
      if (Time.time >= _strikeAt) {
        _strikeAt = Time.time + 0.3f;
        StrikeMarks = 0;
        foreach (var pair in frame.GetComponentIterator<MissileStrike>()) {
          var target = pair.Component.Target.ToUnityVector3();
          var radius = frame.FindAsset(pair.Component.Spec).BlastRadius.AsFloat;
          // 반경 가장자리에 머무는 고리 + 안쪽으로 조이는 느낌의 반쪽 고리 + 중심 연막.
          Fx.Ring(target, StrikeColor, radius, 0.45f);
          Fx.Ring(target, StrikeColor, radius * 0.5f, 0.45f);
          Fx.Signal(target);
          StrikeMarks++;
        }
      }

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
      Fx.Explosion(center, actor.IsBuilding ? 2.2f : Mathf.Clamp(actor.Radius * 0.5f, 0.5f, 1.5f));
      if (actor.IsAir == false && actor.IsNaval == false && (actor.IsBuilding || actor.Radius > 1.8f)) {
        Fx.Wreck(actor.View.transform.position, actor.IsBuilding ? 3.2f : actor.Radius * 0.8f, actor.IsBuilding ? 20f : 30f);
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
      _actors[entity] = actor;
      return actor;
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
        actor.TurretYaw = Mathf.MoveTowardsAngle(actor.TurretYaw, want, TurretRate * dt);
        actor.Turret.localRotation = Quaternion.Euler(0, actor.TurretYaw, 0);
      }
      if (actor.Barrel != null) {
        actor.Recoil = Mathf.MoveTowards(actor.Recoil, 0f, RecoilRecover * dt);
        actor.Barrel.localPosition = actor.BarrelHome + Vector3.back * actor.Recoil;
      }
    }

    void Fire(Actor actor, AttackType attack, float range, bool hasTarget, Vector3 target, bool flame) {
      actor.View.Animation?.Fire();
      actor.Recoil = RecoilDistance;
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
          Fx.Tracer(muzzle + forward * (0.3f * i), target + spread, speed, 0.18f, false);
        }
      } else {
        Fx.Tracer(muzzle, target, speed, 0.34f, missile);
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
          var aim = TryTarget(frame, unit->TargetEntity, out var shot, out _) ? (shot - window).normalized : Vector3.forward;
          Fx.MuzzleFlash(window + aim * 2.5f, aim, 0.7f);
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
      var hover = spec.IsAir ? Mathf.Sin(Time.time * 1.6f + actor.Phase) * 0.12f : 0f;
      actor.View.transform.position = spec.IsAir ? actor.Position : actor.Position + Vector3.up * TerrainKit.SurfaceHeight(World.Map, actor.Position.x, actor.Position.z);
      actor.View.Turn.localPosition = Vector3.up * (actor.View.Entry.Altitude + hover);

      var hasTarget = TryTarget(frame, unit->TargetEntity, out var target, out var air);
      if (moved.sqrMagnitude > 0.002f) {
        actor.HullYaw = Mathf.MoveTowardsAngle(actor.HullYaw, Mathf.Atan2(moved.x, moved.z) * Mathf.Rad2Deg, TurnRate * dt);
      } else if (hasTarget && actor.Turret == null) {
        actor.HullYaw = Mathf.MoveTowardsAngle(actor.HullYaw, YawTo(actor.Position, target), TurnRate * dt);
      }
      actor.View.SetYaw(actor.HullYaw);
      Tilt(actor, spec, before, dt);
      Aim(actor, hasTarget, target, air, dt);
      UpdateShield(actor, unit->InCover);

      var cooldown = unit->AttackCooldown.AsFloat;
      if (cooldown > actor.Cooldown + 0.05f && spec.Attack != AttackType.None) {
        Fire(actor, spec.Attack, spec.Range.AsFloat, hasTarget, target, spec.Ability == SpecialAbility.FlameBurst);
      }
      actor.Cooldown = cooldown;

      var working = frame.Unsafe.TryGetPointer<Worker>(entity, out var worker) && worker->Task != WorkerTask.Idle && worker->Task != WorkerTask.Deliver;
      Animate(actor, dt, true, working);
      if (actor.View.Animation != null) {
        var speed = dt > 0f ? (actor.Position - before).magnitude / dt : 0f;
        actor.View.Animation.Cover = unit->InCover;
        actor.View.Animation.Set(AnimState(speed, hasTarget, working ? worker->Task : WorkerTask.Idle), speed);
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
      actor.View.Turn.localRotation = Quaternion.Euler(0, actor.HullYaw, 0) * Quaternion.Euler(actor.Pitch, 0, actor.Roll);
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
      var yard = spec.IsDropOff && spec.Role == BuildingRole.CollectionYard;
      var busy = powered && (processing || producing || yard || spec.Role == BuildingRole.Headquarters || spec.Role == BuildingRole.Barracks);
      Animate(actor, dt, powered, busy);

      var smoke = actor.View.Entry.Smoke;
      if (smoke != null && smoke.Length > 0 && Time.time >= actor.SmokeAt && (powered && (processing || producing || spec.Role == BuildingRole.PowerPlant))) {
        actor.SmokeAt = Time.time + SmokeSeconds / Mathf.Max(GraphicsQuality.FxScale, 0.1f);
        foreach (var point in smoke) {
          Fx.ChimneySmoke(actor.View.Model.transform.TransformPoint(point));
        }
      }
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
        ArtView.DashedRing(kit, 0.82f, 1f, 16);
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
    void CommandLines(Frame frame, SelectionModel selection) {
      _attackLine ??= LineMaterial(new Color(1f, 0.165f, 0.165f, 0.75f));
      _moveLine ??= LineMaterial(new Color(0.22f, 0.95f, 0.48f, 0.7f));
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
          var line = Line(lines++, _attackLine);
          line.SetPosition(0, from);
          line.SetPosition(1, at);
          attack++;
          if (_targeted.Add(unit->TargetEntity) && reticles < 12) {
            var ring = Reticle(reticles++);
            var radius = _actors.TryGetValue(unit->TargetEntity, out var targetActor) ? Mathf.Max(1f, targetActor.Radius) : 1.5f;
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
          if ((goal - from).sqrMagnitude > 4f) {
            var line = Line(lines++, _moveLine);
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
