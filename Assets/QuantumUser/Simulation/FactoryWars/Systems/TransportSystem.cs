namespace Quantum.FactoryWars {
  using System.Collections.Generic;
  using Photon.Deterministic;

  /// <summary>
  /// 플레이어 조종 수송 탑승·하차 시스템(결정론 FP 전용):
  /// - 장갑 병력수송차(APC): 보병 6기, 지상 파괴 시 승객이 40% 체력으로 주변에 긴급 탈출.
  /// - 수륙양용 상륙정(Landing Craft): 보병 6기 또는 전차 1~2대(차량 3슬롯), 해상 격침 시 승객 전원 손실.
  /// - 전술 수송헬기(Transport Helicopter): 보병 4기, 공중 격추 시 승객 전원 손실.
  /// </summary>
  public static unsafe class TransportSystem {
    public static int SlotCost(UnitSpec spec) {
      if (spec == null) {
        return 1;
      }
      return (spec.IsVehicle || spec.Size == UnitSize.Medium || spec.Size == UnitSize.Large) ? 3 : 1;
    }

    /// <summary>승객 유닛 규격이 해당 수송 유닛 규격에 탑승할 수 있는지 판별한다.</summary>
    public static bool CanUnitBoardSpec(UnitSpec passengerSpec, UnitSpec transportSpec) {
      if (passengerSpec == null || transportSpec == null || transportSpec.TransportCapacity <= 0) {
        return false;
      }
      if (passengerSpec.IsAir || passengerSpec.IsNaval || passengerSpec.TransportCapacity > 0) {
        return false;
      }
      var isVehicleOrArmor = passengerSpec.IsVehicle || passengerSpec.Size == UnitSize.Medium || passengerSpec.Size == UnitSize.Large;
      if (isVehicleOrArmor && transportSpec.TransportAllowsVehicles == false) {
        return false;
      }
      return SlotCost(passengerSpec) <= transportSpec.TransportCapacity;
    }

    public static int UsedSlots(Frame f, EntityRef transport) {
      var used = 0;
      var units = f.Filter<Unit>();
      while (units.NextUnsafe(out _, out var unit)) {
        if (unit->Transport == transport && unit->Health > FP._0) {
          used += SlotCost(f.FindAsset(unit->Spec));
        }
      }
      return used;
    }

    public static List<EntityRef> Passengers(Frame f, EntityRef transport) {
      var list = new List<EntityRef>(6);
      var units = f.Filter<Unit>();
      while (units.NextUnsafe(out var entity, out var unit)) {
        if (unit->Transport == transport && unit->Health > FP._0) {
          list.Add(entity);
        }
      }
      return list;
    }

    public static bool CanBoard(Frame f, EntityRef passengerEntity, Unit* pUnit, EntityRef transportEntity, Unit* tUnit) {
      if (pUnit == null || tUnit == null || passengerEntity == transportEntity) {
        return false;
      }
      if (pUnit->Health <= FP._0 || tUnit->Health <= FP._0 || pUnit->Owner != tUnit->Owner) {
        return false;
      }
      if (pUnit->Transport != EntityRef.None || pUnit->Garrison != EntityRef.None || tUnit->Transport != EntityRef.None) {
        return false;
      }
      var pSpec = f.FindAsset(pUnit->Spec);
      var tSpec = f.FindAsset(tUnit->Spec);
      if (CanUnitBoardSpec(pSpec, tSpec) == false) {
        return false;
      }
      return UsedSlots(f, transportEntity) + SlotCost(pSpec) <= tSpec.TransportCapacity;
    }

    /// <summary>선택한 아군 유닛들에게 친선 수송 유닛 후면 램프로 이동해 탑승하도록 명령한다.</summary>
    public static void OrderBoard(Frame f, PlayerRef player, EntityRef[] units, EntityRef transport) {
      if (units == null || units.Length == 0 || f.Unsafe.TryGetPointer<Unit>(transport, out var tUnit) == false
          || tUnit->Owner != player || tUnit->Health <= FP._0 || tUnit->Transport != EntityRef.None) {
        f.Events.ActionRejected(player, ActionKind.Garrison, RejectReason.Invalid, 0);
        return;
      }
      var tSpec = f.FindAsset(tUnit->Spec);
      if (tSpec.TransportCapacity <= 0) {
        f.Events.ActionRejected(player, ActionKind.Garrison, RejectReason.Invalid, 0);
        return;
      }
      var goal = ApproachPoint(f, transport, tSpec);
      var ordered = 0;
      for (var i = 0; i < units.Length; i++) {
        var entity = units[i];
        if (f.Unsafe.TryGetPointer<Unit>(entity, out var pUnit) == false || pUnit->Owner != player) {
          continue;
        }
        if (CanBoard(f, entity, pUnit, transport, tUnit) == false) {
          continue;
        }
        pUnit->GarrisonTarget = EntityRef.None;
        MovementSystem.Order(f, entity, pUnit, UnitOrder.Move, goal);
        pUnit->BoardTarget = transport;
        ordered++;
      }
      if (ordered == 0) {
        f.Events.ActionRejected(player, ActionKind.Garrison, RejectReason.Limit, 0);
      }
    }

    static FPVector3 ApproachPoint(Frame f, EntityRef transport, UnitSpec tSpec) {
      var pos = f.Unsafe.TryGetPointer<Transform3D>(transport, out var t) ? t->Position : default;
      pos.Y = FP._0;
      var rules = FactoryWarsUtil.Rules(f);
      var radius = MovementSystem.UnitRadius(tSpec);
      var rear = pos + new FPVector3(FP._0, FP._0, -(radius + FP._1 + FP._0_50));
      var ground = f.Map.GetNavMesh(rules.NavMeshName);
      var mask = *f.NavMeshRegionMask;
      if (ground != null && ground.Contains(rear, mask, true)) {
        return rear;
      }
      var searchRadius = tSpec.IsNaval ? (FP)16 : (FP)8;
      return FactoryWarsUtil.FindNavPointNear(f, rules.NavMeshName, pos, searchRadius, out var shore) ? shore : pos;
    }

    static FP BoardReach(UnitSpec tSpec) {
      if (tSpec.IsNaval) {
        return (FP)11;
      }
      if (tSpec.IsAir) {
        return (FP)6;
      }
      return MovementSystem.UnitRadius(tSpec) + (FP)3;
    }

    /// <summary>매 틱 탑승 접근 유닛을 수송기에 태우고, 탑승 중인 승객 위치를 수송기에 동기화한다.</summary>
    public static void Update(Frame f) {
      var repathFrames = FactoryWarsUtil.SecondsToFrames(f, FP._1);
      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out var entity, out var transform, out var unit)) {
        if (unit->Health <= FP._0) {
          continue;
        }
        if (unit->Transport != EntityRef.None) {
          if (f.Unsafe.TryGetPointer<Transform3D>(unit->Transport, out var tTransform)
              && f.Unsafe.TryGetPointer<Unit>(unit->Transport, out var tAlive) && tAlive->Health > FP._0) {
            transform->Position = new FPVector3(tTransform->Position.X, FP._0, tTransform->Position.Z);
          } else {
            // 안전망: 수송 엔티티가 이미 사라졌으면 승객을 제자리에 해제한다.
            unit->Transport = EntityRef.None;
            RestoreNavAgent(f, entity, unit, f.FindAsset(unit->Spec));
          }
          continue;
        }
        if (unit->BoardTarget == EntityRef.None) {
          continue;
        }
        var target = unit->BoardTarget;
        if (f.Unsafe.TryGetPointer<Unit>(target, out var tUnit) == false
            || f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == false
            || CanBoard(f, entity, unit, target, tUnit) == false) {
          unit->BoardTarget = EntityRef.None;
          continue;
        }
        var tSpec = f.FindAsset(tUnit->Spec);
        var reach = BoardReach(tSpec);
        if (FactoryWarsUtil.FlatDistanceSqr(transform->Position, targetTransform->Position) <= reach * reach) {
          BoardUnit(f, entity, unit, target, tUnit, targetTransform->Position);
        } else if (f.Number % repathFrames == 0) {
          var goal = ApproachPoint(f, target, tSpec);
          unit->TargetPosition = goal;
          MovementSystem.SetDestination(f, entity, goal);
        }
      }
    }

    static void BoardUnit(Frame f, EntityRef entity, Unit* unit, EntityRef transport, Unit* tUnit, FPVector3 transportPos) {
      CoverSystem.Release(f, unit);
      unit->Transport = transport;
      unit->BoardTarget = EntityRef.None;
      unit->GarrisonTarget = EntityRef.None;
      unit->Order = UnitOrder.Stop;
      unit->TargetEntity = EntityRef.None;
      f.Remove<NavMeshPathfinder>(entity);
      f.Remove<NavMeshSteeringAgent>(entity);
      f.Remove<NavMeshAvoidanceAgent>(entity);
      f.Unsafe.GetPointer<Transform3D>(entity)->Position = new FPVector3(transportPos.X, FP._0, transportPos.Z);
      var openFrames = FactoryWarsUtil.SecondsToFrames(f, FP._1 + FP._0_25);
      tUnit->RampOpenUntilFrame = System.Math.Max(tUnit->RampOpenUntilFrame, f.Number + openFrames);
      f.Events.TransportRamp(transport, false);
    }

    static void RestoreNavAgent(Frame f, EntityRef entity, Unit* unit, UnitSpec spec) {
      var config = f.FindAsset(f.SimulationConfig.Navigation.DefaultNavMeshAgent);
      f.Set(entity, NavMeshPathfinder.Create(f, entity, config));
      f.Add(entity, new NavMeshSteeringAgent());
      f.Add(entity, new NavMeshAvoidanceAgent());
      f.Unsafe.GetPointer<NavMeshSteeringAgent>(entity)->MaxSpeed = spec.MoveSpeed;
      unit->Order = UnitOrder.Stop;
      unit->TargetEntity = EntityRef.None;
    }

    /// <summary>수송기에서 전원 또는 특정 승객 1기를 후면 램프 뒤 비겹침 대형으로 하차시킨다.</summary>
    public static bool Unload(Frame f, PlayerRef player, EntityRef transport, EntityRef specificUnit) {
      if (f.Unsafe.TryGetPointer<Unit>(transport, out var tUnit) == false
          || f.Unsafe.TryGetPointer<Transform3D>(transport, out var tTransform) == false
          || tUnit->Owner != player || tUnit->Health <= FP._0) {
        f.Events.ActionRejected(player, ActionKind.Garrison, RejectReason.Invalid, 0);
        return false;
      }
      var tSpec = f.FindAsset(tUnit->Spec);
      if (tSpec.TransportCapacity <= 0) {
        f.Events.ActionRejected(player, ActionKind.Garrison, RejectReason.Invalid, 0);
        return false;
      }
      var list = new List<EntityRef>(6);
      if (specificUnit != EntityRef.None) {
        if (f.Unsafe.TryGetPointer<Unit>(specificUnit, out var sp) && sp->Transport == transport && sp->Health > FP._0) {
          list.Add(specificUnit);
        }
      } else {
        list = Passengers(f, transport);
      }
      if (list.Count == 0) {
        return false;
      }
      var rules = FactoryWarsUtil.Rules(f);
      var ground = f.Map.GetNavMesh(rules.NavMeshName);
      var mask = *f.NavMeshRegionMask;
      var tPos = new FPVector3(tTransform->Position.X, FP._0, tTransform->Position.Z);
      var tRad = MovementSystem.UnitRadius(tSpec);
      var anchor = tPos + new FPVector3(FP._0, FP._0, -(tRad + FP._2));
      if (ground != null && ground.Contains(anchor, mask, true) == false) {
        var searchRadius = tSpec.IsNaval ? (FP)16 : (FP)8;
        if (FactoryWarsUtil.FindNavPointNear(f, rules.NavMeshName, tPos, searchRadius, out anchor) == false) {
          f.Events.ActionRejected(player, ActionKind.Garrison, RejectReason.BlockedLocation, 0);
          return false;
        }
      }
      PlaceFormation(f, ground, mask, anchor, list, FP._0);
      var openFrames = FactoryWarsUtil.SecondsToFrames(f, FP._1 + FP._0_50);
      tUnit->RampOpenUntilFrame = System.Math.Max(tUnit->RampOpenUntilFrame, f.Number + openFrames);
      f.Events.TransportRamp(transport, true);
      return true;
    }

    /// <summary>수송기 파괴 시 규칙 적용: 지상 APC는 40% 체력 탈출, 공중 수송헬기·해상 상륙정은 승객 전원 손실.</summary>
    public static void OnTransportDestroyed(Frame f, EntityRef transport, UnitSpec tSpec, FPVector3 pos) {
      if (tSpec == null || tSpec.TransportCapacity <= 0) {
        return;
      }
      var list = Passengers(f, transport);
      if (list.Count == 0) {
        return;
      }
      var surviveRatio = (tSpec.IsAir || tSpec.IsNaval) ? FP._0 : tSpec.TransportEjectHealthRatio;
      if (surviveRatio <= FP._0) {
        for (var i = 0; i < list.Count; i++) {
          var pEntity = list[i];
          if (f.Unsafe.TryGetPointer<Unit>(pEntity, out var pUnit) == false) {
            continue;
          }
          pUnit->Transport = EntityRef.None;
          pUnit->Health = FP._0;
          var owner = FactoryWarsUtil.Player(f, pUnit->Owner);
          if (owner != null) {
            owner->UnitsLost++;
          }
          f.Events.UnitDestroyed(pEntity, pUnit->Owner, pUnit->Spec);
          f.Destroy(pEntity);
        }
        return;
      }
      var rules = FactoryWarsUtil.Rules(f);
      var ground = f.Map.GetNavMesh(rules.NavMeshName);
      var mask = *f.NavMeshRegionMask;
      var anchor = new FPVector3(pos.X, FP._0, pos.Z);
      if (ground != null && ground.Contains(anchor, mask, true) == false
          && FactoryWarsUtil.FindNavPointNear(f, rules.NavMeshName, anchor, (FP)10, out var safe)) {
        anchor = safe;
      }
      PlaceFormation(f, ground, mask, anchor, list, surviveRatio);
    }

    /// <summary>반경 기반 비겹침 반원 대형으로 승객을 지상에 전개한다. surviveRatio > 0이면 최대 체력의 해당 비율 이하로 깎는다.</summary>
    static void PlaceFormation(Frame f, NavMesh ground, NavMeshRegionMask mask, FPVector3 anchor, List<EntityRef> passengers, FP surviveRatio) {
      var placed = new List<(FPVector3 Pos, FP Rad)>(passengers.Count);
      var map = FactoryWarsUtil.MapInfo(f);
      for (var i = 0; i < passengers.Count; i++) {
        var pEntity = passengers[i];
        if (f.Unsafe.TryGetPointer<Unit>(pEntity, out var pUnit) == false) {
          continue;
        }
        var pSpec = f.FindAsset(pUnit->Spec);
        var pRad = MovementSystem.UnitRadius(pSpec);
        var chosen = anchor;
        var found = false;
        // 최대 24개 동심원 호 슬롯을 탐색해 이미 배치된 승객과 겹치지 않는 유효 지상 좌표를 고른다.
        for (var ring = 0; ring < 4 && found == false; ring++) {
          var dist = ring == 0 ? (passengers.Count == 1 ? FP._0 : pRad * FP._2 + FP._1)
            : (pRad * FP._2 + FP._1) * (ring + 1);
          var steps = ring == 0 ? 6 : 8 + ring * 4;
          for (var s = 0; s < steps; s++) {
            var angle = FP.PiTimes2 * (s + i) / steps;
            var cand = anchor + new FPVector3(FPMath.Cos(angle) * dist, FP._0, FPMath.Sin(angle) * dist);
            cand.X = FPMath.Clamp(cand.X, map.BoundsMin.X + FP._2, map.BoundsMax.X - FP._2);
            cand.Z = FPMath.Clamp(cand.Z, map.BoundsMin.Y + FP._2, map.BoundsMax.Y - FP._2);
            if (ground != null && ground.Contains(cand, mask, true) == false) {
              continue;
            }
            var overlaps = false;
            for (var k = 0; k < placed.Count; k++) {
              var minGap = pRad + placed[k].Rad + FP._0_75;
              if (FactoryWarsUtil.FlatDistanceSqr(cand, placed[k].Pos) < minGap * minGap) {
                overlaps = true;
                break;
              }
            }
            if (overlaps == false) {
              chosen = cand;
              found = true;
              break;
            }
          }
        }
        pUnit->Transport = EntityRef.None;
        pUnit->BoardTarget = EntityRef.None;
        if (surviveRatio > FP._0) {
          var capHp = FPMath.Max(FP._1, TechSystem.MaxHealth(f, pUnit->Owner, pUnit->Spec) * surviveRatio);
          pUnit->Health = FPMath.Min(pUnit->Health, capHp);
        }
        f.Unsafe.GetPointer<Transform3D>(pEntity)->Position = chosen;
        RestoreNavAgent(f, pEntity, pUnit, pSpec);
        placed.Add((chosen, pRad));
      }
    }
  }
}
