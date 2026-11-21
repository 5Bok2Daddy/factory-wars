namespace Quantum.FactoryWars {
  using Photon.Deterministic;

  public unsafe class MlrsSystem : SystemMainThread {
    public override void Update(Frame f) {
      var rockets = f.Filter<MlrsRocket>();
      while (rockets.NextUnsafe(out var entity, out var rocket)) {
        if (f.Number < rocket->ImpactFrame) continue;
        var impact = *rocket;
        f.Destroy(entity);
        if (!f.Global->GameEnded) {
          CombatSystem.ApplyMlrsImpact(f, impact);
          f.Events.MlrsImpact(impact.Target, impact.BlastRadius);
        }
      }

      if (f.Global->GameEnded) return;
      var batteries = f.Filter<Transform3D, Building, MlrsMagazine>();
      while (batteries.NextUnsafe(out var entity, out var transform, out var building, out var magazine)) {
        TickBattery(f, entity, transform->Position, building, magazine);
      }
    }

    static void TickBattery(Frame f, EntityRef entity, FPVector3 position, Building* building, MlrsMagazine* magazine) {
      var spec = f.FindAsset(building->Spec);
      var player = FactoryWarsUtil.Player(f, building->Owner);
      if (building->Health <= FP._0 || building->ConstructionRemaining > FP._0 || building->EmpUntilFrame > f.Number
          || player->Outage || player->Defeated || spec.RocketCapacity <= 0 || spec.RocketSpeed <= FP._0
          || spec.RocketMuzzles == null || spec.RocketMuzzles.Length == 0) {
        magazine->Status = MlrsStatus.Disabled;
        building->TargetEntity = EntityRef.None;
        return;
      }

      if (magazine->ReloadRemaining > 0) {
        magazine->Status = MlrsStatus.Reloading;
        if (--magazine->ReloadRemaining > 0) return;
        magazine->Ammo = spec.RocketCapacity;
        magazine->SearchFrame = f.Number;
      }

      var owner = building->Owner;
      if (FactoryWarsUtil.InTruce(f)) {
        building->TargetEntity = EntityRef.None;
        magazine->Status = MlrsStatus.Idle;
        return;
      }

      var target = building->TargetEntity;
      if (!CanTrack(f, owner, target, spec, position, out var aimPoint)) {
        building->TargetEntity = EntityRef.None;
        magazine->Status = MlrsStatus.Searching;
        if (f.Number < magazine->SearchFrame) return;
        magazine->SearchFrame = f.Number + Frames(f, spec.RocketSearchSeconds);
        target = FindTarget(f, owner, spec, position);
        if (target == EntityRef.None) return;
        building->TargetEntity = target;
        aimPoint = f.Get<Transform3D>(target).Position;
        magazine->AimFrame = f.Number + Frames(f, spec.RocketAimSeconds);
      }

      var delta = aimPoint - position;
      delta.Y = FP._0;
      magazine->AimDirection = delta.Normalized;
      if (f.Number < magazine->AimFrame) {
        magazine->Status = MlrsStatus.Aiming;
        return;
      }

      magazine->Status = MlrsStatus.Firing;
      if (f.Number < magazine->FireFrame) return;
      var muzzle = (spec.RocketCapacity - magazine->Ammo) % spec.RocketMuzzles.Length;
      Launch(f, entity, owner, position, aimPoint, magazine->AimDirection, spec, muzzle);
      magazine->FireFrame = f.Number + Frames(f, spec.AttackInterval);
      if (--magazine->Ammo > 0) return;

      var reload = spec.RocketReloadSeconds;
      if (TechSystem.Tier(f, owner) >= TechSystem.MaxTier) reload *= spec.RocketTierThreeReloadMultiplier;
      magazine->ReloadTotal = magazine->ReloadRemaining = Frames(f, reload);
      magazine->Status = MlrsStatus.Reloading;
      building->TargetEntity = EntityRef.None;
    }

    static int Frames(Frame f, FP seconds) => System.Math.Max(1, FactoryWarsUtil.SecondsToFrames(f, seconds));

    static bool CanTrack(Frame f, PlayerRef owner, EntityRef target, BuildingSpec spec, FPVector3 origin, out FPVector3 position) {
      position = default;
      if (!f.Unsafe.TryGetPointer<Unit>(target, out var unit) || !f.Unsafe.TryGetPointer<Transform3D>(target, out var transform)
          || unit->Health <= FP._0 || unit->Garrison != EntityRef.None || unit->Transport != EntityRef.None
          || !DiplomacySystem.CanAttack(f, owner, unit->Owner)) return false;
      position = transform->Position;
      var distance = FactoryWarsUtil.FlatDistanceSqr(origin, position);
      if (distance < spec.MinRange * spec.MinRange || distance > spec.Range * spec.Range) return false;
      var targetSpec = f.FindAsset(unit->Spec);
      return !targetSpec.IsAir && !targetSpec.IsNaval && FactoryWarsUtil.IsVisibleTo(f, owner, target);
    }

    static EntityRef FindTarget(Frame f, PlayerRef owner, BuildingSpec spec, FPVector3 position) {
      var best = EntityRef.None;
      var bestPriority = -1;
      var bestDistance = FP.MaxValue;
      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out var entity, out _, out var unit)) {
        if (!CanTrack(f, owner, entity, spec, position, out var candidate)) continue;
        var unitSpec = f.FindAsset(unit->Spec);
        var priority = unitSpec.IsVehicle || unitSpec.Size >= UnitSize.Medium ? 1 : 0;
        if (unitSpec.Attack != AttackType.None && f.Unsafe.TryGetPointer<Building>(unit->TargetEntity, out var threatened)
            && threatened->Health > FP._0 && (threatened->Owner == owner || DiplomacySystem.GetRelation(f, owner, threatened->Owner) == Relation.Allied)) {
          priority = 2;
        }
        var distance = FactoryWarsUtil.FlatDistanceSqr(position, candidate);
        if (priority < bestPriority || (priority == bestPriority && (distance > bestDistance
            || (distance == bestDistance && entity.Index >= best.Index)))) continue;
        bestPriority = priority;
        bestDistance = distance;
        best = entity;
      }
      return best;
    }

    static void Launch(Frame f, EntityRef battery, PlayerRef owner, FPVector3 position, FPVector3 target, FPVector3 heading,
        BuildingSpec spec, int muzzleIndex) {
      var muzzle = spec.RocketMuzzles[muzzleIndex];
      var right = new FPVector3(heading.Z, FP._0, -heading.X);
      var origin = position + right * muzzle.X + FPVector3.Up * muzzle.Y + heading * muzzle.Z;
      var flightFrames = Frames(f, (target - origin).Magnitude / spec.RocketSpeed);
      var rocket = f.Create();
      f.Add(rocket, new MlrsRocket {
        Owner = owner, Origin = origin, Target = target, Arc = spec.RocketArc, LaunchFrame = f.Number,
        ImpactFrame = f.Number + flightFrames, BlastRadius = spec.SplashRadius, Damage = spec.Damage, EdgeDamage = spec.RocketEdgeDamage,
      });
      var pitch = spec.RocketElevation * FP.Deg2Rad;
      var direction = heading * FPMath.Cos(pitch) + FPVector3.Up * FPMath.Sin(pitch);
      f.Events.MlrsLaunched(battery, owner, origin, direction);
    }

    public static FPVector3 PositionAt(in MlrsRocket rocket, int frame) {
      var progress = FPMath.Clamp01((FP)(frame - rocket.LaunchFrame) / (rocket.ImpactFrame - rocket.LaunchFrame));
      return rocket.Origin + (rocket.Target - rocket.Origin) * progress
        + FPVector3.Up * (rocket.Arc * FP._4 * progress * (FP._1 - progress));
    }
  }
}
