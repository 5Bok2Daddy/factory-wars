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

    /// <summary>내 유닛이고 명령을 받는 유닛이면 true. 항모 드론·상륙정은 스스로 움직인다(항목 33).</summary>
    protected static bool IsOwnCommandable(Frame f, PlayerRef player, EntityRef entity, out Unit* unit) {
      return f.Unsafe.TryGetPointer<Unit>(entity, out unit) && unit->Owner == player && UltimateSystem.IsAutonomous(f, entity) == false;
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
        if (IsOwnCommandable(f, player, Units[i], out var unit)) {
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
        if (IsOwnCommandable(f, player, Units[i], out var unit)) {
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
      if (f.Unsafe.TryGetPointer<CityBuilding>(Target, out var city)) {
        if (FactoryWarsUtil.InTruce(f)) {
          f.Events.ActionRejected(player, ActionKind.Attack, RejectReason.Truce, 0);
          return;
        }
        if (city->Occupant.IsValid && DiplomacySystem.CanAttack(f, player, city->Occupant) == false) {
          return;
        }
        Order(f, player);
        return;
      }
      if (f.Has<Barricade>(Target)) {
        if (FactoryWarsUtil.InTruce(f)) {
          f.Events.ActionRejected(player, ActionKind.Attack, RejectReason.Truce, 0);
          return;
        }
        Order(f, player);
        return;
      }
      if (CombatSystem.TryGetOwner(f, Target, out var targetOwner) == false) {
        return;
      }
      if (FactoryWarsUtil.IsVisibleTo(f, player, Target) == false) {
        // 항목 34: 안개 때문에 안 보이는 표적은 이유를 알려 준다. 고지대 은폐는 예전처럼 조용히 무시한다.
        if (FogSystem.Enabled(f) && FogSystem.CanSee(f, player, Target) == false) {
          f.Events.ActionRejected(player, ActionKind.Attack, RejectReason.NoVision, 0);
        }
        return;
      }
      if (FactoryWarsUtil.InTruce(f) && targetOwner != player) {
        f.Events.ActionRejected(player, ActionKind.Attack, RejectReason.Truce, 0);
        return;
      }
      if (DiplomacySystem.CanAttack(f, player, targetOwner) == false) {
        return;
      }
      Order(f, player);
    }

    void Order(Frame f, PlayerRef player) {
      for (var i = 0; i < Units.Length; i++) {
        if (IsOwnCommandable(f, player, Units[i], out var unit)) {
          unit->TargetEntity = Target;
          unit->Order = UnitOrder.AttackTarget;
        }
      }
    }
  }

  /// <summary>보병 분대를 진지 건물(도시 진지 건물, 벙커)로 보낸다. 닿으면 들어간다. 차량·공병·공중은 못 들어간다.</summary>
  public unsafe class EnterGarrisonCommand : FactoryCommand {
    public EntityRef[] Units = System.Array.Empty<EntityRef>();
    public EntityRef Building;

    public override void Serialize(BitStream stream) {
      SerializeUnits(stream, ref Units);
      stream.Serialize(ref Building);
    }

    public override void Execute(Frame f, PlayerRef player) {
      if (CitySystem.TryHost(f, Building, out var shape, out _, out _) == false) {
        f.Events.ActionRejected(player, ActionKind.Garrison, RejectReason.Invalid, 0);
        return;
      }
      if (CitySystem.CanEnter(f, player, Building) == false) {
        var truce = FactoryWarsUtil.IsReachable(f, player, f.Unsafe.GetPointer<Transform3D>(Building)->Position) == false;
        f.Events.ActionRejected(player, ActionKind.Garrison, truce ? RejectReason.Truce : RejectReason.Invalid, 0);
        return;
      }
      for (var i = 0; i < Units.Length; i++) {
        if (f.Unsafe.TryGetPointer<Unit>(Units[i], out var unit) == false || unit->Owner != player || unit->Garrison != EntityRef.None
            || CitySystem.IsInfantry(f.FindAsset(unit->Spec)) == false) {
          continue;
        }
        var from = f.Unsafe.GetPointer<Transform3D>(Units[i])->Position;
        MovementSystem.Order(f, Units[i], unit, UnitOrder.Move, shape.Approach(from, FP._1));
        unit->GarrisonTarget = Building;
      }
    }
  }

  /// <summary>진지 건물에서 자기 분대를 모두 꺼낸다.</summary>
  public unsafe class ExitGarrisonCommand : FactoryCommand {
    public EntityRef Building;

    public override void Serialize(BitStream stream) {
      stream.Serialize(ref Building);
    }

    public override void Execute(Frame f, PlayerRef player) {
      CitySystem.ExitAll(f, player, Building, FP._0);
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

  /// <summary>위치 고수: 멈추고, 사거리 안의 적만 쏜다(쫓지 않음). 항목 30.</summary>
  public unsafe class HoldUnitsCommand : FactoryCommand {
    public EntityRef[] Units = System.Array.Empty<EntityRef>();

    public override void Serialize(BitStream stream) {
      SerializeUnits(stream, ref Units);
    }

    public override void Execute(Frame f, PlayerRef player) {
      for (var i = 0; i < Units.Length; i++) {
        if (IsOwnCommandable(f, player, Units[i], out var unit)) {
          MovementSystem.Stop(f, Units[i], unit);
          unit->Order = UnitOrder.Hold;
        }
      }
    }
  }

  /// <summary>순찰: 지금 위치와 목표 사이를 공격 이동으로 오간다. 항목 30.</summary>
  public unsafe class PatrolUnitsCommand : FactoryCommand {
    public EntityRef[] Units = System.Array.Empty<EntityRef>();
    public FPVector3 Target;

    public override void Serialize(BitStream stream) {
      SerializeUnits(stream, ref Units);
      stream.Serialize(ref Target);
    }

    public override void Execute(Frame f, PlayerRef player) {
      for (var i = 0; i < Units.Length; i++) {
        if (IsOwnCommandable(f, player, Units[i], out var unit) && f.Has<Worker>(Units[i]) == false
            && f.Unsafe.TryGetPointer<Transform3D>(Units[i], out var transform)) {
          MovementSystem.Order(f, Units[i], unit, UnitOrder.Patrol, Target);
          unit->PatrolOrigin = transform->Position;
        }
      }
    }
  }

  /// <summary>생산 건물의 집결 지점. Clear면 지운다. 항목 30.</summary>
  public unsafe class RallyCommand : FactoryCommand {
    public EntityRef Building;
    public FPVector3 Position;
    public bool Clear;

    public override void Serialize(BitStream stream) {
      stream.Serialize(ref Building);
      stream.Serialize(ref Position);
      stream.Serialize(ref Clear);
    }

    public override void Execute(Frame f, PlayerRef player) {
      if (f.Unsafe.TryGetPointer<Building>(Building, out var building) && building->Owner == player && (f.FindAsset(building->Spec).Producible?.Length ?? 0) > 0) {
        building->HasRally = Clear == false;
        building->Rally = Position;
      }
    }
  }

  /// <summary>사령부를 다음 기지 단계로 올린다(항목 31). 비용은 시작할 때 낸다. 올리는 동안에도 사령부는 생산한다.</summary>
  public unsafe class UpgradeBaseCommand : FactoryCommand {
    public EntityRef Building;

    public override void Serialize(BitStream stream) {
      stream.Serialize(ref Building);
    }

    public override void Execute(Frame f, PlayerRef player) {
      TechSystem.StartUpgrade(f, player, Building);
    }
  }

  /// <summary>연구소에서 연구를 시작한다(항목 31). Index는 FactionSpec.Researches 번호.</summary>
  public unsafe class ResearchCommand : FactoryCommand {
    public EntityRef Building;
    public int Index;

    public override void Serialize(BitStream stream) {
      stream.Serialize(ref Building);
      stream.Serialize(ref Index);
    }

    public override void Execute(Frame f, PlayerRef player) {
      TechSystem.StartResearch(f, player, Building, Index);
    }
  }

  /// <summary>궁극 전력 준비를 시작한다(항목 33). 조건과 비용은 UltimateSystem.Locks가 본다.</summary>
  public unsafe class StartUltimateCommand : FactoryCommand {
    public override void Serialize(BitStream stream) {
    }

    public override void Execute(Frame f, PlayerRef player) {
      UltimateSystem.Start(f, player);
    }
  }

  /// <summary>준비가 끝난 궁극 전력을 목표 지점에 쓴다(전략 미사일 발사, 상륙 강습 출발).</summary>
  public unsafe class LaunchUltimateCommand : FactoryCommand {
    public FPVector3 Target;

    public override void Serialize(BitStream stream) {
      stream.Serialize(ref Target);
    }

    public override void Execute(Frame f, PlayerRef player) {
      UltimateSystem.Launch(f, player, Target);
    }
  }

  /// <summary>준비 중이거나 발동 대기 중인 궁극 전력을 취소한다. 비용 일부만 돌려받는다.</summary>
  public unsafe class CancelUltimateCommand : FactoryCommand {
    public override void Serialize(BitStream stream) {
    }

    public override void Execute(Frame f, PlayerRef player) {
      UltimateSystem.Cancel(f, player);
    }
  }

  /// <summary>기권. 다음 승패 판정에서 패배한다(항목 30).</summary>
  public unsafe class SurrenderCommand : FactoryCommand {
    public override void Serialize(BitStream stream) {
    }

    public override void Execute(Frame f, PlayerRef player) {
      var state = FactoryWarsUtil.Player(f, player);
      if (state != null && state->Active) {
        state->Forfeited = true;
      }
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
