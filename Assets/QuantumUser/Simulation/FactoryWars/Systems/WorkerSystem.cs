namespace Quantum.FactoryWars {
  using Photon.Deterministic;

  /// <summary>공병의 채집·운반·건설·수리.</summary>
  public unsafe class WorkerSystem : SystemMainThread {
    /// <summary>공병에게 작업을 준다. 진행 중인 이동은 멈추고 새 대상으로 출발한다.</summary>
    public static void Assign(Frame f, EntityRef entity, WorkerTask task, EntityRef target) {
      var unit = f.Unsafe.GetPointer<Unit>(entity);
      unit->Order = UnitOrder.Stop;
      unit->TargetEntity = EntityRef.None;
      var worker = f.Unsafe.GetPointer<Worker>(entity);
      worker->Task = task;
      worker->Target = target;
      worker->WorkTimer = FP._0;
      worker->NextPathFrame = 0;
      worker->Spot = 0;
      MovementSystem.Halt(f, entity);
    }

    /// <summary>건물 상태에 맞는 작업(건설 또는 수리)을 준다. 할 일이 없으면 false.</summary>
    public static bool AssignToBuilding(Frame f, EntityRef entity, EntityRef target) {
      var building = f.Unsafe.GetPointer<Building>(target);
      if (building->ConstructionRemaining > FP._0) {
        Assign(f, entity, WorkerTask.Build, target);
        return true;
      }
      if (building->Health < f.FindAsset(building->Spec).MaxHealth) {
        Assign(f, entity, WorkerTask.Repair, target);
        return true;
      }
      return false;
    }

    /// <summary>목표 범위 안이면 멈추고 true. 밖이면 경로를 잡고 false.</summary>
    /// <summary>
    /// range 안이면 멈추고 true. square면 정사각형 부지(건물) 기준 거리(가로·세로 중 큰 쪽)로 잰다.
    /// 항목 31: 원형으로 재면 공병이 부지 모서리 쪽에서 5.0을 살짝 넘겨 멈춰 건설이 영영 진행되지 않았다.
    /// </summary>
    static bool MoveInto(Frame f, EntityRef entity, Worker* worker, FPVector3 position, FPVector3 target, FP range, bool square = false) {
      var inside = square
        ? FPMath.Max(FPMath.Abs(position.X - target.X), FPMath.Abs(position.Z - target.Z)) <= range
        : FactoryWarsUtil.FlatDistanceSqr(position, target) <= range * range;
      if (inside) {
        MovementSystem.Halt(f, entity);
        return true;
      }
      if (f.Number >= worker->NextPathFrame
          && f.Unsafe.TryGetPointer<NavMeshPathfinder>(entity, out var pathfinder) && pathfinder->IsActive == false) {
        MovementSystem.SetDestination(f, entity, target);
        worker->NextPathFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, FP._1);
      }
      return false;
    }

    /// <summary>매장량이 남은 같은 종류 노드 중 가장 가까운 것. exclude는 건너뛴다.</summary>
    public static EntityRef NearestNode(Frame f, PlayerRef owner, FPVector3 position, FP radius, ResourceKind kind, EntityRef exclude = default) {
      var best = EntityRef.None;
      // FP.UseableMax는 "반경 없음" 표시다. 거리 제곱의 초기값으로 쓰면 약 181m 밖이 안 보이므로 MaxValue를 쓴다.
      var bestDistance = radius < FP.UseableMax ? radius * radius : FP.MaxValue;
      var nodes = f.Filter<Transform3D, ResourceNode>();
      while (nodes.NextUnsafe(out var entity, out var transform, out var node)) {
        var distance = FactoryWarsUtil.FlatDistanceSqr(transform->Position, position);
        if (entity != exclude && node->Kind == kind && node->Remaining > FP._0 && distance <= bestDistance
            && FactoryWarsUtil.IsReachable(f, owner, transform->Position)) {
          best = entity;
          bestDistance = distance;
        }
      }
      return best;
    }

    static EntityRef NearestDropOff(Frame f, PlayerRef owner, FPVector3 position) {
      var best = EntityRef.None;
      var bestDistance = FP.MaxValue;
      var buildings = f.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out var entity, out var transform, out var building)) {
        if (building->Owner != owner || building->ConstructionRemaining > FP._0 || f.FindAsset(building->Spec).IsDropOff == false) {
          continue;
        }
        var distance = FactoryWarsUtil.FlatDistanceSqr(transform->Position, position);
        if (distance < bestDistance) {
          best = entity;
          bestDistance = distance;
        }
      }
      return best;
    }

    static void SetTask(Worker* worker, WorkerTask task) {
      worker->Task = task;
      worker->WorkTimer = FP._0;
      worker->NextPathFrame = 0;
      worker->Spot = 0;
    }

    /// <summary>이 노드에서 비어 있는 가장 가까운 작업 지점. 번호는 1부터고 없으면 0이다.</summary>
    static int FreeSpot(Frame f, EntityRef self, EntityRef nodeEntity, ResourceNode* node, FPVector3 position, GameRulesSpec rules) {
      var taken = 0L;
      var others = f.Filter<Worker>();
      while (others.NextUnsafe(out var entity, out var other)) {
        if (entity != self && other->Target == nodeEntity && other->Task == WorkerTask.Gather && other->Spot > 0) {
          taken |= 1L << (other->Spot - 1);
        }
      }
      var best = 0;
      var bestDistance = FP.MaxValue;
      var count = node->Shape.SpotCount(rules);
      for (var i = 0; i < count; i++) {
        if ((taken & (1L << i)) != 0) {
          continue;
        }
        var distance = FactoryWarsUtil.FlatDistanceSqr(node->Shape.Spot(i, rules), position);
        if (distance < bestDistance) {
          best = i + 1;
          bestDistance = distance;
        }
      }
      return best;
    }

    static void Gather(Frame f, EntityRef entity, FPVector3 position, Worker* worker, GameRulesSpec rules) {
      if (worker->Carried >= rules.CarryCapacity) {
        SetTask(worker, WorkerTask.Deliver);
        return;
      }
      if (f.Unsafe.TryGetPointer<ResourceNode>(worker->Target, out var node) == false) {
        SetTask(worker, worker->Carried > FP._0 ? WorkerTask.Deliver : WorkerTask.Idle);
        return;
      }
      // 다른 자원을 들고 있으면 먼저 반입한다. 반입이 끝나면 이 노드로 돌아온다.
      if (worker->Carried > FP._0 && worker->CarriedKind != node->Kind) {
        SetTask(worker, WorkerTask.Deliver);
        return;
      }
      if (node->Remaining <= FP._0) {
        worker->Spot = 0;
        worker->Target = NearestNode(f, f.Unsafe.GetPointer<Unit>(entity)->Owner, position, rules.NodeSearchRadius, node->Kind);
        if (worker->Target == EntityRef.None) {
          SetTask(worker, worker->Carried > FP._0 ? WorkerTask.Deliver : WorkerTask.Idle);
        }
        return;
      }

      if (worker->Spot == 0) {
        worker->Spot = FreeSpot(f, entity, worker->Target, node, position, rules);
        if (worker->Spot == 0) {
          // 지점이 다 찼으면 가까운 다른 노드로 가고, 없으면 자리가 날 때까지 기다린다.
          var other = NearestNode(f, f.Unsafe.GetPointer<Unit>(entity)->Owner, position, rules.NodeSearchRadius, node->Kind, worker->Target);
          if (other != EntityRef.None) {
            worker->Target = other;
          }
          return;
        }
      }

      if (MoveInto(f, entity, worker, position, node->Shape.Spot(worker->Spot - 1, rules), rules.SpotArrival) == false) {
        return;
      }
      worker->WorkTimer += f.DeltaTime * SniperSystem.GatherMultiplier(f, worker->Target);
      if (worker->WorkTimer < (node->Kind == ResourceKind.Iron ? rules.IronGatherSeconds : rules.WoodGatherSeconds)) {
        return;
      }

      var amount = FPMath.Min(rules.CarryCapacity - worker->Carried, node->Remaining);
      node->Remaining -= amount;
      worker->Carried += amount;
      worker->CarriedKind = node->Kind;
      SetTask(worker, WorkerTask.Deliver);
    }

    static void Deliver(Frame f, EntityRef entity, FPVector3 position, Unit* unit, Worker* worker, GameRulesSpec rules) {
      var dropOff = NearestDropOff(f, unit->Owner, position);
      if (dropOff == EntityRef.None) {
        // 반입 지점이 생길 때까지 자원을 든 채 기다린다.
        MovementSystem.Halt(f, entity);
        return;
      }
      var spec = f.FindAsset(f.Unsafe.GetPointer<Building>(dropOff)->Spec);
      var target = f.Unsafe.GetPointer<Transform3D>(dropOff)->Position;
      if (MoveInto(f, entity, worker, position, target, spec.FootprintRadius + rules.WorkRange, true)) {
        var state = FactoryWarsUtil.Player(f, unit->Owner);
        if (worker->CarriedKind == ResourceKind.Iron) {
          state->Stock.Iron += worker->Carried;
          state->Earned.Iron += worker->Carried;
        } else {
          state->Stock.Wood += worker->Carried;
          state->Earned.Wood += worker->Carried;
        }
        worker->Carried = FP._0;
        SetTask(worker, WorkerTask.Gather);
      }
    }

    static void Build(Frame f, EntityRef entity, FPVector3 position, Worker* worker, GameRulesSpec rules) {
      if (f.Unsafe.TryGetPointer<Building>(worker->Target, out var building) == false || building->ConstructionRemaining <= FP._0) {
        SetTask(worker, WorkerTask.Idle);
        return;
      }
      var spec = f.FindAsset(building->Spec);
      var target = f.Unsafe.GetPointer<Transform3D>(worker->Target)->Position;
      if (MoveInto(f, entity, worker, position, target, spec.FootprintRadius + rules.WorkRange, true) == false) {
        return;
      }

      var progress = FPMath.Min(f.DeltaTime, building->ConstructionRemaining);
      building->ConstructionRemaining -= progress;
      var gained = spec.MaxHealth * (FP._1 - rules.FoundationHealthRatio) * progress / spec.BuildTimeSeconds;
      building->Health = FPMath.Min(spec.MaxHealth, building->Health + gained);
      if (building->ConstructionRemaining <= FP._0) {
        SetTask(worker, WorkerTask.Idle);
      }
    }

    static void Repair(Frame f, EntityRef entity, FPVector3 position, Unit* unit, Worker* worker, GameRulesSpec rules) {
      if (f.Unsafe.TryGetPointer<Building>(worker->Target, out var building) == false) {
        SetTask(worker, WorkerTask.Idle);
        return;
      }
      var spec = f.FindAsset(building->Spec);
      if (building->Health >= spec.MaxHealth) {
        SetTask(worker, WorkerTask.Idle);
        return;
      }
      var target = f.Unsafe.GetPointer<Transform3D>(worker->Target)->Position;
      if (MoveInto(f, entity, worker, position, target, spec.FootprintRadius + rules.WorkRange, true) == false) {
        return;
      }

      var heal = FPMath.Min(rules.RepairPerSecond * f.DeltaTime, spec.MaxHealth - building->Health);
      // 자원별로 낸다. 곱한 뒤 나눠야 작은 값에서 정밀도를 잃지 않는다.
      var cost = spec.Cost * (heal * rules.RepairCostRatio) / spec.MaxHealth;
      var state = FactoryWarsUtil.Player(f, unit->Owner);
      if (state->Stock.Covers(cost) == false) {
        SetTask(worker, WorkerTask.Idle);
        return;
      }
      state->Stock -= cost;
      building->Health += heal;
    }

    public override void Update(Frame f) {
      if (f.Global->GameEnded) {
        return;
      }
      var rules = FactoryWarsUtil.Rules(f);
      var workers = f.Filter<Transform3D, Unit, Worker>();
      while (workers.NextUnsafe(out var entity, out var transform, out var unit, out var worker)) {
        switch (worker->Task) {
          case WorkerTask.Gather: Gather(f, entity, transform->Position, worker, rules); break;
          case WorkerTask.Deliver: Deliver(f, entity, transform->Position, unit, worker, rules); break;
          case WorkerTask.Build: Build(f, entity, transform->Position, worker, rules); break;
          case WorkerTask.Repair: Repair(f, entity, transform->Position, unit, worker, rules); break;
        }
      }
    }
  }
}
