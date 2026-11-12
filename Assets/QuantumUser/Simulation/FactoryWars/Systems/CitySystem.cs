namespace Quantum.FactoryWars {
  using Photon.Deterministic;

  /// <summary>
  /// 도시 건물: 손상 단계, 붕괴, 쓰러짐, 진지. 위치는 MapSpec.CityBuildings, 수치는 GameRulesSpec에 있다.
  /// 건물 바닥과 낙하 구역은 각각 NavMesh 영역이다. 바닥은 처음에 닫혀 있다가 붕괴하면 열리고, 낙하 구역은 쓰러지면 닫힌다.
  /// </summary>
  public unsafe class CitySystem : SystemMainThread {
    public static void Init(Frame f) {
      var map = FactoryWarsUtil.MapInfo(f);
      var rules = FactoryWarsUtil.Rules(f);
      for (var i = 0; map.CityBuildings != null && i < map.CityBuildings.Length; i++) {
        var placement = map.CityBuildings[i];
        var entity = f.Create();
        f.Add(entity, Transform3D.Create(placement.Shape.Center));
        var health = rules.CityKinds[placement.Kind].Health;
        f.Add(entity, new CityBuilding { Index = i, Health = health, MaxHealth = health, Occupant = PlayerRef.None });
        if (f.Map.RegionMap.TryGetValue(placement.Region, out var region)) {
          f.NavMeshRegionMask->ToggleRegion(region, false);
        }
      }
    }

    public static bool IsCollapsed(Frame f, int index) => (f.Global->CityCollapsed[index >> 5] & (1 << (index & 31))) != 0;

    static void MarkCollapsed(Frame f, int index) {
      f.Global->CityCollapsed[index >> 5] |= 1 << (index & 31);
    }

    public static int StageOf(FP health, FP max, GameRulesSpec rules) {
      var ratio = health / max;
      return ratio > rules.CityCrackedFraction ? 0 : ratio > rules.CityBurningFraction ? 1 : 2;
    }

    /// <summary>소형 보병(분대). 항목 32: 소형 차량(IsVehicle)과 배는 뺀다(31에서 버기·오토바이가 진지에 들어갈 수 있었다).</summary>
    public static bool IsInfantry(UnitSpec spec) => spec.Size == UnitSize.Small && spec.IsAir == false && spec.IsNaval == false && spec.IsVehicle == false && spec.Attack != AttackType.None;

    public static int Occupants(Frame f, EntityRef building) {
      var count = 0;
      var units = f.Filter<Unit>();
      while (units.NextUnsafe(out _, out var unit)) {
        count += unit->Garrison == building ? 1 : 0;
      }
      return count;
    }

    static bool Friendly(Frame f, PlayerRef a, PlayerRef b) => a == b || DiplomacySystem.GetRelation(f, a, b) == Relation.Allied;

    /// <summary>들어갈 수 있으면 true. 진지 표시, 붕괴 안 됨, 빈자리, 주인이 없거나 동맹, 휴전 중에는 자기 본진 안.</summary>
    public static bool CanEnter(Frame f, PlayerRef player, EntityRef building) {
      if (f.Unsafe.TryGetPointer<CityBuilding>(building, out var city) == false) {
        return false;
      }
      var placement = FactoryWarsUtil.MapInfo(f).CityBuildings[city->Index];
      if (placement.Garrison == false || Occupants(f, building) >= FactoryWarsUtil.Rules(f).GarrisonCapacity) {
        return false;
      }
      if (city->Occupant.IsValid && Friendly(f, player, city->Occupant) == false) {
        return false;
      }
      return FactoryWarsUtil.IsReachable(f, player, placement.Shape.Center);
    }

    public override void Update(Frame f) {
      if (f.Global->GameEnded) {
        return;
      }
      var rules = FactoryWarsUtil.Rules(f);
      var map = FactoryWarsUtil.MapInfo(f);
      var reach = rules.GarrisonEnterDistance;
      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out var entity, out var transform, out var unit)) {
        if (unit->GarrisonTarget == EntityRef.None) {
          continue;
        }
        if (f.Unsafe.TryGetPointer<CityBuilding>(unit->GarrisonTarget, out var city) == false) {
          unit->GarrisonTarget = EntityRef.None;
          continue;
        }
        if (map.CityBuildings[city->Index].Shape.Distance(transform->Position) > reach) {
          continue;
        }
        var target = unit->GarrisonTarget;
        unit->GarrisonTarget = EntityRef.None;
        if (CanEnter(f, unit->Owner, target)) {
          Enter(f, entity, unit, target, city);
        } else {
          MovementSystem.Stop(f, entity, unit);
        }
      }
      KeepBotsOutOfFallZones(f, map, rules);
    }

    public static void Enter(Frame f, EntityRef entity, Unit* unit, EntityRef building, CityBuilding* city) {
      unit->Garrison = building;
      unit->Order = UnitOrder.Stop;
      unit->TargetEntity = EntityRef.None;
      f.Remove<NavMeshPathfinder>(entity);
      f.Remove<NavMeshSteeringAgent>(entity);
      f.Remove<NavMeshAvoidanceAgent>(entity);
      var center = f.Unsafe.GetPointer<Transform3D>(building)->Position;
      f.Unsafe.GetPointer<Transform3D>(entity)->Position = new FPVector3(center.X, FP._0, center.Z);
      if (city->Occupant.IsValid == false) {
        city->Occupant = unit->Owner;
      }
      FactoryWarsUtil.Player(f, unit->Owner)->GarrisonEntries++;
      f.Events.GarrisonChanged(building, city->Occupant, Occupants(f, building));
    }

    /// <summary>진지에서 나온다. 건물 둘레의 정해진 자리로 나오고, damageFraction만큼 최대 체력 피해를 입는다.</summary>
    public static void Exit(Frame f, EntityRef entity, Unit* unit, EntityRef building, MapShape shape, int slot, FP damageFraction) {
      unit->Garrison = EntityRef.None;
      var spec = f.FindAsset(unit->Spec);
      var angle = FP.PiTimes2 * slot / 6;
      var away = shape.Center + new FPVector3(FPMath.Cos(angle), FP._0, FPMath.Sin(angle)) * (shape.Reach + FP._3);
      var position = shape.Approach(away, FP._2);
      f.Unsafe.GetPointer<Transform3D>(entity)->Position = position;
      var config = f.FindAsset(f.SimulationConfig.Navigation.DefaultNavMeshAgent);
      f.Set(entity, NavMeshPathfinder.Create(f, entity, config));
      f.Add(entity, new NavMeshSteeringAgent());
      f.Add(entity, new NavMeshAvoidanceAgent());
      f.Unsafe.GetPointer<NavMeshSteeringAgent>(entity)->MaxSpeed = spec.MoveSpeed;
      if (f.Unsafe.TryGetPointer<CityBuilding>(building, out var city) && Occupants(f, building) == 0) {
        city->Occupant = PlayerRef.None;
      }
      if (f.Exists(building)) {
        var left = f.Unsafe.TryGetPointer<CityBuilding>(building, out var after) ? after->Occupant : PlayerRef.None;
        f.Events.GarrisonChanged(building, left, Occupants(f, building));
      }
      if (damageFraction > FP._0) {
        Hurt(f, entity, unit, spec.MaxHealth * damageFraction);
      }
    }

    public static void ExitAll(Frame f, PlayerRef player, EntityRef building, FP damageFraction) {
      if (f.Unsafe.TryGetPointer<CityBuilding>(building, out var city) == false) {
        return;
      }
      var shape = FactoryWarsUtil.MapInfo(f).CityBuildings[city->Index].Shape;
      var slot = 0;
      var units = f.Filter<Unit>();
      while (units.NextUnsafe(out var entity, out var unit)) {
        if (unit->Garrison == building && (player.IsValid == false || unit->Owner == player)) {
          Exit(f, entity, unit, building, shape, slot++, damageFraction);
        }
      }
    }

    /// <summary>무기 없이 입는 피해. 붕괴·깔림에 쓴다.</summary>
    static void Hurt(Frame f, EntityRef entity, Unit* unit, FP amount) {
      unit->Health -= amount;
      if (unit->Health <= FP._0) {
        f.Events.UnitDestroyed(entity, unit->Owner, unit->Spec);
        FactoryWarsUtil.Player(f, unit->Owner)->UnitsLost++;
        f.Destroy(entity);
      }
    }

    public static void Hit(Frame f, EntityRef entity, CityBuilding* city, FP amount) {
      if (city->Health <= FP._0) {
        return;
      }
      var rules = FactoryWarsUtil.Rules(f);
      city->Health -= amount;
      var stage = city->Health > FP._0 ? StageOf(city->Health, city->MaxHealth, rules) : 3;
      if (stage != city->Stage) {
        city->Stage = stage;
        f.Events.CityBuildingDamaged(entity, city->Index, stage);
      }
      if (city->Health <= FP._0) {
        Collapse(f, entity, city);
      }
    }

    static void Collapse(Frame f, EntityRef entity, CityBuilding* city) {
      var map = FactoryWarsUtil.MapInfo(f);
      var rules = FactoryWarsUtil.Rules(f);
      var index = city->Index;
      var placement = map.CityBuildings[index];
      MarkCollapsed(f, index);
      if (f.Map.RegionMap.TryGetValue(placement.Region, out var region)) {
        f.NavMeshRegionMask->ToggleRegion(region, true);
      }
      // 주변 지상 유닛이 무너지는 건물에 맞는다. 진지 병력은 이 피해 대신 퇴출 피해를 입는다.
      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out var unitEntity, out var transform, out var unit)) {
        if (unit->Garrison == EntityRef.None && f.FindAsset(unit->Spec).IsAir == false && placement.Shape.Distance(transform->Position) <= rules.CollapseRadius) {
          Hurt(f, unitEntity, unit, rules.CollapseDamage);
        }
      }
      ExitAll(f, PlayerRef.None, entity, rules.GarrisonEjectFraction);
      if (placement.Topples) {
        Topple(f, placement, rules);
        f.Events.CityBuildingToppled(index);
      }
      f.Events.CityBuildingCollapsed(index);
      f.Destroy(entity);
      Repath(f);
    }

    /// <summary>낙하 구역을 닫고, 그 안의 지상 유닛을 옆으로 밀어내며 피해를 준다. 밀어내는 쪽은 구역 짧은 축에서 가까운 쪽이다.</summary>
    static void Topple(Frame f, CityBuildingPlacement placement, GameRulesSpec rules) {
      if (f.Map.RegionMap.TryGetValue(placement.FallRegion, out var region)) {
        f.NavMeshRegionMask->ToggleRegion(region, false);
      }
      var zone = placement.FallZone;
      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out var entity, out var transform, out var unit)) {
        if (unit->Garrison != EntityRef.None || f.FindAsset(unit->Spec).IsAir || zone.Distance(transform->Position) > FP._0) {
          continue;
        }
        transform->Position = zone.PushOut(transform->Position, rules.TopplePushMargin);
        Hurt(f, entity, unit, rules.ToppleDamage);
      }
    }

    /// <summary>길이 바뀌었으니 이동 중인 지상 유닛의 경로를 다시 찾는다.</summary>
    static void Repath(Frame f) {
      var units = f.Filter<Unit, NavMeshPathfinder>();
      while (units.NextUnsafe(out var entity, out _, out var pathfinder)) {
        if (pathfinder->IsActive) {
          MovementSystem.SetDestination(f, entity, pathfinder->Target);
        }
      }
    }

    /// <summary>체력 30% 이하인 쓰러지는 건물의 낙하 구역에 서 있는 AI 유닛을 비킨다.</summary>
    static void KeepBotsOutOfFallZones(Frame f, MapSpec map, GameRulesSpec rules) {
      if (f.Number % FactoryWarsUtil.SecondsToFrames(f, FP._1) != 0) {
        return;
      }
      var buildings = f.Filter<CityBuilding>();
      while (buildings.NextUnsafe(out _, out var city)) {
        var placement = map.CityBuildings[city->Index];
        if (placement.Topples == false || city->Stage < 2) {
          continue;
        }
        var units = f.Filter<Transform3D, Unit>();
        while (units.NextUnsafe(out var entity, out var transform, out var unit)) {
          if (unit->Order == UnitOrder.Stop && FactoryWarsUtil.Player(f, unit->Owner)->IsBot && placement.FallZone.Distance(transform->Position) <= FP._0
              && f.Has<NavMeshPathfinder>(entity)) {
            MovementSystem.Order(f, entity, unit, UnitOrder.Move, placement.FallZone.PushOut(transform->Position, rules.TopplePushMargin));
          }
        }
      }
    }

    /// <summary>도시 건물 피해. 범위 공격과 화염방사(진지 건물)에 배율이 붙는다.</summary>
    public static FP Multiplier(Frame f, CityBuilding* city, AttackType attack, UnitSpec attacker) {
      var rules = FactoryWarsUtil.Rules(f);
      var value = CombatSystem.BuildingMultiplier(rules, attack);
      if (attack == AttackType.Area) {
        value *= rules.CityAreaMultiplier;
      }
      if (attacker != null && attacker.Ability == SpecialAbility.FlameBurst && FactoryWarsUtil.MapInfo(f).CityBuildings[city->Index].Garrison) {
        value *= rules.GarrisonFlameMultiplier;
      }
      return value;
    }
  }
}
