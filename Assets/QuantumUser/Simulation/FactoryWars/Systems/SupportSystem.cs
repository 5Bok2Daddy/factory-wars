namespace Quantum.FactoryWars {
  using Photon.Deterministic;

  /// <summary>
  /// 항목 32 지원 건물: 의무대(주변 보병 치료)·정비소(주변 차량 수리)·레이더(반경 안 적 보이기·접근 경고).
  /// 정전이면 멈춘다(Paused). 수치는 BuildingSpec에만 있다.
  /// </summary>
  public unsafe class SupportSystem : SystemMainThread {
    /// <summary>player의 완공·가동 중 레이더 반경 안이면 true.</summary>
    public static bool InRadar(Frame f, PlayerRef player, FPVector3 position) {
      var buildings = f.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out _, out var transform, out var building)) {
        if (building->Owner != player || building->ConstructionRemaining > FP._0 || building->Paused != PauseReason.None) {
          continue;
        }
        var radius = f.FindAsset(building->Spec).RevealRadius;
        if (radius > FP._0 && FactoryWarsUtil.FlatDistanceSqr(transform->Position, position) <= radius * radius) {
          return true;
        }
      }
      return false;
    }

    static bool Matches(HealTarget heals, UnitSpec spec) {
      if (spec.IsAir || spec.IsNaval || spec.Size == UnitSize.Engineer) {
        return false;
      }
      var infantry = CitySystem.IsInfantry(spec);
      return heals == HealTarget.Infantry ? infantry : heals == HealTarget.Vehicles && infantry == false;
    }

    public override void Update(Frame f) {
      if (f.Global->GameEnded) {
        return;
      }
      var rules = FactoryWarsUtil.Rules(f);
      var buildings = f.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out var entity, out var transform, out var building)) {
        if (building->ConstructionRemaining > FP._0 || building->Paused != PauseReason.None) {
          continue;
        }
        var spec = f.FindAsset(building->Spec);
        if (spec.Heals != HealTarget.None) {
          var radiusSqr = spec.HealRadius * spec.HealRadius;
          var amount = spec.HealPerSecond * f.DeltaTime;
          var remaining = spec.HealCount;
          var units = f.Filter<Transform3D, Unit>();
          while (remaining > 0 && units.NextUnsafe(out _, out var unitTransform, out var unit)) {
            if (unit->Owner != building->Owner || unit->Health <= FP._0 || FactoryWarsUtil.FlatDistanceSqr(unitTransform->Position, transform->Position) > radiusSqr) {
              continue;
            }
            var unitSpec = f.FindAsset(unit->Spec);
            if (Matches(spec.Heals, unitSpec) == false) {
              continue;
            }
            var max = TechSystem.MaxHealth(f, unit->Owner, unit->Spec);
            if (unit->Health < max) {
              unit->Health = FPMath.Min(max, unit->Health + amount);
              remaining--;
            }
          }
        }
        if (spec.RevealRadius > FP._0) {
          var state = FactoryWarsUtil.Player(f, building->Owner);
          if (state == null || f.Number < state->NextRadarAlertFrame) {
            continue;
          }
          var radiusSqr = spec.RevealRadius * spec.RevealRadius;
          var units = f.Filter<Transform3D, Unit>();
          while (units.NextUnsafe(out _, out var unitTransform, out var unit)) {
            if (unit->Owner != building->Owner && DiplomacySystem.CanAttack(f, building->Owner, unit->Owner)
                && FactoryWarsUtil.FlatDistanceSqr(unitTransform->Position, transform->Position) <= radiusSqr) {
              state->NextRadarAlertFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, rules.RadarAlertSeconds);
              f.Events.RadarContact(building->Owner, unitTransform->Position);
              break;
            }
          }
        }
      }
    }
  }
}
