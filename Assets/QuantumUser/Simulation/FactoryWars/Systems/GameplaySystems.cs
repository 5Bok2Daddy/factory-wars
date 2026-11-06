namespace Quantum.FactoryWars {
  using Photon.Deterministic;

  /// <summary>건설·생산 대기열, 전력·인구 집계.</summary>
  public unsafe class ProductionSystem : SystemMainThread {
    public static EntityRef CreateBuilding(Frame f, PlayerRef owner, AssetRef<BuildingSpec> specRef, FPVector3 position) {
      var spec = f.FindAsset(specRef);
      var entity = f.Create();
      f.Add(entity, Transform3D.Create(position));
      f.Add(entity, new Building {
        Owner = owner,
        Spec = specRef,
        Health = spec.MaxHealth,
        ConstructionRemaining = spec.BuildTimeSeconds,
      });
      return entity;
    }

    public static EntityRef SpawnUnit(Frame f, PlayerRef owner, AssetRef<UnitSpec> specRef, FPVector3 origin, int index, Building* source) {
      var rules = FactoryWarsUtil.Rules(f);
      var spec = f.FindAsset(specRef);

      // 건물 주변에 순서대로 펼쳐 놓는다.
      var angle = FP.PiTimes2 * (index % 8) / 8;
      var position = origin + new FPVector3(FPMath.Cos(angle), FP._0, FPMath.Sin(angle)) * rules.SpawnOffset;
      if (spec.IsAir) {
        position.Y = rules.AirAltitude;
      }

      var entity = f.Create();
      f.Add(entity, Transform3D.Create(position));
      var unit = new Unit {
        Owner = owner,
        Spec = specRef,
        Health = spec.MaxHealth,
        Order = UnitOrder.Stop,
      };
      if (source != null) {
        var slots = FPMath.Min(spec.ModuleSlots, unit.Modules.Length);
        for (var i = 0; i < slots; i++) {
          *unit.Modules.GetPointer(i) = source->Preset[i];
        }
      }
      f.Add(entity, unit);

      if (spec.IsAir == false) {
        var config = f.FindAsset(f.SimulationConfig.Navigation.DefaultNavMeshAgent);
        f.Set(entity, NavMeshPathfinder.Create(f, entity, config));
        f.Add(entity, new NavMeshSteeringAgent());
        f.Unsafe.GetPointer<NavMeshSteeringAgent>(entity)->MaxSpeed = spec.MoveSpeed;
      }
      return entity;
    }

    public static void TryBuild(Frame f, PlayerRef player, AssetRef<BuildingSpec> specRef, FPVector3 position) {
      var state = FactoryWarsUtil.Player(f, player);
      var spec = f.FindAsset(specRef);
      if (state == null || spec == null || Contains(f.FindAsset(state->Faction).Buildings, specRef) == false || state->Funds < spec.Cost) {
        return;
      }
      // TODO: 공병 배치, 건설 가능 위치 검사.
      state->Funds -= spec.Cost;
      CreateBuilding(f, player, specRef, position);
    }

    public static void Enqueue(Frame f, PlayerRef player, EntityRef entity, AssetRef<UnitSpec> unitRef, int count) {
      var state = FactoryWarsUtil.Player(f, player);
      var unit = f.FindAsset(unitRef);
      if (state == null || unit == null || f.Unsafe.TryGetPointer<Building>(entity, out var building) == false) {
        return;
      }
      var spec = f.FindAsset(building->Spec);
      if (building->Owner != player || building->ConstructionRemaining > FP._0 || Contains(spec.Producible, unitRef) == false) {
        return;
      }

      var cost = unit.Cost + PresetCost(f, building, unit);
      for (var i = 0; i < count && building->QueueCount < building->Queue.Length && state->Funds >= cost; i++) {
        state->Funds -= cost;
        var order = building->Queue.GetPointer(building->QueueCount++);
        order->Unit = unitRef;
        order->PaidCost = cost;
      }
    }

    public static void CancelOrders(Frame f, PlayerRef player, EntityRef entity, AssetRef<UnitSpec> unitRef, int count) {
      var state = FactoryWarsUtil.Player(f, player);
      if (state == null || f.Unsafe.TryGetPointer<Building>(entity, out var building) == false || building->Owner != player) {
        return;
      }
      for (var i = building->QueueCount - 1; i >= 0 && count > 0; i--) {
        if (building->Queue[i].Unit == unitRef) {
          state->Funds += building->Queue[i].PaidCost;
          RemoveOrder(building, i);
          count--;
        }
      }
    }

    static void RemoveOrder(Building* building, int index) {
      if (index == 0) {
        building->QueueProgress = FP._0;
      }
      for (var i = index; i < building->QueueCount - 1; i++) {
        *building->Queue.GetPointer(i) = building->Queue[i + 1];
      }
      building->QueueCount--;
      *building->Queue.GetPointer(building->QueueCount) = default;
    }

    static FP PresetCost(Frame f, Building* building, UnitSpec unit) {
      var total = FP._0;
      var slots = FPMath.Min(unit.ModuleSlots, building->Preset.Length);
      for (var i = 0; i < slots; i++) {
        var module = f.FindAsset(building->Preset[i]);
        if (module != null) {
          total += module.Cost;
        }
      }
      return total;
    }

    static bool Contains<T>(AssetRef<T>[] list, AssetRef<T> item) where T : AssetObject {
      if (list == null) {
        return false;
      }
      for (var i = 0; i < list.Length; i++) {
        if (list[i] == item) {
          return true;
        }
      }
      return false;
    }

    public override void Update(Frame f) {
      if (f.Global->GameEnded) {
        return;
      }
      UpdateTotals(f);

      var rules = FactoryWarsUtil.Rules(f);
      var buildings = f.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out _, out var transform, out var building)) {
        if (building->ConstructionRemaining > FP._0) {
          building->ConstructionRemaining = FPMath.Max(FP._0, building->ConstructionRemaining - f.DeltaTime);
          continue;
        }
        if (building->QueueCount == 0) {
          continue;
        }

        var state = FactoryWarsUtil.Player(f, building->Owner);
        var faction = f.FindAsset(state->Faction);
        var unit = f.FindAsset(building->Queue[0].Unit);
        var speed = state->PowerConsumed > state->PowerProduced ? rules.LowPowerProductionMultiplier : FP._1;
        var buildTime = unit.BuildTimeSeconds;
        if (unit.Size == UnitSize.Small) {
          buildTime *= faction.SmallBuildTimeMultiplier;
        }

        building->QueueProgress += f.DeltaTime * speed;
        if (building->QueueProgress < buildTime) {
          continue;
        }

        // 인구가 차면 완성 상태로 대기한다.
        building->QueueProgress = buildTime;
        var population = unit.Population * unit.SpawnCount;
        if (state->PopulationUsed + population > state->PopulationCap) {
          continue;
        }
        for (var i = 0; i < unit.SpawnCount; i++) {
          SpawnUnit(f, building->Owner, building->Queue[0].Unit, transform->Position, i, building);
        }
        state->PopulationUsed += population;
        RemoveOrder(building, 0);
      }
    }

    static void UpdateTotals(Frame f) {
      for (var i = 0; i < f.PlayerCount; i++) {
        var state = f.Global->Players.GetPointer(i);
        state->PowerProduced = 0;
        state->PowerConsumed = 0;
        state->PopulationUsed = FP._0;
        state->PopulationCap = FP._0;
      }

      var buildings = f.Filter<Building>();
      while (buildings.NextUnsafe(out _, out var building)) {
        if (building->ConstructionRemaining > FP._0) {
          continue;
        }
        var spec = f.FindAsset(building->Spec);
        var state = FactoryWarsUtil.Player(f, building->Owner);
        state->PowerProduced += spec.PowerProduced;
        state->PowerConsumed += spec.PowerConsumed;
        state->PopulationCap += spec.PopulationCapBonus;
      }

      for (var i = 0; i < f.PlayerCount; i++) {
        var state = f.Global->Players.GetPointer(i);
        if (state->Active) {
          state->PopulationCap *= f.FindAsset(state->Faction).PopulationCapMultiplier;
        }
      }

      var units = f.Filter<Unit>();
      while (units.NextUnsafe(out _, out var unit)) {
        FactoryWarsUtil.Player(f, unit->Owner)->PopulationUsed += f.FindAsset(unit->Spec).Population;
      }
    }
  }

  /// <summary>조건형 자동 발동 모듈. 1차는 관통탄·반응장갑만 구현.</summary>
  public unsafe class ModuleSystem : SystemMainThread {
    public override void Update(Frame f) {
      var units = f.Filter<Unit>();
      while (units.NextUnsafe(out _, out var unit)) {
        if (unit->ReactiveArmorCooldown > FP._0) {
          unit->ReactiveArmorCooldown = FPMath.Max(FP._0, unit->ReactiveArmorCooldown - f.DeltaTime);
        }
      }
      // TODO: 나머지 모듈 효과.
    }

    /// <summary>관통탄: 중형 이상 대상에게 피해 증가.</summary>
    public static FP ApplyOutgoing(Frame f, Unit* attacker, UnitSpec targetSpec, FP damage) {
      if (attacker == null || targetSpec == null || targetSpec.Size < UnitSize.Medium) {
        return damage;
      }
      for (var i = 0; i < attacker->Modules.Length; i++) {
        var module = f.FindAsset(attacker->Modules[i]);
        if (module != null && module.Effect == ModuleEffect.PiercingRounds) {
          damage *= module.DamageMultiplier;
        }
      }
      return damage;
    }

    /// <summary>반응장갑: 쿨다운이 끝나 있으면 이번 피해를 줄이고 쿨다운에 들어간다.</summary>
    public static FP ApplyIncoming(Frame f, Unit* target, FP damage) {
      if (target->ReactiveArmorCooldown > FP._0) {
        return damage;
      }
      for (var i = 0; i < target->Modules.Length; i++) {
        var module = f.FindAsset(target->Modules[i]);
        if (module != null && module.Effect == ModuleEffect.ReactiveArmor) {
          target->ReactiveArmorCooldown = module.CooldownSeconds;
          return damage * module.DamageMultiplier;
        }
      }
      return damage;
    }
  }

  /// <summary>자동 조준과 공격. 관계표가 Enemy인 대상만 공격한다.</summary>
  public unsafe class CombatSystem : SystemMainThread {
    public static bool TryGetOwner(Frame f, EntityRef entity, out PlayerRef owner) {
      if (f.Unsafe.TryGetPointer<Unit>(entity, out var unit)) {
        owner = unit->Owner;
        return true;
      }
      if (f.Unsafe.TryGetPointer<Building>(entity, out var building)) {
        owner = building->Owner;
        return true;
      }
      owner = default;
      return false;
    }

    static bool CanHit(Frame f, AttackType attack, EntityRef target) {
      var isAir = f.Unsafe.TryGetPointer<Unit>(target, out var unit) && f.FindAsset(unit->Spec).IsAir;
      return attack == AttackType.AntiAir ? isAir : isAir == false;
    }

    static bool IsValidTarget(Frame f, PlayerRef owner, AttackType attack, EntityRef target) {
      return TryGetOwner(f, target, out var targetOwner)
        && DiplomacySystem.CanAttack(f, owner, targetOwner)
        && CanHit(f, attack, target);
    }

    static EntityRef FindTarget(Frame f, PlayerRef owner, AttackType attack, FPVector3 position, FP minRange, FP range) {
      var best = EntityRef.None;
      var bestDistance = range * range;
      var minDistance = minRange * minRange;

      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out var entity, out var transform, out _)) {
        var distance = (transform->Position - position).SqrMagnitude;
        if (distance <= bestDistance && distance >= minDistance && IsValidTarget(f, owner, attack, entity)) {
          best = entity;
          bestDistance = distance;
        }
      }
      if (best != EntityRef.None) {
        return best;
      }

      var buildings = f.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out var entity, out var transform, out _)) {
        var distance = (transform->Position - position).SqrMagnitude;
        if (distance <= bestDistance && distance >= minDistance && IsValidTarget(f, owner, attack, entity)) {
          best = entity;
          bestDistance = distance;
        }
      }
      return best;
    }

    public static FP EffectiveRange(Frame f, Unit* unit, UnitSpec spec) {
      var faction = f.FindAsset(FactoryWarsUtil.Player(f, unit->Owner)->Faction);
      return spec.IsRanged ? spec.Range * faction.RangedRangeMultiplier : spec.Range;
    }

    static void Damage(Frame f, EntityRef target, Unit* attacker, FP amount) {
      if (f.Unsafe.TryGetPointer<Unit>(target, out var unit)) {
        if (unit->Health <= FP._0) {
          return;
        }
        var spec = f.FindAsset(unit->Spec);
        amount = ModuleSystem.ApplyOutgoing(f, attacker, spec, amount);
        unit->Health -= ModuleSystem.ApplyIncoming(f, unit, amount);
        if (unit->Health <= FP._0) {
          f.Events.UnitDestroyed(target, unit->Owner, unit->Spec);
          f.Destroy(target);
        }
      } else if (f.Unsafe.TryGetPointer<Building>(target, out var building)) {
        if (building->Health <= FP._0) {
          return;
        }
        building->Health -= amount;
        if (building->Health <= FP._0) {
          f.Destroy(target);
        }
      }
    }

    static void DamageArea(Frame f, PlayerRef owner, AttackType attack, Unit* attacker, FPVector3 center, FP radius, FP amount) {
      var radiusSqr = radius * radius;
      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out var entity, out var transform, out _)) {
        if ((transform->Position - center).SqrMagnitude <= radiusSqr && IsValidTarget(f, owner, attack, entity)) {
          Damage(f, entity, attacker, amount);
        }
      }
    }

    public override void Update(Frame f) {
      if (f.Global->GameEnded) {
        return;
      }

      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out var entity, out var transform, out var unit)) {
        var spec = f.FindAsset(unit->Spec);
        if (unit->Health <= FP._0 || spec.Attack == AttackType.None) {
          continue;
        }
        unit->AttackCooldown = FPMath.Max(FP._0, unit->AttackCooldown - f.DeltaTime);

        var range = EffectiveRange(f, unit, spec);
        var position = transform->Position;

        if (unit->TargetEntity != EntityRef.None && IsValidTarget(f, unit->Owner, spec.Attack, unit->TargetEntity) == false) {
          unit->TargetEntity = EntityRef.None;
          if (unit->Order == UnitOrder.AttackTarget) {
            unit->Order = UnitOrder.Stop;
          }
        }

        // 이동 명령 중에는 자동 조준하지 않는다.
        if (unit->TargetEntity == EntityRef.None && unit->Order != UnitOrder.Move) {
          unit->TargetEntity = FindTarget(f, unit->Owner, spec.Attack, position, spec.MinRange, range);
        }
        if (unit->TargetEntity == EntityRef.None || unit->AttackCooldown > FP._0) {
          continue;
        }

        var targetPosition = f.Unsafe.GetPointer<Transform3D>(unit->TargetEntity)->Position;
        var distance = (targetPosition - position).SqrMagnitude;
        if (distance > range * range || distance < spec.MinRange * spec.MinRange) {
          // 정지 상태에서 사거리를 벗어난 대상은 쫓지 않는다.
          if (unit->Order == UnitOrder.Stop) {
            unit->TargetEntity = EntityRef.None;
          }
          continue;
        }

        unit->AttackCooldown = spec.AttackInterval;
        if (spec.SplashRadius > FP._0) {
          DamageArea(f, unit->Owner, spec.Attack, unit, targetPosition, spec.SplashRadius, spec.Damage);
        } else {
          Damage(f, unit->TargetEntity, unit, spec.Damage);
        }
      }

      UpdateTowers(f);
      // TODO: 유닛 특수 기능(UnitSpec.Ability), 공격 유형별 상성.
    }

    void UpdateTowers(Frame f) {
      var rules = FactoryWarsUtil.Rules(f);
      var buildings = f.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out var entity, out var transform, out var building)) {
        var spec = f.FindAsset(building->Spec);
        if (spec.Attack == AttackType.None || building->Health <= FP._0 || building->ConstructionRemaining > FP._0) {
          continue;
        }
        var state = FactoryWarsUtil.Player(f, building->Owner);
        var speed = state->PowerConsumed > state->PowerProduced ? rules.LowPowerTowerMultiplier : FP._1;
        building->AttackCooldown = FPMath.Max(FP._0, building->AttackCooldown - f.DeltaTime * speed);
        if (building->AttackCooldown > FP._0) {
          continue;
        }

        var target = FindTarget(f, building->Owner, spec.Attack, transform->Position, FP._0, spec.Range);
        if (target != EntityRef.None) {
          building->TargetEntity = target;
          building->AttackCooldown = spec.AttackInterval;
          Damage(f, target, null, spec.Damage);
        }
      }
    }
  }

  /// <summary>지상은 Quantum NavMesh, 공중은 직선 이동.</summary>
  public unsafe class MovementSystem : SystemMainThread, ISignalOnNavMeshWaypointReached, ISignalOnNavMeshSearchFailed {
    public static void Order(Frame f, EntityRef entity, Unit* unit, UnitOrder order, FPVector3 target) {
      unit->Order = order;
      unit->TargetPosition = target;
      unit->TargetEntity = EntityRef.None;
      SetDestination(f, entity, target);
    }

    static void SetDestination(Frame f, EntityRef entity, FPVector3 target) {
      if (f.Unsafe.TryGetPointer<NavMeshPathfinder>(entity, out var pathfinder)) {
        var navMesh = f.Map.GetNavMesh(FactoryWarsUtil.Rules(f).NavMeshName);
        if (navMesh != null) {
          pathfinder->SetTarget(f, target, navMesh);
        }
      }
    }

    static void Halt(Frame f, EntityRef entity) {
      if (f.Unsafe.TryGetPointer<NavMeshPathfinder>(entity, out var pathfinder) && pathfinder->IsActive) {
        pathfinder->Stop(f, entity, true);
      }
    }

    public void OnNavMeshWaypointReached(Frame f, EntityRef entity, FPVector3 waypoint, Navigation.WaypointFlag waypointFlags, ref bool resetAgent) {
      if ((waypointFlags & Navigation.WaypointFlag.Target) != 0 && f.Unsafe.TryGetPointer<Unit>(entity, out var unit)) {
        if (unit->Order == UnitOrder.Move || unit->Order == UnitOrder.AttackMove) {
          unit->Order = UnitOrder.Stop;
        }
      }
    }

    public void OnNavMeshSearchFailed(Frame f, EntityRef entity, ref bool resetAgent) {
      if (f.Unsafe.TryGetPointer<Unit>(entity, out var unit) && unit->Order != UnitOrder.AttackTarget) {
        unit->Order = UnitOrder.Stop;
      }
    }

    public override void Update(Frame f) {
      if (f.Global->GameEnded) {
        return;
      }
      var repathFrames = FactoryWarsUtil.SecondsToFrames(f, FP._1);

      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out var entity, out var transform, out var unit)) {
        var spec = f.FindAsset(unit->Spec);
        var hasDestination = unit->Order == UnitOrder.Move || unit->Order == UnitOrder.AttackMove;
        var destination = unit->TargetPosition;

        if (unit->TargetEntity != EntityRef.None && unit->Order != UnitOrder.Move
            && f.Unsafe.TryGetPointer<Transform3D>(unit->TargetEntity, out var targetTransform)) {
          var range = CombatSystem.EffectiveRange(f, unit, spec);
          if ((targetTransform->Position - transform->Position).SqrMagnitude <= range * range) {
            Halt(f, entity);
            continue;
          }
          if (unit->Order == UnitOrder.Stop) {
            continue;
          }
          // 쫓는 경로는 1초마다 갱신한다.
          hasDestination = true;
          destination = targetTransform->Position;
          if (spec.IsAir == false && f.Number % repathFrames == 0) {
            SetDestination(f, entity, destination);
          }
        } else if (unit->Order == UnitOrder.AttackMove && spec.IsAir == false
            && f.Unsafe.TryGetPointer<NavMeshPathfinder>(entity, out var pathfinder) && pathfinder->IsActive == false) {
          // 교전이 끝나면 원래 목적지로 재출발한다.
          SetDestination(f, entity, unit->TargetPosition);
        }

        if (spec.IsAir && hasDestination) {
          MoveAir(f, transform, unit, spec, destination);
        }
      }
    }

    static void MoveAir(Frame f, Transform3D* transform, Unit* unit, UnitSpec spec, FPVector3 destination) {
      destination.Y = transform->Position.Y;
      var delta = destination - transform->Position;
      var step = spec.MoveSpeed * f.DeltaTime;
      if (delta.SqrMagnitude <= step * step) {
        transform->Position = destination;
        if (unit->Order != UnitOrder.AttackTarget) {
          unit->Order = UnitOrder.Stop;
        }
        return;
      }
      transform->Position += delta.Normalized * step;
    }
  }
}
