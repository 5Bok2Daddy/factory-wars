namespace Quantum.FactoryWars {
  using Photon.Deterministic;

  /// <summary>Checks the baked ground mesh and the live square footprints used by construction.</summary>
  public static unsafe class GroundOccupancy {
    public static bool Overlaps(FPVector3 point, FPVector3 center, FP halfExtent) =>
      FPMath.Abs(point.X - center.X) < halfExtent && FPMath.Abs(point.Z - center.Z) < halfExtent;

    static bool OnGround(Frame f, FPVector3 point, FP radius) {
      var map = FactoryWarsUtil.MapInfo(f);
      if (point.X - radius < map.BoundsMin.X || point.X + radius > map.BoundsMax.X
          || point.Z - radius < map.BoundsMin.Y || point.Z + radius > map.BoundsMax.Y) return false;
      var nav = f.Map.GetNavMesh(FactoryWarsUtil.Rules(f).NavMeshName);
      if (nav == null) return false;
      var mask = *f.NavMeshRegionMask;
      if (!nav.Contains(point, mask, true)) return false;
      return nav.Contains(point + new FPVector3(radius, FP._0, FP._0), mask, true)
        && nav.Contains(point - new FPVector3(radius, FP._0, FP._0), mask, true)
        && nav.Contains(point + new FPVector3(FP._0, FP._0, radius), mask, true)
        && nav.Contains(point - new FPVector3(FP._0, FP._0, radius), mask, true);
    }

    public static bool CanStand(Frame f, FPVector3 point, FP radius) {
      point.Y = FP._0;
      if (!OnGround(f, point, radius)) return false;
      var buildings = f.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out _, out var transform, out var building)) {
        if (building->Health <= FP._0) continue;
        if (Overlaps(point, transform->Position, f.FindAsset(building->Spec).FootprintRadius + radius)) return false;
      }
      return true;
    }

    public static bool TryGoal(Frame f, FPVector3 requested, FP radius, out FPVector3 target) {
      requested.Y = FP._0;
      target = requested;
      if (CanStand(f, requested, radius)) return true;
      var rules = FactoryWarsUtil.Rules(f);
      if (FactoryWarsUtil.FindNavPointNear(f, rules.NavMeshName, requested, rules.SpawnOffset, out var near)
          && CanStand(f, near, radius)) {
        target = near;
        return true;
      }
      for (var ring = 1; ring <= 3; ++ring) {
        var offset = FPMath.Max(radius + rules.UnitClearance, rules.SpawnOffset / 2) * ring;
        for (var side = 0; side < 8; ++side) {
          var candidate = Offset(requested, offset, side);
          if (!CanStand(f, candidate, radius)) continue;
          target = candidate;
          return true;
        }
      }
      return false;
    }

    public static bool TryProductionSlots(Frame f, UnitSpec spec, Building* source, FPVector3 origin, FPVector3[] slots) {
      var rules = FactoryWarsUtil.Rules(f);
      var radius = MovementSystem.UnitRadius(spec);
      var firstRing = FPMath.Max(rules.SpawnOffset, f.FindAsset(source->Spec).FootprintRadius + radius + rules.UnitClearance);
      for (var slot = 0; slot < slots.Length; ++slot) {
        var chosen = false;
        for (var ring = 0; ring < 3 && !chosen; ++ring) {
          var offset = firstRing + ring * (radius * 2 + rules.UnitClearance);
          for (var side = 0; side < 8; ++side) {
            var point = Offset(origin, offset, (slot + side) % 8);
            if (!CanStand(f, point, radius) || !ClearOfUnits(f, point, radius)) continue;
            var reserved = false;
            for (var i = 0; i < slot; i++) {
              var spacing = radius * 2 + rules.UnitClearance;
              if (FactoryWarsUtil.FlatDistanceSqr(slots[i], point) < spacing * spacing) reserved = true;
            }
            if (reserved) continue;
            slots[slot] = point;
            chosen = true;
            break;
          }
        }
        if (!chosen) return false;
      }
      return true;
    }

    static bool ClearOfUnits(Frame f, FPVector3 point, FP radius) {
      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out _, out var transform, out var unit)) {
        if (unit->Health <= FP._0 || unit->Transport != EntityRef.None || unit->Garrison != EntityRef.None) continue;
        var spec = f.FindAsset(unit->Spec);
        if (spec.IsAir || spec.IsNaval) continue;
        var reach = radius + MovementSystem.UnitRadius(spec);
        if (FactoryWarsUtil.FlatDistanceSqr(point, transform->Position) < reach * reach) return false;
      }
      return true;
    }

    static FPVector3 Offset(FPVector3 origin, FP distance, int direction) {
      var angle = FP.PiTimes2 * direction / 8;
      return new FPVector3(origin.X + FPMath.Cos(angle) * distance, FP._0, origin.Z + FPMath.Sin(angle) * distance);
    }

    public static bool TryDetour(Frame f, FPVector3 from, FPVector3 goal, FP radius, out FPVector3 point) {
      point = goal;
      if (!FirstBuilding(f, from, goal, radius, out var center, out var half)) return false;
      var edge = half + FactoryWarsUtil.Rules(f).UnitClearance;
      var best = FP.MaxValue;
      for (var x = -1; x <= 1; x += 2) {
        for (var z = -1; z <= 1; z += 2) {
          var corner = new FPVector3(center.X + x * edge, FP._0, center.Z + z * edge);
          var leg = FactoryWarsUtil.FlatDistanceSqr(from, corner);
          if (leg <= FP._0_25 || !CanStand(f, corner, radius)
              || FirstBuilding(f, from, corner, radius, out _, out _)) continue;
          var cost = FPMath.Sqrt(leg) + FPMath.Sqrt(FactoryWarsUtil.FlatDistanceSqr(corner, goal));
          if (cost >= best) continue;
          best = cost;
          point = corner;
        }
      }
      return best != FP.MaxValue;
    }

    static bool FirstBuilding(Frame f, FPVector3 from, FPVector3 to, FP radius, out FPVector3 center, out FP half) {
      center = default;
      half = FP._0;
      var first = FP.MaxValue;
      var buildings = f.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out _, out var transform, out var building)) {
        if (building->Health <= FP._0) continue;
        var position = transform->Position;
        var reach = f.FindAsset(building->Spec).FootprintRadius + radius;
        if (Overlaps(from, position, reach)) continue;
        var enter = FP._0;
        var exit = FP._1;
        if (!ClipAxis(from.X - position.X, to.X - from.X, reach, ref enter, ref exit)
            || !ClipAxis(from.Z - position.Z, to.Z - from.Z, reach, ref enter, ref exit) || enter >= first) continue;
        center = position;
        half = reach;
        first = enter;
      }
      return first != FP.MaxValue;
    }

    static bool ClipAxis(FP from, FP delta, FP half, ref FP enter, ref FP exit) {
      if (delta == FP._0) return FPMath.Abs(from) <= half;
      var a = (-half - from) / delta;
      var b = (half - from) / delta;
      enter = FPMath.Max(enter, FPMath.Min(a, b));
      exit = FPMath.Min(exit, FPMath.Max(a, b));
      return enter <= exit;
    }
  }
}
