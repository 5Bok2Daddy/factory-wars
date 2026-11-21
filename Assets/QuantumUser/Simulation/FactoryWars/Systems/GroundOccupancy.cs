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

    static bool InsideBuilding(Frame f, FPVector3 point, FP radius) {
      var buildings = f.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out _, out var transform, out var building)) {
        if (building->Health <= FP._0) continue;
        if (Overlaps(point, transform->Position, f.FindAsset(building->Spec).FootprintRadius + radius)) return true;
      }
      return false;
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
      // 건물 점유가 아닌 지형(광산·숲·바위) 위 목표는 그대로 둔다. NavMesh 길찾기가 가장 가까운 끝점으로 고친다.
      // 여기서 옮기면 목표가 장애물 반대편 3m 밖으로 밀려 접근·탐지 거리가 어긋났다.
      if (!InsideBuilding(f, requested, radius)) {
        // 경계 가까운 목표는 반지름만큼만 안쪽으로 당긴다(먼 대체 지점을 찾지 않는다). 맵 밖 목표는 거절한다.
        var map = FactoryWarsUtil.MapInfo(f);
        if (requested.X < map.BoundsMin.X || requested.X > map.BoundsMax.X || requested.Z < map.BoundsMin.Y || requested.Z > map.BoundsMax.Y) return false;
        target.X = FPMath.Clamp(requested.X, map.BoundsMin.X + radius, map.BoundsMax.X - radius);
        target.Z = FPMath.Clamp(requested.Z, map.BoundsMin.Y + radius, map.BoundsMax.Y - radius);
        if (!InsideBuilding(f, target, radius)) return true;
        target = requested;
      }
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
      // 점유 사각형이 겹치는 건물 무리는 하나로 합쳐 무리의 모서리로 돈다. 건물마다 따로 고르면
      // 붙은 두 건물의 앞쪽 모서리 사이를 1초마다 오가며 갇혔다(Units_PassBetweenBuildingsFacingEachOther).
      Cluster(f, radius, center, half, out var min, out var max);
      // 출발점이 무리 상자 안(건물이 만든 구석)이면 모서리 우회 대신 벽 따라 미끄러지기(PushOutOfBuildings)에 맡긴다.
      if (from.X > min.X && from.X < max.X && from.Z > min.Y && from.Z < max.Y) return false;
      var clearance = FactoryWarsUtil.Rules(f).UnitClearance;
      var best = FP.MaxValue;
      for (var x = -1; x <= 1; x += 2) {
        for (var z = -1; z <= 1; z += 2) {
          var corner = new FPVector3(x < 0 ? min.X - clearance : max.X + clearance, FP._0, z < 0 ? min.Y - clearance : max.Y + clearance);
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

    /// <summary>첫 건물의 점유 사각형에서 시작해, 겹치거나 맞닿은 살아 있는 건물 사각형을 모두 합친 상자(XZ).</summary>
    static void Cluster(Frame f, FP radius, FPVector3 center, FP half, out FPVector2 min, out FPVector2 max) {
      min = new FPVector2(center.X - half, center.Z - half);
      max = new FPVector2(center.X + half, center.Z + half);
      var grew = true;
      for (var pass = 0; grew && pass < 16; pass++) {
        grew = false;
        var buildings = f.Filter<Transform3D, Building>();
        while (buildings.NextUnsafe(out _, out var transform, out var building)) {
          if (building->Health <= FP._0) continue;
          var p = transform->Position;
          var reach = f.FindAsset(building->Spec).FootprintRadius + radius;
          if (p.X - reach > max.X || p.X + reach < min.X || p.Z - reach > max.Y || p.Z + reach < min.Y) continue;
          if (p.X - reach < min.X) { min.X = p.X - reach; grew = true; }
          if (p.X + reach > max.X) { max.X = p.X + reach; grew = true; }
          if (p.Z - reach < min.Y) { min.Y = p.Z - reach; grew = true; }
          if (p.Z + reach > max.Y) { max.Y = p.Z + reach; grew = true; }
        }
      }
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
