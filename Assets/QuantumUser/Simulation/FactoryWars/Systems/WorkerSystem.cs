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
    static bool MoveInto(Frame f, EntityRef entity, Worker* worker, FPVector3 position, FPVector3 target, FP range) {
      if (FactoryWarsUtil.FlatDistanceSqr(position, target) <= range * range) {
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

    static EntityRef NearestNode(Frame f, FPVector3 position, FP radius) {
      var best = EntityRef.None;
      var bestDistance = radius * radius;
      var nodes = f.Filter<Transform3D, ResourceNode>();
      while (nodes.NextUnsafe(out var entity, out var transform, out var node)) {
        var distance = FactoryWarsUtil.FlatDistanceSqr(transform->Position, position);
        if (node->RemainingFunds > FP._0 && distance <= bestDistance) {
          best = entity;
          bestDistance = distance;
        }
      }
      return best;
    }

    static EntityRef NearestRefinery(Frame f, PlayerRef owner, FPVector3 position) {
      var best = EntityRef.None;
      var bestDistance = FP.UseableMax;
      var buildings = f.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out var entity, out var transform, out var building)) {
        if (building->Owner != owner || building->ConstructionRemaining > FP._0 || f.FindAsset(building->Spec).IsRefinery == false) {
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
    }

    static void Gather(Frame f, EntityRef entity, FPVector3 position, Worker* worker, GameRulesSpec rules) {
      if (worker->Carried >= rules.CarryCapacity) {
        SetTask(worker, WorkerTask.Deliver);
        return;
      }
      if (f.Unsafe.TryGetPointer<ResourceNode>(worker->Target, out var node) == false || node->RemainingFunds <= FP._0) {
        worker->Target = NearestNode(f, position, rules.NodeSearchRadius);
        if (worker->Target == EntityRef.None) {
          SetTask(worker, worker->Carried > FP._0 ? WorkerTask.Deliver : WorkerTask.Idle);
        }
        return;
      }

      var nodePosition = f.Unsafe.GetPointer<Transform3D>(worker->Target)->Position;
      if (MoveInto(f, entity, worker, position, nodePosition, rules.ResourceNodeRadius + rules.WorkRange) == false) {
        return;
      }
      worker->WorkTimer += f.DeltaTime;
      if (worker->WorkTimer < rules.GatherSeconds) {
        return;
      }

      var amount = FPMath.Min(rules.CarryCapacity - worker->Carried, node->RemainingFunds);
      node->RemainingFunds -= amount;
      worker->Carried += amount;
      if (node->RemainingFunds <= FP._0) {
        f.Destroy(worker->Target);
      }
      SetTask(worker, WorkerTask.Deliver);
    }

    static void Deliver(Frame f, EntityRef entity, FPVector3 position, Unit* unit, Worker* worker, GameRulesSpec rules) {
      var refinery = NearestRefinery(f, unit->Owner, position);
      if (refinery == EntityRef.None) {
        // 정제소가 생길 때까지 자금을 든 채 기다린다.
        MovementSystem.Halt(f, entity);
        return;
      }
      var spec = f.FindAsset(f.Unsafe.GetPointer<Building>(refinery)->Spec);
      var target = f.Unsafe.GetPointer<Transform3D>(refinery)->Position;
      if (MoveInto(f, entity, worker, position, target, spec.FootprintRadius + rules.WorkRange)) {
        FactoryWarsUtil.Player(f, unit->Owner)->Funds += worker->Carried;
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
      if (MoveInto(f, entity, worker, position, target, spec.FootprintRadius + rules.WorkRange) == false) {
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
      if (MoveInto(f, entity, worker, position, target, spec.FootprintRadius + rules.WorkRange) == false) {
        return;
      }

      var heal = FPMath.Min(rules.RepairPerSecond * f.DeltaTime, spec.MaxHealth - building->Health);
      var cost = heal * spec.Cost / spec.MaxHealth * rules.RepairCostRatio;
      var state = FactoryWarsUtil.Player(f, unit->Owner);
      if (state->Funds < cost) {
        SetTask(worker, WorkerTask.Idle);
        return;
      }
      state->Funds -= cost;
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
