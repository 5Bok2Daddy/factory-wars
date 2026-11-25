namespace Quantum.FactoryWars {
  using Photon.Deterministic;

  /// <summary>건설·생산 대기열, 인구 집계.</summary>
  public unsafe class ProductionSystem : SystemMainThread {
    public static EntityRef CreateBuilding(Frame f, PlayerRef owner, AssetRef<BuildingSpec> specRef, FPVector3 position) {
      var spec = f.FindAsset(specRef);
      var site = default(MapShape);
      var hasWorks = AirfieldWorkSystem.Required(spec.Role) && AirfieldWorkSystem.TryFindSite(f, spec, position, owner, out site);
      var duration = spec.BuildTimeSeconds;
      if (hasWorks) duration += AirfieldWorkSystem.BuildSeconds(FactoryWarsUtil.Rules(f), spec.Role);
      var entity = f.Create();
      f.Add(entity, Transform3D.Create(position));
      f.Add(entity, new Building {
        Owner = owner,
        Spec = specRef,
        Health = spec.BuildTimeSeconds > FP._0 ? spec.MaxHealth * FactoryWarsUtil.Rules(f).FoundationHealthRatio : spec.MaxHealth,
        ConstructionRemaining = duration,
      });
      if (hasWorks) f.Add(entity, new AirfieldWorks { Site = site });
      if (spec.Role == BuildingRole.MlrsBattery) {
        f.Add(entity, new MlrsMagazine { Ammo = spec.RocketCapacity, Status = MlrsStatus.Disabled });
      }
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
      if (unitSpec != null && unitSpec.IsNaval && TryShipLaunchPoint(f, origin, source, index, out var water)) {
        var entity = SpawnUnitAt(f, owner, specRef, water, source);
        var direction = new FPVector3(water.Z - origin.Z, FP._0, origin.X - water.X);
        f.Unsafe.GetPointer<Transform3D>(entity)->Rotation = FPQuaternion.LookRotation(direction);
        return entity;
      }
      // 건물 주변에 골고루 놓는다.
      var angle = FP.PiTimes2 * (index % 8) / 8;
      var position = origin + new FPVector3(FPMath.Cos(angle), FP._0, FPMath.Sin(angle)) * FactoryWarsUtil.Rules(f).SpawnOffset;
      return SpawnUnitAt(f, owner, specRef, position, source);
    }

    public static bool TryShipLaunchPoint(Frame f, FPVector3 origin, Building* source, int index, out FPVector3 launch) {
      var radius = source != null ? f.FindAsset(source->Spec).FootprintRadius : FactoryWarsUtil.Rules(f).SpawnOffset;
      return TryShipLaunchPoint(f, origin, radius, index, out launch);
    }

    static bool TryShipLaunchPoint(Frame f, FPVector3 origin, FP radius, int index, out FPVector3 launch) {
      var rules = FactoryWarsUtil.Rules(f);
      var water = f.Map.GetNavMesh(rules.WaterNavMeshName);
      var map = FactoryWarsUtil.MapInfo(f);
      launch = default;
      if (water == null) return false;
      for (var ring = 0; ring < 3; ring++) {
        var distance = radius + rules.ShipSpawnGap + ring * rules.ShipLaunchClearance;
        var best = FP._0;
        for (var step = 0; step < 8; step++) {
          var angle = FP.PiTimes2 * (index + step) / 8;
          var point = origin + new FPVector3(FPMath.Cos(angle), FP._0, FPMath.Sin(angle)) * distance;
          if (water.Contains(point, NavMeshRegionMask.Default, true) == false || IsOpenWater(map, point, rules.ShipLaunchClearance) == false) continue;
          var depth = FP._0;
          foreach (var piece in map.River ?? System.Array.Empty<MapShape>()) {
            // 운하 조각끼리 이어진 단면에서 양쪽 물가까지의 거리를 잰다.
            if (piece.Distance(point) > FP._0) continue;
            var delta = point - piece.Center;
            var rad = piece.Yaw * FP.Deg2Rad;
            var across = FPMath.Abs(delta.X * FPMath.Sin(rad) + delta.Z * FPMath.Cos(rad));
            depth = FPMath.Max(depth, piece.HalfWidth - across);
          }
          foreach (var piece in map.Channel ?? System.Array.Empty<MapShape>())
            depth = FPMath.Max(depth, -piece.Distance(point));
          if (depth <= best) continue;
          best = depth;
          launch = point;
        }
        if (best >= rules.ShipLaunchClearance) return true;
      }
      launch = default;
      return false;
    }

    static bool IsOpenWater(MapSpec map, FPVector3 point, FP clearance) {
      foreach (var bridge in map.Bridges ?? System.Array.Empty<MapShape>())
        if (bridge.Distance(point) < clearance) return false;
      foreach (var gate in map.WaterGates ?? System.Array.Empty<MapShape>())
        if (gate.Distance(point) < clearance) return false;
      foreach (var road in map.Roads ?? System.Array.Empty<MapShape>())
        if (road.Distance(point) < clearance) return false;
      return true;
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
      if (TryResolveBuildPosition(f, spec, ref position, out var extractNode) == false) {
        f.Events.ActionRejected(player, ActionKind.Build, RejectReason.BlockedLocation, 0);
        return;
      }
      if (FactoryWarsUtil.IsReachable(f, player, position) == false) {
        f.Events.ActionRejected(player, ActionKind.Build, RejectReason.Truce, 0);
        return;
      }
      if (CanPlaceResolved(f, spec, position, player, extractNode) == false) {
        var zone = (spec.Role == BuildingRole.Shipyard || spec.NeedsWater) && SeaGateSystem.ShipyardZoneAt(f, position, player) < 0;
        f.Events.ActionRejected(player, ActionKind.Build, zone ? RejectReason.ShipyardZone : RejectReason.BlockedLocation, 0);
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
      if (spec.Role == BuildingRole.UltimateSite && CountRole(f, player, BuildingRole.UltimateSite) >= 1) {
        f.Events.ActionRejected(player, ActionKind.Build, RejectReason.Limit, 0);
        return;
      }
      var missing = state->Stock.Missing(spec.Cost);
      if (missing != MissingResource.None) {
        f.Events.ActionRejected(player, ActionKind.Build, RejectReason.InsufficientResources, (int)missing);
        return;
      }
      state->Stock -= spec.Cost;
      var created = CreateBuilding(f, player, specRef, position);
      if (extractNode != default) {
        f.Unsafe.GetPointer<Building>(created)->ExtractNode = extractNode;
      }
      WorkerSystem.Assign(f, engineer, WorkerTask.Build, created);
      if (spec.Role == BuildingRole.UltimateSite) {
        var ult = UltimateSystem.SpecOf(f, player);
        if (ult != null && ult.Kind != UltimateKind.StrategicMissile) {
          f.Events.UltimateNotice(player, UltimateStage.Started, ult.Kind, position, -FPMath.CeilToInt(spec.BuildTimeSeconds));
        }
      }
    }

    public static int CountRole(Frame f, PlayerRef player, BuildingRole role) {
      var count = 0;
      var buildings = f.Filter<Building>();
      while (buildings.NextUnsafe(out _, out var building)) {
        if (building->Owner == player && building->Health > FP._0 && f.FindAsset(building->Spec).Role == role) {
          count++;
        }
      }
      return count;
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

    /// <summary>맵 안, 걸어갈 수 있는 땅, 건물·자원·지형에서 떨어진 곳, 건설 금지 구역 밖에만 지을 수 있다. 전략 미사일 격납고는 점령한 중립 거점·암시장 18m 이내만 허용한다.</summary>
    /// <summary>Task 46: why a placement is refused, for the ghost tooltip (Invalid = generic blocked).</summary>
    public static RejectReason PlacementReason(Frame f, BuildingSpec spec, FPVector3 position, PlayerRef player) {
      if (TryGetBuildPlacement(f, spec, position, player, out _)) return RejectReason.InsufficientResources;
      if ((spec.Role == BuildingRole.Shipyard || spec.NeedsWater) && SeaGateSystem.ShipyardZoneAt(f, position, player) < 0) return RejectReason.ShipyardZone;
      return RejectReason.BlockedLocation;
    }

    public static bool CanPlace(Frame f, BuildingSpec spec, FPVector3 position, PlayerRef player = default) {
      return TryGetBuildPlacement(f, spec, position, player, out _);
    }

    public static bool TryGetBuildPlacement(Frame f, BuildingSpec spec, FPVector3 position, PlayerRef player, out FPVector3 alignedPosition) {
      alignedPosition = position;
      return TryResolveBuildPosition(f, spec, ref alignedPosition, out var extractNode)
          && (extractNode == default || FactoryWarsUtil.IsReachable(f, player, alignedPosition))
          && CanPlaceResolved(f, spec, alignedPosition, player, extractNode);
    }

    static bool TryResolveBuildPosition(Frame f, BuildingSpec spec, ref FPVector3 position, out EntityRef extractNode) {
      extractNode = default;
      if (spec.RequiresResourceNode == false) {
        return true;
      }
      var mapRadius = FactoryWarsUtil.MapInfo(f).ExtractorSnapRadius;
      var radius = mapRadius > FP._0 ? mapRadius : FactoryWarsUtil.Rules(f).ExtractorNodeSnapRadius;
      var bestDistance = radius * radius;
      var nodes = f.Filter<ResourceNode>();
      var alignedPosition = position;
      while (nodes.NextUnsafe(out var entity, out var node)) {
        var center = node->Shape.Center;
        var distance = FactoryWarsUtil.FlatDistanceSqr(position, center);
        if (node->Kind != spec.RequiredNodeKind || node->Remaining <= FP._0 || distance > bestDistance) {
          continue;
        }
        if (extractNode != default && distance == bestDistance && entity.Index >= extractNode.Index) {
          continue;
        }
        extractNode = entity;
        alignedPosition = new FPVector3(center.X, FP._0, center.Z);
        bestDistance = distance;
      }
      position = alignedPosition;
      return extractNode != default;
    }

    static bool CanPlaceResolved(Frame f, BuildingSpec spec, FPVector3 position, PlayerRef player, EntityRef extractNode) {
      var map = FactoryWarsUtil.MapInfo(f);
      var rules = FactoryWarsUtil.Rules(f);
      var radius = spec.FootprintRadius;
      if (position.X - radius < map.BoundsMin.X || position.X + radius > map.BoundsMax.X
          || position.Z - radius < map.BoundsMin.Y || position.Z + radius > map.BoundsMax.Y) {
        return false;
      }
      // 보급 상자 위에는 짓지 못한다. 건물 점유 사각형 안으로는 유닛이 들어가지 못해 상자를 영영 줍지 못했다.
      var crateReach = radius + rules.CratePickRadius;
      var crates = f.Filter<Transform3D, SupplyCrate>();
      while (crates.NextUnsafe(out _, out var crateAt, out _)) {
        if (GroundOccupancy.Overlaps(crateAt->Position, position, crateReach)) {
          return false;
        }
      }

      var isShipyard = spec.Role == BuildingRole.Shipyard || spec.NeedsWater;
      var navMesh = f.Map.GetNavMesh(rules.NavMeshName);
      var mask = NavMeshRegionMask.Default;
      if (extractNode != EntityRef.None) {
        // 자원 노드 자체는 NavMesh 장애물이다. 공병이 작업 범위까지 도달할 수 있어야 한다.
        var workRadius = FPMath.Max(radius, f.Get<ResourceNode>(extractNode).Shape.Reach);
        if (FactoryWarsUtil.FindNavPointNear(f, rules.NavMeshName, position, workRadius + rules.WorkRange, out _) == false) {
          return false;
        }
      } else if (isShipyard) {
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
        if ((extractNode != default && building->ExtractNode == extractNode)
            || GroundOccupancy.Overlaps(position, transform->Position, radius + f.FindAsset(building->Spec).FootprintRadius + rules.BuildingGap)) {
          return false;
        }
      }
      var works = f.Filter<AirfieldWorks>();
      while (works.NextUnsafe(out _, out var airfield)) {
        if (AirfieldWorkSystem.IntersectsBuilding(airfield->Site, position, radius, rules.BuildingGap + rules.AirfieldWorkClearance)) return false;
      }
      // 고갈된 노드도 그대로 남아 있어 같은 검사를 받는다.
      var nodes = f.Filter<ResourceNode>();
      while (nodes.NextUnsafe(out var entity, out var node)) {
        if (entity != extractNode && IsClear(node->Shape, position, radius, rules.ResourceBuildMargin) == false) {
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
      // Task 46: shipyards only inside MapSpec.ShipyardZones (own sector coast, canal banks) when the map defines zones.
      if (isShipyard && SeaGateSystem.ShipyardZoneAt(f, position, player) < 0) {
        return false;
      }
      if (isShipyard) {
        if (map.Channel != null) {
          for (var i = 0; i < map.Channel.Length; i++) {
            if (map.Channel[i].Distance(position) <= waterMargin) {
              touchesWater = true;
            }
          }
        }
        if (touchesWater == false
            || (spec.Role == BuildingRole.Shipyard && TryShipLaunchPoint(f, position, radius, 0, out _) == false)) {
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
      if (spec.RequiresNeutralOutpost && player.IsValid) {
        var ult = UltimateSystem.SpecOf(f, player);
        var outpostRadius = ult != null && ult.SiloOutpostRadius > FP._0 ? ult.SiloOutpostRadius : (FP)18;
        if (UltimateSystem.IsNearNeutralOutpost(f, player, position, outpostRadius) == false) {
          return false;
        }
      }
      return AirfieldWorkSystem.Required(spec.Role) == false || AirfieldWorkSystem.TryFindSite(f, spec, position, player, out _);
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
      // Task 46: a shipyard on a canal bank builds small vessels only (warships cannot pass the low canal bridges).
      if (SeaGateSystem.TooTallForYard(f, f.Unsafe.GetPointer<Transform3D>(entity)->Position, unit)) {
        f.Events.ActionRejected(player, ActionKind.Produce, RejectReason.CanalTooNarrow, 0);
        return;
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

    static void UpdateExtractor(Frame f, EntityRef entity, Building* building) {
      if (building->Health <= FP._0 || building->ConstructionRemaining > FP._0
          || building->Paused != PauseReason.None || building->EmpUntilFrame > f.Number
          || f.Unsafe.TryGetPointer<ResourceNode>(building->ExtractNode, out var node) == false
          || node->Remaining <= FP._0) {
        return;
      }
      var occupants = CitySystem.Occupants(f, entity);
      var yields = FactoryWarsUtil.Rules(f).ExtractorYieldMultipliers;
      var spec = f.FindAsset(building->Spec);
      if (occupants == 0 || yields == null || occupants >= yields.Length || spec.ExtractIntervalSeconds <= FP._0) {
        return;
      }
      building->ExtractTimer += f.DeltaTime * SniperSystem.GatherMultiplier(f, building->ExtractNode);
      while (building->ExtractTimer >= spec.ExtractIntervalSeconds && node->Remaining > FP._0) {
        building->ExtractTimer -= spec.ExtractIntervalSeconds;
        var amount = FPMath.Min(node->Remaining, spec.ExtractAmountPerCycle * yields[occupants]);
        if (amount <= FP._0) {
          return;
        }
        node->Remaining -= amount;
        var state = FactoryWarsUtil.Player(f, building->Owner);
        if (node->Kind == ResourceKind.Wood) {
          state->Stock.Wood += amount;
          state->Earned.Wood += amount;
        } else if (node->Kind == ResourceKind.Iron) {
          state->Stock.Iron += amount;
          state->Earned.Iron += amount;
        }
      }
    }

    public override void Update(Frame f) {
      if (f.Global->GameEnded) {
        return;
      }
      UpdateTotals(f);
      FactorySystem.UpdateOverdriveHeat(f);

      var buildings = f.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out var entity, out var transform, out var building)) {
        if (building->ExtractNode != EntityRef.None) {
          UpdateExtractor(f, entity, building);
        }
        // 건설은 공병이 진행한다 (WorkerSystem). 정전·EMP 마비 중에는 진행도를 유지한 채 멈춘다.
        if (building->ConstructionRemaining > FP._0 || building->QueueCount == 0 || building->Paused != PauseReason.None || building->EmpUntilFrame > f.Number) {
          continue;
        }

        var state = FactoryWarsUtil.Player(f, building->Owner);
        var unit = f.FindAsset(building->Queue[0].Unit);
        var buildTime = UnitProductionSeconds(f, state, unit);
        var speedMul = FactorySystem.ProductionSpeedMultiplier(f, entity, building, state);

        building->QueueProgress += f.DeltaTime * speedMul;
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
        FPVector3[] groundSlots = null;
        if (!unit.IsAir && !unit.IsNaval) {
          groundSlots = new FPVector3[unit.SpawnCount];
          if (!GroundOccupancy.TryProductionSlots(f, unit, building, transform->Position, groundSlots)) continue;
        }
        for (var i = 0; i < unit.SpawnCount; i++) {
          var spawned = groundSlots != null
            ? SpawnUnitAt(f, building->Owner, building->Queue[0].Unit, groundSlots[i], building)
            : SpawnUnit(f, building->Owner, building->Queue[0].Unit, transform->Position, i, building);
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

    public static FP UnitProductionSeconds(Frame f, PlayerState* state, UnitSpec unit) {
      var buildTime = unit.BuildTimeSeconds;
      if (unit.Size == UnitSize.Small) {
        buildTime *= f.FindAsset(state->Faction).SmallBuildTimeMultiplier;
      }
      return buildTime;
    }

    /// <summary>생산 타이머가 도는 중이면 true. 인구가 차서 완성 상태로 대기 중이면 false.</summary>
    public static bool IsProducing(Frame f, Building* building, PlayerState* state) {
      return building->QueueCount > 0 && building->QueueProgress < UnitProductionSeconds(f, state, f.FindAsset(building->Queue[0].Unit));
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
    /// <summary>기동 대공차·패트리어트 포대: 공중 항모 대상 +50% 추가 대공 피해.</summary>
    public bool AntiCarrierBonus;

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
      // 지상 조립 중인 공중 항모 선체는 이륙 전까지 지상군 공격 대상이 된다.
      if (f.Unsafe.TryGetPointer<Carrier>(target, out var carrier) && carrier->Assembling) {
        return false;
      }
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
      if (f.Has<MercGuard>(target)) {
        return MercenaryCamp.IsValidGuardTarget(f, target) && weapon.Attack != AttackType.AntiAir;
      }
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
      if (f.Unsafe.TryGetPointer<Unit>(target, out var garrisoned) && (garrisoned->Garrison != EntityRef.None || garrisoned->Transport != EntityRef.None)) {
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
      if (unit->Health <= FP._0 || unit->Transport != EntityRef.None) {
        return;
      }
      var spec = f.FindAsset(unit->Spec);
      DiplomacySystem.NoteAttack(f, weapon.Owner, unit->Owner);
      amount *= UnitMultiplier(FactoryWarsUtil.Rules(f), weapon.Attack, spec);
      // 대전차 로켓병: 전차·차량 대상 +60% 추가 피해.
      if (weapon.Ability == SpecialAbility.AntiArmorBonus && spec.IsAir == false && spec.IsNaval == false
          && (spec.IsVehicle || spec.Size >= UnitSize.Medium)) {
        amount *= weapon.Spec.AbilityMultiplier;
      }
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
      // 항목 31: 방어 연구. 항목 32: 엄폐. 진영 패시브·특화 연구 피격 보정. 주둔 보병/저격수 -25% 피해 감소(화염 돌격병은 엄폐·주둔 무시 +50% 추가 피해).
      amount *= TechSystem.IncomingMultiplier(f, unit->Owner, spec);
      amount *= TechSystem.SpecialIncomingMultiplier(f, target, unit, spec);
      if (unit->Garrison != EntityRef.None) {
        if (weapon.Ability == SpecialAbility.GarrisonBreach) {
          amount *= weapon.Spec.AbilityMultiplier;
        } else if (weapon.IgnoreCover == false) {
          amount *= FP._0_75;
        }
      }
      if (weapon.IgnoreCover == false) {
        amount *= CoverSystem.DamageMultiplier(f, unit, weapon.Origin, weapon.Area);
        // 항목 37 그림자 특수전단: 엄폐 중 받는 피해가 더 준다.
        if (unit->InCover && TacticalSystem.DoctrineOf(f, unit->Owner) == 2) {
          amount *= FP._1 - FactoryWarsUtil.Rules(f).ShadowCoverBonus;
        }
      }
      // 항목 33: 공중 항모는 대공 무기에 더 크게 맞는다(카운터). 기동 대공차·패트리어트 포대는 +50% 추가 항모 저격 피해.
      if (weapon.Attack == AttackType.AntiAir && f.Has<Carrier>(target)) {
        var ultimate = UltimateSystem.SpecOf(f, unit->Owner);
        if (ultimate != null) {
          amount *= ultimate.AntiAirTakenMultiplier;
        }
        if (weapon.AntiCarrierBonus) {
          amount *= FP._1 + FP._0_50;
        }
      }
      unit->Health -= ModuleSystem.ApplyIncoming(f, unit, amount);
      unit->LastDamagedFrame = f.Number;

      if (unit->Health <= FP._0) {
        var deadPos = f.Unsafe.GetPointer<Transform3D>(target)->Position;
        TransportSystem.OnTransportDestroyed(f, target, spec, deadPos);
        UltimateSystem.OnUnitKilled(f, target, unit);
        f.Events.UnitDestroyed(target, unit->Owner, unit->Spec);
        f.Events.Wrecked(deadPos, (int)spec.Size, FP._0);
        DepthSystem.LeaveWreck(f, deadPos, spec, unit->Spec);
        FactoryWarsUtil.Player(f, unit->Owner)->UnitsLost++;
        var killer = FactoryWarsUtil.Player(f, weapon.Owner);
        if (killer != null) {
          killer->UnitsKilled++;
        }
        TechSystem.OnEnemyDestroyed(f, weapon.Owner, unit->Owner);
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
        amount *= TechSystem.StructureDamageMultiplier(f, weapon.Owner);
        if (weapon.Ability == SpecialAbility.FlameBurst || weapon.Ability == SpecialAbility.GarrisonBreach) {
          amount *= weapon.Spec.AbilityMultiplier;
        }
        // 화염 돌격병: 벙커 등 주둔 건물 공격 시 내부 주둔 보병에도 직접 관통 화염 피해(+50%).
        if (weapon.Ability == SpecialAbility.GarrisonBreach) {
          var garrisonUnits = f.Filter<Unit>();
          while (garrisonUnits.NextUnsafe(out var gEntity, out var gUnit)) {
            if (gUnit->Garrison == target && gUnit->Health > FP._0 && DiplomacySystem.CanAttack(f, weapon.Owner, gUnit->Owner)) {
              DamageUnit(f, weapon, gEntity, gUnit, amount);
            }
          }
        }
        amount *= DepthSystem.ProtocolFactor(f, building);
        // 건설 중(<100%) 궁극 전력 전용 건물(전략 미사일 격납고 등)은 받는 피해 +30% 취약.
        var bSpec = f.FindAsset(building->Spec);
        if (bSpec.Role == BuildingRole.UltimateSite && building->ConstructionRemaining > FP._0) {
          var ult = UltimateSystem.SpecOf(f, building->Owner);
          var vuln = ult != null && ult.ConstructionVulnerabilityMultiplier > FP._0 ? ult.ConstructionVulnerabilityMultiplier : (FP)130 / (FP)100;
          amount *= vuln;
        }
        if (f.Number >= FactoryWarsUtil.SecondsToFrames(f, (FP)540)) {
          amount *= f.Number >= FactoryWarsUtil.SecondsToFrames(f, (FP)780) ? (FP)7 / (FP)2 : (FP)2;
        }
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
          if (lost.GarrisonCapacity > 0 || lost.RequiresResourceNode) {
            CitySystem.ExitAll(f, PlayerRef.None, target, FactoryWarsUtil.Rules(f).GarrisonEjectFraction);
          }
          var destroyedPos = f.Unsafe.GetPointer<Transform3D>(target)->Position;
          var destroyedOwner = building->Owner;
          f.Events.Wrecked(destroyedPos, -1, lost.FootprintRadius);
          f.Destroy(target);
          // 군수·연료 보급창 파괴 시 연쇄 대폭발(주변 건물·유닛 광역 피해).
          if (lost.Role == BuildingRole.SupplyDepot) {
            FactorySystem.TriggerDepotExplosion(f, target, destroyedPos, destroyedOwner);
          }
        }
      } else if (f.Unsafe.TryGetPointer<Barricade>(target, out var barricade)) {
        BattlefieldSystem.HitBarricade(f, target, barricade, amount * BuildingMultiplier(FactoryWarsUtil.Rules(f), weapon.Attack));
      } else if (f.Unsafe.TryGetPointer<CityBuilding>(target, out var city)) {
        var cityDmg = amount * CitySystem.Multiplier(f, city, weapon.Attack, weapon.Spec);
        if (weapon.Ability == SpecialAbility.GarrisonBreach) {
          cityDmg *= weapon.Spec.AbilityMultiplier;
          var garrisonUnits = f.Filter<Unit>();
          while (garrisonUnits.NextUnsafe(out var gEntity, out var gUnit)) {
            if (gUnit->Garrison == target && gUnit->Health > FP._0 && DiplomacySystem.CanAttack(f, weapon.Owner, gUnit->Owner)) {
              DamageUnit(f, weapon, gEntity, gUnit, amount);
            }
          }
        }
        CitySystem.Hit(f, target, city, cityDmg);
      } else if (f.Unsafe.TryGetPointer<NpcTrader>(target, out var trader)) {
        NpcTraderSystem.Hit(f, target, trader, weapon.Owner, amount * BuildingMultiplier(FactoryWarsUtil.Rules(f), weapon.Attack));
      } else if (f.Unsafe.TryGetPointer<MercGuard>(target, out var guard)) {
        MercenaryCamp.HitGuard(f, target, guard, weapon.Owner, amount);
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

    public static void ApplyMlrsImpact(Frame f, in MlrsRocket rocket) {
      var weapon = new Weapon {
        Owner = rocket.Owner, Attack = AttackType.Area, Area = true, GroundOnly = true,
        IsRanged = true, Origin = rocket.Origin,
      };
      DamageArea(f, weapon, rocket.Target, rocket.BlastRadius, rocket.Damage, rocket.EdgeDamage);
    }

    static void DamageArea(Frame f, in Weapon weapon, FPVector3 center, FP radius, FP amount, FP? edge = null) {
      f.Events.AreaImpact(center, radius);
      DepthSystem.BlastBridges(f, weapon.Owner, center, radius, amount);
      var radiusSqr = radius * radius;
      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out var entity, out var transform, out var unit)) {
        var distance = FactoryWarsUtil.FlatDistanceSqr(transform->Position, center);
        if (distance <= radiusSqr && IsValidTarget(f, weapon, entity) && (!edge.HasValue || !f.FindAsset(unit->Spec).IsNaval)) {
          Damage(f, weapon, entity, edge.HasValue ? amount * Falloff(distance, radius, edge.Value) : amount);
        }
      }
      var buildings = f.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out var entity, out var transform, out _)) {
        var distance = FactoryWarsUtil.FlatDistanceSqr(transform->Position, center);
        if (distance <= radiusSqr && IsValidTarget(f, weapon, entity)) {
          Damage(f, weapon, entity, edge.HasValue ? amount * Falloff(distance, radius, edge.Value) : amount);
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

    static void Fire(Frame f, EntityRef attacker, in Weapon weapon, Unit* unit, EntityRef target, FPVector3 position, FPVector3 targetPosition, FP range) {
      var spec = weapon.Spec;
      // 항목 31: 공격 연구와 정예화.
      var amount = spec.Damage * TechSystem.OutgoingMultiplier(f, unit->Owner, unit, spec) * TacticalSystem.VetDamage(f, unit);
      if (spec.Ability == SpecialAbility.FirstShotDouble) {
        if (f.Number >= unit->EngagedUntilFrame) {
          amount *= spec.AbilityMultiplier;
        }
        unit->EngagedUntilFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, spec.AbilitySeconds);
      }

      unit->AttackCooldown = spec.AttackInterval * TacticalSystem.VetReload(f, unit)
        * TechSystem.AttackCooldownMultiplier(f, unit->Owner, spec, position)
        * FactorySystem.ArtilleryCooldownMultiplier(f, unit->Owner, spec);
      unit->LastFiredFrame = f.Number;
      if (FactoryWarsUtil.MapInfo(f).HighGroundAt(position) >= 0) {
        FactoryWarsUtil.Player(f, unit->Owner)->HighGroundShots++;
      }
      if (spec.Ability == SpecialAbility.MultiAirTarget) {
        FireMultiAir(f, weapon, target, position, range, amount);
      } else if (spec.FireWhileMoving && spec.Attack == AttackType.Area) {
        // 기동형 MLS 궁극기: 이동 중에도 거치 없이 포물선 로켓 일제 사격(배기·궤적·착탄 VFX 연동).
        var splash = spec.SplashRadius * TechSystem.SplashRadiusMultiplier(f, unit->Owner, spec);
        MlrsSystem.LaunchMobileRocket(f, attacker, unit->Owner, position, targetPosition, amount, splash);
      } else if (spec.Ability == SpecialAbility.RocketSalvo) {
        // 다연장 로켓포차: 4발 결정론적 스플래시 로켓 일제 사격.
        var splash = spec.SplashRadius * TechSystem.SplashRadiusMultiplier(f, unit->Owner, spec);
        var spread = FP._1 + FP._0_20;
        var salvo = spec.AbilityCount > 0 ? spec.AbilityCount : 4;
        for (var r = 0; r < salvo; r++) {
          var ox = r == 0 ? -spread : r == 1 ? spread : FP._0;
          var oz = r == 2 ? -spread : r == 3 ? spread : FP._0;
          DamageArea(f, weapon, targetPosition + new FPVector3(ox, FP._0, oz), splash, amount);
        }
      } else if (spec.SplashRadius > FP._0) {
        var splash = spec.SplashRadius * TechSystem.SplashRadiusMultiplier(f, unit->Owner, spec);
        DamageArea(f, weapon, targetPosition, splash, amount);
      } else {
        Damage(f, weapon, target, amount);
      }
      if (spec.SelfDestructOnAttack) {
        unit->Health = FP._0;
      }
    }

    public override void Update(Frame f) {
      if (f.Global->GameEnded) {
        return;
      }

      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out var entity, out var transform, out var unit)) {
        if (unit->Transport != EntityRef.None) {
          continue;
        }
        var spec = f.FindAsset(unit->Spec);
        if (spec.Ability == SpecialAbility.FieldRepair && unit->Health > FP._0) {
          FieldRepair(f, entity, unit, spec, transform->Position);
        }
        if (unit->Health <= FP._0 || spec.Attack == AttackType.None || spec.GarrisonFireOnly
            || (f.Unsafe.TryGetPointer<Carrier>(entity, out var assemblingCarrier) && assemblingCarrier->Assembling)) {
          // 항목 34-C: 주둔 저격수는 SniperSystem이 따로 쏜다. 조립 중인 공중 항모는 사격 불가.
          continue;
        }
        unit->AttackCooldown = FPMath.Max(FP._0, unit->AttackCooldown - f.DeltaTime);

        var weapon = new Weapon {
          Owner = unit->Owner, Attack = spec.Attack, IsAir = spec.IsAir, IsRanged = spec.IsRanged, Unit = unit, Spec = spec,
          Origin = transform->Position,
          Area = spec.Attack == AttackType.Area || spec.Ability == SpecialAbility.FlameBurst || spec.Ability == SpecialAbility.GarrisonBreach,
          IgnoreCover = spec.Ability == SpecialAbility.GarrisonBreach,
          Ultimate = UltimateSystem.IsUltimateUnit(f, entity),
          AntiCarrierBonus = spec.Ability == SpecialAbility.CarrierHunter,
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

        // 기동 대공차(CarrierHunter): 이동 중에도 공중 항모·공중 표적을 자동 추적하며 기동 사격한다.
        if (spec.Ability == SpecialAbility.CarrierHunter && unit->Order != UnitOrder.AttackTarget) {
          var air = FindTarget(f, weapon, position, spec.MinRange, range, airOnly: true);
          if (air != EntityRef.None) {
            unit->TargetEntity = air;
          }
        }
        // 이동 명령 중에는 자동 조준하지 않는다(기동 대공차 및 FireWhileMoving 기동형 MLS 제외).
        if (unit->Order != UnitOrder.Move && unit->Order != UnitOrder.AttackTarget && spec.Ability == SpecialAbility.AirPriority) {
          var air = FindTarget(f, weapon, position, spec.MinRange, range, airOnly: true);
          if (air != EntityRef.None) {
            unit->TargetEntity = air;
          }
        }
        if (unit->TargetEntity == EntityRef.None && (unit->Order != UnitOrder.Move || spec.FireWhileMoving)) {
          unit->TargetEntity = FindTarget(f, weapon, position, spec.MinRange, range);
        }
        if (unit->TargetEntity == EntityRef.None || unit->AttackCooldown > FP._0) {
          continue;
        }

        var targetPosition = f.Unsafe.GetPointer<Transform3D>(unit->TargetEntity)->Position;
        var distance = FactoryWarsUtil.FlatDistanceSqr(targetPosition, position);
        if (distance > ReachSqr(f, unit->TargetEntity, RangeTo(f, range, position, targetPosition)) || distance < spec.MinRange * spec.MinRange) {
          // 정지·위치 고수 상태에서 사거리를 벗어난 대상은 쫓지 않는다. 이동 중 기동 사격 유닛도 사거리 밖이면 타겟 해제.
          if (unit->Order == UnitOrder.Stop || unit->Order == UnitOrder.Hold || (unit->Order == UnitOrder.Move && spec.FireWhileMoving)) {
            unit->TargetEntity = EntityRef.None;
          }
          continue;
        }

        Fire(f, entity, weapon, unit, unit->TargetEntity, position, targetPosition, range);
      }

      UpdateTowers(f);
    }

    void UpdateTowers(Frame f) {
      var rules = FactoryWarsUtil.Rules(f);
      var buildings = f.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out var entity, out var transform, out var building)) {
        var spec = f.FindAsset(building->Spec);
        if (spec.Attack == AttackType.None || spec.Role == BuildingRole.MlrsBattery || building->Health <= FP._0
            || building->ConstructionRemaining > FP._0 || building->EmpUntilFrame > f.Number) {
          continue;
        }
        var state = FactoryWarsUtil.Player(f, building->Owner);
        var step = state->Outage ? f.DeltaTime / rules.OutageTowerIntervalMultiplier : f.DeltaTime;
        building->AttackCooldown = FPMath.Max(FP._0, building->AttackCooldown - step);
        if (building->AttackCooldown > FP._0) {
          continue;
        }

        var weapon = new Weapon {
          Owner = building->Owner, Attack = spec.Attack, IsRanged = true, GroundOnly = spec.GroundOnly, Origin = transform->Position,
          Area = spec.Attack == AttackType.Area, AntiCarrierBonus = spec.Role == BuildingRole.PatriotTurret,
        };
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
      target = FactoryWarsUtil.MapInfo(f).ClampToPlayable(target);
      // Task 39: without an agreement nobody is ordered into the mercenary camp (the order stops at the gate).
      target = MercenaryCamp.ClampEntry(f, unit->Owner, target);
      // 항목 32: 명령이 엄폐보다 우선한다. 공격 이동을 다시 받는 것(AI 파도)은 엄폐를 유지한다.
      if (order != UnitOrder.AttackMove || unit->Order != UnitOrder.AttackMove) {
        CoverSystem.Release(f, unit);
      }
      unit->Order = order;
      unit->TargetPosition = target;
      unit->TargetEntity = EntityRef.None;
      unit->BoardTarget = EntityRef.None;
      unit->GarrisonTarget = EntityRef.None;
      if (f.Unsafe.TryGetPointer<Worker>(entity, out var worker)) {
        worker->Task = WorkerTask.Idle;
      }
      var spec = f.FindAsset(unit->Spec);
      if (order == UnitOrder.Move && !spec.IsAir && !spec.IsNaval) {
        if (!GroundOccupancy.TryGoal(f, target, UnitRadius(spec), out var goal)) {
          unit->Order = UnitOrder.Stop;
          Halt(f, entity);
          return;
        }
        unit->TargetPosition = goal;
        RouteMove(f, entity, unit);
        return;
      }
      SetDestination(f, entity, target);
    }

    public static void Stop(Frame f, EntityRef entity, Unit* unit) {
      CoverSystem.Release(f, unit);
      unit->Order = UnitOrder.Stop;
      unit->TargetEntity = EntityRef.None;
      unit->BoardTarget = EntityRef.None;
      unit->GarrisonTarget = EntityRef.None;
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

    static void RouteMove(Frame f, EntityRef entity, Unit* unit) {
      var target = unit->TargetPosition;
      var from = f.Get<Transform3D>(entity).Position;
      var radius = UnitRadius(f.FindAsset(unit->Spec));
      if (GroundOccupancy.TryDetour(f, from, target, radius, out var corner)) target = corner;
      SetDestination(f, entity, target);
    }

    public static void Halt(Frame f, EntityRef entity) {
      if (f.Unsafe.TryGetPointer<NavMeshPathfinder>(entity, out var pathfinder) && pathfinder->IsActive) {
        pathfinder->Stop(f, entity, true);
      }
    }

    public void OnNavMeshWaypointReached(Frame f, EntityRef entity, FPVector3 waypoint, Navigation.WaypointFlag waypointFlags, ref bool resetAgent) {
      if ((waypointFlags & Navigation.WaypointFlag.Target) != 0 && f.Unsafe.TryGetPointer<Unit>(entity, out var unit)) {
        // Task 46: a ship that reached the closed gate of its own sector keeps its order; SeaGateSystem re-routes it when the gate opens.
        if (f.Unsafe.TryGetPointer<Transform3D>(entity, out var at) && SeaGateSystem.WaitsForGate(f, at, unit)) return;
        if (unit->Order == UnitOrder.Move) {
          var spec = f.FindAsset(unit->Spec);
          if (!spec.IsAir && !spec.IsNaval && FactoryWarsUtil.FlatDistanceSqr(waypoint, unit->TargetPosition) > FP._1
              && f.Unsafe.TryGetPointer<NavMeshPathfinder>(entity, out var pathfinder)
              && FactoryWarsUtil.FlatDistanceSqr(pathfinder->Target, unit->TargetPosition) > FP._1) {
            resetAgent = false;
            RouteMove(f, entity, unit);
            return;
          }
        }
        if (unit->Order == UnitOrder.Move || unit->Order == UnitOrder.AttackMove) {
          unit->Order = UnitOrder.Stop;
        } else if (unit->Order == UnitOrder.Patrol) {
          TurnPatrol(f, entity, unit);
        }
      }
    }

    /// <summary>
    /// Task 46 (2026-11-24): the building check looks at the leg the unit is actually driving (the NavMesh path's current corner),
    /// not the straight line to the final target. With the straight line, a truck whose path leaves the base around the HQ
    /// was sent back to an HQ corner every second and never left its dock.
    /// </summary>
    static FPVector3 NextCorner(Frame f, NavMeshPathfinder* path, FPVector3 target) {
      return path->WaypointIndex >= 0 && path->WaypointIndex < path->WaypointCount ? path->GetWaypoint(f, path->WaypointIndex) : target;
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
        if (f.Unsafe.TryGetPointer<Transform3D>(entity, out var at) && SeaGateSystem.WaitsForGate(f, at, unit)) return;
        unit->Order = UnitOrder.Stop;
      }
    }

    public override void Update(Frame f) {
      if (f.Global->GameEnded) {
        return;
      }
      TransportSystem.Update(f);
      var repathFrames = FactoryWarsUtil.SecondsToFrames(f, FP._1);
      if (f.Number % repathFrames == 0) {
        UpdateSwarmSpeed(f);
      }

      var map = FactoryWarsUtil.MapInfo(f);
      var rules = FactoryWarsUtil.Rules(f);
      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out var entity, out var transform, out var unit)) {
        transform->Position = map.ClampToPlayable(transform->Position);
        if (unit->Garrison != EntityRef.None || unit->Transport != EntityRef.None || (f.Unsafe.TryGetPointer<Carrier>(entity, out var assemblingCarrier) && assemblingCarrier->Assembling)) {
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
          MoveAir(f, entity, transform, unit, spec, map.ClampToPlayable(destination));
        } else if (spec.IsAir == false && f.Unsafe.TryGetPointer<NavMeshSteeringAgent>(entity, out var agent)) {
          agent->MaxSpeed = GroundSpeed(f, map, rules, spec, unit, transform->Position);
          if (!spec.IsNaval && unit->Order == UnitOrder.Move && f.Number % repathFrames == 0
              && f.Unsafe.TryGetPointer<NavMeshPathfinder>(entity, out var moving) && moving->IsActive
              && FactoryWarsUtil.FlatDistanceSqr(moving->Target, unit->TargetPosition) <= FP._1
              && GroundOccupancy.TryDetour(f, transform->Position, NextCorner(f, moving, unit->TargetPosition), UnitRadius(spec), out var corner)) {
            SetDestination(f, entity, corner);
          }
        }
      }

      SeparateUnits(f);
      PushOutOfBuildings(f);
      PushHeavyArmorOutOfRubble(f);
    }

    /// <summary>붕괴 잔해 통행 규칙(Rubble Passage Rule): 보병은 통과·엄폐 가능하지만 대형 중전차(Large)는 붕괴 잔해 외곽으로 밀려나 진입 차단.</summary>
    static void PushHeavyArmorOutOfRubble(Frame f) {
      var map = FactoryWarsUtil.MapInfo(f);
      if (map.CityBuildings == null || map.CityBuildings.Length == 0) {
        return;
      }
      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out var entity, out var transform, out var unit)) {
        if (unit->Garrison != EntityRef.None || unit->Transport != EntityRef.None || unit->Health <= FP._0) {
          continue;
        }
        if (f.Unsafe.TryGetPointer<Worker>(entity, out var wk) && (wk->Task == WorkerTask.Build || wk->Task == WorkerTask.Repair)) {
          continue;
        }
        var spec = f.FindAsset(unit->Spec);
        if (spec.IsAir || spec.IsNaval || spec.Size < UnitSize.Large) {
          continue;
        }
        for (var i = 0; i < map.CityBuildings.Length; i++) {
          if (CitySystem.IsCollapsed(f, i) == false) {
            continue;
          }
          var shape = map.CityBuildings[i].Shape;
          if (shape.Distance(transform->Position) <= FP._0_50) {
            var delta = transform->Position - shape.Center;
            delta.Y = FP._0;
            var dir = delta.SqrMagnitude > FP._0_01 ? delta.Normalized : new FPVector3(FP._1, FP._0, FP._0);
            var edge = FPMath.Max(shape.HalfLength, shape.HalfWidth) + FP._1;
            transform->Position = new FPVector3(shape.Center.X + dir.X * edge, transform->Position.Y, shape.Center.Z + dir.Z * edge);
            Halt(f, entity);
            break;
          }
        }
      }
    }

    /// <summary>유닛 데이터 기반 충돌·분리·대형 반경(m). 보병 0.65, 경차량 1.4, 중형 전차/MLRS 2.2, 대형 중전차 2.6.</summary>
    public static FP UnitRadius(UnitSpec spec) {
      if (spec != null && spec.ColliderRadius > FP._0) {
        return spec.ColliderRadius;
      }
      if (spec == null) {
        return (FP)65 / (FP)100;
      }
      if (spec.Size == UnitSize.Large) {
        return (FP)26 / (FP)10;
      }
      if (spec.Size == UnitSize.Medium) {
        return (FP)22 / (FP)10;
      }
      return spec.IsVehicle ? (FP)14 / (FP)10 : (FP)65 / (FP)100;
    }

    /// <summary>결정론적 반경 기반 안티-클럼핑: 두 유닛의 ColliderRadius 합 + 여유(차량 1.0m, 보병 0.5m) 이내로 겹치면 2패스로 강하게 분리한다.</summary>
    static void SeparateUnits(Frame f) {
      var map = FactoryWarsUtil.MapInfo(f);
      var navMesh = f.Map.GetNavMesh(FactoryWarsUtil.Rules(f).NavMeshName);
      var mask = *f.NavMeshRegionMask;
      var step = (FP)14 * f.DeltaTime;
      var list = new System.Collections.Generic.List<(EntityRef Entity, FPVector3 Pos, FP Radius)>(64);
      var filter = f.Filter<Transform3D, Unit>();
      while (filter.NextUnsafe(out var entity, out var transform, out var unit)) {
        if (unit->Garrison != EntityRef.None || unit->Transport != EntityRef.None || unit->Health <= FP._0) {
          continue;
        }
        if (f.Unsafe.TryGetPointer<Worker>(entity, out var wk) && (wk->Task == WorkerTask.Build || wk->Task == WorkerTask.Repair)) {
          continue;
        }
        var spec = f.FindAsset(unit->Spec);
        if (spec.IsAir || spec.IsNaval) {
          continue;
        }
        list.Add((entity, transform->Position, UnitRadius(spec)));
      }
      var count = list.Count;
      for (var pass = 0; pass < 2; pass++) {
        for (var i = 0; i < count; i++) {
          var a = list[i];
          var push = FPVector3.Zero;
          var maxOverlap = FP._0;
          for (var j = 0; j < count; j++) {
            if (i == j) {
              continue;
            }
            var b = list[j];
            var pad = (a.Radius >= FP._1_50 || b.Radius >= FP._1_50) ? FP._1 : FP._0_50;
            var minDist = a.Radius + b.Radius + pad;
            var dx = a.Pos.X - b.Pos.X;
            var dz = a.Pos.Z - b.Pos.Z;
            var distSqr = dx * dx + dz * dz;
            var minSqr = minDist * minDist;
            if (distSqr >= minSqr) {
              continue;
            }
            if (distSqr <= FP._0_01) {
              // 정확히 같은 좌표에 겹친 경우 엔티티별 고유 각도 기반 결정론적 방사형 분리 벡터.
              var angle = FP.PiTimes2 * i / System.Math.Max(1, count);
              push += new FPVector3(FPMath.Cos(angle), FP._0, FPMath.Sin(angle));
              maxOverlap = FPMath.Max(maxOverlap, minDist * FP._0_50);
            } else {
              var dist = FPMath.Sqrt(distSqr);
              var pen = minDist - dist;
              maxOverlap = FPMath.Max(maxOverlap, pen * FP._0_50);
              push += new FPVector3(dx / dist, FP._0, dz / dist) * (pen / minDist + FP._0_25);
            }
          }
          if (push.X == FP._0 && push.Z == FP._0) {
            continue;
          }
          var mag = FPMath.Sqrt(push.X * push.X + push.Z * push.Z);
          var moveDist = FPMath.Min(FPMath.Max(step, maxOverlap), step * (FP)2);
          var delta = mag > FP._0_01 ? push / mag * moveDist : FPVector3.Zero;
          var next = new FPVector3(
            FPMath.Clamp(a.Pos.X + delta.X, map.BoundsMin.X + FP._1, map.BoundsMax.X - FP._1),
            a.Pos.Y,
            FPMath.Clamp(a.Pos.Z + delta.Z, map.BoundsMin.Y + FP._1, map.BoundsMax.Y - FP._1));
          if (navMesh == null || navMesh.Contains(next, mask, true)) {
            f.Unsafe.GetPointer<Transform3D>(a.Entity)->Position = next;
            list[i] = (a.Entity, next, a.Radius);
          }
        }
      }
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
        var spec = f.FindAsset(owner->Spec);
        var unitClearance = spec.IsNaval ? clearance : FPMath.Max(clearance, UnitRadius(spec));
        var buildings = f.Filter<Transform3D, Building>();
        while (buildings.NextUnsafe(out _, out var transform, out var building)) {
          if (building->Health <= FP._0) continue;
          var reach = f.FindAsset(building->Spec).FootprintRadius + unitClearance;
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
      speed *= TechSystem.MoveSpeedMultiplier(f, unit->Owner, spec, position);
      speed *= FactorySystem.VehicleSpeedMultiplier(f, unit->Owner, spec);
      speed *= UltimateSystem.ScorchedSpeedMultiplier(f, unit->Owner, position);
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

    static FP AabbDistanceXZ(FPVector3 pos, FPVector3 center, FP half) {
      var dx = FPMath.Max(FP._0, FPMath.Abs(pos.X - center.X) - half);
      var dz = FPMath.Max(FP._0, FPMath.Abs(pos.Z - center.Z) - half);
      if (dx > FP._0 || dz > FP._0) return new FPVector2(dx, dz).Magnitude;
      return -FPMath.Min(half - FPMath.Abs(pos.X - center.X), half - FPMath.Abs(pos.Z - center.Z));
    }

    static FPVector3 PushOutsideAabbXZ(FPVector3 pos, FPVector3 center, FP half, FP clearance) {
      var limit = half + clearance;
      var rx = pos.X - center.X;
      var rz = pos.Z - center.Z;
      var penX = limit - FPMath.Abs(rx);
      var penZ = limit - FPMath.Abs(rz);
      if (penX <= FP._0 || penZ <= FP._0) return pos;
      if (penX <= penZ) {
        pos.X = center.X + (rx >= FP._0 ? limit : -limit);
      } else {
        pos.Z = center.Z + (rz >= FP._0 ? limit : -limit);
      }
      return pos;
    }

    static bool HasCellGrid(MapSpec map) =>
      map != null && map.CellGridSize > 0 && map.CellGrid != null && map.CellGrid.Length == map.CellGridSize * map.CellGridSize;

    static bool FindNearestAirBuilding(Frame f, MapSpec map, FPVector3 probe, FP clearance, out FPVector3 obstacleCenter, out FP obstacleDist) {
      obstacleCenter = default;
      obstacleDist = FP.MaxValue;
      var found = false;

      // 1. Standing CityBuildings (non-grid maps)
      var buildings = map != null ? map.CityBuildings : null;
      if (buildings != null) {
        for (var i = 0; i < buildings.Length; i++) {
          if (CitySystem.IsCollapsed(f, i)) continue;
          var shape = buildings[i].Shape;
          var d = shape.Distance(probe);
          if (d < clearance && d < obstacleDist) {
            obstacleDist = d;
            obstacleCenter = shape.Center;
            found = true;
          }
        }
      }

      // 2. Tall urban building cells ('H') on CellGrid maps (e.g. Map4P v4).
      // Barriers, barbed wire, parks/trees ('K'), bridges ('G'), and low props are NOT 'H', so air units fly freely over them.
      if (HasCellGrid(map)) {
        var n = map.CellGridSize;
        var cell = map.CellGridCell;
        var halfCell = cell * FP._0_50;
        var mapHalf = cell * n * FP._0_50;
        var c0 = FPMath.FloorToInt((probe.X + mapHalf) / cell);
        var r0 = FPMath.FloorToInt((mapHalf - probe.Z) / cell);
        var rMin = r0 - 2 < 0 ? 0 : r0 - 2;
        var rMax = r0 + 2 >= n ? n - 1 : r0 + 2;
        var cMin = c0 - 2 < 0 ? 0 : c0 - 2;
        var cMax = c0 + 2 >= n ? n - 1 : c0 + 2;
        for (var r = rMin; r <= rMax; r++) {
          for (var c = cMin; c <= cMax; c++) {
            if (map.CellGrid[r * n + c] != (byte)'H') continue;
            var center = new FPVector3(-mapHalf + cell * (c + FP._0_50), probe.Y, mapHalf - cell * (r + FP._0_50));
            var d = AabbDistanceXZ(probe, center, halfCell);
            if (d < clearance && d < obstacleDist) {
              obstacleDist = d;
              obstacleCenter = center;
              found = true;
            }
          }
        }
      }

      return found;
    }

    static FPVector3 ClampOutsideAirBuildings(Frame f, MapSpec map, FPVector3 pos, FP clearance) {
      var buildings = map != null ? map.CityBuildings : null;
      if (buildings != null) {
        for (var i = 0; i < buildings.Length; i++) {
          if (CitySystem.IsCollapsed(f, i)) continue;
          var shape = buildings[i].Shape;
          if (shape.Distance(pos) < clearance) {
            var outside = shape.Approach(pos, clearance);
            pos.X = outside.X;
            pos.Z = outside.Z;
          }
        }
      }
      if (HasCellGrid(map)) {
        var n = map.CellGridSize;
        var cell = map.CellGridCell;
        var halfCell = cell * FP._0_50;
        var mapHalf = cell * n * FP._0_50;
        for (var pass = 0; pass < 2; pass++) {
          var c0 = FPMath.FloorToInt((pos.X + mapHalf) / cell);
          var r0 = FPMath.FloorToInt((mapHalf - pos.Z) / cell);
          var rMin = r0 - 1 < 0 ? 0 : r0 - 1;
          var rMax = r0 + 1 >= n ? n - 1 : r0 + 1;
          var cMin = c0 - 1 < 0 ? 0 : c0 - 1;
          var cMax = c0 + 1 >= n ? n - 1 : c0 + 1;
          for (var r = rMin; r <= rMax; r++) {
            for (var c = cMin; c <= cMax; c++) {
              if (map.CellGrid[r * n + c] != (byte)'H') continue;
              var center = new FPVector3(-mapHalf + cell * (c + FP._0_50), pos.Y, mapHalf - cell * (r + FP._0_50));
              if (AabbDistanceXZ(pos, center, halfCell) < clearance) {
                pos = PushOutsideAabbXZ(pos, center, halfCell, clearance);
              }
            }
          }
        }
      }
      return pos;
    }

    static void MoveAir(Frame f, EntityRef entity, Transform3D* transform, Unit* unit, UnitSpec spec, FPVector3 destination) {
      destination.Y = transform->Position.Y;
      var map = FactoryWarsUtil.MapInfo(f);
      var clearance = UnitRadius(spec) + FP._1;

      // If destination lies inside a standing tall building, snap destination cleanly to its perimeter.
      destination = ClampOutsideAirBuildings(f, map, destination, clearance);

      var pos = transform->Position;
      var delta = destination - pos;
      delta.Y = FP._0;
      var step = spec.MoveSpeed * TechSystem.MoveSpeedMultiplier(f, unit->Owner, spec, pos)
        * UltimateSystem.CarrierSpeedMultiplier(f, entity, unit) * f.DeltaTime;
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

      var distToDest = delta.Magnitude;
      var dir = delta / distToDest;
      var steerDir = dir;

      // Height-layered obstacle avoidance: air units fly freely over barriers, fences, trees, and low terrain,
      // while actively steering around standing tall buildings ('H' cells / CityBuildings) so they never clip through meshes.
      var lookAhead = FPMath.Min(distToDest, FPMath.Max(step * 8, clearance + FP._6));
      var midProbe = pos + dir * (lookAhead * FP._0_50);
      var farProbe = pos + dir * lookAhead;
      FPVector3 blockerCenter = default;
      FP closestDist = FP.MaxValue;
      var hasBlocker = false;
      if (FindNearestAirBuilding(f, map, pos, clearance, out var cNow, out var dNow)) {
        blockerCenter = cNow; closestDist = dNow; hasBlocker = true;
      } else if (FindNearestAirBuilding(f, map, midProbe, clearance, out var cMid, out var dMid)) {
        blockerCenter = cMid; closestDist = dMid; hasBlocker = true;
      } else if (FindNearestAirBuilding(f, map, farProbe, clearance, out var cFar, out var dFar)) {
        blockerCenter = cFar; closestDist = dFar; hasBlocker = true;
      }

      if (hasBlocker) {
        var away = pos - blockerCenter;
        away.Y = FP._0;
        var outward = away.SqrMagnitude > FP._0_01 ? away.Normalized : new FPVector3(-dir.Z, FP._0, dir.X);
        var tanA = new FPVector3( outward.Z, FP._0, -outward.X);
        var tanB = new FPVector3(-outward.Z, FP._0,  outward.X);
        var dotA = FPVector3.Dot(tanA, dir);
        var dotB = FPVector3.Dot(tanB, dir);
        FPVector3 tangent;
        if (FPMath.Abs(dotA - dotB) > FP._0_25) {
          // Rounding a corner or angled approach: follow the wall tangent that makes forward progress toward destination.
          tangent = dotA >= dotB ? tanA : tanB;
        } else {
          // Head-on approach to a flat wall: scan perpendicular to travel direction to pick the shorter open corner without ping-pong.
          var perpLeft  = new FPVector3(-dir.Z, FP._0,  dir.X);
          var perpRight = new FPVector3( dir.Z, FP._0, -dir.X);
          var clearLeft = 6;
          var clearRight = 6;
          var scanOrigin = closestDist < clearance ? pos : farProbe;
          for (var k = 1; k <= 5; k++) {
            var distK = (FP)(k * 6);
            if (clearLeft == 6 && !FindNearestAirBuilding(f, map, scanOrigin + perpLeft * distK, clearance, out _, out _)) {
              clearLeft = k;
            }
            if (clearRight == 6 && !FindNearestAirBuilding(f, map, scanOrigin + perpRight * distK, clearance, out _, out _)) {
              clearRight = k;
            }
          }
          tangent = clearLeft <= clearRight ? perpLeft : perpRight;
        }
        var blended = closestDist < clearance
          ? tangent * FP._0_75 + outward * FP._0_25
          : dir * FP._0_25 + tangent * FP._0_75;
        if (blended.SqrMagnitude > FP._0_01) {
          steerDir = blended.Normalized;
        }
      }

      var nextPos = pos + steerDir * step;
      nextPos.Y = pos.Y;

      // Hard perimeter safety guard: never allow an airborne unit center inside a standing tall building.
      nextPos = ClampOutsideAirBuildings(f, map, nextPos, clearance);
      transform->Position = nextPos;
    }
  }
}
