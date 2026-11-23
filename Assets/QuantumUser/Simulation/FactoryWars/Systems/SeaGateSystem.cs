namespace Quantum.FactoryWars {
  using Photon.Deterministic;

  /// <summary>
  /// Task 46: outer-waterway sea gates (maps with MapSpec.SeaGates and GameRulesSpec.Waterway).
  /// - One SeaGate entity per MapSpec.SeaGates entry. When its seat has a player: a barrier boom (Building, WaterwaySpec.Barrier)
  ///   across the waterway and a watchtower (Building, WaterwaySpec.Tower) with one sentry (faction StartingUnits[0]), all owned
  ///   by the sector owner. An empty seat has no barrier: its gate is open.
  /// - Gate: closed, then an owner ship inside SeaGateTriggers[i] for OpenSeconds opens it; with no owner ship it closes over
  ///   CloseSeconds (still passable meanwhile: an enemy that slips in gets in), then it is closed. Water NavMesh region
  ///   "{SeaGateRegion}{i}" is the passage: a global region toggle (frame data, rollback safe), never a per-agent mask.
  /// - Destroyed barrier: the region opens the same frame and stays open until the owner rebuilds it (RebuildSeaBarrierCommand:
  ///   BuildingSpec cost, then BuildTimeSeconds of self-construction). Engineers can repair a live barrier like any building.
  /// - Alert: a hit on the barrier or the tower raises SeaBarrierAlert for the owner once per attack window (AlertQuietSeconds
  ///   without hits re-arms it).
  /// </summary>
  public unsafe class SeaGateSystem : SystemMainThread {
    public const int Closed = 0, Opening = 1, Open = 2, Closing = 3;

    public static WaterwaySpec Spec(Frame f) {
      var map = FactoryWarsUtil.MapInfo(f);
      if (map.SeaGates == null || map.SeaGates.Length == 0) return null;
      var rules = FactoryWarsUtil.Rules(f);
      return rules.Waterway.IsValid ? f.FindAsset(rules.Waterway) : null;
    }

    public override void Update(Frame f) {
      var spec = Spec(f);
      if (spec == null) return;
      var map = FactoryWarsUtil.MapInfo(f);
      EnsureGates(f, map);
      var gates = f.Filter<SeaGate>();
      while (gates.NextUnsafe(out _, out var gate)) {
        if (gate->Spawned == false) TrySpawn(f, spec, map, gate);
        UpdateGate(f, spec, map, gate);
      }
    }

    static void EnsureGates(Frame f, MapSpec map) {
      if (f.ComponentCount<SeaGate>() > 0) return;
      for (var i = 0; i < map.SeaGates.Length; i++) {
        var entity = f.Create();
        var seat = map.SeaGateSeats != null && i < map.SeaGateSeats.Length ? map.SeaGateSeats[i] : -1;
        f.Add(entity, new SeaGate { Index = i, Seat = seat, Owner = PlayerRef.None, Barrier = EntityRef.None, Tower = EntityRef.None, State = Closed, RegionOpen = true });
        // Until a barrier exists the passage is open (empty seat, or before the owner joined).
        SetRegion(f, i, true);
      }
    }

    static void TrySpawn(Frame f, WaterwaySpec spec, MapSpec map, SeaGate* gate) {
      if (gate->Seat < 0) { gate->Spawned = true; return; }
      var player = FactoryWarsUtil.PlayerAtSeat(f, gate->Seat);
      if (player < 0) return;
      var owner = (PlayerRef)player;
      var state = FactoryWarsUtil.Player(f, owner);
      if (state == null || state->Active == false) return;
      gate->Spawned = true;
      gate->Owner = owner;
      var center = map.SeaGates[gate->Index].Center;
      center.Y = FP._0;
      if (spec.Barrier.IsValid) {
        gate->Barrier = ProductionSystem.CreateFinishedBuilding(f, owner, spec.Barrier, center);
        gate->BarrierHealth = f.Unsafe.GetPointer<Building>(gate->Barrier)->Health;
      }
      if (spec.Tower.IsValid && map.SeaGateTowers != null && gate->Index < map.SeaGateTowers.Length) {
        var at = map.SeaGateTowers[gate->Index];
        at.Y = FP._0;
        gate->Tower = ProductionSystem.CreateFinishedBuilding(f, owner, spec.Tower, at);
        gate->TowerHealth = f.Unsafe.GetPointer<Building>(gate->Tower)->Health;
        var faction = f.FindAsset(state->Faction);
        var sentry = faction != null && faction.StartingUnits != null && faction.StartingUnits.Length > 0 ? faction.StartingUnits[0] : default;
        if (sentry.IsValid) {
          // The sentry stands on the inland side of the tower (away from the barrier line).
          var away = at - center;
          away.Y = FP._0;
          away = away.SqrMagnitude > FP._0 ? away.Normalized : FPVector3.Forward;
          var reach = f.FindAsset(spec.Tower).FootprintRadius + FP._2;
          var unit = ProductionSystem.SpawnUnitAt(f, owner, sentry, at + away * reach, null);
          if (f.Unsafe.TryGetPointer<Unit>(unit, out var u)) u->Order = UnitOrder.Hold;
        }
      }
      gate->State = Closed;
      gate->StateFrame = f.Number;
      Apply(f, gate, BarrierAlive(f, gate) == false);
    }

    public static bool BarrierAlive(Frame f, SeaGate* gate) =>
      gate->Barrier != EntityRef.None && f.Unsafe.TryGetPointer<Building>(gate->Barrier, out var b) && b->Health > FP._0 && b->ConstructionRemaining <= FP._0;

    static bool OwnerShipIn(Frame f, MapShape trigger, PlayerRef owner) {
      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out _, out var transform, out var unit)) {
        if (unit->Owner != owner || unit->Health <= FP._0 || unit->Transport != EntityRef.None) continue;
        if (f.FindAsset(unit->Spec).IsNaval == false) continue;
        if (trigger.Distance(transform->Position) <= FP._0) return true;
      }
      return false;
    }

    static void UpdateGate(Frame f, WaterwaySpec spec, MapSpec map, SeaGate* gate) {
      if (gate->Spawned == false || gate->Owner == PlayerRef.None) return;
      TrackHits(f, spec, map, gate);
      // A barrier under (re)construction builds itself.
      if (gate->Barrier != EntityRef.None && f.Unsafe.TryGetPointer<Building>(gate->Barrier, out var building) && building->ConstructionRemaining > FP._0) {
        var bspec = f.FindAsset(building->Spec);
        building->ConstructionRemaining = FPMath.Max(FP._0, building->ConstructionRemaining - f.DeltaTime);
        if (bspec.BuildTimeSeconds > FP._0) building->Health = FPMath.Min(bspec.MaxHealth, building->Health + bspec.MaxHealth * f.DeltaTime / bspec.BuildTimeSeconds);
        gate->BarrierHealth = building->Health;
        if (building->ConstructionRemaining <= FP._0) {
          building->Health = bspec.MaxHealth;
          gate->BarrierHealth = building->Health;
          gate->State = Closed;
          gate->StateFrame = f.Number;
          f.Events.SeaGateChanged(gate->Index, Closed, false);
        }
      }
      if (BarrierAlive(f, gate) == false) {
        if (gate->RegionOpen == false) f.Events.SeaGateChanged(gate->Index, Open, true);
        Apply(f, gate, true);
        return;
      }
      var trigger = map.SeaGateTriggers != null && gate->Index < map.SeaGateTriggers.Length ? map.SeaGateTriggers[gate->Index] : map.SeaGates[gate->Index];
      var ship = OwnerShipIn(f, trigger, gate->Owner);
      var previous = gate->State;
      switch (gate->State) {
        case Closed:
          if (ship) { gate->State = Opening; gate->StateFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, spec.OpenSeconds); }
          break;
        case Opening:
          if (ship == false) gate->State = Closed;
          else if (f.Number >= gate->StateFrame) gate->State = Open;
          break;
        case Open:
          if (ship == false) { gate->State = Closing; gate->StateFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, spec.CloseSeconds); }
          break;
        case Closing:
          if (ship) gate->State = Open;
          else if (f.Number >= gate->StateFrame) gate->State = Closed;
          break;
      }
      if (previous != gate->State) f.Events.SeaGateChanged(gate->Index, gate->State, false);
      Apply(f, gate, gate->State == Open || gate->State == Closing);
    }

    static void TrackHits(Frame f, WaterwaySpec spec, MapSpec map, SeaGate* gate) {
      var hit = false;
      var at = map.SeaGates[gate->Index].Center;
      if (gate->Barrier != EntityRef.None) {
        var alive = f.Unsafe.TryGetPointer<Building>(gate->Barrier, out var b);
        var health = alive ? b->Health : FP._0;
        // Construction raises health; only a drop counts as a hit.
        if (health < gate->BarrierHealth) hit = true;
        gate->BarrierHealth = health;
        if (alive == false || health <= FP._0) {
          gate->Barrier = EntityRef.None;
          gate->BarrierHealth = FP._0;
        }
      }
      if (gate->Tower != EntityRef.None) {
        var alive = f.Unsafe.TryGetPointer<Building>(gate->Tower, out var t);
        var health = alive ? t->Health : FP._0;
        if (health < gate->TowerHealth) {
          hit = true;
          if (map.SeaGateTowers != null && gate->Index < map.SeaGateTowers.Length) at = map.SeaGateTowers[gate->Index];
        }
        gate->TowerHealth = health;
        if (alive == false || health <= FP._0) {
          gate->Tower = EntityRef.None;
          gate->TowerHealth = FP._0;
        }
      }
      if (hit == false) return;
      var quiet = FactoryWarsUtil.SecondsToFrames(f, spec.AlertQuietSeconds);
      if (gate->LastHitFrame == 0 || f.Number - gate->LastHitFrame > quiet) {
        gate->Alerts++;
        f.Events.SeaBarrierAlert(gate->Owner, gate->Index, at);
      }
      gate->LastHitFrame = f.Number;
    }

    static void Apply(Frame f, SeaGate* gate, bool open) {
      if (gate->RegionOpen == open) return;
      gate->RegionOpen = open;
      SetRegion(f, gate->Index, open);
    }

    static void SetRegion(Frame f, int index, bool open) {
      var spec = Spec(f);
      if (spec == null || string.IsNullOrEmpty(spec.SeaGateRegion)) return;
      if (f.Map.RegionMap.TryGetValue($"{spec.SeaGateRegion}{index}", out var region)) {
        f.NavMeshRegionMask->ToggleRegion(region, open);
      }
    }

    public static SeaGate* Gate(Frame f, int index) {
      var gates = f.Filter<SeaGate>();
      while (gates.NextUnsafe(out _, out var gate)) {
        if (gate->Index == index) return gate;
      }
      return null;
    }

    /// <summary>Owner pays the barrier cost; the new barrier builds itself in BuildTimeSeconds. The passage stays open until it is done.</summary>
    public static void Rebuild(Frame f, PlayerRef player, int index) {
      var spec = Spec(f);
      var gate = spec != null ? Gate(f, index) : null;
      if (gate == null || gate->Owner != player || gate->Barrier != EntityRef.None || spec.Barrier.IsValid == false) {
        f.Events.ActionRejected(player, ActionKind.Build, RejectReason.Invalid, 0);
        return;
      }
      var state = FactoryWarsUtil.Player(f, player);
      var bspec = f.FindAsset(spec.Barrier);
      var missing = state->Stock.Missing(bspec.Cost);
      if (missing != MissingResource.None) {
        f.Events.ActionRejected(player, ActionKind.Build, RejectReason.InsufficientResources, (int)missing);
        return;
      }
      state->Stock -= bspec.Cost;
      var center = FactoryWarsUtil.MapInfo(f).SeaGates[index].Center;
      center.Y = FP._0;
      gate->Barrier = ProductionSystem.CreateBuilding(f, player, spec.Barrier, center);
      gate->BarrierHealth = f.Unsafe.GetPointer<Building>(gate->Barrier)->Health;
    }

    /// <summary>Shipyard zone rule. No zones on the map: no extra rule (int.MaxValue). Otherwise the zone index, or -1 when outside every zone.</summary>
    public static int ShipyardZoneAt(Frame f, FPVector3 position, PlayerRef player) {
      var map = FactoryWarsUtil.MapInfo(f);
      if (map.ShipyardZones == null || map.ShipyardZones.Length == 0) return int.MaxValue;
      var seat = player.IsValid ? FactoryWarsUtil.SeatOf(f, player) : -1;
      for (var i = 0; i < map.ShipyardZones.Length; i++) {
        var zoneSeat = map.ShipyardZoneSeats != null && i < map.ShipyardZoneSeats.Length ? map.ShipyardZoneSeats[i] : -1;
        if (zoneSeat >= 0 && zoneSeat != seat) continue;
        if (map.ShipyardZones[i].Distance(position) <= FP._0) return i;
      }
      return -1;
    }

    public static bool InSmallVesselZone(Frame f, FPVector3 position) {
      var map = FactoryWarsUtil.MapInfo(f);
      for (var i = 0; map.ShipyardZones != null && i < map.ShipyardZones.Length; i++) {
        var small = map.ShipyardZoneSmallOnly != null && i < map.ShipyardZoneSmallOnly.Length && map.ShipyardZoneSmallOnly[i];
        if (small && map.ShipyardZones[i].Distance(position) <= FP._0) return true;
      }
      return false;
    }

    /// <summary>A shipyard on a canal bank (small-vessel zone) cannot produce ships taller than SmallVesselMaxHull.</summary>
    public static bool TooTallForYard(Frame f, FPVector3 yardPosition, UnitSpec unit) {
      if (unit == null || unit.IsNaval == false) return false;
      var rules = FactoryWarsUtil.Rules(f);
      if (rules.Waterway.IsValid == false) return false;
      var max = f.FindAsset(rules.Waterway).SmallVesselMaxHull;
      return max > FP._0 && unit.HullHeight > max && InSmallVesselZone(f, yardPosition);
    }
  }
}
