namespace Quantum.FactoryWars {
  using Photon.Deterministic;

  /// <summary>
  /// 항목 32 보병 엄폐. 엄폐점(CoverSpot)은 모래주머니 진지·벙커(건물 데이터 CoverSpots), 바리케이드 앞뒤, 도시 건물 네 변에 생긴다.
  /// 교전 중인 소형 보병은 반경 CoverSearchRadius 안에서 적 쪽을 막는 빈 점으로 가서 사격한다. 플레이어 이동·정지·순찰·위치 고수 명령이 엄폐보다 우선한다.
  /// 판정: 공격자 방향과 엄폐 방향의 내적이 CoverFacingCos 이상이면 피해를 Reduction만큼 줄인다(광역·화염은 CoverAreaFactor배).
  /// 엄폐물(건물·바리케이드·도시 건물)이 부서지면 점이 사라지고 보병이 풀린다. 수치는 GameRulesSpec·BuildingSpec에만 있다.
  /// </summary>
  public unsafe class CoverSystem : SystemMainThread {
    public static bool CanUseCover(UnitSpec spec) => CitySystem.IsInfantry(spec);

    static EntityRef CreateSpot(Frame f, FPVector3 position, FPVector2 facing, FP reduction, EntityRef source) {
      var entity = f.Create();
      f.Add(entity, Transform3D.Create(position));
      f.Add(entity, new CoverSpot { Position = position, Facing = facing.Normalized, Reduction = reduction, Source = source });
      return entity;
    }

    static FPVector2 Rotate(FPVector2 v, FP degrees) {
      var r = degrees * FP.Deg2Rad;
      var c = FPMath.Cos(r);
      var s = FPMath.Sin(r);
      return new FPVector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }

    /// <summary>모양(사각형)의 네 변 바깥에 점을 둔다. 방향은 모양 쪽(그 너머의 적을 막는다).</summary>
    static void ShapeSpots(Frame f, MapShape shape, FP reduction, EntityRef source, bool twoSides) {
      var rules = FactoryWarsUtil.Rules(f);
      var gap = rules.CoverOffset;
      var radius = shape.Radius > FP._0;
      var halfX = radius ? shape.Radius : shape.HalfLength;
      var halfZ = radius ? shape.Radius : shape.HalfWidth;
      for (var side = 0; side < 4; side++) {
        if (twoSides && side % 2 == 0) {
          continue;
        }
        var normal = side == 0 ? new FPVector2(1, 0) : side == 1 ? new FPVector2(0, 1) : side == 2 ? new FPVector2(-1, 0) : new FPVector2(0, -1);
        var reach = (side % 2 == 0 ? halfX : halfZ) + gap;
        var n = Rotate(normal, -shape.Yaw);
        var position = shape.Center + new FPVector3(n.X, FP._0, n.Y) * reach;
        position.Y = FP._0;
        CreateSpot(f, position, -n, reduction, source);
      }
    }

    void MakeMapSpots(Frame f) {
      var rules = FactoryWarsUtil.Rules(f);
      var map = FactoryWarsUtil.MapInfo(f);
      var barricades = f.Filter<Barricade>();
      while (barricades.NextUnsafe(out var entity, out var barricade)) {
        ShapeSpots(f, barricade->Shape, rules.CoverHighReduction, entity, true);
      }
      var cities = f.Filter<CityBuilding>();
      while (cities.NextUnsafe(out var entity, out var city)) {
        if (map.CityBuildings != null && city->Index < map.CityBuildings.Length) {
          ShapeSpots(f, map.CityBuildings[city->Index].Shape, rules.CoverHighReduction, entity, false);
        }
      }
    }

    /// <summary>완공된 엄폐 건물(모래주머니·벙커): 네 변 바로 바깥, 방향은 바깥(건물이 앞의 흉벽이다).</summary>
    static void MakeBuildingSpots(Frame f, EntityRef entity, Building* building, BuildingSpec spec) {
      var rules = FactoryWarsUtil.Rules(f);
      var center = f.Unsafe.GetPointer<Transform3D>(entity)->Position;
      var reach = spec.FootprintRadius + rules.UnitClearance + rules.CoverOffset;
      for (var i = 0; i < spec.CoverSpots; i++) {
        var angle = FP.PiTimes2 * i / spec.CoverSpots;
        var n = new FPVector2(FPMath.Cos(angle), FPMath.Sin(angle));
        var position = center + new FPVector3(n.X, FP._0, n.Y) * reach;
        CreateSpot(f, position, n, spec.CoverReduction, entity);
      }
      building->CoverMade = true;
    }

    public static void Release(Frame f, Unit* unit) {
      if (unit->Cover != EntityRef.None && f.Unsafe.TryGetPointer<CoverSpot>(unit->Cover, out var spot)) {
        spot->Occupant = EntityRef.None;
      }
      unit->Cover = EntityRef.None;
      unit->InCover = false;
    }

    /// <summary>엄폐 피해 배율. 엄폐 중이고 공격자가 막는 쪽에 있으면 1 - Reduction(광역은 절반 효과).</summary>
    public static FP DamageMultiplier(Frame f, Unit* unit, FPVector3 origin, bool area) {
      if (unit->InCover == false || f.Unsafe.TryGetPointer<CoverSpot>(unit->Cover, out var spot) == false) {
        return FP._1;
      }
      var rules = FactoryWarsUtil.Rules(f);
      var toAttacker = new FPVector2(origin.X - spot->Position.X, origin.Z - spot->Position.Z);
      if (toAttacker.SqrMagnitude <= FP._0) {
        return FP._1;
      }
      if (FPVector2.Dot(toAttacker.Normalized, spot->Facing) < rules.CoverFacingCos) {
        return FP._1;
      }
      var reduction = area ? spot->Reduction * rules.CoverAreaFactor : spot->Reduction;
      return FP._1 - reduction;
    }

    static bool Blocks(CoverSpot* spot, FPVector3 enemy, FP cos) {
      var dir = new FPVector2(enemy.X - spot->Position.X, enemy.Z - spot->Position.Z);
      return dir.SqrMagnitude > FP._0 && FPVector2.Dot(dir.Normalized, spot->Facing) >= cos;
    }

    /// <summary>MovementSystem이 부른다. 엄폐로 가는 중이거나 엄폐 중이면 true(이동 처리를 대신했다).</summary>
    public static bool Steer(Frame f, EntityRef entity, Transform3D* transform, Unit* unit) {
      if (unit->Cover == EntityRef.None) {
        return false;
      }
      if (f.Unsafe.TryGetPointer<CoverSpot>(unit->Cover, out var spot) == false || spot->Occupant != entity) {
        Release(f, unit);
        return false;
      }
      var rules = FactoryWarsUtil.Rules(f);
      // 적이 사라지면 엄폐를 푼다.
      if (unit->TargetEntity == EntityRef.None || f.Unsafe.TryGetPointer<Transform3D>(unit->TargetEntity, out var target) == false) {
        Release(f, unit);
        return false;
      }
      if (Blocks(spot, target->Position, rules.CoverFacingCos) == false) {
        Release(f, unit);
        return false;
      }
      var distance = FactoryWarsUtil.FlatDistanceSqr(transform->Position, spot->Position);
      if (unit->InCover == false) {
        if (distance <= rules.CoverArrival * rules.CoverArrival) {
          unit->InCover = true;
          FactoryWarsUtil.Player(f, unit->Owner)->CoverEntries++;
          MovementSystem.Halt(f, entity);
        } else if (f.Number % FactoryWarsUtil.SecondsToFrames(f, FP._1) == 0) {
          MovementSystem.SetDestination(f, entity, spot->Position);
        }
        return true;
      }
      // 엄폐 자리에서 쏠 수 없으면(대상이 사거리 밖) 풀고 평소대로 쫓는다.
      var spec = f.FindAsset(unit->Spec);
      var range = CombatSystem.RangeTo(f, CombatSystem.EffectiveRange(f, unit, spec), transform->Position, target->Position);
      if (FactoryWarsUtil.FlatDistanceSqr(target->Position, transform->Position) > CombatSystem.ReachSqr(f, unit->TargetEntity, range)) {
        Release(f, unit);
        return false;
      }
      MovementSystem.Halt(f, entity);
      return true;
    }

    public override void Update(Frame f) {
      if (f.Global->GameEnded) {
        return;
      }
      var rules = FactoryWarsUtil.Rules(f);
      if (f.Global->CoverReady == false) {
        f.Global->CoverReady = true;
        MakeMapSpots(f);
      }
      var buildings = f.Filter<Building>();
      while (buildings.NextUnsafe(out var entity, out var building)) {
        if (building->CoverMade == false && building->ConstructionRemaining <= FP._0) {
          var spec = f.FindAsset(building->Spec);
          if (spec.CoverSpots > 0) {
            MakeBuildingSpots(f, entity, building, spec);
          }
        }
      }
      // 엄폐물이 사라지면 점도 지운다(보병은 풀린다).
      var spots = f.Filter<CoverSpot>();
      while (spots.NextUnsafe(out var entity, out var spot)) {
        // 점을 잡은 유닛이 죽었으면 비운다.
        if (spot->Occupant != EntityRef.None && f.Exists(spot->Occupant) == false) {
          spot->Occupant = EntityRef.None;
        }
        if (f.Exists(spot->Source) == false) {
          if (spot->Occupant != EntityRef.None && f.Unsafe.TryGetPointer<Unit>(spot->Occupant, out var occupant)) {
            occupant->Cover = EntityRef.None;
            occupant->InCover = false;
          }
          f.Destroy(entity);
        }
      }

      // 엄폐 찾기: 교전 중인 보병, 틱마다 CoverSearchPerTick명까지(엔티티 순서). 같은 점은 먼저 찾은 쪽이 갖는다.
      var searches = 0;
      var radiusSqr = rules.CoverSearchRadius * rules.CoverSearchRadius;
      var units = f.Filter<Transform3D, Unit>();
      while (searches < rules.CoverSearchPerTick && units.NextUnsafe(out var entity, out var transform, out var unit)) {
        if (unit->Cover != EntityRef.None || unit->Garrison != EntityRef.None || unit->GarrisonTarget != EntityRef.None || unit->TargetEntity == EntityRef.None
            || unit->Order == UnitOrder.Move || unit->Order == UnitOrder.Hold || unit->Health <= FP._0
            || f.Unsafe.TryGetPointer<Transform3D>(unit->TargetEntity, out var target) == false) {
          continue;
        }
        var spec = f.FindAsset(unit->Spec);
        if (CanUseCover(spec) == false) {
          continue;
        }
        // 같은 유닛을 매 틱 다시 찾지 않도록 1초에 한 번(엔티티 번호로 틱을 나눈다).
        if ((entity.Index + f.Number) % FactoryWarsUtil.SecondsToFrames(f, FP._1) != 0) {
          continue;
        }
        searches++;
        var range = CombatSystem.EffectiveRange(f, unit, spec);
        var best = EntityRef.None;
        var bestDistance = radiusSqr;
        var candidates = f.Filter<CoverSpot>();
        while (candidates.NextUnsafe(out var spotEntity, out var spot)) {
          if (spot->Occupant != EntityRef.None) {
            continue;
          }
          var distance = FactoryWarsUtil.FlatDistanceSqr(spot->Position, transform->Position);
          if (distance > bestDistance || Blocks(spot, target->Position, rules.CoverFacingCos) == false) {
            continue;
          }
          // 그 자리에서 대상이 사거리 안이어야 한다.
          if (FactoryWarsUtil.FlatDistanceSqr(spot->Position, target->Position) > CombatSystem.ReachSqr(f, unit->TargetEntity, range)) {
            continue;
          }
          best = spotEntity;
          bestDistance = distance;
        }
        if (best != EntityRef.None) {
          f.Unsafe.GetPointer<CoverSpot>(best)->Occupant = entity;
          unit->Cover = best;
          unit->InCover = false;
          MovementSystem.SetDestination(f, entity, f.Unsafe.GetPointer<CoverSpot>(best)->Position);
        }
      }
    }
  }
}
