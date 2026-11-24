namespace Quantum.FactoryWars {
  using Photon.Deterministic;

  /// <summary>
  /// Task 40: PMC drone operator infantry that launches autonomous strike drones from UnitSpec data.
  /// Deterministic FP-only simulation: launch on target in range, leash return, battery expiry, and death cascade.
  /// </summary>
  public unsafe class OperatorDroneSystem : SystemMainThread {
    public override void Update(Frame f) {
      AttachOperators(f);
      UpdateOperators(f);
      UpdateDrones(f);
    }

    static void AttachOperators(Frame f) {
      var units = f.Filter<Unit>();
      while (units.NextUnsafe(out var entity, out var unit)) {
        if (unit->Health <= FP._0 || f.Has<DroneOperator>(entity)) continue;
        var spec = f.FindAsset(unit->Spec);
        if (spec != null && spec.OperatorDrone.IsValid && spec.OperatorMaxDrones > 0) {
          f.Add(entity, new DroneOperator { LaunchCooldown = FP._0, Target = EntityRef.None });
        }
      }
    }

    static void UpdateOperators(Frame f) {
      var operators = f.Filter<Transform3D, Unit, DroneOperator>();
      var scanFrames = FactoryWarsUtil.SecondsToFrames(f, FP._0_50);
      if (scanFrames < 1) scanFrames = 1;
      while (operators.NextUnsafe(out var entity, out var transform, out var unit, out var op)) {
        if (unit->Health <= FP._0) continue;
        var spec = f.FindAsset(unit->Spec);
        if (spec == null || spec.OperatorDrone.IsValid == false || spec.OperatorMaxDrones <= 0) continue;
        var droneSpec = f.FindAsset(spec.OperatorDrone);
        if (droneSpec == null) continue;

        var range = spec.OperatorLaunchRange > FP._0 ? spec.OperatorLaunchRange : (FP)20;
        var pos = transform->Position;

        if (op->Target != EntityRef.None && CombatSystem.CanEngage(f, unit->Owner, droneSpec, op->Target, pos, range) == false) {
          op->Target = EntityRef.None;
        }
        if (op->Target == EntityRef.None && (entity.Index + f.Number) % scanFrames == 0) {
          op->Target = CombatSystem.FindTargetFor(f, unit->Owner, droneSpec, pos, range);
        }

        op->LaunchCooldown = FPMath.Max(FP._0, op->LaunchCooldown - f.DeltaTime);
        if (op->Target == EntityRef.None || op->LaunchCooldown > FP._0) continue;

        var flying = 0;
        var existing = f.Filter<OperatorDrone>();
        while (existing.NextUnsafe(out _, out var link)) {
          if (link->Operator == entity) flying++;
        }
        if (flying >= spec.OperatorMaxDrones) continue;

        var angle = FP.PiTimes2 * (flying % 8) / 8;
        var spawnPos = pos + new FPVector3(FPMath.Cos(angle), FP._0, FPMath.Sin(angle));
        var drone = ProductionSystem.SpawnUnitAt(f, unit->Owner, spec.OperatorDrone, spawnPos, null);
        var battery = spec.OperatorBatterySeconds > FP._0 ? spec.OperatorBatterySeconds : (FP)25;
        f.Add(drone, new OperatorDrone { Operator = entity, Battery = battery });
        var droneUnit = f.Unsafe.GetPointer<Unit>(drone);
        droneUnit->ReservedUntilFrame = int.MaxValue;
        droneUnit->TargetEntity = op->Target;
        droneUnit->Order = UnitOrder.AttackTarget;
        op->LaunchCooldown = spec.OperatorRelaunchSeconds > FP._0 ? spec.OperatorRelaunchSeconds : (FP)8;
      }
    }

    static void UpdateDrones(Frame f) {
      var drones = f.Filter<Transform3D, Unit, OperatorDrone>();
      while (drones.NextUnsafe(out var entity, out var transform, out var unit, out var drone)) {
        if (unit->Health <= FP._0) continue;

        // Operator dead or gone -> immediate death cascade.
        if (f.Exists(drone->Operator) == false
            || f.Unsafe.TryGetPointer<Unit>(drone->Operator, out var opUnit) == false
            || opUnit->Health <= FP._0
            || f.Unsafe.TryGetPointer<Transform3D>(drone->Operator, out var opTransform) == false
            || f.Unsafe.TryGetPointer<DroneOperator>(drone->Operator, out var op) == false) {
          CrashDrone(f, entity, transform->Position, unit);
          continue;
        }

        // Battery drain -> crash when depleted.
        drone->Battery -= f.DeltaTime;
        if (drone->Battery <= FP._0) {
          CrashDrone(f, entity, transform->Position, unit);
          continue;
        }

        var opSpec = f.FindAsset(opUnit->Spec);
        var leash = opSpec != null && opSpec.OperatorLeash > FP._0 ? opSpec.OperatorLeash : (FP)26;
        var opPos = opTransform->Position;
        var awaySqr = FactoryWarsUtil.FlatDistanceSqr(transform->Position, opPos);

        if (awaySqr > leash * leash || op->Target == EntityRef.None) {
          unit->TargetEntity = EntityRef.None;
          unit->Order = UnitOrder.Move;
          unit->TargetPosition = opPos;
        } else if (unit->TargetEntity != op->Target || unit->Order != UnitOrder.AttackTarget) {
          unit->TargetEntity = op->Target;
          unit->Order = UnitOrder.AttackTarget;
        }
      }
    }

    static void CrashDrone(Frame f, EntityRef entity, FPVector3 position, Unit* unit) {
      unit->Health = FP._0;
      f.Events.UnitDestroyed(entity, unit->Owner, unit->Spec);
      f.Events.Wrecked(position, (int)UnitSize.Small, FP._0);
      f.Destroy(entity);
    }
  }
}
