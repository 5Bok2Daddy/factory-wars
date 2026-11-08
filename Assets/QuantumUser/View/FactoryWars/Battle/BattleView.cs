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
      public float Height;
    }

    static Mesh _scaffold;

    readonly Dictionary<EntityRef, Actor> _actors = new Dictionary<EntityRef, Actor>();
    readonly HashSet<EntityRef> _seen = new HashSet<EntityRef>();
    readonly List<EntityRef> _gone = new List<EntityRef>();
    readonly List<Actor> _marked = new List<Actor>();
    ViewCatalog _catalog;
    MatchController _controller;

    public WorldView World { get; private set; }
    public BattlefieldView Field { get; private set; }
    public CityView City { get; private set; }
    public BattleFx Fx { get; private set; }
    public IReadOnlyDictionary<EntityRef, Actor> Actors => _actors;

    static Transform FindDeep(Transform root, string name) {
      foreach (var child in root.GetComponentsInChildren<Transform>(true)) {
        if (child.name == name) {
          return child;
        }
      }
      return null;
    }

    Transform _stage;

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
      World = new WorldView(_stage, frame.FindAsset<MapSpec>(frame.Map.UserAsset), _catalog);
      Fx = new GameObject("Fx").AddComponent<BattleFx>();
      Fx.transform.SetParent(_stage, false);
      World.Fx = Fx;
      Field = new BattlefieldView(_stage);
      QuantumEvent.Subscribe<EventCratePicked>(this, e => Fx.Burst(e.Position.ToUnityVector3(), new Color(1f, 0.85f, 0.3f), 1f));
      QuantumEvent.Subscribe<EventCaptureChanged>(this, e => {
        var frame = e.Game.Frames.Verified;
        if (e.Next.IsValid && frame != null && frame.Exists(e.Site)) {
          Fx.Pulse(frame.Get<Transform3D>(e.Site).Position.ToUnityVector3(), Palette.Of(frame, e.Next), frame.Get<CaptureSite>(e.Site).Radius.AsFloat);
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
      _marked.Clear();
      World = null;
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
      World.ShowNoBuild(_controller != null && _controller.Input != null && _controller.Input.IsPlacing);

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

      _gone.Clear();
      foreach (var pair in _actors) {
        if (_seen.Contains(pair.Key) == false) {
          _gone.Add(pair.Key);
        }
      }
      foreach (var entity in _gone) {
        Remove(entity);
      }
      UpdateSelection(frame);
    }

    void Remove(EntityRef entity) {
      var actor = _actors[entity];
      var center = actor.View.transform.position + Vector3.up * (actor.IsAir ? actor.View.Entry.Altitude : actor.Height * 0.4f);
      Fx.Explosion(center, actor.IsBuilding ? 2.2f : Mathf.Clamp(actor.Radius * 0.5f, 0.5f, 1.5f));
      if (actor.IsAir == false && (actor.IsBuilding || actor.Radius > 1.8f)) {
        Fx.Wreck(actor.View.transform.position, actor.IsBuilding ? 3.2f : actor.Radius * 0.8f, actor.IsBuilding ? 20f : 30f);
      }
      _marked.Remove(actor);
      Destroy(actor.View.gameObject);
      _actors.Remove(entity);
    }

    Actor Create(EntityRef entity, string id, bool isBuilding, bool isAir) {
      var view = ArtView.Create(_catalog, id, _stage, 0);
      if (view == null) {
        return null;
      }
      var actor = new Actor { View = view, Entity = entity, IsBuilding = isBuilding, IsAir = isAir, Radius = view.Entry.RingRadius, Height = view.Entry.BarHeight, Phase = Random.value * 10f };
      var model = view.Model.transform;
      actor.Turret = FindDeep(model, "Turret") ?? FindDeep(model, "Launcher");
      actor.Barrel = FindDeep(model, "Barrel") ?? FindDeep(model, "Guns") ?? FindDeep(model, "Tubes");
      if (actor.Barrel != null) {
        actor.BarrelHome = actor.Barrel.localPosition;
      }
      actor.Muzzle = FindDeep(model, view.Entry.MuzzlePart) ?? model;

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
      Fx.Tracer(muzzle, target, speed, attack == AttackType.AntiInfantry ? 0.18f : 0.34f, missile);
      Fx.Impact(target, attack == AttackType.AntiInfantry ? 0.8f : 1.6f);
      if (attack == AttackType.Area) {
        Fx.Crater(target, 1.8f);
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
      actor.Position += moved * (1f - Mathf.Exp(-FollowRate * dt));
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
      Aim(actor, hasTarget, target, air, dt);

      var cooldown = unit->AttackCooldown.AsFloat;
      if (cooldown > actor.Cooldown + 0.05f && spec.Attack != AttackType.None) {
        Fire(actor, spec.Attack, spec.Range.AsFloat, hasTarget, target, spec.Ability == SpecialAbility.FlameBurst);
      }
      actor.Cooldown = cooldown;

      var working = frame.Unsafe.TryGetPointer<Worker>(entity, out var worker) && worker->Task != WorkerTask.Idle && worker->Task != WorkerTask.Deliver;
      Animate(actor, dt, true, working);
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
      // 땅 밑에서 올라온다. 바닥이 불투명해서 아래쪽은 가려진다.
      var position = actor.View.Model.transform.localPosition;
      position.y = building ? -(1f - progress) * actor.Height : 0f;
      actor.View.Model.transform.localPosition = position;
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
        actor.HullYaw = 180f;
        actor.View.SetYaw(180f);
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
    }

    void Mark(Actor actor, float health) {
      actor.View.SetSelected(true);
      actor.View.SetHealth(health);
      _marked.Add(actor);
    }
  }
}
