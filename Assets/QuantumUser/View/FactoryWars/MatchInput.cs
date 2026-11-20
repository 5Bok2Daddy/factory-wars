namespace FactoryWars.View {
  using System.Collections.Generic;
  using System.Linq;
  using Photon.Deterministic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;

  /// <summary>화면 입력을 선택 변경이나 커맨드로 해석한다. 시뮬레이션은 읽기만 한다.</summary>
  public unsafe class MatchInput {
    public const float UnitPickRadius = 2f;
    static float CameraPitch => VisualProfile.Current.Pitch;

    static ViewCatalog _catalog;

    static ViewCatalog Catalog => _catalog != null ? _catalog : _catalog = Resources.Load<ViewCatalog>(ViewCatalog.ResourcePath);

    /// <summary>표시 크기에 맞춘 터치 반경. 선택 링과 같고, 작은 유닛도 손가락으로 누를 수 있는 크기는 보장한다.</summary>
    public static float PickRadius(string unitId) {
      return Catalog != null && Catalog.TryGet(unitId, out var entry) ? Mathf.Max(UnitPickRadius, entry.RingRadius * ArtView.ScaleOf(entry)) : UnitPickRadius;
    }

    /// <summary>공중 유닛은 화면에서 땅 위치보다 앞쪽 위에 보인다. 그 자리를 눌렀을 때 맞는 땅 좌표로 옮긴다.</summary>
    public static float AirTapOffset(string unitId) {
      return Catalog != null && Catalog.TryGet(unitId, out var entry) ? entry.Altitude * ArtView.ScaleOf(entry) / Mathf.Tan(CameraPitch * Mathf.Deg2Rad) : 0f;
    }

    public readonly SelectionModel Selection = new SelectionModel();
    /// <summary>값이 있으면 다음 탭 위치에 이 건물을 짓는다.</summary>
    public AssetRef<BuildingSpec> Placing;

    public bool IsPlacing => Placing != default;

    /// <summary>다음 땅 탭의 뜻(항목 30). 버튼으로 켜고, 탭 한 번에 꺼진다.</summary>
    public enum TapMode { None, AttackMove, Patrol, Rally, Ultimate, Jammer, Sortie, Drone }

    public TapMode Mode;
    /// <summary>항목 34-D: 작전 목표를 고르는 중인 비행장과 격납 칸.</summary>
    public EntityRef SortieAirfield;
    public int SortieSlot;

    /// <summary>항목 34-C: 고른 유닛 중 은신·교란 장치를 쓸 수 있는 저격수.</summary>
    public List<EntityRef> SelectedSnipers(Frame frame) =>
      Selection.Units.Where(e => frame.Exists(e) && frame.Has<Unit>(e) && frame.FindAsset(frame.Get<Unit>(e).Spec).StealthSeconds > FP._0).ToList();

    static FPVector3 ToFP(Vector3 world) => new FPVector3(FP.FromFloat_UNSAFE(world.x), FP._0, FP.FromFloat_UNSAFE(world.z));

    public static EntityRef PickUnit(Frame frame, Vector3 world) {
      // 큰 유닛이 작은 유닛을 덮지 않도록 반경에 대한 비율이 가장 작은 유닛을 고른다.
      var best = EntityRef.None;
      var bestScore = 1f;
      foreach (var pair in frame.GetComponentIterator<Unit>()) {
        if (pair.Component.Transport != EntityRef.None || pair.Component.Garrison != EntityRef.None) {
          continue;
        }
        var id = frame.FindAsset(pair.Component.Spec).name;
        var position = frame.Get<Transform3D>(pair.Entity).Position.ToUnityVector3();
        var distance = new Vector2(position.x - world.x, position.z + AirTapOffset(id) - world.z).magnitude;
        var score = distance / PickRadius(id);
        if (score < bestScore) {
          best = pair.Entity;
          bestScore = score;
        }
      }
      return best;
    }

    public static EntityRef PickBuilding(Frame frame, Vector3 world) {
      foreach (var pair in frame.GetComponentIterator<Building>()) {
        var position = frame.Get<Transform3D>(pair.Entity).Position.ToUnityVector3();
        var radius = frame.FindAsset(pair.Component.Spec).FootprintRadius.AsFloat;
        if (Mathf.Abs(position.x - world.x) <= radius && Mathf.Abs(position.z - world.z) <= radius) {
          return pair.Entity;
        }
      }
      return EntityRef.None;
    }

    public static EntityRef PickCity(Frame frame, Vector3 world) {
      var map = frame.FindAsset<MapSpec>(frame.Map.UserAsset);
      var point = ToFP(world);
      foreach (var pair in frame.GetComponentIterator<CityBuilding>()) {
        if (map.CityBuildings[pair.Component.Index].Shape.Distance(point) <= FP._0) {
          return pair.Entity;
        }
      }
      return EntityRef.None;
    }

    /// <summary>보병을 고른 채 진지를 누르면 들어가고, 다른 유닛이면 공격한다. 고른 유닛이 없으면 건물을 고른다.</summary>
    DeterministicCommand TapCity(Frame frame, EntityRef city) {
      var map = frame.FindAsset<MapSpec>(frame.Map.UserAsset);
      var garrison = map.CityBuildings[frame.Get<CityBuilding>(city).Index].Garrison;
      var units = SelectedUnits(frame);
      var infantry = units.Where(e => CitySystem.IsInfantry(frame.FindAsset(frame.Get<Unit>(e).Spec))).ToList();
      if (garrison && infantry.Count > 0) {
        return new EnterGarrisonCommand { Units = infantry.ToArray(), Building = city };
      }
      if (units.Count > 0) {
        return new AttackTargetCommand { Units = units.ToArray(), Target = city };
      }
      Selection.SelectBuilding(city);
      return null;
    }

    public static EntityRef PickNode(Frame frame, Vector3 world) {
      var point = ToFP(world);
      foreach (var pair in frame.GetComponentIterator<ResourceNode>()) {
        if (pair.Component.Shape.Distance(point) <= FP._1) {
          return pair.Entity;
        }
      }
      return EntityRef.None;
    }

    public static EntityRef PickWreck(Frame frame, Vector3 world) {
      var point = ToFP(world);
      foreach (var pair in frame.GetComponentIterator<Wreck>()) {
        var pos = frame.Get<Transform3D>(pair.Entity).Position;
        if (FactoryWarsUtil.FlatDistanceSqr(pos, point) <= FP._3 * FP._3) {
          return pair.Entity;
        }
      }
      return EntityRef.None;
    }

    public static int PickBridge(Frame frame, Vector3 world, out bool destroyed) {
      destroyed = false;
      var map = frame.FindAsset<MapSpec>(frame.Map.UserAsset);
      if (map?.Bridges == null) {
        return -1;
      }
      var point = ToFP(world);
      foreach (var pair in frame.GetComponentIterator<Quantum.Bridge>()) {
        var idx = pair.Component.Index;
        if (idx >= 0 && idx < map.Bridges.Length && map.Bridges[idx].Distance(point) <= FP._2) {
          destroyed = pair.Component.Destroyed;
          return idx;
        }
      }
      return -1;
    }

    List<EntityRef> SelectedEngineers(Frame frame) => Selection.Units
      .Where(e => frame.Has<Worker>(e) && frame.TryGet<Unit>(e, out var unit)
        && unit.Garrison == EntityRef.None && unit.Transport == EntityRef.None).ToList();

    List<EntityRef> SelectedUnits(Frame frame) => Selection.Units.Where(e => frame.Exists(e)).Take(MoveUnitsCommand.MaxUnits).ToList();

    /// <summary>미니맵 오른쪽 클릭: 선택한 내 유닛을 그 자리로 이동(공격 이동). 선택이 없으면 null.</summary>
    public MoveUnitsCommand MinimapOrder(Frame frame, Vector3 world, bool attackMove) {
      if (frame == null) {
        return null;
      }
      Selection.Prune(frame);
      var units = SelectedUnits(frame);
      return units.Count == 0 ? null : new MoveUnitsCommand { Units = units.ToArray(), Target = ToFP(world), AttackMove = attackMove };
    }

    /// <summary>탭을 해석한다. 내릴 커맨드가 있으면 돌려주고, 선택만 바뀌면 null이다.</summary>
    public DeterministicCommand Tap(Frame frame, PlayerRef player, Vector3 world) {
      Selection.Prune(frame);
      if (IsPlacing) {
        return Place(frame, world);
      }
      if (Mode != TapMode.None) {
        var mode = Mode;
        Mode = TapMode.None;
        var chosen = SelectedUnits(frame);
        switch (mode) {
          case TapMode.AttackMove:
            foreach (var pair in frame.GetComponentIterator<NpcTrader>()) {
              var at = frame.Get<Transform3D>(pair.Entity).Position.ToUnityVector3();
              if ((new Vector2(at.x, at.z) - new Vector2(world.x, world.z)).magnitude <= frame.FindAsset(frame.RuntimeConfig.Rules).TraderRadius.AsFloat && chosen.Count > 0 && pair.Component.Destroyed == false) {
                return new AttackTargetCommand { Units = chosen.ToArray(), Target = pair.Entity };
              }
            }
            var enemy = PickUnit(frame, world);
            if (enemy != EntityRef.None && frame.Get<Unit>(enemy).Owner != player) {
              return Attack(frame, player, enemy);
            }
            return chosen.Count == 0 ? null : new MoveUnitsCommand { Units = chosen.ToArray(), Target = ToFP(world), AttackMove = true };
          case TapMode.Patrol:
            return chosen.Count == 0 ? null : new PatrolUnitsCommand { Units = chosen.ToArray(), Target = ToFP(world) };
          case TapMode.Rally:
            return Selection.Building == EntityRef.None ? null : new RallyCommand { Building = Selection.Building, Position = ToFP(world) };
          case TapMode.Ultimate:
            // 항목 33: 궁극 전력 목표 지정(미사일 착탄 지점, 상륙 해안).
            return new LaunchUltimateCommand { Target = ToFP(world) };
          case TapMode.Drone:
            return new TradeCommand { Offer = NpcTraderSystem.OfferDrone, Target = ToFP(world) };
          case TapMode.Jammer: {
            // 항목 34-C: 은신한 저격수를 고른 채 자원 노드를 누르면 교란 장치를 설치한다.
            var jamNode = PickNode(frame, world);
            var snipers = SelectedSnipers(frame);
            return jamNode == EntityRef.None || snipers.Count == 0 ? null : new PlaceJammerCommand { Sniper = snipers[0], Node = jamNode };
          }
          case TapMode.Sortie: {
            // 항목 34-D: 누른 곳이 목표, 진입 방향은 비행장 → 목표.
            if (frame.Exists(SortieAirfield) == false) {
              return null;
            }
            var from = frame.Get<Transform3D>(SortieAirfield).Position;
            var target = ToFP(world);
            var heading = new FPVector2(target.X - from.X, target.Z - from.Z);
            return new OrderSortieCommand { Airfield = SortieAirfield, Slot = SortieSlot, Target = target, Heading = heading.SqrMagnitude > FP._0 ? heading.Normalized : new FPVector2(FP._1, FP._0) };
          }
        }
      }

      var unit = PickUnit(frame, world);
      if (unit != EntityRef.None) {
        if (frame.Get<Unit>(unit).Owner == player) {
          // 보병·전차 선택 상태에서 아군 수송 유닛(APC·상륙정·수송헬기)을 탭하면 즉시 탑승 명령을 내린다.
          var targetSpec = frame.FindAsset(frame.Get<Unit>(unit).Spec);
          if (targetSpec.TransportCapacity > 0 && Selection.Units.Count > 0) {
            var boarders = SelectedUnits(frame).Where(e => e != unit && TransportSystem.CanUnitBoardSpec(frame.FindAsset(frame.Get<Unit>(e).Spec), targetSpec)).ToArray();
            if (boarders.Length > 0) {
              return new BoardTransportCommand { Units = boarders, Transport = unit };
            }
          }
          Selection.SelectUnit(unit);
          return null;
        }
        return Attack(frame, player, unit);
      }

      // 중앙 암시장: 공격 이동 중이면 공격, 아니면 선택(거래 패널).
      foreach (var pair in frame.GetComponentIterator<NpcTrader>()) {
        var at = frame.Get<Transform3D>(pair.Entity).Position.ToUnityVector3();
        var reach = frame.FindAsset(frame.RuntimeConfig.Rules).TraderRadius.AsFloat;
        if ((new Vector2(at.x, at.z) - new Vector2(world.x, world.z)).magnitude <= reach && pair.Component.Destroyed == false) {
          Selection.SelectBuilding(pair.Entity);
          return null;
        }
      }
      var city = PickCity(frame, world);
      if (city != EntityRef.None) {
        return TapCity(frame, city);
      }

      var building = PickBuilding(frame, world);
      if (building != EntityRef.None) {
        var state = frame.Get<Building>(building);
        if (state.Owner != player) {
          return Attack(frame, player, building);
        }
        var builders = SelectedEngineers(frame);
        var spec = frame.FindAsset(state.Spec);
        if (builders.Count > 0 && state.ConstructionRemaining <= FP._0 && spec.RequiresResourceNode
            && CitySystem.Occupants(frame, building) < spec.WorkerSlotCapacity) {
          return new EnterGarrisonCommand { Units = builders.ToArray(), Building = building };
        }
        var needsWork = state.ConstructionRemaining > FP._0 || state.Health < spec.MaxHealth;
        if (builders.Count > 0 && needsWork) {
          return new RepairCommand { Units = builders.ToArray(), Target = building };
        }
        // 항목 33: 보병을 고른 채 자기 벙커를 누르면 들어간다.
        if (state.ConstructionRemaining <= FP._0 && spec.GarrisonCapacity > 0) {
          var squads = SelectedUnits(frame).Where(e => CitySystem.IsInfantry(frame.FindAsset(frame.Get<Unit>(e).Spec))).ToList();
          if (squads.Count > 0) {
            return new EnterGarrisonCommand { Units = squads.ToArray(), Building = building };
          }
        }
        Selection.SelectBuilding(building);
        return null;
      }

      var node = PickNode(frame, world);
      if (node != EntityRef.None) {
        var gatherers = SelectedEngineers(frame);
        if (gatherers.Count > 0) {
          return new GatherCommand { Units = gatherers.ToArray(), Node = node };
        }
      }

      var wreck = PickWreck(frame, world);
      if (wreck != EntityRef.None) {
        var salvagers = SelectedEngineers(frame);
        if (salvagers.Count > 0) {
          return new SalvageCommand { Units = salvagers.ToArray(), Wreck = wreck };
        }
      }

      var bridge = PickBridge(frame, world, out var bridgeDestroyed);
      if (bridge >= 0) {
        var bridgeWorkers = SelectedEngineers(frame);
        if (bridgeWorkers.Count > 0) {
          return new WorkBridgeCommand { Units = bridgeWorkers.ToArray(), Bridge = bridge, Demolish = bridgeDestroyed == false };
        }
      }

      var units = SelectedUnits(frame);
      if (units.Count > 0) {
        return new MoveUnitsCommand { Units = units.ToArray(), Target = ToFP(world) };
      }
      Selection.Clear();
      return null;
    }

    DeterministicCommand Attack(Frame frame, PlayerRef player, EntityRef target) {
      var units = SelectedUnits(frame);
      if (units.Count == 0 || CombatSystem.TryGetOwner(frame, target, out var owner) == false) {
        return null;
      }
      // 휴전 중이면 시뮬레이션이 거부 사유를 돌려주도록 그대로 보낸다.
      if (FactoryWarsUtil.InTruce(frame) == false && DiplomacySystem.CanAttack(frame, player, owner) == false) {
        return null;
      }
      return new AttackTargetCommand { Units = units.ToArray(), Target = target };
    }

    DeterministicCommand Place(Frame frame, Vector3 world) {
      var building = Placing;
      Placing = default;
      var engineers = SelectedEngineers(frame);
      if (engineers.Count == 0) {
        return null;
      }
      return new BuildCommand { Engineer = engineers[0], Building = building, Position = ToFP(world) };
    }

    /// <summary>길게 누르면 그 지점으로 공격 이동한다.</summary>
    public DeterministicCommand LongPress(Frame frame, Vector3 world) {
      Selection.Prune(frame);
      var units = SelectedUnits(frame);
      return units.Count == 0 ? null : new MoveUnitsCommand { Units = units.ToArray(), Target = ToFP(world), AttackMove = true };
    }

    public DeterministicCommand Hold(Frame frame) {
      Selection.Prune(frame);
      var units = SelectedUnits(frame);
      return units.Count == 0 ? null : new HoldUnitsCommand { Units = units.ToArray() };
    }

    int _idleCursor;

    /// <summary>놀고 있는 공병을 하나씩 돌아가며 고른다. 고른 위치를 돌려준다(카메라 이동용).</summary>
    public bool SelectIdleEngineer(Frame frame, PlayerRef player, out Vector3 position) {
      var idle = new List<EntityRef>();
      foreach (var pair in frame.GetComponentIterator<Worker>()) {
        var unit = frame.Get<Unit>(pair.Entity);
        if (unit.Owner == player && pair.Component.Task == WorkerTask.Idle && unit.Order == UnitOrder.Stop) {
          idle.Add(pair.Entity);
        }
      }
      position = default;
      if (idle.Count == 0) {
        return false;
      }
      var pick = idle[_idleCursor++ % idle.Count];
      Selection.Clear();
      Selection.SelectUnit(pick);
      position = frame.Get<Transform3D>(pick).Position.ToUnityVector3();
      return true;
    }

    /// <summary>공병·진지 안 분대를 뺀 내 전투 유닛 전부(최대 64기).</summary>
    public int SelectArmy(Frame frame, PlayerRef player) {
      Selection.Clear();
      foreach (var pair in frame.GetComponentIterator<Unit>()) {
        // 항목 33: 스스로 움직이는 드론·상륙정은 뺀다.
        if (pair.Component.Owner == player && frame.Has<Worker>(pair.Entity) == false && pair.Component.Garrison == EntityRef.None
            && UltimateSystem.IsAutonomous(frame, pair.Entity) == false
            && frame.FindAsset(pair.Component.Spec).Attack != AttackType.None && Selection.Units.Count < MoveUnitsCommand.MaxUnits) {
          Selection.Units.Add(pair.Entity);
        }
      }
      return Selection.Units.Count;
    }

    public DeterministicCommand Stop(Frame frame) {
      Selection.Prune(frame);
      var units = SelectedUnits(frame);
      return units.Count == 0 ? null : new StopUnitsCommand { Units = units.ToArray() };
    }
  }
}
