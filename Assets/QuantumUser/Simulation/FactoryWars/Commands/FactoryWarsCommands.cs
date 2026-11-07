namespace Quantum.FactoryWars {
  using Photon.Deterministic;

  /// <summary>플레이어 일회성 행동의 공통 기반. CommandSystem이 Execute를 호출한다.</summary>
  public abstract unsafe class FactoryCommand : DeterministicCommand {
    public abstract void Execute(Frame f, PlayerRef player);

    protected static void SerializeUnits(BitStream stream, ref EntityRef[] units) {
      var count = units.Length;
      stream.Serialize(ref count);
      if (stream.Reading) {
        units = new EntityRef[FPMath.Clamp(count, 0, MoveUnitsCommand.MaxUnits)];
      }
      for (var i = 0; i < units.Length; i++) {
        stream.Serialize(ref units[i]);
      }
    }

    protected static bool IsOwnWorker(Frame f, PlayerRef player, EntityRef entity) {
      return f.Unsafe.TryGetPointer<Unit>(entity, out var unit) && unit->Owner == player && f.Has<Worker>(entity);
    }
  }

  public unsafe class MoveUnitsCommand : FactoryCommand {
    public const int MaxUnits = 64;

    public EntityRef[] Units = System.Array.Empty<EntityRef>();
    public FPVector3 Target;
    public bool AttackMove;

    public override void Serialize(BitStream stream) {
      SerializeUnits(stream, ref Units);
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

  /// <summary>하던 일을 멈춘다. 공병은 작업도 그만둔다.</summary>
  public unsafe class StopUnitsCommand : FactoryCommand {
    public EntityRef[] Units = System.Array.Empty<EntityRef>();

    public override void Serialize(BitStream stream) {
      SerializeUnits(stream, ref Units);
    }

    public override void Execute(Frame f, PlayerRef player) {
      for (var i = 0; i < Units.Length; i++) {
        if (f.Unsafe.TryGetPointer<Unit>(Units[i], out var unit) && unit->Owner == player) {
          MovementSystem.Stop(f, Units[i], unit);
        }
      }
    }
  }

  public unsafe class AttackTargetCommand : FactoryCommand {
    public EntityRef[] Units = System.Array.Empty<EntityRef>();
    public EntityRef Target;

    public override void Serialize(BitStream stream) {
      SerializeUnits(stream, ref Units);
      stream.Serialize(ref Target);
    }

    public override void Execute(Frame f, PlayerRef player) {
      if (CombatSystem.TryGetOwner(f, Target, out var targetOwner) == false) {
        return;
      }
      if (FactoryWarsUtil.InTruce(f) && targetOwner != player) {
        f.Events.ActionRejected(player, ActionKind.Attack, RejectReason.Truce, 0);
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

  /// <summary>공병이 지정 위치에 건물을 짓는다. 비용은 부지를 잡을 때 낸다.</summary>
  public unsafe class BuildCommand : FactoryCommand {
    public EntityRef Engineer;
    public AssetRef<BuildingSpec> Building;
    public FPVector3 Position;

    public override void Serialize(BitStream stream) {
      stream.Serialize(ref Engineer);
      stream.Serialize(ref Building);
      stream.Serialize(ref Position);
    }

    public override void Execute(Frame f, PlayerRef player) {
      if (IsOwnWorker(f, player, Engineer) == false) {
        f.Events.ActionRejected(player, ActionKind.Build, RejectReason.Invalid, 0);
        return;
      }
      ProductionSystem.TryBuild(f, player, Engineer, Building, Position);
    }
  }

  /// <summary>공병이 철광산이나 숲에서 채집해 반입 지점으로 나른다.</summary>
  public unsafe class GatherCommand : FactoryCommand {
    public EntityRef[] Units = System.Array.Empty<EntityRef>();
    public EntityRef Node;

    public override void Serialize(BitStream stream) {
      SerializeUnits(stream, ref Units);
      stream.Serialize(ref Node);
    }

    public override void Execute(Frame f, PlayerRef player) {
      if (f.Has<ResourceNode>(Node) == false) {
        f.Events.ActionRejected(player, ActionKind.Gather, RejectReason.Invalid, 0);
        return;
      }
      if (FactoryWarsUtil.IsReachable(f, player, f.Unsafe.GetPointer<Transform3D>(Node)->Position) == false) {
        f.Events.ActionRejected(player, ActionKind.Gather, RejectReason.Truce, 0);
        return;
      }
      for (var i = 0; i < Units.Length; i++) {
        if (IsOwnWorker(f, player, Units[i])) {
          WorkerSystem.Assign(f, Units[i], WorkerTask.Gather, Node);
        }
      }
    }
  }

  /// <summary>공병이 건물을 수리한다. 짓다 만 건물이면 건설을 이어 간다.</summary>
  public unsafe class RepairCommand : FactoryCommand {
    public EntityRef[] Units = System.Array.Empty<EntityRef>();
    public EntityRef Target;

    public override void Serialize(BitStream stream) {
      SerializeUnits(stream, ref Units);
      stream.Serialize(ref Target);
    }

    public override void Execute(Frame f, PlayerRef player) {
      if (f.Unsafe.TryGetPointer<Building>(Target, out var building) == false || building->Owner != player) {
        f.Events.ActionRejected(player, ActionKind.Repair, RejectReason.Invalid, 0);
        return;
      }
      for (var i = 0; i < Units.Length; i++) {
        if (IsOwnWorker(f, player, Units[i]) && WorkerSystem.AssignToBuilding(f, Units[i], Target) == false) {
          f.Events.ActionRejected(player, ActionKind.Repair, RejectReason.Invalid, 0);
          return;
        }
      }
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

  /// <summary>가공소를 켜거나 끈다. 끄면 철과 전기를 쓰지 않는다.</summary>
  public unsafe class SetProcessorCommand : FactoryCommand {
    public EntityRef Building;
    public bool Enabled;

    public override void Serialize(BitStream stream) {
      stream.Serialize(ref Building);
      stream.Serialize(ref Enabled);
    }

    public override void Execute(Frame f, PlayerRef player) {
      if (f.Unsafe.TryGetPointer<Building>(Building, out var building) == false || building->Owner != player
          || f.FindAsset(building->Spec).ProcessSeconds <= FP._0) {
        f.Events.ActionRejected(player, ActionKind.Processor, RejectReason.Invalid, 0);
        return;
      }
      building->ProcessorOff = Enabled == false;
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
      var state = FactoryWarsUtil.Player(f, player);
      if (state == null) {
        return;
      }

      var receivers = 0;
      for (var i = 0; i < f.PlayerCount; i++) {
        if (IsReceiver(f, player, i)) {
          receivers |= 1 << i;
        }
      }
      if (PhraseId < 0 || PhraseId >= rules.NotePhraseCount || receivers == 0) {
        f.Events.ActionRejected(player, ActionKind.Note, RejectReason.Invalid, 0);
        return;
      }
      if (f.Number < state->NextNoteFrame) {
        f.Events.ActionRejected(player, ActionKind.Note, RejectReason.Cooldown, 0);
        return;
      }
      var cost = TargetMode == NoteTarget.Player ? rules.NoteSingleCost : rules.NoteGroupCost;
      if (state->Stock.Iron < cost) {
        f.Events.ActionRejected(player, ActionKind.Note, RejectReason.InsufficientResources, (int)MissingResource.Iron);
        return;
      }

      state->Stock.Iron -= cost;
      state->NextNoteFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, rules.NoteCooldownSeconds);
      for (var i = 0; i < f.PlayerCount; i++) {
        if ((receivers & (1 << i)) != 0) {
          f.Events.NoteReceived(player, i, PhraseId);
          BotSystem.OnNoteReceived(f, i, player, PhraseId);
        }
      }
    }

    bool IsReceiver(Frame f, PlayerRef player, PlayerRef other) {
      if (other == player || f.Global->Players.GetPointer(other)->Active == false) {
        return false;
      }
      return TargetMode == NoteTarget.Everyone
        || (TargetMode == NoteTarget.Player && other == Receiver)
        || (TargetMode == NoteTarget.Allies && DiplomacySystem.GetRelation(f, player, other) == Relation.Allied);
    }
  }
}
