namespace Quantum.FactoryWars {
  using Photon.Deterministic;

  /// <summary>건설·생산 대기열, 인구 집계.</summary>
  public unsafe class ProductionSystem : SystemMainThread {
    public static EntityRef CreateBuilding(Frame f, PlayerRef owner, AssetRef<BuildingSpec> specRef, FPVector3 position) {
      var spec = f.FindAsset(specRef);
      var entity = f.Create();
      f.Add(entity, Transform3D.Create(position));
      f.Add(entity, new Building {
        Owner = owner,
        Spec = specRef,
        Health = spec.BuildTimeSeconds > FP._0 ? spec.MaxHealth * FactoryWarsUtil.Rules(f).FoundationHealthRatio : spec.MaxHealth,
        ConstructionRemaining = spec.BuildTimeSeconds,
      });
      return entity;
    }

    public static EntityRef CreateFinishedBuilding(Frame f, PlayerRef owner, AssetRef<BuildingSpec> specRef, FPVector3 position) {
      var entity = CreateBuilding(f, owner, specRef, position);
      var building = f.Unsafe.GetPointer<Building>(entity);
      building->ConstructionRemaining = FP._0;
      building->Health = f.FindAsset(specRef).MaxHealth;
      return entity;
    }

    public static EntityRef SpawnUnit(Frame f, PlayerRef owner, AssetRef<UnitSpec> specRef, FPVector3 origin, int index, Building* source) {
      // 건물 주변에 순서대로 펼쳐 놓는다.
      var angle = FP.PiTimes2 * (index % 8) / 8;
      var position = origin + new FPVector3(FPMath.Cos(angle), FP._0, FPMath.Sin(angle)) * FactoryWarsUtil.Rules(f).SpawnOffset;
      return SpawnUnitAt(f, owner, specRef, position, source);
    }

    public static EntityRef SpawnUnitAt(Frame f, PlayerRef owner, AssetRef<UnitSpec> specRef, FPVector3 position, Building* source) {
      var rules = FactoryWarsUtil.Rules(f);
      var spec = f.FindAsset(specRef);
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
        f.Add(entity, new NavMeshAvoidanceAgent());
        f.Unsafe.GetPointer<NavMeshSteeringAgent>(entity)->MaxSpeed = spec.MoveSpeed;
      }
      if (spec.Ability == SpecialAbility.EngineerWork) {
        f.Add(entity, new Worker());
      }
      return entity;
    }

    public static void TryBuild(Frame f, PlayerRef player, EntityRef engineer, AssetRef<BuildingSpec> specRef, FPVector3 position) {
      var state = FactoryWarsUtil.Player(f, player);
      var spec = f.FindAsset(specRef);
      if (state == null || spec == null || Contains(f.FindAsset(state->Faction).Buildings, specRef) == false) {
        f.Events.ActionRejected(player, ActionKind.Build, RejectReason.Invalid);
        return;
      }
      position.Y = FP._0;
      if (CanPlace(f, spec, position) == false) {
        f.Events.ActionRejected(player, ActionKind.Build, RejectReason.BlockedLocation);
        return;
      }
      if (IsUnlocked(f, player, spec) == false) {
        f.Events.ActionRejected(player, ActionKind.Build, RejectReason.Locked);
        return;
      }
      if (state->Stock.Covers(spec.Cost) == false) {
        f.Events.ActionRejected(player, ActionKind.Build, RejectReason.InsufficientFunds);
        return;
      }
      state->Stock -= spec.Cost;
      WorkerSystem.Assign(f, engineer, WorkerTask.Build, CreateBuilding(f, player, specRef, position));
    }

    /// <summary>선행 건물을 모두 완공했으면 true.</summary>
    public static bool IsUnlocked(Frame f, PlayerRef player, BuildingSpec spec) {
      for (var i = 0; spec.Prerequisites != null && i < spec.Prerequisites.Length; i++) {
        if (HasFinished(f, player, spec.Prerequisites[i]) == false) {
          return false;
        }
      }
      return true;
    }

    static bool HasFinished(Frame f, PlayerRef player, AssetRef<BuildingSpec> specRef) {
      var buildings = f.Filter<Building>();
      while (buildings.NextUnsafe(out _, out var building)) {
        if (building->Owner == player && building->Spec == specRef && building->ConstructionRemaining <= FP._0) {
          return true;
        }
      }
      return false;
    }

    /// <summary>맵 안, 걸어갈 수 있는 땅, 다른 건물·자원 노드와 겹치지 않는 곳에만 지을 수 있다.</summary>
    public static bool CanPlace(Frame f, BuildingSpec spec, FPVector3 position) {
      var map = FactoryWarsUtil.MapInfo(f);
      var rules = FactoryWarsUtil.Rules(f);
      var radius = spec.FootprintRadius;
      if (position.X - radius < map.BoundsMin.X || position.X + radius > map.BoundsMax.X
          || position.Z - radius < map.BoundsMin.Y || position.Z + radius > map.BoundsMax.Y) {
        return false;
      }

      var navMesh = f.Map.GetNavMesh(rules.NavMeshName);
      var mask = NavMeshRegionMask.Default;
      if (navMesh.Contains(position, mask, true) == false
          || navMesh.Contains(position + new FPVector3(radius, FP._0, radius), mask, true) == false
          || navMesh.Contains(position + new FPVector3(radius, FP._0, -radius), mask, true) == false
          || navMesh.Contains(position + new FPVector3(-radius, FP._0, radius), mask, true) == false
          || navMesh.Contains(position + new FPVector3(-radius, FP._0, -radius), mask, true) == false) {
        return false;
      }

      var buildings = f.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out _, out var transform, out var building)) {
        if (Overlaps(position, transform->Position, radius + f.FindAsset(building->Spec).FootprintRadius)) {
          return false;
        }
      }
      var nodes = f.Filter<Transform3D, ResourceNode>();
      while (nodes.NextUnsafe(out _, out var transform, out _)) {
        if (Overlaps(position, transform->Position, radius + rules.ResourceNodeRadius)) {
          return false;
        }
      }
      return true;
    }

    static bool Overlaps(FPVector3 a, FPVector3 b, FP reach) {
      return FPMath.Abs(a.X - b.X) < reach && FPMath.Abs(a.Z - b.Z) < reach;
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
      for (var i = 0; i < count && building->QueueCount < building->Queue.Length && state->Stock.Covers(cost); i++) {
        state->Stock -= cost;
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
          state->Stock += building->Queue[i].PaidCost;
          state->Stock.Power = FPMath.Min(state->Stock.Power, state->PowerCap);
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

    static ResourceCost PresetCost(Frame f, Building* building, UnitSpec unit) {
      var total = default(ResourceCost);
      var slots = FPMath.Min(unit.ModuleSlots, building->Preset.Length);
      for (var i = 0; i < slots; i++) {
        var module = f.FindAsset(building->Preset[i]);
        if (module != null) {
          total = total + module.Cost;
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

      var buildings = f.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out _, out var transform, out var building)) {
        // 건설은 공병이 진행한다 (WorkerSystem). 정전 중에는 진행도를 유지한 채 멈춘다.
        if (building->ConstructionRemaining > FP._0 || building->QueueCount == 0 || building->Paused != PauseReason.None) {
          continue;
        }

        var state = FactoryWarsUtil.Player(f, building->Owner);
        var unit = f.FindAsset(building->Queue[0].Unit);
        var buildTime = BuildTime(f, state, unit);

        building->QueueProgress += f.DeltaTime;
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
          state->UnitsProduced++;
        }
        state->PopulationUsed += population;
        RemoveOrder(building, 0);
      }
    }

    static FP BuildTime(Frame f, PlayerState* state, UnitSpec unit) {
      var buildTime = unit.BuildTimeSeconds;
      if (unit.Size == UnitSize.Small) {
        buildTime *= f.FindAsset(state->Faction).SmallBuildTimeMultiplier;
      }
      return buildTime;
    }

    /// <summary>생산 타이머가 도는 중이면 true. 인구가 차서 완성 상태로 대기 중이면 false.</summary>
    public static bool IsProducing(Frame f, Building* building, PlayerState* state) {
      return building->QueueCount > 0 && building->QueueProgress < BuildTime(f, state, f.FindAsset(building->Queue[0].Unit));
    }

    static void UpdateTotals(Frame f) {
      for (var i = 0; i < f.PlayerCount; i++) {
        var state = f.Global->Players.GetPointer(i);
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

  /// <summary>공격하는 쪽의 정보. 유닛과 방어탑이 같은 피해 계산을 쓴다.</summary>
  public unsafe struct Weapon {
    public PlayerRef Owner;
    public AttackType Attack;
    public bool IsAir;
    public bool IsRanged;
    public Unit* Unit;
    public UnitSpec Spec;

    public SpecialAbility Ability => Spec != null ? Spec.Ability : SpecialAbility.None;
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

    static bool IsAirUnit(Frame f, EntityRef target) {
      return f.Unsafe.TryGetPointer<Unit>(target, out var unit) && f.FindAsset(unit->Spec).IsAir;
    }

    /// <summary>
    /// 대공은 공중만, 나머지는 지상만 맞힌다. 예외는 둘이다. 저공 비행 유닛은 지상 대장갑 무기에 맞고,
    /// 공중 우선 조준 유닛은 공중이 없으면 지상도 쏜다.
    /// </summary>
    static bool CanHit(Frame f, in Weapon weapon, EntityRef target) {
      if (IsAirUnit(f, target)) {
        if (weapon.Attack == AttackType.AntiAir) {
          return true;
        }
        var spec = f.FindAsset(f.Unsafe.GetPointer<Unit>(target)->Spec);
        return weapon.Attack == AttackType.AntiArmor && weapon.IsAir == false && spec.Ability == SpecialAbility.GroundArmorResist;
      }
      return weapon.Attack != AttackType.AntiAir || weapon.Ability == SpecialAbility.AirPriority;
    }

    static bool IsValidTarget(Frame f, in Weapon weapon, EntityRef target) {
      return TryGetOwner(f, target, out var targetOwner)
        && DiplomacySystem.CanAttack(f, weapon.Owner, targetOwner)
        && CanHit(f, weapon, target);
    }

    static EntityRef FindTarget(Frame f, in Weapon weapon, FPVector3 position, FP minRange, FP range, bool airOnly = false) {
      var best = EntityRef.None;
      var bestDistance = range * range;
      var minDistance = minRange * minRange;

      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out var entity, out var transform, out _)) {
        var distance = FactoryWarsUtil.FlatDistanceSqr(transform->Position, position);
        if (distance <= bestDistance && distance >= minDistance && IsValidTarget(f, weapon, entity)
            && (airOnly == false || IsAirUnit(f, entity))) {
          best = entity;
          bestDistance = distance;
        }
      }
      if (best != EntityRef.None || airOnly) {
        return best;
      }

      bestDistance = FP.UseableMax;
      var buildings = f.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out var entity, out var transform, out var building)) {
        var distance = FactoryWarsUtil.FlatDistanceSqr(transform->Position, position);
        var reach = range + f.FindAsset(building->Spec).FootprintRadius;
        if (distance < bestDistance && distance <= reach * reach && distance >= minDistance && IsValidTarget(f, weapon, entity)) {
          best = entity;
          bestDistance = distance;
        }
      }
      return best;
    }

    /// <summary>대상을 맞힐 수 있는 중심 간 거리의 제곱. 건물은 부지 가장자리까지 잰다.</summary>
    public static FP ReachSqr(Frame f, EntityRef target, FP range) {
      if (f.Unsafe.TryGetPointer<Building>(target, out var building)) {
        range += f.FindAsset(building->Spec).FootprintRadius;
      }
      return range * range;
    }

    public static FP EffectiveRange(Frame f, Unit* unit, UnitSpec spec) {
      var faction = f.FindAsset(FactoryWarsUtil.Player(f, unit->Owner)->Faction);
      return spec.IsRanged ? spec.Range * faction.RangedRangeMultiplier : spec.Range;
    }

    static bool FindMatchup(GameRulesSpec rules, AttackType attack, out DamageMatchup matchup) {
      for (var i = 0; rules.Matchups != null && i < rules.Matchups.Length; i++) {
        if (rules.Matchups[i].Attack == attack) {
          matchup = rules.Matchups[i];
          return true;
        }
      }
      matchup = default;
      return false;
    }

    /// <summary>공격 유형과 유닛 대상의 상성 배율. 표에 없는 공격 유형은 1이다.</summary>
    public static FP UnitMultiplier(GameRulesSpec rules, AttackType attack, UnitSpec target) {
      if (FindMatchup(rules, attack, out var matchup) == false) {
        return FP._1;
      }
      if (target.IsAir) {
        return matchup.VsAir;
      }
      switch (target.Size) {
        case UnitSize.Medium: return matchup.VsMedium;
        case UnitSize.Large: return matchup.VsLarge;
        default: return matchup.VsSmall;
      }
    }

    public static FP BuildingMultiplier(GameRulesSpec rules, AttackType attack) {
      return FindMatchup(rules, attack, out var matchup) ? matchup.VsBuilding : FP._1;
    }

    static void DamageUnit(Frame f, in Weapon weapon, EntityRef target, Unit* unit, FP amount) {
      if (unit->Health <= FP._0) {
        return;
      }
      var spec = f.FindAsset(unit->Spec);
      amount *= UnitMultiplier(FactoryWarsUtil.Rules(f), weapon.Attack, spec);
      if (spec.Ability == SpecialAbility.GroundArmorResist && weapon.Attack == AttackType.AntiArmor && weapon.IsAir == false) {
        amount *= spec.AbilityMultiplier;
      }
      if (weapon.IsRanged && f.Number < unit->DesignatedUntilFrame
          && (weapon.Owner == unit->DesignatedBy || DiplomacySystem.GetRelation(f, weapon.Owner, unit->DesignatedBy) == Relation.Allied)) {
        amount *= unit->DesignatedBonus;
      }
      amount = ModuleSystem.ApplyOutgoing(f, weapon.Unit, spec, amount);
      unit->Health -= ModuleSystem.ApplyIncoming(f, unit, amount);

      if (unit->Health <= FP._0) {
        f.Events.UnitDestroyed(target, unit->Owner, unit->Spec);
        FactoryWarsUtil.Player(f, unit->Owner)->UnitsLost++;
        f.Destroy(target);
      } else if (weapon.Ability == SpecialAbility.TargetDesignator) {
        unit->DesignatedBy = weapon.Owner;
        unit->DesignatedBonus = weapon.Spec.AbilityMultiplier;
        unit->DesignatedUntilFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, weapon.Spec.AbilitySeconds);
      }
    }

    static void Damage(Frame f, in Weapon weapon, EntityRef target, FP amount) {
      if (f.Unsafe.TryGetPointer<Unit>(target, out var unit)) {
        DamageUnit(f, weapon, target, unit, amount);
      } else if (f.Unsafe.TryGetPointer<Building>(target, out var building)) {
        if (building->Health <= FP._0) {
          return;
        }
        amount *= BuildingMultiplier(FactoryWarsUtil.Rules(f), weapon.Attack);
        if (weapon.Ability == SpecialAbility.FlameBurst) {
          amount *= weapon.Spec.AbilityMultiplier;
        }
        building->Health -= amount;
        if (building->Health <= FP._0) {
          FactoryWarsUtil.Player(f, building->Owner)->BuildingsLost++;
          f.Destroy(target);
        }
      }
    }

    static void DamageArea(Frame f, in Weapon weapon, FPVector3 center, FP radius, FP amount) {
      var radiusSqr = radius * radius;
      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out var entity, out var transform, out _)) {
        if (FactoryWarsUtil.FlatDistanceSqr(transform->Position, center) <= radiusSqr && IsValidTarget(f, weapon, entity)) {
          Damage(f, weapon, entity, amount);
        }
      }
      var buildings = f.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out var entity, out var transform, out _)) {
        if (FactoryWarsUtil.FlatDistanceSqr(transform->Position, center) <= radiusSqr && IsValidTarget(f, weapon, entity)) {
          Damage(f, weapon, entity, amount);
        }
      }
    }

    /// <summary>주변 공중 적을 주 대상 포함 최대 AbilityCount기까지 동시에 맞힌다.</summary>
    static void FireMultiAir(Frame f, in Weapon weapon, EntityRef primary, FPVector3 position, FP range, FP amount) {
      Damage(f, weapon, primary, amount);
      var remaining = weapon.Spec.AbilityCount - 1;
      var units = f.Filter<Transform3D, Unit>();
      while (remaining > 0 && units.NextUnsafe(out var entity, out var transform, out _)) {
        if (entity != primary && FactoryWarsUtil.FlatDistanceSqr(transform->Position, position) <= range * range
            && IsAirUnit(f, entity) && IsValidTarget(f, weapon, entity)) {
          Damage(f, weapon, entity, amount);
          remaining--;
        }
      }
    }

    /// <summary>쿨다운이 끝났고 주변 적이 AbilityCount기 이상이면 폭격기 아래를 넓게 폭격한다.</summary>
    static void TryCarpetBomb(Frame f, in Weapon weapon, Unit* unit, FPVector3 position) {
      var spec = weapon.Spec;
      unit->AbilityCooldown = FPMath.Max(FP._0, unit->AbilityCooldown - f.DeltaTime);
      if (unit->AbilityCooldown > FP._0) {
        return;
      }
      var radiusSqr = spec.AbilityRadius * spec.AbilityRadius;
      var count = 0;
      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out var entity, out var transform, out _)) {
        if (FactoryWarsUtil.FlatDistanceSqr(transform->Position, position) <= radiusSqr && IsValidTarget(f, weapon, entity)) {
          count++;
        }
      }
      if (count >= spec.AbilityCount) {
        unit->AbilityCooldown = spec.AbilityCooldownSeconds;
        DamageArea(f, weapon, position, spec.AbilityRadius, spec.Damage * spec.AbilityMultiplier);
      }
    }

    static void Fire(Frame f, in Weapon weapon, Unit* unit, EntityRef target, FPVector3 position, FPVector3 targetPosition, FP range) {
      var spec = weapon.Spec;
      var amount = spec.Damage;
      if (spec.Ability == SpecialAbility.FirstShotDouble) {
        if (f.Number >= unit->EngagedUntilFrame) {
          amount *= spec.AbilityMultiplier;
        }
        unit->EngagedUntilFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, spec.AbilitySeconds);
      }

      unit->AttackCooldown = spec.AttackInterval;
      if (spec.Ability == SpecialAbility.MultiAirTarget) {
        FireMultiAir(f, weapon, target, position, range, amount);
      } else if (spec.SplashRadius > FP._0) {
        DamageArea(f, weapon, targetPosition, spec.SplashRadius, amount);
      } else {
        Damage(f, weapon, target, amount);
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

        var weapon = new Weapon {
          Owner = unit->Owner, Attack = spec.Attack, IsAir = spec.IsAir, IsRanged = spec.IsRanged, Unit = unit, Spec = spec,
        };
        var range = EffectiveRange(f, unit, spec);
        var position = transform->Position;

        if (spec.Ability == SpecialAbility.CarpetBomb) {
          TryCarpetBomb(f, weapon, unit, position);
        }

        if (unit->TargetEntity != EntityRef.None && IsValidTarget(f, weapon, unit->TargetEntity) == false) {
          unit->TargetEntity = EntityRef.None;
          if (unit->Order == UnitOrder.AttackTarget) {
            unit->Order = UnitOrder.Stop;
          }
        }

        // 이동 명령 중에는 자동 조준하지 않는다.
        if (unit->Order != UnitOrder.Move && unit->Order != UnitOrder.AttackTarget && spec.Ability == SpecialAbility.AirPriority) {
          var air = FindTarget(f, weapon, position, spec.MinRange, range, airOnly: true);
          if (air != EntityRef.None) {
            unit->TargetEntity = air;
          }
        }
        if (unit->TargetEntity == EntityRef.None && unit->Order != UnitOrder.Move) {
          unit->TargetEntity = FindTarget(f, weapon, position, spec.MinRange, range);
        }
        if (unit->TargetEntity == EntityRef.None || unit->AttackCooldown > FP._0) {
          continue;
        }

        var targetPosition = f.Unsafe.GetPointer<Transform3D>(unit->TargetEntity)->Position;
        var distance = FactoryWarsUtil.FlatDistanceSqr(targetPosition, position);
        if (distance > ReachSqr(f, unit->TargetEntity, range) || distance < spec.MinRange * spec.MinRange) {
          // 정지 상태에서 사거리를 벗어난 대상은 쫓지 않는다.
          if (unit->Order == UnitOrder.Stop) {
            unit->TargetEntity = EntityRef.None;
          }
          continue;
        }

        Fire(f, weapon, unit, unit->TargetEntity, position, targetPosition, range);
      }

      UpdateTowers(f);
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
        var step = state->Outage ? f.DeltaTime / rules.OutageTowerIntervalMultiplier : f.DeltaTime;
        building->AttackCooldown = FPMath.Max(FP._0, building->AttackCooldown - step);
        if (building->AttackCooldown > FP._0) {
          continue;
        }

        var weapon = new Weapon { Owner = building->Owner, Attack = spec.Attack, IsRanged = true };
        var target = FindTarget(f, weapon, transform->Position, FP._0, spec.Range);
        if (target != EntityRef.None) {
          building->TargetEntity = target;
          building->AttackCooldown = spec.AttackInterval;
          Damage(f, weapon, target, spec.Damage);
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
      if (f.Unsafe.TryGetPointer<Worker>(entity, out var worker)) {
        worker->Task = WorkerTask.Idle;
      }
      SetDestination(f, entity, target);
    }

    public static void SetDestination(Frame f, EntityRef entity, FPVector3 target) {
      if (f.Unsafe.TryGetPointer<NavMeshPathfinder>(entity, out var pathfinder)) {
        var navMesh = f.Map.GetNavMesh(FactoryWarsUtil.Rules(f).NavMeshName);
        if (navMesh != null) {
          pathfinder->SetTarget(f, target, navMesh);
        }
      }
    }

    public static void Halt(Frame f, EntityRef entity) {
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
      if (f.Number % repathFrames == 0) {
        UpdateSwarmSpeed(f);
      }

      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out var entity, out var transform, out var unit)) {
        var spec = f.FindAsset(unit->Spec);
        var hasDestination = unit->Order == UnitOrder.Move || unit->Order == UnitOrder.AttackMove;
        var destination = unit->TargetPosition;

        if (unit->TargetEntity != EntityRef.None && unit->Order != UnitOrder.Move
            && f.Unsafe.TryGetPointer<Transform3D>(unit->TargetEntity, out var targetTransform)) {
          var range = CombatSystem.EffectiveRange(f, unit, spec);
          if (FactoryWarsUtil.FlatDistanceSqr(targetTransform->Position, transform->Position) <= CombatSystem.ReachSqr(f, unit->TargetEntity, range)) {
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

      PushOutOfBuildings(f);
    }

    /// <summary>
    /// NavMesh는 고정이라 경로가 건물을 지나간다. 지상 유닛을 가까운 변 밖으로 밀어내고,
    /// 이동 중이면 변을 따라 가까운 모서리로 미끄러뜨려 건물을 돌아가게 한다.
    /// </summary>
    static void PushOutOfBuildings(Frame f) {
      var clearance = FactoryWarsUtil.Rules(f).UnitClearance;
      var units = f.Filter<Transform3D, NavMeshSteeringAgent, NavMeshPathfinder>();
      while (units.NextUnsafe(out _, out var unit, out var agent, out var pathfinder)) {
        var buildings = f.Filter<Transform3D, Building>();
        while (buildings.NextUnsafe(out _, out var transform, out var building)) {
          var reach = f.FindAsset(building->Spec).FootprintRadius + clearance;
          var x = unit->Position.X - transform->Position.X;
          var z = unit->Position.Z - transform->Position.Z;
          var overlapX = reach - FPMath.Abs(x);
          var overlapZ = reach - FPMath.Abs(z);
          if (overlapX <= FP._0 || overlapZ <= FP._0) {
            continue;
          }
          var slide = pathfinder->IsActive ? agent->MaxSpeed * f.DeltaTime : FP._0;
          var position = unit->Position;
          var center = transform->Position;
          var target = pathfinder->Target;
          if (overlapX < overlapZ) {
            var side = x < FP._0 ? -reach : reach;
            position.X = center.X + side;
            position.Z += slide * Detour(position.X, position.Z, target.X, target.Z, center.X + side, center.Z, reach);
          } else {
            var side = z < FP._0 ? -reach : reach;
            position.Z = center.Z + side;
            position.X += slide * Detour(position.Z, position.X, target.Z, target.X, center.Z + side, center.X, reach);
          }
          unit->Position = position;
        }
      }
    }

    /// <summary>같은 유닛이 AbilityCount대 이상이면 이동 속도가 오른다. 1초마다 갱신한다.</summary>
    static void UpdateSwarmSpeed(Frame f) {
      var units = f.Filter<Unit, NavMeshSteeringAgent>();
      while (units.NextUnsafe(out _, out var unit, out var agent)) {
        var spec = f.FindAsset(unit->Spec);
        if (spec.Ability != SpecialAbility.SwarmSpeed) {
          continue;
        }
        var count = 0;
        var others = f.Filter<Unit>();
        while (others.NextUnsafe(out _, out var other)) {
          if (other->Owner == unit->Owner && other->Spec == unit->Spec) {
            count++;
          }
        }
        agent->MaxSpeed = count >= spec.AbilityCount ? spec.MoveSpeed * spec.AbilityMultiplier : spec.MoveSpeed;
      }
    }

    /// <summary>
    /// 면을 따라 갈 방향(+1 또는 -1). 면의 양 끝 모서리 중 목표까지 총거리가 짧은 쪽을 고른다.
    /// 면 위 어디서든 같은 모서리가 나오므로 왕복하지 않는다. n은 면에 수직인 축, t는 면을 따르는 축이다.
    /// </summary>
    static int Detour(FP n, FP t, FP targetN, FP targetT, FP faceN, FP centerT, FP reach) {
      var high = centerT + reach;
      var low = centerT - reach;
      var viaHigh = FPMath.Abs(high - t) + new FPVector2(targetN - faceN, targetT - high).Magnitude;
      var viaLow = FPMath.Abs(t - low) + new FPVector2(targetN - faceN, targetT - low).Magnitude;
      return viaHigh <= viaLow ? 1 : -1;
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
