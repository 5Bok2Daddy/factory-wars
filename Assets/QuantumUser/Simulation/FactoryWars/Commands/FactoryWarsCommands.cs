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
      return f.Unsafe.TryGetPointer<Unit>(entity, out var unit) && unit->Owner == player
          && unit->Transport == EntityRef.None && unit->Garrison == EntityRef.None && f.Has<Worker>(entity);
    }

    /// <summary>내 유닛이고 명령을 받는 유닛이면 true. 항모 드론·상륙정은 스스로 움직인다(항목 33).</summary>
    protected static bool IsOwnCommandable(Frame f, PlayerRef player, EntityRef entity, out Unit* unit) {
      return f.Unsafe.TryGetPointer<Unit>(entity, out unit) && unit->Owner == player && unit->Transport == EntityRef.None
          && (unit->Garrison == EntityRef.None || f.Has<Worker>(entity) == false)
          && UltimateSystem.IsAutonomous(f, entity) == false;
    }
  }

  public unsafe class MoveUnitsCommand : FactoryCommand {
    public const int MaxUnits = 64;
    public const byte ModeNormal = 0;
    public const byte ModeFormUp = 1;
    public const byte ModeSpread = 2;

    public EntityRef[] Units = System.Array.Empty<EntityRef>();
    public FPVector3 Target;
    public bool AttackMove;
    /// <summary>0=일반 자동 정렬 이동, 1=현재 위치 기준 전술 대형 정렬(Form Up), 2=포격 회피 산개(Spread).</summary>
    public byte FormationMode;

    public override void Serialize(BitStream stream) {
      SerializeUnits(stream, ref Units);
      stream.Serialize(ref Target);
      stream.Serialize(ref AttackMove);
      stream.Serialize(ref FormationMode);
    }

    static int RoleTier(UnitSpec spec) {
      // 앞열(0): 전차·중기갑 / 중간열(1): 소총수·일반 보병 / 뒷열(2): 저격수·지원·포병·공병.
      if (spec.Size == UnitSize.Large || (spec.Size == UnitSize.Medium && spec.Attack == AttackType.AntiArmor) || spec.IsVehicle) {
        return 0;
      }
      if (spec.Range >= (FP)15 || spec.Attack == AttackType.None || spec.Ability == SpecialAbility.FieldRepair || spec.Size == UnitSize.Engineer) {
        return 2;
      }
      return 1;
    }

    static FPVector3 FindFrontlineDirection(Frame f, PlayerRef player, FPVector3 centroid) {
      var best = FP.MaxValue;
      var enemyPos = FPVector3.Zero;
      var found = false;
      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out var entity, out var transform, out var u)) {
        if (u->Health <= FP._0 || DiplomacySystem.CanAttack(f, player, u->Owner) == false || FactoryWarsUtil.IsVisibleTo(f, player, entity) == false) {
          continue;
        }
        var d = FactoryWarsUtil.FlatDistanceSqr(transform->Position, centroid);
        if (d < best) {
          best = d;
          enemyPos = transform->Position;
          found = true;
        }
      }
      var dir = (found ? enemyPos : FPVector3.Zero) - centroid;
      dir.Y = FP._0;
      var len = dir.Magnitude;
      return len > FP._0_10 ? dir / len : new FPVector3(FP._0, FP._0, FP._1);
    }

    public override void Execute(Frame f, PlayerRef player) {
      var valid = new System.Collections.Generic.List<(EntityRef Entity, int Tier, int Index)>(Units.Length);
      var centroid = FPVector3.Zero;
      for (var i = 0; i < Units.Length; i++) {
        if (IsOwnCommandable(f, player, Units[i], out var unit) && f.Unsafe.TryGetPointer<Transform3D>(Units[i], out var t)) {
          var spec = f.FindAsset(unit->Spec);
          valid.Add((Units[i], RoleTier(spec), i));
          centroid += t->Position;
        }
      }
      if (valid.Count == 0) {
        return;
      }
      var order = AttackMove ? UnitOrder.AttackMove : UnitOrder.Move;
      if (valid.Count == 1 && FormationMode == ModeNormal) {
        MovementSystem.Order(f, valid[0].Entity, f.Unsafe.GetPointer<Unit>(valid[0].Entity), order, Target);
        return;
      }
      centroid /= valid.Count;
      centroid.Y = FP._0;
      var map = FactoryWarsUtil.MapInfo(f);
      FPVector3 ClampMap(FPVector3 p) => new FPVector3(
        FPMath.Clamp(p.X, map.BoundsMin.X + FP._2, map.BoundsMax.X - FP._2),
        FP._0,
        FPMath.Clamp(p.Z, map.BoundsMin.Y + FP._2, map.BoundsMax.Y - FP._2));

      if (FormationMode == ModeSpread) {
        // 산개(Spread): 중심에서 바깥으로 방사형 전개(대형 차량은 반경에 비례해 넓게 전개).
        var baseRadius = FP._3 + FP._0_50;
        for (var i = 0; i < valid.Count; i++) {
          var e = valid[i].Entity;
          var uPtr = f.Unsafe.GetPointer<Unit>(e);
          var uRadius = MovementSystem.UnitRadius(f.FindAsset(uPtr->Spec));
          var radius = FPMath.Max(baseRadius, uRadius * FP._2 + FP._1_50);
          var pos = f.Unsafe.GetPointer<Transform3D>(e)->Position;
          var delta = new FPVector3(pos.X - centroid.X, FP._0, pos.Z - centroid.Z);
          var len = delta.Magnitude;
          FPVector3 dir;
          if (len > FP._0_10) {
            dir = delta / len;
          } else {
            var angle = (FP)(i * 360 / valid.Count) * FP.Deg2Rad;
            dir = new FPVector3(FPMath.Cos(angle), FP._0, FPMath.Sin(angle));
          }
          MovementSystem.Order(f, e, uPtr, UnitOrder.Move, ClampMap(pos + dir * radius));
        }
        return;
      }

      // 반경 인지 자동 대형 정렬: 각 열의 최대 유닛 지름 + 0.8m 여백으로 전차·보병 혼성 부대도 절대 겹치지 않게 배치.
      valid.Sort((a, b) => a.Tier != b.Tier ? a.Tier.CompareTo(b.Tier) : a.Index.CompareTo(b.Index));
      var anchor = FormationMode == ModeFormUp ? centroid : new FPVector3(Target.X, FP._0, Target.Z);
      FPVector3 forward;
      if (FormationMode == ModeFormUp) {
        forward = FindFrontlineDirection(f, player, centroid);
      } else {
        var d = anchor - centroid;
        d.Y = FP._0;
        var len = d.Magnitude;
        forward = len > FP._0_10 ? d / len : new FPVector3(FP._0, FP._0, FP._1);
      }
      var right = new FPVector3(-forward.Z, FP._0, forward.X);
      var colPad = (FP)8 / (FP)10; // 0.8m padding
      var rowPad = FP._1; // 1.0m row depth padding
      const int MaxPerRow = 6;
      var rows = new System.Collections.Generic.List<System.Collections.Generic.List<EntityRef>>();
      var rowRadii = new System.Collections.Generic.List<FP>();
      for (var tier = 0; tier <= 2; tier++) {
        var bucket = new System.Collections.Generic.List<EntityRef>();
        for (var i = 0; i < valid.Count; i++) {
          if (valid[i].Tier == tier) {
            bucket.Add(valid[i].Entity);
          }
        }
        for (var offset = 0; offset < bucket.Count; offset += MaxPerRow) {
          var take = System.Math.Min(MaxPerRow, bucket.Count - offset);
          var row = new System.Collections.Generic.List<EntityRef>(take);
          var maxR = (FP)65 / (FP)100;
          for (var k = 0; k < take; k++) {
            var e = bucket[offset + k];
            row.Add(e);
            var r = MovementSystem.UnitRadius(f.FindAsset(f.Unsafe.GetPointer<Unit>(e)->Spec));
            if (r > maxR) {
              maxR = r;
            }
          }
          rows.Add(row);
          rowRadii.Add(maxR);
        }
      }
      // 행별 누적 깊이 계산 후 전체 중심을 anchor에 맞춘다(전차 앞열 · 보병 중간 · 저격/지원 뒷열).
      var depths = new FP[rows.Count];
      for (var r = 1; r < rows.Count; r++) {
        depths[r] = depths[r - 1] + rowRadii[r - 1] + rowRadii[r] + rowPad;
      }
      var midDepth = rows.Count > 0 ? depths[rows.Count - 1] * FP._0_50 : FP._0;
      for (var r = 0; r < rows.Count; r++) {
        var row = rows[r];
        var colSpacing = rowRadii[r] * FP._2 + colPad;
        var halfCols = (FP)(row.Count - 1) * FP._0_50;
        var depthOffset = midDepth - depths[r];
        for (var c = 0; c < row.Count; c++) {
          var sideOffset = (c - halfCols) * colSpacing;
          var slot = ClampMap(anchor + right * sideOffset + forward * depthOffset);
          var e = row[c];
          MovementSystem.Order(f, e, f.Unsafe.GetPointer<Unit>(e), order, slot);
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
      // 중앙 암시장 공격: 맞히는 순간 그 플레이어는 영구 적대가 된다(NpcTraderSystem.Hit).
      if (f.Unsafe.TryGetPointer<NpcTrader>(Target, out var trader)) {
        if (FactoryWarsUtil.InTruce(f)) {
          f.Events.ActionRejected(player, ActionKind.Attack, RejectReason.Truce, 0);
          return;
        }
        if (trader->Destroyed == false) {
          Order(f, player);
        }
        return;
      }
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
          if (f.FindAsset(unit->Spec).GarrisonFireOnly && unit->Garrison == EntityRef.None) {
            // 항목 34-C: 주둔 저격수는 지상에서 쏠 수 없다(건물에 올라가야 사격 가능).
            f.Events.ActionRejected(player, ActionKind.Attack, RejectReason.NeedGarrison, 0);
            continue;
          }
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
        if (f.Unsafe.TryGetPointer<Unit>(Units[i], out var unit) == false || unit->Owner != player
            || CitySystem.CanOccupy(f, Units[i], unit, Building) == false) {
          continue;
        }
        CitySystem.OrderEntry(f, Units[i], unit, Building, shape);
      }
    }
  }

  /// <summary>진지의 자기 분대를 꺼낸다. 채굴 시설에서는 공병 한 명씩 꺼낸다.</summary>
  public unsafe class ExitGarrisonCommand : FactoryCommand {
    public EntityRef Building;

    public override void Serialize(BitStream stream) {
      stream.Serialize(ref Building);
    }

    public override void Execute(Frame f, PlayerRef player) {
      var isExtractor = f.Unsafe.TryGetPointer<Building>(Building, out var building) && f.FindAsset(building->Spec).RequiresResourceNode;
      CitySystem.ExitAll(f, player, Building, FP._0, isExtractor ? 1 : int.MaxValue);
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

namespace Quantum.FactoryWars {
  using Photon.Deterministic;

  /// <summary>항목 34-C 은신: 지상 저격수가 지속 시간 동안 은신한다(재사용 대기 있음).</summary>
  public unsafe class StealthCommand : FactoryCommand {
    public EntityRef[] Units = System.Array.Empty<EntityRef>();

    public override void Serialize(BitStream stream) {
      SerializeUnits(stream, ref Units);
    }

    public override void Execute(Frame f, PlayerRef player) {
      for (var i = 0; i < Units.Length; i++) {
        if (IsOwnCommandable(f, player, Units[i], out var unit)) {
          SniperSystem.TryStealth(f, player, unit);
        }
      }
    }
  }

  /// <summary>항목 34-C 채굴 교란 장치: 은신한 저격수가 가까운 자원 노드에 장치를 설치하기 시작한다(설치 시간 동안 제자리).</summary>
  public unsafe class PlaceJammerCommand : FactoryCommand {
    public EntityRef Sniper;
    public EntityRef Node;

    public override void Serialize(BitStream stream) {
      stream.Serialize(ref Sniper);
      stream.Serialize(ref Node);
    }

    public override void Execute(Frame f, PlayerRef player) {
      if (IsOwnCommandable(f, player, Sniper, out var unit)) {
        SniperSystem.TryPlaceJammer(f, player, Sniper, unit, Node);
      }
    }
  }
}

namespace Quantum.FactoryWars {
  using Photon.Deterministic;

  /// <summary>항목 34-D 항공 작전 지시: 비행장 격납 칸, 목표 지점(지금 보이는 칸), 진입 방향.</summary>
  public unsafe class OrderSortieCommand : FactoryCommand {
    public EntityRef Airfield;
    public int Slot;
    public FPVector3 Target;
    public FPVector2 Heading;

    public override void Serialize(BitStream stream) {
      stream.Serialize(ref Airfield);
      stream.Serialize(ref Slot);
      stream.Serialize(ref Target);
      stream.Serialize(ref Heading);
    }

    public override void Execute(Frame f, PlayerRef player) {
      SortieSystem.TryLaunch(f, player, Airfield, Slot, Target, Heading);
    }
  }
}

namespace Quantum.FactoryWars {
  using Photon.Deterministic;

  /// <summary>항목 37 전술 도크린 선택(1 공군 타격 사령부, 2 그림자 특수전단, 3 기갑 요새화 군단). 한 번만 고를 수 있다.</summary>
  public unsafe class ChooseDoctrineCommand : FactoryCommand {
    public int Doctrine;

    public override void Serialize(BitStream stream) {
      stream.Serialize(ref Doctrine);
    }

    public override void Execute(Frame f, PlayerRef player) {
      TacticalSystem.Choose(f, player, Doctrine, false);
    }
  }
}

namespace Quantum.FactoryWars {
  using Photon.Deterministic;

  /// <summary>중앙 암시장 거래(1 용병 투입, 2 자폭 드론 타격: Target 지점, 3 비행장 긴급 재정비).</summary>
  /// <summary>항목 37: 공병이 다리에 폭약을 설치(Demolish)하거나 폰툰으로 가교를 복구(Repair)한다. 다리 번호는 MapSpec.Bridges 순서다.</summary>
  public unsafe class WorkBridgeCommand : FactoryCommand {
    public EntityRef[] Units = System.Array.Empty<EntityRef>();
    public int Bridge;
    public bool Demolish;

    public override void Serialize(BitStream stream) {
      SerializeUnits(stream, ref Units);
      stream.Serialize(ref Bridge);
      stream.Serialize(ref Demolish);
    }

    public override void Execute(Frame f, PlayerRef player) {
      var target = DepthSystem.BridgeEntity(f, Bridge);
      if (target == EntityRef.None) {
        f.Events.ActionRejected(player, ActionKind.Repair, RejectReason.Invalid, 0);
        return;
      }
      var bridge = f.Unsafe.GetPointer<Bridge>(target);
      if (bridge->Destroyed == Demolish) {
        f.Events.ActionRejected(player, ActionKind.Repair, RejectReason.Invalid, 0);
        return;
      }
      if (Demolish && FactoryWarsUtil.InTruce(f)) {
        f.Events.ActionRejected(player, ActionKind.Repair, RejectReason.Truce, 0);
        return;
      }
      for (var i = 0; i < Units.Length; i++) {
        if (IsOwnWorker(f, player, Units[i])) {
          WorkerSystem.Assign(f, Units[i], Demolish ? WorkerTask.Demolish : WorkerTask.Pontoon, target);
        }
      }
    }
  }

  /// <summary>항목 37: 공병이 기갑 잔해를 분해해 고철을 회수한다.</summary>
  public unsafe class SalvageCommand : FactoryCommand {
    public EntityRef[] Units = System.Array.Empty<EntityRef>();
    public EntityRef Wreck;

    public override void Serialize(BitStream stream) {
      SerializeUnits(stream, ref Units);
      stream.Serialize(ref Wreck);
    }

    public override void Execute(Frame f, PlayerRef player) {
      if (f.Has<Wreck>(Wreck) == false) {
        f.Events.ActionRejected(player, ActionKind.Gather, RejectReason.Invalid, 0);
        return;
      }
      for (var i = 0; i < Units.Length; i++) {
        if (IsOwnWorker(f, player, Units[i])) {
          WorkerSystem.Assign(f, Units[i], WorkerTask.Salvage, Wreck);
        }
      }
    }
  }

  public unsafe class TradeCommand : FactoryCommand {
    public int Offer;
    public FPVector3 Target;

    public override void Serialize(BitStream stream) {
      stream.Serialize(ref Offer);
      stream.Serialize(ref Target);
    }

    public override void Execute(Frame f, PlayerRef player) {
      NpcTraderSystem.Trade(f, player, Offer, Target);
    }
  }

  /// <summary>공장 증축 명령: 선택한 공장을 Lv.1 -> Lv.2(+25%) -> Lv.3(+50%)로 확장한다.</summary>
  public unsafe class UpgradeFactoryCommand : FactoryCommand {
    public EntityRef Building;

    public override void Serialize(BitStream stream) {
      stream.Serialize(ref Building);
    }

    public override void Execute(Frame f, PlayerRef player) {
      FactorySystem.TryUpgradeFactory(f, player, Building);
    }
  }

  /// <summary>비상 과부하(Overdrive) 명령: 20초간 생산 속도 +50%, 전력 소비 2배, 초당 소량 과열 피해.</summary>
  public unsafe class OverdriveFactoryCommand : FactoryCommand {
    public EntityRef Building;

    public override void Serialize(BitStream stream) {
      stream.Serialize(ref Building);
    }

    public override void Execute(Frame f, PlayerRef player) {
      FactorySystem.ToggleOverdrive(f, player, Building);
    }
  }

  /// <summary>수송 탑승 명령: 선택한 보병·전차가 아군 수송 유닛(APC·상륙정·수송헬기) 후면 램프로 이동해 탑승한다.</summary>
  public unsafe class BoardTransportCommand : FactoryCommand {
    public EntityRef[] Units = System.Array.Empty<EntityRef>();
    public EntityRef Transport;

    public override void Serialize(BitStream stream) {
      SerializeUnits(stream, ref Units);
      stream.Serialize(ref Transport);
    }

    public override void Execute(Frame f, PlayerRef player) {
      TransportSystem.OrderBoard(f, player, Units, Transport);
    }
  }

  /// <summary>수송 하차 명령: SpecificUnit이 None이면 전원 하차, 지정되어 있으면 해당 승객 1기만 하차시킨다.</summary>
  public unsafe class UnloadTransportCommand : FactoryCommand {
    public EntityRef Transport;
    public EntityRef SpecificUnit;

    public override void Serialize(BitStream stream) {
      stream.Serialize(ref Transport);
      stream.Serialize(ref SpecificUnit);
    }

    public override void Execute(Frame f, PlayerRef player) {
      TransportSystem.Unload(f, player, Transport, SpecificUnit);
    }
  }
}
