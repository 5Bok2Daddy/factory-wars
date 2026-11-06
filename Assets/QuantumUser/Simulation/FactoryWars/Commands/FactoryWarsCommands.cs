namespace Quantum.FactoryWars {
  using Photon.Deterministic;

  /// <summary>플레이어 일회성 행동의 공통 기반. CommandSystem이 Execute를 호출한다.</summary>
  public abstract class FactoryCommand : DeterministicCommand {
    public abstract void Execute(Frame f, PlayerRef player);
  }

  public unsafe class MoveUnitsCommand : FactoryCommand {
    public const int MaxUnits = 64;

    public EntityRef[] Units = System.Array.Empty<EntityRef>();
    public FPVector3 Target;
    public bool AttackMove;

    public override void Serialize(BitStream stream) {
      var count = Units.Length;
      stream.Serialize(ref count);
      if (stream.Reading) {
        Units = new EntityRef[FPMath.Clamp(count, 0, MaxUnits)];
      }
      for (var i = 0; i < Units.Length; i++) {
        stream.Serialize(ref Units[i]);
      }
      stream.Serialize(ref Target);
      stream.Serialize(ref AttackMove);
    }

    public override void Execute(Frame f, PlayerRef player) {
      for (var i = 0; i < Units.Length; i++) {
        if (f.Unsafe.TryGetPointer<Unit>(Units[i], out var unit) && unit->Owner == player) {
          MovementSystem.Order(f, Units[i], unit, AttackMove ? UnitOrder.AttackMove : UnitOrder.Move, Target);
        }
      }
    }
  }

  public unsafe class AttackTargetCommand : FactoryCommand {
    public EntityRef[] Units = System.Array.Empty<EntityRef>();
    public EntityRef Target;

    public override void Serialize(BitStream stream) {
      var count = Units.Length;
      stream.Serialize(ref count);
      if (stream.Reading) {
        Units = new EntityRef[FPMath.Clamp(count, 0, MoveUnitsCommand.MaxUnits)];
      }
      for (var i = 0; i < Units.Length; i++) {
        stream.Serialize(ref Units[i]);
      }
      stream.Serialize(ref Target);
    }

    public override void Execute(Frame f, PlayerRef player) {
      if (CombatSystem.TryGetOwner(f, Target, out var targetOwner) == false) {
        return;
      }
      if (DiplomacySystem.CanAttack(f, player, targetOwner) == false) {
        return;
      }
      for (var i = 0; i < Units.Length; i++) {
        if (f.Unsafe.TryGetPointer<Unit>(Units[i], out var unit) && unit->Owner == player) {
          unit->TargetEntity = Target;
          unit->Order = UnitOrder.AttackTarget;
        }
      }
    }
  }

  public unsafe class BuildCommand : FactoryCommand {
    public AssetRef<BuildingSpec> Building;
    public FPVector3 Position;

    public override void Serialize(BitStream stream) {
      stream.Serialize(ref Building);
      stream.Serialize(ref Position);
    }

    public override void Execute(Frame f, PlayerRef player) {
      ProductionSystem.TryBuild(f, player, Building, Position);
    }
  }

  public unsafe class ProduceCommand : FactoryCommand {
    public EntityRef Building;
    public AssetRef<UnitSpec> Unit;
    public int Count = 1;
    /// <summary>true면 대기열 뒤쪽의 같은 유닛을 Count개 취소하고 환불한다.</summary>
    public bool Cancel;

    public override void Serialize(BitStream stream) {
      stream.Serialize(ref Building);
      stream.Serialize(ref Unit);
      stream.Serialize(ref Count);
      stream.Serialize(ref Cancel);
    }

    public override void Execute(Frame f, PlayerRef player) {
      if (Cancel) {
        ProductionSystem.CancelOrders(f, player, Building, Unit, Count);
      } else {
        ProductionSystem.Enqueue(f, player, Building, Unit, Count);
      }
    }
  }

  public unsafe class SetPresetCommand : FactoryCommand {
    public EntityRef Building;
    public AssetRef<ModuleSpec> Module0;
    public AssetRef<ModuleSpec> Module1;
    public AssetRef<ModuleSpec> Module2;

    public override void Serialize(BitStream stream) {
      stream.Serialize(ref Building);
      stream.Serialize(ref Module0);
      stream.Serialize(ref Module1);
      stream.Serialize(ref Module2);
    }

    public override void Execute(Frame f, PlayerRef player) {
      if (f.Unsafe.TryGetPointer<Building>(Building, out var building) == false || building->Owner != player) {
        return;
      }
      *building->Preset.GetPointer(0) = Module0;
      *building->Preset.GetPointer(1) = Module1;
      *building->Preset.GetPointer(2) = Module2;
    }
  }

  public class DiplomacyCommand : FactoryCommand {
    public DiplomacyAction Action;
    public PlayerRef Target;

    public override void Serialize(BitStream stream) {
      var action = (int)Action;
      stream.Serialize(ref action);
      Action = (DiplomacyAction)action;
      stream.Serialize(ref Target);
    }

    public override void Execute(Frame f, PlayerRef player) {
      DiplomacySystem.Handle(f, player, Target, Action);
    }
  }

  public unsafe class SendNoteCommand : FactoryCommand {
    public int PhraseId;
    public NoteTarget TargetMode;
    public PlayerRef Receiver;

    public override void Serialize(BitStream stream) {
      stream.Serialize(ref PhraseId);
      var mode = (int)TargetMode;
      stream.Serialize(ref mode);
      TargetMode = (NoteTarget)mode;
      stream.Serialize(ref Receiver);
    }

    public override void Execute(Frame f, PlayerRef player) {
      var rules = FactoryWarsUtil.Rules(f);
      if (PhraseId < 0 || PhraseId >= rules.NotePhraseCount) {
        return;
      }
      var state = FactoryWarsUtil.Player(f, player);
      if (state == null || f.Number < state->NextNoteFrame) {
        return;
      }
      state->NextNoteFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, rules.NoteCooldownSeconds);

      for (var i = 0; i < f.PlayerCount; i++) {
        PlayerRef other = i;
        if (other == player || f.Global->Players.GetPointer(i)->Active == false) {
          continue;
        }
        var send = TargetMode == NoteTarget.Everyone
          || (TargetMode == NoteTarget.Player && other == Receiver)
          || (TargetMode == NoteTarget.Allies && DiplomacySystem.GetRelation(f, player, other) == Relation.Allied);
        if (send) {
          f.Events.NoteReceived(player, other, PhraseId);
        }
      }
    }
  }
}
