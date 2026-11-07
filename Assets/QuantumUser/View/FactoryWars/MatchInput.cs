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

    public readonly SelectionModel Selection = new SelectionModel();
    /// <summary>값이 있으면 다음 탭 위치에 이 건물을 짓는다.</summary>
    public AssetRef<BuildingSpec> Placing;

    public bool IsPlacing => Placing != default;

    static FPVector3 ToFP(Vector3 world) => new FPVector3(FP.FromFloat_UNSAFE(world.x), FP._0, FP.FromFloat_UNSAFE(world.z));

    public static EntityRef PickUnit(Frame frame, Vector3 world) {
      var best = EntityRef.None;
      var bestDistance = UnitPickRadius * UnitPickRadius;
      foreach (var pair in frame.GetComponentIterator<Unit>()) {
        var position = frame.Get<Transform3D>(pair.Entity).Position.ToUnityVector3();
        var distance = new Vector2(position.x - world.x, position.z - world.z).sqrMagnitude;
        if (distance < bestDistance) {
          best = pair.Entity;
          bestDistance = distance;
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

    public static EntityRef PickNode(Frame frame, Vector3 world) {
      var point = ToFP(world);
      foreach (var pair in frame.GetComponentIterator<ResourceNode>()) {
        if (pair.Component.Shape.Distance(point) <= FP._1) {
          return pair.Entity;
        }
      }
      return EntityRef.None;
    }

    List<EntityRef> SelectedEngineers(Frame frame) => Selection.Units.Where(e => frame.Exists(e) && frame.Has<Worker>(e)).ToList();

    List<EntityRef> SelectedUnits(Frame frame) => Selection.Units.Where(e => frame.Exists(e)).Take(MoveUnitsCommand.MaxUnits).ToList();

    /// <summary>탭을 해석한다. 내릴 커맨드가 있으면 돌려주고, 선택만 바뀌면 null이다.</summary>
    public DeterministicCommand Tap(Frame frame, PlayerRef player, Vector3 world) {
      Selection.Prune(frame);
      if (IsPlacing) {
        return Place(frame, world);
      }

      var unit = PickUnit(frame, world);
      if (unit != EntityRef.None) {
        if (frame.Get<Unit>(unit).Owner == player) {
          Selection.SelectUnit(unit);
          return null;
        }
        return Attack(frame, player, unit);
      }

      var building = PickBuilding(frame, world);
      if (building != EntityRef.None) {
        var state = frame.Get<Building>(building);
        if (state.Owner != player) {
          return Attack(frame, player, building);
        }
        var builders = SelectedEngineers(frame);
        var needsWork = state.ConstructionRemaining > FP._0 || state.Health < frame.FindAsset(state.Spec).MaxHealth;
        if (builders.Count > 0 && needsWork) {
          return new RepairCommand { Units = builders.ToArray(), Target = building };
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

      var units = SelectedUnits(frame);
      if (units.Count > 0) {
        return new MoveUnitsCommand { Units = units.ToArray(), Target = ToFP(world) };
      }
      Selection.Clear();
      return null;
    }

    DeterministicCommand Attack(Frame frame, PlayerRef player, EntityRef target) {
      var units = SelectedUnits(frame);
      if (units.Count == 0 || CombatSystem.TryGetOwner(frame, target, out var owner) == false || DiplomacySystem.CanAttack(frame, player, owner) == false) {
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

    public DeterministicCommand Stop(Frame frame) {
      Selection.Prune(frame);
      var units = SelectedUnits(frame);
      return units.Count == 0 ? null : new StopUnitsCommand { Units = units.ToArray() };
    }
  }
}
