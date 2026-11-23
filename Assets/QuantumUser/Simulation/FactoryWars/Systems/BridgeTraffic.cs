namespace Quantum.FactoryWars {
  using Photon.Deterministic;

  /// <summary>
  /// Task 39: bridges are decks above the water. Ships pass under a bridge when their hull height is not above its clearance.
  /// Drawbridges (MapSpec.Drawbridges) raise for taller ships: a tall ship inside the approach radius plus a deck clear of ground
  /// units for DeckClearSeconds starts the lift; while the deck is not down the bridge's ground NavMesh region is closed; the deck
  /// lowers LowerDelaySeconds after the last tall ship left. Units on the deck block raising (nobody is crushed).
  /// All numbers come from GameRulesSpec / MapSpec / UnitSpec; the state lives in the Bridge component (rollback safe).
  /// </summary>
  public static unsafe class BridgeTraffic {
    public const int Down = 0, Raising = 1, Up = 2, Lowering = 3;

    public static FP ClearanceOf(MapSpec map, GameRulesSpec rules, int index) =>
      map.BridgeClearances != null && index < map.BridgeClearances.Length && map.BridgeClearances[index] > FP._0 ? map.BridgeClearances[index] : rules.BridgeClearance;

    public static bool IsDrawbridge(MapSpec map, GameRulesSpec rules, int index) {
      if (rules.DrawbridgeEnabled == false) return false;
      for (var i = 0; map.Drawbridges != null && i < map.Drawbridges.Length; i++) {
        if (map.Drawbridges[i] == index) return true;
      }
      return false;
    }

    /// <summary>Ship of this hull can pass under the bridge right now (deck destroyed, raised drawbridge, or enough clearance).</summary>
    public static bool ShipPasses(Bridge* bridge, FP hull) => bridge->Destroyed || (bridge->Drawbridge && bridge->Lift == Up) || hull <= bridge->Clearance;

    /// <summary>Position in bridge space: along the deck (road) and across it (the water flow).</summary>
    static void Local(MapShape shape, FPVector3 p, out FP along, out FP across, out FP halfAlong, out FP halfAcross) {
      var yaw = shape.Yaw * FP.Deg2Rad;
      var axis = new FPVector3(FPMath.Cos(yaw), FP._0, -FPMath.Sin(yaw));
      var side = new FPVector3(FPMath.Sin(yaw), FP._0, FPMath.Cos(yaw));
      var d = p - shape.Center;
      d.Y = FP._0;
      var u = FPVector3.Dot(d, axis);
      var v = FPVector3.Dot(d, side);
      // The road runs along the longer side; water flows along the shorter one.
      if (shape.HalfLength >= shape.HalfWidth) {
        along = u; across = v; halfAlong = shape.HalfLength; halfAcross = shape.HalfWidth;
      } else {
        along = v; across = u; halfAlong = shape.HalfWidth; halfAcross = shape.HalfLength;
      }
    }

    static FPVector3 FromLocal(MapShape shape, FP along, FP across) {
      var yaw = shape.Yaw * FP.Deg2Rad;
      var axis = new FPVector3(FPMath.Cos(yaw), FP._0, -FPMath.Sin(yaw));
      var side = new FPVector3(FPMath.Sin(yaw), FP._0, FPMath.Cos(yaw));
      return shape.HalfLength >= shape.HalfWidth ? shape.Center + axis * along + side * across : shape.Center + side * along + axis * across;
    }

    static bool DeckOccupied(Frame f, MapShape shape) {
      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out _, out var transform, out var unit)) {
        if (unit->Health <= FP._0 || unit->Garrison != EntityRef.None) continue;
        var spec = f.FindAsset(unit->Spec);
        if (spec.IsAir || spec.IsNaval) continue;
        if (shape.Distance(transform->Position) <= FP._1) return true;
      }
      return false;
    }

    static bool TallShipNear(Frame f, MapShape shape, FP clearance, FP radius) {
      var units = f.Filter<Transform3D, Unit>();
      var r2 = radius * radius;
      while (units.NextUnsafe(out _, out var transform, out var unit)) {
        if (unit->Health <= FP._0) continue;
        var spec = f.FindAsset(unit->Spec);
        if (spec.IsNaval == false || spec.HullHeight <= clearance) continue;
        if (FactoryWarsUtil.FlatDistanceSqr(transform->Position, shape.Center) <= r2) return true;
      }
      return false;
    }

    static void SetLift(Frame f, Bridge* bridge, int lift) {
      if (bridge->Lift == lift) return;
      var wasDown = bridge->Lift == Down;
      bridge->Lift = lift;
      if (wasDown || lift == Down) DepthSystem.ApplyBridgeRegion(f, bridge->Index, bridge->Destroyed);
      f.Events.DrawbridgeMoved(bridge->Index, lift);
    }

    public static void Update(Frame f, GameRulesSpec rules) {
      var map = FactoryWarsUtil.MapInfo(f);
      if (map.Bridges == null || map.Bridges.Length == 0) return;
      var dt = f.DeltaTime;
      var bridges = f.Filter<Bridge>();
      while (bridges.NextUnsafe(out _, out var bridge)) {
        if (bridge->Drawbridge == false) continue;
        var shape = map.Bridges[bridge->Index];
        if (bridge->Destroyed) {
          if (bridge->Lift != Down) { bridge->LiftT = FP._0; SetLift(f, bridge, Down); }
          continue;
        }
        var ship = TallShipNear(f, shape, bridge->Clearance, rules.DrawbridgeApproachRadius);
        var step = rules.DrawbridgeRaiseSeconds > FP._0 ? dt / rules.DrawbridgeRaiseSeconds : FP._1;
        switch (bridge->Lift) {
          case Down:
            if (ship == false) { bridge->DeckClearFrames = 0; break; }
            bridge->DeckClearFrames = DeckOccupied(f, shape) ? 0 : bridge->DeckClearFrames + 1;
            if (bridge->DeckClearFrames >= FactoryWarsUtil.SecondsToFrames(f, rules.DrawbridgeDeckClearSeconds)) {
              bridge->DeckClearFrames = 0;
              SetLift(f, bridge, Raising);
            }
            break;
          case Raising:
            bridge->LiftT = FPMath.Min(FP._1, bridge->LiftT + step);
            if (bridge->LiftT >= FP._1) {
              bridge->LowerAtFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, rules.DrawbridgeLowerDelaySeconds);
              SetLift(f, bridge, Up);
            }
            break;
          case Up:
            if (ship) bridge->LowerAtFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, rules.DrawbridgeLowerDelaySeconds);
            else if (f.Number >= bridge->LowerAtFrame) SetLift(f, bridge, Lowering);
            break;
          case Lowering:
            if (ship) { SetLift(f, bridge, Raising); break; }
            bridge->LiftT = FPMath.Max(FP._0, bridge->LiftT - step);
            if (bridge->LiftT <= FP._0) SetLift(f, bridge, Down);
            break;
        }
      }
      HoldShips(f, map);
    }

    /// <summary>A ship that may not pass waits at the bridge edge (pushed back to the side it came from).</summary>
    static void HoldShips(Frame f, MapSpec map) {
      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out _, out var transform, out var unit)) {
        if (unit->Health <= FP._0) continue;
        var spec = f.FindAsset(unit->Spec);
        if (spec.IsNaval == false) continue;
        var bridges = f.Filter<Bridge>();
        while (bridges.NextUnsafe(out _, out var bridge)) {
          if (ShipPasses(bridge, spec.HullHeight)) continue;
          var shape = map.Bridges[bridge->Index];
          Local(shape, transform->Position, out var along, out var across, out var halfAlong, out var halfAcross);
          var keep = halfAcross + spec.ColliderRadius;
          if (FPMath.Abs(along) > halfAlong || FPMath.Abs(across) >= keep) continue;
          var p = FromLocal(shape, along, across < FP._0 ? -keep : keep);
          p.Y = transform->Position.Y;
          transform->Position = p;
        }
      }
    }
  }
}
