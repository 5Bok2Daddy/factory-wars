namespace Quantum.FactoryWars {
  using Photon.Deterministic;

  /// <summary>
  /// 심화 산업 공장 시스템(결정론 FP 전용):
  /// 1) 공장 증축(Lv.1 -> Lv.2 +25% -> Lv.3 +50%)
  /// 2) 12m 인접 시너지 파이프라인(강철 제련소 · 터빈 발전소 인접 시 생산 속도 +15%)
  /// 3) 비상 과부하(Overdrive: 20초간 생산 속도 +50%, 전력 소비 2배, 초당 소량 과열 피해)
  /// 4) 군수·연료 보급창 진영 전역 버프(차량 속도 +10%, 포병 재장전 +12%, 항공 출격 대기 -15%) & 파괴 시 연쇄 대폭발
  /// 5) 전력 부족 시 공장 생산 -35% 및 패트리어트/전략 미사일 비활성화.
  /// </summary>
  public static unsafe class FactorySystem {
    public static bool IsProductionFactory(BuildingRole role) {
      return role == BuildingRole.Barracks || role == BuildingRole.VehicleFactory || role == BuildingRole.HeavyWorks
        || role == BuildingRole.Airfield || role == BuildingRole.UltimateSite || role == BuildingRole.Shipyard || role == BuildingRole.Helipad;
    }

    public static bool IsUpgradeableFactory(BuildingRole role) {
      return IsProductionFactory(role) || role == BuildingRole.Processor || role == BuildingRole.PowerPlant || role == BuildingRole.ResearchLab;
    }

    public static bool IsSynergySource(BuildingRole role) {
      return role == BuildingRole.Processor || role == BuildingRole.PowerPlant;
    }

    public static int LevelOf(Building* building) {
      if (building == null) {
        return 1;
      }
      if (building->FactoryLevel <= 1) return 1;
      return building->FactoryLevel >= 3 ? 3 : 2;
    }

    public static bool IsOverdriving(Frame f, Building* building) {
      return building != null && building->OverdriveUntilFrame > f.Number;
    }

    public static bool IsLowPower(PlayerState* state) {
      return state != null && (state->Outage || state->PowerRate < FP._0);
    }

    /// <summary>심각한 전력 부족·정전(정전이거나 전력 적자이면서 저장 전력 25% 미만) — 패트리어트/전략 미사일 발사 차단.</summary>
    public static bool IsCriticalLowPower(PlayerState* state) {
      return state != null && (state->Outage || (state->PowerRate < FP._0 && state->Stock.Power < state->PowerCap / (FP)4));
    }

    /// <summary>생산 공장이 12m 이내에 아군 강철 제련소 또는 터빈 발전소와 인접해 있으면 true(+15% 시너지).</summary>
    public static bool HasAdjacencySynergy(Frame f, EntityRef factoryEntity, Building* factory) {
      if (factory == null || factory->ConstructionRemaining > FP._0) {
        return false;
      }
      var spec = f.FindAsset(factory->Spec);
      if (IsProductionFactory(spec.Role) == false) {
        return false;
      }
      if (f.Unsafe.TryGetPointer<Transform3D>(factoryEntity, out var fTransform) == false) {
        return false;
      }
      var rules = FactoryWarsUtil.Rules(f);
      var r = rules.FactorySynergyRadius > FP._0 ? rules.FactorySynergyRadius : (FP)12;
      var r2 = r * r;
      var buildings = f.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out var otherEntity, out var oTransform, out var other)) {
        if (otherEntity == factoryEntity || other->Owner != factory->Owner || other->ConstructionRemaining > FP._0 || other->Health <= FP._0) {
          continue;
        }
        var oSpec = f.FindAsset(other->Spec);
        if (IsSynergySource(oSpec.Role) && FactoryWarsUtil.FlatDistanceSqr(fTransform->Position, oTransform->Position) <= r2) {
          return true;
        }
      }
      return false;
    }

    /// <summary>공장 최종 생산 속도 배율: (1 + 레벨 보너스 + 인접 시너지 + 과부하) × 전력 상태 배율.</summary>
    public static FP ProductionSpeedMultiplier(Frame f, EntityRef entity, Building* building, PlayerState* state) {
      var rules = FactoryWarsUtil.Rules(f);
      var bonus = FP._1;
      var level = LevelOf(building);
      if (level == 2) {
        bonus += rules.FactoryLv2SpeedBonus > FP._0 ? rules.FactoryLv2SpeedBonus : (FP)25 / (FP)100;
      } else if (level >= 3) {
        bonus += rules.FactoryLv3SpeedBonus > FP._0 ? rules.FactoryLv3SpeedBonus : FP._0_50;
      }
      if (HasAdjacencySynergy(f, entity, building)) {
        bonus += rules.FactorySynergyBonus > FP._0 ? rules.FactorySynergyBonus : (FP)15 / (FP)100;
      }
      if (IsOverdriving(f, building)) {
        bonus += rules.OverdriveSpeedBonus > FP._0 ? rules.OverdriveSpeedBonus : FP._0_50;
      }
      if (IsLowPower(state)) {
        var lowFactor = rules.LowPowerProductionMultiplier > FP._0 ? rules.LowPowerProductionMultiplier : (FP)65 / (FP)100;
        bonus *= lowFactor;
      }
      return bonus;
    }

    /// <summary>공장 업그레이드(Lv.1 -> Lv.2 -> Lv.3) 비용.</summary>
    public static ResourceCost UpgradeCost(Frame f, int nextLevel) {
      var rules = FactoryWarsUtil.Rules(f);
      if (nextLevel == 2) {
        return rules.FactoryLv2Cost.Iron > FP._0 ? rules.FactoryLv2Cost : new ResourceCost { Iron = 200, Steel = 100 };
      }
      return rules.FactoryLv3Cost.Iron > FP._0 ? rules.FactoryLv3Cost : new ResourceCost { Iron = 380, Steel = 220 };
    }

    /// <summary>공장 증축 실행(Lv.1 -> Lv.2 -> Lv.3).</summary>
    public static bool TryUpgradeFactory(Frame f, PlayerRef player, EntityRef entity) {
      if (f.Unsafe.TryGetPointer<Building>(entity, out var building) == false || building->Owner != player || building->ConstructionRemaining > FP._0 || building->Health <= FP._0) {
        f.Events.ActionRejected(player, ActionKind.Upgrade, RejectReason.Invalid, 0);
        return false;
      }
      var spec = f.FindAsset(building->Spec);
      if (IsUpgradeableFactory(spec.Role) == false) {
        f.Events.ActionRejected(player, ActionKind.Upgrade, RejectReason.Invalid, 0);
        return false;
      }
      var current = LevelOf(building);
      if (current >= 3) {
        f.Events.ActionRejected(player, ActionKind.Upgrade, RejectReason.Limit, 0);
        return false;
      }
      var next = current + 1;
      var cost = UpgradeCost(f, next);
      var state = FactoryWarsUtil.Player(f, player);
      if (state == null || state->Stock.Covers(cost) == false) {
        f.Events.ActionRejected(player, ActionKind.Upgrade, RejectReason.InsufficientResources, 0);
        return false;
      }
      state->Stock -= cost;
      building->FactoryLevel = next;
      // 증축 시 내구도 소폭 보강(+15%).
      building->Health = FPMath.Min(spec.MaxHealth, building->Health + spec.MaxHealth * ((FP)15 / (FP)100));
      return true;
    }

    /// <summary>비상 과부하(Overdrive) 토글: 20초간 생산 속도 +50%, 전력 소비 2배, 초당 과열 피해.</summary>
    public static bool ToggleOverdrive(Frame f, PlayerRef player, EntityRef entity) {
      if (f.Unsafe.TryGetPointer<Building>(entity, out var building) == false || building->Owner != player || building->ConstructionRemaining > FP._0 || building->Health <= FP._0) {
        f.Events.ActionRejected(player, ActionKind.Upgrade, RejectReason.Invalid, 0);
        return false;
      }
      var spec = f.FindAsset(building->Spec);
      if (IsProductionFactory(spec.Role) == false) {
        f.Events.ActionRejected(player, ActionKind.Upgrade, RejectReason.Invalid, 0);
        return false;
      }
      if (IsOverdriving(f, building)) {
        building->OverdriveUntilFrame = 0;
        return true;
      }
      if (f.Number < building->OverdriveCooldownFrame) {
        f.Events.ActionRejected(player, ActionKind.Upgrade, RejectReason.Cooldown, 0);
        return false;
      }
      var rules = FactoryWarsUtil.Rules(f);
      var dur = rules.OverdriveDurationSeconds > FP._0 ? rules.OverdriveDurationSeconds : (FP)20;
      var cd = rules.OverdriveCooldownSeconds > FP._0 ? rules.OverdriveCooldownSeconds : (FP)35;
      building->OverdriveUntilFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, dur);
      building->OverdriveCooldownFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, dur + cd);
      return true;
    }

    /// <summary>과부하 중인 공장에 초당 소량의 자가 과열 피해를 준다(최소 체력 10% 유지).</summary>
    public static void UpdateOverdriveHeat(Frame f) {
      var rules = FactoryWarsUtil.Rules(f);
      var dps = rules.OverdriveHeatDamagePerSecond > FP._0 ? rules.OverdriveHeatDamagePerSecond : (FP)8;
      var tickDmg = dps * f.DeltaTime;
      var buildings = f.Filter<Building>();
      while (buildings.NextUnsafe(out _, out var building)) {
        if (building->OverdriveUntilFrame <= f.Number || building->Health <= FP._0 || building->ConstructionRemaining > FP._0) {
          continue;
        }
        var spec = f.FindAsset(building->Spec);
        var minHp = FPMath.Max(FP._1, spec.MaxHealth / (FP)10);
        if (building->Health > minHp) {
          building->Health = FPMath.Max(minHp, building->Health - tickDmg);
        }
      }
    }

    /// <summary>아군이 완공된 군수·연료 보급창(SupplyDepot)을 1채 이상 보유 중인지 확인.</summary>
    public static bool HasMunitionsDepot(Frame f, PlayerRef player) {
      var buildings = f.Filter<Building>();
      while (buildings.NextUnsafe(out _, out var b)) {
        if (b->Owner == player && b->ConstructionRemaining <= FP._0 && b->Health > FP._0 && f.FindAsset(b->Spec).Role == BuildingRole.SupplyDepot) {
          return true;
        }
      }
      return false;
    }

    /// <summary>군수·연료 보급창 버프: 지상 차량 이동 속도 +10%.</summary>
    public static FP VehicleSpeedMultiplier(Frame f, PlayerRef owner, UnitSpec spec) {
      if (spec == null || spec.IsAir || spec.IsNaval) return FP._1;
      var isVehicleOrTank = spec.IsVehicle || spec.Size == UnitSize.Medium || spec.Size == UnitSize.Large;
      if (isVehicleOrTank == false || HasMunitionsDepot(f, owner) == false) return FP._1;
      var bonus = FactoryWarsUtil.Rules(f).DepotVehicleSpeedBonus;
      return FP._1 + (bonus > FP._0 ? bonus : FP._0_10);
    }

    /// <summary>군수·연료 보급창 버프: 포병(Area 공격) 재장전 속도 +12% -> 공격 간격 1 / 1.12 배.</summary>
    public static FP ArtilleryCooldownMultiplier(Frame f, PlayerRef owner, UnitSpec spec) {
      if (spec == null || spec.Attack != AttackType.Area || HasMunitionsDepot(f, owner) == false) return FP._1;
      var bonus = FactoryWarsUtil.Rules(f).DepotArtilleryReloadBonus;
      var rate = FP._1 + (bonus > FP._0 ? bonus : (FP)12 / (FP)100);
      return FP._1 / rate;
    }

    /// <summary>군수·연료 보급창 버프: 항공 출격 재정비 대기시간 -15%.</summary>
    public static FP SortieRearmMultiplier(Frame f, PlayerRef owner) {
      if (HasMunitionsDepot(f, owner) == false) return FP._1;
      var red = FactoryWarsUtil.Rules(f).DepotSortieCooldownReduction;
      return FPMath.Max(FP._0_50, FP._1 - (red > FP._0 ? red : (FP)15 / (FP)100));
    }

    /// <summary>군수·연료 보급창 파괴 시 연쇄 대폭발: 반경 10m 내 인접 건물·유닛에 480 광역 폭발 피해(인접 보급창도 연쇄 폭발).</summary>
    public static void TriggerDepotExplosion(Frame f, EntityRef sourceDepot, FPVector3 origin, PlayerRef owner, int depth = 0) {
      if (depth >= 4) {
        return;
      }
      var rules = FactoryWarsUtil.Rules(f);
      var radius = rules.DepotExplosionRadius > FP._0 ? rules.DepotExplosionRadius : (FP)10;
      var damage = rules.DepotExplosionDamage > FP._0 ? rules.DepotExplosionDamage : (FP)480;
      var r2 = radius * radius;
      f.Events.DepotExploded(origin, radius);
      f.Events.AreaImpact(origin, radius);

      // 인접 유닛 피해.
      var weapon = new Weapon { Owner = owner, Attack = AttackType.Area, IsRanged = true, Origin = origin, Area = true, IgnoreCover = true };
      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out var uEntity, out var uTrans, out var unit)) {
        if (unit->Health <= FP._0 || unit->Garrison != EntityRef.None) continue;
        if (FactoryWarsUtil.FlatDistanceSqr(uTrans->Position, origin) <= r2) {
          CombatSystem.DamageUnit(f, weapon, uEntity, unit, damage / (FP)2);
        }
      }

      // 인접 건물 광역 피해 + 파괴된 인접 보급창 연쇄 폭발 수집.
      var chainDepots = new System.Collections.Generic.List<(EntityRef Entity, FPVector3 Pos, PlayerRef Owner)>();
      var buildings = f.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out var bEntity, out var bTrans, out var building)) {
        if (bEntity == sourceDepot || building->Health <= FP._0) continue;
        if (FactoryWarsUtil.FlatDistanceSqr(bTrans->Position, origin) <= r2) {
          building->Health -= damage;
          if (building->Health <= FP._0) {
            var lost = f.FindAsset(building->Spec);
            var loser = FactoryWarsUtil.Player(f, building->Owner);
            if (loser != null) {
              loser->BuildingsLost++;
              if (lost.IsCoreBuilding) loser->CoreLost++;
            }
            if (lost.Role == BuildingRole.SupplyDepot) {
              chainDepots.Add((bEntity, bTrans->Position, building->Owner));
            }
            f.Events.Wrecked(bTrans->Position, -1, lost.FootprintRadius);
            f.Destroy(bEntity);
          }
        }
      }
      for (var i = 0; i < chainDepots.Count; i++) {
        TriggerDepotExplosion(f, chainDepots[i].Entity, chainDepots[i].Pos, chainDepots[i].Owner, depth + 1);
      }
    }
  }
}
