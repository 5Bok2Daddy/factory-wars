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
      // 배는 조선소 주변 여덟 방향 중 처음 물 위인 곳에 나온다.
      var unitSpec = f.FindAsset(specRef);
      if (unitSpec != null && unitSpec.IsNaval) {
        var rules = FactoryWarsUtil.Rules(f);
        var waterNavMesh = f.Map.GetNavMesh(rules.WaterNavMeshName);
        var radius = source != null ? f.FindAsset(source->Spec).FootprintRadius : rules.SpawnOffset;
        var dist = radius + rules.ShipSpawnGap;
        for (var d = 0; d < 8; d++) {
          var a = FP.PiTimes2 * (index + d) / 8;
          var p = origin + new FPVector3(FPMath.Cos(a), FP._0, FPMath.Sin(a)) * dist;
          if (waterNavMesh != null && waterNavMesh.Contains(p, NavMeshRegionMask.Default, true)) {
            return SpawnUnitAt(f, owner, specRef, p, source);
          }
        }
      }
      // 건물 주변에 골고루 놓는다.
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
        Health = TechSystem.MaxHealth(f, owner, specRef),
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
        // 배는 수상 전용 에이전트 설정을 쓴다(물 밖 목표를 가까운 물 위로 보정, 항목 24).
        var naval = spec.IsNaval ? FactoryWarsUtil.Rules(f).NavalAgent : default;
        var config = f.FindAsset(naval.IsValid ? naval : f.SimulationConfig.Navigation.DefaultNavMeshAgent);
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
        f.Events.ActionRejected(player, ActionKind.Build, RejectReason.Invalid, 0);
        return;
      }
      position.Y = FP._0;
      if (FactoryWarsUtil.IsReachable(f, player, position) == false) {
        f.Events.ActionRejected(player, ActionKind.Build, RejectReason.Truce, 0);
        return;
      }
      if (CanPlace(f, spec, position) == false) {
        f.Events.ActionRejected(player, ActionKind.Build, RejectReason.BlockedLocation, 0);
        return;
      }
      if (spec.RequiredTier > TechSystem.Tier(f, player)) {
        f.Events.ActionRejected(player, ActionKind.Build, RejectReason.TierLocked, 0);
        return;
      }
      if (IsUnlocked(f, player, spec) == false) {
        f.Events.ActionRejected(player, ActionKind.Build, RejectReason.Locked, 0);
        return;
      }
      var missing = state->Stock.Missing(spec.Cost);
      if (missing != MissingResource.None) {
        f.Events.ActionRejected(player, ActionKind.Build, RejectReason.InsufficientResources, (int)missing);
        return;
      }
      state->Stock -= spec.Cost;
      WorkerSystem.Assign(f, engineer, WorkerTask.Build, CreateBuilding(f, player, specRef, position));
    }

    /// <summary>기지 단계가 되고 선행 건물을 모두 완공했으면 true.</summary>
    public static bool IsUnlocked(Frame f, PlayerRef player, BuildingSpec spec) {
      if (spec.RequiredTier > TechSystem.Tier(f, player)) {
        return false;
      }
      for (var i = 0; spec.Prerequisites != null && i < spec.Prerequisites.Length; i++) {
        if (HasFinished(f, player, spec.Prerequisites[i]) == false) {
          return false;
        }
      }
      return true;
    }

    public static bool HasFinished(Frame f, PlayerRef player, AssetRef<BuildingSpec> specRef) {
      var buildings = f.Filter<Building>();
      while (buildings.NextUnsafe(out _, out var building)) {
        if (building->Owner == player && building->Spec == specRef && building->ConstructionRemaining <= FP._0) {
          return true;
        }
      }
      return false;
    }

    /// <summary>맵 안, 걸어갈 수 있는 땅, 건물·자원·지형에서 떨어진 곳, 건설 금지 구역 밖에만 지을 수 있다.</summary>
    public static bool CanPlace(Frame f, BuildingSpec spec, FPVector3 position) {
      var map = FactoryWarsUtil.MapInfo(f);
      var rules = FactoryWarsUtil.Rules(f);
      var radius = spec.FootprintRadius;
      if (position.X - radius < map.BoundsMin.X || position.X + radius > map.BoundsMax.X
          || position.Z - radius < map.BoundsMin.Y || position.Z + radius > map.BoundsMax.Y) {
        return false;
      }

      var isShipyard = spec.Role == BuildingRole.Shipyard || spec.NeedsWater;
      var navMesh = f.Map.GetNavMesh(rules.NavMeshName);
      var mask = NavMeshRegionMask.Default;
      if (isShipyard) {
        if (navMesh.Contains(position, mask, true) == false) {
          return false;
        }
        var cornersOnGround = 0;
        if (navMesh.Contains(position + new FPVector3(radius, FP._0, radius), mask, true)) cornersOnGround++;
        if (navMesh.Contains(position + new FPVector3(radius, FP._0, -radius), mask, true)) cornersOnGround++;
        if (navMesh.Contains(position + new FPVector3(-radius, FP._0, radius), mask, true)) cornersOnGround++;
        if (navMesh.Contains(position + new FPVector3(-radius, FP._0, -radius), mask, true)) cornersOnGround++;
        if (cornersOnGround < 2) {
          return false;
        }
      } else {
        if (navMesh.Contains(position, mask, true) == false
            || navMesh.Contains(position + new FPVector3(radius, FP._0, radius), mask, true) == false
            || navMesh.Contains(position + new FPVector3(radius, FP._0, -radius), mask, true) == false
            || navMesh.Contains(position + new FPVector3(-radius, FP._0, radius), mask, true) == false
            || navMesh.Contains(position + new FPVector3(-radius, FP._0, -radius), mask, true) == false) {
          return false;
        }
      }

      var buildings = f.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out _, out var transform, out var building)) {
        if (Overlaps(position, transform->Position, radius + f.FindAsset(building->Spec).FootprintRadius + rules.BuildingGap)) {
          return false;
        }
      }
      // 고갈된 노드도 그대로 남아 있어 같은 검사를 받는다.
      var nodes = f.Filter<ResourceNode>();
      while (nodes.NextUnsafe(out _, out var node)) {
        if (IsClear(node->Shape, position, radius, rules.ResourceBuildMargin) == false) {
          return false;
        }
      }
      var touchesWater = false;
      var waterMargin = radius + rules.ShipyardWaterReach;
      for (var i = 0; map.Terrain != null && i < map.Terrain.Length; i++) {
        if (map.Terrain[i].Kind == TerrainKind.River) {
          if (isShipyard) {
            if (map.Terrain[i].Shape.Distance(position) <= waterMargin) {
              touchesWater = true;
            }
            continue;
          }
        }
        if (IsClear(map.Terrain[i].Shape, position, radius, rules.TerrainBuildMargin) == false) {
          return false;
        }
      }
      if (isShipyard) {
        if (map.Channel != null) {
          for (var i = 0; i < map.Channel.Length; i++) {
            if (map.Channel[i].Distance(position) <= waterMargin) {
              touchesWater = true;
            }
          }
        }
        if (touchesWater == false) {
          return false;
        }
      }
      for (var i = 0; map.NoBuildZones != null && i < map.NoBuildZones.Length; i++) {
        if (IsClear(map.NoBuildZones[i], position, radius, FP._0) == false) {
          return false;
        }
      }
      for (var i = 0; map.CityBuildings != null && i < map.CityBuildings.Length; i++) {
        if (IsClear(map.CityBuildings[i].Shape, position, radius, rules.TerrainBuildMargin) == false
            || (map.CityBuildings[i].Topples && IsClear(map.CityBuildings[i].FallZone, position, radius, FP._0) == false)) {
          return false;
        }
      }
      return true;
    }

    /// <summary>정사각형 부지의 가장자리·중심 아홉 점이 모양에서 margin 이상 떨어져 있으면 true.</summary>
    static bool IsClear(MapShape shape, FPVector3 position, FP radius, FP margin) {
      var reach = shape.Reach + radius * 2 + margin;
      if (FPMath.Abs(position.X - shape.Center.X) > reach || FPMath.Abs(position.Z - shape.Center.Z) > reach) {
        return true;
      }
      for (var x = -1; x <= 1; x++) {
        for (var z = -1; z <= 1; z++) {
          if (shape.Distance(position + new FPVector3(radius * x, FP._0, radius * z)) < margin) {
            return false;
          }
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
      // 항목 37 기갑 요새화 군단: 전차·포격함 생산비 할인.
      if (TacticalSystem.DoctrineOf(f, player) == 3 && TacticalSystem.IsArmorClass(unit)) {
        cost = cost * FactoryWarsUtil.Rules(f).ArmorCostMultiplier;
      }
      var population = unit.Population * unit.SpawnCount;
      for (var i = 0; i < count && building->QueueCount < building->Queue.Length; i++) {
        // 항목 31: 기지 단계와 크기별 보유 상한.
        if (TechSystem.CanProduce(f, player, unit, out var locked) == false) {
          f.Events.ActionRejected(player, ActionKind.Produce, locked, 0);
          return;
        }
        var missing = state->Stock.Missing(cost);
        // 대기열에 든 유닛의 인구도 이미 쓴 것으로 본다.
        if (state->PopulationUsed + QueuedPopulation(f, player) + population > state->PopulationCap) {
          missing |= MissingResource.Population;
        }
        if (missing != MissingResource.None) {
          var reason = missing == MissingResource.Population ? RejectReason.PopulationLimit : RejectReason.InsufficientResources;
          f.Events.ActionRejected(player, ActionKind.Produce, reason, (int)missing);
          return;
        }
        state->Stock -= cost;
        var order = building->Queue.GetPointer(building->QueueCount++);
        order->Unit = unitRef;
        order->PaidCost = cost;
      }
    }

    /// <summary>대기열에 있어 아직 태어나지 않은 유닛의 인구 합.</summary>
    public static FP QueuedPopulation(Frame f, PlayerRef player) {
      var total = FP._0;
      var buildings = f.Filter<Building>();
      while (buildings.NextUnsafe(out _, out var building)) {
        if (building->Owner != player) {
          continue;
        }
        for (var i = 0; i < building->QueueCount; i++) {
          var unit = f.FindAsset(building->Queue[i].Unit);
          total += unit.Population * unit.SpawnCount;
        }
      }
      return total;
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
        // 항목 34-D: 작전기는 유닛으로 나오지 않고 비행장 격납 칸에 대기한다(칸이 없으면 완성 상태로 기다린다).
        if (unit.SortieKind != SortieKind.None) {
          if (SortieSystem.Store(f, building, building->Queue[0].Unit) == false) {
            continue;
          }
          state->UnitsProduced++;
          RemoveOrder(building, 0);
          continue;
        }
        for (var i = 0; i < unit.SpawnCount; i++) {
          var spawned = SpawnUnit(f, building->Owner, building->Queue[0].Unit, transform->Position, i, building);
          state->UnitsProduced++;
          if (building->HasRally && f.Unsafe.TryGetPointer<Unit>(spawned, out var fresh)) {
            MovementSystem.Order(f, spawned, fresh, UnitOrder.Move, building->Rally);
          }
        }
        state->PopulationUsed += population;
        state->ProducedBySize[(int)unit.Size] += unit.SpawnCount;
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
    public bool GroundOnly;
    public Unit* Unit;
    public UnitSpec Spec;
    /// <summary>항목 32: 쏜 자리(엄폐 방향 판정).</summary>
    public FPVector3 Origin;
    /// <summary>범위 공격(포격·폭격·화염): 엄폐 효과가 CoverAreaFactor배.</summary>
    public bool Area;
    /// <summary>항목 33: 엄폐를 무시한다(전략 미사일).</summary>
    public bool IgnoreCover;
    /// <summary>항목 33: 궁극 전력의 공격(미사일·항모·드론·상륙정). 부순 핵심 건물을 따로 센다.</summary>
    public bool Ultimate;

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
    /// 대공은 공중만, 나머지는 지상만 맞힌다. 예외는 둘이다. 저공 비행 유닛은 지상 대장갑 무기에 맞고(지상 전용 무기는 제외),
    /// 공중 우선 조준 유닛은 공중이 없으면 지상도 쏜다.
    /// </summary>
    static bool CanHit(Frame f, in Weapon weapon, EntityRef target) {
      if (IsAirUnit(f, target)) {
        if (weapon.GroundOnly) {
          return false;
        }
        if (weapon.Attack == AttackType.AntiAir) {
          return true;
        }
        var spec = f.FindAsset(f.Unsafe.GetPointer<Unit>(target)->Spec);
        return weapon.Attack == AttackType.AntiArmor && weapon.IsAir == false && spec.Ability == SpecialAbility.GroundArmorResist;
      }
      return weapon.Attack != AttackType.AntiAir || weapon.Ability == SpecialAbility.AirPriority;
    }

    static bool IsValidTarget(Frame f, in Weapon weapon, EntityRef target) {
      if (f.Unsafe.TryGetPointer<NpcTrader>(target, out var trader)) {
        return trader->Destroyed == false && FactoryWarsUtil.InTruce(f) == false && weapon.Attack != AttackType.AntiAir;
      }
      if (f.Has<Barricade>(target)) {
        return FactoryWarsUtil.InTruce(f) == false && weapon.Attack != AttackType.AntiAir;
      }
      if (f.Unsafe.TryGetPointer<CityBuilding>(target, out var city)) {
        return FactoryWarsUtil.InTruce(f) == false && weapon.Attack != AttackType.AntiAir
          && (city->Occupant.IsValid == false || DiplomacySystem.CanAttack(f, weapon.Owner, city->Occupant));
      }
      if (f.Unsafe.TryGetPointer<Unit>(target, out var garrisoned) && garrisoned->Garrison != EntityRef.None) {
        return false;
      }
      return TryGetOwner(f, target, out var targetOwner)
        && DiplomacySystem.CanAttack(f, weapon.Owner, targetOwner)
        && CanHit(f, weapon, target);
    }

    /// <summary>고지대 위에서 고지대 밖을 쏘면 사거리가 는다.</summary>
    public static FP RangeTo(Frame f, FP range, FPVector3 from, FPVector3 to) {
      var map = FactoryWarsUtil.MapInfo(f);
      return map.HighGroundAt(from) >= 0 && map.HighGroundAt(to) < 0 ? range * FactoryWarsUtil.Rules(f).HighGroundRangeMultiplier : range;
    }

    static EntityRef FindTarget(Frame f, in Weapon weapon, FPVector3 position, FP minRange, FP range, bool airOnly = false) {
      var best = EntityRef.None;
      var map = FactoryWarsUtil.MapInfo(f);
      var elevated = map.HighGroundAt(position) >= 0;
      var longest = elevated ? range * FactoryWarsUtil.Rules(f).HighGroundRangeMultiplier : range;
      var bestDistance = longest * longest;
      var minDistance = minRange * minRange;

      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out var entity, out var transform, out _)) {
        var distance = FactoryWarsUtil.FlatDistanceSqr(transform->Position, position);
        if (distance <= bestDistance && distance >= minDistance && IsValidTarget(f, weapon, entity)
            && (airOnly == false || IsAirUnit(f, entity))
            && (elevated == false || distance <= RangeSqr(f, range, position, transform->Position))
            && FactoryWarsUtil.IsVisibleTo(f, weapon.Owner, entity)) {
          best = entity;
          bestDistance = distance;
        }
      }
      if (best != EntityRef.None || airOnly) {
        return best;
      }

      bestDistance = FP.MaxValue;
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

    static FP RangeSqr(Frame f, FP range, FPVector3 from, FPVector3 to) {
      var value = RangeTo(f, range, from, to);
      return value * value;
    }

    /// <summary>대상을 맞힐 수 있는 중심 간 거리의 제곱. 건물은 부지 가장자리까지, 바리케이드는 앞면까지 잰다.</summary>
    public static FP ReachSqr(Frame f, EntityRef target, FP range) {
      if (f.Unsafe.TryGetPointer<Building>(target, out var building)) {
        range += f.FindAsset(building->Spec).FootprintRadius;
      } else if (f.Unsafe.TryGetPointer<Barricade>(target, out var barricade)) {
        range += barricade->Shape.HalfWidth + FP._2 + FP._1;
      } else if (f.Has<NpcTrader>(target)) {
        range += FactoryWarsUtil.Rules(f).TraderRadius;
      } else if (f.Unsafe.TryGetPointer<CityBuilding>(target, out var city)) {
        var shape = FactoryWarsUtil.MapInfo(f).CityBuildings[city->Index].Shape;
        range += FPMath.Max(shape.HalfLength, shape.HalfWidth) + FP._1;
      }
      return range * range;
    }

    public static FP EffectiveRange(Frame f, Unit* unit, UnitSpec spec) {
      var faction = f.FindAsset(FactoryWarsUtil.Player(f, unit->Owner)->Faction);
      var range = spec.Range + TechSystem.RangeBonus(f, unit->Owner, unit->Spec);
      var result = spec.IsRanged ? range * faction.RangedRangeMultiplier : range;
      return result * TacticalSystem.VetRange(f, unit);
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

    public static void DamageUnit(Frame f, in Weapon weapon, EntityRef target, Unit* unit, FP amount) {
      if (unit->Health <= FP._0) {
        return;
      }
      var spec = f.FindAsset(unit->Spec);
      DiplomacySystem.NoteAttack(f, weapon.Owner, unit->Owner);
      amount *= UnitMultiplier(FactoryWarsUtil.Rules(f), weapon.Attack, spec);
      if (spec.Ability == SpecialAbility.GroundArmorResist && weapon.Attack == AttackType.AntiArmor && weapon.IsAir == false) {
        amount *= spec.AbilityMultiplier;
      }
      if (weapon.IsRanged && f.Number < unit->DesignatedUntilFrame
          && (weapon.Owner == unit->DesignatedBy || DiplomacySystem.GetRelation(f, weapon.Owner, unit->DesignatedBy) == Relation.Allied)) {
        amount *= unit->DesignatedBonus;
      }
      amount = ModuleSystem.ApplyOutgoing(f, weapon.Unit, spec, amount);
      // 항목 37 블랙박스: 영구 화력 보너스(회수 횟수만큼 중첩).
      if (weapon.Owner.IsValid) {
        amount *= DepthSystem.OutgoingBonus(f, weapon.Owner);
      }
        // 항목 31: 방어 연구. 항목 32: 엄폐. 주둔 보병/저격수 -25% 피해 감소.
      amount *= TechSystem.IncomingMultiplier(f, unit->Owner, spec);
      if (unit->Garrison != EntityRef.None) {
        amount *= FP._0_75;
      }
      if (weapon.IgnoreCover == false) {
        amount *= CoverSystem.DamageMultiplier(f, unit, weapon.Origin, weapon.Area);
        // 항목 37 그림자 특수전단: 엄폐 중 받는 피해가 더 준다.
        if (unit->InCover && TacticalSystem.DoctrineOf(f, unit->Owner) == 2) {
          amount *= FP._1 - FactoryWarsUtil.Rules(f).ShadowCoverBonus;
        }
      }
      // 항목 33: 공중 항모는 대공 무기에 더 크게 맞는다(카운터).
      if (weapon.Attack == AttackType.AntiAir && f.Has<Carrier>(target)) {
        var ultimate = UltimateSystem.SpecOf(f, unit->Owner);
        if (ultimate != null) {
          amount *= ultimate.AntiAirTakenMultiplier;
        }
      }
      unit->Health -= ModuleSystem.ApplyIncoming(f, unit, amount);
      unit->LastDamagedFrame = f.Number;

      if (unit->Health <= FP._0) {
        UltimateSystem.OnUnitKilled(f, target, unit);
        f.Events.UnitDestroyed(target, unit->Owner, unit->Spec);
        f.Events.Wrecked(f.Unsafe.GetPointer<Transform3D>(target)->Position, (int)spec.Size, FP._0);
        DepthSystem.LeaveWreck(f, f.Unsafe.GetPointer<Transform3D>(target)->Position, spec, unit->Spec);
        FactoryWarsUtil.Player(f, unit->Owner)->UnitsLost++;
        var killer = FactoryWarsUtil.Player(f, weapon.Owner);
        if (killer != null) {
          killer->UnitsKilled++;
        }
        TacticalSystem.OnKill(f, weapon.Unit);
        f.Destroy(target);
      } else if (weapon.Ability == SpecialAbility.TargetDesignator) {
        unit->DesignatedBy = weapon.Owner;
        unit->DesignatedBonus = weapon.Spec.AbilityMultiplier;
        unit->DesignatedUntilFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, weapon.Spec.AbilitySeconds);
      }
    }

    public static void Damage(Frame f, in Weapon weapon, EntityRef target, FP amount) {
      if (f.Unsafe.TryGetPointer<Unit>(target, out var unit)) {
        DamageUnit(f, weapon, target, unit, amount);
      } else if (f.Unsafe.TryGetPointer<Building>(target, out var building)) {
        if (building->Health <= FP._0) {
          return;
        }
        DiplomacySystem.NoteAttack(f, weapon.Owner, building->Owner);
        WarnBase(f, target, building);
        amount *= BuildingMultiplier(FactoryWarsUtil.Rules(f), weapon.Attack);
        if (weapon.Ability == SpecialAbility.FlameBurst) {
          amount *= weapon.Spec.AbilityMultiplier;
        }
        amount *= DepthSystem.ProtocolFactor(f, building);
        building->Health -= amount;
        DepthSystem.CheckProtocol(f, target, building);
        if (building->Health <= FP._0) {
          var lost = f.FindAsset(building->Spec);
          var loser = FactoryWarsUtil.Player(f, building->Owner);
          loser->BuildingsLost++;
          var destroyer = FactoryWarsUtil.Player(f, weapon.Owner);
          if (destroyer != null) {
            destroyer->BuildingsDestroyed++;
          }
          // 항목 33: 핵심 건물 손실과, 그중 궁극 전력이 부순 수(측정).
          if (lost.IsCoreBuilding) {
            loser->CoreLost++;
            if (weapon.Ultimate && destroyer != null && weapon.Owner != building->Owner) {
              destroyer->UltimateCoreKills++;
            }
          }
          // 벙커 안 병력은 튕겨 나오며 피해를 입는다.
          if (lost.GarrisonCapacity > 0) {
            CitySystem.ExitAll(f, PlayerRef.None, target, FactoryWarsUtil.Rules(f).GarrisonEjectFraction);
          }
          f.Events.Wrecked(f.Unsafe.GetPointer<Transform3D>(target)->Position, -1, f.FindAsset(building->Spec).FootprintRadius);
          f.Destroy(target);
        }
      } else if (f.Unsafe.TryGetPointer<Barricade>(target, out var barricade)) {
        BattlefieldSystem.HitBarricade(f, target, barricade, amount * BuildingMultiplier(FactoryWarsUtil.Rules(f), weapon.Attack));
      } else if (f.Unsafe.TryGetPointer<CityBuilding>(target, out var city)) {
        CitySystem.Hit(f, target, city, amount * CitySystem.Multiplier(f, city, weapon.Attack, weapon.Spec));
      } else if (f.Unsafe.TryGetPointer<NpcTrader>(target, out var trader)) {
        NpcTraderSystem.Hit(f, target, trader, weapon.Owner, amount * BuildingMultiplier(FactoryWarsUtil.Rules(f), weapon.Attack));
      }
    }

    /// <summary>본진 안 건물이 맞으면 주인에게 알린다. 자주 울리지 않게 간격을 둔다.</summary>
    static void WarnBase(Frame f, EntityRef target, Building* building) {
      var state = FactoryWarsUtil.Player(f, building->Owner);
      var areas = FactoryWarsUtil.MapInfo(f).BaseAreas;
      if (areas == null || areas.Length == 0 || f.Number < state->NextBaseAlertFrame) {
        return;
      }
      var position = f.Unsafe.GetPointer<Transform3D>(target)->Position;
      // 플레이어 번호가 아니라 본진 자리 번호다(2인은 대각선 자리, 항목 17B). 항목 25 점검에서 고침.
      if (areas[FactoryWarsUtil.SeatOf(f, building->Owner) % areas.Length].Distance(position) <= FP._0) {
        state->NextBaseAlertFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, FactoryWarsUtil.Rules(f).BaseAlertSeconds);
        f.Events.BaseUnderAttack(building->Owner, position);
      }
    }

    static void DamageArea(Frame f, in Weapon weapon, FPVector3 center, FP radius, FP amount) {
      f.Events.AreaImpact(center, radius);
      DepthSystem.BlastBridges(f, weapon.Owner, center, radius, amount);
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
      var barricades = f.Filter<Barricade>();
      while (barricades.NextUnsafe(out var entity, out var barricade)) {
        if (barricade->Shape.Distance(center) <= radius && IsValidTarget(f, weapon, entity)) {
          Damage(f, weapon, entity, amount);
        }
      }
      var map = FactoryWarsUtil.MapInfo(f);
      var cities = f.Filter<CityBuilding>();
      while (cities.NextUnsafe(out var entity, out var city)) {
        if (map.CityBuildings[city->Index].Shape.Distance(center) <= radius && IsValidTarget(f, weapon, entity)) {
          Damage(f, weapon, entity, amount);
        }
      }
    }

    static FP Falloff(FP distanceSqr, FP radius, FP edge) {
      if (radius <= FP._0) {
        return FP._1;
      }
      var ratio = FPMath.Clamp01(FPMath.Sqrt(FPMath.Max(FP._0, distanceSqr)) / radius);
      return FP._1 - (FP._1 - edge) * ratio;
    }

    /// <summary>
    /// 항목 33 전략 미사일: 중심에서 amount, 가장자리에서 amount x edge로 직선으로 줄어드는 광역 피해.
    /// 공중 유닛과 건물 안 병력은 맞지 않는다. friendlyFire면 관계와 상관없이 전부 맞는다. 건물·도시 건물에는 배율이 붙는다.
    /// </summary>
    public static void Blast(Frame f, in Weapon weapon, FPVector3 center, FP radius, FP amount, FP edge, FP buildingMultiplier, FP cityMultiplier, bool friendlyFire) {
      f.Events.AreaImpact(center, radius);
      DepthSystem.BlastBridges(f, weapon.Owner, center, radius, amount);
      var radiusSqr = radius * radius;
      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out var entity, out var transform, out var unit)) {
        var distance = FactoryWarsUtil.FlatDistanceSqr(transform->Position, center);
        if (distance > radiusSqr || unit->Garrison != EntityRef.None || f.FindAsset(unit->Spec).IsAir
            || (friendlyFire == false && DiplomacySystem.CanAttack(f, weapon.Owner, unit->Owner) == false)) {
          continue;
        }
        DamageUnit(f, weapon, entity, unit, amount * Falloff(distance, radius, edge));
      }
      var buildings = f.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out var entity, out var transform, out var building)) {
        var distance = FactoryWarsUtil.FlatDistanceSqr(transform->Position, center);
        if (distance > radiusSqr || (friendlyFire == false && DiplomacySystem.CanAttack(f, weapon.Owner, building->Owner) == false)) {
          continue;
        }
        Damage(f, weapon, entity, amount * Falloff(distance, radius, edge) * buildingMultiplier);
      }
      var barricades = f.Filter<Barricade>();
      while (barricades.NextUnsafe(out var entity, out var barricade)) {
        var distance = FPMath.Max(FP._0, barricade->Shape.Distance(center));
        if (distance <= radius) {
          BattlefieldSystem.HitBarricade(f, entity, barricade, amount * Falloff(distance * distance, radius, edge) * buildingMultiplier);
        }
      }
      var map = FactoryWarsUtil.MapInfo(f);
      var cities = f.Filter<CityBuilding>();
      while (cities.NextUnsafe(out var entity, out var city)) {
        var distance = FPMath.Max(FP._0, map.CityBuildings[city->Index].Shape.Distance(center));
        if (distance <= radius) {
          CitySystem.Hit(f, entity, city, amount * Falloff(distance * distance, radius, edge) * cityMultiplier);
        }
      }
    }

    static Weapon WeaponOf(PlayerRef owner, UnitSpec spec) {
      return new Weapon { Owner = owner, Attack = spec.Attack, IsAir = spec.IsAir, IsRanged = spec.IsRanged, Spec = spec };
    }

    /// <summary>항목 33 항모 드론: position에서 range 안의, 이 유닛 종류가 맞힐 수 있는 가장 가까운 적.</summary>
    public static EntityRef FindTargetFor(Frame f, PlayerRef owner, UnitSpec spec, FPVector3 position, FP range) {
      var weapon = WeaponOf(owner, spec);
      return FindTarget(f, weapon, position, FP._0, range);
    }

    /// <summary>target이 아직 살아 있고, 이 유닛 종류가 맞힐 수 있고, position에서 range 안이면 true.</summary>
    public static bool CanEngage(Frame f, PlayerRef owner, UnitSpec spec, EntityRef target, FPVector3 position, FP range) {
      if (f.Exists(target) == false || f.Unsafe.TryGetPointer<Transform3D>(target, out var transform) == false) {
        return false;
      }
      var weapon = WeaponOf(owner, spec);
      return IsValidTarget(f, weapon, target) && FactoryWarsUtil.FlatDistanceSqr(transform->Position, position) <= ReachSqr(f, target, range);
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
        DamageArea(f, weapon, position, spec.AbilityRadius, spec.Damage * spec.AbilityMultiplier * TechSystem.OutgoingMultiplier(f, unit->Owner, unit, spec));
      }
    }

    /// <summary>야전 정비(항목 31): 주변 아군 지상 차량(중·대형)의 체력을 초당 AbilityMultiplier만큼 채운다. 자기 자신은 빼고, 최대 AbilityCount대.</summary>
    static void FieldRepair(Frame f, EntityRef self, Unit* unit, UnitSpec spec, FPVector3 position) {
      var radiusSqr = spec.AbilityRadius * spec.AbilityRadius;
      var heal = spec.AbilityMultiplier * f.DeltaTime;
      var remaining = spec.AbilityCount > 0 ? spec.AbilityCount : 1;
      var units = f.Filter<Transform3D, Unit>();
      while (remaining > 0 && units.NextUnsafe(out var entity, out var transform, out var other)) {
        if (entity == self || other->Owner != unit->Owner || other->Health <= FP._0) {
          continue;
        }
        var otherSpec = f.FindAsset(other->Spec);
        if (otherSpec.IsAir || otherSpec.IsNaval || otherSpec.Size < UnitSize.Medium
            || FactoryWarsUtil.FlatDistanceSqr(transform->Position, position) > radiusSqr) {
          continue;
        }
        var max = TechSystem.MaxHealth(f, other->Owner, other->Spec);
        if (other->Health < max) {
          other->Health = FPMath.Min(max, other->Health + heal);
          remaining--;
        }
      }
    }

    static void Fire(Frame f, in Weapon weapon, Unit* unit, EntityRef target, FPVector3 position, FPVector3 targetPosition, FP range) {
      var spec = weapon.Spec;
      // 항목 31: 공격 연구와 정예화.
      var amount = spec.Damage * TechSystem.OutgoingMultiplier(f, unit->Owner, unit, spec) * TacticalSystem.VetDamage(f, unit);
      if (spec.Ability == SpecialAbility.FirstShotDouble) {
        if (f.Number >= unit->EngagedUntilFrame) {
          amount *= spec.AbilityMultiplier;
        }
        unit->EngagedUntilFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, spec.AbilitySeconds);
      }

      unit->AttackCooldown = spec.AttackInterval * TacticalSystem.VetReload(f, unit);
      unit->LastFiredFrame = f.Number;
      if (FactoryWarsUtil.MapInfo(f).HighGroundAt(position) >= 0) {
        FactoryWarsUtil.Player(f, unit->Owner)->HighGroundShots++;
      }
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
        if (spec.Ability == SpecialAbility.FieldRepair && unit->Health > FP._0) {
          FieldRepair(f, entity, unit, spec, transform->Position);
        }
        if (unit->Health <= FP._0 || spec.Attack == AttackType.None || spec.GarrisonFireOnly) {
          // 항목 34-C: 주둔 저격수는 SniperSystem이 따로 쏜다.
          continue;
        }
        unit->AttackCooldown = FPMath.Max(FP._0, unit->AttackCooldown - f.DeltaTime);

        var weapon = new Weapon {
          Owner = unit->Owner, Attack = spec.Attack, IsAir = spec.IsAir, IsRanged = spec.IsRanged, Unit = unit, Spec = spec,
          Origin = transform->Position, Area = spec.Attack == AttackType.Area || spec.Ability == SpecialAbility.FlameBurst,
          Ultimate = UltimateSystem.IsUltimateUnit(f, entity),
        };
        var range = EffectiveRange(f, unit, spec);
        if (unit->Garrison != EntityRef.None) {
          range = range * (FP._1 + FP._0_10 + FP._0_05) + FactoryWarsUtil.Rules(f).GarrisonRangeBonus;
          // 항목 33 벙커: 안의 분대는 건물 한가운데에 있지만 가장자리 총안에서 쏘는 것으로 친다(공격하는 쪽도 건물 가장자리까지만 재므로 서로 닿는다).
          if (f.Unsafe.TryGetPointer<Building>(unit->Garrison, out var host)) {
            range += f.FindAsset(host->Spec).FootprintRadius;
          }
        }
        var position = transform->Position;

        if (spec.Ability == SpecialAbility.CarpetBomb) {
          TryCarpetBomb(f, weapon, unit, position);
        }

        if (unit->TargetEntity != EntityRef.None && (IsValidTarget(f, weapon, unit->TargetEntity) == false || FactoryWarsUtil.IsVisibleTo(f, unit->Owner, unit->TargetEntity) == false)) {
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
        if (distance > ReachSqr(f, unit->TargetEntity, RangeTo(f, range, position, targetPosition)) || distance < spec.MinRange * spec.MinRange) {
          // 정지·위치 고수 상태에서 사거리를 벗어난 대상은 쫓지 않는다.
          if (unit->Order == UnitOrder.Stop || unit->Order == UnitOrder.Hold) {
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

        var weapon = new Weapon { Owner = building->Owner, Attack = spec.Attack, IsRanged = true, GroundOnly = spec.GroundOnly, Origin = transform->Position, Area = spec.Attack == AttackType.Area };
        var target = FindTarget(f, weapon, transform->Position, spec.MinRange, spec.Range);
        if (target != EntityRef.None) {
          building->TargetEntity = target;
          building->AttackCooldown = spec.AttackInterval;
          // 항목 32 포병 진지: 범위 피해.
          if (spec.SplashRadius > FP._0) {
            DamageArea(f, weapon, f.Unsafe.GetPointer<Transform3D>(target)->Position, spec.SplashRadius, spec.Damage);
          } else {
            Damage(f, weapon, target, spec.Damage);
          }
        }
      }
    }
  }

  /// <summary>지상은 Quantum NavMesh, 공중은 직선 이동.</summary>
  public unsafe class MovementSystem : SystemMainThread, ISignalOnNavMeshWaypointReached, ISignalOnNavMeshSearchFailed {
    public static void Order(Frame f, EntityRef entity, Unit* unit, UnitOrder order, FPVector3 target) {
      // 항목 32: 명령이 엄폐보다 우선한다. 공격 이동을 다시 받는 것(AI 파도)은 엄폐를 유지한다.
      if (order != UnitOrder.AttackMove || unit->Order != UnitOrder.AttackMove) {
        CoverSystem.Release(f, unit);
      }
      unit->Order = order;
      unit->TargetPosition = target;
      unit->TargetEntity = EntityRef.None;
      if (f.Unsafe.TryGetPointer<Worker>(entity, out var worker)) {
        worker->Task = WorkerTask.Idle;
      }
      SetDestination(f, entity, target);
    }

    public static void Stop(Frame f, EntityRef entity, Unit* unit) {
      CoverSystem.Release(f, unit);
      unit->Order = UnitOrder.Stop;
      unit->TargetEntity = EntityRef.None;
      if (f.Unsafe.TryGetPointer<Worker>(entity, out var worker)) {
        worker->Task = WorkerTask.Idle;
      }
      Halt(f, entity);
    }

    public static void SetDestination(Frame f, EntityRef entity, FPVector3 target) {
      if (f.Unsafe.TryGetPointer<NavMeshPathfinder>(entity, out var pathfinder)) {
        var navMesh = f.Map.GetNavMesh(FactoryWarsUtil.NavMeshNameOf(f, entity));
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
        } else if (unit->Order == UnitOrder.Patrol) {
          TurnPatrol(f, entity, unit);
        }
      }
    }

    /// <summary>순찰 끝에 닿으면 출발점과 목적지를 바꿔 되돌아간다.</summary>
    public static void TurnPatrol(Frame f, EntityRef entity, Unit* unit) {
      var next = unit->PatrolOrigin;
      unit->PatrolOrigin = unit->TargetPosition;
      unit->TargetPosition = next;
      SetDestination(f, entity, next);
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

      var map = FactoryWarsUtil.MapInfo(f);
      var rules = FactoryWarsUtil.Rules(f);
      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out var entity, out var transform, out var unit)) {
        if (unit->Garrison != EntityRef.None) {
          continue;
        }
        // 항목 32: 엄폐로 가는 중이거나 엄폐 중이면 엄폐가 이동을 맡는다.
        if (unit->Cover != EntityRef.None && CoverSystem.Steer(f, entity, transform, unit)) {
          continue;
        }
        var spec = f.FindAsset(unit->Spec);
        var hasDestination = unit->Order == UnitOrder.Move || unit->Order == UnitOrder.AttackMove || unit->Order == UnitOrder.Patrol;
        var destination = unit->TargetPosition;

        if (unit->TargetEntity != EntityRef.None && unit->Order != UnitOrder.Move
            && f.Unsafe.TryGetPointer<Transform3D>(unit->TargetEntity, out var targetTransform)) {
          var range = CombatSystem.RangeTo(f, CombatSystem.EffectiveRange(f, unit, spec), transform->Position, targetTransform->Position);
          if (FactoryWarsUtil.FlatDistanceSqr(targetTransform->Position, transform->Position) <= CombatSystem.ReachSqr(f, unit->TargetEntity, range)) {
            Halt(f, entity);
            continue;
          }
          if (unit->Order == UnitOrder.Stop || unit->Order == UnitOrder.Hold) {
            continue;
          }
          // 쫓는 경로는 1초마다 갱신한다.
          hasDestination = true;
          destination = f.Unsafe.TryGetPointer<Barricade>(unit->TargetEntity, out var barricade) ? BattlefieldSystem.FrontOf(barricade, transform->Position)
            : f.Unsafe.TryGetPointer<CityBuilding>(unit->TargetEntity, out var city) ? map.CityBuildings[city->Index].Shape.Approach(transform->Position, FP._2)
            : targetTransform->Position;
          if (spec.IsAir == false && f.Number % repathFrames == 0) {
            SetDestination(f, entity, destination);
          }
        } else if ((unit->Order == UnitOrder.AttackMove || unit->Order == UnitOrder.Patrol) && spec.IsAir == false
            && f.Unsafe.TryGetPointer<NavMeshPathfinder>(entity, out var pathfinder) && pathfinder->IsActive == false) {
          // 교전이 끝나면 원래 목적지로 재출발한다.
          SetDestination(f, entity, unit->TargetPosition);
        }

        if (spec.IsAir && hasDestination) {
          MoveAir(f, transform, unit, spec, destination);
        } else if (spec.IsAir == false && f.Unsafe.TryGetPointer<NavMeshSteeringAgent>(entity, out var agent)) {
          agent->MaxSpeed = GroundSpeed(f, map, rules, spec, unit, transform->Position);
        }
      }

      PushOutOfBuildings(f);
    }

    /// <summary>
    /// NavMesh는 고정이라 경로가 건물을 지나간다. 지상 유닛을 가까운 변 밖으로 밀어내고, 이동 중이면 변을 따라
    /// 미끄러뜨려 돌아가게 한다. 처음 닿을 때 고른 회전 방향을 유지해 붙어 있는 건물들도 한 덩어리처럼 돌아간다.
    /// 면에 닿은 동안 면을 따른 순 이동은 조종과 상관없이 최고 속도다. 조종을 빼지 않으면 반대로 당길 때 정확히 상쇄되어 멈춘다.
    /// </summary>
    static void PushOutOfBuildings(Frame f) {
      var rules = FactoryWarsUtil.Rules(f);
      var clearance = rules.UnitClearance;
      var memory = FactoryWarsUtil.SecondsToFrames(f, rules.SlideMemorySeconds);
      var units = f.Filter<Transform3D, Unit, NavMeshSteeringAgent, NavMeshPathfinder>();
      while (units.NextUnsafe(out _, out var unit, out var owner, out var agent, out var pathfinder)) {
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
          var moving = pathfinder->IsActive;
          var slide = agent->MaxSpeed * f.DeltaTime;
          var steered = agent->Velocity * f.DeltaTime;
          var position = unit->Position;
          var center = transform->Position;
          var target = pathfinder->Target;
          var remembered = f.Number < owner->SlideUntilFrame;
          if (overlapX < overlapZ) {
            var face = x < FP._0 ? -1 : 1;
            position.X = center.X + reach * face;
            // 면의 바깥 방향을 왼쪽으로 돌린 것이 회전 방향 +1의 진행 방향이다.
            if (remembered == false) {
              owner->SlideTurn = face * Detour(position.X, position.Z, target.X, target.Z, center.X + reach * face, center.Z, reach);
            } else {
              KeepDirection(owner, face);
            }
            owner->SlideFace = face;
            if (moving) {
              position.Z += slide * owner->SlideTurn * face - steered.Y;
            }
          } else {
            var face = z < FP._0 ? -1 : 1;
            position.Z = center.Z + reach * face;
            if (remembered == false) {
              owner->SlideTurn = -face * Detour(position.Z, position.X, target.Z, target.X, center.Z + reach * face, center.X, reach);
            } else {
              KeepDirection(owner, face * 2);
            }
            owner->SlideFace = face * 2;
            if (moving) {
              position.X -= slide * owner->SlideTurn * face + steered.X;
            }
          }
          owner->SlideUntilFrame = f.Number + memory;
          unit->Position = position;
        }
      }
    }

    /// <summary>마주 보는 면으로 넘어가면 같은 회전 방향이 반대로 간다. 부호를 뒤집어 가던 쪽을 유지한다. face는 x면 ±1, z면 ±2다.</summary>
    static void KeepDirection(Unit* owner, int face) {
      if (owner->SlideFace == -face) {
        owner->SlideTurn = -owner->SlideTurn;
      }
    }

    /// <summary>능력과 도로·여울을 합친 지상 이동 속도.</summary>
    public static FP GroundSpeed(Frame f, MapSpec map, GameRulesSpec rules, UnitSpec spec, Unit* unit, FPVector3 position) {
      var speed = unit->Swarming ? spec.MoveSpeed * spec.AbilityMultiplier : spec.MoveSpeed;
      // 항목 37 공군 타격 사령부: 정찰 유닛(탐지 반경이 있는 소형 차량) 이동 속도 증가.
      if (spec.DetectRadius > FP._0 && TacticalSystem.DoctrineOf(f, unit->Owner) == 1) {
        speed *= rules.ScoutSpeedMultiplier;
      }
      if (unit->StealthUntilFrame > f.Number) {
        speed *= spec.StealthSpeedMultiplier;
      }
      var city = map.CityAt(position);
      if (city >= 0 && CitySystem.IsCollapsed(f, city)) {
        return speed * rules.RubbleSpeedMultiplier;
      }
      var ground = map.GroundAt(position);
      return ground == MapSpec.GroundRoad ? speed * rules.RoadSpeedMultiplier : ground == MapSpec.GroundFord ? speed * rules.FordSpeedMultiplier : speed;
    }

    /// <summary>같은 유닛이 AbilityCount대 이상이면 이동 속도가 오른다. 1초마다 갱신한다.</summary>
    static void UpdateSwarmSpeed(Frame f) {
      var units = f.Filter<Unit>();
      while (units.NextUnsafe(out _, out var unit)) {
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
        unit->Swarming = count >= spec.AbilityCount;
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
        if (unit->Order == UnitOrder.Patrol) {
          if (destination.X == unit->TargetPosition.X && destination.Z == unit->TargetPosition.Z) {
            var next = unit->PatrolOrigin;
            unit->PatrolOrigin = unit->TargetPosition;
            unit->TargetPosition = next;
          }
        } else if (unit->Order != UnitOrder.AttackTarget) {
          unit->Order = UnitOrder.Stop;
        }
        return;
      }
      transform->Position += delta.Normalized * step;
    }
  }
}
