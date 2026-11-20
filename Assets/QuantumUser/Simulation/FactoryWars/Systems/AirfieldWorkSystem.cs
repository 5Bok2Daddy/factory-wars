namespace Quantum.FactoryWars {
  using Photon.Deterministic;

  public static unsafe class AirfieldWorkSystem {
    public static bool Required(BuildingRole role) => role == BuildingRole.Airfield || role == BuildingRole.Helipad;

    public static FP BuildSeconds(GameRulesSpec rules, BuildingRole role) =>
      role == BuildingRole.Helipad ? rules.LandingPadBuildSeconds : rules.RunwayBuildSeconds;

    /// <summary>포장 부지는 건물 옆에 예약하며, 방향 후보를 고정된 순서로 확인한다.</summary>
    public static bool TryFindSite(Frame f, BuildingSpec spec, FPVector3 building, PlayerRef owner, out MapShape site) {
      var rules = FactoryWarsUtil.Rules(f);
      var pad = spec.Role == BuildingRole.Helipad;
      var length = pad ? rules.LandingPadHalfSize : rules.RunwayHalfLength;
      var width = pad ? length : rules.RunwayHalfWidth;
      for (var side = 0; side < 4; side++) {
        var direction = side < 2 ? new FPVector3(FP._0, FP._0, side == 0 ? FP._1 : -FP._1)
          : new FPVector3(side == 2 ? FP._1 : -FP._1, FP._0, FP._0);
        var candidate = new MapShape {
          Center = building + direction * (spec.FootprintRadius + rules.BuildingGap + rules.AirfieldWorkClearance + width),
          HalfLength = length,
          HalfWidth = width,
          Yaw = side < 2 ? FP._0 : (FP)90,
        };
        if ((owner.IsValid == false || FactoryWarsUtil.IsReachable(f, owner, candidate.Center)) && SiteClear(f, candidate, rules)) {
          site = candidate;
          return true;
        }
      }
      site = default;
      return false;
    }

    public static bool IntersectsBuilding(MapShape site, FPVector3 center, FP half, FP gap) {
      Extents(site, out var x, out var z);
      return FPMath.Abs(site.Center.X - center.X) < x + half + gap
        && FPMath.Abs(site.Center.Z - center.Z) < z + half + gap;
    }

    static void Extents(MapShape site, out FP x, out FP z) {
      x = site.Yaw == FP._0 ? site.HalfLength : site.HalfWidth;
      z = site.Yaw == FP._0 ? site.HalfWidth : site.HalfLength;
    }

    static bool Touches(MapShape site, FP x, FP z, MapShape obstacle) {
      var offset = new FPVector2(obstacle.Center.X - site.Center.X, obstacle.Center.Z - site.Center.Z);
      if (obstacle.IsCircle) {
        var dx = FPMath.Max(FPMath.Abs(offset.X) - x, FP._0);
        var dz = FPMath.Max(FPMath.Abs(offset.Y) - z, FP._0);
        return dx * dx + dz * dz < obstacle.Radius * obstacle.Radius;
      }
      var radians = obstacle.Yaw * FP.Deg2Rad;
      var cos = FPMath.Cos(radians);
      var sin = FPMath.Sin(radians);
      var c = FPMath.Abs(cos);
      var s = FPMath.Abs(sin);
      return FPMath.Abs(offset.X) < x + obstacle.HalfLength * c + obstacle.HalfWidth * s
        && FPMath.Abs(offset.Y) < z + obstacle.HalfLength * s + obstacle.HalfWidth * c
        && FPMath.Abs(offset.X * cos - offset.Y * sin) < obstacle.HalfLength + x * c + z * s
        && FPMath.Abs(offset.X * sin + offset.Y * cos) < obstacle.HalfWidth + x * s + z * c;
    }

    static bool SiteClear(Frame f, MapShape site, GameRulesSpec rules) {
      var map = FactoryWarsUtil.MapInfo(f);
      Extents(site, out var x, out var z);
      if (site.Center.X - x < map.BoundsMin.X || site.Center.X + x > map.BoundsMax.X
          || site.Center.Z - z < map.BoundsMin.Y || site.Center.Z + z > map.BoundsMax.Y) return false;

      var nav = f.Map.GetNavMesh(rules.NavMeshName);
      var mask = *f.NavMeshRegionMask;
      if (nav == null) return false;
      for (var a = -1; a <= 1; a++) {
        for (var b = -1; b <= 1; b++) {
          if (nav.Contains(site.Center + new FPVector3(x * a, FP._0, z * b), mask, true) == false) return false;
        }
      }
      foreach (var road in map.Roads ?? System.Array.Empty<MapShape>())
        if (Touches(site, x, z, road)) return false;
      foreach (var terrain in map.Terrain ?? System.Array.Empty<TerrainFeature>())
        if (Touches(site, x, z, terrain.Shape)) return false;
      foreach (var ground in map.HighGrounds ?? System.Array.Empty<MapShape>())
        if (Touches(site, x, z, ground)) return false;
      foreach (var ramp in map.Ramps ?? System.Array.Empty<MapShape>())
        if (Touches(site, x, z, ramp)) return false;
      foreach (var zone in map.NoBuildZones ?? System.Array.Empty<MapShape>())
        if (Touches(site, x, z, zone)) return false;
      foreach (var city in map.CityBuildings ?? System.Array.Empty<CityBuildingPlacement>())
        if (Touches(site, x, z, city.Shape) || (city.Topples && Touches(site, x, z, city.FallZone))) return false;
      foreach (var bridge in map.Bridges ?? System.Array.Empty<MapShape>())
        if (Touches(site, x, z, bridge)) return false;
      foreach (var barricade in map.Barricades ?? System.Array.Empty<BarricadePlacement>())
        if (Touches(site, x, z, barricade.Shape)) return false;
      foreach (var capture in map.CaptureSites ?? System.Array.Empty<CaptureSitePlacement>())
        if (Touches(site, x, z, new MapShape { Center = capture.Center, Radius = capture.Radius })) return false;
      var resources = f.Filter<ResourceNode>();
      while (resources.NextUnsafe(out _, out var node)) {
        if (Touches(site, x, z, node->Shape)) return false;
      }
      var buildings = f.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out _, out var transform, out var other)) {
        if (IntersectsBuilding(site, transform->Position, f.FindAsset(other->Spec).FootprintRadius,
            rules.BuildingGap + rules.AirfieldWorkClearance)) return false;
      }
      var strips = f.Filter<AirfieldWorks>();
      while (strips.NextUnsafe(out _, out var other)) {
        if (Touches(site, x + rules.BuildingGap, z + rules.BuildingGap, other->Site)) return false;
      }
      return true;
    }

    public static bool InPavingPhase(Frame f, EntityRef entity, Building* building, BuildingSpec spec, out MapShape site) {
      if (building->ConstructionRemaining > FP._0 && f.TryGet<AirfieldWorks>(entity, out var works)
          && building->ConstructionRemaining <= BuildSeconds(FactoryWarsUtil.Rules(f), spec.Role)) {
        site = works.Site;
        return true;
      }
      site = default;
      return false;
    }

    public static FPVector3 WorkTarget(Frame f, EntityRef entity, Building* building, BuildingSpec spec, FPVector3 center, out FP range) {
      var rules = FactoryWarsUtil.Rules(f);
      if (InPavingPhase(f, entity, building, spec, out var site)) {
        range = rules.WorkRange;
        return site.Center;
      }
      range = spec.FootprintRadius + rules.WorkRange;
      return center;
    }
  }
}
